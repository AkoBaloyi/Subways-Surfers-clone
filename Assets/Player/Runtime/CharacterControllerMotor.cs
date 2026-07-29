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
    /// Owns the <see cref="CharacterController"/> capsule: one combined displacement submission per
    /// movement update, and the capsule queries the movement loop needs. Grounding lives in
    /// <see cref="GroundProbe"/>, and profile application plus the safe-restoration query live in
    /// <see cref="ColliderProfileApplicator"/>; this component only wires them to the capsule. It
    /// never decides state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterControllerMotor : MonoBehaviour, IPlayerMotorSurface
    {
        private readonly GroundProbe groundProbe = new GroundProbe();
        private readonly ColliderProfileApplicator profileApplicator = new ColliderProfileApplicator();

        private CharacterController controller;
        private EnvironmentContactAdapter contactAdapter;
        private bool contactAdapterResolved;
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
            profileApplicator.Apply(Controller, profile);
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
            return profileApplicator.IsRestorationSafe(
                Controller, baselineProfile, obstructionLayerMask);
        }

        /// <summary>
        /// Unity reports each collider the capsule touched during the last displacement. The probe
        /// keeps those normals so overlapping geometry can be judged by its real orientation, and the
        /// contact adapter receives the contact point so a published contact carries the world-space
        /// position of the touch. The hit never decides whether a contact exists; the adapter's overlap
        /// sample does.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null) return;

            groundProbe.RecordContactSample(hit.collider, hit.normal);

            var adapter = ContactAdapter;
            if (adapter != null) adapter.RecordContactPosition(hit.collider, hit.point);
        }

        /// <summary>
        /// The optional contact adapter on this player, resolved once. A player without one still
        /// moves and probes; only contact positions are then unavailable.
        /// </summary>
        private EnvironmentContactAdapter ContactAdapter
        {
            get
            {
                if (!contactAdapterResolved)
                {
                    TryGetComponent(out contactAdapter);
                    contactAdapterResolved = true;
                }

                return contactAdapter;
            }
        }
    }
}
