using Kingmaker.Blueprints.Root; // LocalizedTexts
using Kingmaker.PubSubSystem; // EventBus, IWarningNotificationUIHandler
using Kingmaker.UI; // WarningNotificationType

namespace WrathAccess
{
    /// <summary>
    /// Speaks the game's own "you can't do that" warnings (<see cref="IWarningNotificationUIHandler"/>) — the
    /// toast it shows when an action is refused: ability-cast refusals (with the exact restriction reason),
    /// "not enough actions", "no path", save/rest warnings, etc. So a refused cast reports the game's precise
    /// reason instead of a generic guess. A persistent EventBus subscriber, like <see cref="GameLogReader"/>.
    /// </summary>
    internal sealed class WarningReader : IWarningNotificationUIHandler
    {
        private static WarningReader _instance;

        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new WarningReader();
            EventBus.Subscribe(_instance);
        }

        // Enum form: resolve to the game's localized text (same source the on-screen warnings use).
        public void HandleWarning(WarningNotificationType warningType, bool addToLog = true)
            => Speak(LocalizedTexts.Instance?.WarningNotification?.GetText(warningType));

        // Text form: already the reason text (e.g. an ability target restriction's message).
        public void HandleWarning(string text, bool addToLog = true) => Speak(text);

        private static void Speak(string text)
        {
            if (!Main.Enabled || string.IsNullOrWhiteSpace(text)) return;
            Tts.Speak(text, interrupt: false); // reactive feedback — queue behind any current line
        }
    }
}
