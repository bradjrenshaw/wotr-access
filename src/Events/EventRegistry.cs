using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using WrathAccess.Settings;
using WrathAccess.Speech;

namespace WrathAccess.Events
{
    /// <summary>
    /// Builds the event settings (the events analogue of <see cref="WrathAccess.UI.Announcements.AnnouncementRegistry"/>).
    /// For every concrete <see cref="ModEvent"/> with an <see cref="EventSettingsAttribute"/>: a category
    /// <c>events.&lt;key&gt;</c>; sourceless events get the output settings directly, sourced events get a
    /// sub-category per applicable source (party/enemy/neutral), each with <c>enabled</c> + a
    /// <c>speech_config</c> picker (Default + the user's additional configs). Built POST-load (so the
    /// config list is known and saved values re-apply), like the overlay/config rosters. The dispatcher
    /// reads these to decide whether/how to read each event.
    /// </summary>
    internal static class EventRegistry
    {
        private static readonly Dictionary<Type, string> _keys = new Dictionary<Type, string>();
        private static readonly EventSources[] SourceOrder = { EventSources.Party, EventSources.Enemy, EventSources.Neutral };

        public static void RegisterDefaults()
        {
            ModSettingsRegistry.EnsureCategory("events", "Events", "category.events");

            Assembly asm = Assembly.GetExecutingAssembly();
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types.Where(t => !t.IsAbstract && typeof(ModEvent).IsAssignableFrom(t)
                                               && t.GetCustomAttribute<EventSettingsAttribute>() != null))
            {
                try { Register(t); }
                catch (Exception e) { Main.Log?.Error("[events] register failed for " + t.Name + ": " + e.Message); }
            }

            ModSettings.Reindex();
            ModSettings.ReapplyUnknown(); // apply values saved before these categories existed
        }

        private static void Register(Type t)
        {
            var attr = t.GetCustomAttribute<EventSettingsAttribute>();
            var key = ToSnake(StripSuffix(t.Name, "Event"));
            _keys[t] = key;

            var cat = ModSettingsRegistry.EnsureCategory("events." + key, "Events/" + attr.Label, "event." + key);

            if (attr.Sources == EventSources.None)
                BuildOutput(cat);
            else
                foreach (var src in SourceOrder)
                {
                    if ((attr.Sources & src) == 0) continue;
                    var name = src.ToString().ToLowerInvariant();
                    var sc = ModSettingsRegistry.EnsureCategory("events." + key + "." + name,
                        "Events/" + attr.Label + "/" + src, "source." + name);
                    BuildOutput(sc);
                }

            // Optional per-event extra settings (custom verbosity etc.), same hook as announcements.
            t.GetMethod("RegisterSettings", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(CategorySetting) }, null)?.Invoke(null, new object[] { cat });
        }

        // One output config: whether to announce, and which speech config to read it through.
        private static void BuildOutput(CategorySetting into)
        {
            if (into.GetByKey("enabled") == null)
                into.Add(new BoolSetting("enabled", "Announce", true, "event.enabled"));
            if (into.GetByKey("speech_config") == null)
                into.Add(new ChoiceSetting("speech_config", "Speech configuration", ConfigChoices(), "default", "event.speech_config"));
        }

        // Default + the user's additional speech configs. Fixed at startup: a config added later needs a
        // restart to appear in these dropdowns (prototype limitation).
        private static List<Choice> ConfigChoices()
        {
            var choices = new List<Choice> { new Choice("default", "Default", "event.config_default") };
            foreach (var id in SpeechConfigRegistry.Ids())
                choices.Add(new Choice(id, SpeechConfigRegistry.Name(id)));
            return choices;
        }

        // ---- dispatcher-side resolution ----

        public static bool Enabled(ModEvent e)
            => ModSettings.GetSetting<BoolSetting>(BasePath(e) + ".enabled")?.Get() ?? true;

        public static string ConfigId(ModEvent e)
            => ModSettings.GetSetting<ChoiceSetting>(BasePath(e) + ".speech_config")?.Current?.Id ?? "default";

        private static string BasePath(ModEvent e)
        {
            var key = _keys.TryGetValue(e.GetType(), out var k) ? k : ToSnake(StripSuffix(e.GetType().Name, "Event"));
            var s = e.Source;
            return s == EventSources.None ? "events." + key : "events." + key + "." + s.ToString().ToLowerInvariant();
        }

        // ---- key derivation (matches AnnouncementRegistry's snake_case) ----

        private static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;

        private static string ToSnake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }
    }
}
