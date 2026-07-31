using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Validation-only visual feedback for the slide.
    ///
    /// The controller shrinks the <see cref="CharacterController"/> capsule when a slide begins, but the
    /// capsule is invisible and the character mesh is never touched by movement code. A jump is visible
    /// because it moves the transform; a slide is not, so a working slide can look like nothing happened.
    /// This squashes the visual child while the player is Sliding purely so a human playtester can see
    /// the state they are in.
    ///
    /// This is presentation only and holds no movement authority. It reads the player's state and writes
    /// nothing back, so removing it changes exactly nothing about movement, collision, timing, or events.
    /// It is not production art and is not a substitute for a real animated slide: a production build
    /// should drive the slide through <c>IAnimationReceiver</c> and an Animator instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ValidationSlideVisual : MonoBehaviour, INonProductionValidationDouble
    {
        [Tooltip("The player whose state drives the squash. Left empty, the component searches its own " +
                 "parents during Awake.")]
        [SerializeField] private PlayerControllerFacade player;

        [Tooltip("The transform to squash. Left empty, the component looks for a child named Visual.")]
        [SerializeField] private Transform visual;

        [Tooltip("Vertical scale applied to the visual while Sliding, as a fraction of its authored scale.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float slideHeightFraction = 0.5f;

        private Vector3 baselineScale;
        private Vector3 baselineLocalPosition;
        private bool baselineCaptured;
        private bool squashed;

        public string NonProductionNotice
        {
            get
            {
                return "Validation-only slide visual. Presentation feedback for manual playtesting, " +
                       "with no movement authority. Not production art.";
            }
        }

        /// <summary>True while the visual is currently squashed, for test and inspector observation.</summary>
        public bool Squashed
        {
            get { return squashed; }
        }

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerControllerFacade>();
            if (visual == null) visual = FindVisualChild();
            CaptureBaseline();
        }

        /// <summary>
        /// Runs in <c>LateUpdate</c> so the squash is applied after the movement update has settled the
        /// player's state for the frame, matching where the camera reads the player pose.
        /// </summary>
        private void LateUpdate()
        {
            if (player == null || visual == null) return;
            CaptureBaseline();

            var sliding = player.CurrentState == PlayerState.Sliding;
            if (sliding == squashed) return;

            // Exact assignment in both directions, never an interpolation, so repeated slides cannot
            // accumulate drift in the authored scale.
            if (sliding)
            {
                visual.localScale = new Vector3(
                    baselineScale.x,
                    baselineScale.y * slideHeightFraction,
                    baselineScale.z);
                visual.localPosition = new Vector3(
                    baselineLocalPosition.x,
                    baselineLocalPosition.y - (baselineScale.y - baselineScale.y * slideHeightFraction),
                    baselineLocalPosition.z);
            }
            else
            {
                visual.localScale = baselineScale;
                visual.localPosition = baselineLocalPosition;
            }

            squashed = sliding;
        }

        private void CaptureBaseline()
        {
            if (baselineCaptured || visual == null) return;
            baselineScale = visual.localScale;
            baselineLocalPosition = visual.localPosition;
            baselineCaptured = true;
        }

        private Transform FindVisualChild()
        {
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child.name == "Visual") return child;
            }

            // Fall back to the first child with a renderer, so an differently named mesh still shows the
            // slide rather than silently doing nothing.
            var renderer = GetComponentInChildren<Renderer>();
            return renderer == null ? null : renderer.transform;
        }
    }
}
