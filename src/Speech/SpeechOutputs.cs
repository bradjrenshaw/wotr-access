using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The user-facing OUTPUT METHODS a speech config can pick — "who speaks": Auto (your screen
    /// reader), each screen reader Prism detects on this machine (NVDA, JAWS, …), SAPI, Clipboard.
    /// This replaced the handler-per-library model (2026-07-22): "Prism vs SAPI vs Clipboard" was our
    /// implementation org chart leaking into the UI, and Prism's own SAPI backend (very slow) sat
    /// beside our fast manual-COM one under the same name. Prism remains the ENGINE for the
    /// screen-reader outputs; the SAPI output routes to <see cref="SapiHandler"/> exclusively —
    /// Prism's SAPI-ish backends are filtered out of the list and can no longer be chosen.
    /// </summary>
    internal static class SpeechOutputs
    {
        public const string Auto = "auto";
        public const string Sapi = "sapi";
        public const string Clipboard = "clipboard";

        private static List<Choice> _choices;
        // Synthesized per-output Prism params ({"backend": <name>}) — the handler contract passes a
        // params subtree per speak; these stand in for the per-config backend setting that no longer
        // exists. Cached: they're immutable and per-utterance allocation is off-limits.
        private static readonly Dictionary<string, CategorySetting> _prismParams
            = new Dictionary<string, CategorySetting>();

        /// <summary>The pickable outputs, availability-filtered for this machine: Auto, then Prism's
        /// detected screen readers (SAPI-ish backends excluded — ours supersedes them), SAPI, Clipboard.
        /// Enumerated once (the Prism registry probe is expensive).</summary>
        public static List<Choice> Choices()
        {
            if (_choices != null) return _choices;
            var choices = new List<Choice> { new Choice(Auto, "Auto (your screen reader)", "speech.output.auto") };
            foreach (var name in PrismHandler.ProbeBackendNames())
            {
                if (name.IndexOf("sapi", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                choices.Add(new Choice(name, name)); // product names — not translated
            }
            choices.Add(new Choice(Sapi, "SAPI", "speech.sapi"));
            choices.Add(new Choice(Clipboard, "Clipboard", "speech.clipboard"));
            return _choices = choices;
        }

        /// <summary>Is this output id a Prism screen-reader backend (as opposed to auto/sapi/clipboard)?</summary>
        public static bool IsScreenReader(string outputId)
            => !string.IsNullOrEmpty(outputId) && outputId != Auto && outputId != Sapi && outputId != Clipboard;

        /// <summary>The handler KEY an output routes through — the routing table of the whole model.</summary>
        public static string HandlerKeyFor(string outputId)
        {
            if (outputId == Sapi) return "sapi";
            if (outputId == Clipboard) return "clipboard";
            if (IsScreenReader(outputId)) return "prism";
            return Auto; // auto = SpeechManager's detect-chain (prism first)
        }

        /// <summary>The Prism params to pass for an output: a fixed backend pick for a named screen
        /// reader, backend-auto for Auto, null for non-Prism outputs.</summary>
        public static CategorySetting PrismParamsFor(string outputId)
        {
            string backend = IsScreenReader(outputId) ? outputId : outputId == Auto ? "auto" : null;
            if (backend == null) return null;
            if (_prismParams.TryGetValue(backend, out var cached)) return cached;
            var cat = new CategorySetting("prism", "Prism");
            cat.Add(new ChoiceSetting("backend", "Backend",
                new List<Choice> { new Choice(backend, backend) }, backend));
            return _prismParams[backend] = cat;
        }

        /// <summary>Map a legacy handler/backend pair (pre-refactor saved settings) to an output id.</summary>
        public static string FromLegacy(string handler, string prismBackend)
        {
            switch (handler)
            {
                case "sapi": return Sapi;
                case "clipboard": return Clipboard;
                case "prism":
                    // A specific screen-reader backend carries over if it still exists (and isn't a
                    // SAPI-ish backend, which we no longer offer); anything else becomes Auto.
                    if (!string.IsNullOrEmpty(prismBackend) && prismBackend != "auto")
                        foreach (var c in Choices())
                            if (c.Id == prismBackend) return prismBackend;
                    return Auto;
                default: return Auto;
            }
        }
    }
}
