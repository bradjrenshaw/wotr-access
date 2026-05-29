using System;
using System.Collections.Generic;
using Kingmaker;

namespace WrathAccess.Input
{
    /// <summary>
    /// Registry + per-frame poll, ticked from Main.OnUpdate. Each JustPressed
    /// action is dispatched into the active navigator first (only while focus mode
    /// owns the keyboard); if unconsumed, its global handler fires. Nav keys
    /// (arrows/confirm) have no global handler, so off-focus-mode they're inert and
    /// the game keeps its keys; global hotkeys (toggle/etc.) fire in either mode.
    /// </summary>
    public static class InputManager
    {
        private static readonly List<InputAction> _actions = new List<InputAction>();
        public static IReadOnlyList<InputAction> Actions => _actions;

        public static InputAction Register(string key, string label, Action onPerformed = null)
        {
            var action = new InputAction(key, label);
            if (onPerformed != null) action.Performed += onPerformed;
            _actions.Add(action);
            return action;
        }

        /// <summary>Whether the action with this key is currently held — for per-frame polling (e.g. the
        /// continuous overlay reading held arrows), rather than the press/repeat Performed path.</summary>
        public static bool Held(string key)
        {
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Key == key) return _actions[i].Held;
            return false;
        }

        public static void Tick()
        {
            // Don't steal keystrokes while the player is typing in a game text field — either the
            // game's own console field (IsInInputField) or one we're driving via TextEntry.
            if (IsTypingInTextField() || WrathAccess.UI.TextEntry.SuppressInput) return;

            // A screen capturing raw input (e.g. key-binding capture) wants the keys to reach
            // the game's own handler — stand down entirely while it's focused.
            var current = WrathAccess.Screens.ScreenManager.Current;
            if (current != null && current.CapturesRawInput) return;

            // Typematic repeat: fire once, pause, then repeat while held — at the user's
            // own OS keyboard delay/rate (falls back to defaults off Windows).
            float now = UnityEngine.Time.unscaledTime;
            float initialDelay = OsKeyboard.InitialDelay;
            float repeatInterval = OsKeyboard.RepeatInterval;
            for (int i = 0; i < _actions.Count; i++)
            {
                var action = _actions[i];
                bool held = action.Held;

                bool fire = false;
                if (action.JustPressed)
                {
                    fire = true;
                    action.NextRepeatTime = now + initialDelay;
                }
                else if (action.Repeats && held && now >= action.NextRepeatTime)
                {
                    // Held past the delay → auto-repeat. Catch up at most one step per frame.
                    fire = true;
                    action.NextRepeatTime = now + repeatInterval;
                }
                if (!held) action.NextRepeatTime = 0f; // reset on release

                if (!fire) continue;
                bool consumed = FocusMode.Active && WrathAccess.UI.Navigation.DispatchJustPressed(action);
                if (!consumed) action.InvokePerformed();
            }
        }

        private static bool IsTypingInTextField()
        {
            var game = Game.Instance;
            return game != null && game.RootUiContext != null && game.RootUiContext.IsInInputField;
        }
    }
}
