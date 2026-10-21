using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacSim.Editor
{
    public static class OceanSceneBuilder
    {
        const string Art = "Assets/TacSim/Ocean/";
        const string Scene = "Assets/Scenes/SampleScene.unity";
        static float Height(float x, float z) => -65 + 34 * Mathf.PerlinNoise(x*.0018f+31,z*.0018f+17)
            + 10*Mathf.PerlinNoise(x*.009f+7,z*.009f+9) - 38*Mathf.Exp(-Mathf.Pow((x-200*Mathf.Sin(z*.003f))/150,2));
        [MenuItem("TAC/Create ocean in SampleScene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Art);
            // Preserve the original scene once before replacing its empty template.
            if (!File.Exists("Assets/Scenes/SampleScene.before-ocean.unity"))
                AssetDatabase.CopyAsset(Scene,"Assets/Scenes/SampleScene.before-ocean.unity");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sand=Mat("Silt and sand",new Color(.29f,.28f,.20f));
            var rock=Mat("Algae covered rock",new Color(.17f,.22f,.18f));
            var kelp=Mat("Kelp",new Color(.12f,.22f,.09f));
            var tex=new Texture2D(256,256); tex.name="Sediment ripples";
            for(int y=0;y<256;y++) for(int x=0;x<256;x++) {
                float a=.65f+.22f*Mathf.PerlinNoise(x*.12f,y*.12f)+.1f*Mathf.Sin(y*.45f+4*Mathf.PerlinNoise(x*.025f,y*.025f));
                tex.SetPixel(x,y,new Color(a,a,a)); }
            tex.Apply(); AssetDatabase.CreateAsset(tex,Art+"SiltTexture.asset"); sand.mainTexture=tex;
            var floor=new GameObject("Seabed • 2000 x 2000 m • collidable tiles").transform;
            for(int tz=0;tz<8;tz++) for(int tx=0;tx<8;tx++) {
                const int n=32; var vertices=new Vector3[(n+1)*(n+1)]; var uv=new Vector2[vertices.Length]; var triangles=new int[n*n*6];
                for(int z=0;z<=n;z++) for(int x=0;x<=n;x++) {
                    int i=z*(n+1)+x; float wx=-1000+tx*250+x*250f/n,wz=-1000+tz*250+z*250f/n;
                    vertices[i]=new Vector3(wx,Height(wx,wz),wz); uv[i]=new Vector2(wx/12,wz/12);
                    if(x<n && z<n) {int t=(z*n+x)*6; triangles[t]=i;triangles[t+1]=i+n+1;triangles[t+2]=i+1;triangles[t+3]=i+1;triangles[t+4]=i+n+1;triangles[t+5]=i+n+2;}
                }
                var mesh=new Mesh {name=$"Seabed {tx} {tz}",vertices=vertices,uv=uv,triangles=triangles}; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,Art+$"Seabed-{tx}-{tz}.asset");
                var tile=new GameObject(mesh.name);tile.transform.SetParent(floor);tile.AddComponent<MeshFilter>().sharedMesh=mesh;
                tile.AddComponent<MeshRenderer>().sharedMaterial=sand;tile.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            var random=new System.Random(812); float R(float a,float b)=>(float)(a+(b-a)*random.NextDouble());
            var props=new GameObject("Rock fields and kelp beds").transform;
            for(int i=0;i<650;i++) {
                float x=R(-980,980),z=R(-980,980),s=R(2,14);
                var b=GameObject.CreatePrimitive(PrimitiveType.Sphere);b.name="Glacial boulder";b.transform.SetParent(props);
                b.transform.position=new Vector3(x,Height(x,z)+s*.12f,z);b.transform.localScale=new Vector3(s,s*R(.4f,.8f),s*R(.7f,1.5f));
                b.transform.rotation=Quaternion.Euler(R(-20,20),R(0,360),R(-20,20));b.GetComponent<Renderer>().sharedMaterial=rock;
                if(i%3==0) for(int j=0;j<4;j++) {
                    float kx=x+R(-8,8),kz=z+R(-8,8),h=R(1.5f,5);
                    var plant=GameObject.CreatePrimitive(PrimitiveType.Capsule);plant.name="Kelp frond";plant.transform.SetParent(props);
                    plant.transform.position=new Vector3(kx,Height(kx,kz)+h/2,kz);plant.transform.localScale=new Vector3(.3f,h/2,.65f);
                    plant.transform.rotation=Quaternion.Euler(12,R(0,360),15);plant.GetComponent<Renderer>().sharedMaterial=kelp;Object.DestroyImmediate(plant.GetComponent<Collider>());
                }
            }
            var water=GameObject.CreatePrimitive(PrimitiveType.Plane);water.name="Ocean surface • sea level 0 m";water.transform.localScale=new Vector3(240,1,240);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var wm=new Material(Shader.Find("TacSim/OceanWater"));AssetDatabase.CreateAsset(wm,Art+"Water.mat");water.GetComponent<Renderer>().sharedMaterial=wm;
            var camera=new GameObject("Ocean explorer").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.1f;camera.farClipPlane=3000;
            camera.transform.position=new Vector3(-220,Height(-220,-280)+10,-280);camera.transform.rotation=Quaternion.Euler(12,25,0);camera.gameObject.AddComponent<AudioListener>();
            var explorer=camera.gameObject.AddComponent<TacSim.OceanExplorer>();
            var light=new GameObject("Dive floodlight").AddComponent<Light>();light.transform.SetParent(camera.transform,false);light.type=LightType.Spot;light.range=65;light.spotAngle=90;light.intensity=18;light.color=new Color(.8f,.91f,.83f);explorer.diveLight=light;
            var dust=new GameObject("Suspended silt • follows observer").AddComponent<ParticleSystem>();dust.transform.SetParent(camera.transform,false);
            dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=dust.main;main.loop=true;main.prewarm=true;main.startLifetime=18;main.startSpeed=.08f;main.startSize=new ParticleSystem.MinMaxCurve(.025f,.12f);main.startColor=new Color(.55f,.63f,.43f,.4f);main.maxParticles=2400;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var em=dust.emission;em.rateOverTime=120;var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(45,30,45);
            var noise=dust.noise;noise.enabled=true;noise.strength=.25f;noise.frequency=.08f;noise.scrollSpeed=.12f;
            var pm=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));pm.SetFloat("_Surface",1);pm.SetFloat("_Blend",0);pm.SetFloat("_SrcBlend",5);pm.SetFloat("_DstBlend",10);pm.SetFloat("_ZWrite",0);pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");pm.renderQueue=3000;
            var dot=new Texture2D(32,32);for(int y=0;y<32;y++)for(int x=0;x<32;x++){float a=Mathf.Clamp01(1-new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude);dot.SetPixel(x,y,new Color(1,1,1,a*a));}dot.Apply();AssetDatabase.CreateAsset(dot,Art+"SiltParticle.asset");pm.SetTexture("_BaseMap",dot);AssetDatabase.CreateAsset(pm,Art+"SiltParticles.mat");dust.GetComponent<ParticleSystemRenderer>().sharedMaterial=pm;explorer.sediment=dust;
            var sun=new GameObject("Filtered daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(.53f,.73f,.68f);sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(48,-32,0);
            RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.18f,.29f,.25f);RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=1.5f/45;RenderSettings.fogColor=new Color(.025f,.12f,.115f);camera.backgroundColor=RenderSettings.fogColor;
            EditorSceneManager.SaveScene(scene,Scene);AssetDatabase.SaveAssets();
            if(ShaderUtil.ShaderHasError(wm.shader)) throw new System.Exception("Ocean shader compilation failed");
            if(floor.childCount!=64) throw new System.Exception("Missing seabed tiles");
            Debug.Log("OCEAN_SCENE_CREATED: 2 km x 2 km, 64 collision tiles, 650 boulders, suspended silt.");
        }
        static Material Mat(string name,Color color) {var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.color=color;m.SetFloat("_Smoothness",.15f);AssetDatabase.CreateAsset(m,Art+name+".mat");return m;}
    }
}

