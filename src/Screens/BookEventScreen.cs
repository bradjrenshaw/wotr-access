using System.Collections.Generic;
using Kingmaker;
using Kingmaker.DialogSystem.Blueprints; // BlueprintBookPage
using Kingmaker.UI.MVVM._VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.UI.MVVM._VM.Dialog.Interchapter; // InterchapterVM (a BookEventVM subclass)
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

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
        public override string ScreenName => "Book event";
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
            if (page != _spokenPage) { _spokenPage = page; Tts.Speak(Passage(vm), interrupt: true); }
        }

        private void Rebuild(BookEventVM vm)
        {
            Clear();
            Add(new TextElement(() => Passage(vm))); // the whole passage — focus here to re-read it

            var answers = new ListContainer("Answers");
            var list = vm.Answers.Value;
            if (list != null)
                foreach (var a in list)
                    if (a != null) answers.Add(DialogAnswerButton.For(a));
            if (answers.Children.Count > 0) Add(answers);

            Navigation.Attach(this); // re-bind to the rebuilt tree (silent; focus → the passage)
        }

        // All the page's cues (paragraphs) joined — the plain text, no speaker formatting. Interchapter pages
        // also carry a title ("Trapped in the Darkness"), read first.
        private static string Passage(BookEventVM vm)
        {
            var parts = new List<string>();
            if (vm is InterchapterVM ic && !string.IsNullOrWhiteSpace(ic.Title.Value)) parts.Add(ic.Title.Value);
            foreach (var cue in vm.Cues)
            {
                var t = cue?.BaseText;
                if (!string.IsNullOrWhiteSpace(t)) parts.Add(t);
            }
            return string.Join("\n", parts);
        }
    }
}
