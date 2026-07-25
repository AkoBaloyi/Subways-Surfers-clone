using System;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player.Tests
{
    public sealed class JumpControllerValidationTests
    {
        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ConstructorRejectsInvalidJumpVelocity_Requirements_4_3_4_4(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new JumpController(default(PlayerSnapshot), value, 9.81f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ConstructorRejectsInvalidGravityAcceleration_Requirement_4_5(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new JumpController(default(PlayerSnapshot), 8f, value));
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void AdvanceRejectsInvalidElapsedTimeWithoutMutation_Requirement_4_5(float value)
        {
            var controller = new JumpController(default(PlayerSnapshot), 8f, 9.81f);
            var before = controller.Snapshot;

            Assert.Throws<ArgumentOutOfRangeException>(() => controller.Advance(value));
            Assert.That(controller.Snapshot, Is.EqualTo(before));
        }
    }
}
