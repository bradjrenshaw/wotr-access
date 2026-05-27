using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// A focusable piece of text — raw or game TMP/HTML markup — read out when focused. The base
    /// for any navigable text: tooltip bricks, dialogue lines, descriptions, journal entries.
    /// Optionally carries a nested tooltip (so the tooltip key drills into it — e.g. a feature's
    /// full writeup) and is the intended home for glossary &lt;link&gt; handling, so links work
    /// uniformly everywhere text appears. Rich-text tags are stripped for speech; empty text isn't
    /// focusable.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public class TextElement : UIElement
    {
        private readonly Func<string> _text;
        private readonly string _role;
        private readonly Func<TooltipBaseTemplate> _tooltip;

        /// <summary>For subclasses that produce their text by overriding <see cref="GetText"/>.</summary>
        protected TextElement() { }

        // The tooltip is a FACTORY, resolved live on each drill-in — never a cached instance — so a
        // cell whose value changes (a skill rank, an ability score) always drills into current data.
        public TextElement(string text, string role = null, Func<TooltipBaseTemplate> tooltip = null)
            : this(() => text, role, tooltip) { }

        public TextElement(Func<string> text, string role = null, Func<TooltipBaseTemplate> tooltip = null)
        {
            _text = text;
            _role = role;
            _tooltip = tooltip;
        }

        public virtual string GetText() => _text != null ? _text() : null;

        public override bool CanFocus => !string.IsNullOrWhiteSpace(GetText());

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            var t = GetText();
            if (!string.IsNullOrWhiteSpace(t)) yield return new LabelAnnouncement(Message.Raw(t));
            if (!string.IsNullOrEmpty(_role)) yield return new RoleAnnouncement(_role);
        }

        // A nested tooltip (e.g. a feature's full description) — built fresh on each drill-in.
        public override TooltipBaseTemplate GetTooltipTemplate() => _tooltip != null ? _tooltip() : null;
    }
}
