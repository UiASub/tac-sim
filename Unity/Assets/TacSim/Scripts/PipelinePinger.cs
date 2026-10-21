using UnityEngine;

namespace TacSim
{
    // Idealized acoustic beacon. Frequency is signal metadata, not speaker audio.
    public sealed class PipelinePinger : MonoBehaviour
    {
        public float frequencyKhz = 30;
        public float intervalSeconds = 1;
        public float detectionRange = 150;
        public float soundSpeed = 1480;
        public float StartedAt { get; private set; }
        void OnEnable() => StartedAt = Time.time;
        public int ReceivedPulse(float now, float distance) => Mathf.FloorToInt(
            (now - StartedAt - distance / Mathf.Max(1, soundSpeed)) / Mathf.Max(.1f, intervalSeconds));
    }
}
