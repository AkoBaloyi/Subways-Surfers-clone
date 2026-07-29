using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;

namespace SubwaySurfers.Player.Tests
{
    public sealed class ForwardDisplacementPropertyTests
    {
        private const int Seed = 210025;
        private const int CaseCount = 125;

        // **Validates: Requirements 2.1, 2.2, 2.5**
        [Test]
        [Description("Feature: player-controller, Property 1: State-dependent forward displacement")]
        public void StateDependentForwardDisplacement_Property1_Requirements_2_1_2_2_2_5()
        {
            var generatedCaseIndex = 0;
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random =>
                {
                    var index = generatedCaseIndex++;
                    var state = (PlayerState)(index % Enum.GetValues(typeof(PlayerState)).Length);
                    var speed = index >= 5 && index < 10
                        ? 0f
                        : GeneratedValues.NextFiniteFloat(random, 0f, 10000f);
                    var elapsedTime = index < 5
                        ? 0f
                        : GeneratedValues.NextFiniteFloat(random, 0f, 1000f);
                    return (State: state, Speed: speed, ElapsedTime: elapsedTime);
                },
                generated =>
                {
                    var expected = IsActive(generated.State)
                        ? Vector3.forward * generated.Speed * generated.ElapsedTime
                        : Vector3.zero;
                    Assert.That(Calculate(generated.State, generated.Speed, generated.ElapsedTime),
                        Is.EqualTo(expected));
                },
                render: generated => string.Format(
                    CultureInfo.InvariantCulture,
                    "State={0}, Speed={1:R}, ElapsedTime={2:R}",
                    generated.State, generated.Speed, generated.ElapsedTime));
        }

        private static bool IsActive(PlayerState state)
        {
            return state == PlayerState.Running ||
                state == PlayerState.Jumping ||
                state == PlayerState.Sliding;
        }

        private static Vector3 Calculate(PlayerState state, float speed, float elapsedTime)
        {
            var type = typeof(PlayerState).Assembly.GetType(
                "SubwaySurfers.Player.Domain.ForwardDisplacement");
            Assert.That(type, Is.Not.Null, "Task 3.6 must provide ForwardDisplacement.");
            var method = type.GetMethod(
                "Calculate",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(PlayerState), typeof(float), typeof(float) },
                null);
            Assert.That(method, Is.Not.Null,
                "Task 3.6 must provide Calculate(PlayerState, float, float).");
            return (Vector3)method.Invoke(null, new object[] { state, speed, elapsedTime });
        }
    }
}
