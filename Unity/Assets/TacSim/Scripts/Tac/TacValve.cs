using System.Collections.Generic;
using UnityEngine;

namespace TacSim
{
    // Standard subsea valve replica: orange bucket with a handle limited to a 90° sector between S and O.
    // The bucket opening faces local +Y. The handle is turned by TacArm's fork (kinematic coupling,
    // see Drive) or set by code.
    public sealed class TacValve : MonoBehaviour
    {
        public static readonly List<TacValve> All = new();
        // Fork seating window in the valve frame and the alignment needed to catch / keep the handle. ASSUMED.
        public const float SeatRadius = 0.03f;
        public const float SeatMinHeight = 0.005f;
        public const float SeatMaxHeight = 0.07f;
        public const float SeatAxisDegrees = 20;
        public const float CatchDegrees = 8;
        public const float SlipDegrees = 25;
        // Booklet size, before the valve's scale: outer radius 120 mm, rim above the origin (bucket depth 85 mm).
        public const float OuterRadius = 0.12f;
        public const float RimHeight = 0.0855f;

        // 0° = S (shut), 90° = O (open). The start position may be anywhere in between.
        [Range(0, 90)] public float angle;
        public string valveName;
        Transform handle;

        public bool IsOpen => angle >= 85;
        public float Radius => OuterRadius * transform.lossyScale.x;
        public bool IsShut => angle <= 5;
        public float StartAngle { get; private set; }
        public float TurnedByTool { get; private set; }
        public bool Engaged { get; private set; }
        public Vector3 ToolOffset { get; private set; }
        // Physical mode: the handle is a hinged rigid body that only turns when something pushes it.
        public bool Physical => handleBody != null;
        public bool ToolContact => handleBody != null && Time.time - lastContact < .1f;
        Rigidbody handleBody;
        float friction, lastContact = -1;
        // Task: open the valve with the manipulator (ASSUMED: the booklet text does not give the target position).
        public bool Completed => IsOpen && TurnedByTool >= 5;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // Couples the handle to a fork tool. tip/axis/slot are world-space: the fork tip, the direction the
        // tool points, and the direction of its slot. Returns true while the fork holds the handle.
        public bool Drive(Vector3 tip, Vector3 axis, Vector3 slot)
        {
            Vector3 p = ToolOffset = transform.InverseTransformPoint(tip);
            Vector3 a = transform.InverseTransformDirection(axis);
            Vector3 s = transform.InverseTransformDirection(slot);
            bool seated = new Vector2(p.x, p.z).magnitude <= SeatRadius && p.y >= SeatMinHeight && p.y <= SeatMaxHeight
                && a.y <= -Mathf.Cos(SeatAxisDegrees * Mathf.Deg2Rad);
            if (!seated) return Engaged = false;
            // The slot is symmetric; fold its direction to −45°…135° so it compares with the 0°…90° handle.
            float slotAngle = Mathf.Repeat(Mathf.Atan2(s.x, s.z) * Mathf.Rad2Deg + 45, 180) - 45;
            float error = Mathf.Abs(slotAngle - angle);
            if (error > (Engaged ? SlipDegrees : CatchDegrees)) return Engaged = false;
            float before = angle;
            SetAngle(slotAngle);
            TurnedByTool += Mathf.Abs(angle - before);
            return Engaged = true;
        }

        // withModel: false gives the handle logic only, for a valve whose visuals come from a 3D model.
        public static TacValve Create(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, float angle,
            bool withModel = true)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            var valve = root.AddComponent<TacValve>();
            valve.valveName = name;
            valve.SetAngle(angle);
            valve.StartAngle = valve.angle;
            if (!withModel) return valve;
            Material orange = TacMaterials.Lit("TAC valve", TacMaterials.ValveOrange, 0, 0.4f);
            Material dark = TacMaterials.Lit("TAC valve bucket inside", TacMaterials.ValveOrange * 0.35f, 0, 0.2f);
            Material white = TacMaterials.Lit("TAC valve marking", Color.white, 0, 0.3f);

            // Booklet: outer radius 120 mm, inner radius 68 mm, bucket depth 85 mm, handle 65 × 25 mm.
            TacMaterials.Primitive(PrimitiveType.Cylinder, "Bucket", root.transform, new Vector3(0, 0.0425f, 0),
                new Vector3(2 * OuterRadius, 0.0425f, 2 * OuterRadius), orange);
            TacMaterials.Primitive(PrimitiveType.Cylinder, "Bucket opening", root.transform, new Vector3(0, RimHeight, 0),
                new Vector3(0.136f, 0.001f, 0.136f), dark, false);
            for (int i = 0; i < 2; i++)
            {
                float a = i * 90 * Mathf.Deg2Rad;
                TacMaterials.Primitive(PrimitiveType.Cube, i == 0 ? "S mark" : "O mark", root.transform,
                    new Vector3(Mathf.Sin(a) * 0.1f, 0.086f, Mathf.Cos(a) * 0.1f), new Vector3(0.02f, 0.002f, 0.02f), white, false);
            }
            valve.handle = new GameObject("Handle").transform;
            valve.handle.SetParent(root.transform, false);
            valve.handle.localPosition = new Vector3(0, 0.02f, 0);
            TacMaterials.Primitive(PrimitiveType.Cube, "Handle bar", valve.handle, new Vector3(0, 0.0325f, 0),
                new Vector3(0.025f, 0.065f, 0.12f), orange);
            TacMaterials.Primitive(PrimitiveType.Cube, "Arrow", valve.handle, new Vector3(0, 0.066f, 0.045f),
                new Vector3(0.012f, 0.002f, 0.03f), white, false);
            valve.SetAngle(angle);
            return valve;
        }

        // Drives an imported handle instead of the generated one: the pivot's local Y is the valve axis
        // and its rotation is Euler(0, angle, 0), so place the handle under it at the current angle.
        public void AttachHandle(Transform pivot)
        {
            handle = pivot;
            SetAngle(angle);
        }

        // Hinges the attached handle to mount (a kinematic body on the valve frame) about the valve axis.
        // The colliders on the handle decide what can push it; friction (N·m) is the torque needed to turn it.
        public void MakePhysical(Rigidbody mount, float frictionTorque)
        {
            friction = frictionTorque;
            handleBody = handle.gameObject.AddComponent<Rigidbody>();
            handleBody.useGravity = false;
            handleBody.mass = 1.2f;
            handleBody.angularDamping = 1;
            handleBody.interpolation = RigidbodyInterpolation.Interpolate;
            handleBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            handleBody.solverIterations = 20;
            handleBody.solverVelocityIterations = 10;
            handleBody.maxDepenetrationVelocity = .05f;
            var hinge = handle.gameObject.AddComponent<HingeJoint>();
            hinge.connectedBody = mount;
            hinge.anchor = Vector3.zero;
            hinge.axis = Vector3.up;
            handle.gameObject.AddComponent<ContactProbe>().valve = this;
        }

        // Held: the handle follows SetAngle only (the easy-valve assist turns it directly).
        public void Hold(bool held)
        {
            if (handleBody == null) return;
            handleBody.isKinematic = held;
            SetAngle(angle);
        }

        void FixedUpdate()
        {
            if (handleBody == null || handleBody.isKinematic) return;
            // The angle follows the body; S and O are hard stops.
            Vector3 bar = Quaternion.Inverse(transform.rotation) * (handleBody.rotation * Vector3.forward);
            float measured = Mathf.Atan2(bar.x, bar.z) * Mathf.Rad2Deg;
            Vector3 axis = transform.up;
            float spin = Vector3.Dot(handleBody.angularVelocity, axis);
            if (measured < 0 || measured > 90)
            {
                measured = Mathf.Clamp(measured, 0, 90);
                handleBody.rotation = transform.rotation * Quaternion.Euler(0, measured, 0);
                if (measured <= 0 ? spin < 0 : spin > 0) { handleBody.angularVelocity -= axis * spin; spin = 0; }
            }
            // Coulomb friction: the push has to exceed the friction torque before the handle moves.
            Quaternion principal = handleBody.rotation * handleBody.inertiaTensorRotation;
            Vector3 inertia = handleBody.inertiaTensor;
            float about = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 basis = Vector3.zero;
                basis[i] = 1;
                float c = Vector3.Dot(axis, principal * basis);
                about += inertia[i] * c * c;
            }
            float stop = friction / Mathf.Max(about, 1e-6f) * Time.fixedDeltaTime;
            handleBody.angularVelocity -= axis * (Mathf.Abs(spin) <= stop ? spin : Mathf.Sign(spin) * stop);
            TurnedByTool += Mathf.Abs(measured - angle);
            angle = measured;
        }

        internal void Touched() => lastContact = Time.time;

        public void SetAngle(float degrees)
        {
            angle = Mathf.Clamp(degrees, 0, 90);
            if (handle != null) handle.localRotation = Quaternion.Euler(0, angle, 0);
            if (handleBody == null) return;
            handleBody.rotation = handle.rotation;
            if (handleBody.isKinematic) return;
            handleBody.angularVelocity = Vector3.zero;
            handleBody.linearVelocity = Vector3.zero;
        }

        void OnValidate() => SetAngle(angle);

        // Sits on the handle body to hear its collisions; the handle ignores the structure, so any contact is the tool or the ROV.
        sealed class ContactProbe : MonoBehaviour
        {
            public TacValve valve;
            void OnCollisionEnter() => valve.Touched();
            void OnCollisionStay() => valve.Touched();
        }
    }
}
