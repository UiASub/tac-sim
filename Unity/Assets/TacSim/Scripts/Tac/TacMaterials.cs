using System.Collections.Generic;
using UnityEngine;

namespace TacSim
{
    // Runtime URP materials for procedurally generated TAC apparatus.
    public static class TacMaterials
    {
        static readonly Dictionary<string, Material> Cache = new();

        // TAC Mission Booklet 2026 colours.
        public static readonly Color PipelineYellow = new(1f, 0.82f, 0.05f);
        public static readonly Color StructureYellow = new Color32(228, 158, 0, 255); // RAL 1004
        public static readonly Color ValveOrange = new Color32(226, 83, 3, 255);      // RAL 2004

        public static Material Lit(string name, Color color, float metallic = 0, float smoothness = 0.35f)
        {
            if (Cache.TryGetValue(name, out Material material) && material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader) { name = name, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            Cache[name] = material;
            return material;
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position,
            Vector3 scale, Material material, bool collision = true)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) Object.Destroy(item.GetComponent<Collider>());
            return item;
        }

        // Cylinder of the given diameter between two local points.
        public static GameObject Tube(string name, Transform parent, Vector3 from, Vector3 to, float diameter,
            Material material, bool collision = true)
        {
            var tube = Primitive(PrimitiveType.Cylinder, name, parent, (from + to) / 2,
                new Vector3(diameter, Vector3.Distance(from, to) / 2, diameter), material, collision);
            tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            return tube;
        }
    }
}
