using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingmaker.ResourceLinks; // EquipmentEntityLink
using Kingmaker.Visual.CharacterSystem; // EquipmentEntity, BodyPartType
using UnityEngine;
using WrathAccess.Localization;

namespace WrathAccess.Screens
{
    /// <summary>
    /// What the appearance phase SAYS about the game's unnamed options. The game titles every shape
    /// "Face 3" / "Hairstyle 7" and every colour is a bare swatch, so this layer supplies:
    /// <list type="bullet">
    /// <item><b>Descriptions</b> — authored prose per equipment-entity asset, keyed by the asset's name
    /// (shared across races that reuse an asset) in the <c>appearance</c> locale table
    /// (<c>assets/locale/&lt;lang&gt;/appearance.json</c>, key <c>desc.&lt;asset name&gt;</c>). Absent → nothing
    /// extra is said; the game's title still reads.</item>
    /// <item><b>Regions</b> — where a paint / tattoo / scar lands, read off the asset's body parts (the
    /// data the game paints with), localized per part type.</item>
    /// <item><b>Colour names</b> — a ramp texture's hue from its name (<c>CR_Armor_Brown3</c>,
    /// <c>CR_Skin_Pale_U_EL</c>) and its lightness from sampling the ramp through a render-texture copy
    /// (the textures aren't CPU-readable), cached per texture name.</item>
    /// </list>
    /// </summary>
    internal static class AppearanceCatalog
    {
        public const string Table = "appearance";

        // ---- descriptions ----

        /// <summary>The authored description for an asset, or null when none is authored yet.</summary>
        public static string Describe(EquipmentEntityLink link)
        {
            var name = AssetName(link);
            return name == null ? null : LocalizationManager.GetOrDefault(Table, "desc." + name, null);
        }

        /// <summary>The authored description for a race body preset, or null.</summary>
        public static string DescribePreset(Kingmaker.Blueprints.CharGen.BlueprintRaceVisualPreset preset)
            => preset == null ? null : LocalizationManager.GetOrDefault(Table, "desc." + preset.name, null);

        /// <summary>The asset (equipment entity) name an option is keyed by, or null.</summary>
        public static string AssetName(EquipmentEntityLink link)
        {
            try { return link?.Load()?.name; } catch { return null; }
        }

        /// <summary>The game's "no paint" / "no scar" placeholder entities.</summary>
        public static bool IsEmpty(EquipmentEntityLink link)
        {
            var n = AssetName(link);
            return n == null || n.IndexOf("EMPTY", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- regions ----

        /// <summary>The body regions an asset paints, as one localized phrase ("upper arms, forearms and
        /// hands"), or null when it has none.</summary>
        public static string Regions(EquipmentEntityLink link)
        {
            EquipmentEntity ee;
            try { ee = link?.Load(); } catch { return null; }
            if (ee == null || ee.BodyParts == null || ee.BodyParts.Count == 0) return null;
            var words = new List<string>();
            foreach (var bp in ee.BodyParts)
            {
                if (bp == null) continue;
                var w = RegionWord(bp.Type);
                if (!string.IsNullOrEmpty(w) && !words.Contains(w)) words.Add(w);
            }
            return words.Count == 0 ? null : string.Join(", ", words.ToArray());
        }

        private static string RegionWord(BodyPartType type)
            => LocalizationManager.GetOrDefault(Table, "region." + type, SplitCamel(type.ToString()).ToLowerInvariant());

        // ---- colour names ----

        // Per texture name: the sampled colour (a ramp runs dark → light along x; the blend of its mid
        // and lit samples is the colour as worn). The textures aren't CPU-readable, so one render-texture
        // copy per ramp, then cached for the session.
        private sealed class RampInfo { public string Hue; public string SkinWord; public Color Colour; public float Light; public bool Sampled; }
        private static readonly Dictionary<string, RampInfo> _ramps = new Dictionary<string, RampInfo>();
        private static readonly Dictionary<string, string[]> _listNames = new Dictionary<string, string[]>();
        private static readonly Regex ArmorRamp = new Regex(@"^CR_Armor_([A-Za-z]+?)(\d*)$", RegexOptions.Compiled);
        private static readonly Regex SkinRamp = new Regex(@"^CR_Skin_([A-Za-z]+)_U_", RegexOptions.Compiled);

        /// <summary>Spoken names for a whole swatch list, UNIQUE within the list. Ramps are grouped by
        /// family (the hue from the name, or the skin word) and each family's members are ranked by sampled
        /// lightness; a family of up to seven gets evenly spread shade words (light / medium / dark, with
        /// "very" and "medium light" as it grows), a lone member a shade only when it is extreme, a larger
        /// family a number from light to dark. <paramref name="context"/> ("hair", "eyes", …) lets a hue
        /// take a context word ("blond" for a hair Yellow). Cached per list.</summary>
        public static string[] ColourNames(IList<Texture2D> ramps, string context)
        {
            if (ramps == null || ramps.Count == 0) return new string[0];
            var sb = new System.Text.StringBuilder(context ?? "");
            foreach (var r in ramps) sb.Append('|').Append(r != null ? r.name : "");
            string key = sb.ToString();
            if (_listNames.TryGetValue(key, out var cached)) return cached;

            var infos = new RampInfo[ramps.Count];
            var names = new string[ramps.Count];
            var families = new Dictionary<string, List<int>>();
            for (int i = 0; i < ramps.Count; i++)
            {
                infos[i] = Info(ramps[i]);
                string fam = Family(infos[i]);
                if (fam == null) { names[i] = ramps[i] != null ? ramps[i].name : Loc.T("value.blank"); continue; }
                if (!families.TryGetValue(fam, out var g)) families[fam] = g = new List<int>();
                g.Add(i);
            }
            foreach (var pair in families)
            {
                var g = pair.Value;
                g.Sort((a, b) => infos[b].Light.CompareTo(infos[a].Light)); // lightest first
                string word = FamilyWord(infos[g[0]], context);
                bool shaded = infos[g[0]].SkinWord == null && infos[g[0]].Hue != "Black" && infos[g[0]].Hue != "White";
                if (g.Count == 1)
                {
                    var info = infos[g[0]];
                    int step = !shaded || !info.Sampled ? 0 : info.Light > 0.78f ? 1 : info.Light > 0.58f ? 2 : info.Light < 0.18f ? 7 : info.Light < 0.36f ? 6 : 0;
                    names[g[0]] = step == 0 ? word : Shade(step, word);
                }
                else if (shaded && g.Count <= 7)
                {
                    int[] steps = Spread[g.Count];
                    for (int n = 0; n < g.Count; n++) names[g[n]] = Shade(steps[n], word);
                }
                else if (infos[g[0]].Hue == "White" && g.Count <= 3)
                {
                    // Whites differ only in brightness: bright white / white / off-white (two → the outer pair).
                    int[] steps = g.Count == 2 ? new[] { 1, 3 } : new[] { 1, 2, 3 };
                    for (int n = 0; n < g.Count; n++)
                        names[g[n]] = LocalizationManager.GetOrDefault(Table, "white." + steps[n], word);
                }
                else if (g.Count == 2)
                {
                    names[g[0]] = LocalizationManager.GetOrDefault(Table, "shade.lighter", "lighter {name}").Replace("{name}", word);
                    names[g[1]] = LocalizationManager.GetOrDefault(Table, "shade.darker", "darker {name}").Replace("{name}", word);
                }
                else
                {
                    for (int n = 0; n < g.Count; n++)
                        names[g[n]] = LocalizationManager.GetOrDefault(Table, "shade.nth", "{name} {n}")
                            .Replace("{name}", word).Replace("{n}", (n + 1).ToString());
                }
            }
            _listNames[key] = names;
            return names;
        }

        // Shade steps 1 (very light) … 7 (very dark), spread evenly for a family of N.
        private static readonly int[][] Spread =
        {
            null, null, new[] { 2, 6 }, new[] { 2, 4, 6 }, new[] { 1, 2, 6, 7 }, new[] { 1, 2, 4, 6, 7 },
            new[] { 1, 2, 3, 5, 6, 7 }, new[] { 1, 2, 3, 4, 5, 6, 7 },
        };

        private static string Shade(int step, string hueWord)
            => LocalizationManager.GetOrDefault(Table, "shade." + step, "{hue}").Replace("{hue}", hueWord);

        private static string Family(RampInfo info)
        {
            if (info.SkinWord != null) return "skin:" + info.SkinWord;
            var hue = info.Hue ?? (info.Sampled ? HueFromSample(info.Colour) : null);
            return hue == null ? null : "hue:" + hue;
        }

        private static string FamilyWord(RampInfo info, string context)
        {
            if (info.SkinWord != null)
                return LocalizationManager.GetOrDefault(Table, "skin." + info.SkinWord, SplitCamel(info.SkinWord).ToLowerInvariant());
            string hue = info.Hue ?? (info.Sampled ? HueFromSample(info.Colour) : "");
            string w = context != null ? LocalizationManager.GetOrDefault(Table, "hue." + context + "." + hue, null) : null;
            return w ?? LocalizationManager.GetOrDefault(Table, "hue." + hue, hue.ToLowerInvariant());
        }

        /// <summary>One ramp's name on its own (no list to disambiguate against).</summary>
        public static string ColourName(Texture2D ramp, string context = null)
            => ramp == null ? Loc.T("value.blank") : BaseName(ramp, Info(ramp), context);

        private static RampInfo Info(Texture2D ramp)
        {
            if (ramp == null) return new RampInfo { Light = 0.5f };
            if (_ramps.TryGetValue(ramp.name, out var info)) return info;
            info = new RampInfo();
            var m = SkinRamp.Match(ramp.name);
            if (m.Success) info.SkinWord = m.Groups[1].Value;
            m = ArmorRamp.Match(ramp.name);
            if (m.Success) info.Hue = m.Groups[1].Value;
            var c = Sample(ramp);
            if (c.HasValue) { info.Colour = c.Value; info.Light = c.Value.grayscale; info.Sampled = true; }
            else info.Light = 0.5f;
            _ramps[ramp.name] = info;
            return info;
        }

        // A lone ramp (no list to rank against): its family word, shaded only when extreme.
        private static string BaseName(Texture2D ramp, RampInfo info, string context)
        {
            string w = FamilyWord(info, context);
            bool shaded = info.SkinWord == null && info.Hue != "Black" && info.Hue != "White" && info.Sampled;
            int step = !shaded ? 0 : info.Light > 0.78f ? 1 : info.Light > 0.58f ? 2 : info.Light < 0.18f ? 7 : info.Light < 0.36f ? 6 : 0;
            return step == 0 ? w : Shade(step, w);
        }

        private static Color? Sample(Texture2D ramp)
        {
            try
            {
                var rt = RenderTexture.GetTemporary(256, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                Graphics.Blit(ramp, rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 256, 1), 0, 0);
                tex.Apply();
                var mid = tex.GetPixel(128, 0);
                var lit = tex.GetPixel(224, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex);
                return Color.Lerp(mid, lit, 0.5f);
            }
            catch (Exception e)
            {
                Main.Log?.Log("[appearance] ramp sample failed for " + ramp.name + ": " + e.Message);
                return null;
            }
        }

        // No hue in the name: the hue family of the sampled colour (grey when unsaturated).
        private static string HueFromSample(Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            if (sat < 0.12f) return v < 0.2f ? "Black" : v > 0.85f ? "White" : "Gray";
            if (h < 0.04f || h >= 0.95f) return "Red";
            if (h < 0.10f) return "Orange";
            if (h < 0.18f) return "Yellow";
            if (h < 0.43f) return "Green";
            if (h < 0.52f) return "Turquoise";
            if (h < 0.60f) return "Cyan";
            if (h < 0.72f) return "Blue";
            if (h < 0.80f) return "Violet";
            return "Purple";
        }

        private static string SplitCamel(string s)
            => Regex.Replace(s ?? "", "(?<=[a-z])(?=[A-Z])", " ");
    }
}
