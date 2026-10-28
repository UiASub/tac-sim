using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TacSim.Editor
{
    // Diagnostic stills from Play mode: the wall valve, an eelgrass meadow and the structure, in murky fjord
    // and coastal water. Triggered by Temp/teknologidagen-capture.request; writes Temp/capture-*.png.
    // Temp/teknologidagen-tooltest.request starts the physical torque-tool test (TeknologidagenToolTest).
    public static class TeknologidagenCapture
    {
        const string Request = "Temp/teknologidagen-capture.request";
        const string ToolTest = "Temp/teknologidagen-tooltest.request";
        static readonly Queue<Action> Steps = new();
        static int wait;

        [InitializeOnLoadMethod] static void Init() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

        static void Poll()
        {
            if (Steps.Count > 0)
            {
                // Give the dive script a few frames to apply a water preset before rendering.
                if (wait-- <= 0) { Steps.Dequeue()(); wait = 5; }
                return;
            }
            if (File.Exists(ToolTest) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
            {
                File.Delete(ToolTest);
                Object.FindFirstObjectByType<TeknologidagenDive>().gameObject.AddComponent<TeknologidagenToolTest>();
                return;
            }
            if (!File.Exists(Request) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            File.Delete(Request);
            var dive = Object.FindFirstObjectByType<TeknologidagenDive>();
            int original = dive.waterPreset;
            var valve = dive.Valves[0];
            var meadow = dive.transform.Find("Environment/Eelgrass meadows").GetComponentsInChildren<Renderer>()
                .OrderByDescending(r => r.bounds.size.x).First().bounds;
            var structure = TeknologidagenDive.BoundsOf(dive.structure);
            var shots = new (string name, Vector3 from, Vector3 at)[]
            {
                ("valve", valve.transform.position + valve.transform.up * 1.1f + Vector3.up * .15f, valve.transform.position),
                ("meadow", meadow.center + new Vector3(0, 1f, -1.6f), meadow.center),
                ("structure", structure.center + new Vector3(1.6f, .9f, -2.2f), structure.center),
            };
            foreach (int preset in new[] { 0, 1 })
            {
                Steps.Enqueue(() => dive.waterPreset = preset);
                foreach (var shot in shots) Steps.Enqueue(() => Shoot(dive, shot.name + "-" + preset, shot.from, shot.at));
            }
            Steps.Enqueue(() => { dive.waterPreset = original; File.WriteAllText("Temp/teknologidagen-capture.txt", "CAPTURED"); });
            wait = 5;
        }

        static void Shoot(TeknologidagenDive dive, string name, Vector3 from, Vector3 at)
        {
            var go = new GameObject("Capture camera");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(dive.diveCamera);
            camera.cullingMask = ~0;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            var target = new RenderTexture(1000, 625, 24);
            var image = new Texture2D(1000, 625, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1000, 625), 0, 0);
                image.Apply();
                File.WriteAllBytes($"Temp/capture-{name}.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(go);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
