using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Configuration
{
    /// <summary>
    /// The single player-owned configuration source. One asset carries two serialized blocks: the
    /// values the prefab is authored with, and the documented fallback every validated field is
    /// repaired from. Keeping both in one asset is what lets the prefab and the tests validate against
    /// exactly the same pair, and what makes a fallback an authored, inspectable value rather than a
    /// constant hidden in code.
    ///
    /// The type lives in its own file so a serialized script reference resolves to it, which is what an
    /// imported asset and the prefab that references it both depend on.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerConfiguration", menuName = "Subway Surfers/Player Configuration")]
    public sealed class PlayerConfigurationAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Prefab-authored values validated before simulation.")]
        private SerializedPlayerConfiguration configured = new SerializedPlayerConfiguration();
        [SerializeField, Tooltip("Documented field fallbacks. These values must form a valid configuration.")]
        private SerializedPlayerConfiguration safeDefaults = new SerializedPlayerConfiguration();

        public PlayerConfiguration ConfiguredConfiguration
        {
            get { return configured == null ? PlayerConfiguration.SafeDefaults : configured.ToImmutable(); }
        }

        public PlayerConfiguration SafeDefaultConfiguration
        {
            get { return safeDefaults == null ? PlayerConfiguration.SafeDefaults : safeDefaults.ToImmutable(); }
        }
    }
}
