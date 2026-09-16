using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TacSim
{
    // Opt-in standalone check. Normal play never takes this command path.
    public sealed class SimulationSmokeCheck : MonoBehaviour
    {
        public RovVehicle vehicle;
        public PilotInput input;
        public PilotView view;

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-smokeTest") < 0) yield break;
            input.ExternalControl = false;
            vehicle.ResetVehicle();
            input.SendMessage("OnApplicationFocus", false);
            if (!Check(!vehicle.Armed, "pilot focus loss cuts thrust")) yield break;
            input.ExternalControl = true;
            vehicle.ResetVehicle();
            input.SendMessage("OnApplicationFocus", false);
            if (!Check(vehicle.Armed, "external check ignores desktop focus")) yield break;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && !material.shader.isSupported)
                {
                    Check(false, "unsupported shader: " + material.shader.name);
                    yield break;
                }
            }
            vehicle.ResetVehicle();
            Vector3 start = vehicle.Body.position;
            yield return new WaitForSeconds(1);
            if (!Check(Vector3.Distance(vehicle.Body.position, start) < 0.1f, "neutral buoyancy")) yield break;
            vehicle.SetCommand(Vector3.forward, Vector3.zero);
            yield return new WaitForSeconds(2);
            if (!Check(vehicle.Body.position.z > start.z + 0.5f, "forward propulsion")) yield break;
            vehicle.ResetVehicle();
            vehicle.SetCommand(Vector3.up, Vector3.zero);
            yield return new WaitForSeconds(1);
            if (!Check(vehicle.Body.position.y > start.y + 0.2f, "vertical propulsion")) yield break;
            vehicle.ResetVehicle();
            vehicle.SetCommand(Vector3.zero, Vector3.up);
            yield return new WaitForSeconds(1);
            if (!Check(Quaternion.Angle(vehicle.Body.rotation, Quaternion.identity) > 5, "yaw torque")) yield break;
            vehicle.SetArmed(false);
            yield return new WaitForFixedUpdate();
            if (!Check(vehicle.Throttle == 0 && vehicle.TranslationCommand == Vector3.zero, "emergency cut")) yield break;
            vehicle.ResetVehicle();
            if (!Check(vehicle.Body.linearVelocity == Vector3.zero && vehicle.Body.angularVelocity == Vector3.zero
                && Vector3.Distance(vehicle.Body.position, start) < 0.001f, "reset")) yield break;
            yield return new WaitForSeconds(0.5f);
            if (!Check(Quaternion.Angle(vehicle.Body.rotation, Quaternion.identity) < 1
                && Vector3.Distance(vehicle.Body.position, start) < 0.05f, "reset remains stable")) yield break;
            view.CycleCamera();
            yield return null;
            if (!Check(view.Mode == 1, "camera switch")) yield break;
            view.CycleCamera();
            view.CycleCamera();
            if (view.appearance != null)
            {
                var appearance = view.appearance;
                Vector3 position = vehicle.Body.position;
                appearance.ApplyPreset(2);
                float density = RenderSettings.fogDensity;
                appearance.ApplyPreset(0);
                if (!Check(RenderSettings.fogDensity < density, "water visibility presets")) yield break;
                appearance.SetFilters(false);
                if (!Check(appearance.effects.weight == 0 && RenderSettings.fog, "camera filters independent of water")) yield break;
                appearance.SetParticles(false);
                if (!Check(!appearance.suspendedParticles.emission.enabled, "particle toggle")) yield break;
                appearance.SetCaustics(false);
                foreach (Renderer surface in appearance.causticSurfaces)
                    if (!Check(!surface.enabled, "caustics toggle")) yield break;
                appearance.SetVisibility(100);
                if (!Check(appearance.Visibility == 35 && vehicle.Body.position == position, "visual settings leave physics unchanged")) yield break;
                appearance.SetParticles(true);
                appearance.SetCaustics(true);
                appearance.SetFilters(true);
                appearance.ApplyPreset(0);

                int captures = Array.IndexOf(args, "-capturePresets");
                if (captures >= 0 && captures + 1 < args.Length)
                {
                    string folder = args[captures + 1];
                    Directory.CreateDirectory(folder);
                    for (int preset = 0; preset < WaterAppearance.PresetNames.Length; preset++)
                    {
                        appearance.ApplyPreset(preset);
                        yield return new WaitForSeconds(0.5f);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"preset-{preset}.png"));
                        yield return new WaitForSeconds(0.5f);
                    }
                    appearance.ApplyPreset(0);
                    appearance.MenuOpen = true;
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "settings.png"));
                    yield return new WaitForSeconds(0.5f);
                    appearance.MenuOpen = false;
                    appearance.SetFilters(false);
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "effects-off.png"));
                    yield return new WaitForSeconds(0.5f);
                    appearance.SetFilters(true);
                }
                float elapsed = 0;
                for (int frame = 0; frame < 120; frame++) { yield return null; elapsed += Time.unscaledDeltaTime; }
                Debug.Log($"TAC_RENDER_SAMPLE: {120 / elapsed:F1} fps over 120 frames at {Screen.width}x{Screen.height}");
            }
            int capture = Array.IndexOf(args, "-captureScreenshot");
            if (capture >= 0 && capture + 1 < args.Length)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(args[capture + 1]);
                yield return new WaitForSeconds(1);
            }
            Debug.Log("TAC_SMOKE_PASSED");
            Application.Quit(0);
        }

        static bool Check(bool passed, string condition)
        {
            if (passed) { Debug.Log($"TAC_CHECK_PASSED: {condition}"); return true; }
            Debug.LogError($"TAC_CHECK_FAILED: {condition}");
            Application.Quit(1);
            return false;
        }
    }
}
