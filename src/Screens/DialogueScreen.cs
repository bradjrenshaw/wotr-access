using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.GameModes;
using Kingmaker.UI.Common; // UIUtility.SkillCheckText
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// An in-game conversation (the common <see cref="DialogVM"/>): the NPC cue line, then the numbered
    /// player answers (or a single Continue). The cue is the first focusable element, so focusing it
    /// repeats the line; Tab/arrow to the answers to choose.
    ///
    /// We speak a line only once it's actually delivered on screen, driven by model state — so it fires
    /// whether the cue was advanced by our nav, a mouse click, or an auto-continue. The catch: the game
    /// sets <c>Cue.Value</c> seconds before delivery for cutscene-gated lines (it swaps the cue while the
    /// previous line is still shown, runs an intro cutscene, then delivers the line — voiceover and all —
    /// when control returns to Dialog mode). It marks those with <c>DialogVM.m_CutsceneScheduled</c> (the
    /// same flag it uses to defer the voiceover) and clears it in <c>OnGameModeStart(Dialog)</c> at
    /// delivery. So: announce when the cue is new, we're in Dialog mode, AND the cue isn't cutscene-
    /// scheduled — immediate for ordinary in-place lines, deferred to delivery for cutscene lines, with
    /// no artificial delay on the common case. Book events, interchapters, and global-map conversations
    /// are separate and not handled here yet.
    /// </summary>
    public sealed class DialogueScreen : Screen
    {
        public override string Key => "ctx.dialogue";
        public override int Layer => 15; // over the in-game context + service windows

        private static readonly FieldInfo CutsceneScheduledField = AccessTools.Field(typeof(DialogVM), "m_CutsceneScheduled");

        private CueVM _builtCue;  // cue the navigable tree was built for
        private CueVM _spokenCue; // cue we've spoken

        private static DialogVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.DialogContextVM?.DialogVM?.Value;
        }

        // True while this cue's delivery is gated behind a cutscene (its voiceover/text appear only when
        // Dialog mode resumes). If the field can't be read, treat as not-scheduled (announce on Dialog mode).
        private static bool CutsceneScheduled(DialogVM vm)
            => CutsceneScheduledField != null && CutsceneScheduledField.GetValue(vm) is bool b && b;

        private static bool DialogMode()
        {
            var g = Game.Instance;
            return g != null && g.CurrentMode == GameModeType.Dialog;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Clear(); Reset(); }
        public override void OnPop() { Clear(); Reset(); }
        private void Reset() { _builtCue = null; _spokenCue = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var cue = vm.Cue.Value;
            if (cue == null) return;

            // Keep the navigable tree in sync with the model (silent) so the answer/continue buttons are current.
            if (cue != _builtCue) { _builtCue = cue; Rebuild(vm); }

            // Speak once delivered: in Dialog mode and not waiting on a cutscene. Once per cue, interrupt
            // so the current line is always heard.
            if (cue != _spokenCue && DialogMode() && !CutsceneScheduled(vm))
            {
                _spokenCue = cue;
                Tts.Speak(CueLine(vm), interrupt: true);
            }
        }

        private void Rebuild(DialogVM vm)
        {
            Clear();
            Add(new TextElement(() => CueLine(vm))); // the line — focus here to repeat it

            var answers = new ListContainer("Answers");
            if (vm.Answers.Value != null && vm.Answers.Value.Count > 0)
            {
                foreach (var a in vm.Answers.Value)
                    if (a != null) answers.Add(DialogAnswerButton.For(a));
            }
            else if (vm.SystemAnswer.Value != null)
            {
                answers.Add(DialogAnswerButton.For(vm.SystemAnswer.Value));
            }
            if (answers.Children.Count > 0) Add(answers);

            Navigation.Attach(this); // re-bind to the rebuilt tree (silent; focus → the line)
        }

        private static string CueLine(DialogVM vm)
        {
            var cue = vm.Cue.Value;
            var text = cue != null ? cue.BaseText : null;

            // The check result ("[Failed an Athletics check]") is a runtime prefix the cue view composes
            // from the cue's SkillChecks — it's NOT part of BaseText — so prepend it the same way the game
            // does (UIUtility.SkillCheckText). Tts strips the rich-text colour at speak time.
            if (cue != null && cue.SkillChecks != null && cue.SkillChecks.Count > 0)
            {
                var check = UIUtility.SkillCheckText(cue.SkillChecks);
                if (!string.IsNullOrEmpty(check)) text = string.IsNullOrEmpty(text) ? check : check + " " + text;
            }

            var speaker = vm.SpeakerName.Value;
            if (string.IsNullOrEmpty(text)) return speaker;
            return string.IsNullOrEmpty(speaker) ? text : speaker + ": " + text;
        }
    }
}
