using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace TacSim
{
    [DefaultExecutionOrder(200)]
    public sealed class TeknologidagenDive : MonoBehaviour
    {
        public const string WallValvePart = "STEP valve - wall right";
        public RovVehicle vehicle;
        public Transform structure;
        public Camera diveCamera;
        public ParticleSystem silt;
        [Header("Set by TAC/Teknologidagen/Upgrade lake")]
        public Collider seabed;
        public ParticleSystem sediment;
        public Mesh wallValveBody, wallValveHandle;
        public float arenaHalfSize = 38;
        public bool armEnabled = true;
        public bool randomizeOnStart = true;
        public int seed = 42;
        // 0 = the handle pose in the CAD model, so the valve looks the same before and after Play.
        [Range(0,90)] public float wallValveStart;
        [Header("Water and handling")]
        [Range(0,3)] public int waterPreset;
        [Range(0,.3f)] public float drift = .05f;
        [Tooltip("Pilot gain on forward, sideways and vertical thrust")] [Range(.05f,1)] public float moveGain = .3f;
        [Tooltip("Pilot gain on yaw")] [Range(.05f,1)] public float yawGain = .3f;
        [Tooltip("Torque tool motor limit, N·m")] public float toolTorque = 6;
        [Tooltip("Torque needed to turn the valve handle, N·m")] public float valveFriction = 1.5f;
        public bool showSonar = true;
        static readonly float[] SonarRanges = { 5, 10, 15, 20 };
        int sonarRangeIndex = 2;

        public static readonly string[] WaterNames = { "Grumsete fjord", "Kystvann", "Klart vann", "Natt" };
        // Horizontal visibility: a dark target fades out at about 3.9 / c, c being the beam attenuation.
        static readonly float[] WaterVisibility = { 2.5f, 6, 12, 6 };
        // Diffuse attenuation Kd (1/m) for red, green, blue: turbid fjord with dissolved organics
        // (absorbs blue), green coastal water, clear oceanic water (Jerlov types C5–C9, C1, II).
        static readonly Vector3[] WaterKd = { new(.85f,.30f,.55f), new(.55f,.13f,.22f), new(.40f,.07f,.05f), new(.55f,.13f,.22f) };
        // Light scattered back by the water itself (path radiance), relative to the light reaching that depth.
        // Turbid fjord water carries silt and humic matter: olive and grey-brown rather than clean green.
        static readonly Color[] WaterTint = { new(.30f,.27f,.15f), new(.12f,.24f,.20f), new(.06f,.20f,.28f), new(.12f,.24f,.20f) };
        static readonly float[] SurfaceDaylight = { 1, 1, 1, .02f };
        static readonly float[] SnowRate = { 160, 70, 25, 70 };
        static readonly float[] SnowAlpha = { .55f, .4f, .25f, .4f };

        public TacValve ValveA { get; private set; }
        public TacValve ValveB { get; private set; }
        public TacArm Arm { get; private set; }
        public TacValve[] Valves { get; private set; }
        public float Daylight { get; private set; }
        System.Random rng;
        Vector3 startPosition;
        Quaternion startRotation;
        float structureFloorOffset;
        Vector2 structureFootprint;
        bool started;
        PilotView view;
        int exteriorCameraMask;
        EasyValveAssist assist;
        bool showPanel;
        GUIStyle hudText, hudLeft, smallText;
        Light sun;
        Light[] lamps;
        float driftSeed, previousFixedStep, exposure = 1;
        float[] lampIntensity;
        PilotInput pilot;

        void Start()
        {
            rng = new System.Random(randomizeOnStart ? Environment.TickCount : seed);
            driftSeed = (float)rng.NextDouble() * 100;
            view=diveCamera.GetComponent<PilotView>();
            exteriorCameraMask=diveCamera.cullingMask;
            view.ModelInspection=true; // Use the rendered camera, fitted to the hull in this scene.
            view.enabled=false; // This scene supplies its own compact HUD and camera positioning.
            view.SetMode(1); // Forward camera: the pilot sees the torque tool, as on the real ROV.
            pilot=vehicle.GetComponent<PilotInput>();
            // The tool fork clears the valve bar by a few millimetres; contacts that fine need a short physics step.
            previousFixedStep=Time.fixedDeltaTime;
            Time.fixedDeltaTime=.005f;
            // Pushed against the valve, the hull should settle, not be kicked out of small overlaps.
            vehicle.Body.maxDepenetrationVelocity=.2f;
            vehicle.Body.solverVelocityIterations=10;
            if(SensorSuite.Instance!=null)
            {
                SensorSuite.Instance.TelemetryVisible=false; SensorSuite.Instance.BeamsVisible=false;
                var sonar=SensorSuite.Instance.Sonar;
                // Flatter than the pool mount, so the fan reaches across the lake instead of into the floor.
                if(sonar!=null) { sonar.transform.localRotation=Quaternion.Euler(8,0,0); sonar.SetRange(SonarRanges[sonarRangeIndex]); }
            }
            sun=FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l=>l.type==LightType.Directional);
            lamps=vehicle.GetComponentsInChildren<Light>(true);
            lampIntensity=lamps.Select(l=>l.intensity).ToArray();
            structure.rotation = Quaternion.identity;
            var wall = InstallWallValve();
            if (wallValveHandle != null) MakeValvePhysical(wall);
            Valves = new[] { wall };
            ValveA = ValveB = wall;
            var bounds = BoundsOf(structure);
            structureFloorOffset = structure.position.y - bounds.min.y;
            structureFootprint = new Vector2(bounds.extents.x, bounds.extents.z);
            Arm = TacArm.Attach(vehicle, true);
            var hull = vehicle.GetComponent<BoxCollider>();
            // Just below the forward camera, so the fork sits in the lower half of the picture.
            Arm.transform.localPosition = new Vector3(0, -.02f, hull.center.z + hull.size.z * .5f + .06f);
            // The structure stands at 1.61× its CAD size, so the 25 mm T-bar is about 40 mm wide here;
            // the fork leaves 4 mm either side of whatever bar it meets.
            Arm.MakePhysical(vehicle, toolTorque, .025f * wall.transform.lossyScale.x + .008f);
            vehicle.waterSurface = 0;
            vehicle.currentPoolFloor = -16;
            vehicle.currentPoolSize = Vector2.one * arenaHalfSize * 2;
            vehicle.VehicleReset += RestoreStart;
            started = true;
            Randomize();
            assist=gameObject.AddComponent<EasyValveAssist>(); assist.dive=this;
        }

        // The imported valve is one fused mesh. The upgrade step split it into body and handle; here the
        // body replaces the fused mesh and the handle turns on the valve axis, so nothing changes visually
        // until the handle is turned.
        TacValve InstallWallValve()
        {
            var part = structure.GetComponentsInChildren<Transform>(true).First(t => t.name == WallValvePart);
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            if (wallValveBody == null || wallValveHandle == null)
            {
                Debug.LogError("The wall valve handle has not been split out. Run TAC/Teknologidagen/Upgrade lake.");
                var logic = TacValve.Create(null, "Ventil", part.position, Quaternion.identity, 0, false);
                logic.transform.SetParent(structure, true);
                return logic;
            }
            Bounds body = wallValveBody.bounds;
            int axis = body.size.x <= body.size.y && body.size.x <= body.size.z ? 0 : body.size.y <= body.size.z ? 1 : 2;
            Vector3 axisLocal = Vector3.zero;
            axisLocal[axis] = 1;
            // The T-bar sits 16 mm below the open face but 30 mm from the back: the nearer end is the open one.
            Bounds grip = wallValveHandle.bounds;
            if (body.max[axis] - grip.max[axis] > grip.min[axis] - body.min[axis]) axisLocal = -axisLocal;
            Vector3 e1 = Vector3.zero, e2 = Vector3.zero;
            e1[(axis + 1) % 3] = 1;
            e2[(axis + 2) % 3] = 1;
            // The handle bar is the principal direction of the handle's vertices in the face plane.
            var points = wallValveHandle.vertices;
            Vector3 mean = Vector3.zero;
            foreach (var p in points) mean += p;
            mean /= points.Length;
            float sxx = 0, sxy = 0, syy = 0, top = float.MinValue;
            foreach (var p in points)
            {
                Vector3 d = p - mean;
                float x = Vector3.Dot(d, e1), y = Vector3.Dot(d, e2);
                sxx += x * x; sxy += x * y; syy += y * y;
                top = Mathf.Max(top, Vector3.Dot(p - body.center, axisLocal));
            }
            float theta = .5f * Mathf.Atan2(2 * sxy, sxx - syy);
            Vector3 barLocal = e1 * Mathf.Cos(theta) + e2 * Mathf.Sin(theta);
            Vector3 axisWorld = part.TransformDirection(axisLocal).normalized;
            Vector3 barWorld = Vector3.ProjectOnPlane(part.TransformDirection(barLocal), axisWorld).normalized;
            float scale = part.lossyScale.x * body.size[(axis + 1) % 3] / (2 * TacValve.OuterRadius);
            // The fork seats on the handle: put the handle top at the booklet rim height above the valve origin.
            Vector3 origin = part.TransformPoint(body.center + axisLocal * top) - axisWorld * (TacValve.RimHeight * scale);
            Vector3 reference = Vector3.ProjectOnPlane(Vector3.up, axisWorld);
            if (reference.sqrMagnitude < 1e-4f) reference = Vector3.ProjectOnPlane(structure.forward, axisWorld);
            Quaternion rotation = Quaternion.LookRotation(reference.normalized, axisWorld);
            Vector3 bar = Quaternion.Inverse(rotation) * barWorld;
            float restAngle = Mathf.Repeat(Mathf.Atan2(bar.x, bar.z) * Mathf.Rad2Deg + 45, 180) - 45;
            // Turn the valve frame so the modelled handle lies exactly at the start angle.
            rotation *= Quaternion.Euler(0, restAngle - wallValveStart, 0);
            var valve = TacValve.Create(null, "Ventil (vegg)", origin, rotation, wallValveStart, false);
            valve.transform.localScale = Vector3.one * scale;
            var pivot = new GameObject("Handle").transform;
            pivot.SetParent(valve.transform, false);
            pivot.localRotation = Quaternion.Euler(0, valve.angle, 0);
            filter.sharedMesh = wallValveBody;
            // The body collides without the bar (the upgrade step bakes this; cooking here is the fallback).
            var bodyCollider = part.GetComponent<MeshCollider>();
            if (bodyCollider != null && bodyCollider.sharedMesh != wallValveBody) bodyCollider.sharedMesh = wallValveBody;
            var handle = new GameObject("Wall valve handle");
            handle.layer = part.gameObject.layer;
            handle.transform.SetPositionAndRotation(part.position, part.rotation);
            handle.transform.localScale = part.lossyScale;
            handle.AddComponent<MeshFilter>().sharedMesh = wallValveHandle;
            var handleRenderer = handle.AddComponent<MeshRenderer>();
            handleRenderer.sharedMaterials = renderer.sharedMaterials;
            handleRenderer.shadowCastingMode = renderer.shadowCastingMode;
            handle.transform.SetParent(pivot, true);
            valve.AttachHandle(pivot);
            valve.transform.SetParent(structure, true);
            return valve;
        }

        // The T-bar becomes a hinged body that only the tool (or the hull) can push round. Its colliders are
        // the bar and the stem plate below it, in the valve frame (CAD metres; the bar top is at the rim height).
        void MakeValvePhysical(TacValve valve)
        {
            var pivot = valve.transform.Find("Handle");
            AddBox(pivot, new Vector3(0, .06975f, 0), new Vector3(.025f, .0315f, .192f));
            AddBox(pivot, new Vector3(0, .03445f, 0), new Vector3(.025f, .0393f, .12f));
            var mount = valve.gameObject.AddComponent<Rigidbody>();
            mount.isKinematic = true;
            valve.MakePhysical(mount, valveFriction);
            // The bar is fused into the rim ring and hub in the CAD model; let it turn freely inside them.
            foreach (var mine in pivot.GetComponents<Collider>())
            foreach (var other in structure.GetComponentsInChildren<Collider>(true))
                if (other.transform != pivot) Physics.IgnoreCollision(mine, other);
        }
        static void AddBox(Transform parent, Vector3 centre, Vector3 size)
        {
            var box = parent.gameObject.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.contactOffset = .002f;
        }

        public void Randomize()
        {
            if (!started) return;
            // The structure stands on the shallow plateau, levelled on the mean floor under its footprint.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var centre = new Vector3(Range(-24, 24), 0, Range(-28, -2));
                var yaw = Quaternion.Euler(0, Range(0, 360), 0);
                float floor = FloorAt(centre);
                foreach (var corner in new[] { new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(1,0,-1), new Vector3(-1,0,-1) })
                    floor += FloorAt(centre + yaw * Vector3.Scale(corner, new Vector3(structureFootprint.x, 0, structureFootprint.y)));
                structure.SetPositionAndRotation(new Vector3(centre.x, floor / 5 + structureFloorOffset + .02f, centre.z), yaw);
                Physics.SyncTransforms();
                if (!TouchesRock(BoundsOf(structure))) break;
            }
            // The ROV starts out of sight of the structure, facing anywhere: find it with the sonar.
            startPosition = structure.position + new Vector3(0, 3, -15);
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float a = Range(0, 360) * Mathf.Deg2Rad, r = Range(14, 26);
                var p = structure.position + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                if (Mathf.Abs(p.x) > arenaHalfSize - 4 || Mathf.Abs(p.z) > arenaHalfSize - 4) continue;
                p.y = Range(-3, FloorAt(p) + 2.5f);
                if (Physics.CheckSphere(p, .8f, ~SensorSuite.VehicleMask, QueryTriggerInteraction.Ignore)) continue;
                startPosition = p;
                break;
            }
            startRotation = Quaternion.Euler(0, Range(0, 360), 0);
            vehicle.ResetVehicle();
            foreach(var valve in Valves) valve.SetAngle(valve.StartAngle);
            Arm.SetTargets(0,0);
            Physics.SyncTransforms();
        }
        float Range(float min, float max) => min + (float)rng.NextDouble() * (max-min);
        public float FloorAt(Vector3 p)
        {
            if (seabed != null && seabed.Raycast(new Ray(new Vector3(p.x, 20, p.z), Vector3.down), out var hit, 80)) return hit.point.y;
            return -10;
        }
        static bool TouchesRock(Bounds b)
        {
            foreach (var c in Physics.OverlapBox(b.center, b.extents + Vector3.one * .1f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (c.name.StartsWith("Rock")) return true;
            return false;
        }
        void RestoreStart()
        {
            vehicle.transform.SetPositionAndRotation(startPosition,startRotation);
            vehicle.Body.position=startPosition; vehicle.Body.rotation=startRotation;
            vehicle.Body.linearVelocity=Vector3.zero; vehicle.Body.angularVelocity=Vector3.zero;
            if(Arm!=null) Arm.SyncSocket();
        }
        void OnDestroy()
        {
            if (vehicle != null) vehicle.VehicleReset -= RestoreStart;
            if (previousFixedStep > 0) Time.fixedDeltaTime = previousFixedStep;
        }
        void Update()
        {
            if (!started) return;
            Arm.gameObject.SetActive(armEnabled);
            var k = Keyboard.current;
            if(k != null && Application.isFocused)
            {
                if(k.nKey.wasPressedThisFrame) Randomize();
                if(k.gKey.wasPressedThisFrame) armEnabled = !armEnabled;
                if(k.hKey.wasPressedThisFrame) showPanel=!showPanel;
                if(k.kKey.wasPressedThisFrame) assist.Active=!assist.Active;
                if(k.oKey.wasPressedThisFrame) showSonar=!showSonar;
                if(k.commaKey.wasPressedThisFrame) SetSonarRange(sonarRangeIndex-1);
                if(k.periodKey.wasPressedThisFrame) SetSonarRange(sonarRangeIndex+1);
                if(k.digit1Key.wasPressedThisFrame) waterPreset=0;
                if(k.digit2Key.wasPressedThisFrame) waterPreset=1;
                if(k.digit3Key.wasPressedThisFrame) waterPreset=2;
                if(k.digit4Key.wasPressedThisFrame) waterPreset=3;
                if(armEnabled && vehicle.Armed && assist.Connected==null)
                {
                    Arm.NudgeWrist((k.xKey.isPressed?1:0)-(k.zKey.isPressed?1:0),Time.deltaTime);
                    if(k.cKey.wasPressedThisFrame) Arm.ToggleTilt();
                }
            }
            if (pilot != null) { pilot.moveScale = moveGain; pilot.yawScale = yawGain; }
            ApplyExposure();
            ApplyDrift();
            ApplyWater();
            UpdateSilt();
        }
        void SetSonarRange(int index)
        {
            sonarRangeIndex = Mathf.Clamp(index, 0, SonarRanges.Length - 1);
            if (SensorSuite.Instance != null && SensorSuite.Instance.Sonar != null) SensorSuite.Instance.Sonar.SetRange(SonarRanges[sonarRangeIndex]);
        }
        // The camera's auto exposure, applied to the ROV lamps: up close to a surface they would blow the
        // picture out, so dim them with the square of the distance to what the camera looks at.
        void ApplyExposure()
        {
            var eye = diveCamera.transform;
            float target = Physics.Raycast(eye.position, eye.forward, out var hit, 1.5f, ~SensorSuite.VehicleMask, QueryTriggerInteraction.Ignore)
                ? Mathf.Clamp(hit.distance * hit.distance / (1.5f * 1.5f), .12f, 1) : 1;
            exposure = Mathf.MoveTowards(exposure, target, Time.deltaTime * 1.5f);
            for (int i = 0; i < lamps.Length; i++)
                if (lamps[i] != null) lamps[i].intensity = lampIntensity[i] * exposure;
        }
        void ApplyDrift()
        {
            vehicle.currentSpeed = drift;
            // Slow, uneven gusts on top of the lake-wide circulation, so an idle ROV wanders like a real one.
            float t = Time.time * .05f;
            vehicle.current = drift * .8f * new Vector3(Mathf.PerlinNoise(t, driftSeed) * 2 - 1,
                (Mathf.PerlinNoise(driftSeed, t) * 2 - 1) * .2f, Mathf.PerlinNoise(t + 31, driftSeed + 7) * 2 - 1);
        }
        // Daylight falls off per colour channel with depth (Beer–Lambert), the water scatters what is left
        // back as haze, and the camera partly compensates with exposure and white balance.
        void ApplyWater()
        {
            int p = Mathf.Clamp(waterPreset, 0, WaterNames.Length - 1);
            float depth = Mathf.Max(0, vehicle.waterSurface - diveCamera.transform.position.y);
            bool under = depth > 0;
            Vector3 kd = WaterKd[p];
            float surface = SurfaceDaylight[p];
            var down = new Color(Mathf.Exp(-kd.x * depth) * surface, Mathf.Exp(-kd.y * depth) * surface, Mathf.Exp(-kd.z * depth) * surface);
            float lum = .2126f * down.r + .7152f * down.g + .0722f * down.b;
            Daylight = lum;
            float gain = Mathf.Clamp(Mathf.Pow(Mathf.Max(lum, 1e-4f), -.65f), 1, 14);
            Color seen = under ? Color.Lerp(down, new Color(lum, lum, lum), .5f) * gain : Color.white;
            float peak = Mathf.Max(seen.r, Mathf.Max(seen.g, Mathf.Max(seen.b, 1e-4f)));
            if (sun != null) { sun.color = seen / peak; sun.intensity = 1.2f * peak; }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = under ? new Color(.30f * seen.r, .36f * seen.g, .34f * seen.b) : new Color(.45f, .5f, .55f);
            RenderSettings.fog = under;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 3.9f / WaterVisibility[p];
            Color tint = WaterTint[p];
            var water = new Color(tint.r * seen.r + .002f, tint.g * seen.g + .003f, tint.b * seen.b + .004f);
            RenderSettings.fogColor = water;
            diveCamera.backgroundColor = under ? water : new Color(.42f,.64f,.72f);
            if (sediment == null) return;
            // Marine snow follows the camera; the ROV lamps light it up as backscatter.
            sediment.transform.position = diveCamera.transform.position;
            var emission = sediment.emission;
            emission.enabled = under;
            emission.rateOverTime = SnowRate[p];
            bool lit = lamps.Any(l => l != null && l.enabled);
            float brightness = Mathf.Max(Mathf.Min(lum * gain, 1) * .9f, lit ? .45f : .03f);
            var main = sediment.main;
            main.startColor = new Color(.62f * brightness, .64f * brightness, .52f * brightness, SnowAlpha[p]);
            if (!under) sediment.Clear();
        }
        void UpdateSilt()
        {
            if (silt == null) return;
            Vector3 p = vehicle.transform.position;
            float floor = FloorAt(p);
            silt.transform.position = new Vector3(p.x, floor + .15f, p.z);
            var em = silt.emission;
            em.rateOverTime = p.y - floor < 1.5f ? 18 + vehicle.Throttle*70 : 0;
        }
        void OnGUI()
        {
            if(!started) return;
            Matrix4x4 old = GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale, height=Screen.height/scale;
            hudText ??= new GUIStyle(GUI.skin.label) {fontSize=18,alignment=TextAnchor.MiddleCenter};
            smallText ??= new GUIStyle(GUI.skin.label) {fontSize=14};
            hudLeft ??= new GUIStyle(GUI.skin.label) {fontSize=18};
            float heading = Mathf.Repeat(vehicle.transform.eulerAngles.y, 360);
            // What the real ROV reports: Bar30 depth, and the DVL A50's altitude and velocity over the bottom
            // (A50 frame: forward, starboard, down).
            var sensors = SensorSuite.Instance;
            var pressure = sensors != null ? sensors.Pressure : null;
            var dvl = sensors != null ? sensors.Dvl : null;
            GUI.Box(new Rect(18,18,370,96),"");
            string depth = pressure != null && pressure.HasSample ? $"DYBDE {pressure.Depth:F2} m" : "DYBDE —";
            GUI.Label(new Rect(28,22,350,28),$"{depth}   KURS {heading:000}°",hudLeft);
            GUI.Label(new Rect(28,52,350,24),dvl != null && dvl.HasSample && dvl.Altitude >= 0 ? $"HØYDE OVER BUNN {dvl.Altitude:F2} m" : "HØYDE OVER BUNN —  (ingen bunn)",smallText);
            GUI.Label(new Rect(28,78,350,24),dvl == null || !dvl.HasSample ? "DVL venter …"
                : dvl.VelocityValid ? $"DVL  fram {dvl.Velocity.x:+0.00;-0.00}   side {dvl.Velocity.y:+0.00;-0.00}   ned {dvl.Velocity.z:+0.00;-0.00} m/s"
                : "DVL  ingen bunnlås",smallText);
            GUI.Box(new Rect(width-340,18,322,42),"");
            GUI.Label(new Rect(width-336,18,314,42),string.Join("   ",Valves.Select((v,i)=>$"V{i+1} {v.angle/90:P0}"))+"   "+WaterNames[waterPreset],hudText);
            string hint=!vehicle.Armed ? "Motor stoppet · Esc for å aktivere"
                : !armEnabled ? "G · Aktiver verktøyet"
                : assist.Active ? (assist.Connected!=null ? $"{assist.Connected.valveName} tilkoblet · X åpne / Z lukke · F slipp"
                    : assist.Candidate!=null ? $"F · Koble til {assist.Candidate.valveName} (assist)" : "Assist på · kjør nær ventilen · K slår av")
                : Arm.Engaged!=null ? $"Verktøyet berører {Arm.Engaged.valveName} · X/Z vri · hold ROV-en inntil"
                : "Før gaffelen over T-baren · X/Z vri gaffelen · Shift presisjon";
            GUI.Box(new Rect(width/2-260,height-75,520,42),"");
            GUI.Label(new Rect(width/2-255,height-75,510,42),hint,hudText);
            GUI.Label(new Rect(width/2-180,height-30,360,25),"H · "+(showPanel?"Skjul panel":"Kontroller, vann og drift"),hudText);
            GUI.Label(new Rect(width/2-10,height/2-15,20,30),"+",hudText);
            var sonar = SensorSuite.Instance != null ? SensorSuite.Instance.Sonar : null;
            if(showSonar && sonar!=null && sonar.Fan!=null)
            {
                float w=340, h=w*sonar.Fan.height/sonar.Fan.width;
                var area=new Rect(18,height-h-46,w,h);
                GUI.Box(new Rect(area.x-6,area.y-6,w+12,h+36),"");
                GUI.DrawTexture(area,sonar.Fan,ScaleMode.StretchToFill,true);
                GUI.Label(new Rect(area.x,area.yMax+4,w,22),$"SONAR {sonar.range:0} m · ringer {sonar.RingSpacing:0} m · , . rekkevidde · O skjul",smallText);
            }
            if(showPanel)
            {
                GUI.Box(new Rect(18,122,440,390),"");
                GUI.Label(new Rect(30,130,420,130),"WASD kjør · Q/E sving · Space/Ctrl opp/ned · Shift presisjon\nX/Z vri verktøyet · G verktøy av/på\nK assist (låser på ventilen, F kobler til)\nV kamera · L lys · O sonar · , . sonar-rekkevidde\n1–4 vann · N ny plassering · R tilbake · Esc motor",smallText);
                GUI.Label(new Rect(30,262,120,24),"Vann:",smallText);
                for(int i=0;i<WaterNames.Length;i++)
                {
                    Color before=GUI.backgroundColor;
                    if(i==waterPreset) GUI.backgroundColor=new Color(.3f,.8f,.75f);
                    if(GUI.Button(new Rect(30+i*104,284,100,26),$"{i+1} {WaterNames[i]}")) waterPreset=i;
                    GUI.backgroundColor=before;
                }
                GUI.Label(new Rect(30,320,420,24),$"Strøm / drift: {drift:0.00} m/s",smallText);
                drift=GUI.HorizontalSlider(new Rect(30,346,410,20),drift,0,.3f);
                GUI.Label(new Rect(30,368,420,24),$"Gain fram / side / opp-ned: {moveGain:P0}",smallText);
                moveGain=GUI.HorizontalSlider(new Rect(30,394,410,20),moveGain,.05f,1);
                GUI.Label(new Rect(30,416,420,24),$"Gain sving (yaw): {yawGain:P0}",smallText);
                yawGain=GUI.HorizontalSlider(new Rect(30,442,410,20),yawGain,.05f,1);
                GUI.Label(new Rect(30,466,420,24),$"Assist (K): {(assist.Active?"PÅ":"AV")}   ·   sikt ≈ {WaterVisibility[waterPreset]:0.#} m   ·   dagslys {Daylight:P0}",smallText);
            }
            GUI.matrix=old;
        }
        void LateUpdate()
        {
            if(!started) return;
            var hull=vehicle.GetComponent<BoxCollider>();
            // Onboard views must not render the ROV's own camera housing/enclosure.
            // The manipulator stays on its separate layer and remains visible.
            diveCamera.cullingMask = view.Mode == 0 ? exteriorCameraMask
                : exteriorCameraMask & ~(1 << SensorSuite.VehicleLayer);
            Vector3 offset=view.Mode==0 ? new Vector3(1.5f,1.1f,-3f) : view.Mode==1 ? new Vector3(0,.12f,hull.center.z+hull.size.z*.5f+.02f) : new Vector3(0,hull.center.y-hull.size.y*.5f-.04f,.1f);
            var t=vehicle.transform;
            diveCamera.transform.position=t.TransformPoint(offset);
            diveCamera.transform.rotation=view.Mode==0 ? Quaternion.LookRotation(t.TransformPoint(new Vector3(0,0,1))-diveCamera.transform.position) : t.rotation*(view.Mode==2?Quaternion.Euler(90,0,0):Quaternion.identity);
        }
        public static Bounds BoundsOf(Transform root)
        {
            var renderers=root.GetComponentsInChildren<MeshRenderer>();
            var b=renderers[0].bounds;
            foreach(var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
