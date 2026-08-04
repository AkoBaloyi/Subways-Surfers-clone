using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Audio
{
    /// <summary>
    /// Plays player sound effects by listening to the Player Controller's published events.
    ///
    /// This component is a pure listener. It never writes player transform, state, velocity, lanes,
    /// timers, collider, camera, or animation state, and it never mutates an environment object. The
    /// Player Controller does not know it exists: audio depends on the player, never the reverse, so
    /// the player still compiles and runs with no audio present. The event hub also catches subscriber
    /// exceptions at the subscriber boundary, so a fault in here cannot disturb the simulation.
    ///
    /// An interface field cannot be serialized, so the player arrives as its concrete facade component
    /// and is resolved to <see cref="IPlayerEventSource"/> and <see cref="IPlayerQueries"/> on enable,
    /// matching how the animation and camera adapters resolve their references.
    ///
    /// Two sounds cannot come from events and are read from the query surface instead:
    ///
    /// Footsteps have no event, and the source audio is an alternating left/right pair rather than a
    /// loop, so cadence is derived from the player's own forward speed.
    ///
    /// A lane change publishes no state transition, because the player stays in Running throughout, so
    /// the target lane is observed instead. Both are read-only observations of public queries.
    ///
    /// Every clip slot is optional. An unassigned clip is skipped silently, so the scene stays playable
    /// while audio is still being sourced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAudioDirector : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField]
        [Tooltip("The player whose events drive audio. Serialized as the concrete facade because " +
                 "interface-typed fields are never serialized.")]
        private PlayerControllerFacade player;

        [Header("Source")]
        [SerializeField]
        [Tooltip("Source used for one-shot effects. Added automatically when left empty.")]
        private AudioSource effectSource;

        [Header("Movement clips")]
        [SerializeField] [Tooltip("Played once on take-off.")] private AudioClip jump;
        [SerializeField] [Tooltip("Played once on a valid landing.")] private AudioClip land;
        [SerializeField] [Tooltip("Played once when a slide begins.")] private AudioClip slide;
        [SerializeField]
        [Tooltip("Played once when the baseline collider is safely restored after a slide.")]
        private AudioClip standUp;

        [SerializeField]
        [Tooltip("Played once per accepted lane change. A lane change publishes no state transition, " +
                 "so the target lane is observed instead.")]
        private AudioClip laneChange;

        [Header("Footsteps")]
        [SerializeField]
        [Tooltip("Alternating footstep pair, played only while Running and grounded.")]
        private AudioClip footstepLeft;

        [SerializeField] private AudioClip footstepRight;

        [SerializeField]
        [Range(0.1f, 3f)]
        [Tooltip("Footsteps per metre of forward travel. Cadence follows the player's forward speed, " +
                 "so a speed change is audible without any extra wiring.")]
        private float stepsPerMetre = 0.6f;

        [Header("Contact clips")]
        [SerializeField] [Tooltip("Played once per logical obstacle contact.")] private AudioClip hit;
        [SerializeField] [Tooltip("Played once per logical coin contact.")] private AudioClip coin;

        [Header("Run lifecycle clips")]
        [SerializeField] [Tooltip("Played once when the run fails.")] private AudioClip fail;
        [SerializeField] [Tooltip("Played once when a reset completes.")] private AudioClip resetComplete;

        [Header("Levels")]
        [SerializeField] [Range(0f, 1f)] [Tooltip("Scales every one-shot effect.")]
        private float effectVolume = 0.8f;

        [SerializeField] [Range(0f, 1f)] [Tooltip("Scales footsteps, which sit under the action.")]
        private float footstepVolume = 0.4f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Random pitch spread applied to one-shots so repeated sounds do not sound identical.")]
        private float pitchVariation = 0.05f;

        private IPlayerEventSource events;
        private IPlayerQueries queries;
        private LogicalLane observedTargetLane;
        private float distanceSinceLastStep;
        private bool nextStepIsLeft;

        /// <summary>True once the serialized player reference resolved to the player contracts.</summary>
        public bool PlayerResolved { get { return events != null && queries != null; } }

        private void OnEnable()
        {
            effectSource = EnsureSource(effectSource);

            if (player == null) return;

            queries = player;
            observedTargetLane = queries.Snapshot.TargetLane;
            distanceSinceLastStep = 0f;

            events = player.Events;
            if (events == null) return;

            events.StateChanged += OnStateChanged;
            events.PlayerHit += OnPlayerHit;
            events.CoinCollected += OnCoinCollected;
            events.ResetCompleted += OnResetCompleted;
        }

        private void OnDisable()
        {
            if (events != null)
            {
                events.StateChanged -= OnStateChanged;
                events.PlayerHit -= OnPlayerHit;
                events.CoinCollected -= OnCoinCollected;
                events.ResetCompleted -= OnResetCompleted;
                events = null;
            }

            queries = null;
        }

        private void Update()
        {
            if (queries == null) return;

            ObserveLaneChange();
            AdvanceFootsteps(Time.deltaTime);
        }

        /// <summary>
        /// One published transition produces at most one effect. The transition cause carries what
        /// happened, so nothing here has to infer intent from state pairs.
        /// </summary>
        private void OnStateChanged(PlayerStateChangedEvent published)
        {
            switch (published.TransitionCause)
            {
                case PlayerTransitionCause.JumpRequested:
                    PlayOnce(jump, effectVolume);
                    break;
                case PlayerTransitionCause.Landed:
                    PlayOnce(land, effectVolume);
                    break;
                case PlayerTransitionCause.SlideRequested:
                    PlayOnce(slide, effectVolume);
                    break;
                case PlayerTransitionCause.SlideRestored:
                    PlayOnce(standUp, effectVolume);
                    break;
                case PlayerTransitionCause.FailureRequested:
                    PlayOnce(fail, effectVolume);
                    break;
            }

            // Leaving Running interrupts the stride, so the next step starts a fresh interval instead
            // of firing immediately on landing.
            if (published.CurrentState != PlayerState.Running) distanceSinceLastStep = 0f;
        }

        private void OnPlayerHit(PlayerHitEvent published)
        {
            // Reported only. What a hit means for the run belongs to the Game State system, and the
            // contacted object belongs to the Environment system.
            PlayOnce(hit, effectVolume);
        }

        private void OnCoinCollected(CoinCollectedEvent published)
        {
            PlayOnce(coin, effectVolume);
        }

        private void OnResetCompleted(PlayerResetCompletedEvent published)
        {
            PlayOnce(resetComplete, effectVolume);

            // Reset returns the player to the Centre lane, so re-baseline rather than treat that as a
            // lane change the player asked for.
            if (queries != null) observedTargetLane = queries.Snapshot.TargetLane;
            distanceSinceLastStep = 0f;
        }

        /// <summary>
        /// The target lane changes only when a lane request is accepted and begins, so a change of
        /// target is exactly one accepted lane movement. Boundary requests leave the target unchanged
        /// and correctly stay silent.
        /// </summary>
        private void ObserveLaneChange()
        {
            var target = queries.Snapshot.TargetLane;
            if (target == observedTargetLane) return;

            observedTargetLane = target;
            PlayOnce(laneChange, effectVolume);
        }

        /// <summary>
        /// Footsteps only while Running and grounded: airborne and sliding produce no stride, and the
        /// interval follows forward speed so faster running steps faster.
        /// </summary>
        private void AdvanceFootsteps(float elapsed)
        {
            if (footstepLeft == null && footstepRight == null) return;
            if (elapsed <= 0f || !IsFinite(elapsed)) return;
            if (queries.CurrentState != PlayerState.Running || !queries.IsGrounded)
            {
                distanceSinceLastStep = 0f;
                return;
            }

            var speed = queries.ForwardSpeed;
            if (!IsFinite(speed) || speed <= 0f) return;

            var metresPerStep = 1f / stepsPerMetre;
            distanceSinceLastStep += speed * elapsed;
            if (distanceSinceLastStep < metresPerStep) return;

            distanceSinceLastStep -= metresPerStep;
            var step = nextStepIsLeft ? footstepLeft : footstepRight;
            nextStepIsLeft = !nextStepIsLeft;
            PlayOnce(step ?? footstepLeft ?? footstepRight, footstepVolume);
        }

        private void PlayOnce(AudioClip clip, float volume)
        {
            if (clip == null || effectSource == null) return;

            effectSource.pitch = pitchVariation <= 0f
                ? 1f
                : 1f + Random.Range(-pitchVariation, pitchVariation);
            effectSource.PlayOneShot(clip, volume);
        }

        private AudioSource EnsureSource(AudioSource existing)
        {
            if (existing != null) return existing;

            var created = gameObject.AddComponent<AudioSource>();
            created.playOnAwake = false;
            created.loop = false;
            created.spatialBlend = 0f;
            return created;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
