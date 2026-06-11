namespace WrathAccess.Input
{
    /// <summary>
    /// The input layer an action belongs to. Screens declare which categories they use
    /// (<see cref="WrathAccess.Screens.Screen.InputCategories"/>, in priority order) and only the
    /// TOP screen's declaration is live — plus <see cref="Global"/>, which is always on. Within the
    /// live declaration, an identical chord bound in two categories resolves to the earlier-declared
    /// one (shadowing): the in-game screen flips [UI, Exploration] ↔ [Exploration, UI] with HUD focus,
    /// so the same arrows navigate the HUD when focused and move the world cursor when not — while
    /// chords only one category binds (Shift+arrows, scanner keys) work in both states. Conflict
    /// prevention therefore only applies WITHIN a category (the rebind capture steals).
    /// </summary>
    public enum InputCategory
    {
        /// <summary>Always live, even when focus mode is off (focus toggle, mod menu, quick save).</summary>
        Global,
        /// <summary>Screen/menu navigation — live when the focused screen declares it.</summary>
        UI,
        /// <summary>The in-game world: cursor movement, interaction, scanner, overlays, party, combat.</summary>
        Exploration,
    }
}
