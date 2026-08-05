using SubwaySurfers.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SubwaySurfers.Integration
{
    /// <summary>
    /// Adds W and up arrow for jump, and S and down arrow for slide.
    ///
    /// The shared input action asset is read only for this project, so rather than rebinding it this
    /// reads the keys directly and goes through the player's public commands, exactly as the player's own
    /// input adapter does. The player cannot tell the difference between a jump asked for here and one
    /// asked for through a binding, which is the point of having commands rather than an input coupling.
    ///
    /// Presses are edge triggered, so holding a key does not queue a stream of requests.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExtraKeyboardControls : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The player these keys drive. Serialized as the concrete facade because interface " +
                 "fields are never serialized.")]
        private PlayerControllerFacade player;

        [SerializeField]
        [Tooltip("Log each accepted or refused request, so it is obvious whether a key reached the " +
                 "player at all.")]
        private bool logRequests;

        private void Update()
        {
            if (player == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Only act while the run is actually going, so keys pressed on a menu do nothing.
            var manager = GameManager.Instance;
            if (manager != null && manager.currentState != GameManager.GameState.Playing) return;

            var jumpPressed =
                (keyboard.wKey != null && keyboard.wKey.wasPressedThisFrame) ||
                (keyboard.upArrowKey != null && keyboard.upArrowKey.wasPressedThisFrame);

            if (jumpPressed)
            {
                var result = player.RequestJump();
                if (logRequests) Debug.Log("Jump key: " + result.Status + " (" + result.Reason + ")", this);
            }

            var slidePressed =
                (keyboard.sKey != null && keyboard.sKey.wasPressedThisFrame) ||
                (keyboard.downArrowKey != null && keyboard.downArrowKey.wasPressedThisFrame);

            if (slidePressed)
            {
                var result = player.RequestSlide();
                if (logRequests) Debug.Log("Slide key: " + result.Status + " (" + result.Reason + ")", this);
            }
        }
    }
}
