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
            unchecked { return ((Radius.GetHashCode() * 397) ^ Height.GetHashCode()) * 397 ^ Center.GetHashCode(); }
        }
        public static bool operator ==(ColliderProfile left, ColliderProfile right) { return left.Equals(right); }
        public static bool operator !=(ColliderProfile left, ColliderProfile right) { return !left.Equals(right); }
    }

    public readonly struct LogicalContactIdentity : IEquatable<LogicalContactIdentity>
    {
        public LogicalContactIdentity(ulong contactId, string environmentObjectId, EnvironmentObjectKind kind)
        {
            ContactId = contactId;
            EnvironmentObjectId = environmentObjectId;
            Kind = kind;
        }

        public ulong ContactId { get; }
        public string EnvironmentObjectId { get; }
        public EnvironmentObjectKind Kind { get; }

        public bool Equals(LogicalContactIdentity other)
        {
            return ContactId == other.ContactId && Kind == other.Kind &&
                   string.Equals(EnvironmentObjectId, other.EnvironmentObjectId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is LogicalContactIdentity other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                return ((ContactId.GetHashCode() * 397) ^ (EnvironmentObjectId == null ? 0 :
                    StringComparer.Ordinal.GetHashCode(EnvironmentObjectId))) * 397 ^ (int)Kind;
            }
        }
        public static bool operator ==(LogicalContactIdentity left, LogicalContactIdentity right) { return left.Equals(right); }
        public static bool operator !=(LogicalContactIdentity left, LogicalContactIdentity right) { return !left.Equals(right); }
    }

    public readonly struct ContactEventIdentity : IEquatable<ContactEventIdentity>
    {
        public ContactEventIdentity(ulong contactId, EnvironmentObjectKind kind)
        {
            ContactId = contactId;
            Kind = kind;
        }

        public ulong ContactId { get; }
        public EnvironmentObjectKind Kind { get; }

        public bool Equals(ContactEventIdentity other)
        {
            return ContactId == other.ContactId && Kind == other.Kind;
        }

        public override bool Equals(object obj) { return obj is ContactEventIdentity other && Equals(other); }
        public override int GetHashCode() { return (ContactId.GetHashCode() * 397) ^ (int)Kind; }
        public static bool operator ==(ContactEventIdentity left, ContactEventIdentity right) { return left.Equals(right); }
        public static bool operator !=(ContactEventIdentity left, ContactEventIdentity right) { return !left.Equals(right); }
    }
    public readonly struct PlayerSnapshot : IEquatable<PlayerSnapshot>
    {
        public PlayerSnapshot(Vector3 position, Quaternion rotation, PlayerState state, bool isGrounded,
            float forwardSpeed, LogicalLane currentLane, LogicalLane targetLane, float lateralPosition,
            float laneSegmentStart, float laneSegmentTarget, float laneChangeProgress,
            float verticalVelocity, float slideElapsedTime, ColliderProfile colliderProfile,
            ImmutableValueSequence<LaneRequest> laneRequestQueue,
            ImmutableValueSequence<PlayerCommandKind> pendingActionRequests, bool resetInProgress)
        {
            Position = position;
            Rotation = rotation;
            State = state;
            IsGrounded = isGrounded;
            ForwardSpeed = forwardSpeed;
            CurrentLane = currentLane;
            TargetLane = targetLane;
            LateralPosition = lateralPosition;
            LaneSegmentStart = laneSegmentStart;
            LaneSegmentTarget = laneSegmentTarget;
            LaneChangeProgress = laneChangeProgress;
            VerticalVelocity = verticalVelocity;
            SlideElapsedTime = slideElapsedTime;
            ColliderProfile = colliderProfile;
            LaneRequestQueue = laneRequestQueue ?? ImmutableValueSequence<LaneRequest>.Empty;
            PendingActionRequests = pendingActionRequests ?? ImmutableValueSequence<PlayerCommandKind>.Empty;
            ResetInProgress = resetInProgress;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public PlayerState State { get; }
        public bool IsGrounded { get; }
        public float ForwardSpeed { get; }
        public LogicalLane CurrentLane { get; }
        public LogicalLane TargetLane { get; }
        public float LateralPosition { get; }
        public float LaneSegmentStart { get; }
        public float LaneSegmentTarget { get; }
        public float LaneChangeProgress { get; }
        public float VerticalVelocity { get; }
        public float SlideElapsedTime { get; }
        public ColliderProfile ColliderProfile { get; }
        public ImmutableValueSequence<LaneRequest> LaneRequestQueue { get; }
        public ImmutableValueSequence<PlayerCommandKind> PendingActionRequests { get; }
        public bool ResetInProgress { get; }

        public bool Equals(PlayerSnapshot other)
        {
            return Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && State == other.State &&
                   IsGrounded == other.IsGrounded && ForwardSpeed.Equals(other.ForwardSpeed) &&
                   CurrentLane == other.CurrentLane && TargetLane == other.TargetLane &&
                   LateralPosition.Equals(other.LateralPosition) && LaneSegmentStart.Equals(other.LaneSegmentStart) &&
                   LaneSegmentTarget.Equals(other.LaneSegmentTarget) && LaneChangeProgress.Equals(other.LaneChangeProgress) &&
                   VerticalVelocity.Equals(other.VerticalVelocity) && SlideElapsedTime.Equals(other.SlideElapsedTime) &&
                   ColliderProfile.Equals(other.ColliderProfile) && SequenceEquals(LaneRequestQueue, other.LaneRequestQueue) &&
                   SequenceEquals(PendingActionRequests, other.PendingActionRequests) && ResetInProgress == other.ResetInProgress;
        }

        public override bool Equals(object obj) { return obj is PlayerSnapshot other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Position.GetHashCode();
                hash = hash * 397 ^ Rotation.GetHashCode();
                hash = hash * 397 ^ (int)State;
                hash = hash * 397 ^ IsGrounded.GetHashCode();
                hash = hash * 397 ^ ForwardSpeed.GetHashCode();
                hash = hash * 397 ^ (int)CurrentLane;
                hash = hash * 397 ^ (int)TargetLane;
                hash = hash * 397 ^ LateralPosition.GetHashCode();
                hash = hash * 397 ^ LaneSegmentStart.GetHashCode();
                hash = hash * 397 ^ LaneSegmentTarget.GetHashCode();
                hash = hash * 397 ^ LaneChangeProgress.GetHashCode();
                hash = hash * 397 ^ VerticalVelocity.GetHashCode();
                hash = hash * 397 ^ SlideElapsedTime.GetHashCode();
                hash = hash * 397 ^ ColliderProfile.GetHashCode();
                hash = hash * 397 ^ SequenceHash(LaneRequestQueue);
                hash = hash * 397 ^ SequenceHash(PendingActionRequests);
                return hash * 397 ^ ResetInProgress.GetHashCode();
            }
        }

        public static bool operator ==(PlayerSnapshot left, PlayerSnapshot right) { return left.Equals(right); }
        public static bool operator !=(PlayerSnapshot left, PlayerSnapshot right) { return !left.Equals(right); }

        private static bool SequenceEquals<T>(ImmutableValueSequence<T> left, ImmutableValueSequence<T> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) || right.Count == 0;
            if (ReferenceEquals(right, null)) return left.Count == 0;
            return left.Equals(right);
        }

        private static int SequenceHash<T>(ImmutableValueSequence<T> value)
        {
            return ReferenceEquals(value, null) ? ImmutableValueSequence<T>.Empty.GetHashCode() : value.GetHashCode();
        }
    }

    public readonly struct PlayerResetSnapshot : IEquatable<PlayerResetSnapshot>
    {
        public PlayerResetSnapshot(PlayerSnapshot player, Vector3 cameraPosition, Quaternion cameraRotation,
            Vector3 cameraFollowOffset, Vector3 previousCameraTarget, Vector3 cameraConvergenceVelocity,
            float remainingCameraSettleTime, bool hasCameraTarget, InputLatchState inputLatchState,
            bool inputConsumptionEnabled, ImmutableValueSequence<LogicalContactIdentity> activeLogicalContacts,
            ImmutableValueSequence<ContactEventIdentity> contactEventDeduplication)
        {
            Player = player;
            CameraPosition = cameraPosition;
            CameraRotation = cameraRotation;
            CameraFollowOffset = cameraFollowOffset;
            PreviousCameraTarget = previousCameraTarget;
            CameraConvergenceVelocity = cameraConvergenceVelocity;
            RemainingCameraSettleTime = remainingCameraSettleTime;
            HasCameraTarget = hasCameraTarget;
            InputLatchState = inputLatchState;
            InputConsumptionEnabled = inputConsumptionEnabled;
            ActiveLogicalContacts = activeLogicalContacts ?? ImmutableValueSequence<LogicalContactIdentity>.Empty;
            ContactEventDeduplication = contactEventDeduplication ?? ImmutableValueSequence<ContactEventIdentity>.Empty;
        }

        public PlayerSnapshot Player { get; }
        public Vector3 CameraPosition { get; }
        public Quaternion CameraRotation { get; }
        public Vector3 CameraFollowOffset { get; }
        public Vector3 PreviousCameraTarget { get; }
        public Vector3 CameraConvergenceVelocity { get; }
        public float RemainingCameraSettleTime { get; }
        public bool HasCameraTarget { get; }
        public InputLatchState InputLatchState { get; }
        public bool InputConsumptionEnabled { get; }
        public ImmutableValueSequence<LogicalContactIdentity> ActiveLogicalContacts { get; }
        public ImmutableValueSequence<ContactEventIdentity> ContactEventDeduplication { get; }

        public bool Equals(PlayerResetSnapshot other)
        {
            return Player.Equals(other.Player) && CameraPosition.Equals(other.CameraPosition) &&
                   CameraRotation.Equals(other.CameraRotation) && CameraFollowOffset.Equals(other.CameraFollowOffset) &&
                   PreviousCameraTarget.Equals(other.PreviousCameraTarget) &&
                   CameraConvergenceVelocity.Equals(other.CameraConvergenceVelocity) &&
                   RemainingCameraSettleTime.Equals(other.RemainingCameraSettleTime) &&
                   HasCameraTarget == other.HasCameraTarget && InputLatchState == other.InputLatchState &&
                   InputConsumptionEnabled == other.InputConsumptionEnabled &&
                   SequenceEquals(ActiveLogicalContacts, other.ActiveLogicalContacts) &&
                   SequenceEquals(ContactEventDeduplication, other.ContactEventDeduplication);
        }

        public override bool Equals(object obj) { return obj is PlayerResetSnapshot other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Player.GetHashCode();
                hash = hash * 397 ^ CameraPosition.GetHashCode();
                hash = hash * 397 ^ CameraRotation.GetHashCode();
                hash = hash * 397 ^ CameraFollowOffset.GetHashCode();
                hash = hash * 397 ^ PreviousCameraTarget.GetHashCode();
                hash = hash * 397 ^ CameraConvergenceVelocity.GetHashCode();
                hash = hash * 397 ^ RemainingCameraSettleTime.GetHashCode();
                hash = hash * 397 ^ HasCameraTarget.GetHashCode();
                hash = hash * 397 ^ (int)InputLatchState;
                hash = hash * 397 ^ InputConsumptionEnabled.GetHashCode();
                hash = hash * 397 ^ SequenceHash(ActiveLogicalContacts);
                return hash * 397 ^ SequenceHash(ContactEventDeduplication);
            }
        }

        public static bool operator ==(PlayerResetSnapshot left, PlayerResetSnapshot right) { return left.Equals(right); }
        public static bool operator !=(PlayerResetSnapshot left, PlayerResetSnapshot right) { return !left.Equals(right); }

        private static bool SequenceEquals<T>(ImmutableValueSequence<T> left, ImmutableValueSequence<T> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) || right.Count == 0;
            if (ReferenceEquals(right, null)) return left.Count == 0;
            return left.Equals(right);
        }

        private static int SequenceHash<T>(ImmutableValueSequence<T> value)
        {
            return ReferenceEquals(value, null) ? ImmutableValueSequence<T>.Empty.GetHashCode() : value.GetHashCode();
        }
    }
}