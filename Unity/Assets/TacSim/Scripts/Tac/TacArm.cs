using UnityEngine;

namespace TacSim
{
    // DRAFT manipulator, not the real Malstrøm arm: a boom under the bow with a rotating wrist that
    // carries a fork fitting over a valve handle, plus a tilt joint (forward ↔ straight down) so the
    // same tool reaches valves on vertical and horizontal surfaces.
    //
    // The wrist matches Topside's single manipulator channel (±50°, 45°/s). The tilt joint and all
    // dimensions are ASSUMED. The arm has no colliders: it couples to a valve kinematically when the
    // fork is seated in the bucket (see TacValve.Drive), which keeps the vehicle physics unchanged.
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

        public static TacArm Attach(RovVehicle vehicle)
        {
            TacArm existing = vehicle.GetComponentInChildren<TacArm>();
            if (existing != null) return existing;
            var root = new GameObject("Manipulator (draft)");
            root.transform.SetParent(vehicle.transform, false);
            root.transform.localPosition = Mount;
            var arm = root.AddComponent<TacArm>();
            Material dark = TacMaterials.Lit("TAC arm", new Color(0.1f, 0.11f, 0.12f), 0.5f, 0.4f);
            Material steel = TacMaterials.Lit("TAC arm tool", new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);

            arm.pivot = new GameObject("Tilt joint").transform;
            arm.pivot.SetParent(root.transform, false);
            TacMaterials.Primitive(PrimitiveType.Sphere, "Joint", arm.pivot, Vector3.zero, Vector3.one * 0.05f, dark, false);
            TacMaterials.Tube("Boom", arm.pivot, Vector3.zero, new Vector3(0, 0, Reach - 0.07f), 0.03f, dark, false);

            arm.wrist = new GameObject("Wrist").transform;
            arm.wrist.SetParent(arm.pivot, false);
            arm.wrist.localPosition = new Vector3(0, 0, Reach - 0.07f);
            TacMaterials.Primitive(PrimitiveType.Cube, "Fork base", arm.wrist, new Vector3(0, 0, 0.005f),
                new Vector3(0.07f, 0.045f, 0.01f), steel, false);
            // Two prongs leave a 30 mm slot along local X for the 25 mm handle bar.
            foreach (float y in new[] { -0.02f, 0.02f })
                TacMaterials.Primitive(PrimitiveType.Cube, "Fork prong", arm.wrist, new Vector3(0, y, 0.04f),
                    new Vector3(0.07f, 0.008f, 0.06f), steel, false);

            arm.tip = new GameObject("Tool tip").transform;
            arm.tip.SetParent(arm.pivot, false);
            arm.tip.localPosition = new Vector3(0, 0, Reach);
            arm.Apply();
            return arm;
        }

        // wrist: −1…1 like Topside's manip axis; tilt: 0 (forward) … 1 (straight down).
        public void SetTargets(float wrist, float tilt)
        {
            WristTarget = Mathf.Clamp(wrist, -1, 1) * WristLimit;
            TiltTarget = Mathf.Clamp01(tilt) * TiltLimit;
        }

        public void NudgeWrist(float direction, float deltaTime) =>
            WristTarget = Mathf.Clamp(WristTarget + direction * WristRate * deltaTime, -WristLimit, WristLimit);

        public void ToggleTilt() => TiltTarget = TiltTarget > TiltLimit / 2 ? 0 : TiltLimit;

        void FixedUpdate()
        {
            Wrist = Mathf.MoveTowards(Wrist, WristTarget, WristRate * Time.fixedDeltaTime);
            Tilt = Mathf.MoveTowards(Tilt, TiltTarget, TiltRate * Time.fixedDeltaTime);
            Apply();
            Engaged = null;
            foreach (TacValve valve in TacValve.All)
                if (valve.Drive(TipPosition, ToolAxis, SlotDirection)) Engaged = valve;
        }

        void Apply()
        {
            pivot.localRotation = Quaternion.Euler(Tilt, 0, 0);
            wrist.localRotation = Quaternion.Euler(0, 0, Wrist + SlotOffset);
        }
    }
}
