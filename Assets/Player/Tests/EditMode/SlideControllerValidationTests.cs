using System;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class SlideControllerValidationTests
    {
        private static readonly ColliderProfile Baseline =
            new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f));
        private static readonly ColliderProfile Slide =
            new ColliderProfile(0.4f, 1f, new Vector3(0f, 0.5f, 0f));

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ConstructorRejectsInvalidSlideDuration_Requirement_5_4(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                    value, Baseline, Slide));
        }

        [Test]
        public void ConstructorRejectsInvalidBaselineProfiles_Requirement_5_3()
        {
            var invalidProfiles = new[]
            {
                new ColliderProfile(0f, 2f, Vector3.up),
                new ColliderProfile(0.6f, 1f, Vector3.up),
                new ColliderProfile(float.NaN, 2f, Vector3.up),
                new ColliderProfile(0.5f, float.PositiveInfinity, Vector3.up),
                new ColliderProfile(0.5f, 2f,
                    new Vector3(0f, float.NegativeInfinity, 0f))
            };

            foreach (var profile in invalidProfiles)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                        1f, profile, Slide));
            }
        }
        [Test]
        public void ConstructorRejectsInvalidOrNonFittingSlideProfiles_Requirement_5_3()
        {
            var invalidProfiles = new[]
            {
                new ColliderProfile(-0.1f, 1f, new Vector3(0f, 0.5f, 0f)),
                new ColliderProfile(0.6f, 1.2f, new Vector3(0f, 0.6f, 0f)),
                new ColliderProfile(0.4f, 1f, new Vector3(0.2f, 0.5f, 0f)),
                new ColliderProfile(0.4f, 1f, new Vector3(0f, 1.6f, 0f)),
                new ColliderProfile(0.4f, 1f,
                    new Vector3(float.NaN, 0.5f, 0f))
            };

            foreach (var profile in invalidProfiles)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                        1f, Baseline, profile));
            }
        }

        [TestCase("horizontal", 0.1f, 0.5f)]
        [TestCase("bottom", 0f, 0.5f)]
        [TestCase("top", 0f, 1.5f)]
        public void ConstructorAcceptsInclusiveContainmentBoundary_Requirement_5_3(
            string boundary,
            float centerX,
            float centerY)
        {
            var profile = new ColliderProfile(
                0.4f, 1f, new Vector3(centerX, centerY, 0f));

            Assert.DoesNotThrow(() =>
                new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                    1f, Baseline, profile), boundary);
        }

        [TestCase("horizontal", 0.1000001f, 0.5f)]
        [TestCase("bottom", 0f, 0.4999999f)]
        [TestCase("top", 0f, 1.5000001f)]
        public void ConstructorAcceptsRoundingNearContainmentBoundary_Requirement_5_3(
            string boundary,
            float centerX,
            float centerY)
        {
            var profile = new ColliderProfile(
                0.4f, 1f, new Vector3(centerX, centerY, 0f));

            Assert.DoesNotThrow(() =>
                new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                    1f, Baseline, profile), boundary);
        }

        [TestCase("horizontal", 0.1001f, 0.5f)]
        [TestCase("bottom", 0f, 0.4999f)]
        [TestCase("top", 0f, 1.5001f)]
        public void ConstructorRejectsClearlyOutsideContainmentBoundary_Requirement_5_3(
            string boundary,
            float centerX,
            float centerY)
        {
            var profile = new ColliderProfile(
                0.4f, 1f, new Vector3(centerX, centerY, 0f));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SlideController(CreateSnapshot(PlayerState.Running, Baseline),
                    1f, Baseline, profile), boundary);
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void AdvanceRejectsInvalidElapsedTimeWithoutMutation_Requirement_5_4(float value)
        {
            var controller = CreateSlidingController();
            var before = controller.Snapshot;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                controller.Advance(value, () => true));
            Assert.That(controller.Snapshot, Is.EqualTo(before));
        }

        [TestCase(PlayerState.Running)]
        [TestCase(PlayerState.Sliding)]
        public void AdvanceRejectsMissingRestorationQueryWithoutMutation_Requirements_5_6_5_9(
            PlayerState state)
        {
            var profile = state == PlayerState.Sliding ? Slide : Baseline;
            var controller = new SlideController(
                CreateSnapshot(state, profile), 1f, Baseline, Slide);
            var before = controller.Snapshot;

            Assert.Throws<ArgumentNullException>(() => controller.Advance(0f, null));
            Assert.That(controller.Snapshot, Is.EqualTo(before));
        }

        private static SlideController CreateSlidingController()
        {
            var controller = new SlideController(
                CreateSnapshot(PlayerState.Running, Baseline), 1f, Baseline, Slide);
            controller.RequestSlide();
            return controller;
        }

        private static PlayerSnapshot CreateSnapshot(
            PlayerState state,
            ColliderProfile profile)
        {
            return new PlayerSnapshot(
                new Vector3(2f, 3f, 5f),
                Quaternion.Euler(0f, 15f, 0f),
                state,
                true,
                8f,
                LogicalLane.Center,
                LogicalLane.Right,
                0.25f,
                0f,
                1f,
                0.25f,
                0f,
                state == PlayerState.Sliding ? 0.75f : 0f,
                profile,
                new ImmutableValueSequence<LaneRequest>(new[]
                {
                    new LaneRequest(LaneDirection.Right, 9UL)
                }),
                new ImmutableValueSequence<PlayerCommandKind>(new[]
                {
                    PlayerCommandKind.Slide
                }),
                false);
        }
    }
}
