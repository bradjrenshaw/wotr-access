using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI; // UISoundType
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

        public static void SelectWholeParty() { if (Active) DoSelectAll(); }
        public static void SelectMember(int index) { if (Active) DoSelectMember(index); }

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
            var sm = Game.Instance?.UI?.SelectionManager;
            var ring = BuildRing(index);
            if (sm == null || ring.Count == 0)
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

            // The portrait click plays this select sound before selecting (GroupCharacter.SelectUnit), so
            // we replay it for parity. (Select-all plays no sound, so Ctrl+A stays silent, matching.)
            UiSound.Play(UISoundType.CharacterSelect);
            // single: true clears the rest and selects only this unit. ask: true mirrors the game's own
            // portrait/world-click select: it lets the unit's "Selected" voice bark fire through the game's
            // logic (Asks.Selected.Schedule), gated by the player's VoicedAskFrequency setting + the bark
            // cooldown/chance — exactly as for a sighted player. We don't suppress or de-dupe it; our
            // "X selected" announce just adds the name (the voice line doesn't reliably say who it is).
            sm.SelectUnit(unit.View, single: true, sendEvent: true, ask: true);
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
