using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A named, reusable way to speak — a handler choice plus that handler's params (SAPI rate/volume/
    /// voice, Prism backend), held in a settings subtree. The DEFAULT config is the root Speech settings
    /// (drives all UI/announcement/dialogue speech); the user can add more (advanced) for the events
    /// system to speak specific things through — e.g. enemy damage in a different voice. An event "speaks
    /// through" a config: the config resolves its handler (loading on demand), applies its params, and
    /// speaks or renders.
    /// </summary>
    public sealed class SpeechConfig
    {
        /// <summary>The config's settings subtree: a "handler" choice + one subnode per handler.</summary>
        public CategorySetting Tree { get; }

        // The config this one inherits from (the default config) — null for the default config itself, which
        // is the terminal base. An inheriting config's "inherit" settings fall back to this config's values.
        private readonly SpeechConfig _base;

        public SpeechConfig(CategorySetting tree, SpeechConfig baseConfig = null) { Tree = tree; _base = baseConfig; }

        /// <summary>The chosen OUTPUT id ("auto" / a screen-reader name / "sapi" / "clipboard"). The
        /// default config's output is a plain choice; an inheriting config's is a NullableChoiceSetting
        /// whose EffectiveId already resolves through the base.</summary>
        public string OutputId
        {
            get
            {
                var plain = Tree?.Get<ChoiceSetting>("output");
                if (plain != null) return plain.Current?.Id ?? SpeechOutputs.Auto;
                var nullable = Tree?.Get<NullableChoiceSetting>("output");
                return nullable?.EffectiveId ?? SpeechOutputs.Auto;
            }
        }

        // The resolved, loaded handler for this config's output (load-on-demand + fallback via
        // SpeechManager; the output→engine mapping is SpeechOutputs' routing table).
        private ISpeechHandler Handler => SpeechManager.ResolveHandler(SpeechOutputs.HandlerKeyFor(OutputId));

        /// <summary>The engine key the output currently resolves to ("prism"/"sapi"/"clipboard") — what
        /// the settings UI uses to show only the params that apply (Auto resolving to SAPI on a
        /// screen-reader-less machine correctly reveals the SAPI settings).</summary>
        public string ResolvedHandlerKey => Handler?.Key;

        // The handler's params for this config: the config's own subtree — the default config's raw,
        // an inheriting config's RESOLVED snapshot (explicit override else the base's value), so the
        // handler reads plain settings and never sees "inherit". Prism additionally gets the backend
        // pick synthesized from the OUTPUT (the output IS the backend), merged with a VALUE SNAPSHOT
        // of the user params — real settings must never be re-parented out of the saved tree
        // (FullPath rides Parent), and the handler applies-on-change by value so a fresh snapshot
        // per speak is correct and cheap.
        private CategorySetting Params(ISpeechHandler h)
        {
            if (h == null) return null;
            var mine = Tree?.Get<CategorySetting>(h.Key);
            var resolved = _base == null ? mine : Resolve(mine, _base.Tree?.Get<CategorySetting>(h.Key), h);
            if (h.Key != "prism") return resolved;

            string backend = SpeechOutputs.IsScreenReader(OutputId) ? OutputId : SpeechOutputs.Auto;
            var cat = new CategorySetting("prism", "Prism");
            cat.Add(new ChoiceSetting("backend", "Backend", SpeechOutputs.BackendChoiceList(backend), backend));
            if (resolved != null)
                foreach (var c in resolved.Children)
                    switch (c)
                    {
                        case IntSetting i:
                            cat.Add(new IntSetting(i.Key, i.Label, i.Get(), i.Min, i.Max, i.Step));
                            break;
                        case ChoiceSetting ch:
                            cat.Add(new ChoiceSetting(ch.Key, ch.Label, ch.Choices, ch.ValueId));
                            break;
                    }
            return cat;
        }

        // Merge an inheriting config's param subtree over the base's: walk the base (canonical) subtree and,
        // per setting, take the explicit override (nullable setting overridden) or the base's value.
        private static CategorySetting Resolve(CategorySetting mine, CategorySetting baseSub, ISpeechHandler h)
        {
            if (baseSub == null) return mine; // nothing to inherit from
            var resolved = new CategorySetting(h.Key, h.Label);
            foreach (var b in baseSub.Children)
            {
                switch (b)
                {
                    case IntSetting bi:
                        int v = mine?.Get<NullableIntSetting>(bi.Key) is NullableIntSetting ni && ni.IsOverridden
                            ? ni.LocalValue.Value : bi.Get();
                        resolved.Add(new IntSetting(bi.Key, bi.Label, v, bi.Min, bi.Max, bi.Step, bi.LocalizationKey));
                        break;
                    case ChoiceSetting bc:
                        var mc = mine?.Get<NullableChoiceSetting>(bc.Key);
                        string id = mc != null && mc.IsOverridden ? mc.LocalValue : bc.ValueId;
                        resolved.Add(new ChoiceSetting(bc.Key, bc.Label, bc.Choices, id, bc.LocalizationKey));
                        break;
                }
            }
            return resolved;
        }

        /// <summary>Can speech through this config be positioned in the world (handler renders to PCM)?
        /// SAPI yes; Prism when the bound backend implements speak-to-memory (OneCore — screen readers
        /// don't); clipboard no. The "use positional" CHOICE is a separate, event-side option.</summary>
        public bool SupportsPositional => Handler?.SupportsAudioRender ?? false;

        public bool Speak(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Speak(text, interrupt, Params(h));
        }

        public bool Output(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Output(text, interrupt, Params(h));
        }

        public bool Silence()
        {
            var h = Handler;
            return h != null && h.Silence();
        }

        /// <summary>Render through this config's handler for world-positioned playback, applying its
        /// voice. Falls back to any render-capable handler when this config's can't render.</summary>
        public SpeechAudio RenderToAudio(string text)
        {
            // Try the config's own handler first — its render call applies the config (Prism binds the
            // config's backend there, so a stale SupportsAudioRender must not pre-filter) and returns
            // null when it genuinely can't render — then fall back to any render-capable handler.
            var h = Handler;
            var audio = h?.RenderToAudio(text, Params(h));
            return audio ?? SpeechManager.RenderToAudioFallback(text);
        }
    }
}
