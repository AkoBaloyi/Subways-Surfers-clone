using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using UnityEngine;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class StateTransitionModelPropertyTests
    {
        private const int Seed = 309041;
        private const int CaseCount = 173;
        private const int ExhaustiveCombinationCount = 160;

        // **Validates: Requirements 3.11, 6.1-6.14, 14.4**
        [Test]
        [Description("Feature: player-controller, Property 9: State commands match the exhaustive transition model")]
        public void StateCommandsMatchExhaustiveTransitionModel_Property9()
        {
            var generatedCaseIndex = 0;
            var coveredInitialStates = new HashSet<PlayerState>();
            var coveredCommands = new HashSet<ModelCommand>();
            var coveredGrounded = new HashSet<bool>();
            var coveredSafeRestoration = new HashSet<bool>();
            var coveredStatuses = new HashSet<CommandStatus>();

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, generatedCaseIndex++),
                generated => AssertSequence(generated, coveredInitialStates, coveredCommands,
                    coveredGrounded, coveredSafeRestoration, coveredStatuses),
                render: generated => generated.ToString());

            Assert.That(coveredInitialStates.Count, Is.EqualTo(5));
            Assert.That(coveredCommands.Count, Is.EqualTo(8));
            Assert.That(coveredGrounded, Is.EquivalentTo(new[] { false, true }));
            Assert.That(coveredSafeRestoration, Is.EquivalentTo(new[] { false, true }));
            Assert.That(coveredStatuses,
                Is.EquivalentTo(new[] { CommandStatus.Accepted, CommandStatus.Rejected }));
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var exhaustiveIndex = caseIndex % ExhaustiveCombinationCount;
            var initialState = (PlayerState)(exhaustiveIndex % 5);
            var grounded = ((exhaustiveIndex / 5) & 1) != 0;
            var safeRestoration = ((exhaustiveIndex / 10) & 1) != 0;
            var firstCommand = (ModelCommand)((exhaustiveIndex / 20) % 8);
            var stepCount = random.Next(6, 19);
            var steps = new List<GeneratedStep>(stepCount)
            {
                new GeneratedStep(firstCommand, grounded, safeRestoration)
            };

            for (var index = 1; index < stepCount; index++)
            {
                steps.Add(new GeneratedStep(
                    (ModelCommand)random.Next(8),
                    random.Next(2) == 1,
                    random.Next(2) == 1));
            }

            return new GeneratedCase(initialState, steps);
        }

        private static void AssertSequence(
            GeneratedCase generated,
            ISet<PlayerState> coveredInitialStates,
            ISet<ModelCommand> coveredCommands,
            ISet<bool> coveredGrounded,
            ISet<bool> coveredSafeRestoration,
            ISet<CommandStatus> coveredStatuses)
        {
            coveredInitialStates.Add(generated.InitialState);
            var modelState = generated.InitialState;

            for (var index = 0; index < generated.Steps.Count; index++)
            {
                var step = generated.Steps[index];
                coveredCommands.Add(step.Command);
                coveredGrounded.Add(step.Grounded);
                coveredSafeRestoration.Add(step.SafeRestoration);

                var expected = TransitionOracle.Evaluate(modelState, step);
                var seam = PlayerStateMachineSeam.Create(modelState, step.Grounded);
                var actual = seam.Execute(step, index);
                coveredStatuses.Add(actual.Status);

                Assert.That(actual.Status, Is.EqualTo(expected.Status),
                    Context(generated, index, modelState, step));
                Assert.That(actual.CurrentState, Is.EqualTo(expected.CurrentState),
                    Context(generated, index, modelState, step));
                Assert.That(seam.CurrentState, Is.EqualTo(expected.CurrentState),
                    "The state query must equal the command result. " +
                    Context(generated, index, modelState, step));

                modelState = expected.CurrentState;
            }
        }

        private static string Context(
            GeneratedCase generated,
            int stepIndex,
            PlayerState priorState,
            GeneratedStep step)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Initial={0}, Step={1}, Prior={2}, Command={3}, Grounded={4}, SafeRestoration={5}",
                generated.InitialState,
                stepIndex,
                priorState,
                step.Command,
                step.Grounded,
                step.SafeRestoration);
        }

        private enum ModelCommand
        {
            Lane,
            Jump,
            Slide,
            Failure,
            Reset,
            Landing,
            SafeRestoration,
            ResetCompleted
        }

        private readonly struct GeneratedStep
        {
            public GeneratedStep(
                ModelCommand command,
                bool grounded,
                bool safeRestoration)
            {
                Command = command;
                Grounded = grounded;
                SafeRestoration = safeRestoration;
            }

            public ModelCommand Command { get; }
            public bool Grounded { get; }
            public bool SafeRestoration { get; }
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(PlayerState initialState, IReadOnlyList<GeneratedStep> steps)
            {
                InitialState = initialState;
                Steps = steps;
            }

            public PlayerState InitialState { get; }
            public IReadOnlyList<GeneratedStep> Steps { get; }

            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.Append("Initial=").Append(InitialState).Append(", Steps=[");
                for (var index = 0; index < Steps.Count; index++)
                {
                    if (index > 0) builder.Append("; ");
                    var step = Steps[index];
                    builder.Append(step.Command)
                        .Append("(grounded=").Append(step.Grounded)
                        .Append(", safe=").Append(step.SafeRestoration).Append(')');
                }

                return builder.Append(']').ToString();
            }
        }

        private readonly struct ExpectedTransition
        {
            public ExpectedTransition(CommandStatus status, PlayerState currentState)
            {
                Status = status;
                CurrentState = currentState;
            }

            public CommandStatus Status { get; }
            public PlayerState CurrentState { get; }
        }

        private static class TransitionOracle
        {
            public static ExpectedTransition Evaluate(PlayerState state, GeneratedStep step)
            {
                switch (step.Command)
                {
                    case ModelCommand.Lane:
                        return IsActive(state)
                            ? Accepted(state)
                            : Rejected(state);
                    case ModelCommand.Jump:
                        return state == PlayerState.Running && step.Grounded
                            ? Accepted(PlayerState.Jumping)
                            : Rejected(state);
                    case ModelCommand.Slide:
                        return state == PlayerState.Running && step.Grounded
                            ? Accepted(PlayerState.Sliding)
                            : Rejected(state);
                    case ModelCommand.Failure:
                        return IsActive(state)
                            ? Accepted(PlayerState.Failed)
                            : Rejected(state);
                    case ModelCommand.Reset:
                        return state != PlayerState.Resetting
                            ? Accepted(PlayerState.Resetting)
                            : Rejected(state);
                    case ModelCommand.Landing:
                        return state == PlayerState.Jumping && step.Grounded
                            ? Accepted(PlayerState.Running)
                            : Rejected(state);
                    case ModelCommand.SafeRestoration:
                        return state == PlayerState.Sliding && step.SafeRestoration
                            ? Accepted(PlayerState.Running)
                            : Rejected(state);
                    case ModelCommand.ResetCompleted:
                        return state == PlayerState.Resetting
                            ? Accepted(PlayerState.Running)
                            : Rejected(state);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private static bool IsActive(PlayerState state)
            {
                return state == PlayerState.Running ||
                    state == PlayerState.Jumping ||
                    state == PlayerState.Sliding;
            }

            private static ExpectedTransition Accepted(PlayerState state)
            {
                return new ExpectedTransition(CommandStatus.Accepted, state);
            }

            private static ExpectedTransition Rejected(PlayerState state)
            {
                return new ExpectedTransition(CommandStatus.Rejected, state);
            }
        }

        private readonly struct CommandObservation
        {
            public CommandObservation(CommandStatus status, PlayerState currentState)
            {
                Status = status;
                CurrentState = currentState;
            }

            public CommandStatus Status { get; }
            public PlayerState CurrentState { get; }

            public static CommandObservation From(ActionRequestResult result)
            {
                return new CommandObservation(result.Status, result.CurrentState);
            }

            public static CommandObservation From(FailureCommandResult result)
            {
                return new CommandObservation(result.Status, result.CurrentState);
            }

            public static CommandObservation From(ResetRequestResult result)
            {
                return new CommandObservation(result.Status, result.CurrentState);
            }
        }

        private sealed class PlayerStateMachineSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.PlayerStateMachine";

            private readonly object instance;
            private readonly PropertyInfo snapshotProperty;
            private readonly MethodInfo requestLane;
            private readonly MethodInfo requestJump;
            private readonly MethodInfo requestSlide;
            private readonly MethodInfo requestFailure;
            private readonly MethodInfo requestReset;
            private readonly MethodInfo resolveLanding;
            private readonly MethodInfo resolveSlideRestoration;
            private readonly MethodInfo completeReset;

            private PlayerStateMachineSeam(object instance, Type type)
            {
                this.instance = instance;
                snapshotProperty = RequiredProperty(type, "Snapshot", typeof(PlayerSnapshot));
                requestLane = RequiredMethod(type, "RequestLane", typeof(LaneDirection));
                requestJump = RequiredMethod(type, "RequestJump");
                requestSlide = RequiredMethod(type, "RequestSlide");
                requestFailure = RequiredMethod(type, "RequestFailure");
                requestReset = RequiredMethod(type, "RequestReset", typeof(string));
                resolveLanding = RequiredMethod(type, "ResolveLanding");
                resolveSlideRestoration = RequiredMethod(
                    type, "ResolveSlideRestoration", typeof(bool));
                completeReset = RequiredMethod(type, "CompleteReset");
            }

            public PlayerState CurrentState => Snapshot.State;

            private PlayerSnapshot Snapshot =>
                (PlayerSnapshot)snapshotProperty.GetValue(instance);

            public static PlayerStateMachineSeam Create(PlayerState state, bool grounded)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 3.6 must provide " + RuntimeTypeName + ".");
                var constructor = type.GetConstructor(new[]
                {
                    typeof(PlayerSnapshot),
                    typeof(ImmutableValueSequence<string>)
                });
                Assert.That(constructor, Is.Not.Null,
                    RuntimeTypeName + " must expose PlayerStateMachine(" +
                    "PlayerSnapshot, ImmutableValueSequence<string>).");

                var snapshot = CreateSnapshot(state, grounded);
                var instance = constructor.Invoke(new object[]
                {
                    snapshot,
                    ImmutableValueSequence<string>.Empty
                });
                return new PlayerStateMachineSeam(instance, type);
            }

            public CommandObservation Execute(GeneratedStep step, int stepIndex)
            {
                switch (step.Command)
                {
                    case ModelCommand.Lane:
                        var direction = (stepIndex & 1) == 0
                            ? LaneDirection.Left
                            : LaneDirection.Right;
                        return CommandObservation.From((ActionRequestResult)requestLane.Invoke(
                            instance, new object[] { direction }));
                    case ModelCommand.Jump:
                        return CommandObservation.From((ActionRequestResult)requestJump.Invoke(
                            instance, Array.Empty<object>()));
                    case ModelCommand.Slide:
                        return CommandObservation.From((ActionRequestResult)requestSlide.Invoke(
                            instance, Array.Empty<object>()));
                    case ModelCommand.Failure:
                        return CommandObservation.From((FailureCommandResult)requestFailure.Invoke(
                            instance, Array.Empty<object>()));
                    case ModelCommand.Reset:
                        return CommandObservation.From((ResetRequestResult)requestReset.Invoke(
                            instance,
                            new object[] { "property-9-" + stepIndex.ToString(CultureInfo.InvariantCulture) }));
                    case ModelCommand.Landing:
                        return ObserveInternal(resolveLanding, Array.Empty<object>());
                    case ModelCommand.SafeRestoration:
                        return ObserveInternal(resolveSlideRestoration,
                            new object[] { step.SafeRestoration });
                    case ModelCommand.ResetCompleted:
                        return ObserveInternal(completeReset, Array.Empty<object>());
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private CommandObservation ObserveInternal(MethodInfo method, object[] arguments)
            {
                var previousState = CurrentState;
                var result = method.Invoke(instance, arguments);
                var currentState = CurrentState;
                if (result is bool accepted)
                {
                    return new CommandObservation(
                        accepted ? CommandStatus.Accepted : CommandStatus.Rejected,
                        currentState);
                }

                if (result != null)
                {
                    var status = result.GetType().GetProperty(
                        "Status", BindingFlags.Instance | BindingFlags.Public);
                    var resultState = result.GetType().GetProperty(
                        "CurrentState", BindingFlags.Instance | BindingFlags.Public);
                    if (status != null && resultState != null)
                    {
                        return new CommandObservation(
                            (CommandStatus)status.GetValue(result),
                            (PlayerState)resultState.GetValue(result));
                    }
                }

                return new CommandObservation(
                    currentState == previousState
                        ? CommandStatus.Rejected
                        : CommandStatus.Accepted,
                    currentState);
            }

            private static PlayerSnapshot CreateSnapshot(PlayerState state, bool grounded)
            {
                return new PlayerSnapshot(
                    Vector3.zero,
                    Quaternion.identity,
                    state,
                    grounded,
                    10f,
                    LogicalLane.Center,
                    LogicalLane.Center,
                    0f,
                    0f,
                    0f,
                    0f,
                    grounded ? -1f : 1f,
                    state == PlayerState.Sliding ? 1f : 0f,
                    new ColliderProfile(0.5f, 2f, new Vector3(0f, 1f, 0f)),
                    ImmutableValueSequence<LaneRequest>.Empty,
                    ImmutableValueSequence<PlayerCommandKind>.Empty,
                    state == PlayerState.Resetting);
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
                Assert.That(method, Is.Not.Null,
                    type.FullName + " must expose " + name + ".");
                return method;
            }
        }
    }
}
