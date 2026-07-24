using NUnit.Framework;
using UnityEngine;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    public class PlayerMovementTests
    {
        [Test]
        public void ComputeDisplacement_ZeroInput_ReturnsZero()
        {
            Vector3 displacement = PlayerMovement.ComputeDisplacement(Vector2.zero, speed: 5f, deltaTime: 1f);

            Assert.AreEqual(Vector3.zero, displacement);
        }

        [Test]
        public void ComputeDisplacement_AxisAlignedInput_ScalesBySpeedAndDeltaTime()
        {
            Vector3 displacement = PlayerMovement.ComputeDisplacement(Vector2.right, speed: 4f, deltaTime: 0.5f);

            Assert.AreEqual(new Vector3(2f, 0f, 0f), displacement);
        }

        [Test]
        public void ComputeDisplacement_DiagonalInput_IsNormalized_NotFasterThanAxisAligned()
        {
            Vector3 diagonal = PlayerMovement.ComputeDisplacement(new Vector2(1f, 1f), speed: 4f, deltaTime: 1f);
            Vector3 straight = PlayerMovement.ComputeDisplacement(Vector2.right, speed: 4f, deltaTime: 1f);

            Assert.AreEqual(straight.magnitude, diagonal.magnitude, 0.0001f);
        }

        [Test]
        public void ComputeDisplacement_ResultIsAlwaysOnTheZPlane()
        {
            Vector3 displacement = PlayerMovement.ComputeDisplacement(new Vector2(1f, 1f), speed: 4f, deltaTime: 1f);

            Assert.AreEqual(0f, displacement.z);
        }
    }
}
