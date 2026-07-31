using System;
using SubwaySurfers.Player.Configuration;
using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Unity-side owner of the animation adapter's lifetime. An interface field cannot be serialized,
    /// so the receiver arrives as a concrete <see cref="MonoBehaviour"/> or asset reference through
    /// validated configuration and is resolved to <see cref="IAnimationReceiver"/> here, during facade
    /// initialization, exactly as <see cref="PlayerInputAdapter"/> resolves its action asset. A
    /// reference that does not implement the interface resolves to no receiver, which the adapter
    /// reports per attempted command instead of failing initialization.
    ///
    /// Re-initialization replaces the receiver on the existing adapter rather than rebuilding it, so a
    /// configuration pass never adds a second subscription and never replays a past command.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationBridge : MonoBehaviour, IPlayerConfigurationConsumer
    {
        private PlayerEventHub events;
        private IAnimationReceiver receiver;
        private PlayerAnimationAdapter adapter;

        /// <summary>The adapter this component owns, or null while no event source is bound.</summary>
        public PlayerAnimationAdapter Adapter { get { return adapter; } }

        /// <summary>True once a serialized reference resolved to a usable animation receiver.</summary>
        public bool ReceiverResolved { get { return receiver != null; } }

        /// <summary>
        /// Binds the event source transitions are observed on and the receiver commands are applied to.
        /// Receiver failures are published as validation diagnostics on the same hub, so an integration
        /// already listening to the player's diagnostics sees them without extra wiring.
        /// </summary>
        public void Configure(PlayerEventHub events, IAnimationReceiver receiver)
        {
            this.events = events;
            this.receiver = receiver;
            Rebuild();
        }

        /// <summary>
        /// Facade-driven configuration: the player's event source is bound and the effective animation
        /// receiver reference is resolved to its interface and installed on the adapter. The hub is
        /// owned in code rather than authored, so a serialized prefab has no reference to assign for
        /// it; without binding it here the adapter would never exist and a configured animation layer
        /// would silently apply no command. An event source bound by an explicit
        /// <see cref="Configure"/> call is kept, so initialization never rebuilds a live adapter.
        /// </summary>
        public void Initialize(
            PlayerConfiguration configuration, PlayerConfigurationReferences references)
        {
            if (events == null)
                events = PlayerControllerFacade.ResolveEventSource(gameObject, references);

            receiver = references.AnimationReceiver as IAnimationReceiver;
            if (adapter == null)
            {
                Rebuild();
                return;
            }

            adapter.ReplaceReceiver(receiver);
        }

        private void OnDestroy()
        {
            Release();
        }

        private void Rebuild()
        {
            Release();
            if (events == null) return;

            Action<ValidationDiagnostic> sink = events.PublishValidation;
            adapter = new PlayerAnimationAdapter(events, receiver, sink);
        }

        private void Release()
        {
            if (adapter == null) return;

            adapter.Dispose();
            adapter = null;
        }
    }
}
