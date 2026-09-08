using System.Collections.Generic;
using Kingmaker;
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The BOOKMARKS screen (Alt+B, in-area): a label field, "Bookmark this position" (the movement
    /// cursor, or the leader when none is set), "Bookmark &lt;reviewed object&gt;" (the scanner's
    /// current review target — an attached bookmark that follows the entity), then this area's
    /// existing bookmarks as a second Tab-stop: Enter reviews one (the review cursor jumps to it),
    /// Delete removes it. Escape closes. Same shell as the mod menu: takes focus mode while up and
    /// hands it back on close. The bookmarks themselves live in <see cref="BookmarkModel"/> and
    /// surface in the scanner's "Bookmarks" category.
    /// </summary>
    public sealed class BookmarkScreen : Screen
    {
        private static bool s_open;
        private static string s_label = "";

        public static void Toggle()
        {
            if (Game.Instance?.CurrentlyLoadedArea == null) return; // nothing to bookmark outside an area
            s_open = !s_open;
            if (s_open) s_label = "";
        }
        public static void CloseScreen() { s_open = false; }

        public override string Key => "overlay.bookmarks";
        public override string ScreenName => Loc.T("screen.bookmarks");
        public override int Layer => 34; // beside the mod menu (35); above the in-game context
        public override bool IsActive() => s_open && Game.Instance?.CurrentlyLoadedArea != null;

        private bool _priorFocus;
        public override void OnPush() { _priorFocus = FocusMode.Active; FocusMode.Set(true); }
        public override void OnPop() { FocusMode.Set(_priorFocus); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseScreen());
        }

        public override void Build(GraphBuilder b)
        {
            b.BeginStop("new").PushContext(Loc.T("bookmark.new"), "list");

            // The label: a button that opens the mod's text editor; reads back the current value.
            b.AddItem(ControlId.Structural("bm:label"), GraphNodes.Button(
                () => string.IsNullOrEmpty(s_label) ? Loc.T("bookmark.label_blank") : Loc.T("bookmark.label", new { label = s_label }),
                () => ModTextEntryScreen.Open(Loc.T("bookmark.label_prompt"), s_label, v => s_label = v ?? "")));

            b.AddItem(ControlId.Structural("bm:point"), GraphNodes.Button(
                () => Loc.T("bookmark.add_point"),
                () =>
                {
                    var bm = BookmarkModel.AddPoint(s_label, Scanner.ReferencePoint);
                    Tts.Speak(Loc.T("bookmark.added", new { label = bm.Label }), interrupt: true);
                    CloseScreen();
                }));

            var reviewed = Scanner.Reviewed;
            b.AddItem(ControlId.Structural("bm:attached"), GraphNodes.Button(
                () => reviewed != null
                    ? Loc.T("bookmark.add_attached", new { name = reviewed.Name })
                    : Loc.T("bookmark.add_attached_none"),
                () =>
                {
                    var target = Scanner.Reviewed;
                    if (target == null) { Tts.Speak(Loc.T("bookmark.no_target"), interrupt: true); return; }
                    var bm = BookmarkModel.AddAttached(s_label, target);
                    Tts.Speak(Loc.T("bookmark.added", new { label = bm.Label }), interrupt: true);
                    CloseScreen();
                },
                () => reviewed != null));
            b.PopContext();

            // This area's bookmarks: Enter = review it (jump the review cursor there), Delete = remove.
            b.BeginStop("list").PushContext(Loc.T("bookmark.list"), "list");
            var items = BookmarkModel.Current;
            if (items.Count == 0)
                b.AddItem(ControlId.Structural("bm:none"), GraphNodes.Text(() => Loc.T("bookmark.none")));
            var refPos = Scanner.ReferencePoint;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                b.AddItem(ControlId.Referenced(item, "bm:item:" + item.Bookmark.Id), new NodeVtable
                {
                    ControlType = ControlTypes.Item,
                    Announcements = new[] { GraphNodes.LabelPart(() => item.Describe(refPos)) },
                    SearchText = () => item.Name,
                    OnActivate = () => { CloseScreen(); Scanner.ReviewItem(item); },
                    OnDelete = () =>
                    {
                        var label = item.Bookmark.Label;
                        BookmarkModel.Remove(item.Bookmark);
                        Tts.Speak(Loc.T("bookmark.removed", new { label }), interrupt: true);
                    },
                });
            }
            b.PopContext();
        }
    }
}
