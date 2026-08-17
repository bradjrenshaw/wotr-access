using HarmonyLib;
using Kingmaker.View.MapObjects;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Rebuild the room map when a door changes state. Doors carry NavmeshCuts that move with the
    /// door's animation (most do NOT use DisableNavmeshCutWhenOpen), so walkability and CONNECTIVITY
    /// change with no event — the room map, built once per area part, kept the closed-world
    /// segmentation: the DH basement's secret room stayed unwalkable after the puzzle opened it, and
    /// V listed no exits because every room was still portal-less as at build time.
    /// <c>InteractionDoorPart.Open()</c> is the single chokepoint — player interactions AND the
    /// scripted SwitchDoor action both funnel through it (it toggles, despite the name).
    /// </summary>
    [HarmonyPatch(typeof(InteractionDoorPart), nameof(InteractionDoorPart.Open))]
    internal static class DoorNavPatch
    {
        private static void Postfix() => WrathAccess.Exploration.RoomMap.InvalidateSoon();
    }
}
