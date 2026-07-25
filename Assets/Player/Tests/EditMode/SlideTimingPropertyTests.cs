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
    public sealed class SlideTimingPropertyTests
    {
        private const int Seed = 735173;
        private const int CaseCount = 160;
        private const float NumericTolerance = 0.0001f;

        // **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**
        [Test]
        [Description("Feature: player-controller, Property 7: Slide timing and restoration follow the first-safe rule")]
        public void SlideTimingAndRestorationFollowTheFirstSafeRule_Property7_Requirements_5_1_Through_5_10()
        {
            var caseIndex = 0;
            var coverage = new Coverage();
            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                generated => AssertSlideBehavior(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.InvalidStates.Count, Is.EqualTo(3));
            Assert.That(coverage.SawZeroElapsedUpdate, Is.True);
            Assert.That(coverage.SawExpiryCrossingResidual, Is.True);
            Assert.That(coverage.SawImmediateSafeRestoration, Is.True);
            Assert.That(coverage.SawBlockedRestoration, Is.True);
            Assert.That(coverage.SawZeroTimeBlockedRecheck, Is.True);
            Assert.That(coverage.SawFirstLaterSafeRestoration, Is.True);
            Assert.That(coverage.SawConcurrentForwardAndLaneProgress, Is.True);
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            if (caseIndex == 0)
            {
                return new GeneratedCase(
                    PlayerState.Jumping,
                    new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                    new ColliderProfile(0.5f, 1f, new Vector3(0f, 0.5f, 0f)),
                    1f,
                    8f,
                    1f,
                    0.1f,
                    LaneDirection.Right,
                    new[] { 0f, 0.2f, 0.2f },
                    0.65f,
                    new[] { true },
                    Array.Empty<float>());
            }

            var baselineRadius = NextFloat(random, 0.3f, 0.9f);
            var baselineHeight = NextFloat(random, baselineRadius * 2f, 4f);
            var slideRadiusMaximum = Math.Min(baselineRadius, baselineHeight * 0.35f);
            var slideRadius = NextFloat(
                random,
                baselineRadius * 0.4f,
                slideRadiusMaximum);
            var slideHeightMaximum = baselineHeight * 0.75f;
            var slideHeight = NextFloat(random, slideRadius * 2f, slideHeightMaximum);
            var baselineCenter = new Vector3(
                NextFloat(random, -0.25f, 0.25f),
                NextFloat(random, 0.75f, 2.25f),
                NextFloat(random, -0.25f, 0.25f));
            var slideCenter = new Vector3(
                baselineCenter.x,
                baselineCenter.y - (baselineHeight - slideHeight) * 0.5f,
                baselineCenter.z);
            var duration = NextFloat(random, 0.15f, 4f);
            var concurrentElapsed = duration * 0.1f;
            var preExpiryBudget = duration * 0.4f;
            var firstPartition = preExpiryBudget * NextFloat(random, 0.15f, 0.85f);
            var preExpiryUpdates = new[]
            {
                0f,
                firstPartition,
                preExpiryBudget - firstPartition
            };
            var crossingResidual = duration * NextFloat(random, 0.05f, 0.3f);
            var crossingElapsed = duration * 0.5f + crossingResidual;
            var blockedResponseCount = caseIndex % 5;
            var safeSequence = new bool[blockedResponseCount + 1];
            safeSequence[safeSequence.Length - 1] = true;
            var laterElapsed = new float[blockedResponseCount];
            for (var index = 0; index < laterElapsed.Length; index++)
            {
                laterElapsed[index] = index == 0
                    ? 0f
                    : duration * NextFloat(random, 0.01f, 0.2f);
            }

            var invalidStates = new[]
            {
                PlayerState.Jumping,
                PlayerState.Failed,
                PlayerState.Resetting
            };
            return new GeneratedCase(
                invalidStates[caseIndex % invalidStates.Length],
                new ColliderProfile(baselineRadius, baselineHeight, baselineCenter),
                new ColliderProfile(slideRadius, slideHeight, slideCenter),
                duration,
                NextFloat(random, 0.1f, 40f),
                NextFloat(random, concurrentElapsed * 2f, concurrentElapsed * 8f),
                concurrentElapsed,
                random.Next(2) == 0 ? LaneDirection.Left : LaneDirection.Right,
                preExpiryUpdates,
                crossingElapsed,
                safeSequence,
                laterElapsed);
        }

        private static void AssertSlideBehavior(GeneratedCase generated, Coverage coverage)
        {
            var groundedRunning = CreateSnapshot(
                PlayerState.Running,
                true,
                generated.BaselineProfile,
                generated.Duration * 0.37f,
                generated.ForwardSpeed);
            var slide = SlideControllerSeam.Create(
                groundedRunning,
                generated.Duration,
                generated.BaselineProfile,
                generated.SlideProfile);

            var accepted = slide.RequestSlide();
            Assert.That(accepted.Status, Is.EqualTo(CommandStatus.Accepted));
            Assert.That(accepted.Command, Is.EqualTo(PlayerCommandKind.Slide));
            Assert.That(accepted.Reason, Is.EqualTo(RejectionReason.None));
            Assert.That(accepted.CurrentState, Is.EqualTo(PlayerState.Sliding));
            Assert.That(slide.Snapshot.State, Is.EqualTo(PlayerState.Sliding));
            Assert.That(slide.Snapshot.SlideElapsedTime, Is.Zero);
            Assert.That(slide.Snapshot.ColliderProfile, Is.EqualTo(generated.SlideProfile));
            AssertEntryMutationOnly(groundedRunning, slide.Snapshot);

            var afterEntry = slide.Snapshot;
            var repeated = slide.RequestSlide();
            Assert.That(repeated.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(repeated.Command, Is.EqualTo(PlayerCommandKind.Slide));
            Assert.That(repeated.Reason, Is.EqualTo(RejectionReason.InvalidState));
            Assert.That(repeated.CurrentState, Is.EqualTo(PlayerState.Sliding));
            SnapshotAssert.Preserved(
                afterEntry,
                slide.Snapshot,
                context: "A repeated slide must preserve the complete snapshot and not extend its timer.");

            var expectedElapsed = AssertConcurrentMovement(slide, generated, coverage);
            var restorationQueries = 0;

            for (var index = 0; index < generated.PreExpiryUpdates.Count; index++)
            {
                var elapsed = generated.PreExpiryUpdates[index];
                var beforeElapsed = slide.Snapshot.SlideElapsedTime;
                slide.Advance(elapsed, () =>
                {
                    restorationQueries++;
                    return true;
                });
                expectedElapsed += elapsed;

                Assert.That(slide.Snapshot.State, Is.EqualTo(PlayerState.Sliding));
                Assert.That(slide.Snapshot.SlideElapsedTime,
                    Is.EqualTo(expectedElapsed).Within(NumericTolerance),
                    "Each Sliding update must add elapsed time exactly once.");
                Assert.That(slide.Snapshot.ColliderProfile, Is.EqualTo(generated.SlideProfile));
                Assert.That(restorationQueries, Is.Zero,
                    "Safe restoration must not be queried before the minimum duration.");
                if (elapsed == 0f)
                {
                    coverage.SawZeroElapsedUpdate = true;
                    Assert.That(slide.Snapshot.SlideElapsedTime, Is.EqualTo(beforeElapsed));
                }
            }

            Assert.That(expectedElapsed, Is.LessThan(generated.Duration));
            Assert.That(expectedElapsed + generated.CrossingElapsed,
                Is.GreaterThan(generated.Duration));
            coverage.SawExpiryCrossingResidual = true;

            var safeIndex = 0;
            slide.Advance(generated.CrossingElapsed, () =>
            {
                restorationQueries++;
                return generated.SafeSequence[safeIndex++];
            });
            expectedElapsed += generated.CrossingElapsed;
            Assert.That(restorationQueries, Is.EqualTo(1));
            Assert.That(slide.Snapshot.SlideElapsedTime,
                Is.EqualTo(expectedElapsed).Within(NumericTolerance));

            if (generated.SafeSequence[0])
            {
                AssertRestored(slide.Snapshot, generated.BaselineProfile);
                coverage.SawImmediateSafeRestoration = true;
            }
            else
            {
                AssertBlocked(slide.Snapshot, generated.SlideProfile);
                coverage.SawBlockedRestoration = true;
                for (var index = 0; index < generated.LaterElapsedUpdates.Count; index++)
                {
                    var elapsed = generated.LaterElapsedUpdates[index];
                    var expectedSafe = generated.SafeSequence[index + 1];
                    var queryCountBefore = restorationQueries;
                    slide.Advance(elapsed, () =>
                    {
                        restorationQueries++;
                        return generated.SafeSequence[safeIndex++];
                    });
                    expectedElapsed += elapsed;

                    Assert.That(restorationQueries, Is.EqualTo(queryCountBefore + 1),
                        "Blocked expiry must recheck restoration exactly once per later update.");
                    Assert.That(slide.Snapshot.SlideElapsedTime,
                        Is.EqualTo(expectedElapsed).Within(NumericTolerance));
                    if (elapsed == 0f) coverage.SawZeroTimeBlockedRecheck = true;
                    if (expectedSafe)
                    {
                        AssertRestored(slide.Snapshot, generated.BaselineProfile);
                        coverage.SawFirstLaterSafeRestoration = true;
                    }
                    else
                    {
                        AssertBlocked(slide.Snapshot, generated.SlideProfile);
                    }
                }
            }

            Assert.That(safeIndex, Is.EqualTo(generated.SafeSequence.Count));
            var afterRestoration = slide.Snapshot;
            var finalQueryCount = restorationQueries;
            slide.Advance(generated.CrossingElapsed, () =>
            {
                restorationQueries++;
                return true;
            });
            Assert.That(restorationQueries, Is.EqualTo(finalQueryCount),
                "The first safe restoration must end further restoration queries.");
            SnapshotAssert.Preserved(
                afterRestoration,
                slide.Snapshot,
                context: "Updates after first-safe restoration must not mutate slide state.");

            AssertInvalidRequestPreservation(generated, coverage);
        }

        private static float AssertConcurrentMovement(
            SlideControllerSeam slide,
            GeneratedCase generated,
            Coverage coverage)
        {
            var planner = new LanePlanner(
                LogicalLane.Center,
                new Vector3(-1f, 0f, 1f),
                generated.LaneDuration,
                0f);
            Assert.That(planner.TryEnqueue(
                new LaneRequest(generated.LaneDirection, 1UL),
                slide.Snapshot.State), Is.True);

            var beforeX = planner.LateralPosition;
            var restorationQueries = 0;
            slide.Advance(generated.ConcurrentElapsed, () =>
            {
                restorationQueries++;
                return true;
            });
            var expectedElapsed = generated.ConcurrentElapsed;
            planner.Advance(generated.ConcurrentElapsed, slide.Snapshot.State);
            var forward = ForwardDisplacement.Calculate(
                slide.Snapshot.State,
                generated.ForwardSpeed,
                generated.ConcurrentElapsed);
            var directionSign = generated.LaneDirection == LaneDirection.Right ? 1f : -1f;
            var expectedX = directionSign *
                (generated.ConcurrentElapsed / generated.LaneDuration);

            Assert.That(slide.Snapshot.State, Is.EqualTo(PlayerState.Sliding));
            Assert.That(slide.Snapshot.SlideElapsedTime,
                Is.EqualTo(expectedElapsed).Within(NumericTolerance));
            Assert.That(slide.Snapshot.ColliderProfile, Is.EqualTo(generated.SlideProfile));
            Assert.That(restorationQueries, Is.Zero);
            Assert.That(forward.x, Is.Zero);
            Assert.That(forward.y, Is.Zero);
            Assert.That(forward.z,
                Is.EqualTo(generated.ForwardSpeed * generated.ConcurrentElapsed)
                    .Within(NumericTolerance));
            Assert.That(forward.z, Is.GreaterThan(0f));
            Assert.That(planner.LateralPosition,
                Is.EqualTo(expectedX).Within(NumericTolerance));
            Assert.That(directionSign * (planner.LateralPosition - beforeX),
                Is.GreaterThan(0f));
            coverage.SawConcurrentForwardAndLaneProgress = true;
            return expectedElapsed;
        }

        private static void AssertInvalidRequestPreservation(
            GeneratedCase generated,
            Coverage coverage)
        {
            var airborne = CreateSnapshot(
                PlayerState.Running,
                false,
                generated.BaselineProfile,
                generated.Duration * 0.41f,
                generated.ForwardSpeed);
            var airborneSlide = SlideControllerSeam.Create(
                airborne,
                generated.Duration,
                generated.BaselineProfile,
                generated.SlideProfile);
            var airborneResult = airborneSlide.RequestSlide();
            Assert.That(airborneResult.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(airborneResult.Command, Is.EqualTo(PlayerCommandKind.Slide));
            Assert.That(airborneResult.Reason, Is.EqualTo(RejectionReason.NotGrounded));
            Assert.That(airborneResult.CurrentState, Is.EqualTo(PlayerState.Running));
            SnapshotAssert.Preserved(
                airborne,
                airborneSlide.Snapshot,
                context: "An airborne Running slide request must preserve the complete snapshot.");

            var invalidProfile = generated.InvalidState == PlayerState.Sliding
                ? generated.SlideProfile
                : generated.BaselineProfile;
            var invalid = CreateSnapshot(
                generated.InvalidState,
                true,
                invalidProfile,
                generated.Duration * 0.63f,
                generated.ForwardSpeed);
            var invalidSlide = SlideControllerSeam.Create(
                invalid,
                generated.Duration,
                generated.BaselineProfile,
                generated.SlideProfile);
            var invalidResult = invalidSlide.RequestSlide();
            Assert.That(invalidResult.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(invalidResult.Command, Is.EqualTo(PlayerCommandKind.Slide));
            Assert.That(invalidResult.Reason, Is.EqualTo(RejectionReason.InvalidState));
            Assert.That(invalidResult.CurrentState, Is.EqualTo(generated.InvalidState));
            SnapshotAssert.Preserved(
                invalid,
                invalidSlide.Snapshot,
                context: "A state-invalid slide request must preserve every snapshot field.");
            coverage.InvalidStates.Add(generated.InvalidState);
        }

        private static void AssertBlocked(PlayerSnapshot snapshot, ColliderProfile slideProfile)
        {
            Assert.That(snapshot.State, Is.EqualTo(PlayerState.Sliding));
            Assert.That(snapshot.ColliderProfile, Is.EqualTo(slideProfile));
        }

        private static void AssertRestored(PlayerSnapshot snapshot, ColliderProfile baselineProfile)
        {
            Assert.That(snapshot.State, Is.EqualTo(PlayerState.Running));
            Assert.That(snapshot.ColliderProfile, Is.EqualTo(baselineProfile));
        }

        private static void AssertEntryMutationOnly(PlayerSnapshot before, PlayerSnapshot after)
        {
            Assert.That(after.Position, Is.EqualTo(before.Position));
            Assert.That(after.Rotation, Is.EqualTo(before.Rotation));
            Assert.That(after.IsGrounded, Is.EqualTo(before.IsGrounded));
            Assert.That(after.ForwardSpeed, Is.EqualTo(before.ForwardSpeed));
            Assert.That(after.CurrentLane, Is.EqualTo(before.CurrentLane));
            Assert.That(after.TargetLane, Is.EqualTo(before.TargetLane));
            Assert.That(after.LateralPosition, Is.EqualTo(before.LateralPosition));
            Assert.That(after.LaneSegmentStart, Is.EqualTo(before.LaneSegmentStart));
            Assert.That(after.LaneSegmentTarget, Is.EqualTo(before.LaneSegmentTarget));
            Assert.That(after.LaneChangeProgress, Is.EqualTo(before.LaneChangeProgress));
            Assert.That(after.VerticalVelocity, Is.EqualTo(before.VerticalVelocity));
            Assert.That(after.LaneRequestQueue, Is.EqualTo(before.LaneRequestQueue));
            Assert.That(after.PendingActionRequests, Is.EqualTo(before.PendingActionRequests));
            Assert.That(after.ResetInProgress, Is.EqualTo(before.ResetInProgress));
        }

        private static PlayerSnapshot CreateSnapshot(
            PlayerState state,
            bool grounded,
            ColliderProfile colliderProfile,
            float slideElapsedTime,
            float forwardSpeed)
        {
            var failed = state == PlayerState.Failed;
            return new PlayerSnapshot(
                new Vector3(2f, 3f, 5f),
                Quaternion.Euler(0f, 17f, 0f),
                state,
                grounded,
                forwardSpeed,
                LogicalLane.Center,
                LogicalLane.Right,
                0.25f,
                0f,
                1f,
                0.25f,
                state == PlayerState.Jumping ? -1.25f : 0f,
                slideElapsedTime,
                colliderProfile,
                failed
                    ? ImmutableValueSequence<LaneRequest>.Empty
                    : new ImmutableValueSequence<LaneRequest>(new[]
                    {
                        new LaneRequest(LaneDirection.Right, 19UL)
                    }),
                failed
                    ? ImmutableValueSequence<PlayerCommandKind>.Empty
                    : new ImmutableValueSequence<PlayerCommandKind>(new[]
                    {
                        PlayerCommandKind.Lane,
                        PlayerCommandKind.Slide
                    }),
                state == PlayerState.Resetting);
        }

        private static float NextFloat(Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                PlayerState invalidState,
                ColliderProfile baselineProfile,
                ColliderProfile slideProfile,
                float duration,
                float forwardSpeed,
                float laneDuration,
                float concurrentElapsed,
                LaneDirection laneDirection,
                IReadOnlyList<float> preExpiryUpdates,
                float crossingElapsed,
                IReadOnlyList<bool> safeSequence,
                IReadOnlyList<float> laterElapsedUpdates)
            {
                InvalidState = invalidState;
                BaselineProfile = baselineProfile;
                SlideProfile = slideProfile;
                Duration = duration;
                ForwardSpeed = forwardSpeed;
                LaneDuration = laneDuration;
                ConcurrentElapsed = concurrentElapsed;
                LaneDirection = laneDirection;
                PreExpiryUpdates = preExpiryUpdates;
                CrossingElapsed = crossingElapsed;
                SafeSequence = safeSequence;
                LaterElapsedUpdates = laterElapsedUpdates;
            }

            public PlayerState InvalidState { get; }
            public ColliderProfile BaselineProfile { get; }
            public ColliderProfile SlideProfile { get; }
            public float Duration { get; }
            public float ForwardSpeed { get; }
            public float LaneDuration { get; }
            public float ConcurrentElapsed { get; }
            public LaneDirection LaneDirection { get; }
            public IReadOnlyList<float> PreExpiryUpdates { get; }
            public float CrossingElapsed { get; }
            public IReadOnlyList<bool> SafeSequence { get; }
            public IReadOnlyList<float> LaterElapsedUpdates { get; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "InvalidState={0}, Baseline={1}, Slide={2}, Duration={3:R}, " +
                    "ForwardSpeed={4:R}, LaneDuration={5:R}, ConcurrentElapsed={6:R}, " +
                    "LaneDirection={7}, PreExpiry=[{8}], CrossingElapsed={9:R}, " +
                    "Safe=[{10}], LaterElapsed=[{11}]",
                    InvalidState,
                    FormatProfile(BaselineProfile),
                    FormatProfile(SlideProfile),
                    Duration,
                    ForwardSpeed,
                    LaneDuration,
                    ConcurrentElapsed,
                    LaneDirection,
                    FormatFloats(PreExpiryUpdates),
                    CrossingElapsed,
                    FormatBooleans(SafeSequence),
                    FormatFloats(LaterElapsedUpdates));
            }

            private static string FormatProfile(ColliderProfile profile)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "(R={0:R},H={1:R},C={2:R}/{3:R}/{4:R})",
                    profile.Radius,
                    profile.Height,
                    profile.Center.x,
                    profile.Center.y,
                    profile.Center.z);
            }

            private static string FormatFloats(IReadOnlyList<float> values)
            {
                var rendered = new string[values.Count];
                for (var index = 0; index < values.Count; index++)
                    rendered[index] = values[index].ToString("R", CultureInfo.InvariantCulture);
                return string.Join(",", rendered);
            }

            private static string FormatBooleans(IReadOnlyList<bool> values)
            {
                var rendered = new string[values.Count];
                for (var index = 0; index < values.Count; index++)
                    rendered[index] = values[index] ? "true" : "false";
                return string.Join(",", rendered);
            }
        }

        private sealed class Coverage
        {
            public readonly HashSet<PlayerState> InvalidStates = new HashSet<PlayerState>();
            public bool SawZeroElapsedUpdate;
            public bool SawExpiryCrossingResidual;
            public bool SawImmediateSafeRestoration;
            public bool SawBlockedRestoration;
            public bool SawZeroTimeBlockedRecheck;
            public bool SawFirstLaterSafeRestoration;
            public bool SawConcurrentForwardAndLaneProgress;
        }

        private sealed class SlideControllerSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.SlideController";

            private readonly object instance;
            private readonly PropertyInfo snapshot;
            private readonly MethodInfo requestSlide;
            private readonly MethodInfo advance;

            private SlideControllerSeam(object instance, Type type)
            {
                this.instance = instance;
                snapshot = RequiredProperty(type, "Snapshot", typeof(PlayerSnapshot));
                requestSlide = RequiredMethod(type, "RequestSlide");
                Assert.That(requestSlide.ReturnType, Is.EqualTo(typeof(ActionRequestResult)));
                advance = RequiredMethod(
                    type,
                    "Advance",
                    typeof(float),
                    typeof(Func<bool>));
                Assert.That(advance.ReturnType, Is.EqualTo(typeof(void)));
            }

            public PlayerSnapshot Snapshot =>
                (PlayerSnapshot)snapshot.GetValue(instance);

            public static SlideControllerSeam Create(
                PlayerSnapshot initialSnapshot,
                float slideDuration,
                ColliderProfile baselineProfile,
                ColliderProfile slideProfile)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 4.9 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[]
                {
                    typeof(PlayerSnapshot),
                    typeof(float),
                    typeof(ColliderProfile),
                    typeof(ColliderProfile)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose SlideController(PlayerSnapshot initialSnapshot, " +
                    "float slideDuration, ColliderProfile baselineProfile, ColliderProfile slideProfile).");
                return new SlideControllerSeam(
                    constructor.Invoke(new object[]
                    {
                        initialSnapshot,
                        slideDuration,
                        baselineProfile,
                        slideProfile
                    }),
                    type);
            }

            public ActionRequestResult RequestSlide()
            {
                return (ActionRequestResult)requestSlide.Invoke(instance, Array.Empty<object>());
            }

            public void Advance(float elapsedSimulationTime, Func<bool> safeColliderRestoration)
            {
                advance.Invoke(instance, new object[]
                {
                    elapsedSimulationTime,
                    safeColliderRestoration
                });
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
