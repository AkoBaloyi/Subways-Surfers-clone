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
    public sealed class CameraConvergencePropertyTests
    {
        private const int Seed = 913009;
        private const int CaseCount = 180;
        private const float NumericTolerance = 0.0001f;

        // **Validates: Requirements 9.2, 9.3, 9.4, 9.9**
        [Test]
        [Description("Feature: player-controller, Property 13: Camera convergence is non-overshooting and bounded")]
        public void CameraConvergenceIsNonOvershootingAndBounded_Property13_Requirements_9_2_9_3_9_4_And_9_9()
        {
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                Generate,
                AssertConvergence,
                render: generated => generated.ToString());
        }

        [Test]
        public void ExactAndOverBudgetSamplesReachTargetWithoutOvershoot_Requirements_9_3_9_4()
        {
            AssertSingleBudgetSample(0.75f);
            AssertSingleBudgetSample(2f);
        }

        [Test]
        public void ZeroDistanceAndZeroTimeSamplesPreserveCameraPosition_Requirement_9_9()
        {
            var initialPlayer = new Vector3(-3f, 2f, 7f);
            var initialCamera = new Vector3(1f, 5f, -9f);
            var convergence = CameraConvergenceSeam.Create(
                initialPlayer, initialCamera, 1.25f, 0f);

            convergence.Advance(initialPlayer, 0f);

            Assert.That(convergence.CameraPosition, Is.EqualTo(initialCamera));
        }

        [Test]
        public void ConstructorRejectsNonFinitePosesWithoutRuntimeCoupling_Requirements_9_2_9_3()
        {
            var nonFiniteVectors = new[]
            {
                new Vector3(float.NaN, 0f, 0f),
                new Vector3(0f, float.PositiveInfinity, 0f),
                new Vector3(0f, 0f, float.NegativeInfinity)
            };

            foreach (var value in nonFiniteVectors)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CameraConvergenceSeam.Create(value, Vector3.zero, 1f, 0f));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CameraConvergenceSeam.Create(Vector3.zero, value, 1f, 0f));
            }
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ConstructorRejectsInvalidSettleDuration_Requirement_9_4(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CameraConvergenceSeam.Create(Vector3.zero, Vector3.one, value, 0f));
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ConstructorRejectsInvalidTolerance_Requirement_9_4(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CameraConvergenceSeam.Create(Vector3.zero, Vector3.one, 1f, value));
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void AdvanceRejectsInvalidElapsedTimeWithoutMutation_Requirements_9_2_9_9(float value)
        {
            var convergence = CameraConvergenceSeam.Create(
                Vector3.zero, new Vector3(0f, 2f, -6f), 1f, 0.01f);
            convergence.Advance(Vector3.right, 0f);
            var before = convergence.CameraPosition;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                convergence.Advance(Vector3.right, value));
            Assert.That(convergence.CameraPosition, Is.EqualTo(before));
        }

        [Test]
        public void AdvanceRejectsNonFinitePlayerPositionWithoutMutation_Requirements_9_2_9_3()
        {
            var convergence = CameraConvergenceSeam.Create(
                Vector3.zero, new Vector3(0f, 2f, -6f), 1f, 0.01f);
            convergence.Advance(Vector3.right, 0f);
            var before = convergence.CameraPosition;
            var invalidPositions = new[]
            {
                new Vector3(float.NaN, 0f, 0f),
                new Vector3(0f, float.PositiveInfinity, 0f),
                new Vector3(0f, 0f, float.NegativeInfinity)
            };

            foreach (var position in invalidPositions)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    convergence.Advance(position, 0.1f));
                Assert.That(convergence.CameraPosition, Is.EqualTo(before));
            }
        }

        private static GeneratedCase Generate(Random random)
        {
            var initialPlayer = NextVector(random, -40f, 40f);
            var initialCamera = NextVector(random, -40f, 40f);
            var direction = NextVector(random, -1f, 1f);
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.right;
            direction.Normalize();

            var targetDistance = GeneratedValues.NextFiniteFloat(random, 0.5f, 30f);
            var fixedPlayer = initialPlayer + direction * targetDistance;
            var duration = GeneratedValues.NextFiniteFloat(random, 0.05f, 5f);
            var tolerance = GeneratedValues.NextFiniteFloat(
                random, 0f, targetDistance * 0.25f);
            var frameTimes = CreateFrameTimes(random, duration);
            return new GeneratedCase(
                initialPlayer,
                initialCamera,
                fixedPlayer,
                duration,
                tolerance,
                frameTimes);
        }

        private static void AssertConvergence(GeneratedCase generated)
        {
            var convergence = CameraConvergenceSeam.Create(
                generated.InitialPlayer,
                generated.InitialCamera,
                generated.Duration,
                generated.Tolerance);
            var offset = generated.InitialCamera - generated.InitialPlayer;
            var target = generated.FixedPlayer + offset;

            convergence.Advance(generated.FixedPlayer, 0f);
            var zeroPosition = convergence.CameraPosition;
            var zeroError = Vector3.Distance(zeroPosition, target);
            convergence.Advance(generated.FixedPlayer, 0f);
            Assert.That(convergence.CameraPosition, Is.EqualTo(zeroPosition),
                "A zero-time fixed-target sample must preserve camera position. " + generated);
            Assert.That(Vector3.Distance(convergence.CameraPosition, target), Is.EqualTo(zeroError),
                "A zero-time fixed-target sample must preserve camera error. " + generated);

            var elapsed = 0f;
            for (var index = 0; index < generated.FrameTimes.Count; index++)
            {
                var frameTime = generated.FrameTimes[index];
                var before = convergence.CameraPosition;
                var beforeError = Vector3.Distance(before, target);
                convergence.Advance(generated.FixedPlayer, frameTime);
                var after = convergence.CameraPosition;
                var afterError = Vector3.Distance(after, target);

                AssertOnSegment(before, after, target, generated, index);
                if (frameTime == 0f)
                {
                    Assert.That(after, Is.EqualTo(before),
                        "Zero-time samples must preserve position at frame " + index + ". " + generated);
                    Assert.That(afterError, Is.EqualTo(beforeError),
                        "Zero-time samples must preserve error at frame " + index + ". " + generated);
                }
                else if (beforeError > generated.Tolerance)
                {
                    Assert.That(afterError, Is.LessThan(beforeError),
                        "Positive-time samples outside tolerance must strictly decrease error at frame " +
                        index + ". " + generated);
                }

                elapsed += frameTime;
            }

            Assert.That(elapsed, Is.EqualTo(generated.Duration).Within(NumericTolerance),
                "Generated frame times must consume exactly the settle duration. " + generated);
            Assert.That(Vector3.Distance(convergence.CameraPosition, target),
                Is.LessThanOrEqualTo(generated.Tolerance + NumericTolerance),
                "A fixed target must settle within duration and tolerance. " + generated);
        }

        private static void AssertOnSegment(
            Vector3 start,
            Vector3 sample,
            Vector3 target,
            GeneratedCase generated,
            int frameIndex)
        {
            var segment = target - start;
            var progress = sample - start;
            var scale = Math.Max(1f, segment.magnitude);
            var epsilon = NumericTolerance * scale;
            var crossError = Vector3.Cross(segment, progress).magnitude;
            var projected = Vector3.Dot(segment, progress);

            Assert.That(crossError, Is.LessThanOrEqualTo(epsilon),
                "The sample left the current-to-target segment at frame " + frameIndex + ". " + generated);
            Assert.That(projected, Is.GreaterThanOrEqualTo(-epsilon),
                "The sample moved behind its frame start at frame " + frameIndex + ". " + generated);
            Assert.That(projected, Is.LessThanOrEqualTo(segment.sqrMagnitude + epsilon),
                "The sample overshot its target at frame " + frameIndex + ". " + generated);
        }

        private static void AssertSingleBudgetSample(float elapsedTime)
        {
            var initialPlayer = new Vector3(2f, -1f, 4f);
            var initialCamera = new Vector3(-3f, 6f, -8f);
            var fixedPlayer = new Vector3(8f, 2f, 15f);
            var convergence = CameraConvergenceSeam.Create(
                initialPlayer, initialCamera, 0.75f, 0f);
            var target = fixedPlayer + initialCamera - initialPlayer;

            convergence.Advance(fixedPlayer, elapsedTime);

            Assert.That(convergence.CameraPosition, Is.EqualTo(target));
        }

        private static Vector3 NextVector(Random random, float minimum, float maximum)
        {
            return new Vector3(
                GeneratedValues.NextFiniteFloat(random, minimum, maximum),
                GeneratedValues.NextFiniteFloat(random, minimum, maximum),
                GeneratedValues.NextFiniteFloat(random, minimum, maximum));
        }

        private static IReadOnlyList<float> CreateFrameTimes(Random random, float duration)
        {
            var positiveCount = random.Next(1, 17);
            var zeroIndex = random.Next(0, positiveCount + 1);
            var result = new List<float>(positiveCount + 1);
            var elapsed = 0f;
            for (var index = 0; index < positiveCount; index++)
            {
                if (index == zeroIndex) result.Add(0f);
                var frameTime = index == positiveCount - 1
                    ? duration - elapsed
                    : duration / positiveCount;
                result.Add(frameTime);
                elapsed += frameTime;
            }

            if (zeroIndex == positiveCount) result.Add(0f);
            return result;
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                Vector3 initialPlayer,
                Vector3 initialCamera,
                Vector3 fixedPlayer,
                float duration,
                float tolerance,
                IReadOnlyList<float> frameTimes)
            {
                InitialPlayer = initialPlayer;
                InitialCamera = initialCamera;
                FixedPlayer = fixedPlayer;
                Duration = duration;
                Tolerance = tolerance;
                FrameTimes = frameTimes;
            }

            public Vector3 InitialPlayer { get; }
            public Vector3 InitialCamera { get; }
            public Vector3 FixedPlayer { get; }
            public float Duration { get; }
            public float Tolerance { get; }
            public IReadOnlyList<float> FrameTimes { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "InitialPlayer={0}, InitialCamera={1}, FixedPlayer={2}, Duration={3:R}, " +
                    "Tolerance={4:R}, FrameTimes=[{5}]",
                    Render(InitialPlayer),
                    Render(InitialCamera),
                    Render(FixedPlayer),
                    Duration,
                    Tolerance,
                    Render(FrameTimes));
            }

            private static string Render(Vector3 value)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "({0:R},{1:R},{2:R})",
                    value.x,
                    value.y,
                    value.z);
            }

            private static string Render(IReadOnlyList<float> values)
            {
                var rendered = new string[values.Count];
                for (var index = 0; index < values.Count; index++)
                    rendered[index] = values[index].ToString("R", CultureInfo.InvariantCulture);
                return string.Join(",", rendered);
            }
        }

        private sealed class CameraConvergenceSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.CameraConvergenceState";

            private readonly object instance;
            private readonly PropertyInfo cameraPosition;
            private readonly MethodInfo advance;

            private CameraConvergenceSeam(object instance, Type type)
            {
                this.instance = instance;
                cameraPosition = type.GetProperty(
                    "CameraPosition", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(cameraPosition, Is.Not.Null,
                    RuntimeTypeName + " must expose CameraPosition.");
                Assert.That(cameraPosition.PropertyType, Is.EqualTo(typeof(Vector3)));
                advance = type.GetMethod(
                    "Advance",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(Vector3), typeof(float) },
                    null);
                Assert.That(advance, Is.Not.Null,
                    RuntimeTypeName + " must expose Advance(Vector3 playerPosition, " +
                    "float elapsedCameraFollowTime).");
                Assert.That(advance.ReturnType, Is.EqualTo(typeof(void)));
            }

            public Vector3 CameraPosition =>
                (Vector3)cameraPosition.GetValue(instance);

            public static CameraConvergenceSeam Create(
                Vector3 initialPlayerPosition,
                Vector3 initialCameraPosition,
                float cameraSettleDuration,
                float cameraFollowTolerance)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 6.2 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[]
                {
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(float),
                    typeof(float)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose CameraConvergenceState(" +
                    "Vector3 initialPlayerPosition, Vector3 initialCameraPosition, " +
                    "float cameraSettleDuration, float cameraFollowTolerance).");
                return new CameraConvergenceSeam(
                    Invoke(constructor, new object[]
                    {
                        initialPlayerPosition,
                        initialCameraPosition,
                        cameraSettleDuration,
                        cameraFollowTolerance
                    }),
                    type);
            }

            public void Advance(Vector3 playerPosition, float elapsedCameraFollowTime)
            {
                Invoke(advance, instance, new object[]
                {
                    playerPosition,
                    elapsedCameraFollowTime
                });
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

            private static void Invoke(MethodInfo method, object target, object[] arguments)
            {
                try
                {
                    method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }
        }
    }
}
