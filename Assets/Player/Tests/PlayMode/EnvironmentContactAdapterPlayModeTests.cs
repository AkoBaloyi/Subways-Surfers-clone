using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode coverage for the overlap-diff contact adapter that reconciles the player capsule
    /// against Lucky-owned <see cref="IEnvironmentObject"/> geometry after every physics step.
    ///
    /// The facts pinned here are the ones a callback-driven adapter gets wrong. Contact lifetime comes
    /// from the sampled overlap set, not from callback arrival: a controller hit for geometry the
    /// capsule does not overlap must never open a contact, and it must never close one. A logical
    /// contact spans every child collider of one environment identity, so a partial child exit keeps
    /// the contact open and republishes nothing, while a full exit followed by reentry is a new
    /// contact interval with a new identifier. Sampling itself is a steady per-step path and must not
    /// allocate.
    ///
    /// Logical contacts are routed through the existing <see cref="EnvironmentContactTracker"/> and
    /// <see cref="PlayerEventHub"/>; this fixture observes the published player-domain events rather
    /// than any adapter-local bookkeeping. All geometry is player-owned test doubles implementing only
    /// public Player contracts, and every fixture is built programmatically.
    /// </summary>
    public sealed class EnvironmentContactAdapterPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float PositionTolerance = 0.0005f;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;
        private static readonly Vector3 PlayerOrigin = Vector3.zero;

        /// <summary>The capsule center in world space for a player standing at the fixture origin.</summary>
        private static Vector3 CapsuleCenter
        {
            get { return PlayerOrigin + Defaults.BaselineCollider.Center; }
        }

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<PlayerHitEvent> hits = new List<PlayerHitEvent>();
        private readonly List<CoinCollectedEvent> coins = new List<CoinCollectedEvent>();
        private readonly List<ValidationDiagnostic> diagnostics = new List<ValidationDiagnostic>();

        private PlayerEventHub eventHub;
        private EnvironmentContactTracker tracker;
        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;

        [SetUp]
        public void SetUp()
        {
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            Time.fixedDeltaTime = FixedStep;
            Time.maximumDeltaTime = FixedStep;

            hits.Clear();
            coins.Clear();
            diagnostics.Clear();

            eventHub = new PlayerEventHub();
            eventHub.PlayerHit += hits.Add;
            eventHub.CoinCollected += coins.Add;
            eventHub.ValidationReported += diagnostics.Add;
            tracker = new EnvironmentContactTracker(eventHub);
        }

        [TearDown]
        public void TearDown()
        {
            ClearFixtures();
            tracker = null;
            eventHub = null;
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 7.1, 7.4, 12.8, 14.11**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the capsule is sampled once per physics step without allocating")]
        public IEnumerator SamplesPlayerCapsuleOncePerPhysicsStepWithoutAllocating_Requirements_7_1_7_4_12_8_And_14_11()
        {
            var seam = CreateAdapter();
            var obstacle = CreateEnvironmentObject(
                "SteadyObstacle", EnvironmentObjectKind.Obstacle, 0f, 1);
            yield return new WaitForFixedUpdate();

            // Automatic sampling: exactly one reconciliation per physics step, no more and no fewer.
            var before = seam.ReconcileCount;
            yield return new WaitForFixedUpdate();
            var afterOneStep = seam.ReconcileCount;
            yield return new WaitForFixedUpdate();
            var afterTwoSteps = seam.ReconcileCount;

            Assert.That(afterOneStep - before, Is.EqualTo(1),
                "One physics step must reconcile the sampled overlap set exactly once. " +
                Describe(seam, obstacle));
            Assert.That(afterTwoSteps - afterOneStep, Is.EqualTo(1),
                "Every physics step must reconcile the sampled overlap set exactly once. " +
                Describe(seam, obstacle));

            seam.SuspendAutomaticSampling();

            Assert.That(seam.SampledColliderCount, Is.EqualTo(1),
                "The sample must contain the overlapping environment collider and exclude the " +
                "player's own capsule. " + Describe(seam, obstacle));
            Assert.That(hits.Count, Is.EqualTo(1),
                "An uninterrupted obstacle contact must publish exactly one Player_Hit_Event. " +
                Describe(seam, obstacle));

            // The first reconciliation pays one-time managed costs, so a few warm-up passes put the
            // measured region on the steady per-step path with an unchanged overlap set.
            for (var warmUp = 0; warmUp < 4; warmUp++) seam.Reconcile();

            var reconcile = seam.Reconcile;
            Assert.That(
                () =>
                {
                    for (var step = 0; step < 8; step++) reconcile();
                },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "Per-step capsule sampling and overlap diffing must not allocate. " +
                Describe(seam, obstacle));

            Assert.That(hits.Count, Is.EqualTo(1),
                "Repeated sampling of an unchanged overlap set must not republish. " +
                Describe(seam, obstacle));
            Assert.That(coins, Is.Empty,
                "Obstacle contacts must not publish Coin_Collected_Events. " +
                Describe(seam, obstacle));
            Assert.That(diagnostics, Is.Empty,
                "Valid environment metadata must not produce diagnostics. " +
                Describe(seam, obstacle));
        }

        // **Validates: Requirements 7.1, 7.3, 7.4, 7.6, 12.8**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: child collider identities are diffed against the previous sample")]
        public IEnumerator DiffsChildColliderIdentitiesAgainstPreviousSample_Requirements_7_1_7_3_7_4_7_6_And_12_8()
        {
            var seam = CreateAdapter();
            var obstacle = CreateEnvironmentObject(
                "MultiColliderObstacle", EnvironmentObjectKind.Obstacle, 0f, 3);
            yield return new WaitForFixedUpdate();
            seam.SuspendAutomaticSampling();

            seam.Reconcile();
            Assert.That(seam.SampledColliderCount, Is.EqualTo(3),
                "Every overlapping child collider of one environment identity must be sampled. " +
                Describe(seam, obstacle));
            Assert.That(hits.Count, Is.EqualTo(1),
                "Three child colliders of one obstacle identity must publish exactly one " +
                "Player_Hit_Event. " + Describe(seam, obstacle));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "One environment identity must produce one logical contact. " +
                Describe(seam, obstacle));

            var firstContactId = hits[0].ContactId;

            // An unchanged sample is a no-op diff: no identity entered, so nothing is published.
            for (var step = 0; step < 3; step++) seam.Reconcile();
            Assert.That(hits.Count, Is.EqualTo(1),
                "Sampling the same child identities again must publish nothing new. " +
                Describe(seam, obstacle));

            // A partial child exit is a diff, but not the end of the logical contact.
            SetChildColliderEnabled(obstacle, 0, false);
            seam.Reconcile();
            Assert.That(seam.SampledColliderCount, Is.EqualTo(2),
                "The sample must drop the child collider that left. " + Describe(seam, obstacle));
            Assert.That(hits.Count, Is.EqualTo(1),
                "A partial child exit must not republish. " + Describe(seam, obstacle));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "The logical contact must stay open while any child collider still overlaps. " +
                Describe(seam, obstacle));

            // Re-entering the same child of an already-open contact is not a new contact interval.
            SetChildColliderEnabled(obstacle, 0, true);
            seam.Reconcile();
            Assert.That(hits.Count, Is.EqualTo(1),
                "A child collider rejoining an open logical contact must not republish. " +
                Describe(seam, obstacle));
            Assert.That(hits[0].ContactId, Is.EqualTo(firstContactId),
                "The open logical contact must keep its Contact_Id. " + Describe(seam, obstacle));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "Child churn must not split one identity into several logical contacts. " +
                Describe(seam, obstacle));
        }

        // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: enter and exit samples are deterministic per environment identity")]
        public IEnumerator EmitsDeterministicEnterAndExitSamplesPerIdentity_Requirements_7_1_Through_7_6()
        {
            var seam = CreateAdapter();
            var obstacle = CreateEnvironmentObject(
                "DeterministicObstacle", EnvironmentObjectKind.Obstacle, 0f, 2);
            var coin = CreateEnvironmentObject(
                "DeterministicCoin", EnvironmentObjectKind.Coin, 7.5f, 1);
            yield return new WaitForFixedUpdate();
            seam.SuspendAutomaticSampling();

            seam.Reconcile();

            Assert.That(hits.Count, Is.EqualTo(1),
                "The obstacle identity must publish exactly one Player_Hit_Event. " +
                Describe(seam, obstacle));
            Assert.That(coins.Count, Is.EqualTo(1),
                "The coin identity must publish exactly one Coin_Collected_Event. " +
                Describe(seam, coin));
            Assert.That(hits[0].EnvironmentObjectId, Is.EqualTo("DeterministicObstacle"),
                "The hit payload must carry the Lucky-supplied Environment_Object_Id. " +
                Describe(seam, obstacle));
            Assert.That(coins[0].EnvironmentObjectId, Is.EqualTo("DeterministicCoin"),
                "The coin payload must carry the Lucky-supplied Environment_Object_Id. " +
                Describe(seam, coin));
            Assert.That(coins[0].CollectibleValue, Is.EqualTo(7.5f).Within(PositionTolerance),
                "The coin payload must carry the configured collectible value. " +
                Describe(seam, coin));
            Assert.That(hits[0].EventId, Is.Not.EqualTo(coins[0].EventId),
                "Each published player-domain event must carry a unique Event_Id. " +
                Describe(seam, obstacle));
            Assert.That(hits[0].ContactId, Is.Not.EqualTo(coins[0].ContactId),
                "Distinct environment identities must carry distinct Contact_Ids. " +
                Describe(seam, obstacle));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(2),
                "Two overlapping environment identities must produce two logical contacts. " +
                Describe(seam, obstacle));

            // A full exit of one identity leaves the other contact untouched.
            SetChildColliderEnabled(coin, 0, false);
            seam.Reconcile();
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "A fully exited identity must end its logical contact. " + Describe(seam, coin));
            Assert.That(coins.Count, Is.EqualTo(1),
                "An exit must not publish a contact event. " + Describe(seam, coin));
            Assert.That(hits.Count, Is.EqualTo(1),
                "One identity leaving must not disturb another open contact. " +
                Describe(seam, obstacle));

            SetChildColliderEnabled(obstacle, 0, false);
            SetChildColliderEnabled(obstacle, 1, false);
            seam.Reconcile();
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(0),
                "Every fully exited identity must end its logical contact. " +
                Describe(seam, obstacle));
            Assert.That(seam.SampledColliderCount, Is.EqualTo(0),
                "An empty overlap set must sample no environment colliders. " +
                Describe(seam, obstacle));
            Assert.That(hits.Count, Is.EqualTo(1),
                "Exits must not publish contact events. " + Describe(seam, obstacle));
            Assert.That(coins.Count, Is.EqualTo(1),
                "Exits must not publish contact events. " + Describe(seam, coin));
            Assert.That(diagnostics, Is.Empty,
                "Valid environment metadata must not produce diagnostics. " +
                Describe(seam, obstacle));
        }

        // **Validates: Requirements 7.2, 7.3, 7.6, 14.11**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: controller hits supply contact positions only, never contact lifetime")]
        public IEnumerator ControllerHitsSupplyPositionsOnlyNeverLifetime_Requirements_7_2_7_3_7_6_And_14_11()
        {
            var seam = CreateAdapter();
            var obstacle = CreateEnvironmentObject(
                "PositionObstacle", EnvironmentObjectKind.Obstacle, 0f, 1);
            var distant = CreateEnvironmentObject(
                "DistantObstacle", EnvironmentObjectKind.Obstacle, 0f, 1,
                CapsuleCenter + new Vector3(0f, 0f, 25f));
            yield return new WaitForFixedUpdate();
            seam.SuspendAutomaticSampling();

            var distantCollider = ChildCollider(distant, 0);
            var distantPosition = distant.transform.position;
            var distantChildPosition = distantCollider.transform.position;
            var distantLayer = distantCollider.gameObject.layer;
            var distantEnabled = distantCollider.enabled;

            // A controller hit for geometry the capsule does not overlap must not open a contact.
            seam.RecordContactPosition(distantCollider, new Vector3(11f, 12f, 13f));
            seam.Reconcile();
            Assert.That(hits.Count, Is.EqualTo(1),
                "Only the overlapping identity may publish; a controller hit must not open a " +
                "contact for non-overlapping geometry. " + Describe(seam, distant));
            Assert.That(hits[0].EnvironmentObjectId, Is.EqualTo("PositionObstacle"),
                "The published contact must belong to the overlapping identity. " +
                Describe(seam, obstacle));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "A controller hit must not create a logical contact by itself. " +
                Describe(seam, distant));

            // The adapter only reads Lucky-owned geometry.
            Assert.That(distant.transform.position, Is.EqualTo(distantPosition),
                "The adapter must not move Lucky-owned geometry. " + Describe(seam, distant));
            Assert.That(distantCollider.transform.position, Is.EqualTo(distantChildPosition),
                "The adapter must not move Lucky-owned child colliders. " +
                Describe(seam, distant));
            Assert.That(distantCollider.gameObject.layer, Is.EqualTo(distantLayer),
                "The adapter must not relayer Lucky-owned geometry. " + Describe(seam, distant));
            Assert.That(distantCollider.enabled, Is.EqualTo(distantEnabled),
                "The adapter must not disable Lucky-owned colliders. " + Describe(seam, distant));

            // A controller hit must not close an open contact either.
            seam.RecordContactPosition(ChildCollider(obstacle, 0), new Vector3(1f, 2f, 3f));
            seam.Reconcile();
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "A controller hit must not end a contact the overlap sample still reports. " +
                Describe(seam, obstacle));
            Assert.That(hits.Count, Is.EqualTo(1),
                "A controller hit for an open contact must not republish. " +
                Describe(seam, obstacle));

            // A recorded hit position is the contact position of the next contact interval.
            SetChildColliderEnabled(obstacle, 0, false);
            seam.Reconcile();
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(0),
                "The logical contact must end when the overlap sample drops every child. " +
                Describe(seam, obstacle));

            var recorded = new Vector3(0.25f, 1.5f, -0.75f);
            SetChildColliderEnabled(obstacle, 0, true);
            seam.RecordContactPosition(ChildCollider(obstacle, 0), recorded);
            seam.Reconcile();
            Assert.That(hits.Count, Is.EqualTo(2),
                "Reentry must publish one further Player_Hit_Event. " + Describe(seam, obstacle));
            Assert.That((hits[1].ContactPosition - recorded).magnitude,
                Is.LessThanOrEqualTo(PositionTolerance),
                "The recorded controller-hit point must be the published contact position. " +
                Describe(seam, obstacle));
        }

        // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: full exit and reentry publishes a new contact interval")]
        public IEnumerator AllowsPublicationAgainAfterFullExitAndReentry_Requirements_7_1_7_2_7_3_7_4_And_7_6()
        {
            var seam = CreateAdapter();
            var coin = CreateEnvironmentObject(
                "ReentryCoin", EnvironmentObjectKind.Coin, 3f, 2);
            yield return new WaitForFixedUpdate();
            seam.SuspendAutomaticSampling();

            seam.Reconcile();
            Assert.That(coins.Count, Is.EqualTo(1),
                "The first contact interval must publish exactly one Coin_Collected_Event. " +
                Describe(seam, coin));

            // Only a full exit ends the interval, so the first child exit must not rearm publication.
            SetChildColliderEnabled(coin, 0, false);
            seam.Reconcile();
            SetChildColliderEnabled(coin, 0, true);
            seam.Reconcile();
            Assert.That(coins.Count, Is.EqualTo(1),
                "A partial exit must not rearm publication. " + Describe(seam, coin));

            SetChildColliderEnabled(coin, 0, false);
            SetChildColliderEnabled(coin, 1, false);
            seam.Reconcile();
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(0),
                "A full exit must end the logical contact. " + Describe(seam, coin));
            Assert.That(coins.Count, Is.EqualTo(1),
                "An exit must not publish. " + Describe(seam, coin));

            SetChildColliderEnabled(coin, 0, true);
            SetChildColliderEnabled(coin, 1, true);
            seam.Reconcile();

            Assert.That(coins.Count, Is.EqualTo(2),
                "Reentry after a full exit must publish exactly one further " +
                "Coin_Collected_Event. " + Describe(seam, coin));
            Assert.That(coins[1].ContactId, Is.Not.EqualTo(coins[0].ContactId),
                "A new contact interval must carry a new Contact_Id. " + Describe(seam, coin));
            Assert.That(coins[1].EventId, Is.Not.EqualTo(coins[0].EventId),
                "Each publication must carry a unique Event_Id. " + Describe(seam, coin));
            Assert.That(coins[1].EnvironmentObjectId, Is.EqualTo(coins[0].EnvironmentObjectId),
                "Reentry must report the same environment identity. " + Describe(seam, coin));
            Assert.That(tracker.ActiveContactCount, Is.EqualTo(1),
                "Reentry must open exactly one logical contact. " + Describe(seam, coin));
            Assert.That(diagnostics, Is.Empty,
                "Valid environment metadata must not produce diagnostics. " + Describe(seam, coin));
        }

        private static string Describe(ContactAdapterSeam seam, GameObject geometry)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[geometry={0}, geometryPosition={1}, capsuleCenter={2}, sampled={3}, " +
                "reconciliations={4}, activeContacts={5}, hits={6}, coins={7}]",
                geometry == null ? "none" : geometry.name,
                geometry == null
                    ? "none"
                    : geometry.transform.position.ToString("R", CultureInfo.InvariantCulture),
                CapsuleCenter.ToString("R", CultureInfo.InvariantCulture),
                seam == null ? -1 : seam.SampledColliderCount,
                seam == null ? -1 : seam.ReconcileCount,
                seam == null ? -1 : seam.ActiveContactCountOrMinusOne,
                seam == null ? -1 : seam.PublishedHitCount,
                seam == null ? -1 : seam.PublishedCoinCount);
        }

        private static Collider ChildCollider(GameObject environmentObject, int childIndex)
        {
            var colliders = environmentObject.GetComponentsInChildren<BoxCollider>(true);
            Assert.That(colliders.Length, Is.GreaterThan(childIndex),
                environmentObject.name + " must own child collider " + childIndex + ".");
            return colliders[childIndex];
        }

        private static void SetChildColliderEnabled(
            GameObject environmentObject,
            int childIndex,
            bool enabled)
        {
            ChildCollider(environmentObject, childIndex).enabled = enabled;
        }

        private static int FirstLayerOf(int mask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private ContactAdapterSeam CreateAdapter()
        {
            var player = new GameObject("ContactAdapterFixture");
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = PlayerOrigin;

            var capsule = player.AddComponent<CharacterController>();
            capsule.radius = Defaults.BaselineCollider.Radius;
            capsule.height = Defaults.BaselineCollider.Height;
            capsule.center = Defaults.BaselineCollider.Center;
            capsule.minMoveDistance = 0f;
            capsule.stepOffset = 0.1f;

            var seam = ContactAdapterSeam.Attach(player, tracker, Defaults.GroundLayerMask, this);
            player.SetActive(true);
            return seam;
        }

        /// <summary>
        /// A Lucky-owned identity double with <paramref name="childColliderCount"/> child colliders,
        /// all overlapping the player capsule so the sample sees several children of one identity.
        /// </summary>
        private GameObject CreateEnvironmentObject(
            string environmentObjectId,
            EnvironmentObjectKind kind,
            float collectibleValue,
            int childColliderCount)
        {
            return CreateEnvironmentObject(
                environmentObjectId, kind, collectibleValue, childColliderCount, CapsuleCenter);
        }

        private GameObject CreateEnvironmentObject(
            string environmentObjectId,
            EnvironmentObjectKind kind,
            float collectibleValue,
            int childColliderCount,
            Vector3 worldCenter)
        {
            var root = new GameObject(environmentObjectId);
            spawnedObjects.Add(root);
            root.SetActive(false);
            root.transform.position = worldCenter;

            var identity = root.AddComponent<EnvironmentObjectDouble>();
            identity.Configure(environmentObjectId, kind, collectibleValue);

            for (var child = 0; child < childColliderCount; child++)
            {
                var body = new GameObject(environmentObjectId + "Child" + child);
                body.layer = FirstLayerOf(Defaults.GroundLayerMask);
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.05f * child, 0f);
                body.transform.localScale = Vector3.one;

                var collider = body.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.2f, 0.2f, 0.2f);
            }

            root.SetActive(true);
            return root;
        }

        private void ClearFixtures()
        {
            for (var index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                if (spawnedObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawnedObjects[index]);
                }
            }

            spawnedObjects.Clear();
        }

        private sealed class EnvironmentObjectDouble : MonoBehaviour, IEnvironmentObject
        {
            private string environmentObjectId;
            private EnvironmentObjectKind kind;
            private float collectibleValue;

            public string EnvironmentObjectId { get { return environmentObjectId; } }
            public EnvironmentObjectKind Kind { get { return kind; } }
            public float CollectibleValue { get { return collectibleValue; } }

            public void Configure(string id, EnvironmentObjectKind objectKind, float value)
            {
                environmentObjectId = id;
                kind = objectKind;
                collectibleValue = value;
            }
        }

        /// <summary>
        /// The public surface Task 7.8 has to provide, reached by reflection so this fixture states the
        /// required contract before the adapter exists. The reconciliation entry point is bound as a
        /// delegate because a reflected invocation would allocate inside the measured region.
        /// </summary>
        private sealed class ContactAdapterSeam
        {
            private const string AdapterTypeName = "SubwaySurfers.Player.EnvironmentContactAdapter";

            private readonly Component adapter;
            private readonly Behaviour behaviour;
            private readonly PropertyInfo reconcileCount;
            private readonly PropertyInfo sampledColliderCount;
            private readonly MethodInfo recordContactPosition;
            private readonly EnvironmentContactAdapterPlayModeTests owner;

            private ContactAdapterSeam(
                Component adapter,
                EnvironmentContactAdapterPlayModeTests owner)
            {
                this.adapter = adapter;
                this.owner = owner;
                behaviour = adapter as Behaviour;
                Assert.That(behaviour, Is.Not.Null,
                    "Task 7.8 must make " + AdapterTypeName + " a Behaviour so per-step sampling " +
                    "can be suspended in tests.");

                var type = adapter.GetType();
                reconcileCount = RequiredProperty(type, "ReconcileCount", typeof(int));
                sampledColliderCount = RequiredProperty(type, "SampledColliderCount", typeof(int));
                recordContactPosition = RequiredMethod(
                    type, "RecordContactPosition", typeof(Collider), typeof(Vector3));

                var reconcile = RequiredMethod(type, "ReconcileOverlapSamples");
                Assert.That(reconcile.ReturnType, Is.EqualTo(typeof(void)),
                    "ReconcileOverlapSamples must return void.");
                Reconcile = (Action)Delegate.CreateDelegate(typeof(Action), adapter, reconcile);
            }

            /// <summary>One reconciliation of the sampled overlap set, as one physics step performs.</summary>
            public Action Reconcile { get; }

            public int ReconcileCount { get { return (int)reconcileCount.GetValue(adapter); } }

            public int SampledColliderCount
            {
                get { return (int)sampledColliderCount.GetValue(adapter); }
            }

            public int ActiveContactCountOrMinusOne
            {
                get { return owner.tracker == null ? -1 : owner.tracker.ActiveContactCount; }
            }

            public int PublishedHitCount { get { return owner.hits.Count; } }
            public int PublishedCoinCount { get { return owner.coins.Count; } }

            public static ContactAdapterSeam Attach(
                GameObject player,
                EnvironmentContactTracker tracker,
                int environmentLayerMask,
                EnvironmentContactAdapterPlayModeTests owner)
            {
                var type = typeof(CharacterControllerMotor).Assembly.GetType(AdapterTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 7.8 must provide " + AdapterTypeName + ".");
                Assert.That(typeof(Component).IsAssignableFrom(type), Is.True,
                    "Task 7.8 must make " + AdapterTypeName + " a player component.");

                var adapter = player.AddComponent(type);
                var configure = RequiredMethod(
                    type, "Configure", typeof(EnvironmentContactTracker), typeof(int));
                configure.Invoke(adapter, new object[] { tracker, environmentLayerMask });
                return new ContactAdapterSeam(adapter, owner);
            }

            public void RecordContactPosition(Collider contact, Vector3 contactPosition)
            {
                recordContactPosition.Invoke(adapter, new object[] { contact, contactPosition });
            }

            public void SuspendAutomaticSampling()
            {
                behaviour.enabled = false;
            }

            private static PropertyInfo RequiredProperty(Type type, string name, Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null,
                    "Task 7.8 must expose " + type.FullName + "." + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType),
                    name + " must be typed " + propertyType.Name + ".");
                return property;
            }

            private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    parameterTypes,
                    null);
                Assert.That(method, Is.Not.Null,
                    "Task 7.8 must expose " + type.FullName + "." + name + ".");
                return method;
            }
        }
    }
}
