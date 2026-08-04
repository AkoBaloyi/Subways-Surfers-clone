using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using UnityEditor;
using UnityEngine;

namespace SubwaySurfers.Integration.EditorTools
{
    /// <summary>
    /// Reports the world truth of the gameplay scene.
    ///
    /// Scene files only store local transforms, so any parent rotation or scale makes them misleading.
    /// This reads the live hierarchy instead: real world positions, real lane spacing, real collider
    /// layers, and whether the pieces that must reference each other actually do. Run it, paste the
    /// output, and integration decisions rest on measurements rather than inference.
    ///
    /// Editor-only and read-only. It changes nothing.
    /// </summary>
    public static class TrackDiagnostics
    {
        [MenuItem("Tools/Integration/Report Track And Player Truth")]
        public static void Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TRACK AND PLAYER TRUTH ===");
            sb.AppendLine("scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            sb.AppendLine("play mode: " + Application.isPlaying);
            if (!Application.isPlaying)
            {
                sb.AppendLine("NOTE: not in play mode, so Awake has not run and the player has not");
                sb.AppendLine("      validated its configuration yet. Configuration status will read");
                sb.AppendLine("      Fatal with simulation disabled and zero diagnostics, and the");
                sb.AppendLine("      reported configuration will be the safe defaults rather than the");
                sb.AppendLine("      authored asset. That is an artefact of edit mode, not a defect.");
                sb.AppendLine("      Enter play mode and run this again to judge the configuration.");
            }
            sb.AppendLine();

            ReportNamedObjects(sb);
            ReportLaneSpacing(sb);
            ReportGroundCandidates(sb);
            ReportObstacles(sb);
            ReportPlayer(sb);
            ReportWiring(sb);

            Debug.Log(sb.ToString());
        }

        private static readonly string[] Interesting =
        {
            "LeftRaillMarker", "MiddleRailMarker", "RightRailMarker", "Middle Rail",
            "Ground", "Terrain", "TempAutoPlayer", "Main Camera", "TrackRoot"
        };

        private static void ReportNamedObjects(StringBuilder sb)
        {
            sb.AppendLine("--- key objects, WORLD space ---");
            foreach (var name in Interesting)
            {
                var found = FindByName(name);
                if (found == null)
                {
                    sb.AppendLine(Pad(name) + "NOT FOUND");
                    continue;
                }

                var t = found.transform;
                sb.AppendLine(Pad(name) +
                              "pos=" + V(t.position) +
                              " scale=" + V(t.lossyScale) +
                              " rot=" + V(t.eulerAngles) +
                              " layer=" + LayerMask.LayerToName(found.layer) +
                              " active=" + found.activeInHierarchy +
                              " parent=" + (t.parent == null ? "(none)" : t.parent.name));
            }
            sb.AppendLine();
        }

        private static void ReportLaneSpacing(StringBuilder sb)
        {
            sb.AppendLine("--- lane spacing, measured ---");
            var left = FindByName("LeftRaillMarker");
            var middle = FindByName("MiddleRailMarker");
            var right = FindByName("RightRailMarker");
            if (left == null || middle == null || right == null)
            {
                sb.AppendLine("  one or more rail markers missing; cannot measure");
                sb.AppendLine();
                return;
            }

            var lp = left.transform.position;
            var mp = middle.transform.position;
            var rp = right.transform.position;

            sb.AppendLine("  left  world = " + V(lp));
            sb.AppendLine("  middle world = " + V(mp));
            sb.AppendLine("  right world = " + V(rp));
            sb.AppendLine("  |left-middle| on X = " + F(Mathf.Abs(lp.x - mp.x)) +
                          "   on Z = " + F(Mathf.Abs(lp.z - mp.z)));
            sb.AppendLine("  |middle-right| on X = " + F(Mathf.Abs(mp.x - rp.x)) +
                          "   on Z = " + F(Mathf.Abs(mp.z - rp.z)));
            sb.AppendLine("  => lanes are separated on the axis with the larger numbers above.");
            sb.AppendLine();
        }

        private static void ReportGroundCandidates(StringBuilder sb)
        {
            sb.AppendLine("--- colliders the player could stand on ---");
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
            sb.AppendLine("  total active colliders in scene: " + colliders.Length);

            var byLayer = new Dictionary<int, int>();
            var nonTrigger = 0;
            var withMarker = 0;
            foreach (var c in colliders)
            {
                if (c.isTrigger) continue;
                nonTrigger++;
                var layer = c.gameObject.layer;
                byLayer.TryGetValue(layer, out var count);
                byLayer[layer] = count + 1;
                if (c.GetComponent<IRunningSurface>() != null) withMarker++;
            }

            sb.AppendLine("  non-trigger colliders: " + nonTrigger);
            sb.AppendLine("  already carrying IRunningSurface: " + withMarker);
            foreach (var pair in byLayer)
            {
                sb.AppendLine("    layer " + pair.Key + " (" +
                              LayerMask.LayerToName(pair.Key) + "): " + pair.Value);
            }

            var ground = FindByName("Ground");
            var terrain = FindByName("Terrain");
            foreach (var candidate in new[] { ground, terrain })
            {
                if (candidate == null) continue;
                var c = candidate.GetComponent<Collider>();
                sb.AppendLine("  " + candidate.name + " collider: " +
                              (c == null ? "NONE - nothing to stand on" : c.GetType().Name) +
                              (c == null ? "" : " bounds=" + c.bounds.center.ToString("F2") +
                                                " size=" + c.bounds.size.ToString("F2")));
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Names identifying authored obstacles and collectibles in this project. Taken from the prefabs
        /// under Assets/Prefab/Obstacles and the coin groups inside the track segment, so the report
        /// covers what the segment actually contains rather than a guess at conventions.
        /// </summary>
        private static readonly string[] ObstacleNames =
        {
            "Block_Barrier", "Pedestrian_Barrier", "Water_Barricade", "Slide Barricade",
            "Obstacle Left", "Obstacle Middle", "Obstacle Right",
            "Coin Left", "Coin Middle", "Coin Right", "Train_Type"
        };

        /// <summary>
        /// Reports whether the things the player is meant to hit can be hit at all.
        ///
        /// Three conditions have to hold together, and each one silently produces the same symptom of
        /// nothing happening. There must be a collider, because a contact is found by overlapping the
        /// player capsule and geometry without a collider is not there as far as physics is concerned.
        /// There must be an environment identity on the object or an ancestor, because a contact without
        /// one is discarded before it becomes an event. And geometry meant to be slid under needs an
        /// obstruction marker, because that marker is the only reason the restoration query refuses to
        /// stand the player back up.
        ///
        /// The obstacle prefabs in this project are imported with Generate Colliders off and add no
        /// collider of their own, so as authored the first condition fails for every one of them.
        /// </summary>
        private static void ReportObstacles(StringBuilder sb)
        {
            sb.AppendLine("--- obstacles and collectibles: can they be hit? ---");

            var reported = 0;
            var withoutCollider = 0;
            var withoutIdentity = 0;

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                var candidate = t.gameObject;
                if (!MatchesAny(candidate.name, ObstacleNames)) continue;

                var colliders = candidate.GetComponentsInChildren<Collider>(true).Length;
                var meshes = candidate.GetComponentsInChildren<MeshFilter>(true).Length;
                var identity = candidate.GetComponentInParent<IEnvironmentObject>();
                var obstruction = candidate.GetComponentInParent<IEnvironmentObstruction>() != null;

                if (colliders == 0) withoutCollider++;
                if (identity == null) withoutIdentity++;

                reported++;
                if (reported > 16) continue;

                sb.AppendLine("  " + candidate.name.Trim() +
                              " active=" + candidate.activeInHierarchy +
                              " layer=" + candidate.layer +
                              " colliders=" + colliders +
                              " meshes=" + meshes +
                              " identity=" + (identity == null ? "NONE" : identity.EnvironmentObjectId +
                                                                          "/" + identity.Kind) +
                              " obstruction=" + obstruction +
                              " pos=" + V(t.position));
            }

            if (reported == 0)
            {
                sb.AppendLine("  none found. In edit mode the track has not spawned yet, so this is");
                sb.AppendLine("  expected; enter play mode and run the report again.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("  matched " + reported + " objects" +
                          (reported > 16 ? " (first 16 listed)" : "") +
                          ", of which " + withoutCollider + " have no collider and " +
                          withoutIdentity + " have no environment identity.");

            if (withoutCollider > 0)
            {
                sb.AppendLine("  => the ones without a collider cannot raise a hit and cannot block");
                sb.AppendLine("     standing up from a slide. Enable repairObstacleColliders on");
                sb.AppendLine("     MvpIntegrationBridge, or turn on Generate Colliders in the obstacle");
                sb.AppendLine("     model importers.");
            }

            sb.AppendLine();
        }

        private static bool MatchesAny(string name, string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                if (name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static void ReportPlayer(StringBuilder sb)
        {
            sb.AppendLine("--- player ---");
            var facade = Object.FindAnyObjectByType<PlayerControllerFacade>();
            if (facade == null)
            {
                sb.AppendLine("  NO PlayerControllerFacade in the scene. The prefab is not instantiated.");
                sb.AppendLine();
                return;
            }

            var config = facade.EffectiveConfiguration;
            sb.AppendLine("  world pos = " + V(facade.transform.position));
            sb.AppendLine("  state = " + facade.CurrentState +
                          "  grounded = " + facade.IsGrounded +
                          "  speed = " + F(facade.ForwardSpeed));
            sb.AppendLine("  config status = " + facade.ConfigurationStatus +
                          "  simulation = " + facade.SimulationEnabled +
                          "  diagnostics = " + facade.ConfigurationDiagnostics.Count);
            foreach (var d in facade.ConfigurationDiagnostics) sb.AppendLine("    diag: " + d);

            sb.AppendLine("  laneCenters = " + V(config.LaneCenters) +
                          "  tolerance = " + F(config.LanePositionTolerance));
            sb.AppendLine("  groundLayerMask bits = " + config.GroundLayerMask + " -> layers: " +
                          DescribeMask(config.GroundLayerMask));
            sb.AppendLine("  baseline capsule radius = " + F(config.BaselineCollider.Radius) +
                          " height = " + F(config.BaselineCollider.Height) +
                          "  => width = " + F(config.BaselineCollider.Radius * 2f));
            sb.AppendLine("  slide capsule    radius = " + F(config.SlideCollider.Radius) +
                          " height = " + F(config.SlideCollider.Height));
            sb.AppendLine("  initialCameraPosition = " + V(config.InitialCameraPosition));

            var motor = facade.GetComponent<CharacterControllerMotor>();
            sb.AppendLine("  motor track basis = " +
                          (motor == null ? "NO MOTOR" : V(motor.TrackBasis.eulerAngles)));

            // Outside play mode the effective configuration is the safe defaults, which are identical
            // across assets and therefore say nothing about which asset is assigned. Read the
            // serialized reference directly so the assignment is visible without entering play mode.
            var serialized = new SerializedObject(facade);
            var assetProperty = serialized.FindProperty("configurationAsset");
            sb.AppendLine("  assigned configurationAsset = " +
                          (assetProperty == null || assetProperty.objectReferenceValue == null
                              ? "NONE - the facade will fall back to safe defaults"
                              : assetProperty.objectReferenceValue.name));

            var cc = facade.GetComponent<CharacterController>();
            if (cc != null)
            {
                sb.AppendLine("  CharacterController radius=" + F(cc.radius) +
                              " height=" + F(cc.height) + " center=" + V(cc.center) +
                              " isGrounded=" + cc.isGrounded);
                ReportCapsuleOverlaps(sb, facade, cc);
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Lists geometry the player capsule currently intersects.
        ///
        /// A capsule spawned inside a collider is depenetrated by Unity along the shortest escape route,
        /// which on a track laid out along X means being shoved sideways on Z until it clears or jams.
        /// That reads as the player mysteriously drifting off the lanes, so it is worth measuring rather
        /// than inferring: anything listed here is geometry the player is standing inside, not on.
        /// </summary>
        private static void ReportCapsuleOverlaps(
            StringBuilder sb, PlayerControllerFacade facade, CharacterController cc)
        {
            var centre = facade.transform.TransformPoint(cc.center);
            var half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            var up = facade.transform.up * half;
            var top = centre + up;
            var bottom = centre - up;

            var hits = Physics.OverlapCapsule(bottom, top, cc.radius, ~0,
                QueryTriggerInteraction.Ignore);

            var offenders = 0;
            sb.AppendLine("  capsule world centre = " + V(centre) +
                          "  segment " + V(bottom) + " to " + V(top));
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (hit.transform.IsChildOf(facade.transform)) continue;

                offenders++;
                if (offenders <= 12)
                {
                    sb.AppendLine("    INTERSECTING: " + hit.gameObject.name +
                                  " (" + hit.GetType().Name + ") at " + V(hit.bounds.center) +
                                  " size " + V(hit.bounds.size));
                }
            }

            sb.AppendLine("  colliders intersecting the capsule: " + offenders +
                          (offenders == 0
                              ? "  (clear, so depenetration is not moving the player)"
                              : "  <-- the player is spawned INSIDE geometry and will be pushed out"));

            // How far above the nearest surface the capsule's feet sit. Negative means the feet are
            // below the surface, which is the penetration case.
            RaycastHit ground;
            if (Physics.Raycast(centre, Vector3.down, out ground, 50f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                var feet = facade.transform.position.y;
                sb.AppendLine("  nearest surface below centre: " + ground.collider.gameObject.name +
                              " at y=" + F(ground.point.y) +
                              "  feet y=" + F(feet) +
                              "  clearance=" + F(feet - ground.point.y));
            }
            else
            {
                sb.AppendLine("  no surface found within 50 units below the capsule centre");
            }
        }

        private static void ReportWiring(StringBuilder sb)
        {
            sb.AppendLine("--- wiring that must not be empty ---");

            var trackManager = Object.FindAnyObjectByType<TrackManager>();
            if (trackManager == null)
            {
                sb.AppendLine("  TrackManager: not in scene");
            }
            else
            {
                sb.AppendLine("  TrackManager.player = " +
                              (trackManager.player == null
                                  ? "NULL - spawning will throw every frame"
                                  : trackManager.player.name + " at " + V(trackManager.player.position)));
            }

            sb.AppendLine("  GameManager.Instance present = " + (GameManager.Instance != null));

            var bridge = Object.FindAnyObjectByType<MvpIntegrationBridge>();
            sb.AppendLine("  MvpIntegrationBridge in scene = " + (bridge != null));

            var follow = Object.FindObjectsByType<PlayerCameraFollow>(FindObjectsInactive.Include);
            sb.AppendLine("  PlayerCameraFollow count = " + follow.Length);
            foreach (var f in follow)
            {
                sb.AppendLine("    on '" + f.gameObject.name + "' resolved=" + f.PlayerResolved);
            }

            var temp = FindByName("TempAutoPlayer");
            if (temp != null)
            {
                sb.AppendLine("  TempAutoPlayer active = " + temp.activeInHierarchy +
                              (temp.activeInHierarchy ? "  <-- DISABLE THIS" : "  (correctly disabled)"));
            }
        }

        private static string DescribeMask(int mask)
        {
            var names = new List<string>();
            for (var i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                var n = LayerMask.LayerToName(i);
                names.Add(i + (string.IsNullOrEmpty(n) ? "" : ":" + n));
            }
            return names.Count == 0 ? "(empty)" : string.Join(", ", names);
        }

        private static GameObject FindByName(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.gameObject.name.Trim() == name.Trim()) return t.gameObject;
            }
            return null;
        }

        private static string Pad(string s) { return (s + ":").PadRight(20); }

        private static string V(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        }

        private static string F(float f)
        {
            return f.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
