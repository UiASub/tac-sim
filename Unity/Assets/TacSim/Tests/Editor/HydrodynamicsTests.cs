using NUnit.Framework;
using UnityEngine;

namespace TacSim.Tests
{
    public class HydrodynamicsTests
    {
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
