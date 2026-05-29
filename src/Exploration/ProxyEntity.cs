using Kingmaker.EntitySystem; // EntityDataBase
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A <see cref="ScanItem"/> backed by a live game entity. Reads its position live and filters to
    /// what the player can actually perceive (so we don't leak fogged/hidden things — see the
    /// surface-only-visible memory). Concrete kinds: <see cref="ProxyUnit"/>, <see cref="ProxyMapObject"/>.
    /// </summary>
    internal abstract class ProxyEntity : ScanItem
    {
        protected readonly EntityDataBase Entity;

        protected ProxyEntity(EntityDataBase entity) { Entity = entity; }

        public override Vector3 Position => Geo.Live(Entity); // live view transform, not the lagging data position

        public override bool IsVisible => Entity.IsInGame && Entity.IsVisibleForPlayer;
    }
}
