using System.Collections.Generic;
using Kingmaker;                            // Game
using Kingmaker.UI.MVVM._VM.Dialog;         // DialogContextVM
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;  // AnswerVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;              // DialogAnswerButton

namespace WrathAccess.Screens
{
    /// <summary>
    /// Builds the shared conversation transcript as one FlowSheet — used by both the ordinary dialogue
    /// screen and book-event/interchapter pages so they read and behave identically. Two unlabeled
    /// regions: a log (the scrollback / passage, one row per line, plus an optional trailing live row —
    /// the dialogue cue), then the answers (a <see cref="DialogAnswerButton"/> per <see cref="AnswerVM"/>;
    /// dropped when empty). Returns the sheet and the row to focus (the trailing row if given, else the
    /// first line). Callers do the Add / Attach / Focus so each can land focus and time speech its way.
    /// </summary>
    internal static class DialogTranscript
    {
        /// <summary>The active dialog context — the SAME <see cref="DialogContextVM"/> (holding DialogVM /
        /// BookEventVM / InterchapterVM) whether the conversation runs in an area (on <c>InGameVM</c>) or on
        /// the world map (on <c>GlobalMapVM</c>, which carries its own context). The dialogue + book-event
        /// screens read from whichever is live, so they work in both places with no other change.</summary>
        public static DialogContextVM Context()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            return rc.InGameVM?.StaticPartVM?.DialogContextVM ?? rc.GlobalMapVM?.DialogContextVM;
        }

        public static FlowSheet Build(IList<string> lines, UIElement trailingRow,
            IEnumerable<AnswerVM> answers, out UIElement focusRow)
        {
            var sheet = new FlowSheet();
            var log = sheet.List(null); // unlabeled — region entry stays quiet
            focusRow = null;
            if (lines != null)
                foreach (var line in lines)
                {
                    var text = line;
                    var row = new TextElement(() => text);
                    log.Item(row);
                    if (focusRow == null) focusRow = row;
                }
            if (trailingRow != null) { log.Item(trailingRow); focusRow = trailingRow; }

            var ans = sheet.List(null); // unlabeled: "Answers" before the choices is auditory noise
            int count = 0;
            if (answers != null)
                foreach (var a in answers)
                    if (a != null) { ans.Item(DialogAnswerButton.For(a)); count++; }
            if (count == 0) sheet.RemoveRegion(ans);

            sheet.Reflow();
            if (focusRow == null) focusRow = sheet.FirstFocusable();
            return sheet;
        }
    }
}
