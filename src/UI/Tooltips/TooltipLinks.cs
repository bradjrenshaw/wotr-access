using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingmaker.UI.Common;                          // UIUtility.GetKeysFromLink
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;      // TooltipTemplateGlossary
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>One followable inline glossary link found in an element's text: its visible word and a
    /// factory that builds the glossary/encyclopedia tooltip it points at (resolved live each follow,
    /// per the tooltips-live-not-cached rule).</summary>
    public sealed class LinkTarget
    {
        public string Label { get; }
        public Func<TooltipBaseTemplate> Open { get; }
        public LinkTarget(string label, Func<TooltipBaseTemplate> open) { Label = label; Open = open; }
    }

    /// <summary>
    /// Extracts glossary <c>&lt;link&gt;</c> anchors from a raw (un-stripped) game string and resolves
    /// each to a tooltip. The game's TextTools have already expanded its <c>{...}</c> markup into TMP
    /// <c>&lt;link="ID"&gt;text&lt;/link&gt;</c> by the time we see the text, so we scan that form. Each
    /// link ID → <see cref="UIUtility.GetKeysFromLink"/> (handles multi-key packing and the <c>ui:</c>
    /// prefix, exactly as the game's own gamepad glossary code does) → <see cref="TooltipTemplateGlossary"/>,
    /// which resolves the keys to a <see cref="Kingmaker.Blueprints.Root.Strings.GlossaryEntry"/> or an
    /// encyclopedia page. Links that DON'T resolve through the glossary (the dialogue skill-check kinds —
    /// see the deferred-link-types note) are dropped, so only followable targets become menu entries.
    /// </summary>
    public static class TooltipLinks
    {
        // TMP link tag: <link="ID">visible</link> or <link=ID>visible</link>; visible may hold more tags.
        private static readonly Regex LinkTag = new Regex(
            "<link=\"?(?<id>[^\">]+)\"?>(?<txt>.*?)</link>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        /// <summary>The resolvable glossary links in <paramref name="raw"/>, in document order, de-duped
        /// by link ID. Empty when there are none (the common case) or on any failure.</summary>
        public static List<LinkTarget> Extract(string raw)
        {
            var targets = new List<LinkTarget>();
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("<link", StringComparison.OrdinalIgnoreCase) < 0)
                return targets;

            var seen = new HashSet<string>();
            try
            {
                foreach (Match m in LinkTag.Matches(raw))
                {
                    var id = m.Groups["id"].Value;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id.ToLowerInvariant())) continue;

                    var keys = Keys(id);
                    // Probe resolution now (cheap, static data): only keep links that actually resolve to
                    // a glossary entry or encyclopedia page — others (skill-check links) aren't followable.
                    var probe = new TooltipTemplateGlossary(keys);
                    if (probe.GlossaryEntry == null && probe.Blueprint == null) continue;

                    var visible = TextUtil.StripRichText(m.Groups["txt"].Value);
                    if (string.IsNullOrWhiteSpace(visible)) visible = probe.GlossaryEntry?.Name ?? id;

                    var capturedKeys = keys; // fresh template on each follow, never the probe instance
                    targets.Add(new LinkTarget(visible, () => new TooltipTemplateGlossary(capturedKeys)));
                }
            }
            catch (Exception e) { Main.Log?.Error("TooltipLinks.Extract: " + e.Message); }
            return targets;
        }

        // The game's own ID → keys split (prefix/multi-key handling); fall back to the raw ID.
        private static string[] Keys(string id)
        {
            try
            {
                var k = UIUtility.GetKeysFromLink(id);
                if (k != null && k.Length > 0) return k;
            }
            catch (Exception e) { Main.Log?.Error("TooltipLinks.Keys: " + e.Message); }
            return new[] { id };
        }
    }
}
