using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// A navigable element. Leaves (proxies) yield typed Announcements that compose
    /// into the spoken focus message; they expose activation handlers but do NOT
    /// handle input keys or navigation — the Navigator does.
    /// </summary>
    public abstract class UIElement
    {
        public Container Parent { get; internal set; }

        public virtual bool CanFocus => true;

        /// <summary>Convenience name used by the default GetFocusAnnouncements; containers/proxies set it.</summary>
        public virtual string Label => null;

        /// <summary>Convenience role used by the default GetFocusAnnouncements (e.g. "button").</summary>
        public virtual string Role => null;

        /// <summary>The type whose [AnnouncementOrder] governs composition (composite proxies can delegate).</summary>
        public virtual Type AnnouncementOrderType => GetType();

        /// <summary>
        /// The actions this element advertises (activate, increase, setValue, …).
        /// Navigators discover and invoke these by id rather than knowing element types.
        /// </summary>
        public virtual IEnumerable<ElementAction> GetActions() { yield break; }

        /// <summary>
        /// True if activating changes this element's value in place (checkbox, tab-select,
        /// slider) and should be re-announced. False for navigation buttons that open a new
        /// screen (the screen change announces itself; re-reading the button is noise).
        /// </summary>
        public virtual bool ReannounceOnActivate => false;

        /// <summary>Like <see cref="ReannounceOnActivate"/>, but for the secondary (context) action —
        /// e.g. clearing a key binding should re-announce the now-empty value.</summary>
        public virtual bool ReannounceOnContext => false;

        /// <summary>Find an advertised action by id and execute it. Returns true if found.</summary>
        public bool InvokeAction(string id, object args = null)
        {
            foreach (var a in GetActions())
            {
                if (a.Id == id) { a.Execute(args); return true; }
            }
            return false;
        }

        /// <summary>The announcement parts this element contributes. Default: label + role.</summary>
        public virtual IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            if (!string.IsNullOrEmpty(Role)) yield return new RoleAnnouncement(Role);
        }

        /// <summary>
        /// Just this element's changed state ("checked", "selected", a slider amount…), for
        /// re-announcing after an in-place activation — we already know which control we're on,
        /// so we don't repeat the whole focus message.
        /// </summary>
        public Message GetStateMessage()
        {
            var ctx = new AnnouncementContext(this);
            var parts = new List<Message>();
            foreach (var a in GetFocusAnnouncements())
                if (a is ValueAnnouncement || a is SelectedAnnouncement)
                    parts.Add(a.Render(ctx));
            return Message.Join(", ", parts.ToArray());
        }

        /// <summary>The composed spoken focus message (parts + parent-supplied position).</summary>
        public Message GetFocusMessage()
        {
            var anns = new List<Announcement>(GetFocusAnnouncements());
            if (Parent != null && Parent.AnnouncePosition)
            {
                var pos = Parent.GetPositionString(this);
                if (pos != null) anns.Add(new PositionAnnouncement(pos));
            }
            return AnnouncementComposer.Compose(this, anns);
        }
    }
}
