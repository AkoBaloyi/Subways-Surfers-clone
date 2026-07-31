using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Property 14. Every generated case builds one hub-wired fixture programmatically - motor
    /// surface, movement loop, camera convergence state, contact tracker, and the reset service -
    /// drives it into a genuinely dirty state, and then compares the complete
    /// <see cref="PlayerResetSnapshot"/> observed after each accepted reset against the snapshot
    /// observed before any accepted command. The equality surface is the whole reset snapshot:
    /// transform, lanes and lateral geometry, forward speed, collider profile, lane and action
    /// queues, slide timer, vertical velocity, camera pose plus smoothing state, active and
    /// deduplicated contact identities, input latch and consumption state, and reset status.
    ///
    /// Dirty state before each reset is asserted, not assumed: a non-Center current lane with an
    /// in-flight lane segment and a queued request behind it, a non-configured forward speed, a
    /// non-zero vertical velocity or a non-zero slide timer with the slide collider profile applied,
    /// a camera held mid-convergence with an outstanding smoothing budget and a non-zero convergence
    /// velocity, and recorded active plus deduplicated contacts.
    ///
    /// Two consecutive accepted resets with distinct Request_Ids and no intervening accepted command
    /// must produce equivalent snapshots, while session identity must survive: the Event_Id sequence
    /// keeps increasing without reuse and the reset request ledger keeps rejecting the first
    /// Request_Id as a duplicate after the later reset.
    ///
    /// No scene objects, prefabs, or serialized assets are involved. Each case unsubscribes its
    /// event handlers and drops its fixture references.
    /// </summary>
    public sealed class ResetCompletenessPropertyTests
    {
        private const int Seed = 914_022;
        private const int CaseCount = 120;
        private static readonly PlayerConfiguration Configuration = PlayerConfiguration.SafeDefaults;

        private int generatedCaseIndex;

        // **Validates: Requirements 9.7, 9.8, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11, 12.11**
        [Test]
        [Description("Feature: player-controller, Property 14: Reset is complete and repeat-safe")]
        public void ResetIsCompleteAndRepeatSafe_Property14_Requirements_9_7_9_8_10_1_Through_10_11_And_12_11()
        {
            generatedCaseIndex = 0;
            var coverage = new Coverage();

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                generated => AssertProperty(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.SourceStates, Is.SupersetOf(new[]
            {
                PlayerState.Running,
                PlayerState.Jumping,
                PlayerState.Sliding,
                PlayerState.Failed
            }), "Accepted resets must be exercised from every eligible source state.");
            Assert.That(coverage.DirtyVerticalVelocity, Is.True,
                "At least one case must reset a non-zero vertical velocity.");
            Assert.That(coverage.DirtySlideProfile, Is.True,
                "At least one case must reset a non-zero slide timer with the slide collider applied.");
            Assert.That(coverage.DirtyLaneSegment, Is.True,
                "At least one case must reset an in-flight lane segment with a queued request.");
            Assert.That(coverage.DirtyCamera, Is.True,
                "At least one case must reset a camera held mid-convergence.");
            Assert.That(coverage.DirtyContacts, Is.True,
                "At least one case must reset recorded active and deduplicated contacts.");
        }

        private ResetSurfaceCase Generate(Random random)
        {
            var caseIndex = generatedCaseIndex++;
            return new ResetSurfaceCase(
                caseIndex,
                GeneratedValues.NextFiniteFloat(random, -40f, 40f),
                DirtyForwardSpeed(random),
                GeneratedValues.NextFiniteFloat(random, 0.02f, 0.15f),
                GeneratedValues.NextFiniteFloat(random, 0.005f, 0.03f),
                GeneratedValues.NextFiniteFloat(random, 0.005f, 0.03f),
                GeneratedValues.NextFiniteFloat(random, 0.005f, 0.03f),
                (DirtyAction)(caseIndex % 4),
                random.Next(0, 2) == 0);
        }

        private static float DirtyForwardSpeed(Random random)
        {
            // Any finite non-negative speed other than the configured one, so reset has to restore
            // the configured Forward_Run_Speed rather than keep the value in effect.
            var speed = GeneratedValues.NextFiniteFloat(random, 0.5f, 30f);
            return Mathf.Approximately(speed, Configuration.ForwardSpeed) ? speed + 1.5f : speed;
        }

        private static void AssertProperty(ResetSurfaceCase generated, Coverage coverage)
        {
            var motor = new DeterministicMotorSurface(
                new Vector3(Configuration.LaneCenters.y, 0f, generated.InitialForwardPosition),
                generated.RestorationSafe);
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

            Action<PlayerHitEvent> hit = log.Add;
            Action<CoinCollectedEvent> coin = log.Add;
            Action<PlayerStateChangedEvent> stateChanged = log.Add;
            Action<PlayerResetStartedEvent> resetStarted = log.Add;
            Action<PlayerResetCompletedEvent> resetCompleted = log.Add;

            hub.PlayerHit += hit;
            hub.CoinCollected += coin;
            hub.StateChanged += stateChanged;
            hub.ResetStarted += resetStarted;
            hub.ResetCompleted += resetCompleted;
            try
            {
                Execute(generated, loop, camera, contacts, hub, service, log, coverage);
            }
            finally
            {
                hub.PlayerHit -= hit;
                hub.CoinCollected -= coin;
                hub.StateChanged -= stateChanged;
                hub.ResetStarted -= resetStarted;
                hub.ResetCompleted -= resetCompleted;
            }
        }

        private static void Execute(
            ResetSurfaceCase generated,
            PlayerMovementLoop loop,
            CameraConvergenceState camera,
            EnvironmentContactTracker contacts,
            PlayerEventHub hub,
            ResetSurfaceSeam service,
            EventLog log,
            Coverage coverage)
        {
            // One zero-time update samples grounding without moving anything, so the baseline is the
            // observable Player_Initial_State before any accepted command.
            loop.ExecuteMovementUpdate(0f);
            var baseline = service.Snapshot;
            AssertInitialState(baseline, generated, "Baseline");

            Dirty(generated, loop, camera, contacts);
            var dirty = service.Snapshot;
            AssertDirty(generated, dirty, baseline, loop, camera, contacts, coverage);

            var sourceState = loop.CurrentState;
            var firstRequestId = RequestId(generated.CaseIndex, "a");
            var eventIdBeforeFirst = hub.LastEventId;
            var first = service.RequestReset(firstRequestId);
            AssertAccepted(first, firstRequestId, sourceState, generated, "first reset");
            var afterFirst = service.Snapshot;
            AssertResetSurface(baseline, afterFirst, generated, "after the first accepted reset");
            AssertInitialState(afterFirst, generated, "AfterFirstReset");
            coverage.SourceStates.Add(sourceState);

            var secondRequestId = RequestId(generated.CaseIndex, "b");
            var second = service.RequestReset(secondRequestId);
            AssertAccepted(second, secondRequestId, PlayerState.Running, generated, "second reset");
            var afterSecond = service.Snapshot;
            AssertResetSurface(
                afterFirst, afterSecond, generated,
                "after a second accepted reset with a distinct Request_Id and no intervening command");
            AssertResetSurface(
                baseline, afterSecond, generated, "after the second accepted reset");
            AssertInitialState(afterSecond, generated, "AfterSecondReset");

            AssertSessionIdentity(
                generated, service, hub, log, firstRequestId, eventIdBeforeFirst, afterSecond);
        }

        private static void Dirty(
            ResetSurfaceCase generated,
            PlayerMovementLoop loop,
            CameraConvergenceState camera,
            EnvironmentContactTracker contacts)
        {
            loop.SetForwardSpeed(generated.DirtyForwardSpeed);

            // Leave Center for good, then start a second segment and leave a request queued behind
            // it so the lane surface carries a non-Center lane, in-flight progress, and a queue.
            loop.RequestLane(LaneDirection.Right);
            loop.ExecuteMovementUpdate(Configuration.LaneChangeDuration);
            loop.RequestLane(LaneDirection.Left);
            loop.RequestLane(LaneDirection.Right);
            loop.ExecuteMovementUpdate(generated.PartialLaneTime);

            switch (generated.Action)
            {
                case DirtyAction.Jump:
                    loop.RequestJump();
                    loop.ExecuteMovementUpdate(generated.ActionTime);
                    break;
                case DirtyAction.Slide:
                    loop.RequestSlide();
                    loop.ExecuteMovementUpdate(generated.ActionTime);
                    break;
                case DirtyAction.Failure:
                    loop.RequestFailure();
                    break;
            }

            // The camera is driven after the player has moved, so the target differs from the pose
            // and the convergence stops mid-epoch with an outstanding smoothing budget.
            camera.Advance(loop.Snapshot.Position, generated.FirstCameraFrame);
            camera.Advance(loop.Snapshot.Position, generated.SecondCameraFrame);

            var obstacle = new StubEnvironmentObject(
                "obstacle-" + generated.CaseIndex.ToString(CultureInfo.InvariantCulture),
                EnvironmentObjectKind.Obstacle,
                0f);
            var collectible = new StubEnvironmentObject(
                "coin-" + generated.CaseIndex.ToString(CultureInfo.InvariantCulture),
                EnvironmentObjectKind.Coin,
                2.5f);
            contacts.Enter(11, obstacle, loop.Snapshot.Position);
            // A second child collider of the same logical contact is deduplicated rather than
            // recorded again, which is part of the state reset must clear.
            contacts.Enter(12, obstacle, loop.Snapshot.Position);
            contacts.Enter(21, collectible, loop.Snapshot.Position);
        }

        private static void AssertDirty(
            ResetSurfaceCase generated,
            PlayerResetSnapshot dirty,
            PlayerResetSnapshot baseline,
            PlayerMovementLoop loop,
            CameraConvergenceState camera,
            EnvironmentContactTracker contacts,
            Coverage coverage)
        {
            var context = "Case=" + generated;

            // Fixture-side evidence first: the drive itself must have produced dirty state.
            Assert.That(loop.Snapshot.CurrentLane, Is.Not.EqualTo(LogicalLane.Center),
                "The dirty drive must leave the current Logical_Lane away from Center. " + context);
            if (generated.Action == DirtyAction.Failure)
            {
                // Requirement 6.15: entering Failed clears the Lane_Request_Queue, so a failed case
                // carries the cleared queue into reset rather than a queued request.
                Assert.That(loop.Snapshot.LaneRequestQueue.Count, Is.EqualTo(0),
                    "Entering Failed must clear the Lane_Request_Queue before reset. " + context);
            }
            else
            {
                Assert.That(loop.Snapshot.LaneRequestQueue.Count, Is.GreaterThan(0),
                    "The dirty drive must leave Lane_Requests queued. " + context);
            }
            Assert.That(loop.ForwardSpeed, Is.Not.EqualTo(Configuration.ForwardSpeed),
                "The dirty drive must leave a non-configured Forward_Run_Speed. " + context);
            Assert.That(camera.RemainingCameraSettleTime, Is.GreaterThan(0f),
                "The dirty drive must leave an outstanding camera smoothing budget. " + context);
            Assert.That(camera.HasCameraTarget, Is.True,
                "The dirty drive must leave a live Camera_Target_Position epoch. " + context);
            Assert.That(camera.CameraConvergenceVelocity, Is.Not.EqualTo(Vector3.zero),
                "The dirty drive must leave a non-zero camera convergence velocity. " + context);
            Assert.That(camera.CameraPosition, Is.Not.EqualTo(Configuration.InitialCameraPosition),
                "The dirty drive must move the camera off the configured initial pose. " + context);
            Assert.That(contacts.ActiveContactCount, Is.EqualTo(2),
                "The dirty drive must record two active logical contacts. " + context);
            Assert.That(contacts.LastContactId, Is.EqualTo(2UL),
                "The dirty drive must deduplicate the repeated child collider entry. " + context);

            // The observed reset surface must report that dirty state, otherwise the equality
            // assertion after reset would be vacuous.
            Assert.That(dirty, Is.Not.EqualTo(baseline),
                "The observed reset surface must differ from Player_Initial_State before reset. " +
                context);
            Assert.That(dirty.Player.CurrentLane, Is.Not.EqualTo(LogicalLane.Center),
                "The observed reset surface must carry the non-Center current lane. " + context);
            Assert.That(dirty.Player.ForwardSpeed, Is.EqualTo(generated.DirtyForwardSpeed),
                "The observed reset surface must carry the requested Forward_Run_Speed. " + context);
            Assert.That(dirty.Player.Position, Is.Not.EqualTo(baseline.Player.Position),
                "The observed reset surface must carry the moved transform. " + context);
            Assert.That(dirty.RemainingCameraSettleTime, Is.GreaterThan(0f),
                "The observed reset surface must carry the outstanding camera smoothing budget. " +
                context);
            Assert.That(dirty.HasCameraTarget, Is.True,
                "The observed reset surface must carry the live camera target epoch. " + context);
            Assert.That(dirty.CameraConvergenceVelocity, Is.Not.EqualTo(Vector3.zero),
                "The observed reset surface must carry the camera convergence velocity. " + context);
            Assert.That(dirty.CameraPosition, Is.Not.EqualTo(Configuration.InitialCameraPosition),
                "The observed reset surface must carry the mid-convergence camera pose. " + context);
            Assert.That(dirty.ActiveLogicalContacts.Count, Is.EqualTo(2),
                "The observed reset surface must carry the active logical contacts. " + context);
            Assert.That(dirty.ContactEventDeduplication.Count, Is.EqualTo(2),
                "The observed reset surface must carry the contact event deduplication state. " +
                context);
            coverage.DirtyCamera = true;
            coverage.DirtyContacts = true;

            if (generated.Action != DirtyAction.Failure)
            {
                Assert.That(dirty.Player.LaneChangeProgress, Is.GreaterThan(0f),
                    "The observed reset surface must carry in-flight lane progress. " + context);
                Assert.That(dirty.Player.LaneRequestQueue.Count, Is.GreaterThan(0),
                    "The observed reset surface must carry the queued Lane_Requests. " + context);
                coverage.DirtyLaneSegment = true;
            }

            if (generated.Action == DirtyAction.Jump)
            {
                Assert.That(dirty.Player.VerticalVelocity, Is.Not.EqualTo(0f),
                    "A jumping case must carry a non-zero vertical velocity into reset. " + context);
                coverage.DirtyVerticalVelocity = true;
            }

            if (generated.Action != DirtyAction.Slide) return;

            Assert.That(dirty.Player.SlideElapsedTime, Is.GreaterThan(0f),
                "A sliding case must carry a non-zero Slide_Elapsed_Time into reset. " + context);
            Assert.That(dirty.Player.ColliderProfile, Is.EqualTo(Configuration.SlideCollider),
                "A sliding case must carry Slide_Collider_Profile into reset. " + context);
            coverage.DirtySlideProfile = true;
        }

        private static void AssertAccepted(
            ResetRequestResult result,
            string requestId,
            PlayerState sourceState,
            ResetSurfaceCase generated,
            string label)
        {
            var context = "Reset=" + label + ", SourceState=" + sourceState + ", Case=" + generated;

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "A fresh Request_Id must be accepted from an eligible state. " + context);
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset), context);
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.None), context);
            Assert.That(result.RequestId, Is.EqualTo(requestId),
                "An accepted result must return the supplied Request_Id. " + context);
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running),
                "A completed reset must expose Running. " + context);
        }

        /// <summary>
        /// The complete reset equality surface, field by field for a readable failure and then as one
        /// whole-snapshot comparison so no field can be added to the surface without being checked.
        /// </summary>
        private static void AssertResetSurface(
            PlayerResetSnapshot expected,
            PlayerResetSnapshot actual,
            ResetSurfaceCase generated,
            string label)
        {
            var prefix = label + ": ";
            SnapshotAssert.Field(prefix + "Player.Position", expected.Player.Position, actual.Player.Position);
            SnapshotAssert.Field(prefix + "Player.Rotation", expected.Player.Rotation, actual.Player.Rotation);
            SnapshotAssert.Field(prefix + "Player.State", expected.Player.State, actual.Player.State);
            SnapshotAssert.Field(prefix + "Player.IsGrounded", expected.Player.IsGrounded, actual.Player.IsGrounded);
            SnapshotAssert.Field(prefix + "Player.ForwardSpeed", expected.Player.ForwardSpeed, actual.Player.ForwardSpeed);
            SnapshotAssert.Field(prefix + "Player.CurrentLane", expected.Player.CurrentLane, actual.Player.CurrentLane);
            SnapshotAssert.Field(prefix + "Player.TargetLane", expected.Player.TargetLane, actual.Player.TargetLane);
            SnapshotAssert.Field(prefix + "Player.LateralPosition", expected.Player.LateralPosition, actual.Player.LateralPosition);
            SnapshotAssert.Field(prefix + "Player.LaneSegmentStart", expected.Player.LaneSegmentStart, actual.Player.LaneSegmentStart);
            SnapshotAssert.Field(prefix + "Player.LaneSegmentTarget", expected.Player.LaneSegmentTarget, actual.Player.LaneSegmentTarget);
            SnapshotAssert.Field(prefix + "Player.LaneChangeProgress", expected.Player.LaneChangeProgress, actual.Player.LaneChangeProgress);
            SnapshotAssert.Field(prefix + "Player.VerticalVelocity", expected.Player.VerticalVelocity, actual.Player.VerticalVelocity);
            SnapshotAssert.Field(prefix + "Player.SlideElapsedTime", expected.Player.SlideElapsedTime, actual.Player.SlideElapsedTime);
            SnapshotAssert.Field(prefix + "Player.ColliderProfile", expected.Player.ColliderProfile, actual.Player.ColliderProfile);
            SnapshotAssert.Field(prefix + "Player.LaneRequestQueue.Count", expected.Player.LaneRequestQueue.Count, actual.Player.LaneRequestQueue.Count);
            SnapshotAssert.Field(prefix + "Player.LaneRequestQueue", expected.Player.LaneRequestQueue, actual.Player.LaneRequestQueue);
            SnapshotAssert.Field(prefix + "Player.PendingActionRequests.Count", expected.Player.PendingActionRequests.Count, actual.Player.PendingActionRequests.Count);
            SnapshotAssert.Field(prefix + "Player.PendingActionRequests", expected.Player.PendingActionRequests, actual.Player.PendingActionRequests);
            SnapshotAssert.Field(prefix + "Player.ResetInProgress", expected.Player.ResetInProgress, actual.Player.ResetInProgress);
            SnapshotAssert.Field(prefix + "CameraPosition", expected.CameraPosition, actual.CameraPosition);
            SnapshotAssert.Field(prefix + "CameraRotation", expected.CameraRotation, actual.CameraRotation);
            SnapshotAssert.Field(prefix + "CameraFollowOffset", expected.CameraFollowOffset, actual.CameraFollowOffset);
            SnapshotAssert.Field(prefix + "PreviousCameraTarget", expected.PreviousCameraTarget, actual.PreviousCameraTarget);
            SnapshotAssert.Field(prefix + "CameraConvergenceVelocity", expected.CameraConvergenceVelocity, actual.CameraConvergenceVelocity);
            SnapshotAssert.Field(prefix + "RemainingCameraSettleTime", expected.RemainingCameraSettleTime, actual.RemainingCameraSettleTime);
            SnapshotAssert.Field(prefix + "HasCameraTarget", expected.HasCameraTarget, actual.HasCameraTarget);
            SnapshotAssert.Field(prefix + "InputLatchState", expected.InputLatchState, actual.InputLatchState);
            SnapshotAssert.Field(prefix + "InputConsumptionEnabled", expected.InputConsumptionEnabled, actual.InputConsumptionEnabled);
            SnapshotAssert.Field(prefix + "ActiveLogicalContacts.Count", expected.ActiveLogicalContacts.Count, actual.ActiveLogicalContacts.Count);
            SnapshotAssert.Field(prefix + "ActiveLogicalContacts", expected.ActiveLogicalContacts, actual.ActiveLogicalContacts);
            SnapshotAssert.Field(prefix + "ContactEventDeduplication.Count", expected.ContactEventDeduplication.Count, actual.ContactEventDeduplication.Count);
            SnapshotAssert.Field(prefix + "ContactEventDeduplication", expected.ContactEventDeduplication, actual.ContactEventDeduplication);
            SnapshotAssert.Preserved(
                expected,
                actual,
                context: "The complete PlayerResetSnapshot must be equal " + label + ". Case=" +
                    generated);
        }

        /// <summary>
        /// The reset-defined values of <c>Player_Initial_State</c>, asserted independently of the
        /// baseline so an equality that holds because both sides are wrong is still caught.
        /// </summary>
        private static void AssertInitialState(
            PlayerResetSnapshot snapshot,
            ResetSurfaceCase generated,
            string label)
        {
            var context = label + ", Case=" + generated;

            Assert.That(snapshot.Player.State, Is.EqualTo(PlayerState.Running),
                "Player_Initial_State must expose Running. " + context);
            Assert.That(snapshot.Player.ResetInProgress, Is.False,
                "Player_Initial_State must clear in-progress reset status. " + context);
            Assert.That(snapshot.Player.CurrentLane, Is.EqualTo(LogicalLane.Center),
                "Player_Initial_State must place the current Logical_Lane at Center. " + context);
            Assert.That(snapshot.Player.TargetLane, Is.EqualTo(LogicalLane.Center),
                "Player_Initial_State must place Target_Lane at Center. " + context);
            Assert.That(snapshot.Player.LateralPosition, Is.EqualTo(Configuration.LaneCenters.y),
                "Player_Initial_State must sit on the Center Lane_Center. " + context);
            Assert.That(snapshot.Player.LaneChangeProgress, Is.EqualTo(0f),
                "Player_Initial_State must carry zero lane-change progress. " + context);
            Assert.That(snapshot.Player.VerticalVelocity, Is.EqualTo(0f),
                "Player_Initial_State must carry zero transient vertical velocity. " + context);
            Assert.That(snapshot.Player.SlideElapsedTime, Is.EqualTo(0f),
                "Player_Initial_State must carry zero Slide_Elapsed_Time. " + context);
            Assert.That(snapshot.Player.ColliderProfile, Is.EqualTo(Configuration.BaselineCollider),
                "Player_Initial_State must carry Baseline_Collider_Profile. " + context);
            Assert.That(snapshot.Player.ForwardSpeed, Is.EqualTo(Configuration.ForwardSpeed),
                "Player_Initial_State must carry the configured Forward_Run_Speed. " + context);
            Assert.That(snapshot.Player.LaneRequestQueue.Count, Is.EqualTo(0),
                "Player_Initial_State must carry an empty Lane_Request_Queue. " + context);
            Assert.That(snapshot.Player.PendingActionRequests.Count, Is.EqualTo(0),
                "Player_Initial_State must carry empty pending action requests. " + context);
            Assert.That(snapshot.CameraPosition, Is.EqualTo(Configuration.InitialCameraPosition),
                "Player_Initial_State must carry the configured initial camera pose. " + context);
            Assert.That(snapshot.CameraConvergenceVelocity, Is.EqualTo(Vector3.zero),
                "Player_Initial_State must clear camera convergence velocity. " + context);
            Assert.That(snapshot.RemainingCameraSettleTime, Is.EqualTo(0f),
                "Player_Initial_State must clear camera smoothing progress. " + context);
            Assert.That(snapshot.HasCameraTarget, Is.False,
                "Player_Initial_State must clear the camera target epoch. " + context);
            Assert.That(snapshot.InputLatchState, Is.EqualTo(InputLatchState.Neutral),
                "Player_Initial_State must leave the input latch neutral. " + context);
            Assert.That(snapshot.InputConsumptionEnabled, Is.True,
                "Player_Initial_State must re-enable input consumption. " + context);
            Assert.That(snapshot.ActiveLogicalContacts.Count, Is.EqualTo(0),
                "Player_Initial_State must clear contact tracking. " + context);
            Assert.That(snapshot.ContactEventDeduplication.Count, Is.EqualTo(0),
                "Player_Initial_State must clear event-deduplication tracking. " + context);
        }

        private static void AssertSessionIdentity(
            ResetSurfaceCase generated,
            ResetSurfaceSeam service,
            PlayerEventHub hub,
            EventLog log,
            string firstRequestId,
            ulong eventIdBeforeFirstReset,
            PlayerResetSnapshot afterSecondReset)
        {
            var context = "Case=" + generated;

            Assert.That(hub.LastEventId, Is.GreaterThan(eventIdBeforeFirstReset),
                "The session Event_Id sequence must keep advancing across resets rather than " +
                "restarting. " + context);
            var seen = new HashSet<ulong>();
            var previous = 0UL;
            for (var index = 0; index < log.EventIds.Count; index++)
            {
                var eventId = log.EventIds[index];
                Assert.That(seen.Add(eventId), Is.True,
                    "No session Event_Id may be reused after a reset. EventId=" +
                    eventId.ToString(CultureInfo.InvariantCulture) + ", " + context);
                Assert.That(eventId, Is.GreaterThan(previous),
                    "Session Event_Ids must increase monotonically in publication order across " +
                    "resets. EventId=" + eventId.ToString(CultureInfo.InvariantCulture) + ", " +
                    context);
                previous = eventId;
            }

            var replay = service.RequestReset(firstRequestId);
            Assert.That(replay.Status, Is.EqualTo(CommandStatus.Rejected),
                "The reset request ledger must survive later resets, so an accepted Request_Id " +
                "stays rejected as a duplicate. " + context);
            Assert.That(replay.Reason, Is.EqualTo(RejectionReason.DuplicateRequestId), context);
            Assert.That(replay.RequestId, Is.EqualTo(firstRequestId), context);
            Assert.That(replay.CurrentState, Is.EqualTo(PlayerState.Running), context);
            AssertResetSurface(
                afterSecondReset, service.Snapshot, generated,
                "after a rejected duplicate Request_Id");
        }

        private static string RequestId(int caseIndex, string suffix)
        {
            return "reset-" + caseIndex.ToString(CultureInfo.InvariantCulture) + "-" + suffix;
        }

        private enum DirtyAction
        {
            None,
            Jump,
            Slide,
            Failure
        }

        private sealed class ResetSurfaceCase
        {
            public ResetSurfaceCase(
                int caseIndex,
                float initialForwardPosition,
                float dirtyForwardSpeed,
                float partialLaneTime,
                float actionTime,
                float firstCameraFrame,
                float secondCameraFrame,
                DirtyAction action,
                bool restorationSafe)
            {
                CaseIndex = caseIndex;
                InitialForwardPosition = initialForwardPosition;
                DirtyForwardSpeed = dirtyForwardSpeed;
                PartialLaneTime = partialLaneTime;
                ActionTime = actionTime;
                FirstCameraFrame = firstCameraFrame;
                SecondCameraFrame = secondCameraFrame;
                Action = action;
                RestorationSafe = restorationSafe;
            }

            public int CaseIndex { get; }
            public float InitialForwardPosition { get; }
            public float DirtyForwardSpeed { get; }
            public float PartialLaneTime { get; }
            public float ActionTime { get; }
            public float FirstCameraFrame { get; }
            public float SecondCameraFrame { get; }
            public DirtyAction Action { get; }
            public bool RestorationSafe { get; }

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append("Case=").Append(CaseIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(", Action=").Append(Action)
                    .Append(", InitialForwardPosition=")
                    .Append(InitialForwardPosition.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", DirtyForwardSpeed=")
                    .Append(DirtyForwardSpeed.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", PartialLaneTime=")
                    .Append(PartialLaneTime.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", ActionTime=")
                    .Append(ActionTime.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", CameraFrames=[")
                    .Append(FirstCameraFrame.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(SecondCameraFrame.ToString("R", CultureInfo.InvariantCulture))
                    .Append("], RestorationSafe=").Append(RestorationSafe);
                return builder.ToString();
            }
        }

        private sealed class Coverage
        {
            public bool DirtyVerticalVelocity;
            public bool DirtySlideProfile;
            public bool DirtyLaneSegment;
            public bool DirtyCamera;
            public bool DirtyContacts;

            public readonly HashSet<PlayerState> SourceStates = new HashSet<PlayerState>();
        }

        private sealed class EventLog
        {
            private readonly List<ulong> eventIds = new List<ulong>();

            public IReadOnlyList<ulong> EventIds { get { return eventIds; } }

            public void Add(PlayerHitEvent value) { eventIds.Add(value.EventId); }
            public void Add(CoinCollectedEvent value) { eventIds.Add(value.EventId); }
            public void Add(PlayerStateChangedEvent value) { eventIds.Add(value.EventId); }
            public void Add(PlayerResetStartedEvent value) { eventIds.Add(value.EventId); }
            public void Add(PlayerResetCompletedEvent value) { eventIds.Add(value.EventId); }
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
        /// Deterministic stand-in for the Unity motor: displacement integration against a flat ground
        /// plane at y = 0, with no scene objects.
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
        /// Red-first seam for the Task 9.4 facade wiring: the movement loop must be constructible with
        /// the session <see cref="PlayerEventHub"/> so every accepted transition is published through
        /// the single session Event_Id sequence.
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
        /// resolved by parameter type so Task 9.4 keeps freedom over the exact signature, provided the
        /// service consumes the movement loop, the session event hub, the camera convergence state,
        /// and the contact tracker whose state it must reset, exposes
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
