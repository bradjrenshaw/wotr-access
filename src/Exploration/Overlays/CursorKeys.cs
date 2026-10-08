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

        /// <summary>The slot's held vector regardless of screen (+Z = north, +X = east). The explore.*
        /// movement actions are SHARED across in-area / local map / world map (one binding set), so each
        /// cursor gates on its own screen first (<see cref="CursorSpace.OwnsKeys"/> via
        /// <see cref="Cursor.HeldVector"/>; the local map screen's cursor checks its screen itself).</summary>
        public static void HeldVectorRaw(MovementSlot slot, out int dx, out int dz)
        {
            dx = 0; dz = 0;
            var p = Prefix(slot);
            if (InputManager.Held(p + "Up")) dz += 1;
            if (InputManager.Held(p + "Down")) dz -= 1;
            if (InputManager.Held(p + "Right")) dx += 1;
            if (InputManager.Held(p + "Left")) dx -= 1;
        }
    }
}
