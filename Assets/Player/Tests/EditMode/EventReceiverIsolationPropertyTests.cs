using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Property 12. Every generated case replays one identical simulation script four times, once
    /// per Animation_Receiver behaviour: a successful no-op receiver, a missing receiver, a receiver
    /// that rejects commands, and a receiver that throws. The simulation trace must be identical
    /// across all four runs, and inside each run the snapshot captured immediately before receiver
    /// invocation must equal the snapshot observed after the publication completes.
    /// </summary>
    public sealed class EventReceiverIsolationPropertyTests
    {
        private const int Seed = 812207;
        private const int CaseCount = 128;
        private static readonly PlayerConfiguration Configuration = PlayerConfiguration.SafeDefaults;

        private int generatedCaseIndex;

        // **Validates: Requirements 7.14, 8.9**
        [Test]
        [Description("Feature: player-controller, Property 12: Event and animation failures cannot mutate simulation")]
        public void EventAndAnimationFailuresCannotMutateSimulation_Property12()
        {
            generatedCaseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                AssertProperty,
                render: generated => generated.ToString());
        }

        private IsolationCase Generate(Random random)
        {
            var triggers = Enum.GetValues(typeof(TriggerKind));
            var trigger = generatedCaseIndex < triggers.Length
                ? (TriggerKind)generatedCaseIndex
                : (TriggerKind)random.Next(triggers.Length);
            generatedCaseIndex++;

            var warmupCount = random.Next(1, 4);
            var warmup = new float[warmupCount];
            for (var index = 0; index < warmupCount; index++)
                warmup[index] = GeneratedValues.NextFiniteFloat(random, 0f, 0.05f);

            var stepCount = random.Next(1, 9);
            var steps = new ScenarioStep[stepCount];
            for (var index = 0; index < stepCount; index++)
            {
                steps[index] = new ScenarioStep(
                    (StepKind)random.Next(5),
                    GeneratedValues.NextFiniteFloat(random, 0f, 0.05f));
            }

            return new IsolationCase(
                trigger,
                warmup,
                steps,
                new Vector3(
                    GeneratedValues.NextFiniteFloat(random, -3f, 3f),
                    0f,
                    GeneratedValues.NextFiniteFloat(random, -50f, 50f)),
                GeneratedValues.NextFiniteFloat(random, 1f, 20f),
                random.Next(2) == 1,
                "reset-" + generatedCaseIndex.ToString(CultureInfo.InvariantCulture));
        }

        private static void AssertProperty(IsolationCase generated)
        {
            var baseline = RunScenario(generated, ReceiverBehavior.SuccessfulNoOp);
            Assert.That(baseline.ReceiverCallCount, Is.GreaterThan(0),
                "The successful no-op receiver must observe at least one animation command, " +
                "otherwise receiver-failure isolation is not exercised.");
            AssertReceiverInvocationIsolation(baseline);

            AssertIdenticalSimulation(baseline, RunScenario(generated, ReceiverBehavior.Missing));
            AssertIdenticalSimulation(baseline, RunScenario(generated, ReceiverBehavior.Rejecting));
            AssertIdenticalSimulation(baseline, RunScenario(generated, ReceiverBehavior.Throwing));
        }

        private static void AssertReceiverInvocationIsolation(SimulationTrace trace)
        {
            var behavior = trace.Behavior;
            Assert.That(trace.PostPublicationSnapshots.Count, Is.EqualTo(trace.PreReceiverSnapshots.Count),
                "Receiver behaviour " + behavior +
                " must produce one post-publication snapshot per pre-receiver snapshot.");
            for (var index = 0; index < trace.PreReceiverSnapshots.Count; index++)
            {
                SnapshotAssert.Preserved(
                    trace.PreReceiverSnapshots[index],
                    trace.PostPublicationSnapshots[index],
                    context: "Receiver behaviour " + behavior + " must leave publication " + index +
                        " with the committed post-transition snapshot unchanged.");
            }

            SnapshotAssert.Field(
                "State (obstacle publication, Requirement 7.14)",
                trace.StateBeforeObstacleEvent,
                trace.StateAfterObstacleEvent);
            SnapshotAssert.Preserved(
                trace.SnapshotBeforeObstacleEvent,
                trace.SnapshotAfterObstacleEvent,
                context: "Publishing a Player_Hit_Event must preserve the complete simulation snapshot.");
        }

        private static void AssertIdenticalSimulation(SimulationTrace baseline, SimulationTrace variant)
        {
            AssertReceiverInvocationIsolation(variant);

            var label = "Receiver behaviour " + variant.Behavior +
                " must produce the same simulation as " + baseline.Behavior + ". ";

            Assert.That(variant.Snapshots.Count, Is.EqualTo(baseline.Snapshots.Count),
                label + "Simulation step counts differ.");
            for (var index = 0; index < baseline.Snapshots.Count; index++)
            {
                SnapshotAssert.Preserved(
                    baseline.Snapshots[index],
                    variant.Snapshots[index],
                    context: label + "Snapshot " + index + " differs.");
            }

            Assert.That(variant.Commands.Count, Is.EqualTo(baseline.Commands.Count),
                label + "Command counts differ.");
            for (var index = 0; index < baseline.Commands.Count; index++)
            {
                SnapshotAssert.Field(
                    "Command result " + index,
                    baseline.Commands[index],
                    variant.Commands[index]);
            }

            Assert.That(variant.ColliderProfiles.Count, Is.EqualTo(baseline.ColliderProfiles.Count),
                label + "Collider profile application counts differ.");
            for (var index = 0; index < baseline.ColliderProfiles.Count; index++)
            {
                SnapshotAssert.Field(
                    "Applied collider profile " + index,
                    baseline.ColliderProfiles[index],
                    variant.ColliderProfiles[index]);
            }

            var expected = baseline.FinalSnapshot;
            var actual = variant.FinalSnapshot;
            SnapshotAssert.Field("State", expected.State, actual.State);
            SnapshotAssert.Field("IsGrounded", expected.IsGrounded, actual.IsGrounded);
            SnapshotAssert.Field("Position", expected.Position, actual.Position);
            SnapshotAssert.Field("ForwardSpeed", expected.ForwardSpeed, actual.ForwardSpeed);
            SnapshotAssert.Field("VerticalVelocity", expected.VerticalVelocity, actual.VerticalVelocity);
            SnapshotAssert.Field("SlideElapsedTime", expected.SlideElapsedTime, actual.SlideElapsedTime);
            SnapshotAssert.Field("CurrentLane", expected.CurrentLane, actual.CurrentLane);
            SnapshotAssert.Field("TargetLane", expected.TargetLane, actual.TargetLane);
            SnapshotAssert.Field("LateralPosition", expected.LateralPosition, actual.LateralPosition);
            SnapshotAssert.Field("LaneChangeProgress", expected.LaneChangeProgress, actual.LaneChangeProgress);
            SnapshotAssert.Field("LaneRequestQueue", expected.LaneRequestQueue, actual.LaneRequestQueue);
            SnapshotAssert.Field("PendingActionRequests", expected.PendingActionRequests, actual.PendingActionRequests);
            SnapshotAssert.Field("ResetInProgress", expected.ResetInProgress, actual.ResetInProgress);
            SnapshotAssert.Field("MovementUpdateCount", baseline.MovementUpdateCount, variant.MovementUpdateCount);
            SnapshotAssert.Field("MoveInvocationCount", baseline.MoveInvocationCount, variant.MoveInvocationCount);
            SnapshotAssert.Field("LastRequestedDisplacement",
                baseline.LastRequestedDisplacement, variant.LastRequestedDisplacement);
            SnapshotAssert.Field("LastRealizedDisplacement",
                baseline.LastRealizedDisplacement, variant.LastRealizedDisplacement);
            SnapshotAssert.Field("MotorPosition", baseline.MotorPosition, variant.MotorPosition);
        }

        private static SimulationTrace RunScenario(IsolationCase generated, ReceiverBehavior behavior)
        {
            var motor = new DeterministicMotorSurface(generated.InitialPosition, generated.RestorationSafe);
            var diagnostics = new List<ValidationDiagnostic>();
            var hub = new PlayerEventHub();
            var trace = new SimulationTrace(behavior);
            var loop = new PlayerMovementLoop(Configuration, motor, diagnostics.Add);
            var receiver = AnimationReceiverDouble.Create(behavior);

            Action<ValidationDiagnostic> diagnosticSink = diagnostics.Add;
            Action<PlayerStateChangedEvent> preReceiverProbe =
                _ => trace.CapturePreReceiver(loop.Snapshot);

            // The probe subscribes first, so the hub's ordered publication runs it immediately
            // before the animation adapter forwards the command to the receiver.
            hub.ValidationReported += diagnosticSink;
            hub.StateChanged += preReceiverProbe;

            var adapter = PlayerAnimationAdapterSeam.Create(hub, receiver, diagnosticSink);
            try
            {
                Simulate(generated, loop, hub, motor, trace);
                trace.Finish(loop, motor, receiver, diagnostics);
                return trace;
            }
            finally
            {
                adapter.Release();
                hub.StateChanged -= preReceiverProbe;
                hub.ValidationReported -= diagnosticSink;
            }
        }

        private static void Simulate(
            IsolationCase generated,
            PlayerMovementLoop loop,
            PlayerEventHub hub,
            DeterministicMotorSurface motor,
            SimulationTrace trace)
        {
            loop.SetForwardSpeed(generated.ForwardSpeed);
            trace.CaptureSnapshot(loop.Snapshot);

            var observedState = loop.CurrentState;

            // One zero-time update samples grounding before any action command is issued.
            loop.ExecuteMovementUpdate(0f);
            observedState = PublishTransitions(hub, loop, trace, observedState);
            trace.CaptureSnapshot(loop.Snapshot);

            for (var index = 0; index < generated.WarmupTimes.Count; index++)
            {
                loop.ExecuteMovementUpdate(generated.WarmupTimes[index]);
                observedState = PublishTransitions(hub, loop, trace, observedState);
                trace.CaptureSnapshot(loop.Snapshot);
            }

            trace.CaptureCommand(ExecuteTrigger(generated, loop));
            observedState = PublishTransitions(hub, loop, trace, observedState);
            trace.CaptureSnapshot(loop.Snapshot);

            trace.CaptureObstacleEventStart(loop.CurrentState, loop.Snapshot);
            hub.PublishPlayerHit(1UL, "obstacle-generated", null, motor.Position);
            trace.CaptureObstacleEventEnd(loop.CurrentState, loop.Snapshot);

            for (var index = 0; index < generated.Steps.Count; index++)
            {
                var step = generated.Steps[index];
                switch (step.Kind)
                {
                    case StepKind.Advance:
                        loop.ExecuteMovementUpdate(step.ElapsedTime);
                        break;
                    case StepKind.LaneLeft:
                        trace.CaptureCommand(CommandObservation.From(loop.RequestLane(LaneDirection.Left)));
                        break;
                    case StepKind.LaneRight:
                        trace.CaptureCommand(CommandObservation.From(loop.RequestLane(LaneDirection.Right)));
                        break;
                    case StepKind.Jump:
                        trace.CaptureCommand(CommandObservation.From(loop.RequestJump()));
                        break;
                    default:
                        trace.CaptureCommand(CommandObservation.From(loop.RequestSlide()));
                        break;
                }

                observedState = PublishTransitions(hub, loop, trace, observedState);
                trace.CaptureSnapshot(loop.Snapshot);
            }
        }

        private static CommandObservation ExecuteTrigger(IsolationCase generated, PlayerMovementLoop loop)
        {
            switch (generated.Trigger)
            {
                case TriggerKind.Jump:
                    return CommandObservation.From(loop.RequestJump());
                case TriggerKind.Slide:
                    return CommandObservation.From(loop.RequestSlide());
                case TriggerKind.Failure:
                    return CommandObservation.From(loop.RequestFailure());
                default:
                    return CommandObservation.From(loop.RequestReset(generated.ResetRequestId));
            }
        }

        private static PlayerState PublishTransitions(
            PlayerEventHub hub,
            PlayerMovementLoop loop,
            SimulationTrace trace,
            PlayerState observedState)
        {
            var current = loop.CurrentState;
            if (current == observedState) return observedState;

            hub.PublishStateChanged(observedState, current, CauseFor(observedState, current));
            trace.CapturePostPublication(loop.Snapshot);
            return current;
        }

        private static PlayerTransitionCause CauseFor(PlayerState previous, PlayerState current)
        {
            switch (current)
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
                    return previous == PlayerState.Sliding
                        ? PlayerTransitionCause.SlideRestored
                        : PlayerTransitionCause.Landed;
            }
        }

        private enum TriggerKind
        {
            Jump,
            Slide,
            Failure,
            Reset
        }

        private enum StepKind
        {
            Advance,
            LaneLeft,
            LaneRight,
            Jump,
            Slide
        }

        private enum ReceiverBehavior
        {
            SuccessfulNoOp,
            Missing,
            Rejecting,
            Throwing
        }

        private readonly struct ScenarioStep
        {
            public ScenarioStep(StepKind kind, float elapsedTime)
            {
                Kind = kind;
                ElapsedTime = elapsedTime;
            }

            public StepKind Kind { get; }
            public float ElapsedTime { get; }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}@{1:R}", Kind, ElapsedTime);
            }
        }

        private readonly struct IsolationCase
        {
            public IsolationCase(
                TriggerKind trigger,
                IReadOnlyList<float> warmupTimes,
                IReadOnlyList<ScenarioStep> steps,
                Vector3 initialPosition,
                float forwardSpeed,
                bool restorationSafe,
                string resetRequestId)
            {
                Trigger = trigger;
                WarmupTimes = warmupTimes;
                Steps = steps;
                InitialPosition = initialPosition;
                ForwardSpeed = forwardSpeed;
                RestorationSafe = restorationSafe;
                ResetRequestId = resetRequestId;
            }

            public TriggerKind Trigger { get; }
            public IReadOnlyList<float> WarmupTimes { get; }
            public IReadOnlyList<ScenarioStep> Steps { get; }
            public Vector3 InitialPosition { get; }
            public float ForwardSpeed { get; }
            public bool RestorationSafe { get; }
            public string ResetRequestId { get; }

            public override string ToString()
            {
                var warmup = new List<string>(WarmupTimes.Count);
                for (var index = 0; index < WarmupTimes.Count; index++)
                    warmup.Add(WarmupTimes[index].ToString("R", CultureInfo.InvariantCulture));

                var steps = new List<string>(Steps.Count);
                for (var index = 0; index < Steps.Count; index++) steps.Add(Steps[index].ToString());

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Trigger={0}, Position=({1:R},{2:R},{3:R}), ForwardSpeed={4:R}, " +
                    "RestorationSafe={5}, ResetRequestId={6}, Warmup=[{7}], Steps=[{8}]",
                    Trigger,
                    InitialPosition.x,
                    InitialPosition.y,
                    InitialPosition.z,
                    ForwardSpeed,
                    RestorationSafe,
                    ResetRequestId,
                    string.Join(",", warmup),
                    string.Join(",", steps));
            }
        }

        private readonly struct CommandObservation : IEquatable<CommandObservation>
        {
            private CommandObservation(
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

            public bool Equals(CommandObservation other)
            {
                return Status == other.Status && Command == other.Command &&
                    Reason == other.Reason && CurrentState == other.CurrentState;
            }

            public override bool Equals(object obj)
            {
                return obj is CommandObservation other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (((int)Status * 397 ^ (int)Command) * 397 ^ (int)Reason) * 397 ^ (int)CurrentState;
                }
            }

            public override string ToString()
            {
                return Status + "/" + Command + "/" + Reason + "/" + CurrentState;
            }
        }

        private sealed class SimulationTrace
        {
            private readonly List<PlayerSnapshot> snapshots = new List<PlayerSnapshot>();
            private readonly List<PlayerSnapshot> preReceiverSnapshots = new List<PlayerSnapshot>();
            private readonly List<PlayerSnapshot> postPublicationSnapshots = new List<PlayerSnapshot>();
            private readonly List<CommandObservation> commands = new List<CommandObservation>();

            public SimulationTrace(ReceiverBehavior behavior)
            {
                Behavior = behavior;
            }

            public ReceiverBehavior Behavior { get; }
            public IReadOnlyList<PlayerSnapshot> Snapshots { get { return snapshots; } }
            public IReadOnlyList<PlayerSnapshot> PreReceiverSnapshots { get { return preReceiverSnapshots; } }
            public IReadOnlyList<PlayerSnapshot> PostPublicationSnapshots { get { return postPublicationSnapshots; } }
            public IReadOnlyList<CommandObservation> Commands { get { return commands; } }
            public IReadOnlyList<ColliderProfile> ColliderProfiles { get; private set; }
            public PlayerState StateBeforeObstacleEvent { get; private set; }
            public PlayerState StateAfterObstacleEvent { get; private set; }
            public PlayerSnapshot SnapshotBeforeObstacleEvent { get; private set; }
            public PlayerSnapshot SnapshotAfterObstacleEvent { get; private set; }
            public PlayerSnapshot FinalSnapshot { get; private set; }
            public Vector3 MotorPosition { get; private set; }
            public Vector3 LastRequestedDisplacement { get; private set; }
            public Vector3 LastRealizedDisplacement { get; private set; }
            public int MovementUpdateCount { get; private set; }
            public int MoveInvocationCount { get; private set; }
            public int ReceiverCallCount { get; private set; }
            public int DiagnosticCount { get; private set; }

            public void CaptureSnapshot(PlayerSnapshot snapshot) { snapshots.Add(snapshot); }
            public void CapturePreReceiver(PlayerSnapshot snapshot) { preReceiverSnapshots.Add(snapshot); }
            public void CapturePostPublication(PlayerSnapshot snapshot) { postPublicationSnapshots.Add(snapshot); }
            public void CaptureCommand(CommandObservation observation) { commands.Add(observation); }

            public void CaptureObstacleEventStart(PlayerState state, PlayerSnapshot snapshot)
            {
                StateBeforeObstacleEvent = state;
                SnapshotBeforeObstacleEvent = snapshot;
            }

            public void CaptureObstacleEventEnd(PlayerState state, PlayerSnapshot snapshot)
            {
                StateAfterObstacleEvent = state;
                SnapshotAfterObstacleEvent = snapshot;
            }

            public void Finish(
                PlayerMovementLoop loop,
                DeterministicMotorSurface motor,
                AnimationReceiverDouble receiver,
                IReadOnlyList<ValidationDiagnostic> diagnostics)
            {
                FinalSnapshot = loop.Snapshot;
                MovementUpdateCount = loop.MovementUpdateCount;
                MoveInvocationCount = loop.MoveInvocationCount;
                LastRequestedDisplacement = loop.LastRequestedDisplacement;
                LastRealizedDisplacement = loop.LastRealizedDisplacement;
                MotorPosition = motor.Position;
                ColliderProfiles = motor.AppliedProfiles;
                ReceiverCallCount = receiver == null ? 0 : receiver.CallCount;
                DiagnosticCount = diagnostics.Count;
            }
        }

        /// <summary>
        /// Deterministic stand-in for the Unity motor: displacement integration with a flat ground
        /// plane at y = 0. No scene objects are created, so every case is disposed by dropping the
        /// fixture references at the end of the scenario run.
        /// </summary>
        private sealed class DeterministicMotorSurface : IPlayerMotorSurface
        {
            private readonly List<ColliderProfile> appliedProfiles = new List<ColliderProfile>();
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
            public IReadOnlyList<ColliderProfile> AppliedProfiles { get { return appliedProfiles; } }

            public void ApplyColliderProfile(ColliderProfile profile) { appliedProfiles.Add(profile); }

            public bool SampleGrounded(
                ColliderProfile profile,
                int groundLayerMask,
                float contactTolerance,
                float normalThreshold)
            {
                return position.y <= contactTolerance;
            }

            public bool IsBaselineRestorationSafe(ColliderProfile baselineProfile, int obstructionLayerMask)
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

        private sealed class AnimationReceiverDouble : IAnimationReceiver
        {
            private readonly ReceiverBehavior behavior;

            private AnimationReceiverDouble(ReceiverBehavior behavior)
            {
                this.behavior = behavior;
            }

            public int CallCount { get; private set; }

            public static AnimationReceiverDouble Create(ReceiverBehavior behavior)
            {
                return behavior == ReceiverBehavior.Missing
                    ? null
                    : new AnimationReceiverDouble(behavior);
            }

            public bool TryApply(AnimationCommand command)
            {
                CallCount++;
                if (behavior == ReceiverBehavior.Throwing)
                {
                    throw new InvalidOperationException(
                        "Generated animation receiver failure for command '" + command.Name + "'.");
                }

                return behavior != ReceiverBehavior.Rejecting;
            }
        }

        /// <summary>
        /// Red-first reflection seam for the Task 8.5 animation adapter. The seam resolves the
        /// adapter's constructor arguments by parameter type so Task 8.5 stays free to choose the
        /// exact signature: the event source it subscribes to, the replaceable
        /// <see cref="IAnimationReceiver"/>, and the diagnostic sink.
        /// </summary>
        private sealed class PlayerAnimationAdapterSeam
        {
            private const string RuntimeTypeName = "SubwaySurfers.Player.PlayerAnimationAdapter";

            private readonly object instance;

            private PlayerAnimationAdapterSeam(object instance)
            {
                this.instance = instance;
            }

            public static PlayerAnimationAdapterSeam Create(
                PlayerEventHub hub,
                IAnimationReceiver receiver,
                Action<ValidationDiagnostic> diagnosticSink)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 8.5 must provide " + RuntimeTypeName + ".");

                var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
                foreach (var constructor in constructors)
                {
                    object[] arguments;
                    if (!TryResolveArguments(constructor, hub, receiver, diagnosticSink, out arguments))
                        continue;

                    return new PlayerAnimationAdapterSeam(constructor.Invoke(arguments));
                }

                Assert.Fail(
                    RuntimeTypeName + " must expose a public constructor accepting the player event " +
                    "source, the replaceable IAnimationReceiver, and a diagnostic sink.");
                return null;
            }

            public void Release()
            {
                var disposable = instance as IDisposable;
                if (disposable != null) disposable.Dispose();
            }

            private static bool TryResolveArguments(
                ConstructorInfo constructor,
                PlayerEventHub hub,
                IAnimationReceiver receiver,
                Action<ValidationDiagnostic> diagnosticSink,
                out object[] arguments)
            {
                var parameters = constructor.GetParameters();
                arguments = new object[parameters.Length];
                for (var index = 0; index < parameters.Length; index++)
                {
                    var parameterType = parameters[index].ParameterType;
                    if (parameterType.IsInstanceOfType(hub))
                    {
                        arguments[index] = hub;
                        continue;
                    }

                    if (parameterType == typeof(IAnimationReceiver))
                    {
                        arguments[index] = receiver;
                        continue;
                    }

                    if (parameterType == typeof(Action<ValidationDiagnostic>))
                    {
                        arguments[index] = diagnosticSink;
                        continue;
                    }

                    arguments = null;
                    return false;
                }

                return true;
            }
        }
    }
}
