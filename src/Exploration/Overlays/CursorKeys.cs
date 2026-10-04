using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>The held movement keys of a cursor slot as one combined vector. Four slots, each its own
    /// action set: primary = W A S D, secondary = Shift+W A S D, tertiary = arrows, quaternary =
    /// Shift+arrows. Both movement styles poll this, so held diagonals (e.g. Up+Right) move the cursor
    /// along the combined direction instead of zigzagging per-key.</summary>
    internal static class CursorKeys
    {
        public static readonly MovementSlot[] Slots =
            { MovementSlot.Primary, MovementSlot.Secondary, MovementSlot.Tertiary, MovementSlot.Quaternary };

        /// <summary>The input-action key prefix of a slot ("explore.cursor" + Up/Down/Left/Right …).</summary>
        public static string Prefix(MovementSlot slot)
        {
            switch (slot)
            {
                case MovementSlot.Secondary: return "explore.secondary";
                case MovementSlot.Tertiary: return "explore.tertiary";
                case MovementSlot.Quaternary: return "explore.quaternary";
                default: return "explore.cursor";
            }
        }

        /// <summary>The slot's held vector regardless of screen (+Z = north, +X = east). The map cursors
        /// gate on their own screen before calling this.</summary>
        public static void HeldVectorRaw(MovementSlot slot, out int dx, out int dz)
        {
            dx = 0; dz = 0;
            var p = Prefix(slot);
            if (InputManager.Held(p + "Up")) dz += 1;
            if (InputManager.Held(p + "Down")) dz -= 1;
            if (InputManager.Held(p + "Right")) dx += 1;
            if (InputManager.Held(p + "Left")) dx -= 1;
        }

        public static void HeldVector(MovementSlot slot, out int dx, out int dz)
        {
            dx = 0; dz = 0;
            // The explore.* movement actions are SHARED across in-area / local map / world map (one
            // binding set); only the in-area overlay modes read them through here, so gate on the
            // in-area context — the map cursors poll the same actions with their own screen gates.
            if (WrathAccess.Screens.ScreenManager.Current?.Key != "ctx.ingame") return;
            HeldVectorRaw(slot, out dx, out dz);
        }
    }
}
