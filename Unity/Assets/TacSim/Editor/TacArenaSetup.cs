using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    // TAC Challenge arena: a copy of the training pool with the practice props hidden and a procedural
    // TacCourse (pipeline, docking station, subsea structure). The course itself is built in Play mode.
    public static class TacArenaSetup
    {
        public const string ArenaPath = "Assets/TacSim/Scenes/TacArena.unity";
        const string AutomationMenu = "TAC/Automation server in Play mode (port 8765)";

        [MenuItem("TAC/Create TAC arena scene (replaces TacArena)")]
        public static void CreateArena()
        {
            if (!File.Exists(TrainingProject.ScenePath)) throw new System.Exception("Missing " + TrainingProject.ScenePath);
            AssetDatabase.DeleteAsset(ArenaPath);
            if (!AssetDatabase.CopyAsset(TrainingProject.ScenePath, ArenaPath))
                throw new System.Exception("Could not copy the training pool scene");
            var scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("Practice landing pad")) root.SetActive(false);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name.StartsWith("Practice hoop") || child.name.StartsWith("Hoop "))
                        child.gameObject.SetActive(false);
            }

            // Start near the launch end, at the surface, facing down the pool.
            var vehicle = Object.FindFirstObjectByType<RovVehicle>();
            vehicle.transform.SetPositionAndRotation(new Vector3(0, -0.6f, -10.2f), Quaternion.identity);

            var course = new GameObject("TAC course").AddComponent<TacCourse>();
            course.transform.position = new Vector3(0, -5f, 0); // pool floor surface
            course.seed = 1;

            EditorSceneManager.SaveScene(scene);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ArenaPath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ArenaPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("TAC_ARENA_CREATED");
        }

        [MenuItem("TAC/Build Windows player (TAC arena)")]
        public static void BuildWindows()
        {
            if (!File.Exists(ArenaPath)) CreateArena();
            Directory.CreateDirectory("Builds/Windows");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ArenaPath },
                locationPathName = "Builds/Windows/TacSim.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.Exception($"Windows build failed: {report.summary.result}");
            Debug.Log("TAC_BUILD_SUCCEEDED");
        }

        [MenuItem(AutomationMenu)]
        static void ToggleAutomation() =>
            EditorPrefs.SetBool(AutomationServer.EditorPrefKey, !EditorPrefs.GetBool(AutomationServer.EditorPrefKey, false));

        [MenuItem(AutomationMenu, true)]
        static bool ToggleAutomationValidate()
        {
            Menu.SetChecked(AutomationMenu, EditorPrefs.GetBool(AutomationServer.EditorPrefKey, false));
            return true;
        }
    }
}
