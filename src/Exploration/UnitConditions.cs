using System.Collections.Generic;
using Kingmaker.Blueprints.Root; // LocalizedTexts (the game's condition names)
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic; // UnitCondition

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A unit's ACTING-RELEVANT conditions in the game's own words — what a sighted player reads off
    /// the model and the portrait's status icons (a knocked-down paladin, a stunned enemy) and what
    /// explains an otherwise baffling refusal ("no actions left" on a prone unit's charge). Prone is a
    /// separate state part (<c>UnitState.Prone</c>), not a UnitCondition flag, in this game; the rest
    /// come from the condition flags. Names via <see cref="LocalizedTexts.UnitConditions"/>, so every
    /// language the game ships is covered. Order = display order (most action-blocking first).
    /// </summary>
    internal static class UnitConditions
    {
        // Conditions worth speaking: the ones the game's inspect / status icons surface and that change
        // what the unit can do. Perception/immunity toggles (SeeInvisibility, TrueSeeing, …) stay out.
        private static readonly UnitCondition[] Spoken =
        {
            UnitCondition.Unconscious, UnitCondition.Paralyzed, UnitCondition.Petrified, UnitCondition.Helpless,
            UnitCondition.Stunned, UnitCondition.Sleeping, UnitCondition.Dazed, UnitCondition.Staggered,
            UnitCondition.Nauseated, UnitCondition.Confusion, UnitCondition.Frightened, UnitCondition.Shaken,
            UnitCondition.Sickened, UnitCondition.Fatigued, UnitCondition.Entangled, UnitCondition.Slowed,
            UnitCondition.Blindness, UnitCondition.Dazzled, UnitCondition.Invisible, UnitCondition.DeathDoor,
            UnitCondition.CantAct, UnitCondition.CantMove, UnitCondition.MovementBan,
        };

        /// <summary>The unit's active spoken conditions, localized; empty when none.</summary>
        public static List<string> Active(UnitEntityData unit)
        {
            var list = new List<string>();
            if (unit == null) return list;
            try
            {
                var state = unit.Descriptor?.State;
                if (state == null) return list;
                if (state.Prone.Active) list.Add(Name(UnitCondition.Prone));
                for (int i = 0; i < Spoken.Length; i++)
                    if (state.HasCondition(Spoken[i])) list.Add(Name(Spoken[i]));
            }
            catch { }
            return list;
        }

        /// <summary>The list as one spoken phrase ("prone, stunned"), or null when there is none.</summary>
        public static string Phrase(UnitEntityData unit)
        {
            var list = Active(unit);
            return list.Count > 0 ? string.Join(", ", list.ToArray()) : null;
        }

        private static string Name(UnitCondition c)
        {
            try
            {
                var s = LocalizedTexts.Instance?.UnitConditions?.GetText(c);
                if (!string.IsNullOrEmpty(s)) return TextUtil.StripRichText(s);
            }
            catch { }
            return c.ToString();
        }
    }
}
