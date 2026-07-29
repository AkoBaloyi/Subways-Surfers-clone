using System;
using System.Globalization;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Presentation-only listener that turns a published <c>Player_State</c> transition into one
    /// animation command. The adapter observes the event source and nothing else: it holds no motor,
    /// no movement loop, and no command surface, so it has no route through which it could move the
    /// player or change simulation state. Every failure it can encounter is therefore observable only
    /// as a diagnostic.
    ///
    /// Each of the five states maps to its own command, and the five command names are distinct, so a
    /// receiver can tell the transitions apart by name alone. A state outside the mapping is reported
    /// as unavailable rather than guessed at, and the receiver is left uninvoked.
    ///
    /// One published transition is exactly one receiver call, issued inline in the publication that
    /// carried it, so animation never lags a frame behind the state it depicts and never fires twice
    /// for one transition. Nothing is buffered: replacing the receiver swaps a single reference and
    /// replays nothing, so a receiver installed after a transition sees only later transitions and the
    /// receiver it replaced sees no further commands.
    ///
    /// A receiver that is absent, unmapped, rejecting, or throwing produces exactly one categorized
    /// diagnostic for the attempted command, naming the receiver, the state, and - when a receiver
    /// instance exists - its type. A throwing receiver is contained here rather than allowed to escape
    /// into the publication, so one broken receiver cannot disturb the publication or any other
    /// subscriber.
    /// </summary>
    public sealed class PlayerAnimationAdapter : IDisposable
    {
        private const string RunningCommandName = "Player_Run";
        private const string JumpingCommandName = "Player_Jump";
        private const string SlidingCommandName = "Player_Slide";
        private const string FailedCommandName = "Player_Fail";
        private const string ResettingCommandName = "Player_Reset";

        private static readonly AnimationCommand[] ConfiguredCommands =
        {
            new AnimationCommand(RunningCommandName),
            new AnimationCommand(JumpingCommandName),
            new AnimationCommand(SlidingCommandName),
            new AnimationCommand(FailedCommandName),
            new AnimationCommand(ResettingCommandName)
        };

        private readonly IPlayerEventSource events;
        private readonly Action<ValidationDiagnostic> diagnosticSink;
        private readonly Action<PlayerStateChangedEvent> stateChangedHandler;

        // Read once per command and written whole on replacement: a reference assignment is atomic, so
        // a command in flight either sees the previous receiver or the new one, never a mixed state.
        private IAnimationReceiver receiver;
        private bool subscribed;

        /// <summary>
        /// Binds the event source whose transitions drive animation, the receiver commands are applied
        /// to, and the sink receiver failures are reported to. A null receiver is a supported
        /// configuration: the adapter still runs and reports each attempted command as absent. The
        /// subscription is made once, here, so a transition is never observed twice.
        /// </summary>
        public PlayerAnimationAdapter(
            IPlayerEventSource events,
            IAnimationReceiver receiver,
            Action<ValidationDiagnostic> diagnosticSink)
        {
            this.events = events;
            this.receiver = receiver;
            this.diagnosticSink = diagnosticSink;
            stateChangedHandler = HandleStateChanged;

            if (events == null) return;

            events.StateChanged += stateChangedHandler;
            subscribed = true;
        }

        /// <summary>The receiver commands are currently routed to, or null while none is configured.</summary>
        public IAnimationReceiver Receiver { get { return receiver; } }

        /// <summary>Animation commands attempted since construction, one per published transition.</summary>
        public int AttemptedCommandCount { get; private set; }

        /// <summary>Receiver failures reported since construction, one per failed attempt.</summary>
        public int ReportedFailureCount { get; private set; }

        /// <summary>
        /// The command configured for a state, or the default command when the state has no mapping.
        /// </summary>
        public AnimationCommand ResolveCommand(PlayerState state)
        {
            AnimationCommand command;
            return TryResolveCommand(state, out command) ? command : default(AnimationCommand);
        }

        /// <summary>
        /// Installs the receiver later commands are applied to. The swap is a single reference
        /// assignment: no command is replayed to the new receiver, the replaced receiver is not
        /// invoked again, and no simulation state is touched.
        /// </summary>
        public void ReplaceReceiver(IAnimationReceiver replacement)
        {
            receiver = replacement;
        }

        /// <summary>Removes the subscription this adapter added, leaving the event source otherwise untouched.</summary>
        public void Dispose()
        {
            if (!subscribed) return;

            events.StateChanged -= stateChangedHandler;
            subscribed = false;
        }

        private bool TryResolveCommand(PlayerState state, out AnimationCommand command)
        {
            var index = (int)state;
            if (index >= 0 && index < ConfiguredCommands.Length)
            {
                command = ConfiguredCommands[index];
                return true;
            }

            command = default(AnimationCommand);
            return false;
        }

        private void HandleStateChanged(PlayerStateChangedEvent payload)
        {
            Apply(payload.CurrentState);
        }

        private void Apply(PlayerState state)
        {
            AttemptedCommandCount++;

            // One read of the reference for the whole attempt, so a replacement made while this
            // command is in flight cannot split it across two receivers.
            var target = receiver;

            AnimationCommand command;
            if (!TryResolveCommand(state, out command))
            {
                Report(
                    DiagnosticCode.AnimationReceiverUnavailable,
                    state,
                    "No animation command is configured for Player_State " + StateName(state) +
                    ", so the animation receiver " + ReceiverDescription(target) +
                    " was not invoked and no command was issued.");
                return;
            }

            if (target == null)
            {
                Report(
                    DiagnosticCode.AnimationReceiverAbsent,
                    state,
                    "No animation receiver is configured, so the animation command '" + command.Name +
                    "' for Player_State " + StateName(state) + " was not applied.");
                return;
            }

            bool applied;
            try
            {
                applied = target.TryApply(command);
            }
            catch (Exception exception)
            {
                // Contained here: a receiver exception must not escape into the publication that
                // carried the transition, and it must leave the simulation exactly as it found it.
                Report(
                    DiagnosticCode.AnimationReceiverException,
                    state,
                    "The animation receiver " + ReceiverDescription(target) + " threw " +
                    exception.GetType().Name + " while applying the animation command '" +
                    command.Name + "' for Player_State " + StateName(state) + ".");
                return;
            }

            if (applied) return;

            Report(
                DiagnosticCode.AnimationReceiverRejected,
                state,
                "The animation receiver " + ReceiverDescription(target) +
                " rejected the animation command '" + command.Name + "' for Player_State " +
                StateName(state) + ".");
        }

        private void Report(DiagnosticCode code, PlayerState state, string message)
        {
            ReportedFailureCount++;

            var diagnostic = new ValidationDiagnostic(
                DiagnosticSeverity.Warning,
                code,
                "AnimationReceiver/" + StateName(state),
                message);

            var sink = diagnosticSink;
            if (sink != null) sink(diagnostic);
        }

        private static string ReceiverDescription(IAnimationReceiver target)
        {
            return target == null ? "(absent)" : target.GetType().Name;
        }

        /// <summary>
        /// The state's name, or its numeric value when the value is outside the defined states, so a
        /// diagnostic always identifies the state it concerns.
        /// </summary>
        private static string StateName(PlayerState state)
        {
            return Enum.IsDefined(typeof(PlayerState), state)
                ? state.ToString()
                : ((int)state).ToString(CultureInfo.InvariantCulture);
        }
    }
}
