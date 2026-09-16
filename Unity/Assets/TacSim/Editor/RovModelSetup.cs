using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    public static class RovModelSetup
    {
        const string Low = "Assets/External/ROV/malstrom-low.fbx";
        const string High = "Assets/External/ROV/malstrom-high.fbx";
        [Serializable] class Palette { public Entry[] materials; }
        [Serializable] class Entry { public string name; public float[] color; public float metallic; public float smoothness; }

        [MenuItem("TAC/Import Malstrom models")]
        public static void InstallModels()
        {
            ImportModels();
            EditorSceneManager.OpenScene(TrainingProject.ScenePath);
            Configure(UnityEngine.Object.FindFirstObjectByType<RovVehicle>().gameObject);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("TAC_MODELS_INSTALLED");
        }

        public static void ImportModels()
        {
            Directory.CreateDirectory("Assets/TacSim/Art/Materials/ROV");
            AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText("../assets/rov-materials.json"));
            foreach (string path in new[] { Low, High })
            {
                if (!File.Exists(path)) throw new Exception("Missing model: run ./assets.sh sync first: " + path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale = 1;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                importer.isReadable = false;
                foreach (var entry in palette.materials)
                {
                    string materialPath = $"Assets/TacSim/Art/Materials/ROV/{entry.name}.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    material.color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]).gamma;
                    material.SetFloat("_Metallic", entry.metallic);
                    material.SetFloat("_Smoothness", entry.smoothness);
                    EditorUtility.SetDirty(material);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
                }
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Bounds bounds = new Bounds();
                bool first = true;
                long triangles = 0;
                foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
                {
                    for (int i = 0; i < mesh.sharedMesh.subMeshCount; i++) triangles += (long)mesh.sharedMesh.GetIndexCount(i) / 3;
                }
                var instance = UnityEngine.Object.Instantiate(model);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                UnityEngine.Object.DestroyImmediate(instance);
                Debug.Log($"TAC_MODEL: {path}, triangles={triangles}, bounds={bounds}");
                if (Vector3.Distance(bounds.size, new Vector3(0.406063f, 0.32f, 0.515178f)) > 0.01f)
                    throw new Exception("Unexpected model scale/orientation: " + bounds);
            }
        }

        public static void Configure(GameObject rov)
        {
            var visuals = rov.GetComponent<RovVisuals>();
            if (visuals == null) visuals = rov.AddComponent<RovVisuals>();
            visuals.lowModel = AssetDatabase.LoadAssetAtPath<GameObject>(Low);
            visuals.highModel = AssetDatabase.LoadAssetAtPath<GameObject>(High);
            if (visuals.lowModel == null || visuals.highModel == null) throw new Exception("Import Malstrom models first");
            // A single centered box fits the true-scale hull, independent of detail selection.
            rov.GetComponent<BoxCollider>().size = new Vector3(0.406063f, 0.32f, 0.515178f);
            int index = 0;
            foreach (Light light in rov.GetComponentsInChildren<Light>())
                light.transform.localPosition = new Vector3(index++ == 0 ? -0.13f : 0.13f, 0.02f, 0.28f);
        }
    }
}
