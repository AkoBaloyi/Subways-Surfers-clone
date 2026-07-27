using System;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public sealed class CameraConvergenceState
    {
        private readonly float cameraSettleDuration;
        private readonly float cameraFollowTolerance;

        public CameraConvergenceState(
            Vector3 initialPlayerPosition,
            Vector3 initialCameraPosition,
            float cameraSettleDuration,
            float cameraFollowTolerance)
        {
            ValidatePositiveFinite(cameraSettleDuration, nameof(cameraSettleDuration));
            ValidateNonNegativeFinite(cameraFollowTolerance, nameof(cameraFollowTolerance));

            this.cameraSettleDuration = cameraSettleDuration;
            this.cameraFollowTolerance = cameraFollowTolerance;
            Reset(initialPlayerPosition, initialCameraPosition);
        }

        public Vector3 CameraPosition { get; private set; }
        public Vector3 CameraFollowOffset { get; private set; }
        public Vector3 PreviousCameraTarget { get; private set; }
        public Vector3 CameraConvergenceVelocity { get; private set; }
        public float RemainingCameraSettleTime { get; private set; }
        public bool HasCameraTarget { get; private set; }
        public float CameraSettleDuration { get { return cameraSettleDuration; } }
        public float CameraFollowTolerance { get { return cameraFollowTolerance; } }

        public void Advance(Vector3 playerPosition, float elapsedCameraFollowTime)
        {
            ValidateFinite(playerPosition, nameof(playerPosition));
            ValidateElapsedTime(elapsedCameraFollowTime);

            var target = playerPosition + CameraFollowOffset;
            if (!IsFinite(target))
                throw new ArgumentOutOfRangeException(nameof(playerPosition));

            if (!HasCameraTarget || !target.Equals(PreviousCameraTarget))
            {
                PreviousCameraTarget = target;
                RemainingCameraSettleTime = cameraSettleDuration;
                HasCameraTarget = true;
            }

            if (elapsedCameraFollowTime == 0f)
            {
                CameraConvergenceVelocity = Vector3.zero;
                return;
            }

            var before = CameraPosition;
            if (before == target || RemainingCameraSettleTime <= elapsedCameraFollowTime)
            {
                CameraPosition = target;
                RemainingCameraSettleTime = 0f;
            }
            else
            {
                var fraction = Mathf.Clamp01(
                    elapsedCameraFollowTime / RemainingCameraSettleTime);
                CameraPosition = Vector3.Lerp(before, target, fraction);
                RemainingCameraSettleTime = Mathf.Max(
                    0f,
                    RemainingCameraSettleTime - elapsedCameraFollowTime);
            }

            CameraConvergenceVelocity =
                (CameraPosition - before) / elapsedCameraFollowTime;
        }

        public void Reset(Vector3 initialPlayerPosition, Vector3 initialCameraPosition)
        {
            ValidateFinite(initialPlayerPosition, nameof(initialPlayerPosition));
            ValidateFinite(initialCameraPosition, nameof(initialCameraPosition));

            var followOffset = initialCameraPosition - initialPlayerPosition;
            if (!IsFinite(followOffset))
                throw new ArgumentOutOfRangeException(nameof(initialCameraPosition));

            CameraPosition = initialCameraPosition;
            CameraFollowOffset = followOffset;
            PreviousCameraTarget = initialCameraPosition;
            CameraConvergenceVelocity = Vector3.zero;
            RemainingCameraSettleTime = 0f;
            HasCameraTarget = false;
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateElapsedTime(float elapsedCameraFollowTime)
        {
            if (!IsFinite(elapsedCameraFollowTime) || elapsedCameraFollowTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedCameraFollowTime));
        }

        private static void ValidateFinite(Vector3 value, string parameterName)
        {
            if (!IsFinite(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}