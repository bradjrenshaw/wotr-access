using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UINotificationTexts (game-localized notification formats)
using Kingmaker.GameModes;
using Kingmaker.Settings;                // SettingsRoot (the game's per-category notification toggles)
using Kingmaker.UI.Common;               // UIUtility.SkillCheckText / alignment texts
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;
using UniRx;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// An in-game conversation (the common <see cref="DialogVM"/>) as ONE FlowSheet that reads like a
    /// transcript: the scrollback (the game's own pre-formatted <c>DialogVM.History</c> lines — past
    /// cues and chosen answers — plus NOTIFICATION rows we inject: alignment shifts, items gained or
    /// lost, XP, revealed locations, mirroring DialogNotificationsView's formats and settings gates),
    /// then the CURRENT cue row, then the answers region. A new cue rebuilds the sheet and lands focus
    /// on the cue row silently — you hear the line via the delivery announcement, press Down for the
    /// answers/continue, or Up to re-read earlier lines. No Tab hop (user spec: dialogue should flow).
    ///
    /// We speak a line only once it's actually delivered on screen, driven by model state — so it fires
    /// whether the cue was advanced by our nav, a mouse click, or an auto-continue. The catch: the game
    /// sets <c>Cue.Value</c> seconds before delivery for cutscene-gated lines (it swaps the cue while the
    /// previous line is still shown, runs an intro cutscene, then delivers the line — voiceover and all —
    /// when control returns to Dialog mode). It marks those with <c>DialogVM.m_CutsceneScheduled</c> (the
    /// same flag it uses to defer the voiceover) and clears it in <c>OnGameModeStart(Dialog)</c> at
    /// delivery. So: announce when the cue is new, we're in Dialog mode, AND the cue isn't cutscene-
    /// scheduled. Notification lines arrive with the cue-show event (the VM clears its lists right after
    /// the command, so the subscriber snapshots synchronously) and are QUEUED ahead of the cue line as
    /// separate utterances — nothing in dialogue ever interrupts speech (user spec). Book events,
    /// interchapters, global-map conversations, and kingdom/crusade notification categories are not
    /// handled here yet.
    /// </summary>
    public sealed class DialogueScreen : Screen
    {
        public override string Key => "ctx.dialogue";
        public override int Layer => 15; // over the in-game context + service windows

        private static readonly FieldInfo CutsceneScheduledField = AccessTools.Field(typeof(DialogVM), "m_CutsceneScheduled");

        private DialogVM _subscribedVm;   // the conversation our notification subscription belongs to
        private IDisposable _notifSub;
        private CueVM _builtCue;          // cue the sheet was built for
        private CueVM _spokenCue;         // cue we've spoken

        private readonly List<string> _rows = new List<string>();         // the transcript: history + notifications
        private readonly List<string> _pendingNotifRows = new List<string>(); // notif lines awaiting ordered insertion
        private readonly List<string> _pendingSpeak = new List<string>();     // notif lines to speak before the next cue
        private int _historyConsumed;
        private TextElement _cueRow;

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

        // Active only while the conversation exists AND its window is actually shown. When a cutscene
        // transition hides the window we POP off the stack so the in-game context beneath regains the
        // keyboard (Escape, sonar, etc. keep working) and there's no hidden dialogue to browse ahead in;
        // we re-push when the window returns.
        public override bool IsActive() => Vm() != null && DialogVisibility.Shown;

        // A hide is a pop-while-the-conversation-continues: keep the transcript + notification subscription
        // (so nothing is lost across the cutscene), and just force a rebuild on the way back in. Only fully
        // reset when the conversation has actually ended (the VM is gone).
        public override void OnPush() { Clear(); _builtCue = null; }
        public override void OnPop() { Clear(); if (Vm() == null) Reset(); }

        // Escape opens the game's pause menu, exactly like the game's own Esc key during a conversation —
        // required for save/load/quit/settings mid-dialogue. Without this the dialogue screen swallows
        // Escape (its UI category claims ui.back) and nothing happens; the InGame Escape only kicks in
        // during the cutscene gaps when this screen pops. EscMenuScreen takes over while it's open.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                    delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); }));
        }

        private void Reset()
        {
            _builtCue = null;
            _spokenCue = null;
            _rows.Clear();
            _pendingNotifRows.Clear();
            _pendingSpeak.Clear();
            _historyConsumed = 0;
            _cueRow = null;
            _notifSub?.Dispose();
            _notifSub = null;
            _subscribedVm = null;
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;

            // A new conversation = a fresh VM: reset the transcript and re-subscribe to its notifications.
            if (vm != _subscribedVm)
            {
                Reset();
                _subscribedVm = vm;
                // The VM CLEARS its notification lists right after firing the command, so this snapshot
                // must happen synchronously inside the subscription.
                _notifSub = vm.DialogNotifications.OnUpdateCommand.Subscribe(show =>
                {
                    if (!show) return;
                    var lines = ComposeNotifications(vm.DialogNotifications);
                    _pendingNotifRows.AddRange(lines);
                    _pendingSpeak.AddRange(lines);
                });
            }

            // Transcript order: history first (the previous cue + chosen answer are appended by the game
            // at answer-selection, BEFORE the cue-show event raised the notifications), then the
            // notification lines those events produced.
            var history = vm.History;
            for (; _historyConsumed < history.Count; _historyConsumed++)
                _rows.Add(TextUtil.StripRichText(history[_historyConsumed]));
            if (_pendingNotifRows.Count > 0)
            {
                _rows.AddRange(_pendingNotifRows);
                _pendingNotifRows.Clear();
            }

            var cue = vm.Cue.Value;
            if (cue == null) return;

            if (cue != _builtCue) { _builtCue = cue; Rebuild(vm); }

            // Speak once delivered: in Dialog mode and not waiting on a cutscene. Once per cue, QUEUED —
            // never interrupting (user spec) — the notification lines first (the results of the previous
            // answer), then the new line, each its own utterance.
            if (cue != _spokenCue && DialogMode() && !CutsceneScheduled(vm))
            {
                _spokenCue = cue;
                foreach (var line in _pendingSpeak) Tts.Speak(line, interrupt: false);
                _pendingSpeak.Clear();
                Tts.Speak(CueLine(vm), interrupt: false);
            }
        }

        private void Rebuild(DialogVM vm)
        {
            Clear();
            bool hasRealAnswers = vm.Answers.Value != null && vm.Answers.Value.Count > 0;
            // The live line — focus here to repeat it; Enter on it presses Continue when that's the
            // only way forward (never when real choices exist).
            _cueRow = new CueRow(() => CueLine(vm), () => hasRealAnswers ? null : vm.SystemAnswer.Value,
                () => vm.Cue.Value?.SkillChecks);

            // Real answers, else the system Continue when that's the only way forward, else none.
            List<AnswerVM> answers = null;
            if (hasRealAnswers) answers = new List<AnswerVM>(vm.Answers.Value);
            else if (vm.SystemAnswer.Value != null) answers = new List<AnswerVM> { vm.SystemAnswer.Value };

            var sheet = DialogTranscript.Build(_rows, _cueRow, answers, out var focus);
            Add(sheet);
            Navigation.Attach(this);
            // Land on the current line silently (the delivery announcement speaks it): Down reaches the
            // answers, Up scrolls back through the conversation so far.
            Navigation.Focus(focus, announce: false);
        }

        // The current-line row: focusing repeats the line; when the only way forward is the system
        // Continue, Enter HERE advances the dialogue too (user spec — Enter straight through exposition
        // without scrolling down). Real answer sets never ride this shortcut. Activation is the same
        // VM call as the Continue button (the game plays its own NextDialogLine sound; ours stays off).
        private sealed class CueRow : TextElement
        {
            private readonly Func<AnswerVM> _continueAnswer;
            private readonly Func<List<Kingmaker.Controllers.Dialog.SkillCheckResult>> _skillChecks;

            public CueRow(Func<string> text, Func<AnswerVM> continueAnswer,
                Func<List<Kingmaker.Controllers.Dialog.SkillCheckResult>> skillChecks) : base(text)
            {
                _continueAnswer = continueAnswer;
                _skillChecks = skillChecks;
            }

            public override Kingmaker.UI.UISoundType? ActivateSound => null;

            // The cue line carries a skill-check RESULT <link> when a check was just rolled; resolve it
            // from the cue's result list (Space → the roll breakdown). Glossary links fall through.
            public override Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate ResolveLink(string id, string[] keys)
                => WrathAccess.UI.Proxies.DialogLinks.ResolveSkillCheck(keys, _skillChecks?.Invoke(), null);

            public override IEnumerable<ElementAction> GetActions()
            {
                // Only while the window is actually shown — during a cutscene transition the game hides it
                // and the button isn't clickable, so we must not let Enter spam-advance the dialogue.
                var a = _continueAnswer();
                if (a != null && a.Enable.Value && DialogVisibility.Shown)
                    yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.choose"),
                        _ => a.OnChooseAnswer());
            }
        }

        // Mirrors DialogNotificationsView: the same per-category game settings gates and the same
        // game-localized UINotificationTexts formats; colors stripped for speech. Kingdom/crusade
        // categories (stats, events, free buildings, morale) are deferred with the kingdom screens.
        private static List<string> ComposeNotifications(DialogNotificationsVM n)
        {
            var lines = new List<string>();
            var t = UINotificationTexts.Instance;

            if (SettingsRoot.Game.Dialogs.ShowAlignmentShiftsNotifications)
            {
                foreach (var shift in n.AlignmentShifts)
                    lines.Add(TextUtil.StripRichText(string.Format(t.AlignmentShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentShiftDirectionText(shift.Direction), shift.Value)));
                var cueData = n.CueData;
                if (cueData != null && cueData.NewAlignment.HasValue)
                    lines.Add(TextUtil.StripRichText(string.Format(t.NewAlignmentAfterShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentName(cueData.NewAlignment.Value))));
            }

            if (SettingsRoot.Game.Dialogs.ShowLocationRevealedNotification && n.RevealedLocationNames.Count > 0)
                lines.Add(TextUtil.StripRichText(string.Format(
                    n.RevealedLocationNames.Count < 2 ? t.RevealedLocationFormat : t.RevealedLocationsFormat,
                    string.Join(", ", n.RevealedLocationNames.ToArray()))));

            if (SettingsRoot.Game.Dialogs.ShowItemsReceivedNotification)
            {
                var got = new List<string>();
                var lost = new List<string>();
                foreach (var kv in n.ItemsChanged)
                {
                    if (kv.Key == null || kv.Value == 0) continue;
                    int abs = Math.Abs(kv.Value);
                    var label = abs > 1 ? kv.Key.Name + " ×" + abs : kv.Key.Name;
                    (kv.Value > 0 ? got : lost).Add(label);
                }
                if (got.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsRecievedFormat, string.Join(", ", got.ToArray()))));
                if (lost.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsLostFormat, string.Join(", ", lost.ToArray()))));
            }

            if (SettingsRoot.Game.Dialogs.ShowXPGainedNotification && n.XpGains.Count > 0)
            {
                int sum = 0;
                foreach (var x in n.XpGains) sum += x;
                lines.Add(TextUtil.StripRichText(string.Format(t.XPGainedFormat, sum)));
            }

            foreach (var c in n.CustomNotifications)
                if (!string.IsNullOrEmpty(c)) lines.Add(TextUtil.StripRichText(c));

            return lines;
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
