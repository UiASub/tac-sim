using NUnit.Framework;
using UnityEngine;

namespace TacSim.Tests
{
    public class HydrodynamicsTests
    {
        [Test]
        public void FourDofCommandsIgnorePitchAndRollAndClampYaw()
        {
            var obj = new GameObject("Command test");
            try
            {
                var vehicle = obj.AddComponent<RovVehicle>();
                vehicle.SetCommand(Vector3.forward, new Vector3(100, 0.5f, -100));
                Assert.That(vehicle.RotationCommand, Is.EqualTo(new Vector3(0, 0.5f, 0)));
                Assert.That(vehicle.TranslationCommand, Is.EqualTo(Vector3.forward));
                vehicle.SetCommand(Vector3.zero, new Vector3(1, -2, 1));
                Assert.That(vehicle.RotationCommand, Is.EqualTo(Vector3.down));
            }
            finally { Object.DestroyImmediate(obj); }
        }

        [Test]
        public void FourDofSpawnAndResetAreLevelAndKeepHeading()
        {
            var obj = new GameObject("Pose test");
            try
            {
                obj.transform.rotation = Quaternion.Euler(20, 65, -15);
                float heading = obj.transform.eulerAngles.y;
                var vehicle = obj.AddComponent<RovVehicle>();
                // EditMode does not run MonoBehaviour lifecycle callbacks.
                typeof(RovVehicle).GetMethod("Awake", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).Invoke(vehicle, null);
                Assert.That(vehicle.Body.constraints, Is.EqualTo(
                    RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ));
                Assert.That(Quaternion.Angle(vehicle.Body.rotation, Quaternion.Euler(0, heading, 0)), Is.LessThan(0.01f));
                vehicle.Body.rotation = Quaternion.Euler(30, 120, 20);
                vehicle.ResetVehicle();
                Assert.That(Quaternion.Angle(vehicle.Body.rotation, Quaternion.Euler(0, heading, 0)), Is.LessThan(0.01f));
                Assert.That(vehicle.Body.angularVelocity, Is.EqualTo(Vector3.zero));
            }
            finally { Object.DestroyImmediate(obj); }
        }

        [Test]
        public void DragCannotAddEnergy()
        {
            var linear = new Vector3(12, 18, 10);
            var quadratic = new Vector3(35, 45, 25);
            for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            {
                var velocity = new Vector3(x, y, z);
                Assert.That(Vector3.Dot(velocity, RovVehicle.Drag(velocity, linear, quadratic)), Is.LessThanOrEqualTo(0));
            }
        }

        [Test]
        public void StillWaterRelativeVelocityProducesNoDrag()
        {
            Assert.That(RovVehicle.Drag(Vector3.zero, Vector3.one, Vector3.one), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void QuadraticDragScalesWithSpeedSquared()
        {
            var first = RovVehicle.Drag(new Vector3(1, -2, 3), Vector3.zero, Vector3.one);
            var twice = RovVehicle.Drag(new Vector3(2, -4, 6), Vector3.zero, Vector3.one);
            Assert.That(twice, Is.EqualTo(first * 4));
        }
    }
}
