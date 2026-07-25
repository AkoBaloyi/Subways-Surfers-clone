using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class LaneRequestOrderingPropertyTests
    {
        private const int Seed = 410031;
        private const int CaseCount = 180;

        // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.9**
        [Test]
        [Description("Feature: player-controller, Property 3: Lane requests follow the ordered adjacent-lane model")]
        public void LaneRequestsFollowOrderedAdjacentLaneModel_Property3_Requirements_3_1_Through_3_5_And_3_9()
        {
            var generatedCaseIndex = 0;
            var coveredStates = new HashSet<PlayerState>();
            var coveredInitialLanes = new HashSet<LogicalLane>();
            var observedBoundaryNoOp = false;
            var observedAdjacentMove = false;

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, generatedCaseIndex++),
                generated => AssertSequence(
                    generated,
                    coveredStates,
                    coveredInitialLanes,
                    ref observedBoundaryNoOp,
                    ref observedAdjacentMove),
                render: generated => generated.ToString());

            Assert.That(Enum.GetValues(typeof(LogicalLane)),
                Is.EquivalentTo(new[] { LogicalLane.Left, LogicalLane.Center, LogicalLane.Right }));
            Assert.That(coveredStates,
                Is.EquivalentTo(new[] { PlayerState.Running, PlayerState.Jumping, PlayerState.Sliding }));
            Assert.That(coveredInitialLanes,
                Is.EquivalentTo(new[] { LogicalLane.Left, LogicalLane.Center, LogicalLane.Right }));
            Assert.That(observedBoundaryNoOp, Is.True);
            Assert.That(observedAdjacentMove, Is.True);
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var state = (PlayerState)(caseIndex % 3);
            var initialLane = (LogicalLane)((caseIndex / 3) % 3);
            var directions = new List<LaneDirection>();

            switch (initialLane)
            {
                case LogicalLane.Left:
                    directions.Add(LaneDirection.Left);
                    directions.Add(LaneDirection.Right);
                    break;
                case LogicalLane.Center:
                    var direction = random.Next(2) == 0
                        ? LaneDirection.Left
                        : LaneDirection.Right;
                    directions.Add(direction);
                    directions.Add(direction);
                    break;
                case LogicalLane.Right:
                    directions.Add(LaneDirection.Right);
                    directions.Add(LaneDirection.Left);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var count = random.Next(3, 19);
            while (directions.Count < count)
            {
                directions.Add(random.Next(2) == 0
                    ? LaneDirection.Left
                    : LaneDirection.Right);
            }

            var requests = new List<LaneRequest>(directions.Count);
            var order = (ulong)random.Next(0, 1000);
            for (var index = 0; index < directions.Count; index++)
            {
                order += (ulong)random.Next(1, 5);
                requests.Add(new LaneRequest(directions[index], order));
            }

            return new GeneratedCase(state, initialLane, requests);
        }

        private static void AssertSequence(
            GeneratedCase generated,
            ISet<PlayerState> coveredStates,
            ISet<LogicalLane> coveredInitialLanes,
            ref bool observedBoundaryNoOp,
            ref bool observedAdjacentMove)
        {
            coveredStates.Add(generated.State);
            coveredInitialLanes.Add(generated.InitialLane);

            var stateMachine = new PlayerStateMachine(
                CreateSnapshot(generated.State, generated.InitialLane),
                ImmutableValueSequence<string>.Empty);
            var planner = LanePlannerSeam.Create(generated.InitialLane);
            var expectedQueue = new List<LaneRequest>();

            for (var index = 0; index < generated.Requests.Count; index++)
            {
                var request = generated.Requests[index];
                var acceptance = stateMachine.RequestLane(request.Direction);
                Assert.That(acceptance.Status, Is.EqualTo(CommandStatus.Accepted),
                    Context(generated, index));
                Assert.That(acceptance.Command, Is.EqualTo(PlayerCommandKind.Lane),
                    Context(generated, index));
                Assert.That(acceptance.Reason, Is.EqualTo(RejectionReason.None),
                    Context(generated, index));

                planner.Enqueue(request);
                expectedQueue.Add(request);
                AssertQueue(planner.Queue, expectedQueue, generated, index, "enqueue");
            }

            var expectedCurrent = generated.InitialLane;
            for (var completionIndex = 0; completionIndex < generated.Requests.Count; completionIndex++)
            {
                var request = generated.Requests[completionIndex];
                Assert.That(planner.Queue[0], Is.EqualTo(request),
                    "The next completion must use the FIFO queue head. " + Context(generated, completionIndex));

                var expectedTarget = AdjacentOrBoundary(expectedCurrent, request.Direction);
                var completed = planner.CompleteNextRequest();
                Assert.That(completed, Is.EqualTo(request),
                    "Completion must identify the oldest accepted request. " + Context(generated, completionIndex));

                var laneDelta = Math.Abs((int)expectedTarget - (int)expectedCurrent);
                Assert.That(laneDelta, Is.LessThanOrEqualTo(1),
                    "A request may select at most one adjacent lane. " + Context(generated, completionIndex));

                if (expectedTarget == expectedCurrent)
                    observedBoundaryNoOp = true;
                else
                    observedAdjacentMove = true;

                expectedCurrent = expectedTarget;
                expectedQueue.RemoveAt(0);
                Assert.That(planner.CurrentLane, Is.EqualTo(expectedCurrent), Context(generated, completionIndex));
                Assert.That(planner.TargetLane, Is.EqualTo(expectedCurrent), Context(generated, completionIndex));
                AssertQueue(planner.Queue, expectedQueue, generated, completionIndex, "completion");
            }
        }

        private static void AssertQueue(
            IReadOnlyList<LaneRequest> actual,
            IReadOnlyList<LaneRequest> expected,
            GeneratedCase generated,
            int requestIndex,
            string phase)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count),
                "Queue count after " + phase + ". " + Context(generated, requestIndex));
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index], Is.EqualTo(expected[index]),
                    string.Format(CultureInfo.InvariantCulture,
                        "Queue order after {0}, queue index {1}. {2}",
                        phase, index, Context(generated, requestIndex)));
                if (index > 0)
                {
                    Assert.That(actual[index - 1].RequestOrder,
                        Is.LessThan(actual[index].RequestOrder),
                        "Queued Request_Order values must remain ascending. " +
                        Context(generated, requestIndex));
                }
            }
        }

        private static LogicalLane AdjacentOrBoundary(
            LogicalLane current,
            LaneDirection direction)
        {
            if (direction == LaneDirection.Left)
                return current == LogicalLane.Left ? LogicalLane.Left : current - 1;
            if (direction == LaneDirection.Right)
                return current == LogicalLane.Right ? LogicalLane.Right : current + 1;
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        private static PlayerSnapshot CreateSnapshot(PlayerState state, LogicalLane lane)
        {
            return new PlayerSnapshot(
                Vector3.zero,
                Quaternion.identity,
                state,
                state != PlayerState.Jumping,
                10f,
                lane,
                lane,
                (float)(int)lane,
                (float)(int)lane,
                (float)(int)lane,
                0f,
                0f,
                0f,
                new ColliderProfile(0.5f, 2f, Vector3.up),
                ImmutableValueSequence<LaneRequest>.Empty,
                ImmutableValueSequence<PlayerCommandKind>.Empty,
                false);
        }

        private static string Context(GeneratedCase generated, int requestIndex)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "State={0}, InitialLane={1}, RequestIndex={2}",
                generated.State,
                generated.InitialLane,
                requestIndex);
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                PlayerState state,
                LogicalLane initialLane,
                IReadOnlyList<LaneRequest> requests)
            {
                State = state;
                InitialLane = initialLane;
                Requests = requests;
            }

            public PlayerState State { get; }
            public LogicalLane InitialLane { get; }
            public IReadOnlyList<LaneRequest> Requests { get; }

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append("State=").Append(State)
                    .Append(", InitialLane=").Append(InitialLane)
                    .Append(", Requests=[");
                for (var index = 0; index < Requests.Count; index++)
                {
                    if (index > 0) builder.Append("; ");
                    builder.Append(Requests[index].RequestOrder)
                        .Append(':').Append(Requests[index].Direction);
                }

                return builder.Append(']').ToString();
            }
        }

        private sealed class LanePlannerSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.LanePlanner";

            private readonly object instance;
            private readonly PropertyInfo currentLane;
            private readonly PropertyInfo targetLane;
            private readonly PropertyInfo queue;
            private readonly MethodInfo enqueue;
            private readonly MethodInfo completeNextRequest;

            private LanePlannerSeam(object instance, Type type)
            {
                this.instance = instance;
                currentLane = RequiredProperty(type, "CurrentLane", typeof(LogicalLane));
                targetLane = RequiredProperty(type, "TargetLane", typeof(LogicalLane));
                queue = type.GetProperty("LaneRequestQueue", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(queue, Is.Not.Null,
                    RuntimeTypeName + " must expose LaneRequestQueue.");
                Assert.That(typeof(IReadOnlyList<LaneRequest>).IsAssignableFrom(queue.PropertyType), Is.True,
                    RuntimeTypeName + ".LaneRequestQueue must implement IReadOnlyList<LaneRequest>.");
                enqueue = RequiredMethod(type, "Enqueue", typeof(LaneRequest));
                Assert.That(enqueue.ReturnType, Is.EqualTo(typeof(void)));
                completeNextRequest = RequiredMethod(type, "CompleteNextRequest");
                Assert.That(completeNextRequest.ReturnType, Is.EqualTo(typeof(LaneRequest)));
            }

            public LogicalLane CurrentLane =>
                (LogicalLane)currentLane.GetValue(instance);

            public LogicalLane TargetLane =>
                (LogicalLane)targetLane.GetValue(instance);

            public IReadOnlyList<LaneRequest> Queue =>
                (IReadOnlyList<LaneRequest>)queue.GetValue(instance);

            public static LanePlannerSeam Create(LogicalLane initialLane)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 4.3 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[] { typeof(LogicalLane) });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose LanePlanner(LogicalLane initialLane).");
                return new LanePlannerSeam(
                    constructor.Invoke(new object[] { initialLane }),
                    type);
            }

            public void Enqueue(LaneRequest request)
            {
                enqueue.Invoke(instance, new object[] { request });
            }

            public LaneRequest CompleteNextRequest()
            {
                return (LaneRequest)completeNextRequest.Invoke(instance, Array.Empty<object>());
            }

            private static PropertyInfo RequiredProperty(
                Type type,
                string name,
                Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null, type.FullName + " must expose " + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType));
                return property;
            }

            private static MethodInfo RequiredMethod(
                Type type,
                string name,
                params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    parameterTypes,
                    null);
                Assert.That(method, Is.Not.Null, type.FullName + " must expose " + name + ".");
                return method;
            }
        }
    }
}
