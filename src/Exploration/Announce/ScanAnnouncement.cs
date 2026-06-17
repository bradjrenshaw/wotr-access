using WrathAccess.Settings; // ModSettings, BoolSetting, NullableBoolSetting

namespace WrathAccess.Exploration.Announce
{
    /// <summary>
    /// One addressable piece of a scan item's spoken line (name, type, hp, condition, object state,
    /// spatial). The proxy parallel to the UI <c>Announcement</c> family — deliberately NOT the same
    /// pipeline (scan items aren't focusable UI elements), but the same shape: each part has a stable
    /// <see cref="Key"/> used for its enable/sub-toggle settings, carries its own data, and renders to a
    /// <see cref="Message"/> (empty ⇒ self-skips). Composed by <see cref="ScanAnnounceComposer"/>.
    /// </summary>
    internal abstract class ScanAnnouncement
    {
        /// <summary>Settings key (e.g. "name", "spatial") — matches the registry's part keys.</summary>
        public abstract string Key { get; }

        /// <summary>Rendered text. <see cref="ScanAnnounceContext"/> resolves per-part sub-toggles.</summary>
        public abstract Message Render(ScanAnnounceContext ctx);
    }

    /// <summary>
    /// Resolves a scan announcement's settings with the per-proxy-type cascade: a per-element override
    /// (<c>proxy_elem.{element}.{part}.{setting}</c>) when explicitly set, else the global
    /// (<c>proxy_announce.{part}.{setting}</c>), else the code default. <paramref name="elementKey"/> null
    /// ⇒ globals only (the generic ScanItem default, which declares no element identity).
    /// </summary>
    internal sealed class ScanAnnounceContext
    {
        private readonly string _element;
        public ScanAnnounceContext(string elementKey) { _element = elementKey; }

        public bool ResolveBool(string part, string setting, bool def)
        {
            if (!string.IsNullOrEmpty(_element))
            {
                var ov = ModSettings.GetSetting<NullableBoolSetting>(
                    "proxy_elem." + _element + "." + part + "." + setting);
                if (ov != null && ov.IsOverridden) return ov.LocalValue.Value;
            }
            var global = ModSettings.GetSetting<BoolSetting>("proxy_announce." + part + "." + setting);
            return global != null ? global.Get() : def;
        }
    }
}
