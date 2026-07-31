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
        private readonly List<LogicalContactKey> activeContactOrder;
        private readonly List<ContactEventIdentity> publishedContactEvents;
        private ulong contactSequence;

        public EnvironmentContactTracker(PlayerEventHub eventHub)
        {
            this.eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            activeContacts = new Dictionary<LogicalContactKey, ActiveContact>();
            activeContactOrder = new List<LogicalContactKey>();
            publishedContactEvents = new List<ContactEventIdentity>();
        }

        public ulong LastContactId { get { return contactSequence; } }
        public int ActiveContactCount { get { return activeContacts.Count; } }

        /// <summary>
        /// Identity of every open logical contact, in the order the contacts were opened. Read-only:
        /// contact lifetime stays owned by <see cref="Enter"/> and <see cref="Exit"/>.
        /// </summary>
        public ImmutableValueSequence<LogicalContactIdentity> ActiveLogicalContacts
        {
            get
            {
                if (activeContactOrder.Count == 0)
                    return ImmutableValueSequence<LogicalContactIdentity>.Empty;

                var identities = new List<LogicalContactIdentity>(activeContactOrder.Count);
                for (var index = 0; index < activeContactOrder.Count; index++)
                {
                    var key = activeContactOrder[index];
                    ActiveContact contact;
                    if (!activeContacts.TryGetValue(key, out contact)) continue;

                    identities.Add(new LogicalContactIdentity(
                        contact.ContactId, key.EnvironmentObjectId, key.Kind));
                }

                return new ImmutableValueSequence<LogicalContactIdentity>(identities);
            }
        }

        /// <summary>
        /// Identity of every contact event published this session, in publication order. This is the
        /// deduplication record a reset clears; it never decides whether a contact publishes, which
        /// stays owned by logical contact lifetime.
        /// </summary>
        public ImmutableValueSequence<ContactEventIdentity> ContactEventDeduplication
        {
            get
            {
                return publishedContactEvents.Count == 0
                    ? ImmutableValueSequence<ContactEventIdentity>.Empty
                    : new ImmutableValueSequence<ContactEventIdentity>(publishedContactEvents);
            }
        }

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
            activeContactOrder.Add(key);
            publishedContactEvents.Add(new ContactEventIdentity(contactId, kind));

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
            if (contact.ChildColliderIds.Count != 0) return;

            activeContacts.Remove(key);
            activeContactOrder.Remove(key);
        }

        /// <summary>
        /// Clears contact tracking and the contact-event deduplication record. The session Contact_Id
        /// sequence is session identity and keeps advancing, so a contact opened after a clear is
        /// still a new contact.
        /// </summary>
        public void Clear()
        {
            activeContacts.Clear();
            activeContactOrder.Clear();
            publishedContactEvents.Clear();
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

            public string EnvironmentObjectId { get; }
            public EnvironmentObjectKind Kind { get; }

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