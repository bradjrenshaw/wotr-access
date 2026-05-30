using System.Collections.Generic;
using Kingmaker;                              // Game
using Kingmaker.Controllers.Clicks.Handlers;  // ClickMapObjectHandler
using Kingmaker.EntitySystem.Entities; // MapObjectEntityData
using Kingmaker.View.MapObjects;       // InteractionPart family, LocalMapMarkerPart
using UnityEngine;                     // Bounds, Collider, Mathf

namespace WrathAccess.Exploration
{
    /// <summary>
    /// An interactable map object. Its categories come from the interaction parts it carries (many-to-
    /// many — a thing that's both lootable and searchable is in Containers and Search Points); lock/trap
    /// parts are state, not categories. Map objects rarely have a real name, so we label by the local-
    /// map marker description if it has one, else by interaction type ("Door", "Container", …). An object
    /// with no relevant interactions reports no categories and is excluded by the scanner.
    /// </summary>
    internal sealed class ProxyMapObject : ProxyEntity
    {
        private readonly MapObjectEntityData _obj;

        public ProxyMapObject(MapObjectEntityData obj) : base(obj) { _obj = obj; }

        // Mirror the local map's own marker filter (LocalMapMarkerPart.IsVisible): a static object stays
        // listed once it's been revealed, even if it's currently back in fog — so we keep showing things
        // the player has seen, like the map does. Perception gates the hidden ones: IsPerceptionCheckPassed
        // is true by default but false for secret/trapped objects until their perception check passes, so
        // undiscovered ones don't leak. (This is why we don't use the base current-visibility filter.)
        public override bool IsVisible => _obj.IsInGame && _obj.IsRevealed && _obj.IsPerceptionCheckPassed;

        // Footprint = half the object's larger horizontal extent (an enclosing radius for our planar
        // cursor). Sourced from the COLLIDERS, not renderers: MapObjectView sets AutoUpdateRenderers=false,
        // so the view never caches its renderers and GetMaxBounds() returns a zero-size box — but the
        // colliders ARE cached (the clickable physical extent). Looked up live (objects can resize).
        public override float Footprint
        {
            get
            {
                var view = _obj.View;
                if (view == null) return 0f;
                var b = new Bounds(view.transform.position, Vector3.zero);
                bool any = false;
                foreach (var col in view.Colliders)
                {
                    if (col == null) continue;
                    b.Encapsulate(col.bounds);
                    any = true;
                }
                if (!any) b = view.GetMaxBounds(); // fallback (e.g. an object with only hide-renderers)
                return Mathf.Max(b.extents.x, b.extents.z);
            }
        }

        public override IEnumerable<ScanCategory> Categories
        {
            get
            {
                var cats = new HashSet<ScanCategory>();
                var interactions = _obj.Interactions; // InteractionPart parts only
                for (int i = 0; i < interactions.Count; i++)
                {
                    var part = interactions[i];
                    // A HiddenPart gates the object's OTHER interactions: while unrevealed it disables them
                    // (Enabled = false) and they only turn on once a skill check is passed. Skip disabled
                    // parts so a hidden chest reads as a search point, not a container, until it's opened.
                    if (!part.Enabled) continue;
                    switch (part)
                    {
                        // Unrevealed hidden object → a search point (a skill check to reveal). Once Opened
                        // the real parts are enabled and categorize themselves, so we add nothing here.
                        case HiddenPart h: if (!h.Opened) cats.Add(ScanCategory.SearchPoints); break;
                        case InteractionDoorPart _: cats.Add(ScanCategory.Doors); break;
                        case InteractionLootPart _: cats.Add(ScanCategory.Containers); break;
                        case InteractionSkillCheckPart _: cats.Add(ScanCategory.SearchPoints); break;
                        case DisableTrapInteractionPart _: break;     // trap = state (see Extra), not a category
                        default: cats.Add(ScanCategory.Other); break; // dialog, combine, button, device, bark
                    }
                }
                // Area transitions and restrictions are separate entity parts, not InteractionParts.
                if (_obj.Get<AreaTransitionPart>() != null) cats.Add(ScanCategory.Exits);
                // No interactable role → plain scenery (a prop). Surfaced so it can be browsed; visibility
                // still gates whether it's listed.
                if (cats.Count == 0) cats.Add(ScanCategory.Scenery);
                return cats;
            }
        }

        // The sonar cue for this object: loot sub-type / door / trap if we have one, else "unknown" for any
        // other interactable role, else null (scenery — nothing to ping). Mirrors Categories: skips
        // disabled parts (a HiddenPart gates the rest until searched → reads "unknown" while hidden).
        public override string SonarSound
        {
            get
            {
                InteractionLootPart loot = null;
                bool door = false, trap = false, other = false;
                var interactions = _obj.Interactions;
                for (int i = 0; i < interactions.Count; i++)
                {
                    var part = interactions[i];
                    if (!part.Enabled) continue;
                    switch (part)
                    {
                        case InteractionLootPart l: loot = l; break;
                        case InteractionDoorPart _: door = true; break;
                        case DisableTrapInteractionPart _: trap = true; break;
                        case HiddenPart h: if (!h.Opened) other = true; break;
                        default: other = true; break;
                    }
                }
                if (_obj.Get<AreaTransitionPart>() != null) other = true;

                if (loot != null) return LootSound(loot);
                if (door) return "door";
                if (trap) return "trap";
                return other ? "unknown" : null;
            }
        }

        private static string LootSound(InteractionLootPart loot)
        {
            switch (loot.Settings.LootContainerType) // Settings is public on InteractionPart<T>
            {
                case LootContainerType.Chest: return "loot-chest";
                case LootContainerType.Unit: return "loot-corpse";
                case LootContainerType.Environment: return "loot-environment";
                case LootContainerType.PlayerChest: return "loot-stash";
                case LootContainerType.OneSlot: return "loot-single";
                default: return "loot-generic"; // DefaultLoot + anything new
            }
        }

        public override string Name
        {
            get
            {
                var desc = _obj.Get<LocalMapMarkerPart>()?.GetDescription();
                if (!string.IsNullOrEmpty(desc)) return desc;
                foreach (var c in Categories) return ScanCategories.Singular(c); // first category's singular
                return "Object";
            }
        }

        protected override string Extra
        {
            get
            {
                var bits = new List<string>();
                if (_obj.Get<InteractionRestrictionPart>() != null) bits.Add("restricted");
                if (_obj.Get<DisableTrapInteractionPart>() != null) bits.Add("trapped");
                return bits.Count > 0 ? string.Join(", ", bits.ToArray()) : null;
            }
        }

        // Same as clicking the object: paths to it and runs its interaction. We pass the current
        // selection (the unit source the click uses); the interaction's SelectUnit picks the actor.
        // forceOvertipInteractions: true — overtip interactions are hover-triggered in mouse mode and
        // we have no hover.
        public override bool Interact()
        {
            var view = _obj.View;
            if (view == null) return false;
            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (units == null || units.Count == 0) return false;
            return ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions: true);
        }
    }
}
