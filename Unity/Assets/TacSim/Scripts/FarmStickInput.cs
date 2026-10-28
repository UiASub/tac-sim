using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace TacSim
{
    // Scene-local support; does not change keyboard/gamepad controls in other scenes.
    [DefaultExecutionOrder(250)]
    public sealed class FarmStickInput : MonoBehaviour
    {
        [Serializable] public sealed class Profile
        {
            public string forward="stick/y", turn="rz", valve="rx", depth="";
            public bool invertForward, invertTurn, invertValve, invertDepth;
            public float deadzone=.12f;
            public string[] buttons={"button31","button19","","","button29","button30","button20","button7","button21","button22","button23","button24"};
        }
        public static string DeviceDescription() => string.Join("\n\n",InputSystem.devices.Select(d=>d.displayName+"\n"+d.description.capabilities+"\n"+string.Join("\n",d.allControls.Select(c=>c.path+" : "+c.layout+" / "+c.displayName))));
        static readonly string[] Labels={"Koble til / slipp","Bytt kamera","Åpne ventil","Lukk ventil","Opp","Ned","Presisjon (hold)","Ny start (hold 1 sek)","Lys","Arm av/på","Vipp arm","Motor av/på"};
        const string PrefKey="TacSim.FarmStick.v2";
        Profile profile=new();
        TeknologidagenDive dive;
        EasyValveAssist assist;
        PilotInput pilot;
        InputDevice device;
        bool panel, owned, resetFired;
        int learning=-1;
        string learningAxis;
        readonly Dictionary<string,float> baseline=new();
        double learnAfter;
        float restartHeld;
        Vector2 scroll;
        string status="Koble til SimTask FarmStick via USB";
        double nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded-=SceneLoaded;
            SceneManager.sceneLoaded+=SceneLoaded;
            Attach();
        }
        static void SceneLoaded(Scene scene,LoadSceneMode mode) => Attach();
        static void Attach()
        {
            var world=UnityEngine.Object.FindFirstObjectByType<TeknologidagenDive>();
            if(world!=null && world.GetComponent<FarmStickInput>()==null) world.gameObject.AddComponent<FarmStickInput>();
        }
        void Awake()
        {
            dive=GetComponent<TeknologidagenDive>();
            try { if(PlayerPrefs.HasKey(PrefKey)) profile=JsonUtility.FromJson<Profile>(PlayerPrefs.GetString(PrefKey)) ?? new Profile(); }
            catch { profile=new Profile(); }
            if(profile.buttons==null || profile.buttons.Length!=Labels.Length) profile=new Profile();
        }
        void FindDevice()
        {
            device=InputSystem.devices.FirstOrDefault(d=> (d.displayName+" "+d.description.product).IndexOf("farmstick",StringComparison.OrdinalIgnoreCase)>=0);
            status=device==null ? "FarmStick ikke funnet" : device.displayName;
            if(device!=null && !PlayerPrefs.HasKey(PrefKey)) panel=true;
        }
        T Control<T>(string path) where T:InputControl => device==null || string.IsNullOrEmpty(path) ? null : device.TryGetChildControl<T>(path);
        ButtonControl Button(int index) => Control<ButtonControl>(profile.buttons[index]);
        bool Held(int index) => Button(index)?.isPressed==true;
        bool Pressed(int index) => Button(index)?.wasPressedThisFrame==true;
        public static float WithDeadzone(float value,float zone)
        {
            zone=Mathf.Clamp(zone,0,.5f);
            return Mathf.Abs(value)<=zone ? 0 : Mathf.Sign(value)*Mathf.Clamp01((Mathf.Abs(value)-zone)/(1-zone));
        }
        float Axis(string path,bool invert) => WithDeadzone((Control<AxisControl>(path)?.ReadValue() ?? 0)*(invert?-1:1),profile.deadzone);
        void Neutral()
        {
            if(owned && dive.vehicle!=null) dive.vehicle.SetCommand(Vector3.zero,Vector3.zero);
            owned=false; restartHeld=0; resetFired=false;
        }
        void Update()
        {
            if(dive.vehicle==null || dive.Valves==null) return;
            assist ??= dive.GetComponent<EasyValveAssist>();
            pilot ??= dive.vehicle.GetComponent<PilotInput>();
            if(device!=null && !device.added) { Neutral(); assist?.Release(); device=null; }
            if(device==null && Time.realtimeSinceStartupAsDouble>=nextScan) { nextScan=Time.realtimeSinceStartupAsDouble+1; FindDevice(); }
            if(device==null || !device.enabled || !Application.isFocused) { Neutral(); return; }
            if(learning>=0)
            {
                foreach(var button in device.allControls.OfType<ButtonControl>().Where(b=>b.parent==device))
                    if(button.wasPressedThisFrame) { profile.buttons[learning]=RelativePath(button); learning=-1; Save(); break; }
            }
            if(learningAxis!=null && Time.realtimeSinceStartupAsDouble>learnAfter)
            {
                foreach(var axis in device.allControls.OfType<AxisControl>().Where(a=>!(a is ButtonControl)))
                {
                    string path=RelativePath(axis);
                    if(!baseline.TryGetValue(path,out float before) || Mathf.Abs(axis.ReadValue()-before)<.45f) continue;
                    bool inverted=axis.ReadValue()<before;
                    if(learningAxis=="Twist høyre") { profile.turn=path; profile.invertTurn=inverted; }
                    if(learningAxis=="Minispak høyre") { profile.valve=path; profile.invertValve=inverted; }
                    if(learningAxis=="Slider opp") { profile.depth=path; profile.invertDepth=inverted; }
                    if(learningAxis=="Spak frem") { profile.forward=path; profile.invertForward=inverted; }
                    learningAxis=null; Save(); break;
                }
            }
            if(panel) { Neutral(); dive.vehicle.SetCommand(Vector3.zero,Vector3.zero); return; }
            if(pilot!=null && pilot.ExternalControl) { Neutral(); return; }
            float precision=Held(6) ? .3f : 1f;
            float depth=string.IsNullOrEmpty(profile.depth) ? (Held(4)?1:0)-(Held(5)?1:0) : Axis(profile.depth,profile.invertDepth);
            dive.vehicle.SetCommand(new Vector3(0,depth,Axis(profile.forward,profile.invertForward))*precision*dive.moveGain,
                new Vector3(0,Axis(profile.turn,profile.invertTurn)*precision*dive.yawGain,0));
            owned=true;
            if(Pressed(0) && assist!=null && assist.Active) { if(assist.Connected!=null) assist.Release(); else assist.Connect(assist.Candidate); }
            if(Pressed(1)) dive.diveCamera.GetComponent<PilotView>().CycleCamera();
            // Without the assist the valve stick turns the free torque tool; the pilot holds it on the handle.
            float valve=Axis(profile.valve,profile.invertValve);
            if(assist!=null && assist.Connected!=null) assist.Turn(valve,Time.deltaTime);
            else if(dive.armEnabled && dive.vehicle.Armed) dive.Arm.NudgeWrist(valve,Time.deltaTime);
            if(Held(7))
            {
                restartHeld+=Time.unscaledDeltaTime;
                if(restartHeld>=1 && !resetFired) { assist?.Release(); dive.Randomize(); resetFired=true; }
            }
            else { restartHeld=0; resetFired=false; }
            if(Pressed(8)) pilot?.ToggleLights();
            if(Pressed(9)) { assist?.Release(); dive.armEnabled=!dive.armEnabled; }
            if(Pressed(10) && assist?.Connected==null) dive.Arm.ToggleTilt();
            if(Pressed(11)) dive.vehicle.SetArmed(!dive.vehicle.Armed);
        }
        string RelativePath(InputControl control) => control.path.Substring(device.path.Length+1);
        void Save() { PlayerPrefs.SetString(PrefKey,JsonUtility.ToJson(profile)); PlayerPrefs.Save(); }
        void OnDisable() { Neutral(); assist?.Release(); }
        void OnApplicationFocus(bool focused) { if(!focused) { Neutral(); assist?.Release(); } }
        void OnGUI()
        {
            Matrix4x4 old=GUI.matrix;
            float scale=Mathf.Max(.5f,Mathf.Min(Screen.width/1280f,Screen.height/720f));
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale,height=Screen.height/scale;
            if(GUI.Button(new Rect(18,66,210,28),device==null ? "FarmStick · ikke tilkoblet" : "FarmStick · oppsett")) { panel=!panel; learning=-1; }
            if(restartHeld>0 && !resetFired) GUI.Box(new Rect(width/2-140,70,280,30),"Hold for ny start: "+Mathf.RoundToInt(restartHeld*100)+"%");
            if(panel)
            {
                float boxHeight=Mathf.Min(600,height-120);
                GUI.Box(new Rect(width/2-300,100,600,boxHeight),"SIMTASK FARMSTICK");
                GUILayout.BeginArea(new Rect(width/2-285,126,570,boxHeight-40));
                scroll=GUILayout.BeginScrollView(scroll);
                GUILayout.Label(status);
                GUILayout.Label("Kontroller står stille mens oppsettet er åpent.");
                if(device!=null)
                {
                    GUILayout.Label("Frem/bak: hovedspak · Sving: twist · Ventil: minispak");
                    GUILayout.Label("Opp/ned: slider · Tilkobling: frontknapp · Restart: hold trekant");
                    GUILayout.Label("Trykk Registrer, slipp alt til midten og beveg bare kontrollen som angitt.");
                    profile.forward=AxisPicker("Spak frem",profile.forward);
                    profile.invertForward=GUILayout.Toggle(profile.invertForward,"Snu frem/bak-retning");
                    profile.turn=AxisPicker("Twist høyre",profile.turn);
                    profile.invertTurn=GUILayout.Toggle(profile.invertTurn,"Snu svingretning");
                    profile.valve=AxisPicker("Minispak høyre",profile.valve);
                    profile.invertValve=GUILayout.Toggle(profile.invertValve,"Snu ventilretning");
                    profile.depth=AxisPicker("Slider opp",profile.depth);
                    profile.invertDepth=GUILayout.Toggle(profile.invertDepth,"Snu opp/ned-retning");
                    if(GUILayout.Button("Slideren er en vippebryter: bruk Opp/Ned-knappene nedenfor")) profile.depth="";
                    GUILayout.Label("Dødsone: "+Mathf.RoundToInt(profile.deadzone*100)+"%");
                    profile.deadzone=GUILayout.HorizontalSlider(profile.deadzone,.03f,.3f);
                    for(int i=0;i<Labels.Length;i++)
                    {
                        if(i==2 || i==3 || (!string.IsNullOrEmpty(profile.depth) && (i==4 || i==5))) continue;
                        GUILayout.BeginHorizontal(); GUILayout.Label(Labels[i],GUILayout.Width(210));
                        if(GUILayout.Button(learning==i ? "Trykk ønsket knapp …" : profile.buttons[i]+(Button(i)==null?" (mangler)":""))) { learning=i; learningAxis=null; }
                        GUILayout.EndHorizontal();
                    }
                    if(GUILayout.Button("Lagre og kjør")) { Save(); learning=-1; learningAxis=null; panel=false; }
                }
                if(GUILayout.Button("Lukk")) { learning=-1; learningAxis=null; panel=false; }
                GUILayout.EndScrollView(); GUILayout.EndArea();
            }
            GUI.matrix=old;
        }
        string AxisPicker(string label,string path)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label+": "+path+"  "+(Control<AxisControl>(path)?.ReadValue() ?? 0).ToString("F2"),GUILayout.Width(420));
            if(GUILayout.Button(learningAxis==label ? "Beveg nå …" : "Registrer"))
            {
                learning=-1; learningAxis=label; baseline.Clear(); learnAfter=Time.realtimeSinceStartupAsDouble+.3;
                foreach(var axis in device.allControls.OfType<AxisControl>().Where(a=>!(a is ButtonControl))) baseline[RelativePath(axis)]=axis.ReadValue();
            }
            GUILayout.EndHorizontal(); return path;
        }
    }
}

