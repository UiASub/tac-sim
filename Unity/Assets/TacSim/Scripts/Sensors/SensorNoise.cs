using System;
using UnityEngine;

namespace TacSim
{
    // Seeded noise source, so a training episode can reproduce its sensor errors exactly.
    public sealed class SensorNoise
    {
        System.Random random;
        bool hasSpare;
        double spare;

        public SensorNoise(int seed) => Reseed(seed);

        public void Reseed(int seed)
        {
            random = new System.Random(seed);
            hasSpare = false;
        }

        public float Uniform() => (float)random.NextDouble();
        public float Uniform(float min, float max) => min + (max - min) * Uniform();

        // Box-Muller; keeps the second value for the next call.
        public float Gaussian(float sigma = 1)
        {
            if (hasSpare)
            {
                hasSpare = false;
                return (float)(spare * sigma);
            }
            double u = 1 - random.NextDouble();
            double v = random.NextDouble();
            double radius = Math.Sqrt(-2 * Math.Log(u));
            spare = radius * Math.Sin(2 * Math.PI * v);
            hasSpare = true;
            return (float)(radius * Math.Cos(2 * Math.PI * v) * sigma);
        }

        public Vector3 Gaussian3(float sigma) => new(Gaussian(sigma), Gaussian(sigma), Gaussian(sigma));

        // Rayleigh amplitude with unit mean, the classic fully developed acoustic speckle.
        public float Speckle() => Mathf.Sqrt(-2 * Mathf.Log(Mathf.Max(1e-6f, 1 - Uniform()))) / 1.2533f;

        public static float Quantize(float value, float step) => step > 0 ? Mathf.Round(value / step) * step : value;

        // White noise density (unit/√Hz) to the standard deviation of one sample at a given rate.
        public static float DensityToSigma(float density, float rateHz) => density * Mathf.Sqrt(rateHz / 2);
    }

    // First-order Gauss-Markov process: a bias that wanders slowly around zero with a fixed RMS.
    [Serializable]
    public struct GaussMarkov
    {
        public float value;

        public void Reset(SensorNoise noise, float sigma) => value = noise.Gaussian(sigma);

        public float Step(SensorNoise noise, float sigma, float correlationTime, float dt)
        {
            float decay = Mathf.Exp(-dt / Mathf.Max(1e-3f, correlationTime));
            value = decay * value + noise.Gaussian(sigma * Mathf.Sqrt(1 - decay * decay));
            return value;
        }
    }
}
