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
        [SerializeField, Tooltip("Finite positive standing capsule radius. Fallback 0.5.")]
        private float baselineRadius = 0.5f;
        [SerializeField, Tooltip("Finite positive standing capsule height, at least twice the baseline radius. Fallback 2.")]
        private float baselineHeight = 2f;
        [SerializeField, Tooltip("Finite standing capsule center in player-local space, lifting the capsule base to the pivot. Fallback (0, 1, 0).")]
        private Vector3 baselineCenter = new Vector3(0f, 1f, 0f);
        [SerializeField, Tooltip("Finite positive slide capsule radius, no wider than the baseline radius. Fallback 0.5.")]
        private float slideRadius = 0.5f;
        [SerializeField, Tooltip("Finite positive slide capsule height, at least twice the slide radius and shorter than the baseline height. Fallback 1.")]
        private float slideHeight = 1f;
        [SerializeField, Tooltip("Finite slide capsule center, keeping the slide capsule inside the baseline capsule. Fallback derived from the effective baseline capsule, (0, 0.5, 0) against the baseline fallback.")]
        private Vector3 slideCenter = new Vector3(0f, 0.5f, 0f);
        [SerializeField, Tooltip("Non-empty layer mask for running surfaces.")]
        private LayerMask groundLayerMask = 1;
        [SerializeField, Tooltip("Non-empty layer mask for standing-volume obstructions.")]
        private LayerMask obstructionLayerMask = 1;
        [SerializeField, Tooltip("Finite non-negative distance a running-surface contact may sit below the capsule base and still count. Fallback 0.1.")]
        private float groundContactTolerance = 0.1f;
        [SerializeField, Range(0f, 1f), Tooltip("Minimum upward component of a contact normal that can ground the player. Fallback 0.6.")]
        private float groundNormalThreshold = 0.6f;
        [SerializeField, Tooltip("Finite world-space camera pose at the start position; the follow offset is derived from it rather than serialized. Fallback (0, 5, -8).")]
        private Vector3 initialCameraPosition = new Vector3(0f, 5f, -8f);
        [SerializeField, Tooltip("Finite positive time within which the camera reaches a fixed target. Fallback 0.25.")]
        private float cameraSettleDuration = 0.25f;
        [SerializeField, Tooltip("Finite non-negative distance at which the camera counts as settled on its target. Fallback 0.05.")]
        private float cameraFollowTolerance = 0.05f;
        [SerializeField, Range(0f, 1f), Tooltip("Axis magnitude at or below which the lane control is rearmed. Fallback 0.2.")]
        private float inputNeutralThreshold = 0.2f;
        [SerializeField, Range(0f, 1f), Tooltip("Axis magnitude that issues one lane request; must exceed the neutral threshold. Fallback 0.5.")]
        private float inputActuationThreshold = 0.5f;

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
}
