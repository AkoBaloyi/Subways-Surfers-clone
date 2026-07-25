using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;

namespace SubwaySurfers.Player.Domain
{
    public interface IPlayerStateTransitionSource
    {
        event Action<PlayerStateChangedEvent> StateChanged;
    }

    public sealed class PlayerStateMachine :
        IPlayerStateQuery,
        IGroundedStatusQuery,
        IPlayerStateTransitionSource
    {
        private readonly HashSet<string> resetRequestLedger;
        private PlayerSnapshot snapshot;
        private ulong transitionEventSequence;

        public PlayerStateMachine(
            PlayerSnapshot initialSnapshot,
            ImmutableValueSequence<string> completedResetRequestIds)
        {
            snapshot = initialSnapshot;
            resetRequestLedger = new HashSet<string>(StringComparer.Ordinal);
            var ids = completedResetRequestIds ?? ImmutableValueSequence<string>.Empty;
            for (var index = 0; index < ids.Count; index++)
            {
                var requestId = ids[index];
                if (!string.IsNullOrEmpty(requestId)) resetRequestLedger.Add(requestId);
            }
        }

        public event Action<PlayerStateChangedEvent> StateChanged;

        public PlayerSnapshot Snapshot { get { return snapshot; } }
        public PlayerState CurrentState { get { return snapshot.State; } }
        public bool IsGrounded { get { return snapshot.IsGrounded; } }

        public ActionRequestResult RequestLane(LaneDirection direction)
        {
            if (direction != LaneDirection.Left && direction != LaneDirection.Right)
                return ActionResult(PlayerCommandKind.Lane, RejectionReason.InvalidValue);

            return IsActive(CurrentState)
                ? ActionResult(PlayerCommandKind.Lane, RejectionReason.None)
                : ActionResult(PlayerCommandKind.Lane, RejectionReason.InvalidState);
        }
        public ActionRequestResult RequestJump()
        {
            if (CurrentState != PlayerState.Running)
                return ActionResult(PlayerCommandKind.Jump, RejectionReason.InvalidState);
            if (!snapshot.IsGrounded)
                return ActionResult(PlayerCommandKind.Jump, RejectionReason.NotGrounded);

            TransitionTo(PlayerState.Jumping, PlayerTransitionCause.JumpRequested);
            return ActionResult(PlayerCommandKind.Jump, RejectionReason.None);
        }

        public ActionRequestResult RequestSlide()
        {
            if (CurrentState != PlayerState.Running)
                return ActionResult(PlayerCommandKind.Slide, RejectionReason.InvalidState);
            if (!snapshot.IsGrounded)
                return ActionResult(PlayerCommandKind.Slide, RejectionReason.NotGrounded);

            TransitionTo(PlayerState.Sliding, PlayerTransitionCause.SlideRequested);
            return ActionResult(PlayerCommandKind.Slide, RejectionReason.None);
        }

        public FailureCommandResult RequestFailure()
        {
            if (!IsActive(CurrentState))
            {
                return new FailureCommandResult(
                    CommandStatus.Rejected,
                    PlayerCommandKind.Failure,
                    RejectionReason.InvalidState,
                    CurrentState);
            }

            var previous = CurrentState;
            snapshot = Copy(
                PlayerState.Failed,
                false,
                0f,
                ImmutableValueSequence<LaneRequest>.Empty,
                ImmutableValueSequence<PlayerCommandKind>.Empty);
            Publish(previous, PlayerState.Failed, PlayerTransitionCause.FailureRequested);
            return new FailureCommandResult(
                CommandStatus.Accepted,
                PlayerCommandKind.Failure,
                RejectionReason.None,
                CurrentState);
        }
        public ResetRequestResult RequestReset(string requestId)
        {
            if (CurrentState == PlayerState.Resetting)
                return ResetResult(RejectionReason.ResetInProgress, requestId);
            if (string.IsNullOrEmpty(requestId))
                return ResetResult(RejectionReason.MissingRequestId, requestId);
            if (resetRequestLedger.Contains(requestId))
                return ResetResult(RejectionReason.DuplicateRequestId, requestId);

            resetRequestLedger.Add(requestId);
            TransitionTo(
                PlayerState.Resetting,
                PlayerTransitionCause.ResetRequested,
                true);
            return ResetResult(RejectionReason.None, requestId);
        }

        public bool ResolveLanding()
        {
            if (CurrentState != PlayerState.Jumping ||
                !snapshot.IsGrounded ||
                snapshot.VerticalVelocity > 0f)
            {
                return false;
            }

            TransitionTo(PlayerState.Running, PlayerTransitionCause.Landed);
            return true;
        }

        public bool ResolveSlideRestoration(bool safeRestoration)
        {
            if (CurrentState != PlayerState.Sliding || !safeRestoration) return false;

            TransitionTo(PlayerState.Running, PlayerTransitionCause.SlideRestored);
            return true;
        }

        public bool CompleteReset()
        {
            if (CurrentState != PlayerState.Resetting) return false;

            TransitionTo(
                PlayerState.Running,
                PlayerTransitionCause.ResetCompleted,
                false);
            return true;
        }

        private ActionRequestResult ActionResult(
            PlayerCommandKind command,
            RejectionReason reason)
        {
            return new ActionRequestResult(
                reason == RejectionReason.None
                    ? CommandStatus.Accepted
                    : CommandStatus.Rejected,
                command,
                reason,
                CurrentState);
        }

        private ResetRequestResult ResetResult(
            RejectionReason reason,
            string requestId)
        {
            return new ResetRequestResult(
                reason == RejectionReason.None
                    ? CommandStatus.Accepted
                    : CommandStatus.Rejected,
                PlayerCommandKind.Reset,
                reason,
                CurrentState,
                requestId);
        }
        private void TransitionTo(
            PlayerState nextState,
            PlayerTransitionCause cause,
            bool? resetInProgress = null)
        {
            var previous = CurrentState;
            snapshot = Copy(
                nextState,
                resetInProgress ?? snapshot.ResetInProgress,
                snapshot.VerticalVelocity,
                snapshot.LaneRequestQueue,
                snapshot.PendingActionRequests);
            Publish(previous, nextState, cause);
        }

        private PlayerSnapshot Copy(
            PlayerState state,
            bool resetInProgress,
            float verticalVelocity,
            ImmutableValueSequence<LaneRequest> laneRequests,
            ImmutableValueSequence<PlayerCommandKind> pendingActions)
        {
            return new PlayerSnapshot(
                snapshot.Position,
                snapshot.Rotation,
                state,
                snapshot.IsGrounded,
                snapshot.ForwardSpeed,
                snapshot.CurrentLane,
                snapshot.TargetLane,
                snapshot.LateralPosition,
                snapshot.LaneSegmentStart,
                snapshot.LaneSegmentTarget,
                snapshot.LaneChangeProgress,
                verticalVelocity,
                snapshot.SlideElapsedTime,
                snapshot.ColliderProfile,
                laneRequests,
                pendingActions,
                resetInProgress);
        }

        private void Publish(
            PlayerState previousState,
            PlayerState currentState,
            PlayerTransitionCause cause)
        {
            transitionEventSequence++;
            var handler = StateChanged;
            if (handler == null) return;

            var transition = new PlayerStateChangedEvent(
                transitionEventSequence,
                previousState,
                currentState,
                cause);
            foreach (Action<PlayerStateChangedEvent> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(transition);
                }
                catch
                {
                    // State mutation is authoritative; integration diagnostics are owned by the event hub.
                }
            }
        }

        private static bool IsActive(PlayerState state)
        {
            return state == PlayerState.Running ||
                state == PlayerState.Jumping ||
                state == PlayerState.Sliding;
        }
    }
}
