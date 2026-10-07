using System.Collections.Generic;
using UnityEngine;

namespace TacSim
{
    // A printed Original-ArUco marker on a white backing plate. The marker face is the local +Y side.
    public sealed class ArucoMarker : MonoBehaviour
    {
        const float PlateThickness = 0.005f;
        static readonly Dictionary<int, Material> Faces = new();
        static Material backing;

        public int id;
        // Side length of the black marker square (7 × 7 cells including the black border), metres.
        public float size;

        public static ArucoMarker Create(Transform parent, int id, float size, Vector3 localPosition,
            Quaternion localRotation, float whiteMargin = 0.2f)
        {
            var root = new GameObject($"ArUco {id}");
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            var marker = root.AddComponent<ArucoMarker>();
            marker.id = id;
            marker.size = size;

            float plate = size * (1 + 2 * whiteMargin);
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "White backing";
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0, PlateThickness / 2, 0);
            back.transform.localScale = new Vector3(plate, PlateThickness, plate);
            back.GetComponent<Renderer>().sharedMaterial = Backing();

            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Marker face";
            Object.Destroy(face.GetComponent<Collider>());
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = new Vector3(0, PlateThickness + 0.0005f, 0);
            // Quad faces -Z; tip it so it faces +Y with texture top towards local +Z.
            face.transform.localRotation = Quaternion.Euler(90, 0, 0);
            face.transform.localScale = new Vector3(size, size, 1);
            face.GetComponent<Renderer>().sharedMaterial = Face(id);
            return marker;
        }

        static Material Backing()
        {
            if (backing == null) backing = TacMaterials.Lit("ArUco backing", Color.white, 0, 0.2f);
            return backing;
        }

        static Material Face(int id)
        {
            if (Faces.TryGetValue(id, out Material material) && material != null) return material;
            var texture = new Texture2D(7, 7, TextureFormat.RGB24, false)
            {
                name = $"ArUco {id}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
            {
                bool white = row is > 0 and < 6 && column is > 0 and < 6
                    && ArucoOriginal.IsWhite(id, row - 1, column - 1);
                // Texture rows start at the bottom; marker rows start at the top.
                texture.SetPixel(column, 6 - row, white ? Color.white : Color.black);
            }
            texture.Apply(false, true);
            material = TacMaterials.Lit($"ArUco {id}", Color.white, 0, 0.15f);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            Faces[id] = material;
            return material;
        }
    }
}
