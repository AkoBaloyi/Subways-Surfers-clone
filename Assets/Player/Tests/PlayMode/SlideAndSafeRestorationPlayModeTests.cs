using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;
using UnityEngine.TestTools;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode coverage for timed sliding and safe collider restoration: the applied capsule stays
    /// a valid CharacterController capsule, Slide_Collider_Profile reaches the capsule before the next
    /// Movement_Update, Slide_Elapsed_Time advances once per Movement_Update, blocked overhead
    /// geometry retains Sliding, Safe_Collider_Restoration is reevaluated once per Movement_Update,
    /// self colliders and triggers never obstruct, and the first safe update restores radius, height,
    /// and center together.
    ///
    /// Every fixture is built programmatically from player-owned doubles that implement only the
    /// public Player contracts, so no production environment asset is needed. Overhead geometry is
    /// created after the slide capsule is applied, which keeps the baseline capsule from ever
    /// starting inside the obstruction and keeps the observed motion deterministic.
    /// </summary>
    public sealed class SlideAndSafeRestorationPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float ProfileTolerance = 0.0005f;
        private const float TimeTolerance = 0.0005f;
        private const int SettleSteps = 3;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;
        private static readonly Vector3 PlayerSpawn = new Vector3(Defaults.LaneCenters.y, 0.02f, 0f);

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<ScriptableObject> spawnedAssets = new List<ScriptableObject>();

        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;

        [SetUp]
        public void SetUp()
        {
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            Time.fixedDeltaTime = FixedStep;

            // One physics step per rendered frame keeps fixed-step counts deterministic.
            Time.maximumDeltaTime = FixedStep;
        }

        [TearDown]
        public void TearDown()
        {
            ClearFixtures();
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 5.1, 5.2, 5.3, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: slide entry applies a valid capsule before movement")]
        public IEnumerator SlideEntryAppliesValidCapsuleBeforeMovement_Requirements_5_1_5_2_5_3_And_12_7()
        {
            var player = CreatePlayer();
            yield return Settle(player);

            // Deterministic single-stepping isolates slide entry from automatic fixed stepping.
            player.enabled = false;
            var capsule = player.GetComponent<CharacterController>();
            var updatesBefore = player.MovementUpdateCount;
            var movesBefore = player.MoveInvocationCount;

            var result = player.RequestSlide();
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Sliding),
                "An accepted Slide_Request must transition Running to Sliding. " + Describe(player, 0));
            Assert.That(player.Snapshot.SlideElapsedTime, Is.EqualTo(0f).Within(TimeTolerance),
                "Entering Sliding must set Slide_Elapsed_Time to zero. " + Describe(player, 0));
            Assert.That(player.MovementUpdateCount, Is.EqualTo(updatesBefore),
                "Slide entry must not run a Movement_Update by itself. " + Describe(player, 0));
            Assert.That(player.MoveInvocationCount, Is.EqualTo(movesBefore),
                "Slide entry must not submit displacement by itself. " + Describe(player, 0));

            AssertValidCapsule(capsule, player, 0);
            AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, 0,
                "Slide_Collider_Profile must reach the capsule before the next Movement_Update.");

            player.ExecuteMovementUpdate(FixedStep);
            Assert.That(player.MoveInvocationCount - movesBefore, Is.EqualTo(1),
                "The Movement_Update after slide entry must submit exactly one displacement. " +
                Describe(player, 1));
            Assert.That(player.PreMovementSnapshot.ColliderProfile, Is.EqualTo(Defaults.SlideCollider),
                "The first Sliding Movement_Update must move with Slide_Collider_Profile. " +
                Describe(player, 1));
            AssertValidCapsule(capsule, player, 1);
            AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, 1,
                "Sliding must keep Slide_Collider_Profile on the capsule.");
        }

        // **Validates: Requirements 5.4, 5.5, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: Slide_Elapsed_Time advances once per Movement_Update")]
        public IEnumerator SlideTimerAdvancesOncePerMovementUpdate_Requirements_5_4_5_5_And_12_7()
        {
            var player = CreatePlayer();
            yield return Settle(player);

            player.enabled = false;
            var capsule = player.GetComponent<CharacterController>();
            Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));

            // Two steps short of Slide_Duration keeps every observed update inside the slide window.
            var steps = Mathf.FloorToInt(Defaults.SlideDuration / FixedStep) - 2;
            Assert.That(steps, Is.GreaterThanOrEqualTo(5),
                "The fixture must observe several Sliding Movement_Updates. " + Describe(player, 0));

            for (var step = 1; step <= steps; step++)
            {
                var elapsedBefore = player.Snapshot.SlideElapsedTime;
                var updatesBefore = player.MovementUpdateCount;

                player.ExecuteMovementUpdate(FixedStep);

                Assert.That(player.MovementUpdateCount - updatesBefore, Is.EqualTo(1),
                    "Each single-stepped update must be exactly one Movement_Update. " +
                    Describe(player, step));
                Assert.That(player.Snapshot.SlideElapsedTime - elapsedBefore,
                    Is.EqualTo(FixedStep).Within(TimeTolerance),
                    "Sliding must increase Slide_Elapsed_Time by Elapsed_Simulation_Time exactly once " +
                    "per Movement_Update. " + Describe(player, step));
                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Sliding),
                    "Slide_Elapsed_Time below Slide_Duration must retain Sliding. " +
                    Describe(player, step));
                AssertValidCapsule(capsule, player, step);
                AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, step,
                    "Slide_Elapsed_Time below Slide_Duration must retain Slide_Collider_Profile.");
            }

            Assert.That(player.Snapshot.SlideElapsedTime,
                Is.EqualTo(steps * FixedStep).Within(TimeTolerance),
                "Slide_Elapsed_Time must equal the accumulated Elapsed_Simulation_Time. " +
                Describe(player, steps));
            Assert.That(player.Snapshot.SlideElapsedTime, Is.LessThan(Defaults.SlideDuration),
                "The measured window must stay below Slide_Duration. " + Describe(player, steps));
        }

        // **Validates: Requirements 5.11, 5.12, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: rejected Slide_Requests preserve slide state")]
        public IEnumerator RejectedSlideRequestsPreserveSlideState_Requirements_5_11_5_12_And_12_7()
        {
            var player = CreatePlayer();
            yield return Settle(player);

            player.enabled = false;
            var capsule = player.GetComponent<CharacterController>();
            Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));
            for (var step = 0; step < 3; step++) player.ExecuteMovementUpdate(FixedStep);

            var elapsedBefore = player.Snapshot.SlideElapsedTime;
            var repeated = player.RequestSlide();
            Assert.That(repeated.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Slide_Request while Sliding must be rejected. " + Describe(player, 3));
            Assert.That(repeated.Reason, Is.EqualTo(RejectionReason.InvalidState),
                "A Slide_Request while Sliding must be rejected as InvalidState. " + Describe(player, 3));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Sliding),
                "A rejected Slide_Request must preserve Sliding. " + Describe(player, 3));
            Assert.That(player.Snapshot.SlideElapsedTime, Is.EqualTo(elapsedBefore),
                "A rejected Slide_Request must not extend Slide_Duration. " + Describe(player, 3));
            Assert.That(player.Snapshot.PendingActionRequests.Count, Is.EqualTo(0),
                "A rejected Slide_Request must not enqueue a pending action request. " +
                Describe(player, 3));
            AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, 3,
                "A rejected Slide_Request must preserve the Player_Collider profile.");

            yield return RestoreByManualStepping(player);
            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, 0));

            var airborneElapsed = player.Snapshot.SlideElapsedTime;
            var airborne = player.RequestSlide();
            Assert.That(airborne.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Slide_Request while Jumping must be rejected. " + Describe(player, 0));
            Assert.That(airborne.Reason, Is.EqualTo(RejectionReason.InvalidState),
                "A Slide_Request while Jumping must be rejected as InvalidState. " + Describe(player, 0));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Jumping),
                "A rejected Slide_Request must preserve Player_State. " + Describe(player, 0));
            Assert.That(player.Snapshot.SlideElapsedTime, Is.EqualTo(airborneElapsed),
                "A rejected Slide_Request must preserve Slide_Elapsed_Time. " + Describe(player, 0));
            AssertCapsuleProfile(capsule, Defaults.BaselineCollider, player, 0,
                "A rejected Slide_Request must preserve the Player_Collider profile.");
        }

        // **Validates: Requirements 5.8, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: blocked overhead geometry retains Sliding")]
        public IEnumerator BlockedOverheadGeometryRetainsSliding_Requirements_5_8_And_12_7()
        {
            var player = CreatePlayer();
            yield return Settle(player);

            player.enabled = false;
            var capsule = player.GetComponent<CharacterController>();
            Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));

            CreateOverheadObstruction("OverheadObstructionDouble", false, null);
            var expirySteps = Mathf.CeilToInt(Defaults.SlideDuration / FixedStep) + 1;
            for (var step = 1; step <= expirySteps + 10; step++)
            {
                player.ExecuteMovementUpdate(FixedStep);

                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Sliding),
                    "Blocked Safe_Collider_Restoration must retain Sliding. " + Describe(player, step));
                AssertValidCapsule(capsule, player, step);
                AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, step,
                    "Blocked Safe_Collider_Restoration must retain Slide_Collider_Profile.");
                Assert.That(player.Snapshot.ColliderProfile, Is.EqualTo(Defaults.SlideCollider),
                    "Blocked Safe_Collider_Restoration must retain Slide_Collider_Profile. " +
                    Describe(player, step));
            }

            Assert.That(player.Snapshot.SlideElapsedTime,
                Is.GreaterThanOrEqualTo(Defaults.SlideDuration),
                "The fixture must run past Slide_Duration while restoration stays blocked. " +
                Describe(player, expirySteps + 10));
        }

        // **Validates: Requirements 5.9, 5.10, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: Safe_Collider_Restoration is evaluated once per Movement_Update")]
        public IEnumerator RestorationEvaluatedOncePerMovementUpdate_Requirements_5_9_5_10_And_12_7()
        {
            var fixture = CreateCountingFixture();
            yield return Settle(fixture);

            Assert.That(fixture.Loop.IsGrounded, Is.True,
                "The fixture must establish Valid_Ground_Contact before sliding. " + Describe(fixture, 0));
            Assert.That(fixture.Loop.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(fixture, 0));

            var obstruction = CreateOverheadObstruction("OverheadObstructionDouble", false, null);
            var queriesAtSlideEntry = fixture.Motor.RestorationQueryCount;

            // One step short of Slide_Duration: no restoration is eligible yet.
            var windowSteps = Mathf.FloorToInt(Defaults.SlideDuration / FixedStep) - 1;
            for (var step = 1; step <= windowSteps; step++) fixture.Loop.ExecuteMovementUpdate(FixedStep);

            Assert.That(fixture.Motor.RestorationQueryCount, Is.EqualTo(queriesAtSlideEntry),
                "Slide_Elapsed_Time below Slide_Duration must not query Safe_Collider_Restoration. " +
                Describe(fixture, windowSteps));
            Assert.That(fixture.Loop.Snapshot.SlideElapsedTime, Is.LessThan(Defaults.SlideDuration),
                "The measured window must stay below Slide_Duration. " + Describe(fixture, windowSteps));
            Assert.That(fixture.Loop.CurrentState, Is.EqualTo(PlayerState.Sliding),
                "The fixture must still be Sliding inside the slide window. " +
                Describe(fixture, windowSteps));

            // Cross Slide_Duration without depending on the exact accumulation step, then measure the
            // reevaluation cadence while restoration stays blocked.
            for (var step = 1; step <= 3 && fixture.Loop.Snapshot.SlideElapsedTime < Defaults.SlideDuration; step++)
            {
                fixture.Loop.ExecuteMovementUpdate(FixedStep);
            }

            Assert.That(fixture.Loop.Snapshot.SlideElapsedTime,
                Is.GreaterThanOrEqualTo(Defaults.SlideDuration),
                "The fixture must run past Slide_Duration while restoration stays blocked. " +
                Describe(fixture, windowSteps));
            Assert.That(fixture.Motor.RestorationQueryCount, Is.GreaterThan(queriesAtSlideEntry),
                "Reaching Slide_Duration must query Safe_Collider_Restoration. " +
                Describe(fixture, windowSteps));

            for (var step = 1; step <= 8; step++)
            {
                var before = fixture.Motor.RestorationQueryCount;
                fixture.Loop.ExecuteMovementUpdate(FixedStep);

                Assert.That(fixture.Motor.RestorationQueryCount - before, Is.EqualTo(1),
                    "Blocked restoration must reevaluate Safe_Collider_Restoration exactly once per " +
                    "Movement_Update. " + Describe(fixture, step));
                Assert.That(fixture.Loop.CurrentState, Is.EqualTo(PlayerState.Sliding),
                    "Blocked Safe_Collider_Restoration must retain Sliding. " + Describe(fixture, step));
            }

            obstruction.GetComponent<Collider>().enabled = false;
            var queriesBeforeSafeUpdate = fixture.Motor.RestorationQueryCount;
            fixture.Loop.ExecuteMovementUpdate(FixedStep);

            Assert.That(fixture.Motor.RestorationQueryCount - queriesBeforeSafeUpdate, Is.EqualTo(1),
                "The first safe Movement_Update must query Safe_Collider_Restoration exactly once. " +
                Describe(fixture, 0));
            Assert.That(fixture.Loop.CurrentState, Is.EqualTo(PlayerState.Running),
                "The first safe update after Slide_Duration must restore Running. " +
                Describe(fixture, 0));

            var queriesAfterRestoration = fixture.Motor.RestorationQueryCount;
            for (var step = 1; step <= 3; step++) fixture.Loop.ExecuteMovementUpdate(FixedStep);

            Assert.That(fixture.Motor.RestorationQueryCount, Is.EqualTo(queriesAfterRestoration),
                "A completed restoration must end further Safe_Collider_Restoration queries. " +
                Describe(fixture, 3));
        }

        // **Validates: Requirements 5.6, 5.7, 5.10, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: self colliders and triggers never obstruct restoration")]
        public IEnumerator SelfAndTriggerCollidersNeverObstructRestoration_Requirements_5_6_5_7_5_10_And_12_7()
        {
            var variants = new[] { "trigger", "self" };
            foreach (var variant in variants)
            {
                var player = CreatePlayer();
                yield return Settle(player);

                player.enabled = false;
                var capsule = player.GetComponent<CharacterController>();
                Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                    "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));

                CreateOverheadObstruction(
                    "OverheadObstructionDouble-" + variant,
                    variant == "trigger",
                    variant == "self" ? player.transform : null);

                var budget = Mathf.CeilToInt(Defaults.SlideDuration / FixedStep) + 3;
                for (var step = 1; step <= budget; step++)
                {
                    if (player.CurrentState == PlayerState.Running) break;
                    player.ExecuteMovementUpdate(FixedStep);
                }

                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                    "A " + variant + " obstruction must not block Safe_Collider_Restoration. " +
                    Describe(player, budget));
                AssertValidCapsule(capsule, player, budget);
                AssertCapsuleProfile(capsule, Defaults.BaselineCollider, player, budget,
                    "A " + variant + " obstruction must not prevent Baseline_Collider_Profile restoration.");

                ClearFixtures();
                yield return null;
            }
        }

        // **Validates: Requirements 5.6, 5.7, 5.10, 12.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the first safe update restores the baseline capsule atomically")]
        public IEnumerator FirstSafeUpdateRestoresBaselineAtomically_Requirements_5_6_5_7_5_10_And_12_7()
        {
            var player = CreatePlayer();
            yield return Settle(player);

            player.enabled = false;
            var capsule = player.GetComponent<CharacterController>();
            Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, 0));

            var obstruction = CreateOverheadObstruction("OverheadObstructionDouble", false, null);
            var blockedSteps = Mathf.CeilToInt(Defaults.SlideDuration / FixedStep) + 5;
            for (var step = 1; step <= blockedSteps; step++) player.ExecuteMovementUpdate(FixedStep);

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Sliding),
                "Blocked Safe_Collider_Restoration must retain Sliding past Slide_Duration. " +
                Describe(player, blockedSteps));
            AssertCapsuleProfile(capsule, Defaults.SlideCollider, player, blockedSteps,
                "Blocked Safe_Collider_Restoration must retain Slide_Collider_Profile.");

            obstruction.GetComponent<Collider>().enabled = false;
            player.ExecuteMovementUpdate(FixedStep);

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "The first safe Movement_Update after Slide_Duration must transition to Running. " +
                Describe(player, blockedSteps + 1));
            Assert.That(player.PreMovementSnapshot.State, Is.EqualTo(PlayerState.Running),
                "Restoration must resolve before the movement of the restoring update. " +
                Describe(player, blockedSteps + 1));
            Assert.That(player.PreMovementSnapshot.ColliderProfile, Is.EqualTo(Defaults.BaselineCollider),
                "Radius, height, and center must be restored together before that movement. " +
                Describe(player, blockedSteps + 1));
            Assert.That(player.Snapshot.ColliderProfile, Is.EqualTo(Defaults.BaselineCollider),
                "The restored snapshot must carry Baseline_Collider_Profile. " +
                Describe(player, blockedSteps + 1));
            AssertValidCapsule(capsule, player, blockedSteps + 1);
            AssertCapsuleProfile(capsule, Defaults.BaselineCollider, player, blockedSteps + 1,
                "The first safe update must restore radius, height, and center together.");
        }

        private static void AssertValidCapsule(
            CharacterController capsule,
            PlayerControllerFacade player,
            int step)
        {
            Assert.That(capsule.radius, Is.GreaterThan(0f),
                "The applied Player_Collider profile must keep a positive capsule radius. " +
                Describe(player, step));
            Assert.That(capsule.height, Is.GreaterThan(0f),
                "The applied Player_Collider profile must keep a positive capsule height. " +
                Describe(player, step));
            Assert.That(capsule.height, Is.GreaterThanOrEqualTo(2f * capsule.radius - ProfileTolerance),
                "A valid CharacterController capsule needs height of at least twice its radius. " +
                Describe(player, step));
            Assert.That(IsFinite(capsule.center), Is.True,
                "The applied Player_Collider profile must keep a finite capsule center. " +
                Describe(player, step));
        }

        private static void AssertCapsuleProfile(
            CharacterController capsule,
            ColliderProfile expected,
            PlayerControllerFacade player,
            int step,
            string because)
        {
            Assert.That(capsule.radius, Is.EqualTo(expected.Radius).Within(ProfileTolerance),
                because + " " + Describe(player, step));
            Assert.That(capsule.height, Is.EqualTo(expected.Height).Within(ProfileTolerance),
                because + " " + Describe(player, step));
            Assert.That((capsule.center - expected.Center).magnitude,
                Is.LessThanOrEqualTo(ProfileTolerance),
                because + " " + Describe(player, step));
        }

        private IEnumerator Settle(PlayerControllerFacade player)
        {
            for (var index = 0; index < SettleSteps; index++) yield return new WaitForFixedUpdate();
            Assert.That(player.MovementUpdateCount, Is.GreaterThanOrEqualTo(SettleSteps),
                "Each fixed step must run one Movement_Update. " + Describe(player, SettleSteps));
            Assert.That(player.IsGrounded, Is.True,
                "The fixture must establish Valid_Ground_Contact before sliding. " +
                Describe(player, SettleSteps));
        }

        /// <summary>
        /// Lets physics register the fixture colliders, then runs one Movement_Update so the loop
        /// samples Grounded before a Slide_Request. The loop is driven manually throughout.
        /// </summary>
        private IEnumerator Settle(CountingFixture fixture)
        {
            for (var index = 0; index < SettleSteps; index++) yield return new WaitForFixedUpdate();
            fixture.Loop.ExecuteMovementUpdate(FixedStep);
        }

        private IEnumerator RestoreByManualStepping(PlayerControllerFacade player)
        {
            var budget = Mathf.CeilToInt(Defaults.SlideDuration / FixedStep) + 10;
            for (var step = 0; step < budget; step++)
            {
                if (player.CurrentState == PlayerState.Running) break;
                player.ExecuteMovementUpdate(FixedStep);
            }

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "An unobstructed slide must restore Running within Slide_Duration. " +
                Describe(player, budget));
            Assert.That(player.IsGrounded, Is.True,
                "A restored player must keep Valid_Ground_Contact. " + Describe(player, budget));
            yield break;
        }

        private static string Describe(PlayerControllerFacade player, int step)
        {
            var capsule = player.GetComponent<CharacterController>();
            return string.Format(
                CultureInfo.InvariantCulture,
                "[step={0}, state={1}, grounded={2}, slideElapsed={3}, profile={4}, " +
                "capsule=(r={5}, h={6}, c={7}), position={8}, updates={9}]",
                step,
                player.CurrentState,
                player.IsGrounded,
                player.Snapshot.SlideElapsedTime.ToString("R", CultureInfo.InvariantCulture),
                player.Snapshot.ColliderProfile,
                capsule == null ? "none" : capsule.radius.ToString("R", CultureInfo.InvariantCulture),
                capsule == null ? "none" : capsule.height.ToString("R", CultureInfo.InvariantCulture),
                capsule == null ? "none" : capsule.center.ToString("R", CultureInfo.InvariantCulture),
                player.transform.position.ToString("R", CultureInfo.InvariantCulture),
                player.MovementUpdateCount);
        }

        private static string Describe(CountingFixture fixture, int step)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[step={0}, state={1}, grounded={2}, slideElapsed={3}, profile={4}, " +
                "restorationQueries={5}, position={6}, updates={7}]",
                step,
                fixture.Loop.CurrentState,
                fixture.Loop.IsGrounded,
                fixture.Loop.Snapshot.SlideElapsedTime.ToString("R", CultureInfo.InvariantCulture),
                fixture.Loop.Snapshot.ColliderProfile,
                fixture.Motor.RestorationQueryCount,
                fixture.Transform.position.ToString("R", CultureInfo.InvariantCulture),
                fixture.Loop.MovementUpdateCount);
        }

        private PlayerControllerFacade CreatePlayer()
        {
            CreateRunningSurface();

            var player = CreatePlayerBody("SlideFixture");
            var facade = player.AddComponent<PlayerControllerFacade>();
            InjectReference(facade, "inputActionAsset", CreateReferencePlaceholder());
            InjectReference(facade, "fallbackInputActionAsset", CreateReferencePlaceholder());
            InjectReference(facade, "cameraTarget", facade.transform);
            InjectReference(facade, "fallbackCameraTarget", facade.transform);
            player.SetActive(true);

            Assert.That(facade.SimulationEnabled, Is.True,
                "The fixture configuration must enable movement simulation. Diagnostics: " +
                RenderDiagnostics(facade));
            return facade;
        }

        /// <summary>
        /// The restoration-query count is not observable through the facade, so this fixture drives
        /// the public <see cref="PlayerMovementLoop"/> directly with a counting decorator wrapped
        /// around the real <see cref="CharacterControllerMotor"/>. The physics behaviour under test
        /// stays the production motor's; only the call count is observed.
        /// </summary>
        private CountingFixture CreateCountingFixture()
        {
            CreateRunningSurface();

            var player = CreatePlayerBody("SlideRestorationQueryFixture");
            var motor = player.AddComponent<CharacterControllerMotor>();
            player.SetActive(true);

            var counting = new QueryCountingMotorSurface(motor);
            var loop = new PlayerMovementLoop(Defaults, counting, null);
            return new CountingFixture(
                loop, counting, player.GetComponent<CharacterController>(), player.transform);
        }

        private GameObject CreatePlayerBody(string name)
        {
            var player = new GameObject(name);
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = PlayerSpawn;

            var controller = player.AddComponent<CharacterController>();
            controller.radius = Defaults.BaselineCollider.Radius;
            controller.height = Defaults.BaselineCollider.Height;
            controller.center = Defaults.BaselineCollider.Center;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.1f;
            return player;
        }

        private void CreateRunningSurface()
        {
            var ground = new GameObject("RunningSurfaceDouble");
            spawnedObjects.Add(ground);
            ground.layer = FirstLayerOf(Defaults.GroundLayerMask);
            ground.transform.position = new Vector3(0f, -0.5f, 40f);
            var collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(40f, 1f, 200f);
            ground.AddComponent<RunningSurfaceDouble>();
        }

        /// <summary>
        /// Marked obstruction geometry that overlaps the baseline capsule but clears the slide capsule.
        /// A <paramref name="parent"/> makes it a self collider that travels with the player.
        /// </summary>
        private GameObject CreateOverheadObstruction(string name, bool isTrigger, Transform parent)
        {
            var body = new GameObject(name);
            body.layer = FirstLayerOf(Defaults.ObstructionLayerMask);

            var slideTop = Defaults.SlideCollider.Center.y + Defaults.SlideCollider.Height * 0.5f;
            var baselineTop = Defaults.BaselineCollider.Center.y + Defaults.BaselineCollider.Height * 0.5f;
            var thickness = 0.5f;
            var centerY = slideTop + 0.5f * (baselineTop - slideTop);

            if (parent == null)
            {
                spawnedObjects.Add(body);
                body.transform.position = new Vector3(0f, PlayerSpawn.y + centerY, 40f);
            }
            else
            {
                body.transform.SetParent(parent, false);
                body.transform.localPosition = new Vector3(0f, centerY, 0f);
            }

            var collider = body.AddComponent<BoxCollider>();
            collider.size = parent == null
                ? new Vector3(20f, thickness, 400f)
                : new Vector3(2f * Defaults.BaselineCollider.Radius, thickness, 2f);
            collider.isTrigger = isTrigger;
            body.AddComponent<EnvironmentObstructionDouble>();
            return body;
        }

        private UnityEngine.Object CreateReferencePlaceholder()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "SlideFixtureReferencePlaceholder";
            spawnedAssets.Add(asset);
            return asset;
        }

        private void ClearFixtures()
        {
            for (var index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                if (spawnedObjects[index] != null) UnityEngine.Object.DestroyImmediate(spawnedObjects[index]);
            }

            for (var index = spawnedAssets.Count - 1; index >= 0; index--)
            {
                if (spawnedAssets[index] != null) UnityEngine.Object.DestroyImmediate(spawnedAssets[index]);
            }

            spawnedObjects.Clear();
            spawnedAssets.Clear();
        }

        private static int FirstLayerOf(int mask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void InjectReference(
            PlayerControllerFacade facade,
            string fieldName,
            UnityEngine.Object value)
        {
            var field = typeof(PlayerControllerFacade).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "The configuration surface must retain the serialized field " + fieldName + ".");
            field.SetValue(facade, value);
        }

        private static string RenderDiagnostics(PlayerControllerFacade facade)
        {
            var diagnostics = facade.ConfigurationDiagnostics;
            var rendered = new string[diagnostics.Count];
            for (var index = 0; index < diagnostics.Count; index++)
            {
                rendered[index] = diagnostics[index].Severity + ":" + diagnostics[index].Field;
            }

            return rendered.Length == 0 ? "none" : string.Join(", ", rendered);
        }

        private sealed class RunningSurfaceDouble : MonoBehaviour, IRunningSurface
        {
        }

        private sealed class EnvironmentObstructionDouble : MonoBehaviour, IEnvironmentObstruction
        {
        }

        private sealed class ReferencePlaceholderDouble : ScriptableObject
        {
        }

        private sealed class CountingFixture
        {
            public CountingFixture(
                PlayerMovementLoop loop,
                QueryCountingMotorSurface motor,
                CharacterController capsule,
                Transform transform)
            {
                Loop = loop;
                Motor = motor;
                Capsule = capsule;
                Transform = transform;
            }

            public PlayerMovementLoop Loop { get; }
            public QueryCountingMotorSurface Motor { get; }
            public CharacterController Capsule { get; }
            public Transform Transform { get; }
        }

        /// <summary>
        /// Counts Safe_Collider_Restoration queries while forwarding every call to the production
        /// motor, so the observed physics stays the real capsule behaviour.
        /// </summary>
        private sealed class QueryCountingMotorSurface : IPlayerMotorSurface
        {
            private readonly IPlayerMotorSurface inner;

            public QueryCountingMotorSurface(IPlayerMotorSurface inner)
            {
                this.inner = inner;
            }

            public int RestorationQueryCount { get; private set; }
            public Vector3 Position { get { return inner.Position; } }
            public Quaternion Rotation { get { return inner.Rotation; } }
            public int MoveInvocationCount { get { return inner.MoveInvocationCount; } }

            public void ApplyColliderProfile(ColliderProfile profile)
            {
                inner.ApplyColliderProfile(profile);
            }

            public bool SampleGrounded(
                ColliderProfile profile,
                int groundLayerMask,
                float contactTolerance,
                float normalThreshold)
            {
                return inner.SampleGrounded(profile, groundLayerMask, contactTolerance, normalThreshold);
            }

            public bool IsBaselineRestorationSafe(ColliderProfile baselineProfile, int obstructionLayerMask)
            {
                RestorationQueryCount++;
                return inner.IsBaselineRestorationSafe(baselineProfile, obstructionLayerMask);
            }

            public Vector3 Move(Vector3 displacement)
            {
                return inner.Move(displacement);
            }
        }
    }
}
