using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class RejectedCommandPreservationPropertyTests
    {
        private const int Seed = 280033;
        private const int CaseCount = 161;
        private const string DuplicateRequestId = "duplicate-reset";
        private int generatedCaseIndex;

        // **Validates: Requirements 3.10, 4.6, 4.7, 5.11, 5.12, 6.9-6.18, 12.18, 14.5, 14.13**
        [Test]
        [Description("Feature: player-controller, Property 8: Repeated or invalid actions preserve the complete snapshot")]
        public void RepeatedOrInvalidActionsPreserveCompleteSnapshot_Property8()
        {
            generatedCaseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                AssertProperty,
                render: generated => generated.ToString());
        }

        private RejectedCase Generate(Random random)
        {
            var categories = Enum.GetValues(typeof(RejectionCategory));
            var category = generatedCaseIndex < categories.Length
                ? (RejectionCategory)generatedCaseIndex
                : (RejectionCategory)random.Next(categories.Length);
            generatedCaseIndex++;

            var state = StateFor(category);
            var grounded = category != RejectionCategory.JumpRunningAirborne &&
                category != RejectionCategory.SlideRunningAirborne && random.Next(2) == 1;
            var requestId = RequestIdFor(category, generatedCaseIndex);
            var completedIds = IsDuplicateReset(category)
                ? new ImmutableValueSequence<string>(new[] { DuplicateRequestId })
                : ImmutableValueSequence<string>.Empty;

            return new RejectedCase(
                category,
                CreateReachableSnapshot(random, state, grounded),
                requestId,
                completedIds,
                ExpectedCommand(category),
                ExpectedReason(category));
        }

        private static void AssertProperty(RejectedCase generated)
        {
            var transitionEvents = 0;
            var seam = PlayerStateMachineSeam.Create(
                generated.Before,
                generated.CompletedResetRequestIds,
                _ => transitionEvents++);

            var result = seam.Execute(generated.Category, generated.RequestId);
            var after = seam.Snapshot;

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected),
                "Every generated invalid or repeated action must be observably rejected.");
            Assert.That(result.Command, Is.EqualTo(generated.ExpectedCommand));
            Assert.That(result.Reason, Is.EqualTo(generated.ExpectedReason));
            Assert.That(result.CurrentState, Is.EqualTo(generated.Before.State));
            SnapshotAssert.Preserved(generated.Before, after,
                context: "A rejected command must preserve the complete PlayerSnapshot.");
            Assert.That(transitionEvents, Is.Zero,
                "A rejected command or transition must publish no state transition event.");

            if (generated.Category == RejectionCategory.SlideRepeatedSliding)
            {
                Assert.That(after.SlideElapsedTime, Is.EqualTo(generated.Before.SlideElapsedTime),
                    "A repeated slide must not reset elapsed time or extend the slide timer.");
            }
        }

        private static PlayerSnapshot CreateReachableSnapshot(
            Random random,
            PlayerState state,
            bool grounded)
        {
            var position = NextVector(random, -500f, 500f);
            var rotation = Quaternion.Euler(NextVector(random, -180f, 180f));
            var currentLane = (LogicalLane)random.Next(3);
            var targetLane = (LogicalLane)random.Next(3);
            var lateral = GeneratedValues.NextFiniteFloat(random, -4f, 4f);
            var segmentStart = GeneratedValues.NextFiniteFloat(random, -4f, 4f);
            var segmentTarget = GeneratedValues.NextFiniteFloat(random, -4f, 4f);
            var progress = GeneratedValues.NextFiniteFloat(random, 0f, 1f);
            var verticalVelocity = state == PlayerState.Failed
                ? 0f
                : GeneratedValues.NextFiniteFloat(random, -30f, 30f);
            var slideElapsed = state == PlayerState.Sliding
                ? GeneratedValues.NextFiniteFloat(random, 0.001f, 5f)
                : 0f;
            var collider = state == PlayerState.Sliding
                ? new ColliderProfile(0.35f, 1f, new Vector3(0f, 0.5f, 0f))
                : new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f));
            var laneQueue = state == PlayerState.Failed
                ? ImmutableValueSequence<LaneRequest>.Empty
                : new ImmutableValueSequence<LaneRequest>(new[]
                {
                    new LaneRequest(LaneDirection.Left, (ulong)random.Next(1, 1000)),
                    new LaneRequest(LaneDirection.Right, (ulong)random.Next(1000, 2000))
                });
            var pending = state == PlayerState.Failed
                ? ImmutableValueSequence<PlayerCommandKind>.Empty
                : new ImmutableValueSequence<PlayerCommandKind>(new[]
                {
                    PlayerCommandKind.Lane,
                    PlayerCommandKind.Jump
                });

            return new PlayerSnapshot(
                position,
                rotation,
                state,
                grounded,
                GeneratedValues.NextFiniteFloat(random, 0f, 100f),
                currentLane,
                targetLane,
                lateral,
                segmentStart,
                segmentTarget,
                progress,
                verticalVelocity,
                slideElapsed,
                collider,
                laneQueue,
                pending,
                state == PlayerState.Resetting);
        }

        private static Vector3 NextVector(Random random, float minimum, float maximum)
        {
            return new Vector3(
                GeneratedValues.NextFiniteFloat(random, minimum, maximum),
                GeneratedValues.NextFiniteFloat(random, minimum, maximum),
                GeneratedValues.NextFiniteFloat(random, minimum, maximum));
        }

        private static PlayerState StateFor(RejectionCategory category)
        {
            switch (category)
            {
                case RejectionCategory.LaneFailed:
                case RejectionCategory.JumpFailed:
                case RejectionCategory.SlideFailed:
                case RejectionCategory.FailureFailed:
                case RejectionCategory.ResetMissingFailed:
                case RejectionCategory.ResetDuplicateFailed:
                    return PlayerState.Failed;
                case RejectionCategory.LaneResetting:
                case RejectionCategory.JumpResetting:
                case RejectionCategory.SlideResetting:
                case RejectionCategory.FailureResetting:
                case RejectionCategory.ResetInProgressResetting:
                    return PlayerState.Resetting;
                case RejectionCategory.JumpJumping:
                case RejectionCategory.SlideJumping:
                case RejectionCategory.ResetMissingJumping:
                case RejectionCategory.ResetDuplicateJumping:
                    return PlayerState.Jumping;
                case RejectionCategory.JumpSliding:
                case RejectionCategory.SlideRepeatedSliding:
                case RejectionCategory.ResetMissingSliding:
                case RejectionCategory.ResetDuplicateSliding:
                    return PlayerState.Sliding;
                default:
                    return PlayerState.Running;
            }
        }

        private static PlayerCommandKind ExpectedCommand(RejectionCategory category)
        {
            switch (category)
            {
                case RejectionCategory.LaneFailed:
                case RejectionCategory.LaneResetting:
                    return PlayerCommandKind.Lane;
                case RejectionCategory.JumpRunningAirborne:
                case RejectionCategory.JumpJumping:
                case RejectionCategory.JumpSliding:
                case RejectionCategory.JumpFailed:
                case RejectionCategory.JumpResetting:
                    return PlayerCommandKind.Jump;
                case RejectionCategory.SlideRunningAirborne:
                case RejectionCategory.SlideJumping:
                case RejectionCategory.SlideRepeatedSliding:
                case RejectionCategory.SlideFailed:
                case RejectionCategory.SlideResetting:
                    return PlayerCommandKind.Slide;
                case RejectionCategory.FailureFailed:
                case RejectionCategory.FailureResetting:
                    return PlayerCommandKind.Failure;
                default:
                    return PlayerCommandKind.Reset;
            }
        }

        private static RejectionReason ExpectedReason(RejectionCategory category)
        {
            if (category == RejectionCategory.JumpRunningAirborne ||
                category == RejectionCategory.SlideRunningAirborne)
                return RejectionReason.NotGrounded;
            if (category == RejectionCategory.ResetMissingRunning ||
                category == RejectionCategory.ResetMissingJumping ||
                category == RejectionCategory.ResetMissingSliding ||
                category == RejectionCategory.ResetMissingFailed)
                return RejectionReason.MissingRequestId;
            if (IsDuplicateReset(category)) return RejectionReason.DuplicateRequestId;
            if (category == RejectionCategory.ResetInProgressResetting)
                return RejectionReason.ResetInProgress;
            return RejectionReason.InvalidState;
        }

        private static bool IsDuplicateReset(RejectionCategory category)
        {
            return category == RejectionCategory.ResetDuplicateRunning ||
                category == RejectionCategory.ResetDuplicateJumping ||
                category == RejectionCategory.ResetDuplicateSliding ||
                category == RejectionCategory.ResetDuplicateFailed;
        }

        private static string RequestIdFor(RejectionCategory category, int index)
        {
            if (category == RejectionCategory.ResetMissingRunning ||
                category == RejectionCategory.ResetMissingJumping ||
                category == RejectionCategory.ResetMissingSliding ||
                category == RejectionCategory.ResetMissingFailed)
                return index % 2 == 0 ? null : string.Empty;
            if (IsDuplicateReset(category)) return DuplicateRequestId;
            return "reset-" + index.ToString(CultureInfo.InvariantCulture);
        }

        private enum RejectionCategory
        {
            LaneFailed,
            LaneResetting,
            JumpRunningAirborne,
            JumpJumping,
            JumpSliding,
            JumpFailed,
            JumpResetting,
            SlideRunningAirborne,
            SlideJumping,
            SlideRepeatedSliding,
            SlideFailed,
            SlideResetting,
            FailureFailed,
            FailureResetting,
            ResetMissingRunning,
            ResetMissingJumping,
            ResetMissingSliding,
            ResetMissingFailed,
            ResetDuplicateRunning,
            ResetDuplicateJumping,
            ResetDuplicateSliding,
            ResetDuplicateFailed,
            ResetInProgressResetting
        }

        private readonly struct RejectedCase
        {
            public RejectedCase(
                RejectionCategory category,
                PlayerSnapshot before,
                string requestId,
                ImmutableValueSequence<string> completedResetRequestIds,
                PlayerCommandKind expectedCommand,
                RejectionReason expectedReason)
            {
                Category = category;
                Before = before;
                RequestId = requestId;
                CompletedResetRequestIds = completedResetRequestIds;
                ExpectedCommand = expectedCommand;
                ExpectedReason = expectedReason;
            }

            public RejectionCategory Category { get; }
            public PlayerSnapshot Before { get; }
            public string RequestId { get; }
            public ImmutableValueSequence<string> CompletedResetRequestIds { get; }
            public PlayerCommandKind ExpectedCommand { get; }
            public RejectionReason ExpectedReason { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Category={0}, State={1}, Grounded={2}, SlideElapsed={3:R}, RequestId={4}",
                    Category,
                    Before.State,
                    Before.IsGrounded,
                    Before.SlideElapsedTime,
                    RequestId ?? "<null>");
            }
        }

        private readonly struct CommandObservation
        {
            public CommandObservation(
                CommandStatus status,
                PlayerCommandKind command,
                RejectionReason reason,
                PlayerState currentState)
            {
                Status = status;
                Command = command;
                Reason = reason;
                CurrentState = currentState;
            }

            public CommandStatus Status { get; }
            public PlayerCommandKind Command { get; }
            public RejectionReason Reason { get; }
            public PlayerState CurrentState { get; }

            public static CommandObservation From(ActionRequestResult result)
            {
                return new CommandObservation(
                    result.Status, result.Command, result.Reason, result.CurrentState);
            }

            public static CommandObservation From(FailureCommandResult result)
            {
                return new CommandObservation(
                    result.Status, result.Command, result.Reason, result.CurrentState);
            }

            public static CommandObservation From(ResetRequestResult result)
            {
                return new CommandObservation(
                    result.Status, result.Command, result.Reason, result.CurrentState);
            }
        }

        private sealed class PlayerStateMachineSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.PlayerStateMachine";

            private readonly object instance;
            private readonly PropertyInfo snapshotProperty;
            private readonly MethodInfo requestLane;
            private readonly MethodInfo requestJump;
            private readonly MethodInfo requestSlide;
            private readonly MethodInfo requestFailure;
            private readonly MethodInfo requestReset;

            private PlayerStateMachineSeam(
                object instance,
                PropertyInfo snapshotProperty,
                MethodInfo requestLane,
                MethodInfo requestJump,
                MethodInfo requestSlide,
                MethodInfo requestFailure,
                MethodInfo requestReset)
            {
                this.instance = instance;
                this.snapshotProperty = snapshotProperty;
                this.requestLane = requestLane;
                this.requestJump = requestJump;
                this.requestSlide = requestSlide;
                this.requestFailure = requestFailure;
                this.requestReset = requestReset;
            }

            public PlayerSnapshot Snapshot =>
                (PlayerSnapshot)snapshotProperty.GetValue(instance);

            public static PlayerStateMachineSeam Create(
                PlayerSnapshot initialSnapshot,
                ImmutableValueSequence<string> completedResetRequestIds,
                Action<PlayerStateChangedEvent> transitionSink)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 3.6 must provide " + RuntimeTypeName + ".");

                var constructor = type.GetConstructor(new[]
                {
                    typeof(PlayerSnapshot),
                    typeof(ImmutableValueSequence<string>)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose PlayerStateMachine(" +
                    "PlayerSnapshot, ImmutableValueSequence<string>).");

                var instance = constructor.Invoke(new object[]
                {
                    initialSnapshot,
                    completedResetRequestIds
                });
                var snapshot = RequiredProperty(type, "Snapshot", typeof(PlayerSnapshot));
                var stateChanged = type.GetEvent("StateChanged", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(stateChanged, Is.Not.Null,
                    RuntimeTypeName + " must expose StateChanged for rejected-transition suppression.");
                stateChanged.AddEventHandler(instance, transitionSink);

                return new PlayerStateMachineSeam(
                    instance,
                    snapshot,
                    RequiredMethod(type, "RequestLane", typeof(LaneDirection)),
                    RequiredMethod(type, "RequestJump"),
                    RequiredMethod(type, "RequestSlide"),
                    RequiredMethod(type, "RequestFailure"),
                    RequiredMethod(type, "RequestReset", typeof(string)));
            }

            public CommandObservation Execute(RejectionCategory category, string requestId)
            {
                switch (ExpectedCommand(category))
                {
                    case PlayerCommandKind.Lane:
                        var direction = category == RejectionCategory.LaneFailed
                            ? LaneDirection.Left
                            : LaneDirection.Right;
                        return CommandObservation.From((ActionRequestResult)requestLane.Invoke(
                            instance, new object[] { direction }));
                    case PlayerCommandKind.Jump:
                        return CommandObservation.From((ActionRequestResult)requestJump.Invoke(
                            instance, Array.Empty<object>()));
                    case PlayerCommandKind.Slide:
                        return CommandObservation.From((ActionRequestResult)requestSlide.Invoke(
                            instance, Array.Empty<object>()));
                    case PlayerCommandKind.Failure:
                        return CommandObservation.From((FailureCommandResult)requestFailure.Invoke(
                            instance, Array.Empty<object>()));
                    case PlayerCommandKind.Reset:
                        return CommandObservation.From((ResetRequestResult)requestReset.Invoke(
                            instance, new object[] { requestId }));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(category), category, null);
                }
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
                Assert.That(method, Is.Not.Null,
                    type.FullName + " must expose " + name + ".");
                return method;
            }
        }
    }
}
