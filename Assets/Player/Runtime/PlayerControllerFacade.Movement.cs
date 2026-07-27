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
        private PlayerMovementLoop movementLoop;

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

        public ResetRequestResult RequestReset(string requestId)
        {
            var loop = EnsureMovementLoop();
            return loop == null
                ? new ResetRequestResult(
                    CommandStatus.Rejected, RejectionReason.InvalidState,
                    PlayerState.Running, requestId)
                : loop.RequestReset(requestId);
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

            movementLoop = new PlayerMovementLoop(EffectiveConfiguration, motor, null);
            return movementLoop;
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
