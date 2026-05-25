using System;
using System.Collections.Generic;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI
{
    /// <summary>
    /// A focusable piece of text — raw or game TMP/HTML markup — read out when focused. The base
    /// for any navigable text: tooltip bricks (via <c>ProxyTooltipBrick</c>), dialogue lines,
    /// descriptions, journal entries. Rich-text tags are stripped for speech (by Message/Tts), and
    /// empty text isn't focusable. This is the intended home for glossary <c>&lt;link&gt;</c>
    /// handling, so links can work uniformly everywhere text appears — not just in tooltips.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(PositionAnnouncement))]
    public class TextElement : UIElement
    {
        private readonly Func<string> _text;
        private readonly string _role;

        /// <summary>For subclasses that produce their text by overriding <see cref="GetText"/>.</summary>
        protected TextElement() { }

        public TextElement(string text, string role = null) : this(() => text, role) { }

        public TextElement(Func<string> text, string role = null)
        {
            _text = text;
            _role = role;
        }

        /// <summary>The text to read. Subclasses (e.g. tooltip bricks) override to derive it.</summary>
        public virtual string GetText() => _text != null ? _text() : null;

        public override bool CanFocus => !string.IsNullOrWhiteSpace(GetText());

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            var t = GetText();
            if (!string.IsNullOrWhiteSpace(t)) yield return new LabelAnnouncement(Message.Raw(t));
            if (!string.IsNullOrEmpty(_role)) yield return new RoleAnnouncement(_role);
        }
    }
}
