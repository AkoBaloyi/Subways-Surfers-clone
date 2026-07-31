using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    /// <summary>
    /// Unity-side state a reset restores alongside the player simulation fields. Implemented by the
    /// camera follow and the input adapter, which own state the pure domain does not.
    /// </summary>
    public interface IPlayerResetParticipant
    {
        void RestoreInitialState();
    }

    /// <summary>
    /// The input latch the reset surface observes. Absent wiring reports the neutral latch with
    /// consumption enabled, which is what <c>Player_Initial_State</c> defines.
    /// </summary>
    public interface IPlayerInputLatchQuery
    {
        InputLatchState LatchState { get; }
        bool InputConsumptionEnabled { get; }
    }

    /// <summary>
    /// Owner of the atomic reset sequence. One accepted <c>Reset_Request</c> runs entirely on the
    /// calling (main) thread before the next <c>Movement_Update</c>: the Request_Id enters
    /// <c>Reset_Request_Ledger</c> before any mutation, the started event is published, the player
    /// enters Resetting, every field of <c>Player_Initial_State</c> is restored, the player returns to
    /// Running, and the completed event is published reporting Running. No movement update runs and no
    /// displacement is submitted.
    ///
    /// An empty or absent Request_Id, a Request_Id already in the ledger, and a request arriving while
    /// a reset is in progress are refused before any mutation and publish nothing. A refused
    /// Request_Id never enters the ledger, so it stays usable later.
    ///
    /// The session Event_Id sequence and the ledger are session integration state, not fields of
    /// <c>Player_Initial_State</c>: neither resets, so no Event_Id is reused and an accepted
    /// Request_Id stays a duplicate for the rest of the session.
    /// </summary>
    public sealed class PlayerResetService
    {
        private readonly PlayerConfiguration configuration;
        private readonly PlayerMovementLoop movementLoop;
        private readonly PlayerEventHub eventHub;
        private readonly CameraConvergenceState cameraConvergence;
        private readonly EnvironmentContactTracker contactTracker;
        private readonly Action<ValidationDiagnostic> diagnosticSink;
        private readonly HashSet<string> resetRequestLedger = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<IPlayerResetParticipant> participants = new List<IPlayerResetParticipant>();

        private bool resetInProgress;

        public PlayerResetService(
            PlayerConfiguration configuration,
            PlayerMovementLoop movementLoop,
            PlayerEventHub eventHub,
            Action<ValidationDiagnostic> diagnosticSink)
            : this(configuration, movementLoop, eventHub, null, null, diagnosticSink)
        {
        }

        public PlayerResetService(
            PlayerConfiguration configuration,
            PlayerMovementLoop movementLoop,
            PlayerEventHub eventHub,
            CameraConvergenceState cameraConvergence,
            EnvironmentContactTracker contactTracker,
            Action<ValidationDiagnostic> diagnosticSink)
        {
            if (movementLoop == null) throw new ArgumentNullException(nameof(movementLoop));
            if (eventHub == null) throw new ArgumentNullException(nameof(eventHub));

            this.configuration = configuration;
            this.movementLoop = movementLoop;
            this.eventHub = eventHub;
            this.cameraConvergence = cameraConvergence;
            this.contactTracker = contactTracker;
            this.diagnosticSink = diagnosticSink;
        }

        /// <summary>The input latch the reset surface reports and reset returns to neutral.</summary>
        public IPlayerInputLatchQuery InputLatch { get; set; }

        /// <summary>The complete reset equality surface: every field of <c>Player_Initial_State</c>.</summary>
        public PlayerResetSnapshot Snapshot
        {
            get
            {
                return new PlayerResetSnapshot(
                    movementLoop.Snapshot,
                    CameraPosition,
                    Quaternion.identity,
                    CameraFollowOffset,
                    PreviousCameraTarget,
                    CameraConvergenceVelocity,
                    RemainingCameraSettleTime,
                    HasCameraTarget,
                    InputLatch == null ? InputLatchState.Neutral : InputLatch.LatchState,
                    InputConsumptionEnabled,
                    contactTracker == null
                        ? ImmutableValueSequence<LogicalContactIdentity>.Empty
                        : contactTracker.ActiveLogicalContacts,
                    contactTracker == null
                        ? ImmutableValueSequence<ContactEventIdentity>.Empty
                        : contactTracker.ContactEventDeduplication);
            }
        }

        /// <summary>
        /// Registers Unity-side state the atomic sequence restores between the Resetting and Running
        /// transitions. Registration is idempotent, and a null participant is ignored.
        /// </summary>
        public void AddResetParticipant(IPlayerResetParticipant participant)
        {
            if (participant == null || participants.Contains(participant)) return;

            participants.Add(participant);
        }

        /// <summary>
        /// Runs one atomic reset for <paramref name="requestId"/>, or refuses it without mutation.
        /// </summary>
        public ResetRequestResult RequestReset(string requestId)
        {
            if (resetInProgress) return Refused(RejectionReason.ResetInProgress, requestId);
            if (string.IsNullOrEmpty(requestId)) return Refused(RejectionReason.MissingRequestId, requestId);
            if (resetRequestLedger.Contains(requestId))
                return Refused(RejectionReason.DuplicateRequestId, requestId);

            // The ledger records the accepted Request_Id before the sequence mutates or publishes
            // anything, so a re-entrant request for the same Request_Id can never be accepted twice.
            resetRequestLedger.Add(requestId);
            resetInProgress = true;
            try
            {
                eventHub.PublishResetStarted(requestId);

                var entered = movementLoop.RequestReset(requestId);
                if (entered.Status != CommandStatus.Accepted)
                {
                    Report(
                        "The player refused the Resetting transition of accepted Request_Id '" +
                        requestId + "' with " + entered.Reason + ".");
                    return entered;
                }

                RestoreInitialState();
                movementLoop.CompleteReset();
                resetInProgress = false;
                eventHub.PublishResetCompleted(requestId, movementLoop.CurrentState);
                return new ResetRequestResult(
                    CommandStatus.Accepted,
                    PlayerCommandKind.Reset,
                    RejectionReason.None,
                    movementLoop.CurrentState,
                    requestId);
            }
            finally
            {
                resetInProgress = false;
            }
        }

        private void RestoreInitialState()
        {
            movementLoop.RestorePlayerInitialState();

            // Camera_Follow_Offset is derived from the restored player pose and the configured initial
            // camera pose, so consecutive resets produce the same pose and offset.
            if (cameraConvergence != null)
            {
                cameraConvergence.Reset(
                    movementLoop.Snapshot.Position, configuration.InitialCameraPosition);
            }

            if (contactTracker != null) contactTracker.Clear();

            for (var index = 0; index < participants.Count; index++)
            {
                try
                {
                    participants[index].RestoreInitialState();
                }
                catch (Exception exception)
                {
                    Report(
                        "A reset participant threw " + exception.GetType().FullName +
                        " while restoring Player_Initial_State.");
                }
            }
        }

        private bool InputConsumptionEnabled
        {
            get
            {
                if (resetInProgress) return false;

                return InputLatch == null || InputLatch.InputConsumptionEnabled;
            }
        }

        private Vector3 CameraPosition
        {
            get { return cameraConvergence == null ? Vector3.zero : cameraConvergence.CameraPosition; }
        }

        private Vector3 CameraFollowOffset
        {
            get
            {
                return cameraConvergence == null
                    ? Vector3.zero
                    : cameraConvergence.CameraFollowOffset;
            }
        }

        private Vector3 PreviousCameraTarget
        {
            get
            {
                return cameraConvergence == null
                    ? Vector3.zero
                    : cameraConvergence.PreviousCameraTarget;
            }
        }

        private Vector3 CameraConvergenceVelocity
        {
            get
            {
                return cameraConvergence == null
                    ? Vector3.zero
                    : cameraConvergence.CameraConvergenceVelocity;
            }
        }

        private float RemainingCameraSettleTime
        {
            get
            {
                return cameraConvergence == null
                    ? 0f
                    : cameraConvergence.RemainingCameraSettleTime;
            }
        }

        private bool HasCameraTarget
        {
            get { return cameraConvergence != null && cameraConvergence.HasCameraTarget; }
        }

        private ResetRequestResult Refused(RejectionReason reason, string requestId)
        {
            return new ResetRequestResult(
                CommandStatus.Rejected,
                PlayerCommandKind.Reset,
                reason,
                movementLoop.CurrentState,
                requestId);
        }

        private void Report(string message)
        {
            var diagnostic = new ValidationDiagnostic(
                DiagnosticSeverity.Error, DiagnosticCode.InvalidValue, "ResetRequest", message);
            if (diagnosticSink != null) diagnosticSink(diagnostic);
            eventHub.PublishValidation(diagnostic);
        }
    }
}
