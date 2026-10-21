using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    public static class OceanMapResize
    {
        [MenuItem("TAC/Resize ocean to 100 x 100 m")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var roots = scene.GetRootGameObjects();
            var floor = roots.Single(g => g.name.StartsWith("Seabed"));
            Bounds bounds = floor.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (var renderer in floor.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            float ratio = 100 / bounds.size.x;
            if (Mathf.Abs(ratio - 1) > .001f)
            {
                floor.transform.localScale = Vector3.Scale(floor.transform.localScale, new Vector3(ratio,.3f,ratio));
                floor.name = "Seabed • 100 x 100 m • collidable tiles";
                var props = roots.Single(g => g.name == "Rock fields and kelp beds");
                int rock = 0, kelp = 0;
                foreach (Transform child in props.transform.Cast<Transform>().ToArray())
                {
                    bool isRock = child.name == "Glacial boulder";
                    int index = isRock ? rock++ : kelp++;
                    if (index % (isRock ? 10 : 6) != 0) { UnityEngine.Object.DestroyImmediate(child.gameObject); continue; }
                    child.position = Vector3.Scale(child.position,new Vector3(ratio,.3f,ratio));
                    child.localScale *= isRock ? .3f : .6f;
                }
            }
            var surface = roots.Single(g => g.name.StartsWith("Ocean surface"));
            surface.transform.localScale = new Vector3(10,1,10);
            var explorer = UnityEngine.Object.FindFirstObjectByType<OceanExplorer>();
            explorer.mapHalfExtent = 50;
            explorer.speed = 4;
            var vehicle = UnityEngine.Object.FindFirstObjectByType<RovVehicle>();
            Physics.SyncTransforms();
            Vector3 spawn = new Vector3(-11,-12,-13.5f);
            if (!Physics.Raycast(new Vector3(spawn.x,10,spawn.z),Vector3.down,out var hit,100)) throw new Exception("No seabed under ROV start.");
            spawn.y = hit.point.y + 3;
            vehicle.transform.position = spawn;
            vehicle.currentPoolSize = new Vector2(100,100);
            vehicle.currentPoolFloor = -33;
            explorer.transform.position = spawn + new Vector3(.65f,.45f,-.95f);
            explorer.transform.LookAt(spawn);
            explorer.GetComponent<PilotView>().missionHint = "OCEAN / 100 x 100 m. Explore the seabed. Suspended silt limits visibility.";
            if (!roots.Any(g => g.name == "Ocean map boundaries"))
            {
                var edges = new GameObject("Ocean map boundaries").transform;
                for(int i=0;i<4;i++)
                {
                    var edge = new GameObject("Map edge " + i); edge.transform.SetParent(edges);
                    bool x = i < 2; float sign = i % 2 == 0 ? -1 : 1;
                    edge.transform.position = x ? new Vector3(sign*50.5f,0,0) : new Vector3(0,0,sign*50.5f);
                    edge.AddComponent<BoxCollider>().size = x ? new Vector3(1,200,102) : new Vector3(102,200,1);
                }
            }
            Physics.SyncTransforms();
            bounds = floor.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach(var renderer in floor.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            if(Mathf.Abs(bounds.size.x-100)>.01f || Mathf.Abs(bounds.size.z-100)>.01f) throw new Exception("Incorrect ocean dimensions: " + bounds.size);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("OCEAN_RESIZED_OK: seabed 100 x 100 m, surface 100 x 100 m, ROV scale preserved, spawn above bottom, four boundary colliders.");
        }
    }
}
