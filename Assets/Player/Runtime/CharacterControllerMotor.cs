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
    /// Optional capability of a motor that owns a real transform: writing the pose directly, which is
    /// how reset restores the start transform without submitting displacement. A motor surface without
    /// this capability leaves the restored pose to the movement loop.
    /// </summary>
    public interface IPlayerPoseRestoration
    {
        void RestorePose(Vector3 position, Quaternion rotation);
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
    public sealed class CharacterControllerMotor : MonoBehaviour, IPlayerMotorSurface, IPlayerPoseRestoration
    {
        private readonly GroundProbe groundProbe = new GroundProbe();
        private readonly ColliderProfileApplicator profileApplicator = new ColliderProfileApplicator();

        /// <summary>
        /// Yaw in degrees mapping domain displacement onto the world. The domain always works in its
        /// own basis: forward is +Z, lateral is X, exactly as the movement rules are specified and
        /// tested. A track laid out along a different world axis is an authoring fact, not a change to
        /// those rules, so it is resolved here at the single point where displacement reaches Unity.
        ///
        /// Zero is the identity mapping and keeps world behaviour identical to the domain, which is why
        /// it is the default. Set 90 for a track whose travel direction is world +X.
        /// </summary>
        [Tooltip("Yaw in degrees mapping the player's domain forward (+Z) onto the track's world " +
                 "travel direction. 0 leaves domain and world aligned. Use 90 for an +X track.")]
        [SerializeField] private float trackYawDegrees;

        private CharacterController controller;
        private EnvironmentContactAdapter contactAdapter;
        private bool contactAdapterResolved;
        private int moveInvocationCount;

        /// <summary>
        /// The player's pose expressed in the domain's own basis, where forward is +Z and lateral is X.
        ///
        /// The domain reads components of this position directly: the lane planner takes X as its lateral
        /// origin, for instance. Returning the raw world position would therefore feed forward distance
        /// in as lateral offset whenever the track runs along a different world axis, and the planner
        /// would try to travel that distance sideways. Mapping back through the basis keeps every
        /// position the domain sees in the same frame as the displacement it produces.
        /// </summary>
        public Vector3 Position
        {
            get
            {
                var basis = TrackBasis;
                return basis == Quaternion.identity
                    ? transform.position
                    : Quaternion.Inverse(basis) * transform.position;
            }
        }

        public Quaternion Rotation { get { return transform.rotation; } }
        public int MoveInvocationCount { get { return moveInvocationCount; } }

        /// <summary>
        /// The rotation taking domain displacement into world space. Identity when
        /// <see cref="trackYawDegrees"/> is zero, so the common case costs nothing observable.
        /// </summary>
        public Quaternion TrackBasis
        {
            get
            {
                return trackYawDegrees == 0f
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, trackYawDegrees, 0f);
            }
        }

        /// <summary>Sets the world mapping at runtime, for scene wiring and tests.</summary>
        public void SetTrackYaw(float degrees)
        {
            trackYawDegrees = degrees;
        }

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
            var basis = TrackBasis;

            // Collision normals belong to the displacement that produced them.
            groundProbe.ClearContactSamples();
            if (capsule != null) capsule.Move(basis * displacement);

            // Realized displacement is reported back in domain space, so the caller compares like with
            // like: a lane movement blocked by collision still reads as blocked lateral travel rather
            // than appearing as motion on an axis the domain does not use.
            var realized = transform.position - before;
            return basis == Quaternion.identity ? realized : Quaternion.Inverse(basis) * realized;
        }

        /// <summary>
        /// Writes the pose directly for reset restoration. The capsule is suspended for the write, so
        /// the controller cannot resolve the teleport as a collision, and it is restored to the
        /// enabled state it had. No displacement is submitted, so Move_Invocation_Count is unchanged.
        /// </summary>
        public void RestorePose(Vector3 position, Quaternion rotation)
        {
            var capsule = Controller;
            var wasEnabled = capsule != null && capsule.enabled;
            if (wasEnabled) capsule.enabled = false;

            // The caller works in domain space, the same frame Position reports, so the pose maps back
            // through the basis on the way out. Without this a reset would restore a world pose built
            // from domain components and teleport the player off the track.
            var basis = TrackBasis;
            var worldPosition = basis == Quaternion.identity ? position : basis * position;

            transform.SetPositionAndRotation(worldPosition, rotation);
            groundProbe.ClearContactSamples();

            if (wasEnabled) capsule.enabled = true;
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
