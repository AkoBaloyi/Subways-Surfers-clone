using System;

namespace SubwaySurfers.Player.Domain
{
    public sealed class JumpController
    {
        private readonly float jumpVelocity;
        private readonly float gravityAcceleration;
        private PlayerSnapshot snapshot;

        public JumpController(
            PlayerSnapshot initialSnapshot,
            float jumpVelocity,
            float gravityAcceleration)
        {
            ValidatePositiveFinite(jumpVelocity, nameof(jumpVelocity));
            ValidatePositiveFinite(gravityAcceleration, nameof(gravityAcceleration));

            snapshot = initialSnapshot;
            this.jumpVelocity = jumpVelocity;
            this.gravityAcceleration = gravityAcceleration;
        }

        public PlayerSnapshot Snapshot { get { return snapshot; } }

        public ActionRequestResult RequestJump()
        {
            if (snapshot.State != PlayerState.Running)
                return Result(RejectionReason.InvalidState);
            if (!snapshot.IsGrounded)
                return Result(RejectionReason.NotGrounded);

            snapshot = Copy(PlayerState.Jumping, jumpVelocity);
            return Result(RejectionReason.None);
        }

        public void Advance(float elapsedSimulationTime)
        {
            ValidateElapsedTime(elapsedSimulationTime);
            if (snapshot.State != PlayerState.Jumping || elapsedSimulationTime == 0f)
                return;

            snapshot = Copy(
                snapshot.State,
                snapshot.VerticalVelocity - gravityAcceleration * elapsedSimulationTime);
        }

        public bool ResolveLanding(bool grounded)
        {
            if (snapshot.State != PlayerState.Jumping ||
                !grounded ||
                snapshot.VerticalVelocity > 0f)
            {
                return false;
            }

            snapshot = Copy(PlayerState.Running, snapshot.VerticalVelocity);
            return true;
        }

        private ActionRequestResult Result(RejectionReason reason)
        {
            return new ActionRequestResult(
                reason == RejectionReason.None
                    ? CommandStatus.Accepted
                    : CommandStatus.Rejected,
                PlayerCommandKind.Jump,
                reason,
                snapshot.State);
        }

        private PlayerSnapshot Copy(PlayerState state, float verticalVelocity)
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
                snapshot.LaneRequestQueue,
                snapshot.PendingActionRequests,
                snapshot.ResetInProgress);
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateElapsedTime(float elapsedSimulationTime)
        {
            if (!IsFinite(elapsedSimulationTime) || elapsedSimulationTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulationTime));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
