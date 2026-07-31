using System.Globalization;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Validation-only environment object supplying the identity, kind, and collectible value the
    /// contact tracker consumes. One authored identity is shared by every child collider on the
    /// object, which is what keeps logical contact deduplication stable for multi-collider objects.
    ///
    /// The player never mutates this component: it is read for identity only. Nothing here collects,
    /// scores, destroys, deactivates, or repositions anything.
    /// Non-production: the Environment system owns real obstacles and coins.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ValidationEnvironmentObject : MonoBehaviour, IEnvironmentObject,
        INonProductionValidationDouble
    {
        [SerializeField]
        [Tooltip("Stable non-empty identity shared by every collider belonging to this object. " +
                 "Logical contact deduplication keys on this value, so two distinct objects must " +
                 "never share it.")]
        private string environmentObjectId = string.Empty;

        [SerializeField]
        [Tooltip("Whether the player treats a contact with this object as a hit or as a coin " +
                 "collection.")]
        private EnvironmentObjectKind kind = EnvironmentObjectKind.Obstacle;

        [SerializeField]
        [Tooltip("Finite value reported on the coin event. Ignored for the Obstacle kind. The " +
                 "player reports this value and never applies it to a score.")]
        private float collectibleValue;

        public string EnvironmentObjectId { get { return environmentObjectId; } }

        public EnvironmentObjectKind Kind { get { return kind; } }

        public float CollectibleValue { get { return collectibleValue; } }

        public string NonProductionNotice
        {
            get
            {
                return "Validation-only environment object for PlayerTestScene. Not production " +
                       "environment content.";
            }
        }

        /// <summary>
        /// Authors identity from code so a scene fixture or a test can build an object without an
        /// Inspector pass. Kept separate from the serialized fields so authored scene values are the
        /// default source of truth.
        /// </summary>
        public void Configure(string identity, EnvironmentObjectKind objectKind, float value)
        {
            environmentObjectId = identity;
            kind = objectKind;
            collectibleValue = value;
        }

        public string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} [id={1}, kind={2}, value={3}]",
                name,
                string.IsNullOrEmpty(environmentObjectId) ? "<empty>" : environmentObjectId,
                kind,
                collectibleValue.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
