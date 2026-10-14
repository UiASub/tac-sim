using NUnit.Framework;
using UnityEngine;

namespace TacSim.Tests
{
    public class SensorTests
    {
        [Test]
        public void FrdConversionRoundTripsAndFlipsHandedness()
        {
            var forwardRightDown = SensorFrames.ToFrd(new Vector3(1, -1, 1));
            Assert.That(forwardRightDown, Is.EqualTo(new Vector3(1, 1, 1)));
            var vector = new Vector3(0.3f, -2, 5);
            Assert.That(SensorFrames.FromFrd(SensorFrames.ToFrd(vector)), Is.EqualTo(vector));
            // A right turn in Unity (+Y angular velocity) is a positive FRD yaw rate.
            Assert.That(SensorFrames.RateToFrd(Vector3.up).z, Is.EqualTo(1));
        }

        [Test]
        public void YawPitchRollUseMarineConventions()
        {
            AssertAngles(Quaternion.Euler(0, 90, 0), new Vector3(90, 0, 0));
            AssertAngles(Quaternion.Euler(-10, 0, 0), new Vector3(0, 10, 0));
            AssertAngles(Quaternion.Euler(0, 0, -20), new Vector3(0, 0, 20));
            AssertAngles(Quaternion.Euler(90, 0, 0), new Vector3(0, -90, 0));
            AssertAngles(Quaternion.Euler(0, 90, 0) * Quaternion.Euler(90, 0, 0), new Vector3(90, -90, 0));
        }

        [Test]
        public void NedQuaternionMatchesYaw()
        {
            Vector4 identity = SensorFrames.NedQuaternion(Vector3.zero);
            Assert.That(identity.w, Is.EqualTo(1).Within(1e-6f));
            Vector4 east = SensorFrames.NedQuaternion(new Vector3(90, 0, 0));
            Assert.That(east.z, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(1e-5f));
            Assert.That(east.w, Is.EqualTo(Mathf.Sqrt(0.5f)).Within(1e-5f));
        }

        [Test]
        public void SeededNoiseIsReproducibleAndCalibrated()
        {
            var first = new SensorNoise(7);
            var second = new SensorNoise(7);
            for (int i = 0; i < 100; i++) Assert.That(first.Gaussian(), Is.EqualTo(second.Gaussian()));

            var noise = new SensorNoise(3);
            double sum = 0, squares = 0;
            const int count = 20000;
            for (int i = 0; i < count; i++)
            {
                double value = noise.Gaussian(2);
                sum += value;
                squares += value * value;
            }
            Assert.That(sum / count, Is.EqualTo(0).Within(0.05));
            Assert.That(System.Math.Sqrt(squares / count), Is.EqualTo(2).Within(0.05));
            // 0.0035 °/s/√Hz sampled at 50 Hz (25 Hz bandwidth) is 0.0175 °/s per sample.
            Assert.That(SensorNoise.DensityToSigma(0.0035f, 50), Is.EqualTo(0.0175f).Within(1e-6f));
        }

        static void AssertAngles(Quaternion rotation, Vector3 expected)
        {
            Vector3 actual = SensorFrames.YawPitchRoll(rotation);
            Assert.That(Mathf.DeltaAngle(actual.x, expected.x), Is.EqualTo(0).Within(0.01f), "yaw");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.01f), "pitch");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.01f), "roll");
        }
    }
}
