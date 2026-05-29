using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;
using Kingmaker.Utility; // UIConsts.GetAnswerString
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// One player answer in a conversation (<see cref="AnswerVM"/>). Announces its number + text and,
    /// when the answer can't be chosen (a failed requirement — the game greys these out), an
    /// "unavailable" state. Activate selects it via the VM's contract (<see cref="AnswerVM.OnChooseAnswer"/>),
    /// which advances the dialogue — so the next cue announces itself, we don't re-announce — and plays
    /// the game's NextDialogLine sound itself, so we suppress our own click sound.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyDialogAnswer : UIElement
    {
        private readonly AnswerVM _vm;

        public ProxyDialogAnswer(AnswerVM vm) { _vm = vm; }

        private bool Enabled => _vm != null && _vm.Enable.Value;

        // OnChooseAnswer plays NextDialogLine; returning null avoids doubling it.
        public override Kingmaker.UI.UISoundType? ActivateSound => null;

        // Use the game's own answer formatter so we read exactly what's drawn: the numbered prefix plus
        // the skill-check "[Athletics 12]", alignment "(Evil)", mythic, and show-check tags — all gated by
        // the same dialogue settings, so we surface only what's actually shown. It returns TMP rich text
        // ((<link>)-wrapped checks); Tts strips that at speak time. The bind name matches the view's own
        // (DialogAnswerView builds "DialogChoice{Index}"). Falls back to plain text for system answers.
        private string AnswerText()
        {
            var bp = _vm?.Answer?.Value;
            if (bp != null)
            {
                try { return UIConsts.GetAnswerString(bp, "DialogChoice" + _vm.Index, _vm.Index); }
                catch { /* fall through to the plain form */ }
            }
            var text = bp != null ? bp.DisplayText : null;
            if (string.IsNullOrEmpty(text)) text = "Continue"; // system/continue answers carry no text
            return _vm.Index + ". " + text;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(AnswerText()));
            yield return new RoleAnnouncement("button");
            yield return new EnabledAnnouncement(Enabled);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Enabled)
                yield return new ElementAction(ActionIds.Activate, Message.Raw("Choose"), _ => _vm.OnChooseAnswer());
        }
    }
}
