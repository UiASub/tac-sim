using System.Text;
using UnityEngine;

namespace TacSim
{
    // VectorNav VN-100 IMU/AHRS. Raw outputs are body FRD at the mount: specific force (an
    // accelerometer at rest reads Z ≈ -9.81 m/s²), angular rate, and the magnetic field. The
    // AHRS attitude is the true attitude with datasheet-sized heading and tilt errors; sustained
    // acceleration leaks into pitch and roll the way it does in a gravity-referenced filter.
    public sealed class InertialSensor : RovSensor
    {
        [Tooltip("Output rate; limited to the 50 Hz physics step")]
        public float rate = 50;

        [Header("VN-100 datasheet")]
        [Tooltip("Accelerometer noise density, mg/√Hz")]
        public float accelNoiseDensity = 0.14f;
        [Tooltip("Accelerometer in-run bias stability, mg")]
        public float accelBiasStability = 0.04f;
        [Tooltip("Gyro noise density, °/s/√Hz")]
        public float gyroNoiseDensity = 0.0035f;
        [Tooltip("Gyro in-run bias stability, °/h")]
        public float gyroBiasStability = 5;
        [Tooltip("Magnetometer noise density, Gauss/√Hz")]
        public float magNoiseDensity = 140e-6f;
        [Tooltip("Magnetic heading accuracy, ° RMS")]
        public float headingAccuracy = 2;
        [Tooltip("Static pitch/roll accuracy, ° RMS")]
        public float tiltAccuracy = 0.5f;
        [Tooltip("Fraction of a sustained horizontal acceleration mistaken for tilt")]
        public float accelerationTiltCoupling = 0.3f;

        [Header("Environment")]
        [Tooltip("Earth field in NED, Gauss (approx. southern Norway, 73° inclination)")]
        public Vector3 earthField = new(0.149f, 0.008f, 0.490f);
        public float enclosureTemperature = 35;
        public float enclosurePressure = 101.3f;

        public Vector3 Acceleration { get; private set; }
        public Vector3 AngularRate { get; private set; }
        public Vector3 MagneticField { get; private set; }
        // Yaw (-180..180, clockwise from pool north), pitch nose-up, roll starboard-down, degrees.
        public Vector3 YawPitchRoll { get; private set; }
        public Vector4 AttitudeQuaternion { get; private set; }
        public float Temperature { get; private set; }
        public float EnclosurePressure { get; private set; }
        public override string Kind => "imu";

        const float G = 9.80665f;
        Vector3 accelBias, gyroBias, hardIron;
        GaussMarkov accelDriftX, accelDriftY, accelDriftZ, gyroDriftX, gyroDriftY, gyroDriftZ;
        GaussMarkov yawError, pitchError, rollError;
        float yawOffset;
        Vector3 previousVelocity;
        bool hasPrevious;
        Vector2 sustainedAcceleration;

        protected override void OnReseed()
        {
            // Turn-on errors left after factory calibration; in-run terms wander around them.
            accelBias = Noise.Gaussian3(0.5e-3f * G);
            gyroBias = Noise.Gaussian3(0.02f * Mathf.Deg2Rad);
            hardIron = Noise.Gaussian3(0.01f);
            yawOffset = Noise.Gaussian(headingAccuracy * 0.6f);
            yawError.Reset(Noise, headingAccuracy * 0.8f);
            pitchError.Reset(Noise, tiltAccuracy);
            rollError.Reset(Noise, tiltAccuracy);
        }

        public override void ResetState()
        {
            base.ResetState();
            hasPrevious = false;
            sustainedAcceleration = Vector2.zero;
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            double previous = SampleTime;
            if (!SampleDue(rate)) return;
            float dt = previous >= 0 ? (float)(SampleTime - previous) : Time.fixedDeltaTime;
            Vector3 position = MountPosition;
            Quaternion rotation = MountRotation;
            Quaternion toBody = Quaternion.Inverse(rotation);

            Vector3 velocity = vehicle.Body.GetPointVelocity(position);
            Vector3 acceleration = hasPrevious ? (velocity - previousVelocity) / dt : Vector3.zero;
            previousVelocity = velocity;
            hasPrevious = true;
            Vector3 specificForce = SensorFrames.ToFrd(toBody * (acceleration - Physics.gravity));
            Vector3 gyro = SensorFrames.RateToFrd(toBody * vehicle.Body.angularVelocity);
            Vector3 field = SensorFrames.ToFrd(toBody * SensorFrames.FromFrd(earthField));

            float accelSigma = SensorNoise.DensityToSigma(accelNoiseDensity * 1e-3f * G, rate);
            float gyroSigma = SensorNoise.DensityToSigma(gyroNoiseDensity * Mathf.Deg2Rad, rate);
            float magSigma = SensorNoise.DensityToSigma(magNoiseDensity, rate);
            float accelDrift = accelBiasStability * 1e-3f * G;
            float gyroDrift = gyroBiasStability / 3600 * Mathf.Deg2Rad;
            if (noiseEnabled)
            {
                Vector3 accelWander = new(accelDriftX.Step(Noise, accelDrift, 100, dt),
                    accelDriftY.Step(Noise, accelDrift, 100, dt), accelDriftZ.Step(Noise, accelDrift, 100, dt));
                Vector3 gyroWander = new(gyroDriftX.Step(Noise, gyroDrift, 100, dt),
                    gyroDriftY.Step(Noise, gyroDrift, 100, dt), gyroDriftZ.Step(Noise, gyroDrift, 100, dt));
                specificForce += accelBias + accelWander + Noise.Gaussian3(accelSigma);
                gyro += gyroBias + gyroWander + Noise.Gaussian3(gyroSigma);
                field += hardIron + Noise.Gaussian3(magSigma);
            }
            Acceleration = specificForce;
            AngularRate = gyro;
            MagneticField = field;

            // Body-frame horizontal acceleration, low-passed like the AHRS gravity reference.
            Vector3 linear = SensorFrames.ToFrd(toBody * acceleration);
            sustainedAcceleration = Vector2.Lerp(sustainedAcceleration, new Vector2(linear.x, linear.y),
                1 - Mathf.Exp(-dt / 2));
            Vector3 attitude = SensorFrames.YawPitchRoll(rotation);
            if (noiseEnabled)
            {
                attitude.x += yawOffset + yawError.Step(Noise, headingAccuracy * 0.8f, 120, dt);
                attitude.y += pitchError.Step(Noise, tiltAccuracy, 30, dt)
                    + accelerationTiltCoupling * Mathf.Atan2(sustainedAcceleration.x, G) * Mathf.Rad2Deg;
                attitude.z += rollError.Step(Noise, tiltAccuracy, 30, dt)
                    - accelerationTiltCoupling * Mathf.Atan2(sustainedAcceleration.y, G) * Mathf.Rad2Deg;
            }
            attitude.x = Mathf.DeltaAngle(0, attitude.x);
            YawPitchRoll = attitude;
            AttitudeQuaternion = SensorFrames.NedQuaternion(attitude);
            Temperature = enclosureTemperature + (noiseEnabled ? Noise.Gaussian(0.05f) : 0);
            EnclosurePressure = enclosurePressure + (noiseEnabled ? Noise.Gaussian(0.01f) : 0);
        }

        public override void AppendTelemetry(StringBuilder text)
        {
            text.AppendLine($"{SensorOverlay.Header("IMU")}  {model}");
            if (!HasSample)
            {
                text.AppendLine("  waiting for first sample");
                return;
            }
            Vector3 ypr = YawPitchRoll;
            Vector3 degrees = AngularRate * Mathf.Rad2Deg;
            text.AppendLine($"  yaw {ypr.x:+000.0;-000.0}°  pitch {ypr.y:+0.0;-0.0}°  roll {ypr.z:+0.0;-0.0}°   {rate:0} Hz");
            text.AppendLine($"  gyro {degrees.x:+0.00;-0.00} {degrees.y:+0.00;-0.00} {degrees.z:+0.00;-0.00} °/s   "
                + $"acc {Acceleration.x:+0.00;-0.00} {Acceleration.y:+0.00;-0.00} {Acceleration.z:+0.00;-0.00}");
            text.AppendLine($"  mag {MagneticField.x:+0.000;-0.000} {MagneticField.y:+0.000;-0.000} {MagneticField.z:+0.000;-0.000} G   {Temperature:0.0} °C");
        }
    }
}
