using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace TacSim
{
    // Diagnostic: puts the ROV in front of the wall valve, lines the fork up with the T-bar, pushes in with
    // the thrusters and turns the tool, all through the physics. Added by the editor in Play mode
    // (Temp/teknologidagen-tooltest.request); writes Temp/tooltest.txt and Temp/tooltest-*.png.
    public sealed class TeknologidagenToolTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var dive = GetComponent<TeknologidagenDive>();
            var vehicle = dive.vehicle;
            var arm = dive.Arm;
            var valve = dive.Valves[0];
            var pilot = vehicle.GetComponent<PilotInput>();
            var log = new StringBuilder();
            float drift = dive.drift;
            dive.drift = 0;
            if (pilot != null) pilot.ExternalControl = true;
            valve.SetAngle(0);

            // Square on to the valve, the tool tip 6 cm out from the bar, on the valve axis.
            Vector3 axis = valve.transform.up;
            Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(-axis, Vector3.up).normalized);
            Vector3 tip = arm.transform.localPosition + Vector3.forward * TacArm.Reach;
            Vector3 barTop = valve.transform.TransformPoint(0, TacValve.RimHeight, 0);
            Vector3 place = barTop + axis * .06f - facing * tip;
            vehicle.Body.position = place;
            vehicle.Body.rotation = facing;
            vehicle.transform.SetPositionAndRotation(place, facing);
            vehicle.Body.linearVelocity = Vector3.zero;
            vehicle.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            arm.SyncSocket();

            // Turn the fork slot (the wrist's local X at Wrist + 45°) parallel to the bar.
            Vector3 bar = Quaternion.Inverse(arm.transform.rotation) * valve.transform.Find("Handle").forward;
            float slot = Mathf.Atan2(bar.y, bar.x) * Mathf.Rad2Deg;
            float wrist = Mathf.Repeat(slot - 45 + 90, 180) - 90;
            log.AppendLine($"valve axis {axis:F2}, bar in tool frame {bar:F2}, wrist target {wrist:F1}°");
            arm.SetTargets(wrist / arm.WristRange, 0);
            yield return new WaitForSeconds(2.5f);
            log.AppendLine($"aligned: wrist {arm.Wrist:F1}°, socket twist {Twist(vehicle, arm):F1}°, tip gap {Gap(arm, barTop, axis):F3} m");
            Capture(dive, "aligned");

            // Push in gently with the thrusters, recording what the socket touches. The pilot input cuts the
            // thrusters whenever the editor loses focus, so re-arm on every frame of the test.
            var contacts = arm.Socket.gameObject.AddComponent<ContactLog>();
            contacts.frame = valve.transform;
            for (float t = 0; t < 3; t += Time.deltaTime)
            {
                if (!vehicle.Armed) vehicle.SetArmed(true);
                vehicle.SetCommand(new Vector3(0, 0, .25f), Vector3.zero);
                yield return null;
            }
            Vector3 offset = valve.transform.InverseTransformPoint(arm.Socket.TransformPoint(0, 0, .07f));
            log.AppendLine($"pushed: tip gap {Gap(arm, barTop, axis):F3} m, tip in valve frame {offset:F4}, contact {valve.ToolContact}, hull contact {vehicle.IsColliding}");
            log.Append(contacts.Report());
            Destroy(contacts);
            Capture(dive, "engaged");

            // Turn one way, then the other, still leaning on the valve.
            foreach (float direction in new[] { 1f, -1f })
            {
                float start = valve.angle, nearest = float.MaxValue, farthest = float.MinValue, peak = 0;
                for (float t = 0; t < 3; t += Time.deltaTime)
                {
                    if (!vehicle.Armed) vehicle.SetArmed(true);
                    vehicle.SetCommand(new Vector3(0, 0, .15f), Vector3.zero);
                    arm.NudgeWrist(direction, Time.deltaTime);
                    yield return null;
                    // Bumping shows as the hull surging along the valve axis while the tool turns.
                    float along = Vector3.Dot(vehicle.Body.position, axis);
                    nearest = Mathf.Min(nearest, along);
                    farthest = Mathf.Max(farthest, along);
                    peak = Mathf.Max(peak, vehicle.Body.linearVelocity.magnitude);
                }
                log.AppendLine($"turn {direction:+0;-0}: valve {start:F1}° → {valve.angle:F1}°, wrist {arm.Wrist:F1}°, socket twist {Twist(vehicle, arm):F1}°, contact {valve.ToolContact}");
                log.AppendLine($"  hull surge {(farthest - nearest) * 1000:F1} mm, peak speed {peak * 1000:F0} mm/s");
                if (direction > 0) Capture(dive, "turned");
            }
            log.AppendLine($"completed {valve.Completed}, turned by tool {valve.TurnedByTool:F1}°");

            vehicle.SetCommand(Vector3.zero, Vector3.zero);
            if (pilot != null) pilot.ExternalControl = false;
            dive.drift = drift;
            File.WriteAllText("Temp/tooltest.txt", log.ToString());
            Destroy(this);
        }

        // First contacts of each collider pair, with the contact point in the valve frame.
        sealed class ContactLog : MonoBehaviour
        {
            public Transform frame;
            readonly System.Collections.Generic.Dictionary<string, string> seen = new();
            void OnCollisionStay(Collision collision)
            {
                foreach (var contact in collision.contacts)
                {
                    var box = contact.thisCollider as BoxCollider;
                    string key = $"socket box {(box != null ? box.center.ToString("F3") : contact.thisCollider.name)} ↔ {collision.collider.name}";
                    if (!seen.ContainsKey(key))
                        seen[key] = $"point {frame.InverseTransformPoint(contact.point):F4}, normal {frame.InverseTransformDirection(contact.normal):F2}, separation {contact.separation:F4}";
                }
            }
            public string Report()
            {
                var text = new StringBuilder();
                foreach (var pair in seen) text.AppendLine($"  {pair.Key}: {pair.Value}");
                return seen.Count == 0 ? "  no socket contacts\n" : text.ToString();
            }
        }

        static float Twist(RovVehicle vehicle, TacArm arm)
        {
            Vector3 right = Quaternion.Inverse(vehicle.transform.rotation) * arm.Socket.right;
            return Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        }

        static float Gap(TacArm arm, Vector3 barTop, Vector3 axis) =>
            Vector3.Dot(arm.Socket.TransformPoint(0, 0, .07f) - barTop, axis);

        static void Capture(TeknologidagenDive dive, string name)
        {
            var camera = dive.diveCamera;
            var target = new RenderTexture(1000, 625, 24);
            var image = new Texture2D(1000, 625, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var old = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1000, 625), 0, 0);
                image.Apply();
                File.WriteAllBytes($"Temp/tooltest-{name}.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = old;
                RenderTexture.active = previous;
                target.Release();
                Destroy(target);
                Destroy(image);
            }
        }
    }
}
