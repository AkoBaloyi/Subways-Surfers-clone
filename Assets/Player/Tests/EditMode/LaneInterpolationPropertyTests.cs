using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class LaneInterpolationPropertyTests
    {
        private const int Seed = 420041;
        private const int CaseCount = 180;
        private const float NumericTolerance = 0.0001f;

        // **Validates: Requirements 3.6, 3.7, 3.8, 12.5**
        [Test]
        [Description("Feature: player-controller, Property 4: Lane interpolation is bounded and timely")]
        public void LaneInterpolationIsBoundedAndTimely_Property4_Requirements_3_6_3_7_3_8_And_12_5()
        {
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                AssertInterpolation,
                render: generated => generated.ToString());
        }

        private static GeneratedCase Generate(Random random)
        {
            var left = GeneratedValues.NextFiniteFloat(random, -20f, -1f);
            var leftGap = GeneratedValues.NextFiniteFloat(random, 0.25f, 10f);
            var rightGap = GeneratedValues.NextFiniteFloat(random, 0.25f, 10f);
            var centers = new Vector3(left, left + leftGap, left + leftGap + rightGap);
            var duration = GeneratedValues.NextFiniteFloat(random, 0.01f, 5f);
            var minimumGap = Math.Min(leftGap, rightGap);
            var tolerance = GeneratedValues.NextFiniteFloat(random, 0f, minimumGap * 0.49f);
            var partitions = GeneratedValues.ElapsedTimePartitions(random, duration, 16);
            var residual = GeneratedValues.NextFiniteFloat(random, duration * 0.01f, duration * 0.99f);
            var movesRight = random.Next(2) == 0;
            return new GeneratedCase(centers, duration, tolerance, partitions, residual, movesRight);
        }
        private static void AssertInterpolation(GeneratedCase generated)
        {
            var initialLane = generated.MovesRight ? LogicalLane.Left : LogicalLane.Right;
            var direction = generated.MovesRight ? LaneDirection.Right : LaneDirection.Left;
            var start = LaneCenter(generated.Centers, initialLane);
            var firstTargetLane = generated.MovesRight ? LogicalLane.Center : LogicalLane.Center;
            var finalTargetLane = generated.MovesRight ? LogicalLane.Right : LogicalLane.Left;
            var firstTarget = LaneCenter(generated.Centers, firstTargetLane);
            var finalTarget = LaneCenter(generated.Centers, finalTargetLane);

            var timely = LanePlannerSeam.Create(
                initialLane,
                generated.Centers,
                generated.Duration,
                generated.Tolerance);
            timely.Enqueue(new LaneRequest(direction, 1UL));

            var previous = start;
            var accumulated = 0d;
            for (var index = 0; index < generated.Partitions.Count; index++)
            {
                accumulated += generated.Partitions[index];
                timely.Advance(generated.Partitions[index]);
                var actual = timely.LateralPosition;
                var normalized = Mathf.Clamp01((float)(accumulated / generated.Duration));
                var expected = Mathf.Lerp(start, firstTarget, normalized);

                AssertMonotonicAndBounded(
                    previous,
                    actual,
                    start,
                    firstTarget,
                    generated.MovesRight,
                    generated,
                    "partition " + index);
                Assert.That(actual, Is.EqualTo(expected).Within(NumericTolerance),
                    "Interpolation must be based on accumulated elapsed time. " + generated);
                previous = actual;
            }

            Assert.That(accumulated, Is.EqualTo(generated.Duration).Within(NumericTolerance),
                "Generated partitions must represent exactly one lane-change duration. " + generated);
            Assert.That(Math.Abs(timely.LateralPosition - firstTarget),
                Is.LessThanOrEqualTo(generated.Tolerance + NumericTolerance),
                "The target lane center must be reached within tolerance by Lane_Change_Duration. " + generated);

            var residual = LanePlannerSeam.Create(
                initialLane,
                generated.Centers,
                generated.Duration,
                generated.Tolerance);
            residual.Enqueue(new LaneRequest(direction, 1UL));
            residual.Enqueue(new LaneRequest(direction, 2UL));

            for (var index = 0; index < generated.Partitions.Count - 1; index++)
                residual.Advance(generated.Partitions[index]);
            residual.Advance(
                generated.Partitions[generated.Partitions.Count - 1] + generated.Residual);

            var expectedResidual = Mathf.Lerp(
                firstTarget,
                finalTarget,
                generated.Residual / generated.Duration);
            AssertMonotonicAndBounded(
                start,
                residual.LateralPosition,
                start,
                finalTarget,
                generated.MovesRight,
                generated,
                "residual update");
            Assert.That(residual.LateralPosition,
                Is.EqualTo(expectedResidual).Within(NumericTolerance),
                "Elapsed time beyond the first segment must advance the next queued segment. " + generated);
        }

        private static void AssertMonotonicAndBounded(
            float previous,
            float actual,
            float start,
            float target,
            bool movesRight,
            GeneratedCase generated,
            string sample)
        {
            var lower = Math.Min(start, target);
            var upper = Math.Max(start, target);
            Assert.That(actual, Is.GreaterThanOrEqualTo(lower - NumericTolerance),
                "A lateral sample crossed the lower segment endpoint at " + sample + ". " + generated);
            Assert.That(actual, Is.LessThanOrEqualTo(upper + NumericTolerance),
                "A lateral sample crossed the upper segment endpoint at " + sample + ". " + generated);
            if (movesRight)
            {
                Assert.That(actual, Is.GreaterThanOrEqualTo(previous - NumericTolerance),
                    "Rightward samples must be monotonic at " + sample + ". " + generated);
            }
            else
            {
                Assert.That(actual, Is.LessThanOrEqualTo(previous + NumericTolerance),
                    "Leftward samples must be monotonic at " + sample + ". " + generated);
            }
        }

        private static float LaneCenter(Vector3 centers, LogicalLane lane)
        {
            switch (lane)
            {
                case LogicalLane.Left: return centers.x;
                case LogicalLane.Center: return centers.y;
                case LogicalLane.Right: return centers.z;
                default: throw new ArgumentOutOfRangeException(nameof(lane));
            }
        }
        private sealed class GeneratedCase
        {
            public GeneratedCase(
                Vector3 centers,
                float duration,
                float tolerance,
                IReadOnlyList<float> partitions,
                float residual,
                bool movesRight)
            {
                Centers = centers;
                Duration = duration;
                Tolerance = tolerance;
                Partitions = partitions;
                Residual = residual;
                MovesRight = movesRight;
            }

            public Vector3 Centers { get; }
            public float Duration { get; }
            public float Tolerance { get; }
            public IReadOnlyList<float> Partitions { get; }
            public float Residual { get; }
            public bool MovesRight { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Centers=({0:R},{1:R},{2:R}), Duration={3:R}, Tolerance={4:R}, " +
                    "Partitions=[{5}], Residual={6:R}, Direction={7}",
                    Centers.x,
                    Centers.y,
                    Centers.z,
                    Duration,
                    Tolerance,
                    Render(Partitions),
                    Residual,
                    MovesRight ? LaneDirection.Right : LaneDirection.Left);
            }

            private static string Render(IReadOnlyList<float> values)
            {
                var rendered = new string[values.Count];
                for (var index = 0; index < values.Count; index++)
                    rendered[index] = values[index].ToString("R", CultureInfo.InvariantCulture);
                return string.Join(",", rendered);
            }
        }

        private sealed class LanePlannerSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.LanePlanner";

            private readonly object instance;
            private readonly PropertyInfo lateralPosition;
            private readonly MethodInfo enqueue;
            private readonly MethodInfo advance;

            private LanePlannerSeam(object instance, Type type)
            {
                this.instance = instance;
                lateralPosition = RequiredProperty(type, "LateralPosition", typeof(float));
                enqueue = RequiredMethod(type, "Enqueue", typeof(LaneRequest));
                Assert.That(enqueue.ReturnType, Is.EqualTo(typeof(void)));
                advance = RequiredMethod(type, "Advance", typeof(float));
                Assert.That(advance.ReturnType, Is.EqualTo(typeof(void)));
            }

            public float LateralPosition =>
                (float)lateralPosition.GetValue(instance);

            public static LanePlannerSeam Create(
                LogicalLane initialLane,
                Vector3 laneCenters,
                float laneChangeDuration,
                float lanePositionTolerance)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 4.3 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[]
                {
                    typeof(LogicalLane),
                    typeof(Vector3),
                    typeof(float),
                    typeof(float)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose LanePlanner(LogicalLane initialLane, " +
                    "Vector3 laneCenters, float laneChangeDuration, float lanePositionTolerance).");
                return new LanePlannerSeam(
                    constructor.Invoke(new object[]
                    {
                        initialLane,
                        laneCenters,
                        laneChangeDuration,
                        lanePositionTolerance
                    }),
                    type);
            }

            public void Enqueue(LaneRequest request)
            {
                enqueue.Invoke(instance, new object[] { request });
            }

            public void Advance(float elapsedSimulationTime)
            {
                advance.Invoke(instance, new object[] { elapsedSimulationTime });
            }

            private static PropertyInfo RequiredProperty(
                Type type,
                string name,
                Type propertyType)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null, type.FullName + " must expose " + name + ".");
                Assert.That(property.PropertyType, Is.EqualTo(propertyType));
                return property;
            }

            private static MethodInfo RequiredMethod(
                Type type,
                string name,
                params Type[] parameterTypes)
            {
                var method = type.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    parameterTypes,
                    null);
                Assert.That(method, Is.Not.Null, type.FullName + " must expose " + name + ".");
                return method;
            }
        }
    }
}
