using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Turn-based path feedback, as a contribution to the cursor's readout pipeline: whether the acting
    /// unit has a path to the cursor's spot and how long the walk is — "Path, 25 feet" (+ "beyond
    /// remaining movement" when it overruns this turn's budget), or "No path" when the spot is
    /// unreachable (the pathfinder only gets a partial route toward it). It reads with whatever else
    /// the pipeline composes for that stop — the tile, the unit under the cursor — as one utterance.
    /// Silent outside turn-based combat. Uses the game's own pathfinder via
    /// <see cref="CombatMode.TryPathInfo"/> — the same path a move would walk.
    /// </summary>
    internal sealed class PathInfoSystem : OverlaySystem
    {
        public override string Name => "Path info";
        public override string Key => "path";

        // A readout (no movement-timed playback) — Off/Continuous only.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        // Reachable means the path actually arrives at the spot; the pathfinder's fallback partial path
        // ends well short. Half a tile of slack covers node snapping.
        private const float ReachToleranceMeters = 1.0f;

        /// <summary>The path readout, for the ONE readout pipeline: on a tile Step, a glide Settle and
        /// on Demand alike, whenever turn-based combat is on and nothing is being aimed (aiming hands
        /// the stop to the area preview).</summary>
        public override System.Collections.Generic.IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled || !CombatMode.InTurnBased || Targeting.Aiming) yield break;
            yield return new OverlayAnnouncement(ctx.Want, Message.Raw(Line(ctx.Cursor)), OverlayAnnouncement.OrderPath);
        }

        private static string Line(Vector3 dest)
        {
            if (!CombatMode.TryPathInfo(dest, out float len, out float gap, out float moveAction, out float total)
                || gap > ReachToleranceMeters)
                return Message.Localized("ui", "path.none").Resolve();
            int feet = Mathf.RoundToInt(len / Geo.MetresPerFoot);
            string line = Message.Localized("ui", "path.distance", new { feet }).Resolve();
            // Mirror the game's break markers: within the move action → plain; past it but reachable →
            // it costs the standard action too; past everything → not reachable this turn.
            if (len > total + 0.05f) line += ", " + Message.Localized("ui", "path.beyond").Resolve();
            else if (len > moveAction + 0.05f) line += ", " + Message.Localized("ui", "path.uses_standard").Resolve();
            return line;
        }
    }
}
