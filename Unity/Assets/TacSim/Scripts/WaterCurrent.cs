using UnityEngine;

namespace TacSim
{
    // Gentle recirculation in the training basin, in world-space metres/second.
    // A horizontal stream function keeps the flow divergence-free and tangent to walls.
    public static class WaterCurrent
    {
        public static Vector3 Sample(Vector3 position, float time, float speed,
            Vector2 poolSize, float floor, float surface)
        {
            if (speed <= 0 || poolSize.x <= 0 || poolSize.y <= 0 || surface <= floor
                || position.y >= surface || position.y <= floor
                || Mathf.Abs(position.x) > poolSize.x * .5f || Mathf.Abs(position.z) > poolSize.y * .5f)
                return Vector3.zero;
            float kx = Mathf.PI / poolSize.x, kz = Mathf.PI / poolSize.y;
            float x = kx * position.x, z = kz * position.z;
            float pulse = .85f + .15f * Mathf.Sin(time * .23f);
            float eddy = .12f * Mathf.Sin(time * .37f + .7f);
            // Two smooth circulation cells; both have zero velocity through each wall.
            float vx = -Mathf.Cos(x) * Mathf.Sin(z) * pulse
                + 2 * eddy * Mathf.Sin(2*x) * Mathf.Cos(2*z);
            float vz = Mathf.Sin(x) * Mathf.Cos(z) * pulse
                - 2 * eddy * Mathf.Cos(2*x) * Mathf.Sin(2*z);
            float bottomFade = Mathf.SmoothStep(0, 1, (position.y - floor) / .8f);
            return new Vector3(vx * kz / kx, 0, vz) * (speed * bottomFade);
        }
    }
}
