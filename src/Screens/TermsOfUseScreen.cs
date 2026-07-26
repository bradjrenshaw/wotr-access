using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings, UITermsOfUseTexts
using Kingmaker.Settings; // SettingsRoot
using Kingmaker.UI; // TermsOfUseController
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Accessible mirror of the game's license window (<see cref="TermsOfUseController"/>, the
    /// "TermsOfUse_Legacy" object in the main-menu scene). The game opens it over the main menu on any
    /// machine whose settings never recorded an acceptance (<c>terms-of-use-seen-v3</c> in
    /// general_settings.json — first launch on both Steam and GOG; Steam's own install-time EULA is a
    /// separate client-side record the game never reads), and again from the menu's License entry. The
    /// window itself is mouse-only OwlcatButtons — invisible to keyboard flow — so blind players either
    /// coasted through it unaware (never accepting; it reopened every launch) or found it via OCR with
    /// no way to act on it. This mirror speaks the game's own localized license text and drives the
    /// window's real handlers: Accept persists the acceptance (never shows again), Decline QUITS the
    /// game (the game's own behavior, mirrored), OK (re-opened later, already accepted) just closes.
    /// Escape hides it for the session without accepting, exactly like the game's Esc handling.
    ///
    /// Layer sits BELOW the mod-menu launcher and setup wizard: on a fresh boot the wizard configures
    /// speech FIRST (you need a voice to hear a license), and this screen takes focus the moment the
    /// wizard closes. Everything spoken here is game content passed through — no strings of our own.
    /// </summary>
    public sealed class TermsOfUseScreen : Screen
    {
        public TermsOfUseScreen() { Wrap = true; }

        public override string Key => "ctx.termsofuse";
        public override string ScreenName
        {
            get
            {
                string s = UIStrings.Instance?.MainMenu?.License;
                return string.IsNullOrEmpty(s) ? Loc.T("screen.license") : s;
            }
        }
        public override int Layer => 34; // above the menu context; below the launcher (35) / wizard (36)
        public override bool Exclusive => true; // a legal gate — own the keyboard until answered

        // LIVE from the game (read-live rule): the window's own shown flag via RootUIContext.
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsPCTermOfUseShow ?? false;

        private static TermsOfUseController Controller()
            => Game.Instance?.RootUiContext?.MainMenuPCView?.TermOfUse;

        // Accept/Decline on first run, OK once accepted — the same predicate the window's own button
        // setup uses (TermsOfUseController.Bind).
        private static bool FirstRun() => !SettingsRoot.Game.Main.IsTermsOfUseShown.GetValue();

        private static UITermsOfUseTexts Texts()
            => Game.Instance?.BlueprintRoot?.LocalizedTexts?.UserInterfacesText?.TermsOfUseTexts;

        // The license body split into arrowable paragraphs (a EULA as one text node is unnavigable).
        // Cached per activation; cleared on pop so a locale change rebuilds next open.
        private List<string> _paragraphs;
        public override void OnPop() { _paragraphs = null; }

        private List<string> Paragraphs(UITermsOfUseTexts texts)
        {
            if (_paragraphs != null) return _paragraphs;
            var all = new List<string>();
            foreach (string block in new string[] { texts.Licence, texts.SubLicence })
            {
                if (string.IsNullOrEmpty(block)) continue;
                foreach (var line in block.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) all.Add(t);
                }
            }
            return _paragraphs = all;
        }

        public override void Build(GraphBuilder b)
        {
            var texts = Texts();
            if (texts == null) return;
            string k = "tou:";

            // The license text under the game's header as context; each paragraph its own item.
            var paras = Paragraphs(texts);
            b.BeginStop("text").PushContext((string)texts.Header ?? "", "list");
            for (int i = 0; i < paras.Count; i++)
            {
                var p = paras[i];
                b.AddItem(ControlId.Structural(k + "p" + i), GraphNodes.Text(() => p));
            }
            b.PopContext();

            if (FirstRun())
            {
                b.BeginStop("accept").AddItem(ControlId.Structural(k + "accept"),
                    GraphNodes.Button(() => (string)texts.AcceptBtn, () => Controller()?.Accept()));
                // Mirrors the game: Decline quits the application outright.
                b.BeginStop("decline").AddItem(ControlId.Structural(k + "decline"),
                    GraphNodes.Button(() => (string)texts.DeclineBtn, () => Controller()?.Decline()));
            }
            else
            {
                b.BeginStop("ok").AddItem(ControlId.Structural(k + "ok"),
                    GraphNodes.Button(() => (string)texts.OkBtn, () => Controller()?.Show(state: false)));
            }
        }

        // Escape = the game's own Esc handling: hide for this session without accepting (it will
        // reopen next launch until accepted).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Controller()?.Show(state: false));
        }
    }
}
