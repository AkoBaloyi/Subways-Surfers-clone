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

        [Test]
        public void RuntimeAssemblyHasNoProductionOrTestReferences_Requirements_1_3_1_4_1_6_14_12()
        {
            var definition = ReadAssemblyDefinition("Runtime/SubwaySurfers.Player.Runtime.asmdef");
            Assert.That(definition.references ?? Array.Empty<string>(), Is.Empty);
            Assert.That(definition.includePlatforms ?? Array.Empty<string>(), Is.Empty);
            Assert.That(definition.optionalUnityReferences ?? Array.Empty<string>(), Is.Empty);
            Assert.That(definition.name, Is.EqualTo("SubwaySurfers.Player.Runtime"));
            Assert.That(definition.autoReferenced, Is.True);
        }

        [Test]
        public void TestAssembliesReferenceOnlyPlayerRuntime_Requirements_11_11_12_13()
        {
            AssertTestAssembly("Tests/EditMode/SubwaySurfers.Player.Tests.EditMode.asmdef", true);
            AssertTestAssembly("Tests/PlayMode/SubwaySurfers.Player.Tests.PlayMode.asmdef", false);
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

        private static void AssertTestAssembly(string relativePath, bool editorOnly)
        {
            var definition = ReadAssemblyDefinition(relativePath);
            Assert.That(definition.references, Is.EqualTo(new[] { "SubwaySurfers.Player.Runtime" }));
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

        private static bool EvidenceResolves(Evidence evidence)
        {
            if (evidence.Kind == "Automated")
            {
                return typeof(FoundationMetaTests).Assembly.GetTypes()
                    .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                        BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(method => type.FullName + "." + method.Name))
                    .Contains(evidence.Target);
            }

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
