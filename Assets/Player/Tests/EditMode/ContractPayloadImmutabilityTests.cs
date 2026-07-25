using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ContractPayloadImmutabilityTests
    {
        [Test]
        public void CommandResultsSnapshotsAndDiagnosticsAreImmutable_Requirements_14_1_14_2_14_12()
        {
            var payloads = new[]
            {
                typeof(ActionRequestResult), typeof(FailureCommandResult), typeof(ResetRequestResult),
                typeof(SpeedSetResult), typeof(PlayerSnapshot), typeof(EnvironmentContactData),
                typeof(ValidationDiagnostic)
            };
            foreach (var payload in payloads) AssertImmutable(payload);
        }

        [Test]
        public void EventPayloadsAreImmutableAndComplete_Requirements_7_2_7_5_7_8_7_10_7_11_14_3()
        {
            AssertPayload<PlayerHitEvent>("EventId", "ContactId", "EnvironmentObjectId", "Obstacle", "ContactPosition");
            AssertPayload<CoinCollectedEvent>("EventId", "ContactId", "EnvironmentObjectId", "Coin", "CollectibleValue");
            AssertPayload<PlayerStateChangedEvent>("EventId", "PreviousState", "CurrentState", "TransitionCause");
            AssertPayload<PlayerResetStartedEvent>("EventId", "RequestId");
            AssertPayload<PlayerResetCompletedEvent>("EventId", "RequestId", "ResultingState");
        }

        [Test]
        public void DiagnosticPayloadExposesCategorizedObservableFields_Requirements_8_8_14_12()
        {
            var names = typeof(ValidationDiagnostic).GetProperties().Select(property => property.Name).ToArray();
            Assert.That(names, Does.Contain("Severity"));
            Assert.That(names, Does.Contain("Code"));
            Assert.That(names, Does.Contain("Field"));
            Assert.That(names, Does.Contain("Message"));
        }

        private static void AssertPayload<T>(params string[] expectedProperties)
        {
            AssertImmutable(typeof(T));
            Assert.That(typeof(T).GetProperties().Select(property => property.Name), Is.EquivalentTo(expectedProperties));
        }

        private static void AssertImmutable(Type payload)
        {
            Assert.That(payload.IsValueType, Is.True, payload.Name + " must be a value type.");
            Assert.That(payload.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"), Is.True,
                payload.Name + " must be declared readonly.");
            Assert.That(payload.GetProperties().All(property => property.CanRead && !property.CanWrite), Is.True);
            Assert.That(payload.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That(payload.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(field => field.IsInitOnly), Is.True);
            Assert.That(payload.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Any(), Is.True,
                payload.Name + " must expose an explicit constructor.");
        }
    }
}
