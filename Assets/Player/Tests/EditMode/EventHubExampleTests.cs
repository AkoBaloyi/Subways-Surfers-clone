using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class EventHubExampleTests
    {
        // **Validates: Requirements 7.2, 7.5, 7.7, 7.8, 12.8, 14.3**
        [Test]
        public void AcceptedTransitionHitAndCoinPublishCompleteOrderedPayloads()
        {
            var hub = new PlayerEventHub();
            var machine = CreateMachine(PlayerState.Running, true);
            RelayTransitions(machine, hub);
            var obstacle = new TestEnvironmentObject(
                "obstacle-complete", EnvironmentObjectKind.Obstacle, 0f);
            var coin = new TestEnvironmentObject(
                "coin-complete", EnvironmentObjectKind.Coin, 37.5f);
            var hitPosition = new Vector3(4.25f, 1.5f, -9.75f);
            PlayerStateChangedEvent? stateEvent = null;
            PlayerHitEvent? hitEvent = null;
            CoinCollectedEvent? coinEvent = null;
            hub.StateChanged += value => stateEvent = value;
            hub.PlayerHit += value => hitEvent = value;
            hub.CoinCollected += value => coinEvent = value;

            var transitionResult = machine.RequestJump();
            hub.PublishPlayerHit(81UL, obstacle.EnvironmentObjectId, obstacle, hitPosition);
            hub.PublishCoinCollected(82UL, coin.EnvironmentObjectId, coin, coin.CollectibleValue);

            Assert.That(transitionResult.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(stateEvent.HasValue, Is.True);
            Assert.That(stateEvent.Value.EventId, Is.EqualTo(1UL));
            Assert.That(stateEvent.Value.PreviousState, Is.EqualTo(PlayerState.Running));
            Assert.That(stateEvent.Value.CurrentState, Is.EqualTo(PlayerState.Jumping));
            Assert.That(stateEvent.Value.TransitionCause, Is.EqualTo(PlayerTransitionCause.JumpRequested));
            Assert.That(hitEvent.HasValue, Is.True);
            Assert.That(hitEvent.Value.EventId, Is.EqualTo(2UL));
            Assert.That(hitEvent.Value.ContactId, Is.EqualTo(81UL));
            Assert.That(hitEvent.Value.EnvironmentObjectId, Is.EqualTo("obstacle-complete"));
            Assert.That(hitEvent.Value.Obstacle, Is.SameAs(obstacle));
            Assert.That(hitEvent.Value.ContactPosition, Is.EqualTo(hitPosition));
            Assert.That(coinEvent.HasValue, Is.True);
            Assert.That(coinEvent.Value.EventId, Is.EqualTo(3UL));
            Assert.That(coinEvent.Value.ContactId, Is.EqualTo(82UL));
            Assert.That(coinEvent.Value.EnvironmentObjectId, Is.EqualTo("coin-complete"));
            Assert.That(coinEvent.Value.Coin, Is.SameAs(coin));
            Assert.That(coinEvent.Value.CollectibleValue, Is.EqualTo(37.5f));
            Assert.That(hub.LastEventId, Is.EqualTo(3UL));
        }
        // **Validates: Requirements 7.9, 12.8**
        [Test]
        public void RejectedTransitionPublishesNoStateEvent()
        {
            var hub = new PlayerEventHub();
            var machine = CreateMachine(PlayerState.Jumping, false);
            RelayTransitions(machine, hub);
            var publicationCount = 0;
            hub.StateChanged += _ => publicationCount++;

            var result = machine.RequestSlide();

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.InvalidState));
            Assert.That(machine.CurrentState, Is.EqualTo(PlayerState.Jumping));
            Assert.That(publicationCount, Is.Zero);
            Assert.That(hub.LastEventId, Is.Zero);
        }

        // **Validates: Requirements 7.2, 7.13, 7.14, 12.8**
        [Test]
        public void HitPublicationDoesNotImplicitlyFailPlayerOrMutateExternalState()
        {
            var hub = new PlayerEventHub();
            var machine = CreateMachine(PlayerState.Running, true);
            RelayTransitions(machine, hub);
            var tracker = new EnvironmentContactTracker(hub);
            var obstacle = new TestEnvironmentObject(
                "obstacle-no-failure", EnvironmentObjectKind.Obstacle, 0f);
            var before = machine.Snapshot;
            var globalState = new ExternalState("running", 125, "hud-visible");
            var beforeGlobalState = globalState.Capture();
            var hitCount = 0;
            var transitionCount = 0;
            hub.PlayerHit += _ => hitCount++;
            hub.StateChanged += _ => transitionCount++;

            tracker.Enter(101, obstacle, new Vector3(8f, 2f, 3f));

            Assert.That(hitCount, Is.EqualTo(1));
            Assert.That(transitionCount, Is.Zero);
            Assert.That(machine.CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(machine.Snapshot, Is.EqualTo(before));
            Assert.That(globalState.Capture(), Is.EqualTo(beforeGlobalState));
        }

        // **Validates: Requirements 7.10, 7.11, 12.8, 14.3**
        [Test]
        public void ResetLifecyclePayloadsPreserveRequestCorrelationAndRunningResult()
        {
            var hub = new PlayerEventHub();
            PlayerResetStartedEvent? started = null;
            PlayerResetCompletedEvent? completed = null;
            hub.ResetStarted += value => started = value;
            hub.ResetCompleted += value => completed = value;

            hub.PublishResetStarted("reset-example-17");
            hub.PublishResetCompleted("reset-example-17", PlayerState.Running);

            Assert.That(started.HasValue, Is.True);
            Assert.That(started.Value.EventId, Is.EqualTo(1UL));
            Assert.That(started.Value.RequestId, Is.EqualTo("reset-example-17"));
            Assert.That(completed.HasValue, Is.True);
            Assert.That(completed.Value.EventId, Is.EqualTo(2UL));
            Assert.That(completed.Value.RequestId, Is.EqualTo("reset-example-17"));
            Assert.That(completed.Value.ResultingState, Is.EqualTo(PlayerState.Running));
            Assert.That(hub.LastEventId, Is.EqualTo(2UL));
        }
        // **Validates: Requirements 7.7, 7.14, 12.8**
        [Test]
        public void SubscriberExceptionsAreIsolatedAndReportedWithCategory()
        {
            var hub = new PlayerEventHub();
            var healthySubscriberCalls = 0;
            var diagnostics = new List<ValidationDiagnostic>();
            hub.PlayerHit += _ => throw new InvalidOperationException("subscriber failure");
            hub.PlayerHit += _ => healthySubscriberCalls++;
            hub.ValidationReported += _ => throw new ApplicationException("diagnostic failure");
            hub.ValidationReported += diagnostics.Add;

            Assert.DoesNotThrow(() => hub.PublishPlayerHit(
                19UL,
                "obstacle-subscriber",
                new TestEnvironmentObject(
                    "obstacle-subscriber", EnvironmentObjectKind.Obstacle, 0f),
                Vector3.one));

            Assert.That(healthySubscriberCalls, Is.EqualTo(1));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics[0].Code, Is.EqualTo(DiagnosticCode.SubscriberException));
            Assert.That(diagnostics[0].Field, Is.EqualTo("PlayerHit"));
            Assert.That(diagnostics[0].Message, Does.Contain(typeof(InvalidOperationException).FullName));
            Assert.That(diagnostics[0].Message, Does.Contain("PlayerHit"));
        }

        // **Validates: Requirements 7.2, 7.5, 7.7, 12.8**
        [Test]
        public void EventAndContactSequencesSurviveTrackerClearExitAndReentry()
        {
            var hub = new PlayerEventHub();
            var tracker = new EnvironmentContactTracker(hub);
            var obstacle = new TestEnvironmentObject(
                "obstacle-sequence", EnvironmentObjectKind.Obstacle, 0f);
            var observations = new List<PlayerHitEvent>();
            hub.PlayerHit += observations.Add;

            tracker.Enter(301, obstacle, Vector3.zero);
            tracker.Clear();
            tracker.Enter(301, obstacle, Vector3.right);
            tracker.Exit(301, obstacle);
            tracker.Enter(302, obstacle, Vector3.up);

            Assert.That(observations.Count, Is.EqualTo(3));
            Assert.That(observations.Select(value => value.EventId),
                Is.EqualTo(new[] { 1UL, 2UL, 3UL }));
            Assert.That(observations.Select(value => value.ContactId),
                Is.EqualTo(new[] { 1UL, 2UL, 3UL }));
            Assert.That(observations[0].ContactPosition, Is.EqualTo(Vector3.zero));
            Assert.That(observations[1].ContactPosition, Is.EqualTo(Vector3.right));
            Assert.That(observations[2].ContactPosition, Is.EqualTo(Vector3.up));
            Assert.That(hub.LastEventId, Is.EqualTo(3UL));
            Assert.That(tracker.LastContactId, Is.EqualTo(3UL));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1));
        }

        // **Validates: Requirements 7.12, 12.8, 12.13, 14.3**
        [Test]
        public void EventSourceRuntimeSurfaceHasZeroConcreteRachelDependency()
        {
            var runtimeAssembly = typeof(PlayerEventHub).Assembly;
            var referencedRachelAssemblies = runtimeAssembly.GetReferencedAssemblies()
                .Where(reference => ContainsRachel(reference.Name))
                .Select(reference => reference.Name)
                .ToArray();
            var eventRuntimeTypes = new[]
            {
                typeof(PlayerEventHub),
                typeof(EnvironmentContactTracker),
                typeof(IPlayerEventSource),
                typeof(PlayerHitEvent),
                typeof(CoinCollectedEvent),
                typeof(PlayerStateChangedEvent),
                typeof(PlayerResetStartedEvent),
                typeof(PlayerResetCompletedEvent)
            };
            var concreteRachelDependencies = eventRuntimeTypes
                .SelectMany(DependencyTypes)
                .Where(type => ContainsRachel(type.FullName) ||
                    ContainsRachel(type.Assembly.GetName().Name))
                .Select(type => type.AssemblyQualifiedName)
                .Distinct()
                .ToArray();

            Assert.That(referencedRachelAssemblies, Is.Empty);
            Assert.That(concreteRachelDependencies, Is.Empty);
            Assert.That(typeof(IPlayerEventSource).IsAssignableFrom(typeof(PlayerEventHub)), Is.True);
        }
        private static IEnumerable<Type> DependencyTypes(Type type)
        {
            yield return type;
            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters())
                    yield return parameter.ParameterType;
            }
            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field.FieldType;
            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return property.PropertyType;
            foreach (var eventInfo in type.GetEvents(
                         BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return eventInfo.EventHandlerType;
        }

        private static bool ContainsRachel(string value)
        {
            return value != null &&
                value.IndexOf("Rachel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static PlayerStateMachine CreateMachine(PlayerState state, bool grounded)
        {
            var snapshot = new PlayerSnapshot(
                new Vector3(2f, 3f, 4f),
                Quaternion.Euler(5f, 10f, 15f),
                state,
                grounded,
                12f,
                LogicalLane.Center,
                LogicalLane.Center,
                0f,
                0f,
                0f,
                0f,
                state == PlayerState.Jumping ? 3f : 0f,
                state == PlayerState.Sliding ? 0.5f : 0f,
                new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                ImmutableValueSequence<LaneRequest>.Empty,
                ImmutableValueSequence<PlayerCommandKind>.Empty,
                state == PlayerState.Resetting);
            return new PlayerStateMachine(
                snapshot,
                ImmutableValueSequence<string>.Empty);
        }

        private static void RelayTransitions(PlayerStateMachine machine, PlayerEventHub hub)
        {
            machine.StateChanged += value => hub.PublishStateChanged(
                value.PreviousState,
                value.CurrentState,
                value.TransitionCause);
        }

        private sealed class TestEnvironmentObject : IEnvironmentObject
        {
            public TestEnvironmentObject(
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

        private sealed class ExternalState
        {
            private readonly string lifecycle;
            private readonly int score;
            private readonly string userInterface;

            public ExternalState(string lifecycle, int score, string userInterface)
            {
                this.lifecycle = lifecycle;
                this.score = score;
                this.userInterface = userInterface;
            }

            public string Capture()
            {
                return lifecycle + "|" + score + "|" + userInterface;
            }
        }
    }
}
