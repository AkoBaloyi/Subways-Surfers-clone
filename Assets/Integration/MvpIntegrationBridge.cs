using System.Collections.Generic;
using System.Globalization;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Integration
{
    /// <summary>
    /// Wires the three systems together for the MVP.
    ///
    /// This lives outside Assets/Player on purpose. The player controller is built to compile and run
    /// with zero references to the game manager or the environment scripts, and its tests assert exactly
    /// that. Putting the coupling here keeps that guarantee intact: the player still knows nothing about
    /// GameManager, and GameManager still knows nothing about the player. This bridge is the only thing
    /// that knows both.
    ///
    /// It also repairs two integration gaps at runtime rather than requiring edits to environment assets:
    /// ground colliders get a running-surface marker so the player can be grounded, and tagged obstacles
    /// and coins get an environment identity so contacts raise events. Both are additive and neither
    /// mutates the objects beyond adding a marker component.
    ///
    /// Drop one of these into the gameplay scene. Everything else self-resolves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MvpIntegrationBridge : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Left empty, the bridge finds the player in the scene.")]
        [SerializeField] private PlayerControllerFacade player;

        [Header("Failure")]
        [Tooltip("When enabled, hitting an obstacle ends the run. The player never decides this itself.")]
        [SerializeField] private bool hitEndsRun = true;

        [Header("Speed")]
        [Tooltip("When enabled, the player's forward speed follows GameManager.gameSpeed, so the " +
                 "existing speed ramp drives the player.")]
        [SerializeField] private bool driveSpeedFromGameManager = true;

        [Header("Spawn")]
        [Tooltip("Places the player on the named rail marker at run time, at a height measured against " +
                 "the surface below it. Guarantees the start pose regardless of what the scene has " +
                 "saved, and regardless of anything that moved the player during load.")]
        [SerializeField] private bool snapToLaneOnStart = true;

        [Tooltip("Marker whose position defines the starting lane and distance along the track.")]
        [SerializeField] private string startMarkerName = "MiddleRailMarker";

        [Tooltip("Gap left between the capsule's feet and the measured surface, so the capsule never " +
                 "starts intersecting geometry and cannot be depenetrated sideways off the lanes.")]
        [SerializeField] private float spawnClearance = 0.15f;

        [Header("Runtime marker repair")]
        [Tooltip("Adds a running-surface marker to non-trigger colliders on the configured ground " +
                 "layer, so the player can be grounded on environment geometry that predates the marker.")]
        [SerializeField] private bool markGroundAsRunningSurface = true;

        [Tooltip("Colliders with this tag are given an Obstacle identity so contacts raise hit events.")]
        [SerializeField] private string obstacleTag = "Obstacle";

        [Tooltip("Colliders with this tag are given a Coin identity so contacts raise coin events.")]
        [SerializeField] private string coinTag = "Coin";

        [Tooltip("Value reported for each collected coin.")]
        [SerializeField] private float coinValue = 1f;

        [Header("Diagnostics")]
        [Tooltip("Logs what the bridge resolved and repaired. Leave on until the MVP is stable.")]
        [SerializeField] private bool logSummary = true;

        private readonly HashSet<string> handledResetIds = new HashSet<string>();
        private bool subscribed;
        private int resetCounter;
        private int identityCounter;
        private float lastAppliedSpeed = -1f;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerControllerFacade>();
        }

        private void Start()
        {
            if (player == null)
            {
                Debug.LogError("MvpIntegrationBridge found no PlayerControllerFacade in the scene. " +
                               "Instantiate Assets/Player/Prefabs/Player.prefab.", this);
                return;
            }

            var surfaces = 0;
            var obstacles = 0;
            var coins = 0;

            // Marking has to happen before the snap measures a surface, because the surface the player
            // will stand on must already be a valid running surface for grounding to take afterwards.
            if (markGroundAsRunningSurface) surfaces = MarkGroundColliders();
            if (snapToLaneOnStart) SnapToStartLane();
            obstacles = MarkTagged(obstacleTag, EnvironmentObjectKind.Obstacle, 0f);
            coins = MarkTagged(coinTag, EnvironmentObjectKind.Coin, coinValue);

            var cameras = WireCameraFollow();

            Subscribe();

            if (logSummary)
            {
                Debug.Log(string.Format(
                    "MvpIntegrationBridge ready. Running surfaces marked: {0}. Obstacles marked: {1}. " +
                    "Coins marked: {2}. Cameras wired: {3}. GameManager present: {4}. " +
                    "Player grounded: {5}.",
                    surfaces, obstacles, coins, cameras,
                    GameManager.Instance != null, player.IsGrounded), this);

                if (surfaces == 0)
                {
                    Debug.LogWarning("No ground colliders were marked as running surfaces. The player " +
                                     "cannot jump or slide unless it is standing on a collider that is " +
                                     "both on the configured ground layer and marked IRunningSurface.", this);
                }
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (player == null) return;

            var manager = GameManager.Instance;
            if (manager == null) return;

            // Rachel's speed ramp is the single source of pace. Pushing it through the validated speed
            // API keeps the player's rejection rules in play rather than writing the field directly.
            if (driveSpeedFromGameManager &&
                manager.currentState == GameManager.GameState.Playing &&
                !Mathf.Approximately(manager.gameSpeed, lastAppliedSpeed))
            {
                var result = player.SetForwardSpeed(manager.gameSpeed);
                if (result.Status == CommandStatus.Accepted)
                {
                    lastAppliedSpeed = manager.gameSpeed;
                }
                else if (logSummary)
                {
                    Debug.LogWarning("Player rejected gameSpeed " + manager.gameSpeed +
                                     ": " + result.Reason, this);
                }
            }

            // Entering Playing from any other state restarts the run, so the player returns to its
            // start pose. Reset ids must be unique, so each one is minted fresh.
            if (manager.currentState == GameManager.GameState.Playing &&
                player.CurrentState == PlayerState.Failed)
            {
                RequestFreshReset();
            }
        }

        private void Subscribe()
        {
            if (subscribed || player == null || player.Events == null) return;

            player.Events.PlayerHit += OnPlayerHit;
            player.Events.CoinCollected += OnCoinCollected;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || player == null || player.Events == null) return;

            player.Events.PlayerHit -= OnPlayerHit;
            player.Events.CoinCollected -= OnCoinCollected;
            subscribed = false;
        }

        /// <summary>
        /// A hit does not fail the player by itself; that decision belongs to the run coordinator. This
        /// is where the MVP makes it fatal, and it is the one place to change for shields or revives.
        /// </summary>
        private void OnPlayerHit(PlayerHitEvent hit)
        {
            if (!hitEndsRun) return;

            var failure = player.RequestFailure();
            if (logSummary)
            {
                Debug.Log("Hit " + hit.EnvironmentObjectId + " -> failure " + failure.Status, this);
            }

            var manager = GameManager.Instance;
            if (manager != null && manager.currentState == GameManager.GameState.Playing)
            {
                manager.ChangeState(GameManager.GameState.GameOver);
            }
        }

        /// <summary>
        /// The player reports the coin and its value and never touches the score or the coin object.
        /// Applying the value and removing the pickup are both owned here.
        /// </summary>
        private void OnCoinCollected(CoinCollectedEvent coin)
        {
            var manager = GameManager.Instance;
            if (manager != null) manager.coins += Mathf.RoundToInt(coin.CollectibleValue);

            var collected = coin.Coin as Component;
            if (collected != null) collected.gameObject.SetActive(false);
        }

        private void RequestFreshReset()
        {
            resetCounter++;
            var id = "mvp-reset-" + resetCounter;
            if (!handledResetIds.Add(id)) return;

            var result = player.RequestReset(id);
            if (logSummary && result.Status != CommandStatus.Accepted)
            {
                Debug.LogWarning("Reset " + id + " rejected: " + result.Reason, this);
            }
        }

        /// <summary>
        /// Places the player on the starting lane marker, at a height measured against the surface below.
        ///
        /// The saved scene pose has proven unreliable: a capsule that starts inside the train geometry is
        /// depenetrated sideways off the lanes, and any stale saved position survives until someone moves
        /// it again by hand. Deciding the spawn at run time removes both, so pressing Play always begins
        /// on the middle lane whatever the scene happens to hold.
        ///
        /// The controller is suspended for the write so the move is a placement rather than something the
        /// capsule tries to resolve as a collision. The player's own reset start pose was captured during
        /// its Awake, which ran before this, so the reset service is told to treat this as the new origin
        /// where it can; otherwise a later reset would return to the old pose.
        /// </summary>
        private void SnapToStartLane()
        {
            var marker = FindByName(startMarkerName);
            if (marker == null)
            {
                if (logSummary)
                {
                    Debug.LogWarning("Spawn marker '" + startMarkerName + "' not found, so the player " +
                                     "keeps its scene pose.", this);
                }
                return;
            }

            var reference = marker.transform.position;
            var target = reference;

            RaycastHit surface;
            var probe = new Vector3(reference.x, reference.y + 20f, reference.z);
            if (Physics.Raycast(probe, Vector3.down, out surface, 60f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                target = new Vector3(reference.x, surface.point.y + spawnClearance, reference.z);
            }

            var controller = player.GetComponent<CharacterController>();
            var wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;

            var before = player.transform.position;
            player.transform.position = target;

            if (controller != null) controller.enabled = wasEnabled;

            if (logSummary)
            {
                Debug.Log("Player spawn snapped from " + before + " to " + target +
                          " using marker '" + marker.name + "'" +
                          (Mathf.Abs(before.z - target.z) > 0.5f
                              ? ". The saved scene pose was off-lane, which is what produced the " +
                                "sideways drift."
                              : "."), this);
            }
        }

        private static GameObject FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() == name.Trim()) return t.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Wires every camera follow in the scene to the player.
        ///
        /// The follow adapter only builds its convergence state when the facade initializes it as a
        /// configuration consumer, or when something supplies the player directly. A camera that lives
        /// outside the player prefab is not in the facade's consumer list, so it would sit unresolved
        /// and never move. Calling Configure here is the supported programmatic route and takes
        /// precedence over the serialized reference.
        ///
        /// The follow offset is derived as configured initial camera pose minus the player's pose at
        /// this moment, so the configuration's initialCameraPosition must be an absolute world pose
        /// near the player's start, not a relative offset.
        /// </summary>
        private int WireCameraFollow()
        {
            var follows = FindObjectsByType<PlayerCameraFollow>(FindObjectsInactive.Include);
            var wired = 0;

            foreach (var follow in follows)
            {
                follow.Configure(player, player.EffectiveConfiguration);
                if (!follow.PlayerResolved) continue;

                wired++;
                if (!logSummary) continue;

                var offset = follow.CameraFollowOffset;
                Debug.Log("Camera '" + follow.gameObject.name + "' follow offset = " + offset +
                          ". If that is not roughly the framing you want, adjust " +
                          "initialCameraPosition in the player configuration: it is an absolute " +
                          "world pose, and the offset is that pose minus the player's start position.",
                    follow);
            }

            return wired;
        }

        private int MarkGroundColliders()
        {
            var mask = player.EffectiveConfiguration.GroundLayerMask;
            var marked = 0;

            foreach (var collider in FindObjectsByType<Collider>(FindObjectsInactive.Include))
            {
                if (collider.isTrigger) continue;
                if ((mask & (1 << collider.gameObject.layer)) == 0) continue;
                if (collider.GetComponent<IRunningSurface>() != null) continue;
                if (collider.GetComponentInParent<PlayerControllerFacade>() != null) continue;

                collider.gameObject.AddComponent<MvpRunningSurface>();
                marked++;
            }

            return marked;
        }

        private int MarkTagged(string tag, EnvironmentObjectKind kind, float value)
        {
            if (string.IsNullOrEmpty(tag)) return 0;

            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag(tag);
            }
            catch (UnityException)
            {
                // An undefined tag is not an error worth stopping for: the scene simply has not adopted
                // it yet, and the bridge reports zero rather than throwing during startup.
                if (logSummary) Debug.LogWarning("Tag '" + tag + "' is not defined in this project.", this);
                return 0;
            }

            var marked = 0;
            foreach (var candidate in tagged)
            {
                if (candidate.GetComponent<IEnvironmentObject>() != null) continue;

                // A monotonic counter rather than an engine instance id: it is stable for the object's
                // lifetime, unique across both kinds, and avoids depending on an engine identity API.
                // Each tagged object is marked once, so one object cannot receive two identities.
                identityCounter++;
                var identity = candidate.AddComponent<MvpEnvironmentObject>();
                identity.Configure(
                    kind + "-" + identityCounter.ToString(CultureInfo.InvariantCulture), kind, value);
                marked++;
            }

            return marked;
        }
    }

    /// <summary>
    /// Runtime running-surface marker. Added by the bridge to existing ground geometry so the player can
    /// be grounded without editing environment assets. Long term the environment system should carry
    /// <see cref="IRunningSurface"/> on its own prefabs.
    /// </summary>
    public sealed class MvpRunningSurface : MonoBehaviour, IRunningSurface { }

    /// <summary>
    /// Runtime environment identity. Added by the bridge to tagged obstacles and coins so contacts raise
    /// player events. The identity is derived from the instance id, which is stable for the lifetime of
    /// the object. A multi-collider object should carry this on its parent so every collider shares one
    /// identity, which is what keeps contact deduplication correct.
    /// </summary>
    public sealed class MvpEnvironmentObject : MonoBehaviour, IEnvironmentObject
    {
        [SerializeField] private string environmentObjectId;
        [SerializeField] private EnvironmentObjectKind kind;
        [SerializeField] private float collectibleValue;

        public string EnvironmentObjectId { get { return environmentObjectId; } }
        public EnvironmentObjectKind Kind { get { return kind; } }
        public float CollectibleValue { get { return collectibleValue; } }

        public void Configure(string id, EnvironmentObjectKind objectKind, float value)
        {
            environmentObjectId = id;
            kind = objectKind;
            collectibleValue = value;
        }
    }
}
