using System;
using System.Globalization;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Integration
{
    /// <summary>
    /// Owns the pace of a run: the player accelerates as the score multiplies, and collected stars brake
    /// them temporarily. Surviving is the goal, and it gets harder because the floor the brake can reach
    /// rises with every tier.
    ///
    /// The loop is deliberately self-limiting. Score is distance, so it accrues at the current speed:
    /// braking lowers the speed now and also slows how fast the next tier arrives, which is why braking
    /// is worth doing. But the brake wears off, and each tier lifts the minimum speed, so no amount of
    /// collecting holds the run open forever.
    ///
    /// This drives the coordinator's <c>gameSpeed</c> only. The bridge pushes that value through the
    /// player's validated <c>SetForwardSpeed</c>, so the player's own rules still decide what is
    /// acceptable and nothing here writes player state directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunPaceController : MonoBehaviour
    {
        /// <summary>
        /// One difficulty tier. The score threshold is where the tier begins, and the speeds are the
        /// ceiling natural acceleration climbs to and the floor braking can pull down to.
        /// </summary>
        [Serializable]
        public struct PaceTier
        {
            [Tooltip("Score at which this tier begins. Thresholds multiply, so each tier takes " +
                     "noticeably longer to reach than the last.")]
            public float scoreThreshold;

            [Tooltip("Fastest the player can go in this tier. Natural acceleration plateaus here until " +
                     "the next tier unlocks.")]
            public float maximumSpeed;

            [Tooltip("Slowest the player can be brought in this tier, however many stars are collected. " +
                     "This rising floor is what makes a long run eventually impossible.")]
            public float minimumSpeed;
        }

        [Header("Player")]
        [SerializeField]
        [Tooltip("Player whose star pickups feed the brake. Serialized as the concrete facade because " +
                 "interface-typed fields are never serialized.")]
        private PlayerControllerFacade player;

        [Header("Acceleration")]
        [SerializeField]
        [Tooltip("Speed at the very start of a run, before any score has accrued.")]
        private float startSpeed = 12f;

        [Header("Braking")]
        [SerializeField]
        [Tooltip("Speed removed per star collected. Stars stack, so a cluster brakes harder.")]
        private float slowPerStar = 6f;

        [SerializeField]
        [Tooltip("How fast the brake wears off, in speed per second. At 1.5 a single star buys about " +
                 "four seconds of relief.")]
        private float brakeDecayPerSecond = 1.5f;

        [SerializeField]
        [Tooltip("Largest brake that can accumulate, so a hoard of stars cannot bank unlimited relief.")]
        private float maximumBrake = 30f;

        [Header("Tiers")]
        [SerializeField]
        [Tooltip("Difficulty tiers in ascending score order. Both the ceiling and the floor rise, so " +
                 "the run gets faster and braking buys less.")]
        private PaceTier[] tiers =
        {
            new PaceTier { scoreThreshold = 0f,     maximumSpeed = 30f, minimumSpeed = 12f },
            new PaceTier { scoreThreshold = 250f,   maximumSpeed = 45f, minimumSpeed = 18f },
            new PaceTier { scoreThreshold = 750f,   maximumSpeed = 50f, minimumSpeed = 20f },
            new PaceTier { scoreThreshold = 2250f,  maximumSpeed = 60f, minimumSpeed = 24f },
            new PaceTier { scoreThreshold = 6750f,  maximumSpeed = 75f, minimumSpeed = 30f },
            new PaceTier { scoreThreshold = 20250f, maximumSpeed = 95f, minimumSpeed = 40f }
        };

        [Header("Diagnostics")]
        [SerializeField] private bool logTierChanges = true;

        private IPlayerEventSource events;
        private float brake;
        private int reportedTier = -1;

        /// <summary>Current brake from recently collected stars, in speed units.</summary>
        public float Brake { get { return brake; } }

        /// <summary>Zero-based index of the tier the current score falls in.</summary>
        public int TierIndex { get; private set; }

        /// <summary>Speed the run would be at with no braking.</summary>
        public float NaturalSpeed { get; private set; }

        /// <summary>Speed floor for the current tier: the best braking can achieve right now.</summary>
        public float FloorSpeed { get; private set; }

        private void OnEnable()
        {
            if (player == null) return;

            events = player.Events;
            if (events != null) events.CoinCollected += OnStarCollected;
        }

        private void OnDisable()
        {
            if (events != null) events.CoinCollected -= OnStarCollected;
            events = null;
        }

        private void Update()
        {
            var manager = GameManager.Instance;
            if (manager == null) return;

            // The coordinator's own timed ramp is switched off here rather than in its file, so this
            // component is the single writer of gameSpeed and the two cannot fight over it.
            manager.speedIncreaseRate = 0f;

            if (manager.currentState != GameManager.GameState.Playing)
            {
                brake = 0f;
                return;
            }

            var elapsed = Time.deltaTime;
            if (elapsed > 0f && IsFinite(elapsed))
            {
                brake = Mathf.Max(0f, brake - brakeDecayPerSecond * elapsed);
            }

            var tier = ResolveTier(manager.score);
            TierIndex = tier;
            var ceiling = tiers[tier].maximumSpeed;
            FloorSpeed = Mathf.Min(tiers[tier].minimumSpeed, ceiling);
            NaturalSpeed = ResolveNaturalSpeed(manager.score, tier);

            var effective = Mathf.Clamp(NaturalSpeed - brake, FloorSpeed, ceiling);
            manager.gameSpeed = effective;

            if (logTierChanges && tier != reportedTier)
            {
                reportedTier = tier;
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "Pace tier {0} at score {1:F0}: speed {2:F1}, ceiling {3:F1}, floor {4:F1}. " +
                    "Stars now brake down to {4:F1} at best.",
                    tier + 1, manager.score, effective, ceiling, FloorSpeed), this);
            }
        }

        /// <summary>
        /// Stars brake instead of scoring. The player reports the pickup and its value and never applies
        /// it, so translating a star into pace is owned here.
        /// </summary>
        private void OnStarCollected(CoinCollectedEvent collected)
        {
            var value = collected.CollectibleValue;
            if (!IsFinite(value) || value <= 0f) value = 1f;

            brake = Mathf.Min(maximumBrake, brake + slowPerStar * value);
        }

        /// <summary>
        /// Natural speed climbs smoothly across the tier, from the previous tier's ceiling to this
        /// tier's ceiling, reaching each ceiling exactly as the next threshold is crossed. The tier
        /// ceilings are therefore real waypoints on one continuous acceleration curve rather than caps
        /// the run may never approach. The final tier holds its ceiling.
        /// </summary>
        private float ResolveNaturalSpeed(float score, int tier)
        {
            var from = tier == 0 ? startSpeed : tiers[tier - 1].maximumSpeed;
            var to = tiers[tier].maximumSpeed;

            if (tier >= tiers.Length - 1) return to;

            var spanStart = tiers[tier].scoreThreshold;
            var spanEnd = tiers[tier + 1].scoreThreshold;
            if (spanEnd <= spanStart) return to;

            var progress = Mathf.Clamp01((score - spanStart) / (spanEnd - spanStart));
            return Mathf.Lerp(from, to, progress);
        }

        private int ResolveTier(float score)
        {
            if (tiers == null || tiers.Length == 0) return 0;

            var index = 0;
            for (var candidate = 0; candidate < tiers.Length; candidate++)
            {
                if (score >= tiers[candidate].scoreThreshold) index = candidate;
            }

            return index;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
