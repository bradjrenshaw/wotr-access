using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;

namespace WrathAccess
{
    /// <summary>
    /// Reads the ambient lines that print to the on-screen log without opening a dialogue window:
    /// unit <b>barks</b> (overhead speech — companion/NPC chatter, reactions) and narrative
    /// <b>game-log messages</b> (the designer GameLog action — "You hear…", quest beats, etc.). Hooks
    /// the game's own EventBus handlers (<see cref="IBarkUIHandler"/>, <see cref="ILogMessageUIHandler"/>),
    /// so it fires whatever the source. NOT the combat roll log — that's a separate thread system.
    ///
    /// Announcements queue (never interrupt) so they don't cut off navigation; an exact-duplicate guard
    /// drops a line that fires twice (a bark can be raised by both the voice component and its log event).
    /// </summary>
    internal sealed class GameLogReader : IBarkUIHandler, ILogMessageUIHandler
    {
        private static GameLogReader _instance;

        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new GameLogReader();
            EventBus.Subscribe(_instance);
        }

        public void HandleOnShowBark(EntityDataBase unit, string text)
        {
            var speaker = (unit as UnitEntityData)?.CharacterName;
            Announce(string.IsNullOrEmpty(speaker) ? text : speaker + ": " + text);
        }

        public void HandleLogMessage(string text) => Announce(text);

        private void Announce(string text)
        {
            if (!Main.Enabled || string.IsNullOrWhiteSpace(text)) return;
            // No dedup: each bark/log line raises its handler exactly once (GameLogEventBark consumes the
            // bark into a log entry rather than re-raising it), so a repeated line is a real repeat.
            Tts.Speak(text, interrupt: false); // passive content — queue behind, don't cut off nav
        }
    }
}
