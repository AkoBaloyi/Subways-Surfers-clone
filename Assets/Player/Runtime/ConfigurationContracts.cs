using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Configuration
{
    public enum ConfigurationField
    {
        ForwardSpeed,
        JumpVelocity,
        GravityAcceleration,
        LaneChangeDuration,
        SlideDuration,
        LanePositionTolerance,
        LaneCenters,
        BaselineCollider,
        SlideCollider,
        GroundLayerMask,
        ObstructionLayerMask,
        GroundContactTolerance,
        GroundNormalThreshold,
        InitialCameraPosition,
        CameraSettleDuration,
        CameraFollowTolerance,
        InputThresholds,
        PlayerRoot,
        InputActionAsset,
        CameraTarget,
        AnimationReceiver,
        ConfigurationAsset,
        SafeDefaults
    }

    public enum ConfigurationStatus { Valid, Repaired, Halted, Fatal }
    public enum ConfigurationValidationPhase { BeforeSimulationStarted, AfterSimulationStarted }

    public readonly struct PlayerConfigurationReferences
    {
        public PlayerConfigurationReferences(UnityEngine.Object playerRoot,
            UnityEngine.Object inputActionAsset, UnityEngine.Object cameraTarget,
            UnityEngine.Object animationReceiver)
        {
            PlayerRoot = playerRoot;
            InputActionAsset = inputActionAsset;
            CameraTarget = cameraTarget;
            AnimationReceiver = animationReceiver;
        }

        public UnityEngine.Object PlayerRoot { get; }
        public UnityEngine.Object InputActionAsset { get; }
        public UnityEngine.Object CameraTarget { get; }
        public UnityEngine.Object AnimationReceiver { get; }
    }

    public readonly struct ConfigurationDiagnostic : IEquatable<ConfigurationDiagnostic>
    {
        public ConfigurationDiagnostic(DiagnosticSeverity severity, DiagnosticCode code,
            ConfigurationField field, string constraint, string fallbackCategory, string message)
        {
            Severity = severity;
            Code = code;
            Field = field;
            Constraint = constraint;
            FallbackCategory = fallbackCategory;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; }
        public DiagnosticCode Code { get; }
        public ConfigurationField Field { get; }
        public string Constraint { get; }
        public string FallbackCategory { get; }
        public string Message { get; }

        public ValidationDiagnostic ToValidationDiagnostic()
        {
            return new ValidationDiagnostic(Severity, Code, Field.ToString(), Message);
        }

        public bool Equals(ConfigurationDiagnostic other)
        {
            return Severity == other.Severity && Code == other.Code && Field == other.Field &&
                   string.Equals(Constraint, other.Constraint, StringComparison.Ordinal) &&
                   string.Equals(FallbackCategory, other.FallbackCategory, StringComparison.Ordinal) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ConfigurationDiagnostic other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ((int)Severity * 397) ^ (int)Code;
                hash = (hash * 397) ^ (int)Field;
                hash = (hash * 397) ^ (Constraint == null ? 0 : StringComparer.Ordinal.GetHashCode(Constraint));
                hash = (hash * 397) ^ (FallbackCategory == null ? 0 : StringComparer.Ordinal.GetHashCode(FallbackCategory));
                return (hash * 397) ^ (Message == null ? 0 : StringComparer.Ordinal.GetHashCode(Message));
            }
        }
    }

    public sealed class ConfigurationValidationResult
    {
        private readonly ConfigurationDiagnostic[] diagnostics;

        public ConfigurationValidationResult(ConfigurationStatus status,
            PlayerConfiguration effectiveConfiguration,
            PlayerConfigurationReferences effectiveReferences,
            IEnumerable<ConfigurationDiagnostic> diagnostics, bool simulationEnabled)
        {
            Status = status;
            EffectiveConfiguration = effectiveConfiguration;
            EffectiveReferences = effectiveReferences;
            this.diagnostics = diagnostics == null
                ? Array.Empty<ConfigurationDiagnostic>()
                : new List<ConfigurationDiagnostic>(diagnostics).ToArray();
            SimulationEnabled = simulationEnabled;
        }

        public ConfigurationStatus Status { get; }
        public PlayerConfiguration EffectiveConfiguration { get; }
        public PlayerConfigurationReferences EffectiveReferences { get; }
        public IReadOnlyList<ConfigurationDiagnostic> Diagnostics { get { return diagnostics; } }
        public bool SimulationEnabled { get; }
    }
}
