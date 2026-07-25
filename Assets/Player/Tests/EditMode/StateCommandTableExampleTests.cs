using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class StateCommandTableExampleTests
    {
        private const string DuplicateResetId = "completed-reset";
        private const string ValidResetId = "new-reset";

        // **Validates: Requirements 6.1-6.14, 12.3, 14.4, 14.5, 14.13**
        [TestCaseSource(nameof(PublicStateCommandCases))]
        public void PublicStateCommandTableReturnsSpecifiedResult(StateCommandCase example)
        {
            var transitionEvents = 0;
            var before = CreateSentinelSnapshot(example.State, example.Grounded);
            var completedIds = example.DuplicateReset
                ? new ImmutableValueSequence<string>(new[] { DuplicateResetId })
                : ImmutableValueSequence<string>.Empty;
            var seam = PlayerStateMachineSeam.Create(before, completedIds, _ => transitionEvents++);

            var actual = seam.Execute(example.Command, example.RequestId);

            Assert.That(actual.Status, Is.EqualTo(example.ExpectedStatus));
            Assert.That(actual.Command, Is.EqualTo(ExpectedCommandKind(example.Command)));
            Assert.That(actual.Reason, Is.EqualTo(example.ExpectedReason));
            Assert.That(actual.CurrentState, Is.EqualTo(example.ExpectedState));
            Assert.That(seam.Snapshot.State, Is.EqualTo(example.ExpectedState));
            Assert.That(seam.CurrentState, Is.EqualTo(example.ExpectedState));
            Assert.That(transitionEvents, Is.EqualTo(example.ExpectedTransitionEvents));

            if (example.ExpectedStatus == CommandStatus.Rejected)
                Assert.That(seam.Snapshot, Is.EqualTo(before),
                    "A rejected table entry must preserve the complete snapshot.");
        }

        // **Validates: Requirements 6.2-6.9, 12.3, 14.4**
        [TestCaseSource(nameof(InternalTransitionCases))]
        public void InternalTransitionTableReturnsSpecifiedResult(InternalTransitionCase example)
        {
            var transitionEvents = 0;
            var before = CreateSentinelSnapshot(example.State, example.Grounded);
            var seam = PlayerStateMachineSeam.Create(
                before,
                ImmutableValueSequence<string>.Empty,
                _ => transitionEvents++);

            var actual = seam.ExecuteInternal(example.Transition, example.SafeRestoration);

            Assert.That(actual.Status, Is.EqualTo(example.ExpectedStatus));
            Assert.That(actual.CurrentState, Is.EqualTo(example.ExpectedState));
            Assert.That(seam.CurrentState, Is.EqualTo(example.ExpectedState));
            Assert.That(transitionEvents, Is.EqualTo(example.ExpectedTransitionEvents));

            if (example.ExpectedStatus == CommandStatus.Rejected)
                Assert.That(seam.Snapshot, Is.EqualTo(before),
                    "An unspecified internal transition must preserve the complete snapshot.");
        }

        // **Validates: Requirements 6.6, 6.15, 14.4**
        [TestCase(PlayerState.Running)]
        [TestCase(PlayerState.Jumping)]
        [TestCase(PlayerState.Sliding)]
        public void AcceptedFailureClearsVelocityQueuesAndPendingActions(PlayerState initialState)
        {
            var transitionEvents = 0;
            var before = CreateSentinelSnapshot(initialState, initialState == PlayerState.Running);
            var seam = PlayerStateMachineSeam.Create(
                before,
                ImmutableValueSequence<string>.Empty,
                _ => transitionEvents++);

            var result = seam.Execute(StateCommand.Failure, null);
            var after = seam.Snapshot;

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Failed));
            Assert.That(after.State, Is.EqualTo(PlayerState.Failed));
            Assert.That(after.Position, Is.EqualTo(before.Position));
            Assert.That(after.Rotation, Is.EqualTo(before.Rotation));
            Assert.That(after.VerticalVelocity, Is.Zero);
            Assert.That(after.LaneRequestQueue.Count, Is.Zero);
            Assert.That(after.PendingActionRequests.Count, Is.Zero);
            Assert.That(transitionEvents, Is.EqualTo(1));
        }

        // **Validates: Requirements 6.13, 6.16-6.18, 12.18, 14.5, 14.13**
        [Test]
        public void FailedPreservesTransformAndZeroVelocityAcrossRejectedCommands()
        {
            var transitionEvents = 0;
            var expected = CreateSentinelSnapshot(PlayerState.Failed, false);
            var seam = PlayerStateMachineSeam.Create(
                expected,
                ImmutableValueSequence<string>.Empty,
                _ => transitionEvents++);

            foreach (var command in new[]
            {
                StateCommand.Lane,
                StateCommand.Jump,
                StateCommand.Slide,
                StateCommand.Failure
            })
            {
                var result = seam.Execute(command, null);
                Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected), command.ToString());
                Assert.That(result.Reason, Is.EqualTo(RejectionReason.InvalidState), command.ToString());
                Assert.That(seam.Snapshot, Is.EqualTo(expected), command.ToString());
                Assert.That(seam.Snapshot.Position, Is.EqualTo(expected.Position), command.ToString());
                Assert.That(seam.Snapshot.Rotation, Is.EqualTo(expected.Rotation), command.ToString());
                Assert.That(seam.Snapshot.VerticalVelocity, Is.Zero, command.ToString());
            }

            Assert.That(transitionEvents, Is.Zero,
                "Rejected commands in Failed must publish no state events.");
        }

        // **Validates: Requirements 6.14, 6.17, 6.18, 12.3, 12.18, 14.5, 14.13**
        [Test]
        public void ResettingRejectsEveryPublicStateCommandWithoutMutationOrEvents()
        {
            var transitionEvents = 0;
            var expected = CreateSentinelSnapshot(PlayerState.Resetting, false);
            var seam = PlayerStateMachineSeam.Create(
                expected,
                ImmutableValueSequence<string>.Empty,
                _ => transitionEvents++);

            foreach (var command in new[]
            {
                StateCommand.Lane,
                StateCommand.Jump,
                StateCommand.Slide,
                StateCommand.Failure,
                StateCommand.Reset
            })
            {
                var result = seam.Execute(command, ValidResetId);
                Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected), command.ToString());
                Assert.That(result.Reason,
                    Is.EqualTo(command == StateCommand.Reset
                        ? RejectionReason.ResetInProgress
                        : RejectionReason.InvalidState),
                    command.ToString());
                Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Resetting), command.ToString());
                Assert.That(seam.Snapshot, Is.EqualTo(expected), command.ToString());
            }

            Assert.That(transitionEvents, Is.Zero,
                "Resetting rejections must publish no state events.");
        }

        // **Validates: Requirements 6.1, 14.9**
        [TestCase(PlayerState.Running)]
        [TestCase(PlayerState.Jumping)]
        [TestCase(PlayerState.Sliding)]
        [TestCase(PlayerState.Failed)]
        [TestCase(PlayerState.Resetting)]
        public void PublicStateQueryReturnsExactlyCurrentState(PlayerState state)
        {
            var seam = PlayerStateMachineSeam.Create(
                CreateSentinelSnapshot(state, state == PlayerState.Running),
                ImmutableValueSequence<string>.Empty,
                _ => { });

            Assert.That(seam.Instance, Is.InstanceOf<IPlayerStateQuery>());
            Assert.That(((IPlayerStateQuery)seam.Instance).CurrentState, Is.EqualTo(state));
            Assert.That(seam.CurrentState, Is.EqualTo(state));
            Assert.That(seam.Snapshot.State, Is.EqualTo(state));
        }

        private static IEnumerable<TestCaseData> PublicStateCommandCases
        {
            get
            {
                yield return PublicCase("Running_Lane_Accepted", PlayerState.Running, true,
                    StateCommand.Lane, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Running, 0);
                yield return PublicCase("Running_Jump_Grounded_Accepted", PlayerState.Running, true,
                    StateCommand.Jump, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Jumping, 1);
                yield return PublicCase("Running_Jump_Airborne_Rejected", PlayerState.Running, false,
                    StateCommand.Jump, null, false, CommandStatus.Rejected,
                    RejectionReason.NotGrounded, PlayerState.Running, 0);
                yield return PublicCase("Running_Slide_Grounded_Accepted", PlayerState.Running, true,
                    StateCommand.Slide, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Sliding, 1);
                yield return PublicCase("Running_Slide_Airborne_Rejected", PlayerState.Running, false,
                    StateCommand.Slide, null, false, CommandStatus.Rejected,
                    RejectionReason.NotGrounded, PlayerState.Running, 0);
                yield return PublicCase("Running_Failure_Accepted", PlayerState.Running, true,
                    StateCommand.Failure, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Failed, 1);
                yield return PublicCase("Running_Reset_Valid_Accepted", PlayerState.Running, true,
                    StateCommand.Reset, ValidResetId, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Resetting, 1);
                yield return PublicCase("Running_Reset_Missing_Rejected", PlayerState.Running, true,
                    StateCommand.Reset, string.Empty, false, CommandStatus.Rejected,
                    RejectionReason.MissingRequestId, PlayerState.Running, 0);
                yield return PublicCase("Running_Reset_Duplicate_Rejected", PlayerState.Running, true,
                    StateCommand.Reset, DuplicateResetId, true, CommandStatus.Rejected,
                    RejectionReason.DuplicateRequestId, PlayerState.Running, 0);

                yield return PublicCase("Jumping_Lane_Accepted", PlayerState.Jumping, false,
                    StateCommand.Lane, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Jumping, 0);
                yield return PublicCase("Jumping_Jump_Rejected", PlayerState.Jumping, false,
                    StateCommand.Jump, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Jumping, 0);
                yield return PublicCase("Jumping_Slide_Rejected", PlayerState.Jumping, false,
                    StateCommand.Slide, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Jumping, 0);
                yield return PublicCase("Jumping_Failure_Accepted", PlayerState.Jumping, false,
                    StateCommand.Failure, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Failed, 1);
                yield return PublicCase("Jumping_Reset_Valid_Accepted", PlayerState.Jumping, false,
                    StateCommand.Reset, ValidResetId, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Resetting, 1);
                yield return PublicCase("Jumping_Reset_Missing_Rejected", PlayerState.Jumping, false,
                    StateCommand.Reset, null, false, CommandStatus.Rejected,
                    RejectionReason.MissingRequestId, PlayerState.Jumping, 0);
                yield return PublicCase("Jumping_Reset_Duplicate_Rejected", PlayerState.Jumping, false,
                    StateCommand.Reset, DuplicateResetId, true, CommandStatus.Rejected,
                    RejectionReason.DuplicateRequestId, PlayerState.Jumping, 0);

                yield return PublicCase("Sliding_Lane_Accepted", PlayerState.Sliding, true,
                    StateCommand.Lane, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Sliding, 0);
                yield return PublicCase("Sliding_Jump_Rejected", PlayerState.Sliding, true,
                    StateCommand.Jump, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Sliding, 0);
                yield return PublicCase("Sliding_Slide_Rejected", PlayerState.Sliding, true,
                    StateCommand.Slide, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Sliding, 0);
                yield return PublicCase("Sliding_Failure_Accepted", PlayerState.Sliding, true,
                    StateCommand.Failure, null, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Failed, 1);
                yield return PublicCase("Sliding_Reset_Valid_Accepted", PlayerState.Sliding, true,
                    StateCommand.Reset, ValidResetId, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Resetting, 1);
                yield return PublicCase("Sliding_Reset_Missing_Rejected", PlayerState.Sliding, true,
                    StateCommand.Reset, string.Empty, false, CommandStatus.Rejected,
                    RejectionReason.MissingRequestId, PlayerState.Sliding, 0);
                yield return PublicCase("Sliding_Reset_Duplicate_Rejected", PlayerState.Sliding, true,
                    StateCommand.Reset, DuplicateResetId, true, CommandStatus.Rejected,
                    RejectionReason.DuplicateRequestId, PlayerState.Sliding, 0);

                yield return PublicCase("Failed_Lane_Rejected", PlayerState.Failed, false,
                    StateCommand.Lane, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Failed, 0);
                yield return PublicCase("Failed_Jump_Rejected", PlayerState.Failed, false,
                    StateCommand.Jump, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Failed, 0);
                yield return PublicCase("Failed_Slide_Rejected", PlayerState.Failed, false,
                    StateCommand.Slide, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Failed, 0);
                yield return PublicCase("Failed_Failure_Rejected", PlayerState.Failed, false,
                    StateCommand.Failure, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Failed, 0);
                yield return PublicCase("Failed_Reset_Valid_Accepted", PlayerState.Failed, false,
                    StateCommand.Reset, ValidResetId, false, CommandStatus.Accepted,
                    RejectionReason.None, PlayerState.Resetting, 1);
                yield return PublicCase("Failed_Reset_Missing_Rejected", PlayerState.Failed, false,
                    StateCommand.Reset, null, false, CommandStatus.Rejected,
                    RejectionReason.MissingRequestId, PlayerState.Failed, 0);
                yield return PublicCase("Failed_Reset_Duplicate_Rejected", PlayerState.Failed, false,
                    StateCommand.Reset, DuplicateResetId, true, CommandStatus.Rejected,
                    RejectionReason.DuplicateRequestId, PlayerState.Failed, 0);

                yield return PublicCase("Resetting_Lane_Rejected", PlayerState.Resetting, false,
                    StateCommand.Lane, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Resetting, 0);
                yield return PublicCase("Resetting_Jump_Rejected", PlayerState.Resetting, false,
                    StateCommand.Jump, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Resetting, 0);
                yield return PublicCase("Resetting_Slide_Rejected", PlayerState.Resetting, false,
                    StateCommand.Slide, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Resetting, 0);
                yield return PublicCase("Resetting_Failure_Rejected", PlayerState.Resetting, false,
                    StateCommand.Failure, null, false, CommandStatus.Rejected,
                    RejectionReason.InvalidState, PlayerState.Resetting, 0);
                yield return PublicCase("Resetting_Reset_Rejected", PlayerState.Resetting, false,
                    StateCommand.Reset, ValidResetId, false, CommandStatus.Rejected,
                    RejectionReason.ResetInProgress, PlayerState.Resetting, 0);
            }
        }

        private static IEnumerable<TestCaseData> InternalTransitionCases
        {
            get
            {
                yield return InternalCase("Running_Landing_Rejected", PlayerState.Running, true,
                    InternalTransition.Landing, false, CommandStatus.Rejected, PlayerState.Running, 0);
                yield return InternalCase("Jumping_Landing_Grounded_Accepted", PlayerState.Jumping, true,
                    InternalTransition.Landing, false, CommandStatus.Accepted, PlayerState.Running, 1);
                yield return InternalCase("Jumping_Landing_Airborne_Rejected", PlayerState.Jumping, false,
                    InternalTransition.Landing, false, CommandStatus.Rejected, PlayerState.Jumping, 0);
                yield return InternalCase("Sliding_Landing_Rejected", PlayerState.Sliding, true,
                    InternalTransition.Landing, false, CommandStatus.Rejected, PlayerState.Sliding, 0);
                yield return InternalCase("Failed_Landing_Rejected", PlayerState.Failed, false,
                    InternalTransition.Landing, false, CommandStatus.Rejected, PlayerState.Failed, 0);
                yield return InternalCase("Resetting_Landing_Rejected", PlayerState.Resetting, false,
                    InternalTransition.Landing, false, CommandStatus.Rejected, PlayerState.Resetting, 0);

                yield return InternalCase("Running_SafeRestoration_Rejected", PlayerState.Running, true,
                    InternalTransition.SafeRestoration, true, CommandStatus.Rejected, PlayerState.Running, 0);
                yield return InternalCase("Jumping_SafeRestoration_Rejected", PlayerState.Jumping, false,
                    InternalTransition.SafeRestoration, true, CommandStatus.Rejected, PlayerState.Jumping, 0);
                yield return InternalCase("Sliding_SafeRestoration_Accepted", PlayerState.Sliding, true,
                    InternalTransition.SafeRestoration, true, CommandStatus.Accepted, PlayerState.Running, 1);
                yield return InternalCase("Sliding_UnsafeRestoration_Rejected", PlayerState.Sliding, true,
                    InternalTransition.SafeRestoration, false, CommandStatus.Rejected, PlayerState.Sliding, 0);
                yield return InternalCase("Failed_SafeRestoration_Rejected", PlayerState.Failed, false,
                    InternalTransition.SafeRestoration, true, CommandStatus.Rejected, PlayerState.Failed, 0);
                yield return InternalCase("Resetting_SafeRestoration_Rejected", PlayerState.Resetting, false,
                    InternalTransition.SafeRestoration, true, CommandStatus.Rejected, PlayerState.Resetting, 0);

                yield return InternalCase("Running_ResetCompleted_Rejected", PlayerState.Running, true,
                    InternalTransition.ResetCompleted, false, CommandStatus.Rejected, PlayerState.Running, 0);
                yield return InternalCase("Jumping_ResetCompleted_Rejected", PlayerState.Jumping, false,
                    InternalTransition.ResetCompleted, false, CommandStatus.Rejected, PlayerState.Jumping, 0);
                yield return InternalCase("Sliding_ResetCompleted_Rejected", PlayerState.Sliding, true,
                    InternalTransition.ResetCompleted, false, CommandStatus.Rejected, PlayerState.Sliding, 0);
                yield return InternalCase("Failed_ResetCompleted_Rejected", PlayerState.Failed, false,
                    InternalTransition.ResetCompleted, false, CommandStatus.Rejected, PlayerState.Failed, 0);
                yield return InternalCase("Resetting_ResetCompleted_Accepted", PlayerState.Resetting, false,
                    InternalTransition.ResetCompleted, false, CommandStatus.Accepted, PlayerState.Running, 1);
            }
        }

        private static TestCaseData PublicCase(
            string name,
            PlayerState state,
            bool grounded,
            StateCommand command,
            string requestId,
            bool duplicateReset,
            CommandStatus expectedStatus,
            RejectionReason expectedReason,
            PlayerState expectedState,
            int expectedTransitionEvents)
        {
            return new TestCaseData(new StateCommandCase(
                state,
                grounded,
                command,
                requestId,
                duplicateReset,
                expectedStatus,
                expectedReason,
                expectedState,
                expectedTransitionEvents)).SetName("StateCommandTable_" + name);
        }

        private static TestCaseData InternalCase(
            string name,
            PlayerState state,
            bool grounded,
            InternalTransition transition,
            bool safeRestoration,
            CommandStatus expectedStatus,
            PlayerState expectedState,
            int expectedTransitionEvents)
        {
            return new TestCaseData(new InternalTransitionCase(
                state,
                grounded,
                transition,
                safeRestoration,
                expectedStatus,
                expectedState,
                expectedTransitionEvents)).SetName("StateTransitionTable_" + name);
        }

        private static PlayerCommandKind ExpectedCommandKind(StateCommand command)
        {
            switch (command)
            {
                case StateCommand.Lane: return PlayerCommandKind.Lane;
                case StateCommand.Jump: return PlayerCommandKind.Jump;
                case StateCommand.Slide: return PlayerCommandKind.Slide;
                case StateCommand.Failure: return PlayerCommandKind.Failure;
                case StateCommand.Reset: return PlayerCommandKind.Reset;
                default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }

        private static PlayerSnapshot CreateSentinelSnapshot(PlayerState state, bool grounded)
        {
            var laneQueue = state == PlayerState.Failed
                ? ImmutableValueSequence<LaneRequest>.Empty
                : new ImmutableValueSequence<LaneRequest>(new[]
                {
                    new LaneRequest(LaneDirection.Left, 41UL),
                    new LaneRequest(LaneDirection.Right, 42UL)
                });
            var pendingActions = state == PlayerState.Failed
                ? ImmutableValueSequence<PlayerCommandKind>.Empty
                : new ImmutableValueSequence<PlayerCommandKind>(new[]
                {
                    PlayerCommandKind.Lane,
                    PlayerCommandKind.Jump
                });

            return new PlayerSnapshot(
                new Vector3(12.5f, 3.25f, -7.75f),
                Quaternion.Euler(15f, 35f, 5f),
                state,
                grounded,
                13.5f,
                LogicalLane.Left,
                LogicalLane.Right,
                1.25f,
                -2f,
                2f,
                0.375f,
                state == PlayerState.Failed
                    ? 0f
                    : state == PlayerState.Jumping && grounded ? -0.5f : 8.5f,
                state == PlayerState.Sliding ? 0.75f : 0f,
                state == PlayerState.Sliding
                    ? new ColliderProfile(0.35f, 1f, new Vector3(0f, 0.5f, 0f))
                    : new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                laneQueue,
                pendingActions,
                state == PlayerState.Resetting);
        }

        public enum StateCommand
        {
            Lane,
            Jump,
            Slide,
            Failure,
            Reset
        }

        public enum InternalTransition
        {
            Landing,
            SafeRestoration,
            ResetCompleted
        }

        public sealed class StateCommandCase
        {
            public StateCommandCase(
                PlayerState state,
                bool grounded,
                StateCommand command,
                string requestId,
                bool duplicateReset,
                CommandStatus expectedStatus,
                RejectionReason expectedReason,
                PlayerState expectedState,
                int expectedTransitionEvents)
            {
                State = state;
                Grounded = grounded;
                Command = command;
                RequestId = requestId;
                DuplicateReset = duplicateReset;
                ExpectedStatus = expectedStatus;
                ExpectedReason = expectedReason;
                ExpectedState = expectedState;
                ExpectedTransitionEvents = expectedTransitionEvents;
            }

            public PlayerState State { get; }
            public bool Grounded { get; }
            public StateCommand Command { get; }
            public string RequestId { get; }
            public bool DuplicateReset { get; }
            public CommandStatus ExpectedStatus { get; }
            public RejectionReason ExpectedReason { get; }
            public PlayerState ExpectedState { get; }
            public int ExpectedTransitionEvents { get; }
        }

        public sealed class InternalTransitionCase
        {
            public InternalTransitionCase(
                PlayerState state,
                bool grounded,
                InternalTransition transition,
                bool safeRestoration,
                CommandStatus expectedStatus,
                PlayerState expectedState,
                int expectedTransitionEvents)
            {
                State = state;
                Grounded = grounded;
                Transition = transition;
                SafeRestoration = safeRestoration;
                ExpectedStatus = expectedStatus;
                ExpectedState = expectedState;
                ExpectedTransitionEvents = expectedTransitionEvents;
            }

            public PlayerState State { get; }
            public bool Grounded { get; }
            public InternalTransition Transition { get; }
            public bool SafeRestoration { get; }
            public CommandStatus ExpectedStatus { get; }
            public PlayerState ExpectedState { get; }
            public int ExpectedTransitionEvents { get; }
        }

        private readonly struct CommandObservation
        {
            public CommandObservation(
                CommandStatus status,
                PlayerCommandKind command,
                RejectionReason reason,
                PlayerState currentState,
                string requestId)
            {
                Status = status;
                Command = command;
                Reason = reason;
                CurrentState = currentState;
                RequestId = requestId;
            }

            public CommandStatus Status { get; }
            public PlayerCommandKind Command { get; }
            public RejectionReason Reason { get; }
            public PlayerState CurrentState { get; }
            public string RequestId { get; }

            public static CommandObservation From(ActionRequestResult result)
            {
                return new CommandObservation(
                    result.Status,
                    result.Command,
                    result.Reason,
                    result.CurrentState,
                    null);
            }

            public static CommandObservation From(FailureCommandResult result)
            {
                return new CommandObservation(
                    result.Status,
                    result.Command,
                    result.Reason,
                    result.CurrentState,
                    null);
            }

            public static CommandObservation From(ResetRequestResult result)
            {
                return new CommandObservation(
                    result.Status,
                    result.Command,
                    result.Reason,
                    result.CurrentState,
                    result.RequestId);
            }
        }

        private readonly struct TransitionObservation
        {
            public TransitionObservation(CommandStatus status, PlayerState currentState)
            {
                Status = status;
                CurrentState = currentState;
            }

            public CommandStatus Status { get; }
            public PlayerState CurrentState { get; }
        }

        private sealed class PlayerStateMachineSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.PlayerStateMachine";

            private readonly object instance;
            private readonly PropertyInfo snapshotProperty;
            private readonly PropertyInfo currentStateProperty;
            private readonly MethodInfo requestLane;
            private readonly MethodInfo requestJump;
            private readonly MethodInfo requestSlide;
            private readonly MethodInfo requestFailure;
            private readonly MethodInfo requestReset;
            private readonly MethodInfo resolveLanding;
            private readonly MethodInfo resolveSlideRestoration;
            private readonly MethodInfo completeReset;

            private PlayerStateMachineSeam(object instance, Type type)
            {
                this.instance = instance;
                snapshotProperty = RequiredProperty(type, "Snapshot", typeof(PlayerSnapshot));
                currentStateProperty = RequiredProperty(type, "CurrentState", typeof(PlayerState));
                requestLane = RequiredMethod(type, "RequestLane", typeof(LaneDirection));
                requestJump = RequiredMethod(type, "RequestJump");
                requestSlide = RequiredMethod(type, "RequestSlide");
                requestFailure = RequiredMethod(type, "RequestFailure");
                requestReset = RequiredMethod(type, "RequestReset", typeof(string));
                resolveLanding = RequiredMethod(type, "ResolveLanding");
                resolveSlideRestoration = RequiredMethod(
                    type, "ResolveSlideRestoration", typeof(bool));
                completeReset = RequiredMethod(type, "CompleteReset");
            }

            public object Instance => instance;

            public PlayerSnapshot Snapshot =>
                (PlayerSnapshot)snapshotProperty.GetValue(instance);

            public PlayerState CurrentState =>
                (PlayerState)currentStateProperty.GetValue(instance);

            public static PlayerStateMachineSeam Create(
                PlayerSnapshot initialSnapshot,
                ImmutableValueSequence<string> completedResetRequestIds,
                Action<PlayerStateChangedEvent> transitionSink)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 3.6 must provide " + RuntimeTypeName + ".");
                Assert.That(typeof(IPlayerStateQuery).IsAssignableFrom(type), Is.True,
                    RuntimeTypeName + " must expose the public player-state query contract.");

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
                var stateChanged = type.GetEvent(
                    "StateChanged", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(stateChanged, Is.Not.Null,
                    RuntimeTypeName + " must expose StateChanged.");
                stateChanged.AddEventHandler(instance, transitionSink);
                return new PlayerStateMachineSeam(instance, type);
            }

            public CommandObservation Execute(StateCommand command, string requestId)
            {
                switch (command)
                {
                    case StateCommand.Lane:
                        return CommandObservation.From((ActionRequestResult)requestLane.Invoke(
                            instance, new object[] { LaneDirection.Right }));
                    case StateCommand.Jump:
                        return CommandObservation.From((ActionRequestResult)requestJump.Invoke(
                            instance, Array.Empty<object>()));
                    case StateCommand.Slide:
                        return CommandObservation.From((ActionRequestResult)requestSlide.Invoke(
                            instance, Array.Empty<object>()));
                    case StateCommand.Failure:
                        return CommandObservation.From((FailureCommandResult)requestFailure.Invoke(
                            instance, Array.Empty<object>()));
                    case StateCommand.Reset:
                        return CommandObservation.From((ResetRequestResult)requestReset.Invoke(
                            instance, new object[] { requestId }));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(command), command, null);
                }
            }

            public TransitionObservation ExecuteInternal(
                InternalTransition transition,
                bool safeRestoration)
            {
                MethodInfo method;
                object[] arguments;
                switch (transition)
                {
                    case InternalTransition.Landing:
                        method = resolveLanding;
                        arguments = Array.Empty<object>();
                        break;
                    case InternalTransition.SafeRestoration:
                        method = resolveSlideRestoration;
                        arguments = new object[] { safeRestoration };
                        break;
                    case InternalTransition.ResetCompleted:
                        method = completeReset;
                        arguments = Array.Empty<object>();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(transition), transition, null);
                }

                var previousState = CurrentState;
                var result = method.Invoke(instance, arguments);
                var resultingState = CurrentState;
                if (result is bool accepted)
                {
                    return new TransitionObservation(
                        accepted ? CommandStatus.Accepted : CommandStatus.Rejected,
                        resultingState);
                }

                if (result != null)
                {
                    var status = result.GetType().GetProperty(
                        "Status", BindingFlags.Instance | BindingFlags.Public);
                    var currentState = result.GetType().GetProperty(
                        "CurrentState", BindingFlags.Instance | BindingFlags.Public);
                    if (status != null && currentState != null)
                    {
                        return new TransitionObservation(
                            (CommandStatus)status.GetValue(result),
                            (PlayerState)currentState.GetValue(result));
                    }
                }

                return new TransitionObservation(
                    resultingState == previousState
                        ? CommandStatus.Rejected
                        : CommandStatus.Accepted,
                    resultingState);
            }

            private static PropertyInfo RequiredProperty(
                Type type,
                string name,
                Type propertyType)
            {
                var property = type.GetProperty(
                    name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null,
                    type.FullName + " must expose " + name + ".");
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
