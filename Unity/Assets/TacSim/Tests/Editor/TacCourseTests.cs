using NUnit.Framework;
using UnityEngine;

namespace TacSim.Tests
{
    public sealed class TacCourseTests
    {
        [Test]
        public void ArucoTableMatchesOpenCvMarker28()
        {
            // cv2.aruco.generateImageMarker(DICT_ARUCO_ORIGINAL, 28, 7) data cells (1 = white).
            int[,] expected =
            {
                { 1, 0, 0, 0, 0 },
                { 1, 0, 0, 0, 0 },
                { 1, 0, 1, 1, 1 },
                { 0, 1, 1, 1, 0 },
                { 1, 0, 0, 0, 0 },
            };
            Assert.AreEqual(1024, ArucoOriginal.Codes.Length);
            for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
                Assert.AreEqual(expected[row, column] == 1, ArucoOriginal.IsWhite(28, row, column), $"cell {row},{column}");
        }

        [Test]
        public void SegmentDistanceDetectsCrossingAndGap()
        {
            Assert.AreEqual(0, TacCourse.SegmentDistance(new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1)));
            Assert.AreEqual(2, TacCourse.SegmentDistance(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(1, 2)), 1e-5f);
        }
    }
}
