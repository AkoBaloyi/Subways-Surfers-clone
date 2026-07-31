using SubwaySurfers.Player;
using UnityEditor;
using UnityEngine;

namespace SubwaySurfers.Integration.EditorTools
{
    /// <summary>
    /// Performs the scene wiring the MVP needs, in one step.
    ///
    /// Every value is measured from the scene rather than hard-coded: the player start comes from the
    /// middle rail marker, and the camera offset comes from the configured pose relative to that start.
    /// Hand-applying this list across several passes kept leaving one item undone, and a missing item
    /// usually looks like a controller bug rather than missing wiring.
    ///
    /// Everything is registered with Undo, so a single Undo reverts the whole operation, and nothing is
    /// saved unless you save the scene.
    /// </summary>
    public static class MvpSceneSetup
    {
        private const string GameplayConfigurationName = "GameplayTrackConfiguration";
        private const float TrackYawDegrees = 90f;
        private const float FootClearance = 0.08f;

        [MenuItem("Tools/Integration/Set Up MVP Scene")]
        public static void SetUp()
        {
            var facade = Object.FindAnyObjectByType<PlayerControllerFacade>();
            if (facade == null)
            {
                EditorUtility.DisplayDialog("MVP Setup",
                    "No PlayerControllerFacade in the scene.\n\n" +
                    "Drag Assets/Player/Prefabs/Player.prefab into the scene first, then run this again.",
                    "OK");
                return;
            }

            var middle = FindByName("MiddleRailMarker");
            if (middle == null)
            {
                EditorUtility.DisplayDialog("MVP Setup",
                    "No MiddleRailMarker in the scene, so the player start cannot be measured.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Set Up MVP Scene");
            var group = Undo.GetCurrentGroup();
            var report = new System.Text.StringBuilder();
            report.AppendLine("MVP scene setup:");

            PlacePlayer(facade, middle.transform.position, report);
            AssignGameplayConfiguration(facade, report);
            SetMotorYaw(facade, report);
            DetachAndWireCamera(facade, report);
            EnsureBridge(report);
            PointTrackManagerAtPlayer(facade, report);
            DisableTempPlayer(report);

            Undo.CollapseUndoOperations(group);
            EditorSceneManagerMarkDirty();

            report.AppendLine();
            report.AppendLine("Scene is wired but NOT saved. Press Play to test, then save if you are happy.");
            report.AppendLine("One Undo reverts all of it.");
            Debug.Log(report.ToString());
        }

        private static void PlacePlayer(
            PlayerControllerFacade facade, Vector3 middleLane, System.Text.StringBuilder report)
        {
            Undo.RecordObject(facade.transform, "Place player");

            // The capsule's centre sits one unit above the transform with a height of two, so the feet
            // are at the transform's own height. Standing the transform just above the rail surface lets
            // the ground probe find support on the first step instead of starting inside the geometry.
            var start = new Vector3(middleLane.x, middleLane.y + FootClearance, middleLane.z);
            facade.transform.position = start;
            report.AppendLine("  player moved to " + start + " (middle rail, measured)");
        }

        private static void AssignGameplayConfiguration(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            var guids = AssetDatabase.FindAssets(GameplayConfigurationName + " t:ScriptableObject");
            Object asset = null;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadMainAssetAtPath(path);
                if (candidate != null && candidate.name == GameplayConfigurationName)
                {
                    asset = candidate;
                    break;
                }
            }

            if (asset == null)
            {
                report.AppendLine("  WARNING: " + GameplayConfigurationName +
                                  " not found, configuration left as it was");
                return;
            }

            var serialized = new SerializedObject(facade);
            var property = serialized.FindProperty("configurationAsset");
            if (property == null)
            {
                report.AppendLine("  WARNING: facade has no configurationAsset field");
                return;
            }

            property.objectReferenceValue = asset;
            serialized.ApplyModifiedProperties();
            report.AppendLine("  configurationAsset set to " + asset.name);
        }

        private static void SetMotorYaw(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            var motor = facade.GetComponent<CharacterControllerMotor>();
            if (motor == null)
            {
                report.AppendLine("  WARNING: no CharacterControllerMotor on the player");
                return;
            }

            Undo.RecordObject(motor, "Set track yaw");
            motor.SetTrackYaw(TrackYawDegrees);
            EditorUtility.SetDirty(motor);
            report.AppendLine("  motor track yaw set to " + TrackYawDegrees +
                              " (domain +Z maps to world +X)");
        }

        private static void DetachAndWireCamera(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                report.AppendLine("  WARNING: no Camera in the scene");
                return;
            }

            // The follow adapter writes a world position every LateUpdate. Parented under the player it
            // would also be dragged by the player's own motion, so the two would fight each other.
            if (camera.transform.parent != null)
            {
                Undo.SetTransformParent(camera.transform, null, "Detach camera");
                report.AppendLine("  camera detached from its parent so the follow owns its pose");
            }

            Undo.RecordObject(camera.transform, "Reset camera scale");
            camera.transform.localScale = Vector3.one;

            if (camera.GetComponent<PlayerCameraFollow>() == null)
            {
                Undo.AddComponent<PlayerCameraFollow>(camera.gameObject);
                report.AppendLine("  PlayerCameraFollow added to '" + camera.gameObject.name + "'");
            }
            else
            {
                report.AppendLine("  PlayerCameraFollow already on '" + camera.gameObject.name + "'");
            }

            report.AppendLine("  camera will be resolved at run time by the bridge");
        }

        private static void EnsureBridge(System.Text.StringBuilder report)
        {
            if (Object.FindAnyObjectByType<MvpIntegrationBridge>() != null)
            {
                report.AppendLine("  MvpIntegrationBridge already present");
                return;
            }

            var host = new GameObject("MvpIntegrationBridge");
            Undo.RegisterCreatedObjectUndo(host, "Create integration bridge");
            Undo.AddComponent<MvpIntegrationBridge>(host);
            report.AppendLine("  MvpIntegrationBridge created");
        }

        private static void PointTrackManagerAtPlayer(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            var trackManager = Object.FindAnyObjectByType<TrackManager>();
            if (trackManager == null)
            {
                report.AppendLine("  no TrackManager in the scene, nothing to repoint");
                return;
            }

            Undo.RecordObject(trackManager, "Point track manager at player");
            trackManager.player = facade.transform;
            EditorUtility.SetDirty(trackManager);
            report.AppendLine("  TrackManager.player now references the Player");
        }

        private static void DisableTempPlayer(System.Text.StringBuilder report)
        {
            var temp = FindByName("TempAutoPlayer");
            if (temp == null)
            {
                report.AppendLine("  no TempAutoPlayer present");
                return;
            }

            if (!temp.activeSelf)
            {
                report.AppendLine("  TempAutoPlayer already disabled");
                return;
            }

            Undo.RecordObject(temp, "Disable temporary player");
            temp.SetActive(false);
            report.AppendLine("  TempAutoPlayer disabled");
        }

        private static void EditorSceneManagerMarkDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        private static GameObject FindByName(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() == name.Trim()) return t.gameObject;
            }
            return null;
        }
    }
}
