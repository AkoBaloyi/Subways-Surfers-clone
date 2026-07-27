using System;
using System.Collections.Generic;

namespace SubwaySurfers.Player.Domain
{
    public enum HandoffStatus
    {
        NotReady,
        Ready
    }

    public enum EvidenceDisposition
    {
        PassingValidation,
        NonTestableRationale
    }

    public enum FindingSeverity
    {
        Blocking,
        NonBlocking
    }

    public enum FindingDisposition
    {
        Unresolved,
        Resolved
    }

    public sealed class CriterionEvidence
    {
        public CriterionEvidence(string acceptanceCriterionId,
            EvidenceDisposition disposition, string evidence)
            : this(acceptanceCriterionId, disposition, evidence, null, null,
                null, null, null)
        {
        }

        public CriterionEvidence(string acceptanceCriterionId,
            EvidenceDisposition disposition, string evidence, string reviewId,
            string owner, DateTimeOffset? date, string changedReviewOutput,
            string unresolvedAction)
        {
            RequireText(acceptanceCriterionId, nameof(acceptanceCriterionId));
            RequireDefined(disposition, nameof(disposition));
            RequireText(evidence, nameof(evidence));
            AcceptanceCriterionId = acceptanceCriterionId;
            Disposition = disposition;
            Evidence = evidence;
            ReviewId = reviewId;
            Owner = owner;
            Date = date;
            ChangedReviewOutput = changedReviewOutput;
            UnresolvedAction = unresolvedAction;
        }

        public string AcceptanceCriterionId { get; }
        public EvidenceDisposition Disposition { get; }
        public string Evidence { get; }
        public string ReviewId { get; }
        public string Owner { get; }
        public DateTimeOffset? Date { get; }
        public string ChangedReviewOutput { get; }
        public string UnresolvedAction { get; }

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        private static void RequireDefined(EvidenceDisposition value,
            string parameterName)
        {
            if (!Enum.IsDefined(typeof(EvidenceDisposition), value))
                throw new ArgumentOutOfRangeException(parameterName, value,
                    "The evidence disposition is not defined.");
        }
    }
    public sealed class ReviewFinding
    {
        public ReviewFinding(string findingId, FindingSeverity severity,
            FindingDisposition disposition)
            : this(findingId, severity, disposition, Array.Empty<string>(),
                null, null, null, null, null)
        {
        }

        public ReviewFinding(string findingId, FindingSeverity severity,
            FindingDisposition disposition,
            IEnumerable<string> affectedAcceptanceCriterionIds,
            string changedReviewOutputs, string resolutionEvidence,
            string owner, DateTimeOffset? resolutionDate,
            string unresolvedAction)
        {
            if (string.IsNullOrWhiteSpace(findingId))
                throw new ArgumentException("A non-empty value is required.",
                    nameof(findingId));
            if (!Enum.IsDefined(typeof(FindingSeverity), severity))
                throw new ArgumentOutOfRangeException(nameof(severity), severity,
                    "The finding severity is not defined.");
            if (!Enum.IsDefined(typeof(FindingDisposition), disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition,
                    "The finding disposition is not defined.");
            if (affectedAcceptanceCriterionIds == null)
                throw new ArgumentNullException(nameof(affectedAcceptanceCriterionIds));

            FindingId = findingId;
            Severity = severity;
            Disposition = disposition;
            AffectedAcceptanceCriterionIds = CopyCriterionIds(
                affectedAcceptanceCriterionIds);
            ChangedReviewOutputs = changedReviewOutputs;
            ResolutionEvidence = resolutionEvidence;
            Owner = owner;
            ResolutionDate = resolutionDate;
            UnresolvedAction = unresolvedAction;
        }

        public string FindingId { get; }
        public FindingSeverity Severity { get; }
        public FindingDisposition Disposition { get; }
        public IReadOnlyList<string> AffectedAcceptanceCriterionIds { get; }
        public string ChangedReviewOutputs { get; }
        public string ResolutionEvidence { get; }
        public string Owner { get; }
        public DateTimeOffset? ResolutionDate { get; }
        public DateTimeOffset? Date { get { return ResolutionDate; } }
        public string UnresolvedAction { get; }

        private static IReadOnlyList<string> CopyCriterionIds(
            IEnumerable<string> criterionIds)
        {
            var copy = new List<string>();
            foreach (var criterionId in criterionIds)
            {
                if (string.IsNullOrWhiteSpace(criterionId))
                    throw new ArgumentException(
                        "Affected criterion IDs must be non-empty.",
                        nameof(criterionIds));
                copy.Add(criterionId);
            }
            return new ImmutableValueSequence<string>(copy);
        }
    }

    public static class HandoffReadiness
    {
        public static HandoffStatus Evaluate(
            IEnumerable<string> requiredCriterionIds,
            IEnumerable<CriterionEvidence> criterionEvidence,
            IEnumerable<ReviewFinding> reviewFindings)
        {
            if (requiredCriterionIds == null || criterionEvidence == null ||
                reviewFindings == null)
                return HandoffStatus.NotReady;

            var uncovered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var criterionId in requiredCriterionIds)
            {
                if (string.IsNullOrWhiteSpace(criterionId))
                    return HandoffStatus.NotReady;
                uncovered.Add(criterionId);
            }

            foreach (var evidence in criterionEvidence)
            {
                if (!IsValid(evidence)) return HandoffStatus.NotReady;
                uncovered.Remove(evidence.AcceptanceCriterionId);
            }

            foreach (var finding in reviewFindings)
            {
                if (!IsValid(finding)) return HandoffStatus.NotReady;
                if (finding.Severity == FindingSeverity.Blocking &&
                    finding.Disposition == FindingDisposition.Unresolved)
                    return HandoffStatus.NotReady;
            }

            return uncovered.Count == 0
                ? HandoffStatus.Ready
                : HandoffStatus.NotReady;
        }

        private static bool IsValid(CriterionEvidence evidence)
        {
            return evidence != null &&
                   !string.IsNullOrWhiteSpace(evidence.AcceptanceCriterionId) &&
                   !string.IsNullOrWhiteSpace(evidence.Evidence) &&
                   Enum.IsDefined(typeof(EvidenceDisposition), evidence.Disposition);
        }

        private static bool IsValid(ReviewFinding finding)
        {
            return finding != null &&
                   !string.IsNullOrWhiteSpace(finding.FindingId) &&
                   Enum.IsDefined(typeof(FindingSeverity), finding.Severity) &&
                   Enum.IsDefined(typeof(FindingDisposition), finding.Disposition);
        }
    }
}
