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

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode orchestration coverage for the combined CharacterController movement loop.
    /// Movement behaviour is observed through the public facade surface required by Task 7.2;
    /// the surface is resolved with a seam so this file stays independent of internal wiring.
    /// </summary>
    public sealed class MotorOrchestrationPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float LinearTolerance = 0.002f;
        private const int SettleSteps = 3;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;

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
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 2.1, 12.4**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: exactly one combined Move call per fixed step")]
        public IEnumerator OneCombinedMoveCallPerFixedStep_Requirements_2_1_And_12_4()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            var previousUpdates = seam.MovementUpdateCount;
            var previousMoves = seam.MoveInvocationCount;
            for (var step = 0; step < 8; step++)
            {
                yield return new WaitForFixedUpdate();

                var updates = seam.MovementUpdateCount;
                var moves = seam.MoveInvocationCount;
                Assert.That(updates - previousUpdates, Is.EqualTo(1),
                    "Each fixed step must perform exactly one Movement_Update. " + Describe(seam, step));
                Assert.That(moves - previousMoves, Is.EqualTo(1),
                    "Each Movement_Update must submit exactly one combined CharacterController.Move call. " +
                    Describe(seam, step));
                previousUpdates = updates;
                previousMoves = moves;
            }
        }

        // **Validates: Requirements 2.1, 4.9, 5.13, 12.4**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: automatic positive-Z motion in every active state")]
        public IEnumerator AutomaticForwardMotionInActiveStates_Requirements_2_1_4_9_5_13_And_12_4()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            yield return AssertForwardMotion(seam, PlayerState.Running);

            Assert.That(seam.Queries.IsGrounded, Is.True,
                "The fixture must establish valid ground contact before a jump. " + Describe(seam, 0));
            Assert.That(seam.Commands.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running jump must be accepted. " + Describe(seam, 0));
            yield return AssertForwardMotion(seam, PlayerState.Jumping);

            yield return LandFromJump(seam);
            Assert.That(seam.Commands.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running slide must be accepted. " + Describe(seam, 0));
            yield return AssertForwardMotion(seam, PlayerState.Sliding);
        }

        // **Validates: Requirements 2.2, 12.4**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: zero automatic motion in Failed and Resetting")]
        public IEnumerator ZeroAutomaticMotionInFailedAndResetting_Requirements_2_2_And_12_4()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            Assert.That(seam.Commands.RequestFailure().Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a failure command. " + Describe(seam, 0));
            Assert.That(seam.Queries.CurrentState, Is.EqualTo(PlayerState.Failed),
                "An accepted failure command must produce Failed. " + Describe(seam, 0));

            var failedPosition = seam.Transform.position;
            for (var step = 0; step < 5; step++)
            {
                yield return new WaitForFixedUpdate();
                AssertZeroDisplacement(seam, step, "Failed");
                Assert.That(Distance(seam.Transform.position, failedPosition),
                    Is.LessThanOrEqualTo(LinearTolerance),
                    "Failed must not move the player. " + Describe(seam, step));
            }

            seam.SuspendAutomaticStepping();
            var resetResult = seam.Commands.RequestReset("motor-orchestration-reset-1");
            Assert.That(resetResult.Status, Is.EqualTo(CommandStatus.Accepted),
                "A Failed player must accept a first reset request. " + Describe(seam, 0));

            seam.ExecuteMovementUpdate(FixedStep);
            if (seam.PreMovementSnapshot.State == PlayerState.Resetting)
            {
                AssertZeroDisplacement(seam, 0, "Resetting");
            }
            else
            {
                // Restoration completed before the next Movement_Update, so no Resetting movement
                // update exists to displace the player.
                Assert.That(seam.Queries.CurrentState, Is.EqualTo(PlayerState.Running),
                    "A completed reset must expose Running. " + Describe(seam, 0));
                Assert.That(seam.PreMovementSnapshot.State, Is.Not.EqualTo(PlayerState.Resetting),
                    "Resetting must never reach movement integration with non-zero displacement. " +
                    Describe(seam, 0));
            }
        }

        // **Validates: Requirements 3.6, 3.7, 3.8, 4.9, 12.4, 12.5**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: simultaneous lane and vertical motion in one call")]
        public IEnumerator SimultaneousLaneAndVerticalMotionInOneCall_Requirements_3_6_3_7_3_8_4_9_12_4_And_12_5()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            Assert.That(seam.Commands.RequestLane(LaneDirection.Right).Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a lane request. " + Describe(seam, 0));
            Assert.That(seam.Commands.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running jump must be accepted. " + Describe(seam, 0));

            var movesBefore = seam.MoveInvocationCount;
            yield return new WaitForFixedUpdate();

            Assert.That(seam.MoveInvocationCount - movesBefore, Is.EqualTo(1),
                "Lane, forward, and vertical displacement must share one Move call. " + Describe(seam, 0));
            var requested = seam.LastRequestedDisplacement;
            Assert.That(requested.x, Is.GreaterThan(0f),
                "A rightward lane change must request positive lateral displacement. " + Describe(seam, 0));
            Assert.That(requested.y, Is.GreaterThan(0f),
                "Jump takeoff must request positive vertical displacement. " + Describe(seam, 0));
            Assert.That(requested.z, Is.GreaterThan(0f),
                "Automatic running must request positive forward displacement. " + Describe(seam, 0));

            var targetCenter = Defaults.LaneCenters.z;
            var laneSteps = Mathf.CeilToInt(Defaults.LaneChangeDuration / FixedStep) + 2;
            for (var step = 0; step < laneSteps; step++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(seam.Transform.position.x,
                    Is.LessThanOrEqualTo(targetCenter + LinearTolerance),
                    "Lane interpolation must not cross the target lane center. " + Describe(seam, step));
            }

            Assert.That(Math.Abs(seam.Transform.position.x - targetCenter),
                Is.LessThanOrEqualTo(Defaults.LanePositionTolerance),
                "The lane change must arrive within Lane_Position_Tolerance by Lane_Change_Duration. " +
                Describe(seam, laneSteps));
        }

        // **Validates: Requirements 2.1, 5.13, 12.4, 12.5**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: zero elapsed time produces no movement or timer progress")]
        public IEnumerator ZeroElapsedTimeProducesNoMovementOrTimerProgress_Requirements_2_1_5_13_12_4_And_12_5()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            Assert.That(seam.Commands.RequestLane(LaneDirection.Left).Status, Is.EqualTo(CommandStatus.Accepted),
                "An active player must accept a lane request. " + Describe(seam, 0));
            Assert.That(seam.Commands.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running slide must be accepted. " + Describe(seam, 0));
            yield return Step(seam, 2);

            seam.SuspendAutomaticStepping();
            var before = seam.Queries.Snapshot;
            var beforePosition = seam.Transform.position;

            seam.ExecuteMovementUpdate(0f);

            Assert.That(seam.LastRequestedDisplacement.magnitude, Is.LessThanOrEqualTo(LinearTolerance),
                "Zero elapsed time must request zero displacement. " + Describe(seam, 0));
            Assert.That(seam.LastRealizedDisplacement.magnitude, Is.LessThanOrEqualTo(LinearTolerance),
                "Zero elapsed time must realize zero displacement. " + Describe(seam, 0));
            Assert.That(Distance(seam.Transform.position, beforePosition),
                Is.LessThanOrEqualTo(LinearTolerance),
                "Zero elapsed time must not move the transform. " + Describe(seam, 0));

            var after = seam.Queries.Snapshot;
            Assert.That(after.State, Is.EqualTo(before.State),
                "Zero elapsed time must preserve Player_State. " + Describe(seam, 0));
            Assert.That(after.SlideElapsedTime, Is.EqualTo(before.SlideElapsedTime),
                "Zero elapsed time must not advance Slide_Elapsed_Time. " + Describe(seam, 0));
            Assert.That(after.LaneChangeProgress, Is.EqualTo(before.LaneChangeProgress),
                "Zero elapsed time must not advance lane-change progress. " + Describe(seam, 0));
            Assert.That(after.LateralPosition, Is.EqualTo(before.LateralPosition),
                "Zero elapsed time must not advance lateral position. " + Describe(seam, 0));
            Assert.That(after.VerticalVelocity, Is.EqualTo(before.VerticalVelocity),
                "Zero elapsed time must not integrate vertical velocity. " + Describe(seam, 0));
        }

        // **Validates: Requirements 2.1, 12.4**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: collision resolution constrains realized displacement")]
        public IEnumerator CollisionConstrainedRealizedDisplacement_Requirements_2_1_And_12_4()
        {
            var seam = CreatePlayer();
            var wallFace = 3f;
            CreateBlockingWall(wallFace);
            yield return Step(seam, SettleSteps);

            var expectedForward = seam.Queries.ForwardSpeed * FixedStep;
            var blockedStepObserved = false;
            for (var step = 0; step < 40; step++)
            {
                yield return new WaitForFixedUpdate();

                var requested = seam.LastRequestedDisplacement;
                var realized = seam.LastRealizedDisplacement;
                Assert.That(requested.z, Is.EqualTo(expectedForward).Within(LinearTolerance),
                    "Requested forward displacement must remain Forward_Run_Speed * Elapsed_Simulation_Time. " +
                    Describe(seam, step));
                Assert.That(realized.z, Is.LessThanOrEqualTo(requested.z + LinearTolerance),
                    "Realized displacement cannot exceed requested displacement. " + Describe(seam, step));
                if (realized.z < requested.z - LinearTolerance) blockedStepObserved = true;
            }

            Assert.That(blockedStepObserved, Is.True,
                "Environment collision must shorten realized displacement at the wall. " + Describe(seam, 40));
            Assert.That(seam.Transform.position.z, Is.LessThan(wallFace),
                "Collision resolution must stop the player before the wall. " + Describe(seam, 40));
            Assert.That(seam.Transform.position.z, Is.GreaterThan(1f),
                "The player must travel toward the wall before being blocked. " + Describe(seam, 40));
        }

        // **Validates: Requirements 4.5, 4.9, 5.13, 12.4**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: landing and slide restoration resolve before movement")]
        public IEnumerator StateResolutionPrecedesMovement_Requirements_4_5_4_9_5_13_And_12_4()
        {
            var seam = CreatePlayer();
            yield return Step(seam, SettleSteps);

            Assert.That(seam.Commands.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running jump must be accepted. " + Describe(seam, 0));

            var jumpBudget = Mathf.CeilToInt(4f * Defaults.JumpVelocity / (Defaults.GravityAcceleration * FixedStep)) + 20;
            var landingResolvedBeforeMovement = false;
            var observedJumping = false;
            for (var step = 0; step < jumpBudget && !landingResolvedBeforeMovement; step++)
            {
                var stateBefore = seam.Queries.CurrentState;
                yield return new WaitForFixedUpdate();

                if (stateBefore == PlayerState.Jumping) observedJumping = true;
                if (stateBefore == PlayerState.Jumping && seam.PreMovementSnapshot.State == PlayerState.Running)
                {
                    landingResolvedBeforeMovement = true;
                    Assert.That(seam.LastRequestedDisplacement.y, Is.LessThanOrEqualTo(LinearTolerance),
                        "A resolved landing must not request upward displacement. " + Describe(seam, step));
                    Assert.That(seam.LastRequestedDisplacement.z, Is.GreaterThan(0f),
                        "Landing must not interrupt automatic forward movement. " + Describe(seam, step));
                }
            }

            Assert.That(observedJumping, Is.True,
                "The fixture must observe at least one Jumping movement update. " + Describe(seam, 0));
            Assert.That(landingResolvedBeforeMovement, Is.True,
                "Landing must be resolved before the movement of the landing update. " + Describe(seam, 0));
            Assert.That(seam.Queries.CurrentState, Is.EqualTo(PlayerState.Running),
                "A valid landing must expose Running. " + Describe(seam, 0));

            Assert.That(seam.Commands.RequestSlide().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running slide must be accepted. " + Describe(seam, 0));

            var slideBudget = Mathf.CeilToInt(Defaults.SlideDuration / FixedStep) + 10;
            var restorationResolvedBeforeMovement = false;
            for (var step = 0; step < slideBudget && !restorationResolvedBeforeMovement; step++)
            {
                var stateBefore = seam.Queries.CurrentState;
                yield return new WaitForFixedUpdate();

                if (stateBefore != PlayerState.Sliding) continue;
                if (seam.PreMovementSnapshot.State != PlayerState.Running) continue;

                restorationResolvedBeforeMovement = true;
                Assert.That(seam.PreMovementSnapshot.ColliderProfile, Is.EqualTo(Defaults.BaselineCollider),
                    "Baseline restoration must be applied before the movement of the restoring update. " +
                    Describe(seam, step));
                Assert.That(seam.LastRequestedDisplacement.z, Is.GreaterThan(0f),
                    "Slide restoration must not interrupt automatic forward movement. " + Describe(seam, step));
            }

            Assert.That(restorationResolvedBeforeMovement, Is.True,
                "Slide restoration must be resolved before the movement of the restoring update. " +
                Describe(seam, 0));
        }

        private IEnumerator AssertForwardMotion(PlayerMovementSeam seam, PlayerState expectedState)
        {
            Assert.That(seam.Queries.CurrentState, Is.EqualTo(expectedState),
                "The fixture must occupy " + expectedState + " before measuring forward motion. " +
                Describe(seam, 0));

            var expectedForward = seam.Queries.ForwardSpeed * FixedStep;
            for (var step = 0; step < 3; step++)
            {
                var beforeZ = seam.Transform.position.z;
                yield return new WaitForFixedUpdate();

                Assert.That(seam.LastRequestedDisplacement.z, Is.EqualTo(expectedForward).Within(LinearTolerance),
                    "Active states must request Forward_Axis * Forward_Run_Speed * Elapsed_Simulation_Time. " +
                    Describe(seam, step));
                Assert.That(seam.Transform.position.z, Is.GreaterThan(beforeZ),
                    "Active states must move along positive Z. " + Describe(seam, step));
            }
        }

        private IEnumerator LandFromJump(PlayerMovementSeam seam)
        {
            var budget = Mathf.CeilToInt(4f * Defaults.JumpVelocity / (Defaults.GravityAcceleration * FixedStep)) + 20;
            for (var step = 0; step < budget; step++)
            {
                if (seam.Queries.CurrentState == PlayerState.Running) yield break;
                yield return new WaitForFixedUpdate();
            }

            Assert.That(seam.Queries.CurrentState, Is.EqualTo(PlayerState.Running),
                "The jump must land within the ballistic step budget. " + Describe(seam, budget));
        }

        private static void AssertZeroDisplacement(PlayerMovementSeam seam, int step, string state)
        {
            Assert.That(seam.LastRequestedDisplacement.magnitude, Is.LessThanOrEqualTo(LinearTolerance),
                state + " must request zero automatic displacement. " + Describe(seam, step));
            Assert.That(seam.LastRealizedDisplacement.magnitude, Is.LessThanOrEqualTo(LinearTolerance),
                state + " must realize zero automatic displacement. " + Describe(seam, step));
        }

        private IEnumerator Step(PlayerMovementSeam seam, int steps)
        {
            for (var index = 0; index < steps; index++) yield return new WaitForFixedUpdate();
            Assert.That(seam.MovementUpdateCount, Is.GreaterThanOrEqualTo(steps),
                "Each fixed step must run one Movement_Update. " + Describe(seam, steps));
        }

        private static float Distance(Vector3 left, Vector3 right)
        {
            return (left - right).magnitude;
        }

        private static string Describe(PlayerMovementSeam seam, int step)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[step={0}, state={1}, grounded={2}, position={3}, requested={4}, realized={5}, " +
                "updates={6}, moves={7}]",
                step,
                seam.Queries.CurrentState,
                seam.Queries.IsGrounded,
                seam.Transform.position.ToString("R", CultureInfo.InvariantCulture),
                seam.LastRequestedDisplacement.ToString("R", CultureInfo.InvariantCulture),
                seam.LastRealizedDisplacement.ToString("R", CultureInfo.InvariantCulture),
                seam.MovementUpdateCount,
                seam.MoveInvocationCount);
        }

        private PlayerMovementSeam CreatePlayer()
        {
            CreateRunningSurface();

            var player = new GameObject("PlayerControllerFixture");
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = new Vector3(Defaults.LaneCenters.y, 0.02f, 0f);

            var controller = player.AddComponent<CharacterController>();
            controller.radius = Defaults.BaselineCollider.Radius;
            controller.height = Defaults.BaselineCollider.Height;
            controller.center = Defaults.BaselineCollider.Center;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.1f;

            var facade = player.AddComponent<PlayerControllerFacade>();
            var seam = PlayerMovementSeam.Attach(facade, CreateReferenceAsset());
            player.SetActive(true);

            Assert.That(facade.SimulationEnabled, Is.True,
                "The fixture configuration must enable movement simulation. Diagnostics: " +
                RenderDiagnostics(facade));
            return seam;
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

        private void CreateBlockingWall(float frontFaceZ)
        {
            var wall = new GameObject("EnvironmentObstructionDouble");
            spawnedObjects.Add(wall);
            wall.layer = FirstLayerOf(Defaults.ObstructionLayerMask);
            wall.transform.position = new Vector3(0f, 2f, frontFaceZ + 1f);
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(20f, 4f, 2f);
            wall.AddComponent<EnvironmentObstructionDouble>();
        }

        private UnityEngine.Object CreateReferenceAsset()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "PlayerFixtureReferencePlaceholder";
            spawnedAssets.Add(asset);
            return asset;
        }

        private static int FirstLayerOf(int mask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) != 0) return layer;
            }

            return 0;
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

        /// <summary>
        /// Reflection seam over the movement surface Task 7.2 must add to
        /// <see cref="PlayerControllerFacade"/>. The seam fails with an explicit message while the
        /// movement loop is absent, and needs no changes once the loop exists.
        /// </summary>
        private sealed class PlayerMovementSeam
        {
            private const string MotorTypeName = "SubwaySurfers.Player.CharacterControllerMotor";

            private readonly PlayerControllerFacade facade;
            private readonly PropertyInfo movementUpdateCount;
            private readonly PropertyInfo moveInvocationCount;
            private readonly PropertyInfo lastRequestedDisplacement;
            private readonly PropertyInfo lastRealizedDisplacement;
            private readonly PropertyInfo preMovementSnapshot;
            private readonly MethodInfo executeMovementUpdate;

            private PlayerMovementSeam(PlayerControllerFacade facade)
            {
                this.facade = facade;
                var type = facade.GetType();

                // The facade is sealed, so the contract implementations are resolved through object
                // to keep this seam compiling before Task 7.2 adds them.
                var candidate = (object)facade;
                Commands = candidate as IPlayerCommands;
                Queries = candidate as IPlayerQueries;
                Assert.That(Commands, Is.Not.Null,
                    "Task 7.2 must make " + type.FullName + " implement IPlayerCommands.");
                Assert.That(Queries, Is.Not.Null,
                    "Task 7.2 must make " + type.FullName + " implement IPlayerQueries.");

                movementUpdateCount = RequiredProperty(type, "MovementUpdateCount", typeof(int));
                moveInvocationCount = RequiredProperty(type, "MoveInvocationCount", typeof(int));
                lastRequestedDisplacement = RequiredProperty(type, "LastRequestedDisplacement", typeof(Vector3));
                lastRealizedDisplacement = RequiredProperty(type, "LastRealizedDisplacement", typeof(Vector3));
                preMovementSnapshot = RequiredProperty(type, "PreMovementSnapshot", typeof(PlayerSnapshot));
                executeMovementUpdate = RequiredMethod(type, "ExecuteMovementUpdate", typeof(float));
                Assert.That(executeMovementUpdate.ReturnType, Is.EqualTo(typeof(void)),
                    "ExecuteMovementUpdate must return void.");
            }

            public IPlayerCommands Commands { get; }
            public IPlayerQueries Queries { get; }
            public Transform Transform { get { return facade.transform; } }
            public int MovementUpdateCount { get { return (int)movementUpdateCount.GetValue(facade); } }
            public int MoveInvocationCount { get { return (int)moveInvocationCount.GetValue(facade); } }

            public Vector3 LastRequestedDisplacement
            {
                get { return (Vector3)lastRequestedDisplacement.GetValue(facade); }
            }

            public Vector3 LastRealizedDisplacement
            {
                get { return (Vector3)lastRealizedDisplacement.GetValue(facade); }
            }

            public PlayerSnapshot PreMovementSnapshot
            {
                get { return (PlayerSnapshot)preMovementSnapshot.GetValue(facade); }
            }

            public static PlayerMovementSeam Attach(
                PlayerControllerFacade facade,
                UnityEngine.Object referencePlaceholder)
            {
                AttachMotor(facade.gameObject);
                InjectReference(facade, "inputActionAsset", referencePlaceholder);
                InjectReference(facade, "fallbackInputActionAsset", referencePlaceholder);
                InjectReference(facade, "cameraTarget", facade.transform);
                InjectReference(facade, "fallbackCameraTarget", facade.transform);
                return new PlayerMovementSeam(facade);
            }

            public void ExecuteMovementUpdate(float elapsedSimulationTime)
            {
                executeMovementUpdate.Invoke(facade, new object[] { elapsedSimulationTime });
            }

            public void SuspendAutomaticStepping()
            {
                facade.enabled = false;
            }

            private static void AttachMotor(GameObject player)
            {
                var motorType = typeof(PlayerControllerFacade).Assembly.GetType(MotorTypeName);
                if (motorType == null || !typeof(Component).IsAssignableFrom(motorType)) return;
                if (player.GetComponent(motorType) == null) player.AddComponent(motorType);
            }

            private static void InjectReference(
                PlayerControllerFacade facade,
                string fieldName,
                UnityEngine.Object value)
            {
                var field = typeof(PlayerControllerFacade).GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null,
                    "The configuration surface must retain the serialized field " + fieldName + ".");
                field.SetValue(facade, value);
            }

            private static PropertyInfo RequiredProperty(Type type, string name, Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null,
                    "Task 7.2 must expose " + type.FullName + "." + name + ".");
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
                    "Task 7.2 must expose " + type.FullName + "." + name + ".");
                return method;
            }
        }
    }
}
