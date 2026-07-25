using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ConfigurationPropertyTests
    {
        // **Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5**
        [Test]
        [Description("Feature: player-controller, Property 15: Validation repairs exactly invalid fields")]
        public void ValidationRepairsExactlyInvalidFields_Property15_Requirements_15_1_15_2_15_3_15_4_15_5()
        {
            var ownedReference = ScriptableObject.CreateInstance<TestReference>();
            try
            {
                var defaults = ConfigurationTestData.SafeDefaults;
                var references = new PlayerConfigurationReferences(
                    ownedReference, ownedReference, ownedReference, ownedReference);
                GeneratedCaseRunner.Run(
                    150015,
                    160,
                    random => GeneratedConfigurationCase.Create(random, defaults),
                    generated => AssertRepair(generated, defaults, references),
                    render: generated => generated.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ownedReference);
            }
        }

        private static void AssertRepair(GeneratedConfigurationCase generated, PlayerConfiguration defaults,
            PlayerConfigurationReferences references)
        {
            var result = PlayerConfigurationValidator.Validate(generated.Configuration, defaults, references, references);
            Assert.That(result.SimulationEnabled, Is.True);
            Assert.That(PlayerConfigurationValidator.IsValid(result.EffectiveConfiguration), Is.True);
            Assert.That(result.Diagnostics.Select(x => x.Field), Is.EquivalentTo(generated.InvalidFields));
            Assert.That(result.Diagnostics.Count, Is.EqualTo(generated.InvalidFields.Count));
            foreach (var field in ConfigurationTestData.ValidatedFields)
            {
                var expectedSource = generated.InvalidFields.Contains(field) ? defaults : generated.Configuration;
                Assert.That(ConfigurationTestData.Read(expectedSource, field),
                    Is.EqualTo(ConfigurationTestData.Read(result.EffectiveConfiguration, field)),
                    field.ToString());
            }
        }

        private sealed class TestReference : ScriptableObject, IAnimationReceiver
        {
            public bool TryApply(AnimationCommand command) { return true; }
        }
    }

    internal sealed class GeneratedConfigurationCase
    {
        private GeneratedConfigurationCase(PlayerConfiguration configuration,
            HashSet<ConfigurationField> invalidFields)
        {
            Configuration = configuration;
            InvalidFields = invalidFields;
        }

        public PlayerConfiguration Configuration { get; }
        public HashSet<ConfigurationField> InvalidFields { get; }

        public static GeneratedConfigurationCase Create(Random random, PlayerConfiguration defaults)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            var configuration = ConfigurationTestData.RandomValidConfiguration(random, defaults);
            var candidates = ConfigurationTestData.ValidatedFields.ToList();
            for (var index = candidates.Count - 1; index > 0; index--)
            {
                var swapIndex = random.Next(index + 1);
                var temporary = candidates[index];
                candidates[index] = candidates[swapIndex];
                candidates[swapIndex] = temporary;
            }

            var invalidFields = new HashSet<ConfigurationField>();
            var invalidCount = random.Next(1, Math.Min(6, candidates.Count) + 1);
            for (var index = 0; index < invalidCount; index++)
            {
                var field = candidates[index];
                configuration = ConfigurationTestData.WithInvalid(configuration, field, random);
                invalidFields.Add(field);
            }

            return new GeneratedConfigurationCase(configuration, invalidFields);
        }

        public override string ToString()
        {
            return "Invalid fields: " + string.Join(", ", InvalidFields.OrderBy(x => x));
        }
    }
}
