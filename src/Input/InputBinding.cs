namespace WrathAccess.Input
{
    /// <summary>
    /// Base for a single key/button combo. Phase queries are polled each frame by
    /// InputManager; controller bindings can be added later as a sibling.
    /// </summary>
    public abstract class InputBinding
    {
        /// <summary>Human-readable combo, e.g. "Ctrl+Shift+A".</summary>
        public abstract string DisplayName { get; }

        public abstract bool JustPressed();
        public abstract bool Held();
        public abstract bool Released();
    }
}
