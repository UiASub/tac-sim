using System.Text;
using UnityEngine;

namespace TacSim
{
    // Water Linked DVL A50: four 1 MHz beams in a convex Janus array, 22.5° from the axis.
    // Each beam measures the transducer's velocity over the bottom along the beam; three or
    // four valid beams are solved for velocity by least squares. Reports use the A50 frame:
    // X forward, Y right, Z down. Beams lock onto any acoustic surface, including walls.
    public sealed class DopplerVelocityLog : RovSensor
    {
        [Header("Water Linked DVL A50 (Standard)")]
        public float beamAngle = 22.5f;
        public float minAltitude = 0.05f;
        public float maxAltitude = 50;
        [Tooltip("Ping rate adapts to altitude between these limits")]
        public float minPingRate = 4;
        public float maxPingRate = 15;
        public float maxVelocity = 3.75f;
        [Tooltip("Beyond this incidence angle the beam loses bottom lock")]
        public float maxIncidence = 70;

        [Header("Errors")]
        [Tooltip("Single-ping beam velocity noise, m/s")]
        public float beamNoise = 0.003f;
        [Tooltip("Additional beam noise as a fraction of beam velocity")]
        public float beamNoiseFraction = 0.004f;
        [Tooltip("Long-term accuracy ±1.01 % (Standard); drawn per unit as a 2σ scale error")]
        public float scaleAccuracy = 0.0101f;
        public float rangeNoise = 0.005f;
        [Tooltip("Heading drift of the internal AHRS used for dead reckoning, degrees RMS")]
        public float headingDrift = 2;

        public struct Beam
        {
            public int id;
            public bool valid;
            public float velocity;
            public float distance;
            public float rssi;
            public float nsd;
        }

        public readonly Beam[] Beams = new Beam[4];
        public Vector3 Velocity { get; private set; }
        public bool VelocityValid { get; private set; }
        public float Altitude { get; private set; } = -1;
        public float FigureOfMerit { get; private set; }
        public float PingRate { get; private set; }
        public float PingInterval { get; private set; }
        // Dead reckoning: NED metres since reset, its standard deviation and roll/pitch/yaw.
        public Vector3 DeadReckoning { get; private set; }
        public float DeadReckoningStd { get; private set; }
        public Vector3 Orientation { get; private set; }
        public int Status => VelocityValid ? 0 : 1;
        public override string Kind => "dvl";

        // Unity axes in the mount frame: 0 forward-right, 1 back-right, 2 back-left, 3 forward-left.
        readonly Vector3[] localBeams = new Vector3[4];
        readonly float[] hitDistance = new float[4];
        float scaleError;
        GaussMarkov headingError;
        double lastValidTime = -1;
        float variance;
        SensorBeams lines;

        void Awake()
        {
            float tilt = beamAngle * Mathf.Deg2Rad;
            for (int i = 0; i < 4; i++)
            {
                float azimuth = (45 + 90 * i) * Mathf.Deg2Rad;
                localBeams[i] = SensorFrames.FromFrd(new Vector3(Mathf.Cos(azimuth) * Mathf.Sin(tilt),
                    Mathf.Sin(azimuth) * Mathf.Sin(tilt), Mathf.Cos(tilt)));
                Beams[i].id = i;
            }
        }

        protected override void OnReseed()
        {
            scaleError = Noise.Gaussian(scaleAccuracy / 2);
            headingError.Reset(Noise, headingDrift);
        }

        public override void ResetState()
        {
            base.ResetState();
            DeadReckoning = Vector3.zero;
            variance = 0;
            DeadReckoningStd = 0;
            lastValidTime = -1;
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            PingRate = Altitude > 0
                ? Mathf.Lerp(maxPingRate, minPingRate, Mathf.InverseLerp(2, maxAltitude, Altitude))
                : minPingRate;
            double previous = SampleTime;
            if (!SampleDue(PingRate)) return;
            PingInterval = previous >= 0 ? (float)(SampleTime - previous) : 0;
            Ping();
        }

        void Ping()
        {
            Vector3 origin = MountPosition;
            Quaternion rotation = MountRotation;
            bool submerged = WaterDepth(origin) > 0.03f;
            Vector3 overGround = vehicle.Body.GetPointVelocity(origin);
            float cosTilt = Mathf.Cos(beamAngle * Mathf.Deg2Rad);
            Matrix4x4 normal = Matrix4x4.zero;
            Vector3 rhs = Vector3.zero;
            int valid = 0;
            float altitudeSum = 0, varianceSum = 0;

            for (int i = 0; i < 4; i++)
            {
                ref Beam beam = ref Beams[i];
                beam.valid = false;
                beam.velocity = 0;
                beam.distance = 0;
                beam.rssi = -110;
                beam.nsd = -95 + Gauss(1.5f);
                hitDistance[i] = -1;
                Vector3 direction = rotation * localBeams[i];
                if (!submerged || !Physics.Raycast(origin, direction, out RaycastHit hit, maxAltitude / cosTilt,
                        ~SensorSuite.VehicleMask, QueryTriggerInteraction.Ignore)) continue;
                hitDistance[i] = hit.distance;
                float incidence = Vector3.Angle(-direction, hit.normal);
                float echo = Mathf.Max(1e-3f, Mathf.Cos(incidence * Mathf.Deg2Rad));
                beam.rssi = -30 - 40 * Mathf.Log10(Mathf.Max(0.1f, hit.distance)) + 20 * Mathf.Log10(echo) + Gauss(1);
                if (hit.distance * cosTilt < minAltitude || incidence > maxIncidence) continue;

                float truth = Vector3.Dot(overGround, direction);
                float sigma = beamNoise + beamNoiseFraction * Mathf.Abs(truth);
                beam.velocity = truth * (1 + (noiseEnabled ? scaleError : 0)) + Gauss(sigma);
                beam.distance = hit.distance + Gauss(rangeNoise);
                beam.valid = true;
                Vector3 b = SensorFrames.ToFrd(localBeams[i]);
                for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    normal[row, column] += b[row] * b[column];
                rhs += b * beam.velocity;
                altitudeSum += beam.distance * cosTilt;
                varianceSum += sigma * sigma;
                valid++;
            }

            Altitude = valid > 0 ? altitudeSum / valid : -1;
            VelocityValid = false;
            if (valid >= 3)
            {
                normal[3, 3] = 1;
                Matrix4x4 inverse = normal.inverse;
                Vector3 velocity = inverse.MultiplyVector(rhs);
                if (velocity.magnitude <= maxVelocity)
                {
                    VelocityValid = true;
                    Velocity = velocity;
                    float sigma = Mathf.Sqrt(varianceSum / valid);
                    FigureOfMerit = sigma * Mathf.Sqrt(Mathf.Max(inverse.m00, Mathf.Max(inverse.m11, inverse.m22)));
                    DeadReckon(velocity);
                }
            }
            if (!VelocityValid)
            {
                Velocity = Vector3.zero;
                FigureOfMerit = 10;
            }
        }

        // The A50 integrates its own velocity with an internal gyro-based attitude.
        void DeadReckon(Vector3 velocity)
        {
            float heading = noiseEnabled ? headingError.value : 0;
            if (lastValidTime >= 0)
            {
                float dt = (float)(SampleTime - lastValidTime);
                if (noiseEnabled) heading = headingError.Step(Noise, headingDrift, 600, dt);
                if (dt < 1)
                {
                    Quaternion attitude = Quaternion.AngleAxis(heading, Vector3.up) * MountRotation;
                    Vector3 world = attitude * SensorFrames.FromFrd(velocity);
                    DeadReckoning += SensorFrames.ToFrd(world) * dt;
                    float error = FigureOfMerit + scaleAccuracy * world.magnitude;
                    variance += error * error * dt * dt;
                    DeadReckoningStd = Mathf.Sqrt(variance);
                }
            }
            lastValidTime = SampleTime;
            Vector3 ypr = SensorFrames.YawPitchRoll(Quaternion.AngleAxis(heading, Vector3.up) * MountRotation);
            Orientation = new Vector3(ypr.z, ypr.y, ypr.x);
        }

        void LateUpdate()
        {
            SensorSuite suite = SensorSuite.Instance;
            bool visible = suite != null && suite.BeamsVisible;
            lines ??= new SensorBeams(transform, "DVL beam", 4, 0.012f);
            lines.SetVisible(visible);
            if (!visible) return;
            Vector3 origin = transform.position;
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = transform.rotation * localBeams[i];
                if (hitDistance[i] > 0)
                    lines.Set(i, origin, origin + direction * hitDistance[i],
                        Beams[i].valid ? new Color(0.2f, 1, 0.75f, 0.9f) : new Color(1, 0.55f, 0.1f, 0.9f));
                else
                    lines.Set(i, origin, origin + direction * 1.5f, new Color(1, 0.2f, 0.15f, 0.5f));
            }
        }

        public override void AppendTelemetry(StringBuilder text)
        {
            text.AppendLine($"{SensorOverlay.Header("DVL")}  {model}");
            if (!HasSample)
            {
                text.AppendLine("  waiting for first ping");
                return;
            }
            string state = VelocityValid ? $"FOM {FigureOfMerit:0.000}" : "<color=#FF6A4D>NO LOCK</color>";
            text.AppendLine($"  vx {Velocity.x:+0.000;-0.000}  vy {Velocity.y:+0.000;-0.000}  vz {Velocity.z:+0.000;-0.000} m/s   {state}");
            text.Append($"  altitude {(Altitude >= 0 ? $"{Altitude:0.00} m" : "—")}   ping {PingRate:0.0} Hz   beams ");
            foreach (Beam beam in Beams)
                text.Append(beam.valid ? $"<color=#33FFBF>{beam.id}</color> " : $"<color=#FF6A4D>{beam.id}</color> ");
            text.AppendLine();
            text.AppendLine($"  DR  N {DeadReckoning.x:+0.00;-0.00}  E {DeadReckoning.y:+0.00;-0.00}  D {DeadReckoning.z:+0.00;-0.00} m   σ {DeadReckoningStd:0.00} m");
        }
    }
}
