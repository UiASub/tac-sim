using System.Text;
using UnityEngine;

namespace TacSim
{
    // Pilot-display telemetry for the sensor suite, drawn by PilotView in its scaled 1280×720 space.
    public static class SensorOverlay
    {
        public const float PanelWidth = 430;
        static readonly StringBuilder Text = new();
        static GUIStyle style;

        public static string Header(string kind) => $"<color=#F2B838><b>{kind}</b></color>";

        // Draws the sensor telemetry column on the left, between the status panel and the help bar.
        public static void DrawTelemetry(SensorSuite suite, GUIStyle baseStyle, Texture2D panel, float top, float bottom)
        {
            if (suite == null || !suite.TelemetryVisible) return;
            style ??= new GUIStyle(baseStyle) { fontSize = 13, richText = true, wordWrap = false };
            Text.Clear();
            for (int i = 0; i < suite.Sensors.Count; i++)
            {
                if (i > 0) Text.Append('\n');
                suite.Sensors[i].AppendTelemetry(Text);
            }
            if (!suite.NoiseEnabled) Text.Append("\n<i>Sensor noise disabled</i>");
            var content = new GUIContent(Text.ToString().TrimEnd());
            float height = Mathf.Min(style.CalcHeight(content, PanelWidth - 24) + 16, bottom - top);
            GUI.DrawTexture(new Rect(22, top, PanelWidth, height), panel);
            GUI.Label(new Rect(34, top + 8, PanelWidth - 24, height - 12), content, style);
        }

        // Sonar fan in the lower right, apex at the vehicle, with range rings labelled.
        public static void DrawSonar(SensorSuite suite, GUIStyle baseStyle, Texture2D panel, float width, float bottom)
        {
            if (suite == null || !suite.TelemetryVisible) return;
            ImagingSonar sonar = suite.Sonar;
            if (sonar == null || sonar.Fan == null) return;
            style ??= new GUIStyle(baseStyle) { fontSize = 13, richText = true, wordWrap = false };
            Texture2D fan = sonar.Fan;
            var rect = new Rect(width - 22 - (fan.width + 24), bottom - (fan.height + 46), fan.width + 24, fan.height + 46);
            GUI.DrawTexture(rect, panel);
            GUI.Label(new Rect(rect.x + 12, rect.y + 6, rect.width - 24, 20),
                $"{Header("SONAR")}  {sonar.FrequencyKhz} kHz   {sonar.HorizontalAperture:0}°   {sonar.range:0} m", style);
            var image = new Rect(rect.x + 12, rect.y + 32, fan.width, fan.height);
            GUI.DrawTexture(image, fan);
            for (float metres = sonar.RingSpacing; metres <= sonar.range + 0.01f; metres += sonar.RingSpacing)
            {
                float y = image.yMax - metres / sonar.range * fan.height;
                GUI.Label(new Rect(image.center.x + 4, y, 44, 18), $"{metres:0} m", style);
            }
        }

        // Letterboxed sensor video, the way a pilot station shows a camera feed.
        public static void DrawVideo(Texture frame, float width, float height)
        {
            if (frame == null || Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.blackTexture);
            GUI.DrawTexture(new Rect(0, 0, width, height), frame, ScaleMode.ScaleToFit, false);
        }
    }
}
