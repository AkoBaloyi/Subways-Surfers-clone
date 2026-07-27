using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class HandoffReadinessPropertyTests
    {
        private const int Seed = 161708;
        private const int CaseCount = 180;

        // **Validates: Requirements 16.1, 16.5, 16.8**
        [Test]
        [Description("Feature: player-controller, Property 17: Handoff readiness is an exact evidence predicate")]
        public void HandoffReadinessIsAnExactEvidencePredicate_Property17_Requirements_16_1_16_5_And_16_8()
        {
            var caseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                AssertExactPredicate,
                render: generated => generated.ToString());
        }

        [Test]
        public void EmptyRequiredCriteriaAndEvidenceAreReady_Requirements_16_1_16_8()
        {
            AssertStatus(
                Array.Empty<string>(),
                Array.Empty<EvidenceSpec>(),
                Array.Empty<FindingSpec>(),
                true);
        }
        [Test]
        public void MissingEvidenceIsNotReady_Requirements_16_1_16_8()
        {
            AssertStatus(
                new[] { "16.2" },
                Array.Empty<EvidenceSpec>(),
                Array.Empty<FindingSpec>(),
                false);
        }

        [Test]
        public void DuplicateEvidenceCannotReplaceMissingCriterion_Requirements_16_1_16_8()
        {
            AssertStatus(
                new[] { "16.2", "16.3" },
                new[]
                {
                    EvidenceSpec.Passing("16.2"),
                    EvidenceSpec.NonTestable("16.2")
                },
                Array.Empty<FindingSpec>(),
                false);
        }

        [Test]
        public void PassingAndDocumentedNonTestableEvidenceBothCoverCriteria_Requirement_16_8()
        {
            AssertStatus(
                new[] { "16.2", "16.3" },
                new[]
                {
                    EvidenceSpec.Passing("16.2"),
                    EvidenceSpec.NonTestable("16.3")
                },
                Array.Empty<FindingSpec>(),
                true);
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        public void OnlyUnresolvedBlockingFindingsPreventReadiness_Requirement_16_5(
            bool blocking,
            bool resolved,
            bool expectedReady)
        {
            AssertStatus(
                new[] { "16.2" },
                new[] { EvidenceSpec.Passing("16.2") },
                new[] { new FindingSpec("finding-edge", blocking, resolved) },
                expectedReady);
        }
        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var criterionCount = random.Next(0, 9);
            var required = new List<string>(criterionCount);
            for (var index = 0; index < criterionCount; index++)
                required.Add("criterion-" + index.ToString(CultureInfo.InvariantCulture));

            var evidence = new List<EvidenceSpec>();
            for (var index = 0; index < criterionCount; index++)
            {
                if (random.Next(0, 4) == 0) continue;
                evidence.Add(random.Next(0, 2) == 0
                    ? EvidenceSpec.Passing(required[index])
                    : EvidenceSpec.NonTestable(required[index]));
                if (random.Next(0, 4) == 0)
                    evidence.Add(EvidenceSpec.Passing(required[index]));
            }

            if (caseIndex % 5 == 0 && criterionCount > 1)
            {
                evidence.Clear();
                evidence.Add(EvidenceSpec.Passing(required[0]));
                evidence.Add(EvidenceSpec.NonTestable(required[0]));
            }
            if (random.Next(0, 3) == 0)
                evidence.Add(EvidenceSpec.Passing("extra-criterion"));

            var findings = new List<FindingSpec>();
            var findingCount = random.Next(0, 6);
            for (var index = 0; index < findingCount; index++)
            {
                findings.Add(new FindingSpec(
                    "finding-" + index.ToString(CultureInfo.InvariantCulture),
                    random.Next(0, 2) == 0,
                    random.Next(0, 2) == 0));
            }

            return new GeneratedCase(required, evidence, findings);
        }

        private static void AssertExactPredicate(GeneratedCase generated)
        {
            AssertStatus(
                generated.RequiredCriteria,
                generated.Evidence,
                generated.Findings,
                ExpectedReady(generated));
        }
        private static bool ExpectedReady(GeneratedCase generated)
        {
            var uncovered = new HashSet<string>(
                generated.RequiredCriteria,
                StringComparer.Ordinal);
            foreach (var item in generated.Evidence)
                uncovered.Remove(item.CriterionId);

            if (uncovered.Count != 0) return false;
            foreach (var finding in generated.Findings)
            {
                if (finding.Blocking && !finding.Resolved) return false;
            }

            return true;
        }

        private static void AssertStatus(
            IReadOnlyList<string> required,
            IReadOnlyList<EvidenceSpec> evidence,
            IReadOnlyList<FindingSpec> findings,
            bool expectedReady)
        {
            var actual = HandoffReadinessSeam.Evaluate(required, evidence, findings);
            Assert.That(
                actual,
                Is.EqualTo(expectedReady),
                "Ready must equal complete criterion coverage AND zero unresolved " +
                "blocking findings.");
        }

        private enum EvidenceKind
        {
            PassingValidation,
            NonTestableRationale
        }

        private sealed class EvidenceSpec
        {
            private EvidenceSpec(string criterionId, EvidenceKind kind, string evidence)
            {
                CriterionId = criterionId;
                Kind = kind;
                Evidence = evidence;
            }

            public string CriterionId { get; }
            public EvidenceKind Kind { get; }
            public string Evidence { get; }

            public static EvidenceSpec Passing(string id)
            {
                return new EvidenceSpec(id, EvidenceKind.PassingValidation, "evidence://" + id);
            }
            public static EvidenceSpec NonTestable(string id)
            {
                return new EvidenceSpec(
                    id,
                    EvidenceKind.NonTestableRationale,
                    "Documented non-testable rationale for " + id);
            }

            public override string ToString()
            {
                return CriterionId + ":" + Kind;
            }
        }

        private sealed class FindingSpec
        {
            public FindingSpec(string id, bool blocking, bool resolved)
            {
                Id = id;
                Blocking = blocking;
                Resolved = resolved;
            }

            public string Id { get; }
            public bool Blocking { get; }
            public bool Resolved { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}",
                    Id,
                    Blocking ? "Blocking" : "NonBlocking",
                    Resolved ? "Resolved" : "Unresolved");
            }
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                IReadOnlyList<string> requiredCriteria,
                IReadOnlyList<EvidenceSpec> evidence,
                IReadOnlyList<FindingSpec> findings)
            {
                RequiredCriteria = requiredCriteria;
                Evidence = evidence;
                Findings = findings;
            }

            public IReadOnlyList<string> RequiredCriteria { get; }
            public IReadOnlyList<EvidenceSpec> Evidence { get; }
            public IReadOnlyList<FindingSpec> Findings { get; }
            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Required=[{0}], Evidence=[{1}], Findings=[{2}]",
                    string.Join(",", RequiredCriteria),
                    string.Join(",", Evidence),
                    string.Join(",", Findings));
            }
        }

        private static class HandoffReadinessSeam
        {
            private const string ReadinessTypeName =
                "SubwaySurfers.Player.Domain.HandoffReadiness";
            private const string EvidenceTypeName =
                "SubwaySurfers.Player.Domain.CriterionEvidence";
            private const string EvidenceDispositionTypeName =
                "SubwaySurfers.Player.Domain.EvidenceDisposition";
            private const string FindingTypeName =
                "SubwaySurfers.Player.Domain.ReviewFinding";
            private const string FindingSeverityTypeName =
                "SubwaySurfers.Player.Domain.FindingSeverity";
            private const string FindingDispositionTypeName =
                "SubwaySurfers.Player.Domain.FindingDisposition";

            public static bool Evaluate(
                IReadOnlyList<string> required,
                IReadOnlyList<EvidenceSpec> evidence,
                IReadOnlyList<FindingSpec> findings)
            {
                var assembly = typeof(PlayerState).Assembly;
                var readinessType = RequireType(assembly, ReadinessTypeName);
                var evidenceType = RequireType(assembly, EvidenceTypeName);
                var evidenceDispositionType = RequireType(
                    assembly, EvidenceDispositionTypeName);
                var findingType = RequireType(assembly, FindingTypeName);
                var findingSeverityType = RequireType(
                    assembly, FindingSeverityTypeName);
                var findingDispositionType = RequireType(
                    assembly, FindingDispositionTypeName);

                var evidenceValues = CreateEvidence(
                    evidenceType, evidenceDispositionType, evidence);
                var findingValues = CreateFindings(
                    findingType,
                    findingSeverityType,
                    findingDispositionType,
                    findings);
                var evaluate = FindEvaluate(readinessType);
                var result = Invoke(evaluate, null, new object[]
                {
                    Copy(required),
                    evidenceValues,
                    findingValues
                });
                Assert.That(result, Is.Not.Null, "Evaluate must return HandoffStatus.");
                var name = result.ToString();
                Assert.That(
                    name,
                    Is.EqualTo("Ready").Or.EqualTo("NotReady"),
                    "HandoffStatus must expose Ready and NotReady values.");
                return string.Equals(name, "Ready", StringComparison.Ordinal);
            }

            private static Type RequireType(Assembly assembly, string fullName)
            {
                var type = assembly.GetType(fullName);
                Assert.That(type, Is.Not.Null,
                    "Task 6.4 must provide " + fullName + ".");
                return type;
            }

            private static Array CreateEvidence(
                Type evidenceType,
                Type dispositionType,
                IReadOnlyList<EvidenceSpec> values)
            {
                var constructor = evidenceType.GetConstructor(new[]
                {
                    typeof(string), dispositionType, typeof(string)
                });
                Assert.That(constructor, Is.Not.Null,
                    EvidenceTypeName + " must expose CriterionEvidence(string " +
                    "acceptanceCriterionId, EvidenceDisposition disposition, string evidence).");
                var result = Array.CreateInstance(evidenceType, values.Count);
                for (var index = 0; index < values.Count; index++)
                {
                    var value = values[index];
                    var disposition = Enum.Parse(
                        dispositionType, value.Kind.ToString(), false);
                    result.SetValue(Invoke(constructor, new object[]
                    {
                        value.CriterionId, disposition, value.Evidence
                    }), index);
                }
                return result;
            }
            private static Array CreateFindings(
                Type findingType,
                Type severityType,
                Type dispositionType,
                IReadOnlyList<FindingSpec> values)
            {
                var constructor = findingType.GetConstructor(new[]
                {
                    typeof(string), severityType, dispositionType
                });
                Assert.That(constructor, Is.Not.Null,
                    FindingTypeName + " must expose ReviewFinding(string findingId, " +
                    "FindingSeverity severity, FindingDisposition disposition).");
                var result = Array.CreateInstance(findingType, values.Count);
                for (var index = 0; index < values.Count; index++)
                {
                    var value = values[index];
                    var severity = Enum.Parse(
                        severityType,
                        value.Blocking ? "Blocking" : "NonBlocking",
                        false);
                    var disposition = Enum.Parse(
                        dispositionType,
                        value.Resolved ? "Resolved" : "Unresolved",
                        false);
                    result.SetValue(Invoke(constructor, new[]
                    {
                        (object)value.Id, severity, disposition
                    }), index);
                }
                return result;
            }

            private static MethodInfo FindEvaluate(Type readinessType)
            {
                foreach (var method in readinessType.GetMethods(
                             BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "Evaluate" &&
                        method.GetParameters().Length == 3)
                        return method;
                }
                Assert.Fail(ReadinessTypeName + " must expose static Evaluate(" +
                    "requiredCriterionIds, criterionEvidence, reviewFindings).");
                return null;
            }

            private static string[] Copy(IReadOnlyList<string> values)
            {
                var result = new string[values.Count];
                for (var index = 0; index < values.Count; index++)
                    result[index] = values[index];
                return result;
            }
            private static object Invoke(ConstructorInfo constructor, object[] arguments)
            {
                try
                {
                    return constructor.Invoke(arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }

            private static object Invoke(
                MethodInfo method,
                object target,
                object[] arguments)
            {
                try
                {
                    return method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }
        }
    }
}
