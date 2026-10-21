using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    public sealed class OceanExplorer : MonoBehaviour
    {
        [Tooltip("Approximate 95% contrast-loss distance; select 1, 2 or 5 metres.")]
        public float visibility = 2;
        public Light daylight;
        public float surfaceDaylightIntensity = 1.1f;
        public Color surfaceAmbient = new Color(.18f,.29f,.25f);
        [Min(0)] public float lightAttenuationPerMetre = .12f;
        public float Depth { get; private set; }
        public float DaylightFraction { get; private set; }
        public float speed = 12;
        public bool pilotControlled;
        public float mapHalfExtent = 1000;
        public ParticleSystem sediment;
        public Light diveLight;
        float yaw, pitch;
        Camera view;
        RovVehicle vehicle;
        void Start()
        {
            view = GetComponent<Camera>();
            vehicle = FindFirstObjectByType<RovVehicle>();
            SetVisibility(visibility);
            yaw = transform.eulerAngles.y; pitch = transform.eulerAngles.x;
        }
        void Update()
        {
            var k = Keyboard.current; var m = Mouse.current;
            if(k!=null)
            {
                if(k.digit1Key.wasPressedThisFrame) SetVisibility(1);
                if(k.digit2Key.wasPressedThisFrame) SetVisibility(2);
                if(k.digit3Key.wasPressedThisFrame) SetVisibility(5);
            }
            if (!pilotControlled && k != null && m != null)
            {
                if (m.rightButton.isPressed)
                {
                    Vector2 delta = m.delta.ReadValue();
                    yaw += delta.x * .12f; pitch = Mathf.Clamp(pitch - delta.y * .12f, -85, 85);
                    transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                }
                Vector3 move = new Vector3((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
                    (k.eKey.isPressed ? 1 : 0) - (k.qKey.isPressed ? 1 : 0),
                    (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                Vector3 next = transform.position + transform.TransformDirection(Vector3.ClampMagnitude(move, 1)) * speed * (k.leftShiftKey.isPressed ? 4 : 1) * Time.deltaTime;
                next.x = Mathf.Clamp(next.x, -mapHalfExtent + 1, mapHalfExtent - 1); next.z = Mathf.Clamp(next.z, -mapHalfExtent + 1, mapHalfExtent - 1);
                next.y = Mathf.Clamp(next.y, -180, 80);
                if (Physics.Raycast(new Vector3(next.x, 100, next.z), Vector3.down, out var hit, 400)) next.y = Mathf.Max(next.y, hit.point.y + 1.5f);
                transform.position = next;
                if (k.lKey.wasPressedThisFrame) diveLight.enabled = !diveLight.enabled;
            }
            float y=pilotControlled && vehicle!=null ? vehicle.transform.position.y : transform.position.y;
            ApplyWaterAtDepth(-y);
        }
        public void SetVisibility(float metres)
        {
            visibility=metres<1.5f ? 1 : metres<3.5f ? 2 : 5;
            if(sediment!=null)
            {
                var shape=sediment.shape; shape.scale=new Vector3(visibility*2,visibility*2,visibility*2);
            }
        }
        public void ApplyWaterAtDepth(float depth)
        {
            Depth=Mathf.Max(0,depth);
            DaylightFraction=Mathf.Exp(-Mathf.Max(0,lightAttenuationPerMetre)*Depth);
            bool under=depth>0;
            RenderSettings.fog = under;
            RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 1.73f / Mathf.Max(1,visibility);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=surfaceAmbient*Mathf.Max(.01f,DaylightFraction);
            if(daylight!=null) daylight.intensity=surfaceDaylightIntensity*DaylightFraction;
            Color water=new Color(.025f,.12f,.115f)*Mathf.Max(.015f,DaylightFraction);
            water.a=1;
            RenderSettings.fogColor=water;
            if(view==null) view=GetComponent<Camera>();
            view.backgroundColor=under ? water : new Color(.38f,.53f,.59f);
            if(sediment!=null)
            {
                var emission = sediment.emission; emission.enabled = under; emission.rateOverTime=25;
                var main=sediment.main;main.maxParticles=300;main.startLifetime=8;
                // Dim all existing particles too; retain a little visibility for lamp backscatter.
                var tint=sediment.colorOverLifetime;tint.enabled=true;
                float brightness=Mathf.Max(.04f,DaylightFraction);
                if(vehicle!=null)
                    foreach(var lamp in vehicle.GetComponentsInChildren<Light>())
                        if(lamp.enabled) {brightness=Mathf.Max(.25f,brightness);break;}
                tint.color=new Color(brightness,brightness,brightness,1);
                if (!under) sediment.Clear();
            }
        }
        public void DrawWaterPanel(float width)
        {
            float x=width-290;
            GUI.Box(new Rect(x,22,268,110),"WATER / VISIBILITY");
            float[] values={1,2,5};
            for(int i=0;i<3;i++)
            {
                Color old=GUI.backgroundColor;
                if(Mathf.Approximately(visibility,values[i])) GUI.backgroundColor=new Color(.3f,.8f,.75f);
                if(GUI.Button(new Rect(x+10+i*83,50,78,30),$"{values[i]:0} m [{i+1}]")) SetVisibility(values[i]);
                GUI.backgroundColor=old;
            }
            GUI.Label(new Rect(x+12,86,250,22),$"Depth {Depth:F1} m / daylight {DaylightFraction:P0}");
            GUI.Label(new Rect(x+12,106,250,22),"L: ROV lights");
        }
        void OnGUI()
        {
            if (pilotControlled) return;
            DrawWaterPanel(Screen.width);
            GUI.Box(new Rect(16, 16, 405, 72), "NORTH SEA / OCEAN EXPLORER");
            GUI.Label(new Rect(28, 41, 390, 22), "WASD move  ·  Q/E down/up  ·  Shift boost  ·  RMB look");
            GUI.Label(new Rect(28, 62, 390, 22), $"L dive light   |   Depth {Mathf.Max(0,-transform.position.y):F1} m   |   Visibility {visibility:F0} m");
        }
    }
}
