using System.Collections.Generic;
using System.Globalization;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Integration
{
    /// <summary>
    /// Places star pickups along the lanes ahead of the player so the braking mechanic has something to
    /// brake on. The environment system owns real collectibles; this is a stand-in that exists because
    /// the scene currently contains no star or coin prefab at all, so no pickup event can ever fire.
    ///
    /// Stars are triggers. The player's contact sampling includes triggers, so a trigger still raises the
    /// pickup event, while a solid collider would physically stop the character instead of being
    /// collected. Each placement gets a fresh identity because logical contact deduplication keys on it,
    /// and a recycled star must read as a new object rather than a re-entry into the old one.
    ///
    /// Lane and forward directions come from the player's own configuration and motor basis, so this
    /// works whichever way the track is oriented rather than assuming a world axis.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StarSpawner : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField]
        [Tooltip("Player the stars are placed ahead of. Serialized as the concrete facade because " +
                 "interface-typed fields are never serialized.")]
        private PlayerControllerFacade player;

        [Header("Placement")]
        [SerializeField]
        [Tooltip("How many stars are kept alive ahead of the player at once.")]
        private int activeStars = 12;

        [SerializeField]
        [Tooltip("Distance along the run between one star and the next.")]
        private float spacing = 22f;

        [SerializeField]
        [Tooltip("How far ahead of the player the first star sits.")]
        private float leadDistance = 40f;

        [SerializeField]
        [Tooltip("Height above the run surface, so a star meets the capsule rather than the feet.")]
        private float heightAboveRun = 0.9f;

        [SerializeField]
        [Tooltip("Radius of the placeholder sphere.")]
        private float starRadius = 0.45f;

        [Header("Value")]
        [SerializeField]
        [Tooltip("Value reported on the pickup event. The pace controller turns this into braking.")]
        private float starValue = 1f;

        [SerializeField]
        [Tooltip("Stars per placement position. One per lane makes braking easy; leave at 1 so lane " +
                 "choice matters.")]
        private int starsPerRow = 1;

        private readonly List<Transform> pool = new List<Transform>();
        private readonly List<float> placedAt = new List<float>();
        private Material starMaterial;
        private int nextIdentity;
        private float nextPlacementDistance;

        private void Start()
        {
            if (player == null)
            {
                Debug.LogWarning("StarSpawner has no player assigned, so no stars will appear.", this);
                enabled = false;
                return;
            }

            nextPlacementDistance = ForwardDistance() + leadDistance;
            for (var index = 0; index < activeStars; index++) Place(CreateStar());
        }

        private void Update()
        {
            if (player == null) return;

            var progress = ForwardDistance();
            for (var index = 0; index < pool.Count; index++)
            {
                var star = pool[index];
                if (star == null) continue;

                // The bridge deactivates a collected pickup, so an inactive star is a collected one.
                // Anything the player has already run past is recycled too.
                if (!star.gameObject.activeSelf || placedAt[index] < progress - spacing) Place(index);
            }
        }

        private Transform CreateStar()
        {
            var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            star.name = "Star";
            star.transform.localScale = Vector3.one * (starRadius * 2f);
            star.transform.SetParent(transform, true);

            var collider = star.GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;

            if (starMaterial == null)
            {
                var renderer = star.GetComponent<Renderer>();
                if (renderer != null)
                {
                    starMaterial = new Material(renderer.sharedMaterial);
                    starMaterial.name = "StarPlaceholder";
                    starMaterial.color = new Color(1f, 0.85f, 0.15f);
                }
            }

            var starRenderer = star.GetComponent<Renderer>();
            if (starRenderer != null && starMaterial != null) starRenderer.sharedMaterial = starMaterial;

            star.AddComponent<MvpEnvironmentObject>();

            pool.Add(star.transform);
            placedAt.Add(float.NegativeInfinity);
            return star.transform;
        }

        private void Place(Transform star)
        {
            Place(pool.IndexOf(star));
        }

        private void Place(int index)
        {
            if (index < 0 || index >= pool.Count) return;

            var star = pool[index];
            if (star == null) return;

            var motor = player.GetComponent<CharacterControllerMotor>();
            var basis = motor == null ? Quaternion.identity : motor.TrackBasis;
            var forward = basis * Vector3.forward;
            var lateral = basis * Vector3.right;

            var laneCenters = player.EffectiveConfiguration.LaneCenters;
            var lanes = new[] { laneCenters.x, laneCenters.y, laneCenters.z };
            var lane = lanes[Random.Range(0, starsPerRow > 1 ? 3 : 3)];

            var distance = nextPlacementDistance;
            nextPlacementDistance += spacing;

            star.position = forward * distance +
                            lateral * lane +
                            Vector3.up * (player.transform.position.y + heightAboveRun);

            // A recycled star is a new object as far as contact tracking is concerned, so it gets a new
            // identity rather than reusing the one that was just collected.
            var environmentObject = star.GetComponent<MvpEnvironmentObject>();
            if (environmentObject != null)
            {
                environmentObject.Configure(
                    "star-" + nextIdentity.ToString(CultureInfo.InvariantCulture),
                    EnvironmentObjectKind.Coin,
                    starValue);
                nextIdentity++;
            }

            placedAt[index] = distance;
            star.gameObject.SetActive(true);
        }

        private float ForwardDistance()
        {
            var motor = player.GetComponent<CharacterControllerMotor>();
            var basis = motor == null ? Quaternion.identity : motor.TrackBasis;
            return Vector3.Dot(player.transform.position, basis * Vector3.forward);
        }
    }
}
