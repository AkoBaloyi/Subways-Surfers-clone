using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class LogicalContactPublicationPropertyTests
    {
        private const int Seed = 710051;
        private const int CaseCount = 160;

        // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**
        [Test]
        [Description("Feature: player-controller, Property 10: Logical contacts publish once with preserved identity")]
        public void LogicalContactsPublishOnceWithPreservedIdentity_Property10_Requirements_7_1_Through_7_6()
        {
            var caseIndex = 0;
            var coverage = new Coverage();

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                generated => AssertPublication(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.DuplicateEnter, Is.True);
            Assert.That(coverage.DuplicateExit, Is.True);
            Assert.That(coverage.MultiChildContact, Is.True);
            Assert.That(coverage.FullExit, Is.True);
            Assert.That(coverage.Reentry, Is.True);
            Assert.That(coverage.PublishedKinds,
                Is.EquivalentTo(new[] { EnvironmentObjectKind.Obstacle, EnvironmentObjectKind.Coin }));
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var sharedId = caseIndex % 5 == 0;
            var obstacleId = "obstacle-" + caseIndex.ToString(CultureInfo.InvariantCulture);
            var coinId = sharedId
                ? obstacleId
                : "coin-" + caseIndex.ToString(CultureInfo.InvariantCulture);
            var obstacle = new TestEnvironmentObject(
                obstacleId,
                EnvironmentObjectKind.Obstacle,
                GeneratedValues.NextFiniteFloat(random, 0.01f, 500f));
            var coin = new TestEnvironmentObject(
                coinId,
                EnvironmentObjectKind.Coin,
                GeneratedValues.NextFiniteFloat(random, 0.01f, 500f));

            var obstacleSteps = GenerateLifecycle(random, obstacle, caseIndex * 100 + 1);
            var coinSteps = GenerateLifecycle(random, coin, caseIndex * 100 + 51);
            var interleaved = Interleave(random, obstacleSteps, coinSteps);
            return new GeneratedCase(obstacle, coin, interleaved);
        }

        private static IReadOnlyList<ContactOperationCase> GenerateLifecycle(
            Random random,
            TestEnvironmentObject environmentObject,
            int childBase)
        {
            var childCount = random.Next(2, 5);
            var children = new List<int>(childCount);
            for (var index = 0; index < childCount; index++) children.Add(childBase + index);
            Shuffle(random, children);

            var steps = new List<ContactOperationCase>();
            for (var index = 0; index < children.Count; index++)
            {
                var position = NextPosition(random);
                steps.Add(ContactOperationCase.Enter(environmentObject, children[index], position));
                if (index == 0 || random.Next(2) == 0)
                {
                    steps.Add(ContactOperationCase.Enter(
                        environmentObject,
                        children[index],
                        NextPosition(random)));
                }
            }

            Shuffle(random, children);
            for (var index = 0; index < children.Count; index++)
            {
                steps.Add(ContactOperationCase.Exit(environmentObject, children[index]));
                if (index == 0 || random.Next(2) == 0)
                    steps.Add(ContactOperationCase.Exit(environmentObject, children[index]));
            }

            var reentryChild = children[random.Next(children.Count)];
            var reentryPosition = NextPosition(random);
            steps.Add(ContactOperationCase.Enter(environmentObject, reentryChild, reentryPosition));
            steps.Add(ContactOperationCase.Enter(environmentObject, reentryChild, NextPosition(random)));
            steps.Add(ContactOperationCase.Exit(environmentObject, reentryChild));
            return steps;
        }

        private static IReadOnlyList<ContactOperationCase> Interleave(
            Random random,
            IReadOnlyList<ContactOperationCase> first,
            IReadOnlyList<ContactOperationCase> second)
        {
            var result = new List<ContactOperationCase>(first.Count + second.Count);
            var firstIndex = 0;
            var secondIndex = 0;
            while (firstIndex < first.Count || secondIndex < second.Count)
            {
                var takeFirst = secondIndex >= second.Count ||
                    (firstIndex < first.Count && random.Next(2) == 0);
                result.Add(takeFirst ? first[firstIndex++] : second[secondIndex++]);
            }

            return result;
        }

        private static void Shuffle<T>(Random random, IList<T> values)
        {
            for (var index = values.Count - 1; index > 0; index--)
            {
                var replacement = random.Next(index + 1);
                var temporary = values[index];
                values[index] = values[replacement];
                values[replacement] = temporary;
            }
        }

        private static Vector3 NextPosition(Random random)
        {
            return new Vector3(
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f),
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f),
                GeneratedValues.NextFiniteFloat(random, -1000f, 1000f));
        }

        private static void AssertPublication(GeneratedCase generated, Coverage coverage)
        {
            var observations = new List<PublishedContact>();
            var seam = ContactPublicationSeam.Create(
                hit => observations.Add(PublishedContact.From(hit)),
                coin => observations.Add(PublishedContact.From(coin)));
            var active = new Dictionary<LogicalKey, HashSet<int>>();
            var completedLifecycles = new HashSet<LogicalKey>();
            var eventIds = new HashSet<ulong>();
            var contactIds = new HashSet<ulong>();
            var expectedPublications = 0;

            for (var stepIndex = 0; stepIndex < generated.Steps.Count; stepIndex++)
            {
                var step = generated.Steps[stepIndex];
                var key = new LogicalKey(
                    step.EnvironmentObject.EnvironmentObjectId,
                    step.EnvironmentObject.Kind);
                if (!active.TryGetValue(key, out var children))
                {
                    children = new HashSet<int>();
                    active.Add(key, children);
                }

                var countBefore = observations.Count;
                var expectedPublication = false;
                if (step.Operation == ContactOperation.Enter)
                {
                    var wasEmpty = children.Count == 0;
                    var added = children.Add(step.ChildColliderId);
                    coverage.DuplicateEnter |= !added;
                    coverage.MultiChildContact |= added && !wasEmpty;
                    expectedPublication = added && wasEmpty;
                    coverage.Reentry |= expectedPublication && completedLifecycles.Contains(key);
                    seam.Enter(step.ChildColliderId, step.EnvironmentObject, step.ContactPosition);
                }
                else
                {
                    var removed = children.Remove(step.ChildColliderId);
                    coverage.DuplicateExit |= !removed;
                    if (removed && children.Count == 0)
                    {
                        coverage.FullExit = true;
                        completedLifecycles.Add(key);
                    }
                    seam.Exit(step.ChildColliderId, step.EnvironmentObject);
                }

                if (!expectedPublication)
                {
                    Assert.That(observations.Count, Is.EqualTo(countBefore),
                        "Duplicate, additional-child, and exit callbacks must not publish. " +
                        Context(generated, stepIndex));
                    continue;
                }

                expectedPublications++;
                Assert.That(observations.Count, Is.EqualTo(countBefore + 1),
                    "The first child entry of a logical contact must publish exactly once. " +
                    Context(generated, stepIndex));
                var published = observations[observations.Count - 1];
                AssertPayload(published, step, generated, stepIndex);
                Assert.That(eventIds.Add(published.EventId), Is.True,
                    "Every contact event must have a unique EventId. " + Context(generated, stepIndex));
                Assert.That(contactIds.Add(published.ContactId), Is.True,
                    "A full exit and reentry must create a new ContactId. " + Context(generated, stepIndex));
                coverage.PublishedKinds.Add(published.Kind);
            }

            Assert.That(observations.Count, Is.EqualTo(expectedPublications));
            Assert.That(observations.Count, Is.EqualTo(4),
                "Each obstacle and coin lifecycle must publish on initial entry and reentry only.");
            foreach (var contactId in contactIds)
            {
                var count = 0;
                for (var index = 0; index < observations.Count; index++)
                    if (observations[index].ContactId == contactId) count++;
                Assert.That(count, Is.EqualTo(1),
                    "An uninterrupted logical contact may publish only once for its ContactId.");
            }
        }

        private static void AssertPayload(
            PublishedContact published,
            ContactOperationCase step,
            GeneratedCase generated,
            int stepIndex)
        {
            var context = Context(generated, stepIndex);
            Assert.That(published.Kind, Is.EqualTo(step.EnvironmentObject.Kind), context);
            Assert.That(published.EnvironmentObjectId,
                Is.EqualTo(step.EnvironmentObject.EnvironmentObjectId), context);
            Assert.That(published.EnvironmentObject,
                Is.SameAs(step.EnvironmentObject), context);

            if (published.Kind == EnvironmentObjectKind.Obstacle)
            {
                Assert.That(published.ContactPosition, Is.EqualTo(step.ContactPosition),
                    "PlayerHitEvent must preserve the first entry's world-space contact position. " + context);
                return;
            }

            Assert.That(published.CollectibleValue,
                Is.EqualTo(step.EnvironmentObject.CollectibleValue),
                "CoinCollectedEvent must preserve the configured collectible value. " + context);
        }

        private static string Context(GeneratedCase generated, int stepIndex)
        {
            return "Step=" + stepIndex.ToString(CultureInfo.InvariantCulture) + ", " +
                generated.Steps[stepIndex] + ", Case=" + generated;
        }

        private sealed class TestEnvironmentObject : IEnvironmentObject
        {
            public TestEnvironmentObject(
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
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                TestEnvironmentObject obstacle,
                TestEnvironmentObject coin,
                IReadOnlyList<ContactOperationCase> steps)
            {
                Obstacle = obstacle;
                Coin = coin;
                Steps = steps;
            }

            public TestEnvironmentObject Obstacle { get; }
            public TestEnvironmentObject Coin { get; }
            public IReadOnlyList<ContactOperationCase> Steps { get; }

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append("Obstacle=").Append(Obstacle.EnvironmentObjectId)
                    .Append(", Coin=").Append(Coin.EnvironmentObjectId)
                    .Append("@").Append(Coin.CollectibleValue.ToString("R", CultureInfo.InvariantCulture))
                    .Append(", Steps=[");
                for (var index = 0; index < Steps.Count; index++)
                {
                    if (index > 0) builder.Append("; ");
                    builder.Append(Steps[index]);
                }
                return builder.Append(']').ToString();
            }
        }

        private readonly struct ContactOperationCase
        {
            private ContactOperationCase(
                ContactOperation operation,
                TestEnvironmentObject environmentObject,
                int childColliderId,
                Vector3 contactPosition)
            {
                Operation = operation;
                EnvironmentObject = environmentObject;
                ChildColliderId = childColliderId;
                ContactPosition = contactPosition;
            }

            public ContactOperation Operation { get; }
            public TestEnvironmentObject EnvironmentObject { get; }
            public int ChildColliderId { get; }
            public Vector3 ContactPosition { get; }

            public static ContactOperationCase Enter(
                TestEnvironmentObject environmentObject,
                int childColliderId,
                Vector3 contactPosition)
            {
                return new ContactOperationCase(
                    ContactOperation.Enter,
                    environmentObject,
                    childColliderId,
                    contactPosition);
            }

            public static ContactOperationCase Exit(
                TestEnvironmentObject environmentObject,
                int childColliderId)
            {
                return new ContactOperationCase(
                    ContactOperation.Exit,
                    environmentObject,
                    childColliderId,
                    Vector3.zero);
            }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}({1}:{2},Child={3},Position={4})",
                    Operation,
                    EnvironmentObject.Kind,
                    EnvironmentObject.EnvironmentObjectId,
                    ChildColliderId,
                    ContactPosition);
            }
        }

        private readonly struct LogicalKey : IEquatable<LogicalKey>
        {
            public LogicalKey(string environmentObjectId, EnvironmentObjectKind kind)
            {
                EnvironmentObjectId = environmentObjectId;
                Kind = kind;
            }

            public string EnvironmentObjectId { get; }
            public EnvironmentObjectKind Kind { get; }

            public bool Equals(LogicalKey other)
            {
                return Kind == other.Kind && string.Equals(
                    EnvironmentObjectId,
                    other.EnvironmentObjectId,
                    StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is LogicalKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ((EnvironmentObjectId == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(EnvironmentObjectId)) * 397) ^ (int)Kind;
            }
        }

        private readonly struct PublishedContact
        {
            private PublishedContact(
                ulong eventId,
                ulong contactId,
                string environmentObjectId,
                IEnvironmentObject environmentObject,
                EnvironmentObjectKind kind,
                Vector3 contactPosition,
                float collectibleValue)
            {
                EventId = eventId;
                ContactId = contactId;
                EnvironmentObjectId = environmentObjectId;
                EnvironmentObject = environmentObject;
                Kind = kind;
                ContactPosition = contactPosition;
                CollectibleValue = collectibleValue;
            }

            public ulong EventId { get; }
            public ulong ContactId { get; }
            public string EnvironmentObjectId { get; }
            public IEnvironmentObject EnvironmentObject { get; }
            public EnvironmentObjectKind Kind { get; }
            public Vector3 ContactPosition { get; }
            public float CollectibleValue { get; }

            public static PublishedContact From(PlayerHitEvent value)
            {
                return new PublishedContact(
                    value.EventId,
                    value.ContactId,
                    value.EnvironmentObjectId,
                    value.Obstacle,
                    EnvironmentObjectKind.Obstacle,
                    value.ContactPosition,
                    0f);
            }

            public static PublishedContact From(CoinCollectedEvent value)
            {
                return new PublishedContact(
                    value.EventId,
                    value.ContactId,
                    value.EnvironmentObjectId,
                    value.Coin,
                    EnvironmentObjectKind.Coin,
                    Vector3.zero,
                    value.CollectibleValue);
            }
        }

        private sealed class Coverage
        {
            public bool DuplicateEnter;
            public bool DuplicateExit;
            public bool MultiChildContact;
            public bool FullExit;
            public bool Reentry;
            public readonly HashSet<EnvironmentObjectKind> PublishedKinds =
                new HashSet<EnvironmentObjectKind>();
        }

        private sealed class ContactPublicationSeam
        {
            private const string EventHubTypeName =
                "SubwaySurfers.Player.Domain.PlayerEventHub";
            private const string ContactTrackerTypeName =
                "SubwaySurfers.Player.Domain.EnvironmentContactTracker";

            private readonly object tracker;
            private readonly MethodInfo enter;
            private readonly MethodInfo exit;

            private ContactPublicationSeam(object tracker, Type trackerType)
            {
                this.tracker = tracker;
                enter = RequiredMethod(
                    trackerType,
                    "Enter",
                    typeof(int),
                    typeof(IEnvironmentObject),
                    typeof(Vector3));
                exit = RequiredMethod(
                    trackerType,
                    "Exit",
                    typeof(int),
                    typeof(IEnvironmentObject));
            }

            public static ContactPublicationSeam Create(
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
                AddHandler(hubType, hub, "CoinCollected", typeof(Action<CoinCollectedEvent>), coinSink);

                var trackerType = runtimeAssembly.GetType(ContactTrackerTypeName);
                Assert.That(trackerType, Is.Not.Null,
                    "Task 5.3 must provide " + ContactTrackerTypeName + ".");
                var trackerConstructor = trackerType.GetConstructor(new[] { hubType });
                Assert.That(trackerConstructor, Is.Not.Null,
                    ContactTrackerTypeName + " must expose EnvironmentContactTracker(PlayerEventHub).");
                var tracker = trackerConstructor.Invoke(new[] { hub });
                return new ContactPublicationSeam(tracker, trackerType);
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

            public void Exit(int childColliderId, IEnvironmentObject environmentObject)
            {
                exit.Invoke(tracker, new object[] { childColliderId, environmentObject });
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
                Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
                return method;
            }
        }
    }
}
