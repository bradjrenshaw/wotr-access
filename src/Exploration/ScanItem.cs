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
        public abstract IEnumerable<ScanCategory> Categories { get; }

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

        /// <summary>Subtype state folded into the spoken line (HP, "locked", marker type, …), or null.</summary>
        protected virtual string Extra => null;

        public string Describe(Vector3 reference)
        {
            var name = string.IsNullOrEmpty(Name) ? Loc.T("scan.unnamed") : Name;
            var extra = Extra;
            var rel = Geo.Relative(reference, Position);
            return string.IsNullOrEmpty(extra) ? name + ", " + rel : name + ", " + extra + ", " + rel;
        }

        /// <summary>The spoken line for the thing itself, no position — for at-cursor announcements
        /// (the cursor is on it, so distance/bearing would be noise).</summary>
        public string DescribeInPlace()
        {
            var name = string.IsNullOrEmpty(Name) ? Loc.T("scan.unnamed") : Name;
            var extra = Extra;
            return string.IsNullOrEmpty(extra) ? name : name + ", " + extra;
        }

        /// <summary>
        /// Interact with this item — mirroring the game's click (auto-path + act), driven through the
        /// game's own click handlers/commands (see the interaction-pipeline memory). Returns true if
        /// something was triggered. Base: not interactable (e.g. a raw map marker).
        /// </summary>
        public virtual bool Interact() => false;
    }
}
