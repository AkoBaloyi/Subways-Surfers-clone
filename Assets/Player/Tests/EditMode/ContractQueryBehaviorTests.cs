using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ContractQueryBehaviorTests
    {
        [Test]
        public void PublicQueriesReturnCurrentValuesWithoutConcreteFacadeAccess_Requirements_14_2_14_9_14_10()
        {
            var probe = new QueryProbe();
            IPlayerQueries queries = probe;

            probe.Set(PlayerState.Jumping, false, 12.5f, default(PlayerSnapshot));
            Assert.That(queries.CurrentState, Is.EqualTo(PlayerState.Jumping));
            Assert.That(queries.IsGrounded, Is.False);
            Assert.That(queries.ForwardSpeed, Is.EqualTo(12.5f));

            var updatedSnapshot = default(PlayerSnapshot);
            probe.Set(PlayerState.Running, true, 8f, updatedSnapshot);
            Assert.That(queries.CurrentState, Is.EqualTo(PlayerState.Running));
            Assert.That(queries.IsGrounded, Is.True);
            Assert.That(queries.ForwardSpeed, Is.EqualTo(8f));
            Assert.That(queries.Snapshot, Is.EqualTo(updatedSnapshot));
        }

        private sealed class QueryProbe : IPlayerQueries
        {
            public PlayerState CurrentState { get; private set; }
            public bool IsGrounded { get; private set; }
            public float ForwardSpeed { get; private set; }
            public PlayerSnapshot Snapshot { get; private set; }

            public void Set(PlayerState state, bool grounded, float speed, PlayerSnapshot snapshot)
            {
                CurrentState = state;
                IsGrounded = grounded;
                ForwardSpeed = speed;
                Snapshot = snapshot;
            }
        }
    }
}
