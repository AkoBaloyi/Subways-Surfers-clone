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
    /// Play Mode coverage for Grounded_Query against geometry the player capsule already penetrates.
    /// A downward capsule sweep that starts inside a collider carries no usable normal, so these
    /// fixtures pin the rule that penetrating geometry grounds the player only when its real support
    /// normal meets Ground_Normal_Threshold. Every fixture is a player-owned test double built from
    /// the public Player contracts, and probing must leave those doubles untouched.
    /// </summary>
    public sealed class GroundProbePenetrationPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float VelocityTolerance = 0.001f;

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
            Time.maximumDeltaTime = FixedStep;
        }

        [TearDown]
        public void TearDown()
        {
            ClearFixtures();
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 4.1, 4.8, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: penetrated flat support geometry grounds the player")]
        public IEnumerator PenetratedFlatSurfaceGroundsPlayer_Requirements_4_1_4_8_And_14_10()
        {
            var surface = CreateFlatSurface("PenetratedFlatSurfaceDouble", true);
            var player = CreatePlayer();

            // Deterministic single-stepping keeps the capsule inside the surface for the first probe.
            player.enabled = false;
            yield return null;

            Assert.That(SampleGrounded(player), Is.True,
                "Marked non-trigger geometry the capsule penetrates, whose real support normal points " +
                "up, must satisfy Valid_Ground_Contact. " + Describe(player, surface, 0));

            player.ExecuteMovementUpdate(0f);
            Assert.That(player.PreMovementSnapshot.IsGrounded, Is.True,
                "The Movement_Update grounded sample must agree with the probe while penetrating. " +
                Describe(player, surface, 0));
            Assert.That(player.IsGrounded, Is.True,
                "Grounded_Status_Query must report contact with penetrated flat support geometry. " +
                Describe(player, surface, 0));

            Assert.That(player.RequestJump().Status, Is.EqualTo(CommandStatus.Accepted),
                "A grounded Running Jump_Request must be accepted from penetrated support geometry. " +
                Describe(player, surface, 0));

            var landed = LandByManualStepping(player);
            Assert.That(landed, Is.True,
                "The jump must land back on the penetrated surface within the ballistic budget. " +
                Describe(player, surface, 0));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Running),
                "A valid landing must transition Jumping to Running. " + Describe(player, surface, 0));
            Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(0f).Within(VelocityTolerance),
                "A valid landing must clear vertical velocity. " + Describe(player, surface, 0));
        }

        // **Validates: Requirements 4.1, 4.2, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: penetrated steep geometry never grounds the player")]
        public IEnumerator PenetratedSteepSurfaceNeverGrounds_Requirements_4_1_4_2_And_14_10()
        {
            // Sixty degrees puts the support normal at 0.5, below Ground_Normal_Threshold, so only a
            // real resolved normal can reject it; an assumed upward normal would ground the player.
            var surface = CreateSteepSurface("PenetratedSteepSurfaceDouble", 60f);
            var player = CreatePlayer();

            player.enabled = false;
            yield return null;

            Assert.That(SampleGrounded(player), Is.False,
                "Marked geometry the capsule penetrates whose real support normal is below " +
                "Ground_Normal_Threshold must not satisfy Valid_Ground_Contact. " +
                Describe(player, surface, 0));

            player.ExecuteMovementUpdate(0f);
            Assert.That(player.PreMovementSnapshot.IsGrounded, Is.False,
                "The Movement_Update grounded sample must agree with the probe while penetrating. " +
                Describe(player, surface, 0));
            Assert.That(player.IsGrounded, Is.False,
                "Grounded_Status_Query must expose Grounded as false without Valid_Ground_Contact. " +
                Describe(player, surface, 0));

            var jump = player.RequestJump();
            Assert.That(jump.Status, Is.EqualTo(CommandStatus.Rejected),
                "A Jump_Request without Valid_Ground_Contact must be rejected. " +
                Describe(player, surface, 0));
            Assert.That(jump.Reason, Is.EqualTo(RejectionReason.NotGrounded),
                "A Jump_Request without Valid_Ground_Contact must be rejected as NotGrounded. " +
                Describe(player, surface, 0));
            Assert.That(player.Snapshot.VerticalVelocity, Is.EqualTo(0f).Within(VelocityTolerance),
                "A rejected Jump_Request must not apply Jump_Velocity. " + Describe(player, surface, 0));
        }

        // **Validates: Requirements 4.1, 4.2, 14.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: penetrated unmarked geometry never grounds the player")]
        public IEnumerator PenetratedUnmarkedGeometryNeverGrounds_Requirements_4_1_4_2_And_14_10()
        {
            var geometry = CreateFlatSurface("PenetratedUnmarkedGeometry", false);
            var player = CreatePlayer();

            player.enabled = false;
            yield return null;

            Assert.That(SampleGrounded(player), Is.False,
                "Geometry without an IRunningSurface marker must not ground the player, even when " +
                "the capsule penetrates it. " + Describe(player, geometry, 0));

            player.ExecuteMovementUpdate(0f);
            Assert.That(player.IsGrounded, Is.False,
                "Grounded_Status_Query must expose Grounded as false without Valid_Ground_Contact. " +
                Describe(player, geometry, 0));
            Assert.That(player.RequestJump().Reason, Is.EqualTo(RejectionReason.NotGrounded),
                "A Jump_Request without Valid_Ground_Contact must be rejected as NotGrounded. " +
                Describe(player, geometry, 0));
        }

        // **Validates: Requirements 14.11**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: probing penetrated geometry never mutates it")]
        public IEnumerator ProbingPenetratedGeometryNeverMutatesIt_Requirements_14_11()
        {
            var surface = CreateFlatSurface("ReadOnlyPenetratedSurfaceDouble", true);
            var collider = surface.GetComponent<BoxCollider>();
            var position = surface.transform.position;
            var rotation = surface.transform.rotation;
            var scale = surface.transform.localScale;
            var size = collider.size;
            var center = collider.center;
            var isTrigger = collider.isTrigger;
            var layer = surface.layer;
            var isActive = surface.activeSelf;

            var player = CreatePlayer();
            player.enabled = false;
            yield return null;

            for (var step = 0; step < 5; step++)
            {
                Assert.That(SampleGrounded(player), Is.True,
                    "Repeated probing must keep reporting the same Valid_Ground_Contact. " +
                    Describe(player, surface, step));
                player.ExecuteMovementUpdate(FixedStep);
            }

            Assert.That(surface.transform.position, Is.EqualTo(position),
                "Grounded_Query must not move probed geometry. " + Describe(player, surface, 5));
            Assert.That(surface.transform.rotation, Is.EqualTo(rotation),
                "Grounded_Query must not rotate probed geometry. " + Describe(player, surface, 5));
            Assert.That(surface.transform.localScale, Is.EqualTo(scale),
                "Grounded_Query must not rescale probed geometry. " + Describe(player, surface, 5));
            Assert.That(collider.size, Is.EqualTo(size),
                "Grounded_Query must not resize a probed collider. " + Describe(player, surface, 5));
            Assert.That(collider.center, Is.EqualTo(center),
                "Grounded_Query must not re-center a probed collider. " + Describe(player, surface, 5));
            Assert.That(collider.isTrigger, Is.EqualTo(isTrigger),
                "Grounded_Query must not change the trigger flag of probed geometry. " +
                Describe(player, surface, 5));
            Assert.That(surface.layer, Is.EqualTo(layer),
                "Grounded_Query must not change the layer of probed geometry. " +
                Describe(player, surface, 5));
            Assert.That(surface.activeSelf, Is.EqualTo(isActive),
                "Grounded_Query must not deactivate probed geometry. " + Describe(player, surface, 5));
        }

        private static bool SampleGrounded(PlayerControllerFacade player)
        {
            var motor = player.GetComponent<CharacterControllerMotor>();
            Assert.That(motor, Is.Not.Null,
                "The facade must resolve a CharacterController motor surface for probing.");
            return motor.SampleGrounded(
                Defaults.BaselineCollider,
                Defaults.GroundLayerMask,
                Defaults.GroundContactTolerance,
                Defaults.GroundNormalThreshold);
        }

        private static bool LandByManualStepping(PlayerControllerFacade player)
        {
            var budget = Mathf.CeilToInt(
                4f * Defaults.JumpVelocity / (Defaults.GravityAcceleration * FixedStep)) + 20;
            for (var step = 0; step < budget; step++)
            {
                if (player.CurrentState == PlayerState.Running && player.IsGrounded) return true;
                player.ExecuteMovementUpdate(FixedStep);
            }

            return player.CurrentState == PlayerState.Running && player.IsGrounded;
        }

        private static string Describe(PlayerControllerFacade player, GameObject geometry, int step)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[geometry={0}, step={1}, state={2}, grounded={3}, verticalVelocity={4}, position={5}]",
                geometry == null ? "none" : geometry.name,
                step,
                player.CurrentState,
                player.IsGrounded,
                player.Snapshot.VerticalVelocity.ToString("R", CultureInfo.InvariantCulture),
                player.transform.position.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Axis-aligned geometry whose top face sits above the capsule bottom, so the capsule starts
        /// inside it and the downward sweep reports an overlap instead of a surface normal.
        /// </summary>
        private GameObject CreateFlatSurface(string name, bool marked)
        {
            var body = new GameObject(name);
            spawnedObjects.Add(body);
            body.layer = GroundLayer();

            const float halfThickness = 0.5f;
            var topY = PlayerSpawn.y + 0.38f;
            body.transform.position = new Vector3(0f, topY - halfThickness, 40f);

            var collider = body.AddComponent<BoxCollider>();
            collider.size = new Vector3(40f, 2f * halfThickness, 200f);
            if (marked) body.AddComponent<RunningSurfaceDouble>();
            return body;
        }

        /// <summary>
        /// Marked geometry tilted by <paramref name="tiltDegrees"/> and placed so the capsule's lower
        /// sphere overlaps its tilted face while the sphere center stays outside that face. The real
        /// support normal is therefore the tilted normal, not an upward one.
        /// </summary>
        private GameObject CreateSteepSurface(string name, float tiltDegrees)
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

            const float halfThickness = 0.5f;
            var penetrationDepth = radius * 0.5f;
            slope.transform.position =
                lowerSphereCenter - normal * (halfThickness + penetrationDepth);

            var collider = slope.AddComponent<BoxCollider>();
            collider.size = new Vector3(20f, 2f * halfThickness, 200f);
            slope.AddComponent<RunningSurfaceDouble>();
            return slope;
        }

        private PlayerControllerFacade CreatePlayer()
        {
            var player = new GameObject("GroundProbeFixture");
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
                "The fixture configuration must enable movement simulation.");
            return facade;
        }

        private UnityEngine.Object CreateReferencePlaceholder()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "GroundProbeFixtureReferencePlaceholder";
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

        private sealed class RunningSurfaceDouble : MonoBehaviour, IRunningSurface
        {
        }

        private sealed class ReferencePlaceholderDouble : ScriptableObject
        {
        }
    }
}
