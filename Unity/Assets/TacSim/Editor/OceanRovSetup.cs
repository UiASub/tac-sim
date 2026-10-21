using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TacSim.Editor
{
    public static class OceanRovSetup
    {
        [MenuItem("TAC/Add existing Malstrom ROV to ocean")]
        public static void Install()
        {
            const string path = "Assets/Scenes/SampleScene.unity";
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var ocean = EditorSceneManager.OpenScene(path);
            var explorer = UnityEngine.Object.FindFirstObjectByType<OceanExplorer>();
            if (explorer == null) throw new Exception("Open ocean scene has no explorer camera.");
            var vehicle = UnityEngine.Object.FindFirstObjectByType<RovVehicle>();
            if (vehicle == null)
            {
                var source = EditorSceneManager.OpenScene(TrainingProject.ScenePath, OpenSceneMode.Additive);
                var original = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RovVehicle>(true)).Single();
                var clone = UnityEngine.Object.Instantiate(original.gameObject);
                clone.name = "Malstrom ROV • ocean pilot";
                SceneManager.MoveGameObjectToScene(clone, ocean);
                vehicle = clone.GetComponent<RovVehicle>();
                EditorSceneManager.CloseScene(source, true);
            }
            SceneManager.SetActiveScene(ocean);
            // Place the actual vehicle safely above the local seabed, near the original viewpoint.
            Physics.SyncTransforms();
            var position = new Vector3(-220, -30, -270);
            if (Physics.Raycast(new Vector3(position.x, 10, position.z), Vector3.down, out var hit, 250)) position.y = hit.point.y + 3;
            vehicle.transform.SetPositionAndRotation(position, Quaternion.Euler(0,25,0));
            vehicle.waterSurface = 0;
            vehicle.currentPoolSize = new Vector2(2000,2000);
            vehicle.currentPoolFloor = -110;
            var visuals = vehicle.GetComponent<RovVisuals>();
            if (vehicle.thrusters.Length != 8) throw new Exception("Existing ROV thruster references are incomplete.");
            if (visuals == null || visuals.lowModel == null || visuals.highModel == null)
            {
                // Keep the training scene's existing primitive ROV visible until external CAD assets are restored.
                if (visuals != null) UnityEngine.Object.DestroyImmediate(visuals);
                foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                Debug.LogWarning("OCEAN_ROV_MODEL_MISSING: using existing training ROV geometry; restore Assets/External/ROV FBX files for Malstrom detail.");
            }
            var camera = explorer.GetComponent<Camera>();
            var view = explorer.GetComponent<PilotView>() ?? explorer.gameObject.AddComponent<PilotView>();
            view.vehicle = vehicle; view.pilotCamera = camera; view.appearance = null;
            view.missionHint = "OCEAN / Explore the seabed. Sea level 0 m. Suspended silt limits visibility.";
            var input = vehicle.GetComponent<PilotInput>();
            input.vehicle = vehicle; input.view = view; input.headlights = vehicle.GetComponentsInChildren<Light>(true);
            explorer.pilotControlled = true;
            explorer.diveLight.enabled = false;
            camera.transform.position = position + new Vector3(.65f,.45f,-.95f);
            camera.transform.LookAt(position);
            EditorSceneManager.SaveScene(ocean);
            AssetDatabase.SaveAssets();
            if (input.headlights.Length != 2) throw new Exception("Expected two original ROV headlights.");
            if (ocean.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RovVehicle>()).Count() != 1)
                throw new Exception("Expected exactly one ocean ROV.");
            Debug.Log("OCEAN_ROV_INSTALLED: existing training ROV, eight thrusters, two headlights, pilot camera and input wired.");
        }
    }
}
