using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// Play Mode coverage for Player_Collider profile application and the Safe_Collider_Restoration
    /// query at the motor seam, where the queried volume is observable directly.
    ///
    /// These fixtures pin the facts a loop-level slide test cannot separate: the queried volume is the
    /// Baseline_Collider_Profile mapped through the player's transform - position, rotation, and scale
    /// together - and only marked Environment_Obstruction geometry blocks restoration. Geometry is
    /// placed so an axis-aligned unit-scale reading of the profile answers the opposite of the correct
    /// reading, which makes each assertion fail for the wrong implementation rather than pass by
    /// coincidence. All geometry is player-owned test doubles implementing only public contracts.
    /// </summary>
    public sealed class ColliderProfileRestorationPlayModeTests
    {
        private const float FixedStep = 0.02f;
        private const float ProfileTolerance = 0.0005f;

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;

        /// <summary>
        /// A yaw of ninety degrees maps the profile's local +X offset onto world -Z, and the
        /// non-uniform scale doubles the queried radius while halving the queried height. Every
        /// component of the transform therefore changes the answer.
        /// </summary>
        private static readonly Quaternion PlayerYaw = Quaternion.Euler(0f, 90f, 0f);
        private static readonly Vector3 PlayerScale = new Vector3(2f, 0.5f, 1f);
        private static readonly Vector3 PlayerOrigin = new Vector3(0f, 0f, 0f);
        private static readonly ColliderProfile OffsetProfile =
            new ColliderProfile(0.15f, 2f, new Vector3(0.4f, 1f, 0f));

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();

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

        // **Validates: Requirements 5.6, 5.8, 5.10, 14.11**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the restoration query follows transform rotation and scale")]
        public IEnumerator RestorationQueryFollowsRotationAndScale_Requirements_5_6_5_8_5_10_And_14_11()
        {
            var motor = CreateTransformedMotor();
            yield return new WaitForFixedUpdate();

            var worldCenter = WorldProfileCenter();
            var worldRadius = OffsetProfile.Radius * Mathf.Max(PlayerScale.x, PlayerScale.z);
            var worldHalfHeight = OffsetProfile.Height * PlayerScale.y * 0.5f;
            var unscaledHalfHeight = OffsetProfile.Height * 0.5f;

            // Inside the transformed volume: the correct query is blocked.
            var inside = CreateObstruction("InsideTransformedVolume", worldCenter, 0.2f, true, null);
            yield return new WaitForFixedUpdate();
            Assert.That(IsSafe(motor), Is.False,
                "Marked geometry inside the transform-mapped Baseline_Collider_Profile volume must " +
                "block Safe_Collider_Restoration. " + Describe(motor, inside, worldCenter));
            DestroyFixture(inside);

            // Where an axis-aligned unit-scale reading of the same profile would put the volume, and
            // where the real volume is not: the correct query stays safe.
            var untransformed = PlayerOrigin + OffsetProfile.Center;
            var aside = CreateObstruction("AtUntransformedProfileCenter", untransformed, 0.2f, true, null);
            yield return new WaitForFixedUpdate();
            Assert.That(IsSafe(motor), Is.True,
                "Marked geometry outside the transform-mapped volume must not block " +
                "Safe_Collider_Restoration, even where an untransformed profile would sit. " +
                Describe(motor, aside, worldCenter));
            DestroyFixture(aside);

            // Between the unscaled and scaled radius: only the scaled radius reaches it.
            const float radialOffset = 0.22f;
            const float radialSize = 0.04f;
            var radialGap = worldCenter + new Vector3(radialOffset, 0f, 0f);
            var radial = CreateObstruction("BetweenUnscaledAndScaledRadius", radialGap, radialSize, true, null);
            yield return new WaitForFixedUpdate();
            Assert.That(radialOffset - 0.5f * radialSize, Is.GreaterThan(OffsetProfile.Radius),
                "The fixture must sit outside the unscaled radius. " +
                Describe(motor, radial, worldCenter));
            Assert.That(radialOffset + 0.5f * radialSize, Is.LessThan(worldRadius),
                "The fixture must sit inside the scaled radius. " +
                Describe(motor, radial, worldCenter));
            Assert.That(IsSafe(motor), Is.False,
                "The queried radius must follow the transform's lateral scale. " +
                Describe(motor, radial, worldCenter));
            DestroyFixture(radial);

            // Above the scaled capsule but below the unscaled one: only an unscaled height reaches it.
            const float axialOffset = 0.75f;
            const float axialSize = 0.1f;
            var axialGap = worldCenter + new Vector3(0f, axialOffset, 0f);
            var axial = CreateObstruction("AboveScaledBelowUnscaledTop", axialGap, axialSize, true, null);
            yield return new WaitForFixedUpdate();
            Assert.That(axialOffset - 0.5f * axialSize, Is.GreaterThan(worldHalfHeight),
                "The fixture must sit above the scaled capsule. " +
                Describe(motor, axial, worldCenter));
            Assert.That(axialOffset + 0.5f * axialSize, Is.LessThan(unscaledHalfHeight),
                "The fixture must sit below an unscaled capsule of the same profile. " +
                Describe(motor, axial, worldCenter));
            Assert.That(IsSafe(motor), Is.True,
                "The queried height must follow the transform's vertical scale. " +
                Describe(motor, axial, worldCenter));
        }

        // **Validates: Requirements 5.6, 5.7, 5.8, 5.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: only marked, external, non-trigger geometry blocks restoration")]
        public IEnumerator OnlyMarkedExternalGeometryBlocksRestoration_Requirements_5_6_5_7_5_8_And_5_10()
        {
            var motor = CreateBaselineMotor();
            yield return new WaitForFixedUpdate();

            var center = motor.transform.position + Defaults.BaselineCollider.Center;

            var unmarked = CreateObstruction("UnmarkedOverlappingGeometry", center, 0.4f, false, null);
            yield return new WaitForFixedUpdate();
            Assert.That(IsBaselineSafe(motor), Is.True,
                "Geometry on the obstruction mask without an IEnvironmentObstruction marker must not " +
                "block Safe_Collider_Restoration. " + Describe(motor, unmarked, center));

            // The same geometry, now marked, must block: the marker is the deciding fact.
            unmarked.AddComponent<EnvironmentObstructionDouble>();
            Assert.That(IsBaselineSafe(motor), Is.False,
                "Marked overlapping geometry must block Safe_Collider_Restoration. " +
                Describe(motor, unmarked, center));
            DestroyFixture(unmarked);

            var trigger = CreateObstruction("MarkedTriggerGeometry", center, 0.4f, true, null);
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            yield return new WaitForFixedUpdate();
            Assert.That(IsBaselineSafe(motor), Is.True,
                "A trigger must never block Safe_Collider_Restoration. " +
                Describe(motor, trigger, center));
            DestroyFixture(trigger);

            var self = CreateObstruction("MarkedSelfGeometry", center, 0.4f, true, motor.transform);
            yield return new WaitForFixedUpdate();
            Assert.That(IsBaselineSafe(motor), Is.True,
                "A collider in the player's own hierarchy must never block " +
                "Safe_Collider_Restoration. " + Describe(motor, self, center));
        }

        // **Validates: Requirements 14.11**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the restoration query allocates nothing and mutates nothing")]
        public IEnumerator RestorationQueryIsNonAllocatingAndReadOnly_Requirements_14_11()
        {
            var motor = CreateBaselineMotor();
            var center = motor.transform.position + Defaults.BaselineCollider.Center;
            var obstruction = CreateObstruction("ReadOnlyObstruction", center, 0.4f, true, null);
            yield return new WaitForFixedUpdate();

            var collider = obstruction.GetComponent<BoxCollider>();
            var obstructionPosition = obstruction.transform.position;
            var obstructionRotation = obstruction.transform.rotation;
            var obstructionScale = obstruction.transform.localScale;
            var colliderSize = collider.size;
            var colliderCenter = collider.center;
            var colliderIsTrigger = collider.isTrigger;
            var obstructionLayer = obstruction.layer;
            var obstructionActive = obstruction.activeSelf;

            var playerPosition = motor.transform.position;
            var capsule = motor.GetComponent<CharacterController>();
            var capsuleRadius = capsule.radius;
            var capsuleHeight = capsule.height;
            var capsuleCenter = capsule.center;

            // One warm-up query pays the one-time managed cost of first execution, so the measured
            // region is the steady per-step path.
            Assert.That(IsBaselineSafe(motor), Is.False,
                "Marked overlapping geometry must block Safe_Collider_Restoration. " +
                Describe(motor, obstruction, center));

            Assert.That(
                () =>
                {
                    for (var query = 0; query < 8; query++)
                    {
                        motor.IsBaselineRestorationSafe(
                            Defaults.BaselineCollider, Defaults.ObstructionLayerMask);
                    }
                },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "Repeated Safe_Collider_Restoration queries must not allocate. " +
                Describe(motor, obstruction, center));

            for (var query = 0; query < 8; query++)
            {
                Assert.That(IsBaselineSafe(motor), Is.False,
                    "Repeated queries must keep returning the same answer. " +
                    Describe(motor, obstruction, center));
            }

            Assert.That(obstruction.transform.position, Is.EqualTo(obstructionPosition),
                "The query must not move queried geometry. " + Describe(motor, obstruction, center));
            Assert.That(obstruction.transform.rotation, Is.EqualTo(obstructionRotation),
                "The query must not rotate queried geometry. " + Describe(motor, obstruction, center));
            Assert.That(obstruction.transform.localScale, Is.EqualTo(obstructionScale),
                "The query must not rescale queried geometry. " + Describe(motor, obstruction, center));
            Assert.That(collider.size, Is.EqualTo(colliderSize),
                "The query must not resize a queried collider. " + Describe(motor, obstruction, center));
            Assert.That(collider.center, Is.EqualTo(colliderCenter),
                "The query must not re-center a queried collider. " +
                Describe(motor, obstruction, center));
            Assert.That(collider.isTrigger, Is.EqualTo(colliderIsTrigger),
                "The query must not change the trigger flag of queried geometry. " +
                Describe(motor, obstruction, center));
            Assert.That(obstruction.layer, Is.EqualTo(obstructionLayer),
                "The query must not change the layer of queried geometry. " +
                Describe(motor, obstruction, center));
            Assert.That(obstruction.activeSelf, Is.EqualTo(obstructionActive),
                "The query must not deactivate queried geometry. " +
                Describe(motor, obstruction, center));

            Assert.That(motor.transform.position, Is.EqualTo(playerPosition),
                "The query must not move the player. " + Describe(motor, obstruction, center));
            Assert.That(capsule.radius, Is.EqualTo(capsuleRadius),
                "The query must not change the applied capsule radius. " +
                Describe(motor, obstruction, center));
            Assert.That(capsule.height, Is.EqualTo(capsuleHeight),
                "The query must not change the applied capsule height. " +
                Describe(motor, obstruction, center));
            Assert.That(capsule.center, Is.EqualTo(capsuleCenter),
                "The query must not change the applied capsule center. " +
                Describe(motor, obstruction, center));
        }

        // **Validates: Requirements 5.3, 5.6**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: profile application writes radius, height, and center together")]
        public IEnumerator ProfileApplicationWritesRadiusHeightAndCenterTogether_Requirements_5_3_And_5_6()
        {
            var motor = CreateBaselineMotor();
            yield return new WaitForFixedUpdate();

            var capsule = motor.GetComponent<CharacterController>();

            motor.ApplyColliderProfile(Defaults.SlideCollider);
            AssertAppliedProfile(capsule, Defaults.SlideCollider, motor,
                "Applying Slide_Collider_Profile must write radius, height, and center together.");

            motor.ApplyColliderProfile(Defaults.BaselineCollider);
            AssertAppliedProfile(capsule, Defaults.BaselineCollider, motor,
                "Restoring Baseline_Collider_Profile must write radius, height, and center together.");

            // A profile with a larger radius than the current one is the order-sensitive direction:
            // a naive radius-first write would pass through a capsule shorter than twice its radius.
            var widened = new ColliderProfile(0.9f, 2.4f, new Vector3(0f, 1.2f, 0f));
            motor.ApplyColliderProfile(widened);
            AssertAppliedProfile(capsule, widened,
                motor, "Applying a wider profile must write radius, height, and center together.");
        }

        private static void AssertAppliedProfile(
            CharacterController capsule,
            ColliderProfile expected,
            CharacterControllerMotor motor,
            string because)
        {
            Assert.That(capsule.radius, Is.EqualTo(expected.Radius).Within(ProfileTolerance),
                because + " " + DescribeCapsule(capsule, motor));
            Assert.That(capsule.height, Is.EqualTo(expected.Height).Within(ProfileTolerance),
                because + " " + DescribeCapsule(capsule, motor));
            Assert.That((capsule.center - expected.Center).magnitude,
                Is.LessThanOrEqualTo(ProfileTolerance),
                because + " " + DescribeCapsule(capsule, motor));
            Assert.That(capsule.height, Is.GreaterThanOrEqualTo(2f * capsule.radius - ProfileTolerance),
                because + " " + DescribeCapsule(capsule, motor));
        }

        private static bool IsSafe(CharacterControllerMotor motor)
        {
            return motor.IsBaselineRestorationSafe(OffsetProfile, Defaults.ObstructionLayerMask);
        }

        private static bool IsBaselineSafe(CharacterControllerMotor motor)
        {
            return motor.IsBaselineRestorationSafe(
                Defaults.BaselineCollider, Defaults.ObstructionLayerMask);
        }

        /// <summary>
        /// The profile center mapped through the fixture transform: local scale first, then rotation,
        /// then translation. This is the center the query has to use.
        /// </summary>
        private static Vector3 WorldProfileCenter()
        {
            var scaled = new Vector3(
                OffsetProfile.Center.x * PlayerScale.x,
                OffsetProfile.Center.y * PlayerScale.y,
                OffsetProfile.Center.z * PlayerScale.z);
            return PlayerOrigin + PlayerYaw * scaled;
        }

        private static string Describe(
            CharacterControllerMotor motor,
            GameObject geometry,
            Vector3 queriedCenter)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[geometry={0}, geometryPosition={1}, playerPosition={2}, playerRotation={3}, " +
                "playerScale={4}, queriedCenter={5}]",
                geometry == null ? "none" : geometry.name,
                geometry == null
                    ? "none"
                    : geometry.transform.position.ToString("R", CultureInfo.InvariantCulture),
                motor.transform.position.ToString("R", CultureInfo.InvariantCulture),
                motor.transform.rotation.eulerAngles.ToString("R", CultureInfo.InvariantCulture),
                motor.transform.localScale.ToString("R", CultureInfo.InvariantCulture),
                queriedCenter.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string DescribeCapsule(
            CharacterController capsule,
            CharacterControllerMotor motor)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[capsule=(r={0}, h={1}, c={2}), playerPosition={3}]",
                capsule.radius.ToString("R", CultureInfo.InvariantCulture),
                capsule.height.ToString("R", CultureInfo.InvariantCulture),
                capsule.center.ToString("R", CultureInfo.InvariantCulture),
                motor.transform.position.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A motor on a rotated, non-uniformly scaled transform. The capsule keeps the queried profile
        /// so the fixture matches the volume under test.
        /// </summary>
        private CharacterControllerMotor CreateTransformedMotor()
        {
            var motor = CreateMotor("TransformedRestorationFixture", OffsetProfile);
            motor.transform.rotation = PlayerYaw;
            motor.transform.localScale = PlayerScale;
            return motor;
        }

        private CharacterControllerMotor CreateBaselineMotor()
        {
            return CreateMotor("BaselineRestorationFixture", Defaults.BaselineCollider);
        }

        private CharacterControllerMotor CreateMotor(string name, ColliderProfile profile)
        {
            var player = new GameObject(name);
            spawnedObjects.Add(player);
            player.SetActive(false);
            player.transform.position = PlayerOrigin;

            var controller = player.AddComponent<CharacterController>();
            controller.radius = profile.Radius;
            controller.height = profile.Height;
            controller.center = profile.Center;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.1f;

            var motor = player.AddComponent<CharacterControllerMotor>();
            player.SetActive(true);
            return motor;
        }

        /// <summary>
        /// A cube of <paramref name="size"/> on the obstruction mask at <paramref name="worldCenter"/>.
        /// A <paramref name="parent"/> makes it a collider in the player's own hierarchy.
        /// </summary>
        private GameObject CreateObstruction(
            string name,
            Vector3 worldCenter,
            float size,
            bool marked,
            Transform parent)
        {
            var body = new GameObject(name);
            body.layer = FirstLayerOf(Defaults.ObstructionLayerMask);
            if (parent == null)
            {
                spawnedObjects.Add(body);
            }
            else
            {
                body.transform.SetParent(parent, true);
            }

            body.transform.position = worldCenter;
            body.transform.localScale = Vector3.one;

            var collider = body.AddComponent<BoxCollider>();
            collider.size = new Vector3(size, size, size);
            if (marked) body.AddComponent<EnvironmentObstructionDouble>();
            return body;
        }

        private void DestroyFixture(GameObject fixture)
        {
            spawnedObjects.Remove(fixture);
            if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
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

        private static int FirstLayerOf(int mask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private sealed class EnvironmentObstructionDouble : MonoBehaviour, IEnvironmentObstruction
        {
        }
    }
}
