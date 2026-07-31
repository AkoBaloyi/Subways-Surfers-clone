using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Edit Mode examples for the ordering guarantees and the rejection rules of the atomic reset
    /// sequence. Every fixture is built programmatically from the same hub-wired parts the reset
    /// service must consume - deterministic motor surface, movement loop, camera convergence state,
    /// and contact tracker - so no scene objects, prefabs, or serialized assets are involved. Each
    /// test unsubscribes its event handlers and drops its fixture references.
    ///
    /// The examples cover:
    /// ledger insertion before any mutation, so an accepted Request_Id is already recorded as the
    /// atomic sequence begins; the exact published order of started event, Resetting transition,
    /// restoration, Running transition, and completed event; completion before the next
    /// Movement_Update; acceptance from every eligible source state; and empty, absent, duplicate,
    /// and in-progress requests preserving state while publishing nothing.
    ///
    /// The reflection seams below match the ones used by <c>ResetEventCorrelationPropertyTests</c>
    /// and <c>ResetCompletenessPropertyTests</c>, so all three files require the same Task 9.4 API.
    /// </summary>
    public sealed class ResetOrderingAndRejectionTests
    {
        private const float ShortFrame = 0.05f;
        private const float PositionTolerance = 1e-4f;
        private static readonly PlayerConfiguration Configuration = PlayerConfiguration.SafeDefaults;

        private ResetFixture fixture;

        [TearDown]
        public void TearDown()
        {
            if (fixture != null) fixture.Dispose();
            fixture = null;
        }

        // **Validates: Requirements 10.1, 10.8, 10.12, 14.6, 14.7, 14.8**
        [Test]
        public void LedgerRecordsRequestIdBeforeAnyMutation_Requirements_10_1_10_8_10_12_14_6_14_7_14_8()
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            fixture.DriveDirty();
            var dirty = fixture.Snapshot;
            const string requestId = "ordering-ledger";
            var probe = fixture.ArmProbe(requestId, ProbeTrigger.FirstPublication);

            var result = fixture.Service.RequestReset(requestId);

            Assert.That(probe.Fired, Is.True,
                "The atomic reset sequence must publish an observable event so the ledger can be " +
                "probed while the sequence is still running.");
            Assert.That(probe.Result.Status, Is.EqualTo(CommandStatus.Rejected),
                "The accepted Request_Id must already be recorded when the atomic sequence begins, " +
                "so the same Request_Id cannot be accepted a second time.");
            Assert.That(
                probe.Result.Reason,
                Is.EqualTo(RejectionReason.DuplicateRequestId)
                    .Or.EqualTo(RejectionReason.ResetInProgress),
                "A re-entrant request for the in-flight Request_Id must be refused by the ledger " +
                "or by the in-progress guard.");
            Assert.That(probe.Result.RequestId, Is.EqualTo(requestId),
                "A rejected result must return the supplied Request_Id.");

            var observed = probe.ObservedSnapshot;
            Assert.That(observed.Player.Position, Is.EqualTo(dirty.Player.Position),
                "Ledger insertion must precede mutation, so the transform is still unrestored at " +
                "the first publication of the sequence.");
            Assert.That(observed.Player.CurrentLane, Is.EqualTo(dirty.Player.CurrentLane),
                "Ledger insertion must precede lane restoration.");
            Assert.That(observed.Player.ForwardSpeed, Is.EqualTo(dirty.Player.ForwardSpeed),
                "Ledger insertion must precede Forward_Run_Speed restoration.");
            Assert.That(observed.ActiveLogicalContacts.Count,
                Is.EqualTo(dirty.ActiveLogicalContacts.Count),
                "Ledger insertion must precede contact clearing.");

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "The original Reset_Request must stay accepted despite the re-entrant probe.");
            Assert.That(result.RequestId, Is.EqualTo(requestId));
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(fixture.Log.CountFor(requestId, ObservedKind.ResetStarted), Is.EqualTo(1),
                "The accepted Request_Id must correlate exactly one Player_Reset_Started_Event.");
            Assert.That(fixture.Log.CountFor(requestId, ObservedKind.ResetCompleted), Is.EqualTo(1),
                "The accepted Request_Id must correlate exactly one Player_Reset_Completed_Event.");

            var replay = fixture.Service.RequestReset(requestId);
            Assert.That(replay.Status, Is.EqualTo(CommandStatus.Rejected),
                "The recorded Request_Id must stay in Reset_Request_Ledger after the sequence.");
            Assert.That(replay.Reason, Is.EqualTo(RejectionReason.DuplicateRequestId));
        }

        // **Validates: Requirements 6.7, 6.8, 7.10, 7.11, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.12**
        [Test]
        public void AtomicSequenceOrdersStartedResettingRestorationRunningCompleted_Requirements_6_7_6_8_7_10_7_11_10_2_Through_10_7_And_10_12()
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            var baseline = fixture.Snapshot;
            fixture.DriveDirty();
            var dirty = fixture.Snapshot;
            Assert.That(dirty, Is.Not.EqualTo(baseline),
                "The fixture must be genuinely dirty before the ordering is observed.");
            const string requestId = "ordering-sequence";
            var publishedBefore = fixture.Log.Count;

            var result = fixture.Service.RequestReset(requestId);

            var published = fixture.Log.Since(publishedBefore);
            Assert.That(published.Count, Is.EqualTo(4),
                "One accepted reset must publish the started event, the Resetting transition, the " +
                "Running transition, and the completed event, and nothing else. Published=" +
                Render(published));

            Assert.That(published[0].Kind, Is.EqualTo(ObservedKind.ResetStarted),
                "The started event must be published first. Published=" + Render(published));
            Assert.That(published[0].RequestId, Is.EqualTo(requestId));

            Assert.That(published[1].Kind, Is.EqualTo(ObservedKind.StateChanged),
                "The Resetting transition must follow the started event. Published=" +
                Render(published));
            Assert.That(published[1].PreviousState, Is.EqualTo(PlayerState.Running));
            Assert.That(published[1].CurrentState, Is.EqualTo(PlayerState.Resetting));
            Assert.That(published[1].Cause, Is.EqualTo(PlayerTransitionCause.ResetRequested));

            Assert.That(published[2].Kind, Is.EqualTo(ObservedKind.StateChanged),
                "The Running transition must follow restoration. Published=" + Render(published));
            Assert.That(published[2].PreviousState, Is.EqualTo(PlayerState.Resetting));
            Assert.That(published[2].CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(published[2].Cause, Is.EqualTo(PlayerTransitionCause.ResetCompleted));

            Assert.That(published[3].Kind, Is.EqualTo(ObservedKind.ResetCompleted),
                "The completed event must be published last. Published=" + Render(published));
            Assert.That(published[3].RequestId, Is.EqualTo(requestId));
            Assert.That(published[3].ResultingState, Is.EqualTo(PlayerState.Running));

            for (var index = 1; index < published.Count; index++)
            {
                Assert.That(published[index].EventId, Is.GreaterThan(published[index - 1].EventId),
                    "Session Event_Ids must increase in publication order. Published=" +
                    Render(published));
            }

            var atResetting = published[1].ObservedSnapshot;
            Assert.That(atResetting.Player.Position, Is.EqualTo(dirty.Player.Position),
                "Restoration must run after the Resetting transition is published.");
            Assert.That(atResetting.Player.CurrentLane, Is.EqualTo(dirty.Player.CurrentLane),
                "Lane restoration must run after the Resetting transition is published.");
            Assert.That(atResetting.Player.ForwardSpeed, Is.EqualTo(dirty.Player.ForwardSpeed),
                "Forward_Run_Speed restoration must run after the Resetting transition.");

            var atRunning = published[2].ObservedSnapshot;
            Assert.That(atRunning.Player.Position, Is.EqualTo(baseline.Player.Position),
                "The configured start transform must be restored before the Running transition.");
            Assert.That(atRunning.Player.Rotation, Is.EqualTo(baseline.Player.Rotation));
            Assert.That(atRunning.Player.CurrentLane, Is.EqualTo(LogicalLane.Center),
                "Current Logical_Lane must be Center before the Running transition.");
            Assert.That(atRunning.Player.TargetLane, Is.EqualTo(LogicalLane.Center),
                "Target_Lane must be Center before the Running transition.");
            Assert.That(atRunning.Player.LaneChangeProgress, Is.EqualTo(0f));
            Assert.That(atRunning.Player.VerticalVelocity, Is.EqualTo(0f));
            Assert.That(atRunning.Player.SlideElapsedTime, Is.EqualTo(0f));
            Assert.That(atRunning.Player.LaneRequestQueue.Count, Is.EqualTo(0));
            Assert.That(atRunning.Player.PendingActionRequests.Count, Is.EqualTo(0));
            Assert.That(atRunning.Player.ColliderProfile, Is.EqualTo(Configuration.BaselineCollider),
                "Baseline_Collider_Profile must be restored before the Running transition.");
            Assert.That(atRunning.Player.ForwardSpeed, Is.EqualTo(Configuration.ForwardSpeed),
                "The configured Forward_Run_Speed must be restored before the Running transition.");
            Assert.That(atRunning.CameraPosition, Is.EqualTo(Configuration.InitialCameraPosition),
                "The configured initial camera pose must be restored before the Running transition.");
            Assert.That(atRunning.CameraConvergenceVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(atRunning.RemainingCameraSettleTime, Is.EqualTo(0f));
            Assert.That(atRunning.HasCameraTarget, Is.False);
            Assert.That(atRunning.ActiveLogicalContacts.Count, Is.EqualTo(0),
                "Contact tracking must be cleared before the Running transition.");
            Assert.That(atRunning.ContactEventDeduplication.Count, Is.EqualTo(0),
                "Event-deduplication tracking must be cleared before the Running transition.");

            var completedSnapshot = published[3].ObservedSnapshot;
            Assert.That(completedSnapshot.Player.State, Is.EqualTo(PlayerState.Running),
                "The completed event must be published with Running exposed.");
            Assert.That(completedSnapshot.Player.ResetInProgress, Is.False,
                "In-progress reset status must be cleared before the completed event.");
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running));
        }

        // **Validates: Requirements 10.7, 10.8, 10.9, 10.10, 10.11, 12.11**
        [Test]
        public void ResetCompletesBeforeTheNextMovementUpdate_Requirements_10_7_10_8_10_9_10_10_10_11_And_12_11()
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            var baseline = fixture.Snapshot;
            fixture.DriveDirty();
            Assert.That(fixture.Snapshot, Is.Not.EqualTo(baseline),
                "The fixture must be genuinely dirty before the reset is requested.");
            var updatesBefore = fixture.Loop.MovementUpdateCount;
            var movesBefore = fixture.Loop.MoveInvocationCount;

            var result = fixture.Service.RequestReset("ordering-before-update");

            Assert.That(fixture.Loop.MovementUpdateCount, Is.EqualTo(updatesBefore),
                "The atomic reset sequence must complete without running a Movement_Update.");
            Assert.That(fixture.Loop.MoveInvocationCount, Is.EqualTo(movesBefore),
                "The atomic reset sequence must not submit displacement through the motor.");
            Assert.That(fixture.Log.Last.Kind, Is.EqualTo(ObservedKind.ResetCompleted),
                "The completed event must be published before RequestReset returns.");
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running));

            var restored = fixture.Snapshot;
            Assert.That(restored, Is.EqualTo(baseline),
                "Restoration must be complete before the next Movement_Update, so the whole reset " +
                "surface already equals Player_Initial_State.");
            Assert.That(restored.Player.ResetInProgress, Is.False,
                "In-progress reset status must be cleared before the next Movement_Update.");
            Assert.That(restored.Player.State, Is.EqualTo(PlayerState.Running));

            fixture.Loop.ExecuteMovementUpdate(ShortFrame);
            var advanced = fixture.Snapshot;

            Assert.That(fixture.Loop.MovementUpdateCount, Is.EqualTo(updatesBefore + 1));
            Assert.That(
                advanced.Player.Position.z,
                Is.EqualTo(baseline.Player.Position.z + Configuration.ForwardSpeed * ShortFrame)
                    .Within(PositionTolerance),
                "The next Movement_Update must run from the restored transform at the configured " +
                "Forward_Run_Speed.");
            Assert.That(
                advanced.Player.Position.x,
                Is.EqualTo(Configuration.LaneCenters.y).Within(PositionTolerance),
                "The next Movement_Update must run from the restored Center Lane_Center.");
        }

        // **Validates: Requirements 6.7, 6.8, 7.10, 7.11, 10.1, 10.9, 12.11, 14.6, 14.8**
        [TestCase(PlayerState.Running)]
        [TestCase(PlayerState.Jumping)]
        [TestCase(PlayerState.Sliding)]
        [TestCase(PlayerState.Failed)]
        public void AcceptedResetFromEveryEligibleSourceState_Requirements_6_7_6_8_7_10_7_11_10_1_10_9_12_11_14_6_14_8(
            PlayerState sourceState)
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            var baseline = fixture.Snapshot;
            fixture.DriveTo(sourceState);

            Assert.That(fixture.Loop.CurrentState, Is.EqualTo(sourceState),
                "The fixture must reach the eligible source state before the Reset_Request.");
            Assert.That(fixture.Snapshot, Is.Not.EqualTo(baseline),
                "The fixture must be dirty before the Reset_Request from " + sourceState + ".");

            var requestId = "ordering-source-" + sourceState;
            var publishedBefore = fixture.Log.Count;

            var result = fixture.Service.RequestReset(requestId);

            var published = fixture.Log.Since(publishedBefore);
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "A fresh Request_Id must be accepted from " + sourceState + ".");
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset));
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.None));
            Assert.That(result.RequestId, Is.EqualTo(requestId));
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running),
                "A completed reset must expose Running regardless of the source state.");

            Assert.That(published.Count, Is.EqualTo(4),
                "A reset accepted from " + sourceState + " must publish the correlated pair and " +
                "the two transitions only. Published=" + Render(published));
            Assert.That(published[0].Kind, Is.EqualTo(ObservedKind.ResetStarted));
            Assert.That(published[0].RequestId, Is.EqualTo(requestId));
            Assert.That(published[1].PreviousState, Is.EqualTo(sourceState),
                "The Resetting transition must report " + sourceState + " as the previous state.");
            Assert.That(published[1].CurrentState, Is.EqualTo(PlayerState.Resetting));
            Assert.That(published[1].Cause, Is.EqualTo(PlayerTransitionCause.ResetRequested));
            Assert.That(published[2].PreviousState, Is.EqualTo(PlayerState.Resetting));
            Assert.That(published[2].CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(published[2].Cause, Is.EqualTo(PlayerTransitionCause.ResetCompleted));
            Assert.That(published[3].Kind, Is.EqualTo(ObservedKind.ResetCompleted));
            Assert.That(published[3].RequestId, Is.EqualTo(requestId));
            Assert.That(published[3].ResultingState, Is.EqualTo(PlayerState.Running));

            Assert.That(fixture.Snapshot, Is.EqualTo(baseline),
                "A reset accepted from " + sourceState + " must restore Player_Initial_State " +
                "across the whole reset surface.");
        }

        // **Validates: Requirements 6.14, 7.10, 7.11, 14.7, 14.13**
        [TestCase((string)null)]
        [TestCase("")]
        public void MissingRequestIdPreservesStateAndPublishesNothing_Requirements_6_14_7_10_7_11_14_7_14_13(
            string requestId)
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            fixture.DriveDirty();
            var before = fixture.Snapshot;
            var stateBefore = fixture.Loop.CurrentState;
            var publishedBefore = fixture.Log.Count;

            var result = fixture.Service.RequestReset(requestId);

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected),
                "An absent or empty Request_Id must be rejected.");
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset));
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.MissingRequestId));
            Assert.That(result.RequestId, Is.EqualTo(requestId),
                "A rejected result must return the supplied Request_Id.");
            Assert.That(result.CurrentState, Is.EqualTo(stateBefore),
                "A rejected Reset_Request must report the unchanged Player_State.");
            Assert.That(fixture.Loop.CurrentState, Is.EqualTo(stateBefore));
            Assert.That(fixture.Snapshot, Is.EqualTo(before),
                "A rejected Reset_Request must preserve the whole player-owned surface.");
            Assert.That(fixture.Log.Since(publishedBefore).Count, Is.EqualTo(0),
                "A rejected Reset_Request must publish zero player-domain events.");
        }

        // **Validates: Requirements 7.10, 7.11, 10.12, 14.7, 14.8, 14.13**
        [Test]
        public void DuplicateRequestIdPreservesStateAndPublishesNothing_Requirements_7_10_7_11_10_12_14_7_14_8_14_13()
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            const string requestId = "ordering-duplicate";
            var accepted = fixture.Service.RequestReset(requestId);
            Assert.That(accepted.Status, Is.EqualTo(CommandStatus.Accepted),
                "The first use of the Request_Id must be accepted so it enters the ledger.");

            fixture.DriveDirty();
            var before = fixture.Snapshot;
            var stateBefore = fixture.Loop.CurrentState;
            var publishedBefore = fixture.Log.Count;

            var result = fixture.Service.RequestReset(requestId);

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Request_Id already in Reset_Request_Ledger must be rejected.");
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset));
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.DuplicateRequestId));
            Assert.That(result.RequestId, Is.EqualTo(requestId));
            Assert.That(result.CurrentState, Is.EqualTo(stateBefore));
            Assert.That(fixture.Loop.CurrentState, Is.EqualTo(stateBefore));
            Assert.That(fixture.Snapshot, Is.EqualTo(before),
                "A duplicate Request_Id must preserve the whole player-owned surface.");
            Assert.That(fixture.Log.Since(publishedBefore).Count, Is.EqualTo(0),
                "A duplicate Request_Id must publish zero player-domain events.");
            Assert.That(fixture.Log.CountFor(requestId, ObservedKind.ResetStarted), Is.EqualTo(1),
                "The Request_Id must still correlate exactly one started event for the session.");
            Assert.That(fixture.Log.CountFor(requestId, ObservedKind.ResetCompleted), Is.EqualTo(1),
                "The Request_Id must still correlate exactly one completed event for the session.");
        }

        // **Validates: Requirements 6.14, 7.10, 7.11, 10.9, 14.7, 14.13**
        [Test]
        public void InProgressRequestPreservesStateAndPublishesNothing_Requirements_6_14_7_10_7_11_10_9_14_7_14_13()
        {
            fixture = ResetFixture.Create();
            fixture.SampleGrounding();
            var baseline = fixture.Snapshot;
            fixture.DriveDirty();
            const string acceptedId = "ordering-in-progress-outer";
            const string nestedId = "ordering-in-progress-nested";
            var probe = fixture.ArmProbe(nestedId, ProbeTrigger.ResettingTransition);
            var publishedBefore = fixture.Log.Count;

            var result = fixture.Service.RequestReset(acceptedId);

            Assert.That(probe.Fired, Is.True,
                "The accepted reset must publish its Resetting transition so a re-entrant " +
                "Reset_Request can be issued while the sequence is in progress.");
            Assert.That(probe.Result.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Reset_Request arriving while a reset is in progress must be rejected.");
            Assert.That(probe.Result.Command, Is.EqualTo(PlayerCommandKind.Reset));
            Assert.That(probe.Result.Reason, Is.EqualTo(RejectionReason.ResetInProgress));
            Assert.That(probe.Result.RequestId, Is.EqualTo(nestedId));
            Assert.That(probe.Result.CurrentState, Is.EqualTo(PlayerState.Resetting),
                "The in-progress rejection must report the unchanged Resetting Player_State.");
            Assert.That(fixture.Log.CountFor(nestedId), Is.EqualTo(0),
                "An in-progress rejection must publish zero reset lifecycle events.");

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "The in-flight reset must still complete despite the rejected re-entrant request.");
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(fixture.Log.Since(publishedBefore).Count, Is.EqualTo(4),
                "The rejected re-entrant request must add no publication to the sequence. " +
                "Published=" + Render(fixture.Log.Since(publishedBefore)));
            Assert.That(fixture.Snapshot, Is.EqualTo(baseline),
                "The rejected re-entrant request must leave restoration to Player_Initial_State " +
                "unaffected.");

            var later = fixture.Service.RequestReset(nestedId);
            Assert.That(later.Status, Is.EqualTo(CommandStatus.Accepted),
                "A rejected Request_Id must not enter Reset_Request_Ledger, so it stays usable.");
            Assert.That(later.RequestId, Is.EqualTo(nestedId));
        }

        private static string Render(IReadOnlyList<Observation> published)
        {
            var builder = new System.Text.StringBuilder("[");
            for (var index = 0; index < published.Count; index++)
            {
                if (index > 0) builder.Append("; ");
                builder.Append(published[index]);
            }

            return builder.Append(']').ToString();
        }

        private enum ProbeTrigger
        {
            FirstPublication,
            ResettingTransition
        }

        private enum ObservedKind
        {
            PlayerHit,
            CoinCollected,
            StateChanged,
            ResetStarted,
            ResetCompleted
        }

        /// <summary>
        /// One published player-domain event plus the complete reset surface observed at the moment
        /// of publication, which is how the ordering of the atomic sequence is verified.
        /// </summary>
        private readonly struct Observation
        {
            private Observation(
                ObservedKind kind,
                ulong eventId,
                PlayerState previousState,
                PlayerState currentState,
                PlayerTransitionCause cause,
                string requestId,
                PlayerState resultingState,
                PlayerResetSnapshot observedSnapshot)
            {
                Kind = kind;
                EventId = eventId;
                PreviousState = previousState;
                CurrentState = currentState;
                Cause = cause;
                RequestId = requestId;
                ResultingState = resultingState;
                ObservedSnapshot = observedSnapshot;
            }

            public ObservedKind Kind { get; }
            public ulong EventId { get; }
            public PlayerState PreviousState { get; }
            public PlayerState CurrentState { get; }
            public PlayerTransitionCause Cause { get; }
            public string RequestId { get; }
            public PlayerState ResultingState { get; }
            public PlayerResetSnapshot ObservedSnapshot { get; }

            public static Observation From(PlayerHitEvent value, PlayerResetSnapshot snapshot)
            {
                return new Observation(
                    ObservedKind.PlayerHit, value.EventId, PlayerState.Running, PlayerState.Running,
                    PlayerTransitionCause.ResetRequested, null, PlayerState.Running, snapshot);
            }

            public static Observation From(CoinCollectedEvent value, PlayerResetSnapshot snapshot)
            {
                return new Observation(
                    ObservedKind.CoinCollected, value.EventId, PlayerState.Running,
                    PlayerState.Running, PlayerTransitionCause.ResetRequested, null,
                    PlayerState.Running, snapshot);
            }

            public static Observation From(PlayerStateChangedEvent value, PlayerResetSnapshot snapshot)
            {
                return new Observation(
                    ObservedKind.StateChanged, value.EventId, value.PreviousState,
                    value.CurrentState, value.TransitionCause, null, value.CurrentState, snapshot);
            }

            public static Observation From(PlayerResetStartedEvent value, PlayerResetSnapshot snapshot)
            {
                return new Observation(
                    ObservedKind.ResetStarted, value.EventId, PlayerState.Resetting,
                    PlayerState.Resetting, PlayerTransitionCause.ResetRequested, value.RequestId,
                    PlayerState.Resetting, snapshot);
            }

            public static Observation From(PlayerResetCompletedEvent value, PlayerResetSnapshot snapshot)
            {
                return new Observation(
                    ObservedKind.ResetCompleted, value.EventId, PlayerState.Resetting,
                    value.ResultingState, PlayerTransitionCause.ResetCompleted, value.RequestId,
                    value.ResultingState, snapshot);
            }

            public override string ToString()
            {
                switch (Kind)
                {
                    case ObservedKind.StateChanged:
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "StateChanged#{0}({1}->{2},{3})",
                            EventId, PreviousState, CurrentState, Cause);
                    case ObservedKind.ResetStarted:
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "ResetStarted#{0}('{1}')", EventId, RequestId);
                    case ObservedKind.ResetCompleted:
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "ResetCompleted#{0}('{1}',{2})", EventId, RequestId, ResultingState);
                    default:
                        return string.Format(
                            CultureInfo.InvariantCulture, "{0}#{1}", Kind, EventId);
                }
            }
        }

        private sealed class EventLog
        {
            private readonly List<Observation> events = new List<Observation>();
            private Func<PlayerResetSnapshot> snapshotProvider;
            private Action<Observation> observer;

            public int Count { get { return events.Count; } }

            public Observation Last
            {
                get
                {
                    Assert.That(events.Count, Is.GreaterThan(0),
                        "At least one player-domain event must have been published.");
                    return events[events.Count - 1];
                }
            }

            public void UseSnapshotProvider(Func<PlayerResetSnapshot> provider)
            {
                snapshotProvider = provider;
            }

            public void UseObserver(Action<Observation> value) { observer = value; }

            public void Add(PlayerHitEvent value) { Record(Observation.From(value, Read())); }
            public void Add(CoinCollectedEvent value) { Record(Observation.From(value, Read())); }
            public void Add(PlayerStateChangedEvent value) { Record(Observation.From(value, Read())); }
            public void Add(PlayerResetStartedEvent value) { Record(Observation.From(value, Read())); }

            public void Add(PlayerResetCompletedEvent value)
            {
                Record(Observation.From(value, Read()));
            }

            public IReadOnlyList<Observation> Since(int startIndex)
            {
                var result = new List<Observation>();
                for (var index = startIndex; index < events.Count; index++) result.Add(events[index]);
                return result;
            }

            public int CountFor(string requestId)
            {
                var count = 0;
                for (var index = 0; index < events.Count; index++)
                {
                    if (events[index].RequestId == null) continue;
                    if (string.Equals(events[index].RequestId, requestId, StringComparison.Ordinal))
                        count++;
                }

                return count;
            }

            public int CountFor(string requestId, ObservedKind kind)
            {
                var count = 0;
                for (var index = 0; index < events.Count; index++)
                {
                    if (events[index].Kind != kind) continue;
                    if (string.Equals(events[index].RequestId, requestId, StringComparison.Ordinal))
                        count++;
                }

                return count;
            }

            private void Record(Observation observation)
            {
                events.Add(observation);
                if (observer != null) observer(observation);
            }

            private PlayerResetSnapshot Read()
            {
                return snapshotProvider == null
                    ? default(PlayerResetSnapshot)
                    : snapshotProvider();
            }
        }

        /// <summary>
        /// Issues one re-entrant Reset_Request at the first publication of the atomic sequence, or at
        /// the Resetting transition, which are the only deterministic points where the sequence is
        /// still in progress.
        /// </summary>
        private sealed class ReentrantProbe
        {
            private readonly ResetSurfaceSeam service;
            private readonly string requestId;
            private readonly ProbeTrigger trigger;

            public ReentrantProbe(ResetSurfaceSeam service, string requestId, ProbeTrigger trigger)
            {
                this.service = service;
                this.requestId = requestId;
                this.trigger = trigger;
            }

            public bool Fired { get; private set; }
            public ResetRequestResult Result { get; private set; }
            public PlayerResetSnapshot ObservedSnapshot { get; private set; }

            public void Observe(Observation observation)
            {
                if (Fired) return;
                if (trigger == ProbeTrigger.ResettingTransition &&
                    (observation.Kind != ObservedKind.StateChanged ||
                        observation.CurrentState != PlayerState.Resetting))
                {
                    return;
                }

                Fired = true;
                ObservedSnapshot = observation.ObservedSnapshot;
                Result = service.RequestReset(requestId);
            }
        }

        /// <summary>
        /// One programmatically built hub-wired fixture: deterministic motor surface, movement loop,
        /// camera convergence state, contact tracker, and the reset service under test.
        /// </summary>
        private sealed class ResetFixture : IDisposable
        {
            private readonly PlayerEventHub hub;
            private readonly Action<PlayerHitEvent> hit;
            private readonly Action<CoinCollectedEvent> coin;
            private readonly Action<PlayerStateChangedEvent> stateChanged;
            private readonly Action<PlayerResetStartedEvent> resetStarted;
            private readonly Action<PlayerResetCompletedEvent> resetCompleted;
            private int contactChildColliderId;

            private ResetFixture(
                PlayerEventHub hub,
                PlayerMovementLoop loop,
                ResetSurfaceSeam service,
                CameraConvergenceState camera,
                EnvironmentContactTracker contacts,
                EventLog log,
                IReadOnlyList<ValidationDiagnostic> diagnostics)
            {
                this.hub = hub;
                Loop = loop;
                Service = service;
                Camera = camera;
                Contacts = contacts;
                Log = log;
                Diagnostics = diagnostics;

                hit = log.Add;
                coin = log.Add;
                stateChanged = log.Add;
                resetStarted = log.Add;
                resetCompleted = log.Add;

                log.UseSnapshotProvider(() => service.Snapshot);
                hub.PlayerHit += hit;
                hub.CoinCollected += coin;
                hub.StateChanged += stateChanged;
                hub.ResetStarted += resetStarted;
                hub.ResetCompleted += resetCompleted;
            }

            public PlayerMovementLoop Loop { get; }
            public ResetSurfaceSeam Service { get; }
            public CameraConvergenceState Camera { get; }
            public EnvironmentContactTracker Contacts { get; }
            public EventLog Log { get; }
            public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }

            public PlayerResetSnapshot Snapshot { get { return Service.Snapshot; } }

            public static ResetFixture Create()
            {
                var motor = new DeterministicMotorSurface(
                    new Vector3(Configuration.LaneCenters.y, 0f, 12.5f), true);
                var diagnostics = new List<ValidationDiagnostic>();
                var hub = new PlayerEventHub();
                var log = new EventLog();
                var loop = MovementPublicationSeam.CreateLoop(
                    Configuration, motor, diagnostics.Add, hub);
                var camera = new CameraConvergenceState(
                    loop.Snapshot.Position,
                    Configuration.InitialCameraPosition,
                    Configuration.CameraSettleDuration,
                    Configuration.CameraFollowTolerance);
                var contacts = new EnvironmentContactTracker(hub);
                var service = ResetSurfaceSeam.Create(
                    Configuration, motor, loop, hub, camera, contacts, diagnostics.Add);

                return new ResetFixture(hub, loop, service, camera, contacts, log, diagnostics);
            }

            /// <summary>
            /// One zero-time Movement_Update samples grounding without moving anything, so
            /// Player_Initial_State can be observed before any accepted command.
            /// </summary>
            public void SampleGrounding() { Loop.ExecuteMovementUpdate(0f); }

            public void DriveDirty()
            {
                Loop.SetForwardSpeed(Configuration.ForwardSpeed + 5f);

                // Leave Center for good, then start a second segment and leave a request queued
                // behind it, so lanes carry a non-Center lane, in-flight progress, and a queue.
                Loop.RequestLane(LaneDirection.Right);
                Loop.ExecuteMovementUpdate(Configuration.LaneChangeDuration);
                Loop.RequestLane(LaneDirection.Left);
                Loop.RequestLane(LaneDirection.Right);
                Loop.ExecuteMovementUpdate(ShortFrame);

                // The camera is driven after the player has moved, so it stops mid-convergence with
                // an outstanding smoothing budget and a non-zero convergence velocity.
                Camera.Advance(Loop.Snapshot.Position, 0.01f);
                Camera.Advance(Loop.Snapshot.Position, 0.01f);

                EnterContacts();
            }

            public void DriveTo(PlayerState sourceState)
            {
                DriveDirty();
                switch (sourceState)
                {
                    case PlayerState.Jumping:
                        Loop.RequestJump();
                        Loop.ExecuteMovementUpdate(ShortFrame);
                        break;
                    case PlayerState.Sliding:
                        Loop.RequestSlide();
                        Loop.ExecuteMovementUpdate(ShortFrame);
                        break;
                    case PlayerState.Failed:
                        Loop.RequestFailure();
                        break;
                }
            }

            public ReentrantProbe ArmProbe(string requestId, ProbeTrigger trigger)
            {
                var probe = new ReentrantProbe(Service, requestId, trigger);
                Log.UseObserver(probe.Observe);
                return probe;
            }

            public void Dispose()
            {
                Log.UseObserver(null);
                Log.UseSnapshotProvider(null);
                hub.PlayerHit -= hit;
                hub.CoinCollected -= coin;
                hub.StateChanged -= stateChanged;
                hub.ResetStarted -= resetStarted;
                hub.ResetCompleted -= resetCompleted;
            }

            private void EnterContacts()
            {
                var suffix = contactChildColliderId.ToString(CultureInfo.InvariantCulture);
                var obstacle = new StubEnvironmentObject(
                    "obstacle-" + suffix, EnvironmentObjectKind.Obstacle, 0f);
                var collectible = new StubEnvironmentObject(
                    "coin-" + suffix, EnvironmentObjectKind.Coin, 2.5f);
                var position = Loop.Snapshot.Position;

                Contacts.Enter(++contactChildColliderId, obstacle, position);
                // A second child collider of the same logical contact is deduplicated rather than
                // recorded again, which is part of the state reset must clear.
                Contacts.Enter(++contactChildColliderId, obstacle, position);
                Contacts.Enter(++contactChildColliderId, collectible, position);
            }
        }

        private sealed class StubEnvironmentObject : IEnvironmentObject
        {
            public StubEnvironmentObject(
                string environmentObjectId,
                EnvironmentObjectKind kind,
                float collectibleValue)
            {
                EnvironmentObjectId = environmentObjectId;
                Kind = kind;
                CollectibleValue = collectibleValue;
            }

            public string EnvironmentObjectId { get; }
            public EnvironmentObjectKind Kind { get; }
            public float CollectibleValue { get; }
        }

        /// <summary>
        /// Deterministic stand-in for the Unity motor: displacement integration against a flat
        /// ground plane at y = 0, with no scene objects.
        /// </summary>
        private sealed class DeterministicMotorSurface : IPlayerMotorSurface
        {
            private readonly bool restorationSafe;
            private Vector3 position;

            public DeterministicMotorSurface(Vector3 initialPosition, bool restorationSafe)
            {
                position = initialPosition;
                this.restorationSafe = restorationSafe;
            }

            public Vector3 Position { get { return position; } }
            public Quaternion Rotation { get { return Quaternion.identity; } }
            public int MoveInvocationCount { get; private set; }

            public void ApplyColliderProfile(ColliderProfile profile) { }

            public bool SampleGrounded(
                ColliderProfile profile,
                int groundLayerMask,
                float contactTolerance,
                float normalThreshold)
            {
                return position.y <= contactTolerance;
            }

            public bool IsBaselineRestorationSafe(
                ColliderProfile baselineProfile,
                int obstructionLayerMask)
            {
                return restorationSafe;
            }

            public Vector3 Move(Vector3 displacement)
            {
                MoveInvocationCount++;
                var target = position + displacement;
                if (target.y < 0f) target.y = 0f;
                var realized = target - position;
                position = target;
                return realized;
            }
        }

        /// <summary>
        /// Red-first seam for the Task 9.4 facade wiring: the movement loop must be constructible
        /// with the session <see cref="PlayerEventHub"/> so every accepted transition is published
        /// through the single session Event_Id sequence.
        /// </summary>
        private static class MovementPublicationSeam
        {
            public static PlayerMovementLoop CreateLoop(
                PlayerConfiguration configuration,
                IPlayerMotorSurface motor,
                Action<ValidationDiagnostic> diagnosticSink,
                PlayerEventHub hub)
            {
                var candidates = new object[] { hub, motor, diagnosticSink, configuration };
                var constructors = typeof(PlayerMovementLoop)
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                foreach (var constructor in constructors)
                {
                    object[] arguments;
                    if (!SeamArguments.TryResolve(
                        constructor, candidates, new object[] { hub }, out arguments))
                    {
                        continue;
                    }

                    return (PlayerMovementLoop)SeamArguments.Invoke(constructor, arguments);
                }

                Assert.Fail(
                    "Task 9.4 must wire " + typeof(PlayerMovementLoop).FullName + " to " +
                    typeof(PlayerEventHub).FullName + " with a public constructor accepting the " +
                    "PlayerConfiguration, the IPlayerMotorSurface, the diagnostic sink, and the " +
                    "session PlayerEventHub, so every accepted transition publishes exactly one " +
                    "Player_State_Changed_Event carrying a unique session Event_Id.");
                return null;
            }
        }

        /// <summary>
        /// Red-first reflection seam for the Task 9.4 reset service. Constructor arguments are
        /// resolved by parameter type so Task 9.4 keeps freedom over the exact signature, provided
        /// the service consumes the movement loop, the session event hub, the camera convergence
        /// state, and the contact tracker whose state it must reset, exposes
        /// <c>ResetRequestResult RequestReset(string requestId)</c>, and observes the complete
        /// <see cref="PlayerResetSnapshot"/> through one public parameterless property or method.
        /// </summary>
        private sealed class ResetSurfaceSeam
        {
            private const string PreferredTypeName = "SubwaySurfers.Player.Domain.PlayerResetService";
            private const string AlternateTypeName = "SubwaySurfers.Player.PlayerResetService";

            private readonly object instance;
            private readonly MethodInfo requestReset;
            private readonly MethodInfo readSnapshot;

            private ResetSurfaceSeam(object instance, MethodInfo requestReset, MethodInfo readSnapshot)
            {
                this.instance = instance;
                this.requestReset = requestReset;
                this.readSnapshot = readSnapshot;
            }

            public static ResetSurfaceSeam Create(
                PlayerConfiguration configuration,
                IPlayerMotorSurface motor,
                PlayerMovementLoop loop,
                PlayerEventHub hub,
                CameraConvergenceState camera,
                EnvironmentContactTracker contacts,
                Action<ValidationDiagnostic> diagnosticSink)
            {
                var runtimeAssembly = typeof(PlayerState).Assembly;
                var type = runtimeAssembly.GetType(PreferredTypeName) ??
                    runtimeAssembly.GetType(AlternateTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 9.4 must provide " + PreferredTypeName + " with a public constructor " +
                    "consuming the PlayerMovementLoop, the session PlayerEventHub, the " +
                    "CameraConvergenceState, and the EnvironmentContactTracker, a public " +
                    "ResetRequestResult RequestReset(string requestId), and a public parameterless " +
                    "observation returning the complete PlayerResetSnapshot.");

                var method = type.GetMethod(
                    "RequestReset",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
                Assert.That(method, Is.Not.Null,
                    type.FullName + " must expose ResetRequestResult RequestReset(string requestId).");
                Assert.That(method.ReturnType, Is.EqualTo(typeof(ResetRequestResult)),
                    type.FullName + " must return ResetRequestResult from RequestReset.");

                var snapshotReader = FindSnapshotReader(type);
                Assert.That(snapshotReader, Is.Not.Null,
                    type.FullName + " must observe the complete reset equality surface through one " +
                    "public parameterless property or method returning " +
                    typeof(PlayerResetSnapshot).FullName + " - transform, lanes, speed, collider " +
                    "profile, queues, timers, vertical velocity, camera pose and smoothing state, " +
                    "active and deduplicated contact identities, input latch and consumption state, " +
                    "and reset status.");

                var candidates = new object[]
                {
                    loop, hub, camera, contacts, motor, diagnosticSink, configuration
                };
                var required = new object[] { loop, hub, camera, contacts };
                var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                foreach (var constructor in constructors)
                {
                    object[] arguments;
                    if (!SeamArguments.TryResolve(constructor, candidates, required, out arguments))
                        continue;

                    return new ResetSurfaceSeam(
                        SeamArguments.Invoke(constructor, arguments), method, snapshotReader);
                }

                Assert.Fail(
                    type.FullName + " must expose a public constructor consuming the " +
                    "PlayerMovementLoop, the session PlayerEventHub, the CameraConvergenceState, " +
                    "and the EnvironmentContactTracker, optionally with the PlayerConfiguration, " +
                    "the IPlayerMotorSurface, and a diagnostic sink, so one accepted reset can " +
                    "restore the complete reset equality surface.");
                return null;
            }

            public PlayerResetSnapshot Snapshot
            {
                get { return (PlayerResetSnapshot)Invoke(readSnapshot, Array.Empty<object>()); }
            }

            public ResetRequestResult RequestReset(string requestId)
            {
                return (ResetRequestResult)Invoke(requestReset, new object[] { requestId });
            }

            private static MethodInfo FindSnapshotReader(Type type)
            {
                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (var property in properties)
                {
                    if (property.PropertyType != typeof(PlayerResetSnapshot)) continue;
                    var getter = property.GetGetMethod();
                    if (getter != null && getter.GetParameters().Length == 0) return getter;
                }

                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                foreach (var candidate in methods)
                {
                    if (candidate.ReturnType != typeof(PlayerResetSnapshot)) continue;
                    if (candidate.GetParameters().Length == 0) return candidate;
                }

                return null;
            }

            private object Invoke(MethodInfo method, object[] arguments)
            {
                try
                {
                    return method.Invoke(instance, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }
        }

        private static class SeamArguments
        {
            public static bool TryResolve(
                ConstructorInfo constructor,
                IReadOnlyList<object> candidates,
                IReadOnlyList<object> required,
                out object[] arguments)
            {
                arguments = null;
                var parameters = constructor.GetParameters();
                var resolved = new object[parameters.Length];
                var used = new bool[candidates.Count];

                for (var index = 0; index < parameters.Length; index++)
                {
                    var matched = false;
                    for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        if (used[candidateIndex]) continue;
                        if (!parameters[index].ParameterType
                            .IsInstanceOfType(candidates[candidateIndex]))
                        {
                            continue;
                        }

                        resolved[index] = candidates[candidateIndex];
                        used[candidateIndex] = true;
                        matched = true;
                        break;
                    }

                    if (!matched) return false;
                }

                for (var index = 0; index < required.Count; index++)
                    if (!Contains(resolved, required[index])) return false;

                arguments = resolved;
                return true;
            }

            public static object Invoke(ConstructorInfo constructor, object[] arguments)
            {
                try
                {
                    return constructor.Invoke(arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }

            private static bool Contains(IReadOnlyList<object> values, object required)
            {
                for (var index = 0; index < values.Count; index++)
                    if (ReferenceEquals(values[index], required)) return true;
                return false;
            }
        }
    }
}
