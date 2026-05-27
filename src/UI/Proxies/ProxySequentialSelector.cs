using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Common;
using WrathAccess.UI.Announcements;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// A sequential (cycle) selector — Left/Right step through the options (mapped to the nav's
    /// Decrease/Increase), reading the label + current value. Used for the race ability-bonus chooser
    /// (which stat gets the racial +2). The VM is fetched live since it's created on demand.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement),
        typeof(ValueAnnouncement), typeof(EnabledAnnouncement))]
    public sealed class ProxySequentialSelector : UIElement
    {
        private readonly string _label;
        private readonly Func<StringSequentialSelectorVM> _vm;

        public ProxySequentialSelector(string label, Func<StringSequentialSelectorVM> vm)
        {
            _label = label;
            _vm = vm;
        }

        private StringSequentialSelectorVM Vm => _vm?.Invoke();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_label ?? ""));
            yield return new RoleAnnouncement("slider"); // Left/Right cycles, so reads like the settings sliders
            var v = Vm;
            if (v != null) yield return new ValueAnnouncement(Message.Raw(v.Value?.Value ?? ""));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            var v = Vm;
            if (v == null) yield break;
            yield return new ElementAction(ActionIds.Decrease, Message.Raw("Previous"), _ => v.OnLeft());
            yield return new ElementAction(ActionIds.Increase, Message.Raw("Next"), _ => v.OnRight());
        }
    }
}
