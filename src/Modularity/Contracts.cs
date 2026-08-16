using System;

namespace WrathAccess.Modularity
{
    /// <summary>
    /// The reloadable feature module (the TBW/NonVisualCalculus host-module pattern, adapted: the
    /// module references the HOST assembly directly — host statics like <c>Main.Log</c>/<c>ModDir</c>
    /// are its services, no indirection layer — while the host knows the module ONLY through this
    /// interface). One implementor per module assembly; the loader instantiates it, calls
    /// <see cref="Load"/>, then <see cref="Tick"/> every frame on the Unity main thread.
    ///
    /// <see cref="IDisposable.Dispose"/> must undo every PERSISTENT game-side effect Load created:
    /// Harmony patches (use a per-generation id), EventBus/RulebookEventBus subscriptions, spawned
    /// GameObjects/components, the KeyboardAccess.Disabled scope, NAudio outputs, speech engine
    /// handles, dev-server route registrations. Statics inside the old generation just become
    /// garbage — only hooks INTO the game or the host outlive it. The loader disposes the old
    /// generation BEFORE loading the new one (our systems are process-global; two live generations
    /// would double-subscribe and double-speak).
    /// </summary>
    public interface IModModule : IDisposable
    {
        void Load();
        void Tick();
    }
}
