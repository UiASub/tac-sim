using System;
using System.Text;
using UnityEngine;

namespace TacSim
{
    // A simulated payload sensor mounted on the ROV. Its Transform is the mounting pose,
    // a direct child of the vehicle. Sampling uses the physics pose, not the interpolated one.
    public abstract class RovSensor : MonoBehaviour
    {
        public RovVehicle vehicle;
        [Tooltip("Product name shown in telemetry")]
        public string model;
        public bool noiseEnabled = true;

        public abstract string Kind { get; }
        public double SampleTime { get; protected set; } = -1;
        public bool HasSample => SampleTime >= 0;
        protected SensorNoise Noise { get; private set; }
        double nextSample;

        public void Initialise(RovVehicle owner, int seed)
        {
            vehicle = owner;
            Noise = new SensorNoise(seed);
            OnReseed();
        }

        public void Reseed(int seed)
        {
            Noise.Reseed(seed);
            OnReseed();
        }

        // Draw per-unit errors such as turn-on biases and scale factors.
        protected virtual void OnReseed() { }

        // Called after the vehicle resets: clears integrators and filters, keeps unit errors.
        public virtual void ResetState()
        {
            SampleTime = -1;
            nextSample = 0;
        }

        public abstract void AppendTelemetry(StringBuilder text);

        protected float Gauss(float sigma) => noiseEnabled ? Noise.Gaussian(sigma) : 0;
        protected Vector3 Gauss3(float sigma) => noiseEnabled ? Noise.Gaussian3(sigma) : Vector3.zero;

        // Fixed-step scheduler: averages the requested rate even when it does not divide 50 Hz.
        protected bool SampleDue(float rateHz)
        {
            double now = Time.fixedTimeAsDouble;
            if (now + 1e-6 < nextSample) return false;
            double period = 1.0 / Math.Max(0.01f, rateHz);
            nextSample = nextSample + period < now ? now + period : nextSample + period;
            SampleTime = now;
            return true;
        }

        protected Vector3 MountPosition => vehicle.Body.position + vehicle.Body.rotation * transform.localPosition;
        protected Quaternion MountRotation => vehicle.Body.rotation * transform.localRotation;
        protected float WaterDepth(Vector3 point) => vehicle.waterSurface - point.y;
    }

    // Unity is left-handed (X right, Y up, Z forward). Marine sensors report right-handed
    // body FRD (X forward, Y right, Z down) and world NED with pool north along Unity +Z.
    public static class SensorFrames
    {
        public static Vector3 ToFrd(Vector3 unity) => new(unity.z, unity.x, -unity.y);
        public static Vector3 FromFrd(Vector3 frd) => new(frd.y, -frd.z, frd.x);
        // Angular rates are axial vectors, so they also change sign with the handedness flip.
        public static Vector3 RateToFrd(Vector3 unity) => -ToFrd(unity);

        // Heading clockwise from north, pitch nose-up and roll starboard-down, in degrees.
        public static Vector3 YawPitchRoll(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            // atan2 stays accurate near vertical, where asin loses float precision.
            float pitch = Mathf.Atan2(forward.y, new Vector2(forward.x, forward.z).magnitude) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
            if (Mathf.Abs(forward.y) > 0.9999f)
            {
                // Looking straight down or up: put all heading into yaw, taken from the up vector.
                float sign = Mathf.Sign(forward.y);
                yaw = Mathf.Atan2(sign * -up.x, sign * -up.z) * Mathf.Rad2Deg;
                roll = 0;
            }
            return new Vector3(Mathf.Repeat(yaw, 360), pitch, roll);
        }

        // Body-to-NED attitude quaternion (x, y, z, w) from a 3-2-1 yaw, pitch, roll sequence.
        public static Vector4 NedQuaternion(Vector3 yawPitchRoll)
        {
            Vector3 half = yawPitchRoll * (Mathf.Deg2Rad / 2);
            float cy = Mathf.Cos(half.x), sy = Mathf.Sin(half.x);
            float cp = Mathf.Cos(half.y), sp = Mathf.Sin(half.y);
            float cr = Mathf.Cos(half.z), sr = Mathf.Sin(half.z);
            return new Vector4(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy);
        }
    }
}
