using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEditor;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    /// <summary>
    /// Asset-level coverage for the serialized Player prefab and configuration asset Task 10.2 has to
    /// create. The assets are inspected as imported assets rather than instantiated, so every fact
    /// here is a property of what was authored and serialized: which components the prefab root
    /// carries, what the capsule was authored with, which serialized references were assigned, and
    /// whether the configured values and the documented safe defaults each form a valid
    /// configuration.
    ///
    /// This fixture is deliberately red until the assets exist. Each required asset, component, and
    /// serialized field is resolved by name and reported by name when it is missing, so the failure
    /// message states exactly what Task 10.2 still owes rather than a bare null reference. Runtime
    /// components that live in an assembly this test assembly cannot reference - the input adapter,
    /// which compiles against the Input System package - are resolved by type name, the same seam
    /// convention the Play Mode input fixture uses.
    ///
    /// Nothing here instantiates the prefab or steps physics: prefab reload, interface resolution
    /// after a serialization round trip, and validation ordering against the first Movement_Update are
    /// engine behavior and belong to the Play Mode fixture. The read-only input action asset is
    /// resolved by path and compared by reference only; it is never opened for writing.
    /// </summary>
    public sealed class PrefabConfigurationSerializationTests
    {
        private const string PrefabPath = "Assets/Player/Prefabs/Player.prefab";
        private const string ConfigurationFolder = "Assets/Player/Config";
        private const string InputActionAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string RuntimeAssemblyName = "SubwaySurfers.Player.Runtime";
        private const string InputAdapterTypeName = "SubwaySurfers.Player.PlayerInputAdapter";
        private const float Tolerance = 1e-5f;

        /// <summary>
        /// Components the prefab root must carry, named so a missing one is reported as the component
        /// Task 10.2 has to add rather than as a null dereference somewhere later.
        /// </summary>
        private static readonly Type[] RequiredRootComponents =
        {
            typeof(CharacterController),
            typeof(PlayerControllerFacade),
            typeof(CharacterControllerMotor),
            typeof(EnvironmentContactAdapter),
            typeof(PlayerAnimationBridge)
        };

        // **Validates: Requirements 11.1, 11.7, 12.12, 15.8, 15.9**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the prefab root carries every required player component and no production Rachel or Lucky component")]
        public void PrefabRootCarriesRequiredComponentsAndNoProductionComponents_Requirements_11_1_11_7_12_12_15_8_15_9()
        {
            var root = LoadPrefabRoot();

            var missing = new List<string>();
            for (var index = 0; index < RequiredRootComponents.Length; index++)
            {
                if (root.GetComponent(RequiredRootComponents[index]) == null)
                    missing.Add(RequiredRootComponents[index].Name);
            }

            if (root.GetComponent(RequiredInputAdapterType()) == null)
                missing.Add(InputAdapterTypeName);

            Assert.That(missing, Is.Empty,
                "The Player_Prefab root must carry the CharacterController it drives, the facade that " +
                "owns validation and the command surface, the motor, the input adapter, the contact " +
                "adapter, and the animation bridge. Missing components: " + string.Join(", ", missing) +
                ". " + Describe(root));

            // Camera follow is authored on the camera that observes the player, not on the player, so
            // the prefab stays usable in a scene whose camera the player does not own.
            Assert.That(root.GetComponentInChildren<PlayerCameraFollow>(true), Is.Null,
                "Player_Camera_Follow belongs on the observing camera rather than inside the " +
                "Player_Prefab, so the prefab carries no camera component. " + Describe(root));

            var brokenScripts = new List<string>();
            var foreignComponents = new List<string>();
            var components = root.GetComponentsInChildren<Component>(true);
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null)
                {
                    brokenScripts.Add("component slot " + index.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                var assemblyName = component.GetType().Assembly.GetName().Name;
                var allowed = string.Equals(assemblyName, RuntimeAssemblyName, StringComparison.Ordinal) ||
                    assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Unity.", StringComparison.Ordinal);
                if (!allowed)
                {
                    foreignComponents.Add(
                        component.GetType().FullName + " from " + assemblyName +
                        " on " + component.gameObject.name);
                }
            }

            Assert.That(brokenScripts, Is.Empty,
                "Every component reference in the Player_Prefab must resolve to a loaded script, so a " +
                "reload never produces a missing-script slot. Unresolved slots: " +
                string.Join(", ", brokenScripts) + ". " + Describe(root));
            Assert.That(foreignComponents, Is.Empty,
                "The Player_Prefab must compose only player-owned runtime components and engine " +
                "components, so it loads with zero production Rachel or Lucky implementations " +
                "present. Unexpected components: " + string.Join(", ", foreignComponents) + ". " +
                Describe(root));
        }

        // **Validates: Requirements 11.1, 12.12, 15.1, 15.2**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the authored CharacterController capsule matches the configured baseline collider profile")]
        public void PrefabCharacterControllerMatchesConfiguredBaselineCapsule_Requirements_11_1_12_12_15_1_15_2()
        {
            var root = LoadPrefabRoot();
            var controller = root.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null,
                "The Player_Prefab root must carry the CharacterController. " + Describe(root));

            var baseline = LoadConfigurationAsset().ConfiguredConfiguration.BaselineCollider;

            Assert.That(controller.radius, Is.EqualTo(baseline.Radius).Within(Tolerance),
                "The authored capsule radius must be the configured Baseline_Collider_Profile " +
                "radius, so the prefab and the configuration asset describe one capsule. " +
                Describe(root));
            Assert.That(controller.height, Is.EqualTo(baseline.Height).Within(Tolerance),
                "The authored capsule height must be the configured Baseline_Collider_Profile " +
                "height. " + Describe(root));
            Assert.That((controller.center - baseline.Center).magnitude,
                Is.LessThanOrEqualTo(Tolerance),
                "The authored capsule Center must be the configured Baseline_Collider_Profile " +
                "center. An uninitialized Center leaves the capsule straddling the pivot, which " +
                "silently changes grounding and slide restoration. Authored center " +
                Render(controller.center) + ", configured center " + Render(baseline.Center) + ". " +
                Describe(root));

            // The default CharacterController Center is the origin, so an authored capsule that still
            // sits on the origin is indistinguishable from one nobody initialized.
            Assert.That(controller.center, Is.Not.EqualTo(Vector3.zero),
                "Center must be initialized rather than left at the CharacterController default of " +
                "(0, 0, 0). " + Describe(root));
            Assert.That(controller.center.y, Is.GreaterThanOrEqualTo(controller.height * 0.5f - Tolerance),
                "Center must lift the capsule so its base sits at or above the player pivot; a " +
                "capsule sunk below the pivot cannot rest on a Running_Surface at the configured " +
                "start pose. Center " + Render(controller.center) + ", height " +
                controller.height.ToString("R", CultureInfo.InvariantCulture) + ". " + Describe(root));
            Assert.That(controller.enabled, Is.True,
                "The authored CharacterController must be enabled so the first Movement_Update can " +
                "submit displacement. " + Describe(root));
            Assert.That(controller.minMoveDistance, Is.EqualTo(0f),
                "Min Move Distance must be zero so no elapsed-time displacement is swallowed and " +
                "movement stays a function of elapsed simulation time alone. " + Describe(root));
        }

        // **Validates: Requirements 12.12, 15.1, 15.2, 15.5**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the configured baseline and slide capsules are valid and the slide capsule is nested inside the baseline")]
        public void ConfigurationAssetCapsulesAreValidAndNested_Requirements_12_12_15_1_15_2_15_5()
        {
            var configured = LoadConfigurationAsset().ConfiguredConfiguration;
            var baseline = configured.BaselineCollider;
            var slide = configured.SlideCollider;

            AssertValidCapsule(baseline, "Baseline_Collider_Profile");
            AssertValidCapsule(slide, "Slide_Collider_Profile");

            Assert.That(slide.Height, Is.LessThan(baseline.Height - Tolerance),
                "The slide capsule must be shorter than the baseline capsule, otherwise an accepted " +
                "Slide_Request changes nothing observable. Baseline height " +
                baseline.Height.ToString("R", CultureInfo.InvariantCulture) + ", slide height " +
                slide.Height.ToString("R", CultureInfo.InvariantCulture) + ".");

            var horizontalOffset = new Vector2(
                slide.Center.x - baseline.Center.x, slide.Center.z - baseline.Center.z).magnitude;
            Assert.That(horizontalOffset + slide.Radius,
                Is.LessThanOrEqualTo(baseline.Radius + Tolerance),
                "The slide capsule must stay inside the baseline capsule horizontally so restoration " +
                "never has to grow the capsule sideways into geometry it never occupied.");
            Assert.That(slide.Center.y - slide.Height * 0.5f,
                Is.GreaterThanOrEqualTo(baseline.Center.y - baseline.Height * 0.5f - Tolerance),
                "The slide capsule base must not drop below the baseline capsule base.");
            Assert.That(slide.Center.y + slide.Height * 0.5f,
                Is.LessThanOrEqualTo(baseline.Center.y + baseline.Height * 0.5f + Tolerance),
                "The slide capsule top must stay inside the baseline capsule, which is what makes " +
                "Safe_Collider_Restoration a question about the baseline volume alone.");
        }

        // **Validates: Requirements 12.12, 15.1, 15.2, 15.4, 15.5**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the authored configuration and the serialized safe defaults each form a valid configuration")]
        public void ConfiguredValuesAndSafeDefaultsAreValidConfigurations_Requirements_12_12_15_1_15_2_15_4_15_5()
        {
            var asset = LoadConfigurationAsset();
            var configured = asset.ConfiguredConfiguration;
            var safeDefaults = asset.SafeDefaultConfiguration;

            Assert.That(PlayerConfigurationValidator.IsValid(safeDefaults), Is.True,
                "Safe_Default_Configuration must itself satisfy every constraint, otherwise a single " +
                "invalid authored field turns into ConfigurationStatus.Fatal instead of a repaired " +
                "field. Serialized safe defaults: " + Render(safeDefaults));
            Assert.That(PlayerConfigurationValidator.IsValid(configured), Is.True,
                "The authored configuration must be valid so the shipped prefab reports valid status " +
                "and preserves its configured values. Authored configuration: " + Render(configured));

            var root = LoadPrefabRoot();
            var references = ReferencesFromPrefab(root);
            var result = PlayerConfigurationValidator.Validate(
                configured, safeDefaults, references, references,
                ConfigurationValidationPhase.BeforeSimulationStarted);

            Assert.That(result.Status, Is.EqualTo(ConfigurationStatus.Valid),
                "Validating the authored assets must report valid status. Diagnostics: " +
                RenderDiagnostics(result.Diagnostics));
            Assert.That(result.Diagnostics, Is.Empty,
                "A valid authored configuration must produce no Validation_Diagnostic. Diagnostics: " +
                RenderDiagnostics(result.Diagnostics));
            Assert.That(result.EffectiveConfiguration, Is.EqualTo(configured),
                "Validation must preserve every authored value rather than substituting a default.");
            Assert.That(result.SimulationEnabled, Is.True,
                "A valid authored configuration must leave movement simulation enabled.");

            Assert.That(configured.ForwardSpeed, Is.GreaterThan(0f),
                "The authored Forward_Run_Speed must be positive so the prefab runs automatically " +
                "without an external Speed_Set_Request.");
            Assert.That(configured.LaneCenters.y,
                Is.GreaterThan(configured.LaneCenters.x).And.LessThan(configured.LaneCenters.z),
                "The authored Lane_Centers must be strictly increasing Left, Center, Right values so " +
                "the player starts at the Center Lane_Center. Lane centers " +
                Render(configured.LaneCenters) + ".");
        }

        // **Validates: Requirements 15.7**
        [Test]
        [Description("Feature: player-controller, Edit Mode: every validated configuration field documents its fallback alongside the serialized safe defaults")]
        public void SafeDefaultsDocumentEveryValidatedConfigurationField_Requirement_15_7()
        {
            var undocumented = new List<string>();
            var unserialized = new List<string>();
            var fields = typeof(SerializedPlayerConfiguration).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                if (Attribute.IsDefined(field, typeof(NonSerializedAttribute))) continue;
                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)))
                {
                    unserialized.Add(field.FieldType.Name + " " + field.Name);
                    continue;
                }

                var tooltip = (TooltipAttribute)Attribute.GetCustomAttribute(
                    field, typeof(TooltipAttribute));
                if (tooltip == null || string.IsNullOrWhiteSpace(tooltip.tooltip))
                    undocumented.Add(field.FieldType.Name + " " + field.Name);
            }

            Assert.That(unserialized, Is.Empty,
                "Every configuration field converted into validated domain configuration must be " +
                "serialized so the authored asset is the single source. Unserialized fields: " +
                string.Join(", ", unserialized));
            Assert.That(undocumented, Is.Empty,
                "Safe_Default_Configuration must document the fallback value or reference source for " +
                "every validated configuration field, and the established mechanism for that is a " +
                "Tooltip on the serialized field. Undocumented fields: " +
                string.Join(", ", undocumented));

            AssertDocumentedAssetField("configured");
            AssertDocumentedAssetField("safeDefaults");
        }

        // **Validates: Requirements 11.1, 11.7, 12.12, 15.1, 15.6, 15.8**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the prefab's serialized references name the configuration asset, the read-only input actions, the camera target, the animation receiver, and the configuration consumers")]
        public void PrefabSerializedReferencesAreAssigned_Requirements_11_1_11_7_12_12_15_1_15_6_15_8()
        {
            var root = LoadPrefabRoot();
            var facade = root.GetComponent<PlayerControllerFacade>();
            Assert.That(facade, Is.Not.Null,
                "The Player_Prefab root must carry the facade that owns configuration. " +
                Describe(root));

            var asset = LoadConfigurationAsset();
            Assert.That(SerializedReference(facade, "configurationAsset"), Is.SameAs(asset),
                "The prefab must reference the player-owned configuration asset so the prefab and " +
                "the tests share one validated source. " + Describe(root));

            var inputActions = AssetDatabase.LoadMainAssetAtPath(InputActionAssetPath);
            Assert.That(inputActions, Is.Not.Null,
                "The existing action asset must remain resolvable at " + InputActionAssetPath + ".");
            Assert.That(SerializedReference(facade, "inputActionAsset"), Is.SameAs(inputActions),
                "The prefab must reference the existing " + InputActionAssetPath +
                " rather than a copy, so Player/Move, Player/Jump, and Player/Crouch resolve from " +
                "the read-only asset. " + Describe(root));
            Assert.That(SerializedReference(facade, "fallbackInputActionAsset"), Is.SameAs(inputActions),
                "The documented input-asset fallback must resolve to the same existing action " +
                "asset, otherwise a repaired reference is itself invalid and validation turns " +
                "fatal. " + Describe(root));

            AssertPrefabTransformReference(facade, root, "cameraTarget");
            AssertPrefabTransformReference(facade, root, "fallbackCameraTarget");
            AssertPrefabAnimationReceiverReference(facade, root, "animationReceiver");
            AssertPrefabAnimationReceiverReference(facade, root, "fallbackAnimationReceiver");

            var consumers = SerializedValue(facade, "configurationConsumers") as MonoBehaviour[];
            Assert.That(consumers, Is.Not.Null,
                "The prefab must serialize the configuration consumers the facade initializes. " +
                Describe(root));
            Assert.That(consumers.Length, Is.GreaterThan(0),
                "No peer component may consume raw serialized configuration on its own, so the " +
                "facade needs at least one registered consumer. " + Describe(root));

            var invalidConsumers = new List<string>();
            var consumerTypes = new List<Type>();
            for (var index = 0; index < consumers.Length; index++)
            {
                var consumer = consumers[index];
                if (consumer == null)
                {
                    invalidConsumers.Add(
                        "empty slot " + index.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                consumerTypes.Add(consumer.GetType());
                if (!(consumer is IPlayerConfigurationConsumer))
                {
                    invalidConsumers.Add(
                        consumer.GetType().FullName + " does not implement " +
                        nameof(IPlayerConfigurationConsumer));
                }

                if (!consumer.transform.IsChildOf(root.transform))
                {
                    invalidConsumers.Add(
                        consumer.GetType().FullName + " lives outside the prefab hierarchy");
                }
            }

            Assert.That(invalidConsumers, Is.Empty,
                "Every serialized configuration consumer must be a component inside the prefab that " +
                "implements the consumer contract. Findings: " +
                string.Join(", ", invalidConsumers) + ". " + Describe(root));
            Assert.That(consumerTypes, Does.Contain(typeof(PlayerAnimationBridge)),
                "The animation bridge must receive facade-controlled initialization so the serialized " +
                "receiver reference is resolved to IAnimationReceiver. " + Describe(root));
            Assert.That(consumerTypes, Does.Contain(RequiredInputAdapterType()),
                "The input adapter must receive facade-controlled initialization so the effective " +
                "input thresholds and action asset arrive from validated configuration. " +
                Describe(root));
        }

        // **Validates: Requirements 11.1, 12.12, 15.8**
        [Test]
        [Description("Feature: player-controller, Edit Mode: the prefab exposes a visual child and a camera target the prefab itself owns")]
        public void PrefabExposesVisualChildAndCameraTarget_Requirements_11_1_12_12_15_8()
        {
            var root = LoadPrefabRoot();
            var facade = root.GetComponent<PlayerControllerFacade>();
            Assert.That(facade, Is.Not.Null,
                "The Player_Prefab root must carry the facade. " + Describe(root));

            var cameraTarget = SerializedReference(facade, "cameraTarget") as Transform;
            Assert.That(cameraTarget, Is.Not.Null,
                "The prefab must own the camera target the follow adapter converges on. " +
                Describe(root));

            var visualChildren = new List<string>();
            var children = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (child == root.transform) continue;
                if (child == cameraTarget) continue;
                if (child.GetComponent<Renderer>() == null &&
                    !(child.GetComponent<MonoBehaviour>() is IAnimationReceiver)) continue;

                visualChildren.Add(child.name);
            }

            Assert.That(visualChildren, Is.Not.Empty,
                "The prefab must carry a visual child distinct from the camera target so the player " +
                "is observable and the animation receiver has something to drive. " + Describe(root));
        }

        private static void AssertValidCapsule(ColliderProfile profile, string name)
        {
            Assert.That(profile.Radius, Is.GreaterThan(0f),
                name + " must have a positive radius. Profile " + Render(profile) + ".");
            Assert.That(profile.Height, Is.GreaterThan(0f),
                name + " must have a positive height. Profile " + Render(profile) + ".");
            Assert.That(profile.Height,
                Is.GreaterThanOrEqualTo(2f * profile.Radius - Tolerance),
                name + " must be at least twice as tall as it is wide, otherwise the capsule Unity " +
                "resolves is not the capsule that was authored. Profile " + Render(profile) + ".");
            Assert.That(IsFinite(profile.Center), Is.True,
                name + " must have a finite center. Profile " + Render(profile) + ".");
        }

        private static void AssertDocumentedAssetField(string fieldName)
        {
            var field = typeof(PlayerConfigurationAsset).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "The configuration asset must serialize a field named " + fieldName + ".");
            Assert.That(Attribute.IsDefined(field, typeof(SerializeField)), Is.True,
                fieldName + " must carry [SerializeField] so the authored values survive " +
                "serialization.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(SerializedPlayerConfiguration)),
                fieldName + " must be a serialized player configuration block.");

            var tooltip = (TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute));
            Assert.That(tooltip, Is.Not.Null,
                fieldName + " must document what the block is for, so an author can tell configured " +
                "values from documented fallbacks in the Inspector.");
            Assert.That(tooltip.tooltip, Is.Not.Null.And.Not.Empty,
                fieldName + " must carry a non-empty description.");
        }

        private static void AssertPrefabTransformReference(
            PlayerControllerFacade facade, GameObject root, string fieldName)
        {
            var reference = SerializedReference(facade, fieldName) as Transform;
            Assert.That(reference, Is.Not.Null,
                "The prefab must assign " + fieldName + " so camera following has a player-owned " +
                "target after load. " + Describe(root));
            Assert.That(reference.IsChildOf(root.transform), Is.True,
                fieldName + " must reference a transform inside the prefab, so instantiating the " +
                "prefab remaps it to the instance rather than leaving it pointing at another " +
                "hierarchy. " + Describe(root));
        }

        private static void AssertPrefabAnimationReceiverReference(
            PlayerControllerFacade facade, GameObject root, string fieldName)
        {
            var reference = SerializedReference(facade, fieldName) as MonoBehaviour;
            Assert.That(reference, Is.Not.Null,
                "The prefab must assign " + fieldName + " as a concrete component, because an " +
                "interface-typed field cannot be serialized. " + Describe(root));
            Assert.That(reference is IAnimationReceiver, Is.True,
                fieldName + " must resolve to " + nameof(IAnimationReceiver) + "; " +
                reference.GetType().FullName + " does not implement it. " + Describe(root));
            Assert.That(reference.transform.IsChildOf(root.transform), Is.True,
                fieldName + " must reference a component inside the prefab so the reference is " +
                "remapped on instantiation. " + Describe(root));
        }

        private static PlayerConfigurationReferences ReferencesFromPrefab(GameObject root)
        {
            var facade = root.GetComponent<PlayerControllerFacade>();
            Assert.That(facade, Is.Not.Null,
                "The Player_Prefab root must carry the facade. " + Describe(root));
            return new PlayerConfigurationReferences(
                root,
                SerializedReference(facade, "inputActionAsset"),
                SerializedReference(facade, "cameraTarget"),
                SerializedReference(facade, "animationReceiver"));
        }

        private static GameObject LoadPrefabRoot()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(root, Is.Not.Null,
                "Task 10.2 must create the reusable player prefab at " + PrefabPath + ".");
            return root;
        }

        private static PlayerConfigurationAsset LoadConfigurationAsset()
        {
            var guids = AssetDatabase.FindAssets(
                "t:" + nameof(PlayerConfigurationAsset), new[] { ConfigurationFolder });
            var paths = new List<string>();
            for (var index = 0; index < guids.Length; index++)
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[index]));

            Assert.That(paths.Count, Is.EqualTo(1),
                "Task 10.2 must create exactly one " + nameof(PlayerConfigurationAsset) + " under " +
                ConfigurationFolder + " so the prefab and the tests share one validated source. " +
                "Found: " + (paths.Count == 0 ? "none" : string.Join(", ", paths)));

            var asset = AssetDatabase.LoadAssetAtPath<PlayerConfigurationAsset>(paths[0]);
            Assert.That(asset, Is.Not.Null,
                "The configuration asset at " + paths[0] + " must load as a " +
                nameof(PlayerConfigurationAsset) + ".");
            return asset;
        }

        private static Type RequiredInputAdapterType()
        {
            var type = typeof(PlayerControllerFacade).Assembly.GetType(InputAdapterTypeName);
            Assert.That(type, Is.Not.Null,
                "The runtime assembly must expose " + InputAdapterTypeName + ".");
            return type;
        }

        private static UnityEngine.Object SerializedReference(
            PlayerControllerFacade facade, string fieldName)
        {
            return SerializedValue(facade, fieldName) as UnityEngine.Object;
        }

        private static object SerializedValue(PlayerControllerFacade facade, string fieldName)
        {
            var field = typeof(PlayerControllerFacade).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "The configuration surface must retain the serialized field " + fieldName + ".");
            return field.GetValue(facade);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static string Describe(GameObject root)
        {
            if (root == null) return "[prefab=missing]";

            var components = root.GetComponents<Component>();
            var names = new List<string>();
            for (var index = 0; index < components.Length; index++)
            {
                names.Add(components[index] == null
                    ? "(missing script)"
                    : components[index].GetType().Name);
            }

            return "[prefab=" + root.name + ", rootComponents=" + string.Join("/", names) +
                ", children=" + root.transform.childCount.ToString(CultureInfo.InvariantCulture) + "]";
        }

        private static string RenderDiagnostics(IReadOnlyList<ConfigurationDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return "none";

            var rendered = new List<string>();
            for (var index = 0; index < diagnostics.Count; index++)
            {
                rendered.Add(
                    diagnostics[index].Severity + ":" + diagnostics[index].Field + " (" +
                    diagnostics[index].Constraint + ")");
            }

            return string.Join(", ", rendered);
        }

        private static string Render(ColliderProfile profile)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "radius={0}, height={1}, center={2}",
                profile.Radius.ToString("R", CultureInfo.InvariantCulture),
                profile.Height.ToString("R", CultureInfo.InvariantCulture),
                Render(profile.Center));
        }

        private static string Render(PlayerConfiguration configuration)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[forwardSpeed={0}, jumpVelocity={1}, gravity={2}, laneDuration={3}, " +
                "slideDuration={4}, laneTolerance={5}, laneCenters={6}, baseline=({7}), " +
                "slide=({8}), groundMask={9}, obstructionMask={10}, cameraSettle={11}]",
                configuration.ForwardSpeed.ToString("R", CultureInfo.InvariantCulture),
                configuration.JumpVelocity.ToString("R", CultureInfo.InvariantCulture),
                configuration.GravityAcceleration.ToString("R", CultureInfo.InvariantCulture),
                configuration.LaneChangeDuration.ToString("R", CultureInfo.InvariantCulture),
                configuration.SlideDuration.ToString("R", CultureInfo.InvariantCulture),
                configuration.LanePositionTolerance.ToString("R", CultureInfo.InvariantCulture),
                Render(configuration.LaneCenters),
                Render(configuration.BaselineCollider),
                Render(configuration.SlideCollider),
                configuration.GroundLayerMask.ToString(CultureInfo.InvariantCulture),
                configuration.ObstructionLayerMask.ToString(CultureInfo.InvariantCulture),
                configuration.CameraSettleDuration.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string Render(Vector3 value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
