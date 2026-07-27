using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Unity-side surface the movement loop drives. Keeping the seam narrow lets the
    /// orchestration stay free of physics calls and lets richer probing arrive later
    /// without touching the loop.
    /// </summary>
    public interface IPlayerMotorSurface
    {
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        int MoveInvocationCount { get; }
        void ApplyColliderProfile(ColliderProfile profile);
        bool SampleGrounded(
            ColliderProfile profile,
            int groundLayerMask,
            float contactTolerance,
            float normalThreshold);
        bool IsBaselineRestorationSafe(ColliderProfile baselineProfile, int obstructionLayerMask);
        Vector3 Move(Vector3 displacement);
    }

    /// <summary>
    /// Owns the <see cref="CharacterController"/> capsule: profile application, one combined
    /// displacement submission per movement update, and the minimal capsule queries the
    /// movement loop needs. It never decides state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterControllerMotor : MonoBehaviour, IPlayerMotorSurface
    {
        private const int QueryBufferLength = 16;
        private const float MinimumProbeRadius = 0.001f;

        private readonly Collider[] overlapBuffer = new Collider[QueryBufferLength];
        private readonly GroundProbe groundProbe = new GroundProbe();

        private CharacterController controller;
        private int moveInvocationCount;

        public Vector3 Position { get { return transform.position; } }
        public Quaternion Rotation { get { return transform.rotation; } }
        public int MoveInvocationCount { get { return moveInvocationCount; } }

        private CharacterController Controller
        {
            get
            {
                if (controller == null) controller = GetComponent<CharacterController>();
                return controller;
            }
        }

        public void ApplyColliderProfile(ColliderProfile profile)
        {
            var capsule = Controller;
            if (capsule == null) return;

            capsule.radius = profile.Radius;
            capsule.height = profile.Height;
            capsule.center = profile.Center;
        }

        public Vector3 Move(Vector3 displacement)
        {
            moveInvocationCount++;

            var capsule = Controller;
            var before = transform.position;

            // Collision normals belong to the displacement that produced them.
            groundProbe.ClearContactSamples();
            if (capsule != null) capsule.Move(displacement);
            return transform.position - before;
        }

        public bool SampleGrounded(
            ColliderProfile profile,
            int groundLayerMask,
            float contactTolerance,
            float normalThreshold)
        {
            return groundProbe.SampleGrounded(
                Controller, profile, groundLayerMask, contactTolerance, normalThreshold);
        }

        public bool IsBaselineRestorationSafe(ColliderProfile baselineProfile, int obstructionLayerMask)
        {
            if (obstructionLayerMask == 0) return true;

            var radius = Mathf.Max(MinimumProbeRadius, baselineProfile.Radius);
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                LowerSphereCenter(baselineProfile, radius),
                UpperSphereCenter(baselineProfile, radius),
                radius, overlapBuffer, obstructionLayerMask, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlapCount; index++)
            {
                var overlapping = overlapBuffer[index];
                if (overlapping == null || IsOwnCollider(overlapping)) continue;
                if (overlapping.GetComponentInParent<IEnvironmentObstruction>() != null) return false;
            }

            return true;
        }

        /// <summary>
        /// Unity reports each collider the capsule touched during the last displacement. The probe
        /// keeps those normals so overlapping geometry can be judged by its real orientation.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null) return;

            groundProbe.RecordContactSample(hit.collider, hit.normal);
        }

        private bool IsOwnCollider(Collider candidate)
        {
            return candidate.transform == transform || candidate.transform.IsChildOf(transform);
        }

        private Vector3 LowerSphereCenter(ColliderProfile profile, float radius)
        {
            return transform.TransformPoint(profile.Center) - transform.up * SphereOffset(profile, radius);
        }

        private Vector3 UpperSphereCenter(ColliderProfile profile, float radius)
        {
            return transform.TransformPoint(profile.Center) + transform.up * SphereOffset(profile, radius);
        }

        private static float SphereOffset(ColliderProfile profile, float radius)
        {
            return Mathf.Max(0f, profile.Height * 0.5f - radius);
        }
    }
}
