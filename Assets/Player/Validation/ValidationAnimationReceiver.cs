using System.Collections.Generic;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player.Validation
{
    /// <summary>
    /// Validation-only animation receiver. Records the ordered commands the animation layer applied so
    /// the scene can display state-to-command mapping without an Animator or any production art.
    /// Returning false on demand exercises the receiver-failure diagnostic path.
    /// Non-production: real projects bind an Animator-backed receiver instead.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ValidationAnimationReceiver : MonoBehaviour, IAnimationReceiver,
        INonProductionValidationDouble
    {
        [SerializeField]
        [Tooltip("When enabled, every command is rejected so the receiver-failure diagnostic path " +
                 "becomes observable. Simulation must stay unaffected either way.")]
        private bool rejectEveryCommand;

        private readonly List<string> appliedCommands = new List<string>();

        public IReadOnlyList<string> AppliedCommands
        {
            get { return appliedCommands; }
        }

        public string LastCommand
        {
            get { return appliedCommands.Count == 0 ? string.Empty : appliedCommands[appliedCommands.Count - 1]; }
        }

        public bool RejectEveryCommand
        {
            get { return rejectEveryCommand; }
            set { rejectEveryCommand = value; }
        }

        public string NonProductionNotice
        {
            get
            {
                return "Validation-only animation receiver for PlayerTestScene. Not production " +
                       "animation content.";
            }
        }

        public bool TryApply(AnimationCommand command)
        {
            if (rejectEveryCommand) return false;

            appliedCommands.Add(command.Name ?? string.Empty);
            return true;
        }

        public void ClearRecordedCommands()
        {
            appliedCommands.Clear();
        }
    }
}
