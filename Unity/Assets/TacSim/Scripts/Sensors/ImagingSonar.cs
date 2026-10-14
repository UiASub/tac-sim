using System;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace TacSim
{
    // Sonoptix ECHO multibeam imaging sonar. Each of the 256 beams is sampled by a column of
    // rays across the 20° vertical aperture. Returns are weighted by the vertical beam pattern
    // and incidence angle, smeared in range between neighbouring rays on the same surface and
    // across beams to the angular resolution, then given speckle and a noise floor.
    // The image is range bins (rows, nearest first) × beams (columns, port to starboard).
    public sealed class ImagingSonar : RovSensor
    {
        public enum Band { High700kHz, Low400kHz }

        [Header("Sonoptix ECHO")]
        public Band band = Band.High700kHz;
        public int beamCount = 256;
        public float verticalAperture = 20;
        public float range = 15;
        public float minRange = 0.2f;
        [Tooltip("Range bins in the simulated image; the hardware resolves 8 mm")]
        public int rangeBins = 512;
        [Tooltip("Ping rate, limited to 25 Hz and the two-way travel time")]
        public float pingRate = 15;
        [Tooltip("Rays across the vertical aperture per beam")]
        public int verticalRays = 20;

        [Header("Image")]
        public float gain = 10;
        public float noiseFloor = 0.004f;
        public float soundSpeed = 1480;

        public float HorizontalAperture => band == Band.High700kHz ? 120 : 90;
        public float MaxRange => band == Band.High700kHz ? 30 : 200;
        public float AngularResolution => band == Band.High700kHz ? 1.5f : 2.5f;
        public int FrequencyKhz => band == Band.High700kHz ? 700 : 400;
        public float EffectivePingRate => Mathf.Min(pingRate, 25, soundSpeed / (2 * range));
        public byte[] Image { get; private set; }
        // First return per beam in metres (0 when nothing within range): a compact scan for policies.
        public float[] BeamRanges { get; private set; }
        public Texture2D Fan { get; private set; }
        public override string Kind => "sonar";

        const int FanRadius = 210;
        Vector3[] localRays;
        float[] rayWeights;
        float[] intensity;
        float[] smeared;
        float[] kernel;
        NativeArray<RaycastCommand> commands;
        NativeArray<RaycastHit> results;
        int[] fanLookup;
        float[] fanRadius;
        Color32[] fanPixels;
        readonly Color32[] palette = new Color32[256];
        string configuration;
        uint random = 1;
        readonly Vector3[] footprint = new Vector3[65];
        int footprintCount;
        SensorBeams lines;

        void Awake()
        {
            for (int i = 0; i < 256; i++)
            {
                // Black through amber to white, the usual imaging-sonar palette.
                float t = i / 255f;
                Color color = t < 0.5f ? Color.Lerp(new Color(0, 0.02f, 0.04f), new Color(0.95f, 0.45f, 0.05f), t * 2)
                    : Color.Lerp(new Color(0.95f, 0.45f, 0.05f), new Color(1, 0.97f, 0.8f), (t - 0.5f) * 2);
                palette[i] = color;
            }
        }

        protected override void OnReseed() => random = (uint)Noise.Uniform(1, uint.MaxValue - 1) | 1;

        public void SetRange(float metres)
        {
            range = Mathf.Clamp(metres, 1, MaxRange);
        }

        void Configure()
        {
            string key = $"{band}/{beamCount}/{verticalRays}/{rangeBins}/{verticalAperture}";
            if (key == configuration) return;
            configuration = key;
            int rays = beamCount * verticalRays;
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
            commands = new NativeArray<RaycastCommand>(rays, Allocator.Persistent);
            results = new NativeArray<RaycastHit>(rays, Allocator.Persistent);
            localRays = new Vector3[rays];
            rayWeights = new float[verticalRays];
            for (int j = 0; j < verticalRays; j++)
            {
                float elevation = -verticalAperture / 2 + verticalAperture * (j + 0.5f) / verticalRays;
                // Gaussian vertical pattern, half power at the aperture edges.
                rayWeights[j] = Mathf.Exp(-2.77f * Mathf.Pow(elevation / verticalAperture, 2)) * 20f / verticalRays;
                for (int beam = 0; beam < beamCount; beam++)
                {
                    float azimuth = -HorizontalAperture / 2 + HorizontalAperture * (beam + 0.5f) / beamCount;
                    localRays[beam * verticalRays + j] = Quaternion.Euler(-elevation, azimuth, 0) * Vector3.forward;
                }
            }
            intensity = new float[beamCount * rangeBins];
            smeared = new float[beamCount * rangeBins];
            Image = new byte[beamCount * rangeBins];
            BeamRanges = new float[beamCount];

            float sigma = AngularResolution / 2.355f / (HorizontalAperture / beamCount);
            int half = Mathf.CeilToInt(sigma * 2.5f);
            kernel = new float[half * 2 + 1];
            float sum = 0;
            for (int i = -half; i <= half; i++) sum += kernel[i + half] = Mathf.Exp(-0.5f * i * i / (sigma * sigma));
            for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

            int width = Mathf.CeilToInt(2 * FanRadius * Mathf.Sin(HorizontalAperture / 2 * Mathf.Deg2Rad));
            if (Fan != null) Destroy(Fan);
            Fan = new Texture2D(width, FanRadius, TextureFormat.RGBA32, false)
            {
                name = "Sonar fan", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            fanLookup = new int[width * FanRadius];
            fanRadius = new float[width * FanRadius];
            fanPixels = new Color32[width * FanRadius];
            for (int y = 0; y < FanRadius; y++)
            for (int x = 0; x < width; x++)
            {
                int pixel = y * width + x;
                float dx = x + 0.5f - width / 2f, dy = y + 0.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy) / FanRadius;
                float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                fanRadius[pixel] = radius;
                fanLookup[pixel] = radius > 1 || Mathf.Abs(angle) > HorizontalAperture / 2 ? -1
                    : Mathf.Min(rangeBins - 1, (int)(radius * rangeBins)) * beamCount
                      + Mathf.Clamp((int)((angle / HorizontalAperture + 0.5f) * beamCount), 0, beamCount - 1);
            }
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            if (!SampleDue(EffectivePingRate)) return;
            Configure();
            Ping();
            RenderFan();
        }

        void Ping()
        {
            Array.Clear(intensity, 0, intensity.Length);
            Array.Clear(BeamRanges, 0, BeamRanges.Length);
            footprintCount = 0;
            Vector3 origin = MountPosition;
            Quaternion rotation = MountRotation;
            float binSize = range / rangeBins;
            if (WaterDepth(origin) > 0.05f)
            {
                var query = new QueryParameters(~SensorSuite.VehicleMask, false, QueryTriggerInteraction.Ignore, false);
                for (int i = 0; i < commands.Length; i++)
                    commands[i] = new RaycastCommand(origin, rotation * localRays[i], query, range);
                RaycastCommand.ScheduleBatch(commands, results, 64, 1).Complete();

                for (int beam = 0; beam < beamCount; beam++)
                {
                    float previousRange = -1, previousValue = 0;
                    Vector3 previousNormal = Vector3.zero;
                    float nearest = float.MaxValue;
                    for (int j = 0; j < verticalRays; j++)
                    {
                        int ray = beam * verticalRays + j;
                        Vector3 direction = rotation * localRays[ray];
                        RaycastHit hit = results[ray];
                        float distance = -1;
                        Vector3 normal = Vector3.zero;
                        if (hit.collider != null)
                        {
                            distance = hit.distance;
                            normal = hit.normal;
                        }
                        // The water surface is a strong specular reflector seen from below.
                        if (direction.y > 1e-3f)
                        {
                            float surface = (vehicle.waterSurface - origin.y) / direction.y;
                            if (surface < range && (distance < 0 || surface < distance))
                            {
                                distance = surface;
                                normal = Vector3.down;
                            }
                        }
                        if (distance < minRange)
                        {
                            previousRange = -1;
                            continue;
                        }
                        float cos = Mathf.Max(0, Vector3.Dot(-direction, normal));
                        float value = rayWeights[j] * (0.2f * cos + 0.8f * cos * cos);
                        int bin = Mathf.Min(rangeBins - 1, (int)(distance / binSize));
                        intensity[bin * beamCount + beam] += value;
                        // Fill the range between adjacent rays on one surface, as the beam's
                        // vertical footprint does on a sloping floor or wall.
                        if (previousRange > 0 && Mathf.Abs(distance - previousRange) < 0.75f
                            && Vector3.Dot(normal, previousNormal) > 0.9f)
                        {
                            int from = Mathf.Min(rangeBins - 1, (int)(previousRange / binSize));
                            int step = bin > from ? 1 : -1;
                            int span = Mathf.Abs(bin - from);
                            for (int k = 1; k < span; k++)
                                intensity[(from + k * step) * beamCount + beam] += Mathf.Lerp(previousValue, value, k / (float)span);
                        }
                        previousRange = distance;
                        previousValue = value;
                        previousNormal = normal;
                        if (value > 0.02f) nearest = Mathf.Min(nearest, distance);
                        if (j == verticalRays / 2 && beam % 4 == 0 && footprintCount < footprint.Length)
                            footprint[footprintCount++] = origin + direction * distance;
                    }
                    BeamRanges[beam] = nearest < float.MaxValue ? nearest : 0;
                }
            }
            ComposeImage();
        }

        void ComposeImage()
        {
            int half = kernel.Length / 2;
            float scale = 1 / Mathf.Log(1 + gain * 2);
            for (int bin = 0; bin < rangeBins; bin++)
            {
                int row = bin * beamCount;
                for (int beam = 0; beam < beamCount; beam++)
                {
                    float sum = 0;
                    for (int k = -half; k <= half; k++)
                    {
                        int source = beam + k;
                        if (source >= 0 && source < beamCount) sum += kernel[k + half] * intensity[row + source];
                    }
                    smeared[row + beam] = sum;
                }
                bool belowMinimum = (bin + 0.5f) * range / rangeBins < minRange;
                for (int beam = 0; beam < beamCount; beam++)
                {
                    float value = smeared[row + beam];
                    if (noiseEnabled)
                    {
                        // Exponential intensity speckle and an exponential noise floor.
                        if (value > 0) value *= -Mathf.Log(Mathf.Max(1e-4f, NextUniform()));
                        value += noiseFloor * -Mathf.Log(Mathf.Max(1e-4f, NextUniform()));
                    }
                    Image[row + beam] = belowMinimum ? (byte)0
                        : (byte)Mathf.Clamp(Mathf.Log(1 + gain * value) * scale * 255, 0, 255);
                }
            }
        }

        // xorshift32: fast enough for per-bin speckle, seeded from the sensor's reproducible noise.
        float NextUniform()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (random & 0xFFFFFF) / 16777216f;
        }

        void RenderFan()
        {
            float ringSpacing = RingSpacing;
            float ringWidth = 0.6f / FanRadius * range;
            var ring = new Color32(110, 200, 210, 255);
            for (int i = 0; i < fanPixels.Length; i++)
            {
                int index = fanLookup[i];
                if (index < 0)
                {
                    fanPixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float metres = fanRadius[i] * range;
                float toRing = Mathf.Abs(metres - Mathf.Round(metres / ringSpacing) * ringSpacing);
                Color32 color = palette[Image[index]];
                fanPixels[i] = toRing < ringWidth && metres > ringSpacing / 2 ? Color32.Lerp(color, ring, 0.6f) : color;
            }
            Fan.SetPixels32(fanPixels);
            Fan.Apply(false);
        }

        public float RingSpacing => range <= 6 ? 1 : range <= 15 ? 5 : range <= 40 ? 10 : 50;

        void LateUpdate()
        {
            SensorSuite suite = SensorSuite.Instance;
            bool visible = suite != null && suite.BeamsVisible && footprintCount > 1;
            lines ??= new SensorBeams(transform, "Sonar footprint", 3, 0.025f);
            lines.SetVisible(visible);
            if (!visible) return;
            var color = new Color(1, 0.55f, 0.1f, 1);
            lines.Set(0, transform.position, footprint[0], color);
            lines.Set(1, transform.position, footprint[footprintCount - 1], color);
            lines.SetPath(2, footprint, footprintCount, color);
        }

        public override void AppendTelemetry(StringBuilder text)
        {
            text.AppendLine($"{SensorOverlay.Header("SONAR")}  {model}");
            text.AppendLine($"  {FrequencyKhz} kHz   {HorizontalAperture:0}°×{verticalAperture:0}°   {beamCount} beams   {AngularResolution}° res.");
            float nearest = float.MaxValue;
            int nearestBeam = -1;
            if (BeamRanges != null)
                for (int beam = 0; beam < beamCount; beam++)
                    if (BeamRanges[beam] > 0 && BeamRanges[beam] < nearest) { nearest = BeamRanges[beam]; nearestBeam = beam; }
            string closest = nearestBeam < 0 ? "no echo"
                : $"nearest {nearest:0.00} m at {-HorizontalAperture / 2 + HorizontalAperture * (nearestBeam + 0.5f) / beamCount:+0;-0}°";
            text.AppendLine($"  range {range:0} m   {rangeBins} bins   {EffectivePingRate:0} Hz   {closest}");
        }

        void OnDestroy()
        {
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
            if (Fan != null) Destroy(Fan);
        }
    }
}
