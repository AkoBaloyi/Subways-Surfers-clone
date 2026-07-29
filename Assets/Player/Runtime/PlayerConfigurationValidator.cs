using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Configuration
{
    public static class PlayerConfigurationValidator
    {
        private const string SerializedFallback = "Serialized safe default";
        private const string EffectiveDependencyFallback = "Safe default derived from effective dependency";

        public static ConfigurationValidationResult Validate(PlayerConfiguration configured,
            PlayerConfiguration safeDefaults, PlayerConfigurationReferences configuredReferences,
            PlayerConfigurationReferences safeReferences,
            ConfigurationValidationPhase phase = ConfigurationValidationPhase.BeforeSimulationStarted)
        {
            if (!IsValid(safeDefaults))
            {
                return Fatal(configured, configuredReferences, ConfigurationField.SafeDefaults,
                    "Safe defaults must themselves satisfy every configuration constraint.");
            }

            var invalidFallbackReference = FirstInvalidRequiredReference(safeReferences);
            if (invalidFallbackReference.HasValue)
            {
                return Fatal(configured, configuredReferences, invalidFallbackReference.Value,
                    "A required safe fallback reference is absent.");
            }

            var diagnostics = new List<ConfigurationDiagnostic>();
            var repaired = false;

            var forwardSpeed = RepairScalar(configured.ForwardSpeed, safeDefaults.ForwardSpeed,
                IsNonNegative, ConfigurationField.ForwardSpeed, "finite and non-negative",
                phase, diagnostics, ref repaired);
            var jumpVelocity = RepairScalar(configured.JumpVelocity, safeDefaults.JumpVelocity,
                IsPositive, ConfigurationField.JumpVelocity, "finite and positive",
                phase, diagnostics, ref repaired);
            var gravity = RepairScalar(configured.GravityAcceleration, safeDefaults.GravityAcceleration,
                IsPositive, ConfigurationField.GravityAcceleration, "finite and positive",
                phase, diagnostics, ref repaired);
            var laneDuration = RepairScalar(configured.LaneChangeDuration, safeDefaults.LaneChangeDuration,
                IsPositive, ConfigurationField.LaneChangeDuration, "finite and positive",
                phase, diagnostics, ref repaired);
            var slideDuration = RepairScalar(configured.SlideDuration, safeDefaults.SlideDuration,
                IsPositive, ConfigurationField.SlideDuration, "finite and positive",
                phase, diagnostics, ref repaired);

            var laneCenters = configured.LaneCenters;
            if (!AreOrderedLaneCenters(laneCenters))
            {
                laneCenters = safeDefaults.LaneCenters;
                AddRepair(ConfigurationField.LaneCenters,
                    "finite and strictly increasing Left, Center, Right lane centers",
                    SerializedFallback, phase, diagnostics, ref repaired);
            }

            var laneTolerance = configured.LanePositionTolerance;
            var halfLaneSeparation = HalfMinimumLaneSeparation(laneCenters);
            if (!IsNonNegative(laneTolerance) || !(laneTolerance < halfLaneSeparation))
            {
                laneTolerance = safeDefaults.LanePositionTolerance;
                var fallbackCategory = SerializedFallback;
                if (!(laneTolerance < halfLaneSeparation))
                {
                    laneTolerance = halfLaneSeparation * 0.5f;
                    fallbackCategory = EffectiveDependencyFallback;
                }

                AddRepair(ConfigurationField.LanePositionTolerance,
                    "finite, non-negative, and below half the effective minimum lane separation",
                    fallbackCategory, phase, diagnostics, ref repaired);
            }

            var baseline = configured.BaselineCollider;
            if (!IsValidCapsule(baseline))
            {
                baseline = safeDefaults.BaselineCollider;
                AddRepair(ConfigurationField.BaselineCollider,
                    "finite positive capsule with Height >= 2 * Radius",
                    SerializedFallback, phase, diagnostics, ref repaired);
            }

            var slide = configured.SlideCollider;
            if (!IsValidCapsule(slide) || !FitsInside(slide, baseline))
            {
                slide = safeDefaults.SlideCollider;
                var fallbackCategory = SerializedFallback;
                if (!FitsInside(slide, baseline))
                {
                    slide = DeriveSlideProfile(safeDefaults.SlideCollider, baseline);
                    fallbackCategory = EffectiveDependencyFallback;
                }

                AddRepair(ConfigurationField.SlideCollider,
                    "valid capsule fitting within the effective baseline capsule",
                    fallbackCategory, phase, diagnostics, ref repaired);
            }

            var groundMask = configured.GroundLayerMask;
            if (groundMask == 0)
            {
                groundMask = safeDefaults.GroundLayerMask;
                AddRepair(ConfigurationField.GroundLayerMask, "non-empty layer mask",
                    SerializedFallback, phase, diagnostics, ref repaired);
            }

            var obstructionMask = configured.ObstructionLayerMask;
            if (obstructionMask == 0)
            {
                obstructionMask = safeDefaults.ObstructionLayerMask;
                AddRepair(ConfigurationField.ObstructionLayerMask, "non-empty layer mask",
                    SerializedFallback, phase, diagnostics, ref repaired);
            }

            var groundTolerance = RepairScalar(configured.GroundContactTolerance,
                safeDefaults.GroundContactTolerance, IsNonNegative,
                ConfigurationField.GroundContactTolerance, "finite and non-negative",
                phase, diagnostics, ref repaired);
            var groundNormal = RepairScalar(configured.GroundNormalThreshold,
                safeDefaults.GroundNormalThreshold, IsUnitInterval,
                ConfigurationField.GroundNormalThreshold, "finite and in the inclusive range [0, 1]",
                phase, diagnostics, ref repaired);

            var initialCameraPosition = configured.InitialCameraPosition;
            if (!IsFinite(initialCameraPosition))
            {
                initialCameraPosition = safeDefaults.InitialCameraPosition;
                AddRepair(ConfigurationField.InitialCameraPosition, "all components finite",
                    SerializedFallback, phase, diagnostics, ref repaired);
            }

            var cameraDuration = RepairScalar(configured.CameraSettleDuration,
                safeDefaults.CameraSettleDuration, IsPositive,
                ConfigurationField.CameraSettleDuration, "finite and positive",
                phase, diagnostics, ref repaired);
            var cameraTolerance = RepairScalar(configured.CameraFollowTolerance,
                safeDefaults.CameraFollowTolerance, IsNonNegative,
                ConfigurationField.CameraFollowTolerance, "finite and non-negative",
                phase, diagnostics, ref repaired);

            var thresholds = RepairThresholds(configured.InputThresholds,
                safeDefaults.InputThresholds, phase, diagnostics, ref repaired);
            var effectiveReferences = RepairReferences(configuredReferences, safeReferences,
                phase, diagnostics, ref repaired);

            var effective = new PlayerConfiguration(forwardSpeed, jumpVelocity, gravity,
                laneDuration, slideDuration, laneTolerance, laneCenters, baseline, slide,
                groundMask, obstructionMask, groundTolerance, groundNormal,
                initialCameraPosition, cameraDuration, cameraTolerance, thresholds);

            if (!IsValid(effective))
            {
                return Fatal(effective, effectiveReferences, ConfigurationField.SafeDefaults,
                    "Fallback application did not produce a valid effective configuration.", diagnostics);
            }

            var halted = phase == ConfigurationValidationPhase.AfterSimulationStarted && repaired;
            return new ConfigurationValidationResult(
                halted ? ConfigurationStatus.Halted : repaired ? ConfigurationStatus.Repaired : ConfigurationStatus.Valid,
                effective, effectiveReferences, diagnostics, !halted);
        }

        public static bool IsValid(PlayerConfiguration configuration)
        {
            if (!IsNonNegative(configuration.ForwardSpeed) ||
                !IsPositive(configuration.JumpVelocity) ||
                !IsPositive(configuration.GravityAcceleration) ||
                !IsPositive(configuration.LaneChangeDuration) ||
                !IsPositive(configuration.SlideDuration) ||
                !AreOrderedLaneCenters(configuration.LaneCenters)) return false;

            var halfSeparation = HalfMinimumLaneSeparation(configuration.LaneCenters);
            if (!IsNonNegative(configuration.LanePositionTolerance) ||
                !(configuration.LanePositionTolerance < halfSeparation) ||
                !IsValidCapsule(configuration.BaselineCollider) ||
                !IsValidCapsule(configuration.SlideCollider) ||
                !FitsInside(configuration.SlideCollider, configuration.BaselineCollider)) return false;

            return configuration.GroundLayerMask != 0 &&
                   configuration.ObstructionLayerMask != 0 &&
                   IsNonNegative(configuration.GroundContactTolerance) &&
                   IsUnitInterval(configuration.GroundNormalThreshold) &&
                   IsFinite(configuration.InitialCameraPosition) &&
                   IsPositive(configuration.CameraSettleDuration) &&
                   IsNonNegative(configuration.CameraFollowTolerance) &&
                   AreValidThresholds(configuration.InputThresholds);
        }

        private static float RepairScalar(float configured, float fallback,
            Func<float, bool> predicate, ConfigurationField field, string constraint,
            ConfigurationValidationPhase phase, ICollection<ConfigurationDiagnostic> diagnostics,
            ref bool repaired)
        {
            if (predicate(configured)) return configured;
            AddRepair(field, constraint, SerializedFallback, phase, diagnostics, ref repaired);
            return fallback;
        }

        private static InputThresholds RepairThresholds(InputThresholds configured,
            InputThresholds fallback, ConfigurationValidationPhase phase,
            ICollection<ConfigurationDiagnostic> diagnostics, ref bool repaired)
        {
            var reported = false;
            var neutral = configured.NeutralThreshold;
            if (!IsFinite(neutral) || neutral < 0f || neutral >= 1f)
            {
                neutral = fallback.NeutralThreshold;
                AddRepair(ConfigurationField.InputThresholds,
                    "finite thresholds satisfying 0 <= neutral < actuation <= 1",
                    SerializedFallback, phase, diagnostics, ref repaired);
                reported = true;
            }

            var actuation = configured.ActuationThreshold;
            if (!IsFinite(actuation) || actuation <= neutral || actuation > 1f)
            {
                actuation = fallback.ActuationThreshold;
                if (!(actuation > neutral && actuation <= 1f))
                {
                    neutral = fallback.NeutralThreshold;
                    actuation = fallback.ActuationThreshold;
                }

                if (!reported)
                {
                    AddRepair(ConfigurationField.InputThresholds,
                        "finite thresholds satisfying 0 <= neutral < actuation <= 1",
                        SerializedFallback, phase, diagnostics, ref repaired);
                }
            }

            return new InputThresholds(neutral, actuation);
        }

        private static PlayerConfigurationReferences RepairReferences(
            PlayerConfigurationReferences configured, PlayerConfigurationReferences fallback,
            ConfigurationValidationPhase phase, ICollection<ConfigurationDiagnostic> diagnostics,
            ref bool repaired)
        {
            var root = RepairRequiredReference(configured.PlayerRoot, fallback.PlayerRoot,
                ConfigurationField.PlayerRoot, phase, diagnostics, ref repaired);
            var input = RepairRequiredReference(configured.InputActionAsset, fallback.InputActionAsset,
                ConfigurationField.InputActionAsset, phase, diagnostics, ref repaired);
            var camera = RepairRequiredReference(configured.CameraTarget, fallback.CameraTarget,
                ConfigurationField.CameraTarget, phase, diagnostics, ref repaired);

            var animation = configured.AnimationReceiver;
            if (IsUnityNull(animation))
            {
                animation = IsAnimationReceiver(fallback.AnimationReceiver)
                    ? fallback.AnimationReceiver
                    : null;
                diagnostics.Add(new ConfigurationDiagnostic(DiagnosticSeverity.Warning,
                    DiagnosticCode.AnimationReceiverAbsent, ConfigurationField.AnimationReceiver,
                    "optional reference implementing IAnimationReceiver",
                    "Optional no-op diagnostic behavior",
                    "Animation receiver is absent; movement simulation remains enabled."));
            }
            else if (!IsAnimationReceiver(animation))
            {
                animation = IsAnimationReceiver(fallback.AnimationReceiver)
                    ? fallback.AnimationReceiver
                    : null;
                diagnostics.Add(new ConfigurationDiagnostic(DiagnosticSeverity.Warning,
                    DiagnosticCode.InvalidReference, ConfigurationField.AnimationReceiver,
                    "optional reference implementing IAnimationReceiver",
                    "Optional no-op diagnostic behavior",
                    "Animation receiver does not implement IAnimationReceiver; movement simulation remains enabled."));
            }

            return new PlayerConfigurationReferences(root, input, camera, animation);
        }

        private static UnityEngine.Object RepairRequiredReference(UnityEngine.Object configured,
            UnityEngine.Object fallback, ConfigurationField field,
            ConfigurationValidationPhase phase, ICollection<ConfigurationDiagnostic> diagnostics,
            ref bool repaired)
        {
            if (!IsUnityNull(configured)) return configured;
            var code = phase == ConfigurationValidationPhase.AfterSimulationStarted
                ? DiagnosticCode.PostStartInvalidConfiguration
                : DiagnosticCode.MissingReference;
            diagnostics.Add(new ConfigurationDiagnostic(DiagnosticSeverity.Error, code, field,
                "required reference must be assigned", SerializedFallback,
                field + " was absent and its documented safe reference was applied."));
            repaired = true;
            return fallback;
        }

        private static ConfigurationField? FirstInvalidRequiredReference(
            PlayerConfigurationReferences references)
        {
            if (IsUnityNull(references.PlayerRoot)) return ConfigurationField.PlayerRoot;
            if (IsUnityNull(references.InputActionAsset)) return ConfigurationField.InputActionAsset;
            if (IsUnityNull(references.CameraTarget)) return ConfigurationField.CameraTarget;
            return null;
        }

        private static ConfigurationValidationResult Fatal(PlayerConfiguration configuration,
            PlayerConfigurationReferences references, ConfigurationField field, string message,
            IEnumerable<ConfigurationDiagnostic> preceding = null)
        {
            var diagnostics = preceding == null
                ? new List<ConfigurationDiagnostic>()
                : new List<ConfigurationDiagnostic>(preceding);
            diagnostics.Add(new ConfigurationDiagnostic(DiagnosticSeverity.Fatal,
                DiagnosticCode.InvalidFallbackConfiguration, field,
                "safe fallback must be valid and complete", "No fallback; simulation disabled", message));
            return new ConfigurationValidationResult(ConfigurationStatus.Fatal,
                configuration, references, diagnostics, false);
        }

        private static void AddRepair(ConfigurationField field, string constraint,
            string fallbackCategory, ConfigurationValidationPhase phase,
            ICollection<ConfigurationDiagnostic> diagnostics, ref bool repaired)
        {
            var code = phase == ConfigurationValidationPhase.AfterSimulationStarted
                ? DiagnosticCode.PostStartInvalidConfiguration
                : DiagnosticCode.InvalidValue;
            diagnostics.Add(new ConfigurationDiagnostic(DiagnosticSeverity.Error, code, field,
                constraint, fallbackCategory,
                field + " violated " + constraint + "; " + fallbackCategory + " applied."));
            repaired = true;
        }

        private static bool AreOrderedLaneCenters(Vector3 lanes)
        {
            return IsFinite(lanes) && lanes.x < lanes.y && lanes.y < lanes.z;
        }

        private static float HalfMinimumLaneSeparation(Vector3 lanes)
        {
            return Mathf.Min(lanes.y - lanes.x, lanes.z - lanes.y) * 0.5f;
        }

        private static bool IsValidCapsule(ColliderProfile profile)
        {
            return IsPositive(profile.Radius) && IsPositive(profile.Height) &&
                   profile.Height >= 2f * profile.Radius && IsFinite(profile.Center);
        }

        private static bool FitsInside(ColliderProfile inner, ColliderProfile outer)
        {
            if (!IsValidCapsule(inner) || !IsValidCapsule(outer)) return false;
            var horizontalOffset = new Vector2(
                inner.Center.x - outer.Center.x, inner.Center.z - outer.Center.z).magnitude;
            if (horizontalOffset + inner.Radius > outer.Radius) return false;
            var innerBottom = inner.Center.y - inner.Height * 0.5f;
            var innerTop = inner.Center.y + inner.Height * 0.5f;
            var outerBottom = outer.Center.y - outer.Height * 0.5f;
            var outerTop = outer.Center.y + outer.Height * 0.5f;
            return innerBottom >= outerBottom && innerTop <= outerTop;
        }

        private static ColliderProfile DeriveSlideProfile(ColliderProfile fallback,
            ColliderProfile baseline)
        {
            var radius = Mathf.Min(fallback.Radius, baseline.Radius);
            var height = Mathf.Min(fallback.Height, baseline.Height);
            radius = Mathf.Min(radius, height * 0.5f);
            var minimumCenterY = baseline.Center.y - baseline.Height * 0.5f + height * 0.5f;
            var maximumCenterY = baseline.Center.y + baseline.Height * 0.5f - height * 0.5f;
            var centerY = Mathf.Clamp(fallback.Center.y, minimumCenterY, maximumCenterY);
            return new ColliderProfile(radius, height,
                new Vector3(baseline.Center.x, centerY, baseline.Center.z));
        }

        private static bool AreValidThresholds(InputThresholds thresholds)
        {
            return IsFinite(thresholds.NeutralThreshold) &&
                   IsFinite(thresholds.ActuationThreshold) &&
                   thresholds.NeutralThreshold >= 0f &&
                   thresholds.NeutralThreshold < thresholds.ActuationThreshold &&
                   thresholds.ActuationThreshold <= 1f;
        }

        private static bool IsAnimationReceiver(UnityEngine.Object value)
        {
            return !IsUnityNull(value) && value is IAnimationReceiver;
        }

        private static bool IsUnityNull(UnityEngine.Object value)
        {
            return value == null;
        }

        private static bool IsPositive(float value) { return IsFinite(value) && value > 0f; }
        private static bool IsNonNegative(float value) { return IsFinite(value) && value >= 0f; }
        private static bool IsUnitInterval(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
