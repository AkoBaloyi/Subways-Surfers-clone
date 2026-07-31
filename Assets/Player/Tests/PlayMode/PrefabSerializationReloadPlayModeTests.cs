using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Play Mode coverage for the engine behavior the serialized player assets depend on: interface
    /// resolution across a serialization round trip, reference remapping into the loaded copy, and the
    /// ordering of configuration validation against the first Movement_Update.
    ///
    /// The prefab asset itself belongs to Task 10.2, and this assembly cannot create or load an asset,
    /// so the rig is assembled programmatically in the prefab's shape and then instantiated.
    /// Instantiation is the reload: it round-trips the serialized data and remaps every reference that
    /// points inside the copied hierarchy, which is exactly the path a prefab instance takes on load.
    /// A clone that resolves its own facade, its own camera target, and its own animation receiver has
    /// proven the serialized concrete references resolve to their interfaces after the round trip; a
    /// clone that still answers with the original's components has not.
    ///
    /// Validation ordering is observed rather than assumed. The facade validates in <c>Awake</c>, so
    /// the assertions read the effective configuration, the applied capsule, and the diagnostics while
    /// Movement_Update_Count is still zero, then supply one explicit update and observe it consume the
    /// effective values. The invalid-field case supplies a non-finite authored speed and a distinct
    /// valid authored jump velocity, so a repair that overwrites more than the invalid field is
    /// visible.
    ///
    /// The input adapter is reached by type name because it compiles against the Input System package
    /// this assembly does not reference. Every fixture object is built programmatically, marked
    /// non-production, and destroyed in teardown.
    /// </summary>
    public sealed class PrefabSerializationReloadPlayModeTests
    {
        private const float FrameStep = 0.02f;
        private const float LinearTolerance = 0.0005f;
        private const string InputAdapterTypeName = "SubwaySurfers.Player.PlayerInputAdapter";

        private static readonly PlayerConfiguration Defaults = PlayerConfiguration.SafeDefaults;

        private static readonly Vector3 PlayerSpawn =
            new Vector3(Defaults.LaneCenters.y, 0.02f, 0f);

        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<ScriptableObject> spawnedAssets = new List<ScriptableObject>();

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
            for (var index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                if (spawnedObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(spawnedObjects[index]);
            }

            for (var index = spawnedAssets.Count - 1; index >= 0; index--)
            {
                if (spawnedAssets[index] != null)
                    UnityEngine.Object.DestroyImmediate(spawnedAssets[index]);
            }

            spawnedObjects.Clear();
            spawnedAssets.Clear();
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.maximumDeltaTime = originalMaximumDeltaTime;
        }

        // **Validates: Requirements 12.12, 15.1, 15.2, 15.8**
        [Test]
        [Description("Feature: player-controller, Play Mode: configuration validation and adapter initialization complete before the first Movement_Update")]
        public void ValidationCompletesBeforeFirstMovementUpdate_Requirements_12_12_15_1_15_2_15_8()
        {
            var asset = CreateConfigurationAsset("ValidConfiguration");
            PlayerControllerFacade facade;
            var rig = BuildRig("ValidationOrderingRig", asset, out facade);
            rig.SetActive(true);

            var controller = facade.GetComponent<CharacterController>();

            // Everything below is read while no Movement_Update has run, which is what makes the
            // ordering observable rather than assumed.
            Assert.That(facade.MovementUpdateCount, Is.EqualTo(0),
                "The fixture must observe the configuration pass before any Movement_Update. " +
                Describe(facade));
            Assert.That(facade.MoveInvocationCount, Is.EqualTo(0),
                "No displacement may be submitted before the first Movement_Update. " +
                Describe(facade));
            Assert.That(facade.ConfigurationStatus, Is.EqualTo(ConfigurationStatus.Valid),
                "A valid serialized configuration must report valid status during initialization. " +
                Describe(facade));
            Assert.That(facade.ConfigurationDiagnostics, Is.Empty,
                "A valid serialized configuration must publish no Validation_Diagnostic. " +
                Describe(facade));
            Assert.That(facade.SimulationEnabled, Is.True,
                "A valid serialized configuration must leave movement simulation enabled. " +
                Describe(facade));
            Assert.That(facade.EffectiveConfiguration, Is.EqualTo(asset.ConfiguredConfiguration),
                "The effective configuration must be the authored configuration, converted once " +
                "before simulation. " + Describe(facade));

            AssertAppliedCapsule(controller, asset.ConfiguredConfiguration.BaselineCollider,
                "initialization", facade);

            var consumers = ResolveConsumers(facade);
            Assert.That(consumers.CameraFollow.PlayerResolved, Is.True,
                "Facade-controlled initialization must resolve the camera's serialized reference to " +
                "the player query contract before the first Movement_Update. " + Describe(facade));
            Assert.That(consumers.AnimationBridge.ReceiverResolved, Is.True,
                "Facade-controlled initialization must resolve the serialized animation reference to " +
                "IAnimationReceiver before the first Movement_Update. " + Describe(facade));

            facade.ExecuteMovementUpdate(FrameStep);

            Assert.That(facade.MovementUpdateCount, Is.EqualTo(1),
                "One supplied update must be one Movement_Update. " + Describe(facade));
            Assert.That(facade.MoveInvocationCount, Is.EqualTo(1),
                "One Movement_Update must submit exactly one displacement. " + Describe(facade));
            Assert.That(facade.LastRequestedDisplacement.z,
                Is.EqualTo(asset.ConfiguredConfiguration.ForwardSpeed * FrameStep)
                    .Within(LinearTolerance),
                "The first Movement_Update must consume the effective Forward_Run_Speed. " +
                Describe(facade));
        }

        // **Validates: Requirements 12.12, 15.1, 15.3, 15.4, 15.5, 15.8**
        [Test]
        [Description("Feature: player-controller, Play Mode: an invalid serialized field is diagnosed and repaired from the documented safe defaults before the first Movement_Update")]
        public void InvalidSerializedFieldIsRepairedBeforeFirstMovementUpdate_Requirements_12_12_15_1_15_3_15_4_15_5_15_8()
        {
            var asset = CreateConfigurationAsset("InvalidConfiguration");

            // One authored field is non-finite and a second authored field is valid but distinct from
            // its documented fallback, so a repair that reaches past the invalid field is visible.
            const float authoredJumpVelocity = 9.5f;
            SetAuthoredValue(asset, "forwardSpeed", float.NaN);
            SetAuthoredValue(asset, "jumpVelocity", authoredJumpVelocity);

            PlayerControllerFacade facade;
            var rig = BuildRig("ConfigurationFallbackRig", asset, out facade);
            rig.SetActive(true);

            Assert.That(facade.MovementUpdateCount, Is.EqualTo(0),
                "The repair must be observable before any Movement_Update. " + Describe(facade));
            Assert.That(facade.ConfigurationStatus, Is.EqualTo(ConfigurationStatus.Repaired),
                "An invalid authored field before simulation started must report repaired status. " +
                Describe(facade));
            Assert.That(facade.ConfigurationDiagnostics.Count, Is.EqualTo(1),
                "Exactly one Validation_Diagnostic must be published, for the one invalid field. " +
                Describe(facade));

            var diagnostic = facade.ConfigurationDiagnostics[0];
            Assert.That(diagnostic.Field, Is.EqualTo(ConfigurationField.ForwardSpeed),
                "The diagnostic must identify the invalid field. " + Describe(facade));
            Assert.That(diagnostic.Constraint, Is.Not.Null.And.Not.Empty,
                "The diagnostic must name the violated constraint. " + Describe(facade));
            Assert.That(diagnostic.FallbackCategory, Is.Not.Null.And.Not.Empty,
                "The diagnostic must name the fallback category that was applied. " +
                Describe(facade));

            Assert.That(facade.EffectiveConfiguration.ForwardSpeed,
                Is.EqualTo(asset.SafeDefaultConfiguration.ForwardSpeed),
                "The invalid field must be replaced by its documented safe default before the first " +
                "Movement_Update. " + Describe(facade));
            Assert.That(facade.EffectiveConfiguration.JumpVelocity,
                Is.EqualTo(authoredJumpVelocity),
                "A field that remains valid must be preserved rather than reset alongside the " +
                "repaired field. " + Describe(facade));
            Assert.That(facade.SimulationEnabled, Is.True,
                "A repaired configuration must still enable movement simulation. " + Describe(facade));

            AssertAppliedCapsule(facade.GetComponent<CharacterController>(),
                facade.EffectiveConfiguration.BaselineCollider, "a repaired configuration", facade);

            facade.ExecuteMovementUpdate(FrameStep);

            Assert.That(facade.LastRequestedDisplacement.z,
                Is.EqualTo(facade.EffectiveConfiguration.ForwardSpeed * FrameStep)
                    .Within(LinearTolerance),
                "The first Movement_Update must consume the repaired Forward_Run_Speed rather than " +
                "the invalid authored value. " + Describe(facade));
        }

        // **Validates: Requirements 11.1, 11.7, 12.12, 12.13, 15.1**
        [Test]
        [Description("Feature: player-controller, Play Mode: serialized concrete references resolve to their interfaces and remap into the loaded copy after a prefab-style reload")]
        public void SerializedReferencesResolveAfterPrefabStyleReload_Requirements_11_1_11_7_12_12_12_13_15_1()
        {
            var asset = CreateConfigurationAsset("ReloadConfiguration");
            PlayerControllerFacade facade;
            var rig = BuildRig("ReloadRig", asset, out facade);
            rig.SetActive(true);

            var original = ResolveConsumers(facade);
            Assert.That(facade.SimulationEnabled, Is.True,
                "The rig configuration must enable movement simulation. " + Describe(facade));
            Assert.That(original.CameraFollow.PlayerResolved, Is.True,
                "The original rig must resolve its serialized player reference. " + Describe(facade));

            // Instantiation round-trips the serialized data and remaps references inside the copied
            // hierarchy. The clone is displaced so the two players cannot be confused.
            var displacement = new Vector3(30f, 0f, 0f);
            var clone = UnityEngine.Object.Instantiate(rig, displacement, Quaternion.identity);
            clone.name = "ReloadRigReloaded";
            spawnedObjects.Add(clone);

            var cloneFacade = clone.GetComponentInChildren<PlayerControllerFacade>();
            Assert.That(cloneFacade, Is.Not.Null,
                "The reloaded rig must carry its own facade. " + Describe(facade));
            Assert.That(cloneFacade, Is.Not.SameAs(facade),
                "Fixture precondition: the reload must produce a distinct facade. " +
                Describe(cloneFacade));

            var reloaded = ResolveConsumers(cloneFacade);

            Assert.That(cloneFacade.ConfigurationStatus, Is.EqualTo(ConfigurationStatus.Valid),
                "The reloaded configuration must validate exactly as the original did. " +
                Describe(cloneFacade));
            Assert.That(cloneFacade.SimulationEnabled, Is.True,
                "The reloaded configuration must enable movement simulation. " +
                Describe(cloneFacade));
            Assert.That(cloneFacade.EffectiveConfiguration, Is.EqualTo(facade.EffectiveConfiguration),
                "A serialization round trip must not change the effective configuration. " +
                Describe(cloneFacade));

            Assert.That(reloaded.CameraFollow, Is.Not.SameAs(original.CameraFollow),
                "Fixture precondition: the reload must produce a distinct camera adapter. " +
                Describe(cloneFacade));
            Assert.That(reloaded.CameraFollow.PlayerResolved, Is.True,
                "A serialized concrete reference must still resolve to IPlayerQueries after a " +
                "serialization round trip. " + Describe(cloneFacade));
            Assert.That(
                (reloaded.CameraFollow.CameraFollowOffset -
                    (Defaults.InitialCameraPosition - (PlayerSpawn + displacement))).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "The reloaded camera must derive Camera_Follow_Offset from its own player's pose, " +
                "which it can only do if it resolved the facade in its own hierarchy. " +
                Describe(cloneFacade));

            Assert.That(reloaded.AnimationBridge, Is.Not.SameAs(original.AnimationBridge),
                "Fixture precondition: the reload must produce a distinct animation bridge. " +
                Describe(cloneFacade));
            Assert.That(reloaded.AnimationBridge.ReceiverResolved, Is.True,
                "A serialized concrete reference must still resolve to IAnimationReceiver after a " +
                "serialization round trip. " + Describe(cloneFacade));

            var reloadedReceiver = cloneFacade.EffectiveReferences.AnimationReceiver as MonoBehaviour;
            Assert.That(reloadedReceiver, Is.Not.Null,
                "The reloaded effective references must carry the resolved animation receiver. " +
                Describe(cloneFacade));
            Assert.That(reloadedReceiver.transform.IsChildOf(clone.transform), Is.True,
                "The reloaded animation reference must be remapped to the receiver inside the " +
                "reloaded hierarchy rather than left pointing at the original. Resolved receiver on " +
                reloadedReceiver.gameObject.name + ". " + Describe(cloneFacade));

            var reloadedCameraTarget = cloneFacade.EffectiveReferences.CameraTarget as Component;
            Assert.That(reloadedCameraTarget, Is.Not.Null,
                "The reloaded effective references must carry the camera target. " +
                Describe(cloneFacade));
            Assert.That(reloadedCameraTarget.transform.IsChildOf(clone.transform), Is.True,
                "The reloaded camera target must be remapped into the reloaded hierarchy. " +
                Describe(cloneFacade));

            Assert.That(ResolveInputAdapter(cloneFacade.gameObject), Is.Not.Null,
                "The reloaded player must carry its own input adapter. " + Describe(cloneFacade));
            AssertAppliedCapsule(cloneFacade.GetComponent<CharacterController>(),
                cloneFacade.EffectiveConfiguration.BaselineCollider, "a reload", cloneFacade);
        }

        // **Validates: Requirements 11.7, 12.12, 15.8**
        [Test]
        [Description("Feature: player-controller, Play Mode: the serialized animation receiver receives one command per published transition after a prefab-style reload")]
        public void AnimationLayerRoutesPublishedTransitionsToTheSerializedReceiver_Requirements_11_7_12_12_15_8()
        {
            var asset = CreateConfigurationAsset("AnimationRoutingConfiguration");
            PlayerControllerFacade facade;
            var rig = BuildRig("AnimationRoutingRig", asset, out facade);
            rig.SetActive(true);

            var clone = UnityEngine.Object.Instantiate(rig, new Vector3(60f, 0f, 0f), Quaternion.identity);
            clone.name = "AnimationRoutingRigReloaded";
            spawnedObjects.Add(clone);

            var cloneFacade = clone.GetComponentInChildren<PlayerControllerFacade>();
            Assert.That(cloneFacade, Is.Not.Null,
                "The reloaded rig must carry its own facade. " + Describe(facade));

            var originalReceiver = FindReceiver(rig);
            var reloadedReceiver = FindReceiver(clone);
            Assert.That(reloadedReceiver, Is.Not.SameAs(originalReceiver),
                "Fixture precondition: the reload must produce a distinct receiver. " +
                Describe(cloneFacade));

            cloneFacade.Events.PublishStateChanged(
                PlayerState.Running, PlayerState.Jumping, PlayerTransitionCause.JumpRequested);

            Assert.That(reloadedReceiver.AppliedCommands.Count, Is.EqualTo(1),
                "A configured animation layer must turn one published transition into exactly one " +
                "command on the serialized receiver, which requires the animation bridge to be bound " +
                "to the player's own event source during facade-controlled initialization. " +
                Describe(cloneFacade) + " Applied commands: " +
                Render(reloadedReceiver.AppliedCommands));
            Assert.That(originalReceiver.AppliedCommands, Is.Empty,
                "A transition published by the reloaded player must not reach the original player's " +
                "receiver. " + Describe(cloneFacade) + " Applied commands: " +
                Render(originalReceiver.AppliedCommands));
        }

        private static void AssertAppliedCapsule(
            CharacterController controller,
            ColliderProfile expected,
            string context,
            PlayerControllerFacade facade)
        {
            Assert.That(controller, Is.Not.Null,
                "The player must carry the CharacterController the motor drives. " + Describe(facade));
            Assert.That(controller.radius, Is.EqualTo(expected.Radius).Within(LinearTolerance),
                "After " + context + ", the capsule radius must be the effective " +
                "Baseline_Collider_Profile radius. " + Describe(facade));
            Assert.That(controller.height, Is.EqualTo(expected.Height).Within(LinearTolerance),
                "After " + context + ", the capsule height must be the effective " +
                "Baseline_Collider_Profile height. " + Describe(facade));
            Assert.That((controller.center - expected.Center).magnitude,
                Is.LessThanOrEqualTo(LinearTolerance),
                "After " + context + ", the capsule Center must be the effective " +
                "Baseline_Collider_Profile center; an uninitialized Center changes grounding and " +
                "slide restoration silently. Applied center " +
                controller.center.ToString("R", CultureInfo.InvariantCulture) + ", expected " +
                expected.Center.ToString("R", CultureInfo.InvariantCulture) + ". " + Describe(facade));
        }

        /// <summary>
        /// A prefab-shaped rig returned inactive, so every serialized reference is assigned before any
        /// initialization runs, exactly as it is for a prefab on load.
        /// </summary>
        private GameObject BuildRig(
            string name,
            PlayerConfigurationAsset asset,
            out PlayerControllerFacade facade)
        {
            var rig = new GameObject(name);
            spawnedObjects.Add(rig);
            rig.SetActive(false);
            rig.transform.position = Vector3.zero;

            var player = new GameObject("Player");
            player.transform.SetParent(rig.transform, false);
            player.transform.position = PlayerSpawn;

            var controller = player.AddComponent<CharacterController>();

            // Deliberately authored away from the configured capsule so an applied profile is
            // distinguishable from an unchanged authored one.
            controller.radius = 0.25f;
            controller.height = 1.5f;
            controller.center = Vector3.zero;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.1f;

            facade = player.AddComponent<PlayerControllerFacade>();
            player.AddComponent<CharacterControllerMotor>();
            player.AddComponent<EnvironmentContactAdapter>();
            var bridge = player.AddComponent<PlayerAnimationBridge>();
            var inputAdapter = player.AddComponent(RequiredInputAdapterType()) as MonoBehaviour;
            Assert.That(inputAdapter, Is.Not.Null,
                "The runtime input adapter must be a MonoBehaviour so a prefab can carry it.");

            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            var receiver = visual.AddComponent<RecordingAnimationReceiverDouble>();

            var camera = new GameObject("FollowCamera");
            camera.transform.SetParent(rig.transform, false);
            camera.transform.position = Defaults.InitialCameraPosition;
            var follow = camera.AddComponent<PlayerCameraFollow>();
            InjectField(follow, "playerQuerySource", facade);

            var placeholder = CreateReferencePlaceholder();
            InjectFacadeField(facade, "configurationAsset", asset);
            InjectFacadeField(facade, "inputActionAsset", placeholder);
            InjectFacadeField(facade, "fallbackInputActionAsset", placeholder);
            InjectFacadeField(facade, "cameraTarget", camera.transform);
            InjectFacadeField(facade, "fallbackCameraTarget", camera.transform);
            InjectFacadeField(facade, "animationReceiver", receiver);
            InjectFacadeField(facade, "fallbackAnimationReceiver", receiver);
            InjectFacadeField(facade, "configurationConsumers",
                new MonoBehaviour[] { inputAdapter, bridge, follow });

            CreateRunningSurface();
            return rig;
        }

        /// <summary>Marked, non-trigger support geometry whose top face sits at the world origin.</summary>
        private void CreateRunningSurface()
        {
            var surface = new GameObject("RunningSurfaceDouble");
            spawnedObjects.Add(surface);
            surface.layer = GroundLayer();
            surface.transform.position = new Vector3(0f, -0.5f, 0f);

            var collider = surface.AddComponent<BoxCollider>();
            collider.size = new Vector3(200f, 1f, 400f);
            surface.AddComponent<RunningSurfaceDouble>();
        }

        private PlayerConfigurationAsset CreateConfigurationAsset(string name)
        {
            var asset = ScriptableObject.CreateInstance<PlayerConfigurationAsset>();
            asset.name = name;
            spawnedAssets.Add(asset);
            return asset;
        }

        private UnityEngine.Object CreateReferencePlaceholder()
        {
            var asset = ScriptableObject.CreateInstance<ReferencePlaceholderDouble>();
            asset.name = "PrefabReloadFixtureReferencePlaceholder";
            spawnedAssets.Add(asset);
            return asset;
        }

        private static ResolvedConsumers ResolveConsumers(PlayerControllerFacade facade)
        {
            var root = facade.transform.root.gameObject;
            var follow = root.GetComponentInChildren<PlayerCameraFollow>(true);
            var bridge = facade.GetComponent<PlayerAnimationBridge>();
            Assert.That(follow, Is.Not.Null,
                "The rig must carry the camera follow adapter. " + Describe(facade));
            Assert.That(bridge, Is.Not.Null,
                "The player must carry the animation bridge. " + Describe(facade));
            return new ResolvedConsumers(follow, bridge);
        }

        private static RecordingAnimationReceiverDouble FindReceiver(GameObject root)
        {
            var receiver = root.GetComponentInChildren<RecordingAnimationReceiverDouble>(true);
            Assert.That(receiver, Is.Not.Null,
                "The rig must carry its non-production animation receiver double.");
            return receiver;
        }

        private static Component ResolveInputAdapter(GameObject player)
        {
            return player.GetComponent(RequiredInputAdapterType());
        }

        private static Type RequiredInputAdapterType()
        {
            var type = typeof(PlayerControllerFacade).Assembly.GetType(InputAdapterTypeName);
            Assert.That(type, Is.Not.Null,
                "The runtime assembly must expose " + InputAdapterTypeName + ".");
            return type;
        }

        /// <summary>
        /// Writes one authored scalar inside the asset's configured block, so an invalid serialized
        /// field can be observed without an Inspector or an imported asset.
        /// </summary>
        private static void SetAuthoredValue(
            PlayerConfigurationAsset asset, string fieldName, float value)
        {
            var blockField = typeof(PlayerConfigurationAsset).GetField(
                "configured", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(blockField, Is.Not.Null,
                "The configuration asset must serialize the authored block in a field named " +
                "configured.");

            var block = blockField.GetValue(asset);
            Assert.That(block, Is.Not.Null,
                "The authored configuration block must be instantiated by the asset.");

            var field = block.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "The serialized configuration must retain the field " + fieldName + ".");
            field.SetValue(block, value);
        }

        private static void InjectFacadeField(
            PlayerControllerFacade facade, string fieldName, object value)
        {
            InjectField(facade, fieldName, value);
        }

        private static void InjectField(MonoBehaviour target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                target.GetType().Name + " must retain the serialized field " + fieldName + ".");
            Assert.That(Attribute.IsDefined(field, typeof(SerializeField)), Is.True,
                fieldName + " must carry [SerializeField] so the reference survives serialization.");
            field.SetValue(target, value);
        }

        private static int GroundLayer()
        {
            for (var layer = 0; layer < 32; layer++)
            {
                if ((Defaults.GroundLayerMask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private static string Describe(PlayerControllerFacade facade)
        {
            if (facade == null) return "[facade=none]";

            var diagnostics = facade.ConfigurationDiagnostics;
            var rendered = new List<string>();
            for (var index = 0; index < diagnostics.Count; index++)
                rendered.Add(diagnostics[index].Severity + ":" + diagnostics[index].Field);

            return string.Format(
                CultureInfo.InvariantCulture,
                "[player={0}, status={1}, simulation={2}, updates={3}, moves={4}, diagnostics={5}]",
                facade.gameObject.name,
                facade.ConfigurationStatus,
                facade.SimulationEnabled,
                facade.MovementUpdateCount.ToString(CultureInfo.InvariantCulture),
                facade.MoveInvocationCount.ToString(CultureInfo.InvariantCulture),
                rendered.Count == 0 ? "none" : string.Join("/", rendered));
        }

        private static string Render(IReadOnlyList<string> commands)
        {
            return commands == null || commands.Count == 0 ? "none" : string.Join(", ", commands);
        }

        private struct ResolvedConsumers
        {
            public ResolvedConsumers(PlayerCameraFollow cameraFollow, PlayerAnimationBridge bridge)
            {
                CameraFollow = cameraFollow;
                AnimationBridge = bridge;
            }

            public PlayerCameraFollow CameraFollow { get; }
            public PlayerAnimationBridge AnimationBridge { get; }
        }

        /// <summary>
        /// Non-production animation receiver double. Records the command names it was given so one
        /// published transition can be counted as one receiver call.
        /// </summary>
        private sealed class RecordingAnimationReceiverDouble : MonoBehaviour, IAnimationReceiver
        {
            private readonly List<string> appliedCommands = new List<string>();

            public IReadOnlyList<string> AppliedCommands { get { return appliedCommands; } }

            public bool TryApply(AnimationCommand command)
            {
                appliedCommands.Add(command.Name);
                return true;
            }
        }

        private sealed class RunningSurfaceDouble : MonoBehaviour, IRunningSurface
        {
        }

        private sealed class ReferencePlaceholderDouble : ScriptableObject
        {
        }
    }
}
