using System.Collections.Generic;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Base for a navigable screen — and the root Container of its element tree.
    /// Lifecycle (dispatched by ScreenManager from the stack diff): OnPush (entered
    /// the stack) → OnFocus (became active); OnUnfocus → OnPop on the way out. The
    /// push/focus split enables focus restoration: a covered screen gets OnUnfocus
    /// then, when re-exposed, OnFocus without another OnPush, so its built tree and
    /// remembered focus survive.
    ///
    /// Navigation/input is owned by the active Navigator (Navigation.Active), which
    /// ScreenManager attaches to the focused screen. Screens just build their tree
    /// (OnPush) and expose ScreenName.
    /// </summary>
    public abstract class Screen : Container
    {
        // Screens are panels (Tab between their regions); a single child list then
        // navigates with arrows and doesn't report a meaningless "1 of 1" position.
        protected Screen() { Shape = ContainerShape.Panel; }

        /// <summary>Stable identity used for stack diffing.</summary>
        public abstract string Key { get; }

        /// <summary>Spoken when the screen gains focus. Null/empty = silent.</summary>
        public virtual string ScreenName => null;

        /// <summary>Stack layer: higher sits on top. 0 = base context, then service windows, then overlays.</summary>
        public virtual int Layer => 0;

        /// <summary>Is this screen currently showing? Evaluated every frame.</summary>
        public abstract bool IsActive();

        /// <summary>
        /// When true (and this is the focused screen), InputManager stops dispatching so raw
        /// keys reach the game (e.g. the key-binding capture dialog, which reads input itself).
        /// </summary>
        public virtual bool CapturesRawInput => false;

        public virtual void OnPush() { }

        public virtual void OnFocus()
        {
            // Screen-change announcement (never interrupt — carried SayTheSpire preference).
            // The Navigator separately announces the focused element within the screen.
            if (!string.IsNullOrEmpty(ScreenName))
                Tts.Speak(ScreenName);
        }

        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }
        public virtual void OnUpdate() { }

        public virtual List<string> GetHelpMessages() => new List<string>();
    }
}
