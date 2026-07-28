using System.Collections.Generic;
using System.IO;
using WrathAccess.Exploration;
using WrathAccess.Exploration.Overlays; // OverlayAudio
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The audio glossary (Ctrl+M → Help → Audio glossary): learn which sound is which by playing
    /// them on demand. A tree with a Sonar category holding one button per scanner/sonar entity type
    /// whose sound pick resolves to a real sound (LIVE — reflects the user's own assignments,
    /// including inherited ones; muted types still list, the Play-sound switch gates the sonar, not
    /// reference material). Enter plays the sound flat (centred 2D) at the user's sonar volume, so it
    /// sounds exactly like a ping right on top of the cursor. Structure mirrors the taxonomy:
    /// branch categories with several sounding types become subgroups; single-sound categories are
    /// plain buttons. Future sound families (cues, wall tones) get sibling categories here.
    /// </summary>
    public sealed class GlossaryScreen : Screen
    {
        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.glossary";
        public override string ScreenName => Loc.T("screen.audio_glossary");
        public override int Layer => 41; // above Help (38), so it stacks on top and returns to it
        public override bool IsActive() => s_open;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }

        public override void Build(GraphBuilder b)
        {
            b.BeginStop("tree");
            b.BeginGroup(ControlId.Structural("gloss:sonar"),
                GraphNodes.Group(() => Loc.T("glossary.sonar")));
            foreach (var cat in ScanTaxonomy.Categories) EmitCategory(b, cat);
            foreach (var cat in GlobalMapTaxonomy.Categories) EmitCategory(b, cat);
            b.EndGroup();
        }

        // One taxonomy category. Leaf categories (Doors, Exits, Traps, …) are flat buttons, shown
        // only when their pick resolves to a sound. Branch categories (Units, Containers, World map)
        // are subgroups shown when ANYTHING under them sounds — and then list ALL their children, so
        // the structure is stable ("Units" always holds Party/Enemies/Neutrals): children with no
        // assigned sound say so instead of playing.
        private static void EmitCategory(GraphBuilder b, ScanTaxonomy.Node cat)
        {
            var catStem = ScanSounds.ResolveAssigned(cat.Key);
            if (!cat.IsBranch)
            {
                if (catStem != null)
                    b.AddItem(ControlId.Structural("gloss:" + cat.Key),
                        SoundButton(() => Loc.T(cat.LocKey), catStem));
                return;
            }

            bool anySound = catStem != null;
            foreach (var child in cat.Children)
                if (ScanSounds.ResolveAssigned(child.Key) != null) { anySound = true; break; }
            if (!anySound) return;

            b.BeginGroup(ControlId.Structural("gloss:g:" + cat.Key), GraphNodes.Group(() => Loc.T(cat.LocKey)));
            if (catStem != null)
                b.AddItem(ControlId.Structural("gloss:" + cat.Key),
                    SoundButton(() => Loc.T("taxonomy." + cat.Key + ".all"), catStem));
            foreach (var child in cat.Children)
            {
                var c = child; // capture per-iteration
                b.AddItem(ControlId.Structural("gloss:" + c.Key),
                    SoundButton(() => Loc.T(c.LocKey), ScanSounds.ResolveAssigned(c.Key)));
            }
            b.EndGroup();
        }

        // A play button — WITHOUT the standard UI click (it would mask the cue being learned). A node
        // with no assigned sound announces and speaks that instead.
        private static NodeVtable SoundButton(System.Func<string> label, string stemOrNull)
        {
            if (stemOrNull != null)
                return GraphNodes.Button(label, () => PlayStem(stemOrNull), sound: null);
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => Loc.T("glossary.no_sound"), live: false,
                        kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () => Tts.Speak(Loc.T("glossary.no_sound"), interrupt: true),
            };
        }

        // Play flat/centred at the live sonar volume — what a ping right at the cursor sounds like.
        private static void PlayStem(string stem)
        {
            float volume = (ModSettings.GetSetting<IntSetting>("audio.volumes.sonar")?.Get() ?? 40) / 100f
                * OverlayAudio.Master;
            WrathAccess.Audio.AudioEngines.NAudio.Play2D(
                Path.Combine(OverlayAudio.Dir, "interactables", stem + ".wav"), volume);
        }
    }
}
