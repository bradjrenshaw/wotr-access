using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // ILocalMapMarker, LocalMapMarkType
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A local-map point of interest (<see cref="ILocalMapMarker"/>) — the game's own curated markers,
    /// already fog/reveal-filtered, subcategorized by their designer-assigned
    /// <see cref="LocalMapMarkType"/>: Quest (VeryImportantThing), Exits, Loot, People (named units the
    /// map highlights), Other (generic Poi). Party/self/destination markers never become items at all
    /// (<see cref="IsNoise"/> — they duplicate the party or are transient order flags).
    /// </summary>
    internal sealed class ProxyMarker : ScanItem
    {
        private readonly ILocalMapMarker _marker;

        public ProxyMarker(ILocalMapMarker marker) { _marker = marker; }

        /// <summary>Marker types that shouldn't be scannable things: the party's own map dots and the
        /// transient move-destination flag.</summary>
        public static bool IsNoise(ILocalMapMarker m)
        {
            switch (m.GetMarkerType())
            {
                case LocalMapMarkType.PlayerCharacter:
                case LocalMapMarkType.CompanionPortrait:
                case LocalMapMarkType.DestinationMark:
                    return true;
                default: return false;
            }
        }

        public override string Name
        {
            get
            {
                var d = _marker.GetDescription();
                return string.IsNullOrEmpty(d) ? _marker.GetMarkerType().ToString() : d;
            }
        }

        public override Vector3 Position => _marker.GetPosition();

        public override bool IsVisible => _marker.IsVisible();

        public override IEnumerable<string> Nodes { get { yield return Node(); } }

        public override string Primary => Node(); // silent by default; assignable per subcategory in Sounds

        private string Node()
        {
            switch (_marker.GetMarkerType())
            {
                case LocalMapMarkType.VeryImportantThing: return "poi.quest";
                case LocalMapMarkType.Exit: return "poi.exits";
                case LocalMapMarkType.Loot: return "poi.loot";
                case LocalMapMarkType.Unit: return "poi.units";
                default: return "poi.other";
            }
        }

        // Announce-node = Primary (poi); marker part-set (name + location only).
        // Name only (the game's curated, localized description) + spatial. The marker KIND (an unlocalized
        // enum) was a dev aid the old line tacked on — and double-spoke when there was no description; we
        // drop it rather than ship a raw enum word. (A localized kind/type could be added later.)
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            foreach (var p in NameAndType(_marker.GetDescription(), null)) yield return p;
        }
    }
}
