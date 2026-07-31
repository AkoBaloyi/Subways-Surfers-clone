using System;
using System.Collections.Generic;
using System.Globalization;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Isolated command surface and observation log for PlayerTestScene.
    ///
    /// The harness exists so every player behaviour can be driven and observed without a Game Manager,
    /// a score system, or production user interface. It issues commands through the public player
    /// contracts only, records the synchronous result of each command, and appends every published
    /// player event to one ordered log with its complete payload. It owns no gameplay rule: it never
    /// writes player transform, state, velocity, lanes, timers, collider, camera, or animation state,
    /// and it never applies a coin value to a score or mutates any environment object.
    ///
    /// Non-production. This is validation tooling confined to Assets/Player. Run coordination, score,
    /// currency, and HUD belong to the Game State system and are deliberately absent here.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PlayerTestSceneHarness : MonoBehaviour, INonProductionValidationDouble
    {
        [SerializeField]
        [Tooltip("The player whose public command and query contracts this harness drives. Assigned " +
                 "as a concrete component because interface-typed fields are never serialized.")]
        private PlayerControllerFacade player;

        [SerializeField]
        [Tooltip("Speed submitted by the Set Forward Speed control. Negative and non-finite values " +
                 "are intentionally reachable so the rejection path stays observable.")]
        private float suppliedForwardSpeed = 8f;

        [SerializeField]
        [Tooltip("Reset request identifier submitted by the Request Reset control. Reusing a value " +
                 "exercises duplicate rejection; clearing it exercises missing-identifier rejection.")]
        private string suppliedResetRequestId = "validation-reset-1";

        [SerializeField]
        [Tooltip("Maximum entries retained by the ordered event log. The log preserves publication " +
                 "order; when full it drops the oldest entry so a long session stays observable.")]
        private int eventLogCapacity = 256;

        private readonly List<string> eventLog = new List<string>();
        private readonly List<string> diagnosticLog = new List<string>();

        private IPlayerCommands commands;
        private IPlayerQueries queries;
        private IPlayerEventSource events;
        private bool subscribed;

        private string lastCommandResult = "none";
        private ulong observedEventCount;

        public string NonProductionNotice
        {
            get
            {
                return "Validation-only test-scene harness. Not production user interface, score, or " +
                       "run coordination.";
            }
        }

        /// <summary>True once the harness resolved the player contracts and subscribed to its events.</summary>
        public bool PlayerResolved { get { return commands != null && queries != null; } }

        public bool Subscribed { get { return subscribed; } }

        /// <summary>Rendered result of the most recent command, recorded synchronously at the call.</summary>
        public string LastCommandResult { get { return lastCommandResult; } }

        /// <summary>Every published player event in publication order, with its complete payload.</summary>
        public IReadOnlyList<string> EventLog { get { return eventLog; } }

        /// <summary>Every reported validation diagnostic in report order.</summary>
        public IReadOnlyList<string> DiagnosticLog { get { return diagnosticLog; } }

        public ulong ObservedEventCount { get { return observedEventCount; } }

        public float SuppliedForwardSpeed
        {
            get { return suppliedForwardSpeed; }
            set { suppliedForwardSpeed = value; }
        }

        public string SuppliedResetRequestId
        {
            get { return suppliedResetRequestId; }
            set { suppliedResetRequestId = value; }
        }

        public PlayerControllerFacade Player { get { return player; } }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            Unbind();
        }

        /// <summary>
        /// Resolves the serialized concrete player reference to the public contracts and subscribes to
        /// the player event source. Safe to call repeatedly.
        /// </summary>
        public void Bind()
        {
            if (player == null) return;

            commands = player;
            queries = player;

            if (subscribed) return;

            events = player.Events;
            if (events == null) return;

            events.StateChanged += OnStateChanged;
            events.PlayerHit += OnPlayerHit;
            events.CoinCollected += OnCoinCollected;
            events.ResetStarted += OnResetStarted;
            events.ResetCompleted += OnResetCompleted;
            events.ValidationReported += OnValidationReported;
            subscribed = true;
        }

        public void Unbind()
        {
            if (!subscribed || events == null) return;

            events.StateChanged -= OnStateChanged;
            events.PlayerHit -= OnPlayerHit;
            events.CoinCollected -= OnCoinCollected;
            events.ResetStarted -= OnResetStarted;
            events.ResetCompleted -= OnResetCompleted;
            events.ValidationReported -= OnValidationReported;
            subscribed = false;
        }

        // ----- Observable controls. Each records the synchronous result it received. -----

        public ActionRequestResult RequestLaneLeft()
        {
            return RecordLane(LaneDirection.Left);
        }

        public ActionRequestResult RequestLaneRight()
        {
            return RecordLane(LaneDirection.Right);
        }

        public ActionRequestResult RequestJump()
        {
            if (commands == null) return RecordUnbound<ActionRequestResult>(PlayerCommandKind.Jump);

            var result = commands.RequestJump();
            lastCommandResult = Render(result.Command, result.Status, result.Reason, result.CurrentState, null);
            return result;
        }

        public ActionRequestResult RequestSlide()
        {
            if (commands == null) return RecordUnbound<ActionRequestResult>(PlayerCommandKind.Slide);

            var result = commands.RequestSlide();
            lastCommandResult = Render(result.Command, result.Status, result.Reason, result.CurrentState, null);
            return result;
        }

        public FailureCommandResult RequestFailure()
        {
            if (commands == null)
            {
                lastCommandResult = RenderUnbound(PlayerCommandKind.Failure);
                return default(FailureCommandResult);
            }

            var result = commands.RequestFailure();
            lastCommandResult = Render(result.Command, result.Status, result.Reason, result.CurrentState, null);
            return result;
        }

        public ResetRequestResult RequestReset()
        {
            if (commands == null)
            {
                lastCommandResult = RenderUnbound(PlayerCommandKind.Reset);
                return default(ResetRequestResult);
            }

            var result = commands.RequestReset(suppliedResetRequestId);
            lastCommandResult = Render(
                result.Command, result.Status, result.Reason, result.CurrentState, result.RequestId);
            return result;
        }

        public SpeedSetResult SetForwardSpeed()
        {
            if (commands == null)
            {
                lastCommandResult = RenderUnbound(PlayerCommandKind.SetForwardSpeed);
                return default(SpeedSetResult);
            }

            var result = commands.SetForwardSpeed(suppliedForwardSpeed);
            lastCommandResult = string.Format(
                CultureInfo.InvariantCulture,
                "SetForwardSpeed {0}{1} requested={2} effective={3}",
                result.Status,
                result.Reason == RejectionReason.None ? string.Empty : " reason=" + result.Reason,
                suppliedForwardSpeed.ToString("R", CultureInfo.InvariantCulture),
                result.EffectiveSpeed.ToString("R", CultureInfo.InvariantCulture));
            return result;
        }

        // ----- Observable results. Queries only; the harness never derives player state itself. -----

        public string RenderPlayerResult()
        {
            if (queries == null) return "player=<unbound>";

            var snapshot = queries.Snapshot;
            return string.Format(
                CultureInfo.InvariantCulture,
                "state={0} grounded={1} speed={2} lane={3}->{4} position={5}",
                queries.CurrentState,
                queries.IsGrounded,
                queries.ForwardSpeed.ToString("R", CultureInfo.InvariantCulture),
                snapshot.CurrentLane,
                snapshot.TargetLane,
                snapshot.Position.ToString("R", CultureInfo.InvariantCulture));
        }

        public string RenderConfigurationResult()
        {
            if (player == null) return "configuration=<unbound>";

            return string.Format(
                CultureInfo.InvariantCulture,
                "status={0} simulation={1} diagnostics={2}",
                player.ConfigurationStatus,
                player.SimulationEnabled,
                player.ConfigurationDiagnostics.Count.ToString(CultureInfo.InvariantCulture));
        }

        public void ClearLogs()
        {
            eventLog.Clear();
            diagnosticLog.Clear();
            observedEventCount = 0;
            lastCommandResult = "none";
        }

        // ----- Event log. Complete payloads, appended in publication order. -----

        private void OnStateChanged(PlayerStateChangedEvent published)
        {
            Append(string.Format(
                CultureInfo.InvariantCulture,
                "StateChanged eventId={0} previous={1} current={2} cause={3}",
                published.EventId.ToString(CultureInfo.InvariantCulture),
                published.PreviousState,
                published.CurrentState,
                published.TransitionCause));
        }

        private void OnPlayerHit(PlayerHitEvent published)
        {
            Append(string.Format(
                CultureInfo.InvariantCulture,
                "PlayerHit eventId={0} contactId={1} environmentObjectId={2} obstacle={3} position={4}",
                published.EventId.ToString(CultureInfo.InvariantCulture),
                published.ContactId.ToString(CultureInfo.InvariantCulture),
                Describe(published.EnvironmentObjectId),
                Describe(published.Obstacle),
                published.ContactPosition.ToString("R", CultureInfo.InvariantCulture)));
        }

        private void OnCoinCollected(CoinCollectedEvent published)
        {
            // Reported only. Applying the value to a score or currency total belongs to the Game State
            // system, and collecting or removing the coin belongs to the Environment system.
            Append(string.Format(
                CultureInfo.InvariantCulture,
                "CoinCollected eventId={0} contactId={1} environmentObjectId={2} coin={3} value={4}",
                published.EventId.ToString(CultureInfo.InvariantCulture),
                published.ContactId.ToString(CultureInfo.InvariantCulture),
                Describe(published.EnvironmentObjectId),
                Describe(published.Coin),
                published.CollectibleValue.ToString("R", CultureInfo.InvariantCulture)));
        }

        private void OnResetStarted(PlayerResetStartedEvent published)
        {
            Append(string.Format(
                CultureInfo.InvariantCulture,
                "ResetStarted eventId={0} requestId={1}",
                published.EventId.ToString(CultureInfo.InvariantCulture),
                Describe(published.RequestId)));
        }

        private void OnResetCompleted(PlayerResetCompletedEvent published)
        {
            Append(string.Format(
                CultureInfo.InvariantCulture,
                "ResetCompleted eventId={0} requestId={1} resultingState={2}",
                published.EventId.ToString(CultureInfo.InvariantCulture),
                Describe(published.RequestId),
                published.ResultingState));
        }

        private void OnValidationReported(ValidationDiagnostic published)
        {
            var rendered = string.Format(
                CultureInfo.InvariantCulture,
                "ValidationReported severity={0} code={1} field={2} message={3}",
                published.Severity,
                published.Code,
                Describe(published.Field),
                Describe(published.Message));
            diagnosticLog.Add(rendered);
            Append(rendered);
        }

        private ActionRequestResult RecordLane(LaneDirection direction)
        {
            if (commands == null) return RecordUnbound<ActionRequestResult>(PlayerCommandKind.Lane);

            var result = commands.RequestLane(direction);
            lastCommandResult = Render(
                result.Command, result.Status, result.Reason, result.CurrentState, direction.ToString());
            return result;
        }

        private TResult RecordUnbound<TResult>(PlayerCommandKind kind) where TResult : struct
        {
            lastCommandResult = RenderUnbound(kind);
            return default(TResult);
        }

        private static string RenderUnbound(PlayerCommandKind kind)
        {
            return kind + " not issued: the harness has no player reference assigned.";
        }

        private static string Render(
            PlayerCommandKind kind,
            CommandStatus status,
            RejectionReason reason,
            PlayerState state,
            string supplied)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}{2} state={3}{4}",
                kind,
                status,
                reason == RejectionReason.None ? string.Empty : " reason=" + reason,
                state,
                string.IsNullOrEmpty(supplied) ? string.Empty : " supplied=" + supplied);
        }

        private void Append(string entry)
        {
            observedEventCount++;

            var capacity = eventLogCapacity < 1 ? 1 : eventLogCapacity;
            if (eventLog.Count >= capacity) eventLog.RemoveAt(0);
            eventLog.Add(entry);
        }

        private static string Describe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<empty>" : value;
        }

        private static string Describe(IEnvironmentObject environmentObject)
        {
            if (environmentObject == null) return "<none>";

            var component = environmentObject as Component;
            return component == null ? environmentObject.GetType().Name : component.gameObject.name;
        }
    }
}
