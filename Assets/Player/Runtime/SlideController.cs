using System;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public sealed class SlideController
    {
        private const float SinglePrecisionMachineEpsilon = 1.1920929e-7f;
        private const float ContainmentRoundoffUnits = 8f;

        private readonly float slideDuration;
        private readonly ColliderProfile baselineProfile;
        private readonly ColliderProfile slideProfile;
        private PlayerSnapshot snapshot;

        public SlideController(
            PlayerSnapshot initialSnapshot,
            float slideDuration,
            ColliderProfile baselineProfile,
            ColliderProfile slideProfile)
        {
            ValidatePositiveFinite(slideDuration, nameof(slideDuration));
            ValidateProfile(baselineProfile, nameof(baselineProfile));
            ValidateProfile(slideProfile, nameof(slideProfile));
            if (!FitsInside(slideProfile, baselineProfile))
                throw new ArgumentOutOfRangeException(nameof(slideProfile));

            snapshot = initialSnapshot;
            this.slideDuration = slideDuration;
            this.baselineProfile = baselineProfile;
            this.slideProfile = slideProfile;
        }

        public PlayerSnapshot Snapshot { get { return snapshot; } }

        public ActionRequestResult RequestSlide()
        {
            if (snapshot.State != PlayerState.Running)
                return Result(RejectionReason.InvalidState);
            if (!snapshot.IsGrounded)
                return Result(RejectionReason.NotGrounded);

            snapshot = Copy(PlayerState.Sliding, 0f, slideProfile);
            return Result(RejectionReason.None);
        }
        public void Advance(float elapsedSimulationTime, Func<bool> safeColliderRestoration)
        {
            ValidateElapsedTime(elapsedSimulationTime);
            if (safeColliderRestoration == null)
                throw new ArgumentNullException(nameof(safeColliderRestoration));
            if (snapshot.State != PlayerState.Sliding)
                return;

            var elapsed = snapshot.SlideElapsedTime + elapsedSimulationTime;
            snapshot = Copy(PlayerState.Sliding, elapsed, snapshot.ColliderProfile);
            if (elapsed < slideDuration || !safeColliderRestoration())
                return;

            snapshot = Copy(PlayerState.Running, elapsed, baselineProfile);
        }

        private ActionRequestResult Result(RejectionReason reason)
        {
            return new ActionRequestResult(
                reason == RejectionReason.None
                    ? CommandStatus.Accepted
                    : CommandStatus.Rejected,
                PlayerCommandKind.Slide,
                reason,
                snapshot.State);
        }

        private PlayerSnapshot Copy(
            PlayerState state,
            float slideElapsedTime,
            ColliderProfile colliderProfile)
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
                snapshot.VerticalVelocity,
                slideElapsedTime,
                colliderProfile,
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

        private static void ValidateProfile(ColliderProfile profile, string parameterName)
        {
            if (!IsFinite(profile.Radius) ||
                !IsFinite(profile.Height) ||
                !IsFinite(profile.Center) ||
                profile.Radius <= 0f ||
                profile.Height < 2f * profile.Radius)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool FitsInside(ColliderProfile inner, ColliderProfile outer)
        {
            var horizontalOffset = new Vector2(
                inner.Center.x - outer.Center.x,
                inner.Center.z - outer.Center.z).magnitude;
            var innerHorizontalExtent = horizontalOffset + inner.Radius;
            if (!IsFinite(innerHorizontalExtent))
                return false;

            var horizontalTolerance = ScaleAwareTolerance(
                innerHorizontalExtent,
                outer.Radius,
                horizontalOffset,
                inner.Radius,
                outer.Radius);
            if (innerHorizontalExtent > outer.Radius + horizontalTolerance)
                return false;

            var innerBottom = inner.Center.y - inner.Height * 0.5f;
            var innerTop = inner.Center.y + inner.Height * 0.5f;
            var outerBottom = outer.Center.y - outer.Height * 0.5f;
            var outerTop = outer.Center.y + outer.Height * 0.5f;
            if (!IsFinite(innerBottom) || !IsFinite(innerTop) ||
                !IsFinite(outerBottom) || !IsFinite(outerTop))
            {
                return false;
            }

            var verticalTolerance = ScaleAwareTolerance(
                innerBottom,
                innerTop,
                outerBottom,
                outerTop,
                Mathf.Max(inner.Height, outer.Height));
            return innerBottom >= outerBottom - verticalTolerance &&
                innerTop <= outerTop + verticalTolerance;
        }

        private static float ScaleAwareTolerance(
            float first,
            float second,
            float third,
            float fourth,
            float fifth)
        {
            // Eight single-precision roundoff units cover the short extent-calculation
            // chain. Scaling by the compared profile values keeps this a numerical
            // tolerance rather than gameplay clearance; one is the sub-unit scale floor.
            var scale = Mathf.Max(1f, Mathf.Abs(first));
            scale = Mathf.Max(scale, Mathf.Abs(second));
            scale = Mathf.Max(scale, Mathf.Abs(third));
            scale = Mathf.Max(scale, Mathf.Abs(fourth));
            scale = Mathf.Max(scale, Mathf.Abs(fifth));
            return scale * SinglePrecisionMachineEpsilon * ContainmentRoundoffUnits;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
