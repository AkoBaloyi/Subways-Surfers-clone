using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class JumpBehaviorPropertyTests
    {
        private const int Seed = 460067;
        private const int CaseCount = 160;
        private const float NumericTolerance = 0.0001f;

        // **Validates: Requirements 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9**
        [Test]
        [Description("Feature: player-controller, Property 6: Jump behavior follows one-impulse ballistic rules")]
        public void JumpBehaviorFollowsOneImpulseBallisticRules_Property6_Requirements_4_3_Through_4_9()
        {
            var caseIndex = 0;
            var coverage = new Coverage();
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                generated => AssertJumpBehavior(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.InvalidStates.Count, Is.EqualTo(4));
            Assert.That(coverage.SawZeroElapsedUpdate, Is.True);
            Assert.That(coverage.SawPositiveElapsedUpdate, Is.True);
            Assert.That(coverage.SawAirborneNonPositiveVelocity, Is.True);
            Assert.That(coverage.SawGroundedPositiveVelocity, Is.True);
            Assert.That(coverage.SawValidLanding, Is.True);
            Assert.That(coverage.SawConcurrentForwardAndLaneProgress, Is.True);
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            if (caseIndex == 0)
            {
                return new GeneratedCase(
                    PlayerState.Jumping,
                    8f,
                    20f,
                    10f,
                    1f,
                    0.1f,
                    LaneDirection.Right,
                    new[] { 0f, 0.1f, 0.2f });
            }

            var invalidStates = new[]
            {
                PlayerState.Jumping,
                PlayerState.Sliding,
                PlayerState.Failed,
                PlayerState.Resetting
            };
            var jumpVelocity = NextFloat(random, 3f, 15f);
            var gravityAcceleration = NextFloat(random, 5f, 30f);
            var laneDuration = NextFloat(random, 0.2f, 2f);
            var concurrentElapsed = Math.Min(
                laneDuration * 0.5f,
                jumpVelocity / (gravityAcceleration * 4f));
            var updateCount = random.Next(3, 9);
            var elapsedUpdates = new float[updateCount];
            elapsedUpdates[0] = 0f;
            for (var index = 1; index < updateCount; index++)
                elapsedUpdates[index] = NextFloat(random, 0f, 0.2f);

            return new GeneratedCase(
                invalidStates[caseIndex % invalidStates.Length],
                jumpVelocity,
                gravityAcceleration,
                NextFloat(random, 0.1f, 50f),
                laneDuration,
                concurrentElapsed,
                random.Next(2) == 0 ? LaneDirection.Left : LaneDirection.Right,
                elapsedUpdates);
        }

        private static void AssertJumpBehavior(GeneratedCase generated, Coverage coverage)
        {
            var groundedRunning = CreateSnapshot(
                PlayerState.Running,
                true,
                -generated.GravityAcceleration * 0.25f,
                generated.ForwardSpeed);
            var jump = JumpControllerSeam.Create(
                groundedRunning,
                generated.JumpVelocity,
                generated.GravityAcceleration);

            var accepted = jump.RequestJump();
            Assert.That(accepted.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(accepted.Command, Is.EqualTo(PlayerCommandKind.Jump));
            Assert.That(accepted.Reason, Is.EqualTo(RejectionReason.None));
            Assert.That(accepted.CurrentState, Is.EqualTo(PlayerState.Jumping));
            Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Jumping));
            Assert.That(jump.Snapshot.VerticalVelocity, Is.EqualTo(generated.JumpVelocity));
            AssertEntryMutationOnly(groundedRunning, jump.Snapshot);

            var afterEntry = jump.Snapshot;
            var repeated = jump.RequestJump();
            Assert.That(repeated.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(repeated.Reason, Is.EqualTo(RejectionReason.InvalidState));
            Assert.That(repeated.CurrentState, Is.EqualTo(PlayerState.Jumping));
            SnapshotAssert.Preserved(afterEntry, jump.Snapshot,
                context: "A repeated jump must not apply a second impulse or mutate pending work.");

            var landedWhileAscending = jump.ResolveLanding(true);
            Assert.That(landedWhileAscending, Is.False,
                "Ground contact while ascending must not land the player.");
            Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Jumping));
            Assert.That(jump.Snapshot.VerticalVelocity, Is.EqualTo(generated.JumpVelocity));
            coverage.SawGroundedPositiveVelocity = true;
            Assert.That(jump.ResolveLanding(false), Is.False);

            AssertConcurrentMovement(jump, generated, coverage);
            var expectedVelocity = generated.JumpVelocity -
                generated.GravityAcceleration * generated.ConcurrentElapsed;

            for (var index = 0; index < generated.ElapsedUpdates.Count; index++)
            {
                var elapsed = generated.ElapsedUpdates[index];
                var beforeVelocity = jump.Snapshot.VerticalVelocity;
                jump.Advance(elapsed);
                expectedVelocity -= generated.GravityAcceleration * elapsed;

                Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Jumping));
                Assert.That(jump.Snapshot.VerticalVelocity,
                    Is.EqualTo(expectedVelocity).Within(NumericTolerance),
                    "Each Jumping movement update must apply exactly one gravity decrement.");
                if (elapsed == 0f)
                {
                    coverage.SawZeroElapsedUpdate = true;
                    Assert.That(jump.Snapshot.VerticalVelocity, Is.EqualTo(beforeVelocity),
                        "A zero-time gravity decrement must preserve velocity.");
                }
                else
                {
                    coverage.SawPositiveElapsedUpdate = true;
                }
            }

            var descentElapsed = expectedVelocity > 0f
                ? expectedVelocity / generated.GravityAcceleration + 0.25f
                : 0f;
            jump.Advance(descentElapsed);
            expectedVelocity -= generated.GravityAcceleration * descentElapsed;
            Assert.That(jump.Snapshot.VerticalVelocity,
                Is.EqualTo(expectedVelocity).Within(NumericTolerance));
            Assert.That(jump.ResolveLanding(false), Is.False,
                "Non-positive velocity without valid ground contact must remain Jumping.");
            Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Jumping));
            coverage.SawAirborneNonPositiveVelocity |= jump.Snapshot.VerticalVelocity <= 0f;

            Assert.That(jump.ResolveLanding(true), Is.True,
                "Grounded contact with non-positive velocity must land.");
            Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Running));
            coverage.SawValidLanding = true;

            AssertInvalidRequestPreservation(generated, coverage);
        }

        private static void AssertConcurrentMovement(
            JumpControllerSeam jump,
            GeneratedCase generated,
            Coverage coverage)
        {
            var planner = new LanePlanner(
                LogicalLane.Center,
                new Vector3(-1f, 0f, 1f),
                generated.LaneDuration,
                0f);
            var accepted = planner.TryEnqueue(
                new LaneRequest(generated.LaneDirection, 1UL),
                jump.Snapshot.State);
            Assert.That(accepted, Is.True,
                "Lane requests must remain accepted while Jumping.");

            var beforeX = planner.LateralPosition;
            jump.Advance(generated.ConcurrentElapsed);
            planner.Advance(generated.ConcurrentElapsed, jump.Snapshot.State);
            var forward = ForwardDisplacement.Calculate(
                jump.Snapshot.State,
                generated.ForwardSpeed,
                generated.ConcurrentElapsed);
            var directionSign = generated.LaneDirection == LaneDirection.Right ? 1f : -1f;
            var expectedX = directionSign *
                (generated.ConcurrentElapsed / generated.LaneDuration);

            Assert.That(jump.Snapshot.State, Is.EqualTo(PlayerState.Jumping));
            Assert.That(forward.x, Is.Zero);
            Assert.That(forward.y, Is.Zero);
            Assert.That(forward.z,
                Is.EqualTo(generated.ForwardSpeed * generated.ConcurrentElapsed)
                    .Within(NumericTolerance));
            Assert.That(forward.z, Is.GreaterThan(0f));
            Assert.That(planner.LateralPosition,
                Is.EqualTo(expectedX).Within(NumericTolerance));
            Assert.That(directionSign * (planner.LateralPosition - beforeX),
                Is.GreaterThan(0f));
            coverage.SawConcurrentForwardAndLaneProgress = true;
        }

        private static void AssertInvalidRequestPreservation(
            GeneratedCase generated,
            Coverage coverage)
        {
            var airborne = CreateSnapshot(
                PlayerState.Running,
                false,
                -generated.JumpVelocity * 0.5f,
                generated.ForwardSpeed);
            var airborneJump = JumpControllerSeam.Create(
                airborne,
                generated.JumpVelocity,
                generated.GravityAcceleration);
            var airborneResult = airborneJump.RequestJump();
            Assert.That(airborneResult.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(airborneResult.Reason, Is.EqualTo(RejectionReason.NotGrounded));
            Assert.That(airborneResult.CurrentState, Is.EqualTo(PlayerState.Running));
            SnapshotAssert.Preserved(airborne, airborneJump.Snapshot,
                context: "An airborne Running jump request must preserve the complete snapshot.");

            var invalid = CreateSnapshot(
                generated.InvalidState,
                true,
                generated.JumpVelocity * 0.375f,
                generated.ForwardSpeed);
            var invalidJump = JumpControllerSeam.Create(
                invalid,
                generated.JumpVelocity,
                generated.GravityAcceleration);
            var invalidResult = invalidJump.RequestJump();
            Assert.That(invalidResult.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(invalidResult.Reason, Is.EqualTo(RejectionReason.InvalidState));
            Assert.That(invalidResult.CurrentState, Is.EqualTo(generated.InvalidState));
            SnapshotAssert.Preserved(invalid, invalidJump.Snapshot,
                context: "A state-invalid jump request must preserve every snapshot field.");
            coverage.InvalidStates.Add(generated.InvalidState);
        }

        private static void AssertEntryMutationOnly(PlayerSnapshot before, PlayerSnapshot after)
        {
            Assert.That(after.Position, Is.EqualTo(before.Position));
            Assert.That(after.Rotation, Is.EqualTo(before.Rotation));
            Assert.That(after.IsGrounded, Is.EqualTo(before.IsGrounded));
            Assert.That(after.ForwardSpeed, Is.EqualTo(before.ForwardSpeed));
            Assert.That(after.CurrentLane, Is.EqualTo(before.CurrentLane));
            Assert.That(after.TargetLane, Is.EqualTo(before.TargetLane));
            Assert.That(after.LateralPosition, Is.EqualTo(before.LateralPosition));
            Assert.That(after.LaneSegmentStart, Is.EqualTo(before.LaneSegmentStart));
            Assert.That(after.LaneSegmentTarget, Is.EqualTo(before.LaneSegmentTarget));
            Assert.That(after.LaneChangeProgress, Is.EqualTo(before.LaneChangeProgress));
            Assert.That(after.SlideElapsedTime, Is.EqualTo(before.SlideElapsedTime));
            Assert.That(after.ColliderProfile, Is.EqualTo(before.ColliderProfile));
            Assert.That(after.LaneRequestQueue, Is.EqualTo(before.LaneRequestQueue));
            Assert.That(after.PendingActionRequests, Is.EqualTo(before.PendingActionRequests));
            Assert.That(after.ResetInProgress, Is.EqualTo(before.ResetInProgress));
        }

        private static PlayerSnapshot CreateSnapshot(
            PlayerState state,
            bool grounded,
            float verticalVelocity,
            float forwardSpeed)
        {
            var failed = state == PlayerState.Failed;
            return new PlayerSnapshot(
                new Vector3(2f, 3f, 5f),
                Quaternion.Euler(0f, 17f, 0f),
                state,
                grounded,
                forwardSpeed,
                LogicalLane.Center,
                LogicalLane.Right,
                0.25f,
                0f,
                1f,
                0.25f,
                failed ? 0f : verticalVelocity,
                state == PlayerState.Sliding ? 0.75f : 0f,
                new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                failed
                    ? ImmutableValueSequence<LaneRequest>.Empty
                    : new ImmutableValueSequence<LaneRequest>(new[]
                    {
                        new LaneRequest(LaneDirection.Right, 19UL)
                    }),
                failed
                    ? ImmutableValueSequence<PlayerCommandKind>.Empty
                    : new ImmutableValueSequence<PlayerCommandKind>(new[]
                    {
                        PlayerCommandKind.Lane,
                        PlayerCommandKind.Jump
                    }),
                state == PlayerState.Resetting);
        }

        private static float NextFloat(Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                PlayerState invalidState,
                float jumpVelocity,
                float gravityAcceleration,
                float forwardSpeed,
                float laneDuration,
                float concurrentElapsed,
                LaneDirection laneDirection,
                IReadOnlyList<float> elapsedUpdates)
            {
                InvalidState = invalidState;
                JumpVelocity = jumpVelocity;
                GravityAcceleration = gravityAcceleration;
                ForwardSpeed = forwardSpeed;
                LaneDuration = laneDuration;
                ConcurrentElapsed = concurrentElapsed;
                LaneDirection = laneDirection;
                ElapsedUpdates = elapsedUpdates;
            }

            public PlayerState InvalidState { get; }
            public float JumpVelocity { get; }
            public float GravityAcceleration { get; }
            public float ForwardSpeed { get; }
            public float LaneDuration { get; }
            public float ConcurrentElapsed { get; }
            public LaneDirection LaneDirection { get; }
            public IReadOnlyList<float> ElapsedUpdates { get; }

            public override string ToString()
            {
                var elapsed = new string[ElapsedUpdates.Count];
                for (var index = 0; index < ElapsedUpdates.Count; index++)
                    elapsed[index] = ElapsedUpdates[index].ToString("R", CultureInfo.InvariantCulture);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "InvalidState={0}, JumpVelocity={1:R}, Gravity={2:R}, ForwardSpeed={3:R}, " +
                    "LaneDuration={4:R}, ConcurrentElapsed={5:R}, LaneDirection={6}, Updates=[{7}]",
                    InvalidState,
                    JumpVelocity,
                    GravityAcceleration,
                    ForwardSpeed,
                    LaneDuration,
                    ConcurrentElapsed,
                    LaneDirection,
                    string.Join(",", elapsed));
            }
        }

        private sealed class Coverage
        {
            public readonly HashSet<PlayerState> InvalidStates = new HashSet<PlayerState>();
            public bool SawZeroElapsedUpdate;
            public bool SawPositiveElapsedUpdate;
            public bool SawAirborneNonPositiveVelocity;
            public bool SawGroundedPositiveVelocity;
            public bool SawValidLanding;
            public bool SawConcurrentForwardAndLaneProgress;
        }

        private sealed class JumpControllerSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.JumpController";

            private readonly object instance;
            private readonly PropertyInfo snapshot;
            private readonly MethodInfo requestJump;
            private readonly MethodInfo advance;
            private readonly MethodInfo resolveLanding;

            private JumpControllerSeam(object instance, Type type)
            {
                this.instance = instance;
                snapshot = RequiredProperty(type, "Snapshot", typeof(PlayerSnapshot));
                requestJump = RequiredMethod(type, "RequestJump");
                Assert.That(requestJump.ReturnType, Is.EqualTo(typeof(ActionRequestResult)));
                advance = RequiredMethod(type, "Advance", typeof(float));
                Assert.That(advance.ReturnType, Is.EqualTo(typeof(void)));
                resolveLanding = RequiredMethod(type, "ResolveLanding", typeof(bool));
                Assert.That(resolveLanding.ReturnType, Is.EqualTo(typeof(bool)));
            }

            public PlayerSnapshot Snapshot =>
                (PlayerSnapshot)snapshot.GetValue(instance);

            public static JumpControllerSeam Create(
                PlayerSnapshot initialSnapshot,
                float jumpVelocity,
                float gravityAcceleration)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 4.7 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[]
                {
                    typeof(PlayerSnapshot),
                    typeof(float),
                    typeof(float)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose JumpController(PlayerSnapshot initialSnapshot, " +
                    "float jumpVelocity, float gravityAcceleration).");
                return new JumpControllerSeam(
                    constructor.Invoke(new object[]
                    {
                        initialSnapshot,
                        jumpVelocity,
                        gravityAcceleration
                    }),
                    type);
            }

            public ActionRequestResult RequestJump()
            {
                return (ActionRequestResult)requestJump.Invoke(instance, Array.Empty<object>());
            }

            public void Advance(float elapsedSimulationTime)
            {
                advance.Invoke(instance, new object[] { elapsedSimulationTime });
            }

            public bool ResolveLanding(bool grounded)
            {
                return (bool)resolveLanding.Invoke(instance, new object[] { grounded });
            }

            private static PropertyInfo RequiredProperty(
                Type type,
                string name,
                Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null, type.FullName + " must expose " + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType));
                return property;
            }

            private static MethodInfo RequiredMethod(
                Type type,
                string name,
                params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    parameterTypes,
                    null);
                Assert.That(method, Is.Not.Null, type.FullName + " must expose " + name + ".");
                return method;
            }
        }
    }
}