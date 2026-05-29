using Kingmaker;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Keyboard party selection, driving the game's REAL selection (`UI.SelectionManager` →
    /// `SelectionCharacter.SelectedUnits`), so move-to-cursor's single-vs-formation behaviour falls out of
    /// the game's own logic (see <see cref="Scanner"/> / ClickGroundHandler): one member selected → only
    /// they move; the whole party selected → everyone moves into the set formation at the target.
    ///
    /// The manager writes <c>SelectedUnits</c> directly and synchronously, so a selection set here is
    /// visible to the very next move. Gated to focus-mode exploration so it doesn't collide with the
    /// game's own selection keys (which work when focus mode is off and the game owns the keyboard).
    /// Multi-select (arbitrary subsets) is deferred — Ctrl+A (all) and Ctrl+1..6 (one) cover the cases.
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
            var party = Game.Instance?.Player?.PartyCharacters;
            var sm = Game.Instance?.UI?.SelectionManager;
            if (party == null || sm == null || index < 0 || index >= party.Count)
            {
                Tts.Speak("No party member " + (index + 1), interrupt: true);
                return;
            }
            var unit = party[index].Value;
            if (unit == null || unit.View == null)
            {
                Tts.Speak("No party member " + (index + 1), interrupt: true);
                return;
            }
            // single: true clears the rest and selects only this unit; ask: false suppresses the unit's
            // selection voice line so our name announce reads clean.
            sm.SelectUnit(unit.View, single: true, sendEvent: true, ask: false);
            Tts.Speak(unit.CharacterName + " selected", interrupt: true);
        }
    }
}
