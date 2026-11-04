using System.Linq;
using UnityEngine;

namespace TacSim
{
    // DRAFT manipulator, not the real Malstrøm arm: a boom under the bow with a rotating wrist that
    // carries a fork fitting over a valve handle, plus a tilt joint (forward ↔ straight down) so the
    // same tool reaches valves on vertical and horizontal surfaces.
    //
    // The wrist matches Topside's single manipulator channel (±50°, 45°/s). The tilt joint and all
    // dimensions are ASSUMED. By default the arm has no colliders: it couples to a valve kinematically
    // when the fork is seated in the bucket (see TacValve.Drive), which keeps the vehicle physics
    // unchanged. MakePhysical switches to a colliding tool that turns physical valves by contact.
    public sealed class TacArm : MonoBehaviour
    {
        public const float WristLimit = 50;   // Topside MANIP_MIN_DEG / MANIP_MAX_DEG
        public const float WristRate = 45;    // Topside MANIP_NUDGE_DEG_PER_SEC
        public const float TiltLimit = 90;    // ASSUMED
        public const float TiltRate = 45;     // ASSUMED
        public const float Reach = 0.30f;     // ASSUMED: tilt pivot to fork tip
        public static readonly Vector3 Mount = new(0, -0.12f, 0.26f); // ASSUMED: tilt pivot, vehicle frame
        // The fork slot sits 45° off the wrist zero so the ±50° travel spans a valve's 0°…90°.
        const float SlotOffset = 45;

        public float Wrist { get; private set; }
        public float Tilt { get; private set; }
        public float WristTarget { get; private set; }
        public float TiltTarget { get; private set; }
        public TacValve Engaged { get; private set; }
        public Vector3 TipPosition => tip.position;
        public Vector3 ToolAxis => wrist.forward;
        public Vector3 SlotDirection => wrist.right;

        Transform pivot, wrist, tip;
        // Physical tool (MakePhysical): the socket is its own body on a motor joint about the tool axis.
        Rigidbody socket;
        ConfigurableJoint motor;
        Quaternion socketStart;
        float socketZero;
        public bool PhysicalTool => socket != null;
        // The physical fork has a few degrees of play on the bar; give it room to seat the bar in the O notch.
        float wristLimit = WristLimit;
        public float WristRange => wristLimit;
        public Transform Socket => socket != null ? socket.transform : null;

        // torqueTool: a compact electric torque tool (motor housing, socket sleeve and paddle fork)
        // instead of the draft boom. Same joints, reach and valve coupling.
        public static TacArm Attach(RovVehicle vehicle, bool torqueTool = false)
        {
            TacArm existing = vehicle.GetComponentInChildren<TacArm>();
            if (existing != null) return existing;
            var root = new GameObject(torqueTool ? "Torque tool" : "Manipulator (draft)");
            root.transform.SetParent(vehicle.transform, false);
            root.transform.localPosition = Mount;
            var arm = root.AddComponent<TacArm>();
            Material dark = TacMaterials.Lit("TAC arm", new Color(0.1f, 0.11f, 0.12f), 0.5f, 0.4f);
            Material steel = TacMaterials.Lit("TAC arm tool", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);

            arm.pivot = new GameObject("Tilt joint").transform;
            arm.pivot.SetParent(root.transform, false);
            if (torqueTool)
            {
                Material anodised = TacMaterials.Lit("TAC torque tool housing", new Color(0.16f, 0.17f, 0.19f), 0.6f, 0.55f);
                Material accent = TacMaterials.Lit("TAC torque tool band", new Color(0.85f, 0.55f, 0.08f), 0.2f, 0.45f);
                TacMaterials.Primitive(PrimitiveType.Cube, "Hull bracket", arm.pivot, new Vector3(0, 0.035f, 0.03f),
                    new Vector3(0.05f, 0.03f, 0.09f), dark, false);
                TacMaterials.Tube("Motor housing", arm.pivot, new Vector3(0, 0, -0.02f), new Vector3(0, 0, Reach - 0.1f), 0.055f, anodised, false);
                TacMaterials.Tube("Motor end cap", arm.pivot, new Vector3(0, 0, -0.03f), new Vector3(0, 0, -0.015f), 0.06f, dark, false);
                TacMaterials.Tube("Gearbox band", arm.pivot, new Vector3(0, 0, Reach - 0.115f), new Vector3(0, 0, Reach - 0.1f), 0.06f, accent, false);
                TacMaterials.Tube("Output shaft", arm.pivot, new Vector3(0, 0, Reach - 0.1f), new Vector3(0, 0, Reach - 0.07f), 0.025f, steel, false);
            }
            else
            {
                TacMaterials.Primitive(PrimitiveType.Sphere, "Joint", arm.pivot, Vector3.zero, Vector3.one * 0.05f, dark, false);
                TacMaterials.Tube("Boom", arm.pivot, Vector3.zero, new Vector3(0, 0, Reach - 0.07f), 0.03f, dark, false);
            }

            arm.wrist = new GameObject("Wrist").transform;
            arm.wrist.SetParent(arm.pivot, false);
            arm.wrist.localPosition = new Vector3(0, 0, Reach - 0.07f);
            if (torqueTool)
                TacMaterials.Tube("Socket sleeve", arm.wrist, Vector3.zero, new Vector3(0, 0, 0.015f), 0.06f, steel, false);
            else
                TacMaterials.Primitive(PrimitiveType.Cube, "Fork base", arm.wrist, new Vector3(0, 0, 0.005f),
                    new Vector3(0.07f, 0.045f, 0.01f), steel, false);
            // Two prongs leave a 30 mm slot along local X for the 25 mm handle bar.
            foreach (float y in new[] { -0.02f, 0.02f })
                TacMaterials.Primitive(PrimitiveType.Cube, "Fork prong", arm.wrist, new Vector3(0, y, 0.04f),
                    new Vector3(torqueTool ? 0.05f : 0.07f, 0.008f, 0.06f), steel, false);

            arm.tip = new GameObject("Tool tip").transform;
            arm.tip.SetParent(arm.pivot, false);
            arm.tip.localPosition = new Vector3(0, 0, Reach);
            arm.Apply();
            return arm;
        }

        // wrist: −1…1 like Topside's manip axis; tilt: 0 (forward) … 1 (straight down).
        public void SetTargets(float wrist, float tilt)
        {
            WristTarget = Mathf.Clamp(wrist, -1, 1) * wristLimit;
            TiltTarget = Mathf.Clamp01(tilt) * TiltLimit;
        }

        public void NudgeWrist(float direction, float deltaTime) =>
            WristTarget = Mathf.Clamp(WristTarget + direction * WristRate * deltaTime, -wristLimit, wristLimit);

        public void ToggleTilt()
        {
            if (socket != null) return; // The physical tool is fixed pointing forward.
            TiltTarget = TiltTarget > TiltLimit / 2 ? 0 : TiltLimit;
        }

        // Replaces the kinematic valve coupling with contact physics: the motor housing collides as part of
        // the ROV, and the socket is a separate body the motor turns with at most maxTorque (N·m). Valves
        // turn only when the fork pushes their handle, and the reaction goes back into the ROV.
        // slotWidth: the clear gap between the fork prongs, sized to the valve bar it has to straddle.
        public void MakePhysical(RovVehicle vehicle, float maxTorque, float slotWidth = .032f)
        {
            const float prongThickness = .008f;
            float prongOffset = slotWidth / 2 + prongThickness / 2;
            wristLimit = 55;
            TiltTarget = Tilt = 0;
            Apply();
            var housing = new GameObject("Tool housing collider") { layer = SensorSuite.VehicleLayer };
            housing.transform.SetParent(pivot, false);
            var capsule = housing.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = .0275f;
            capsule.height = Reach - .07f;
            capsule.center = new Vector3(0, 0, (Reach - .13f) / 2);

            var body = new GameObject("Torque tool socket");
            body.transform.SetPositionAndRotation(wrist.position, wrist.rotation);
            foreach (Transform part in wrist.Cast<Transform>().ToArray())
            {
                part.SetParent(body.transform, true);
                // Widen the fork and its sleeve to the requested slot.
                if (part.name == "Fork prong") part.localPosition = new Vector3(0, Mathf.Sign(part.localPosition.y) * prongOffset, .04f);
                if (part.name == "Socket sleeve") part.localScale = new Vector3(slotWidth + .03f, part.localScale.y, slotWidth + .03f);
            }
            var sleeve = body.AddComponent<BoxCollider>();
            sleeve.center = new Vector3(0, 0, .0075f);
            sleeve.size = new Vector3(slotWidth + .03f, slotWidth + .03f, .015f);
            foreach (float side in new[] { -1f, 1f })
            {
                var prong = body.AddComponent<BoxCollider>();
                prong.center = new Vector3(0, side * prongOffset, .04f);
                prong.size = new Vector3(.05f, prongThickness, .06f);
            }
            // The fork leaves a few millimetres either side of the bar; keep contacts that tight. Polished
            // steel: the fork slides off a bar it lands on instead of turning it by friction.
            var steelOnSteel = new PhysicsMaterial("Tool steel")
            {
                dynamicFriction = .05f, staticFriction = .05f, frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var collider in body.GetComponents<Collider>())
            {
                collider.contactOffset = .002f;
                collider.sharedMaterial = steelOnSteel;
            }
            socket = body.AddComponent<Rigidbody>();
            socket.useGravity = false;
            socket.mass = 1;
            socket.interpolation = RigidbodyInterpolation.Interpolate;
            socket.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            socket.solverIterations = 25;
            socket.solverVelocityIterations = 10;
            // Resolve the small overlaps of a tight fit gently, instead of kicking the ROV back.
            socket.maxDepenetrationVelocity = .05f;
            socket.inertiaTensor = Vector3.one * .002f;
            socket.inertiaTensorRotation = Quaternion.identity;
            motor = body.AddComponent<ConfigurableJoint>();
            motor.connectedBody = vehicle.Body;
            motor.anchor = Vector3.zero;
            motor.axis = Vector3.forward;
            motor.secondaryAxis = Vector3.up;
            motor.xMotion = motor.yMotion = motor.zMotion = ConfigurableJointMotion.Locked;
            motor.angularXMotion = ConfigurableJointMotion.Free;
            motor.angularYMotion = motor.angularZMotion = ConfigurableJointMotion.Locked;
            motor.rotationDriveMode = RotationDriveMode.XYAndZ;
            motor.angularXDrive = new JointDrive { positionSpring = 60, positionDamper = 4, maximumForce = maxTorque };
            socketStart = Quaternion.Inverse(vehicle.transform.rotation) * body.transform.rotation;
            socketZero = Wrist + SlotOffset;
            foreach (var mine in body.GetComponents<Collider>())
            foreach (var hull in vehicle.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(mine, hull);
        }

        // Kinematic while the easy-valve assist poses the tool; it then follows the wrist transform.
        public void SetSocketKinematic(bool kinematic)
        {
            if (socket == null) return;
            Apply();
            socket.isKinematic = kinematic;
            SyncSocket();
        }

        // Puts the socket back on the wrist, e.g. after the ROV was teleported.
        public void SyncSocket()
        {
            if (socket == null) return;
            socket.transform.SetPositionAndRotation(wrist.position, wrist.rotation);
            socket.position = wrist.position;
            socket.rotation = wrist.rotation;
            if (socket.isKinematic) return;
            socket.linearVelocity = Vector3.zero;
            socket.angularVelocity = Vector3.zero;
        }

        void FixedUpdate()
        {
            Wrist = Mathf.MoveTowards(Wrist, WristTarget, WristRate * Time.fixedDeltaTime);
            Tilt = Mathf.MoveTowards(Tilt, TiltTarget, TiltRate * Time.fixedDeltaTime);
            Apply();
            Engaged = null;
            if (socket != null)
            {
                if (!socket.isKinematic) DriveSocket();
                foreach (TacValve valve in TacValve.All)
                    if (valve.ToolContact) Engaged = valve;
                return;
            }
            foreach (TacValve valve in TacValve.All)
                if (!valve.Physical && valve.Drive(TipPosition, ToolAxis, SlotDirection)) Engaged = valve;
        }

        // ConfigurableJoint targets are relative to the pose at creation, expressed in joint space
        // (X = axis, Y = secondary axis).
        void DriveSocket()
        {
            Quaternion target = socketStart * Quaternion.Euler(0, 0, Wrist + SlotOffset - socketZero);
            Vector3 right = motor.axis;
            Vector3 forward = Vector3.Cross(motor.axis, motor.secondaryAxis).normalized;
            Quaternion toJoint = Quaternion.LookRotation(forward, Vector3.Cross(forward, right).normalized);
            motor.targetRotation = Quaternion.Inverse(toJoint) * Quaternion.Inverse(target) * socketStart * toJoint;
        }

        void OnDestroy() { if (socket != null) Destroy(socket.gameObject); }

        void Apply()
        {
            pivot.localRotation = Quaternion.Euler(Tilt, 0, 0);
            wrist.localRotation = Quaternion.Euler(0, 0, Wrist + SlotOffset);
        }
    }
}
