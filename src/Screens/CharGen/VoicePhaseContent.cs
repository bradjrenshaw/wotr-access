using System.Collections.Generic;
using Kingmaker.Blueprints; // Gender
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Voice phase: a gender filter (the game's Male/Female filter buttons) over the list of character
    /// voices. The selector holds both genders, but the game shows one at a time, defaulting to the
    /// character's; we mirror that with a "Voice gender" chooser defaulting to the character's gender.
    /// Selecting a voice chooses it and plays a sample (the game plays off the resulting Barks), and
    /// re-selecting the current one replays it. The selector is built lazily by the game — immediate
    /// mode renders it once it materializes.
    /// </summary>
    public sealed class VoicePhaseContent : CharGenPhaseContent<CharGenVoicePhaseVM>
    {
        private static readonly List<string> GenderOptions = new List<string> { "Male", "Female" };

        private Gender? _filter; // view state: which gender's voices show (defaults to the character's)

        public VoicePhaseContent(CharGenVoicePhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            if (_filter == null) _filter = Phase.CharacterGender; // default, like the game

            b.AddItem(ControlId.Structural(k + "genderfilter"),
                ModSettingNodes.ChoiceDropdown("Voice gender", GenderOptions,
                    () => _filter == Gender.Female ? 1 : 0,
                    i => _filter = i == 1 ? Gender.Female : Gender.Male));

            var voices = Voices();
            if (voices.Count == 0) return; // lazy — renders once the selector materializes

            // Mirror CharGenVoiceSelectorPCView.IsVisible: same-gender items are always visible;
            // cross-gender items are hidden EXCEPT the empty ("None") voice, which is always visible
            // regardless of filter. Mirror EntityComparer: empty voices sort to the end.
            var visible = new List<CharGenVoiceItemVM>();
            foreach (var v in voices)
                if (v != null && (v.Gender == _filter || v.IsEmptyVoice)) visible.Add(v);
            visible.Sort((a, bb) => a.IsEmptyVoice == bb.IsEmptyVoice ? 0 : a.IsEmptyVoice ? 1 : -1);

            b.BeginStop("voices").PushContext(Loc.T("chargen.voices"), "list");
            int i = 0;
            foreach (var v in visible)
            {
                var voice = v; // capture for the live closure
                // Activate mirrors the game's item view: re-activating the current voice replays its
                // sample (PlayPreview); picking a new one selects it (the sample plays off the Barks).
                b.AddItem(ControlId.Referenced(voice, k + "voice:" + i),
                    GraphNodes.SelectionItem(voice, () => voice.DisplayName, onActivate: () =>
                    {
                        if (voice.IsSelected.Value) voice.Barks?.PlayPreview();
                        else voice.SetSelectedFromView(true);
                    }, sound: null));
                i++;
            }
            b.PopContext();
        }

        private List<CharGenVoiceItemVM> Voices()
        {
            var result = new List<CharGenVoiceItemVM>();
            var entities = Phase.VoiceSelector?.EntitiesCollection;
            if (entities != null)
                foreach (var v in entities) if (v != null) result.Add(v);
            return result;
        }
    }
}
