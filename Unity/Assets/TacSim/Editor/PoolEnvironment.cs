using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacSim.Editor
{
    // Generated, editable scene assets. No runtime geometry construction.
    public static class PoolEnvironment
    {
        const string Art = "Assets/TacSim/Art/";
        static Material concrete, steel, dark, safety, glow, tiles, wallTiles;

        public static void Build(Transform pool, Transform dock, Camera camera, Light sun, PilotView view)
        {
            Directory.CreateDirectory(Art + "Textures");
            Directory.CreateDirectory(Art + "Meshes");
            concrete = Lit("Deck concrete", new Color(0.38f, 0.42f, 0.43f), 0, 0.18f);
            steel = Lit("Brushed steel", new Color(0.55f, 0.63f, 0.64f), 0.75f, 0.65f);
            dark = Lit("Fixture casing", new Color(0.045f, 0.07f, 0.085f), 0.4f, 0.35f);
            safety = Lit("Safety yellow", new Color(1, 0.63f, 0.05f), 0.2f, 0.35f);
            glow = Lit("Pool light diffuser", new Color(0.65f, 0.92f, 1), 0, 0.5f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(0.6f, 0.88f, 1) * 2.2f);
            tiles = Lit("Ceramic floor", new Color(0.68f, 0.79f, 0.77f), 0.05f, 0.65f);
            wallTiles = Lit("Ceramic end wall", new Color(0.73f, 0.81f, 0.79f), 0.02f, 0.55f);
            Texture2D tileTexture = MakeTiles();
            tiles.mainTexture = wallTiles.mainTexture = tileTexture;
            tiles.mainTextureScale = new Vector2(32, 48);
            wallTiles.mainTextureScale = new Vector2(32, 10.8f);
            Material sideTiles = Lit("Ceramic side wall", wallTiles.color, 0.02f, 0.55f);
            sideTiles.mainTexture = tileTexture;
            sideTiles.mainTextureScale = new Vector2(48, 10.8f);
            pool.Find("Floor").GetComponent<Renderer>().sharedMaterial = tiles;
            foreach (string name in new[] { "Left wall", "Right wall", "End wall", "Start wall" })
                pool.Find(name).GetComponent<Renderer>().sharedMaterial = name.Contains("wall") && (name.StartsWith("Left") || name.StartsWith("Right")) ? sideTiles : wallTiles;

            var root = new GameObject("Pool facility details").transform;
            foreach (float x in new[] { -9.1f, 9.1f })
            {
                Box("Poolside deck", root, new Vector3(x, 0.12f, 0), new Vector3(2.2f, 0.25f, 26), concrete);
                Box("Coping edge", root, new Vector3(Mathf.Sign(x) * 8.05f, 0.1f, 0), new Vector3(0.3f, 0.2f, 24.3f), steel);
                for (int z = -10; z <= 10; z += 4)
                {
                    Rod("Guardrail post", root, new Vector3(x, 0.2f, z), new Vector3(x, 1.3f, z), 0.045f, steel);
                    Box("Deck drain", root, new Vector3(x - Mathf.Sign(x)*0.5f, 0.255f, z), new Vector3(0.2f, 0.025f, 1.1f), dark, false);
                }
                Rod("Guardrail", root, new Vector3(x, 1.3f, -10), new Vector3(x, 1.3f, 10), 0.04f, steel);
            }
            foreach (int z in new[] { -13, 13 })
                Box("End deck", root, new Vector3(0, 0.12f, z), new Vector3(20.4f, 0.25f, 2), concrete);
            // A few structural beams establish a facility above the visible waterline.
            foreach (int z in new[] { -10, 0, 10 })
            {
                foreach (int x in new[] { -10, 10 })
                    Box("Hall column", root, new Vector3(x, 3, z), new Vector3(0.2f, 6, 0.2f), steel);
                Box("Roof beam", root, new Vector3(0, 6, z), new Vector3(20, 0.25f, 0.25f), steel);
                Box("Overhead strip light", root, new Vector3(0, 5.8f, z), new Vector3(6, 0.08f, 0.15f), glow, false);
            }

            var lights = new List<Light>();
            foreach (int side in new[] { -1, 1 })
            for (int z = -8; z <= 8; z += 8)
            {
                Vector3 p = new(side * 7.91f, -1.1f, z);
                Box("Underwater light housing", root, p, new Vector3(0.16f, 0.25f, 0.65f), dark);
                Box("Underwater light glass", root, p + Vector3.right * (-side * 0.1f), new Vector3(0.025f, 0.13f, 0.52f), glow, false);
                var lamp = new GameObject("Underwater pool light").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.position = p + Vector3.right * (-side * 0.3f);
                lamp.type = LightType.Point;
                lamp.color = new Color(0.6f, 0.85f, 0.95f);
                lamp.intensity = 1.5f;
                lamp.range = 5;
                lights.Add(lamp);
            }
            for (int depth = 1; depth <= 4; depth++)
            {
                Box("Depth gauge", root, new Vector3(-6.8f, -depth, 11.97f), new Vector3(0.8f, 0.045f, 0.02f), safety, false);
                Label($"0{depth} M", root, new Vector3(-6.2f, -depth + 0.12f, 11.94f), 0.12f, Color.white);
            }
            Label("T A C   /   TRAINING BASIN", root, new Vector3(0, -0.65f, 11.96f), 0.22f, new Color(0.02f, 0.17f, 0.2f));
            Label("05 M     •     PILOT PRACTICE", root, new Vector3(0, -1.03f, 11.95f), 0.1f, new Color(0.03f, 0.25f, 0.28f));

            // Ladder and pipework add scale cues without obstructing the pilot route.
            foreach (float z in new[] { -6.4f, -5.6f })
                Rod("Ladder rail", root, new Vector3(-7.65f, -4.7f, z), new Vector3(-7.65f, 0.7f, z), 0.05f, steel);
            for (float y = -4.4f; y < 0.5f; y += 0.35f)
                Rod("Ladder rung", root, new Vector3(-7.65f, y, -6.4f), new Vector3(-7.65f, y, -5.6f), 0.035f, steel);
            Rod("Service pipe", root, new Vector3(6.6f, -4.7f, -9), new Vector3(6.6f, -4.7f, 9), 0.12f, steel);
            for (int z = -8; z <= 8; z += 4)
            {
                Rod("Pipe coupling", root, new Vector3(6.6f, -4.7f, z - 0.055f), new Vector3(6.6f, -4.7f, z + 0.055f), 0.17f, dark);
                Box("Pipe support", root, new Vector3(6.6f, -4.95f, z), new Vector3(0.6f, 0.1f, 0.4f), concrete);
            }

            dock.position = new Vector3(1.8f, -4.8f, 4);
            foreach (float x in new[] { -0.73f, 0.73f })
            foreach (float z in new[] { -0.53f, 0.53f })
            {
                Rod("Dock guide post", dock, new Vector3(x, 0.17f, z), new Vector3(x, 0.5f, z), 0.035f, steel, true);
                Box("Guide light", dock, new Vector3(x, 0.51f, z), Vector3.one * 0.07f, glow, false, true);
            }
            Label("DOCK  /  01", dock, new Vector3(0, 0.05f, -0.611f), 0.085f, Color.white, true);

            // Two solid, collision-enabled practice hoops. These are not official TAC apparatus.
            Mesh ringMesh = RingMesh();
            foreach (Vector3 p in new[] { new Vector3(-2.4f, -2.8f, 0), new Vector3(-2.4f, -3.3f, 5.5f) })
            {
                var ring = new GameObject("Practice hoop");
                ring.transform.SetParent(root, false);
                ring.transform.position = p;
                ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                ring.AddComponent<MeshRenderer>().sharedMaterial = safety;
                ring.AddComponent<MeshCollider>().sharedMesh = ringMesh;
                Rod("Hoop stand", root, p + Vector3.down * 1.05f, new Vector3(p.x, -4.95f, p.z), 0.06f, steel);
                Box("Hoop foot", root, new Vector3(p.x, -4.94f, p.z), new Vector3(1.1f, 0.1f, 0.6f), dark);
            }

            Material caustics = ShaderMaterial("Animated caustics", "TacSim/Caustics");
            var causticSurfaces = new List<Renderer>();
            causticSurfaces.Add(Sheet("Floor caustics", root, new Vector3(0, -4.97f, 0), Vector3.up, new Vector2(16, 24), caustics));
            causticSurfaces.Add(Sheet("End caustics", root, new Vector3(0, -2.5f, 11.965f), Vector3.back, new Vector2(16, 5), caustics));
            causticSurfaces.Add(Sheet("Start caustics", root, new Vector3(0, -2.5f, -11.965f), Vector3.forward, new Vector2(16, 5), caustics));
            foreach (int side in new[] { -1, 1 })
                causticSurfaces.Add(Sheet("Side caustics", root, new Vector3(side * 7.965f, -2.5f, 0), Vector3.right * -side, new Vector2(5, 24), caustics));
            Renderer waterSurface = Sheet("Animated water surface", root, Vector3.zero, Vector3.up, new Vector2(16, 24), ShaderMaterial("Water surface", "TacSim/WaterSurface"));

            ParticleSystem silt = MakeSilt(root);
            var volume = new GameObject("Underwater camera effects").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            // Keep the effects in a referenced asset so player shader stripping retains them.
            string profilePath = Art + "Underwater effects.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var grade = profile.Add<ColorAdjustments>(true);
                grade.contrast.value = 10;
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.value = 0.22f;
                profile.Add<Vignette>(true).intensity.value = 0.2f;
                profile.Add<FilmGrain>(true).intensity.value = 0.07f;
                foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
            }
            volume.sharedProfile = profile;
            var appearance = camera.gameObject.AddComponent<WaterAppearance>();
            appearance.targetCamera = camera;
            appearance.overheadLight = sun;
            appearance.effects = volume;
            appearance.suspendedParticles = silt;
            appearance.causticSurfaces = causticSurfaces.ToArray();
            appearance.poolLights = lights.ToArray();
            appearance.waterSurface = waterSurface;
            view.appearance = appearance;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.shadowBias = 0.035f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            // Cap frame rate on the development laptop instead of rendering unlimited frames.
            QualitySettings.vSyncCount = 1;
            foreach (Material material in new[] { concrete, steel, dark, safety, glow, tiles, wallTiles, sideTiles })
                EditorUtility.SetDirty(material);
        }

        static Texture2D MakeTiles()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(x, y, size - 1 - x, size - 1 - y);
                float noise = Mathf.PerlinNoise(x * 0.037f, y * 0.037f);
                float value = edge < 3 ? 0.34f : edge < 5 ? 0.63f : 0.88f + noise * 0.1f;
                texture.SetPixel(x, y, new Color(value, value, value));
            }
            texture.Apply();
            string path = Art + "Textures/ceramic-tile.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static ParticleSystem MakeSilt(Transform root)
        {
            var particles = new GameObject("Suspended fine particles").AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.transform.SetParent(root, false);
            particles.transform.position = new Vector3(0, -2.6f, 0);
            var main = particles.main;
            main.startLifetime = 22;
            main.startSpeed = 0.012f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.009f, 0.027f);
            main.startColor = new Color(0.7f, 0.9f, 0.88f, 0.35f);
            main.maxParticles = 1100;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(15, 4.6f, 23);
            var emission = particles.emission;
            emission.rateOverTime = 15;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.3f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ShaderMaterial("Suspended silt", "TacSim/Silt");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            particles.Play();
            return particles;
        }

        static Renderer Sheet(string name, Transform parent, Vector3 position, Vector3 normal, Vector2 size, Material material)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = name;
            plane.transform.SetParent(parent, false);
            plane.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, normal));
            plane.transform.localScale = new Vector3(size.x / 10, 1, size.y / 10);
            Object.DestroyImmediate(plane.GetComponent<Collider>());
            var renderer = plane.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        static void Label(string text, Transform parent, Vector3 position, float size, Color color, bool local = false)
        {
            var label = new GameObject(text).AddComponent<TextMesh>();
            label.transform.SetParent(parent, false);
            if (local) label.transform.localPosition = position; else label.transform.position = position;
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Material labelMaterial = ShaderMaterial("Facility lettering", "TacSim/WorldLabel");
            labelMaterial.mainTexture = label.font.material.mainTexture;
            label.GetComponent<Renderer>().sharedMaterial = labelMaterial;
            label.gameObject.AddComponent<WorldLabel>();
            label.fontSize = 96;
            label.characterSize = size * 0.25f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = color;
        }

        static GameObject Box(string name, Transform parent, Vector3 p, Vector3 size, Material material, bool collision = true, bool local = false)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            if (local) box.transform.localPosition = p; else box.transform.position = p;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        static void Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material material, bool local = false)
        {
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = name;
            rod.transform.SetParent(parent, false);
            if (local) { a = parent.TransformPoint(a); b = parent.TransformPoint(b); }
            rod.transform.SetPositionAndRotation((a+b)*0.5f, Quaternion.FromToRotation(Vector3.up,(b-a).normalized));
            rod.transform.localScale = new Vector3(radius*2, Vector3.Distance(a,b)*0.5f,radius*2);
            rod.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            Material material = ShaderMaterial(name, "Universal Render Pipeline/Lit");
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        static Material ShaderMaterial(string name, string shaderName)
        {
            string path = Art + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new System.InvalidOperationException("Missing shader: " + shaderName);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh RingMesh()
        {
            const int segments = 64, sides = 10;
            var vertices = new Vector3[(segments+1)*(sides+1)];
            var normals = new Vector3[vertices.Length];
            var indices = new List<int>();
            for (int i=0;i<=segments;i++)
            for (int j=0;j<=sides;j++)
            {
                float a=i*Mathf.PI*2/segments, b=j*Mathf.PI*2/sides;
                Vector3 radial=new(Mathf.Cos(a),Mathf.Sin(a),0);
                int k=i*(sides+1)+j;
                normals[k]=radial*Mathf.Cos(b)+Vector3.forward*Mathf.Sin(b);
                vertices[k]=radial*1.15f+normals[k]*0.055f;
                if(i<segments && j<sides)
                {
                    int next=k+sides+1;
                    indices.AddRange(new[] {k,next,k+1,k+1,next,next+1});
                }
            }
            string path=Art+"Meshes/Practice hoop.asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear(); mesh.vertices=vertices; mesh.normals=normals; mesh.triangles=indices.ToArray();
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
