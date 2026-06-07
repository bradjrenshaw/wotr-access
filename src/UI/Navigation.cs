using WrathAccess.Input;
using WrathAccess.Screens;

namespace WrathAccess.UI
{
    /// <summary>
    /// Holds the active Navigator (swappable by user preference later) and is the
    /// entry point input dispatches into. ScreenManager re-attaches it on screen change.
    /// </summary>
    public static class Navigation
    {
        public static Navigator Active = new TraditionalNavigator();

        public static void Attach(Screen screen) => Active?.Attach(screen);

        /// <summary>True when something is focused (the navigator owns the keys). False in an unfocused
        /// screen like exploration, where arrows bubble to the overlay.</summary>
        public static bool HasFocus => Active != null && Active.Current != null;

        public static bool DispatchJustPressed(InputAction action) =>
            Active != null && Active.OnInputJustPressed(action);

        public static void AnnounceCurrent() => Active?.AnnounceCurrent();

        public static void Focus(UIElement element, bool announce = true) => Active?.Focus(element, announce);
    }
}
