using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Validation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Inventory and contract coverage for PlayerTestScene.
    ///
    /// The scene is loaded from its asset path rather than from Build Settings on purpose:
    /// EditorBuildSettings is a shared project setting this feature must not modify, so the scene stays
    /// out of the build list and is opened in Play Mode through the Editor scene API. That keeps the
    /// validation scene entirely inside Assets/Player.
    ///
    /// These assertions describe the scene as an observable fixture: three visible lanes, the geometry
    /// each behaviour needs, identified obstacle and coin stand-ins, a following camera, an animation
    /// receiver, and a harness that issues commands, shows their synchronous results and diagnostics,
    /// and logs every published event in publication order. They also assert what the scene must not
    /// contain, because isolation from the Game State and Environment systems is the property that lets
    /// this feature be validated on its own.
    /// </summary>
    public sealed class PlayerTestScenePlayModeTests
    {
        private const string ScenePath = "Assets/Player/Scenes/PlayerTestScene.unity";
        private const float FrameStep = 0.02f;
        private const float LaneTolerance = 0.05f;

        /// <summary>
        /// Namespace fragments that would indicate a production Game State or Environment system was
        /// embedded in the validation scene. The scene must be drivable with none of them present.
        /// </summary>
        private static readonly string[] ForbiddenProductionNamespaces =
        {
            "GameManager",
            "GamePlay",
            "TrackManager",
            "EnvironmentManager",
            "Segment",
            "Difficulty",
            "Trains"
        };

        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;

        [SetUp]
        public void SetUp()
        {
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            Time.fixedDeltaTime = FrameStep;
            Time.maximumDeltaTime = FrameStep;
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 11.2**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: PlayerTestScene presents three visible lanes at the configured lane centers")]
        public IEnumerator SceneProvidesThreeVisibleLanesAtConfiguredCenters_Requirement_11_2()
        {
            yield return LoadScene();

            var harness = ResolveHarness();
            var laneCenters = harness.Player.EffectiveConfiguration.LaneCenters;

            var strips = FindAll<Renderer>()
                .Where(renderer => renderer.gameObject.name.StartsWith("LaneStrip", StringComparison.Ordinal))
                .OrderBy(renderer => renderer.transform.position.x)
                .ToArray();

            Assert.That(strips.Length, Is.EqualTo(3),
                "The scene must present exactly three visible lane strips so lane position is " +
                "observable without instrumentation. Found: " + Describe(strips));

            foreach (var strip in strips)
            {
                Assert.That(strip.enabled, Is.True,
                    "Lane strip " + strip.gameObject.name + " must be visible.");
                Assert.That(strip.gameObject.activeInHierarchy, Is.True,
                    "Lane strip " + strip.gameObject.name + " must be active.");
            }

            var expected = new[] { laneCenters.x, laneCenters.y, laneCenters.z };
            for (var index = 0; index < 3; index++)
            {
                Assert.That(strips[index].transform.position.x,
                    Is.EqualTo(expected[index]).Within(LaneTolerance),
                    "Lane strip " + index + " must sit on its configured lane center so the " +
                    "displayed lane matches the lane the controller navigates. Configured centers " +
                    laneCenters.ToString("R", CultureInfo.InvariantCulture) + ", found " +
                    Describe(strips));
            }
        }

        // **Validates: Requirements 11.3, 11.4, 11.5**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: PlayerTestScene carries running surface, jump obstacle, slide obstruction, and blocked-restoration fixtures")]
        public IEnumerator SceneProvidesJumpSlideAndBlockedRestorationFixtures_Requirements_11_3_11_4_11_5()
        {
            yield return LoadScene();

            var surfaces = FindAll<ValidationRunningSurface>();
            Assert.That(surfaces.Length, Is.GreaterThanOrEqualTo(1),
                "The scene must provide at least one marked running surface, because grounding " +
                "requires both the ground layer and a resolved running-surface marker.");

            var groundMask = ResolveHarness().Player.EffectiveConfiguration.GroundLayerMask;
            foreach (var surface in surfaces)
            {
                var collider = surface.GetComponent<Collider>();
                Assert.That(collider, Is.Not.Null,
                    "Running surface " + surface.name + " must carry a collider to stand on.");
                Assert.That(collider.isTrigger, Is.False,
                    "Running surface " + surface.name + " must not be a trigger; a trigger can " +
                    "never ground the player.");
                Assert.That(groundMask & (1 << surface.gameObject.layer), Is.Not.Zero,
                    "Running surface " + surface.name + " must sit on the configured ground layer.");
            }

            var obstructions = FindAll<ValidationEnvironmentObstruction>();
            Assert.That(obstructions.Length, Is.GreaterThanOrEqualTo(1),
                "The scene must provide at least one obstruction so blocked slide restoration is " +
                "reachable. Safe restoration refuses to restore the baseline capsule while an " +
                "obstruction overlaps the baseline volume.");

            foreach (var obstruction in obstructions)
            {
                var collider = obstruction.GetComponent<Collider>();
                Assert.That(collider, Is.Not.Null,
                    "Obstruction " + obstruction.name + " must carry a collider.");
                Assert.That(collider.isTrigger, Is.False,
                    "Obstruction " + obstruction.name + " must not be a trigger; the overlap query " +
                    "that guards restoration ignores triggers.");
            }

            Assert.That(
                obstructions.Any(obstruction =>
                    obstruction.gameObject.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.True,
                "One obstruction must be an overhead ceiling fixture, which is the scenario that " +
                "keeps the player sliding after the timer expires. Found: " + Describe(obstructions));

            Assert.That(
                FindAll<Collider>().Any(collider =>
                    collider.gameObject.name.IndexOf("JumpObstacle", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.True,
                "The scene must carry a jump obstacle so clearing an obstacle by jumping is " +
                "observable.");
        }

        // **Validates: Requirements 11.6, 11.13**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: PlayerTestScene carries identified obstacle and coin stand-ins marked non-production")]
        public IEnumerator SceneProvidesIdentifiedObstacleAndCoinDoubles_Requirements_11_6_11_13()
        {
            yield return LoadScene();

            var objects = FindAll<ValidationEnvironmentObject>();
            Assert.That(objects.Length, Is.GreaterThanOrEqualTo(2),
                "The scene must provide at least one obstacle and one coin stand-in. Found: " +
                Describe(objects));

            var obstacles = objects.Where(item => item.Kind == EnvironmentObjectKind.Obstacle).ToArray();
            var coins = objects.Where(item => item.Kind == EnvironmentObjectKind.Coin).ToArray();

            Assert.That(obstacles.Length, Is.GreaterThanOrEqualTo(1),
                "The scene must carry at least one Obstacle-kind object so the hit event is " +
                "reachable. Found: " + Describe(objects));
            Assert.That(coins.Length, Is.GreaterThanOrEqualTo(1),
                "The scene must carry at least one Coin-kind object so the coin event is reachable. " +
                "Found: " + Describe(objects));

            var identities = new List<string>();
            foreach (var item in objects)
            {
                Assert.That(item.EnvironmentObjectId, Is.Not.Null.And.Not.Empty,
                    "Environment stand-in " + item.name + " must carry a non-empty identity; a " +
                    "missing identity is diagnosed and the contact is ignored.");
                Assert.That(identities, Does.Not.Contain(item.EnvironmentObjectId),
                    "Environment identities must be unique across distinct objects, because logical " +
                    "contact deduplication keys on them. Duplicated: " + item.EnvironmentObjectId);
                identities.Add(item.EnvironmentObjectId);

                Assert.That(item.GetComponentInChildren<Collider>(true), Is.Not.Null,
                    "Environment stand-in " + item.name + " must carry at least one collider.");
                Assert.That(item.NonProductionNotice, Is.Not.Null.And.Not.Empty,
                    "Every validation stand-in must declare that it is not production content.");
            }

            foreach (var coin in coins)
            {
                Assert.That(float.IsNaN(coin.CollectibleValue) || float.IsInfinity(coin.CollectibleValue),
                    Is.False,
                    "Coin " + coin.name + " must report a finite collectible value.");
            }
        }

        // **Validates: Requirements 11.1, 11.7**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: PlayerTestScene wires a configured player initialized at the Center lane, a following camera, and an animation receiver")]
        public IEnumerator SceneWiresPlayerCameraAndAnimationReceiver_Requirements_11_1_11_7()
        {
            yield return LoadScene();

            var harness = ResolveHarness();
            var facade = harness.Player;

            Assert.That(facade, Is.Not.Null,
                "The harness must reference the player instantiated in the scene.");
            Assert.That(facade.GetComponent<CharacterController>(), Is.Not.Null,
                "The scene player must carry the CharacterController the motor drives.");
            Assert.That(facade.Snapshot.CurrentLane, Is.EqualTo(LogicalLane.Center),
                "The scene player must start on the Center lane.");
            Assert.That(facade.Snapshot.TargetLane, Is.EqualTo(LogicalLane.Center),
                "The scene player must start with no lane change pending.");
            Assert.That(facade.transform.position.x,
                Is.EqualTo(facade.EffectiveConfiguration.LaneCenters.y).Within(LaneTolerance),
                "The scene player must be positioned on the configured Center lane center.");
            Assert.That(facade.SimulationEnabled, Is.True,
                "The authored scene configuration must produce a runnable player. " + Describe(facade));
            Assert.That(facade.ConfigurationStatus,
                Is.EqualTo(SubwaySurfers.Player.Configuration.ConfigurationStatus.Valid),
                "The authored scene configuration must validate without repair. " + Describe(facade));

            var follow = FindAll<PlayerCameraFollow>();
            Assert.That(follow.Length, Is.EqualTo(1),
                "The scene must carry exactly one camera follow adapter.");
            Assert.That(follow[0].PlayerResolved, Is.True,
                "The camera's serialized reference must resolve to the player query contract during " +
                "facade-controlled initialization.");
            Assert.That(follow[0].GetComponent<Camera>(), Is.Not.Null,
                "Camera follow must live on the scene camera.");

            var receivers = FindAll<ValidationAnimationReceiver>();
            Assert.That(receivers.Length, Is.GreaterThanOrEqualTo(1),
                "The scene must carry an animation receiver so state-to-command mapping is " +
                "observable without production art.");
            Assert.That(facade.EffectiveReferences.AnimationReceiver, Is.Not.Null,
                "Facade-controlled initialization must resolve the serialized animation reference to " +
                "IAnimationReceiver. " + Describe(facade));
        }

        // **Validates: Requirements 11.8, 11.9, 11.10**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the harness exposes every command control with its synchronous result and logs every published event in order")]
        public IEnumerator HarnessExposesControlsResultsAndOrderedEventLog_Requirements_11_8_11_9_11_10()
        {
            yield return LoadScene();

            var harness = ResolveHarness();
            Assert.That(harness.PlayerResolved, Is.True,
                "The harness must resolve the player command and query contracts.");
            Assert.That(harness.Subscribed, Is.True,
                "The harness must subscribe to the player event source so the log is complete.");

            harness.ClearLogs();

            // Speed: the supplied value and the accepted effective value are both observable.
            harness.SuppliedForwardSpeed = 11.5f;
            var speed = harness.SetForwardSpeed();
            Assert.That(speed.Status, Is.EqualTo(CommandStatus.Accepted),
                "A finite non-negative speed must be accepted. " + harness.LastCommandResult);
            Assert.That(harness.LastCommandResult, Does.Contain("11.5"),
                "The result display must show the supplied value. " + harness.LastCommandResult);
            Assert.That(harness.Player.ForwardSpeed, Is.EqualTo(11.5f).Within(0.0001f),
                "The accepted speed must be visible through the query surface.");

            // Rejection is a result, not an exception, and it must be displayed as one.
            harness.SuppliedForwardSpeed = -1f;
            var rejected = harness.SetForwardSpeed();
            Assert.That(rejected.Status, Is.EqualTo(CommandStatus.Rejected),
                "A negative speed must be rejected. " + harness.LastCommandResult);
            Assert.That(harness.LastCommandResult, Does.Contain("Rejected"),
                "The result display must show the rejection. " + harness.LastCommandResult);
            Assert.That(harness.Player.ForwardSpeed, Is.EqualTo(11.5f).Within(0.0001f),
                "A rejected speed must preserve the previously effective speed.");

            // Lane, jump and slide controls each record their synchronous result.
            var left = harness.RequestLaneLeft();
            Assert.That(harness.LastCommandResult, Does.Contain(left.Status.ToString()),
                "The lane control must record its synchronous result. " + harness.LastCommandResult);

            var right = harness.RequestLaneRight();
            Assert.That(harness.LastCommandResult, Does.Contain(right.Status.ToString()),
                "The lane control must record its synchronous result. " + harness.LastCommandResult);

            yield return SettleGrounded(harness);

            var jump = harness.RequestJump();
            Assert.That(harness.LastCommandResult, Does.Contain(jump.Status.ToString()),
                "The jump control must record its synchronous result. " + harness.LastCommandResult);

            // One accepted transition must appear in the log exactly once, with a complete payload.
            if (jump.Status == CommandStatus.Accepted)
            {
                var transitions = harness.EventLog
                    .Where(entry => entry.StartsWith("StateChanged", StringComparison.Ordinal))
                    .ToArray();
                Assert.That(transitions.Length, Is.EqualTo(1),
                    "One accepted transition must publish exactly one state-changed event. Log:\n" +
                    string.Join("\n", harness.EventLog));
                Assert.That(transitions[0], Does.Contain("eventId=").And
                        .Contain("previous=").And.Contain("current=").And.Contain("cause="),
                    "The log must carry every state-changed payload field. " + transitions[0]);
            }

            // Event ids are session-unique and the log preserves publication order.
            var ids = harness.EventLog
                .Select(ExtractEventId)
                .Where(id => id >= 0)
                .ToArray();
            Assert.That(ids, Is.Ordered.Ascending,
                "The event log must preserve publication order. Log:\n" +
                string.Join("\n", harness.EventLog));
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length),
                "Session event ids must never repeat. Log:\n" + string.Join("\n", harness.EventLog));

            // Criterion 11.8 requires one control per command kind, so slide and failure are driven
            // here too. Whichever result each returns, the control must record it synchronously.
            var slide = harness.RequestSlide();
            Assert.That(harness.LastCommandResult, Does.Contain(slide.Status.ToString()),
                "The slide control must record its synchronous result. " + harness.LastCommandResult);
            Assert.That(harness.LastCommandResult, Does.Contain(PlayerCommandKind.Slide.ToString()),
                "The slide control must name the command it issued. " + harness.LastCommandResult);

            // Failure is issued last: it is a terminal state for this fixture.
            var failure = harness.RequestFailure();
            Assert.That(harness.LastCommandResult, Does.Contain(failure.Status.ToString()),
                "The failure control must record its synchronous result. " + harness.LastCommandResult);
            Assert.That(harness.LastCommandResult, Does.Contain(PlayerCommandKind.Failure.ToString()),
                "The failure control must name the command it issued. " + harness.LastCommandResult);

            Assert.That(harness.RenderPlayerResult(), Does.Contain("state=").And
                    .Contain("grounded=").And.Contain("speed=").And.Contain("lane="),
                "The result display must show state, grounded, speed, and lane.");
            Assert.That(harness.RenderConfigurationResult(), Does.Contain("status=").And
                    .Contain("diagnostics="),
                "The diagnostics display must show configuration status and diagnostic count.");
        }

        // **Validates: Requirements 11.12**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: the harness reset control is repeatable and returns the player to its initial pose each time")]
        public IEnumerator HarnessResetControlIsRepeatable_Requirement_11_12()
        {
            yield return LoadScene();

            var harness = ResolveHarness();
            var facade = harness.Player;

            // Player_Initial_State is the pose the movement loop captured at initialization, not the
            // transform sampled here: loading the scene costs frames, and the player runs forward and
            // settles onto its running surface during them. Establish the baseline with a reset, which
            // is defined to produce Player_Initial_State, so the repeat assertions below compare
            // against the authoritative pose instead of an already-drifted sample.
            harness.SuppliedResetRequestId = "validation-reset-baseline";
            var baseline = harness.RequestReset();
            Assert.That(baseline.Status, Is.EqualTo(CommandStatus.Accepted),
                "Establishing the initial-state baseline requires an accepted reset. " +
                harness.LastCommandResult);

            var initialPosition = facade.transform.position;
            var initialRotation = facade.transform.rotation;
            var initialLane = facade.Snapshot.CurrentLane;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                harness.RequestLaneLeft();
                for (var step = 0; step < 10; step++)
                {
                    facade.ExecuteMovementUpdate(FrameStep);
                    yield return null;
                }

                Assert.That(facade.transform.position,
                    Is.Not.EqualTo(initialPosition),
                    "Fixture precondition: the player must have moved before reset attempt " +
                    attempt + ".");

                harness.SuppliedResetRequestId = "validation-reset-" +
                    attempt.ToString(CultureInfo.InvariantCulture);
                var result = harness.RequestReset();

                Assert.That(result.Status, Is.EqualTo(CommandStatus.Accepted),
                    "A fresh reset identifier must be accepted on attempt " + attempt + ". " +
                    harness.LastCommandResult);
                // Reset assigns the start pose rather than converging on it, so this is an exact
                // comparison. Any difference at all is cumulative drift across repeats.
                Assert.That(facade.transform.position, Is.EqualTo(initialPosition),
                    "Reset attempt " + attempt + " must restore the initial position exactly, with " +
                    "no cumulative drift across repeats. Position " +
                    facade.transform.position.ToString("R", CultureInfo.InvariantCulture) +
                    ", expected " + initialPosition.ToString("R", CultureInfo.InvariantCulture));
                Assert.That(facade.transform.rotation, Is.EqualTo(initialRotation),
                    "Reset attempt " + attempt + " must restore the initial rotation exactly.");
                Assert.That(facade.Snapshot.CurrentLane, Is.EqualTo(initialLane),
                    "Reset attempt " + attempt + " must restore the Center lane.");
                Assert.That(facade.CurrentState, Is.EqualTo(PlayerState.Running),
                    "Reset attempt " + attempt + " must finish in Running.");
            }

            // The same identifier is now spent, so it must be refused without mutating anything.
            harness.SuppliedResetRequestId = "validation-reset-1";
            var duplicate = harness.RequestReset();
            Assert.That(duplicate.Status, Is.EqualTo(CommandStatus.Rejected),
                "A previously accepted reset identifier must be refused. " +
                harness.LastCommandResult);
            Assert.That(duplicate.Reason, Is.EqualTo(RejectionReason.DuplicateRequestId),
                "The refusal must name duplicate identity. " + harness.LastCommandResult);
        }

        // **Validates: Requirements 11.11, 11.13**
        [UnityTest]
        [Description("Feature: player-controller, Play Mode: PlayerTestScene runs with zero production run-coordination or environment systems present")]
        public IEnumerator SceneContainsNoProductionSystems_Requirements_11_11_11_13()
        {
            yield return LoadScene();

            var offending = new List<string>();
            foreach (var behaviour in FindAll<MonoBehaviour>())
            {
                if (behaviour == null) continue;

                var type = behaviour.GetType();
                var qualified = (type.Namespace ?? string.Empty) + "." + type.Name;
                if (type.Assembly.GetName().Name.StartsWith("SubwaySurfers.Player", StringComparison.Ordinal))
                    continue;
                if (type.Assembly.GetName().Name.StartsWith("Unity", StringComparison.Ordinal))
                    continue;

                foreach (var fragment in ForbiddenProductionNamespaces)
                {
                    if (qualified.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    offending.Add(behaviour.gameObject.name + " -> " + qualified +
                                  " (" + type.Assembly.GetName().Name + ")");
                    break;
                }
            }

            Assert.That(offending, Is.Empty,
                "PlayerTestScene must be drivable with zero production run-coordination, score, user " +
                "interface, or endless-track systems present. Found:\n" + string.Join("\n", offending));

            foreach (var component in FindAll<MonoBehaviour>())
            {
                if (component == null) continue;
                if (!(component is INonProductionValidationDouble notice)) continue;

                Assert.That(notice.NonProductionNotice, Is.Not.Null.And.Not.Empty,
                    "Validation component " + component.GetType().Name + " must declare that it is " +
                    "not production content.");
                Assert.That(component.GetType().Assembly.GetName().Name,
                    Is.EqualTo("SubwaySurfers.Player.Validation"),
                    "Test double " + component.GetType().FullName + " must live in the player-owned " +
                    "validation assembly under Assets/Player, so it can never be referenced as a " +
                    "production environment asset.");
            }
        }

        // ----- Fixture helpers -----

        private static IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;

            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo(ScenePath),
                "The validation scene must load from " + ScenePath +
                ". Active scene path was '" + scene.path + "'.");
            Assert.That(scene.isLoaded, Is.True, "The validation scene must finish loading.");
#else
            Assert.Ignore("PlayerTestScene is opened through the Editor scene API so the validation " +
                          "scene stays out of shared Build Settings. Run these tests in the Editor.");
            yield break;
#endif
        }

        private static PlayerTestSceneHarness ResolveHarness()
        {
            var harnesses = FindAll<PlayerTestSceneHarness>();
            Assert.That(harnesses.Length, Is.EqualTo(1),
                "PlayerTestScene must carry exactly one harness. Found " + harnesses.Length + ".");
            harnesses[0].Bind();
            Assert.That(harnesses[0].Player, Is.Not.Null,
                "The harness must reference the scene player.");
            return harnesses[0];
        }

        private static IEnumerator SettleGrounded(PlayerTestSceneHarness harness)
        {
            for (var step = 0; step < 25 && !harness.Player.IsGrounded; step++)
            {
                harness.Player.ExecuteMovementUpdate(FrameStep);
                yield return null;
            }
        }

        private static T[] FindAll<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private static long ExtractEventId(string entry)
        {
            const string marker = "eventId=";
            var start = entry.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return -1;

            start += marker.Length;
            var end = start;
            while (end < entry.Length && char.IsDigit(entry[end])) end++;

            long parsed;
            return long.TryParse(entry.Substring(start, end - start),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : -1;
        }

        private static string Describe<T>(IEnumerable<T> items) where T : Component
        {
            var rendered = items
                .Select(item => item.gameObject.name + "@x=" +
                    item.transform.position.x.ToString("R", CultureInfo.InvariantCulture))
                .ToArray();
            return rendered.Length == 0 ? "none" : string.Join(", ", rendered);
        }

        private static string Describe(PlayerControllerFacade facade)
        {
            if (facade == null) return "[facade=none]";

            return string.Format(
                CultureInfo.InvariantCulture,
                "[player={0}, status={1}, simulation={2}, diagnostics={3}]",
                facade.gameObject.name,
                facade.ConfigurationStatus,
                facade.SimulationEnabled,
                facade.ConfigurationDiagnostics.Count.ToString(CultureInfo.InvariantCulture));
        }
    }
}
