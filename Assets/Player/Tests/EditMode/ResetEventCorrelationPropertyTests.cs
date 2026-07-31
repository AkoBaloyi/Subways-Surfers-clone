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
    /// Property 11. Every generated case replays one deterministic command script through a
    /// hub-wired movement loop and the reset service: valid transitions, fresh reset IDs, empty and
    /// absent reset IDs, an ID already present in the ledger, and a re-entrant request issued while
    /// the accepted reset is still in progress. Each valid transition must publish exactly one
    /// <c>Player_State_Changed_Event</c>, each accepted Request_Id must correlate exactly one
    /// started/completed pair reporting Running, each rejected request must publish no reset
    /// lifecycle event, and every Event_Id in the session must stay unique and monotonic across
    /// resets. No scene objects are created; each case is disposed by unsubscribing the event
    /// handlers and dropping the fixture references.
    /// </summary>
    public sealed class ResetEventCorrelationPropertyTests
    {
        private const int Seed = 911_017;
        private const int CaseCount = 120;
        private static readonly PlayerConfiguration Configuration = PlayerConfiguration.SafeDefaults;

        private int generatedCaseIndex;

        // **Validates: Requirements 7.7, 7.8, 7.9, 7.10, 7.11, 10.12, 14.6, 14.7, 14.8**
        [Test]
        [Description("Feature: player-controller, Property 11: Transition and reset events are exactly-once and correlated")]
        public void TransitionAndResetEventsAreExactlyOnceAndCorrelated_Property11_Requirements_7_7_Through_7_11_10_12_And_14_6_Through_14_8()
        {
            generatedCaseIndex = 0;
            var coverage = new Coverage();

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                generated => AssertProperty(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.AcceptedFresh, Is.True,
                "The generated cases must accept at least one fresh Request_Id.");
            Assert.That(coverage.RejectedEmpty, Is.True,
                "The generated cases must reject an empty Request_Id.");
            Assert.That(coverage.RejectedAbsent, Is.True,
                "The generated cases must reject an absent Request_Id.");
            Assert.That(coverage.RejectedDuplicate, Is.True,
                "The generated cases must reject a Request_Id already in the ledger.");
            Assert.That(coverage.RejectedInProgress, Is.True,
                "The generated cases must reject a Reset_Request while a reset is in progress.");
            Assert.That(coverage.Causes, Is.SupersetOf(new[]
            {
                PlayerTransitionCause.JumpRequested,
                PlayerTransitionCause.SlideRequested,
                PlayerTransitionCause.Landed,
                PlayerTransitionCause.SlideRestored,
                PlayerTransitionCause.FailureRequested,
                PlayerTransitionCause.ResetRequested,
                PlayerTransitionCause.ResetCompleted
            }), "Every transition cause must be exercised by the generated transitions.");
            Assert.That(coverage.ResetSourceStates, Is.SupersetOf(new[]
            {
                PlayerState.Running,
                PlayerState.Jumping,
                PlayerState.Sliding,
                PlayerState.Failed
            }), "Accepted resets must be exercised from every eligible source state.");
        }

        private CorrelationCase Generate(Random random)
        {
            var caseIndex = generatedCaseIndex++;
            var steps = new List<Step>();

            // One zero-time update samples grounding before any action command is issued.
            steps.Add(Step.Advance(0f));
            steps.Add(Step.Action(StepKind.Jump));
            steps.Add(Step.Advance(1f));
            steps.Add(Step.Advance(NextShortFrame(random)));
            steps.Add(Step.Action(StepKind.Slide));
            steps.Add(Step.Advance(Configuration.SlideDuration + NextShortFrame(random)));
            AppendMovement(random, steps, random.Next(1, 4));

            var ledgerId = RequestId(caseIndex, "a");
            steps.Add(Step.Reset(StepKind.ResetFresh, ledgerId));
            AppendMovement(random, steps, random.Next(0, 3));
            steps.Add(Step.Action(StepKind.Failure));
            steps.Add(Step.Reset(StepKind.ResetDuplicate, ledgerId));
            steps.Add(caseIndex % 2 == 0
                ? Step.Reset(StepKind.ResetEmpty, string.Empty)
                : Step.Reset(StepKind.ResetAbsent, null));
            steps.Add(Step.Reset(StepKind.ResetWhileInProgress, RequestId(caseIndex, "b")));

            if (caseIndex % 3 == 0) steps.Add(Step.Action(StepKind.Jump));
            else if (caseIndex % 3 == 1) steps.Add(Step.Action(StepKind.Slide));
            steps.Add(Step.Reset(StepKind.ResetFresh, RequestId(caseIndex, "c")));
            AppendMovement(random, steps, random.Next(0, 3));

            return new CorrelationCase(
                caseIndex,
                new Vector3(
                    Configuration.LaneCenters[random.Next(3)],
                    0f,
                    GeneratedValues.NextFiniteFloat(random, -50f, 50f)),
                GeneratedValues.NextFiniteFloat(random, 1f, 20f),
                caseIndex % 2 == 0,
                "nested-" + caseIndex.ToString(CultureInfo.InvariantCulture),
                steps);
        }

        private static void AppendMovement(Random random, List<Step> steps, int count)
        {
            for (var index = 0; index < count; index++)
            {
                switch (random.Next(6))
                {
                    case 0:
                        steps.Add(Step.Advance(0f));
                        break;
                    case 1:
                        steps.Add(Step.Advance(NextShortFrame(random)));
                        break;
                    case 2:
                        steps.Add(Step.Action(StepKind.LaneLeft));
                        break;
                    case 3:
                        steps.Add(Step.Action(StepKind.LaneRight));
                        break;
                    case 4:
                        steps.Add(Step.Action(StepKind.Jump));
                        break;
                    default:
                        steps.Add(Step.Action(StepKind.Slide));
                        break;
                }
            }
        }

        private static float NextShortFrame(Random random)
        {
            return GeneratedValues.NextFiniteFloat(random, 0.005f, 0.05f);
        }

        private static string RequestId(int caseIndex, string suffix)
        {
            return "reset-" + caseIndex.ToString(CultureInfo.InvariantCulture) + "-" + suffix;
        }

        private static void AssertProperty(CorrelationCase generated, Coverage coverage)
        {
            var motor = new DeterministicMotorSurface(generated.InitialPosition, generated.RestorationSafe);
            var diagnostics = new List<ValidationDiagnostic>();
            var hub = new PlayerEventHub();
            var log = new EventLog();
            var loop = MovementPublicationSeam.CreateLoop(Configuration, motor, diagnostics.Add, hub);
            var service = PlayerResetServiceSeam.Create(Configuration, motor, loop, hub, diagnostics.Add);
            var probe = new InProgressProbe(service, generated.NestedRequestId);

            Action<PlayerStateChangedEvent> stateChanged = value =>
            {
                log.Add(value);
                probe.OnStateChanged(value);
            };
            Action<PlayerResetStartedEvent> resetStarted = log.Add;
            Action<PlayerResetCompletedEvent> resetCompleted = log.Add;

            hub.StateChanged += stateChanged;
            hub.ResetStarted += resetStarted;
            hub.ResetCompleted += resetCompleted;
            try
            {
                Execute(generated, loop, service, probe, log, coverage);
            }
            finally
            {
                hub.StateChanged -= stateChanged;
                hub.ResetStarted -= resetStarted;
                hub.ResetCompleted -= resetCompleted;
            }
        }

        private static void Execute(
            CorrelationCase generated,
            PlayerMovementLoop loop,
            PlayerResetServiceSeam service,
            InProgressProbe probe,
            EventLog log,
            Coverage coverage)
        {
            loop.SetForwardSpeed(generated.ForwardSpeed);
            var acceptedRequestIds = new List<string>();

            for (var index = 0; index < generated.Steps.Count; index++)
            {
                var step = generated.Steps[index];
                var before = loop.CurrentState;
                var publishedBefore = log.Count;

                if (!step.IsReset)
                {
                    ExecuteMovementStep(step, loop);
                    AssertTransitionPublication(
                        generated, index, before, loop.CurrentState, log, publishedBefore, coverage);
                    continue;
                }

                var accepted = step.Kind == StepKind.ResetFresh ||
                    step.Kind == StepKind.ResetWhileInProgress;
                if (step.Kind == StepKind.ResetWhileInProgress) probe.Arm();

                var result = service.RequestReset(step.RequestId);

                if (accepted)
                {
                    AssertAcceptedReset(
                        generated, index, step, before, loop.CurrentState, result,
                        log, publishedBefore, coverage);
                    acceptedRequestIds.Add(step.RequestId);
                }
                else
                {
                    AssertRejectedReset(
                        generated, index, step, before, loop.CurrentState, result,
                        log, publishedBefore, coverage);
                }

                if (step.Kind != StepKind.ResetWhileInProgress) continue;

                AssertInProgressRejection(generated, index, probe, log, coverage);
                probe.Disarm();
            }

            AssertSessionIntegrity(generated, log, acceptedRequestIds);
        }

        private static void ExecuteMovementStep(Step step, PlayerMovementLoop loop)
        {
            switch (step.Kind)
            {
                case StepKind.Advance:
                    loop.ExecuteMovementUpdate(step.ElapsedTime);
                    break;
                case StepKind.LaneLeft:
                    loop.RequestLane(LaneDirection.Left);
                    break;
                case StepKind.LaneRight:
                    loop.RequestLane(LaneDirection.Right);
                    break;
                case StepKind.Jump:
                    loop.RequestJump();
                    break;
                case StepKind.Slide:
                    loop.RequestSlide();
                    break;
                default:
                    loop.RequestFailure();
                    break;
            }
        }

        private static void AssertTransitionPublication(
            CorrelationCase generated,
            int stepIndex,
            PlayerState before,
            PlayerState after,
            EventLog log,
            int publishedBefore,
            Coverage coverage)
        {
            var published = log.Since(publishedBefore);
            var context = Context(generated, stepIndex, before, after);

            if (after == before)
            {
                Assert.That(published.Count, Is.EqualTo(0),
                    "A rejected or no-op command must publish zero events. " + context);
                return;
            }

            Assert.That(published.Count, Is.EqualTo(1),
                "A valid transition must publish exactly one event. " + context);
            var observed = published[0];
            Assert.That(observed.Kind, Is.EqualTo(ObservedKind.StateChanged),
                "A valid transition must publish one Player_State_Changed_Event. " + context);
            Assert.That(observed.PreviousState, Is.EqualTo(before),
                "The published event must carry the previous Player_State. " + context);
            Assert.That(observed.CurrentState, Is.EqualTo(after),
                "The published event must carry the current Player_State. " + context);
            Assert.That(observed.Cause, Is.EqualTo(ExpectedCause(before, after)),
                "The published event must carry the transition cause. " + context);
            coverage.Causes.Add(observed.Cause);
        }

        private static void AssertAcceptedReset(
            CorrelationCase generated,
            int stepIndex,
            Step step,
            PlayerState before,
            PlayerState after,
            ResetRequestResult result,
            EventLog log,
            int publishedBefore,
            Coverage coverage)
        {
            var context = Context(generated, stepIndex, before, after);

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "A fresh Request_Id must be accepted. " + context);
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset), context);
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.None), context);
            Assert.That(result.RequestId, Is.EqualTo(step.RequestId),
                "An accepted result must return the supplied Request_Id. " + context);
            Assert.That(result.CurrentState, Is.EqualTo(PlayerState.Running),
                "An accepted reset must report Running once restoration completes. " + context);
            Assert.That(after, Is.EqualTo(PlayerState.Running),
                "An accepted reset must leave the state machine in Running. " + context);

            var published = log.Since(publishedBefore);
            var started = Select(published, ObservedKind.ResetStarted, step.RequestId);
            var completed = Select(published, ObservedKind.ResetCompleted, step.RequestId);
            var transitions = Select(published, ObservedKind.StateChanged, null);

            Assert.That(started.Count, Is.EqualTo(1),
                "An accepted Request_Id must publish exactly one Player_Reset_Started_Event. " + context);
            Assert.That(completed.Count, Is.EqualTo(1),
                "An accepted Request_Id must publish exactly one Player_Reset_Completed_Event. " + context);
            Assert.That(completed[0].ResultingState, Is.EqualTo(PlayerState.Running),
                "The completed event must report Running. " + context);
            Assert.That(started[0].EventId, Is.LessThan(completed[0].EventId),
                "The started event must precede the completed event. " + context);
            Assert.That(transitions.Count, Is.EqualTo(2),
                "An accepted reset must publish the Resetting and Running transitions only. " + context);
            Assert.That(transitions[0].PreviousState, Is.EqualTo(before), context);
            Assert.That(transitions[0].CurrentState, Is.EqualTo(PlayerState.Resetting), context);
            Assert.That(transitions[0].Cause, Is.EqualTo(PlayerTransitionCause.ResetRequested), context);
            Assert.That(transitions[1].PreviousState, Is.EqualTo(PlayerState.Resetting), context);
            Assert.That(transitions[1].CurrentState, Is.EqualTo(PlayerState.Running), context);
            Assert.That(transitions[1].Cause, Is.EqualTo(PlayerTransitionCause.ResetCompleted), context);
            Assert.That(published.Count, Is.EqualTo(4),
                "An accepted reset must publish exactly its correlated pair and two transitions. " +
                context);

            coverage.AcceptedFresh = true;
            coverage.ResetSourceStates.Add(before);
            coverage.Causes.Add(PlayerTransitionCause.ResetRequested);
            coverage.Causes.Add(PlayerTransitionCause.ResetCompleted);
        }

        private static void AssertRejectedReset(
            CorrelationCase generated,
            int stepIndex,
            Step step,
            PlayerState before,
            PlayerState after,
            ResetRequestResult result,
            EventLog log,
            int publishedBefore,
            Coverage coverage)
        {
            var context = Context(generated, stepIndex, before, after);
            var expectedReason = step.Kind == StepKind.ResetDuplicate
                ? RejectionReason.DuplicateRequestId
                : RejectionReason.MissingRequestId;

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected),
                "An empty, absent, or ledgered Request_Id must be rejected. " + context);
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.Reset), context);
            Assert.That(result.Reason, Is.EqualTo(expectedReason), context);
            Assert.That(result.RequestId, Is.EqualTo(step.RequestId),
                "A rejected result must return the supplied Request_Id. " + context);
            Assert.That(result.CurrentState, Is.EqualTo(before),
                "A rejected reset must preserve the current Player_State. " + context);
            Assert.That(after, Is.EqualTo(before),
                "A rejected reset must preserve the current Player_State. " + context);
            Assert.That(log.Since(publishedBefore).Count, Is.EqualTo(0),
                "A rejected reset must publish zero lifecycle events. " + context);

            if (step.Kind == StepKind.ResetDuplicate) coverage.RejectedDuplicate = true;
            else if (step.Kind == StepKind.ResetEmpty) coverage.RejectedEmpty = true;
            else coverage.RejectedAbsent = true;
        }

        private static void AssertInProgressRejection(
            CorrelationCase generated,
            int stepIndex,
            InProgressProbe probe,
            EventLog log,
            Coverage coverage)
        {
            var context = Context(
                generated, stepIndex, PlayerState.Resetting, PlayerState.Resetting);

            Assert.That(probe.Fired, Is.True,
                "The accepted reset must publish its Resetting transition so a re-entrant " +
                "Reset_Request can be observed. " + context);
            Assert.That(probe.ObservedState, Is.EqualTo(PlayerState.Resetting), context);
            Assert.That(probe.Result.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Reset_Request arriving while a reset is in progress must be rejected. " + context);
            Assert.That(probe.Result.Reason, Is.EqualTo(RejectionReason.ResetInProgress), context);
            Assert.That(probe.Result.RequestId, Is.EqualTo(generated.NestedRequestId), context);
            Assert.That(probe.Result.CurrentState, Is.EqualTo(PlayerState.Resetting), context);
            Assert.That(log.CountFor(generated.NestedRequestId), Is.EqualTo(0),
                "An in-progress rejection must publish zero reset lifecycle events. " + context);

            coverage.RejectedInProgress = true;
        }

        private static void AssertSessionIntegrity(
            CorrelationCase generated,
            EventLog log,
            IReadOnlyList<string> acceptedRequestIds)
        {
            Assert.That(acceptedRequestIds.Count, Is.GreaterThanOrEqualTo(3),
                "Each case must accept several resets so Event_Id integrity is verified " +
                "across resets. Case=" + generated);

            var seen = new HashSet<ulong>();
            var previous = 0UL;
            for (var index = 0; index < log.Count; index++)
            {
                var observed = log.Events[index];
                Assert.That(seen.Add(observed.EventId), Is.True,
                    "Every session Event_Id must be unique, including across resets. Event=" +
                    observed + ", Case=" + generated);
                Assert.That(observed.EventId, Is.GreaterThan(previous),
                    "Session Event_Ids must increase monotonically in publication order, " +
                    "including across resets. Event=" + observed + ", Case=" + generated);
                previous = observed.EventId;
            }

            for (var index = 0; index < acceptedRequestIds.Count; index++)
            {
                var requestId = acceptedRequestIds[index];
                Assert.That(log.CountFor(requestId, ObservedKind.ResetStarted), Is.EqualTo(1),
                    "Request_Id '" + requestId + "' must correlate exactly one started event " +
                    "for the whole session. Case=" + generated);
                Assert.That(log.CountFor(requestId, ObservedKind.ResetCompleted), Is.EqualTo(1),
                    "Request_Id '" + requestId + "' must correlate exactly one completed event " +
                    "for the whole session. Case=" + generated);
            }
        }

        private static PlayerTransitionCause ExpectedCause(PlayerState before, PlayerState after)
        {
            switch (after)
            {
                case PlayerState.Jumping:
                    return PlayerTransitionCause.JumpRequested;
                case PlayerState.Sliding:
                    return PlayerTransitionCause.SlideRequested;
                case PlayerState.Failed:
                    return PlayerTransitionCause.FailureRequested;
                case PlayerState.Resetting:
                    return PlayerTransitionCause.ResetRequested;
                default:
                    if (before == PlayerState.Sliding) return PlayerTransitionCause.SlideRestored;
                    return before == PlayerState.Resetting
                        ? PlayerTransitionCause.ResetCompleted
                        : PlayerTransitionCause.Landed;
            }
        }

        private static IReadOnlyList<Observation> Select(
            IReadOnlyList<Observation> source,
            ObservedKind kind,
            string requestId)
        {
            var result = new List<Observation>();
            for (var index = 0; index < source.Count; index++)
            {
                var observed = source[index];
                if (observed.Kind != kind) continue;
                if (requestId != null &&
                    !string.Equals(observed.RequestId, requestId, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(observed);
            }

            return result;
        }

        private static string Context(
            CorrelationCase generated,
            int stepIndex,
            PlayerState before,
            PlayerState after)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Step={0} ({1}), StateBefore={2}, StateAfter={3}, Case={4}",
                stepIndex,
                generated.Steps[stepIndex],
                before,
                after,
                generated);
        }

        private enum StepKind
        {
            Advance,
            LaneLeft,
            LaneRight,
            Jump,
            Slide,
            Failure,
            ResetFresh,
            ResetEmpty,
            ResetAbsent,
            ResetDuplicate,
            ResetWhileInProgress
        }

        private enum ObservedKind
        {
            StateChanged,
            ResetStarted,
            ResetCompleted
        }

        private readonly struct Step
        {
            private Step(StepKind kind, float elapsedTime, string requestId)
            {
                Kind = kind;
                ElapsedTime = elapsedTime;
                RequestId = requestId;
            }

            public StepKind Kind { get; }
            public float ElapsedTime { get; }
            public string RequestId { get; }

            public bool IsReset
            {
                get
                {
                    return Kind == StepKind.ResetFresh ||
                        Kind == StepKind.ResetEmpty ||
                        Kind == StepKind.ResetAbsent ||
                        Kind == StepKind.ResetDuplicate ||
                        Kind == StepKind.ResetWhileInProgress;
                }
            }

            public static Step Advance(float elapsedTime)
            {
                return new Step(StepKind.Advance, elapsedTime, null);
            }

            public static Step Action(StepKind kind)
            {
                return new Step(kind, 0f, null);
            }

            public static Step Reset(StepKind kind, string requestId)
            {
                return new Step(kind, 0f, requestId);
            }

            public override string ToString()
            {
                if (Kind == StepKind.Advance)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture, "Advance@{0:R}", ElapsedTime);
                }

                return IsReset
                    ? Kind + "(" + (RequestId == null ? "<absent>" : "'" + RequestId + "'") + ")"
                    : Kind.ToString();
            }
        }

        private readonly struct Observation
        {
            private Observation(
                ObservedKind kind,
                ulong eventId,
                PlayerState previousState,
                PlayerState currentState,
                PlayerTransitionCause cause,
                string requestId,
                PlayerState resultingState)
            {
                Kind = kind;
                EventId = eventId;
                PreviousState = previousState;
                CurrentState = currentState;
                Cause = cause;
                RequestId = requestId;
                ResultingState = resultingState;
            }

            public ObservedKind Kind { get; }
            public ulong EventId { get; }
            public PlayerState PreviousState { get; }
            public PlayerState CurrentState { get; }
            public PlayerTransitionCause Cause { get; }
            public string RequestId { get; }
            public PlayerState ResultingState { get; }

            public static Observation From(PlayerStateChangedEvent value)
            {
                return new Observation(
                    ObservedKind.StateChanged,
                    value.EventId,
                    value.PreviousState,
                    value.CurrentState,
                    value.TransitionCause,
                    null,
                    value.CurrentState);
            }

            public static Observation From(PlayerResetStartedEvent value)
            {
                return new Observation(
                    ObservedKind.ResetStarted,
                    value.EventId,
                    PlayerState.Resetting,
                    PlayerState.Resetting,
                    PlayerTransitionCause.ResetRequested,
                    value.RequestId,
                    PlayerState.Resetting);
            }

            public static Observation From(PlayerResetCompletedEvent value)
            {
                return new Observation(
                    ObservedKind.ResetCompleted,
                    value.EventId,
                    PlayerState.Resetting,
                    value.ResultingState,
                    PlayerTransitionCause.ResetCompleted,
                    value.RequestId,
                    value.ResultingState);
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
                    default:
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "ResetCompleted#{0}('{1}',{2})", EventId, RequestId, ResultingState);
                }
            }
        }

        private sealed class EventLog
        {
            private readonly List<Observation> events = new List<Observation>();

            public int Count { get { return events.Count; } }
            public IReadOnlyList<Observation> Events { get { return events; } }

            public void Add(PlayerStateChangedEvent value) { events.Add(Observation.From(value)); }
            public void Add(PlayerResetStartedEvent value) { events.Add(Observation.From(value)); }
            public void Add(PlayerResetCompletedEvent value) { events.Add(Observation.From(value)); }

            public IReadOnlyList<Observation> Since(int startIndex)
            {
                var result = new List<Observation>(events.Count - startIndex);
                for (var index = startIndex; index < events.Count; index++) result.Add(events[index]);
                return result;
            }

            public int CountFor(string requestId)
            {
                var count = 0;
                for (var index = 0; index < events.Count; index++)
                {
                    if (events[index].Kind == ObservedKind.StateChanged) continue;
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
        }

        /// <summary>
        /// Issues one re-entrant Reset_Request the first time an armed reset publishes its Resetting
        /// transition, which is the only deterministic point where the atomic sequence is still in
        /// progress.
        /// </summary>
        private sealed class InProgressProbe
        {
            private readonly PlayerResetServiceSeam service;
            private readonly string nestedRequestId;
            private bool armed;

            public InProgressProbe(PlayerResetServiceSeam service, string nestedRequestId)
            {
                this.service = service;
                this.nestedRequestId = nestedRequestId;
            }

            public bool Fired { get; private set; }
            public PlayerState ObservedState { get; private set; }
            public ResetRequestResult Result { get; private set; }

            public void Arm()
            {
                armed = true;
                Fired = false;
                Result = default(ResetRequestResult);
            }

            public void Disarm() { armed = false; }

            public void OnStateChanged(PlayerStateChangedEvent value)
            {
                if (!armed || Fired || value.CurrentState != PlayerState.Resetting) return;

                Fired = true;
                ObservedState = value.CurrentState;
                Result = service.RequestReset(nestedRequestId);
            }
        }

        private sealed class CorrelationCase
        {
            public CorrelationCase(
                int caseIndex,
                Vector3 initialPosition,
                float forwardSpeed,
                bool restorationSafe,
                string nestedRequestId,
                IReadOnlyList<Step> steps)
            {
                CaseIndex = caseIndex;
                InitialPosition = initialPosition;
                ForwardSpeed = forwardSpeed;
                RestorationSafe = restorationSafe;
                NestedRequestId = nestedRequestId;
                Steps = steps;
            }

            public int CaseIndex { get; }
            public Vector3 InitialPosition { get; }
            public float ForwardSpeed { get; }
            public bool RestorationSafe { get; }
            public string NestedRequestId { get; }
            public IReadOnlyList<Step> Steps { get; }

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append("Case=").Append(CaseIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(", Position=(")
                    .Append(InitialPosition.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(InitialPosition.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(InitialPosition.z.ToString("R", CultureInfo.InvariantCulture))
                    .Append("), ForwardSpeed=")
                    .Append(ForwardSpeed.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", RestorationSafe=").Append(RestorationSafe)
                    .Append(", NestedRequestId=").Append(NestedRequestId)
                    .Append(", Steps=[");
                for (var index = 0; index < Steps.Count; index++)
                {
                    if (index > 0) builder.Append("; ");
                    builder.Append(Steps[index]);
                }

                return builder.Append(']').ToString();
            }
        }

        private sealed class Coverage
        {
            public bool AcceptedFresh;
            public bool RejectedEmpty;
            public bool RejectedAbsent;
            public bool RejectedDuplicate;
            public bool RejectedInProgress;

            public readonly HashSet<PlayerTransitionCause> Causes =
                new HashSet<PlayerTransitionCause>();

            public readonly HashSet<PlayerState> ResetSourceStates = new HashSet<PlayerState>();
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
                    if (!SeamArguments.TryResolve(constructor, candidates, hub, out arguments)) continue;

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
        /// the service consumes the movement loop and the session event hub and exposes
        /// <c>ResetRequestResult RequestReset(string requestId)</c>.
        /// </summary>
        private sealed class PlayerResetServiceSeam
        {
            private const string PreferredTypeName = "SubwaySurfers.Player.Domain.PlayerResetService";
            private const string AlternateTypeName = "SubwaySurfers.Player.PlayerResetService";

            private readonly object instance;
            private readonly MethodInfo requestReset;

            private PlayerResetServiceSeam(object instance, MethodInfo requestReset)
            {
                this.instance = instance;
                this.requestReset = requestReset;
            }

            public static PlayerResetServiceSeam Create(
                PlayerConfiguration configuration,
                IPlayerMotorSurface motor,
                PlayerMovementLoop loop,
                PlayerEventHub hub,
                Action<ValidationDiagnostic> diagnosticSink)
            {
                var runtimeAssembly = typeof(PlayerState).Assembly;
                var type = runtimeAssembly.GetType(PreferredTypeName) ??
                    runtimeAssembly.GetType(AlternateTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 9.4 must provide " + PreferredTypeName + " with a public constructor " +
                    "consuming the PlayerMovementLoop and the session PlayerEventHub and a public " +
                    "ResetRequestResult RequestReset(string requestId) that performs the atomic " +
                    "reset sequence and publishes the correlated started/completed pair.");

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

                var candidates = new object[] { loop, hub, motor, diagnosticSink, configuration };
                var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                foreach (var constructor in constructors)
                {
                    object[] arguments;
                    if (!SeamArguments.TryResolve(constructor, candidates, loop, hub, out arguments))
                        continue;

                    return new PlayerResetServiceSeam(
                        SeamArguments.Invoke(constructor, arguments), method);
                }

                Assert.Fail(
                    type.FullName + " must expose a public constructor consuming the " +
                    "PlayerMovementLoop and the session PlayerEventHub, optionally with the " +
                    "PlayerConfiguration, the IPlayerMotorSurface, and a diagnostic sink.");
                return null;
            }

            public ResetRequestResult RequestReset(string requestId)
            {
                try
                {
                    return (ResetRequestResult)requestReset.Invoke(
                        instance, new object[] { requestId });
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
                object firstRequired,
                out object[] arguments)
            {
                return TryResolve(constructor, candidates, firstRequired, null, out arguments);
            }

            public static bool TryResolve(
                ConstructorInfo constructor,
                IReadOnlyList<object> candidates,
                object firstRequired,
                object secondRequired,
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

                if (!Contains(resolved, firstRequired)) return false;
                if (secondRequired != null && !Contains(resolved, secondRequired)) return false;

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
