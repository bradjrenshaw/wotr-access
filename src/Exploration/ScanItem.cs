using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// One thing the scanner can list: a name, a world position, the categories it belongs to (many-to-
    /// many), and whether the player can currently perceive it. <see cref="Describe"/> composes the
    /// spoken line relative to a reference point. Entity-backed items derive from <see cref="ProxyEntity"/>;
    /// local-map points of interest are <see cref="ProxyMarker"/>.
    /// </summary>
    internal abstract class ScanItem
    {
        public abstract string Name { get; }
        public abstract Vector3 Position { get; }

        /// <summary>The <see cref="ScanTaxonomy"/> leaf node keys this thing belongs to (many-to-many — a
        /// lootable corpse is both "units.enemies" and "containers.corpse"). The scanner buckets it into
        /// each leaf's subcategory list and its parent category's "All" list. Distinct from
        /// <see cref="Primary"/> (the single state-aware node that SOUNDS).</summary>
        public abstract IEnumerable<string> Nodes { get; }

        /// <summary>Only listed when the player could actually know about it (fog/vision). Default: yes.</summary>
        public virtual bool IsVisible => true;

        /// <summary>Can the player see this RIGHT NOW — vs <see cref="IsVisible"/>, which for static
        /// things is reveal-LATCHED like the local map ("we know about it"). Generic fallback: a
        /// fog-texture sample at the position. Entity-backed items override with the game's
        /// per-entity fog state (XZ distance + line of sight, refreshed per frame). Used by the
        /// review cycles ("what's around me now"); the scanner stays area-wide knowledge.</summary>
        public virtual bool CurrentlySeen
        {
            get
            {
                try { return !Kingmaker.Controllers.FogOfWarController.IsInFogOfWar(Position); }
                catch { return true; } // areas without a fog system → no extra filter
            }
        }

        /// <summary>The unit this item represents, for ability targeting (null = target its point instead).</summary>
        public virtual UnitEntityData TargetUnit => null;

        /// <summary>
        /// XZ radius of the thing's footprint, in world units (metres). Large creatures/objects span
        /// several tiles, so the tile view tests footprint-vs-tile overlap, not just the centre point.
        /// Default 0 = a point (markers). Units use their corpulence; map objects their collider bounds.
        /// </summary>
        public virtual float Footprint => 0f;

        /// <summary>The thing's spatial extent. Default: a circle of <see cref="Footprint"/> (a point when
        /// 0). Things with a non-circular shape override — e.g. an exit is the doorway's opening span — so
        /// distance/bearing report the nearest PART of the thing while the cursor still targets its centre.</summary>
        public virtual ScanBounds Bounds
            => Footprint > 0f ? ScanBounds.Circle(Position, Footprint) : ScanBounds.Point(Position);

        /// <summary>Distance from <paramref name="from"/> to the nearest part of the thing (not its centre).</summary>
        public float DistanceTo(Vector3 from) => Geo.Distance(from, Bounds.NearestPoint(from));

        /// <summary>
        /// The PRIMARY taxonomy node (<see cref="SonarTaxonomy"/> key) — the single, state-aware role
        /// this thing sounds as right now (a dead lootable enemy is primary containers.corpse; an exit
        /// door flips doors→exits when opened). Null = not part of the taxonomy at all (sound is then
        /// the user's per-node pick via <see cref="SonarTaxonomy.Resolve"/>; membership for the scanner
        /// stays the full <see cref="Categories"/> set).
        /// </summary>
        public virtual string Primary => null;

        /// <summary>True for a creature/unit (vs a map object or marker). Lets the sonar treat units like
        /// interactables for the object enter/exit cue while leaving plain scenery out.</summary>
        public virtual bool IsUnit => false;

        /// <summary>The per-proxy-type key for announcement overrides ("unit"/"map_object"/"marker"), or
        /// null for the generic default (globals only, no per-type override entries).</summary>
        protected virtual string AnnounceKey => null;

        /// <summary>The identity/state announcement parts, in canonical order, WITHOUT the spatial part
        /// (added by <see cref="Describe"/>). Default: just the name (with the unnamed fallback). Concrete
        /// proxies add type / hp / condition / object-state via <see cref="NameAndType"/>.</summary>
        protected virtual IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            yield return new Announce.NamePart(string.IsNullOrEmpty(Name) ? Loc.T("scan.unnamed") : Name);
        }

        /// <summary>Yield Name (+ Type) honouring the rule: when there's a real name, Name carries it and
        /// Type is a separate part; when there's NO real name the type word becomes the name (so nothing
        /// goes nameless, and we never say the same word twice); with neither, the unnamed fallback.</summary>
        protected IEnumerable<Announce.ScanAnnouncement> NameAndType(string realName, string typeWord)
        {
            if (!string.IsNullOrEmpty(realName))
            {
                yield return new Announce.NamePart(realName);
                if (!string.IsNullOrEmpty(typeWord)) yield return new Announce.TypePart(typeWord);
            }
            else if (!string.IsNullOrEmpty(typeWord)) yield return new Announce.NamePart(typeWord);
            else yield return new Announce.NamePart(Loc.T("scan.unnamed"));
        }

        public string Describe(Vector3 reference)
        {
            var parts = new List<Announce.ScanAnnouncement>(StateParts());
            // Bearing/distance/height to the nearest PART of the thing; coordinates (debug) report the
            // centre (where the cursor would snap).
            parts.Add(new Announce.SpatialPart(reference, Bounds.NearestPoint(reference), Position));
            return Announce.ScanAnnounceComposer.Compose(AnnounceKey, parts);
        }

        /// <summary>The spoken line for the thing itself, no position — for at-cursor announcements
        /// (the cursor is on it, so distance/bearing would be noise).</summary>
        public string DescribeInPlace()
            => Announce.ScanAnnounceComposer.Compose(AnnounceKey, new List<Announce.ScanAnnouncement>(StateParts()));

        /// <summary>
        /// Interact with this item — mirroring the game's click (auto-path + act), driven through the
        /// game's own click handlers/commands (see the interaction-pipeline memory). Returns true if
        /// something was triggered. Base: not interactable (e.g. a raw map marker).
        /// </summary>
        public virtual bool Interact() => false;
    }
}
