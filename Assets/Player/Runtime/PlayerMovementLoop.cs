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
        private readonly IPlayerPoseRestoration poseRestoration;
        private readonly PlayerEventHub eventHub;
        private readonly LanePlanner lanePlanner;
        private readonly ForwardSpeedApi forwardSpeedApi;
        private readonly List<string> acceptedResetRequestIds = new List<string>();
        private readonly Func<bool> baselineRestorationQuery;
        private readonly Vector3 startPosition;
        private readonly Quaternion startRotation;

        private PlayerStateMachine stateMachine;
        private PlayerState state;
        private bool grounded;
        private bool startGrounded;
        private bool startGroundedSampled;
        private float verticalVelocity;
        private float slideElapsedTime;
        private ColliderProfile colliderProfile;
        private bool resetInProgress;
        private ulong laneRequestOrder;

        // Set only when the reset service restored the start pose without a motor able to write the
        // transform. The restored pose then becomes the authoritative player pose and realized
        // displacement accumulates onto it, so the domain surface reports the pose it restored.
        private bool poseRestoredInDomain;
        private Vector3 restoredPosition;

        public PlayerMovementLoop(
            PlayerConfiguration configuration,
            IPlayerMotorSurface motor,
            Action<ValidationDiagnostic> diagnosticSink)
            : this(configuration, motor, diagnosticSink, null)
        {
        }

        /// <summary>
        /// Session-wired loop: every accepted transition publishes exactly one
        /// <c>Player_State_Changed_Event</c> on the supplied hub, so all transitions share the one
        /// session Event_Id sequence. A null hub keeps the loop publication-free.
        /// </summary>
        public PlayerMovementLoop(
            PlayerConfiguration configuration,
            IPlayerMotorSurface motor,
            Action<ValidationDiagnostic> diagnosticSink,
            PlayerEventHub eventHub)
        {
            if (motor == null) throw new ArgumentNullException(nameof(motor));

            this.configuration = configuration;
            this.motor = motor;
            this.eventHub = eventHub;
            poseRestoration = motor as IPlayerPoseRestoration;
            startPosition = motor.Position;
            startRotation = motor.Rotation;
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
            var previous = state;
            state = machine.CurrentState;
            verticalVelocity = jump.Snapshot.VerticalVelocity;
            PublishTransition(previous, PlayerTransitionCause.JumpRequested);
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
            var previous = state;
            state = machine.CurrentState;
            slideElapsedTime = slide.Snapshot.SlideElapsedTime;
            ApplyColliderProfile(slide.Snapshot.ColliderProfile);
            PublishTransition(previous, PlayerTransitionCause.SlideRequested);
            return result;
        }

        public FailureCommandResult RequestFailure()
        {
            var machine = StateMachine;
            var result = machine.RequestFailure();
            if (result.Status != CommandStatus.Accepted) return result;

            var previous = state;
            state = machine.CurrentState;
            grounded = machine.IsGrounded;
            ClearMovementTransients();
            PublishTransition(previous, PlayerTransitionCause.FailureRequested);
            return result;
        }

        public ResetRequestResult RequestReset(string requestId)
        {
            var machine = StateMachine;
            var result = machine.RequestReset(requestId);
            if (result.Status != CommandStatus.Accepted) return result;

            acceptedResetRequestIds.Add(requestId);
            var previous = state;
            state = machine.CurrentState;
            resetInProgress = true;
            ClearMovementTransients();
            PublishTransition(previous, PlayerTransitionCause.ResetRequested);
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
            if (poseRestoredInDomain) restoredPosition += LastRealizedDisplacement;
            grounded = SampleGrounded();
        }

        /// <summary>
        /// Restores every player simulation field of <c>Player_Initial_State</c>: the start transform,
        /// Center lanes, the configured Forward_Run_Speed, Baseline_Collider_Profile, the empty lane
        /// queue, and the transient velocity and timers. The reset service owns the sequence around
        /// it; this step publishes nothing and submits no displacement.
        /// </summary>
        internal void RestorePlayerInitialState()
        {
            RestoreStartTransform();
            lanePlanner.Reset(LogicalLane.Center);
            forwardSpeedApi.SetForwardSpeed(configuration.ForwardSpeed);
            verticalVelocity = 0f;
            slideElapsedTime = 0f;
            ApplyColliderProfile(configuration.BaselineCollider);
            grounded = startGrounded;
        }

        /// <summary>
        /// Leaves Resetting for Running, clears in-progress reset status, and publishes the single
        /// <c>ResetCompleted</c> transition. False while the loop is not resetting.
        /// </summary>
        internal bool CompleteReset()
        {
            var machine = StateMachine;
            if (!machine.CompleteReset()) return false;

            var previous = state;
            state = machine.CurrentState;
            resetInProgress = false;
            PublishTransition(previous, PlayerTransitionCause.ResetCompleted);
            return true;
        }

        private void RestoreStartTransform()
        {
            if (poseRestoration != null)
            {
                // The Unity-side motor owns the capsule, so it writes the transform itself and stays
                // the authoritative pose.
                poseRestoration.RestorePose(startPosition, startRotation);
                poseRestoredInDomain = false;
                return;
            }

            poseRestoredInDomain = true;
            restoredPosition = startPosition;
        }

        private void ResolveLanding()
        {
            if (state != PlayerState.Jumping) return;
            if (!NewJumpController().ResolveLanding(grounded)) return;

            var machine = StateMachine;
            machine.ResolveLanding();
            var previous = state;
            state = machine.CurrentState;
            verticalVelocity = 0f;
            PublishTransition(previous, PlayerTransitionCause.Landed);
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
            var previous = state;
            state = machine.CurrentState;
            PublishTransition(previous, PlayerTransitionCause.SlideRestored);
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
            return IsActive(state) ? lanePlanner.LateralPosition - PlayerPosition.x : 0f;
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
            var sampled = motor.SampleGrounded(
                colliderProfile,
                configuration.GroundLayerMask,
                configuration.GroundContactTolerance,
                configuration.GroundNormalThreshold);

            // The first sample of the session is taken at the start pose, before any displacement, so
            // it is the grounded status Player_Initial_State carries and the one reset restores.
            if (!startGroundedSampled)
            {
                startGrounded = sampled;
                startGroundedSampled = true;
            }

            return sampled;
        }

        private Vector3 PlayerPosition
        {
            get { return poseRestoredInDomain ? restoredPosition : motor.Position; }
        }

        private Quaternion PlayerRotation
        {
            get { return poseRestoredInDomain ? startRotation : motor.Rotation; }
        }

        private void PublishTransition(PlayerState previousState, PlayerTransitionCause cause)
        {
            if (eventHub == null || previousState == state) return;

            eventHub.PublishStateChanged(previousState, state, cause);
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
                PlayerPosition,
                PlayerRotation,
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
