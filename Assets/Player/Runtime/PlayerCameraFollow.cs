using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Unity-side owner of the camera follow. All camera arithmetic lives in the pure
    /// <see cref="CameraConvergenceState"/>; this component only supplies the player position and the
    /// elapsed camera-follow time of a rendered frame and writes the resulting pose to the camera
    /// transform. <c>LateUpdate</c> owns the per-frame drive so the player's rendered transform for
    /// the frame is already available.
    ///
    /// An interface field cannot be serialized, so the followed player arrives as a concrete
    /// <see cref="MonoBehaviour"/> and is resolved to <see cref="IPlayerQueries"/> during
    /// facade-controlled initialization, exactly as <see cref="PlayerAnimationBridge"/> resolves its
    /// receiver. <c>Camera_Follow_Offset</c> is derived from the resolved player's pose and the
    /// configured initial camera pose, never serialized on its own, which is what makes the initial
    /// <c>Camera_Position_Error</c> zero and keeps a reset from accumulating.
    ///
    /// The component holds no movement authority: the player's position is only read, and the target
    /// is derived from it in every <c>Player_State</c>, including Failed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraFollow :
        MonoBehaviour, IPlayerConfigurationConsumer, IPlayerResetParticipant
    {
        [SerializeField] private MonoBehaviour playerQuerySource;

        private IPlayerQueries player;
        private CameraConvergenceState convergence;
        private Vector3 configuredPlayerPose;
        private Vector3 configuredCameraPose;
        private bool playerSuppliedProgrammatically;

        /// <summary>True once a serialized reference or a <see cref="Configure"/> call resolved to a
        /// usable player query contract. An unresolved adapter leaves the camera transform alone.</summary>
        public bool PlayerResolved { get { return player != null; } }

        /// <summary>The camera pose this adapter last wrote to its transform.</summary>
        public Vector3 CameraPosition
        {
            get { return convergence == null ? transform.position : convergence.CameraPosition; }
        }

        /// <summary><c>Camera_Follow_Offset</c>, derived from the configured player and camera poses.</summary>
        public Vector3 CameraFollowOffset
        {
            get { return convergence == null ? Vector3.zero : convergence.CameraFollowOffset; }
        }

        /// <summary><c>Camera_Target_Position</c> for the player's current position.</summary>
        public Vector3 CameraTargetPosition
        {
            get
            {
                if (convergence == null || player == null) return CameraPosition;
                return player.Snapshot.Position + convergence.CameraFollowOffset;
            }
        }

        /// <summary>Distance from the current camera pose to <see cref="CameraTargetPosition"/>.</summary>
        public float CameraPositionError
        {
            get { return (CameraTargetPosition - CameraPosition).magnitude; }
        }

        /// <summary>Convergence velocity realized by the most recent positive-time frame.</summary>
        public Vector3 CameraConvergenceVelocity
        {
            get { return convergence == null ? Vector3.zero : convergence.CameraConvergenceVelocity; }
        }

        /// <summary>Smoothing budget left in the current <c>Camera_Settle_Duration</c> epoch.</summary>
        public float RemainingCameraSettleTime
        {
            get { return convergence == null ? 0f : convergence.RemainingCameraSettleTime; }
        }

        /// <summary>
        /// Programmatic wiring for integrations and tests. Authoritative over the serialized
        /// reference, and callable after <c>Awake</c>, so a later configuration pass keeps following
        /// the supplied player while adopting the new configuration.
        /// </summary>
        public void Configure(IPlayerQueries player, PlayerConfiguration configuration)
        {
            this.player = player;
            playerSuppliedProgrammatically = player != null;
            Rebuild(configuration);
        }

        /// <summary>
        /// Facade-driven configuration: the serialized concrete reference is resolved to the player
        /// query contract and the camera is placed at the configured initial camera pose, from which
        /// <c>Camera_Follow_Offset</c> is derived.
        /// </summary>
        public void Initialize(
            PlayerConfiguration configuration, PlayerConfigurationReferences references)
        {
            if (!playerSuppliedProgrammatically) player = playerQuerySource as IPlayerQueries;
            Rebuild(configuration);
        }

        /// <summary>
        /// One rendered frame of camera follow. Public and independent of the enabled state so a
        /// frame can be supplied explicitly, and a no-op while no player is resolved.
        /// </summary>
        public void ExecuteCameraFollowUpdate(float elapsedCameraFollowTime)
        {
            if (player == null || convergence == null) return;

            convergence.Advance(player.Snapshot.Position, elapsedCameraFollowTime);
            transform.position = convergence.CameraPosition;
        }

        /// <summary>
        /// Restores the configured initial camera pose, keeps the offset derived at initialization,
        /// and clears convergence velocity and smoothing progress. Repeat-safe: consecutive resets
        /// produce the same pose.
        /// </summary>
        public void ResetCameraPose()
        {
            if (convergence == null) return;

            convergence.Reset(configuredPlayerPose, configuredCameraPose);
            transform.position = convergence.CameraPosition;
        }

        /// <summary>
        /// Camera participation in the atomic reset sequence: the same restoration
        /// <see cref="ResetCameraPose"/> performs, run between the Resetting and Running transitions.
        /// </summary>
        public void RestoreInitialState()
        {
            ResetCameraPose();
        }

        private void LateUpdate()
        {
            ExecuteCameraFollowUpdate(Time.deltaTime);
        }

        private void Rebuild(PlayerConfiguration configuration)
        {
            if (player == null)
            {
                convergence = null;
                return;
            }

            configuredPlayerPose = player.Snapshot.Position;
            configuredCameraPose = configuration.InitialCameraPosition;
            convergence = new CameraConvergenceState(
                configuredPlayerPose,
                configuredCameraPose,
                configuration.CameraSettleDuration,
                configuration.CameraFollowTolerance);
            transform.position = convergence.CameraPosition;
        }
    }
}
