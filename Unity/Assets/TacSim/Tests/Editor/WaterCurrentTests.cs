using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TacSim.Tests
{
    public class WaterCurrentTests
    {
        static Vector3 Flow(Vector3 p, float time = 0) => WaterCurrent.Sample(p, time, .16f, new Vector2(16, 24), -5, 0);

        [Test]
        public void CurrentStaysInsideBasinAndDoesNotFlowThroughWalls()
        {
            Assert.That(Flow(new Vector3(0, 1, 0)), Is.EqualTo(Vector3.zero));
            Assert.That(Flow(new Vector3(0, -5, 0)), Is.EqualTo(Vector3.zero));
            Assert.That(Flow(new Vector3(9, -2, 0)), Is.EqualTo(Vector3.zero));
            foreach (float time in new[] {0f, 13f, 42f})
            for (int n = -7; n <= 7; n++)
            {
                Assert.That(Flow(new Vector3(8, -2, n), time).x, Is.EqualTo(0).Within(1e-6));
                Assert.That(Flow(new Vector3(-8, -2, n), time).x, Is.EqualTo(0).Within(1e-6));
                Assert.That(Flow(new Vector3(n, -2, 12), time).z, Is.EqualTo(0).Within(1e-6));
                Assert.That(Flow(new Vector3(n, -2, -12), time).z, Is.EqualTo(0).Within(1e-6));
            }
        }

        [Test]
        public void CurrentIsGentleSmoothAndWeakerAtBottom()
        {
            var p = new Vector3(3, -2, -5);
            Assert.That(Flow(p).magnitude, Is.GreaterThan(.02f));
            Assert.That(Flow(new Vector3(3, -4.95f, -5)).magnitude, Is.LessThan(Flow(p).magnitude * .1f));
            for (int t = 0; t < 120; t++)
            {
                Assert.That(Flow(p, t).magnitude, Is.LessThan(.23f));
                Assert.That((Flow(p, t + .02f) - Flow(p, t)).magnitude, Is.LessThan(.001f));
            }
            Assert.That((Flow(p, 0) - Flow(p, 10)).magnitude, Is.GreaterThan(.005f));
            Assert.That(WaterCurrent.Sample(p, 0, 0, new Vector2(16,24), -5, 0), Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator UnpoweredRovDriftsWithoutChangingBuoyancy()
        {
            yield return new EnterPlayMode();
            var scene = SceneManager.CreateScene("Current physics check", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var obj = new GameObject("Drifting ROV");
            SceneManager.MoveGameObjectToScene(obj, scene);
            try
            {
                obj.transform.position = new Vector3(0, -2.5f, -5);
                var vehicle = obj.AddComponent<RovVehicle>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                vehicle.SetArmed(false);
                var step = typeof(RovVehicle).GetMethod("FixedUpdate", flags);
                Vector3 start = vehicle.Body.position;
                for (int i = 0; i < 500; i++) { step.Invoke(vehicle, null); scene.GetPhysicsScene().Simulate(.02f); }
                Assert.That(vehicle.Body.position.x - start.x, Is.GreaterThan(.25f));
                Assert.That(Vector3.Distance(vehicle.Body.position, start), Is.LessThan(2));
                Assert.That(vehicle.Body.position.y, Is.EqualTo(start.y).Within(.02f));
                Assert.That(vehicle.Throttle, Is.Zero);
                vehicle.simulateWaterCurrent = false;
                Assert.That(vehicle.WaterVelocityAt(start, 0), Is.EqualTo(Vector3.zero));
                vehicle.current = new Vector3(.1f, 0, 0);
                Assert.That(vehicle.WaterVelocityAt(start, 0), Is.EqualTo(vehicle.current));
            }
            finally
            {
                Object.DestroyImmediate(obj);
                SceneManager.UnloadSceneAsync(scene);
            }
            yield return new ExitPlayMode();
        }
    }
}
