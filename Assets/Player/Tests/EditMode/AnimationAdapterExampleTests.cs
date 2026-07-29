using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Example coverage for the player animation layer: the five state-to-command mappings, the
    /// exactly-one-call-per-publication timing rule, receiver replacement as an atomic reference
    /// swap, and the categorized receiver-failure diagnostics.
    ///
    /// Every fixture is built programmatically and released in <see cref="ReleaseFixtures"/>; no
    /// scene objects, assets, or shared static state are involved. The adapter is reached through
    /// the same red-first reflection seam introduced with Property 12, so Task 8.5 keeps freedom
    /// over exact member names while these tests pin the observable behaviour.
    /// </summary>
    public sealed class AnimationAdapterExampleTests
    {
        private static readonly PlayerConfiguration Configuration = PlayerConfiguration.SafeDefaults;

        private static readonly PlayerState[] MappedStates =
        {
            PlayerState.Running,
            PlayerState.Jumping,
            PlayerState.Sliding,
            PlayerState.Failed,
            PlayerState.Resetting
        };

        private readonly List<AdapterFixture> fixtures = new List<AdapterFixture>();

        [TearDown]
        public void ReleaseFixtures()
        {
            for (var index = 0; index < fixtures.Count; index++) fixtures[index].Dispose();
            fixtures.Clear();
        }

        // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 12.9**
        [Test]
        public void EveryPlayerStateMapsToItsOwnConfiguredAnimationCommand()
        {
            var fixture = NewFixture(ReceiverBehavior.Accepting);
            var seenCommands = new Dictionary<string, PlayerState>(StringComparer.Ordinal);

            for (var index = 0; index < MappedStates.Length; index++)
            {
                var state = MappedStates[index];
                var configured = fixture.Adapter.ResolveCommand(state);
                Assert.That(string.IsNullOrEmpty(configured.Name), Is.False,
                    "The configured animation command for " + state + " must have a name.");
                Assert.That(seenCommands.ContainsKey(configured.Name), Is.False,
                    "The animation command '" + configured.Name + "' configured for " + state +
                    " is already mapped to " +
                    (seenCommands.ContainsKey(configured.Name)
                        ? seenCommands[configured.Name].ToString()
                        : string.Empty) +
                    "; each Player_State needs its own configured command.");
                seenCommands.Add(configured.Name, state);

                var before = fixture.Receiver.Commands.Count;
                fixture.PublishTransition(state);

                Assert.That(fixture.Receiver.Commands.Count, Is.EqualTo(before + 1),
                    "Publishing a transition into " + state +
                    " must issue exactly one animation command.");
                Assert.That(fixture.Receiver.Commands[before], Is.EqualTo(configured),
                    "The command issued for " + state + " must be the configured mapping.");
            }

            Assert.That(fixture.Receiver.Commands.Count, Is.EqualTo(MappedStates.Length));
            Assert.That(fixture.ObservedDiagnostics().Count, Is.Zero,
                "A successful receiver must not produce animation diagnostics.");
        }

        // **Validates: Requirements 8.6, 12.9**
        [Test]
        public void OnePublishedTransitionIssuesExactlyOneReceiverCallInThePublicationFrame()
        {
            var fixture = NewFixture(ReceiverBehavior.Accepting);

            fixture.Clock.Frame = 1;
            fixture.PublishTransition(PlayerState.Jumping);
            Assert.That(fixture.Receiver.CallsInFrame(1), Is.EqualTo(1),
                "The transition published in frame 1 must produce exactly one receiver call in frame 1.");

            fixture.Clock.Frame = 2;
            Assert.That(fixture.Receiver.CallsInFrame(2), Is.Zero,
                "A frame without a published transition must produce no animation command.");

            fixture.Clock.Frame = 3;
            fixture.PublishTransition(PlayerState.Running);
            Assert.That(fixture.Receiver.CallsInFrame(3), Is.EqualTo(1),
                "The transition published in frame 3 must produce exactly one receiver call in frame 3.");
            Assert.That(fixture.Receiver.CallsInFrame(1), Is.EqualTo(1),
                "An earlier frame's call count must not change when a later transition publishes.");

            Assert.That(fixture.Receiver.Commands.Count, Is.EqualTo(2),
                "Two published transitions must produce exactly two animation commands.");
            Assert.That(fixture.Receiver.Commands[0],
                Is.EqualTo(fixture.Adapter.ResolveCommand(PlayerState.Jumping)));
            Assert.That(fixture.Receiver.Commands[1],
                Is.EqualTo(fixture.Adapter.ResolveCommand(PlayerState.Running)));
        }

        // **Validates: Requirements 8.7, 12.9**
        [Test]
        public void ReceiverReplacementSwapsTheReferenceWithoutReplayOrStateMutation()
        {
            var fixture = NewFixture(ReceiverBehavior.Accepting);
            var original = fixture.Receiver;
            fixture.PublishTransition(PlayerState.Jumping);
            Assert.That(original.Commands.Count, Is.EqualTo(1));

            var snapshotBeforeSwap = fixture.Loop.Snapshot;
            var stateBeforeSwap = fixture.Loop.CurrentState;
            var diagnosticsBeforeSwap = fixture.ObservedDiagnostics().Count;

            var replacement = new RecordingReceiver(ReceiverBehavior.Accepting, fixture.Clock);
            fixture.Adapter.ReplaceReceiver(replacement);

            Assert.That(replacement.Commands.Count, Is.Zero,
                "Receiver replacement must not replay previously issued animation commands.");
            Assert.That(original.Commands.Count, Is.EqualTo(1),
                "Receiver replacement must not re-issue commands to the previous receiver.");
            Assert.That(fixture.ObservedDiagnostics().Count, Is.EqualTo(diagnosticsBeforeSwap),
                "Receiver replacement alone must not produce a diagnostic.");
            SnapshotAssert.Field("State", stateBeforeSwap, fixture.Loop.CurrentState);
            SnapshotAssert.Preserved(snapshotBeforeSwap, fixture.Loop.Snapshot,
                context: "Receiver replacement must not change Player_Controller state.");

            fixture.PublishTransition(PlayerState.Running);

            Assert.That(replacement.Commands.Count, Is.EqualTo(1),
                "Subsequent animation commands must route to the replacement receiver.");
            Assert.That(replacement.Commands[0],
                Is.EqualTo(fixture.Adapter.ResolveCommand(PlayerState.Running)));
            Assert.That(original.Commands.Count, Is.EqualTo(1),
                "The replaced receiver must receive no further animation commands.");

            var second = new RecordingReceiver(ReceiverBehavior.Accepting, fixture.Clock);
            fixture.Adapter.ReplaceReceiver(second);
            fixture.PublishTransition(PlayerState.Sliding);

            Assert.That(second.Commands.Count, Is.EqualTo(1),
                "Only the most recently configured receiver may receive animation commands.");
            Assert.That(replacement.Commands.Count, Is.EqualTo(1),
                "A superseded receiver must receive no further animation commands.");
            Assert.That(original.Commands.Count, Is.EqualTo(1),
                "A superseded receiver must receive no further animation commands.");
            SnapshotAssert.Preserved(snapshotBeforeSwap, fixture.Loop.Snapshot,
                context: "Repeated receiver replacement must not change Player_Controller state.");
        }

        // **Validates: Requirements 8.8, 8.9, 12.9**
        [Test]
        public void AbsentReceiverProducesOneAbsentDiagnosticPerAttemptedCommand()
        {
            AssertCategorizedDiagnostics(
                ReceiverBehavior.Absent,
                DiagnosticCode.AnimationReceiverAbsent);
        }

        // **Validates: Requirements 8.8, 8.9, 12.9**
        [Test]
        public void UnmappedPlayerStateProducesOneUnavailableDiagnosticPerAttemptedCommand()
        {
            var fixture = NewFixture(ReceiverBehavior.Accepting);
            var unmappedState = UnmappedState();
            var snapshotBefore = fixture.Loop.Snapshot;
            var stateBefore = fixture.Loop.CurrentState;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                fixture.Clock.Frame = attempt + 1;
                fixture.PublishTransition(unmappedState);
            }

            var diagnostics = fixture.ObservedDiagnostics();
            Assert.That(diagnostics.Count, Is.EqualTo(3),
                "An unavailable animation mapping must produce exactly one diagnostic per attempted command.");
            for (var index = 0; index < diagnostics.Count; index++)
            {
                AssertCategorized(
                    diagnostics[index],
                    DiagnosticCode.AnimationReceiverUnavailable,
                    unmappedState,
                    receiverTypeName: null);
            }

            Assert.That(fixture.Receiver.Commands.Count, Is.Zero,
                "No animation command exists for an unmapped Player_State, so none may be issued.");
            SnapshotAssert.Field("State", stateBefore, fixture.Loop.CurrentState);
            SnapshotAssert.Preserved(snapshotBefore, fixture.Loop.Snapshot,
                context: "An unavailable animation mapping must preserve the simulation snapshot.");
        }

        // **Validates: Requirements 8.8, 8.9, 12.9**
        [Test]
        public void RejectingReceiverProducesOneRejectionDiagnosticPerAttemptedCommand()
        {
            AssertCategorizedDiagnostics(
                ReceiverBehavior.Rejecting,
                DiagnosticCode.AnimationReceiverRejected);
        }

        // **Validates: Requirements 8.8, 8.9, 12.9**
        [Test]
        public void ThrowingReceiverProducesOneExceptionDiagnosticPerAttemptedCommand()
        {
            AssertCategorizedDiagnostics(
                ReceiverBehavior.Throwing,
                DiagnosticCode.AnimationReceiverException);
        }

        private void AssertCategorizedDiagnostics(ReceiverBehavior behavior, DiagnosticCode expectedCode)
        {
            var fixture = NewFixture(behavior);
            var snapshotBefore = fixture.Loop.Snapshot;
            var stateBefore = fixture.Loop.CurrentState;
            var attempted = new[] { PlayerState.Jumping, PlayerState.Running, PlayerState.Failed };

            for (var index = 0; index < attempted.Length; index++)
            {
                fixture.Clock.Frame = index + 1;
                fixture.PublishTransition(attempted[index]);
            }

            var diagnostics = fixture.ObservedDiagnostics();
            Assert.That(diagnostics.Count, Is.EqualTo(attempted.Length),
                "Receiver behaviour " + behavior +
                " must produce exactly one diagnostic per attempted animation command.");
            for (var index = 0; index < attempted.Length; index++)
            {
                AssertCategorized(
                    diagnostics[index],
                    expectedCode,
                    attempted[index],
                    fixture.Receiver == null ? null : fixture.Receiver.GetType().Name);
            }

            if (fixture.Receiver != null)
            {
                Assert.That(fixture.Receiver.Commands.Count, Is.EqualTo(attempted.Length),
                    "Each attempted command must reach the configured receiver exactly once.");
            }

            SnapshotAssert.Field("State", stateBefore, fixture.Loop.CurrentState);
            SnapshotAssert.Preserved(snapshotBefore, fixture.Loop.Snapshot,
                context: "Receiver behaviour " + behavior + " must preserve the simulation snapshot.");
        }

        private static void AssertCategorized(
            ValidationDiagnostic diagnostic,
            DiagnosticCode expectedCode,
            PlayerState state,
            string receiverTypeName)
        {
            Assert.That(diagnostic.Code, Is.EqualTo(expectedCode),
                "The diagnostic must carry the failure category. Diagnostic: " + Describe(diagnostic));
            Assert.That(
                diagnostic.Severity == DiagnosticSeverity.Warning ||
                diagnostic.Severity == DiagnosticSeverity.Error,
                Is.True,
                "An animation receiver failure must be reported as a warning or an error. Diagnostic: " +
                Describe(diagnostic));

            var text = (diagnostic.Field ?? string.Empty) + " " + (diagnostic.Message ?? string.Empty);
            AssertMentions(text, "receiver", diagnostic);
            AssertMentions(text, StateName(state), diagnostic);
            if (!string.IsNullOrEmpty(receiverTypeName)) AssertMentions(text, receiverTypeName, diagnostic);
        }

        private static void AssertMentions(string text, string expected, ValidationDiagnostic diagnostic)
        {
            Assert.That(
                text.IndexOf(expected, StringComparison.OrdinalIgnoreCase),
                Is.GreaterThanOrEqualTo(0),
                "The diagnostic must identify '" + expected + "'. Diagnostic: " + Describe(diagnostic));
        }

        private static string Describe(ValidationDiagnostic diagnostic)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2}/{3}",
                diagnostic.Severity,
                diagnostic.Code,
                diagnostic.Field,
                diagnostic.Message);
        }

        private static string StateName(PlayerState state)
        {
            return Enum.IsDefined(typeof(PlayerState), state)
                ? state.ToString()
                : ((int)state).ToString(CultureInfo.InvariantCulture);
        }

        private static PlayerState UnmappedState()
        {
            var candidate = 1;
            while (Enum.IsDefined(typeof(PlayerState), (PlayerState)candidate)) candidate++;
            return (PlayerState)candidate;
        }

        private AdapterFixture NewFixture(ReceiverBehavior behavior)
        {
            var fixture = AdapterFixture.Create(behavior);
            fixtures.Add(fixture);
            return fixture;
        }

        private enum ReceiverBehavior
        {
            Accepting,
            Absent,
            Rejecting,
            Throwing
        }

        private sealed class FrameClock
        {
            public int Frame { get; set; }
        }

        /// <summary>
        /// Owns one adapter, one event hub, one movement loop, and one diagnostic sink. Diagnostics
        /// are observed through the injected sink and through the hub's validation event so the
        /// adapter may report through either channel; both channels must agree.
        /// </summary>
        private sealed class AdapterFixture : IDisposable
        {
            private readonly List<ValidationDiagnostic> sinkDiagnostics = new List<ValidationDiagnostic>();
            private readonly List<ValidationDiagnostic> hubDiagnostics = new List<ValidationDiagnostic>();
            private readonly Action<ValidationDiagnostic> hubHandler;
            private PlayerState previousState;

            private AdapterFixture(
                PlayerEventHub hub,
                PlayerMovementLoop loop,
                RecordingReceiver receiver,
                FrameClock clock)
            {
                Hub = hub;
                Loop = loop;
                Receiver = receiver;
                Clock = clock;
                previousState = loop.CurrentState;
                hubHandler = hubDiagnostics.Add;
                hub.ValidationReported += hubHandler;
            }

            public PlayerEventHub Hub { get; }
            public PlayerMovementLoop Loop { get; }
            public RecordingReceiver Receiver { get; }
            public FrameClock Clock { get; }
            public AnimationAdapterSeam Adapter { get; private set; }

            public static AdapterFixture Create(ReceiverBehavior behavior)
            {
                var hub = new PlayerEventHub();
                var motor = new FlatMotorSurface();
                var loop = new PlayerMovementLoop(Configuration, motor, _ => { });
                var clock = new FrameClock { Frame = 1 };
                var receiver = behavior == ReceiverBehavior.Absent
                    ? null
                    : new RecordingReceiver(behavior, clock);
                var fixture = new AdapterFixture(hub, loop, receiver, clock);

                // One zero-time update settles grounding so later snapshots are stable.
                loop.ExecuteMovementUpdate(0f);
                fixture.previousState = loop.CurrentState;
                fixture.Adapter = AnimationAdapterSeam.Create(hub, receiver, fixture.sinkDiagnostics.Add);
                return fixture;
            }

            public void PublishTransition(PlayerState currentState)
            {
                Hub.PublishStateChanged(previousState, currentState, CauseFor(previousState, currentState));
                previousState = currentState;
            }

            public IReadOnlyList<ValidationDiagnostic> ObservedDiagnostics()
            {
                if (sinkDiagnostics.Count > 0 && hubDiagnostics.Count > 0)
                {
                    Assert.That(hubDiagnostics.Count, Is.EqualTo(sinkDiagnostics.Count),
                        "The animation adapter must report the same diagnostics to the injected sink " +
                        "and to the event source.");
                    for (var index = 0; index < sinkDiagnostics.Count; index++)
                    {
                        Assert.That(hubDiagnostics[index], Is.EqualTo(sinkDiagnostics[index]),
                            "Diagnostic " + index + " differs between the injected sink and the event source.");
                    }
                }

                return sinkDiagnostics.Count > 0 ? sinkDiagnostics : hubDiagnostics;
            }

            public void Dispose()
            {
                if (Adapter != null) Adapter.Release();
                Adapter = null;
                Hub.ValidationReported -= hubHandler;
                sinkDiagnostics.Clear();
                hubDiagnostics.Clear();
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
                    case PlayerState.Running:
                        return previous == PlayerState.Sliding
                            ? PlayerTransitionCause.SlideRestored
                            : PlayerTransitionCause.Landed;
                    default:
                        return PlayerTransitionCause.Landed;
                }
            }
        }

        private sealed class RecordingReceiver : IAnimationReceiver
        {
            private readonly List<AnimationCommand> commands = new List<AnimationCommand>();
            private readonly List<int> frames = new List<int>();
            private readonly ReceiverBehavior behavior;

            public RecordingReceiver(ReceiverBehavior behavior, FrameClock clock)
            {
                this.behavior = behavior;
                Clock = clock;
            }

            public FrameClock Clock { get; }
            public IReadOnlyList<AnimationCommand> Commands { get { return commands; } }

            public int CallsInFrame(int frame)
            {
                var count = 0;
                for (var index = 0; index < frames.Count; index++)
                {
                    if (frames[index] == frame) count++;
                }

                return count;
            }

            public bool TryApply(AnimationCommand command)
            {
                commands.Add(command);
                frames.Add(Clock == null ? 0 : Clock.Frame);
                if (behavior == ReceiverBehavior.Throwing)
                {
                    throw new InvalidOperationException(
                        "The configured animation receiver is unavailable for command '" +
                        command.Name + "'.");
                }

                return behavior != ReceiverBehavior.Rejecting;
            }
        }

        /// <summary>
        /// Minimal deterministic motor: flat ground at y = 0 and displacement integration. No scene
        /// objects, so the fixture is disposed by dropping references.
        /// </summary>
        private sealed class FlatMotorSurface : IPlayerMotorSurface
        {
            private Vector3 position;

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

            public bool IsBaselineRestorationSafe(ColliderProfile baselineProfile, int obstructionLayerMask)
            {
                return true;
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
        /// Red-first reflection seam for the Task 8.5 animation adapter, extending the convention
        /// established for Property 12. Constructor arguments are resolved by parameter type, and
        /// the mapping lookup plus receiver replacement members are resolved by shape so Task 8.5
        /// keeps naming freedom.
        /// </summary>
        private sealed class AnimationAdapterSeam
        {
            private const string RuntimeTypeName = "SubwaySurfers.Player.PlayerAnimationAdapter";

            private readonly object instance;

            private AnimationAdapterSeam(object instance)
            {
                this.instance = instance;
            }

            public static AnimationAdapterSeam Create(
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

                    return new AnimationAdapterSeam(constructor.Invoke(arguments));
                }

                Assert.Fail(
                    RuntimeTypeName + " must expose a public constructor accepting the player event " +
                    "source, the replaceable IAnimationReceiver, and a diagnostic sink.");
                return null;
            }

            public AnimationCommand ResolveCommand(PlayerState state)
            {
                var methods = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (method.ReturnType == typeof(AnimationCommand) &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(PlayerState))
                    {
                        return (AnimationCommand)method.Invoke(instance, new object[] { state });
                    }

                    if (method.ReturnType == typeof(bool) &&
                        parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(PlayerState) &&
                        parameters[1].IsOut &&
                        parameters[1].ParameterType == typeof(AnimationCommand).MakeByRefType())
                    {
                        var arguments = new object[] { state, null };
                        var resolved = (bool)method.Invoke(instance, arguments);
                        Assert.That(resolved, Is.True,
                            RuntimeTypeName + " must configure an animation command for " + state + ".");
                        return (AnimationCommand)arguments[1];
                    }
                }

                Assert.Fail(
                    RuntimeTypeName + " must expose a public mapping lookup, either " +
                    "'AnimationCommand ResolveCommand(PlayerState)' or " +
                    "'bool TryResolveCommand(PlayerState, out AnimationCommand)'.");
                return default(AnimationCommand);
            }

            public void ReplaceReceiver(IAnimationReceiver receiver)
            {
                var methods = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 ||
                        parameters[0].ParameterType != typeof(IAnimationReceiver))
                    {
                        continue;
                    }

                    method.Invoke(instance, new object[] { receiver });
                    return;
                }

                Assert.Fail(
                    RuntimeTypeName + " must expose a public receiver replacement member, either " +
                    "'void ReplaceReceiver(IAnimationReceiver)' or a settable " +
                    "'IAnimationReceiver Receiver' property.");
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
