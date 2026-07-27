using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public sealed class EnvironmentContactTracker
    {
        private readonly PlayerEventHub eventHub;
        private readonly Dictionary<LogicalContactKey, ActiveContact> activeContacts;
        private ulong contactSequence;

        public EnvironmentContactTracker(PlayerEventHub eventHub)
        {
            this.eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            activeContacts = new Dictionary<LogicalContactKey, ActiveContact>();
        }

        public ulong LastContactId { get { return contactSequence; } }
        public int ActiveContactCount { get { return activeContacts.Count; } }

        public void Enter(
            int childColliderId,
            IEnvironmentObject environmentObject,
            Vector3 contactPosition)
        {
            if (!TryReadIdentity(environmentObject, out var environmentObjectId, out var kind))
                return;

            var key = new LogicalContactKey(environmentObjectId, kind);
            if (activeContacts.TryGetValue(key, out var existing))
            {
                existing.ChildColliderIds.Add(childColliderId);
                return;
            }

            var collectibleValue = 0f;
            if (kind == EnvironmentObjectKind.Coin &&
                !TryReadCollectibleValue(environmentObject, out collectibleValue))
            {
                return;
            }

            var contactId = NextContactId();
            var contact = new ActiveContact(contactId, childColliderId);
            activeContacts.Add(key, contact);

            if (kind == EnvironmentObjectKind.Obstacle)
            {
                eventHub.PublishPlayerHit(
                    contactId,
                    environmentObjectId,
                    environmentObject,
                    contactPosition);
                return;
            }

            eventHub.PublishCoinCollected(
                contactId,
                environmentObjectId,
                environmentObject,
                collectibleValue);
        }

        public void Exit(int childColliderId, IEnvironmentObject environmentObject)
        {
            if (!TryReadIdentity(environmentObject, out var environmentObjectId, out var kind))
                return;

            var key = new LogicalContactKey(environmentObjectId, kind);
            if (!activeContacts.TryGetValue(key, out var contact)) return;
            if (!contact.ChildColliderIds.Remove(childColliderId)) return;
            if (contact.ChildColliderIds.Count == 0) activeContacts.Remove(key);
        }

        public void Clear()
        {
            activeContacts.Clear();
        }

        private bool TryReadIdentity(
            IEnvironmentObject environmentObject,
            out string environmentObjectId,
            out EnvironmentObjectKind kind)
        {
            environmentObjectId = null;
            kind = default;
            if (environmentObject == null)
            {
                ReportInvalidEnvironment(
                    DiagnosticCode.MissingReference,
                    "EnvironmentObject",
                    "Environment contact data did not include an environment provider.");
                return false;
            }

            try
            {
                environmentObjectId = environmentObject.EnvironmentObjectId;
                kind = environmentObject.Kind;
            }
            catch (Exception exception)
            {
                ReportInvalidEnvironment(
                    DiagnosticCode.InvalidReference,
                    "EnvironmentObject",
                    "Environment metadata could not be read: " + exception.GetType().FullName + ".");
                return false;
            }

            if (string.IsNullOrEmpty(environmentObjectId))
            {
                ReportInvalidEnvironment(
                    DiagnosticCode.InvalidValue,
                    "EnvironmentObjectId",
                    "Environment contacts require a non-empty environment object ID.");
                return false;
            }

            if (kind != EnvironmentObjectKind.Obstacle && kind != EnvironmentObjectKind.Coin)
            {
                ReportInvalidEnvironment(
                    DiagnosticCode.InvalidValue,
                    "EnvironmentObjectKind",
                    "Environment contacts require an obstacle or coin kind.");
                return false;
            }

            return true;
        }

        private bool TryReadCollectibleValue(
            IEnvironmentObject environmentObject,
            out float collectibleValue)
        {
            collectibleValue = 0f;
            try
            {
                collectibleValue = environmentObject.CollectibleValue;
            }
            catch (Exception exception)
            {
                ReportInvalidEnvironment(
                    DiagnosticCode.InvalidReference,
                    "CollectibleValue",
                    "Coin metadata could not be read: " + exception.GetType().FullName + ".");
                return false;
            }

            if (!float.IsNaN(collectibleValue) && !float.IsInfinity(collectibleValue))
                return true;

            ReportInvalidEnvironment(
                DiagnosticCode.InvalidValue,
                "CollectibleValue",
                "Coin contacts require a finite collectible value.");
            return false;
        }

        private ulong NextContactId()
        {
            if (contactSequence == ulong.MaxValue)
                throw new InvalidOperationException("The session contact ID sequence is exhausted.");

            contactSequence++;
            return contactSequence;
        }

        private void ReportInvalidEnvironment(
            DiagnosticCode code,
            string field,
            string message)
        {
            eventHub.PublishValidation(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                code,
                field,
                message));
        }

        private readonly struct LogicalContactKey : IEquatable<LogicalContactKey>
        {
            public LogicalContactKey(string environmentObjectId, EnvironmentObjectKind kind)
            {
                EnvironmentObjectId = environmentObjectId;
                Kind = kind;
            }

            private string EnvironmentObjectId { get; }
            private EnvironmentObjectKind Kind { get; }

            public bool Equals(LogicalContactKey other)
            {
                return Kind == other.Kind && string.Equals(
                    EnvironmentObjectId,
                    other.EnvironmentObjectId,
                    StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is LogicalContactKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ((EnvironmentObjectId == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(EnvironmentObjectId)) * 397) ^ (int)Kind;
            }
        }

        private sealed class ActiveContact
        {
            public ActiveContact(ulong contactId, int firstChildColliderId)
            {
                ContactId = contactId;
                ChildColliderIds = new HashSet<int> { firstChildColliderId };
            }

            public ulong ContactId { get; }
            public HashSet<int> ChildColliderIds { get; }
        }
    }
}