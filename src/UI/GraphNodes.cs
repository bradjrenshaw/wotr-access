using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.ContextMenu; // ContextMenuEntityVM / ContextMenuCollectionEntity
using Kingmaker.UI.MVVM._VM.Settings; // SettingsVM
using Kingmaker.UI.MVVM._VM.Settings.Entities; // SettingsEntity*VM
using Kingmaker.UI.MVVM._VM.Settings.KeyBindSetupDialog; // GetPrettyString
using Kingmaker.UI.MVVM._VM.Settings.Menu; // SettingsMenuEntityVM
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

        /// <summary>The spoken tooltip/description part ("what this setting does", read after the value —
        /// the old TooltipAnnouncement). User-togglable via the "tooltip" kind settings.</summary>
        public static NodeAnnouncement TooltipPart(Func<string> description)
            => new NodeAnnouncement(description, kind: AnnouncementKinds.Tooltip);

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
                TooltipPart(() => sv?.Description),
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

        /// <summary>An expandable group header (a tree section): label only — the announcer appends the
        /// expanded/collapsed state word. Use with <see cref="GraphBuilder.BeginGroup"/>.</summary>
        public static NodeVtable Group(Func<string> label) => new NodeVtable
        {
            ControlType = ControlTypes.Group,
            Announcements = new[] { LabelPart(label) },
            SearchText = label,
        };

        /// <summary>A boolean GAME setting (<see cref="SettingsEntityBoolVM"/>): toggles via the VM's own
        /// ChangeValue (the click path), value read live from GetTempValue; Space reads its description.
        /// (ModificationAllowed is a snapshot taken at VM construction — fine where settings aren't locked.)</summary>
        public static NodeVtable GameToggle(SettingsEntityBoolVM vm, NodeAnnouncement position = null)
        {
            var vt = Toggle(
                () => vm?.Title ?? "",
                () => vm != null && vm.GetTempValue(),
                () => vm?.ChangeValue(),
                () => vm != null && vm.ModificationAllowed.Value,
                position);
            var anns = new List<NodeAnnouncement>(vt.Announcements) { TooltipPart(() => vm?.Description) };
            vt.Announcements = anns; // the type's kind order slots it after value/enabled
            vt.OnTooltip = () => OpenSimpleTooltip(vm?.Title, vm?.Description);
            return vt;
        }

        /// <summary>A dropdown ("label, combo box, current option[, disabled]"): activation opens the
        /// option submenu — deliberately NOT Left/Right adjust (in a tree those are collapse/ascend).
        /// The value part is LIVE, so returning from the submenu with a new pick announces it.</summary>
        public static NodeVtable Dropdown(Func<string> label, Func<string> value, Action openSubmenu,
            Func<bool> enabled = null, Func<string> description = null, NodeAnnouncement position = null)
        {
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(label),
                new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                DisabledPart(enabled),
            };
            if (description != null) anns.Add(TooltipPart(description));
            if (position != null) anns.Add(position);
            Action tooltip = null;
            if (description != null)
                tooltip = () => OpenSimpleTooltip(label != null ? label() : null, description());
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = anns,
                SearchText = label,
                OnActivate = () =>
                {
                    if (enabled != null && !enabled()) return;
                    openSubmenu?.Invoke();
                },
                OnTooltip = tooltip,
            };
        }

        /// <summary>A plain dropdown GAME setting (<see cref="SettingsEntityDropdownVM"/>).</summary>
        public static NodeVtable GameDropdown(SettingsEntityDropdownVM vm, NodeAnnouncement position = null)
        {
            Func<string> value = () =>
            {
                if (vm == null) return "";
                var vals = vm.LocalizedValues;
                int i = vm.GetTempValue();
                return (vals != null && i >= 0 && i < vals.Count) ? vals[i] : "";
            };
            return Dropdown(() => vm?.Title ?? "", value,
                () => Screens.ChoiceSubmenuScreen.Open(vm.Title, vm.LocalizedValues, vm.GetTempValue(), i => vm.SetTempValue(i)),
                () => vm != null && vm.ModificationAllowed.Value,
                () => vm?.Description, position);
        }

        /// <summary>The game-difficulty picker: a dropdown whose submenu options read
        /// "Title. Description" (each difficulty explains itself).</summary>
        public static NodeVtable GameDifficulty(Kingmaker.UI.MVVM._VM.Settings.Entities.Difficulty.SettingsEntityDropdownGameDifficultyVM vm,
            NodeAnnouncement position = null)
        {
            Func<string> value = () =>
            {
                if (vm == null) return "";
                int i = vm.GetTempValue();
                var items = vm.Items;
                return (items != null && i >= 0 && i < items.Count) ? items[i].Title : "";
            };
            return Dropdown(() => vm?.Title ?? "", value, () =>
                {
                    var items = vm?.Items;
                    if (items == null || items.Count == 0) return;
                    var options = new List<string>(items.Count);
                    foreach (var it in items)
                        options.Add(it.Title + (string.IsNullOrEmpty(it.Description) ? "" : ". " + it.Description));
                    Screens.ChoiceSubmenuScreen.Open(vm.Title, options, vm.GetTempValue(), i => vm.SetTempValue(i));
                },
                () => vm != null && vm.ModificationAllowed.Value,
                () => vm?.Description, position);
        }

        /// <summary>A settings tab (Game / Controls / …): activation replicates the game's own click flow
        /// (SetSelectedFromView → group updates SelectedEntity → SetSettingsList) and announces "selected".</summary>
        public static NodeVtable SettingsTab(SettingsMenuEntityVM tab, SettingsVM settings,
            NodeAnnouncement position = null)
        {
            Func<bool> selected = () => settings != null && ReferenceEquals(settings.SelectedMenuEntity.Value, tab);
            var anns = new List<NodeAnnouncement>
            {
                LabelPart(() => tab?.Title ?? ""),
                SelectedPart(selected),
            };
            if (position != null) anns.Add(position);
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = anns,
                SearchText = () => tab?.Title ?? "",
                StateText = () => selected() ? Loc.T("state.selected") : null,
                OnActivate = () =>
                {
                    UiSound.Play(UISoundType.ButtonClick);
                    tab?.SetSelectedFromView(true);
                },
            };
        }

        /// <summary>One binding slot of a game key-binding row (index 0 = primary, 1 = secondary):
        /// value = the bound combo (LIVE — the capture dialog or a clear changes it under focus and it
        /// announces itself); Enter rebinds (opens the game's capture dialog), Backspace clears.</summary>
        public static NodeVtable KeyBindingSlot(SettingEntityKeyBindingVM vm, int index, string label)
        {
            Func<bool> enabled = () => vm != null && vm.ModificationAllowed.Value;
            Func<string> value = () =>
            {
                if (vm == null) return null;
                var data = index == 0 ? vm.TempBindingValue1.Value : vm.TempBindingValue2.Value;
                string p = data.GetPrettyString(); // empty for an unbound slot (mirrors the row view)
                return string.IsNullOrEmpty(p) ? Loc.T("value.not_bound") : p;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.KeyBinding,
                Announcements = new[]
                {
                    LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    DisabledPart(enabled),
                },
                SearchText = () => vm?.Title ?? label,
                OnActivate = () =>
                {
                    if (!enabled()) return;
                    Screens.KeyBindCaptureScreen.PendingLabel = (vm?.Title ?? "") + ", " + label;
                    vm.OpenBindingDialogVM(index);
                },
                OnSecondary = () =>
                {
                    if (!enabled()) return;
                    // Open the dialog and immediately unbind via its VM — clears the slot without ever
                    // surfacing the capture screen (created and closed within this frame).
                    vm.OpenBindingDialogVM(index);
                    Screens.KeyBindCaptureScreen.Dialog()?.Unbind();
                },
            };
        }

        /// <summary>Placeholder for setting kinds without a factory yet: "label, setting, not accessible yet".</summary>
        public static NodeVtable UnsupportedSetting(Func<string> label) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[]
            {
                LabelPart(label),
                new NodeAnnouncement(() => Loc.T("role.setting"), kind: AnnouncementKinds.Role),
                new NodeAnnouncement(() => Loc.T("value.not_accessible"), kind: AnnouncementKinds.Value),
            },
            SearchText = label,
        };

        private static void OpenSimpleTooltip(string title, string description)
        {
            var tpl = WrathAccess.UI.Tooltips.SimpleTooltip.Make(title, description);
            if (tpl != null) Screens.TooltipScreen.Open(tpl);
        }
    }
}
