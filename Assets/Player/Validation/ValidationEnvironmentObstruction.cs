using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Validation-only overhead or lateral obstruction. Safe slide restoration refuses to restore the
    /// baseline capsule while a collider carrying this marker overlaps the baseline volume, so the
    /// scene needs one to demonstrate blocked restoration.
    /// Non-production: the Environment system owns real obstructions.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ValidationEnvironmentObstruction : MonoBehaviour, IEnvironmentObstruction,
        INonProductionValidationDouble
    {
        public string NonProductionNotice
        {
            get
            {
                return "Validation-only obstruction for PlayerTestScene blocked-restoration " +
                       "coverage. Not production environment content.";
            }
        }
    }
}
