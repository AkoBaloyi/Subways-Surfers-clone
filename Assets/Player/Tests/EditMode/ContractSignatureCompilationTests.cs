using System;
using System.Linq;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ContractSignatureCompilationTests
    {
        [Test]
        public void CommandAndQueryInterfacesExposeExactPublicSignatures_Requirements_14_1_14_2_14_9_14_10()
        {
            AssertMethod(typeof(IPlayerCommands), "RequestLane", typeof(ActionRequestResult), typeof(LaneDirection));
            AssertMethod(typeof(IPlayerCommands), "RequestJump", typeof(ActionRequestResult));
            AssertMethod(typeof(IPlayerCommands), "RequestSlide", typeof(ActionRequestResult));
            AssertMethod(typeof(IPlayerCommands), "RequestFailure", typeof(FailureCommandResult));
            AssertMethod(typeof(IPlayerCommands), "RequestReset", typeof(ResetRequestResult), typeof(string));
            AssertMethod(typeof(IPlayerCommands), "SetForwardSpeed", typeof(SpeedSetResult), typeof(float));
            AssertGetter(typeof(IPlayerQueries), "CurrentState", typeof(PlayerState));
            AssertGetter(typeof(IPlayerQueries), "IsGrounded", typeof(bool));
            AssertGetter(typeof(IPlayerQueries), "ForwardSpeed", typeof(float));
            AssertGetter(typeof(IPlayerQueries), "Snapshot", typeof(PlayerSnapshot));
        }

        [Test]
        public void EventMarkerAndAnimationInterfacesCompileWithOwnedPayloads_Requirements_7_12_7_13_14_3_14_11_14_12()
        {
            AssertEvent<PlayerHitEvent>("PlayerHit");
            AssertEvent<CoinCollectedEvent>("CoinCollected");
            AssertEvent<PlayerStateChangedEvent>("StateChanged");
            AssertEvent<PlayerResetStartedEvent>("ResetStarted");
            AssertEvent<PlayerResetCompletedEvent>("ResetCompleted");
            AssertEvent<ValidationDiagnostic>("ValidationReported");
            AssertGetter(typeof(IEnvironmentObject), "EnvironmentObjectId", typeof(string));
            AssertGetter(typeof(IEnvironmentObject), "Kind", typeof(EnvironmentObjectKind));
            AssertGetter(typeof(IEnvironmentObject), "CollectibleValue", typeof(float));
            Assert.That(typeof(IRunningSurface).IsInterface, Is.True);
            Assert.That(typeof(IEnvironmentObstruction).IsInterface, Is.True);
            AssertMethod(typeof(IAnimationReceiver), "TryApply", typeof(bool), typeof(AnimationCommand));
        }

        private static void AssertMethod(Type owner, string name, Type result, params Type[] parameters)
        {
            var method = owner.GetMethod(name, parameters);
            Assert.That(method, Is.Not.Null, owner.Name + "." + name);
            Assert.That(method.ReturnType, Is.EqualTo(result));
        }

        private static void AssertGetter(Type owner, string name, Type propertyType)
        {
            var property = owner.GetProperty(name);
            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(propertyType));
            Assert.That(property.CanRead, Is.True);
            Assert.That(property.CanWrite, Is.False);
        }

        private static void AssertEvent<T>(string name)
        {
            Assert.That(typeof(IPlayerEventSource).GetEvent(name).EventHandlerType, Is.EqualTo(typeof(Action<T>)));
        }
    }
}
