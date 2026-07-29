using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace SubwaySurfers.Player.Tests.Generated
{
    public sealed class GeneratedInfrastructureTests
    {
        [Test]
        public void MinimumCaseGuardRejectsTooFewCasesAndAllowsExactlyMinimum_Requirements_12_3_12_4_12_5()
        {
            Assert.Throws<AssertionException>(() =>
                GeneratedCaseRunner.Run(17, 99, random => random.Next(), value => { }));

            var executed = 0;
            GeneratedCaseRunner.Run(
                17,
                GeneratedCaseRunner.MinimumCaseCount,
                random => random.Next(),
                value => executed++);
            Assert.That(executed, Is.EqualTo(GeneratedCaseRunner.MinimumCaseCount));
        }

        [Test]
        public void SameSeedReplaysValuesPartitionsCommandsAndContacts_Requirements_12_3_12_4_12_5()
        {
            var first = CreateReplay(481516);
            var second = CreateReplay(481516);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void FailureReportsSeedCaseReplayInputMinimizedInputAndCause_Requirements_12_2_12_18()
        {
            var failure = Assert.Throws<AssertionException>(() =>
                GeneratedCaseRunner.Run(
                    73,
                    GeneratedCaseRunner.MinimumCaseCount,
                    random => random.Next(5, 20),
                    value => Assert.That(value, Is.LessThan(0)),
                    value => new[] { 0 },
                    value => value.ToString(CultureInfo.InvariantCulture)));

            Assert.That(failure.Message, Does.Contain("Seed: 73"));
            Assert.That(failure.Message, Does.Contain("Case index: 0"));
            Assert.That(failure.Message, Does.Contain("Replay: run the same property with seed 73"));
            Assert.That(failure.Message, Does.Contain("Replay input:"));
            Assert.That(failure.Message, Does.Contain("Minimized input: 0"));
            Assert.That(failure.Message, Does.Contain(typeof(AssertionException).FullName));
        }

        [Test]
        public void GeneratorFailureStillReportsReplayableSeedAndCase_Requirements_12_2()
        {
            var failure = Assert.Throws<AssertionException>(() =>
                GeneratedCaseRunner.Run<int>(
                    19,
                    GeneratedCaseRunner.MinimumCaseCount,
                    random => { throw new InvalidOperationException("generator failed"); },
                    value => { }));

            Assert.That(failure.Message, Does.Contain("Seed: 19"));
            Assert.That(failure.Message, Does.Contain("Case index: 0"));
            Assert.That(failure.Message, Does.Contain("Replay input: <generation failed>"));
            Assert.That(failure.Message, Does.Contain(typeof(InvalidOperationException).FullName));
        }

        [Test]
        public void NumericCasesCoverFiniteAndNonFiniteDomains_Requirements_12_4()
        {
            var nonFinite = GeneratedValues.NonFiniteCases();
            Assert.That(nonFinite.Any(float.IsNaN), Is.True);
            Assert.That(nonFinite.Any(float.IsPositiveInfinity), Is.True);
            Assert.That(nonFinite.Any(float.IsNegativeInfinity), Is.True);

            var random = new Random(1);
            for (var index = 0; index < GeneratedCaseRunner.MinimumCaseCount; index++)
            {
                var value = GeneratedValues.NextFiniteFloat(random, float.MinValue, float.MaxValue);
                Assert.That(GeneratedValues.IsFinite(value), Is.True);
                Assert.That(value, Is.InRange(float.MinValue, float.MaxValue));
            }
        }

        [Test]
        public void ElapsedPartitionsAreFiniteNonNegativeBoundedAndPreserveTotal_Requirements_12_4_12_5()
        {
            const float total = 37.5f;
            for (var seed = 0; seed < GeneratedCaseRunner.MinimumCaseCount; seed++)
            {
                var partitions = GeneratedValues.ElapsedTimePartitions(new Random(seed), total, 16);
                Assert.That(partitions.Count, Is.InRange(1, 16));
                Assert.That(partitions.All(value => GeneratedValues.IsFinite(value) && value >= 0f), Is.True);
                Assert.That(partitions.Sum(value => (double)value), Is.EqualTo(total).Within(0.0001d));
            }

            Assert.That(
                GeneratedValues.ElapsedTimePartitions(new Random(4), 0f, 8),
                Has.All.EqualTo(0f));
        }

        [Test]
        public void ContactGeneratorNeverStaysOrExitsAnInactiveIdentity_Requirements_12_3_12_8()
        {
            var active = new HashSet<string>();
            var sequence = GeneratedSequences.Contacts(new Random(91), new[] { "a", "b", "c" }, 100);
            foreach (var step in sequence)
            {
                if (step.Operation == ContactOperation.Enter) active.Add(step.Identity);
                if (step.Operation == ContactOperation.Stay)
                    Assert.That(active.Contains(step.Identity), Is.True);
                if (step.Operation == ContactOperation.Exit)
                    Assert.That(active.Remove(step.Identity), Is.True);
            }
        }

        [Test]
        public void SequenceShrinkersAreDeterministicAndStrictlyReduceInputs_Requirements_12_3_12_8_12_18()
        {
            var commands = new[] { "Left", "Right", "Jump" };
            var first = GeneratedSequences.Shrink(commands).Select(RenderSequence).ToArray();
            var second = GeneratedSequences.Shrink(commands).Select(RenderSequence).ToArray();
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first[0], Is.Empty);
            Assert.That(GeneratedSequences.Shrink(commands).All(candidate => candidate.Count < commands.Length), Is.True);

            var contacts = GeneratedSequences.Contacts(new Random(12), new[] { "a", "b" }, 20);
            Assert.That(
                GeneratedSequences.ShrinkContacts(contacts).All(candidate => candidate.Count < contacts.Count),
                Is.True);
        }

        private static string CreateReplay(int seed)
        {
            var random = new Random(seed);
            var value = GeneratedValues.NextNumericCase(random, -5f, 5f);
            var parts = GeneratedValues.ElapsedTimePartitions(random, 3f, 8);
            var commands = GeneratedSequences.Commands(random, new[] { "Left", "Right", "Jump" }, 12);
            var contacts = GeneratedSequences.Contacts(random, new[] { "coin", "obstacle" }, 12);
            return value.ToString(CultureInfo.InvariantCulture) + "|" +
                string.Join(",", parts.Select(part => part.ToString(CultureInfo.InvariantCulture))) + "|" +
                string.Join(",", commands) + "|" + string.Join(",", contacts);
        }

        private static string RenderSequence<T>(IReadOnlyList<T> sequence)
        {
            return string.Join(",", sequence);
        }
    }
}
