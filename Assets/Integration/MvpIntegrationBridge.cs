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

            if (markGroundAsRunningSurface) surfaces = MarkGroundColliders();
            obstacles = MarkTagged(obstacleTag, EnvironmentObjectKind.Obstacle, 0f);
            coins = MarkTagged(coinTag, EnvironmentObjectKind.Coin, coinValue);

            Subscribe();

            if (logSummary)
            {
                Debug.Log(string.Format(
                    "MvpIntegrationBridge ready. Running surfaces marked: {0}. Obstacles marked: {1}. " +
                    "Coins marked: {2}. GameManager present: {3}. Player grounded: {4}.",
                    surfaces, obstacles, coins, GameManager.Instance != null, player.IsGrounded), this);

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

        private int MarkGroundColliders()
        {
            var mask = player.EffectiveConfiguration.GroundLayerMask;
            var marked = 0;

            foreach (var collider in FindObjectsByType<Collider>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
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
