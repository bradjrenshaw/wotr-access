using System;
using System.Collections.Generic;
using TMPro;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A text-entry control wrapping one of the game's <see cref="TMP_InputField"/>s. The field is
    /// fetched live via <paramref name="acquire"/> (it lives on the active view, not the VM tree), and
    /// activating hands the keyboard to <see cref="TextEntry"/>, which drives the real field so Unity
    /// handles caret/backspace/Unicode/IME. The focus read shows the current value from the model.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement))]
    public sealed class ProxyTextField : UIElement
    {
        private readonly string _label;
        private readonly Func<TMP_InputField> _acquire;
        private readonly Func<string> _value;

        public ProxyTextField(string label, Func<TMP_InputField> acquire, Func<string> value)
        {
            _label = label;
            _acquire = acquire;
            _value = value;
        }

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label ?? ""));
            yield return new RoleAnnouncement("edit");
            string v = _value?.Invoke();
            yield return new ValueAnnouncement(string.IsNullOrEmpty(v)
                ? Message.Localized("ui", "value.blank") : Message.Raw(v));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.edit"), _ =>
            {
                var field = _acquire?.Invoke();
                if (field != null) TextEntry.Begin(field, _label);
                else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
            });
        }
    }
}
