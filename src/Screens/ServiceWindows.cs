using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The live <see cref="ServiceWindowsVM"/>, from whichever ROOT currently owns the service
    /// windows: in an area it's the InGame static part; on the WORLD MAP it's the GlobalMapVM
    /// (each constructs its own instance). Every service-window screen resolves through here —
    /// reading only the in-game chain made a window opened on the world map an EMPTY screen whose
    /// Escape closed nothing (the Ctrl+I-on-the-world-map softlock).
    /// </summary>
    internal static class ServiceWindows
    {
        public static ServiceWindowsVM Current
        {
            get
            {
                var rc = Game.Instance?.RootUiContext;
                if (rc == null) return null;
                return rc.InGameVM?.StaticPartVM?.ServiceWindowsVM ?? rc.GlobalMapVM?.ServiceWindowsVM;
            }
        }

        /// <summary>The shared character-switcher Tab-stop (inventory / char sheet / spellbook lead
        /// with it): the party as buttons driving the game's real selection, the current character
        /// carrying a live "selected" part (it also announces when a switch lands elsewhere).</summary>
        public static void EmitCharacterSwitcher(GraphBuilder b, string keyPrefix)
        {
            var party = Game.Instance?.Player?.Party;
            if (party == null || party.Count == 0) return;
            b.BeginStop("chars").PushContext(Loc.T("label.characters"), "list");
            int ci = 0;
            foreach (var u in party)
            {
                var un = u;
                var vt = GraphNodes.Button(() => un.CharacterName,
                    () => Game.Instance.SelectionCharacter.SetSelected(un));
                vt.Announcements = new System.Collections.Generic.List<NodeAnnouncement>(vt.Announcements)
                {
                    GraphNodes.SelectedPart(
                        () => Game.Instance?.SelectionCharacter?.CurrentSelectedCharacter == un),
                };
                b.AddItem(ControlId.Referenced(un, keyPrefix + "char:" + ci), vt);
                ci++;
            }
            b.PopContext();
        }
    }
}
