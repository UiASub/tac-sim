using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacSim
{
    [Serializable]
    public struct Thruster
    {
        public Vector3 position;
        public Vector3 direction;
        [Min(0)] public float maximumForce;
    }

    [RequireComponent(typeof(Rigidbody))]
    public sealed class RovVehicle : MonoBehaviour
    {
        [Header("SI units; local X right, Y up, Z forward")]
        [Min(1)] public float mass = 30;
        [Min(0)] public float displacedVolume = 0.03f;
        [Min(0)] public float waterDensity = 1000;
        public Vector3 centreOfMass = new(0, -0.08f, 0);
        public Vector3 centreOfBuoyancy = new(0, 0.08f, 0);
        public Vector3 current;
        public Vector3 linearDrag = new(12, 18, 10);
        public Vector3 quadraticDrag = new(35, 45, 25);
        public float angularDrag = 12;
        public float waterSurface;
        [Min(0.01f)] public float hullHeight = 0.6f;
        [Min(0.01f)] public float responseTime = 0.15f;
        public Thruster[] thrusters = Array.Empty<Thruster>();

        public Rigidbody Body { get; private set; }
        public bool Armed { get; private set; } = true;
        public float Depth => Mathf.Max(0, waterSurface - transform.position.y);
        public float Throttle { get; private set; }
        public Vector3 TranslationCommand { get; private set; }
        public Vector3 RotationCommand { get; private set; }
        public bool IsColliding => contacts.Count > 0;
        public float CollisionForce { get; private set; }
        public float PeakCollisionForce { get; private set; }
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float[] forces;
        readonly HashSet<int> contacts = new();

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = mass;
            Body.centerOfMass = centreOfMass;
            Body.linearDamping = 0;
            Body.angularDamping = 0;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Idealized top-float stabilization: three translations and yaw only.
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            spawnPosition = transform.position;
            spawnRotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            Body.rotation = spawnRotation;
            transform.rotation = spawnRotation;
            forces = new float[thrusters.Length];
        }

        public void SetCommand(Vector3 translation, Vector3 rotation)
        {
            TranslationCommand = Vector3.ClampMagnitude(translation, 1);
            RotationCommand = new Vector3(0, Mathf.Clamp(rotation.y, -1, 1), 0);
        }

        public void SetArmed(bool armed)
        {
            Armed = armed;
            SetCommand(Vector3.zero, Vector3.zero);
            if (!armed) Array.Clear(forces, 0, forces.Length);
        }

        public void ResetVehicle()
        {
            Body.position = spawnPosition;
            Body.rotation = spawnRotation;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Array.Clear(forces, 0, forces.Length);
            SetCommand(Vector3.zero, Vector3.zero);
            Throttle = 0;
            contacts.Clear();
            CollisionForce = 0;
            PeakCollisionForce = 0;
            Armed = true;
        }

        void OnCollisionEnter(Collision collision) => RecordCollision(collision);
        void OnCollisionStay(Collision collision) => RecordCollision(collision);

        void OnCollisionExit(Collision collision)
        {
            contacts.Remove(collision.collider.GetInstanceID());
            if (contacts.Count == 0) CollisionForce = 0;
        }

        void RecordCollision(Collision collision)
        {
            contacts.Add(collision.collider.GetInstanceID());
            CollisionForce = CollisionForceFromImpulse(collision.impulse.magnitude, Time.fixedDeltaTime);
            PeakCollisionForce = Mathf.Max(PeakCollisionForce, CollisionForce);
        }

        public static float CollisionForceFromImpulse(float impulse, float fixedDeltaTime)
        {
            return fixedDeltaTime > 0 ? Mathf.Max(0, impulse) / fixedDeltaTime : 0;
        }

        // Drag opposes motion relative to the water, not motion relative to the pool.
        public static Vector3 Drag(Vector3 velocity, Vector3 linear, Vector3 quadratic)
        {
            return new Vector3(
                -velocity.x * (linear.x + quadratic.x * Mathf.Abs(velocity.x)),
                -velocity.y * (linear.y + quadratic.y * Mathf.Abs(velocity.y)),
                -velocity.z * (linear.z + quadratic.z * Mathf.Abs(velocity.z)));
        }

        void FixedUpdate()
        {
            // Use the simulation pose; the interpolated Transform can still show a pre-reset frame.
            Quaternion rotation = Body.rotation;
            float submerged = Mathf.Clamp01((waterSurface - Body.position.y) / hullHeight + 0.5f);
            Body.AddForceAtPosition(-Physics.gravity * (waterDensity * displacedVolume * submerged),
                Body.position + rotation * centreOfBuoyancy);
            Vector3 relativeVelocity = Quaternion.Inverse(rotation) * (Body.linearVelocity - current);
            Body.AddRelativeForce(Drag(relativeVelocity, linearDrag, quadraticDrag) * submerged);
            Body.AddTorque(-Body.angularVelocity * (angularDrag * submerged));

            Throttle = 0;
            for (int i = 0; i < thrusters.Length; i++)
            {
                Thruster t = thrusters[i];
                Vector3 direction = t.direction.normalized;
                Vector3 moment = Vector3.Cross(t.position - centreOfMass, direction);
                float mix = Vector3.Dot(direction, TranslationCommand)
                    + Vector3.Dot(moment, RotationCommand) * 2;
                float target = Armed ? Mathf.Clamp(mix, -1, 1) * t.maximumForce : 0;
                forces[i] = Mathf.MoveTowards(forces[i], target,
                    t.maximumForce * Time.fixedDeltaTime / responseTime);
                Body.AddForceAtPosition(rotation * direction * forces[i] * submerged,
                    Body.position + rotation * t.position);
                Throttle = Mathf.Max(Throttle, Mathf.Abs(forces[i]) / Mathf.Max(1, t.maximumForce));
            }
        }
    }
}
