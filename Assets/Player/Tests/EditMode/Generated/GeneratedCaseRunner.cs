using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace SubwaySurfers.Player.Tests.Generated
{
    public static class GeneratedCaseRunner
    {
        public const int MinimumCaseCount = 100;
        private const int MaximumShrinkPasses = 32;

        public static void Run<T>(
            int seed,
            int caseCount,
            Func<Random, T> generate,
            Action<T> property,
            Func<T, IEnumerable<T>> shrinkCandidates = null,
            Func<T, string> render = null)
        {
            Assert.That(caseCount, Is.GreaterThanOrEqualTo(MinimumCaseCount),
                "Generated properties must execute at least 100 cases.");
            Assert.That(generate, Is.Not.Null);
            Assert.That(property, Is.Not.Null);

            var random = new Random(seed);
            for (var caseIndex = 0; caseIndex < caseCount; caseIndex++)
            {
                var input = default(T);
                var inputGenerated = false;
                try
                {
                    input = generate(random);
                    inputGenerated = true;
                    property(input);
                }
                catch (Exception exception)
                {
                    var minimized = inputGenerated
                        ? Minimize(input, property, shrinkCandidates)
                        : input;
                    Assert.Fail(
                        "Generated case failed.\nSeed: {0}\nCase index: {1}\n" +
                        "Replay: run the same property with seed {0}; failing case index {1}.\n" +
                        "Replay input: {2}\nMinimized input: {3}\nFailure: {4}: {5}",
                        seed,
                        caseIndex,
                        inputGenerated ? Render(input, render) : "<generation failed>",
                        inputGenerated ? Render(minimized, render) : "<generation failed>",
                        exception.GetType().FullName,
                        exception.Message);
                }
            }
        }

        private static T Minimize<T>(
            T input,
            Action<T> property,
            Func<T, IEnumerable<T>> shrinkCandidates)
        {
            if (shrinkCandidates == null) return input;

            var current = input;
            for (var pass = 0; pass < MaximumShrinkPasses; pass++)
            {
                var reduced = false;
                var candidates = shrinkCandidates(current);
                if (candidates == null) break;
                foreach (var candidate in candidates)
                {
                    if (!Fails(property, candidate)) continue;
                    current = candidate;
                    reduced = true;
                    break;
                }

                if (!reduced) break;
            }

            return current;
        }

        private static bool Fails<T>(Action<T> property, T candidate)
        {
            try
            {
                property(candidate);
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string Render<T>(T input, Func<T, string> render)
        {
            if (render != null) return render(input) ?? "<null>";
            if (ReferenceEquals(input, null)) return "<null>";
            var formattable = input as IFormattable;
            return formattable == null
                ? input.ToString()
                : formattable.ToString(null, CultureInfo.InvariantCulture);
        }
    }
}
