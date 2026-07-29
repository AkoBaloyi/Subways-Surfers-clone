using System;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public readonly struct LaneRequest : IEquatable<LaneRequest>
    {
        public LaneRequest(LaneDirection direction, ulong requestOrder)
        {
            Direction = direction;
            RequestOrder = requestOrder;
        }

        public LaneDirection Direction { get; }
        public ulong RequestOrder { get; }

        public bool Equals(LaneRequest other)
        {
            return Direction == other.Direction && RequestOrder == other.RequestOrder;
        }

        public override bool Equals(object obj) { return obj is LaneRequest other && Equals(other); }
        public override int GetHashCode() { return ((int)Direction * 397) ^ RequestOrder.GetHashCode(); }
        public static bool operator ==(LaneRequest left, LaneRequest right) { return left.Equals(right); }
        public static bool operator !=(LaneRequest left, LaneRequest right) { return !left.Equals(right); }
    }

    public readonly struct ActionRequestResult : IEquatable<ActionRequestResult>
    {
        public ActionRequestResult(CommandStatus status, PlayerCommandKind command,
            RejectionReason reason, PlayerState currentState)
        {
            Status = status;
            Command = command;
            Reason = reason;
            CurrentState = currentState;
        }

        public CommandStatus Status { get; }
        public PlayerCommandKind Command { get; }
        public RejectionReason Reason { get; }
        public PlayerState CurrentState { get; }

        public bool Equals(ActionRequestResult other)
        {
            return Status == other.Status && Command == other.Command &&
                   Reason == other.Reason && CurrentState == other.CurrentState;
        }
        public override bool Equals(object obj) { return obj is ActionRequestResult other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked { return (((int)Status * 397 ^ (int)Command) * 397 ^ (int)Reason) * 397 ^ (int)CurrentState; }
        }
        public static bool operator ==(ActionRequestResult left, ActionRequestResult right) { return left.Equals(right); }
        public static bool operator !=(ActionRequestResult left, ActionRequestResult right) { return !left.Equals(right); }
    }

    public readonly struct FailureCommandResult : IEquatable<FailureCommandResult>
    {
        public FailureCommandResult(CommandStatus status, PlayerCommandKind command,
            RejectionReason reason, PlayerState currentState)
        {
            Status = status;
            Command = command;
            Reason = reason;
            CurrentState = currentState;
        }

        public FailureCommandResult(CommandStatus status, RejectionReason reason, PlayerState currentState)
            : this(status, PlayerCommandKind.Failure, reason, currentState) { }

        public CommandStatus Status { get; }
        public PlayerCommandKind Command { get; }
        public RejectionReason Reason { get; }
        public PlayerState CurrentState { get; }

        public bool Equals(FailureCommandResult other)
        {
            return Status == other.Status && Command == other.Command &&
                   Reason == other.Reason && CurrentState == other.CurrentState;
        }

        public override bool Equals(object obj) { return obj is FailureCommandResult other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked { return (((int)Status * 397 ^ (int)Command) * 397 ^ (int)Reason) * 397 ^ (int)CurrentState; }
        }
        public static bool operator ==(FailureCommandResult left, FailureCommandResult right) { return left.Equals(right); }
        public static bool operator !=(FailureCommandResult left, FailureCommandResult right) { return !left.Equals(right); }
    }
    public readonly struct ResetRequestResult : IEquatable<ResetRequestResult>
    {
        public ResetRequestResult(CommandStatus status, PlayerCommandKind command,
            RejectionReason reason, PlayerState currentState, string requestId)
        {
            Status = status;
            Command = command;
            Reason = reason;
            CurrentState = currentState;
            RequestId = requestId;
        }

        public ResetRequestResult(CommandStatus status, RejectionReason reason,
            PlayerState currentState, string requestId)
            : this(status, PlayerCommandKind.Reset, reason, currentState, requestId) { }

        public CommandStatus Status { get; }
        public PlayerCommandKind Command { get; }
        public RejectionReason Reason { get; }
        public PlayerState CurrentState { get; }
        public string RequestId { get; }

        public bool Equals(ResetRequestResult other)
        {
            return Status == other.Status && Command == other.Command && Reason == other.Reason &&
                   CurrentState == other.CurrentState &&
                   string.Equals(RequestId, other.RequestId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is ResetRequestResult other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (((int)Status * 397 ^ (int)Command) * 397 ^ (int)Reason) * 397 ^ (int)CurrentState;
                return hash * 397 ^ (RequestId == null ? 0 : StringComparer.Ordinal.GetHashCode(RequestId));
            }
        }
        public static bool operator ==(ResetRequestResult left, ResetRequestResult right) { return left.Equals(right); }
        public static bool operator !=(ResetRequestResult left, ResetRequestResult right) { return !left.Equals(right); }
    }

    public readonly struct SpeedSetResult : IEquatable<SpeedSetResult>
    {
        public SpeedSetResult(CommandStatus status, PlayerCommandKind command,
            RejectionReason reason, PlayerState currentState, float requestedSpeed, float effectiveSpeed)
        {
            Status = status;
            Command = command;
            Reason = reason;
            CurrentState = currentState;
            RequestedSpeed = requestedSpeed;
            EffectiveSpeed = effectiveSpeed;
        }

        public SpeedSetResult(CommandStatus status, RejectionReason reason,
            PlayerState currentState, float requestedSpeed, float effectiveSpeed)
            : this(status, PlayerCommandKind.SetForwardSpeed, reason, currentState,
                requestedSpeed, effectiveSpeed) { }

        public CommandStatus Status { get; }
        public PlayerCommandKind Command { get; }
        public RejectionReason Reason { get; }
        public PlayerState CurrentState { get; }
        public float RequestedSpeed { get; }
        public float EffectiveSpeed { get; }

        public bool Equals(SpeedSetResult other)
        {
            return Status == other.Status && Command == other.Command && Reason == other.Reason &&
                   CurrentState == other.CurrentState && RequestedSpeed.Equals(other.RequestedSpeed) &&
                   EffectiveSpeed.Equals(other.EffectiveSpeed);
        }

        public override bool Equals(object obj) { return obj is SpeedSetResult other && Equals(other); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (((int)Status * 397 ^ (int)Command) * 397 ^ (int)Reason) * 397 ^ (int)CurrentState;
                return (hash * 397 ^ RequestedSpeed.GetHashCode()) * 397 ^ EffectiveSpeed.GetHashCode();
            }
        }
        public static bool operator ==(SpeedSetResult left, SpeedSetResult right) { return left.Equals(right); }
        public static bool operator !=(SpeedSetResult left, SpeedSetResult right) { return !left.Equals(right); }
    }
}