using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Unity-side owner of input intent. The adapter references an installed
    /// <see cref="InputActionAsset"/> and resolves <c>Player/Move</c>, <c>Player/Jump</c>, and
    /// <c>Player/Crouch</c> from it. The asset is only ever read: no action, binding, or serialized
    /// field of it is written, so <c>Assets/InputSystem_Actions.inputactions</c> stays exactly as the
    /// project installed it.
    ///
    /// Every intent leaves the adapter through the public <see cref="IPlayerCommands"/> surface, which
    /// is the only route it has. The adapter owns no player state, performs no motion, and never
    /// second-guesses a command: the value a command returns is recorded verbatim in
    /// <see cref="LastRequestResult"/>, including a rejection, and no request is retried.
    ///
    /// Lane intent is a crossing, not a level. An axis magnitude at or above the actuation threshold
    /// issues exactly one lane request and latches that direction; the latch clears only when the axis
    /// returns inside the neutral band. A value between the two bands is deliberately inert - it
    /// neither issues a request nor rearms the latch - so a held or partially released control cannot
    /// repeat. Buttons are already discrete, so one <c>performed</c> callback is one request.
    ///
    /// Subscription lifetime belongs to <see cref="Behaviour"/> enablement: <c>OnEnable</c> subscribes
    /// and enables the resolved actions, <c>OnDisable</c> removes exactly what it added. Subscribing is
    /// idempotent, so an enable/disable cycle restores one subscription per action instead of stacking
    /// a second one. Callback delegates are cached once, so a steady stream of callbacks allocates
    /// nothing.
    ///
    /// An action that does not resolve disables only its own binding and reports one
    /// <see cref="DiagnosticCode.MissingInputAction"/> diagnostic naming it. The public command
    /// surface stays fully available, so test controls and integrations keep working while a binding
    /// is missing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputAdapter : MonoBehaviour, IPlayerConfigurationConsumer
    {
        private const string PlayerActionMapName = "Player";
        private const string MoveActionName = "Move";
        private const string JumpActionName = "Jump";
        private const string CrouchActionName = "Crouch";

        // Cached once so no callback registration or invocation allocates a delegate.
        private System.Action<InputAction.CallbackContext> moveCallback;
        private System.Action<InputAction.CallbackContext> jumpCallback;
        private System.Action<InputAction.CallbackContext> crouchCallback;

        private IPlayerCommands commands;
        private InputActionAsset inputActions;
        private InputThresholds thresholds;
        private PlayerEventHub events;

        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction crouchAction;

        private bool moveSubscribed;
        private bool jumpSubscribed;
        private bool crouchSubscribed;
        private bool actionsResolved;

        /// <summary>
        /// Installed actions this adapter is currently subscribed to, one per resolved action. Zero
        /// while the adapter is disabled, because a disabled adapter has removed every handler it
        /// added rather than relying on its actions going quiet.
        /// </summary>
        public int ActiveSubscriptionCount
        {
            get
            {
                return (moveSubscribed ? 1 : 0) + (jumpSubscribed ? 1 : 0) + (crouchSubscribed ? 1 : 0);
            }
        }

        /// <summary>True while <c>Player/Move</c> resolved and its binding is usable.</summary>
        public bool MoveBindingEnabled { get { return ResolvedActions() && moveAction != null; } }

        /// <summary>True while <c>Player/Jump</c> resolved and its binding is usable.</summary>
        public bool JumpBindingEnabled { get { return ResolvedActions() && jumpAction != null; } }

        /// <summary>True while <c>Player/Crouch</c> resolved and its binding is usable.</summary>
        public bool CrouchBindingEnabled { get { return ResolvedActions() && crouchAction != null; } }

        /// <summary>
        /// The direction currently latched by an actuated axis, or <see cref="InputLatchState.Neutral"/>
        /// when the axis sits inside the neutral band and a further crossing may request again.
        /// </summary>
        public InputLatchState LatchState { get; private set; }

        /// <summary>
        /// The result the public command surface returned for the most recent request, exposed
        /// synchronously within the callback that issued it. A rejection is a result: it is reported
        /// as received, never retried.
        /// </summary>
        public ActionRequestResult LastRequestResult { get; private set; }

        /// <summary>
        /// Receipt order of the most recent request, assigned monotonically as requests are issued so
        /// arrival order stays reconstructible. Zero before the first request.
        /// </summary>
        public ulong LastReceiptOrder { get; private set; }

        /// <summary>Requests issued through the public command surface since configuration.</summary>
        public int IssuedRequestCount { get; private set; }

        /// <summary>
        /// Binds the public command surface every intent is routed through, the installed action asset
        /// to read actions from, the neutral and actuation bands that define a crossing, and the hub
        /// missing-action diagnostics are published to. Resolution happens here, once per
        /// configuration, so an enable/disable cycle never republishes a diagnostic.
        /// </summary>
        public void Configure(
            IPlayerCommands commands,
            InputActionAsset inputActions,
            InputThresholds thresholds,
            PlayerEventHub events)
        {
            Unsubscribe();

            this.commands = commands;
            this.inputActions = inputActions;
            this.thresholds = thresholds;
            this.events = events;

            moveAction = null;
            jumpAction = null;
            crouchAction = null;
            actionsResolved = false;
            LatchState = InputLatchState.Neutral;
            LastRequestResult = default(ActionRequestResult);
            LastReceiptOrder = 0;
            IssuedRequestCount = 0;

            ResolveActions();
            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>
        /// Facade-driven configuration: the effective input bands and the effective action asset
        /// reference are taken from validated configuration, and the player's own command surface is
        /// used as the command route. A reference that is not an action asset resolves no actions and
        /// is reported as missing per action, exactly as an absent asset is.
        /// </summary>
        public void Initialize(
            PlayerConfiguration configuration, PlayerConfigurationReferences references)
        {
            Configure(
                commands ?? GetComponent<IPlayerCommands>(),
                references.InputActionAsset as InputActionAsset,
                configuration.InputThresholds,
                events);
        }

        private void OnEnable()
        {
            ResolveActions();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private bool ResolvedActions()
        {
            return actionsResolved;
        }

        private void ResolveActions()
        {
            if (actionsResolved) return;

            actionsResolved = true;
            var map = inputActions == null ? null : inputActions.FindActionMap(PlayerActionMapName);
            moveAction = ResolveAction(map, MoveActionName);
            jumpAction = ResolveAction(map, JumpActionName);
            crouchAction = ResolveAction(map, CrouchActionName);
        }

        private InputAction ResolveAction(InputActionMap map, string actionName)
        {
            var action = map == null ? null : map.FindAction(actionName);
            if (action == null) ReportMissingAction(actionName);
            return action;
        }

        private void ReportMissingAction(string actionName)
        {
            if (events == null) return;

            var field = PlayerActionMapName + "/" + actionName;
            events.PublishValidation(new ValidationDiagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCode.MissingInputAction,
                field,
                "The input action " + field + " did not resolve from the referenced action asset, so " +
                "its binding is disabled. The public command surface remains available."));
        }

        private void Subscribe()
        {
            if (moveAction != null && !moveSubscribed)
            {
                if (moveCallback == null) moveCallback = HandleMove;

                // A Value action reports a return to its default through canceled, which is the
                // callback that rearms the latch, so both callbacks share one handler.
                moveAction.performed += moveCallback;
                moveAction.canceled += moveCallback;
                moveAction.Enable();
                moveSubscribed = true;
            }

            if (jumpAction != null && !jumpSubscribed)
            {
                if (jumpCallback == null) jumpCallback = HandleJump;
                jumpAction.performed += jumpCallback;
                jumpAction.Enable();
                jumpSubscribed = true;
            }

            if (crouchAction != null && !crouchSubscribed)
            {
                if (crouchCallback == null) crouchCallback = HandleCrouch;
                crouchAction.performed += crouchCallback;
                crouchAction.Enable();
                crouchSubscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (moveSubscribed)
            {
                if (moveAction != null)
                {
                    moveAction.performed -= moveCallback;
                    moveAction.canceled -= moveCallback;
                    moveAction.Disable();
                }

                moveSubscribed = false;
            }

            if (jumpSubscribed)
            {
                if (jumpAction != null)
                {
                    jumpAction.performed -= jumpCallback;
                    jumpAction.Disable();
                }

                jumpSubscribed = false;
            }

            if (crouchSubscribed)
            {
                if (crouchAction != null)
                {
                    crouchAction.performed -= crouchCallback;
                    crouchAction.Disable();
                }

                crouchSubscribed = false;
            }
        }

        private void HandleMove(InputAction.CallbackContext context)
        {
            var axis = context.ReadValue<Vector2>().x;
            var magnitude = axis < 0f ? -axis : axis;

            if (magnitude <= thresholds.NeutralThreshold)
            {
                // Inside the neutral band the control is rearmed for the next crossing.
                LatchState = InputLatchState.Neutral;
                return;
            }

            // Between the bands: not actuated enough to request, not neutral enough to rearm.
            if (magnitude < thresholds.ActuationThreshold) return;

            var crossed = axis > 0f ? InputLatchState.Right : InputLatchState.Left;
            if (LatchState == crossed) return;

            LatchState = crossed;
            RecordResult(commands == null
                ? default(ActionRequestResult)
                : commands.RequestLane(
                    crossed == InputLatchState.Right ? LaneDirection.Right : LaneDirection.Left));
        }

        private void HandleJump(InputAction.CallbackContext context)
        {
            RecordResult(commands == null
                ? default(ActionRequestResult)
                : commands.RequestJump());
        }

        private void HandleCrouch(InputAction.CallbackContext context)
        {
            RecordResult(commands == null
                ? default(ActionRequestResult)
                : commands.RequestSlide());
        }

        private void RecordResult(ActionRequestResult result)
        {
            LastRequestResult = result;
            LastReceiptOrder++;
            IssuedRequestCount++;
        }
    }
}
