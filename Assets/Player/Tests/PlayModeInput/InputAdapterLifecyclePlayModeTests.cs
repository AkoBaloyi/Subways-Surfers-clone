using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode coverage for the Unity input adapter that turns installed action callbacks into
    /// discrete player commands.
    ///
    /// The installed action asset at <c>Assets/InputSystem_Actions.inputactions</c> is treated as
    /// strictly read-only: its JSON is read from disk and deserialized into a throwaway in-memory
    /// <see cref="InputActionAsset"/> through <see cref="InputActionAsset.FromJson"/>, so the fixture
    /// never opens, imports, edits, or re-serializes the project asset. The fixture then resolves
    /// <c>Player/Move</c>, <c>Player/Jump</c>, and <c>Player/Crouch</c> from that copy exactly as the
    /// adapter must resolve them from the reference it is given.
    ///
    /// Input is real input. The tests derive from <see cref="InputTestFixture"/>, which severs the
    /// input system from the machine running the test and installs an isolated instance, and they
    /// actuate simulated devices added to that instance. Because these are plain <c>[Test]</c> methods
    /// rather than <c>[UnityTest]</c> coroutines, every <c>Set</c>, <c>Press</c>, and <c>Release</c>
    /// processes its event immediately instead of waiting for a player-loop update, so a callback and
    /// the command it issues are observable within the actuating call and no frame boundary hides an
    /// asynchronously deferred request.
    ///
    /// The facts pinned here are the ones a naive callback wiring gets wrong. Subscriptions are
    /// symmetric: a disabled adapter receives nothing even while the actions themselves stay enabled,
    /// and re-enabling restores exactly one subscription per action rather than stacking a second one.
    /// One lane request belongs to one actuation-threshold crossing, so a held axis that never returns
    /// to the neutral band cannot repeat, and a value between the bands neither requests nor rearms.
    /// Results are synchronous, so the value a public command returned to the callback is observable
    /// immediately - including a rejection. A missing action disables only its own binding and reports
    /// a diagnostic that names it, while the public command surface stays fully available.
    ///
    /// Commands are observed through a player-owned <see cref="IPlayerCommands"/> recorder, which is
    /// the only route the adapter is allowed to take, and diagnostics through the existing
    /// <see cref="PlayerEventHub"/>. Every fixture object is built programmatically and destroyed in
    /// teardown.
    /// </summary>
    public sealed class InputAdapterLifecyclePlayModeTests : InputTestFixture
    {
        private const string InstalledActionsAssetPath = "InputSystem_Actions.inputactions";
        private const string PlayerActionMapName = "Player";

        /// <summary>
        /// A neutral band well clear of a mid-band value and an actuation threshold well below a fully
        /// actuated axis, so the three bands are unambiguous for both digital and analog controls.
        /// </summary>
        private static readonly InputThresholds Thresholds = new InputThresholds(0.25f, 0.75f);

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<ScriptableObject> createdAssets = new List<ScriptableObject>();
        private readonly List<ValidationDiagnostic> diagnostics = new List<ValidationDiagnostic>();

        private RecordingPlayerCommands commands;
        private PlayerEventHub eventHub;
        private Keyboard keyboard;
        private Gamepad gamepad;

        public override void Setup()
        {
            base.Setup();

            diagnostics.Clear();
            commands = new RecordingPlayerCommands();
            eventHub = new PlayerEventHub();
            eventHub.ValidationReported += diagnostics.Add;

            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
        }

        public override void TearDown()
        {
            ClearFixtures();
            commands = null;
            eventHub = null;
            keyboard = null;
            gamepad = null;

            base.TearDown();
        }

        // **Validates: Requirements 3.2, 4.3, 5.1, 15.1, 15.3**
        [Test]
        [Description("Feature: player-controller, Play Mode: installed Player actions resolve and subscriptions are symmetric across OnEnable and OnDisable")]
        public void ResolvesInstalledActionsAndKeepsSubscriptionsSymmetric_Requirements_3_2_4_3_5_1_15_1_And_15_3()
        {
            var actions = LoadInstalledPlayerActions();
            var seam = AttachAdapter(actions);

            Assert.That(seam.ActiveSubscriptionCount, Is.EqualTo(3),
                "Enabling the adapter must subscribe exactly one handler to each of Player/Move, " +
                "Player/Jump, and Player/Crouch. " + Describe(seam));
            Assert.That(seam.MoveBindingEnabled && seam.JumpBindingEnabled && seam.CrouchBindingEnabled,
                Is.True,
                "Every action resolved from the installed asset must stay enabled. " + Describe(seam));
            Assert.That(diagnostics, Is.Empty,
                "Fully resolved installed actions must not report a diagnostic. " + Describe(seam));

            PressAndRelease(keyboard.dKey);
            Assert.That(commands.LaneDirections, Is.EqualTo(new[] { LaneDirection.Right }),
                "One right actuation must issue exactly one right lane request. " + Describe(seam));

            // A disabled adapter must receive nothing because it unsubscribed, not merely because the
            // actions went quiet, so the actions are held enabled across the disabled window.
            seam.SetEnabled(false);
            Assert.That(seam.ActiveSubscriptionCount, Is.EqualTo(0),
                "Disabling the adapter must remove every subscription it added. " + Describe(seam));

            EnableResolvedActions(actions);
            PressAndRelease(keyboard.dKey);
            PressAndRelease(keyboard.spaceKey);
            PressAndRelease(keyboard.cKey);
            Assert.That(commands.Calls.Count, Is.EqualTo(1),
                "A disabled adapter must issue no command even while the installed actions are " +
                "enabled and actuated. " + Describe(seam));

            seam.SetEnabled(true);
            Assert.That(seam.ActiveSubscriptionCount, Is.EqualTo(3),
                "Re-enabling the adapter must restore exactly one subscription per action. " +
                Describe(seam));

            EnableResolvedActions(actions);
            PressAndRelease(keyboard.dKey);
            Assert.That(commands.LaneDirections,
                Is.EqualTo(new[] { LaneDirection.Right, LaneDirection.Right }),
                "A re-enabled adapter must issue one request per actuation rather than one per " +
                "accumulated subscription. " + Describe(seam));
            Assert.That(diagnostics, Is.Empty,
                "Enable and disable cycles must not report diagnostics. " + Describe(seam));
        }

        // **Validates: Requirements 3.2, 11.8, 15.3**
        [Test]
        [Description("Feature: player-controller, Play Mode: each actuation-threshold crossing issues exactly one lane request")]
        public void IssuesOneLaneRequestPerActuationThresholdCrossing_Requirements_3_2_11_8_And_15_3()
        {
            var actions = LoadInstalledPlayerActions();
            var seam = AttachAdapter(actions);
            var move = ResolveAction(actions, "Move");

            for (var crossing = 0; crossing < 3; crossing++)
            {
                Press(keyboard.dKey);
                AssertAxis(move, Is.GreaterThanOrEqualTo(Thresholds.ActuationThreshold),
                    "a held right key must actuate the axis", seam);
                Release(keyboard.dKey);
                AssertAxis(move, Is.EqualTo(0f).Within(0.0001f),
                    "a released key must return the axis to neutral", seam);
            }

            Assert.That(commands.LaneDirections,
                Is.EqualTo(new[] { LaneDirection.Right, LaneDirection.Right, LaneDirection.Right }),
                "Three separate positive crossings must issue three right lane requests. " +
                Describe(seam));

            Press(keyboard.aKey);
            AssertAxis(move, Is.LessThanOrEqualTo(-Thresholds.ActuationThreshold),
                "a held left key must actuate the axis negatively", seam);
            Release(keyboard.aKey);

            Assert.That(commands.LaneDirections, Is.EqualTo(new[]
                {
                    LaneDirection.Right, LaneDirection.Right, LaneDirection.Right, LaneDirection.Left
                }),
                "A negative crossing must issue exactly one left lane request. " + Describe(seam));
            Assert.That(commands.Kinds.TrueForAll(kind => kind == PlayerCommandKind.Lane), Is.True,
                "Axis actuation must issue lane requests only. " + Describe(seam));
            Assert.That(seam.LatchState, Is.EqualTo(InputLatchState.Neutral),
                "A released axis must leave the latch neutral. " + Describe(seam));
        }

        // **Validates: Requirements 3.2, 11.8**
        [Test]
        [Description("Feature: player-controller, Play Mode: a held axis repeats only after returning to the neutral band")]
        public void RequiresNeutralBandReturnBeforeHeldAxisRepeats_Requirements_3_2_And_11_8()
        {
            var actions = LoadInstalledPlayerActions();
            var seam = AttachAdapter(actions);
            var move = ResolveAction(actions, "Move");

            // The analog stick supplies the value between the bands that a digital key cannot.
            Set(gamepad.leftStick, new Vector2(1f, 0f));
            AssertAxis(move, Is.GreaterThanOrEqualTo(Thresholds.ActuationThreshold),
                "a fully deflected stick must reach the actuation band", seam);
            Assert.That(commands.LaneDirections, Is.EqualTo(new[] { LaneDirection.Right }),
                "Crossing into the positive actuation band must issue one right lane request. " +
                Describe(seam));
            Assert.That(seam.LatchState, Is.EqualTo(InputLatchState.Right),
                "An actuated positive axis must latch right. " + Describe(seam));

            // Between the bands: not actuated enough to request, not neutral enough to rearm.
            Set(gamepad.leftStick, new Vector2(0.5f, 0f));
            AssertAxis(move,
                Is.GreaterThan(Thresholds.NeutralThreshold)
                    .And.LessThan(Thresholds.ActuationThreshold),
                "a half-deflected stick must read between the neutral and actuation bands", seam);
            Assert.That(commands.LaneDirections, Is.EqualTo(new[] { LaneDirection.Right }),
                "A value between the bands must not issue a request. " + Describe(seam));
            Assert.That(seam.LatchState, Is.EqualTo(InputLatchState.Right),
                "A value above the neutral band must not rearm the latch. " + Describe(seam));

            Set(gamepad.leftStick, new Vector2(1f, 0f));
            Assert.That(commands.LaneDirections, Is.EqualTo(new[] { LaneDirection.Right }),
                "A held axis that never returned to the neutral band must not repeat. " +
                Describe(seam));

            Set(gamepad.leftStick, Vector2.zero);
            AssertAxis(move, Is.EqualTo(0f).Within(0.0001f),
                "a centered stick must read neutral", seam);
            Assert.That(seam.LatchState, Is.EqualTo(InputLatchState.Neutral),
                "Returning to the neutral band must rearm the latch. " + Describe(seam));
            Assert.That(commands.LaneDirections, Is.EqualTo(new[] { LaneDirection.Right }),
                "Returning to neutral must not issue a request. " + Describe(seam));

            Set(gamepad.leftStick, new Vector2(1f, 0f));
            Assert.That(commands.LaneDirections,
                Is.EqualTo(new[] { LaneDirection.Right, LaneDirection.Right }),
                "A rearmed axis crossing the actuation threshold again must issue one further " +
                "right lane request. " + Describe(seam));
        }

        // **Validates: Requirements 4.3, 5.1, 11.8**
        [Test]
        [Description("Feature: player-controller, Play Mode: one jump and one slide request per button actuation")]
        public void IssuesExactlyOneJumpAndSlideRequestPerActuation_Requirements_4_3_5_1_And_11_8()
        {
            var actions = LoadInstalledPlayerActions();
            var seam = AttachAdapter(actions);

            Press(keyboard.spaceKey);
            Assert.That(commands.Kinds, Is.EqualTo(new[] { PlayerCommandKind.Jump }),
                "One jump actuation must issue exactly one jump request. " + Describe(seam));

            // Holding the key produces no further actuation, so it must produce no further request.
            Set(keyboard.spaceKey, 1f);
            Assert.That(commands.Kinds, Is.EqualTo(new[] { PlayerCommandKind.Jump }),
                "A held jump button must not repeat its request. " + Describe(seam));
            Release(keyboard.spaceKey);
            Assert.That(commands.Kinds, Is.EqualTo(new[] { PlayerCommandKind.Jump }),
                "Releasing the jump button must not issue a request. " + Describe(seam));

            Press(keyboard.cKey);
            Assert.That(commands.Kinds,
                Is.EqualTo(new[] { PlayerCommandKind.Jump, PlayerCommandKind.Slide }),
                "One crouch actuation must issue exactly one slide request. " + Describe(seam));
            Release(keyboard.cKey);

            PressAndRelease(keyboard.spaceKey);
            Assert.That(commands.Kinds, Is.EqualTo(new[]
                {
                    PlayerCommandKind.Jump, PlayerCommandKind.Slide, PlayerCommandKind.Jump
                }),
                "A second jump actuation must issue exactly one further jump request. " +
                Describe(seam));
            Assert.That(diagnostics, Is.Empty,
                "Resolved button actions must not report diagnostics. " + Describe(seam));
        }

        // **Validates: Requirements 11.9, 15.3**
        [Test]
        [Description("Feature: player-controller, Play Mode: input callbacks expose the synchronous command result")]
        public void ExposesSynchronousCommandResultsToInputCallbacks_Requirements_11_9_And_15_3()
        {
            var actions = LoadInstalledPlayerActions();
            var seam = AttachAdapter(actions);

            var frameBeforeActuation = Time.frameCount;
            PressAndRelease(keyboard.dKey);

            Assert.That(Time.frameCount, Is.EqualTo(frameBeforeActuation),
                "The actuation must be processed without crossing a frame boundary, otherwise the " +
                "result cannot be attributed to the callback. " + Describe(seam));
            Assert.That(commands.Calls.Count, Is.EqualTo(1),
                "The actuation must reach the public command surface. " + Describe(seam));
            Assert.That(seam.LastRequestResult, Is.EqualTo(commands.LastReturnedResult),
                "The callback must expose the result the public command returned to it. " +
                Describe(seam));
            Assert.That(seam.LastRequestResult.Status, Is.EqualTo(CommandStatus.Accepted),
                "An accepted request must be exposed as accepted. " + Describe(seam));
            Assert.That(seam.LastRequestResult.Command, Is.EqualTo(PlayerCommandKind.Lane),
                "The exposed result must describe the command the callback issued. " + Describe(seam));

            // A rejection is a result, not a failure: the adapter reports it without retrying.
            commands.RejectWith(RejectionReason.NotGrounded, PlayerState.Jumping);
            PressAndRelease(keyboard.spaceKey);

            Assert.That(commands.Calls.Count, Is.EqualTo(2),
                "A rejected request must still be one request. " + Describe(seam));
            Assert.That(seam.LastRequestResult, Is.EqualTo(commands.LastReturnedResult),
                "The callback must expose a rejected result synchronously. " + Describe(seam));
            Assert.That(seam.LastRequestResult.Status, Is.EqualTo(CommandStatus.Rejected),
                "A rejected request must be exposed as rejected. " + Describe(seam));
            Assert.That(seam.LastRequestResult.Reason, Is.EqualTo(RejectionReason.NotGrounded),
                "The exposed rejection must carry the reason the command reported. " +
                Describe(seam));
            Assert.That(seam.LastRequestResult.CurrentState, Is.EqualTo(PlayerState.Jumping),
                "The exposed result must carry the state the command reported. " + Describe(seam));
        }

        // **Validates: Requirements 11.9, 15.1, 15.3**
        [Test]
        [Description("Feature: player-controller, Play Mode: a missing action disables only its own binding and keeps public commands available")]
        public void DisablesOnlyMissingBindingsAndPreservesPublicCommands_Requirements_11_9_15_1_And_15_3()
        {
            var actions = CreateActionsWithoutCrouch();
            var seam = AttachAdapter(actions);

            Assert.That(seam.CrouchBindingEnabled, Is.False,
                "An unresolved Player/Crouch must disable its own binding. " + Describe(seam));
            Assert.That(seam.MoveBindingEnabled, Is.True,
                "A missing action must not disable the resolved Player/Move binding. " +
                Describe(seam));
            Assert.That(seam.JumpBindingEnabled, Is.True,
                "A missing action must not disable the resolved Player/Jump binding. " +
                Describe(seam));
            Assert.That(seam.ActiveSubscriptionCount, Is.EqualTo(2),
                "Only the resolved actions may be subscribed. " + Describe(seam));

            Assert.That(diagnostics.Count, Is.EqualTo(1),
                "A single missing action must report exactly one diagnostic. " + Describe(seam));
            Assert.That(diagnostics[0].Code, Is.EqualTo(DiagnosticCode.MissingInputAction),
                "A missing action must be reported as a missing input action. " + Describe(seam));
            Assert.That(diagnostics[0].Severity,
                Is.EqualTo(DiagnosticSeverity.Warning).Or.EqualTo(DiagnosticSeverity.Error),
                "A missing action must be reported as a warning or an error. " + Describe(seam));
            Assert.That(diagnostics[0].Field, Does.Contain("Crouch"),
                "The diagnostic must name the missing action so the affected binding is " +
                "identifiable. " + Describe(seam));

            // The surviving bindings keep working.
            PressAndRelease(keyboard.dKey);
            PressAndRelease(keyboard.spaceKey);
            Assert.That(commands.Kinds,
                Is.EqualTo(new[] { PlayerCommandKind.Lane, PlayerCommandKind.Jump }),
                "Resolved bindings must keep issuing their requests while another action is " +
                "missing. " + Describe(seam));

            // And the public command surface stays available to test controls and integrations.
            var directSlide = ((IPlayerCommands)commands).RequestSlide();
            Assert.That(directSlide.Status, Is.EqualTo(CommandStatus.Accepted),
                "A missing input action must not withdraw the public command surface. " +
                Describe(seam));
            Assert.That(commands.Kinds, Is.EqualTo(new[]
                {
                    PlayerCommandKind.Lane, PlayerCommandKind.Jump, PlayerCommandKind.Slide
                }),
                "The public command surface must serve callers directly while a binding is " +
                "disabled. " + Describe(seam));
            Assert.That(diagnostics.Count, Is.EqualTo(1),
                "Using the surviving surface must not report further diagnostics. " + Describe(seam));
        }

        private void AssertAxis(
            InputAction move,
            NUnit.Framework.Constraints.IResolveConstraint constraint,
            string expectation,
            InputAdapterSeam seam)
        {
            var axis = move.ReadValue<Vector2>().x;
            Assert.That(axis, constraint,
                "Fixture precondition: " + expectation + ". " +
                string.Format(CultureInfo.InvariantCulture, "[axis={0}, neutral={1}, actuation={2}] ",
                    axis.ToString("R", CultureInfo.InvariantCulture),
                    Thresholds.NeutralThreshold.ToString("R", CultureInfo.InvariantCulture),
                    Thresholds.ActuationThreshold.ToString("R", CultureInfo.InvariantCulture)) +
                Describe(seam));
        }

        private string Describe(InputAdapterSeam seam)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[subscriptions={0}, move={1}, jump={2}, crouch={3}, latch={4}, calls={5}, " +
                "diagnostics={6}]",
                seam == null ? -1 : seam.ActiveSubscriptionCount,
                seam == null ? "unknown" : seam.MoveBindingEnabled.ToString(),
                seam == null ? "unknown" : seam.JumpBindingEnabled.ToString(),
                seam == null ? "unknown" : seam.CrouchBindingEnabled.ToString(),
                seam == null ? "unknown" : seam.LatchState.ToString(),
                commands == null ? "none" : string.Join("+", commands.Descriptions),
                diagnostics.Count);
        }

        /// <summary>
        /// The installed action asset, read from disk as text and deserialized into a throwaway
        /// in-memory asset. The project file is never opened for writing, imported, or re-serialized.
        /// </summary>
        private InputActionAsset LoadInstalledPlayerActions()
        {
            var path = Path.Combine(Application.dataPath, InstalledActionsAssetPath);
            Assert.That(File.Exists(path), Is.True,
                "The installed action asset must exist at Assets/" + InstalledActionsAssetPath + ".");

            var actions = InputActionAsset.FromJson(File.ReadAllText(path));
            Assert.That(actions, Is.Not.Null,
                "The installed action asset must deserialize into an InputActionAsset.");
            createdAssets.Add(actions);

            var map = actions.FindActionMap(PlayerActionMapName);
            Assert.That(map, Is.Not.Null,
                "The installed asset must contain the " + PlayerActionMapName + " action map.");
            ResolveAction(actions, "Move");
            ResolveAction(actions, "Jump");
            ResolveAction(actions, "Crouch");
            return actions;
        }

        /// <summary>
        /// A player-owned action asset whose Player map deliberately omits Crouch, built in memory so
        /// the missing-action path is exercised without touching the installed asset.
        /// </summary>
        private InputActionAsset CreateActionsWithoutCrouch()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            actions.name = "PlayerActionsWithoutCrouch";
            createdAssets.Add(actions);

            var map = actions.AddActionMap(PlayerActionMapName);
            var move = map.AddAction(
                "Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            return actions;
        }

        private static InputAction ResolveAction(InputActionAsset actions, string actionName)
        {
            var map = actions.FindActionMap(PlayerActionMapName);
            Assert.That(map, Is.Not.Null,
                "The asset must contain the " + PlayerActionMapName + " action map.");

            var action = map.FindAction(actionName);
            Assert.That(action, Is.Not.Null,
                PlayerActionMapName + "/" + actionName + " must resolve from the asset.");
            return action;
        }

        /// <summary>
        /// Enables the Player actions the adapter consumes, so a disabled adapter can be proven silent
        /// while its actions are live rather than merely switched off.
        /// </summary>
        private static void EnableResolvedActions(InputActionAsset actions)
        {
            var map = actions.FindActionMap(PlayerActionMapName);
            if (map == null) return;

            EnableIfPresent(map, "Move");
            EnableIfPresent(map, "Jump");
            EnableIfPresent(map, "Crouch");
        }

        private static void EnableIfPresent(InputActionMap map, string actionName)
        {
            var action = map.FindAction(actionName);
            if (action != null && !action.enabled) action.Enable();
        }

        private InputAdapterSeam AttachAdapter(InputActionAsset actions)
        {
            var player = new GameObject("InputAdapterFixture");
            spawnedObjects.Add(player);
            player.SetActive(false);

            var seam = InputAdapterSeam.Attach(player, commands, actions, Thresholds, eventHub);
            player.SetActive(true);
            return seam;
        }

        private void ClearFixtures()
        {
            for (var index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                if (spawnedObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(spawnedObjects[index]);
            }

            spawnedObjects.Clear();

            for (var index = createdAssets.Count - 1; index >= 0; index--)
            {
                if (createdAssets[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdAssets[index]);
            }

            createdAssets.Clear();
        }

        /// <summary>
        /// The only command route the adapter is allowed to use. Every call is recorded in arrival
        /// order with the result it returned, so both "exactly one request per actuation" and the
        /// synchronously exposed result are observed from the public surface.
        /// </summary>
        private sealed class RecordingPlayerCommands : IPlayerCommands
        {
            private readonly List<PlayerCommandKind> kinds = new List<PlayerCommandKind>();
            private readonly List<LaneDirection> laneDirections = new List<LaneDirection>();
            private readonly List<string> descriptions = new List<string>();

            private CommandStatus status = CommandStatus.Accepted;
            private RejectionReason reason = RejectionReason.None;
            private PlayerState state = PlayerState.Running;

            public List<PlayerCommandKind> Kinds { get { return kinds; } }
            public List<LaneDirection> LaneDirections { get { return laneDirections; } }
            public List<string> Descriptions { get { return descriptions; } }
            public List<PlayerCommandKind> Calls { get { return kinds; } }
            public ActionRequestResult LastReturnedResult { get; private set; }

            public void RejectWith(RejectionReason rejectionReason, PlayerState currentState)
            {
                status = CommandStatus.Rejected;
                reason = rejectionReason;
                state = currentState;
            }

            public ActionRequestResult RequestLane(LaneDirection direction)
            {
                laneDirections.Add(direction);
                return Record(PlayerCommandKind.Lane, "Lane:" + direction);
            }

            public ActionRequestResult RequestJump()
            {
                return Record(PlayerCommandKind.Jump, "Jump");
            }

            public ActionRequestResult RequestSlide()
            {
                return Record(PlayerCommandKind.Slide, "Slide");
            }

            public FailureCommandResult RequestFailure()
            {
                kinds.Add(PlayerCommandKind.Failure);
                descriptions.Add("Failure");
                return new FailureCommandResult(status, reason, state);
            }

            public ResetRequestResult RequestReset(string requestId)
            {
                kinds.Add(PlayerCommandKind.Reset);
                descriptions.Add("Reset:" + requestId);
                return new ResetRequestResult(status, reason, state, requestId);
            }

            public SpeedSetResult SetForwardSpeed(float requestedSpeed)
            {
                kinds.Add(PlayerCommandKind.SetForwardSpeed);
                descriptions.Add("Speed:" +
                    requestedSpeed.ToString("R", CultureInfo.InvariantCulture));
                return new SpeedSetResult(status, reason, state, requestedSpeed, requestedSpeed);
            }

            private ActionRequestResult Record(PlayerCommandKind kind, string description)
            {
                kinds.Add(kind);
                descriptions.Add(description);
                LastReturnedResult = new ActionRequestResult(status, kind, reason, state);
                return LastReturnedResult;
            }
        }

        /// <summary>
        /// The public surface Task 8.2 has to provide, reached by reflection so this fixture states the
        /// required contract before the adapter exists.
        /// </summary>
        private sealed class InputAdapterSeam
        {
            private const string AdapterTypeName = "SubwaySurfers.Player.PlayerInputAdapter";

            private readonly Component adapter;
            private readonly Behaviour behaviour;
            private readonly PropertyInfo activeSubscriptionCount;
            private readonly PropertyInfo moveBindingEnabled;
            private readonly PropertyInfo jumpBindingEnabled;
            private readonly PropertyInfo crouchBindingEnabled;
            private readonly PropertyInfo latchState;
            private readonly PropertyInfo lastRequestResult;

            private InputAdapterSeam(Component adapter)
            {
                this.adapter = adapter;
                behaviour = adapter as Behaviour;
                Assert.That(behaviour, Is.Not.Null,
                    "Task 8.2 must make " + AdapterTypeName + " a Behaviour so OnEnable and " +
                    "OnDisable own subscription lifetime.");

                var type = adapter.GetType();
                activeSubscriptionCount = RequiredProperty(
                    type, "ActiveSubscriptionCount", typeof(int));
                moveBindingEnabled = RequiredProperty(type, "MoveBindingEnabled", typeof(bool));
                jumpBindingEnabled = RequiredProperty(type, "JumpBindingEnabled", typeof(bool));
                crouchBindingEnabled = RequiredProperty(type, "CrouchBindingEnabled", typeof(bool));
                latchState = RequiredProperty(type, "LatchState", typeof(InputLatchState));
                lastRequestResult = RequiredProperty(
                    type, "LastRequestResult", typeof(ActionRequestResult));
            }

            public int ActiveSubscriptionCount
            {
                get { return (int)activeSubscriptionCount.GetValue(adapter); }
            }

            public bool MoveBindingEnabled { get { return (bool)moveBindingEnabled.GetValue(adapter); } }
            public bool JumpBindingEnabled { get { return (bool)jumpBindingEnabled.GetValue(adapter); } }

            public bool CrouchBindingEnabled
            {
                get { return (bool)crouchBindingEnabled.GetValue(adapter); }
            }

            public InputLatchState LatchState
            {
                get { return (InputLatchState)latchState.GetValue(adapter); }
            }

            public ActionRequestResult LastRequestResult
            {
                get { return (ActionRequestResult)lastRequestResult.GetValue(adapter); }
            }

            public static InputAdapterSeam Attach(
                GameObject player,
                IPlayerCommands commands,
                InputActionAsset actions,
                InputThresholds thresholds,
                PlayerEventHub events)
            {
                var type = typeof(CharacterControllerMotor).Assembly.GetType(AdapterTypeName);
                Assert.That(type, Is.Not.Null, "Task 8.2 must provide " + AdapterTypeName + ".");
                Assert.That(typeof(Component).IsAssignableFrom(type), Is.True,
                    "Task 8.2 must make " + AdapterTypeName + " a player component.");

                var adapter = player.AddComponent(type);
                var configure = RequiredMethod(
                    type,
                    "Configure",
                    typeof(IPlayerCommands),
                    typeof(InputActionAsset),
                    typeof(InputThresholds),
                    typeof(PlayerEventHub));
                configure.Invoke(
                    adapter, new object[] { commands, actions, thresholds, events });
                return new InputAdapterSeam(adapter);
            }

            public void SetEnabled(bool enabled)
            {
                behaviour.enabled = enabled;
            }

            private static PropertyInfo RequiredProperty(Type type, string name, Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null,
                    "Task 8.2 must expose " + type.FullName + "." + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType),
                    name + " must be typed " + propertyType.Name + ".");
                return property;
            }

            private static MethodInfo RequiredMethod(
                Type type,
                string name,
                params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null);
                Assert.That(method, Is.Not.Null,
                    "Task 8.2 must expose " + type.FullName + "." + name + ".");
                return method;
            }
        }
    }
}
