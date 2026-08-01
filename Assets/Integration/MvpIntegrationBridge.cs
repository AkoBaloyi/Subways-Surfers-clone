using System.Collections.Generic;
using System.Globalization;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Validation;
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

        [Tooltip("Seconds to wait after a run ends before restarting. Zero leaves the player stopped, " +
                 "which is right once a game manager owns the retry flow. A positive value keeps a " +
                 "playtest moving while no game manager is in the scene.")]
        [SerializeField] private float restartDelay = 2f;

        [Header("Slide visibility")]
        [Tooltip("Adds the validation slide visual to the player so a slide is visible. The controller " +
                 "shrinks the collider, which is invisible on its own, and a real build would drive the " +
                 "slide through an animator instead.")]
        [SerializeField] private bool addSlideVisual = true;

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

        [Header("Run surface")]
        [Tooltip("Creates a flat running surface at a fixed height that follows the player along the " +
                 "track. The player then runs at a constant height instead of depending on whatever " +
                 "geometry happens to have a collider, and trains and obstacles are free to be " +
                 "obstacles rather than things to stand on.")]
        [SerializeField] private bool useRunSurface = true;

        [Tooltip("Height of the run surface. Left at zero, the start marker's own height is used, so " +
                 "the player runs at rail level.")]
        [SerializeField] private float runHeight;

        [Tooltip("Width of the run surface across the lanes. Must cover every lane with margin.")]
        [SerializeField] private float runSurfaceWidth = 14f;

        [Tooltip("Length of the run surface along the track. It follows the player, so this only needs " +
                 "to cover the distance travelled between frames plus a comfortable margin.")]
        [SerializeField] private float runSurfaceLength = 400f;

        [Header("Runtime marker repair")]
        [Tooltip("Adds a running-surface marker to non-trigger colliders on the configured ground " +
                 "layer. Only needed when no run surface is used; with a run surface this would also " +
                 "make trains and obstacles standable, which stops them acting as obstacles.")]
        [SerializeField] private bool markGroundAsRunningSurface;

        [Tooltip("Seconds between marking passes over geometry near the player. The track spawns new " +
                 "segments as the player advances and those clones carry no running-surface marker, so " +
                 "a single pass at startup leaves the player running onto unmarked ground.")]
        [SerializeField] private float markRefreshInterval = 0.4f;

        [Tooltip("Radius around the player searched for unmarked ground on each refresh. Large enough " +
                 "to cover track spawned ahead, small enough to stay cheap.")]
        [SerializeField] private float markRefreshRadius = 60f;

        [Tooltip("Colliders with this tag are given an Obstacle identity so contacts raise hit events.")]
        [SerializeField] private string obstacleTag = "Obstacle";

        [Tooltip("Colliders with this tag are given a Coin identity so contacts raise coin events.")]
        [SerializeField] private string coinTag = "Coin";

        [Tooltip("Name fragments identifying obstacles, since the scene does not use tags for them. " +
                 "Matched case-insensitively against the collider's object name.")]
        [SerializeField]
        private string[] obstacleNameFragments = { "obstacle", "train", "barricade", "barrier" };

        [Tooltip("Name fragments identifying collectibles, matched the same way.")]
        [SerializeField]
        private string[] coinNameFragments = { "coin", "point", "star" };

        [Tooltip("Name fragments identifying geometry the player must slide under. These receive an " +
                 "obstruction marker, which is what keeps the player down while the space above is " +
                 "blocked, and an obstacle identity as well, so running into one while upright ends " +
                 "the run.")]
        [SerializeField]
        private string[] obstructionNameFragments = { "slide", "overhead", "ceiling", "lowbar" };

        [Tooltip("Value reported for each collected coin.")]
        [SerializeField] private float coinValue = 1f;

        [Header("Diagnostics")]
        [Tooltip("Logs what the bridge resolved and repaired. Leave on until the MVP is stable.")]
        [SerializeField] private bool logSummary = true;

        private readonly HashSet<string> handledResetIds = new HashSet<string>();

        /// <summary>Reused buffer so the repeating marking pass allocates nothing per refresh.</summary>
        private readonly Collider[] nearbyGround = new Collider[256];

        /// <summary>Thickness of the synthetic run surface. Deep enough that a capsule cannot tunnel it.</summary>
        private const float RunSurfaceThickness = 2f;

        private bool subscribed;
        private int resetCounter;
        private int identityCounter;
        private float lastAppliedSpeed = -1f;
        private float nextMarkRefreshTime;
        private int totalSurfacesMarked;
        private Transform runSurface;
        private float resolvedRunHeight;

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

            // The surface the player stands on has to exist and be marked before the spawn measures a
            // height, otherwise the probe finds nothing and the player starts above everything.
            if (useRunSurface) surfaces = CreateRunSurface();
            else if (markGroundAsRunningSurface) surfaces = MarkGroundColliders();
            if (snapToLaneOnStart) SnapToStartLane();
            obstacles = MarkTagged(obstacleTag, EnvironmentObjectKind.Obstacle, 0f);
            coins = MarkTagged(coinTag, EnvironmentObjectKind.Coin, coinValue);

            var cameras = WireCameraFollow();
            EnsureSlideVisual();

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

            // The run surface follows the player so it never runs out, which keeps the run height
            // constant no matter what geometry the track spawns underneath.
            KeepRunSurfaceUnderPlayer();

            // The track spawns segments continuously, so obstacles and collectibles the player has not
            // reached yet do not exist at startup and need identities before they can raise events.
            if (Time.time >= nextMarkRefreshTime)
            {
                nextMarkRefreshTime = Time.time + Mathf.Max(0.05f, markRefreshInterval);

                var identities = MarkEnvironmentNearPlayer();
                var added = markGroundAsRunningSurface ? MarkGroundNearPlayer() : 0;

                if ((identities > 0 || added > 0) && logSummary)
                {
                    totalSurfacesMarked += added;
                    Debug.Log("Marked " + identities + " new environment objects" +
                              (added > 0 ? " and " + added + " running surfaces" : "") +
                              " near the player.", this);
                }
            }

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
                Debug.Log("RUN ENDED: hit '" + hit.EnvironmentObjectId + "' at " + hit.ContactPosition +
                          ", failure command " + failure.Status +
                          ". Player state is now " + player.CurrentState + ".", this);
            }

            var manager = GameManager.Instance;
            if (manager != null && manager.currentState == GameManager.GameState.Playing)
            {
                manager.ChangeState(GameManager.GameState.GameOver);
            }
            else if (restartDelay > 0f)
            {
                // No game manager owns the retry flow yet, so the bridge restarts the run itself rather
                // than leaving a playtest stuck on a stopped player. Reset is the player's own supported
                // route back to its start state, and each attempt needs a fresh identifier.
                StartCoroutine(RestartAfterDelay());
            }
        }

        private System.Collections.IEnumerator RestartAfterDelay()
        {
            yield return new WaitForSeconds(restartDelay);

            if (player == null || player.CurrentState != PlayerState.Failed) yield break;

            RequestFreshReset();
            if (snapToLaneOnStart) SnapToStartLane();
            if (logSummary) Debug.Log("Run restarted after failure.", this);
        }

        /// <summary>
        /// Adds the validation slide visual so a slide can be seen. The controller shrinks the collider
        /// on slide entry, which is invisible by itself: a jump reads clearly because the transform moves,
        /// while a slide changes only capsule dimensions. A production build should drive the slide through
        /// an animator via the animation receiver instead of squashing the visual.
        /// </summary>
        private void EnsureSlideVisual()
        {
            if (!addSlideVisual) return;
            if (player.GetComponentInChildren<ValidationSlideVisual>(true) != null) return;

            player.gameObject.AddComponent<ValidationSlideVisual>();
            if (logSummary)
            {
                Debug.Log("Slide visual added to the player, so sliding is visible. " +
                          "Replace it with an animator-driven slide for a real build.", this);
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

            // The marker gives the lane and the distance along the track, but not a standable height: the
            // rail is a thin visual and the nearest collider is the ground well below it. Probing from
            // just above the marker downward finds whatever surface actually exists there, whether that
            // is a rail collider or the ground beneath. Probing from far above instead hits train roofs,
            // and taking the marker height alone leaves the player above every collider.
            //
            // Height accuracy matters more than it looks: Running applies a small settling displacement
            // rather than gravity, so a player left above the surface descends slowly forever instead of
            // falling and landing, never becomes grounded, and can never jump or slide.
            var reference = marker.transform.position;
            var target = new Vector3(reference.x, reference.y + spawnClearance, reference.z);

            RaycastHit surface;
            var probe = new Vector3(reference.x, reference.y + 0.5f, reference.z);
            if (Physics.Raycast(probe, Vector3.down, out surface, 30f,
                    player.EffectiveConfiguration.GroundLayerMask, QueryTriggerInteraction.Ignore))
            {
                target = new Vector3(reference.x, surface.point.y + spawnClearance, reference.z);
                if (logSummary)
                {
                    Debug.Log("Standable surface under the start lane is '" +
                              surface.collider.gameObject.name + "' at y=" +
                              surface.point.y.ToString("F3") + ", marker sits at y=" +
                              reference.y.ToString("F3") + ". Spawning on the surface, not the marker.",
                        this);
                }
            }
            else if (logSummary)
            {
                Debug.LogWarning("No collider found beneath the start lane within 30 units, so the " +
                                 "player will start at marker height and has nothing to stand on.", this);
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
                          " using marker '" + marker.name + "' on '" +
                          (marker.transform.parent == null ? "(no parent)" : marker.transform.parent.name) +
                          "'. Marker height is the rail surface, so the feet start " + spawnClearance +
                          " above it.", this);
            }
        }

        /// <summary>
        /// Finds the authored object of this name, preferring one that is not part of a runtime clone.
        ///
        /// The track manager spawns several segments before this runs, so by the time the bridge starts
        /// there are many objects sharing a marker name, scattered along the track wherever segments were
        /// placed or recycled. Taking the first match placed the player against a clone hundreds of units
        /// away. The authored segment is the stable reference, so clones are only a last resort.
        /// </summary>
        private static GameObject FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            GameObject clone = null;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() != name.Trim()) continue;

                if (IsRuntimeClone(t))
                {
                    if (clone == null) clone = t.gameObject;
                    continue;
                }

                return t.gameObject;
            }

            return clone;
        }

        private static bool IsRuntimeClone(Transform candidate)
        {
            for (var t = candidate; t != null; t = t.parent)
            {
                if (t.gameObject.name.EndsWith("(Clone)", System.StringComparison.Ordinal)) return true;
            }
            return false;
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

        /// <summary>
        /// Creates one flat running surface at a fixed height, spanning the lanes and following the player
        /// along the track.
        ///
        /// This replaces marking arbitrary environment geometry as standable. That approach made the run
        /// height depend on whatever happened to carry a collider, which is why the player descended when
        /// the rail turned out to be a thin visual with the real ground two units below. It also made
        /// trains standable, which stops them from being obstacles.
        ///
        /// With a dedicated surface the run height is constant and deliberate, grounding resolves every
        /// frame, and jump and slide behave normally: the jump leaves this surface and lands back on it.
        /// </summary>
        private int CreateRunSurface()
        {
            if (runSurface != null) return 1;

            var height = runHeight;
            if (Mathf.Approximately(height, 0f))
            {
                var marker = FindByName(startMarkerName);
                height = marker == null ? player.transform.position.y : marker.transform.position.y;
            }
            resolvedRunHeight = height;

            var host = new GameObject("MvpRunSurface");
            host.layer = FirstLayerInMask(player.EffectiveConfiguration.GroundLayerMask);

            var box = host.AddComponent<BoxCollider>();
            box.size = new Vector3(runSurfaceLength, RunSurfaceThickness, runSurfaceWidth);
            host.AddComponent<MvpRunningSurface>();

            // The top face sits at the run height, so a capsule whose feet are at that height stands on
            // it exactly rather than sinking to the middle of a slab.
            runSurface = host.transform;
            runSurface.position = new Vector3(
                player.transform.position.x,
                height - RunSurfaceThickness * 0.5f,
                MiddleLaneWorldZ());

            if (logSummary)
            {
                Debug.Log("Run surface created at height " + height.ToString("F3") +
                          ", " + runSurfaceWidth + " wide across the lanes and " + runSurfaceLength +
                          " long, following the player. Trains and obstacles are no longer marked as " +
                          "standable, so they can act as obstacles.", this);
            }

            return 1;
        }

        private void KeepRunSurfaceUnderPlayer()
        {
            if (runSurface == null) return;

            runSurface.position = new Vector3(
                player.transform.position.x,
                resolvedRunHeight - RunSurfaceThickness * 0.5f,
                runSurface.position.z);
        }

        /// <summary>
        /// The world Z of the middle lane. Domain lateral maps to world through the motor's basis, so the
        /// centre lane's configured lateral value is converted rather than assumed.
        /// </summary>
        private float MiddleLaneWorldZ()
        {
            var motor = player.GetComponent<CharacterControllerMotor>();
            var lateral = new Vector3(player.EffectiveConfiguration.LaneCenters.y, 0f, 0f);
            var world = motor == null ? lateral : motor.TrackBasis * lateral;
            return world.z;
        }

        private static int FirstLayerInMask(int mask)
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((mask & (1 << layer)) != 0) return layer;
            }
            return 0;
        }

        /// <summary>
        /// Gives nearby trains, obstacles, and collectibles an environment identity so contacts raise
        /// events. The scene does not tag them, so names are matched as well as tags. Anything already
        /// identified, anything standable, and the player's own hierarchy are skipped, so the run surface
        /// can never be mistaken for an obstacle.
        /// </summary>
        private int MarkEnvironmentNearPlayer()
        {
            var count = Physics.OverlapSphereNonAlloc(
                player.transform.position, markRefreshRadius, nearbyGround, ~0,
                QueryTriggerInteraction.Collide);

            var marked = 0;
            for (var index = 0; index < count; index++)
            {
                var collider = nearbyGround[index];
                if (collider == null) continue;
                if (collider.GetComponentInParent<PlayerControllerFacade>() != null) continue;
                if (collider.GetComponent<IRunningSurface>() != null) continue;

                var name = collider.gameObject.name;
                var isObstruction = MatchesAny(name, obstructionNameFragments);

                // Obstruction and identity are independent concerns, so each is applied if missing. A low
                // bar needs both: the obstruction marker keeps the player down while it is overhead, and
                // the obstacle identity ends the run if the player meets it standing up.
                if (isObstruction && collider.GetComponent<IEnvironmentObstruction>() == null)
                {
                    collider.gameObject.AddComponent<MvpEnvironmentObstruction>();
                    marked++;
                }

                if (collider.GetComponent<IEnvironmentObject>() != null) continue;

                var kind = EnvironmentObjectKind.Obstacle;
                var value = 0f;

                if (MatchesAny(name, coinNameFragments) || IsTagged(collider, coinTag))
                {
                    kind = EnvironmentObjectKind.Coin;
                    value = coinValue;
                }
                else if (!isObstruction &&
                         !MatchesAny(name, obstacleNameFragments) &&
                         !IsTagged(collider, obstacleTag))
                {
                    continue;
                }

                identityCounter++;
                var identity = collider.gameObject.AddComponent<MvpEnvironmentObject>();
                identity.Configure(
                    kind + "-" + identityCounter.ToString(CultureInfo.InvariantCulture), kind, value);
                marked++;
            }

            return marked;
        }

        private static bool MatchesAny(string name, string[] fragments)
        {
            if (fragments == null) return false;

            foreach (var fragment in fragments)
            {
                if (string.IsNullOrEmpty(fragment)) continue;
                if (name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool IsTagged(Collider collider, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;

            try
            {
                return collider.gameObject.CompareTag(tag);
            }
            catch (UnityException)
            {
                return false;
            }
        }

        /// <summary>
        /// Marks unmarked ground within reach of the player, using a non-allocating overlap so it can run
        /// several times a second without churn. Scanning near the player rather than the whole scene
        /// keeps the cost proportional to what the player can actually stand on next.
        /// </summary>
        private int MarkGroundNearPlayer()
        {
            var mask = player.EffectiveConfiguration.GroundLayerMask;
            var count = Physics.OverlapSphereNonAlloc(
                player.transform.position, markRefreshRadius, nearbyGround, mask,
                QueryTriggerInteraction.Ignore);

            var marked = 0;
            for (var index = 0; index < count; index++)
            {
                var collider = nearbyGround[index];
                if (collider == null || collider.isTrigger) continue;
                if (collider.GetComponent<IRunningSurface>() != null) continue;
                if (collider.GetComponentInParent<PlayerControllerFacade>() != null) continue;

                collider.gameObject.AddComponent<MvpRunningSurface>();
                marked++;
            }

            return marked;
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
    /// Runtime obstruction marker, added by the bridge to geometry the player must slide under.
    ///
    /// Safe collider restoration only refuses to stand the player up when something implementing
    /// <see cref="IEnvironmentObstruction"/> overlaps the standing capsule. Without this marker the
    /// restoration query always succeeds, so the player pops upright underneath low geometry instead of
    /// staying down until it has passed. Long term the environment system should carry this on its own
    /// prefabs rather than having it applied at run time.
    /// </summary>
    public sealed class MvpEnvironmentObstruction : MonoBehaviour, IEnvironmentObstruction { }

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
