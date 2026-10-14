using System.Text;
using UnityEngine;

namespace TacSim
{
    public enum PressureSensorVariant { Bar30, Bar100 }

    // Blue Robotics Bar30 (MS5837-30BA) or Bar100 (Keller 4LD) depth/pressure sensor.
    // Reports absolute pressure with a per-unit offset; depth is computed against a surface
    // calibration taken at power-on, as ArduSub does, so the offset cancels but drift does not.
    public sealed class PressureSensor : RovSensor
    {
        public PressureSensorVariant variant = PressureSensorVariant.Bar30;
        public float rate = 20;
        [Tooltip("Density used for the depth conversion, kg/m³ (ArduSub freshwater default)")]
        public float fluidDensity = 997;
        public float atmosphere = 1013.25f;
        public float waterTemperature = 27;
        public float airTemperature = 22;
        [Tooltip("Thermal time constant of the sensor housing, seconds")]
        public float temperatureTimeConstant = 4;

        public float Pressure { get; private set; }
        public float Depth { get; private set; }
        public float Temperature { get; private set; }
        public float SurfacePressure { get; private set; }
        public override string Kind => "pressure";

        // Datasheet values: resolution and RMS noise (mbar), absolute accuracy, drift, °C accuracy.
        float Resolution => variant == PressureSensorVariant.Bar30 ? 0.2f : 3;
        float NoiseRms => variant == PressureSensorVariant.Bar30 ? 0.2f : 1.5f;
        float Accuracy => variant == PressureSensorVariant.Bar30 ? 200 : 500;
        float DriftRms => variant == PressureSensorVariant.Bar30 ? 2 : 4;
        float TemperatureAccuracy => variant == PressureSensorVariant.Bar30 ? 1 : 2;
        float FullScale => variant == PressureSensorVariant.Bar30 ? 30000 : 100000;
        public string VariantName => variant == PressureSensorVariant.Bar30
            ? "Blue Robotics Bar30 (MS5837-30BA)" : "Blue Robotics Bar100 (Keller 4LD)";

        float offset;
        float temperatureOffset;
        GaussMarkov drift;
        float bodyTemperature = float.NaN;

        protected override void OnReseed()
        {
            model = VariantName;
            // Accuracy figures are treated as 3σ bounds for an individual unit.
            offset = Noise.Gaussian(Accuracy / 3);
            temperatureOffset = Noise.Gaussian(TemperatureAccuracy / 2);
            drift.Reset(Noise, DriftRms);
            SurfacePressure = atmosphere + (noiseEnabled ? offset : 0);
        }

        public override void ResetState()
        {
            base.ResetState();
            bodyTemperature = float.NaN;
        }

        void FixedUpdate()
        {
            if (vehicle == null) return;
            double previous = SampleTime;
            if (!SampleDue(rate)) return;
            float dt = previous >= 0 ? (float)(SampleTime - previous) : 0;
            Vector3 position = MountPosition;
            float depth = WaterDepth(position);
            // Hydrostatic pressure from the vehicle's water model, in mbar.
            float truth = atmosphere + Mathf.Max(0, depth) * vehicle.waterDensity * -Physics.gravity.y / 100;
            float error = noiseEnabled ? offset + drift.Step(Noise, DriftRms, 600, dt) + Noise.Gaussian(NoiseRms) : 0;
            SurfacePressure = atmosphere + (noiseEnabled ? offset : 0);
            Pressure = SensorNoise.Quantize(Mathf.Clamp(truth + error, 0, FullScale), Resolution);
            Depth = (Pressure - SurfacePressure) * 100 / (fluidDensity * -Physics.gravity.y);

            float ambient = depth > 0 ? waterTemperature : airTemperature;
            if (float.IsNaN(bodyTemperature)) bodyTemperature = ambient;
            bodyTemperature += (ambient - bodyTemperature) * (1 - Mathf.Exp(-dt / temperatureTimeConstant));
            Temperature = SensorNoise.Quantize(bodyTemperature + (noiseEnabled ? temperatureOffset + Noise.Gaussian(0.02f) : 0), 0.01f);
        }

        public override void AppendTelemetry(StringBuilder text)
        {
            text.AppendLine($"{SensorOverlay.Header("DEPTH")}  {model}");
            if (!HasSample)
            {
                text.AppendLine("  waiting for first sample");
                return;
            }
            text.AppendLine($"  {Pressure:0.0} mbar   depth {Depth:0.000} m   {Temperature:0.0} °C   {rate:0} Hz");
        }
    }
}
