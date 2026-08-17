using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsVM, ServiceWindowsType
using UnityEngine;
using WrathAccess.Exploration; // LocalMapCursor, LocalMapReview
using WrathAccess.Input; // InputCategory
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The accessible local map (the LocalMap service window, opened from the HUD Windows list or
    /// Ctrl+M). The game's map is a top-down photograph of the level — pure pixels — so this screen is
    /// built entirely over our own world model instead: a free MOVEMENT cursor
    /// (<see cref="LocalMapCursor"/>, WASD/arrows at triple the in-area glide speed, room changes
    /// narrated) plus review cycles over the map's content (<see cref="LocalMapReview"/> — party /
    /// units / the game's point-of-interest markers, which live HERE and not in the in-area scanner:
    /// they're annotations, not world objects). Enter plants the in-area cursor at the map cursor,
    /// Backspace sends the party (the vanilla map's right-click), Escape closes.
    /// Starts unfocused with no tab stops — the cursor owns the keys (mirrors the world-map screen).
    /// </summary>
    public sealed class LocalMapScreen : Screen
    {
        public const string ScreenKey = "service.localmap";

        public override string Key => ScreenKey;
        public override string ScreenName => Loc.T("screen.map");
        public override int Layer => 10;
        public override bool Exclusive => true; // a service window: the in-game screen underneath goes dead

        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.LocalMap;

        public override bool StartUnfocused => true; // the map cursor owns WASD/arrows from the start
        public override bool AllowsTypeahead => false; // letters are map hotkeys (b/m/n/k/c…), not search

        // The movement/review/action keys are the SHARED Exploration actions (Route()d to the map
        // systems by screen); LocalMap holds only the Escape→close key.
        // Windows included so the window.* hotkeys act as the sighted tab bar from the map too.
        private static readonly IReadOnlyList<InputCategory> Focused = new[] { InputCategory.UI, InputCategory.Exploration, InputCategory.LocalMap, InputCategory.Windows };
        private static readonly IReadOnlyList<InputCategory> Unfocused = new[] { InputCategory.Exploration, InputCategory.LocalMap, InputCategory.UI, InputCategory.Windows };
        public override IReadOnlyList<InputCategory> InputCategories
            => Navigation.HasFocus ? Focused : Unfocused;

        public override void OnPush()
        {
            // Reset FIRST (it seeds the map cursor from the real in-area cursor), then redirect all
            // shared-cursor READS at the map cursor: sonar, wall tones, object/fog cues and the audio
            // listener follow it for the rest of the session with no per-system plumbing. The in-area
            // point itself is untouched (Enter writes it explicitly), so closing the map snaps every
            // system back to where you were exploring.
            Exploration.Cursor.ClearOverride();
            LocalMapReview.Reset();
            LocalMapCursor.Reset();
            Exploration.Cursor.SetOverride(() => LocalMapCursor.Position);
        }

        public override void OnPop() => Exploration.Cursor.ClearOverride();

        public override void OnUpdate() => LocalMapCursor.Tick(Time.unscaledDeltaTime);

        public static void Close() => ServiceWindows()?.HandleCloseAll();

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Close());
        }
    }
}
