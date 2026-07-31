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
            sb.AppendLine();

            ReportNamedObjects(sb);
            ReportLaneSpacing(sb);
            ReportGroundCandidates(sb);
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
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
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
            sb.AppendLine("  baseline capsule radius = " + F(config.BaselineRadius) +
                          " height = " + F(config.BaselineHeight) +
                          "  => width = " + F(config.BaselineRadius * 2f));

            var motor = facade.GetComponent<CharacterControllerMotor>();
            sb.AppendLine("  motor track basis = " +
                          (motor == null ? "NO MOTOR" : V(motor.TrackBasis.eulerAngles)));

            var cc = facade.GetComponent<CharacterController>();
            if (cc != null)
            {
                sb.AppendLine("  CharacterController radius=" + F(cc.radius) +
                              " height=" + F(cc.height) + " center=" + V(cc.center) +
                              " isGrounded=" + cc.isGrounded);
            }
            sb.AppendLine();
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

            var follow = Object.FindObjectsByType<PlayerCameraFollow>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
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
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
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
