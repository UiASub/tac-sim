using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TacSim.Editor
{
    public static class TeknologidagenSetup
    {
        const string ScenePath = "Assets/teknologidagen_scene.unity";
        const string Request = "Temp/teknologidagen.request";
        const string Result = "Temp/teknologidagen-result.txt";
        const string Folder = "Assets/TacSim/Art/Teknologidagen";
        [InitializeOnLoadMethod] static void Init() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string action=File.ReadAllText(Request).Trim(); File.Delete(Request);
            try
            {
                if(action=="build") Build();
                else if(action=="play") EditorApplication.EnterPlaymode();
                else if(action=="stop") EditorApplication.ExitPlaymode();
                else if(action=="test") Test();
                else if(action=="polish") Polish();
                else if(action=="upgrade") TeknologidagenUpgrade.Run();
            }
            catch(Exception e) { File.WriteAllText(Result,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("TAC/Teknologidagen/Build lake scene")]
        public static void Build()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            var target=SceneManager.GetActiveScene();
            if(target.path!=ScenePath) throw new InvalidOperationException("Open teknologidagen_scene first.");
            if(target.GetRootGameObjects().Any(g=>g.name=="Teknologidagen lake")) throw new InvalidOperationException("Scene already built; edit the existing lake.");
            // Preserve the original scene before modifying its default camera/light setup.
            EditorSceneManager.SaveScene(target,"Temp/teknologidagen-before.unity",true);
            var source=EditorSceneManager.OpenScene(TrainingProject.ScenePath,OpenSceneMode.Additive);
            var vehicle=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RovVehicle>(true)).First();
            var view=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PilotView>(true)).First();
            SceneManager.MoveGameObjectToScene(vehicle.transform.root.gameObject,target);
            SceneManager.MoveGameObjectToScene(view.transform.root.gameObject,target);
            view.appearance=null;
            foreach(var app in view.GetComponentsInChildren<WaterAppearance>(true)) Object.DestroyImmediate(app);
            foreach(var smoke in view.GetComponentsInChildren<SimulationSmokeCheck>(true)) Object.DestroyImmediate(smoke);
            EditorSceneManager.CloseScene(source,true);
            SceneManager.SetActiveScene(target);
            foreach(var root in target.GetRootGameObjects())
                if(root.name=="Main Camera") Object.DestroyImmediate(root);
            Directory.CreateDirectory(Folder);
            var lake=new GameObject("Teknologidagen lake");
            var dive=lake.AddComponent<TeknologidagenDive>();
            dive.vehicle=vehicle; dive.diveCamera=view.pilotCamera;
            view.missionHint="TEKNOLOGIDAGEN / Finn strukturen og åpne ventilene med armen.";
            vehicle.transform.SetPositionAndRotation(new Vector3(0,-7,-7),Quaternion.identity);
            vehicle.currentPoolFloor=-10; vehicle.currentPoolSize=new Vector2(30,30); vehicle.currentSpeed=.07f;
            var sand=Mat("Silt seabed",new Color(.29f,.28f,.20f));
            var stone=Mat("Lake rock",new Color(.20f,.25f,.23f));
            Box("Seabed -10m",lake.transform,new Vector3(0,-10.25f,0),new Vector3(32,.5f,32),sand);
            Box("Shore west",lake.transform,new Vector3(-15.5f,-5,0),new Vector3(1,10,32),stone);
            Box("Shore east",lake.transform,new Vector3(15.5f,-5,0),new Vector3(1,10,32),stone);
            Box("Shore north",lake.transform,new Vector3(0,-5,15.5f),new Vector3(30,10,1),stone);
            Box("Shore south",lake.transform,new Vector3(0,-5,-15.5f),new Vector3(30,10,1),stone);
            var rng=new System.Random(781);
            for(int i=0;i<95;i++)
            {
                float a=(float)rng.NextDouble()*Mathf.PI*2, radius=11.5f+(float)rng.NextDouble()*2.5f;
                var rock=GameObject.CreatePrimitive(PrimitiveType.Sphere); rock.name="Shore rock "+i;
                rock.transform.SetParent(lake.transform,false);
                float size=.3f+(float)rng.NextDouble()*1.3f;
                rock.transform.localPosition=new Vector3(Mathf.Sin(a)*radius,-10+size*.18f,Mathf.Cos(a)*radius);
                rock.transform.localScale=new Vector3(size*1.3f,size*.7f,size);
                rock.transform.localRotation=Quaternion.Euler(10+i%17,i*73,12);
                rock.GetComponent<Renderer>().sharedMaterial=stone;
            }
            var water=Mat("Lake surface",new Color(.055f,.28f,.29f));
            water.SetFloat("_Smoothness",.92f); water.SetFloat("_Cull",0);
            var surface=GameObject.CreatePrimitive(PrimitiveType.Plane); surface.name="Water surface 0m";
            surface.transform.SetParent(lake.transform,false); surface.transform.localScale=new Vector3(3,1,3);
            surface.GetComponent<Renderer>().sharedMaterial=water; Object.DestroyImmediate(surface.GetComponent<Collider>());
            var structureRoot=new GameObject("Structure spawn").transform; structureRoot.SetParent(lake.transform,false);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TacSim/Prefabs/UnderwaterStructure.prefab");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,target);
            model.transform.SetParent(structureRoot,false); model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            structureRoot.position=new Vector3(0,-10-TeknologidagenDive.BoundsOf(structureRoot).min.y+.02f,0);
            dive.structure=structureRoot;
            Particles("Suspended sediment",lake.transform,new Vector3(0,-5,0),new Vector3(29,9,29),35,40,.018f);
            dive.silt=Particles("Thruster silt cloud",lake.transform,new Vector3(0,-9.8f,0),new Vector3(1.4f,.1f,1.4f),0,7,.15f);
            var sun=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l=>l.type==LightType.Directional);
            if(sun==null) sun=new GameObject("Sun").AddComponent<Light>();
            sun.type=LightType.Directional; sun.intensity=1.2f; sun.color=new Color(.65f,.85f,.80f);
            sun.transform.rotation=Quaternion.Euler(55,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.24f,.36f,.32f);
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(.035f,.14f,.15f); RenderSettings.fogDensity=.065f;
            view.pilotCamera.clearFlags=CameraClearFlags.SolidColor; view.pilotCamera.backgroundColor=RenderSettings.fogColor;
            view.pilotCamera.transform.SetPositionAndRotation(new Vector3(6,-5,-8),Quaternion.LookRotation(new Vector3(-6,-3,8)));
            TeknologidagenUpgrade.Apply(dive,new System.Text.StringBuilder());
            EditorSceneManager.SaveScene(target); AssetDatabase.SaveAssets();
            var scenes=EditorBuildSettings.scenes.ToList();
            if(!scenes.Any(s=>s.path==ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            File.WriteAllText(Result,"BUILT: 80x80 m fjord basin, 10–15 m; structure prefab, ROV, boulders, eelgrass, kelp, marine snow.");
        }
        static Material Mat(string name,Color color)
        {
            string path=Folder+"/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path); }
            m.color=color; m.SetFloat("_Smoothness",.15f); return m;
        }
        static void Box(string name,Transform parent,Vector3 p,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(parent,false);
            go.transform.localPosition=p; go.transform.localScale=size; go.GetComponent<Renderer>().sharedMaterial=material;
        }
        static ParticleSystem Particles(string name,Transform parent,Vector3 p,Vector3 size,float rate,float life,float diameter)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=p;
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.startLifetime=life; main.startSpeed=.035f; main.startSize=new ParticleSystem.MinMaxCurve(diameter*.4f,diameter);
            main.startColor=new Color(.45f,.49f,.36f,.24f); main.maxParticles=2200; main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.prewarm=true; main.duration=40;
            var emission=ps.emission; emission.rateOverTime=rate;
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Box; shape.scale=size;
            var noise=ps.noise; noise.enabled=true; noise.strength=.12f; noise.frequency=.3f; noise.scrollSpeed=.08f;
            string path=Folder+"/Sediment.mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) { mat=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); mat.SetFloat("_Surface",1); mat.SetFloat("_Blend",0); mat.SetFloat("_SrcBlend",5); mat.SetFloat("_DstBlend",10); mat.SetFloat("_ZWrite",0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue=3000; AssetDatabase.CreateAsset(mat,path); }
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;
            return ps;
        }
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        static void Polish()
        {
            var water=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Lake surface.mat");
            water.shader=Shader.Find("TacSim/WaterSurface"); water.SetColor("_Color",new Color(.055f,.24f,.22f,.72f)); water.SetFloat("_LightLevel",.85f);
            var sediment=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Sediment.mat"); sediment.shader=Shader.Find("TacSim/Silt");
            EditorUtility.SetDirty(water); EditorUtility.SetDirty(sediment); AssetDatabase.SaveAssets();
            File.WriteAllText(Result,"POLISHED: animated water and soft sediment shaders.");
        }
        static void Test()
        {
            var dive=Object.FindFirstObjectByType<TeknologidagenDive>();
            Check(EditorApplication.isPlaying && dive!=null && dive.ValveA!=null,"Scene has not started");
            for(int i=0;i<100;i++)
            {
                dive.Randomize();
                var p=dive.vehicle.Body.position;
                Check(p.y<=-3 && p.y>=dive.FloorAt(p)+1,"Depth out of range");
                Check(Vector3.Distance(p,dive.structure.position)>10,"Spawn too close to the structure");
                Check(Mathf.Abs(TeknologidagenDive.BoundsOf(dive.structure).min.y-dive.FloorAt(dive.structure.position))<.6f,"Structure not on bottom");
            }
            Check(dive.Valves.Length==1,"Valve count="+dive.Valves.Length);
            foreach(var valve in dive.Valves)
            {
                valve.SetAngle(0);
                Vector3 tip=valve.transform.TransformPoint(new Vector3(0,.04f,0));
                Check(!valve.Drive(tip+Vector3.one*2,-valve.transform.up,valve.transform.forward),"Remote engagement");
                for(int a=0;a<=90;a+=5)
                    Check(valve.Drive(tip,-valve.transform.up,valve.transform.TransformDirection(Quaternion.Euler(0,a,0)*Vector3.forward)),"Valve grip lost");
                Check(valve.IsOpen && valve.Completed,"Valve did not open");
                Check(Quaternion.Angle(valve.transform.Find("Handle").localRotation,Quaternion.Euler(0,90,0))<.01f,"Handle not rotating");
                valve.SetAngle(120); Check(valve.angle==90,"Upper stop failed");
                valve.SetAngle(-30); Check(valve.angle==0,"Lower stop failed");
            }
            dive.Randomize();
            File.WriteAllText(Result,"PASS: 100 spawn layouts on the sloping floor; depth and bottom clearance; remote grip rejected; wall valve driven 0–90 with the real handle turning and limits.");
        }
    }
}
