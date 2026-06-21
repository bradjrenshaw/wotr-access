using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized rest labels)
using Kingmaker.Controllers.Rest;        // RestController, CheckStatus, RestIterationStatus
using Kingmaker.Controllers.Rest.State;  // CampingRoleType
using Kingmaker.PubSubSystem;            // EventBus, IRestRequestEvents, IRestRoleUIStageEvents
using Kingmaker.UI.MVVM._PCView.Rest;    // CraftStage (the stage-event payload)
using Kingmaker.UI.Common;               // UIUtility.AddSign
using Kingmaker.UI.MVVM._VM.Rest;        // RestVM family, UIRestPhase
using WrathAccess.UI;
using WrathAccess.UI.Announcements; // the slot dropdown's focus announcements
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The camping window (<c>RestContextVM.RestVM</c>, the same reactive pattern as loot/dialogue),
    /// covering all three phases. Management is ONE accordion treeview: each role — guards' two
    /// watches, camouflage, divine service, alchemist, scroll scribe — is a node that reads its live
    /// assignment collapsed ("Guards, first watch: Seelah"); expanding it builds Primary/Assistant
    /// member DROPDOWNS (live options via the shared chooser; selecting drives the game's own radio
    /// contract → AddUnitToRole) and raises the game's stage-open event, so the REAL role panel
    /// appears on screen in step with the tree — and because expansion is exclusive, moving to
    /// another role swaps panels exactly like the sighted card click does. Camp-wide settings
    /// (number-of-rests dropdown over the game's iteration radios; autotune + healing toggles
    /// written to CampingState exactly like the PC view's toggle handlers) and the action buttons
    /// are leaf nodes at the root. Start rest is ONE VM call — RestVM.StartRest fires
    /// StartRestCommand and the game's bound view runs its own per-phase flow (fade + StartCamp /
    /// SkipPhase / FinishRest). Results reads RestController.Status, mirroring
    /// RestPCView.ShowResults' iteration picks (current iteration for camp checks, last-rolled for
    /// craft checks). Escape mirrors CloseRest (refused while InProcess). Craft recipes
    /// (potions/scrolls) are a follow-up.
    /// </summary>
    public sealed class RestScreen : Screen
    {
        public override string Key => "ctx.rest";
        public override string ScreenName => Loc.T("screen.rest");
        public override int Layer => 15; // over the in-game context, alongside dialogue/loot

        private RestVM _builtVm;
        private UIRestPhase _builtPhase = UIRestPhase.None;

        private static RestVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            // In-area rest goes through RestContextVM; the WORLD-MAP fatigue rest (travel → fatigue popup →
            // accept → RestController.Start) lives on GlobalMapVM.RestVM instead. Same RestVM type, so the
            // same accordion screen drives it — we just have to look in both places.
            return rc.InGameVM?.StaticPartVM?.RestContextVM?.RestVM?.Value
                ?? rc.GlobalMapVM?.RestVM?.Value;
        }

        public override bool IsActive()
        {
            var vm = Vm();
            return vm != null && vm.CurrentPhase.Value != UIRestPhase.None;
        }

        public override void OnPush() { _builtVm = null; _builtPhase = UIRestPhase.None; }
        public override void OnPop() { Clear(); _builtVm = null; _builtPhase = UIRestPhase.None; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var phase = vm.CurrentPhase.Value;
            if (vm == _builtVm && phase == _builtPhase) return;
            bool phaseMoved = _builtVm == vm; // a transition within the open window (start → results)
            _builtVm = vm;
            _builtPhase = phase;
            Rebuild(vm, phase);
            if (phaseMoved) Tts.Speak(PhaseName(phase), interrupt: false);
        }

        private static string PhaseName(UIRestPhase phase)
        {
            switch (phase)
            {
                case UIRestPhase.InProcess: return Loc.T("rest.in_process");
                case UIRestPhase.Results: return Loc.T("rest.results");
                default: return Loc.T("screen.rest");
            }
        }

        private void Rebuild(RestVM vm, UIRestPhase phase)
        {
            Clear();
            switch (phase)
            {
                case UIRestPhase.Management: BuildManagement(vm); break;
                case UIRestPhase.InProcess: BuildInProcess(vm); break;
                case UIRestPhase.Results: BuildResults(vm); break;
            }
            Navigation.Attach(this);
        }

        // ---- management (camp setup) ----

        private void BuildManagement(RestVM vm)
        {
            var tree = new TreeGroup { ExclusiveExpansion = true }; // one role open at a time, like the game

            tree.Add(new TextElement(() => Loc.T("rest.time", new { time = TimeText(vm.RestTime.Value) }),
                tooltip: () => vm.RestingTimeTooltip));

            // Role nodes. The shift VMs are fetched INSIDE the closures: the game's panel views
            // dispose their VM whenever a panel hides, and the RestVM lazy properties recreate it.
            tree.Add(new RoleNode(Loc.T("rest.role.guard_first"), CampingRoleType.GuardFirstWatch,
                () => new[] { vm.GuardVM.FirstPrimaryShiftVM, vm.GuardVM.FirstSecondaryShiftVM }, vm.GuardRolesVM));
            tree.Add(new RoleNode(Loc.T("rest.role.guard_second"), CampingRoleType.GuardSecondWatch,
                () => new[] { vm.GuardVM.SecondPrimaryShiftVM, vm.GuardVM.SecondSecondaryShiftVM }, vm.GuardRolesVM));
            tree.Add(new RoleNode(Loc.T("rest.role.camouflage"), CampingRoleType.Camouflage,
                () => new[] { vm.CamouflageVM.PrimaryShiftVM, vm.CamouflageVM.SecondaryShiftVM }, vm.CamouflageRolesVM));
            tree.Add(new RoleNode(Loc.T("rest.role.divine"), CampingRoleType.DivineService,
                () => new[] { vm.DivineServiceVM.PrimaryShiftVM, vm.DivineServiceVM.SecondaryShiftVM }, vm.DivineRolesVM));
            tree.Add(new RoleNode(Loc.T("rest.role.alchemy"), CampingRoleType.Alchemist,
                () => new[] { vm.AlchemyCraftVM.PrimaryShiftVM, vm.AlchemyCraftVM.SecondaryShiftVM }, vm.AlchemyRolesVM));
            tree.Add(new RoleNode(Loc.T("rest.role.scribe"), CampingRoleType.ScrollScribe,
                () => new[] { vm.ScribesCraftVM.PrimaryShiftVM, vm.ScribesCraftVM.SecondaryShiftVM }, vm.ScribeRolesVM));

            // Number of rests: a dropdown over the game's iteration radios — selecting one drives the
            // game's selector, whose bound view writes RestIterationsCount.
            tree.Add(new ProxyChoiceDropdown(Loc.T("rest.iterations"), new List<string> { "1", "2", "3" },
                () => Game.Instance.Player.Camping.RestIterationsCount - 1,
                idx =>
                {
                    foreach (var b in IterationButtons(vm))
                        if (b.IterationNumber == idx + 1) b.SetSelectedFromView(true);
                }));
            // The autotune/healing toggles are written by the game's VIEW straight into CampingState
            // (SetAutotuneIterationsState / SetHealingState) — mirror those one-line writes. The
            // iteration radios update themselves off the autotune event.
            tree.Add(new ProxyBoolToggle(TextUtil.StripRichText(UIStrings.Instance.Rest.RecommendedIterationsNumber),
                () => Game.Instance.Player.Camping.AutotuneRestIterations,
                () => { var c = Game.Instance.Player.Camping; c.AutotuneRestIterations = !c.AutotuneRestIterations; }));
            tree.Add(new ProxyBoolToggle(TextUtil.StripRichText(UIStrings.Instance.Rest.HealingUseSpells),
                () => Game.Instance.Player.Camping.UseSpells,
                () => { var c = Game.Instance.Player.Camping; c.UseSpells = !c.UseSpells; }));

            tree.Add(new ProxyActionButton(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.AutoGroupTooltipHeader),
                () => true, vm.AutoGroup));
            tree.Add(new ProxyActionButton(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.StartButton),
                () => true, vm.StartRest)); // -> the game's view: fade out + RestController.StartCamp

            Add(tree);
        }

        // A role node: collapsed it reads the live assignment ("Guards, first watch: Seelah").
        // Expanding builds its content from FRESH game VMs and raises the game's stage-open event so
        // the real role panel shows on screen; collapsing (incl. the accordion collapse when another
        // role expands) raises stage-close, hiding the panel — the sighted panel swap, mirrored.
        private sealed class RoleNode : Container
        {
            private readonly CampingRoleType _type;
            private readonly Func<RestShiftVM[]> _shifts; // [primary, assistant], fetched fresh per expand
            private readonly RestRolesVM _roles;

            public RoleNode(string label, CampingRoleType type, Func<RestShiftVM[]> shifts, RestRolesVM roles)
                : base(ContainerShape.Tree, label)
            {
                _type = type;
                _shifts = shifts;
                _roles = roles;
                LabelProvider = () =>
                {
                    var unit = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(type, true);
                    return label + ": " + (unit != null ? unit.CharacterName : Loc.T("rest.nobody"));
                };
            }

            public override bool Expandable => true; // children build lazily on expand

            public override void Expand()
            {
                base.Expand(); // accordion: the open sibling collapses (closing its panel) first
                Clear();
                var shifts = _shifts();
                if (_roles != null)
                {
                    var roles = _roles;
                    Add(new TextElement(() => Loc.T("rest.dc", new { value = roles.CalculateDCValue() }),
                        tooltip: () => roles.DCTooltipTemplate));
                }
                Add(new RestSlotDropdown(Loc.T("rest.primary"), shifts[0]));
                Add(new RestSlotDropdown(Loc.T("rest.assistant"), shifts[1]));
                EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h) { h.RestStageOpened(_type, CraftStage.UnitSelection); });
            }

            public override void Collapse()
            {
                base.Collapse();
                EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h) { h.RestStageClosed(_type, CraftStage.UnitSelection, fromCloseButton: true); });
            }
        }

        // A camp-slot dropdown ("Primary, combo box, Seelah, +12"). Enter opens the shared chooser
        // with LIVE options — availability shifts as other roles claim people — "None" first, the
        // assigned person always listed. Selecting drives the game's own radio contract
        // (SetSelectedFromView -> AddUnitToRole / RemoveUnitFromRole, with the game's select sound).
        private sealed class RestSlotDropdown : UIElement
        {
            public override Type AnnouncementOrderType => typeof(ProxyDropdown);

            private readonly string _label;
            private readonly RestShiftVM _shift;

            public RestSlotDropdown(string label, RestShiftVM shift) { _label = label; _shift = shift; }

            private RestShiftUnitVM Selected => _shift != null ? _shift.SelectedUnit.Value : null;

            public override IEnumerable<Announcement> GetFocusAnnouncements()
            {
                yield return new LabelAnnouncement(Message.Raw(_label));
                yield return new RoleAnnouncement("combo box");
                var sel = Selected;
                yield return new ValueAnnouncement(Message.Raw(sel != null ? UnitLabel(sel) : Loc.T("rest.nobody")));
            }

            public override IEnumerable<ElementAction> GetActions()
            {
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.open"), _ => OpenChooser());
            }

            private void OpenChooser()
            {
                if (_shift == null) return;
                // The VM's public Units collection is PAGED (6 portraits per prefab row); list everyone.
                var all = AllUnitsField?.GetValue(_shift) as List<RestShiftUnitVM>;
                if (all == null) all = new List<RestShiftUnitVM>(_shift.Units);
                var sel = Selected;
                var options = new List<string> { Loc.T("rest.nobody") };
                var units = new List<RestShiftUnitVM> { null };
                foreach (var u in all)
                {
                    if (u == null) continue;
                    if (u != sel && !u.IsAvailable.Value) continue; // dead / primary elsewhere — the game greys them
                    options.Add(UnitLabel(u));
                    units.Add(u);
                }
                int current = sel != null ? units.IndexOf(sel) : 0;
                var label = _label;
                ChoiceSubmenuScreen.Open(label, options, current, idx =>
                {
                    if (idx <= 0) Selected?.SetSelectedFromView(false); // None -> unassign
                    else if (idx < units.Count) units[idx].SetSelectedFromView(true);
                });
            }
        }

        private static readonly FieldInfo AllUnitsField =
            typeof(RestShiftVM).GetField("m_AllUnits", BindingFlags.NonPublic | BindingFlags.Instance);

        private static string UnitLabel(RestShiftUnitVM u)
        {
            var name = u.UnitData != null ? u.UnitData.CharacterName : "";
            return name + ", " + UIUtility.AddSign(u.SkillValue.Value);
        }

        private static readonly FieldInfo IterButtonsField =
            typeof(RestVM).GetField("m_IterationsButtons", BindingFlags.NonPublic | BindingFlags.Instance);

        private static IEnumerable<RestIterationRadioButtonVM> IterationButtons(RestVM vm)
            => IterButtonsField?.GetValue(vm) as List<RestIterationRadioButtonVM>
               ?? (IEnumerable<RestIterationRadioButtonVM>)new RestIterationRadioButtonVM[0];

        // ---- in process (the night plays out) ----

        private void BuildInProcess(RestVM vm)
        {
            var list = new ListContainer(Loc.T("rest.in_process"));
            list.Add(new TextElement(() => Loc.T("rest.in_process")));
            list.Add(new ProxyActionButton(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.ContinueButton),
                () => true, vm.StartRest)); // → the game's view: RestController.SkipPhase
            Add(list);
        }

        // ---- results ----

        private void BuildResults(RestVM vm)
        {
            var list = new ListContainer(Loc.T("rest.results"));
            var status = RestController.Instance != null ? RestController.Instance.Status : null;
            if (status != null)
            {
                list.Add(new TextElement(() => Loc.T("rest.total_time", new { time = TimeText(status.TotalTime) })));
                if (status.WasNightRandomEncounter)
                {
                    var watch = Loc.T(status.WakeUpGuardsSlot == 1 ? "rest.role.guard_second" : "rest.role.guard_first");
                    list.Add(new TextElement(() => TextUtil.StripRichText(UIStrings.Instance.Rest.NightEncounter) + ", " + watch));
                }
                var iters = status.Iterations;
                if (iters != null && iters.Count > 0)
                {
                    // Mirror RestPCView.ShowResults: camp checks from the current iteration, craft
                    // checks from whichever iteration last rolled them.
                    var cur = iters[Math.Min(status.IterationNumber, iters.Count - 1)];
                    AddCheck(list, Loc.T("rest.role.divine"), cur.DivineService);
                    AddCheck(list, Loc.T("rest.role.camouflage"), cur.Camouflage);
                    AddCheck(list, Loc.T("rest.role.guard_first"), cur.GuardFirst);
                    AddCheck(list, Loc.T("rest.role.guard_second"), cur.GuardSecond);
                    AddCheck(list, Loc.T("rest.role.alchemy"), LastCheck(iters, s => s.AlchemyPotions));
                    AddCheck(list, Loc.T("rest.role.cooking"), LastCheck(iters, s => s.AlchemyCooking));
                    AddCheck(list, Loc.T("rest.role.scribe"), LastCheck(iters, s => s.ScrollScribing));
                }
            }
            list.Add(new ProxyActionButton(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.ContinueButton),
                () => true, vm.StartRest)); // → the game's view: RestController.FinishRest
            Add(list);
        }

        private static CheckStatus LastCheck(List<RestIterationStatus> iters, Func<RestIterationStatus, CheckStatus> get)
        {
            for (int i = iters.Count - 1; i >= 0; i--)
            {
                var c = get(iters[i]);
                if (c != null && c.Check != null) return c;
            }
            return null;
        }

        private static void AddCheck(ListContainer list, string role, CheckStatus check)
        {
            if (check == null || check.Check == null) return;
            var c = check;
            var r = role;
            list.Add(new TextElement(() => Loc.T("rest.check", new
            {
                role = r,
                unit = c.Check.Initiator != null ? c.Check.Initiator.CharacterName : "",
                roll = c.Check.RollResult,
                dc = c.Check.DC,
                result = Loc.T(c.Success ? "rest.success" : "rest.failure"),
            })));
        }

        // ---- shared ----

        private static string TimeText(TimeSpan t)
            => string.Format(UIStrings.Instance.TimeTexts.TimeDay, t.Days) + " "
             + string.Format(UIStrings.Instance.TimeTexts.TimeHour, t.Hours);

        // Escape mirrors the view's CloseRest: refused mid-rest, otherwise the close-request event
        // (the same one its X button raises) tears the window down and stops Rest mode.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null && vm.CurrentPhase.Value != UIRestPhase.InProcess)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => EventBus.RaiseEvent(delegate(IRestRequestEvents h) { h.HandleRestCloseRequest(); }));
        }
    }
}
