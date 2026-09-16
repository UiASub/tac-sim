using UnityEngine;

namespace TacSim
{
    // TextMesh's default font material draws over the scene and ignores fog.
    // Keep the depth-tested URP replacement bound to the dynamic font atlas.
    [RequireComponent(typeof(TextMesh))]
    public sealed class WorldLabel : MonoBehaviour
    {
        TextMesh text;
        Material material;
        void Awake()
        {
            text = GetComponent<TextMesh>();
            material = GetComponent<Renderer>().material;
            Refresh(text.font);
        }
        void OnEnable() => Font.textureRebuilt += Refresh;
        void Start() => Refresh(text.font);
        void OnDisable() => Font.textureRebuilt -= Refresh;
        void Refresh(Font font)
        {
            if (text != null && material != null && font == text.font)
                material.mainTexture = font.material.mainTexture;
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
