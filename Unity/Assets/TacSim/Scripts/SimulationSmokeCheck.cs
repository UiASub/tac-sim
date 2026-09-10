using System;
using System.Collections;
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
            input.ExternalControl = true;
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
