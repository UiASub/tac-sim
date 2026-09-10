using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacSim.Editor
{
    public static class TrainingProject
    {
        public const string ScenePath = "Assets/TacSim/Scenes/TrainingPool.unity";
        static Material navy, orange, metal, white, teal, yellow;

        [MenuItem("TAC/Create training pool (replaces generated scene)")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/TacSim/Scenes");
            Directory.CreateDirectory("Assets/TacSim/Art/Materials");
            Directory.CreateDirectory("Assets/TacSim/Prefabs");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            navy = Material("Frame", new Color(0.035f, 0.075f, 0.1f), 0.6f);
            orange = Material("Buoyancy", new Color(0.96f, 0.38f, 0.055f));
            metal = Material("Metal", new Color(0.3f, 0.4f, 0.44f), 0.65f);
            white = Material("Pool tiles", new Color(0.55f, 0.74f, 0.72f));
            teal = Material("Lane markings", new Color(0.025f, 0.23f, 0.3f));
            yellow = Material("Landing pad", new Color(1f, 0.72f, 0.05f));

            var pool = new GameObject("Training pool • 16 × 24 × 5 m");
            Box("Floor", pool.transform, new Vector3(0, -5.2f, 0), new Vector3(16, 0.4f, 24), white);
            Box("Left wall", pool.transform, new Vector3(-8.2f, -2.5f, 0), new Vector3(0.4f, 5.4f, 24), white);
            Box("Right wall", pool.transform, new Vector3(8.2f, -2.5f, 0), new Vector3(0.4f, 5.4f, 24), white);
            Box("End wall", pool.transform, new Vector3(0, -2.5f, 12.2f), new Vector3(16, 5.4f, 0.4f), white);
            Box("Start wall", pool.transform, new Vector3(0, -2.5f, -12.2f), new Vector3(16, 5.4f, 0.4f), white);
            for (int x = -6; x <= 6; x += 3)
                Box("Lane", pool.transform, new Vector3(x, -4.985f, 0), new Vector3(0.075f, 0.02f, 23), teal, false);
            for (int z = -10; z <= 10; z += 2)
                Box("Distance grid", pool.transform, new Vector3(0, -4.98f, z), new Vector3(15.9f, 0.01f, 0.025f), teal, false);
            for (int y = -4; y <= -1; y++)
                Box("Wall depth stripe", pool.transform, new Vector3(0, y, 11.985f), new Vector3(16, 0.055f, 0.02f), teal, false);

            var dock = new GameObject("Practice landing pad • not competition scoring");
            dock.transform.position = new Vector3(0, -4.8f, 4);
            Box("Base", dock.transform, Vector3.zero, new Vector3(1.6f, 0.3f, 1.2f), navy);
            Box("Landing plate", dock.transform, new Vector3(0, 0.17f, 0), new Vector3(1.6f, 0.04f, 1.2f), yellow);
            Box("Alignment cross X", dock.transform, new Vector3(0, 0.197f, 0), new Vector3(1.2f, 0.012f, 0.06f), navy, false);
            Box("Alignment cross Z", dock.transform, new Vector3(0, 0.198f, 0), new Vector3(0.06f, 0.012f, 0.9f), navy, false);

            var rov = new GameObject("ROV • eight thrusters");
            rov.transform.position = new Vector3(0, -2.5f, -5);
            rov.AddComponent<Rigidbody>();
            var hull = rov.AddComponent<BoxCollider>();
            hull.size = new Vector3(1.05f, 0.65f, 1.25f);
            var vehicle = rov.AddComponent<RovVehicle>();
            Box("Electronics enclosure", rov.transform, Vector3.zero, new Vector3(0.5f, 0.3f, 0.8f), metal, false);
            foreach (float x in new[] { -0.43f, 0.43f })
            {
                Box("Float", rov.transform, new Vector3(x, 0.24f, 0), new Vector3(0.22f, 0.18f, 1.15f), orange, false);
                Box("Skid", rov.transform, new Vector3(x, -0.28f, 0), new Vector3(0.06f, 0.06f, 1.25f), navy, false);
                foreach (float z in new[] { -0.5f, 0.5f })
                    Box("Upright", rov.transform, new Vector3(x, 0, z), new Vector3(0.055f, 0.5f, 0.055f), navy, false);
            }
            foreach (float z in new[] { -0.5f, 0.5f })
                Box("Crossbar", rov.transform, new Vector3(0, -0.25f, z), new Vector3(0.9f, 0.055f, 0.055f), navy, false);
            vehicle.thrusters = new Thruster[8];
            int i = 0;
            foreach (float x in new[] { -0.36f, 0.36f })
            foreach (float z in new[] { -0.42f, 0.42f })
            {
                Vector3 direction = new Vector3(-Mathf.Sign(x) * Mathf.Sign(z), 0, 1).normalized;
                AddThruster(rov.transform, vehicle, i++, new Vector3(x, -0.08f, z), direction, 32);
                AddThruster(rov.transform, vehicle, i++, new Vector3(x, 0.08f, z * 0.65f), Vector3.up, 24);
            }
            Box("Forward camera", rov.transform, new Vector3(0, 0.06f, 0.48f), new Vector3(0.15f, 0.13f, 0.2f), navy, false);
            var lamps = new Light[2];
            for (int n = 0; n < lamps.Length; n++)
            {
                var lamp = new GameObject("Headlight");
                lamp.transform.SetParent(rov.transform, false);
                lamp.transform.localPosition = new Vector3(n == 0 ? -0.32f : 0.32f, 0.04f, 0.64f);
                lamps[n] = lamp.AddComponent<Light>();
                lamps[n].type = LightType.Spot;
                lamps[n].range = 12;
                lamps[n].spotAngle = 65;
                lamps[n].intensity = 8;
                lamps[n].color = new Color(0.75f, 0.9f, 1);
            }

            var cameraObject = new GameObject("Pilot display");
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 70;
            camera.fieldOfView = 65;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.15f, 0.2f);
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var view = cameraObject.AddComponent<PilotView>();
            view.vehicle = vehicle;
            view.pilotCamera = camera;
            var input = rov.AddComponent<PilotInput>();
            input.vehicle = vehicle;
            input.view = view;
            input.headlights = lamps;
            var smoke = cameraObject.AddComponent<SimulationSmokeCheck>();
            smoke.vehicle = vehicle;
            smoke.input = input;
            smoke.view = view;
            cameraObject.transform.position = new Vector3(0, -1.4f, -8.2f);
            cameraObject.transform.LookAt(rov.transform.position + Vector3.forward);

            var sun = new GameObject("Pool lighting").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(0.66f, 0.85f, 0.95f);
            sun.transform.rotation = Quaternion.Euler(55, -25, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.4f, 0.44f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = camera.backgroundColor;
            RenderSettings.fogDensity = 0.045f;
            Time.fixedDeltaTime = 0.02f;

            PlayerSettings.companyName = "UiASub";
            PlayerSettings.productName = "TAC ROV Training";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("TAC_SCENE_CREATED");
        }

        static void AddThruster(Transform parent, RovVehicle vehicle, int index, Vector3 position, Vector3 direction, float force)
        {
            vehicle.thrusters[index] = new Thruster { position = position, direction = direction, maximumForce = force };
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = $"Thruster {index + 1}";
            body.transform.SetParent(parent, false);
            body.transform.localPosition = position;
            body.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            body.transform.localScale = new Vector3(0.17f, 0.1f, 0.17f);
            body.GetComponent<Renderer>().sharedMaterial = navy;
            Object.DestroyImmediate(body.GetComponent<Collider>());
        }

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        static Material Material(string name, Color color, float metallic = 0)
        {
            string path = $"Assets/TacSim/Art/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(material);
            return material;
        }

        [MenuItem("TAC/Build Linux player")]
        public static void BuildLinux()
        {
            Directory.CreateDirectory("Builds/Linux");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Linux/TacSim.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.Exception($"Linux build failed: {report.summary.result}");
            Debug.Log("TAC_BUILD_SUCCEEDED");
        }
    }
}
