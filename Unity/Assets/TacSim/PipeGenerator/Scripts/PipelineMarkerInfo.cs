// Assets/TAC/Pipeline/Scripts/PipelineMarkerInfo.cs
using UnityEngine;

namespace TAC.Pipeline
{
    /// Ground-truth component attached to every generated marker (detection/score verification).
    public class PipelineMarkerInfo : MonoBehaviour
    {
        public int markerId;
        public int indexFromPinger;
        public float arcLength;
        public int bits;
        public ulong code;
        public float yawDegrees;
        public Vector3 localPositionOnPath;
        public Texture2D cardTexture;
    }
}
