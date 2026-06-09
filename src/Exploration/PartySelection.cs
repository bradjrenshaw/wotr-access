using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.Common; // UIUtilityUnit.SetCharacterSelected (the game's own select path)
using TurnBased.Controllers; // CombatController (turn-based state)
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Keyboard party selection, driving the game's REAL selection (`UI.SelectionManager` →
    /// `SelectionCharacter.SelectedUnits`), so move-to-cursor's single-vs-formation behaviour falls out of
    /// the game's own logic (see <see cref="Scanner"/> / ClickGroundHandler): one member selected → only
    /// they move; the whole party selected → everyone moves into the set formation at the target.
    ///
    /// Ctrl+1..6 each own a member and that member's owned units (pets/mounts — see how the game models
    /// ownership: a pet is its own unit linked by <c>Master</c>, listed in <c>member.Pets</c>): pressing
    /// the key selects the member, pressing it again cycles to their first owned unit, and so on, wrapping
    /// back to the member. The cycle position is derived from the current single selection — no state to
    /// keep — so pressing a different key (or after a select-all) starts that member's ring at the member.
    ///
    /// The manager writes <c>SelectedUnits</c> directly and synchronously, so a selection set here is
    /// visible to the very next move. Gated to focus-mode exploration so it doesn't collide with the
    /// game's own selection keys (which work when focus mode is off and the game owns the keyboard).
    /// Arbitrary multi-select subsets are deferred — Ctrl+A (all) and Ctrl+1..6 (one + owned) cover it.
    /// </summary>
    internal static class PartySelection
    {
        private static bool Active =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        public static void SelectWholeParty() { if (Active && !TurnBasedBlocks()) DoSelectAll(); }
        public static void SelectMember(int index) { if (Active && !TurnBasedBlocks()) DoSelectMember(index); }

        // In turn-based combat the acting unit is fixed by initiative and the game keeps it selected, so
        // switching party members (or selecting all) doesn't apply — you act with one unit per turn. Stand
        // down and announce whose turn it is instead of fighting the game's selection.
        private static bool TurnBasedBlocks()
        {
            if (!CombatController.IsInTurnBasedCombat()) return false;
            var cur = CombatController.SelectedUnit;
            Tts.Speak(cur != null ? cur.CharacterName + "'s turn" : "Turn-based mode", interrupt: true);
            return true;
        }

        private static void DoSelectAll()
        {
            var sm = Game.Instance?.UI?.SelectionManager;
            if (sm == null) return;
            sm.SelectAll();
            int n = Game.Instance.SelectionCharacter.SelectedUnits.Count;
            Tts.Speak("Whole party selected, " + n + (n == 1 ? " character" : " characters"), interrupt: true);
        }

        private static void DoSelectMember(int index)
        {
            var ring = BuildRing(index);
            if (ring.Count == 0)
            {
                Tts.Speak("No party member " + (index + 1), interrupt: true);
                return;
            }

            // Cycle within this member's ring (member → owned units → back). Position is derived from the
            // current SINGLE selection: if it's one of this ring's units, advance to the next (wrapping);
            // otherwise — a different member, or a multi-select like Ctrl+A — start at the member (ring[0]).
            var current = Game.Instance.SelectionCharacter.SingleSelectedUnit;
            int pos = (current != null) ? ring.IndexOf(current) : -1;
            var unit = ring[(pos >= 0) ? (pos + 1) % ring.Count : 0];

            // Route through the game's OWN single-character select — exactly what the "select character N"
            // keybinding and a portrait click do (PartyCharacterVM.SetCharacterSelected → this). It both
            // updates the SelectedUnit reactive (so the character sheet/inventory/etc. follow) AND the
            // SelectedUnits set (via SwitchSelectionUnitInGroup), then scrolls the camera to the unit. Our
            // old sm.SelectUnit only did the SelectedUnits half, so SelectedUnit stayed stale and the sheet
            // never updated. follow:false matches the keybinding (recenter, not continuous follow). The
            // unit's "selected" voice bark still fires through SelectUnit's ask path.
            UIUtilityUnit.SetCharacterSelected(unit, follow: false);
            Tts.Speak(unit.CharacterName + " selected", interrupt: true);
        }

        // A member's selection ring: the member, then each owned, in-game, controllable unit they own
        // (pets/mounts via member.Pets — a pet is its own unit linked back by Master). Empty if there's
        // no such party slot.
        private static List<UnitEntityData> BuildRing(int index)
        {
            var ring = new List<UnitEntityData>();
            var party = Game.Instance?.Player?.PartyCharacters;
            if (party == null || index < 0 || index >= party.Count) return ring;
            var member = party[index].Value;
            if (member == null || member.View == null) return ring;

            ring.Add(member);
            var pets = member.Pets;
            if (pets != null)
            {
                foreach (var pet in pets)
                {
                    var e = pet.Entity;
                    if (e != null && e != member && e.IsInGame && e.IsDirectlyControllable
                        && e.View != null && !ring.Contains(e))
                        ring.Add(e);
                }
            }
            return ring;
        }
    }
}
