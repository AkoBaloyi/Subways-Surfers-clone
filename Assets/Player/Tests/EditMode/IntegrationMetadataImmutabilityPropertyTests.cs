using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class IntegrationMetadataImmutabilityPropertyTests
    {
        private const int Seed = 141116;
        private const int CaseCount = 160;

        // **Validates: Requirements 14.11**
        [Test]
        [Description("Feature: player-controller, Property 16: Integration metadata is consumed without owner mutation")]
        public void IntegrationMetadataIsConsumedWithoutOwnerMutation_Property16_Requirement_14_11()
        {
            var caseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                AssertConsumedWithoutMutation,
                render: generated => generated.ToString());
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var kind = caseIndex % 2 == 0
                ? EnvironmentObjectKind.Obstacle
                : EnvironmentObjectKind.Coin;
            var environmentObjectId = string.Concat(
                kind == EnvironmentObjectKind.Obstacle ? "obstacle-" : "coin-",
                caseIndex.ToString(CultureInfo.InvariantCulture),
                "-",
                random.Next().ToString(CultureInfo.InvariantCulture));
            var collectibleValue = GeneratedValues.NextFiniteFloat(random, 0.01f, 10000f);
            var provider = new MutableEnvironmentRecord(
                environmentObjectId,
                kind,
                collectibleValue);
            var contactPosition = new Vector3(
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f),
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f),
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f));
            var childColliderId = caseIndex + 1000;
            return new GeneratedCase(provider, childColliderId, contactPosition);
        }

        private static void AssertConsumedWithoutMutation(GeneratedCase generated)
        {
            var provider = generated.Provider;
            var originalProviderReference = (IEnvironmentObject)provider;
            var originalIdReference = provider.EnvironmentObjectId;
            var before = ProviderSnapshot.Capture(provider);
            PlayerHitEvent? hit = null;
            CoinCollectedEvent? coin = null;
            var seam = ContactConsumptionSeam.Create(
                value => hit = value,
                value => coin = value);

            seam.Enter(
                generated.ChildColliderId,
                provider,
                generated.ContactPosition);

            var after = ProviderSnapshot.Capture(provider);
            Assert.That(provider, Is.SameAs(originalProviderReference),
                Context(generated, "The input contract must retain its original provider reference."));
            Assert.That(provider.EnvironmentObjectId, Is.SameAs(originalIdReference),
                Context(generated, "The provider's identifier reference must remain stable."));
            Assert.That(after, Is.EqualTo(before),
                Context(generated, "Contact processing must not mutate provider metadata."));

            if (before.Kind == EnvironmentObjectKind.Obstacle)
            {
                Assert.That(hit.HasValue, Is.True,
                    Context(generated, "Obstacle metadata must produce a hit payload."));
                Assert.That(coin.HasValue, Is.False,
                    Context(generated, "Obstacle metadata must not produce a coin payload."));
                AssertHitEquivalent(hit.Value, generated, before, originalProviderReference);
                return;
            }

            Assert.That(coin.HasValue, Is.True,
                Context(generated, "Coin metadata must produce a coin payload."));
            Assert.That(hit.HasValue, Is.False,
                Context(generated, "Coin metadata must not produce a hit payload."));
            AssertCoinEquivalent(coin.Value, generated, before, originalProviderReference);
        }

        private static void AssertHitEquivalent(
            PlayerHitEvent payload,
            GeneratedCase generated,
            ProviderSnapshot before,
            IEnvironmentObject originalProviderReference)
        {
            Assert.That(payload.EnvironmentObjectId, Is.EqualTo(before.EnvironmentObjectId),
                Context(generated, "The obstacle identifier must be equivalent."));
            Assert.That(payload.Obstacle, Is.SameAs(originalProviderReference),
                Context(generated, "The hit payload must retain the original provider reference."));
            Assert.That(payload.ContactPosition, Is.EqualTo(generated.ContactPosition),
                Context(generated, "The contact position must be equivalent."));
        }

        private static void AssertCoinEquivalent(
            CoinCollectedEvent payload,
            GeneratedCase generated,
            ProviderSnapshot before,
            IEnvironmentObject originalProviderReference)
        {
            Assert.That(payload.EnvironmentObjectId, Is.EqualTo(before.EnvironmentObjectId),
                Context(generated, "The coin identifier must be equivalent."));
            Assert.That(payload.Coin, Is.SameAs(originalProviderReference),
                Context(generated, "The coin payload must retain the original provider reference."));
            Assert.That(payload.CollectibleValue, Is.EqualTo(before.CollectibleValue),
                Context(generated, "The collectible value must be equivalent."));
        }

        private static string Context(GeneratedCase generated, string message)
        {
            return message + " Case=" + generated;
        }

        private sealed class MutableEnvironmentRecord : IEnvironmentObject
        {
            public MutableEnvironmentRecord(
                string environmentObjectId,
                EnvironmentObjectKind kind,
                float collectibleValue)
            {
                EnvironmentObjectId = environmentObjectId;
                Kind = kind;
                CollectibleValue = collectibleValue;
            }

            public string EnvironmentObjectId { get; set; }
            public EnvironmentObjectKind Kind { get; set; }
            public float CollectibleValue { get; set; }
        }

        private readonly struct ProviderSnapshot : IEquatable<ProviderSnapshot>
        {
            private ProviderSnapshot(
                string environmentObjectId,
                EnvironmentObjectKind kind,
                float collectibleValue)
            {
                EnvironmentObjectId = environmentObjectId;
                Kind = kind;
                CollectibleValue = collectibleValue;
            }

            public string EnvironmentObjectId { get; }
            public EnvironmentObjectKind Kind { get; }
            public float CollectibleValue { get; }

            public static ProviderSnapshot Capture(IEnvironmentObject value)
            {
                return new ProviderSnapshot(
                    value.EnvironmentObjectId,
                    value.Kind,
                    value.CollectibleValue);
            }

            public bool Equals(ProviderSnapshot other)
            {
                return string.Equals(
                           EnvironmentObjectId,
                           other.EnvironmentObjectId,
                           StringComparison.Ordinal) &&
                       Kind == other.Kind &&
                       CollectibleValue.Equals(other.CollectibleValue);
            }

            public override bool Equals(object obj)
            {
                return obj is ProviderSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = EnvironmentObjectId == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(EnvironmentObjectId);
                    return (hash * 397 ^ (int)Kind) * 397 ^ CollectibleValue.GetHashCode();
                }
            }
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                MutableEnvironmentRecord provider,
                int childColliderId,
                Vector3 contactPosition)
            {
                Provider = provider;
                ChildColliderId = childColliderId;
                ContactPosition = contactPosition;
            }

            public MutableEnvironmentRecord Provider { get; }
            public int ChildColliderId { get; }
            public Vector3 ContactPosition { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Child={0}, Id={1}, Kind={2}, Value={3:R}, Position={4}",
                    ChildColliderId,
                    Provider.EnvironmentObjectId,
                    Provider.Kind,
                    Provider.CollectibleValue,
                    ContactPosition);
            }
        }

        private sealed class ContactConsumptionSeam
        {
            private const string EventHubTypeName =
                "SubwaySurfers.Player.Domain.PlayerEventHub";
            private const string ContactTrackerTypeName =
                "SubwaySurfers.Player.Domain.EnvironmentContactTracker";

            private readonly object tracker;
            private readonly MethodInfo enter;

            private ContactConsumptionSeam(object tracker, Type trackerType)
            {
                this.tracker = tracker;
                enter = trackerType.GetMethod(
                    "Enter",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(int), typeof(IEnvironmentObject), typeof(Vector3) },
                    null);
                Assert.That(enter, Is.Not.Null,
                    ContactTrackerTypeName +
                    " must expose Enter(int, IEnvironmentObject, Vector3).");
                Assert.That(enter.ReturnType, Is.EqualTo(typeof(void)));
            }

            public static ContactConsumptionSeam Create(
                Action<PlayerHitEvent> hitSink,
                Action<CoinCollectedEvent> coinSink)
            {
                var runtimeAssembly = typeof(PlayerState).Assembly;
                var hubType = runtimeAssembly.GetType(EventHubTypeName);
                Assert.That(hubType, Is.Not.Null,
                    "Task 5.3 must provide " + EventHubTypeName + ".");
                var hubConstructor = hubType.GetConstructor(Type.EmptyTypes);
                Assert.That(hubConstructor, Is.Not.Null,
                    EventHubTypeName + " must expose PlayerEventHub().");
                var hub = hubConstructor.Invoke(Array.Empty<object>());

                AddHandler(hubType, hub, "PlayerHit", typeof(Action<PlayerHitEvent>), hitSink);
                AddHandler(
                    hubType,
                    hub,
                    "CoinCollected",
                    typeof(Action<CoinCollectedEvent>),
                    coinSink);

                var trackerType = runtimeAssembly.GetType(ContactTrackerTypeName);
                Assert.That(trackerType, Is.Not.Null,
                    "Task 5.3 must provide " + ContactTrackerTypeName + ".");
                var trackerConstructor = trackerType.GetConstructor(new[] { hubType });
                Assert.That(trackerConstructor, Is.Not.Null,
                    ContactTrackerTypeName +
                    " must expose EnvironmentContactTracker(PlayerEventHub).");
                return new ContactConsumptionSeam(
                    trackerConstructor.Invoke(new[] { hub }),
                    trackerType);
            }

            public void Enter(
                int childColliderId,
                IEnvironmentObject environmentObject,
                Vector3 contactPosition)
            {
                enter.Invoke(tracker, new object[]
                {
                    childColliderId,
                    environmentObject,
                    contactPosition
                });
            }

            private static void AddHandler(
                Type type,
                object instance,
                string name,
                Type handlerType,
                Delegate handler)
            {
                var eventInfo = type.GetEvent(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(eventInfo, Is.Not.Null, type.FullName + " must expose " + name + ".");
                Assert.That(eventInfo.EventHandlerType, Is.EqualTo(handlerType));
                eventInfo.AddEventHandler(instance, handler);
            }
        }
    }
}
