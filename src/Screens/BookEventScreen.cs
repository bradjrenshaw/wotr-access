using System.Collections.Generic;
using Kingmaker;
using Kingmaker.DialogSystem.Blueprints; // BlueprintBookPage
using Kingmaker.UI.MVVM._VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.UI.MVVM._VM.Dialog.Interchapter; // InterchapterVM (a BookEventVM subclass)
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of
    /// narrative and numbered choices (e.g. the Areelu vision). It's a <c>DialogType.Book</c> conversation,
    /// so it rides the SAME in-game HUD VM as ordinary dialogue (<c>DialogContextVM.BookEventVM</c>, beside
    /// <c>DialogVM</c>) and reuses the dialogue <c>AnswerVM</c> → <see cref="WrathAccess.UI.Proxies.DialogAnswerButton"/>.
    ///
    /// A page carries several cues (the paragraphs, shown together) plus the answers; we read the whole
    /// passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like the dialogue cue), and the
    /// passage is the first focusable element so you can re-read it. Choosing an answer advances to the next
    /// page in place (new passage + choices) until the book closes.
    ///
    /// Interchapter/epilogue narration (e.g. "Trapped in the Darkness") is the same thing — <see
    /// cref="InterchapterVM"/> derives from BookEventVM, just stored in a separate context slot and carrying
    /// a page <c>Title</c> — so we pick that VM up too and read its title ahead of the passage. The
    /// skill-check "choose a character" sub-step is still deferred.
    /// </summary>
    public sealed class BookEventScreen : Screen
    {
        public override string Key => "ctx.bookevent";
        public override string ScreenName => Loc.T("screen.book_event");
        public override int Layer => 15; // over the in-game context + service windows, like dialogue

        private BlueprintBookPage _builtPage;  // page the tree was built for
        private BlueprintBookPage _spokenPage; // page we've read aloud

        private static BookEventVM Vm()
        {
            var ctx = Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.DialogContextVM;
            // Interchapter/epilogue is a BookEventVM subclass in its own slot; treat it the same.
            return ctx?.BookEventVM?.Value ?? ctx?.InterchapterVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Clear(); Reset(); }
        public override void OnPop() { Clear(); Reset(); }
        private void Reset() { _builtPage = null; _spokenPage = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.BlueprintBookPage.Value;
            if (page == null) return; // VM exists a frame before the first page is pushed

            if (page != _builtPage) { _builtPage = page; Rebuild(vm); }
            if (page != _spokenPage) { _spokenPage = page; Speak(vm); }
        }

        // Same transcript FlowSheet as ordinary dialogue: the passage as the log region, the choices as
        // the answers region. Focus lands at the top of the passage; Down reaches the choices.
        private void Rebuild(BookEventVM vm)
        {
            Clear();
            var sheet = DialogTranscript.Build(PassageLines(vm), null, vm.Answers.Value, out var focus);
            Add(sheet);
            Navigation.Attach(this);
            Navigation.Focus(focus, announce: false);
        }

        // Speak the whole passage once per page, QUEUED (never interrupting — the dialogue rule). Re-reading
        // individual paragraphs is done by arrowing the rows.
        private void Speak(BookEventVM vm)
        {
            var lines = PassageLines(vm);
            if (lines.Count > 0) Tts.Speak(string.Join("\n", lines.ToArray()), interrupt: false);
        }

        // The page as transcript lines: the interchapter title first (e.g. "Trapped in the Darkness"), then
        // one line per cue paragraph (raw text — kept un-stripped so glossary links survive for Space).
        private static List<string> PassageLines(BookEventVM vm)
        {
            var lines = new List<string>();
            if (vm is InterchapterVM ic && !string.IsNullOrWhiteSpace(ic.Title.Value)) lines.Add(ic.Title.Value);
            foreach (var cue in vm.Cues)
            {
                var t = cue?.BaseText;
                if (string.IsNullOrWhiteSpace(t)) continue;
                foreach (var part in t.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(part)) lines.Add(part.Trim());
            }
            return lines;
        }
    }
}
