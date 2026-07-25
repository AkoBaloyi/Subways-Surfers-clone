using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class GroundedQueryExampleTests
    {
        private const int GroundLayer = 8;
        private const int GroundMask = 1 << GroundLayer;
        private const float Tolerance = 0.25f;
        private const float NormalThreshold = 0.6f;

        // **Validates: Requirements 4.1, 4.2**
        [TestCase(GroundLayer, true, true, Tolerance, NormalThreshold, false,
            TestName = "GroundedQuery_Trigger_IsExcluded")]
        [TestCase(9, true, false, Tolerance, NormalThreshold, false,
            TestName = "GroundedQuery_WrongLayer_IsExcluded")]
        [TestCase(GroundLayer, false, false, Tolerance, NormalThreshold, false,
            TestName = "GroundedQuery_UnmarkedGeometry_IsExcluded")]
        [TestCase(GroundLayer, true, false, Tolerance, 0f, false,
            TestName = "GroundedQuery_WallNormal_IsExcluded")]
        [TestCase(GroundLayer, true, false, Tolerance, -1f, false,
            TestName = "GroundedQuery_CeilingNormal_IsExcluded")]
        [TestCase(GroundLayer, false, true, Tolerance, NormalThreshold, false,
            TestName = "GroundedQuery_Coin_IsExcluded")]
        [TestCase(GroundLayer, false, false, Tolerance, NormalThreshold, false,
            TestName = "GroundedQuery_Obstacle_IsExcluded")]
        [TestCase(GroundLayer, true, false, Tolerance, NormalThreshold, true,
            TestName = "GroundedQuery_ExactToleranceAndNormalBoundaries_AreIncluded")]
        [TestCase(GroundLayer, true, false, 0.2501f, NormalThreshold, false,
            TestName = "GroundedQuery_AboveContactTolerance_IsExcluded")]
        [TestCase(GroundLayer, true, false, Tolerance, 0.5999f, false,
            TestName = "GroundedQuery_BelowSupportNormalThreshold_IsExcluded")]
        public void ContactPredicateExamples(
            int layer, bool marker, bool trigger, float distance, float normal, bool expected)
        {
            Assert.That(GroundContactPredicate.IsValid(
                layer, marker, trigger, distance, normal,
                GroundMask, Tolerance, NormalThreshold), Is.EqualTo(expected));
        }

        // **Validates: Requirements 14.10**
        [Test]
        public void PublicGroundedQueryRemainsCurrentDuringEverySupportedStateTransitionEvent()
        {
            var machine = CreateMachine(true);
            var query = (IGroundedStatusQuery)machine;
            var eventCount = 0;
            machine.StateChanged += _ =>
            {
                eventCount++;
                Assert.That(query.IsGrounded, Is.True);
                Assert.That(query.IsGrounded, Is.EqualTo(machine.Snapshot.IsGrounded));
            };

            Assert.That(machine.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.ResolveLanding(), Is.True);
            Assert.That(machine.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.ResolveSlideRestoration(true), Is.True);
            Assert.That(machine.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.RequestReset("grounded-events").Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.CompleteReset(), Is.True);
            Assert.That(eventCount, Is.EqualTo(7));
            Assert.That(query.IsGrounded, Is.True);
        }

        // **Validates: Requirements 4.2, 14.10**
        [Test]
        public void PublicGroundedQueryRemainsFalseAcrossFailureAndResetEventsWithoutContact()
        {
            var machine = CreateMachine(false);
            var query = (IGroundedStatusQuery)machine;
            var eventCount = 0;
            machine.StateChanged += _ =>
            {
                eventCount++;
                Assert.That(query.IsGrounded, Is.False);
                Assert.That(query.IsGrounded, Is.EqualTo(machine.Snapshot.IsGrounded));
            };

            Assert.That(machine.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.RequestReset("airborne-events").Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(machine.CompleteReset(), Is.True);
            Assert.That(eventCount, Is.EqualTo(3));
            Assert.That(query.IsGrounded, Is.False);
        }

        private static PlayerStateMachine CreateMachine(bool grounded)
        {
            var snapshot = new PlayerSnapshot(
                new Vector3(2f, 3f, 4f),
                Quaternion.identity,
                PlayerState.Running,
                grounded,
                12f,
                LogicalLane.Center,
                LogicalLane.Center,
                0f,
                0f,
                0f,
                0f,
                grounded ? -0.5f : 2f,
                0f,
                new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                ImmutableValueSequence<LaneRequest>.Empty,
                ImmutableValueSequence<PlayerCommandKind>.Empty,
                false);
            return new PlayerStateMachine(snapshot, ImmutableValueSequence<string>.Empty);
        }
    }
}
