using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ContractTests
    {
        [Test]
        public void PublicConsumersCompileAgainstCompletePlayerOwnedContracts_Requirements_1_2_1_6_14_1_14_2_14_3()
        {
            Assert.That(typeof(IPlayerCommands).GetMethods().Select(x => x.Name),
                Is.EquivalentTo(new[] { "RequestLane", "RequestJump", "RequestSlide", "RequestFailure", "RequestReset", "SetForwardSpeed" }));
            Assert.That(typeof(IPlayerQueries).GetProperties().Select(x => x.Name),
                Is.EquivalentTo(new[] { "CurrentState", "IsGrounded", "ForwardSpeed", "Snapshot" }));
            Assert.That(typeof(IPlayerEventSource).GetEvents().Select(x => x.Name),
                Is.EquivalentTo(new[] { "PlayerHit", "CoinCollected", "StateChanged", "ResetStarted", "ResetCompleted", "ValidationReported" }));
            Assert.That(typeof(IFailureCommandContract).GetMethod("RequestFailure").ReturnType, Is.EqualTo(typeof(FailureCommandResult)));
            Assert.That(typeof(IResetRequestContract).GetMethod("RequestReset").ReturnType, Is.EqualTo(typeof(ResetRequestResult)));
            Assert.That(typeof(IForwardSpeedApi).GetProperty("ForwardSpeed").PropertyType, Is.EqualTo(typeof(float)));
            Assert.That(typeof(IPlayerStateQuery).GetProperty("CurrentState").PropertyType, Is.EqualTo(typeof(PlayerState)));
            Assert.That(typeof(IGroundedStatusQuery).GetProperty("IsGrounded").PropertyType, Is.EqualTo(typeof(bool)));
        }

        [Test]
        public void EnvironmentAndAnimationContractsAreReadOnlyPlayerOwnedBoundaries_Requirements_1_3_1_4_7_12_7_13_14_10_14_11()
        {
            Assert.That(typeof(IEnvironmentObject).GetProperties().All(x => !x.CanWrite), Is.True);
            Assert.That(typeof(IEnvironmentObject).GetProperties().Select(x => x.Name),
                Is.EquivalentTo(new[] { "EnvironmentObjectId", "Kind", "CollectibleValue" }));
            Assert.That(typeof(IRunningSurface).GetMethods(), Is.Empty);
            Assert.That(typeof(IEnvironmentObstruction).GetMethods(), Is.Empty);
            Assert.That(typeof(IAnimationReceiver).GetMethod("TryApply").ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(typeof(EnvironmentContactData).GetProperties().Select(x => x.Name),
                Is.EquivalentTo(new[] { "ContactId", "EnvironmentObject", "ContactPosition" }));
        }
    }
}
