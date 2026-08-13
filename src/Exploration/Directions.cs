using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Renders a world bearing (degrees, 0 = north = +Z, 90 = east) in the user's chosen direction
    /// style — the "Direction type" dropdown beside the spatial announcement sub-toggles. The compass
    /// styles are WORLD-aligned (8- or 16-wind, as full words or spoken-letter short forms like
    /// "n ne"); the relative and clock-face styles are EGOCENTRIC, measured from the listener's
    /// facing (Q/E — the cursor's orientation), for players who turn away from north and want the
    /// readouts to follow.
    /// </summary>
    internal static class Directions
    {
        public const string DefaultMode = "compass8";

        // The 16-wind ring: abbreviations (the short-form loc key tails, spoken as letters) and the
        // full-word loc keys. The 8-wind ring is every second entry (index * 2).
        private static readonly string[] Abbr16 =
            { "n", "nne", "ne", "ene", "e", "ese", "se", "sse", "s", "ssw", "sw", "wsw", "w", "wnw", "nw", "nnw" };
        private static readonly string[] Full16 =
            { "geo.north", "geo.nne", "geo.northeast", "geo.ene", "geo.east", "geo.ese", "geo.southeast", "geo.sse",
              "geo.south", "geo.ssw", "geo.southwest", "geo.wsw", "geo.west", "geo.wnw", "geo.northwest", "geo.nnw" };
        private static readonly string[] Rel4 = { "ahead", "right", "behind", "left" };
        private static readonly string[] Rel8 =
            { "ahead", "ahead_right", "right", "behind_right", "behind", "behind_left", "left", "ahead_left" };

        private static string Mode =>
            ModSettings.GetSetting<ChoiceSetting>("proxy_announce.spatial.direction_type")?.ValueId ?? DefaultMode;

        /// <summary>A BEARING (where something lies from the reference) in the chosen style.</summary>
        public static string Word(float worldDeg)
        {
            switch (Mode)
            {
                case "compass16": return Loc.T(Full16[Sector(worldDeg, 16)]);
                case "compass8short": return Loc.T("geo.short." + Abbr16[Sector(worldDeg, 8) * 2]);
                case "compass16short": return Loc.T("geo.short." + Abbr16[Sector(worldDeg, 16)]);
                case "relative4": return Loc.T("geo.rel." + Rel4[Sector(Egocentric(worldDeg), 4)]);
                case "relative8": return Loc.T("geo.rel." + Rel8[Sector(Egocentric(worldDeg), 8)]);
                case "clock":
                {
                    int hour = Mathf.RoundToInt(Normalize(Egocentric(worldDeg)) / 30f) % 12;
                    return Loc.T("geo.clock", new { hour = hour == 0 ? 12 : hour });
                }
                default: return Loc.T(Full16[Sector(worldDeg, 8) * 2]); // compass8
            }
        }

        /// <summary>A FACING (which way something points — the listener, the camera) in the chosen
        /// style's COMPASS flavor. Facings are absolute by nature ("facing 10 o'clock" of itself is
        /// meaningless), so the relative/clock styles fall back to the plain 8-wind compass.</summary>
        public static string CompassWord(float worldDeg)
        {
            switch (Mode)
            {
                case "compass16": return Loc.T(Full16[Sector(worldDeg, 16)]);
                case "compass8short": return Loc.T("geo.short." + Abbr16[Sector(worldDeg, 8) * 2]);
                case "compass16short": return Loc.T("geo.short." + Abbr16[Sector(worldDeg, 16)]);
                default: return Loc.T(Full16[Sector(worldDeg, 8) * 2]);
            }
        }

        // Rotate a world bearing into the listener's frame (0 = dead ahead of the facing).
        private static float Egocentric(float worldDeg) => worldDeg - ListenerFrame.Facing;

        // Which of n equal sectors (centred on the sector directions) the bearing falls in.
        private static int Sector(float deg, int n)
            => Mathf.RoundToInt(Normalize(deg) / (360f / n)) % n;

        private static float Normalize(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }
    }
}
