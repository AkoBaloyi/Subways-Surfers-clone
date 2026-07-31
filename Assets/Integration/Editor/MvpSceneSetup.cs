using SubwaySurfers.Player;
using UnityEditor;
using UnityEngine;

namespace SubwaySurfers.Integration.EditorTools
{
    /// <summary>
    /// Wires the MVP scene by copying the setup that already worked.
    ///
    /// TEMP_AutoRunner drove this scene successfully with a very plain arrangement: the player at a
    /// specific pose on the middle rail, translating along world +X, with the camera parented to it at a
    /// fixed offset and yawed to look down the track. That arrangement is the reference. Rather than
    /// deriving poses from rail geometry and imposing a smoothing camera, this reads the temporary
    /// runner's own transform and reproduces it for the real player.
    ///
    /// Where the real controller differs is only in how displacement is produced: the motor's track yaw
    /// maps its domain forward onto world +X, which is the same direction TEMP_AutoRunner translated.
    ///
    /// Everything is one Undo group and the scene is left unsaved.
    /// </summary>
    public static class MvpSceneSetup
    {
        private const string GameplayConfigurationName = "GameplayTrackConfiguration";
        private const float TrackYawDegrees = 90f;

        /// <summary>
        /// Gap left between the capsule's feet and the measured surface. Small enough to settle in a
        /// step or two, large enough that the capsule never starts intersecting the surface it stands on.
        /// </summary>
        private const float SpawnClearance = 0.15f;

        /// <summary>
        /// Fallback pose and camera rig, matching what TEMP_AutoRunner and its child camera used, for
        /// the case where the temporary runner has already been deleted from the scene.
        /// </summary>
        private static readonly Vector3 ReferencePlayerPosition = new Vector3(-76.170f, 2.993f, -0.241f);
        private static readonly Vector3 ReferenceCameraLocalPosition = new Vector3(-4.470f, 1.430f, 0.030f);
        private static readonly Vector3 ReferenceCameraLocalEuler = new Vector3(0f, 90f, 0f);

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

            Undo.SetCurrentGroupName("Set Up MVP Scene");
            var group = Undo.GetCurrentGroup();
            var report = new System.Text.StringBuilder();
            report.AppendLine("MVP scene setup, copied from the TEMP_AutoRunner arrangement:");

            var temp = FindByName("TempAutoPlayer");
            PlacePlayerLikeTempRunner(facade, temp, report);
            AssignGameplayConfiguration(facade, report);
            SetMotorYaw(facade, report);
            ParentCameraLikeTempRunner(facade, temp, report);
            EnsureBridge(report);
            PointTrackManagerAtPlayer(facade, report);
            DisableTempPlayer(temp, report);

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            report.AppendLine();
            report.AppendLine("Scene wired but NOT saved. Press Play, then save if you are happy.");
            report.AppendLine("One Undo reverts all of it.");
            Debug.Log(report.ToString());
        }

        private static void PlacePlayerLikeTempRunner(
            PlayerControllerFacade facade, GameObject temp, System.Text.StringBuilder report)
        {
            Undo.RecordObject(facade.transform, "Place player");

            // The rail marker is the authored reference for lane and height: it sits on the rail, at the
            // middle lane. Using it directly avoids both traps hit earlier - a downward raycast that
            // lands on a train roof, and the pose of a runtime clone hundreds of units down the track.
            // Placing the player here at edit time means the scene is correct before play even starts,
            // so it is verifiable in the Inspector rather than only through a runtime log.
            var marker = FindAuthored("MiddleRailMarker");
            Vector3 pose;

            if (marker != null)
            {
                var m = marker.transform.position;
                pose = new Vector3(m.x, m.y + SpawnClearance, m.z);
                report.AppendLine("  start taken from authored marker '" + marker.name + "' on '" +
                                  (marker.transform.parent == null
                                      ? "(no parent)"
                                      : marker.transform.parent.name) + "'");
            }
            else
            {
                var reference = temp == null ? ReferencePlayerPosition : temp.transform.position;
                pose = reference;
                report.AppendLine("  WARNING: no MiddleRailMarker found, falling back to " +
                                  (temp == null ? "the recorded pose" : "TempAutoPlayer's pose"));
            }

            facade.transform.position = pose;
            facade.transform.rotation = Quaternion.identity;
            report.AppendLine("  player placed at " + pose +
                              "  <-- check this in the Inspector; z should be about -0.36");
        }

        private static void ParentCameraLikeTempRunner(
            PlayerControllerFacade facade, GameObject temp, System.Text.StringBuilder report)
        {
            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                report.AppendLine("  WARNING: no Camera in the scene");
                return;
            }

            // Read the rig the temporary runner used, if its camera is still attached, so the framing is
            // the one already seen working rather than a guess.
            var localPosition = ReferenceCameraLocalPosition;
            var localEuler = ReferenceCameraLocalEuler;
            if (temp != null && camera.transform.IsChildOf(temp.transform))
            {
                localPosition = camera.transform.localPosition;
                localEuler = camera.transform.localEulerAngles;
                report.AppendLine("  camera rig read from the temporary runner");
            }

            // The follow adapter writes a world pose every LateUpdate, which fights a parent that is
            // also moving the camera. Rigid parenting is what worked here, so the adapter steps aside.
            var follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow != null)
            {
                Undo.RecordObject(follow, "Disable camera follow");
                follow.enabled = false;
                report.AppendLine("  PlayerCameraFollow disabled: the camera is rigidly parented " +
                                  "instead, matching the arrangement that worked");
            }

            Undo.SetTransformParent(camera.transform, facade.transform, "Parent camera to player");
            Undo.RecordObject(camera.transform, "Place camera");
            camera.transform.localPosition = localPosition;
            camera.transform.localEulerAngles = localEuler;
            camera.transform.localScale = Vector3.one;

            if (!camera.gameObject.activeSelf)
            {
                Undo.RecordObject(camera.gameObject, "Enable camera");
                camera.gameObject.SetActive(true);
                report.AppendLine("  camera re-enabled");
            }

            report.AppendLine("  camera parented to the player at local " + localPosition +
                              " yaw " + localEuler.y);
        }

        private static void AssignGameplayConfiguration(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            Object asset = null;
            foreach (var guid in AssetDatabase.FindAssets(GameplayConfigurationName))
            {
                var candidate = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.name == GameplayConfigurationName)
                {
                    asset = candidate;
                    break;
                }
            }

            if (asset == null)
            {
                report.AppendLine("  WARNING: " + GameplayConfigurationName + " not found");
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
            report.AppendLine("  motor track yaw " + TrackYawDegrees +
                              ", so forward displacement lands on world +X like Vector3.right did");
        }

        private static void EnsureBridge(System.Text.StringBuilder report)
        {
            var bridge = Object.FindAnyObjectByType<MvpIntegrationBridge>();
            if (bridge == null)
            {
                var host = new GameObject("MvpIntegrationBridge");
                Undo.RegisterCreatedObjectUndo(host, "Create integration bridge");
                bridge = Undo.AddComponent<MvpIntegrationBridge>(host);
                report.AppendLine("  MvpIntegrationBridge created");
            }
            else
            {
                report.AppendLine("  MvpIntegrationBridge already present");
                if (!bridge.gameObject.activeSelf)
                {
                    Undo.RecordObject(bridge.gameObject, "Enable bridge");
                    bridge.gameObject.SetActive(true);
                    report.AppendLine("  bridge was DISABLED and has been enabled");
                }
            }

            // A component saved before a serialized field existed can deserialize that field as the
            // type's default rather than its initializer, which silently switches behaviour off. Writing
            // the values explicitly removes that as a possibility.
            var serialized = new SerializedObject(bridge);
            SetBool(serialized, "snapToLaneOnStart", true, report);
            SetBool(serialized, "markGroundAsRunningSurface", true, report);
            SetBool(serialized, "logSummary", true, report);
            serialized.ApplyModifiedProperties();
        }

        private static void SetBool(
            SerializedObject serialized, string field, bool value, System.Text.StringBuilder report)
        {
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                report.AppendLine("  WARNING: bridge has no field '" + field + "'");
                return;
            }

            if (property.boolValue == value) return;

            property.boolValue = value;
            report.AppendLine("  bridge." + field + " was " + !value + ", set to " + value);
        }

        private static void PointTrackManagerAtPlayer(
            PlayerControllerFacade facade, System.Text.StringBuilder report)
        {
            var trackManager = Object.FindAnyObjectByType<TrackManager>();
            if (trackManager == null)
            {
                report.AppendLine("  no TrackManager in the scene");
                return;
            }

            Undo.RecordObject(trackManager, "Point track manager at player");
            trackManager.player = facade.transform;
            EditorUtility.SetDirty(trackManager);
            report.AppendLine("  TrackManager.player now references the Player, so spawning follows it");
        }

        private static void DisableTempPlayer(GameObject temp, System.Text.StringBuilder report)
        {
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

        private static GameObject FindByName(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() == name.Trim()) return t.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Finds the authored object of this name, skipping runtime clones. In edit mode there should be
        /// no clones at all, but running setup while the game is playing would otherwise pick a spawned
        /// segment that has already been moved down the track.
        /// </summary>
        private static GameObject FindAuthored(string name)
        {
            GameObject clone = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() != name.Trim()) continue;

                var isClone = false;
                for (var walk = t; walk != null; walk = walk.parent)
                {
                    if (!walk.gameObject.name.EndsWith("(Clone)", System.StringComparison.Ordinal)) continue;
                    isClone = true;
                    break;
                }

                if (isClone)
                {
                    if (clone == null) clone = t.gameObject;
                    continue;
                }

                return t.gameObject;
            }

            return clone;
        }
    }
}
