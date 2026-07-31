using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Validation-only running surface. Grounding requires both the configured ground layer and a
    /// resolved running-surface marker, so the scene needs a marked, non-trigger surface to stand on.
    /// Non-production: the Environment system owns real running surfaces.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ValidationRunningSurface : MonoBehaviour, IRunningSurface,
        INonProductionValidationDouble
    {
        public string NonProductionNotice
        {
            get
            {
                return "Validation-only running surface for PlayerTestScene. Not production " +
                       "environment content.";
            }
        }
    }
}
