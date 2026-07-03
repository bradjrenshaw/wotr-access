using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Voice phase: the list of character voices. The game shows one gender at a time behind Male/Female
    /// filter buttons (view state with no VM home); rather than shadow that, we show the WHOLE selector
    /// — every voice plus the empty "None" — so nothing is hidden and there's no mod state to hold
    /// (typeahead finds a voice by name on the longer list). Empty voices sort to the end, matching the
    /// game's EntityComparer. Selecting a voice chooses it and plays a sample (off the resulting Barks);
    /// re-selecting the current one replays it. The selector is built lazily by the game — immediate
    /// mode renders it once it materializes.
    /// </summary>
    public sealed class VoicePhaseContent : CharGenPhaseContent<CharGenVoicePhaseVM>
    {
        public VoicePhaseContent(CharGenVoicePhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var voices = Voices();
            if (voices.Count == 0) return; // lazy — renders once the selector materializes

            // Mirror EntityComparer: empty voices sort to the end.
            voices.Sort((a, bb) => a.IsEmptyVoice == bb.IsEmptyVoice ? 0 : a.IsEmptyVoice ? 1 : -1);

            b.PushContext(Loc.T("chargen.voices"), "list");
            int i = 0;
            foreach (var v in voices)
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
