using System;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public readonly struct ColliderProfile : IEquatable<ColliderProfile>
    {
        public ColliderProfile(float radius, float height, Vector3 center)
        {
            Radius = radius;
            Height = height;
            Center = center;
        }

        public float Radius { get; }
        public float Height { get; }
        public Vector3 Center { get; }

        public bool Equals(ColliderProfile other)
        {
            return Radius.Equals(other.Radius) && Height.Equals(other.Height) && Center.Equals(other.Center);
        }

        public override bool Equals(object obj) { return obj is ColliderProfile other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Radius.GetHashCode() * 397) ^ Height.GetHashCode()) * 397 ^ Center.GetHashCode();
            }
        }

        public static bool operator ==(ColliderProfile left, ColliderProfile right) { return left.Equals(right); }
        public static bool operator !=(ColliderProfile left, ColliderProfile right) { return !left.Equals(right); }
    }

    public readonly struct InputThresholds : IEquatable<InputThresholds>
    {
        public InputThresholds(float neutralThreshold, float actuationThreshold)
        {
            NeutralThreshold = neutralThreshold;
            ActuationThreshold = actuationThreshold;
        }

        public float NeutralThreshold { get; }
        public float ActuationThreshold { get; }

        public bool Equals(InputThresholds other)
        {
            return NeutralThreshold.Equals(other.NeutralThreshold) &&
                   ActuationThreshold.Equals(other.ActuationThreshold);
        }

        public override bool Equals(object obj) { return obj is InputThresholds other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked { return (NeutralThreshold.GetHashCode() * 397) ^ ActuationThreshold.GetHashCode(); }
        }

        public static bool operator ==(InputThresholds left, InputThresholds right) { return left.Equals(right); }
        public static bool operator !=(InputThresholds left, InputThresholds right) { return !left.Equals(right); }
    }

    public readonly struct PlayerConfiguration : IEquatable<PlayerConfiguration>
    {
        public PlayerConfiguration(float forwardSpeed, float jumpVelocity,
            float gravityAcceleration, float laneChangeDuration, float slideDuration,
            float lanePositionTolerance, Vector3 laneCenters,
            ColliderProfile baselineCollider, ColliderProfile slideCollider,
            int groundLayerMask, int obstructionLayerMask, float groundContactTolerance,
            float groundNormalThreshold, Vector3 initialCameraPosition,
            float cameraSettleDuration, float cameraFollowTolerance,
            InputThresholds inputThresholds)
        {
            ForwardSpeed = forwardSpeed;
            JumpVelocity = jumpVelocity;
            GravityAcceleration = gravityAcceleration;
            LaneChangeDuration = laneChangeDuration;
            SlideDuration = slideDuration;
            LanePositionTolerance = lanePositionTolerance;
            LaneCenters = laneCenters;
            BaselineCollider = baselineCollider;
            SlideCollider = slideCollider;
            GroundLayerMask = groundLayerMask;
            ObstructionLayerMask = obstructionLayerMask;
            GroundContactTolerance = groundContactTolerance;
            GroundNormalThreshold = groundNormalThreshold;
            InitialCameraPosition = initialCameraPosition;
            CameraSettleDuration = cameraSettleDuration;
            CameraFollowTolerance = cameraFollowTolerance;
            InputThresholds = inputThresholds;
        }

        public float ForwardSpeed { get; }
        public float JumpVelocity { get; }
        public float GravityAcceleration { get; }
        public float LaneChangeDuration { get; }
        public float SlideDuration { get; }
        public float LanePositionTolerance { get; }
        public Vector3 LaneCenters { get; }
        public ColliderProfile BaselineCollider { get; }
        public ColliderProfile SlideCollider { get; }
        public int GroundLayerMask { get; }
        public int ObstructionLayerMask { get; }
        public float GroundContactTolerance { get; }
        public float GroundNormalThreshold { get; }
        public Vector3 InitialCameraPosition { get; }
        public float CameraSettleDuration { get; }
        public float CameraFollowTolerance { get; }
        public InputThresholds InputThresholds { get; }

        public bool Equals(PlayerConfiguration other)
        {
            return ForwardSpeed.Equals(other.ForwardSpeed) &&
                   JumpVelocity.Equals(other.JumpVelocity) &&
                   GravityAcceleration.Equals(other.GravityAcceleration) &&
                   LaneChangeDuration.Equals(other.LaneChangeDuration) &&
                   SlideDuration.Equals(other.SlideDuration) &&
                   LanePositionTolerance.Equals(other.LanePositionTolerance) &&
                   LaneCenters.Equals(other.LaneCenters) &&
                   BaselineCollider.Equals(other.BaselineCollider) &&
                   SlideCollider.Equals(other.SlideCollider) &&
                   GroundLayerMask == other.GroundLayerMask &&
                   ObstructionLayerMask == other.ObstructionLayerMask &&
                   GroundContactTolerance.Equals(other.GroundContactTolerance) &&
                   GroundNormalThreshold.Equals(other.GroundNormalThreshold) &&
                   InitialCameraPosition.Equals(other.InitialCameraPosition) &&
                   CameraSettleDuration.Equals(other.CameraSettleDuration) &&
                   CameraFollowTolerance.Equals(other.CameraFollowTolerance) &&
                   InputThresholds.Equals(other.InputThresholds);
        }

        public override bool Equals(object obj) { return obj is PlayerConfiguration other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ForwardSpeed.GetHashCode();
                hash = (hash * 397) ^ JumpVelocity.GetHashCode();
                hash = (hash * 397) ^ GravityAcceleration.GetHashCode();
                hash = (hash * 397) ^ LaneChangeDuration.GetHashCode();
                hash = (hash * 397) ^ SlideDuration.GetHashCode();
                hash = (hash * 397) ^ LanePositionTolerance.GetHashCode();
                hash = (hash * 397) ^ LaneCenters.GetHashCode();
                hash = (hash * 397) ^ BaselineCollider.GetHashCode();
                hash = (hash * 397) ^ SlideCollider.GetHashCode();
                hash = (hash * 397) ^ GroundLayerMask;
                hash = (hash * 397) ^ ObstructionLayerMask;
                hash = (hash * 397) ^ GroundContactTolerance.GetHashCode();
                hash = (hash * 397) ^ GroundNormalThreshold.GetHashCode();
                hash = (hash * 397) ^ InitialCameraPosition.GetHashCode();
                hash = (hash * 397) ^ CameraSettleDuration.GetHashCode();
                hash = (hash * 397) ^ CameraFollowTolerance.GetHashCode();
                return (hash * 397) ^ InputThresholds.GetHashCode();
            }
        }

        public static PlayerConfiguration SafeDefaults
        {
            get
            {
                return new PlayerConfiguration(
                    8f, 7f, 20f, 0.2f, 0.8f, 0.25f,
                    new Vector3(-3f, 0f, 3f),
                    new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                    new ColliderProfile(0.5f, 1f, new Vector3(0f, 0.5f, 0f)),
                    1, 1, 0.1f, 0.6f, new Vector3(0f, 5f, -8f),
                    0.25f, 0.05f, new InputThresholds(0.2f, 0.5f));
            }
        }

        public static bool operator ==(PlayerConfiguration left, PlayerConfiguration right) { return left.Equals(right); }
        public static bool operator !=(PlayerConfiguration left, PlayerConfiguration right) { return !left.Equals(right); }
    }
}
