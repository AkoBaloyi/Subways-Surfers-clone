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
    /// Play Mode coverage for Grounded_Query equivalence, the exactly-once jump impulse, gravity
    /// per Movement_Update, rejected Jump_Requests, valid landing, and airborne lane and forward
    /// movement. Every fixture is built programmatically from player-owned test doubles that
    /// implement only the public Player contracts, so no production environment asset is needed.
    /// </summary>
    public sealed class GroundingAndJumpPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float LinearTolerance = 0.002f;
        private const float VelocityTolerance = 0.001f;
        private const int SettleSteps = 3;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;
        private static readonly Vector3 PlayerSpawn = new Vector3(Defaults.LaneCenters.y, 0.02f, 0f);

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<ScriptableObject> spawnedAssets = new List<ScriptableObject>();

        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;

        /// <summary>Support geometry variants exercised by the Grounded_Query equivalence tests.</summary>
        private enum SupportGeometry
        {
            ValidSurface,
            ShallowSurface,
            WrongLayerSurface,
            UnmarkedGeometry,
            TriggerSurface,
            Wall,
            Ceiling,
            Coin,
            Obstacle,
            OutOfToleranceSurface,
            SteepSurface
        }

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

        // **Validates: Requirements 4.1, 4.3, 12.6, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: valid support geometry grounds the player")]
        public IEnumerator ValidSupportGeometryGroundsPlayer_Requirements_4_1_4_3_12_6_And_14_10()
        {
            // The shallow surface is the positive control for the steep surface rejected below: both
            // are marked, non-trigger, on Ground_Layer_Mask, and inside Ground_Contact_Tolerance.
            var variants = new[] { SupportGeometry.ValidSurface, SupportGeometry.ShallowSurface };
            foreach (var variant in variants)
            {
                var player = CreatePlayer(variant);
                yield return Settle(player);

                Assert.That(player.IsGrounded, Is.True,
                    "A non-trigger IRunningSurface on Ground_Layer_Mask within Ground_Contact_Tolerance " +
                    "and above Ground_Normal_Threshold must produce Grounded. " +
                    Describe(player, variant, SettleSteps));
                Assert.That(player.Snapshot.IsGrounded, Is.True,
                    "Grounded_Status_Query and the snapshot must agree. " +
                    Describe(player, variant, SettleSteps));

                var jump = player.RequestJump();
                Assert.That(jump.Status, Is.EqualTo(CommandStatus.Accepted),
                    "A grounded Running Jump_Request must be accepted. " +
                    Describe(player, variant, SettleSteps));
                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Jumping),
                    "An accepted Jump_Request must transition Running to Jumping. " +
                    Describe(player, variant, SettleSteps));

                ClearFixtures();
                yield return null;
            }
        }

        // **Validates: Requirements 4.1, 4.2, 4.6, 12.6, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: only valid support geometry can ground the player")]
        public IEnumerator InvalidSupportGeometryNeverGrounds_Requirements_4_1_4_2_4_6_12_6_And_14_10()
        {
            var variants = new[]
            {
                SupportGeometry.WrongLayerSurface,
                SupportGeometry.UnmarkedGeometry,
                SupportGeometry.TriggerSurface,
                SupportGeometry.Wall,
                SupportGeometry.Ceiling,
                SupportGeometry.Coin,
                SupportGeometry.Obstacle,
                SupportGeometry.OutOfToleranceSurface,
                SupportGeometry.SteepSurface
            };

            foreach (var variant in variants)
            {
                var player = CreatePlayer(variant);
                yield return Settle(player);

                Assert.That(player.IsGrounded, Is.False,
                    "Geometry that is not a Valid_Ground_Contact must not ground the player. " +
                    Describe(player, variant, SettleSteps));
                Assert.That(player.Snapshot.IsGrounded, Is.False,
                    "Grounded_Status_Query and the snapshot must agree. " +
                    Describe(player, variant, SettleSteps));

                var jump = player.RequestJump();
                Assert.That(jump.Status, Is.EqualTo(CommandStatus.Rejected),
                    "An ungrounded Jump_Request must be rejected. " + Describe(player, variant, SettleSteps));
                Assert.That(jump.Reason, Is.EqualTo(RejectionReason.NotGrounded),
                    "An ungrounded Jump_Request must be rejected as NotGrounded. " +
                    Describe(player, variant, SettleSteps));
                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                    "A rejected Jump_Request must preserve Player_State. " +
                    Describe(player, variant, SettleSteps));
                Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(0f).Within(VelocityTolerance),
                    "A rejected Jump_Request must not apply Jump_Velocity. " +
                    Describe(player, variant, SettleSteps));

                ClearFixtures();
                yield return null;
            }
        }

        // **Validates: Requirements 4.4, 4.7, 12.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: Jump_Velocity is applied exactly once per Jumping entry")]
        public IEnumerator JumpImpulseAppliedExactlyOncePerEntry_Requirements_4_4_4_7_And_12_6()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            // Deterministic single-stepping isolates the impulse from automatic fixed stepping.
            player.enabled = false;

            Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(0f).Within(VelocityTolerance),
                "A grounded Running player must carry no vertical velocity. " + Describe(player, null, 0));
            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));
            Assert.That(player.Snapshot.VerticalVelocity,
                Is.EqualTo(Defaults.JumpVelocity).Within(VelocityTolerance),
                "Entering Jumping must apply Jump_Velocity exactly once. " + Describe(player, null, 0));

            var repeated = player.RequestJump();
            Assert.That(repeated.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Jump_Request in Jumping must be rejected. " + Describe(player, null, 0));
            Assert.That(player.Snapshot.VerticalVelocity,
                Is.EqualTo(Defaults.JumpVelocity).Within(VelocityTolerance),
                "A rejected Jump_Request must not reapply Jump_Velocity. " + Describe(player, null, 0));

            for (var step = 1; step <= 4; step++)
            {
                player.ExecuteMovementUpdate(FixedStep);
                var expected = Defaults.JumpVelocity - Defaults.GravityAcceleration * FixedStep * step;
                Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(expected).Within(VelocityTolerance),
                    "Jumping must integrate exactly one gravity decrement per Movement_Update and " +
                    "never reapply Jump_Velocity. " + Describe(player, null, step));

                Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Rejected),
                    "A Jump_Request in Jumping must be rejected. " + Describe(player, null, step));
                Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(expected).Within(VelocityTolerance),
                    "A rejected Jump_Request must preserve vertical velocity. " + Describe(player, null, step));
            }

            LandByManualStepping(player);
            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A landed grounded player must accept a new Jump_Request. " + Describe(player, null, 0));
            Assert.That(player.Snapshot.VerticalVelocity,
                Is.EqualTo(Defaults.JumpVelocity).Within(VelocityTolerance),
                "A second Jumping entry must apply Jump_Velocity exactly once for that entry. " +
                Describe(player, null, 0));
        }

        // **Validates: Requirements 4.5, 12.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: one gravity decrement per fixed step while Jumping")]
        public IEnumerator GravityDecrementsOncePerFixedStep_Requirements_4_5_And_12_6()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));

            var expectedDecrement = Defaults.GravityAcceleration * FixedStep;
            var observedSteps = 0;
            for (var step = 0; step < 12; step++)
            {
                if (player.CurrentState != PlayerState.Jumping) break;

                var before = player.Snapshot.VerticalVelocity;
                var updatesBefore = player.MovementUpdateCount;
                yield return new WaitForFixedUpdate();
                if (player.CurrentState != PlayerState.Jumping) break;

                Assert.That(player.MovementUpdateCount - updatesBefore, Is.EqualTo(1),
                    "Each fixed step must run exactly one Movement_Update. " + Describe(player, null, step));
                Assert.That(before - player.Snapshot.VerticalVelocity,
                    Is.EqualTo(expectedDecrement).Within(VelocityTolerance),
                    "Each Jumping Movement_Update must apply exactly " +
                    "Gravity_Acceleration * Elapsed_Simulation_Time. " + Describe(player, null, step));
                observedSteps++;
            }

            Assert.That(observedSteps, Is.GreaterThanOrEqualTo(5),
                "The fixture must observe several airborne Movement_Updates. " + Describe(player, null, 0));
        }

        // **Validates: Requirements 4.7, 12.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: rejected jumps preserve state and vertical velocity")]
        public IEnumerator RejectedJumpsPreserveStateAndVerticalVelocity_Requirements_4_7_And_12_6()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));
            yield return new WaitForFixedUpdate();
            AssertJumpRejectedWithoutSideEffects(player, PlayerState.Jumping);

            yield return LandByAutomaticStepping(player);
            Assert.That(player.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(player, null, 0));
            AssertJumpRejectedWithoutSideEffects(player, PlayerState.Sliding);

            Assert.That(player.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a Failure_Command. " + Describe(player, null, 0));
            AssertJumpRejectedWithoutSideEffects(player, PlayerState.Failed);
        }

        // **Validates: Requirements 4.8, 12.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: a valid landing returns the player to Running")]
        public IEnumerator ValidLandingReturnsToRunning_Requirements_4_8_And_12_6()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));

            var budget = FlightStepBudget();
            var landed = false;
            var velocityAtLanding = 0f;
            for (var step = 0; step < budget && !landed; step++)
            {
                var before = player.Snapshot.VerticalVelocity;
                var stateBefore = player.CurrentState;
                yield return new WaitForFixedUpdate();

                if (stateBefore != PlayerState.Jumping) continue;
                if (player.CurrentState != PlayerState.Running) continue;

                landed = true;
                velocityAtLanding = before;
            }

            Assert.That(landed, Is.True,
                "The jump must land within the ballistic step budget. " + Describe(player, null, budget));
            Assert.That(velocityAtLanding, Is.LessThanOrEqualTo(0f),
                "A valid landing requires non-positive vertical velocity. " + Describe(player, null, budget));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "A valid landing must transition Jumping to Running. " + Describe(player, null, budget));
            Assert.That(player.IsGrounded, Is.True,
                "A valid landing must expose Grounded. " + Describe(player, null, budget));
            Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(0f).Within(VelocityTolerance),
                "A valid landing must clear vertical velocity. " + Describe(player, null, budget));
            Assert.That(player.transform.position.y,
                Is.LessThanOrEqualTo(PlayerSpawn.y + Defaults.GroundContactTolerance),
                "A landed player must rest on the running surface. " + Describe(player, null, budget));
        }

        // **Validates: Requirements 4.1, 4.2, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the grounded query tracks contact across game events")]
        public IEnumerator GroundedQueryTracksGameEvents_Requirements_4_1_4_2_And_14_10()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            var queries = (IPlayerQueries)player;
            AssertGroundedQueryAgreement(player, queries, "grounded Running");
            Assert.That(queries.IsGrounded, Is.True,
                "A player resting on a valid running surface must expose Grounded. " +
                Describe(player, null, 0));

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));
            for (var step = 0; step < 6; step++) yield return new WaitForFixedUpdate();

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Jumping),
                "The fixture must still be airborne mid-flight. " + Describe(player, null, 6));
            Assert.That(queries.IsGrounded, Is.False,
                "An airborne player without Valid_Ground_Contact must expose Grounded as false. " +
                Describe(player, null, 6));
            AssertGroundedQueryAgreement(player, queries, "airborne Jumping");

            yield return LandByAutomaticStepping(player);
            AssertGroundedQueryAgreement(player, queries, "landed Running");
            Assert.That(queries.IsGrounded, Is.True,
                "A landed player must expose Grounded. " + Describe(player, null, 0));

            Assert.That(player.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a Failure_Command. " + Describe(player, null, 0));
            yield return new WaitForFixedUpdate();
            AssertGroundedQueryAgreement(player, queries, "Failed");
            Assert.That(queries.IsGrounded, Is.True,
                "Grounded must keep reporting the existence of Valid_Ground_Contact in Failed. " +
                Describe(player, null, 0));

            Assert.That(player.RequestReset("grounding-jump-reset-1").Status,
                Is.EqualTo(CommandStatus.Accepted),
                "A Failed player must accept a first Reset_Request. " + Describe(player, null, 0));
            yield return new WaitForFixedUpdate();
            AssertGroundedQueryAgreement(player, queries, "after a Reset_Request");
        }

        // **Validates: Requirements 4.9, 12.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: airborne lane and forward movement continue")]
        public IEnumerator AirborneLaneAndForwardMovementContinue_Requirements_4_9_And_12_6()
        {
            var player = CreatePlayer(SupportGeometry.ValidSurface);
            yield return Settle(player);

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(player, null, 0));
            Assert.That(player.RequestLane(LaneDirection.Right).Status, Is.EqualTo(CommandStatus.Accepted),
                "A Jumping player must accept a Lane_Request. " + Describe(player, null, 0));

            var targetCenter = Defaults.LaneCenters.z;
            var expectedForward = player.ForwardSpeed * FixedStep;
            var laneSteps = Mathf.CeilToInt(Defaults.LaneChangeDuration / FixedStep) + 2;
            for (var step = 0; step < laneSteps; step++)
            {
                var beforeZ = player.transform.position.z;
                var beforeX = player.transform.position.x;
                yield return new WaitForFixedUpdate();

                Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Jumping),
                    "The lane change must complete while the player is still airborne. " +
                    Describe(player, null, step));
                Assert.That(player.LastRequestedDisplacement.z, Is.EqualTo(expectedForward).Within(LinearTolerance),
                    "Jumping must continue automatic forward displacement. " + Describe(player, null, step));
                Assert.That(player.transform.position.z, Is.GreaterThan(beforeZ),
                    "Jumping must keep moving along positive Z. " + Describe(player, null, step));
                Assert.That(player.transform.position.x, Is.GreaterThanOrEqualTo(beforeX - LinearTolerance),
                    "Airborne lane movement must progress toward the target Lane_Center. " +
                    Describe(player, null, step));
                Assert.That(player.transform.position.x, Is.LessThanOrEqualTo(targetCenter + LinearTolerance),
                    "Airborne lane movement must not cross the target Lane_Center. " +
                    Describe(player, null, step));
            }

            Assert.That(Mathf.Abs(player.transform.position.x - targetCenter),
                Is.LessThanOrEqualTo(Defaults.LanePositionTolerance),
                "An airborne lane change must arrive within Lane_Position_Tolerance. " +
                Describe(player, null, laneSteps));
            Assert.That(player.Snapshot.CurrentLane, Is.EqualTo(LogicalLane.Right),
                "A completed airborne lane change must update the current Logical_Lane. " +
                Describe(player, null, laneSteps));
        }

        private static void AssertJumpRejectedWithoutSideEffects(
            PlayerControllerFacade player,
            PlayerState expectedState)
        {
            Assert.That(player.CurrentState, Is.EqualTo(expectedState),
                "The fixture must occupy " + expectedState + " before the rejected Jump_Request. " +
                Describe(player, null, 0));

            var before = player.Snapshot;
            var result = player.RequestJump();

            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Jump_Request in " + expectedState + " must be rejected. " + Describe(player, null, 0));
            Assert.That(result.Reason, Is.EqualTo(RejectionReason.InvalidState),
                "A Jump_Request in " + expectedState + " must be rejected as InvalidState. " +
                Describe(player, null, 0));
            Assert.That(result.CurrentState, Is.EqualTo(expectedState),
                "A rejected Jump_Request must report the unchanged Player_State. " +
                Describe(player, null, 0));
            Assert.That(player.CurrentState, Is.EqualTo(expectedState),
                "A rejected Jump_Request must preserve Player_State. " + Describe(player, null, 0));
            Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(before.VerticalVelocity),
                "A rejected Jump_Request must preserve vertical velocity. " + Describe(player, null, 0));
            Assert.That(player.Snapshot.PendingActionRequests.Count, Is.EqualTo(0),
                "A rejected Jump_Request must not enqueue a pending action request. " +
                Describe(player, null, 0));
        }

        private static void AssertGroundedQueryAgreement(
            PlayerControllerFacade player,
            IPlayerQueries queries,
            string context)
        {
            Assert.That(queries.IsGrounded, Is.EqualTo(player.Snapshot.IsGrounded),
                "Grounded_Status_Query must return the current Grounded value while " + context + ". " +
                Describe(player, null, 0));
        }

        private IEnumerator Settle(PlayerControllerFacade player)
        {
            for (var index = 0; index < SettleSteps; index++) yield return new WaitForFixedUpdate();
            Assert.That(player.MovementUpdateCount, Is.GreaterThanOrEqualTo(SettleSteps),
                "Each fixed step must run one Movement_Update. " + Describe(player, null, SettleSteps));
        }

        private IEnumerator LandByAutomaticStepping(PlayerControllerFacade player)
        {
            var budget = FlightStepBudget();
            for (var step = 0; step < budget; step++)
            {
                if (player.CurrentState == PlayerState.Running && player.IsGrounded) yield break;
                yield return new WaitForFixedUpdate();
            }

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "The jump must land within the ballistic step budget. " + Describe(player, null, budget));
            Assert.That(player.IsGrounded, Is.True,
                "A landed player must expose Grounded. " + Describe(player, null, budget));
        }

        private static void LandByManualStepping(PlayerControllerFacade player)
        {
            var budget = FlightStepBudget();
            for (var step = 0; step < budget; step++)
            {
                if (player.CurrentState == PlayerState.Running && player.IsGrounded) return;
                player.ExecuteMovementUpdate(FixedStep);
            }

            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "The jump must land within the ballistic step budget. " + Describe(player, null, budget));
            Assert.That(player.IsGrounded, Is.True,
                "A landed player must expose Grounded. " + Describe(player, null, budget));
        }

        private static int FlightStepBudget()
        {
            return Mathf.CeilToInt(
                4f * Defaults.JumpVelocity / (Defaults.GravityAcceleration * FixedStep)) + 20;
        }

        private static string Describe(PlayerControllerFacade player, SupportGeometry? geometry, int step)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[geometry={0}, step={1}, state={2}, grounded={3}, verticalVelocity={4}, position={5}, " +
                "requested={6}, updates={7}]",
                geometry.HasValue ? geometry.Value.ToString() : "unspecified",
                step,
                player.CurrentState,
                player.IsGrounded,
                player.Snapshot.VerticalVelocity.ToString("R", CultureInfo.InvariantCulture),
                player.transform.position.ToString("R", CultureInfo.InvariantCulture),
                player.LastRequestedDisplacement.ToString("R", CultureInfo.InvariantCulture),
                player.MovementUpdateCount);
        }

        private PlayerControllerFacade CreatePlayer(SupportGeometry geometry)
        {
            CreateGeometry(geometry);

            var player = new GameObject("PlayerControllerFixture");
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = PlayerSpawn;

            var controller = player.AddComponent<CharacterController>();
            controller.radius = Defaults.BaselineCollider.Radius;
            controller.height = Defaults.BaselineCollider.Height;
            controller.center = Defaults.BaselineCollider.Center;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.1f;

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

        private void CreateGeometry(SupportGeometry geometry)
        {
            switch (geometry)
            {
                case SupportGeometry.ValidSurface:
                    CreateRunningSurface("RunningSurfaceDouble", GroundLayer(), 0f, false);
                    break;
                case SupportGeometry.WrongLayerSurface:
                    CreateRunningSurface("WrongLayerSurfaceDouble", NonGroundLayer(), 0f, false);
                    break;
                case SupportGeometry.UnmarkedGeometry:
                    CreateSurfaceBody("UnmarkedGeometry", GroundLayer(), 0f, false);
                    break;
                case SupportGeometry.TriggerSurface:
                    CreateRunningSurface("TriggerSurfaceDouble", GroundLayer(), 0f, true);
                    break;
                case SupportGeometry.Wall:
                    CreateWall();
                    break;
                case SupportGeometry.Ceiling:
                    CreateCeiling();
                    break;
                case SupportGeometry.Coin:
                    CreateEnvironmentObject("CoinDouble", EnvironmentObjectKind.Coin, true);
                    break;
                case SupportGeometry.Obstacle:
                    CreateEnvironmentObject("ObstacleDouble", EnvironmentObjectKind.Obstacle, false);
                    break;
                case SupportGeometry.OutOfToleranceSurface:
                    CreateRunningSurface(
                        "OutOfToleranceSurfaceDouble", GroundLayer(),
                        PlayerSpawn.y - 6f * Defaults.GroundContactTolerance, false);
                    break;
                case SupportGeometry.ShallowSurface:
                    CreateTiltedSurface("ShallowSurfaceDouble", 20f);
                    break;
                case SupportGeometry.SteepSurface:
                    CreateTiltedSurface("SteepSurfaceDouble", 60f);
                    break;
            }
        }

        /// <summary>Marked, non-trigger support geometry whose top face sits at <paramref name="topY"/>.</summary>
        private void CreateRunningSurface(string name, int layer, float topY, bool isTrigger)
        {
            var surface = CreateSurfaceBody(name, layer, topY, isTrigger);
            surface.AddComponent<RunningSurfaceDouble>();
        }

        private GameObject CreateSurfaceBody(string name, int layer, float topY, bool isTrigger)
        {
            var body = new GameObject(name);
            spawnedObjects.Add(body);
            body.layer = layer;
            body.transform.position = new Vector3(0f, topY - 0.5f, 40f);
            var collider = body.AddComponent<BoxCollider>();
            collider.size = new Vector3(40f, 1f, 200f);
            collider.isTrigger = isTrigger;
            return body;
        }

        /// <summary>A marked vertical face beside the player: support geometry with no upward normal.</summary>
        private void CreateWall()
        {
            var wall = new GameObject("WallSurfaceDouble");
            spawnedObjects.Add(wall);
            wall.layer = GroundLayer();
            var faceX = PlayerSpawn.x + Defaults.BaselineCollider.Radius + 0.05f;
            wall.transform.position = new Vector3(faceX + 1f, 3f, 40f);
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 6f, 200f);
            wall.AddComponent<RunningSurfaceDouble>();
        }

        /// <summary>Marked support geometry above the player capsule.</summary>
        private void CreateCeiling()
        {
            var ceiling = new GameObject("CeilingSurfaceDouble");
            spawnedObjects.Add(ceiling);
            ceiling.layer = GroundLayer();
            var bottomY = PlayerSpawn.y + Defaults.BaselineCollider.Height + 0.5f;
            ceiling.transform.position = new Vector3(0f, bottomY + 0.5f, 40f);
            var collider = ceiling.AddComponent<BoxCollider>();
            collider.size = new Vector3(40f, 1f, 200f);
            ceiling.AddComponent<RunningSurfaceDouble>();
        }

        /// <summary>An environment object directly under the player that carries no surface marker.</summary>
        private void CreateEnvironmentObject(string name, EnvironmentObjectKind kind, bool isTrigger)
        {
            var body = CreateSurfaceBody(name, GroundLayer(), 0f, isTrigger);
            var environmentObject = body.AddComponent<EnvironmentObjectDouble>();
            environmentObject.Configure(name, kind, kind == EnvironmentObjectKind.Coin ? 1f : 0f);
            if (kind == EnvironmentObjectKind.Obstacle) body.AddComponent<EnvironmentObstructionDouble>();
        }

        /// <summary>
        /// Marked support geometry tilted by <paramref name="tiltDegrees"/> and placed so the contact
        /// distance stays inside Ground_Contact_Tolerance. A twenty degree tilt keeps the upward
        /// normal component at 0.94 and grounds the player; sixty degrees drops it to 0.5, below
        /// Ground_Normal_Threshold, so only the support normal can decide the outcome.
        /// </summary>
        private void CreateTiltedSurface(string name, float tiltDegrees)
        {
            var slope = new GameObject(name);
            spawnedObjects.Add(slope);
            slope.layer = GroundLayer();

            var tilt = Quaternion.Euler(0f, 0f, tiltDegrees);
            slope.transform.rotation = tilt;

            var radius = Defaults.BaselineCollider.Radius;
            var lowerSphereCenter = PlayerSpawn + Defaults.BaselineCollider.Center -
                Vector3.up * (Defaults.BaselineCollider.Height * 0.5f - radius);
            var normal = tilt * Vector3.up;

            // The probe sphere is placed just outside the tilted face, so the downward capsule probe
            // resolves the tilted normal within Ground_Contact_Tolerance instead of reporting a
            // degenerate overlap normal.
            const float halfThickness = 0.5f;
            const float faceClearance = 0.03f;
            slope.transform.position =
                lowerSphereCenter - normal * (halfThickness + radius + faceClearance);

            var collider = slope.AddComponent<BoxCollider>();
            collider.size = new Vector3(20f, 2f * halfThickness, 200f);
            slope.AddComponent<RunningSurfaceDouble>();
        }

        private UnityEngine.Object CreateReferencePlaceholder()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "GroundingFixtureReferencePlaceholder";
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

        private static int GroundLayer()
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((Defaults.GroundLayerMask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private static int NonGroundLayer()
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((Defaults.GroundLayerMask & (1 << layer)) == 0) return layer;
            }

            return 0;
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

        private sealed class EnvironmentObjectDouble : MonoBehaviour, IEnvironmentObject
        {
            [SerializeField] private string environmentObjectId;
            [SerializeField] private EnvironmentObjectKind kind;
            [SerializeField] private float collectibleValue;

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

        private sealed class ReferencePlaceholderDouble : ScriptableObject
        {
        }
    }
}
