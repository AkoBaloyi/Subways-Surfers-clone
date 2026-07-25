using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ForwardSpeedApiPropertyTests
    {
        private const int Seed = 220026;
        private const int CaseCount = 132;
        private int generatedCaseIndex;

        // **Validates: Requirements 2.3, 2.4, 2.6, 2.7, 2.8**
        [Test]
        [Description("Feature: player-controller, Property 2: Speed API accepts exactly the valid domain")]
        public void SpeedApiAcceptsExactlyTheValidDomain_Property2_Requirements_2_3_2_4_2_6_2_7_2_8()
        {
            generatedCaseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                AssertProperty,
                render: generated => generated.ToString());
        }

        private SpeedCase Generate(Random random)
        {
            var categories = Enum.GetValues(typeof(SpeedCategory));
            var category = generatedCaseIndex < categories.Length
                ? (SpeedCategory)generatedCaseIndex
                : (SpeedCategory)random.Next(categories.Length);
            generatedCaseIndex++;

            var previous = NextNonNegativeFinite(random);
            switch (category)
            {
                case SpeedCategory.ValidZero:
                    return new SpeedCase(previous, 0f, category);
                case SpeedCategory.ValidNegativeZero:
                    return new SpeedCase(previous, BitConverter.ToSingle(
                        BitConverter.GetBytes(0x80000000u), 0), category);
                case SpeedCategory.ValidSubnormal:
                    return new SpeedCase(previous, float.Epsilon, category);
                case SpeedCategory.ValidMaximum:
                    return new SpeedCase(previous, float.MaxValue, category);
                case SpeedCategory.ValidFinite:
                    return new SpeedCase(previous, NextNonNegativeFinite(random), category);
                case SpeedCategory.Negative:
                    return new SpeedCase(previous, NextNegativeFinite(random), category);
                case SpeedCategory.NaN:
                    return new SpeedCase(previous, float.NaN, category);
                case SpeedCategory.PositiveInfinity:
                    return new SpeedCase(previous, float.PositiveInfinity, category);
                case SpeedCategory.NegativeInfinity:
                    return new SpeedCase(previous, float.NegativeInfinity, category);
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }

        private static void AssertProperty(SpeedCase generated)
        {
            var diagnostics = new List<ValidationDiagnostic>();
            var api = ForwardSpeedApiSeam.Create(generated.PreviousSpeed, diagnostics.Add);

            var result = api.SetForwardSpeed(generated.RequestedSpeed);
            var expectedAccepted = IsFinite(generated.RequestedSpeed) &&
                generated.RequestedSpeed >= 0f;
            var expectedEffective = expectedAccepted
                ? generated.RequestedSpeed
                : generated.PreviousSpeed;

            Assert.That(result.Status, Is.EqualTo(expectedAccepted
                ? CommandStatus.Accepted
                : CommandStatus.Rejected));
            Assert.That(result.Command, Is.EqualTo(PlayerCommandKind.SetForwardSpeed));
            Assert.That(result.Reason, Is.EqualTo(expectedAccepted
                ? RejectionReason.None
                : RejectionReason.InvalidValue));
            Assert.That(result.RequestedSpeed, Is.EqualTo(generated.RequestedSpeed));
            Assert.That(result.EffectiveSpeed, Is.EqualTo(expectedEffective));
            Assert.That(api.ForwardSpeed, Is.EqualTo(expectedEffective),
                "The query must expose the effective speed used by the next movement update.");
            Assert.That(api.ReadForNextMovementUpdate(), Is.EqualTo(expectedEffective),
                "An accepted speed must be visible beginning with the next movement update; " +
                "a rejected speed must preserve the prior update value.");

            if (expectedAccepted)
            {
                Assert.That(diagnostics, Is.Empty,
                    "Valid finite non-negative speeds must not report rejection diagnostics.");
                return;
            }

            Assert.That(diagnostics, Has.Count.EqualTo(1),
                "Each rejected request must publish exactly one diagnostic.");
            var diagnostic = diagnostics[0];
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCode.InvalidValue));
            Assert.That(diagnostic.Field, Is.EqualTo("ForwardSpeed"));
            Assert.That(diagnostic.Message, Does.Contain(DiagnosticCategory(generated.Category)).IgnoreCase,
                "The diagnostic must identify the rejected numeric category.");
        }

        private static float NextNonNegativeFinite(Random random)
        {
            var exponent = (uint)random.Next(0, 255);
            var mantissa = (uint)random.Next(0, 1 << 23);
            var bits = (exponent << 23) | mantissa;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static float NextNegativeFinite(Random random)
        {
            var exponent = (uint)random.Next(0, 255);
            var mantissa = (uint)random.Next(0, 1 << 23);
            var magnitude = (exponent << 23) | mantissa;
            if (magnitude == 0u) magnitude = 1u;
            return BitConverter.ToSingle(BitConverter.GetBytes(0x80000000u | magnitude), 0);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string DiagnosticCategory(SpeedCategory category)
        {
            switch (category)
            {
                case SpeedCategory.Negative:
                    return "negative";
                case SpeedCategory.NaN:
                    return "NaN";
                case SpeedCategory.PositiveInfinity:
                    return "positive infinity";
                case SpeedCategory.NegativeInfinity:
                    return "negative infinity";
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category,
                        "Accepted categories do not require a rejection diagnostic.");
            }
        }

        private enum SpeedCategory
        {
            ValidZero,
            ValidNegativeZero,
            ValidSubnormal,
            ValidMaximum,
            ValidFinite,
            Negative,
            NaN,
            PositiveInfinity,
            NegativeInfinity
        }

        private readonly struct SpeedCase
        {
            public SpeedCase(float previousSpeed, float requestedSpeed,
                SpeedCategory category)
            {
                PreviousSpeed = previousSpeed;
                RequestedSpeed = requestedSpeed;
                Category = category;
            }

            public float PreviousSpeed { get; }
            public float RequestedSpeed { get; }
            public SpeedCategory Category { get; }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "PreviousSpeed={0:R}, RequestedSpeed={1:R}, Category={2}",
                    PreviousSpeed, RequestedSpeed, Category);
            }
        }

        private sealed class ForwardSpeedApiSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.ForwardSpeedApi";

            private readonly object instance;
            private readonly PropertyInfo forwardSpeedProperty;
            private readonly MethodInfo setForwardSpeedMethod;

            private ForwardSpeedApiSeam(object instance, PropertyInfo forwardSpeedProperty,
                MethodInfo setForwardSpeedMethod)
            {
                this.instance = instance;
                this.forwardSpeedProperty = forwardSpeedProperty;
                this.setForwardSpeedMethod = setForwardSpeedMethod;
            }

            public float ForwardSpeed => (float)forwardSpeedProperty.GetValue(instance);

            public static ForwardSpeedApiSeam Create(float initialSpeed,
                Action<ValidationDiagnostic> diagnosticSink)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 3.6 must provide " + RuntimeTypeName + ".");
                Assert.That(typeof(IForwardSpeedApi).IsAssignableFrom(type), Is.True,
                    RuntimeTypeName + " must implement IForwardSpeedApi.");

                var constructor = type.GetConstructor(new[]
                {
                    typeof(float),
                    typeof(Action<ValidationDiagnostic>)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose " +
                    "ForwardSpeedApi(float, Action<ValidationDiagnostic>).");

                var property = type.GetProperty(nameof(IForwardSpeedApi.ForwardSpeed),
                    BindingFlags.Instance | BindingFlags.Public);
                var method = type.GetMethod(nameof(IForwardSpeedApi.SetForwardSpeed),
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(float) }, null);
                Assert.That(property, Is.Not.Null);
                Assert.That(method, Is.Not.Null);

                return new ForwardSpeedApiSeam(
                    constructor.Invoke(new object[] { initialSpeed, diagnosticSink }),
                    property,
                    method);
            }

            public SpeedSetResult SetForwardSpeed(float requestedSpeed)
            {
                return (SpeedSetResult)setForwardSpeedMethod.Invoke(instance,
                    new object[] { requestedSpeed });
            }

            public float ReadForNextMovementUpdate()
            {
                return ForwardSpeed;
            }
        }
    }
}
