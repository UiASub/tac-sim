using UnityEngine;

namespace TacSim
{
    // Referee for the subsea docking task: measures the vehicle's power puck against the station's.
    // Booklet: +20 docked ≥ 10 s, +50 precision docking (pucks in contact), +50 light via power puck.
    // The contact tolerances are ASSUMED; the booklet only says the pucks must be in contact.
    public sealed class TacDock : MonoBehaviour
    {
        public const float RequiredSeconds = 10;
        public const float ContactGap = 0.02f;     // ASSUMED: max height of the vehicle puck above the plate
        public const float PuckTolerance = 0.03f;  // ASSUMED: max horizontal puck-to-puck offset
        public const float RestSpeed = 0.05f;      // ASSUMED: the vehicle must sit still

        public Vector2 plateSize = new(1.2f, 0.8f);
        public float plateTop;
        public Transform puck;

        public Vector3 Offset { get; private set; }
        public bool OnPlate { get; private set; }
        public bool PuckContact { get; private set; }
        public float DockedSeconds { get; private set; }
        public float PuckSeconds { get; private set; }
        public float BestDockedSeconds { get; private set; }
        public float BestPuckSeconds { get; private set; }
        public bool Docked => BestDockedSeconds >= RequiredSeconds;
        public bool Precision => BestPuckSeconds >= RequiredSeconds;
        public int Points => (Docked ? 20 : 0) + (Precision ? 100 : 0);

        RovVehicle vehicle;
        BoxCollider hull;
        Light indicator;

        void Start()
        {
            vehicle = FindFirstObjectByType<RovVehicle>();
            if (vehicle != null) hull = vehicle.GetComponent<BoxCollider>();
            // Stands in for the light the vehicle powers through the puck.
            indicator = new GameObject("Power puck light").AddComponent<Light>();
            indicator.transform.SetParent(transform, false);
            indicator.transform.localPosition = new Vector3(0, plateTop + 0.25f, 0);
            indicator.type = LightType.Point;
            indicator.color = new Color(0.2f, 1f, 0.3f);
            indicator.range = 2.5f;
            indicator.intensity = 6;
            indicator.enabled = false;
        }

        // Vehicle puck (centre of the hull underside) in the dock frame: x/z from the dock puck, y above the plate.
        public Vector3 VehiclePuckOffset()
        {
            float bottom = hull != null ? hull.center.y - hull.size.y / 2 : -0.16f;
            Vector3 local = transform.InverseTransformPoint(vehicle.transform.TransformPoint(new Vector3(0, bottom, 0)));
            Vector3 centre = puck != null ? transform.InverseTransformPoint(puck.position) : Vector3.zero;
            return new Vector3(local.x - centre.x, local.y - plateTop, local.z - centre.z);
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            Offset = VehiclePuckOffset();
            Vector3 onDock = transform.InverseTransformPoint(vehicle.transform.position);
            bool resting = Offset.y <= ContactGap && vehicle.Body.linearVelocity.magnitude <= RestSpeed;
            OnPlate = resting && Mathf.Abs(onDock.x) <= plateSize.x / 2 && Mathf.Abs(onDock.z) <= plateSize.y / 2;
            PuckContact = OnPlate && new Vector2(Offset.x, Offset.z).magnitude <= PuckTolerance;
            DockedSeconds = OnPlate ? DockedSeconds + Time.fixedDeltaTime : 0;
            PuckSeconds = PuckContact ? PuckSeconds + Time.fixedDeltaTime : 0;
            BestDockedSeconds = Mathf.Max(BestDockedSeconds, DockedSeconds);
            BestPuckSeconds = Mathf.Max(BestPuckSeconds, PuckSeconds);
            indicator.enabled = PuckContact;
        }
    }
}
