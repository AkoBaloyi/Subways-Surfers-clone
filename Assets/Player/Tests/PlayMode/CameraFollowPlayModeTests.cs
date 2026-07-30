using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;
using UnityEngine.TestTools;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode coverage for the camera adapter Task 8.7 has to provide. The adapter is reached
    /// through a reflection seam, so this fixture states the required public surface before the
    /// component exists and fails with a message naming the missing member.
    ///
    /// The facts pinned here are the ones a plausible-looking follow implementation gets wrong.
    /// Camera_Follow_Offset is derived from the configured player and camera poses rather than
    /// serialized on its own, so the fixture places the configured player away from the origin: an
    /// implementation that treats the configured camera position as the offset answers differently.
    /// The target is derived from the player's position in every Player_State, including Failed, so
    /// the state coverage drives a real facade through lane changes, Jumping, Sliding, and Failed
    /// instead of asserting against a scripted position alone. Convergence is observed both through
    /// the component's own per-frame drive across yielded frames and through the public per-frame
    /// entry point with an explicit elapsed time, which is what makes the error arithmetic exact:
    /// positive-time frames must reduce Camera_Position_Error without leaving the segment toward the
    /// current target, a zero-time frame must preserve it, a continuously moving target must keep
    /// converging, and a target that becomes fixed must be reached within Camera_Settle_Duration.
    ///
    /// Prefab-style serialization is exercised without creating an asset: a rig is built
    /// programmatically, the adapter's serialized facade reference is assigned, and the rig is
    /// instantiated. Instantiation round-trips the serialized data and remaps references inside the
    /// copied hierarchy, so the clone has to resolve its own facade's query contract - the same path
    /// a prefab instance takes on load.
    ///
    /// Camera math itself is not reimplemented here; the adapter is expected to consume the pure
    /// <see cref="CameraConvergenceState"/> from Task 6.2, and the expected values below are the
    /// clamped-lerp behavior that state already defines. Every fixture object is built
    /// programmatically and destroyed in teardown.
    /// </summary>
    public sealed class CameraFollowPlayModeTests
    {
        private const float FrameStep = 0.02f;
        private const float LinearTolerance = 0.0005f;
        private const int SettleSteps = 3;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;

        /// <summary>
        /// A configured player pose that is not the origin, so Camera_Follow_Offset cannot be
        /// confused with the configured camera position and every derived value differs from the
        /// value a copied offset would produce.
        /// </summary>
        private static readonly Vector3 ScriptedPlayerStart = new Vector3(3f, 0.5f, 7f);

        private static readonly Vector3 RealPlayerSpawn =
            new Vector3(Defaults.LaneCenters.y, 0.02f, 0f);

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<ScriptableObject> spawnedAssets = new List<ScriptableObject>();

        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;

        [SetUp]
        public void SetUp()
        {
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            Time.fixedDeltaTime = FrameStep;

            // One physics step per rendered frame keeps fixed-step counts deterministic.
            Time.maximumDeltaTime = FrameStep;
        }

        [TearDown]
        public void TearDown()
        {
            ClearFixtures();
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 9.1, 12.10**
        [Test]
        [Description("Feature: player-controller, Play Mode: the initial follow offset is derived from the configured player and camera poses")]
        public void DerivesInitialOffsetFromConfiguredPoses_Requirements_9_1_And_12_10()
        {
            var player = new ScriptedPlayerQueries(ScriptedPlayerStart, PlayerState.Running);
            var seam = CreateCameraFollow("DerivedOffsetFixture");

            // An adapter that has not been given a player must stay inert rather than throw or move.
            var beforeConfiguration = seam.Transform.position;
            Assert.That(seam.PlayerResolved, Is.False,
                "An adapter with no serialized reference and no Configure call must report no " +
                "resolved player. " + Describe(seam, player));
            seam.ExecuteCameraFollowUpdate(FrameStep);
            Assert.That(seam.Transform.position, Is.EqualTo(beforeConfiguration),
                "An unresolved adapter must leave the camera transform untouched. " +
                Describe(seam, player));

            seam.Configure(player, Defaults);

            var derivedOffset = Defaults.InitialCameraPosition - ScriptedPlayerStart;
            Assert.That(seam.PlayerResolved, Is.True,
                "Configure must resolve the supplied Player_Controller query contract. " +
                Describe(seam, player));
            Assert.That((seam.CameraFollowOffset - derivedOffset).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "Camera_Follow_Offset must be the configured camera pose minus the configured " +
                "player pose, not the configured camera position itself. " + Describe(seam, player));
            Assert.That((seam.CameraFollowOffset - Defaults.InitialCameraPosition).magnitude,
                Is.GreaterThan(LinearTolerance),
                "Fixture precondition: the configured player pose is away from the origin, so a " +
                "copied configured camera position must not equal the derived offset. " +
                Describe(seam, player));

            Assert.That((seam.CameraPosition - Defaults.InitialCameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "Initialization must place the camera at the configured initial camera pose. " +
                Describe(seam, player));
            Assert.That((seam.Transform.position - seam.CameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The reported camera position must be the camera transform's position. " +
                Describe(seam, player));
            Assert.That((seam.CameraTargetPosition - Defaults.InitialCameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "Camera_Target_Position for the configured player pose must be the configured " +
                "camera pose, which is what makes the initial error zero. " + Describe(seam, player));
            Assert.That(seam.CameraPositionError,
                Is.LessThanOrEqualTo(Defaults.CameraFollowTolerance),
                "The configured poses must produce an initial Camera_Position_Error inside " +
                "Camera_Follow_Tolerance. " + Describe(seam, player));

            CameraFollowSeam.AssertNoIndependentlySerializedOffset();
        }

        // **Validates: Requirements 9.5, 9.6, 12.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the target follows the player through lane changes, Jumping, Sliding, and Failed")]
        public IEnumerator TargetFollowsPlayerThroughLaneJumpSlideAndFailed_Requirements_9_5_9_6_And_12_10()
        {
            CreateRunningSurface();
            var facade = CreateFacade("StateFollowingPlayerFixture");
            for (var index = 0; index < SettleSteps; index++) yield return new WaitForFixedUpdate();

            var seam = CreateCameraFollow("StateFollowingCameraFixture");
            seam.Configure(facade, Defaults);

            // Both loops are stepped explicitly from here, so each assertion reads the player
            // position the camera frame was given rather than a position a later physics step moved.
            facade.enabled = false;
            seam.SetEnabled(false);

            var offset = seam.CameraFollowOffset;

            Assert.That(facade.RequestLane(LaneDirection.Right).Status,
                Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Lane_Request must be accepted. " + Describe(seam, facade));
            var laneSteps = Mathf.CeilToInt(Defaults.LaneChangeDuration / FrameStep) + 2;
            var lateralBefore = facade.Snapshot.Position.x;
            StepAndAssertTargetFollowsPlayer(seam, facade, offset, laneSteps, "a lane change");
            Assert.That(facade.Snapshot.Position.x, Is.GreaterThan(lateralBefore + LinearTolerance),
                "Fixture precondition: the lane change must move the player laterally. " +
                Describe(seam, facade));

            Assert.That(facade.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted. " + Describe(seam, facade));
            Assert.That(facade.CurrentState, Is.EqualTo(PlayerState.Jumping),
                "An accepted Jump_Request must transition to Jumping. " + Describe(seam, facade));
            var verticalBefore = facade.Snapshot.Position.y;
            StepAndAssertTargetFollowsPlayer(seam, facade, offset, 4, "Jumping");
            Assert.That(facade.Snapshot.Position.y, Is.GreaterThan(verticalBefore + LinearTolerance),
                "Fixture precondition: Jumping must move the player vertically. " +
                Describe(seam, facade));

            LandByManualStepping(seam, facade, offset);

            Assert.That(facade.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Slide_Request must be accepted. " + Describe(seam, facade));
            Assert.That(facade.CurrentState, Is.EqualTo(PlayerState.Sliding),
                "An accepted Slide_Request must transition to Sliding. " + Describe(seam, facade));
            StepAndAssertTargetFollowsPlayer(seam, facade, offset, 4, "Sliding");

            Assert.That(facade.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a Failure_Command. " + Describe(seam, facade));
            Assert.That(facade.CurrentState, Is.EqualTo(PlayerState.Failed),
                "An accepted Failure_Command must transition to Failed. " + Describe(seam, facade));
            StepAndAssertTargetFollowsPlayer(seam, facade, offset, 6, "Failed");
            Assert.That((seam.CameraFollowOffset - offset).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "Failed must retain the configured follow target and Camera_Follow_Offset. " +
                Describe(seam, facade));
        }

        /// <summary>
        /// Drives one movement update and one camera frame at a time and asserts the camera frame
        /// derived its target from the player position that update produced, using the unchanged
        /// Camera_Follow_Offset.
        /// </summary>
        private static void StepAndAssertTargetFollowsPlayer(
            CameraFollowSeam seam,
            PlayerControllerFacade facade,
            Vector3 offset,
            int steps,
            string context)
        {
            for (var step = 0; step < steps; step++)
            {
                facade.ExecuteMovementUpdate(FrameStep);
                var playerPosition = facade.Snapshot.Position;
                seam.ExecuteCameraFollowUpdate(FrameStep);

                Assert.That((seam.CameraTargetPosition - (playerPosition + offset)).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "During " + context + ", Camera_Target_Position must be the player position " +
                    "plus Camera_Follow_Offset. " + Describe(seam, facade));
                Assert.That((seam.CameraFollowOffset - offset).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "During " + context + ", Camera_Follow_Offset must not drift. " +
                    Describe(seam, facade));
                Assert.That((seam.Transform.position - seam.CameraPosition).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "During " + context + ", the camera transform must carry the reported camera " +
                    "position. " + Describe(seam, facade));
            }
        }

        private static void LandByManualStepping(
            CameraFollowSeam seam,
            PlayerControllerFacade facade,
            Vector3 offset)
        {
            var budget = Mathf.CeilToInt(
                4f * Defaults.JumpVelocity / (Defaults.GravityAcceleration * FrameStep)) + 20;
            for (var step = 0; step < budget; step++)
            {
                if (facade.CurrentState == PlayerState.Running && facade.IsGrounded) return;
                StepAndAssertTargetFollowsPlayer(seam, facade, offset, 1, "the return to ground");
            }

            Assert.That(facade.CurrentState, Is.EqualTo(PlayerState.Running),
                "The jump must land within the ballistic step budget. " + Describe(seam, facade));
        }

        // **Validates: Requirements 9.2, 9.3, 9.9, 12.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: positive-time frames converge and zero-time frames preserve camera position and error")]
        public IEnumerator PositiveTimeFramesConvergeAndZeroTimeFramesPreserveError_Requirements_9_2_9_3_9_9_And_12_10()
        {
            var player = new ScriptedPlayerQueries(ScriptedPlayerStart, PlayerState.Running);
            var seam = CreateCameraFollow("ConvergenceFixture");
            seam.Configure(player, Defaults);

            // First the component's own per-frame drive: rendered frames must converge with no
            // explicit stepping at all, which is what pins the adapter to the render-time frame.
            player.Position = ScriptedPlayerStart + new Vector3(2f, 1f, 6f);
            var automaticTarget = player.Position + seam.CameraFollowOffset;
            var automaticStartError = (automaticTarget - seam.CameraPosition).magnitude;
            Assert.That(automaticStartError, Is.GreaterThan(Defaults.CameraFollowTolerance),
                "Fixture precondition: the moved player must put the camera outside " +
                "Camera_Follow_Tolerance. " + Describe(seam, player));

            yield return null;
            yield return null;

            Assert.That((automaticTarget - seam.CameraPosition).magnitude,
                Is.LessThan(automaticStartError),
                "Rendered frames with positive elapsed camera-follow time must decrease " +
                "Camera_Position_Error without any explicit stepping. " + Describe(seam, player));
            Assert.That((seam.Transform.position - seam.CameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The camera transform must carry the converged camera position. " +
                Describe(seam, player));

            // From here the frames are supplied explicitly, so the arithmetic is exact.
            seam.SetEnabled(false);
            player.Position = ScriptedPlayerStart + new Vector3(-4f, 2f, 12f);
            var target = player.Position + seam.CameraFollowOffset;
            var start = seam.CameraPosition;
            var separation = (target - start).magnitude;
            Assert.That(separation, Is.GreaterThan(Defaults.CameraFollowTolerance),
                "Fixture precondition: the fixed target must start outside " +
                "Camera_Follow_Tolerance. " + Describe(seam, player));

            for (var frame = 0; frame < 6; frame++)
            {
                var before = seam.CameraPosition;
                var errorBefore = (target - before).magnitude;
                seam.ExecuteCameraFollowUpdate(FrameStep);
                var after = seam.CameraPosition;
                var errorAfter = (target - after).magnitude;

                Assert.That(errorAfter, Is.LessThan(errorBefore),
                    "A positive-time frame toward an unchanged target must decrease " +
                    "Camera_Position_Error. " + Describe(seam, player));
                Assert.That(Vector3.Dot(after - before, target - before),
                    Is.GreaterThanOrEqualTo(-LinearTolerance),
                    "A frame must never move the camera away from Camera_Target_Position. " +
                    Describe(seam, player));
                Assert.That((after - start).magnitude,
                    Is.LessThanOrEqualTo(separation + LinearTolerance),
                    "The camera must stay on or before Camera_Target_Position along the " +
                    "convergence path. " + Describe(seam, player));
                Assert.That(seam.CameraPositionError, Is.EqualTo(errorAfter).Within(LinearTolerance),
                    "The reported Camera_Position_Error must be the distance to " +
                    "Camera_Target_Position. " + Describe(seam, player));
                Assert.That((seam.Transform.position - after).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "Each frame must write the camera position to the camera transform. " +
                    Describe(seam, player));
            }

            var preservedPosition = seam.CameraPosition;
            var preservedError = seam.CameraPositionError;
            Assert.That(preservedError, Is.GreaterThan(Defaults.CameraFollowTolerance),
                "Fixture precondition: the zero-time frames must be observed while an error " +
                "remains, otherwise preservation is indistinguishable from convergence. " +
                Describe(seam, player));

            for (var frame = 0; frame < 3; frame++)
            {
                seam.ExecuteCameraFollowUpdate(0f);
                Assert.That(seam.CameraPosition, Is.EqualTo(preservedPosition),
                    "A frame representing zero elapsed camera-follow time must preserve the " +
                    "camera position exactly. " + Describe(seam, player));
                Assert.That(seam.CameraPositionError, Is.EqualTo(preservedError),
                    "A frame representing zero elapsed camera-follow time must preserve " +
                    "Camera_Position_Error exactly. " + Describe(seam, player));
                Assert.That((seam.Transform.position - preservedPosition).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "A zero-time frame must not move the camera transform. " +
                    Describe(seam, player));
            }

            // The zero-time frames must not have consumed the smoothing epoch either.
            seam.ExecuteCameraFollowUpdate(FrameStep);
            Assert.That(seam.CameraPositionError, Is.LessThan(preservedError),
                "A positive-time frame after zero-time frames must resume convergence. " +
                Describe(seam, player));
        }

        // **Validates: Requirements 9.3, 9.10, 12.10**
        [Test]
        [Description("Feature: player-controller, Play Mode: a continuously changing target keeps converging without overshoot")]
        public void ChangingTargetsContinueConvergingWithoutOvershoot_Requirements_9_3_9_10_And_12_10()
        {
            var player = new ScriptedPlayerQueries(ScriptedPlayerStart, PlayerState.Running);
            var seam = CreateCameraFollow("ChangingTargetFixture");
            seam.Configure(player, Defaults);
            seam.SetEnabled(false);

            var offset = seam.CameraFollowOffset;

            // A per-frame displacement at the configured Forward_Run_Speed changes the target on
            // every frame, which is the case a single-shot settle implementation stalls on.
            var displacement = new Vector3(0.4f, 0.1f, Defaults.ForwardSpeed * FrameStep);
            for (var frame = 0; frame < 20; frame++)
            {
                var before = seam.CameraPosition;
                player.Position = player.Position + displacement;
                var target = player.Position + offset;
                var errorBefore = (target - before).magnitude;
                Assert.That(errorBefore, Is.GreaterThan(0f),
                    "Fixture precondition: a moved player must put the camera off the current " +
                    "target. " + Describe(seam, player));

                seam.ExecuteCameraFollowUpdate(FrameStep);
                var after = seam.CameraPosition;
                var errorAfter = (target - after).magnitude;

                Assert.That((seam.CameraTargetPosition - target).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "A changed target must be taken from the current player position and the " +
                    "unchanged Camera_Follow_Offset. " + Describe(seam, player));
                Assert.That(errorAfter, Is.LessThan(errorBefore),
                    "A positive-time frame must continue convergence toward the current " +
                    "Camera_Target_Position even while the target keeps changing. " +
                    Describe(seam, player));
                Assert.That((after - before).magnitude + errorAfter,
                    Is.LessThanOrEqualTo(errorBefore + LinearTolerance),
                    "The camera must stay on the segment from its previous position to the " +
                    "current target, so it cannot overshoot. " + Describe(seam, player));
                Assert.That((seam.CameraFollowOffset - offset).magnitude,
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "A changing target must not change Camera_Follow_Offset. " +
                    Describe(seam, player));
            }
        }

        // **Validates: Requirements 9.4, 12.10**
        [Test]
        [Description("Feature: player-controller, Play Mode: a target that becomes fixed is reached within the configured settle duration")]
        public void FixedTargetSettlesWithinConfiguredDuration_Requirements_9_4_And_12_10()
        {
            var player = new ScriptedPlayerQueries(ScriptedPlayerStart, PlayerState.Running);
            var seam = CreateCameraFollow("SettleDurationFixture");
            seam.Configure(player, Defaults);
            seam.SetEnabled(false);

            // The target changes once and then stays fixed, which is the case the configured
            // Camera_Settle_Duration bounds.
            player.Position = ScriptedPlayerStart + new Vector3(6f, -2f, 15f);
            var target = player.Position + seam.CameraFollowOffset;
            var start = seam.CameraPosition;
            var separation = (target - start).magnitude;
            Assert.That(separation, Is.GreaterThan(Defaults.CameraFollowTolerance),
                "Fixture precondition: the changed target must start outside " +
                "Camera_Follow_Tolerance. " + Describe(seam, player));

            var frames = Mathf.CeilToInt(Defaults.CameraSettleDuration / FrameStep);
            for (var frame = 0; frame < frames; frame++)
            {
                seam.ExecuteCameraFollowUpdate(FrameStep);
                Assert.That((seam.CameraPosition - start).magnitude,
                    Is.LessThanOrEqualTo(separation + LinearTolerance),
                    "Convergence must not overshoot the fixed target. " + Describe(seam, player));
            }

            Assert.That(frames * FrameStep,
                Is.LessThanOrEqualTo(Defaults.CameraSettleDuration + FrameStep),
                "Fixture precondition: the stepped frames must cover Camera_Settle_Duration " +
                "without exceeding it by more than one frame. " + Describe(seam, player));
            Assert.That(seam.CameraPositionError,
                Is.LessThanOrEqualTo(Defaults.CameraFollowTolerance),
                "A target that becomes fixed must be reached to within Camera_Follow_Tolerance " +
                "inside Camera_Settle_Duration. " + Describe(seam, player));
            Assert.That((seam.CameraPosition - target).magnitude,
                Is.LessThanOrEqualTo(Defaults.CameraFollowTolerance),
                "The settled camera position must be the fixed Camera_Target_Position. " +
                Describe(seam, player));
            Assert.That(seam.RemainingCameraSettleTime, Is.EqualTo(0f),
                "A settled target must leave no remaining smoothing budget. " +
                Describe(seam, player));
        }

        // **Validates: Requirements 9.1, 9.5, 12.10**
        [Test]
        [Description("Feature: player-controller, Play Mode: a serialized facade reference resolves to the query contract across a prefab-style serialization round trip")]
        public void SerializedFacadeReferenceResolvesAcrossPrefabStyleReload_Requirements_9_1_9_5_And_12_10()
        {
            PlayerControllerFacade facade;
            MonoBehaviour follow;
            var rig = CreateFollowRig("CameraFollowRig", out facade, out follow);

            // Activation runs the facade's own configuration pass, which is the initialization path a
            // prefab instance takes: the adapter has to resolve its serialized reference itself.
            rig.SetActive(true);
            var originalSeam = CameraFollowSeam.FromComponent(follow);
            facade.enabled = false;
            originalSeam.SetEnabled(false);

            Assert.That(facade.SimulationEnabled, Is.True,
                "The rig configuration must enable movement simulation. Diagnostics: " +
                RenderDiagnostics(facade));
            Assert.That(originalSeam.PlayerResolved, Is.True,
                "Facade-driven initialization must resolve the serialized reference to the " +
                "Player_Controller query contract. " + Describe(originalSeam, facade));
            Assert.That((originalSeam.CameraFollowOffset -
                    (Defaults.InitialCameraPosition - RealPlayerSpawn)).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The serialized route must derive Camera_Follow_Offset from the configured player " +
                "and camera poses. " + Describe(originalSeam, facade));

            // Instantiation round-trips the serialized data and remaps references inside the copied
            // hierarchy, which is the reload the adapter has to survive. The clone is placed clear of
            // the original so the two players can move independently.
            var displacement = new Vector3(20f, 0f, 0f);
            var clone = UnityEngine.Object.Instantiate(rig, displacement, Quaternion.identity);
            clone.name = "CameraFollowRigReloaded";
            spawnedObjects.Add(clone);

            var cloneFacade = clone.GetComponentInChildren<PlayerControllerFacade>();
            Assert.That(cloneFacade, Is.Not.Null,
                "The reloaded rig must carry its own facade. " + Describe(originalSeam, facade));
            var cloneSeam = CameraFollowSeam.FromComponent(
                CameraFollowSeam.FindOn(clone, "the reloaded rig"));
            cloneFacade.enabled = false;
            cloneSeam.SetEnabled(false);

            Assert.That(cloneFacade, Is.Not.SameAs(facade),
                "Fixture precondition: the reload must produce a distinct facade. " +
                Describe(cloneSeam, cloneFacade));
            Assert.That(cloneFacade.SimulationEnabled, Is.True,
                "The reloaded configuration must enable movement simulation. Diagnostics: " +
                RenderDiagnostics(cloneFacade));
            Assert.That(cloneSeam.PlayerResolved, Is.True,
                "A serialized concrete reference must still resolve to the query contract after a " +
                "serialization round trip. " + Describe(cloneSeam, cloneFacade));
            Assert.That((cloneFacade.Snapshot.Position - (RealPlayerSpawn + displacement)).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "Fixture precondition: the reloaded player must start clear of the original. " +
                Describe(cloneSeam, cloneFacade));
            Assert.That((cloneSeam.CameraFollowOffset -
                    (Defaults.InitialCameraPosition - (RealPlayerSpawn + displacement))).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The reloaded adapter must derive Camera_Follow_Offset from its own player pose and " +
                "the configured camera pose. " + Describe(cloneSeam, cloneFacade));

            // Only the reloaded player moves, so the reloaded target can only be right if the
            // reloaded adapter resolved the facade in its own hierarchy.
            var beforeZ = cloneFacade.Snapshot.Position.z;
            cloneFacade.ExecuteMovementUpdate(FrameStep);
            var clonePlayer = cloneFacade.Snapshot.Position;
            Assert.That(clonePlayer.z, Is.GreaterThan(beforeZ + LinearTolerance),
                "Fixture precondition: one Movement_Update must move the reloaded player forward. " +
                Describe(cloneSeam, cloneFacade));

            cloneSeam.ExecuteCameraFollowUpdate(FrameStep);
            originalSeam.ExecuteCameraFollowUpdate(FrameStep);

            Assert.That((cloneSeam.CameraTargetPosition -
                    (clonePlayer + cloneSeam.CameraFollowOffset)).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The reloaded adapter must derive Camera_Target_Position from the facade in its own " +
                "hierarchy. " + Describe(cloneSeam, cloneFacade));
            Assert.That((originalSeam.CameraTargetPosition -
                    (facade.Snapshot.Position + originalSeam.CameraFollowOffset)).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The original adapter must keep following its own facade. " +
                Describe(originalSeam, facade));
            Assert.That((cloneSeam.CameraTargetPosition - originalSeam.CameraTargetPosition).magnitude,
                Is.GreaterThan(LinearTolerance),
                "A reloaded adapter that resolved the original facade would produce the same " +
                "target; the two targets must differ once only the reloaded player has moved. " +
                Describe(cloneSeam, cloneFacade));
        }

        // **Validates: Requirements 9.7, 9.8, 12.10**
        [Test]
        [Description("Feature: player-controller, Play Mode: the reset hook restores the configured camera pose and clears smoothing state")]
        public void ResetRestoresConfiguredPoseAndClearsSmoothingState_Requirements_9_7_9_8_And_12_10()
        {
            var player = new ScriptedPlayerQueries(ScriptedPlayerStart, PlayerState.Running);
            var seam = CreateCameraFollow("ResetPoseFixture");
            seam.Configure(player, Defaults);
            seam.SetEnabled(false);

            var offset = seam.CameraFollowOffset;

            // Mid-convergence, with velocity and smoothing budget outstanding, and after a failure so
            // reset is exercised from a state the reset service actually accepts.
            player.Position = ScriptedPlayerStart + new Vector3(5f, 3f, 9f);
            player.State = PlayerState.Failed;
            seam.ExecuteCameraFollowUpdate(FrameStep);
            seam.ExecuteCameraFollowUpdate(FrameStep);

            Assert.That(seam.CameraPositionError, Is.GreaterThan(Defaults.CameraFollowTolerance),
                "Fixture precondition: the camera must be mid-convergence before the reset. " +
                Describe(seam, player));
            Assert.That(seam.CameraConvergenceVelocity.magnitude, Is.GreaterThan(LinearTolerance),
                "Fixture precondition: the camera must carry convergence velocity before the " +
                "reset. " + Describe(seam, player));
            Assert.That(seam.RemainingCameraSettleTime, Is.GreaterThan(0f),
                "Fixture precondition: smoothing progress must be outstanding before the reset. " +
                Describe(seam, player));

            // The reset service restores the player to its configured start pose before restoring the
            // camera, so the reset hook is invoked against the restored player.
            player.Position = ScriptedPlayerStart;
            player.State = PlayerState.Running;
            seam.ResetCameraPose();

            AssertRestoredCameraPose(seam, player, offset, "the first reset");

            // Nothing stale may survive into the next rendered frame, and a zero-time frame must not
            // disturb the restored pose either.
            seam.ExecuteCameraFollowUpdate(FrameStep);
            AssertRestoredCameraPose(seam, player, offset, "the frame after the first reset");
            seam.ExecuteCameraFollowUpdate(0f);
            AssertRestoredCameraPose(seam, player, offset, "a zero-time frame after the reset");

            // Consecutive resets are repeat-safe: the restored pose does not accumulate.
            seam.ResetCameraPose();
            AssertRestoredCameraPose(seam, player, offset, "a consecutive reset");
        }

        private static void AssertRestoredCameraPose(
            CameraFollowSeam seam,
            IPlayerQueries player,
            Vector3 offset,
            string context)
        {
            Assert.That((seam.CameraPosition - Defaults.InitialCameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "After " + context + ", the configured initial camera pose must be restored. " +
                Describe(seam, player));
            Assert.That((seam.Transform.position - Defaults.InitialCameraPosition).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "After " + context + ", the camera transform must carry the configured initial " +
                "camera pose. " + Describe(seam, player));
            Assert.That((seam.CameraFollowOffset - offset).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "After " + context + ", Camera_Follow_Offset must still be the offset derived from " +
                "the configured poses. " + Describe(seam, player));
            Assert.That(seam.CameraPositionError,
                Is.LessThanOrEqualTo(Defaults.CameraFollowTolerance),
                "After " + context + ", Camera_Position_Error must be inside " +
                "Camera_Follow_Tolerance. " + Describe(seam, player));
            Assert.That(seam.CameraConvergenceVelocity.magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "After " + context + ", prior convergence velocity must be cleared. " +
                Describe(seam, player));
            Assert.That(seam.RemainingCameraSettleTime, Is.EqualTo(0f),
                "After " + context + ", prior smoothing progress must be cleared. " +
                Describe(seam, player));
        }

        private static string Describe(CameraFollowSeam seam, IPlayerQueries player)
        {
            var described = player == null
                ? "[player=none] "
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "[state={0}, playerPosition={1}] ",
                    player.CurrentState,
                    player.Snapshot.Position.ToString("R", CultureInfo.InvariantCulture));
            return described + (seam == null ? "[camera=none]" : seam.Render());
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

        /// <summary>
        /// A camera object carrying the adapter, built as its own root so the followed player's
        /// motion never reaches the camera through a parent transform.
        /// </summary>
        private CameraFollowSeam CreateCameraFollow(string name)
        {
            var camera = new GameObject(name);
            spawnedObjects.Add(camera);
            camera.transform.position = Vector3.zero;
            return CameraFollowSeam.FromComponent(CameraFollowSeam.AddTo(camera));
        }

        /// <summary>
        /// A configured facade on its own root at the configured start pose, wired with player-owned
        /// reference placeholders so validation enables simulation.
        /// </summary>
        private PlayerControllerFacade CreateFacade(string name)
        {
            var player = new GameObject(name);
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = RealPlayerSpawn;
            AddPlayerParts(player);
            player.SetActive(true);

            var facade = player.GetComponent<PlayerControllerFacade>();
            Assert.That(facade.SimulationEnabled, Is.True,
                "The fixture configuration must enable movement simulation. Diagnostics: " +
                RenderDiagnostics(facade));
            return facade;
        }

        /// <summary>
        /// A prefab-shaped rig: a neutral root with the player and the camera as siblings, the
        /// adapter's serialized facade reference assigned, and the adapter registered as a facade
        /// configuration consumer. The rig is returned inactive so the serialized wiring is complete
        /// before any initialization runs, exactly as it would be for a prefab on load.
        /// </summary>
        private GameObject CreateFollowRig(
            string name,
            out PlayerControllerFacade facade,
            out MonoBehaviour follow)
        {
            var rig = new GameObject(name);
            spawnedObjects.Add(rig);
            rig.SetActive(false);
            rig.transform.position = Vector3.zero;

            var player = new GameObject("Player");
            player.transform.SetParent(rig.transform, false);
            player.transform.position = RealPlayerSpawn;
            AddPlayerParts(player);
            facade = player.GetComponent<PlayerControllerFacade>();

            var camera = new GameObject("FollowCamera");
            camera.transform.SetParent(rig.transform, false);
            camera.transform.position = Defaults.InitialCameraPosition;
            follow = CameraFollowSeam.AddTo(camera);

            CameraFollowSeam.AssignSerializedPlayerQuerySource(follow, facade);
            InjectReference(facade, "configurationConsumers", new MonoBehaviour[] { follow });
            return rig;
        }

        private void AddPlayerParts(GameObject player)
        {
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
        }

        /// <summary>Marked, non-trigger support geometry whose top face sits at the world origin.</summary>
        private void CreateRunningSurface()
        {
            var surface = new GameObject("RunningSurfaceDouble");
            spawnedObjects.Add(surface);
            surface.layer = GroundLayer();
            surface.transform.position = new Vector3(0f, -0.5f, 0f);

            var collider = surface.AddComponent<BoxCollider>();
            collider.size = new Vector3(40f, 1f, 400f);
            surface.AddComponent<RunningSurfaceDouble>();
        }

        private UnityEngine.Object CreateReferencePlaceholder()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "CameraFollowFixtureReferencePlaceholder";
            spawnedAssets.Add(asset);
            return asset;
        }

        private static void InjectReference(
            PlayerControllerFacade facade,
            string fieldName,
            object value)
        {
            var field = typeof(PlayerControllerFacade).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "The configuration surface must retain the serialized field " + fieldName + ".");
            field.SetValue(facade, value);
        }

        private void ClearFixtures()
        {
            for (var index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                if (spawnedObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(spawnedObjects[index]);
            }

            for (var index = spawnedAssets.Count - 1; index >= 0; index--)
            {
                if (spawnedAssets[index] != null)
                    UnityEngine.Object.DestroyImmediate(spawnedAssets[index]);
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

        /// <summary>
        /// A scripted player query contract. The camera adapter's only route to the player is the
        /// public query surface, so a position and a Player_State the fixture controls exactly is
        /// enough to make the frame arithmetic deterministic.
        /// </summary>
        private sealed class ScriptedPlayerQueries : IPlayerQueries
        {
            public ScriptedPlayerQueries(Vector3 position, PlayerState state)
            {
                Position = position;
                State = state;
            }

            public Vector3 Position { get; set; }
            public PlayerState State { get; set; }

            public PlayerState CurrentState { get { return State; } }
            public bool IsGrounded { get { return State != PlayerState.Jumping; } }
            public float ForwardSpeed { get { return Defaults.ForwardSpeed; } }

            public PlayerSnapshot Snapshot
            {
                get
                {
                    return new PlayerSnapshot(
                        Position,
                        Quaternion.identity,
                        State,
                        IsGrounded,
                        Defaults.ForwardSpeed,
                        LogicalLane.Center,
                        LogicalLane.Center,
                        Position.x,
                        Position.x,
                        Position.x,
                        0f,
                        0f,
                        0f,
                        Defaults.BaselineCollider,
                        ImmutableValueSequence<LaneRequest>.Empty,
                        ImmutableValueSequence<PlayerCommandKind>.Empty,
                        false);
                }
            }
        }

        private sealed class RunningSurfaceDouble : MonoBehaviour, IRunningSurface
        {
        }

        private sealed class ReferencePlaceholderDouble : ScriptableObject
        {
        }

        /// <summary>
        /// The public surface Task 8.7 has to provide, reached by reflection so this fixture states
        /// the required contract before the adapter exists. The adapter is expected to be a
        /// <see cref="MonoBehaviour"/> that resolves a serialized concrete reference to
        /// <see cref="IPlayerQueries"/> during facade-controlled initialization, exposes one public
        /// per-frame entry point so a rendered frame can be supplied explicitly, and exposes one
        /// atomic reset hook for the reset service.
        /// </summary>
        private sealed class CameraFollowSeam
        {
            private const string AdapterTypeName = "SubwaySurfers.Player.PlayerCameraFollow";
            private const string PlayerQuerySourceFieldName = "playerQuerySource";

            private readonly MonoBehaviour adapter;
            private readonly PropertyInfo playerResolved;
            private readonly PropertyInfo cameraPosition;
            private readonly PropertyInfo cameraFollowOffset;
            private readonly PropertyInfo cameraTargetPosition;
            private readonly PropertyInfo cameraPositionError;
            private readonly PropertyInfo cameraConvergenceVelocity;
            private readonly PropertyInfo remainingCameraSettleTime;
            private readonly MethodInfo configure;
            private readonly MethodInfo executeCameraFollowUpdate;
            private readonly MethodInfo resetCameraPose;

            private CameraFollowSeam(MonoBehaviour adapter)
            {
                this.adapter = adapter;

                var type = adapter.GetType();
                playerResolved = RequiredProperty(type, "PlayerResolved", typeof(bool));
                cameraPosition = RequiredProperty(type, "CameraPosition", typeof(Vector3));
                cameraFollowOffset = RequiredProperty(type, "CameraFollowOffset", typeof(Vector3));
                cameraTargetPosition = RequiredProperty(
                    type, "CameraTargetPosition", typeof(Vector3));
                cameraPositionError = RequiredProperty(type, "CameraPositionError", typeof(float));
                cameraConvergenceVelocity = RequiredProperty(
                    type, "CameraConvergenceVelocity", typeof(Vector3));
                remainingCameraSettleTime = RequiredProperty(
                    type, "RemainingCameraSettleTime", typeof(float));
                configure = RequiredMethod(
                    type, "Configure", typeof(IPlayerQueries), typeof(PlayerConfiguration));
                executeCameraFollowUpdate = RequiredMethod(
                    type, "ExecuteCameraFollowUpdate", typeof(float));
                resetCameraPose = RequiredMethod(type, "ResetCameraPose");
            }

            public Transform Transform { get { return adapter.transform; } }
            public bool PlayerResolved { get { return (bool)playerResolved.GetValue(adapter); } }
            public Vector3 CameraPosition { get { return (Vector3)cameraPosition.GetValue(adapter); } }

            public Vector3 CameraFollowOffset
            {
                get { return (Vector3)cameraFollowOffset.GetValue(adapter); }
            }

            public Vector3 CameraTargetPosition
            {
                get { return (Vector3)cameraTargetPosition.GetValue(adapter); }
            }

            public float CameraPositionError
            {
                get { return (float)cameraPositionError.GetValue(adapter); }
            }

            public Vector3 CameraConvergenceVelocity
            {
                get { return (Vector3)cameraConvergenceVelocity.GetValue(adapter); }
            }

            public float RemainingCameraSettleTime
            {
                get { return (float)remainingCameraSettleTime.GetValue(adapter); }
            }

            public static CameraFollowSeam FromComponent(MonoBehaviour adapter)
            {
                Assert.That(adapter, Is.Not.Null,
                    "Task 8.7 must provide " + AdapterTypeName + " as a player component.");
                return new CameraFollowSeam(adapter);
            }

            public static MonoBehaviour AddTo(GameObject host)
            {
                var adapter = host.AddComponent(ResolveType()) as MonoBehaviour;
                Assert.That(adapter, Is.Not.Null,
                    "Task 8.7 must make " + AdapterTypeName + " a MonoBehaviour so LateUpdate owns " +
                    "the per-frame camera drive.");
                return adapter;
            }

            public static MonoBehaviour FindOn(GameObject root, string context)
            {
                var adapter = root.GetComponentInChildren(ResolveType(), true) as MonoBehaviour;
                Assert.That(adapter, Is.Not.Null,
                    AdapterTypeName + " must be present on " + context + ".");
                return adapter;
            }

            /// <summary>
            /// Assigns the serialized concrete reference the adapter resolves to
            /// <see cref="IPlayerQueries"/>. An interface field cannot be serialized, so the
            /// reference has to arrive as a concrete component exactly as the animation receiver does.
            /// </summary>
            public static void AssignSerializedPlayerQuerySource(
                MonoBehaviour adapter, MonoBehaviour source)
            {
                var field = adapter.GetType().GetField(
                    PlayerQuerySourceFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null,
                    "Task 8.7 must expose a serialized field named " + PlayerQuerySourceFieldName +
                    " on " + AdapterTypeName + " so a prefab can carry the facade reference.");
                Assert.That(Attribute.IsDefined(field, typeof(SerializeField)), Is.True,
                    PlayerQuerySourceFieldName + " must carry [SerializeField] so the reference " +
                    "survives serialization.");
                Assert.That(field.FieldType.IsInstanceOfType(source), Is.True,
                    PlayerQuerySourceFieldName + " must accept the concrete facade component; it is " +
                    "typed " + field.FieldType.FullName + ".");
                field.SetValue(adapter, source);
            }

            /// <summary>
            /// Camera_Follow_Offset is derived from the configured poses, so no offset may be
            /// serialized independently of them.
            /// </summary>
            public static void AssertNoIndependentlySerializedOffset()
            {
                var fields = ResolveType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var serializedOffsets = new List<string>();
                foreach (var field in fields)
                {
                    var serialized = field.IsPublic ||
                        Attribute.IsDefined(field, typeof(SerializeField));
                    if (!serialized) continue;
                    if (Attribute.IsDefined(field, typeof(NonSerializedAttribute))) continue;
                    if (field.Name.IndexOf("offset", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    serializedOffsets.Add(field.FieldType.Name + " " + field.Name);
                }

                Assert.That(serializedOffsets, Is.Empty,
                    "Camera_Follow_Offset must be derived from the configured player and camera " +
                    "poses rather than serialized on its own. Unexpected serialized offset fields: " +
                    string.Join(", ", serializedOffsets));
            }

            public void Configure(IPlayerQueries player, PlayerConfiguration configuration)
            {
                Invoke(configure, new object[] { player, configuration });
            }

            public void ExecuteCameraFollowUpdate(float elapsedCameraFollowTime)
            {
                Invoke(executeCameraFollowUpdate, new object[] { elapsedCameraFollowTime });
            }

            public void ResetCameraPose()
            {
                Invoke(resetCameraPose, Array.Empty<object>());
            }

            public void SetEnabled(bool enabled)
            {
                adapter.enabled = enabled;
            }

            /// <summary>
            /// Diagnostic rendering used in assertion messages. Reads defensively so an unresolved or
            /// partially wired adapter still produces a useful message instead of masking the
            /// assertion that is being reported.
            /// </summary>
            public string Render()
            {
                try
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "[resolved={0}, camera={1}, transform={2}, target={3}, offset={4}, " +
                        "error={5}, velocity={6}, remaining={7}]",
                        PlayerResolved,
                        CameraPosition.ToString("R", CultureInfo.InvariantCulture),
                        Transform.position.ToString("R", CultureInfo.InvariantCulture),
                        CameraTargetPosition.ToString("R", CultureInfo.InvariantCulture),
                        CameraFollowOffset.ToString("R", CultureInfo.InvariantCulture),
                        CameraPositionError.ToString("R", CultureInfo.InvariantCulture),
                        CameraConvergenceVelocity.ToString("R", CultureInfo.InvariantCulture),
                        RemainingCameraSettleTime.ToString("R", CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    return "[camera=unavailable: " + exception.GetType().Name + "]";
                }
            }

            private static Type ResolveType()
            {
                var type = typeof(CharacterControllerMotor).Assembly.GetType(AdapterTypeName);
                Assert.That(type, Is.Not.Null, "Task 8.7 must provide " + AdapterTypeName + ".");
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.True,
                    "Task 8.7 must make " + AdapterTypeName + " a MonoBehaviour so a prefab can " +
                    "carry it and LateUpdate can own the per-frame camera drive.");
                Assert.That(typeof(IPlayerConfigurationConsumer).IsAssignableFrom(type), Is.True,
                    "Task 8.7 must make " + AdapterTypeName +
                    " an IPlayerConfigurationConsumer so facade-controlled initialization supplies " +
                    "the validated configuration and resolves the serialized reference.");
                return type;
            }

            private static PropertyInfo RequiredProperty(Type type, string name, Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null,
                    "Task 8.7 must expose " + type.FullName + "." + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType),
                    name + " must be typed " + propertyType.Name + ".");
                return property;
            }

            private static MethodInfo RequiredMethod(
                Type type, string name, params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null);
                Assert.That(method, Is.Not.Null,
                    "Task 8.7 must expose " + type.FullName + "." + name + "(" +
                    RenderParameters(parameterTypes) + ").");
                Assert.That(method.ReturnType, Is.EqualTo(typeof(void)),
                    name + " must return void.");
                return method;
            }

            private static string RenderParameters(Type[] parameterTypes)
            {
                var rendered = new string[parameterTypes.Length];
                for (var index = 0; index < parameterTypes.Length; index++)
                {
                    rendered[index] = parameterTypes[index].Name;
                }

                return string.Join(", ", rendered);
            }

            private void Invoke(MethodInfo method, object[] arguments)
            {
                try
                {
                    method.Invoke(adapter, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }
        }
    }
}
