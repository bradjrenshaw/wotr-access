using System;
using System.Collections.Generic;
using Kingmaker.Blueprints; // Gender
using Kingmaker.Blueprints.CharGen; // CustomizationOptions
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.ResourceLinks; // EquipmentEntityLink
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Appearance;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Common; // StringSequentialSelectorVM, TextureSelectorItemVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // CharacterVisualSettingsEntityVM
using Kingmaker.UnitLogic.Class.LevelUp; // DollState
using Owlcat.Runtime.UI.SelectionGroup; // SelectionGroupRadioVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Appearance phase — the doll. Mirrors the game's detailed view block by block, each block a tab
    /// stop: body (build presets), face (heads + scars + skin and eye colour), hair (style, beard, colour;
    /// hidden for races without hair, like the game's placeholder), horns (+ colour; Tieflings), warpaints
    /// and tattoos (the game's five paged slots, shown as five rows each so the layering is audible), outfit
    /// colours, and the visual-settings toggles (cloth / helmet / backpack on the doll).
    ///
    /// Shapes are the game's own sequential selectors (Left/Right step them, which runs the game's setter
    /// and redraws the doll); colours are its radio groups. Everything the game leaves unnamed is named
    /// by <see cref="AppearanceCatalog"/>: authored descriptions per asset, body regions per paint, and
    /// colour words from the ramp textures. Lists are re-read live each render (race/gender changes
    /// re-list them, as the game's Change() does).
    /// </summary>
    public sealed class AppearancePhaseContent : CharGenPhaseContent<CharGenAppearancePhaseVM>
    {
        public AppearancePhaseContent(CharGenAppearancePhaseVM phase) : base(phase) { }

        private DollState Doll => Phase.DollState;
        private static UICharGen Cg => UIStrings.Instance.CharGen;

        private CustomizationOptions Options
        {
            get
            {
                var d = Doll;
                if (d?.Race == null) return null;
                return d.Gender == Gender.Male ? d.Race.MaleOptions : d.Race.FemaleOptions;
            }
        }

        public override void Build(GraphBuilder b, string k)
        {
            if (Doll == null) return;
            var cg = Cg;

            // ---- body + face ----
            b.BeginStop("body").PushContext((string)cg.Appearance, "group");
            b.AddItem(ControlId.Structural(k + "app:body"), Shape(cg.BodyConstitution, () => Phase.BodySelectorVM,
                i => { var r = Doll?.Race; if (r == null) return null; var p = r.Presets; return i >= 0 && i < p.Length ? AppearanceCatalog.DescribePreset(p[i]) : null; }));
            b.AddItem(ControlId.Structural(k + "app:face"), Shape(cg.Face, () => Phase.HeadSelectorVM,
                i => DescribeLink(Link(Options?.Heads, i))));
            if (Phase.ScarsSelectorVM.TotalCount > 0)
                b.AddItem(ControlId.Structural(k + "app:scar"), Shape(cg.Scar, () => Phase.ScarsSelectorVM,
                    i => DescribePaint(Link(Doll?.Scars, i))));
            b.AddItem(ControlId.Structural(k + "app:skin"), Colour(cg.SkinTone, () => Phase.BodyColorSelectorVM, "skin"));
            b.AddItem(ControlId.Structural(k + "app:eyes"), Colour(cg.EyesColor, () => Phase.EyesColorSelectorVM, "eyes"));
            b.PopContext();

            // ---- hair (the game hides the whole block when nothing in it applies) ----
            bool hair = Phase.HairSelectorVM.TotalCount > 0, beard = Phase.BeardSelectorVM.TotalCount > 0;
            if (hair || beard)
            {
                b.BeginStop("hair").PushContext((string)cg.HairStyle, "group");
                if (hair)
                    b.AddItem(ControlId.Structural(k + "app:hair"), Shape(cg.HairStyle, () => Phase.HairSelectorVM,
                        i => DescribeLink(Link(Options?.Hair, i))));
                if (beard)
                    b.AddItem(ControlId.Structural(k + "app:beard"), Shape(cg.Beard, () => Phase.BeardSelectorVM,
                        i => DescribeLink(Link(Options?.Beards, i))));
                b.AddItem(ControlId.Structural(k + "app:haircolour"), Colour(cg.HairColor, () => Phase.HairColorSelectorVM, "hair"));
                b.PopContext();
            }

            // ---- horns (Tieflings) ----
            if (Phase.HornSelectorVM.TotalCount > 0)
            {
                b.BeginStop("horns").PushContext((string)cg.Horns, "group");
                b.AddItem(ControlId.Structural(k + "app:horns"), Shape(cg.Horns, () => Phase.HornSelectorVM,
                    i => DescribeLink(Link(Options?.Horns, i))));
                b.AddItem(ControlId.Structural(k + "app:horncolour"), Colour(cg.HornsColor, () => Phase.HornColorSelectorVM));
                b.PopContext();
            }

            // ---- warpaints + tattoos: five slots each, all visible (the game pages them) ----
            Slots(b, k, "warpaint", cg.Warpaint, Phase.WarpaintsSelectorVMList, Phase.WarpaintsColorSelectorVMList,
                i => Doll?.Warprints != null && i < Doll.Warprints.Count ? Doll.Warprints[i].Paints : null);
            Slots(b, k, "tattoo", cg.Tattoo, Phase.TattoosSelectorVMList, Phase.TattoosColorSelectorVMList,
                i => Doll?.Tattoos != null && i < Doll.Tattoos.Count ? Doll.Tattoos[i].Paints : null);

            // ---- outfit colours ----
            b.BeginStop("outfit").PushContext((string)cg.ClothColor, "group");
            b.AddItem(ControlId.Structural(k + "app:outfit1"), Colour(cg.PrimaryClothColor, () => Phase.PrimaryOutfitColorVM));
            b.AddItem(ControlId.Structural(k + "app:outfit2"), Colour(cg.SecondaryClothColor, () => Phase.SecondaryOutfitColorVM));
            b.PopContext();

            // ---- visual settings (the game's small panel: cloth / helmet / backpack on the doll) ----
            var cs = UIStrings.Instance.CharacterSheet;
            b.BeginStop("visual").PushContext((string)cs.VisualSettingsTitle, "group");
            b.AddItem(ControlId.Structural(k + "app:cloth"), VisualToggle(cs.VisualSettingsShowCloth, v => v.Cloth, d => d.ShowCloth));
            b.AddItem(ControlId.Structural(k + "app:helmet"), VisualToggle(cs.VisualSettingsShowHelmet, v => v.Helmet, d => d.ShowHelm && d.ShowCloth));
            b.AddItem(ControlId.Structural(k + "app:backpack"), VisualToggle(cs.VisualSettingsShowBackpack, v => v.Backpack, d => d.ShowBackpack && d.ShowCloth));
            b.PopContext();
        }

        // One paint kind: the five slots, each "Warpaint slot 2" (shape) + its colour. Hidden when the
        // race has no list (Kitsune warpaints), exactly like the game's block.
        private void Slots(GraphBuilder b, string k, string key, string label,
            List<StringSequentialSelectorVM> shapes, List<SelectionGroupRadioVM<TextureSelectorItemVM>> colours,
            Func<int, List<EquipmentEntityLink>> paints)
        {
            if (shapes == null || shapes.Count == 0 || shapes[0].TotalCount == 0) return;
            b.BeginStop(key).PushContext(label, "group");
            for (int s = 0; s < shapes.Count; s++)
            {
                int slot = s;
                string slotLabel = Loc.T("chargen.app_slot", new { label, index = slot + 1 });
                b.AddItem(ControlId.Structural(k + "app:" + key + ":" + slot), Shape(slotLabel, () => Slot(shapes, slot),
                    i => DescribePaint(Link(paints(slot), i))));
                if (colours != null && slot < colours.Count)
                    b.AddItem(ControlId.Structural(k + "app:" + key + "colour:" + slot),
                        Colour(Loc.T("chargen.app_slot_colour", new { label, index = slot + 1 }), () => Slot(colours, slot)));
            }
            b.PopContext();
        }

        private static T Slot<T>(List<T> list, int i) where T : class => list != null && i < list.Count ? list[i] : null;

        private static EquipmentEntityLink Link(IList<EquipmentEntityLink> list, int i)
            => list != null && i >= 0 && i < list.Count ? list[i] : null;

        // ---- what we say ----

        private static string DescribeLink(EquipmentEntityLink link) => link == null ? null : AppearanceCatalog.Describe(link);

        // A paint / scar: "none" for the empty entity, else its description plus where it lands.
        private static string DescribePaint(EquipmentEntityLink link)
        {
            if (link == null) return null;
            if (AppearanceCatalog.IsEmpty(link)) return Loc.T("chargen.app_none");
            var desc = AppearanceCatalog.Describe(link);
            var regions = AppearanceCatalog.Regions(link);
            if (regions != null) regions = Loc.T("chargen.app_regions", new { regions });
            if (desc == null) return regions;
            return regions == null ? desc : desc + ", " + regions;
        }

        // ---- nodes ----

        /// <summary>A shape selector: Left/Right step the game's selector (its setter redraws the doll);
        /// reads "Face 3, description, 3 of 12".</summary>
        private static NodeVtable Shape(string label, Func<StringSequentialSelectorVM> vm, Func<int, string> describe)
        {
            Func<string> value = () =>
            {
                var v = vm?.Invoke();
                if (v == null || v.TotalCount == 0) return Loc.T("chargen.nothing_to_select");
                // The game's own title first ("Face 3" — what a sighted helper sees on screen), then the
                // description, then the position: "Face 3, oval face, ..., 3 of 12".
                string title = v.Value?.Value ?? "";
                string text = Loc.T("chargen.app_of", new { index = v.CurrentIndex.Value + 1, total = v.TotalCount });
                var d = describe?.Invoke(v.CurrentIndex.Value);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(title)) parts.Add(title);
                if (!string.IsNullOrEmpty(d)) parts.Add(d);
                parts.Add(text);
                return string.Join(", ", parts.ToArray());
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var v = vm?.Invoke();
                    if (v == null || v.TotalCount == 0) return;
                    if (sign < 0) v.OnLeft(); else v.OnRight(); // the game's arrows (their sound included)
                },
            };
        }

        /// <summary>A colour radio group: Left/Right move the selection (the game's setter recolours the
        /// doll); reads "dark brown, 3 of 12".</summary>
        private static NodeVtable Colour(string label, Func<SelectionGroupRadioVM<TextureSelectorItemVM>> group, string context = null)
        {
            Func<string> value = () =>
            {
                var g = group?.Invoke();
                var items = g?.EntitiesCollection;
                if (items == null || items.Count == 0) return Loc.T("chargen.nothing_to_select");
                // The skin list flags IsSelected directly (no SelectedEntity); the others go through it.
                var sel = g.SelectedEntity?.Value;
                if (sel == null) foreach (var it in items) if (it != null && it.IsSelected.Value) { sel = it; break; }
                int idx = sel != null ? items.IndexOf(sel) : -1;
                if (idx < 0) return Loc.T("chargen.app_colour_unset", new { total = items.Count });
                var ramps = new List<UnityEngine.Texture2D>(items.Count);
                foreach (var it in items) ramps.Add(it?.Texture?.Value);
                var names = AppearanceCatalog.ColourNames(ramps, context); // unique within this list
                return Loc.T("chargen.app_colour", new { index = idx + 1, total = items.Count, name = names[idx] });
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var g = group?.Invoke();
                    if (g == null || g.EntitiesCollection == null || g.EntitiesCollection.Count == 0) return;
                    // Untouched (the doll's default colour): the first step lands on the first / last swatch.
                    bool unset = g.SelectedEntity?.Value == null;
                    if (unset) foreach (var it in g.EntitiesCollection) if (it != null && it.IsSelected.Value) { unset = false; break; }
                    bool moved = unset
                        ? (sign < 0 ? g.TrySelectLastValidEntity() : g.TrySelectFirstValidEntity())
                        : sign < 0 ? g.SelectPrevValidEntity() : g.SelectNextValidEntity();
                    if (moved) UiSound.Play(UISoundType.ButtonClick); // the swatch click
                },
            };
        }

        /// <summary>One of the doll's visual-settings toggles. The game creates the panel's VM only when its
        /// button is pressed, so toggling first opens it (the same panel the sighted see) if needed.</summary>
        private NodeVtable VisualToggle(string label, Func<CharacterVisualSettingsVM, CharacterVisualSettingsEntityVM> pick,
            Func<DollState, bool> dollFlag)
        {
            Func<CharacterVisualSettingsEntityVM> entity = () => { var v = Phase.VisualSettingsVM.Value; return v != null ? pick(v) : null; };
            Func<bool> isOn = () =>
            {
                var e = entity();
                if (e != null) return e.IsOn.Value;
                var d = Doll; // panel closed: read the doll's own flags
                return d != null && dollFlag(d);
            };
            Func<bool> enabled = () => { var e = entity(); return e == null || !e.Locked.Value; };
            return GraphNodes.Toggle(() => label, isOn, () =>
            {
                if (Phase.VisualSettingsVM.Value == null) Phase.ShowVisualSettings(); // open the game's panel
                var e = entity();
                if (e != null && !e.Locked.Value) e.Switch();
            }, enabled);
        }
    }
}
