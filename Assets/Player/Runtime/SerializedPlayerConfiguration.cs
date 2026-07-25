using System;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Configuration
{
    [Serializable]
    public sealed class SerializedPlayerConfiguration
    {
        [SerializeField, Tooltip("Finite non-negative automatic run speed.")]
        private float forwardSpeed = 8f;
        [SerializeField, Tooltip("Finite positive jump takeoff velocity.")]
        private float jumpVelocity = 7f;
        [SerializeField, Tooltip("Finite positive downward acceleration magnitude.")]
        private float gravityAcceleration = 20f;
        [SerializeField, Tooltip("Finite positive duration for one adjacent lane change.")]
        private float laneChangeDuration = 0.2f;
        [SerializeField, Tooltip("Finite positive minimum slide duration.")]
        private float slideDuration = 0.8f;
        [SerializeField, Tooltip("Non-negative tolerance below half the minimum lane separation.")]
        private float lanePositionTolerance = 0.25f;
        [SerializeField, Tooltip("Strictly increasing left, center, and right X coordinates.")]
        private Vector3 laneCenters = new Vector3(-3f, 0f, 3f);
        [SerializeField] private float baselineRadius = 0.5f;
        [SerializeField] private float baselineHeight = 2f;
        [SerializeField] private Vector3 baselineCenter = new Vector3(0f, 1f, 0f);
        [SerializeField] private float slideRadius = 0.5f;
        [SerializeField] private float slideHeight = 1f;
        [SerializeField] private Vector3 slideCenter = new Vector3(0f, 0.5f, 0f);
        [SerializeField, Tooltip("Non-empty layer mask for running surfaces.")]
        private LayerMask groundLayerMask = 1;
        [SerializeField, Tooltip("Non-empty layer mask for standing-volume obstructions.")]
        private LayerMask obstructionLayerMask = 1;
        [SerializeField] private float groundContactTolerance = 0.1f;
        [SerializeField, Range(0f, 1f)] private float groundNormalThreshold = 0.6f;
        [SerializeField] private Vector3 initialCameraPosition = new Vector3(0f, 5f, -8f);
        [SerializeField] private float cameraSettleDuration = 0.25f;
        [SerializeField] private float cameraFollowTolerance = 0.05f;
        [SerializeField, Range(0f, 1f)] private float inputNeutralThreshold = 0.2f;
        [SerializeField, Range(0f, 1f)] private float inputActuationThreshold = 0.5f;

        public PlayerConfiguration ToImmutable()
        {
            return new PlayerConfiguration(
                forwardSpeed, jumpVelocity, gravityAcceleration, laneChangeDuration,
                slideDuration, lanePositionTolerance, laneCenters,
                new ColliderProfile(baselineRadius, baselineHeight, baselineCenter),
                new ColliderProfile(slideRadius, slideHeight, slideCenter),
                groundLayerMask.value, obstructionLayerMask.value,
                groundContactTolerance, groundNormalThreshold, initialCameraPosition,
                cameraSettleDuration, cameraFollowTolerance,
                new InputThresholds(inputNeutralThreshold, inputActuationThreshold));
        }
    }

    [CreateAssetMenu(fileName = "PlayerConfiguration", menuName = "Subway Surfers/Player Configuration")]
    public sealed class PlayerConfigurationAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Prefab-authored values validated before simulation.")]
        private SerializedPlayerConfiguration configured = new SerializedPlayerConfiguration();
        [SerializeField, Tooltip("Documented field fallbacks. These values must form a valid configuration.")]
        private SerializedPlayerConfiguration safeDefaults = new SerializedPlayerConfiguration();

        public PlayerConfiguration ConfiguredConfiguration
        {
            get { return configured == null ? PlayerConfiguration.SafeDefaults : configured.ToImmutable(); }
        }

        public PlayerConfiguration SafeDefaultConfiguration
        {
            get { return safeDefaults == null ? PlayerConfiguration.SafeDefaults : safeDefaults.ToImmutable(); }
        }
    }
}
