using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// One <c>Movement_Update</c>: resolve pending landing and slide restoration, consume queued
    /// lane time, integrate forward and vertical requests, and submit exactly one combined
    /// displacement. All rules come from the pure domain classes; this type only sequences them
    /// and holds the authoritative snapshot fields the domain classes are seeded from.
    /// </summary>
    public sealed class PlayerMovementLoop : IPlayerCommands, IPlayerQueries
    {
        private readonly PlayerConfiguration configuration;
        private readonly IPlayerMotorSurface motor;
        private readonly LanePlanner lanePlanner;
        private readonly ForwardSpeedApi forwardSpeedApi;
        private readonly List<string> acceptedResetRequestIds = new List<string>();
        private readonly Func<bool> baselineRestorationQuery;

        private PlayerStateMachine stateMachine;
        private PlayerState state;
        private bool grounded;
        private float verticalVelocity;
        private float slideElapsedTime;
        private ColliderProfile colliderProfile;
        private bool resetInProgress;
        private ulong laneRequestOrder;

        public PlayerMovementLoop(
            PlayerConfiguration configuration,
            IPlayerMotorSurface motor,
            Action<ValidationDiagnostic> diagnosticSink)
        {
            if (motor == null) throw new ArgumentNullException(nameof(motor));

            this.configuration = configuration;
            this.motor = motor;
            baselineRestorationQuery = QueryBaselineRestoration;

            lanePlanner = new LanePlanner(
                NearestLane(motor.Position.x, configuration.LaneCenters),
                configuration.LaneCenters,
                configuration.LaneChangeDuration,
                configuration.LanePositionTolerance);
            forwardSpeedApi = new ForwardSpeedApi(configuration.ForwardSpeed, diagnosticSink);

            state = PlayerState.Running;
            colliderProfile = configuration.BaselineCollider;
            motor.ApplyColliderProfile(colliderProfile);
            PreMovementSnapshot = BuildSnapshot();
        }

        public int MovementUpdateCount { get; private set; }
        public int MoveInvocationCount { get { return motor.MoveInvocationCount; } }
        public Vector3 LastRequestedDisplacement { get; private set; }
        public Vector3 LastRealizedDisplacement { get; private set; }
        public PlayerSnapshot PreMovementSnapshot { get; private set; }

        public PlayerState CurrentState { get { return state; } }
        public bool IsGrounded { get { return grounded; } }
        public float ForwardSpeed { get { return forwardSpeedApi.ForwardSpeed; } }
        public PlayerSnapshot Snapshot { get { return BuildSnapshot(); } }

        public ActionRequestResult RequestLane(LaneDirection direction)
        {
            var result = StateMachine.RequestLane(direction);
            if (result.Status != CommandStatus.Accepted) return result;

            laneRequestOrder++;
            lanePlanner.Enqueue(new LaneRequest(direction, laneRequestOrder));
            return result;
        }

        public ActionRequestResult RequestJump()
        {
            var machine = StateMachine;
            var result = machine.RequestJump();
            if (result.Status != CommandStatus.Accepted) return result;

            // JumpController owns the single takeoff impulse assignment.
            var jump = NewJumpController();
            jump.RequestJump();
            state = machine.CurrentState;
            verticalVelocity = jump.Snapshot.VerticalVelocity;
            return result;
        }

        public ActionRequestResult RequestSlide()
        {
            var machine = StateMachine;
            var result = machine.RequestSlide();
            if (result.Status != CommandStatus.Accepted) return result;

            // SlideController owns the timer reset and the slide profile selection.
            var slide = NewSlideController();
            slide.RequestSlide();
            state = machine.CurrentState;
            slideElapsedTime = slide.Snapshot.SlideElapsedTime;
            ApplyColliderProfile(slide.Snapshot.ColliderProfile);
            return result;
        }

        public FailureCommandResult RequestFailure()
        {
            var machine = StateMachine;
            var result = machine.RequestFailure();
            if (result.Status != CommandStatus.Accepted) return result;

            state = machine.CurrentState;
            grounded = machine.IsGrounded;
            ClearMovementTransients();
            return result;
        }

        public ResetRequestResult RequestReset(string requestId)
        {
            var machine = StateMachine;
            var result = machine.RequestReset(requestId);
            if (result.Status != CommandStatus.Accepted) return result;

            acceptedResetRequestIds.Add(requestId);
            state = machine.CurrentState;
            resetInProgress = true;
            ClearMovementTransients();
            return result;
        }

        public SpeedSetResult SetForwardSpeed(float requestedSpeed)
        {
            var result = forwardSpeedApi.SetForwardSpeed(requestedSpeed);
            return new SpeedSetResult(
                result.Status, result.Command, result.Reason, state,
                result.RequestedSpeed, result.EffectiveSpeed);
        }

        public void ExecuteMovementUpdate(float elapsedSimulationTime)
        {
            if (!IsFinite(elapsedSimulationTime) || elapsedSimulationTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulationTime));

            MovementUpdateCount++;
            grounded = SampleGrounded();

            if (elapsedSimulationTime > 0f)
            {
                ResolveLanding();
                AdvanceSlide(elapsedSimulationTime);
                AdvanceJump(elapsedSimulationTime);
                lanePlanner.Advance(elapsedSimulationTime, state);
            }

            PreMovementSnapshot = BuildSnapshot();
            LastRequestedDisplacement = elapsedSimulationTime > 0f
                ? ComposeDisplacement(elapsedSimulationTime)
                : Vector3.zero;
            LastRealizedDisplacement = motor.Move(LastRequestedDisplacement);
            grounded = SampleGrounded();
        }

        private void ResolveLanding()
        {
            if (state != PlayerState.Jumping) return;
            if (!NewJumpController().ResolveLanding(grounded)) return;

            var machine = StateMachine;
            machine.ResolveLanding();
            state = machine.CurrentState;
            verticalVelocity = 0f;
        }

        private void AdvanceSlide(float elapsedSimulationTime)
        {
            if (state != PlayerState.Sliding) return;

            var slide = NewSlideController();
            slide.Advance(elapsedSimulationTime, baselineRestorationQuery);
            slideElapsedTime = slide.Snapshot.SlideElapsedTime;
            if (slide.Snapshot.State != PlayerState.Running) return;

            ApplyColliderProfile(slide.Snapshot.ColliderProfile);
            var machine = StateMachine;
            machine.ResolveSlideRestoration(true);
            state = machine.CurrentState;
        }

        private void AdvanceJump(float elapsedSimulationTime)
        {
            if (state != PlayerState.Jumping) return;

            var jump = NewJumpController();
            jump.Advance(elapsedSimulationTime);
            verticalVelocity = jump.Snapshot.VerticalVelocity;
        }

        private Vector3 ComposeDisplacement(float elapsedSimulationTime)
        {
            var displacement = ForwardDisplacement.Calculate(
                state, forwardSpeedApi.ForwardSpeed, elapsedSimulationTime);
            displacement.x += LateralDisplacement();
            displacement.y += VerticalDisplacement(elapsedSimulationTime);
            return displacement;
        }

        private float LateralDisplacement()
        {
            return IsActive(state) ? lanePlanner.LateralPosition - motor.Position.x : 0f;
        }

        private float VerticalDisplacement(float elapsedSimulationTime)
        {
            if (state == PlayerState.Jumping) return verticalVelocity * elapsedSimulationTime;
            if (state != PlayerState.Running && state != PlayerState.Sliding) return 0f;

            // Grounded states settle downward by one gravity step without accumulating velocity,
            // which keeps contact probing valid and stays zero when no time elapses.
            return -configuration.GravityAcceleration * elapsedSimulationTime * elapsedSimulationTime;
        }

        private bool SampleGrounded()
        {
            return motor.SampleGrounded(
                colliderProfile,
                configuration.GroundLayerMask,
                configuration.GroundContactTolerance,
                configuration.GroundNormalThreshold);
        }

        private bool QueryBaselineRestoration()
        {
            return motor.IsBaselineRestorationSafe(
                configuration.BaselineCollider, configuration.ObstructionLayerMask);
        }

        private void ApplyColliderProfile(ColliderProfile profile)
        {
            colliderProfile = profile;
            motor.ApplyColliderProfile(profile);
        }

        private void ClearMovementTransients()
        {
            verticalVelocity = 0f;
            slideElapsedTime = 0f;
            lanePlanner.Clear();
        }

        private PlayerStateMachine StateMachine
        {
            get
            {
                if (stateMachine == null || !ReflectsAuthoritativeState(stateMachine.Snapshot))
                {
                    stateMachine = new PlayerStateMachine(
                        BuildSnapshot(),
                        new ImmutableValueSequence<string>(acceptedResetRequestIds));
                }

                return stateMachine;
            }
        }

        private bool ReflectsAuthoritativeState(PlayerSnapshot snapshot)
        {
            return snapshot.State == state &&
                snapshot.IsGrounded == grounded &&
                snapshot.VerticalVelocity.Equals(verticalVelocity) &&
                snapshot.ResetInProgress == resetInProgress;
        }

        private JumpController NewJumpController()
        {
            return new JumpController(
                BuildSnapshot(), configuration.JumpVelocity, configuration.GravityAcceleration);
        }

        private SlideController NewSlideController()
        {
            return new SlideController(
                BuildSnapshot(),
                configuration.SlideDuration,
                configuration.BaselineCollider,
                configuration.SlideCollider);
        }

        private PlayerSnapshot BuildSnapshot()
        {
            return new PlayerSnapshot(
                motor.Position,
                motor.Rotation,
                state,
                grounded,
                forwardSpeedApi.ForwardSpeed,
                lanePlanner.CurrentLane,
                lanePlanner.TargetLane,
                lanePlanner.LateralPosition,
                lanePlanner.LaneSegmentStart,
                lanePlanner.LaneSegmentTarget,
                lanePlanner.LaneChangeProgress,
                verticalVelocity,
                slideElapsedTime,
                colliderProfile,
                LaneQueue(),
                ImmutableValueSequence<PlayerCommandKind>.Empty,
                resetInProgress);
        }

        private ImmutableValueSequence<LaneRequest> LaneQueue()
        {
            var queue = lanePlanner.LaneRequestQueue;
            return queue.Count == 0
                ? ImmutableValueSequence<LaneRequest>.Empty
                : new ImmutableValueSequence<LaneRequest>(queue);
        }

        private static LogicalLane NearestLane(float lateralPosition, Vector3 laneCenters)
        {
            var lane = LogicalLane.Center;
            var distance = Mathf.Abs(lateralPosition - laneCenters.y);
            if (Mathf.Abs(lateralPosition - laneCenters.x) < distance)
            {
                lane = LogicalLane.Left;
                distance = Mathf.Abs(lateralPosition - laneCenters.x);
            }

            return Mathf.Abs(lateralPosition - laneCenters.z) < distance ? LogicalLane.Right : lane;
        }

        private static bool IsActive(PlayerState value)
        {
            return value == PlayerState.Running ||
                value == PlayerState.Jumping ||
                value == PlayerState.Sliding;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
