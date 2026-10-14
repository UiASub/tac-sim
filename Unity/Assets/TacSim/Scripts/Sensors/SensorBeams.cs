using UnityEngine;
using UnityEngine.Rendering;

namespace TacSim
{
    // In-scene beam lines for acoustic sensors. They live on the vehicle layer, so the
    // simulated cameras never see them; only the pilot's chase view does.
    public sealed class SensorBeams
    {
        static Material material;
        readonly LineRenderer[] lines;

        public SensorBeams(Transform parent, string name, int count, float width)
        {
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("SensorBeam");
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            lines = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var line = new GameObject($"{name} {i}") { hideFlags = HideFlags.DontSave, layer = SensorSuite.VehicleLayer };
                line.transform.SetParent(parent, false);
                lines[i] = line.AddComponent<LineRenderer>();
                lines[i].sharedMaterial = material;
                lines[i].useWorldSpace = true;
                lines[i].widthMultiplier = width;
                lines[i].shadowCastingMode = ShadowCastingMode.Off;
                lines[i].receiveShadows = false;
                lines[i].positionCount = 0;
            }
        }

        public void SetVisible(bool visible)
        {
            foreach (LineRenderer line in lines) line.enabled = visible;
        }

        public void Set(int index, Vector3 from, Vector3 to, Color color)
        {
            LineRenderer line = lines[index];
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, color.a * 0.35f);
        }

        public void SetPath(int index, Vector3[] points, int count, Color color)
        {
            LineRenderer line = lines[index];
            line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, points[i]);
            line.startColor = line.endColor = color;
        }

        public void Clear(int index) => lines[index].positionCount = 0;
    }
}
