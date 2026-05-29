using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // LocalMapModel

namespace WrathAccess.Exploration
{
    /// <summary>
    /// One pass over the current area's live world, yielding every <see cref="ScanItem"/> the player can
    /// currently perceive (each proxy applies its own fog/reveal filter). Built fresh each call — the
    /// pools are live and cheap to walk — so callers always see up-to-date positions, spawns, and door
    /// state. Shared by the <see cref="Scanner"/> (which buckets by category) and the tile-view overlay
    /// (which tests footprint overlap), so both read exactly the same set of things.
    /// </summary>
    internal static class WorldScan
    {
        public static IEnumerable<ScanItem> EnumerateVisible()
        {
            var state = Game.Instance != null ? Game.Instance.State : null;
            if (state != null)
            {
                foreach (var u in state.Units)
                {
                    var p = new ProxyUnit(u);
                    if (p.IsVisible) yield return p;
                }
                foreach (var o in state.MapObjects)
                {
                    var p = new ProxyMapObject(o);
                    if (p.IsVisible) yield return p;
                }
            }
            foreach (var m in LocalMapModel.Markers)
            {
                if (m == null) continue;
                try { if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue; } catch { }
                var p = new ProxyMarker(m);
                if (p.IsVisible) yield return p;
            }
        }
    }
}
