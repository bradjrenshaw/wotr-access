using System;
using System.Collections.Generic;
using UnityEngine; // Application.OpenURL
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The mod's top-level menu (Ctrl+M, available everywhere): a short list of entries — Settings, the
    /// setup wizard, and external links (Discord, Patreon). Mod-pushed (static open flag). Picking Settings
    /// or the wizard opens that screen ON TOP (it sits at a higher layer), so closing it returns here; the
    /// external links open a browser and leave the menu up. Opening engages focus mode so it owns the
    /// keyboard everywhere; Escape closes. The settings browser itself lives in <see cref="ModSettingsScreen"/>.
    /// </summary>
    public sealed class ModMenuScreen : Screen
    {
        private const string DiscordUrl = "https://discord.gg/Dz8u2Pr9py";
        private const string PatreonUrl = "https://www.patreon.com/bradjrenshaw";

        private static bool s_open;
        public static void Toggle() { s_open = !s_open; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.modmenu";
        public override string ScreenName => Loc.T("screen.mod_menu");
        public override int Layer => 35; // launcher; Settings (37) / Setup wizard (36) stack above it
        public override bool IsActive() => s_open;

        private bool _priorFocus;

        public override void OnPush()
        {
            _priorFocus = FocusMode.Active;
            FocusMode.Set(true); // own the keyboard everywhere while the menu is up
            Build();
        }

        public override void OnPop() { Clear(); FocusMode.Set(_priorFocus); }

        // Escape closes the menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }

        private void Build()
        {
            Clear();
            var list = new ListContainer();

            // Settings / the wizard open ON TOP (higher layer) and return here when closed; the launcher
            // stays open beneath them. (We don't close it on pick — the layering is the back-stack.)
            list.Add(Item("menu.settings", ModSettingsScreen.Open));
            list.Add(Item("menu.setup_wizard", SetupWizardScreen.Open));
            list.Add(Link("menu.discord", DiscordUrl, "menu.opening_discord"));
            list.Add(Link("menu.patreon", PatreonUrl, "menu.opening_patreon"));

            Add(list);
            SetFocusedChild(list);
        }

        private static ProxyActionButton Item(string labelKey, Action activate)
            => new ProxyActionButton(() => Loc.T(labelKey), null, activate);

        // An external link: open it in the browser and say so, since the result is off-screen.
        private static ProxyActionButton Link(string labelKey, string url, string speakKey)
            => new ProxyActionButton(() => Loc.T(labelKey), null, () =>
            {
                Application.OpenURL(url);
                Tts.Speak(Loc.T(speakKey));
            });
    }
}
