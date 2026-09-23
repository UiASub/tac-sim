// Assets/TacSim/PipeGenerator/Scripts/ProceduralPipelineGenerator.cs
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace TAC.Pipeline
{
    /// Procedurally generates a TAC-Challenge inspection pipeline (booklet §3.2):
    /// straight ducts + elbows, joint sockets, pinger at the start, horizontal ArUco cards,
    /// everything clamped to a user-defined generation volume.
    ///
    /// STRAIGHT-PIPE DISCIPLINE:
    ///   * every run is a dead-straight, horizontal cylinder,
    ///   * the heading is changed ONLY inside AppendArc() (yaw about world up),
    ///   * an elbow is NEVER greater than 90 deg - even with enforceBookletLimits off,
    ///   * the path never touches an earlier part of itself,
    ///   * no two elbows touch (a straight run always separates them),
    ///   * the pipeline always ends on a straight run.
    ///
    /// COLLIDERS: physics is generated on its own child GameObject (Pipe_Collision) so it can be
    /// toggled / layer-assigned independently of the renderers. Default is a cheap capsule compound.
    [DisallowMultipleComponent]
    public class ProceduralPipelineGenerator : MonoBehaviour
    {
        public enum MarkerOrientation { HorizontalBooklet, PipeRadial }
        public enum PipeColliderMode { None, Capsules, Mesh }

        // ======================================================== §3.2 booklet constraints
        [Header("Booklet limits (TAC 2024 §3.2)")]
        [Tooltip("Clamp what the booklet fixes: <=10 m, 200 mm dia, 4..10 markers, joints -90..90 deg.")]
        public bool enforceBookletLimits = true;
        public float maxTotalLength = 10.0f;
        public float pipeDiameter = 0.200f;
        public int markerCount = 6;
        public int markerIdMin = 1;
        public int markerIdMax = 99;
        public float markerMinSeparation = 0.20f;
        public bool randomMarkerYaw = true;
        public float bendAngleLimit = 90.0f;
        [Tooltip("Always true: elbows rotate about world up only, so depth is constant by construction.")]
        public bool constantDepth = true;
        // allowDepthDrift / pitchJitterDegrees removed on purpose: pitching rotated pipe runs
        // outside of elbows, which the booklet forbids.

        // ======================================================== layout
        [Header("Layout - straight runs only")]
        [Range(0.5f, 12f)] public float totalLength = 7.5f;
        public Vector3 startPosition = Vector3.zero;
        public float pathHeight = 0f;
        public float startHeadingYaw = 0f;
        public bool randomStartInsideVolume = true;
        public float segmentLengthMin = 1.1f;
        public float segmentLengthMax = 2.6f;
        public float sampleSpacing = 0.05f;
        public uint seed = 20240510u;

        // ======================================================== elbows
        [Header("Elbows - the ONLY thing that rotates the pipe")]
        [Tooltip("Exact 90 deg elbows + axis aligned headings: every run is parallel to X or Z.")]
        public bool orthogonalLayout = true;
        [Tooltip("Elbow angle when orthogonalLayout is off. Clamped to <= 90 deg (§3.2.3).")]
        [Range(5f, 90f)] public float elbowAngle = 90f;
        [Tooltip("Random elbow angles are drawn from [elbowAngleMin .. elbowAngle] when orthogonalLayout is off.")]
        [Range(5f, 90f)] public float elbowAngleMin = 50f;
        [Tooltip("Elbow sweep radius. Small = compact fitting, so the pipe reads as straight.")]
        public float elbowRadius = 0.18f;
        [Tooltip("Straight pipe that must separate two elbows (clamped into segmentLengthMin).")]
        public float minStraightBetweenElbows = 0.35f;

        // ======================================================== self avoidance
        [Header("Self avoidance (pipe must not touch itself)")]
        public bool selfAvoidPath = true;
        [Tooltip("Clearance to any earlier part of the path, in pipe radii.")]
        [Range(1f, 8f)] public float selfClearancePipeRadii = 2.6f;

        // ======================================================== colliders
        [Header("Colliders")]
        [Tooltip("Capsules = cheap compound along the spine (recommended).\nMesh = exact render mesh (slow cook).\nNone = no physics on the duct.")]
        public PipeColliderMode pipeColliderMode = PipeColliderMode.Capsules;
        [Tooltip("Trigger colliders pass raycasts/contacts through - only tick this for volume sensing.")]
        public bool colliderIsTrigger = false;
        [Tooltip("Put physics on its own child 'Pipe_Collision' instead of on the renderer objects.")]
        public bool separateColliderObject = true;
        [Tooltip("Optional physics layer NAME (empty = inherit parent). e.g. Pipeline / Obstacle.")]
        public string colliderLayer = "";
        [Tooltip("Thin box collider covering each ArUco card - 'robot saw card #N' events.")]
        public bool addMarkerColliders = true;
        public bool markerColliderIsTrigger = true;
        public bool addPingerCollider = true;
        [Tooltip("Legacy: also put an exact non-convex MeshCollider directly on the renderers.")]
        public bool addMeshCollider = false;

        // ======================================================== markers
        [Header("ArUco markers")]
        [Tooltip("Outer edge of the square card in metres; textures, module size and spacing adapt.")]
        [Range(0.04f, 0.6f)] public float markerSize = 0.150f;
        [Range(4, 7)] public int markerBits = 4;
        [Range(0, 4)] public int quietZoneModules = 1;
        public int requestedCardTextureSize = 256;
        public bool crispPixelFilter = true;
        public bool keepCardTexturesAfterClear = true;
        public float markerLift = 0.006f;
        public MarkerOrientation markerOrientation = MarkerOrientation.HorizontalBooklet;
        public float markerTiltLimitDegrees = 4f;
        public bool addClearPlasticFrame = true;
        [Range(0f, 1f)] public float frameBorderFraction = 0.18f;
        public bool showIdLabelOnCard = false;
        [Tooltip("chev.me / OpenCV DICT_ARUCO_ORIGINAL PNGs, indexed with externalIdOffset.")]
        public bool useExternalMarkerTextures = false;
        public Texture2D[] externalMarkerTextures;
        public int externalIdOffset = 1;

        // ======================================================== volume
        [Header("Generation volume")]
        public bool useVolume = true;
        public Vector3 volumeCenter = Vector3.zero;
        public Vector3 volumeSize = new Vector3(6f, 1.5f, 6f);
        public float volumeSafetyMargin = 0.18f;
        public bool addVolumeCollider = false;
        public bool drawGizmos = true;

        // ======================================================== geometry
        [Header("Geometry & look")]
        [Range(6, 64)] public int radialResolution = 22;
        public bool hollowPipe = true;
        public float wallThickness = 0.012f;
        public bool addJointCollars = true;
        public float collarLength = 0.14f;
        [Range(1.0f, 1.8f)] public float collarRadiusGain = 1.22f;
        public bool addEndFlanges = true;
        public bool addPinger = true;
        public float uvWorldSize = 1.25f;
        public bool generateOnAwake = true;
        [Tooltip("Generate an edit-mode preview (renamed: 'runInEditMode' hides MonoBehaviour's).")]
        public bool generateInEditMode = false;
        public bool logGroundTruth = true;
        public PipelineSurfaceStyle style;

        // ======================================================== read-outs
        public float PathLength { get; private set; }
        public int JointCount { get { return _jointRanges.Count; } }
        public int PlacedMarkerCount { get { return _markers.Count; } }
        public int ColliderCount { get; private set; }
        public IReadOnlyList<PipelineMarkerInfo> Markers { get { return _markers; } }
        public IReadOnlyList<Vector3> PathPoints { get { return _spinePos; } }
        public IReadOnlyList<Vector2Int> JointRanges { get { return _jointRanges; } }
        public GameObject ColliderRootGameObject { get { return _colliderRoot != null ? _colliderRoot.gameObject : null; } }

        // ======================================================== internals
        readonly List<Vector3> _spinePos = new List<Vector3>();
        readonly List<float> _spineArc = new List<float>();
        readonly List<Vector3> _tan = new List<Vector3>();
        readonly List<Vector3> _nor = new List<Vector3>();
        readonly List<Vector2Int> _jointRanges = new List<Vector2Int>();
        readonly List<PipelineMarkerInfo> _markers = new List<PipelineMarkerInfo>();

        Transform _root, _markerRoot, _colliderRoot;
        Mesh _sharedQuad, _pipeMesh, _collarMesh;
        readonly List<Material> _markerMaterials = new List<Material>();
        readonly Dictionary<int, Material> _markerMatById = new Dictionary<int, Material>();
        readonly Dictionary<int, Texture2D> _markerTextures = new Dictionary<int, Texture2D>();

        System.Random _rng;
        Vector3 _pos, _dir;
        float _travelled;
        float _straightSinceLastElbow;
        bool _boxedInLogged;
        const float Eps = 1e-6f;

        float PipeRadius { get { return Mathf.Max(0.01f, pipeDiameter * 0.5f); } }
        float TotalLengthEffective
        {
            get { return enforceBookletLimits ? Mathf.Min(totalLength, maxTotalLength) : totalLength; }
        }
        int MarkerCountEffective
        {
            get { return enforceBookletLimits ? Mathf.Clamp(markerCount, 4, 10) : Mathf.Max(1, markerCount); }
        }
        /// Elbows are yaw-only, so |angle| <= 90 deg is a hard invariant - not just a booklet clamp.
        float BendLimitEffective { get { return Mathf.Min(Mathf.Abs(bendAngleLimit), 90f); } }
        float ElbowRadius { get { return Mathf.Max(PipeRadius * 1.2f, elbowRadius); } }
        float SelfClearance
        {
            get { return selfAvoidPath ? Mathf.Max(0.02f, PipeRadius * Mathf.Max(1f, selfClearancePipeRadii)) : 0f; }
        }
        public Bounds VolumeBounds
        {
            get
            {
                if (useVolume) return new Bounds(volumeCenter, volumeSize);
                Vector3 c = _spinePos.Count > 0 ? _spinePos[0] : Vector3.zero;
                return new Bounds(c, new Vector3(4000f, 4000f, 4000f));
            }
        }

        // ======================================================== lifecycle
        void OnEnable() { if (generateInEditMode && !Application.isPlaying) Generate(); }
        void OnDisable() { if (!Application.isPlaying) ClearGenerated(); }
        void Awake() { if (generateOnAwake) Generate(); }
        void OnDestroy() { ClearGenerated(); }   // style.Release() is a deliberate, explicit call

        // ======================================================== public API
        public void Generate()
        {
            ClampParameters();
            ClearGenerated();
            EnsureRoot();
            _rng = new System.Random(unchecked((int)(seed * 2654435761u ^ 0x51ED2701u)));

            BuildSpine();
            if (_spinePos.Count < 2)
            {
                Debug.LogError("[Pipeline] Degenerate path - check volume size / total length.", this);
                return;
            }
            ComputeFrames();

            BuildPipeMesh();
            if (addJointCollars || addEndFlanges) BuildCollars();
            BuildPipeCollider();
            if (addPinger) BuildPinger();
            BuildMarkers();
            if (addVolumeCollider && useVolume) BuildVolumeCollider();

            if (logGroundTruth)
                Debug.Log(ToString() + "\n  sequence from pinger: " + SequenceFromPingerString(), this);
        }

        /// Rebuild with the SAME seed (deterministic).
        public void Regenerate() { Generate(); }
        public void Clear() { ClearGenerated(); }

        /// New seed, then rebuild. Copy the seed out of the Inspector if you want this layout back.
        public void Randomize()
        {
            seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            Generate();
        }

        /// Same layout family every time: deterministic explore button.
        public void RandomizeFrom(uint fromSeed)
        {
            seed = fromSeed == 0u ? 1u : fromSeed;
            int before = _rng == null ? 0 : 0;   // no-op, keeps the API obvious
            Generate();
        }

        public void SetTotalLength(float m) { totalLength = m; Generate(); }
        public void SetPipeDiameter(float m) { pipeDiameter = m; Generate(); }
        public void SetMarkerCount(int n) { markerCount = n; Generate(); }
        public void SetMarkerSize(float m) { markerSize = Mathf.Max(0.02f, m); Generate(); }
        public void SetMarkerIdRange(int min, int max) { markerIdMin = min; markerIdMax = max; Generate(); }
        public void SetColliderMode(PipeColliderMode mode) { pipeColliderMode = mode; Generate(); }

        public void SetColour(PipelinePart part, Color c)
        {
            if (style == null) return;
            switch (part)
            {
                case PipelinePart.Pipe: style.pipeColour = c; break;
                case PipelinePart.Joint: style.jointColour = c; break;
                case PipelinePart.EndCap: style.endCapColour = c; break;
                case PipelinePart.Pinger: style.pingerColour = c; break;
                case PipelinePart.MarkerCard: style.markerPaper = c; break;
                case PipelinePart.ClearFrame: style.clearPlastic = c; break;
            }
            style.RefreshMaterials();
        }

        public List<int> GetMarkerSequenceFromPinger()
        {
            var list = new List<int>(_markers.Count);
            for (int i = 0; i < _markers.Count; i++) list.Add(_markers[i].markerId);
            return list;
        }

        public string SequenceFromPingerString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _markers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_markers[i].markerId);
            }
            return sb.ToString();
        }

        public void FitVolumeToPath(float pad = 0.25f)
        {
            if (_spinePos.Count == 0) return;
            var b = new Bounds(_spinePos[0], Vector3.zero);
            for (int i = 1; i < _spinePos.Count; i++) b.Encapsulate(_spinePos[i]);
            b.Expand(new Vector3(pad, pad, pad));
            volumeCenter = b.center;
            volumeSize = b.size;
        }

        /// Toggle physics without rebuilding any geometry.
        public void SetCollidersEnabled(bool enabled)
        {
            if (_colliderRoot == null) return;
            _colliderRoot.gameObject.SetActive(enabled);
        }

        public override string ToString()
        {
            return string.Format(
                "[Pipeline] len={0:0.00} m, joints={1}, markers={2}, colliders={3} ({4}), dia={5:0} mm, card={6:0} mm, seed={7}",
                PathLength, JointCount, _markers.Count, ColliderCount, pipeColliderMode,
                pipeDiameter * 1000f, markerSize * 1000f, seed);
        }

        // ======================================================== clamping
        void ClampParameters()
        {
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<PipelineSurfaceStyle>();
                style.name = "TAC_PipelineSurfaceStyle_Runtime";
                style.hideFlags = HideFlags.DontSave;
            }

            maxTotalLength = Mathf.Max(0.5f, maxTotalLength);
            if (enforceBookletLimits) totalLength = Mathf.Min(totalLength, maxTotalLength);
            totalLength = Mathf.Clamp(totalLength, 0.5f, enforceBookletLimits ? maxTotalLength : 100f);

            if (enforceBookletLimits) pipeDiameter = Mathf.Max(0.2f, pipeDiameter);
            pipeDiameter = Mathf.Clamp(pipeDiameter, 0.02f, 2f);

            markerCount = MarkerCountEffective;
            markerIdMin = Mathf.Max(0, markerIdMin);
            markerIdMax = Mathf.Max(markerIdMin, Mathf.Max(markerIdMin, markerIdMax));
            if (enforceBookletLimits)
            {
                markerIdMin = Mathf.Max(1, markerIdMin);
                markerIdMax = Mathf.Min(99, markerIdMax);
            }

            // ---- straight-pipe discipline -------------------------------------------------
            constantDepth = true;                                        // yaw-only elbows -> flat path
            bendAngleLimit = Mathf.Clamp(bendAngleLimit, 1f, 90f);       // NEVER above 90 deg
            elbowAngle = Mathf.Clamp(elbowAngle, 1f, BendLimitEffective);
            elbowAngleMin = Mathf.Clamp(elbowAngleMin, 1f, elbowAngle);
            elbowRadius = Mathf.Max(PipeRadius * 1.2f, elbowRadius);

            minStraightBetweenElbows = Mathf.Max(0.05f, minStraightBetweenElbows);
            segmentLengthMax = Mathf.Max(segmentLengthMax, minStraightBetweenElbows);
            segmentLengthMin = Mathf.Clamp(segmentLengthMin, minStraightBetweenElbows, segmentLengthMax);
            selfClearancePipeRadii = Mathf.Clamp(selfClearancePipeRadii, 1f, 8f);
            if (orthogonalLayout) startHeadingYaw = Mathf.Round(startHeadingYaw / 90f) * 90f;

            // ---- markers / tessellation ----------------------------------------------------
            // Auto-adjust separation to the card size, never below the booklet's 0.2 m.
            float needForSize = markerSize * 1.15f + 0.02f;
            markerMinSeparation = enforceBookletLimits
                ? Mathf.Max(markerMinSeparation, Mathf.Max(needForSize, 0.2f))
                : Mathf.Max(markerMinSeparation, needForSize);

            sampleSpacing = Mathf.Clamp(sampleSpacing, 0.01f, Mathf.Max(0.05f, PipeRadius));
            wallThickness = Mathf.Clamp(wallThickness, 0.002f, PipeRadius * 0.6f);
            quietZoneModules = Mathf.Clamp(quietZoneModules, 0, 4);
            markerBits = Mathf.Clamp(markerBits, ArUcoCodeGenerator.MinBits, ArUcoCodeGenerator.MaxBits);
        }

        void EnsureRoot()
        {
            if (_root != null) return;
            var g = new GameObject("Pipeline_Geometry");
            g.transform.SetParent(transform, false);
            _root = g.transform;
            var m = new GameObject("Markers");
            m.transform.SetParent(transform, false);
            _markerRoot = m.transform;
        }

        void ClearGenerated()
        {
            if (_root != null) DestroyNode(_root.gameObject);
            if (_markerRoot != null) DestroyNode(_markerRoot.gameObject);
            if (_colliderRoot != null) DestroyNode(_colliderRoot.gameObject);
            _root = null; _markerRoot = null; _colliderRoot = null;

            if (_pipeMesh != null) { DestroyUnityObject(_pipeMesh); _pipeMesh = null; }
            if (_collarMesh != null) { DestroyUnityObject(_collarMesh); _collarMesh = null; }
            if (_sharedQuad != null) { DestroyUnityObject(_sharedQuad); _sharedQuad = null; }

            for (int i = 0; i < _markerMaterials.Count; i++) DestroyUnityObject(_markerMaterials[i]);
            _markerMaterials.Clear();
            _markerMatById.Clear();

            if (!keepCardTexturesAfterClear)
                foreach (var kv in _markerTextures) DestroyUnityObject(kv.Value);
            _markerTextures.Clear();

            _markers.Clear();
            _jointRanges.Clear();
            _spinePos.Clear(); _spineArc.Clear(); _tan.Clear(); _nor.Clear();
            PathLength = 0f;
            ColliderCount = 0;
        }

        static void DestroyNode(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        static void DestroyUnityObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ======================================================== spine
        /// Straight runs only. The heading changes EXCLUSIVELY inside AppendArc (a yaw-only elbow
        /// about world up, |angle| <= 90 deg), so depth is constant and nothing else twists (§3.2.3).
        void BuildSpine()
        {
            _boxedInLogged = false;

            _pos = startPosition;
            _pos.y = pathHeight;
            _dir = FlatDir(DirFromYaw(startHeadingYaw));

            if (randomStartInsideVolume) PickRandomStart();
            _pos = ClampPointToVolume(_pos);
            if (!InsideVolume(_pos)) _pos = Shrink(VolumeBounds, volumeSafetyMargin).center;
            _dir = FlatDir(_dir);

            _travelled = 0f;
            _straightSinceLastElbow = float.MaxValue;        // the first elbow may come early
            AppendSample(_pos);

            float hardMin = Mathf.Max(0.10f, PipeRadius * 1.25f);
            float clearance = SelfClearance;
            int guard = 0;

            while (guard++ < 400)
            {
                float remaining = TotalLengthEffective - _travelled;
                if (remaining <= hardMin) break;

                float want = segmentLengthMin + Rng() * Mathf.Max(0f, segmentLengthMax - segmentLengthMin);
                float run = Mathf.Min(want, remaining);
                run = Mathf.Min(run, TraceVolume(_pos, _dir, run));                    // stop at the wall
                run = Mathf.Min(run, TraceSelfClear(_pos, _dir, run, clearance));      // stop before own pipe

                AppendStraight(run);
                _travelled = _spineArc[_spineArc.Count - 1];

                if (_travelled >= TotalLengthEffective - hardMin) break;               // finish on a straight

                if (_straightSinceLastElbow < hardMin * 0.5f)
                {
                    // Wall or our own pipe immediately ahead. A second elbow with no straight duct
                    // between them is just a kink, so the pipeline ends here instead of folding back.
                    LogBoxedIn();
                    break;
                }

                float bend = PickFeasibleBend();
                if (bend == 0f) { LogBoxedIn(); break; }
                if (!AppendArc(bend)) break;

                _travelled = _spineArc[_spineArc.Count - 1];
            }

            PathLength = _spineArc.Count > 0 ? _spineArc[_spineArc.Count - 1] : 0f;
        }

        void LogBoxedIn()
        {
            if (_boxedInLogged) return;
            _boxedInLogged = true;
            Debug.LogWarning(string.Format(
                "[Pipeline] Boxed in after {0:0.00} m of {1:0.00} m - enlarge the generation volume, shrink " +
                "elbowRadius / selfClearancePipeRadii, or allow shallower elbows (orthogonalLayout = off).",
                _travelled, TotalLengthEffective), this);
        }

        void AppendStraight(float length)
        {
            if (length <= Eps) return;                                   // no duplicate samples
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.005f, sampleSpacing)));
            float step = length / steps;
            for (int i = 0; i < steps; i++) { _pos += _dir * step; AppendSample(_pos); }
            _travelled += length;
            _straightSinceLastElbow += length;
        }

        /// Circular elbow about world up (|angleDeg| <= BendLimitEffective <= 90 deg).
        /// This is THE only place the heading is allowed to change.
        bool AppendArc(float angleDeg)
        {
            if (Mathf.Abs(angleDeg) < 0.05f || Mathf.Abs(angleDeg) > BendLimitEffective + 0.01f) return false;

            float radius = ElbowRadius;
            Vector3 side = Vector3.Cross(Vector3.up, _dir);
            if (side.sqrMagnitude < Eps) return false;
            side.Normalize();

            float arcLen = Mathf.Abs(angleDeg) * Mathf.Deg2Rad * radius;
            Vector3 start = _pos;                                   // keep: _pos moves below
            Vector3 pivot = start + side * radius * Mathf.Sign(angleDeg);
            int steps = Mathf.Max(2, Mathf.CeilToInt(arcLen / Mathf.Max(0.005f, sampleSpacing)));
            float dAngle = angleDeg / steps;

            int startIndex = _spinePos.Count - 1;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 v = Quaternion.AngleAxis(dAngle * i, Vector3.up) * (start - pivot);
                AppendSample(pivot + v);
            }

            // Land exactly on the elbow exit BEFORE rotating the heading.
            // (The old code left _pos at the elbow entry, so the next straight run was written out of
            //  the entry point -> the path folded back on itself at every joint.)
            _pos = pivot + Quaternion.AngleAxis(angleDeg, Vector3.up) * (start - pivot);
            _dir = FlatDir(Quaternion.AngleAxis(angleDeg, Vector3.up) * _dir);
            if (constantDepth) _pos.y = start.y;                     // horizontal anyway; belt & braces

            _travelled += arcLen;
            _straightSinceLastElbow = 0f;
            _jointRanges.Add(new Vector2Int(startIndex, _spinePos.Count - 1));
            return true;
        }

        float PickFeasibleBend()
        {
            float lim = BendLimitEffective;                          // <= 90 deg, always

            if (orthogonalLayout)
            {
                float sgn = Rng() > 0.5f ? -1f : 1f;
                float[] ladder = { 90f, 75f, 60f, 45f, 30f };
                for (int i = 0; i < ladder.Length; i++)
                {
                    float m = Mathf.Min(ladder[i], lim);
                    if (ArcFits(m * sgn)) return m * sgn;
                    if (ArcFits(-m * sgn)) return -m * sgn;
                }
                return 0f;
            }

            float lo = Mathf.Min(elbowAngleMin, elbowAngle);
            float hi = Mathf.Max(lo, elbowAngle);
            float mag = Mathf.Clamp(lo + (hi - lo) * Rng(), 1f, lim);
            float a = mag * (Rng() > 0.5f ? -1f : 1f);

            if (ArcFits(a)) return a;
            if (ArcFits(-a)) return -a;
            for (float f = 0.75f; f >= 0.2f; f -= 0.25f)             // shrink until a fitting exists
            {
                float m = Mathf.Clamp(mag * f, 5f, lim);
                if (ArcFits(m)) return m;
                if (ArcFits(-m)) return -m;
            }
            return 0f;
        }

        bool ArcFits(float angleDeg, int probes = 12)
        {
            if (Mathf.Abs(angleDeg) < 0.05f || Mathf.Abs(angleDeg) > BendLimitEffective + 0.01f) return false;

            float radius = ElbowRadius;
            Vector3 side = Vector3.Cross(Vector3.up, _dir);
            if (side.sqrMagnitude < Eps) return false;
            side.Normalize();
            Vector3 pivot = _pos + side * radius * Mathf.Sign(angleDeg);

            Vector3 tip = _pos;
            for (int i = 1; i <= probes; i++)
            {
                float ang = angleDeg * (i / (float)probes);
                Vector3 p = pivot + Quaternion.AngleAxis(ang, Vector3.up) * (_pos - pivot);
                if (!InsideVolume(p)) return false;
                if (!ClearOfOldPath(p)) return false;
                tip = p;
            }

            // the straight run leaving the elbow must not open straight into the old path either
            Vector3 exitDir = FlatDir(Quaternion.AngleAxis(angleDeg, Vector3.up) * _dir);
            float clearance = SelfClearance;
            for (float t = sampleSpacing; t <= clearance * 2f + 0.05f; t += sampleSpacing)
                if (!ClearOfOldPath(tip + exitDir * t)) return false;
            return true;
        }

        /// Clearance test against the path built so far. The tail right at the tip belongs to the
        /// joint we are about to make, so it is skipped.
        bool ClearOfOldPath(Vector3 p)
        {
            if (!selfAvoidPath || _spinePos.Count < 2) return true;
            float c = SelfClearance, c2 = c * c;
            int tail = TailStart(c);
            for (int k = 0; k <= tail; k++)
                if ((_spinePos[k] - p).sqrMagnitude < c2) return false;
            return true;
        }

        int TailStart(float clearance)
        {
            int i = _spinePos.Count - 1;
            float tip = _spineArc[i];
            while (i > 0 && tip - _spineArc[i] < clearance * 2f) i--;
            return i;
        }

        /// How far may we fly straight before our own pipe comes too close?
        float TraceSelfClear(Vector3 o, Vector3 d, float maxDist, float clearance)
        {
            if (!selfAvoidPath || clearance <= 0f || _spinePos.Count < 2) return maxDist;
            float c2 = clearance * clearance;
            int tail = TailStart(clearance);
            float step = Mathf.Max(0.01f, sampleSpacing * 0.5f);

            for (float t = step; t <= maxDist; t += step)
            {
                Vector3 p = o + d * t;
                bool hit = false;
                for (int k = 0; k <= tail; k++)
                    if ((_spinePos[k] - p).sqrMagnitude < c2) { hit = true; break; }
                if (hit) return Mathf.Max(0f, t - step);
            }
            return maxDist;
        }

        void AppendSample(Vector3 p)
        {
            _spinePos.Add(p);
            float arc = _spineArc.Count == 0
                ? 0f
                : _spineArc[_spineArc.Count - 1] + Vector3.Distance(_spinePos[_spinePos.Count - 2], p);
            _spineArc.Add(arc);
        }

        void PickRandomStart()
        {
            Bounds b = Shrink(VolumeBounds, volumeSafetyMargin + PipeRadius);
            for (int i = 0; i < 64; i++)
            {
                var p = new Vector3(
                    Mathf.Lerp(b.min.x, b.max.x, Rng()),
                    pathHeight,
                    Mathf.Lerp(b.min.z, b.max.z, Rng()));
                float yaw = orthogonalLayout ? Mathf.Round(Rng() * 4f) * 90f : Rng() * 360f;
                Vector3 d = FlatDir(DirFromYaw(yaw));
                if (TraceVolume(p, d, Mathf.Min(segmentLengthMin, 0.8f)) > 0.05f && InsideVolume(p))
                {
                    _pos = p; _dir = d; return;
                }
            }
            _pos = b.center; _pos.y = pathHeight;
            _dir = FlatDir(DirFromYaw(orthogonalLayout ? 0f : Rng() * 360f));
        }

        static Vector3 DirFromYaw(float yaw) { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; }

        /// Headings are always horizontal: the pipe is laid at a constant depth (§3.2.3).
        static Vector3 FlatDir(Vector3 d)
        {
            d.y = 0f;
            return d.sqrMagnitude < Eps ? Vector3.forward : d.normalized;
        }

        // ======================================================== volume
        bool InsideVolume(Vector3 p)
        {
            Bounds b = Shrink(VolumeBounds, volumeSafetyMargin);
            return p.x > b.min.x && p.x < b.max.x &&
                   p.y > b.min.y && p.y < b.max.y &&
                   p.z > b.min.z && p.z < b.max.z;
        }

        float TraceVolume(Vector3 o, Vector3 d, float maxDist)
        {
            Bounds b = Shrink(VolumeBounds, volumeSafetyMargin);
            float t = maxDist;
            t = Mathf.Min(t, Slab(o.x, d.x, b.min.x, b.max.x));
            t = Mathf.Min(t, Slab(o.y, d.y, b.min.y, b.max.y));
            t = Mathf.Min(t, Slab(o.z, d.z, b.min.z, b.max.z));
            return Mathf.Max(0f, t);
        }

        static float Slab(float o, float d, float mn, float mx)
        {
            if (Mathf.Abs(d) < Eps) return (o < mn || o > mx) ? 0f : float.MaxValue;
            float t1 = (mn - o) / d, t2 = (mx - o) / d;
            float exit = Mathf.Max(t1, t2);
            return exit < 0f ? 0f : exit;
        }

        Vector3 ClampPointToVolume(Vector3 p)
        {
            Bounds b = Shrink(VolumeBounds, volumeSafetyMargin);
            p.x = Mathf.Clamp(p.x, b.min.x, b.max.x);
            p.y = Mathf.Clamp(p.y, b.min.y, b.max.y);
            p.z = Mathf.Clamp(p.z, b.min.z, b.max.z);
            return p;
        }

        static Bounds Shrink(Bounds b, float margin)
        {
            var size = new Vector3(
                Mathf.Max(0.05f, b.size.x - 2f * margin),
                Mathf.Max(0.05f, b.size.y - 2f * margin),
                Mathf.Max(0.05f, b.size.z - 2f * margin));
            return new Bounds(b.center, size);
        }

        // ======================================================== frames
        void ComputeFrames()
        {
            int n = _spinePos.Count;
            _tan.Clear(); _nor.Clear();
            if (n < 2) return;

            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                Vector3 t = _spinePos[b] - _spinePos[a];
                if (t.sqrMagnitude < Eps) t = i > 0 ? _tan[i - 1] : Vector3.forward;
                _tan.Add(t.normalized);
            }

            Vector3 prevT = _tan[0];
            Vector3 curN = Perp(prevT);
            for (int i = 0; i < n; i++)
            {
                Vector3 T = _tan[i];
                if (i > 0)
                {
                    Vector3 axis = Vector3.Cross(prevT, T);
                    if (axis.sqrMagnitude > 1e-10f)
                    {
                        float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(prevT, T), -1f, 1f));
                        curN = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, axis.normalized) * curN;
                    }
                    prevT = T;
                }
                curN -= T * Vector3.Dot(curN, T);
                if (curN.sqrMagnitude < Eps) curN = Perp(T);
                curN.Normalize();
                _nor.Add(curN);
            }
        }

        static Vector3 Perp(Vector3 t)
        {
            Vector3 a = Mathf.Abs(t.y) > 0.95f ? Vector3.right : Vector3.up;
            return Vector3.Cross(a, t).normalized;
        }

        struct Sample { public Vector3 p, t, n, b; }

        Sample SampleAtIndex(float indexF)
        {
            int n = _spinePos.Count;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(indexF), 0, n - 1);
            int i1 = Mathf.Min(i0 + 1, n - 1);
            float f = Mathf.Clamp01(indexF - i0);

            var s = new Sample();
            s.p = Vector3.Lerp(_spinePos[i0], _spinePos[i1], f);
            Vector3 tv = Vector3.Lerp(_tan[i0], _tan[i1], f);
            if (tv.sqrMagnitude < Eps) tv = _tan[i0];
            s.t = tv.normalized;
            Vector3 nv = Vector3.Lerp(_nor[i0], _nor[i1], f);
            nv -= s.t * Vector3.Dot(nv, s.t);
            if (nv.sqrMagnitude < Eps) nv = Perp(s.t);
            s.n = nv.normalized;
            s.b = Vector3.Cross(s.t, s.n);
            return s;
        }

        Sample SampleAtArcLength(float s)
        {
            if (_spineArc.Count == 1) return SampleAtIndex(0f);
            int lo = 0, hi = _spineArc.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (_spineArc[mid] <= s) lo = mid; else hi = mid;
            }
            float span = Mathf.Max(Eps, _spineArc[hi] - _spineArc[lo]);
            float f = Mathf.Clamp01((s - _spineArc[lo]) / span);
            return SampleAtIndex(lo + f);
        }

        float ArcLengthAtIndex(float indexF)
        {
            int n = _spineArc.Count;
            if (n == 0) return 0f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(indexF), 0, n - 1);
            int i1 = Mathf.Min(i0 + 1, n - 1);
            return Mathf.Lerp(_spineArc[i0], _spineArc[i1], Mathf.Clamp01(indexF - i0));
        }

        // ======================================================== meshes
        void BuildPipeMesh()
        {
            float rOuter = PipeRadius;
            float rInner = hollowPipe ? Mathf.Max(0.001f, PipeRadius - wallThickness) : 0f;

            // Closed profile in (alongFraction, radius): watertight hollow duct with annular ends.
            var profile = new[]
            {
                new Vector2(0f, rOuter),
                new Vector2(1f, rOuter),
                new Vector2(1f, rInner),
                new Vector2(0f, rInner)
            };

            var bucket = new MeshBucket();
            Revolve(0, _spinePos.Count - 1, profile, bucket);
            _pipeMesh = bucket.ToMesh("TAC_PipelineMesh");

            var go = new GameObject("Pipe_Body");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = _pipeMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = style.GetMaterial(PipelinePart.Pipe);
            if (addMeshCollider) go.AddComponent<MeshCollider>().sharedMesh = _pipeMesh;
        }

        void BuildCollars()
        {
            var bucket = new MeshBucket();
            float rC = PipeRadius * collarRadiusGain;
            int n = _spinePos.Count;

            for (int j = 0; j < _jointRanges.Count; j++)
                AddCollar((_jointRanges[j].x + _jointRanges[j].y) / 2, rC, n, bucket);

            if (addEndFlanges)
            {
                AddCollar(0, rC, n, bucket);
                AddCollar(n - 1, rC, n, bucket);
            }

            if (bucket.vertexCount == 0) return;
            _collarMesh = bucket.ToMesh("TAC_PipelineJoints");

            var go = new GameObject("Pipe_Joints");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = _collarMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = style.GetMaterial(PipelinePart.Joint);
            if (addMeshCollider) go.AddComponent<MeshCollider>().sharedMesh = _collarMesh;
        }

        void AddCollar(int centreIndex, float rC, int n, MeshBucket bucket)
        {
            int half = Mathf.Max(1, Mathf.RoundToInt(collarLength * 0.5f / sampleSpacing));
            int i0 = Mathf.Max(0, centreIndex - half);
            int i1 = Mathf.Min(n - 1, centreIndex + half);
            if (i1 - i0 < 1) return;

            var profile = new[]
            {
                new Vector2(0f, PipeRadius * 1.002f),
                new Vector2(0f, rC),
                new Vector2(1f, rC),
                new Vector2(1f, PipeRadius * 1.002f)
            };
            Revolve(i0, i1, profile, bucket);
        }

        /// Revolve a closed (alongFraction, radius) profile over a range of spine samples.
        /// Analytic normals: N = radial*ds - tangent*dr  (crisp edges between wall and end faces).
        void Revolve(int iStart, int iEnd, Vector2[] profile, MeshBucket bucket)
        {
            int rings = Mathf.Max(2, iEnd - iStart + 1);
            int sides = Mathf.Clamp(radialResolution, 3, 128);
            int cols = sides + 1;

            for (int k = 0; k < profile.Length; k++)
            {
                Vector2 a = profile[k];
                Vector2 c = profile[(k + 1) % profile.Length];
                Vector2 d2 = c - a;
                if (Mathf.Abs(d2.x) < Eps && Mathf.Abs(d2.y) < Eps) continue;

                int vBase = bucket.vertexCount;

                for (int i = 0; i < rings; i++)
                {
                    float tEdge = rings == 1 ? 0f : i / (float)(rings - 1);
                    float along = Mathf.Lerp(a.x, c.x, tEdge);
                    float radius = Mathf.Lerp(a.y, c.y, tEdge);
                    float indexF = Mathf.Lerp(iStart, iEnd, along);
                    Sample s = SampleAtIndex(indexF);
                    float alongArc = ArcLengthAtIndex(indexF);

                    for (int j = 0; j < cols; j++)
                    {
                        float ang = (j / (float)sides) * Mathf.PI * 2f;
                        Vector3 radial = Mathf.Cos(ang) * s.n + Mathf.Sin(ang) * s.b;
                        bucket.vertices.Add(s.p + radial * radius);
                        bucket.normals.Add(Vector3.Normalize(radial * d2.x - s.t * d2.y));
                        bucket.uv.Add(new Vector2(alongArc / Mathf.Max(0.05f, uvWorldSize), j / (float)sides));
                    }
                }

                for (int i = 0; i < rings - 1; i++)
                {
                    for (int j = 0; j < sides; j++)
                    {
                        int v00 = vBase + i * cols + j;
                        int v01 = v00 + 1;
                        int v10 = vBase + (i + 1) * cols + j;
                        int v11 = v10 + 1;
                        bucket.tris.Add(v00); bucket.tris.Add(v01); bucket.tris.Add(v11);
                        bucket.tris.Add(v00); bucket.tris.Add(v11); bucket.tris.Add(v10);
                    }
                }
            }
        }

        sealed class MeshBucket
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> tris = new List<int>();
            public int vertexCount { get { return vertices.Count; } }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh
                {
                    name = name,
                    indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    hideFlags = HideFlags.DontSave
                };
                m.SetVertices(vertices);
                m.SetNormals(normals);
                m.SetUVs(0, uv);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        // ======================================================== colliders
        /// Physics on its own child GameObject: one toggle for the whole pipe, its own layer, and it
        /// survives re-painting of the renderers. Colliders are authored in this transform's space,
        /// exactly like the pipe mesh, so Mesh and Capsule modes agree.
        Transform ColliderRoot()
        {
            if (_colliderRoot != null) return _colliderRoot;
            var g = new GameObject("Pipe_Collision");
            g.transform.SetParent(separateColliderObject ? transform : _root, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one;
            _colliderRoot = g.transform;
            return _colliderRoot;
        }

        void BuildPipeCollider()
        {
            ColliderCount = 0;
            if (pipeColliderMode == PipeColliderMode.None || _spinePos.Count < 2) return;

            Transform root = ColliderRoot();
            float r = PipeRadius;

            if (pipeColliderMode == PipeColliderMode.Mesh)
            {
                if (_pipeMesh != null) AddMeshCollider(root, "Col_PipeMesh", _pipeMesh);
                if (_collarMesh != null) AddMeshCollider(root, "Col_JointMesh", _collarMesh);
            }
            else
            {
                BuildSpineColliders(root, r);
            }
        }

        /// One capsule per straight run, one capsule per chord through every elbow arc.
        /// For a 10 m pipe that is a few dozen primitive colliders - no mesh cooking at all, and the
        /// duct is closed on the outside so nothing can clip through the hollow interior.
        void BuildSpineColliders(Transform root, float r)
        {
            int n = _spinePos.Count;
            int i = 0;
            while (i < n - 1)
            {
                Vector3 a = _spinePos[i];
                Vector3 dir = _spinePos[i + 1] - a;
                if (dir.sqrMagnitude < Eps) { i++; continue; }
                dir.Normalize();

                int j = i + 1;
                while (j < n - 1)
                {
                    Vector3 next = _spinePos[j + 1] - _spinePos[j];
                    if (next.sqrMagnitude < Eps) break;
                    next.Normalize();
                    if (Vector3.Dot(next, dir) < 0.9999f) break;      // heading changed -> elbow chord
                    j++;
                }

                AddCapsule(root, a, _spinePos[j], r, j == i + 1 ? "Col_Elbow" : "Col_Run");
                i = j;
            }
        }

        void AddCapsule(Transform parent, Vector3 a, Vector3 b, float radius, string name)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < Eps) return;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d / len);

            Collider col;
            if (len <= radius * 2.05f)                 // too short for a body -> sphere
            {
                var s = go.AddComponent<SphereCollider>();
                s.radius = radius;
                col = s;
            }
            else
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.direction = 1;                       // local Y, already aimed along the run
                c.radius = radius;
                c.height = len;                        // total span == run length, rounded ends
                col = c;
            }
            FinishCollider(go, col);
        }

        void AddMeshCollider(Transform parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false;
            FinishCollider(go, mc);
        }

        void FinishCollider(GameObject go, Collider col)
        {
            col.isTrigger = colliderIsTrigger;
            ApplyColliderLayer(go);
            ColliderCount++;
        }

        void ApplyColliderLayer(GameObject go)
        {
            if (string.IsNullOrEmpty(colliderLayer)) return;
            int layer = LayerMask.NameToLayer(colliderLayer);
            if (layer < 0)
            {
                Debug.LogWarning("[Pipeline] Physics layer '" + colliderLayer + "' does not exist - add it in " +
                                 "Project Settings > Tags and Layers, or clear the field.", this);
                colliderLayer = "";
                return;
            }
            go.layer = layer;
        }

        // ======================================================== markers
        void BuildMarkers()
        {
            int want = MarkerCountEffective;
            var positions = PickMarkerArcPositions(want);
            if (positions.Count == 0)
            {
                Debug.LogWarning("[Pipeline] No room for any marker: lengthen the pipe, shrink the " +
                                 "cards or reduce markerMinSeparation.", this);
                return;
            }
            if (positions.Count < want)
                Debug.LogWarning(string.Format("[Pipeline] Only {0}/{1} markers fit with {2:0.00} m separation " +
                    "in {3:0.0} m of pipe.", positions.Count, want, markerMinSeparation, PathLength), this);

            var ids = ArUcoCodeGenerator.PickUniqueIds(positions.Count, markerIdMin, markerIdMax, seed);
            var codes = useExternalMarkerTextures ? null
                        : ArUcoCodeGenerator.GenerateCodes(ids, markerBits, seed, 3);

            Mesh quad = SharedQuad();

            for (int i = 0; i < positions.Count; i++)
            {
                int id = ids[i];
                Sample s = SampleAtArcLength(positions[i]);

                Texture2D cardTex = ResolveCardTexture(id, codes);
                if (cardTex == null) continue;

                float yaw = randomMarkerYaw ? Rng() * 360f : 0f;

                var go = new GameObject("ArUco_" + id);
                go.transform.SetParent(_markerRoot, false);
                go.transform.localPosition = s.p + Vector3.up * markerLift;

                if (markerOrientation == MarkerOrientation.HorizontalBooklet)
                {
                    // Card normal (local +Z) = generator up  ->  HORIZONTAL card (§3.2.1).
                    // Yaw first so it only spins around that vertical; then a small tilt for realism.
                    Quaternion tilt = markerTiltLimitDegrees > 0f
                        ? Quaternion.Euler((Rng() * 2f - 1f) * markerTiltLimitDegrees, 0f,
                                           (Rng() * 2f - 1f) * markerTiltLimitDegrees)
                        : Quaternion.identity;
                    go.transform.localRotation =
                        Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-90f, 0f, 0f) * tilt;
                }
                else
                {
                    go.transform.localRotation = Quaternion.LookRotation(s.n, s.t) *
                                                 Quaternion.Euler(0f, 0f, yaw);
                }

                go.transform.localScale = Vector3.one * markerSize;   // square, never stretched
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                go.AddComponent<MeshRenderer>().sharedMaterial = MarkerMaterial(id, cardTex);

                var info = go.AddComponent<PipelineMarkerInfo>();
                info.markerId = id;
                info.indexFromPinger = i;
                info.arcLength = positions[i];
                info.bits = markerBits;
                info.code = codes != null && codes.ContainsKey(id) ? codes[id] : 0UL;
                info.yawDegrees = yaw;
                info.localPositionOnPath = s.p;
                info.cardTexture = cardTex;

                if (addMarkerColliders) AddMarkerCollider(go, markerSize);

                if (addClearPlasticFrame)
                {
                    float f = markerSize * (1f + 2f * Mathf.Clamp01(frameBorderFraction));
                    var frame = new GameObject("ClearPlasticFrame");
                    frame.transform.SetParent(go.transform, false);
                    frame.transform.localPosition = new Vector3(0f, 0f, -0.0004f);
                    frame.transform.localScale = Vector3.one * f;
                    frame.AddComponent<MeshFilter>().sharedMesh = quad;
                    frame.AddComponent<MeshRenderer>().sharedMaterial =
                        style.GetMaterial(PipelinePart.ClearFrame);
                }

                if (showIdLabelOnCard) AddLabel(go, id);
                _markers.Add(info);
            }
        }

        /// Thin box exactly over the card. The card object is scaled by markerSize, so the collider
        /// size is expressed in card fractions: 1 x 1 x ~6 mm world.
        void AddMarkerCollider(GameObject go, float size)
        {
            float thickness = Mathf.Max(0.004f, size * 0.04f) / Mathf.Max(0.01f, size);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, thickness);
            box.center = new Vector3(0f, 0f, 0f);
            box.isTrigger = markerColliderIsTrigger;
            ApplyColliderLayer(go);
            ColliderCount++;
        }

        Texture2D ResolveCardTexture(int id, Dictionary<int, ulong> codes)
        {
            Texture2D cached;
            if (_markerTextures.TryGetValue(id, out cached) && cached != null) return cached;

            if (useExternalMarkerTextures && externalMarkerTextures != null)
            {
                int idx = id - externalIdOffset;
                if (idx >= 0 && idx < externalMarkerTextures.Length && externalMarkerTextures[idx] != null)
                {
                    _markerTextures[id] = externalMarkerTextures[idx];
                    return externalMarkerTextures[idx];
                }
                Debug.LogWarning("[Pipeline] No external texture for id " + id + " (index " + idx +
                                 "). Falling back to procedural card.", this);
            }

            ulong code = codes != null && codes.ContainsKey(id) ? codes[id] : 0UL;
            var tex = ArUcoTextureGenerator.CreateCardTexture(code, markerBits, quietZoneModules,
                requestedCardTextureSize, (Color32)style.markerInk, (Color32)style.markerPaper,
                crispPixelFilter);
            tex.name = "ArUcoCard_" + id;
            _markerTextures[id] = tex;
            return tex;
        }

        Material MarkerMaterial(int id, Texture2D tex)
        {
            Material baseMat, inst;
            if (_markerMatById.TryGetValue(id, out inst) && inst != null) return inst;

            baseMat = style.GetMaterial(PipelinePart.MarkerCard);
            inst = new Material(baseMat) { name = "TAC_ArUco_" + id, hideFlags = HideFlags.DontSave };
            MatTex(inst, "_MainTex", tex); MatTex(inst, "_BaseMap", tex);
            MatTex(inst, "_EmissionMap", tex);
            MatColour(inst, "_EmissionColor", Color.white * 0.12f);
            MatColour(inst, "_Color", Color.white); MatColour(inst, "_BaseColor", Color.white);
            _markerMatById[id] = inst;
            _markerMaterials.Add(inst);
            return inst;
        }

        static void MatColour(Material m, string p, Color v) { if (m.HasProperty(p)) m.SetColor(p, v); }
        static void MatTex(Material m, string p, Texture t) { if (m.HasProperty(p)) m.SetTexture(p, t); }

        Mesh SharedQuad()
        {
            if (_sharedQuad != null) return _sharedQuad;
            _sharedQuad = new Mesh
            {
                name = "TAC_UnitQuad",
                hideFlags = HideFlags.DontSave,
                bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.001f))
            };
            _sharedQuad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            // Face +Z, CCW as seen from +Z, UVs un-mirrored (a mirrored ArUco code does not decode
            // as any rotation of itself). With Euler(-90,0,0) the card normal becomes generator up.
            _sharedQuad.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            _sharedQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _sharedQuad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            _sharedQuad.RecalculateBounds();
            return _sharedQuad;
        }

        void AddLabel(GameObject parent, int id)
        {
            var go = new GameObject("Label_" + id);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.55f);
            go.transform.localScale = Vector3.one * 0.45f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = id.ToString();
            tm.characterSize = 0.25f;
            tm.fontSize = 128;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.black;
            var f = LegacyFont();
            if (f != null) tm.font = f;
        }

        static Font _font;
        static Font LegacyFont()
        {
            if (_font != null) return _font;
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { _font = null; }
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; }
            }
            return _font;
        }

        List<float> PickMarkerArcPositions(int count)
        {
            var picked = new List<float>();
            float inset = Mathf.Max(0.12f, markerSize * 1.1f);
            float lo = inset, hi = PathLength - inset;
            float minSep = markerMinSeparation;
            if (hi <= lo + minSep * 0.5f) return picked;

            int guard = 0;
            while (picked.Count < count && guard++ < 30000)
            {
                float s = lo + (hi - lo) * Rng();
                Vector3 p = SampleAtArcLength(s).p;
                if (!InsideVolume(p)) continue;

                bool ok = true;
                for (int i = 0; i < picked.Count; i++)
                {
                    if (Mathf.Abs(picked[i] - s) < minSep) { ok = false; break; }
                    // The booklet's 0.2 m is a real 3D distance, so check the chord as well.
                    if (Vector3.Distance(p, SampleAtArcLength(picked[i]).p) < minSep) { ok = false; break; }
                }
                if (ok) picked.Add(s);
            }
            picked.Sort();
            return picked;
        }

        // ======================================================== pinger (§3.2.2)
        void BuildPinger()
        {
            if (_spinePos.Count < 2) return;
            Vector3 start = _spinePos[0];
            Vector3 heading = _spinePos[1] - start;
            if (heading.sqrMagnitude < Eps) heading = Vector3.forward;
            heading.Normalize();

            var root = new GameObject("Pinger_MFP1 (visual stand-in)");
            root.transform.SetParent(_root, false);
            root.transform.localPosition = start - heading * (PipeRadius * 0.9f);
            root.transform.localRotation = Quaternion.LookRotation(heading, Vector3.up);

            Material mat = style.GetMaterial(PipelinePart.Pinger);
            AddCylinder(root.transform, new Vector3(0.055f, 0.11f, 0.055f), Vector3.zero,
                        Quaternion.Euler(90f, 0f, 0f), mat);
            AddCylinder(root.transform, new Vector3(0.075f, 0.012f, 0.075f), new Vector3(0f, 0f, 0.10f),
                        Quaternion.Euler(90f, 0f, 0f), mat);
            AddCylinder(root.transform, new Vector3(0.012f, 0.20f, 0.012f), new Vector3(0f, 0.12f, -0.02f),
                        Quaternion.Euler(20f, 0f, 0f), mat);

            if (addPingerCollider)
            {
                var bc = root.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.06f, 0.03f);
                bc.size = new Vector3(0.17f, 0.34f, 0.26f);
                bc.isTrigger = colliderIsTrigger;
                ColliderCount++;
            }
        }

        static void AddCylinder(Transform parent, Vector3 scale, Vector3 localPos,
                                Quaternion localRot, Material mat)
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "PingerPart";
            var col = cyl.GetComponent<Collider>();
            if (col != null) DestroyUnityObject(col);

            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = localPos;
            cyl.transform.localRotation = localRot;
            cyl.transform.localScale = scale;

            var mr = cyl.GetComponent<MeshRenderer>();          // primitives already have one
            if (mr == null) mr = cyl.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        void BuildVolumeCollider()
        {
            var go = new GameObject("GenerationVolume");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = volumeCenter;
            var col = go.AddComponent<BoxCollider>();
            col.size = volumeSize;
            col.isTrigger = true;
        }

        // ======================================================== gizmos
        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            if (useVolume)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireCube(volumeCenter, volumeSize);
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.15f);
                Gizmos.DrawWireCube(volumeCenter + Vector3.up * volumeSafetyMargin,
                                    volumeSize - Vector3.one * 2f * volumeSafetyMargin);
            }

            if (_spinePos.Count > 1)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < _spinePos.Count - 1; i++)
                    Gizmos.DrawLine(_spinePos[i], _spinePos[i + 1]);

                Gizmos.color = Color.cyan;
                for (int j = 0; j < _jointRanges.Count; j++)
                {
                    int i = (_jointRanges[j].x + _jointRanges[j].y) / 2;
                    Gizmos.DrawWireSphere(_spinePos[Mathf.Clamp(i, 0, _spinePos.Count - 1)], PipeRadius * 1.4f);
                }

                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_spinePos[0], PipeRadius * 1.8f);   // pinger end = sequence start
            }

            Gizmos.color = Color.magenta;
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                if (m == null) continue;
                Vector3 p = m.transform.position;
                Gizmos.DrawWireCube(p, Vector3.one * markerSize);
                Gizmos.DrawLine(p, p + Vector3.up * (markerSize * 1.5f));  // horizontal normal
            }
        }

        float Rng() { return (float)_rng.NextDouble(); }
    }
}
