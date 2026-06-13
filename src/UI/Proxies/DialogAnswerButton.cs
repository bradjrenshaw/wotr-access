using Kingmaker.UI.MVVM._VM.Dialog.Dialog;
using Kingmaker.Utility; // UIConsts.GetAnswerString

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Builds a <see cref="ProxyActionButton"/> for one player answer in a conversation
    /// (<see cref="AnswerVM"/>). An answer is just label + enabled + choose, so it's a plain button;
    /// the twists are read here: the label uses the game's own answer formatter (numbered prefix plus
    /// skill-check / alignment / mythic tags, gated by the same dialogue settings, so we surface only
    /// what's drawn), activation goes through <see cref="AnswerVM.OnChooseAnswer"/> (which advances the
    /// dialogue — the next cue announces itself — and plays NextDialogLine, so we suppress our own click
    /// sound), and the action verb reads "Choose".
    /// </summary>
    public static class DialogAnswerButton
    {
        public static ProxyActionButton For(AnswerVM vm)
            => new ProxyActionButton(() => AnswerText(vm), () => vm != null && vm.Enable.Value,
                () => vm?.OnChooseAnswer(), suppressActivateSound: true, actionVerb: "choose",
                // The answer text carries a per-stat skill-check DC <link>; resolve it from this
                // answer's own DC list (Space → the DC-preview tooltip). Glossary links fall through.
                linkResolver: (id, keys) => DialogLinks.ResolveSkillCheck(keys, null, vm?.Answer?.Value?.SkillChecksDC));

        // The bind name matches the view's own (DialogAnswerView builds "DialogChoice{Index}"). Returns TMP
        // rich text ((<link>)-wrapped checks); Tts strips that at speak time. Plain fallback for system answers.
        private static string AnswerText(AnswerVM vm)
        {
            if (vm == null) return "";
            var bp = vm.Answer?.Value;
            var text = bp != null ? bp.DisplayText : null;
            // System/continue answers carry no text — the game's button reads a plain "Continue",
            // UNNUMBERED (only real choices get the index prefix).
            if (string.IsNullOrEmpty(text)) return Message.Localized("ui", "label.continue").Resolve();
            try { return UIConsts.GetAnswerString(bp, "DialogChoice" + vm.Index, vm.Index); }
            catch { return vm.Index + ". " + text; }
        }
    }
}
