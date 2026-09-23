using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    public static class StructureSetup
    {
        const string Folder = "Assets/TacSim/Art/Structure/";
        const string Model = Folder + "UnderwaterStructure.fbx";
        const string RootName = "Underwater structure";
        const string Request = "Temp/install-structure.request";
        [Serializable] class Palette { public Entry[] materials; }
        [Serializable] class Entry { public string name; public float[] color; public float metallic, smoothness; }

        [InitializeOnLoadMethod]
        static void WatchRequest() { EditorApplication.update -= ProcessRequest; EditorApplication.update += ProcessRequest; }
        static void ProcessRequest()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Install(); }
            catch (Exception e) { File.WriteAllText("Temp/structure-result.txt", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("TAC/Import structure into training pool")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before importing.");
            if (EditorSceneManager.GetActiveScene().path != TrainingProject.ScenePath)
                throw new InvalidOperationException("Open TrainingPool before importing the structure.");
            Directory.CreateDirectory(Folder + "Materials");
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.globalScale = 1;
            importer.useFileScale = false;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Folder + "structure-materials.json"));
            foreach (var entry in palette.materials)
            {
                string path = Folder + "Materials/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
                material.color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]).gamma;
                material.SetFloat("_Metallic", entry.metallic);
                material.SetFloat("_Smoothness", entry.smoothness);
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
            }
            importer.SaveAndReimport();
            Configure();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void Configure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (asset == null) return;
            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Import underwater structure");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.transform.SetParent(root.transform, false);
            var vehicle = UnityEngine.Object.FindFirstObjectByType<RovVehicle>();
            if (vehicle == null) throw new InvalidOperationException("No ROV scale reference found.");
            // Use the geometry actually displayed, including the placeholder when the ROV assets are absent.
            float rovWidth = BoundsOf(vehicle.gameObject).size.x;
            var visuals = vehicle.GetComponent<RovVisuals>();
            if (visuals != null && visuals.lowModel != null)
            {
                var reference = UnityEngine.Object.Instantiate(visuals.lowModel);
                rovWidth = BoundsOf(reference).size.x;
                UnityEngine.Object.DestroyImmediate(reference);
            }
            var floor = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).First(c => c.name == "Floor");
            Bounds basin = floor.bounds;
            Bounds source = BoundsOf(root);
            float targetWidth = Mathf.Min(rovWidth * 5f, basin.size.x * 0.25f);
            // Uniform scaling preserves valve and frame proportions, capped below the water surface.
            float scale = Mathf.Min(targetWidth / source.size.x, 2.8f / source.size.y);
            root.transform.localScale = Vector3.one * scale;
            Bounds scaled = BoundsOf(root);
            root.transform.position = new Vector3(2.8f - scaled.center.x, basin.max.y + 0.025f - scaled.min.y, 0.5f - scaled.center.z);
            foreach (var mesh in instance.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null || mesh.sharedMesh.vertexCount == 0) continue;
                // Static triangle colliders retain openings in the grating and frame.
                var collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
            }
            Directory.CreateDirectory("Assets/TacSim/Prefabs");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, "Assets/TacSim/Prefabs/UnderwaterStructure.prefab", InteractionMode.AutomatedAction);
            Bounds final = BoundsOf(root);
            if (final.min.x < basin.min.x || final.max.x > basin.max.x || final.min.z < basin.min.z || final.max.z > basin.max.z || final.max.y >= 0)
                throw new InvalidOperationException("Structure is outside pool bounds: " + final);
            string report = $"STRUCTURE_INSTALLED\nROV reference width: {rovWidth}\nUniform scale: {scale}\nBounds: {final}\nMeshes: {instance.GetComponentsInChildren<MeshFilter>().Length}\nColliders: {instance.GetComponentsInChildren<MeshCollider>().Length}\n";
            File.WriteAllText("Temp/structure-result.txt", report);
            Debug.Log(report);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(final, false);
            Capture(root, final);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No visible geometry in " + root.name);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void Capture(GameObject root, Bounds bounds)
        {
            var go = new GameObject("Structure verification camera");
            var camera = go.AddComponent<Camera>();
            camera.transform.position = bounds.center + new Vector3(4.5f, 2.4f, -6f);
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 60;
            camera.fieldOfView = 48;
            var texture = new RenderTexture(1280, 800, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                image.Apply();
                File.WriteAllBytes("Temp/structure-preview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(go);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
