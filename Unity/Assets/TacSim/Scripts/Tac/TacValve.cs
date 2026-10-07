using UnityEngine;

namespace TacSim
{
    // Standard subsea valve replica: orange bucket with a handle limited to a 90° sector between S and O.
    // The bucket opening faces local +Y. There is no manipulator yet, so the handle is set by code.
    public sealed class TacValve : MonoBehaviour
    {
        // 0° = S (shut), 90° = O (open). The start position may be anywhere in between.
        [Range(0, 90)] public float angle;
        public string valveName;
        Transform handle;

        public bool IsOpen => angle >= 85;
        public bool IsShut => angle <= 5;

        public static TacValve Create(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, float angle)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            var valve = root.AddComponent<TacValve>();
            valve.valveName = name;
            Material orange = TacMaterials.Lit("TAC valve", TacMaterials.ValveOrange, 0, 0.4f);
            Material dark = TacMaterials.Lit("TAC valve bucket inside", TacMaterials.ValveOrange * 0.35f, 0, 0.2f);
            Material white = TacMaterials.Lit("TAC valve marking", Color.white, 0, 0.3f);

            // Booklet: outer radius 120 mm, inner radius 68 mm, bucket depth 85 mm, handle 65 × 25 mm.
            TacMaterials.Primitive(PrimitiveType.Cylinder, "Bucket", root.transform, new Vector3(0, 0.0425f, 0),
                new Vector3(0.24f, 0.0425f, 0.24f), orange);
            TacMaterials.Primitive(PrimitiveType.Cylinder, "Bucket opening", root.transform, new Vector3(0, 0.0855f, 0),
                new Vector3(0.136f, 0.001f, 0.136f), dark, false);
            for (int i = 0; i < 2; i++)
            {
                float a = i * 90 * Mathf.Deg2Rad;
                TacMaterials.Primitive(PrimitiveType.Cube, i == 0 ? "S mark" : "O mark", root.transform,
                    new Vector3(Mathf.Sin(a) * 0.1f, 0.086f, Mathf.Cos(a) * 0.1f), new Vector3(0.02f, 0.002f, 0.02f), white, false);
            }
            valve.handle = new GameObject("Handle").transform;
            valve.handle.SetParent(root.transform, false);
            valve.handle.localPosition = new Vector3(0, 0.02f, 0);
            TacMaterials.Primitive(PrimitiveType.Cube, "Handle bar", valve.handle, new Vector3(0, 0.0325f, 0),
                new Vector3(0.025f, 0.065f, 0.12f), orange);
            TacMaterials.Primitive(PrimitiveType.Cube, "Arrow", valve.handle, new Vector3(0, 0.066f, 0.045f),
                new Vector3(0.012f, 0.002f, 0.03f), white, false);
            valve.SetAngle(angle);
            return valve;
        }

        public void SetAngle(float degrees)
        {
            angle = Mathf.Clamp(degrees, 0, 90);
            if (handle != null) handle.localRotation = Quaternion.Euler(0, angle, 0);
        }

        void OnValidate() => SetAngle(angle);
    }
}
