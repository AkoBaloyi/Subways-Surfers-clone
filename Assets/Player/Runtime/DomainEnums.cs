namespace SubwaySurfers.Player.Domain
{
    public enum PlayerState { Running, Jumping, Sliding, Failed, Resetting }
    public enum LogicalLane { Left, Center, Right }
    public enum LaneDirection { Left, Right }
    public enum CommandStatus { Accepted, Rejected }
    public enum PlayerCommandKind { Lane, Jump, Slide, Failure, Reset, SetForwardSpeed }
    public enum RejectionReason
    {
        None,
        InvalidValue,
        InvalidState,
        NotGrounded,
        BoundaryNoOp,
        MissingRequestId,
        DuplicateRequestId,
        ResetInProgress,
        ReceiverFailure
    }

    public enum EnvironmentObjectKind { Obstacle, Coin }
    public enum PlayerTransitionCause
    {
        JumpRequested,
        SlideRequested,
        Landed,
        SlideRestored,
        FailureRequested,
        ResetRequested,
        ResetCompleted
    }

    public enum DiagnosticSeverity { Info, Warning, Error, Fatal }
    public enum DiagnosticCode
    {
        InvalidValue,
        MissingReference,
        InvalidReference,
        AnimationReceiverAbsent,
        AnimationReceiverUnavailable,
        AnimationReceiverRejected,
        AnimationReceiverException,
        SubscriberException,
        MissingInputAction,
        InvalidFallbackConfiguration,
        PostStartInvalidConfiguration
    }

    public enum InputLatchState { Neutral, Left, Right }
}