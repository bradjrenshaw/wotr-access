using Kingmaker;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlotEmpty
using System.Linq;
using WrathAccess.Localization;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Action-bar hotkeys, mirroring the game's own: the PC action bar binds
    /// <c>action-bar-button-&lt;i&gt;</c> for every keybind slot view (three rows of 14 — the main
    /// row plus the two "additional" rows the phase button reveals) straight to that slot VM's
    /// <c>OnMainClick</c>, index = row * 14 + column into <c>ActionBarVM.Slots</c>, which
    /// <c>SetMechanicSlots</c> fills from the selected unit's <c>UISettings.GetSlot(i)</c>. We
    /// register one rebindable action per slot (<c>actionbar.r&lt;row&gt;s&lt;slot&gt;</c>) and route
    /// the press through <see cref="Targeting.Activate"/> — the same branch our HUD node uses
    /// (self-cast / aim / toggle / convert flyout), so a hotkey and an Enter on the node behave
    /// identically. Empty or bad slots speak their position instead of doing nothing.
    /// </summary>
    internal static class ActionBarHotkeys
    {
        public const int Rows = 3;
        public const int PerRow = 14;

        public static string Key(int row, int slot) => $"actionbar.r{row + 1}s{slot + 1}";

        public static void Use(int row, int slot)
        {
            int index = row * PerRow + slot;
            var bar = Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ActionBarVM;
            var unit = bar?.SelectedUnit?.Value;
            if (bar == null || unit == null || index < 0 || index >= bar.Slots.Count)
            {
                Tts.Speak(Loc.T("actionbar.unavailable"), interrupt: true);
                return;
            }
            var vm = bar.Slots[index];
            var m = vm?.MechanicActionBarSlot;
            if (m == null || m is MechanicActionBarSlotEmpty || m.IsBad())
            {
                Tts.Speak(Loc.T("actionbar.slot_empty"), interrupt: true);
                return;
            }
            // A hotkey while aiming another ability abandons that aim (user design): the new key
            // then behaves exactly as a fresh first press — name-and-confirm or enter its own aim.
            if (Targeting.Aiming) { Targeting.Cancel(); _pendingIndex = -1; }
            // Immediate slots (self-cast abilities, items, toggles) fire on the click with no prompt,
            // so a blind press could be the wrong ability, or catch allies in a burst. First press
            // NAMES it — and, for an area effect, who it would hit right now — second press within
            // the window fires it (user design; the sighted equivalent is the hover AoE decals).
            float now = UnityEngine.Time.unscaledTime;
            // Can't be used (nor, for a toggle, switched off) right now: the first press still NAMES
            // it (so you can tell which key is which); the second runs the HUD node's activation,
            // which surfaces the game's own refusal (the warning text when it raises one, a spoken
            // fallback when it only plays the sound).
            bool blocked = !m.IsPossibleActive()
                && !(m is MechanicActionBarSlotActivableAbility act && act.IsPossibleDeactivate());
            if (blocked || AbilityTargeting.IsImmediate(m))
            {
                var stateKey = WrathAccess.UI.ActionBarNodes.ToggleStateKey(m);
                if (_pendingIndex == index && now - _pendingAt <= ConfirmWindowSec)
                {
                    _pendingIndex = -1;
                    WrathAccess.UI.ActionBarNodes.Activate(vm); // same path as Enter on the node
                    if (blocked) return; // the refusal (game warning / fallback) IS the feedback
                    // Toggles report the state they landed in (SetIsOn flips synchronously; a refused
                    // flip reads back unchanged, which is the honest answer).
                    var after = WrathAccess.UI.ActionBarNodes.ToggleStateKey(m);
                    Tts.Speak(after != null
                        ? Loc.T("actionbar.toggled", new { name = m.GetTitle(), state = Loc.T(after) })
                        : m.GetTitle(), interrupt: true);
                    return;
                }
                _pendingIndex = index; _pendingAt = now;
                var ability = AbilityTargeting.AbilityOf(m);
                var hits = ability != null ? AbilityTargeting.AffectedNow(ability) : null;
                if (stateKey != null)
                    Tts.Speak(Loc.T("actionbar.confirm_state", new { name = m.GetTitle(), state = Loc.T(stateKey) }), interrupt: true);
                else if (hits != null && hits.Count > 0)
                    Tts.Speak(Loc.T("actionbar.confirm_affects", new
                    {
                        name = m.GetTitle(),
                        targets = string.Join(", ", hits.Select(u => u.CharacterName)),
                    }), interrupt: true);
                else
                    Tts.Speak(Loc.T("actionbar.confirm", new { name = m.GetTitle() }), interrupt: true);
                return;
            }
            _pendingIndex = -1;
            // Targeted: name what the key hit; the aim prompt queues after it.
            Tts.Speak(m.GetTitle(), interrupt: true);
            WrathAccess.UI.ActionBarNodes.Activate(vm); // same path as Enter on the node
        }

        private const float ConfirmWindowSec = 6f;
        private static int _pendingIndex = -1;
        private static float _pendingAt;
    }
}
