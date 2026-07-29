using SubwaySurfers.Player.Contracts;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Optional <see cref="Animator"/>-backed animation receiver. The adapter depends only on
    /// <see cref="IAnimationReceiver"/>, so this component is one possible presentation and can be
    /// swapped for any other without the adapter changing.
    ///
    /// A command is applied as the animator parameter of the same name: a trigger is set, a boolean is
    /// raised, and any other supported parameter shape is left alone. A command whose parameter the
    /// controller does not declare is reported as unapplied rather than pushed into the animator, which
    /// keeps a mismatched controller visible as one diagnostic per attempt instead of a silent no-op.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimatorAnimationReceiver : MonoBehaviour, IAnimationReceiver
    {
        [SerializeField] private Animator animator;

        /// <summary>Commands applied to the animator since this component was created.</summary>
        public int AppliedCommandCount { get; private set; }

        /// <summary>The animator commands are applied to, resolved from this game object when unset.</summary>
        public Animator Animator
        {
            get
            {
                if (animator == null) animator = GetComponent<Animator>();
                return animator;
            }
        }

        /// <summary>
        /// Applies one command, returning false when no animator, no controller, or no matching
        /// parameter is available so the caller can report the attempt.
        /// </summary>
        public bool TryApply(AnimationCommand command)
        {
            if (string.IsNullOrEmpty(command.Name)) return false;

            var target = Animator;
            if (target == null || target.runtimeAnimatorController == null) return false;

            var parameters = target.parameters;
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (parameter.name != command.Name) continue;

                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Trigger:
                        target.SetTrigger(parameter.nameHash);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        target.SetBool(parameter.nameHash, true);
                        break;
                    default:
                        return false;
                }

                AppliedCommandCount++;
                return true;
            }

            return false;
        }
    }
}
