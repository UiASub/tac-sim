using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace TacSim
{
    // Procedural TAC Challenge 2026 apparatus: pipeline with ArUco markers and pinger, docking station,
    // and the subsea structure with markers and valves. The same seed always produces the same course,
    // and the ground truth is exposed for automatic scoring.
    //
    // Place this component at pool-floor height; local Y = 0 is the floor surface.
    // Dimensions marked ASSUMED are not given in the booklet text (only in its figures / the TAC
    // Shared Info Folder) and must be checked against the official drawings.
    public sealed class TacCourse : MonoBehaviour
    {
        public int seed = 1;
        public bool randomSeedOnStart;

        [Header("Pipeline (booklet: Ø200 mm, yellow, ≤ 10 m, joints −90°…90°, 4–10 markers ≥ 0.2 m apart)")]
        public Vector2 pipelineAreaMin = new(-6.8f, -8f);
        public Vector2 pipelineAreaMax = new(6f, 3.5f);
        public float pipeDiameter = 0.2f;
        public float pipelineMarkerSize = 0.15f; // ASSUMED

        [Header("Docking station (booklet: 1200 × 800 mm white plate, ArUco 28, 7, 19, 96)")]
        public Vector3 dockPosition = new(-4f, 0, 7.8f);
        public float dockYaw;
        public float dockPlateHeight = 0.3f;     // ASSUMED
        public float dockMarkerSize = 0.15f;     // ASSUMED
        public static readonly int[] DockMarkerIds = { 28, 7, 19, 96 };

        [Header("Subsea structure (booklet: RAL 1004, 5–10 markers, valves A and B)")]
        // Prefabs/UnderwaterStructure.prefab: the team's 3D model, used with the prefab's scale (about
        // 4.0 × 2.6 × 2.1 m; origin at the bottom centre). Its two STEP bucket valves become the TAC valves.
        public GameObject structureModel;
        public Vector3 structurePosition = new(3.8f, 0, 7.8f);
        public float structureYaw = 180;                    // valve A's wall at the far side, facing the launch end
        public float structureMarkerSize = 0.1f;            // ASSUMED

        public int Seed { get; private set; }
        public int[] PipelineMarkerIds { get; private set; } = Array.Empty<int>();
        public int[] StructureMarkerIds { get; private set; } = Array.Empty<int>();
        public Vector3 PingerPosition { get; private set; }
        public Transform DockPuck { get; private set; }
        public TacDock Dock { get; private set; }
        public TacArm Arm { get; private set; }
        public Transform Structure { get; private set; }
        public Vector3 StructureSize { get; private set; } // width (X) × height × depth (Z) in the structure frame
        public TacValve ValveA { get; private set; }
        public TacValve ValveB { get; private set; }
        public float PipelineLength { get; private set; }
        public string Hint => $"TAC ARENA  /  seed {Seed}, {PipelineMarkerIds.Length} pipeline markers   •   " +
                              $"DOCK {DockStatus}   •   VALVE A {ValveStatus(ValveA)}  B {ValveStatus(ValveB)}   •   " +
                              "N new course   Z / X wrist   C arm down";

        string DockStatus => Dock == null ? "-" : Dock.Precision ? "PRECISION" : Dock.Docked ? "DOCKED"
            : Dock.PuckContact ? $"puck {Dock.PuckSeconds:F0} s" : Dock.OnPlate ? $"on plate {Dock.DockedSeconds:F0} s" : "-";

        static string ValveStatus(TacValve valve) =>
            valve == null ? "-" : valve.Completed ? "OPEN" : $"{valve.angle:F0}°" + (valve.Engaged ? " held" : "");

        Transform generated;

        void Awake() => Build(randomSeedOnStart ? Environment.TickCount & 0x7fffffff : seed);

        void Start()
        {
            // After every Awake: RovVisuals hides the renderers that exist under the vehicle when it wakes.
            var vehicle = FindFirstObjectByType<RovVehicle>();
            if (vehicle != null) Arm = TacArm.Attach(vehicle);
        }

        public void BuildRandom() => Build(Environment.TickCount & 0x7fffffff);

        public void Build(int courseSeed)
        {
            Seed = courseSeed;
            if (generated != null)
            {
                generated.gameObject.SetActive(false);
                Destroy(generated.gameObject);
            }
            generated = new GameObject($"Generated course • seed {Seed}").transform;
            generated.SetParent(transform, false);
            var rng = new Random(Seed);
            BuildPipeline(rng);
            BuildDock();
            BuildStructure(rng);
            Debug.Log($"TAC_COURSE: seed={Seed} pipeline=[{string.Join(",", PipelineMarkerIds)}] " +
                      $"structure=[{string.Join(",", StructureMarkerIds)}]");
        }

        // Docking error of a point (e.g. the vehicle centre) in the dock frame: x/z horizontal, y above plate.
        public Vector3 DockOffset(Vector3 worldPosition) =>
            DockPuck == null ? Vector3.zero : DockPuck.InverseTransformPoint(worldPosition);

        // ---------------------------------------------------------------- Pipeline

        void BuildPipeline(Random rng)
        {
            var root = new GameObject("Pipeline").transform;
            root.SetParent(generated, false);
            List<Vector2> points = PipelinePath(rng);
            Material yellow = TacMaterials.Lit("TAC pipeline", TacMaterials.PipelineYellow, 0, 0.45f);
            float r = pipeDiameter / 2;
            for (int i = 0; i + 1 < points.Count; i++)
                TacMaterials.Tube($"Pipe segment {i + 1}", root, Flat(points[i], r), Flat(points[i + 1], r), pipeDiameter, yellow);
            for (int i = 0; i < points.Count; i++)
                TacMaterials.Primitive(PrimitiveType.Sphere, i == 0 || i == points.Count - 1 ? "Pipe end" : "Pipe joint",
                    root, Flat(points[i], r), Vector3.one * pipeDiameter * 1.08f, yellow);

            var lengths = new List<float> { 0 };
            for (int i = 1; i < points.Count; i++) lengths.Add(lengths[^1] + Vector2.Distance(points[i - 1], points[i]));
            PipelineLength = lengths[^1];

            // Markers: unique IDs 1–99, edge-to-edge gap ≥ 0.2 m, kept clear of the joints.
            float plate = pipelineMarkerSize * 1.4f;
            float spacing = plate + 0.2f;
            int wanted = rng.Next(4, 11);
            var positions = new List<float>();
            for (int attempt = 0; attempt < 2000 && positions.Count < wanted; attempt++)
            {
                float s = (float)rng.NextDouble() * PipelineLength;
                if (s < plate || s > PipelineLength - plate) continue;
                if (lengths.Any(joint => Mathf.Abs(joint - s) < plate * 0.75f)) continue;
                if (positions.Any(other => Mathf.Abs(other - s) < spacing)) continue;
                positions.Add(s);
            }
            positions.Sort();
            int[] ids = Enumerable.Range(1, 99).OrderBy(_ => rng.Next()).Take(positions.Count).ToArray();
            for (int i = 0; i < positions.Count; i++)
            {
                (Vector2 point, _) = Along(points, lengths, positions[i]);
                ArucoMarker.Create(root, ids[i], pipelineMarkerSize, Flat(point, pipeDiameter + 0.001f),
                    Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0));
            }

            // Pinger (JW Fishers MFP-1) at one end marks the start of the sequence.
            bool pingerAtFirstPoint = rng.Next(2) == 0;
            Vector2 end = pingerAtFirstPoint ? points[0] : points[^1];
            Vector2 outward = pingerAtFirstPoint ? (points[0] - points[1]).normalized : (points[^1] - points[^2]).normalized;
            Vector2 pinger = end + outward * (r + 0.12f);
            Material dark = TacMaterials.Lit("TAC pinger", new Color(0.12f, 0.12f, 0.13f), 0.3f, 0.5f);
            TacMaterials.Primitive(PrimitiveType.Cylinder, "Pinger (30 kHz)", root, Flat(pinger, 0.15f),
                new Vector3(0.07f, 0.15f, 0.07f), dark);
            PingerPosition = transform.TransformPoint(Flat(pinger, 0));
            PipelineMarkerIds = pingerAtFirstPoint ? ids : ids.Reverse().ToArray();
        }

        List<Vector2> PipelinePath(Random rng)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                float total = Lerp(rng, 6, 10);
                int segments = rng.Next(2, 6);
                float[] weights = Enumerable.Range(0, segments).Select(_ => Lerp(rng, 0.6f, 1.4f)).ToArray();
                float sum = weights.Sum();
                float heading = Lerp(rng, 0, Mathf.PI * 2);
                var points = new List<Vector2>
                {
                    new(Lerp(rng, pipelineAreaMin.x, pipelineAreaMax.x), Lerp(rng, pipelineAreaMin.y, pipelineAreaMax.y))
                };
                bool valid = true;
                for (int i = 0; i < segments && valid; i++)
                {
                    float length = weights[i] / sum * total;
                    if (i > 0) heading += Lerp(rng, -90, 90) * Mathf.Deg2Rad;
                    Vector2 next = points[^1] + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * length;
                    valid = length >= 0.8f && Inside(next);
                    for (int j = 0; valid && j + 2 < points.Count; j++)
                        valid = SegmentDistance(points[j], points[j + 1], points[^1], next) > 0.8f;
                    points.Add(next);
                }
                if (valid) return points;
            }
            return new List<Vector2> { new(-3, -4), new(3, -4) };
        }

        static (Vector2, Vector2) Along(List<Vector2> points, List<float> lengths, float s)
        {
            for (int i = 1; i < points.Count; i++)
            {
                if (s > lengths[i]) continue;
                float t = (s - lengths[i - 1]) / (lengths[i] - lengths[i - 1]);
                return (Vector2.Lerp(points[i - 1], points[i], t), (points[i] - points[i - 1]).normalized);
            }
            return (points[^1], (points[^1] - points[^2]).normalized);
        }

        bool Inside(Vector2 p) => p.x >= pipelineAreaMin.x && p.x <= pipelineAreaMax.x
                                  && p.y >= pipelineAreaMin.y && p.y <= pipelineAreaMax.y;

        static Vector3 Flat(Vector2 p, float height) => new(p.x, height, p.y);
        static float Lerp(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
            if (Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0) return 0;
            return Mathf.Min(Mathf.Min(PointDistance(a, c, d), PointDistance(b, c, d)),
                Mathf.Min(PointDistance(c, a, b), PointDistance(d, a, b)));
        }

        static float PointDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        // ---------------------------------------------------------------- Docking station

        void BuildDock()
        {
            var root = new GameObject("Docking station").transform;
            root.SetParent(generated, false);
            root.SetLocalPositionAndRotation(dockPosition, Quaternion.Euler(0, dockYaw, 0));
            Material white = TacMaterials.Lit("TAC dock plate", new Color(0.92f, 0.93f, 0.92f), 0, 0.3f);
            Material frame = TacMaterials.Lit("TAC dock frame", new Color(0.18f, 0.2f, 0.22f), 0.6f, 0.4f);
            Material steel = TacMaterials.Lit("TAC dock steel", new Color(0.5f, 0.52f, 0.54f), 0.8f, 0.5f);
            Material puck = TacMaterials.Lit("TAC power puck", new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.6f);
            float h = dockPlateHeight;

            TacMaterials.Primitive(PrimitiveType.Cube, "Docking plate (EUR pallet 1200 × 800 mm)", root,
                new Vector3(0, h - 0.002f, 0), new Vector3(1.2f, 0.004f, 0.8f), white);
            foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -0.35f, 0.35f })
                TacMaterials.Primitive(PrimitiveType.Cube, "Leg", root, new Vector3(x, (h - 0.004f) / 2, z),
                    new Vector3(0.06f, h - 0.004f, 0.06f), frame);
            foreach (float z in new[] { -0.37f, 0.37f })
                TacMaterials.Primitive(PrimitiveType.Cube, "Rail", root, new Vector3(0, h - 0.03f, z),
                    new Vector3(1.16f, 0.05f, 0.04f), frame);
            // Steel plates 4 mm below the top surface in Ø120 mm holes (positions ASSUMED).
            foreach (float x in new[] { -0.25f, 0.25f })
            foreach (float z in new[] { -0.18f, 0.18f })
                TacMaterials.Primitive(PrimitiveType.Cylinder, "Steel plate", root, new Vector3(x, h + 0.0004f, z),
                    new Vector3(0.12f, 0.0004f, 0.12f), steel, false);
            // No collider: a scaled cylinder gets a Ø100 mm sphere that the vehicle could not land on.
            DockPuck = TacMaterials.Primitive(PrimitiveType.Cylinder, "Primary power puck (BB9862)", root,
                new Vector3(0, h + 0.005f, 0), new Vector3(0.1f, 0.005f, 0.1f), puck, false).transform;
            Dock = root.gameObject.AddComponent<TacDock>();
            Dock.plateTop = h;
            Dock.puck = DockPuck;

            // Corner order ASSUMED: 28 back-left, 7 back-right, 19 front-right, 96 front-left (+Z = back).
            Vector2[] corners = { new(-1, 1), new(1, 1), new(1, -1), new(-1, -1) };
            float inset = dockMarkerSize * 0.7f + 0.02f;
            for (int i = 0; i < 4; i++)
                ArucoMarker.Create(root, DockMarkerIds[i], dockMarkerSize,
                    new Vector3(corners[i].x * (0.6f - inset), h, corners[i].y * (0.4f - inset)), Quaternion.identity);
        }

        // ---------------------------------------------------------------- Subsea structure and valves

        void BuildStructure(Random rng)
        {
            if (structureModel == null)
                throw new InvalidOperationException("TacCourse.structureModel is not set: run TAC → Create TAC arena scene.");
            var root = new GameObject("Subsea structure").transform;
            root.SetParent(generated, false);
            root.SetLocalPositionAndRotation(structurePosition, Quaternion.Euler(0, structureYaw, 0));
            Structure = root;
            // Keep the prefab's scale; drop its training-pool placement.
            Transform model = Instantiate(structureModel, root, false).transform;
            model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The model is shown unchanged. Its STEP valves are static meshes, so each gets an invisible TacValve
            // (handle logic for the arm and the referee) on the same axis.
            // Valve A on the vertical wall (opening towards the deck), valve B on the deck (opening up).
            // Both start near S (0°…30°) so that opening them is a real task.
            ValveA = ReplaceValve(root, model, "STEP valve - wall", "Valve A", Lerp(rng, 0, 30));
            ValveB = ReplaceValve(root, model, "STEP valve - deck", "Valve B", Lerp(rng, 0, 30));

            StructureSize = LocalBounds(root, model).size;

            // Candidate marker slots in unscaled model coordinates (position, outward normal); some deliberately hard to
            // see (under the deck, on the floor inside the frame, behind the wall).
            var slots = new (Vector3, Vector3)[]
            {
                (Model(-0.9f, 1.25f, -0.621f), Model(0, 0, 1)),    // wall, deck side
                (Model(0.25f, 1.1f, -0.621f), Model(0, 0, 1)),     // wall, deck side, beside valve A
                (Model(0.3f, 1.0f, -0.635f), Model(0, 0, -1)),     // wall, outside
                (Model(-0.8f, 0.65f, -0.635f), Model(0, 0, -1)),   // wall, outside, low
                (Model(0.95f, 0.891f, 0.35f), Model(0, 1, 0)),     // deck
                (Model(-0.15f, 0.891f, 0.4f), Model(0, 1, 0)),     // deck
                (Model(-0.6f, 0.8f, 0.635f), Model(0, 0, 1)),      // front guard
                (Model(0.75f, 0.65f, 0.635f), Model(0, 0, 1)),     // front guard
                (Model(0.6f, 0.87f, 0.35f), Model(0, -1, 0)),      // under the deck
                (Model(0.5f, 0.001f, 0.2f), Model(0, 1, 0)),       // floor inside the frame
                (Model(1.151f, 0.35f, 0.57f), Model(1, 0, 0)),     // corner upright, side
                (Model(-1.151f, 0.55f, -0.57f), Model(-1, 0, 0)),  // corner upright, other side
            };
            int count = rng.Next(5, 11);
            var chosen = Enumerable.Range(0, slots.Length).OrderBy(_ => rng.Next()).Take(count).ToArray();
            StructureMarkerIds = new int[count];
            for (int i = 0; i < count; i++)
            {
                (Vector3 position, Vector3 normal) = slots[chosen[i]];
                StructureMarkerIds[i] = rng.Next(1, 100); // IDs may repeat on the structure.
                // Markers keep their real size, so they are placed in the unscaled structure frame.
                ArucoMarker.Create(root, StructureMarkerIds[i], structureMarkerSize,
                    root.InverseTransformPoint(model.TransformPoint(position)),
                    Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Lerp(rng, 0, 360), 0));
            }
        }

        // Blender/FBX model coordinates → Unity: the FBX importer mirrors X.
        static Vector3 Model(float x, float y, float z) => new(-x, y, z);

        static Bounds LocalBounds(Transform frame, Transform model)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 point = frame.InverseTransformPoint(filter.transform.TransformPoint(local));
                    if (first) bounds = new Bounds(point, Vector3.zero); else bounds.Encapsulate(point);
                    first = false;
                }
            }
            return bounds;
        }

        // Seats an invisible TacValve in the model's valve: same bucket axis, rim and diameter (the booklet valve
        // scaled to the model).
        static TacValve ReplaceValve(Transform root, Transform model, string part, string name, float angle)
        {
            Transform mesh = model.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith(part));
            Bounds bounds = mesh.GetComponent<Renderer>().bounds;
            Vector3 centre = root.InverseTransformPoint(bounds.center);
            Vector3 size = root.InverseTransformVector(bounds.size);
            // A deck valve opens upwards; a wall valve opens away from the wall, towards the middle of the frame.
            bool onDeck = Mathf.Abs(size.y) < Mathf.Abs(size.z);
            Vector3 facing = onDeck ? Vector3.up : new Vector3(0, 0, -Mathf.Sign(centre.z));
            float depth = Mathf.Abs(Vector3.Dot(size, facing));
            Vector3 rim = centre + facing * (depth / 2);
            float scale = Mathf.Abs(size.x) / (2 * TacValve.OuterRadius);
            // Handle at 0° (S) points up on a wall valve, as on the booklet drawing.
            Quaternion rotation = onDeck ? Quaternion.identity : Quaternion.LookRotation(Vector3.up, facing);
            TacValve valve = TacValve.Create(root, name, rim - facing * (TacValve.RimHeight * scale), rotation, angle, false);
            valve.transform.localScale = Vector3.one * scale;
            return valve;
        }
    }
}
