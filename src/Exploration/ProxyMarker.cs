using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // ILocalMapMarker
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A local-map point of interest (<see cref="ILocalMapMarker"/>) — the game's own curated markers
    /// (exits, loot, quest things, named units, …), already fog/reveal-filtered. Kept in a single Points
    /// of Interest category for now so we can compare it against the entity-derived categories.
    /// </summary>
    internal sealed class ProxyMarker : ScanItem
    {
        private readonly ILocalMapMarker _marker;

        public ProxyMarker(ILocalMapMarker marker) { _marker = marker; }

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

        public override IEnumerable<ScanCategory> Categories { get { yield return ScanCategory.PointsOfInterest; } }

        public override string Primary => SonarTaxonomy.Poi; // silent by default; assignable in Sounds

        // The marker's own kind (exit/loot/poi/unit…) — handy while comparing against the entity data.
        protected override string Extra => _marker.GetMarkerType().ToString().ToLowerInvariant();
    }
}
