using UnityEngine;

namespace SubwaySurfers.Player.Domain
{
    public static class ForwardDisplacement
    {
        public static Vector3 Calculate(
            PlayerState state,
            float forwardSpeed,
            float elapsedSimulationTime)
        {
            if (state != PlayerState.Running &&
                state != PlayerState.Jumping &&
                state != PlayerState.Sliding)
            {
                return Vector3.zero;
            }

            return Vector3.forward * forwardSpeed * elapsedSimulationTime;
        }
    }
}
