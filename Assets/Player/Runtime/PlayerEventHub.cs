using System;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public sealed class PlayerEventHub : IPlayerEventSource
    {
        private ulong eventSequence;

        public event Action<PlayerHitEvent> PlayerHit;
        public event Action<CoinCollectedEvent> CoinCollected;
        public event Action<PlayerStateChangedEvent> StateChanged;
        public event Action<PlayerResetStartedEvent> ResetStarted;
        public event Action<PlayerResetCompletedEvent> ResetCompleted;
        public event Action<ValidationDiagnostic> ValidationReported;

        public ulong LastEventId { get { return eventSequence; } }

        public void PublishPlayerHit(
            ulong contactId,
            string environmentObjectId,
            IEnvironmentObject obstacle,
            Vector3 contactPosition)
        {
            var payload = new PlayerHitEvent(
                NextEventId(),
                contactId,
                environmentObjectId,
                obstacle,
                contactPosition);
            Publish(PlayerHit, payload, nameof(PlayerHit), true);
        }

        public void PublishCoinCollected(
            ulong contactId,
            string environmentObjectId,
            IEnvironmentObject coin,
            float collectibleValue)
        {
            var payload = new CoinCollectedEvent(
                NextEventId(),
                contactId,
                environmentObjectId,
                coin,
                collectibleValue);
            Publish(CoinCollected, payload, nameof(CoinCollected), true);
        }

        public void PublishStateChanged(
            PlayerState previousState,
            PlayerState currentState,
            PlayerTransitionCause transitionCause)
        {
            var payload = new PlayerStateChangedEvent(
                NextEventId(),
                previousState,
                currentState,
                transitionCause);
            Publish(StateChanged, payload, nameof(StateChanged), true);
        }

        public void PublishResetStarted(string requestId)
        {
            var payload = new PlayerResetStartedEvent(NextEventId(), requestId);
            Publish(ResetStarted, payload, nameof(ResetStarted), true);
        }

        public void PublishResetCompleted(string requestId, PlayerState resultingState)
        {
            var payload = new PlayerResetCompletedEvent(
                NextEventId(),
                requestId,
                resultingState);
            Publish(ResetCompleted, payload, nameof(ResetCompleted), true);
        }

        public void PublishValidation(ValidationDiagnostic diagnostic)
        {
            Publish(
                ValidationReported,
                diagnostic,
                nameof(ValidationReported),
                false);
        }

        private ulong NextEventId()
        {
            if (eventSequence == ulong.MaxValue)
                throw new InvalidOperationException("The session event ID sequence is exhausted.");

            eventSequence++;
            return eventSequence;
        }

        private void Publish<T>(
            Action<T> handlers,
            T payload,
            string eventName,
            bool reportSubscriberExceptions)
        {
            if (handlers == null) return;

            foreach (Action<T> subscriber in handlers.GetInvocationList())
            {
                try
                {
                    subscriber(payload);
                }
                catch (Exception exception)
                {
                    if (reportSubscriberExceptions)
                        ReportSubscriberException(eventName, exception);
                }
            }
        }

        private void ReportSubscriberException(string eventName, Exception exception)
        {
            var diagnostic = new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCode.SubscriberException,
                eventName,
                "A subscriber threw " + exception.GetType().FullName +
                " while handling " + eventName + ".");
            Publish(
                ValidationReported,
                diagnostic,
                nameof(ValidationReported),
                false);
        }
    }
}