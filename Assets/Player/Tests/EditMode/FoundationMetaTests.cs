using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class FoundationMetaTests
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string PlayerRoot => Path.Combine(Application.dataPath, "Player");

        [Test]
        public void SupportedUnityVersionMatchesSpecification_Requirement_1_1()
        {
            Assert.That(Application.unityVersion, Is.EqualTo("6000.5.4f1"));
        }

        /// <summary>
        /// Installed Unity package assemblies the runtime is permitted to reference. The runtime owns
        /// the Unity-side input adapter, so it must compile against the installed Input System package
        /// that supplies the action asset it reads. Nothing else widens: a test assembly, a production
        /// Rachel or Lucky assembly, or any reference outside this allowlist still fails.
        /// </summary>
        private static readonly string[] AllowedRuntimeReferences = { "Unity.InputSystem" };

        [Test]
        public void RuntimeAssemblyHasNoProductionOrTestReferences_Requirements_1_3_1_4_1_6_14_12()
        {
            var definition = ReadAssemblyDefinition("Runtime/SubwaySurfers.Player.Runtime.asmdef");
            var references = definition.references ?? Array.Empty<string>();
            var forbidden = references
                .Where(reference => !AllowedRuntimeReferences.Contains(reference, StringComparer.Ordinal))
                .ToArray();
            Assert.That(forbidden, Is.Empty,
                "The runtime assembly may reference only the allowlisted installed Unity package " +
                "assemblies it needs to consume engine-side input (" +
                string.Join(", ", AllowedRuntimeReferences) +
                "). Every other reference - and in particular any test assembly or any production " +
                "Rachel or Lucky assembly - stays forbidden so the runtime remains independently " +
                "compilable and free of production coupling. Unexpected references:\n" +
                string.Join("\n", forbidden));
            Assert.That(references.Any(reference =>
                reference.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reference.IndexOf("Rachel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reference.IndexOf("Lucky", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
                "The runtime assembly must never reference a test assembly or a production Rachel or " +
                "Lucky assembly:\n" + string.Join("\n", references));
            Assert.That(definition.includePlatforms ?? Array.Empty<string>(), Is.Empty);
            Assert.That(definition.optionalUnityReferences ?? Array.Empty<string>(), Is.Empty);
            Assert.That(definition.name, Is.EqualTo("SubwaySurfers.Player.Runtime"));
            Assert.That(definition.autoReferenced, Is.True);
        }

        /// <summary>
        /// Player-owned assemblies a test assembly may reference. The runtime is the subject under
        /// test. The validation assembly holds the non-production scene doubles and the test-scene
        /// harness, which must be reachable from the Play Mode scene tests and from PlayerTestScene
        /// itself; it is player-owned, contains no production environment or run-coordination code,
        /// and is never referenced as production content. Nothing else widens: a production Rachel or
        /// Lucky assembly, or a reference from one test assembly to another, still fails.
        /// </summary>
        private static readonly string[] AllowedTestReferences =
        {
            "SubwaySurfers.Player.Runtime",
            "SubwaySurfers.Player.Validation"
        };

        [Test]
        public void TestAssembliesReferenceOnlyPlayerRuntime_Requirements_11_11_12_13()
        {
            AssertTestAssembly("Tests/EditMode/SubwaySurfers.Player.Tests.EditMode.asmdef", true);
            AssertTestAssembly("Tests/PlayMode/SubwaySurfers.Player.Tests.PlayMode.asmdef", false);
        }

        /// <summary>
        /// The validation assembly carries the non-production doubles and harness. It must depend on
        /// the player runtime only, so PlayerTestScene can never drag a production Rachel or Lucky
        /// implementation, or a test assembly, into a scene.
        /// </summary>
        [Test]
        public void ValidationAssemblyDependsOnPlayerRuntimeOnly_Requirements_1_4_11_11_11_13_14_11_14_12()
        {
            var definition = ReadAssemblyDefinition("Validation/SubwaySurfers.Player.Validation.asmdef");
            Assert.That(definition.name, Is.EqualTo("SubwaySurfers.Player.Validation"));
            Assert.That(definition.references, Is.EqualTo(new[] { "SubwaySurfers.Player.Runtime" }),
                "The validation assembly must depend on the player runtime and nothing else.");
            Assert.That(definition.optionalUnityReferences ?? Array.Empty<string>(), Is.Empty,
                "The validation assembly is scene content, not a test assembly, so it must not " +
                "request TestAssemblies. A test assembly cannot be referenced by a scene.");
            Assert.That(definition.includePlatforms ?? Array.Empty<string>(), Is.Empty,
                "PlayerTestScene must run in the Editor and in a player build, so the validation " +
                "assembly must not restrict platforms.");
        }

        [Test]
        public void CompiledPlayerAssembliesHaveNoProductionRachelOrLuckyDependencies_Requirements_1_3_1_4_1_6_12_1_14_12()
        {
            var playerAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name.StartsWith("SubwaySurfers.Player", StringComparison.Ordinal))
                .ToArray();
            Assert.That(playerAssemblies.Select(assembly => assembly.GetName().Name),
                Does.Contain(typeof(FoundationMetaTests).Assembly.GetName().Name),
                "The Edit Mode player test assembly must compile and load before this smoke test can run.");

            var forbiddenReferences = playerAssemblies
                .SelectMany(assembly => assembly.GetReferencedAssemblies()
                    .Select(reference => assembly.GetName().Name + " -> " + reference.Name))
                .Where(reference => reference.IndexOf("Rachel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    reference.IndexOf("Lucky", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            Assert.That(forbiddenReferences, Is.Empty,
                "Compiled player assemblies must not depend on production Rachel or Lucky assemblies:\n" +
                string.Join("\n", forbiddenReferences));
        }

        [Test]
        public void PlayerAssetsHaveMetaPairs_Requirement_12_13()
        {
            Assert.That(File.Exists(PlayerRoot + ".meta"), Is.True, "Assets/Player.meta is missing.");
            var missing = new List<string>();
            foreach (var directory in Directory.GetDirectories(PlayerRoot, "*", SearchOption.AllDirectories))
                if (!File.Exists(directory + ".meta")) missing.Add(Relative(directory) + ".meta");
            foreach (var file in Directory.GetFiles(PlayerRoot, "*", SearchOption.AllDirectories))
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !File.Exists(file + ".meta"))
                    missing.Add(Relative(file) + ".meta");

            var orphaned = Directory.GetFiles(PlayerRoot, "*.meta", SearchOption.AllDirectories)
                .Where(meta => !File.Exists(meta.Substring(0, meta.Length - 5)) &&
                               !Directory.Exists(meta.Substring(0, meta.Length - 5)))
                .Select(Relative)
                .ToArray();
            Assert.That(missing, Is.Empty, "Missing metadata:\n" + string.Join("\n", missing));
            Assert.That(orphaned, Is.Empty, "Orphaned metadata:\n" + string.Join("\n", orphaned));
            AssertUniqueGuids();
        }

        [Test]
        public void EveryAcceptanceCriterionHasResolvableEvidence_Requirements_12_2_13_5_16_8()
        {
            AssertEveryPlayerTestAssemblyIsLoaded();
            var expected = ReadCriterionCatalog();
            var evidence = ReadManifest();
            var unresolved = new List<string>();
            foreach (var criterion in expected)
            {
                Evidence record;
                if (!evidence.TryGetValue(criterion, out record))
                {
                    unresolved.Add(criterion + " has no evidence entry.");
                    continue;
                }
                if (!EvidenceResolves(record)) unresolved.Add(criterion + " references missing evidence: " + record.Target);
            }

            Assert.That(unresolved, Is.Empty,
                "Traceability remains intentionally incomplete until criterion tests, checklist scenarios, " +
                "or non-testable rationales are implemented:\n" + string.Join("\n", unresolved));
        }

        /// <summary>
        /// Guards the automated-evidence inventory. Without this check a criterion mapped to a Play Mode
        /// test would resolve or fail purely on whether that assembly happened to be loaded, which would
        /// make traceability depend on run configuration instead of on real evidence.
        /// </summary>
        private static void AssertEveryPlayerTestAssemblyIsLoaded()
        {
            var loaded = PlayerTestAssemblies().Select(assembly => assembly.GetName().Name).ToArray();
            var required = new[]
            {
                "SubwaySurfers.Player.Tests.EditMode",
                "SubwaySurfers.Player.Tests.PlayMode",
                "SubwaySurfers.Player.Tests.PlayModeInput"
            };
            var absent = required.Where(name => !loaded.Contains(name, StringComparer.Ordinal)).ToArray();
            Assert.That(absent, Is.Empty,
                "Automated evidence is resolved by reflection over every player test assembly. These " +
                "assemblies are not loaded, so criteria mapped to their tests cannot be resolved " +
                "honestly:\n" + string.Join("\n", absent) +
                "\nLoaded player test assemblies:\n" + string.Join("\n", loaded));
        }

        private static void AssertTestAssembly(string relativePath, bool editorOnly)
        {
            var definition = ReadAssemblyDefinition(relativePath);
            var references = definition.references ?? Array.Empty<string>();
            Assert.That(references, Does.Contain("SubwaySurfers.Player.Runtime"),
                relativePath + " must reference the player runtime it tests.");
            var forbidden = references
                .Where(reference => !AllowedTestReferences.Contains(reference, StringComparer.Ordinal))
                .ToArray();
            Assert.That(forbidden, Is.Empty,
                relativePath + " may reference only player-owned non-production assemblies (" +
                string.Join(", ", AllowedTestReferences) + "). Unexpected references:\n" +
                string.Join("\n", forbidden));
            Assert.That(definition.optionalUnityReferences, Does.Contain("TestAssemblies"));
            Assert.That(definition.autoReferenced, Is.False);
            Assert.That((definition.references ?? Array.Empty<string>()).Any(reference =>
                reference.IndexOf("Rachel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reference.IndexOf("Lucky", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
            if (editorOnly) Assert.That(definition.includePlatforms, Is.EqualTo(new[] { "Editor" }));
            else Assert.That(definition.includePlatforms ?? Array.Empty<string>(), Is.Empty);
        }

        private static AssemblyDefinition ReadAssemblyDefinition(string relativePath)
        {
            var path = Path.Combine(PlayerRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Missing assembly definition: " + relativePath);
            return JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path));
        }

        private static void AssertUniqueGuids()
        {
            var metaFiles = Directory.GetFiles(PlayerRoot, "*.meta", SearchOption.AllDirectories)
                .Concat(new[] { PlayerRoot + ".meta" });
            var guidOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var malformed = new List<string>();
            var duplicates = new List<string>();
            var pattern = new Regex("^guid:\\s*([0-9a-f]{32})\\s*$", RegexOptions.Multiline);
            foreach (var metaFile in metaFiles)
            {
                var match = pattern.Match(File.ReadAllText(metaFile));
                if (!match.Success)
                {
                    malformed.Add(Relative(metaFile));
                    continue;
                }

                var guid = match.Groups[1].Value;
                string existing;
                if (guidOwners.TryGetValue(guid, out existing))
                    duplicates.Add(guid + ": " + existing + " and " + Relative(metaFile));
                else
                    guidOwners.Add(guid, Relative(metaFile));
            }
            Assert.That(malformed, Is.Empty, "Malformed GUID metadata:\n" + string.Join("\n", malformed));
            Assert.That(duplicates, Is.Empty, "Duplicate GUID metadata:\n" + string.Join("\n", duplicates));
        }

        private static HashSet<string> ReadCriterionCatalog()
        {
            var path = Path.Combine(PlayerRoot, "Documentation", "AcceptanceCriteriaCatalog.txt");
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(':');
                var requirement = int.Parse(parts[0]);
                var count = int.Parse(parts[1]);
                for (var criterion = 1; criterion <= count; criterion++)
                    result.Add(requirement + "." + criterion);
            }
            return result;
        }

        private static Dictionary<string, Evidence> ReadManifest()
        {
            var path = Path.Combine(PlayerRoot, "Documentation", "TraceabilityManifest.txt");
            var result = new Dictionary<string, Evidence>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { '|' }, 4);
                Assert.That(parts.Length, Is.EqualTo(4), "Malformed traceability entry: " + line);
                Assert.That(result.ContainsKey(parts[0]), Is.False, "Duplicate criterion: " + parts[0]);
                result.Add(parts[0], new Evidence(parts[1], parts[2], parts[3]));
            }
            return result;
        }

        /// <summary>
        /// Player test assemblies whose methods may be named as automated evidence. Play Mode suites
        /// carry the only evidence for engine-dependent criteria, so resolution must span every player
        /// test assembly rather than the Edit Mode assembly alone. Resolution stays reflective: a name
        /// counts only when a real loaded method carries it, never because a source file mentions it.
        /// </summary>
        private static Assembly[] PlayerTestAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name
                    .StartsWith("SubwaySurfers.Player.Tests", StringComparison.Ordinal))
                .ToArray();
        }

        private static HashSet<string> automatedEvidenceInventory;

        private static HashSet<string> AutomatedEvidenceInventory()
        {
            if (automatedEvidenceInventory != null) return automatedEvidenceInventory;

            var inventory = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in PlayerTestAssemblies())
                foreach (var type in assembly.GetTypes())
                    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                           BindingFlags.Public | BindingFlags.NonPublic))
                        inventory.Add(type.FullName + "." + method.Name);

            automatedEvidenceInventory = inventory;
            return automatedEvidenceInventory;
        }

        private static bool EvidenceResolves(Evidence evidence)
        {
            if (evidence.Kind == "Automated") return AutomatedEvidenceInventory().Contains(evidence.Target);

            if (evidence.Kind == "DocumentedRationale")
                return !string.IsNullOrWhiteSpace(evidence.Rationale) &&
                       File.Exists(Path.Combine(ProjectRoot,
                           evidence.Target.Replace('/', Path.DirectorySeparatorChar)));

            if (evidence.Kind == "Manual")
            {
                var separator = evidence.Target.IndexOf('#');
                if (separator <= 0) return false;
                var file = evidence.Target.Substring(0, separator);
                var scenario = evidence.Target.Substring(separator + 1);
                var path = Path.Combine(ProjectRoot, file.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path) && File.ReadAllText(path).Contains(scenario);
            }

            return false;
        }

        private static string Relative(string path)
        {
            return path.Substring(ProjectRoot.Length + 1).Replace('\\', '/');
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
            public bool autoReferenced;
            public string[] optionalUnityReferences;
        }

        private struct Evidence
        {
            public Evidence(string kind, string target, string rationale)
            {
                Kind = kind;
                Target = target;
                Rationale = rationale;
            }

            public string Kind { get; }
            public string Target { get; }
            public string Rationale { get; }
        }
    }
}
