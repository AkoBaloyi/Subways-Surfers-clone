using System;
using System.Collections.Generic;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    public interface IPlayerConfigurationConsumer
    {
        void Initialize(PlayerConfiguration configuration, PlayerConfigurationReferences references);
    }

    [DisallowMultipleComponent]
    public sealed partial class PlayerControllerFacade : MonoBehaviour
    {
        [SerializeField] private PlayerConfigurationAsset configurationAsset;
        [SerializeField] private UnityEngine.Object inputActionAsset;
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private MonoBehaviour animationReceiver;
        [SerializeField] private UnityEngine.Object fallbackInputActionAsset;
        [SerializeField] private Transform fallbackCameraTarget;
        [SerializeField] private MonoBehaviour fallbackAnimationReceiver;
        [SerializeField] private MonoBehaviour[] configurationConsumers = Array.Empty<MonoBehaviour>();

        private ConfigurationValidationResult configurationResult;
        private bool simulationStarted;

        public ConfigurationStatus ConfigurationStatus
        {
            get { return configurationResult == null ? ConfigurationStatus.Fatal : configurationResult.Status; }
        }

        public IReadOnlyList<ConfigurationDiagnostic> ConfigurationDiagnostics
        {
            get
            {
                return configurationResult == null
                    ? Array.Empty<ConfigurationDiagnostic>()
                    : configurationResult.Diagnostics;
            }
        }

        public PlayerConfiguration EffectiveConfiguration
        {
            get { return configurationResult == null ? PlayerConfiguration.SafeDefaults : configurationResult.EffectiveConfiguration; }
        }

        public PlayerConfigurationReferences EffectiveReferences
        {
            get
            {
                return configurationResult == null
                    ? default(PlayerConfigurationReferences)
                    : configurationResult.EffectiveReferences;
            }
        }

        public bool SimulationEnabled
        {
            get { return configurationResult != null && configurationResult.SimulationEnabled; }
        }

        public event Action<ConfigurationDiagnostic> ConfigurationDiagnosticReported;

        private void Awake()
        {
            ValidateAndDistribute(ConfigurationValidationPhase.BeforeSimulationStarted);
        }

        public ConfigurationValidationResult ValidateAfterSimulationStarted()
        {
            simulationStarted = true;
            return ValidateAndDistribute(ConfigurationValidationPhase.AfterSimulationStarted);
        }

        public void MarkSimulationStarted()
        {
            simulationStarted = true;
        }

        private ConfigurationValidationResult ValidateAndDistribute(ConfigurationValidationPhase phase)
        {
            var configured = configurationAsset == null
                ? PlayerConfiguration.SafeDefaults
                : configurationAsset.ConfiguredConfiguration;
            var defaults = configurationAsset == null
                ? PlayerConfiguration.SafeDefaults
                : configurationAsset.SafeDefaultConfiguration;

            var configuredReferences = new PlayerConfigurationReferences(
                gameObject, inputActionAsset, cameraTarget, animationReceiver);
            var safeReferences = new PlayerConfigurationReferences(
                gameObject, fallbackInputActionAsset,
                fallbackCameraTarget == null ? transform : fallbackCameraTarget,
                fallbackAnimationReceiver);

            configurationResult = PlayerConfigurationValidator.Validate(
                configured, defaults, configuredReferences, safeReferences,
                simulationStarted ? ConfigurationValidationPhase.AfterSimulationStarted : phase);

            PublishDiagnostics(configurationResult.Diagnostics);
            if (!configurationResult.SimulationEnabled) return configurationResult;

            foreach (var behaviour in configurationConsumers)
            {
                var consumer = behaviour as IPlayerConfigurationConsumer;
                if (consumer != null)
                {
                    consumer.Initialize(configurationResult.EffectiveConfiguration,
                        configurationResult.EffectiveReferences);
                }
            }

            return configurationResult;
        }

        private void PublishDiagnostics(IReadOnlyList<ConfigurationDiagnostic> diagnostics)
        {
            var handler = ConfigurationDiagnosticReported;
            if (handler == null) return;
            for (var index = 0; index < diagnostics.Count; index++) handler(diagnostics[index]);
        }
    }
}
