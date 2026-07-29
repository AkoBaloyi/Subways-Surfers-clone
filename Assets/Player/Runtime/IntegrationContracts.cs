using System;
using UnityEngine;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player.Contracts
{
    public interface IPlayerCommands
    {
        ActionRequestResult RequestLane(LaneDirection direction);
        ActionRequestResult RequestJump();
        ActionRequestResult RequestSlide();
        FailureCommandResult RequestFailure();
        ResetRequestResult RequestReset(string requestId);
        SpeedSetResult SetForwardSpeed(float requestedSpeed);
    }

    public interface IPlayerQueries
    {
        PlayerState CurrentState { get; }
        bool IsGrounded { get; }
        float ForwardSpeed { get; }
        PlayerSnapshot Snapshot { get; }
    }

    public interface IFailureCommandContract
    {
        FailureCommandResult RequestFailure();
    }

    public interface IResetRequestContract
    {
        ResetRequestResult RequestReset(string requestId);
    }

    public interface IForwardSpeedApi
    {
        float ForwardSpeed { get; }
        SpeedSetResult SetForwardSpeed(float requestedSpeed);
    }

    public interface IPlayerStateQuery
    {
        PlayerState CurrentState { get; }
    }

    public interface IGroundedStatusQuery
    {
        bool IsGrounded { get; }
    }

    public interface IPlayerEventSource
    {
        event Action<PlayerHitEvent> PlayerHit;
        event Action<CoinCollectedEvent> CoinCollected;
        event Action<PlayerStateChangedEvent> StateChanged;
        event Action<PlayerResetStartedEvent> ResetStarted;
        event Action<PlayerResetCompletedEvent> ResetCompleted;
        event Action<ValidationDiagnostic> ValidationReported;
    }

    public interface IEnvironmentObject
    {
        string EnvironmentObjectId { get; }
        EnvironmentObjectKind Kind { get; }
        float CollectibleValue { get; }
    }

    public interface IRunningSurface { }
    public interface IEnvironmentObstruction { }

    public interface IAnimationReceiver
    {
        bool TryApply(AnimationCommand command);
    }

    public readonly struct AnimationCommand : IEquatable<AnimationCommand>
    {
        public AnimationCommand(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool Equals(AnimationCommand other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is AnimationCommand other && Equals(other); }
        public override int GetHashCode() { return Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name); }
        public static bool operator ==(AnimationCommand left, AnimationCommand right) { return left.Equals(right); }
        public static bool operator !=(AnimationCommand left, AnimationCommand right) { return !left.Equals(right); }
    }

    public readonly struct EnvironmentContactData : IEquatable<EnvironmentContactData>
    {
        public EnvironmentContactData(ulong contactId, IEnvironmentObject environmentObject,
            Vector3 contactPosition)
        {
            ContactId = contactId;
            EnvironmentObject = environmentObject;
            ContactPosition = contactPosition;
        }

        public ulong ContactId { get; }
        public IEnvironmentObject EnvironmentObject { get; }
        public Vector3 ContactPosition { get; }

        public bool Equals(EnvironmentContactData other)
        {
            return ContactId == other.ContactId && ReferenceEquals(EnvironmentObject, other.EnvironmentObject) &&
                   ContactPosition.Equals(other.ContactPosition);
        }

        public override bool Equals(object obj) { return obj is EnvironmentContactData other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                return ((ContactId.GetHashCode() * 397) ^
                    (EnvironmentObject == null ? 0 : EnvironmentObject.GetHashCode())) * 397 ^ ContactPosition.GetHashCode();
            }
        }
        public static bool operator ==(EnvironmentContactData left, EnvironmentContactData right) { return left.Equals(right); }
        public static bool operator !=(EnvironmentContactData left, EnvironmentContactData right) { return !left.Equals(right); }
    }

    public readonly struct PlayerHitEvent : IEquatable<PlayerHitEvent>
    {
        public PlayerHitEvent(ulong eventId, ulong contactId, string environmentObjectId,
            IEnvironmentObject obstacle, Vector3 contactPosition)
        {
            EventId = eventId;
            ContactId = contactId;
            EnvironmentObjectId = environmentObjectId;
            Obstacle = obstacle;
            ContactPosition = contactPosition;
        }

        public ulong EventId { get; }
        public ulong ContactId { get; }
        public string EnvironmentObjectId { get; }
        public IEnvironmentObject Obstacle { get; }
        public Vector3 ContactPosition { get; }

        public bool Equals(PlayerHitEvent other)
        {
            return EventId == other.EventId && ContactId == other.ContactId &&
                   string.Equals(EnvironmentObjectId, other.EnvironmentObjectId, StringComparison.Ordinal) &&
                   ReferenceEquals(Obstacle, other.Obstacle) && ContactPosition.Equals(other.ContactPosition);
        }

        public override bool Equals(object obj) { return obj is PlayerHitEvent other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (EventId.GetHashCode() * 397) ^ ContactId.GetHashCode();
                hash = hash * 397 ^ (EnvironmentObjectId == null ? 0 : StringComparer.Ordinal.GetHashCode(EnvironmentObjectId));
                hash = hash * 397 ^ (Obstacle == null ? 0 : Obstacle.GetHashCode());
                return hash * 397 ^ ContactPosition.GetHashCode();
            }
        }
        public static bool operator ==(PlayerHitEvent left, PlayerHitEvent right) { return left.Equals(right); }
        public static bool operator !=(PlayerHitEvent left, PlayerHitEvent right) { return !left.Equals(right); }
    }
    public readonly struct CoinCollectedEvent : IEquatable<CoinCollectedEvent>
    {
        public CoinCollectedEvent(ulong eventId, ulong contactId, string environmentObjectId,
            IEnvironmentObject coin, float collectibleValue)
        {
            EventId = eventId;
            ContactId = contactId;
            EnvironmentObjectId = environmentObjectId;
            Coin = coin;
            CollectibleValue = collectibleValue;
        }

        public ulong EventId { get; }
        public ulong ContactId { get; }
        public string EnvironmentObjectId { get; }
        public IEnvironmentObject Coin { get; }
        public float CollectibleValue { get; }

        public bool Equals(CoinCollectedEvent other)
        {
            return EventId == other.EventId && ContactId == other.ContactId &&
                   string.Equals(EnvironmentObjectId, other.EnvironmentObjectId, StringComparison.Ordinal) &&
                   ReferenceEquals(Coin, other.Coin) && CollectibleValue.Equals(other.CollectibleValue);
        }

        public override bool Equals(object obj) { return obj is CoinCollectedEvent other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (EventId.GetHashCode() * 397) ^ ContactId.GetHashCode();
                hash = hash * 397 ^ (EnvironmentObjectId == null ? 0 : StringComparer.Ordinal.GetHashCode(EnvironmentObjectId));
                hash = hash * 397 ^ (Coin == null ? 0 : Coin.GetHashCode());
                return hash * 397 ^ CollectibleValue.GetHashCode();
            }
        }
        public static bool operator ==(CoinCollectedEvent left, CoinCollectedEvent right) { return left.Equals(right); }
        public static bool operator !=(CoinCollectedEvent left, CoinCollectedEvent right) { return !left.Equals(right); }
    }

    public readonly struct PlayerStateChangedEvent : IEquatable<PlayerStateChangedEvent>
    {
        public PlayerStateChangedEvent(ulong eventId, PlayerState previousState,
            PlayerState currentState, PlayerTransitionCause transitionCause)
        {
            EventId = eventId;
            PreviousState = previousState;
            CurrentState = currentState;
            TransitionCause = transitionCause;
        }

        public ulong EventId { get; }
        public PlayerState PreviousState { get; }
        public PlayerState CurrentState { get; }
        public PlayerTransitionCause TransitionCause { get; }

        public bool Equals(PlayerStateChangedEvent other)
        {
            return EventId == other.EventId && PreviousState == other.PreviousState &&
                   CurrentState == other.CurrentState && TransitionCause == other.TransitionCause;
        }

        public override bool Equals(object obj) { return obj is PlayerStateChangedEvent other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked { return ((EventId.GetHashCode() * 397 ^ (int)PreviousState) * 397 ^ (int)CurrentState) * 397 ^ (int)TransitionCause; }
        }
        public static bool operator ==(PlayerStateChangedEvent left, PlayerStateChangedEvent right) { return left.Equals(right); }
        public static bool operator !=(PlayerStateChangedEvent left, PlayerStateChangedEvent right) { return !left.Equals(right); }
    }
    public readonly struct PlayerResetStartedEvent : IEquatable<PlayerResetStartedEvent>
    {
        public PlayerResetStartedEvent(ulong eventId, string requestId)
        {
            EventId = eventId;
            RequestId = requestId;
        }

        public ulong EventId { get; }
        public string RequestId { get; }

        public bool Equals(PlayerResetStartedEvent other)
        {
            return EventId == other.EventId && string.Equals(RequestId, other.RequestId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is PlayerResetStartedEvent other && Equals(other); }
        public override int GetHashCode()
        {
            return (EventId.GetHashCode() * 397) ^ (RequestId == null ? 0 : StringComparer.Ordinal.GetHashCode(RequestId));
        }
        public static bool operator ==(PlayerResetStartedEvent left, PlayerResetStartedEvent right) { return left.Equals(right); }
        public static bool operator !=(PlayerResetStartedEvent left, PlayerResetStartedEvent right) { return !left.Equals(right); }
    }

    public readonly struct PlayerResetCompletedEvent : IEquatable<PlayerResetCompletedEvent>
    {
        public PlayerResetCompletedEvent(ulong eventId, string requestId, PlayerState resultingState)
        {
            EventId = eventId;
            RequestId = requestId;
            ResultingState = resultingState;
        }

        public ulong EventId { get; }
        public string RequestId { get; }
        public PlayerState ResultingState { get; }

        public bool Equals(PlayerResetCompletedEvent other)
        {
            return EventId == other.EventId && ResultingState == other.ResultingState &&
                   string.Equals(RequestId, other.RequestId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is PlayerResetCompletedEvent other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                return ((EventId.GetHashCode() * 397) ^
                    (RequestId == null ? 0 : StringComparer.Ordinal.GetHashCode(RequestId))) * 397 ^ (int)ResultingState;
            }
        }
        public static bool operator ==(PlayerResetCompletedEvent left, PlayerResetCompletedEvent right) { return left.Equals(right); }
        public static bool operator !=(PlayerResetCompletedEvent left, PlayerResetCompletedEvent right) { return !left.Equals(right); }
    }

    public readonly struct ValidationDiagnostic : IEquatable<ValidationDiagnostic>
    {
        public ValidationDiagnostic(DiagnosticSeverity severity, DiagnosticCode code,
            string field, string message)
        {
            Severity = severity;
            Code = code;
            Field = field;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; }
        public DiagnosticCode Code { get; }
        public string Field { get; }
        public string Message { get; }

        public bool Equals(ValidationDiagnostic other)
        {
            return Severity == other.Severity && Code == other.Code &&
                   string.Equals(Field, other.Field, StringComparison.Ordinal) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is ValidationDiagnostic other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ((int)Severity * 397) ^ (int)Code;
                hash = hash * 397 ^ (Field == null ? 0 : StringComparer.Ordinal.GetHashCode(Field));
                return hash * 397 ^ (Message == null ? 0 : StringComparer.Ordinal.GetHashCode(Message));
            }
        }
        public static bool operator ==(ValidationDiagnostic left, ValidationDiagnostic right) { return left.Equals(right); }
        public static bool operator !=(ValidationDiagnostic left, ValidationDiagnostic right) { return !left.Equals(right); }
    }
}