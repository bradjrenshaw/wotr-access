using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Announce
{
    /// <summary>
    /// Builds the scan-announcement settings (the proxy parallel to <c>AnnouncementRegistry</c>, kept
    /// separate so scan items needn't be UI elements). Two trees under the settings Root:
    /// <list type="bullet">
    /// <item><c>proxy_announce.{part}</c> — global per-part settings (an <c>enabled</c> toggle, plus the
    /// spatial part's distance/direction/height/coordinates sub-toggles).</item>
    /// <item><c>proxy_elem.{element}.{part}</c> — per-proxy-type tri-state overrides (NullableBool
    /// inheriting the matching global) for the parts that element actually emits.</item>
    /// </list>
    /// The set is small and fixed, so it's declared explicitly (no reflection). Registered pre-load with
    /// the other static categories, so persistence restores it normally.
    /// </summary>
    internal static class ScanAnnounceRegistry
    {
        private struct Opt { public string Key; public bool Def; public Opt(string k, bool d) { Key = k; Def = d; } }

        // Each part and the settings it carries.
        private static readonly (string part, Opt[] opts)[] Parts =
        {
            ("name",         new[] { new Opt("enabled", true) }),
            ("type",         new[] { new Opt("enabled", true) }),
            ("hp",           new[] { new Opt("enabled", true) }),
            ("condition",    new[] { new Opt("enabled", true) }),
            ("object_state", new[] { new Opt("enabled", true) }),
            ("spatial",      new[] { new Opt("enabled", true), new Opt("distance", true),
                                     new Opt("direction", true), new Opt("height", true),
                                     new Opt("coordinates", false) }),
        };

        // Which parts each proxy type emits → which override entries to build.
        private static readonly (string element, string[] parts)[] Elements =
        {
            ("unit",       new[] { "name", "type", "hp", "condition", "spatial" }),
            ("map_object", new[] { "name", "type", "object_state", "spatial" }),
            ("marker",     new[] { "name", "spatial" }),
        };

        // English fallbacks (the real labels come from the "settings" locale table by these keys).
        private static readonly Dictionary<string, string> PartLabel = new Dictionary<string, string>
        {
            { "name", "Name" }, { "type", "Type" }, { "hp", "Health" }, { "condition", "Condition" },
            { "object_state", "Object state" }, { "spatial", "Location" },
        };
        private static readonly Dictionary<string, string> OptLabel = new Dictionary<string, string>
        {
            { "enabled", "Announce" }, { "distance", "Distance" }, { "direction", "Direction" },
            { "height", "Height difference" }, { "coordinates", "Coordinates (debug)" },
        };
        private static readonly Dictionary<string, string> ElementLabel = new Dictionary<string, string>
        {
            { "unit", "Unit" }, { "map_object", "Map object" }, { "marker", "Marker" },
        };

        public static void RegisterDefaults()
        {
            // Globals, remembering each created BoolSetting so the overrides can inherit it.
            var globals = new Dictionary<string, BoolSetting>(); // "part.setting" -> global
            foreach (var (part, opts) in Parts)
            {
                var cat = ModSettingsRegistry.EnsureCategory(
                    "proxy_announce." + part, "Scan announcements/" + PartLabel[part], "/proxyann." + part);
                foreach (var o in opts)
                {
                    if (cat.GetByKey(o.Key) == null)
                        cat.Add(new BoolSetting(o.Key, OptLabel[o.Key], o.Def, "proxyann." + o.Key));
                    globals[part + "." + o.Key] = cat.Get<BoolSetting>(o.Key);
                }
            }

            // Per-proxy-type overrides (only the parts that type emits).
            foreach (var (element, parts) in Elements)
            {
                foreach (var part in parts)
                {
                    var cat = ModSettingsRegistry.EnsureCategory(
                        "proxy_elem." + element + "." + part,
                        "Scan overrides/" + ElementLabel[element] + "/" + PartLabel[part],
                        "/proxyelem." + element + "/proxyann." + part);
                    foreach (var o in OptsFor(part))
                    {
                        if (cat.GetByKey(o.Key) != null) continue;
                        cat.Add(new NullableBoolSetting(o.Key, OptLabel[o.Key],
                            globals[part + "." + o.Key], "proxyann." + o.Key));
                    }
                }
            }
        }

        private static Opt[] OptsFor(string part)
        {
            foreach (var p in Parts) if (p.part == part) return p.opts;
            return new Opt[0];
        }
    }
}
