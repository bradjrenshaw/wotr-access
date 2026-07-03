using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.ContextMenu; // ContextMenuEntityVM / ContextMenuCollectionEntity
using Kingmaker.UI.MVVM._VM.Settings.Entities; // SettingsEntitySliderVM
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Node factories for graph-native screens: assemble <see cref="NodeVtable"/>s with the same spoken
    /// conventions the element proxies used — role words through the <c>role.*</c> locale keys,
    /// <c>state.disabled</c>, <c>nav.position</c> "n of m" — and the game's UI sounds inside the action
    /// closures (the sounds normally lived in view click handlers we bypass). As screens migrate, the
    /// VM-contract knowledge in each proxy moves into a factory here and the proxy is deleted.
    /// </summary>
    public static class GraphNodes
    {
        /// <summary>An "index of count" announcement part (lists spoke their children's position).</summary>
        public static NodeAnnouncement Position(int index, int count)
            => new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position);

        /// <summary>The label part (always first in the standard order).</summary>
        public static NodeAnnouncement LabelPart(Func<string> label)
            => new NodeAnnouncement(label, kind: AnnouncementKinds.Label);

        /// <summary>The disabled-state part: silent while enabled, "disabled" otherwise — LIVE, so a
        /// control graying out under focus announces it.</summary>
        public static NodeAnnouncement DisabledPart(Func<bool> enabled)
            => new NodeAnnouncement(() => enabled == null || enabled() ? null : Loc.T("state.disabled"),
                live: true, kind: AnnouncementKinds.Enabled);

        /// <summary>The selected-state part: "selected" when selected, silent otherwise — LIVE, so a
        /// selection moving under focus (another option chosen elsewhere) announces it.</summary>
        public static NodeAnnouncement SelectedPart(Func<bool> selected)
            => new NodeAnnouncement(() => selected != null && selected() ? Loc.T("state.selected") : null,
                live: true, kind: AnnouncementKinds.Selected);

        /// <summary>A plain read-only text line (the modal body, a help paragraph).</summary>
        public static NodeVtable Text(Func<string> text) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[] { LabelPart(text) },
        };

        /// <summary>A push button: "label, button[, disabled][, n of m]" — the role word, ordering, and
        /// per-type announcement settings ride <see cref="ControlTypes.Button"/>; activation plays the
        /// game's button click. A disabled button consumes activation silently.</summary>
        public static NodeVtable Button(Func<string> label, Action activate, Func<bool> enabled = null,
            NodeAnnouncement position = null, UISoundType? sound = UISoundType.ButtonClick)
        {
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(label),
                DisabledPart(enabled),
            };
            if (position != null) anns.Add(position);
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = anns,
                SearchText = label,
                OnActivate = () =>
                {
                    if (enabled != null && !enabled()) return;
                    if (sound.HasValue) UiSound.Play(sound.Value);
                    activate?.Invoke();
                },
            };
        }

        /// <summary>A checkbox over an arbitrary boolean ("label, toggle, on/off[, disabled]").
        /// Activation flips it with the game's switch sound and re-announces the new value synchronously
        /// (StateText) — for toggles whose effect settles ASYNCHRONOUSLY in the game, pass
        /// <paramref name="announceOnActivate"/> false and let a state watcher speak the settled truth.
        /// The value part is LIVE, so a game-driven flip under focus announces itself.</summary>
        public static NodeVtable Toggle(Func<string> label, Func<bool> isChecked, Action onToggle,
            Func<bool> enabled = null, NodeAnnouncement position = null, bool announceOnActivate = true)
        {
            Func<string> value = () => Loc.T(isChecked != null && isChecked() ? "value.on" : "value.off");
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(label),
                // Always LIVE: a game-driven flip under focus announces itself. Safe alongside the
                // synchronous StateText — VtableActivate rebaselines the live watch after speaking.
                new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                DisabledPart(enabled),
            };
            if (position != null) anns.Add(position);
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = anns,
                SearchText = label,
                StateText = announceOnActivate ? value : null,
                OnActivate = () =>
                {
                    if (enabled != null && !enabled()) return;
                    UiSound.Play(UISoundType.SettingsSwitchToggle);
                    onToggle?.Invoke();
                },
            };
        }

        /// <summary>One option of a single-select group ("label, radio button[, selected][, n of m]") —
        /// a dropdown option, a tab. Activation selects it (the game's click sound).</summary>
        public static NodeVtable ChoiceOption(Func<string> label, Func<bool> selected, Action select,
            NodeAnnouncement position = null)
        {
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(label),
                SelectedPart(selected),
            };
            if (position != null) anns.Add(position);
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = anns,
                SearchText = label,
                OnActivate = () =>
                {
                    UiSound.Play(UISoundType.ButtonClick);
                    select?.Invoke();
                },
            };
        }

        // The live model behind a ContextMenuEntityVM's cached IsEnabled reactive: entity.IsEnabled
        // re-invokes the entry's Condition each call, where the VM reactive is stale until
        // RefreshEnabling (the MainMenuButton lesson — read live, not cached).
        private static readonly FieldInfo MenuEntityField = AccessTools.Field(typeof(ContextMenuEntityVM), "m_Entity");

        /// <summary>A game context-menu entry (main-menu sidebar, Escape menu): label + live enabled +
        /// Execute. Callers skip separators (<c>vm.IsSeparator</c>) when enumerating.</summary>
        public static NodeVtable MenuEntry(ContextMenuEntityVM vm, NodeAnnouncement position = null)
        {
            var entity = MenuEntityField?.GetValue(vm) as ContextMenuCollectionEntity;
            Func<bool> enabled = () => entity != null ? entity.IsEnabled : (vm != null && vm.IsEnabled.Value);
            return Button(() => vm?.Title ?? "", () => vm?.Execute(), enabled, position);
        }

        /// <summary>A numeric game-settings slider (<see cref="SettingsEntitySliderVM"/>): Left/Right step
        /// by the game's own SetNextValue with the game's slider-move sound (only when the value actually
        /// changes, so stepping at min/max stays silent — the ProxySlider convention); the value is spoken
        /// as immediate state feedback after each step.</summary>
        public static NodeVtable Slider(SettingsEntitySliderVM sv, NodeAnnouncement position = null)
        {
            Func<bool> enabled = () => sv != null && sv.ModificationAllowed.Value;
            Func<string> value = () =>
            {
                if (sv == null) return "";
                float v = sv.GetTempValue();
                return sv.IsInt ? ((int)Math.Round(v)).ToString() : v.ToString("F" + sv.DecimalPlaces);
            };
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(() => sv?.Title ?? ""),
                new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                DisabledPart(enabled),
            };
            if (position != null) anns.Add(position);
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = anns,
                SearchText = () => sv?.Title ?? "",
                StateText = value, // spoken (interrupting) after each adjust — key-repeat friendly
                OnAdjust = (sign, large) =>
                {
                    if (!enabled()) return;
                    float before = sv.GetTempValue();
                    sv.SetNextValue(sign);
                    if (Math.Abs(sv.GetTempValue() - before) > float.Epsilon)
                        UiSound.Play(UISoundType.SettingsSliderMove);
                },
                OnTooltip = () =>
                {
                    var tpl = WrathAccess.UI.Tooltips.SimpleTooltip.Make(sv?.Title, sv?.Description);
                    if (tpl != null) Screens.TooltipScreen.Open(tpl);
                },
            };
        }
    }
}
