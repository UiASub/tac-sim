using UnityEngine;

namespace TacSim
{
    public sealed class PilotView : MonoBehaviour
    {
        public RovVehicle vehicle;
        public Camera pilotCamera;
        public WaterAppearance appearance;
        public int Mode { get; private set; }
        static readonly string[] Modes = { "CHASE / TRAINING", "FORWARD CAMERA", "DOWNWARD CAMERA" };
        GUIStyle title, label, small;
        Texture2D panel;

        public void CycleCamera() => Mode = (Mode + 1) % Modes.Length;
        public void SetMode(int mode)
        {
            Mode = Mathf.Clamp(mode, 0, Modes.Length - 1);
            UpdateCamera();
        }

        void LateUpdate() => UpdateCamera();

        void UpdateCamera()
        {
            Transform rov = vehicle.transform;
            if (Mode == 0)
            {
                Quaternion yaw = Quaternion.Euler(0, rov.eulerAngles.y, 0);
                Vector3 target = rov.position + yaw * new Vector3(0, 1.1f, -3.2f);
                pilotCamera.transform.SetPositionAndRotation(target,
                    Quaternion.LookRotation(rov.position + rov.forward * 1.2f - target, Vector3.up));
            }
            else
            {
                Vector3 offset = Mode == 1 ? new Vector3(0, 0.06f, 0.68f) : new Vector3(0, -0.38f, 0.1f);
                pilotCamera.transform.SetPositionAndRotation(rov.TransformPoint(offset),
                    rov.rotation * (Mode == 1 ? Quaternion.identity : Quaternion.Euler(90, 0, 0)));
            }
        }

        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
                title.normal.textColor = new Color(0.95f, 0.72f, 0.22f);
                label = new GUIStyle(GUI.skin.label) { fontSize = 17 };
                label.normal.textColor = new Color(0.87f, 0.96f, 0.98f);
                small = new GUIStyle(label) { fontSize = 14 };
                panel = new Texture2D(1, 1);
                panel.SetPixel(0, 0, new Color(0.015f, 0.05f, 0.075f, 0.91f));
                panel.Apply();
            }
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.DrawTexture(new Rect(22, 22, 365, 150), panel);
            GUI.Label(new Rect(40, 32, 340, 40), "TAC / ROV TRAINING", title);
            GUI.Label(new Rect(40, 74, 340, 25), $"{Modes[Mode]}    •    {(vehicle.Armed ? "ARMED" : "THRUST CUT")}", small);
            GUI.Label(new Rect(40, 106, 340, 25), $"DEPTH  {vehicle.Depth:F2} m     SPEED  {vehicle.Body.linearVelocity.magnitude:F2} m/s", label);
            string contact = vehicle.IsColliding ? $"     CONTACT {vehicle.CollisionForce:F0} N" : "";
            GUI.Label(new Rect(40, 137, 340, 25), $"HEADING  {vehicle.transform.eulerAngles.y:000}°     THRUST  {vehicle.Throttle:P0}{contact}", small);
            GUI.DrawTexture(new Rect(22, height - 113, width - 44, 91), panel);
            GUI.Label(new Rect(40, height - 105, width - 80, 24), "PRACTICE  /  Approach the yellow landing pad. Use the downward camera to align.", label);
            GUI.Label(new Rect(40, height - 75, width - 80, 22), "WASD move   •   SPACE / CTRL depth   •   Q / E yaw   •   SHIFT precision   •   V camera   •   L lights", small);
            GUI.Label(new Rect(40, height - 51, width - 80, 22), "R reset   •   ESC thrust cut / arm   |   Gamepad: sticks move / turn, triggers depth, A camera, B arm, START reset", small);
            GUI.Label(new Rect(width / 2 - 6, height / 2 - 13, 25, 30), "+", label);
            DrawAppearanceMenu(width);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawAppearanceMenu(float width)
        {
            if (appearance == null) return;
            float x = width - 340;
            GUI.DrawTexture(new Rect(x, 22, 318, appearance.MenuOpen ? 365 : 76), panel);
            GUI.Label(new Rect(x + 16, 32, 220, 24), appearance.Name.ToUpperInvariant(), small);
            if (GUI.Button(new Rect(x + 218, 35, 85, 28), appearance.MenuOpen ? "Close [H]" : "Filters [H]"))
                appearance.MenuOpen = !appearance.MenuOpen;
            GUI.Label(new Rect(x + 16, 69, 290, 22), "F preset   •   P camera effects", small);
            if (!appearance.MenuOpen) return;
            GUI.Label(new Rect(x + 16, 97, 290, 24), "WATER CONDITIONS", small);
            for (int i = 0; i < WaterAppearance.PresetNames.Length; i++)
            {
                Color previous = GUI.backgroundColor;
                if (appearance.Preset == i) GUI.backgroundColor = new Color(0.3f, 0.85f, 0.85f);
                if (GUI.Button(new Rect(x + 16 + (i % 2) * 146, 127 + (i / 2) * 39, 139, 32), WaterAppearance.PresetNames[i]))
                    appearance.ApplyPreset(i);
                GUI.backgroundColor = previous;
            }
            GUI.Label(new Rect(x + 16, 211, 285, 23), $"Visibility  {appearance.Visibility:F0} m (approx.)", small);
            float visibility = GUI.HorizontalSlider(new Rect(x + 18, 241, 280, 18), appearance.Visibility, 3, 35);
            if (Mathf.Abs(visibility - appearance.Visibility) > 0.01f) appearance.SetVisibility(visibility);
            bool filters = GUI.Toggle(new Rect(x + 16, 263, 290, 25), appearance.FiltersEnabled, " Camera effects: colour, bloom, grain");
            if (filters != appearance.FiltersEnabled) appearance.SetFilters(filters);
            bool particles = GUI.Toggle(new Rect(x + 16, 292, 290, 25), appearance.ParticlesEnabled, " Suspended particles");
            if (particles != appearance.ParticlesEnabled) appearance.SetParticles(particles);
            bool caustics = GUI.Toggle(new Rect(x + 16, 321, 290, 25), appearance.CausticsEnabled, " Animated caustics (daylight presets)");
            if (caustics != appearance.CausticsEnabled) appearance.SetCaustics(caustics);
            GUI.Label(new Rect(x + 16, 354, 290, 22), "Does not change vehicle physics.", small);
        }

        void OnDestroy() { if (panel != null) Destroy(panel); }
    }
}
