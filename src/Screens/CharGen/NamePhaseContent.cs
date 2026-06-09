using System.Reflection;
using TMPro;
using Kingmaker.UI.MVVM._PCView.CharGen;       // CharGenView (static self-ref + SelectedDetailView)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Name; // CharGenNamePhaseVM
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Name &amp; birthdate phase: a text field for the character name (driving the game's own
    /// TMP_InputField for full Unicode/IME), a random-name button (mirrors the game's generate
    /// click), and the birth month/day cycle selectors.
    /// </summary>
    public sealed class NamePhaseContent : CharGenPhaseContent<CharGenNamePhaseVM>
    {
        public NamePhaseContent(CharGenNamePhaseVM phase) : base(phase) { }

        public override void Build(Container content)
        {
            content.Add(new ProxyTextField("Character name", AcquireNameField, () => Phase.InputText));

            content.Add(new ProxyActionButton("Random name", () => true, () =>
            {
                // Mirror CharGenNamePhaseDetailedPCView.OnGenerateButtonClick: roll a name, push it to
                // the visual field, and commit it through the VM's onEndEdit.
                string name = Phase.GetRandomName();
                var field = AcquireNameField();
                if (field != null) field.text = name;
                Phase.OnEndEdit(name);
                Tts.Speak(Loc.T("name.announce", new { name = string.IsNullOrEmpty(name) ? Loc.T("name.blank") : name }), interrupt: true);
            }));

            content.Add(new ProxySequentialSelector("Birth month", () => Phase.MonthSelectorVM));
            content.Add(new ProxySequentialSelector("Birth day", () => Phase.DaySelectorVM));
        }

        // The TMP_InputField lives on the active detailed phase view, which the one-way MVVM binding
        // does NOT surface through the VM tree. CharGenView keeps a private static self-reference plus
        // a SelectedDetailView field, so we reach the live view (and its private field) by reflection
        // — a deterministic chain, no FindObjectOfType scene scan.
        private static FieldInfo _fInstance, _fSelected;
        private static TMP_InputField AcquireNameField()
        {
            try
            {
                var cgvType = typeof(CharGenView);
                _fInstance ??= cgvType.GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
                var view = _fInstance?.GetValue(null);
                if (view == null) return null;

                _fSelected ??= cgvType.GetField("SelectedDetailView", BindingFlags.Instance | BindingFlags.NonPublic);
                var detail = _fSelected?.GetValue(view);
                if (detail == null) return null;

                var fName = detail.GetType().GetField("m_NameInputField", BindingFlags.Instance | BindingFlags.NonPublic);
                return fName?.GetValue(detail) as TMP_InputField;
            }
            catch
            {
                return null;
            }
        }
    }
}
