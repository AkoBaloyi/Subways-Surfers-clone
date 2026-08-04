using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Movement half of the facade. Each <c>FixedUpdate</c> is one <c>Movement_Update</c> driven with
    /// <see cref="Time.fixedDeltaTime"/> as <c>Elapsed_Simulation_Time</c>; the public command and
    /// query surface and the realized-motion snapshots are forwarded to
    /// <see cref="PlayerMovementLoop"/>. Configuration ownership stays in the configuration partial.
    /// </summary>
    public sealed partial class PlayerControllerFacade : IPlayerCommands, IPlayerQueries
    {
        private readonly PlayerEventHub events = new PlayerEventHub();

        private PlayerMovementLoop movementLoop;
        private PlayerResetService resetService;
        private EnvironmentContactTracker contactTracker;

        /// <summary>
        /// The session event source: one Event_Id sequence for transitions, contacts, and the reset
        /// lifecycle of this player.
        /// </summary>
        public PlayerEventHub Events { get { return events; } }

        public int MovementUpdateCount
        {
            get { return movementLoop == null ? 0 : movementLoop.MovementUpdateCount; }
        }

        public int MoveInvocationCount
        {
            get { return movementLoop == null ? 0 : movementLoop.MoveInvocationCount; }
        }

        public Vector3 LastRequestedDisplacement
        {
            get { return movementLoop == null ? Vector3.zero : movementLoop.LastRequestedDisplacement; }
        }

        public Vector3 LastRealizedDisplacement
        {
            get { return movementLoop == null ? Vector3.zero : movementLoop.LastRealizedDisplacement; }
        }

        public PlayerSnapshot PreMovementSnapshot
        {
            get
            {
                var loop = EnsureMovementLoop();
                return loop == null ? UnavailableSnapshot() : loop.PreMovementSnapshot;
            }
        }

        public PlayerState CurrentState
        {
            get
            {
                var loop = EnsureMovementLoop();
                return loop == null ? PlayerState.Running : loop.CurrentState;
            }
        }

        public bool IsGrounded
        {
            get
            {
                var loop = EnsureMovementLoop();
                return loop != null && loop.IsGrounded;
            }
        }

        public float ForwardSpeed
        {
            get
            {
                var loop = EnsureMovementLoop();
                return loop == null ? EffectiveConfiguration.ForwardSpeed : loop.ForwardSpeed;
            }
        }

        public PlayerSnapshot Snapshot
        {
            get
            {
                var loop = EnsureMovementLoop();
                return loop == null ? UnavailableSnapshot() : loop.Snapshot;
            }
        }

        /// <summary>
        /// Runs one <c>Movement_Update</c>. Public and independent of the enabled state so tests and
        /// integrations can drive a single deterministic update.
        /// </summary>
        public void ExecuteMovementUpdate(float elapsedSimulationTime)
        {
            var loop = EnsureMovementLoop();
            if (loop == null) return;

            loop.ExecuteMovementUpdate(elapsedSimulationTime);
        }

        public ActionRequestResult RequestLane(LaneDirection direction)
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new ActionRequestResult(
                    CommandStatus.Rejected, PlayerCommandKind.Lane,
                    RejectionReason.InvalidState, PlayerState.Running)
                : loop.RequestLane(direction);
        }

        public ActionRequestResult RequestJump()
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new ActionRequestResult(
                    CommandStatus.Rejected, PlayerCommandKind.Jump,
                    RejectionReason.InvalidState, PlayerState.Running)
                : loop.RequestJump();
        }

        public ActionRequestResult RequestSlide()
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new ActionRequestResult(
                    CommandStatus.Rejected, PlayerCommandKind.Slide,
                    RejectionReason.InvalidState, PlayerState.Running)
                : loop.RequestSlide();
        }

        public FailureCommandResult RequestFailure()
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new FailureCommandResult(
                    CommandStatus.Rejected, RejectionReason.InvalidState, PlayerState.Running)
                : loop.RequestFailure();
        }

        /// <summary>
        /// Public reset command surface. An accepted Request_Id runs the complete atomic reset
        /// sequence through <see cref="PlayerResetService"/> before returning.
        /// </summary>
        public ResetRequestResult RequestReset(string requestId)
        {
            var service = EnsureResetService();
            return service == null
                ? new ResetRequestResult(
                    CommandStatus.Rejected, RejectionReason.InvalidState,
                    PlayerState.Running, requestId)
                : service.RequestReset(requestId);
        }

        /// <summary>The complete reset equality surface of this player.</summary>
        public PlayerResetSnapshot ResetSnapshot
        {
            get
            {
                var service = EnsureResetService();
                return service == null
                    ? new PlayerResetSnapshot(
                        UnavailableSnapshot(), transform.position, transform.rotation,
                        Vector3.zero, Vector3.zero, Vector3.zero, 0f, false,
                        InputLatchState.Neutral, true,
                        ImmutableValueSequence<LogicalContactIdentity>.Empty,
                        ImmutableValueSequence<ContactEventIdentity>.Empty)
                    : service.Snapshot;
            }
        }

        public SpeedSetResult SetForwardSpeed(float requestedSpeed)
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new SpeedSetResult(
                    CommandStatus.Rejected, RejectionReason.InvalidState, PlayerState.Running,
                    requestedSpeed, EffectiveConfiguration.ForwardSpeed)
                : loop.SetForwardSpeed(requestedSpeed);
        }

        private void OnEnable()
        {
            EnsureMovementLoop();
        }

        private void FixedUpdate()
        {
            if (!SimulationEnabled) return;

            ExecuteMovementUpdate(Time.fixedDeltaTime);
        }

        private PlayerMovementLoop EnsureMovementLoop()
        {
            if (movementLoop != null) return movementLoop;
            if (!SimulationEnabled) return null;

            var motor = ResolveMotorSurface();
            if (motor == null) return null;

            movementLoop = new PlayerMovementLoop(EffectiveConfiguration, motor, null, events);
            return movementLoop;
        }

        /// <summary>
        /// The reset service for this player, built once the movement loop exists. Sibling components
        /// that own Unity-side state join the atomic sequence as reset participants.
        /// </summary>
        private PlayerResetService EnsureResetService()
        {
            if (resetService != null) return resetService;

            var loop = EnsureMovementLoop();
            if (loop == null) return null;

            var contacts = GetComponent<EnvironmentContactAdapter>();

            // The contact tracker owns logical contact identity and is what turns overlap samples into
            // hit and coin events. Until now it was only ever constructed by tests, so a real scene had
            // an unconfigured contact adapter: its tracker was null and its mask zero, and it returned
            // immediately from sampling. Every piece was tested in isolation and nothing assembled them.
            //
            // An adapter that already holds a tracker keeps it, and reset then clears that same tracker.
            // A caller that bound its own owns the contact identity for this player, and replacing it
            // would both lose the contacts already open and leave reset clearing a tracker nothing is
            // sampling into.
            contactTracker = contacts != null && contacts.IsConfigured
                ? contacts.Tracker
                : new EnvironmentContactTracker(events);

            resetService = new PlayerResetService(
                EffectiveConfiguration, loop, events, null, contactTracker, null);

            if (contacts != null && !contacts.IsConfigured)
            {
                // Contacts are selected by layer and then resolved by identity, so the mask only has to
                // be wide enough to reach the environment. The obstruction mask is the environment-facing
                // mask this configuration already carries.
                contacts.Configure(contactTracker, EffectiveConfiguration.ObstructionLayerMask);
            }

            var input = GetComponent<PlayerInputAdapter>();
            if (input != null)
            {
                resetService.InputLatch = input;
                resetService.AddResetParticipant(input);
            }

            resetService.AddResetParticipant(GetComponent<EnvironmentContactAdapter>());

            var cameraTarget = EffectiveReferences.CameraTarget as Component;
            resetService.AddResetParticipant(cameraTarget == null
                ? null
                : cameraTarget.GetComponent<PlayerCameraFollow>());
            return resetService;
        }

        private IPlayerMotorSurface ResolveMotorSurface()
        {
            var existing = GetComponent<IPlayerMotorSurface>();
            if (existing != null) return existing;

            // The motor requires the CharacterController it drives; a prefab without one keeps the
            // command surface available and simply performs no movement.
            return GetComponent<CharacterController>() == null
                ? null
                : gameObject.AddComponent<CharacterControllerMotor>();
        }

        private PlayerSnapshot UnavailableSnapshot()
        {
            var configuration = EffectiveConfiguration;
            var lateralPosition = transform.position.x;
            return new PlayerSnapshot(
                transform.position,
                transform.rotation,
                PlayerState.Running,
                false,
                configuration.ForwardSpeed,
                LogicalLane.Center,
                LogicalLane.Center,
                lateralPosition,
                lateralPosition,
                lateralPosition,
                0f,
                0f,
                0f,
                configuration.BaselineCollider,
                ImmutableValueSequence<LaneRequest>.Empty,
                ImmutableValueSequence<PlayerCommandKind>.Empty,
                false);
        }
    }
}
