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

        // ---- persistence (for BindingSetting) ----

        /// <summary>Stable kind tag used to pick a deserializer, e.g. "keyboard".</summary>
        public abstract string Type { get; }

        /// <summary>Serialize this binding's data; round-trips through <see cref="Deserialize"/>.</summary>
        public abstract string Serialize();

        /// <summary>Rebuild a binding from a (type, data) pair, or null if the type is unknown/invalid.</summary>
        public static InputBinding Deserialize(string type, string data)
        {
            switch (type)
            {
                case "keyboard": return KeyboardBinding.Deserialize(data);
                default: return null;
            }
        }
    }
}
