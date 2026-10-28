using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    [DefaultExecutionOrder(200)]
    public sealed class TeknologidagenDive : MonoBehaviour
    {
        public RovVehicle vehicle;
        public Transform structure;
        public Camera diveCamera;
        public ParticleSystem silt;
        public bool armEnabled = true;
        public bool randomizeOnStart = true;
        public int seed = 42;
        [Range(0,90)] public float initialValveA = 15, initialValveB = 25;
        public TacValve ValveA { get; private set; }
        public TacValve ValveB { get; private set; }
        public TacArm Arm { get; private set; }
        public TacValve[] Valves { get; private set; }
        System.Random rng;
        Vector3 startPosition;
        Quaternion startRotation;
        float structureFloorOffset;
        bool started;
        PilotView view;
        int exteriorCameraMask;
        EasyValveAssist assist;
        bool showHelp;
        GUIStyle hudText;

        void Start()
        {
            rng = new System.Random(randomizeOnStart ? Environment.TickCount : seed);
            view=diveCamera.GetComponent<PilotView>();
            exteriorCameraMask=diveCamera.cullingMask;
            view.ModelInspection=true; // Use the rendered camera, fitted to the hull in this scene.
            view.enabled=false; // This scene supplies its own compact HUD and camera positioning.
            if(SensorSuite.Instance!=null) { SensorSuite.Instance.TelemetryVisible=false; SensorSuite.Instance.BeamsVisible=false; }
            structure.rotation = Quaternion.identity;
            structureFloorOffset = structure.position.y - BoundsOf(structure).min.y;
            var parts = structure.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("STEP valve -") && t.GetComponent<Renderer>() != null).ToArray();
            Valves = parts.Select((part,i) => InstallValve(part, "Ventil " + (i+1), part.name.Contains("deck"), part.name.Contains("deck") ? initialValveB : initialValveA)).ToArray();
            ValveA = Valves.First(); ValveB = Valves.Last();
            Arm = TacArm.Attach(vehicle);
            var hull = vehicle.GetComponent<BoxCollider>();
            Arm.transform.localPosition = new Vector3(0, -.12f, hull.center.z + hull.size.z * .5f + .06f);
            vehicle.currentPoolFloor = -10;
            vehicle.currentPoolSize = new Vector2(30,30);
            vehicle.currentSpeed = .07f;
            vehicle.waterSurface = 0;
            vehicle.VehicleReset += RestoreStart;
            started = true;
            Randomize();
            assist=gameObject.AddComponent<EasyValveAssist>(); assist.dive=this;
        }

        TacValve InstallValve(Transform part, string label, bool deck, float angle)
        {
            Bounds b = part.GetComponent<Renderer>().bounds;
            Vector3 centre = structure.InverseTransformPoint(b.center);
            Vector3 facing = deck ? Vector3.up : new Vector3(0,0,-Mathf.Sign(centre.z));
            float depth = deck ? b.size.y : b.size.z;
            Vector3 rim = b.center + facing * depth * .5f;
            float scale = b.size.x / (2 * TacValve.OuterRadius);
            var v = TacValve.Create(null, label, rim - facing * TacValve.RimHeight * scale,
                deck ? Quaternion.identity : Quaternion.LookRotation(Vector3.up, facing), angle);
            v.transform.localScale = Vector3.one * scale;
            v.transform.SetParent(structure, true);
            // The imported STEP part includes a static handle; substitute a visible working valve.
            part.gameObject.SetActive(false);
            return v;
        }

        public void Randomize()
        {
            if (!started) return;
            structure.SetPositionAndRotation(new Vector3(Range(-4,4), -10 + structureFloorOffset + .02f, Range(-4,4)), Quaternion.Euler(0, Range(0,360),0));
            // Rocks occupy the outer shore; the centre remains a reserved working area.
            float a = Range(0,360) * Mathf.Deg2Rad;
            startPosition = new Vector3(Mathf.Sin(a)*10, Range(-7,-3), Mathf.Cos(a)*10);
            Vector3 facing = structure.position - startPosition; facing.y = 0;
            startRotation = Quaternion.LookRotation(facing);
            vehicle.ResetVehicle();
            foreach(var valve in Valves) valve.SetAngle(valve.StartAngle);
            Arm.SetTargets(0,0);
            Physics.SyncTransforms();
        }
        float Range(float min, float max) => min + (float)rng.NextDouble() * (max-min);
        void RestoreStart()
        {
            vehicle.transform.SetPositionAndRotation(startPosition,startRotation);
            vehicle.Body.position=startPosition; vehicle.Body.rotation=startRotation;
            vehicle.Body.linearVelocity=Vector3.zero; vehicle.Body.angularVelocity=Vector3.zero;
        }
        void OnDestroy() { if (vehicle != null) vehicle.VehicleReset -= RestoreStart; }
        void Update()
        {
            if (!started) return;
            Arm.gameObject.SetActive(armEnabled);
            var k = Keyboard.current;
            if(k != null && Application.isFocused)
            {
                if(k.nKey.wasPressedThisFrame) Randomize();
                if(k.gKey.wasPressedThisFrame) armEnabled = !armEnabled;
                if(k.hKey.wasPressedThisFrame) showHelp=!showHelp;
                if(armEnabled && vehicle.Armed && (assist==null || assist.Connected==null))
                {
                    Arm.NudgeWrist((k.xKey.isPressed?1:0)-(k.zKey.isPressed?1:0),Time.deltaTime);
                    if(k.cKey.wasPressedThisFrame) Arm.ToggleTilt();
                }
            }
            if(silt != null)
            {
                silt.transform.position = new Vector3(vehicle.transform.position.x,-9.8f,vehicle.transform.position.z);
                var em = silt.emission;
                em.rateOverTime = vehicle.transform.position.y < -8.5f ? 18 + vehicle.Throttle*70 : 0;
            }
            bool underwater = diveCamera.transform.position.y < 0;
            RenderSettings.fog = underwater;
            diveCamera.backgroundColor = underwater ? new Color(.035f,.14f,.15f) : new Color(.42f,.64f,.72f);
        }
        void OnGUI()
        {
            if(!started) return;
            Matrix4x4 old = GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale, height=Screen.height/scale;
            hudText ??= new GUIStyle(GUI.skin.label) {fontSize=18,alignment=TextAnchor.MiddleCenter};
            GUI.Box(new Rect(18,18,210,42),"");
            GUI.Label(new Rect(24,18,198,42),$"DYBDE {vehicle.Depth:F1} m",hudText);
            GUI.Box(new Rect(width-320,18,302,42),"");
            GUI.Label(new Rect(width-316,18,294,42),string.Join("   ",Valves.Select((v,i)=>$"V{i+1} {v.angle/90:P0}")),hudText);
            string hint=!vehicle.Armed ? "Motor stoppet · Esc for å aktivere" : !armEnabled ? "G · Aktiver armen" : assist!=null && assist.Connected!=null ? $"{assist.Connected.valveName} tilkoblet · X åpne / Z lukke · F slipp" : assist!=null && assist.Candidate!=null ? $"F · Koble til {assist.Candidate.valveName}" : "Kjør nær ventilen · F kobler til automatisk";
            GUI.Box(new Rect(width/2-310,height-75,620,42),"");
            GUI.Label(new Rect(width/2-305,height-75,610,42),hint,hudText);
            GUI.Label(new Rect(width/2-180,height-30,360,25),"H · "+(showHelp?"Skjul hjelp":"Vis kontroller"),hudText);
            GUI.Label(new Rect(width/2-10,height/2-15,20,30),"+",hudText);
            if(showHelp)
            {
                GUI.Box(new Rect(18,74,410,140),"");
                GUI.Label(new Rect(30,84,390,120),"WASD kjør · Q/E sving\nSpace/Ctrl opp/ned · Shift presisjon\nF koble til/slipp · X åpne · Z lukke\nV kamera · L lys · G arm · C vipp\nN ny plassering · R tilbake · Esc motor",new GUIStyle(GUI.skin.label){fontSize=16});
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
