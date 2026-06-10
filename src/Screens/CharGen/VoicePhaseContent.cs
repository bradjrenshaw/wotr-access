using System.Collections.Generic;
using Kingmaker.Blueprints; // Gender
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Voice phase: a gender filter (the game's Male/Female filter buttons) over the list of character
    /// voices. The selector holds both genders, but the game shows one at a time, defaulting to the
    /// character's; we mirror that with a "Voice gender" chooser defaulting to the character's gender.
    /// Selecting a voice chooses it and plays a sample (the game plays off the resulting Barks), and
    /// re-selecting the current one replays it (see the onActivate at the call site). The selector is built
    /// lazily (once gender is known), so we (re)build the list when it materializes.
    /// </summary>
    public sealed class VoicePhaseContent : CharGenPhaseContent<CharGenVoicePhaseVM>
    {
        private static readonly List<string> GenderOptions = new List<string> { "Male", "Female" };

        private Panel _listPanel;
        private int _count = -1;
        private Gender _filter;

        public VoicePhaseContent(CharGenVoicePhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            _filter = Phase.CharacterGender; // default to the character's gender, like the game
            content.Add(new ProxyChoiceDropdown("Voice gender", GenderOptions,
                () => _filter == Gender.Female ? 1 : 0,
                i => { _filter = i == 1 ? Gender.Female : Gender.Male; FillList(); }));

            _listPanel = new Panel();
            content.Add(_listPanel);
            FillList();
        }

        public override void Tick()
        {
            if (VoiceCount() != _count) FillList();
        }

        private void FillList()
        {
            if (_listPanel == null) return;
            var voices = Voices();
            _count = voices.Count; // unfiltered count, for lazy-materialize detection
            _listPanel.Clear();
            var list = new ListContainer(Loc.T("chargen.voices"));

            // Mirror CharGenVoiceSelectorPCView.IsVisible: same-gender items are always visible;
            // cross-gender items are hidden EXCEPT the empty ("None") voice, which is always visible
            // regardless of filter. The VM dedups the cross-gender empty so the selector holds exactly
            // one "None" — that single entry appears under both Male and Female filters.
            var visible = new List<CharGenVoiceItemVM>();
            foreach (var v in voices)
                if (v != null && (v.Gender == _filter || v.IsEmptyVoice)) visible.Add(v);

            // Mirror EntityComparer: empty voices sort to the end.
            visible.Sort((a, b) => a.IsEmptyVoice == b.IsEmptyVoice ? 0 : a.IsEmptyVoice ? 1 : -1);

            // Activate mirrors the game's item view: re-activating the current voice replays its sample
            // (PlayPreview); picking a new one selects it (the game plays the sample off the resulting Barks).
            foreach (var v in visible)
            {
                var voice = v; // capture for the live closure
                list.Add(new ProxySelectionItem(voice, () => voice.DisplayName, onActivate: () =>
                {
                    if (voice.IsSelected.Value) voice.Barks?.PlayPreview();
                    else voice.SetSelectedFromView(true);
                }));
            }
            if (list.Children.Count > 0) _listPanel.Add(list);
        }

        private List<CharGenVoiceItemVM> Voices()
        {
            var result = new List<CharGenVoiceItemVM>();
            var entities = Phase.VoiceSelector?.EntitiesCollection;
            if (entities != null)
                foreach (var v in entities) if (v != null) result.Add(v);
            return result;
        }

        private int VoiceCount() => Phase.VoiceSelector?.EntitiesCollection?.Count ?? 0;
    }
}
