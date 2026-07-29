using System;
using System.Linq;
using NUnit.Framework;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ConfigurationTests
    {
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        public void InvalidSignedAndFiniteDomainsUseDocumentedFallback_Requirements_2_7_15_1_15_3_15_4(float invalid)
        {
            var configured = ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults, forwardSpeed: invalid);
            var result = Validate(configured);
            Assert.That(result.EffectiveConfiguration.ForwardSpeed, Is.EqualTo(ConfigurationTestData.SafeDefaults.ForwardSpeed));
            AssertDiagnostic(result, ConfigurationField.ForwardSpeed);
        }

        [Test]
        public void LaneOrderingToleranceMasksColliderAndThresholdEdgesAreRepaired_Requirements_15_1_15_3_15_4_15_5()
        {
            var invalid = new PlayerConfiguration(8f, 7f, 20f, 0.2f, 0.8f, 2f,
                new Vector3(1f, 0f, -1f), new ColliderProfile(0.5f, 2f, Vector3.zero),
                new ColliderProfile(0.75f, 3f, Vector3.zero), 0, 0, 0.05f, 0.7f,
                Vector3.back, 0.3f, 0.1f, new InputThresholds(0.8f, 0.2f));
            var result = Validate(invalid);
            Assert.That(result.Diagnostics.Select(x => x.Field), Is.EquivalentTo(new[]
            {
                ConfigurationField.LaneCenters, ConfigurationField.LanePositionTolerance,
                ConfigurationField.GroundLayerMask, ConfigurationField.ObstructionLayerMask,
                ConfigurationField.SlideCollider, ConfigurationField.InputThresholds
            }));
            Assert.That(PlayerConfigurationValidator.IsValid(result.EffectiveConfiguration), Is.True);
        }

        [Test]
        public void MissingRequiredReferencesUseFallbackAndAbsentAnimationReceiverIsNonFatal_Requirements_8_8_15_3_15_6_15_9()
        {
            var fallback = ScriptableObject.CreateInstance<TestReference>();
            try
            {
                var missing = new PlayerConfigurationReferences(null, null, null, null);
                var safe = new PlayerConfigurationReferences(fallback, fallback, fallback, null);
                var result = PlayerConfigurationValidator.Validate(ConfigurationTestData.ValidNonDefaults,
                    ConfigurationTestData.SafeDefaults, missing, safe);
                Assert.That(result.EffectiveReferences.PlayerRoot, Is.SameAs(fallback));
                Assert.That(result.EffectiveReferences.InputActionAsset, Is.SameAs(fallback));
                Assert.That(result.EffectiveReferences.CameraTarget, Is.SameAs(fallback));
                Assert.That(result.Diagnostics.Count(x => x.Code == DiagnosticCode.AnimationReceiverAbsent), Is.EqualTo(1));
                Assert.That(result.SimulationEnabled, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(fallback); }
        }


        public static System.Collections.Generic.IEnumerable<TestCaseData> ScalarDomainCases()
        {
            yield return new TestCaseData(ConfigurationField.ForwardSpeed, -0.01f);
            yield return new TestCaseData(ConfigurationField.ForwardSpeed, float.NaN);
            yield return new TestCaseData(ConfigurationField.JumpVelocity, 0f);
            yield return new TestCaseData(ConfigurationField.JumpVelocity, float.PositiveInfinity);
            yield return new TestCaseData(ConfigurationField.GravityAcceleration, 0f);
            yield return new TestCaseData(ConfigurationField.GravityAcceleration, float.NegativeInfinity);
            yield return new TestCaseData(ConfigurationField.LaneChangeDuration, 0f);
            yield return new TestCaseData(ConfigurationField.LaneChangeDuration, float.NaN);
            yield return new TestCaseData(ConfigurationField.SlideDuration, 0f);
            yield return new TestCaseData(ConfigurationField.SlideDuration, float.PositiveInfinity);
            yield return new TestCaseData(ConfigurationField.LanePositionTolerance, -0.01f);
            yield return new TestCaseData(ConfigurationField.GroundContactTolerance, -0.01f);
            yield return new TestCaseData(ConfigurationField.CameraSettleDuration, 0f);
            yield return new TestCaseData(ConfigurationField.CameraFollowTolerance, -0.01f);
        }

        [TestCaseSource(nameof(ScalarDomainCases))]
        public void ScalarDomainsRejectNonFiniteAndOutOfDomainValues_Requirements_15_1_15_3_15_4(
            ConfigurationField field, float invalid)
        {
            var configured = ConfigurationTestData.WithScalar(ConfigurationTestData.ValidNonDefaults, field, invalid);
            var result = Validate(configured);
            Assert.That(ConfigurationTestData.Read(result.EffectiveConfiguration, field),
                Is.EqualTo(ConfigurationTestData.Read(ConfigurationTestData.SafeDefaults, field)));
            AssertDiagnostic(result, field);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        public void GroundNormalThresholdRejectsNonFiniteAndOutsideInclusiveUnitRange_Requirements_15_1_15_3_15_4(
            float invalid)
        {
            var result = Validate(ConfigurationTestData.With(
                ConfigurationTestData.ValidNonDefaults, groundNormalThreshold: invalid));
            Assert.That(result.EffectiveConfiguration.GroundNormalThreshold,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.GroundNormalThreshold));
            AssertDiagnostic(result, ConfigurationField.GroundNormalThreshold);
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        public void CharacterControllerCapsulesRequirePositiveRadiusAndHeightAtLeastTwiceRadius_Requirements_15_1_15_3_15_4(
            float invalidHeight)
        {
            var baseline = new ColliderProfile(0.5f, invalidHeight, Vector3.up);
            var baselineResult = Validate(ConfigurationTestData.With(
                ConfigurationTestData.ValidNonDefaults, baselineCollider: baseline));
            Assert.That(baselineResult.EffectiveConfiguration.BaselineCollider,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.BaselineCollider));
            AssertDiagnostic(baselineResult, ConfigurationField.BaselineCollider);

            var slide = new ColliderProfile(0.4f, invalidHeight, Vector3.up * 0.5f);
            var slideResult = Validate(ConfigurationTestData.With(
                ConfigurationTestData.ValidNonDefaults, slideCollider: slide));
            Assert.That(slideResult.EffectiveConfiguration.SlideCollider,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.SlideCollider));
            AssertDiagnostic(slideResult, ConfigurationField.SlideCollider);
        }

        [TestCase(0, ConfigurationField.GroundLayerMask)]
        [TestCase(0, ConfigurationField.ObstructionLayerMask)]
        public void RequiredPhysicsMasksRejectEmptySets_Requirements_15_1_15_3_15_4(
            int emptyMask, ConfigurationField field)
        {
            var configured = field == ConfigurationField.GroundLayerMask
                ? ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults, groundLayerMask: emptyMask)
                : ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults, obstructionLayerMask: emptyMask);
            var result = Validate(configured);
            Assert.That(ConfigurationTestData.Read(result.EffectiveConfiguration, field),
                Is.EqualTo(ConfigurationTestData.Read(ConfigurationTestData.SafeDefaults, field)));
            AssertDiagnostic(result, field);
        }

        [Test]
        public void RelationalValidationUsesOrderedEffectiveDependencies_Requirements_15_3_15_4_15_5()
        {
            var laneSource = ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults,
                laneCenters: new Vector3(2f, 0f, -2f), lanePositionTolerance: 0.4f);
            var laneResult = Validate(laneSource);
            Assert.That(laneResult.EffectiveConfiguration.LaneCenters,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.LaneCenters));
            Assert.That(laneResult.EffectiveConfiguration.LanePositionTolerance, Is.EqualTo(0.4f));
            Assert.That(laneResult.Diagnostics.Select(x => x.Field),
                Is.EquivalentTo(new[] { ConfigurationField.LaneCenters }));

            var baselineSource = ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults,
                baselineCollider: new ColliderProfile(1f, 1f, Vector3.up),
                slideCollider: ConfigurationTestData.SafeDefaults.SlideCollider);
            var baselineResult = Validate(baselineSource);
            Assert.That(baselineResult.EffectiveConfiguration.BaselineCollider,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.BaselineCollider));
            Assert.That(baselineResult.EffectiveConfiguration.SlideCollider,
                Is.EqualTo(baselineSource.SlideCollider));
            Assert.That(baselineResult.Diagnostics.Select(x => x.Field),
                Is.EquivalentTo(new[] { ConfigurationField.BaselineCollider }));

            var thresholdSource = ConfigurationTestData.With(ConfigurationTestData.ValidNonDefaults,
                inputThresholds: new InputThresholds(-0.1f, 0.7f));
            var thresholdResult = Validate(thresholdSource);
            Assert.That(thresholdResult.EffectiveConfiguration.InputThresholds.NeutralThreshold,
                Is.EqualTo(ConfigurationTestData.SafeDefaults.InputThresholds.NeutralThreshold));
            Assert.That(thresholdResult.EffectiveConfiguration.InputThresholds.ActuationThreshold,
                Is.EqualTo(0.7f));
            Assert.That(thresholdResult.Diagnostics.Select(x => x.Field),
                Is.EquivalentTo(new[] { ConfigurationField.InputThresholds }));
        }

        [Test]
        public void ValidConfigurationIsPreservedAndExposesValidStatus_Requirements_15_1_15_2_15_8_15_9()
        {
            var result = Validate(ConfigurationTestData.ValidNonDefaults);
            Assert.That(result.Status, Is.EqualTo(ConfigurationStatus.Valid));
            Assert.That(result.EffectiveConfiguration, Is.EqualTo(ConfigurationTestData.ValidNonDefaults));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.SimulationEnabled, Is.True);
        }

        [Test]
        public void InvalidFieldDiagnosticIdentifiesConstraintAndDocumentedFallback_Requirements_15_3_15_7_15_8()
        {
            var result = Validate(ConfigurationTestData.With(
                ConfigurationTestData.ValidNonDefaults, laneChangeDuration: 0f));
            var diagnostic = result.Diagnostics.Single(x => x.Field == ConfigurationField.LaneChangeDuration);
            Assert.That(diagnostic.Constraint, Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.FallbackCategory, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void InvalidSafeFallbackProducesFatalStatusAndDisablesSimulation_Requirements_15_4_15_5_15_8()
        {
            var reference = ScriptableObject.CreateInstance<TestReference>();
            try
            {
                var references = new PlayerConfigurationReferences(reference, reference, reference, reference);
                var invalidConfigured = ConfigurationTestData.With(
                    ConfigurationTestData.ValidNonDefaults, jumpVelocity: 0f);
                var invalidFallback = ConfigurationTestData.With(
                    ConfigurationTestData.SafeDefaults, jumpVelocity: float.NaN);
                var result = PlayerConfigurationValidator.Validate(
                    invalidConfigured, invalidFallback, references, references);
                Assert.That(result.Status, Is.EqualTo(ConfigurationStatus.Fatal));
                Assert.That(result.SimulationEnabled, Is.False);
                Assert.That(result.Diagnostics.Any(x =>
                    x.Code == DiagnosticCode.InvalidFallbackConfiguration &&
                    x.Severity == DiagnosticSeverity.Fatal), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        [Test]
        public void PostStartInvalidFieldAppliesFallbackAndHaltsBeforeNextUpdate_Requirement_15_10()
        {
            var reference = ScriptableObject.CreateInstance<TestReference>();
            try
            {
                var references = new PlayerConfigurationReferences(reference, reference, reference, reference);
                var invalid = ConfigurationTestData.With(
                    ConfigurationTestData.ValidNonDefaults, forwardSpeed: -1f);
                var result = PlayerConfigurationValidator.Validate(invalid,
                    ConfigurationTestData.SafeDefaults, references, references,
                    ConfigurationValidationPhase.AfterSimulationStarted);
                Assert.That(result.EffectiveConfiguration.ForwardSpeed,
                    Is.EqualTo(ConfigurationTestData.SafeDefaults.ForwardSpeed));
                Assert.That(result.SimulationEnabled, Is.False);
                Assert.That(result.Diagnostics.Count(x =>
                    x.Field == ConfigurationField.ForwardSpeed &&
                    x.Code == DiagnosticCode.PostStartInvalidConfiguration), Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        private static ConfigurationValidationResult Validate(PlayerConfiguration configured)
        {
            var reference = ScriptableObject.CreateInstance<TestReference>();
            try
            {
                var references = new PlayerConfigurationReferences(reference, reference, reference, reference);
                return PlayerConfigurationValidator.Validate(
                    configured, ConfigurationTestData.SafeDefaults, references, references);
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        private static void AssertDiagnostic(ConfigurationValidationResult result, ConfigurationField field)
        {
            Assert.That(result.Diagnostics.Count(x => x.Field == field), Is.EqualTo(1));
        }

        private sealed class TestReference : ScriptableObject,
            SubwaySurfers.Player.Contracts.IAnimationReceiver
        {
            public bool TryApply(SubwaySurfers.Player.Contracts.AnimationCommand command) { return true; }
        }
    }

    internal static class ConfigurationTestData
    {
        public static readonly PlayerConfiguration SafeDefaults = new PlayerConfiguration(
            8f, 7f, 20f, 0.2f, 0.8f, 0.25f,
            new Vector3(-3f, 0f, 3f),
            new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
            new ColliderProfile(0.5f, 1f, new Vector3(0f, 0.5f, 0f)),
            1, 2, 0.1f, 0.6f, new Vector3(0f, 5f, -8f), 0.25f, 0.05f,
            new InputThresholds(0.2f, 0.5f));

        public static readonly PlayerConfiguration ValidNonDefaults = new PlayerConfiguration(
            9f, 8f, 25f, 0.3f, 1f, 0.4f,
            new Vector3(-4f, 0f, 4f),
            new ColliderProfile(0.6f, 2.4f, new Vector3(0f, 1.2f, 0f)),
            new ColliderProfile(0.5f, 1.2f, new Vector3(0f, 0.6f, 0f)),
            4, 8, 0.2f, 0.75f, new Vector3(1f, 6f, -9f), 0.4f, 0.1f,
            new InputThresholds(0.1f, 0.7f));

        public static readonly ConfigurationField[] ValidatedFields =
        {
            ConfigurationField.ForwardSpeed,
            ConfigurationField.JumpVelocity,
            ConfigurationField.GravityAcceleration,
            ConfigurationField.LaneChangeDuration,
            ConfigurationField.SlideDuration,
            ConfigurationField.LanePositionTolerance,
            ConfigurationField.LaneCenters,
            ConfigurationField.BaselineCollider,
            ConfigurationField.SlideCollider,
            ConfigurationField.GroundLayerMask,
            ConfigurationField.ObstructionLayerMask,
            ConfigurationField.GroundContactTolerance,
            ConfigurationField.GroundNormalThreshold,
            ConfigurationField.InitialCameraPosition,
            ConfigurationField.CameraSettleDuration,
            ConfigurationField.CameraFollowTolerance,
            ConfigurationField.InputThresholds
        };

        public static PlayerConfiguration With(PlayerConfiguration source,
            float? forwardSpeed = null, float? jumpVelocity = null,
            float? gravityAcceleration = null, float? laneChangeDuration = null,
            float? slideDuration = null, float? lanePositionTolerance = null,
            Vector3? laneCenters = null, ColliderProfile? baselineCollider = null,
            ColliderProfile? slideCollider = null, int? groundLayerMask = null,
            int? obstructionLayerMask = null, float? groundContactTolerance = null,
            float? groundNormalThreshold = null, Vector3? initialCameraPosition = null,
            float? cameraSettleDuration = null, float? cameraFollowTolerance = null,
            InputThresholds? inputThresholds = null)
        {
            return new PlayerConfiguration(
                forwardSpeed ?? source.ForwardSpeed,
                jumpVelocity ?? source.JumpVelocity,
                gravityAcceleration ?? source.GravityAcceleration,
                laneChangeDuration ?? source.LaneChangeDuration,
                slideDuration ?? source.SlideDuration,
                lanePositionTolerance ?? source.LanePositionTolerance,
                laneCenters ?? source.LaneCenters,
                baselineCollider ?? source.BaselineCollider,
                slideCollider ?? source.SlideCollider,
                groundLayerMask ?? source.GroundLayerMask,
                obstructionLayerMask ?? source.ObstructionLayerMask,
                groundContactTolerance ?? source.GroundContactTolerance,
                groundNormalThreshold ?? source.GroundNormalThreshold,
                initialCameraPosition ?? source.InitialCameraPosition,
                cameraSettleDuration ?? source.CameraSettleDuration,
                cameraFollowTolerance ?? source.CameraFollowTolerance,
                inputThresholds ?? source.InputThresholds);
        }

        public static PlayerConfiguration WithScalar(
            PlayerConfiguration source, ConfigurationField field, float value)
        {
            switch (field)
            {
                case ConfigurationField.ForwardSpeed: return With(source, forwardSpeed: value);
                case ConfigurationField.JumpVelocity: return With(source, jumpVelocity: value);
                case ConfigurationField.GravityAcceleration: return With(source, gravityAcceleration: value);
                case ConfigurationField.LaneChangeDuration: return With(source, laneChangeDuration: value);
                case ConfigurationField.SlideDuration: return With(source, slideDuration: value);
                case ConfigurationField.LanePositionTolerance: return With(source, lanePositionTolerance: value);
                case ConfigurationField.GroundContactTolerance: return With(source, groundContactTolerance: value);
                case ConfigurationField.GroundNormalThreshold: return With(source, groundNormalThreshold: value);
                case ConfigurationField.CameraSettleDuration: return With(source, cameraSettleDuration: value);
                case ConfigurationField.CameraFollowTolerance: return With(source, cameraFollowTolerance: value);
                default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
            }
        }

        public static PlayerConfiguration RandomValidConfiguration(Random random, PlayerConfiguration defaults)
        {
            var neutral = Next(random, 0f, 0.4f);
            return With(defaults,
                forwardSpeed: Next(random, 0f, 30f),
                jumpVelocity: Next(random, 0.1f, 20f),
                gravityAcceleration: Next(random, 0.1f, 50f),
                laneChangeDuration: Next(random, 0.05f, 2f),
                slideDuration: Next(random, 0.05f, 3f),
                lanePositionTolerance: Next(random, 0f, 1.49f),
                groundLayerMask: 1 << random.Next(0, 30),
                obstructionLayerMask: 1 << random.Next(0, 30),
                groundContactTolerance: Next(random, 0f, 0.5f),
                groundNormalThreshold: Next(random, 0f, 1f),
                initialCameraPosition: new Vector3(
                    Next(random, -20f, 20f), Next(random, -20f, 20f), Next(random, -20f, 20f)),
                cameraSettleDuration: Next(random, 0.01f, 2f),
                cameraFollowTolerance: Next(random, 0f, 1f),
                inputThresholds: new InputThresholds(neutral, Next(random, 0.5f, 1f)));
        }

        public static PlayerConfiguration WithInvalid(
            PlayerConfiguration source, ConfigurationField field, Random random)
        {
            switch (field)
            {
                case ConfigurationField.ForwardSpeed: return With(source, forwardSpeed: InvalidNonNegative(random));
                case ConfigurationField.JumpVelocity: return With(source, jumpVelocity: InvalidPositive(random));
                case ConfigurationField.GravityAcceleration: return With(source, gravityAcceleration: InvalidPositive(random));
                case ConfigurationField.LaneChangeDuration: return With(source, laneChangeDuration: InvalidPositive(random));
                case ConfigurationField.SlideDuration: return With(source, slideDuration: InvalidPositive(random));
                case ConfigurationField.LanePositionTolerance: return With(source, lanePositionTolerance: 100f);
                case ConfigurationField.LaneCenters: return With(source, laneCenters: new Vector3(1f, 0f, -1f));
                case ConfigurationField.BaselineCollider:
                    return With(source, baselineCollider: new ColliderProfile(1f, 1f, Vector3.up));
                case ConfigurationField.SlideCollider:
                    return With(source, slideCollider: new ColliderProfile(2f, 4f, Vector3.up * 2f));
                case ConfigurationField.GroundLayerMask: return With(source, groundLayerMask: 0);
                case ConfigurationField.ObstructionLayerMask: return With(source, obstructionLayerMask: 0);
                case ConfigurationField.GroundContactTolerance:
                    return With(source, groundContactTolerance: InvalidNonNegative(random));
                case ConfigurationField.GroundNormalThreshold:
                    return With(source, groundNormalThreshold: random.Next(2) == 0 ? -0.1f : 1.1f);
                case ConfigurationField.InitialCameraPosition:
                    return With(source, initialCameraPosition: new Vector3(float.NaN, 0f, 0f));
                case ConfigurationField.CameraSettleDuration:
                    return With(source, cameraSettleDuration: InvalidPositive(random));
                case ConfigurationField.CameraFollowTolerance:
                    return With(source, cameraFollowTolerance: InvalidNonNegative(random));
                case ConfigurationField.InputThresholds:
                    return With(source, inputThresholds: new InputThresholds(0.8f, 0.2f));
                default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
            }
        }

        public static object Read(PlayerConfiguration configuration, ConfigurationField field)
        {
            switch (field)
            {
                case ConfigurationField.ForwardSpeed: return configuration.ForwardSpeed;
                case ConfigurationField.JumpVelocity: return configuration.JumpVelocity;
                case ConfigurationField.GravityAcceleration: return configuration.GravityAcceleration;
                case ConfigurationField.LaneChangeDuration: return configuration.LaneChangeDuration;
                case ConfigurationField.SlideDuration: return configuration.SlideDuration;
                case ConfigurationField.LanePositionTolerance: return configuration.LanePositionTolerance;
                case ConfigurationField.LaneCenters: return configuration.LaneCenters;
                case ConfigurationField.BaselineCollider: return configuration.BaselineCollider;
                case ConfigurationField.SlideCollider: return configuration.SlideCollider;
                case ConfigurationField.GroundLayerMask: return configuration.GroundLayerMask;
                case ConfigurationField.ObstructionLayerMask: return configuration.ObstructionLayerMask;
                case ConfigurationField.GroundContactTolerance: return configuration.GroundContactTolerance;
                case ConfigurationField.GroundNormalThreshold: return configuration.GroundNormalThreshold;
                case ConfigurationField.InitialCameraPosition: return configuration.InitialCameraPosition;
                case ConfigurationField.CameraSettleDuration: return configuration.CameraSettleDuration;
                case ConfigurationField.CameraFollowTolerance: return configuration.CameraFollowTolerance;
                case ConfigurationField.InputThresholds: return configuration.InputThresholds;
                default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
            }
        }

        private static float Next(Random random, float minimum, float maximum)
        {
            return minimum + ((maximum - minimum) * (float)random.NextDouble());
        }

        private static float InvalidPositive(Random random)
        {
            switch (random.Next(4))
            {
                case 0: return 0f;
                case 1: return -1f;
                case 2: return float.NaN;
                default: return float.PositiveInfinity;
            }
        }

        private static float InvalidNonNegative(Random random)
        {
            switch (random.Next(3))
            {
                case 0: return -1f;
                case 1: return float.NaN;
                default: return float.NegativeInfinity;
            }
        }
    }
}
