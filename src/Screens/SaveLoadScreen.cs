using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.SaveLoad;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The save/load window (CommonVM.SaveLoadVM) — one screen with a Save or Load mode, opened single-
    /// (just Save / just Load, from the Esc menu) or dual-mode (with a Save/Load selector, from the main
    /// menu). Three Tab-stops: the mode selector (dual-mode only), the slots as a FlowSheet table (one
    /// region per playthrough; column 0 is the save name AND the row's selection radio, the rest are
    /// metadata), then the action buttons (New save in Save mode, Save/Load, Delete) which act on the
    /// selected slot. Overwrite-rename and delete go through the game's own message modals (handled by
    /// <see cref="MessageModalScreen"/>); New save takes its name from our text-entry overlay. Layer 20.
    /// </summary>
    public sealed class SaveLoadScreen : Screen
    {
        public SaveLoadScreen() { Wrap = true; } // Tab cycles mode ↔ slots ↔ buttons

        public override string Key => "overlay.saveload";
        public override string ScreenName => Loc.T("screen.saveload");
        public override int Layer => 20;
        public override bool IsActive() => Vm() != null;

        private static SaveLoadVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.SaveLoadVM.Value
                : null;
        }

        private SaveLoadVM _builtFor;
        private SaveLoadMode _modeBuilt;
        private int _slotsBuilt = -1;

        public override void OnPush() { _builtFor = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFor = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // Rebuild on VM swap, mode flip (Save↔Load tab), or the slot list changing (save/delete).
            if (vm != _builtFor || vm.Mode.Value != _modeBuilt || SlotCount(vm) != _slotsBuilt)
            {
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        private static int SlotCount(SaveLoadVM vm)
            => vm.SaveSlotCollectionVm?.AllSlots?.Count ?? 0;

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFor = vm;
            if (vm == null) return;
            _modeBuilt = vm.Mode.Value;
            _slotsBuilt = SlotCount(vm);
            bool saveMode = vm.Mode.Value == SaveLoadMode.Save;

            // 1) Mode selector — only when both modes are offered (dual-mode).
            var modes = vm.SaveLoadMenuVM?.SelectionGroup?.EntitiesCollection;
            if (modes != null && modes.Count > 1)
            {
                var modeList = new ListContainer(Loc.T("save.mode"));
                foreach (var e in modes)
                    if (e != null) { var me = e; modeList.Add(new ProxySelectionItem(me, () => ModeLabel(me.Mode), role: "tab")); }
                Add(modeList);
            }

            // 2) The slots, grouped by playthrough into one flow-sheet table.
            var sheet = BuildSlots(vm);
            if (sheet != null) Add(sheet);

            // 3) Action buttons — each its own Tab-stop (act on the selected slot).
            if (saveMode)
                Add(new ProxyActionButton(Loc.T("save.new"), () => true, NewSave));
            Add(new ProxyActionButton(
                saveMode ? Loc.T("save.action.save") : Loc.T("save.action.load"),
                () => { var s = vm.SelectedSaveSlot.Value; return s != null && s.ShowSaveLoadButton; },
                () => vm.SelectedSaveSlot.Value?.SaveOrLoad()));
            Add(new ProxyActionButton(Loc.T("save.action.delete"),
                () => { var s = vm.SelectedSaveSlot.Value; return s != null && !s.ShowReadOnlyMark.Value; },
                () => vm.SelectedSaveSlot.Value?.Delete(), actionVerb: "delete"));
        }

        private FlowSheet BuildSlots(SaveLoadVM vm)
        {
            var groups = vm.SaveSlotCollectionVm?.SaveSlotGroups;
            if (groups == null) return null;

            var cols = new[]
            {
                Loc.T("save.col.character"), Loc.T("save.col.location"), Loc.T("save.col.saved"),
                Loc.T("save.col.playtime"), Loc.T("save.col.type"), Loc.T("save.col.description"),
            };
            var sheet = new FlowSheet();
            bool any = false;
            foreach (var g in groups)
            {
                if (g == null || g.SaveLoadSlots == null || g.SaveLoadSlots.Count == 0) continue;
                g.IsExpanded.Value = true; // ensure the group's slots are available/selectable
                var region = sheet.Table(GroupLabel(g), cols).Associate(0); // col 0 = name + selection radio
                foreach (var slot in g.SaveLoadSlots)
                {
                    if (slot == null) continue;
                    var s = slot;
                    region.Row(new ProxySelectionItem(s, () => SlotName(s)), new UIElement[]
                    {
                        new TextElement(() => s.CharacterName.Value),
                        new TextElement(() => s.LocationName.Value),
                        new TextElement(() => s.SaveTime.Value),
                        new TextElement(() => s.TimeInGame.Value),
                        new TextElement(() => SlotType(s)),
                        new TextElement(() => s.Description.Value),
                    });
                    any = true;
                }
            }
            if (!any) return null;
            sheet.Reflow();
            return sheet;
        }

        private void NewSave()
        {
            var vm = Vm();
            if (vm?.NewSaveSlotVm == null) return;
            ModTextEntryScreen.Open(Loc.T("save.new"), NewSaveSlotVM.DefaultSaveName, name =>
            {
                if (!string.IsNullOrEmpty(name)) Vm()?.NewSaveSlotVm.Save(name);
            });
        }

        private static string ModeLabel(SaveLoadMode mode)
            => Loc.T(mode == SaveLoadMode.Save ? "save.mode.save" : "save.mode.load");

        private static string SlotName(SaveSlotVM s)
        {
            var n = s.SaveName.Value;
            return string.IsNullOrEmpty(n) ? Loc.T("save.group.default") : n;
        }

        private static string GroupLabel(SaveSlotGroupVM g)
        {
            if (!string.IsNullOrEmpty(g.CharacterName)) return g.CharacterName;
            if (!string.IsNullOrEmpty(g.GameName)) return g.GameName;
            return Loc.T("save.group.default");
        }

        // The slot's kind, composed from the game's marks (auto/quick saves are their own thing; a manual
        // save may also be read-only and/or DLC-gated).
        private static string SlotType(SaveSlotVM s)
        {
            if (s.ShowAutoSaveMark.Value) return Loc.T("save.mark.auto");
            if (s.ShowQuickSaveMark.Value) return Loc.T("save.mark.quick");
            var parts = new List<string> { Loc.T("save.mark.manual") };
            if (s.ShowReadOnlyMark.Value) parts.Add(Loc.T("save.mark.readonly"));
            if (s.ShowDlcRequiredLabel.Value) parts.Add(Loc.T("save.mark.dlc"));
            return string.Join(", ", parts.ToArray());
        }
    }
}
