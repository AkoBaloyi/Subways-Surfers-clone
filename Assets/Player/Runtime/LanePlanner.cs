using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public sealed class LanePlanner
    {
        private static readonly Vector3 DefaultLaneCenters = new Vector3(-1f, 0f, 1f);

        private readonly List<LaneRequest> requests;
        private readonly ReadOnlyCollection<LaneRequest> requestView;
        private readonly Vector3 laneCenters;
        private readonly float laneChangeDuration;
        private readonly float lanePositionTolerance;

        private bool segmentActive;
        private float segmentElapsed;

        public LanePlanner(LogicalLane initialLane)
            : this(initialLane, DefaultLaneCenters, 1f, 0f)
        {
        }

        public LanePlanner(
            LogicalLane initialLane,
            Vector3 laneCenters,
            float laneChangeDuration,
            float lanePositionTolerance)
        {
            ValidateLane(initialLane, nameof(initialLane));
            ValidateConfiguration(laneCenters, laneChangeDuration, lanePositionTolerance);

            requests = new List<LaneRequest>();
            requestView = requests.AsReadOnly();
            this.laneCenters = laneCenters;
            this.laneChangeDuration = laneChangeDuration;
            this.lanePositionTolerance = lanePositionTolerance;

            CurrentLane = initialLane;
            TargetLane = initialLane;
            LateralPosition = CenterOf(initialLane);
            LaneSegmentStart = LateralPosition;
            LaneSegmentTarget = LateralPosition;
        }

        public LogicalLane CurrentLane { get; private set; }
        public LogicalLane TargetLane { get; private set; }
        public float LateralPosition { get; private set; }
        public float LaneSegmentStart { get; private set; }
        public float LaneSegmentTarget { get; private set; }
        public float LaneChangeProgress { get; private set; }
        public float LanePositionTolerance { get { return lanePositionTolerance; } }
        public bool IsChangingLane { get { return segmentActive && TargetLane != CurrentLane; } }
        public IReadOnlyList<LaneRequest> LaneRequestQueue { get { return requestView; } }

        public void Enqueue(LaneRequest request)
        {
            ValidateDirection(request.Direction);
            if (requests.Count > 0 &&
                request.RequestOrder <= requests[requests.Count - 1].RequestOrder)
            {
                throw new ArgumentException(
                    "Lane request order must be strictly ascending.",
                    nameof(request));
            }

            requests.Add(request);
        }

        public bool TryEnqueue(LaneRequest request, PlayerState state)
        {
            if (!IsActive(state)) return false;

            Enqueue(request);
            return true;
        }

        public LaneRequest CompleteNextRequest()
        {
            if (requests.Count == 0)
                throw new InvalidOperationException("No lane request is available to complete.");

            if (!segmentActive) BeginNextRequest();
            return CompleteActiveRequest();
        }

        public void Advance(float elapsedSimulationTime)
        {
            ValidateElapsedTime(elapsedSimulationTime);
            if (elapsedSimulationTime == 0f) return;

            var residualTime = elapsedSimulationTime;
            while (requests.Count > 0)
            {
                if (!segmentActive) BeginNextRequest();

                if (TargetLane == CurrentLane)
                {
                    CompleteActiveRequest();
                    continue;
                }

                var remainingTime = laneChangeDuration - segmentElapsed;
                var consumedTime = Mathf.Min(residualTime, remainingTime);
                segmentElapsed = Mathf.Min(laneChangeDuration, segmentElapsed + consumedTime);
                residualTime = Mathf.Max(0f, residualTime - consumedTime);

                LaneChangeProgress = Mathf.Clamp01(segmentElapsed / laneChangeDuration);
                LateralPosition = Mathf.Lerp(
                    LaneSegmentStart,
                    LaneSegmentTarget,
                    LaneChangeProgress);

                if (segmentElapsed >= laneChangeDuration)
                {
                    CompleteActiveRequest();
                    if (residualTime > 0f) continue;
                }

                return;
            }
        }

        public void Advance(float elapsedSimulationTime, PlayerState state)
        {
            ValidateElapsedTime(elapsedSimulationTime);
            if (!IsActive(state)) return;

            Advance(elapsedSimulationTime);
        }

        public void Clear()
        {
            requests.Clear();
            segmentActive = false;
            segmentElapsed = 0f;
            TargetLane = CurrentLane;
            LaneSegmentStart = LateralPosition;
            LaneSegmentTarget = LateralPosition;
            LaneChangeProgress = 0f;
        }

        public void Reset(LogicalLane lane)
        {
            ValidateLane(lane, nameof(lane));
            requests.Clear();
            segmentActive = false;
            segmentElapsed = 0f;
            CurrentLane = lane;
            TargetLane = lane;
            LateralPosition = CenterOf(lane);
            LaneSegmentStart = LateralPosition;
            LaneSegmentTarget = LateralPosition;
            LaneChangeProgress = 0f;
        }

        private void BeginNextRequest()
        {
            var request = requests[0];
            TargetLane = AdjacentOrBoundary(CurrentLane, request.Direction);
            LaneSegmentStart = LateralPosition;
            LaneSegmentTarget = CenterOf(TargetLane);
            LaneChangeProgress = 0f;
            segmentElapsed = 0f;
            segmentActive = true;
        }

        private LaneRequest CompleteActiveRequest()
        {
            var completed = requests[0];
            LateralPosition = LaneSegmentTarget;
            CurrentLane = TargetLane;
            requests.RemoveAt(0);
            segmentActive = false;
            segmentElapsed = 0f;
            LaneSegmentStart = LateralPosition;
            LaneSegmentTarget = LateralPosition;
            LaneChangeProgress = 0f;
            return completed;
        }

        private float CenterOf(LogicalLane lane)
        {
            switch (lane)
            {
                case LogicalLane.Left: return laneCenters.x;
                case LogicalLane.Center: return laneCenters.y;
                case LogicalLane.Right: return laneCenters.z;
                default: throw new ArgumentOutOfRangeException(nameof(lane));
            }
        }

        private static LogicalLane AdjacentOrBoundary(
            LogicalLane current,
            LaneDirection direction)
        {
            if (direction == LaneDirection.Left)
                return current == LogicalLane.Left ? LogicalLane.Left : current - 1;
            if (direction == LaneDirection.Right)
                return current == LogicalLane.Right ? LogicalLane.Right : current + 1;
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        private static bool IsActive(PlayerState state)
        {
            return state == PlayerState.Running ||
                state == PlayerState.Jumping ||
                state == PlayerState.Sliding;
        }

        private static void ValidateConfiguration(
            Vector3 centers,
            float duration,
            float tolerance)
        {
            if (!IsFinite(centers.x) || !IsFinite(centers.y) || !IsFinite(centers.z) ||
                !(centers.x < centers.y && centers.y < centers.z))
            {
                throw new ArgumentException(
                    "Lane centers must be finite and strictly increasing.",
                    nameof(centers));
            }

            if (!IsFinite(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));

            var minimumSeparation = Mathf.Min(
                centers.y - centers.x,
                centers.z - centers.y);
            if (!IsFinite(tolerance) || tolerance < 0f ||
                tolerance >= minimumSeparation * 0.5f)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }
        }

        private static void ValidateElapsedTime(float elapsedSimulationTime)
        {
            if (!IsFinite(elapsedSimulationTime) || elapsedSimulationTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulationTime));
        }

        private static void ValidateDirection(LaneDirection direction)
        {
            if (direction != LaneDirection.Left && direction != LaneDirection.Right)
                throw new ArgumentOutOfRangeException(nameof(direction));
        }

        private static void ValidateLane(LogicalLane lane, string parameterName)
        {
            if (lane != LogicalLane.Left && lane != LogicalLane.Center && lane != LogicalLane.Right)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
