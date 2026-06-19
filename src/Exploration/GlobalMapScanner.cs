using System.Collections.Generic;
using System.Linq;
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map scanner — an isolated analogue of the in-area <see cref="Scanner"/> that shares NONE of
    /// its code (no navmesh / rooms / fog / live entity diffing): a categorised, nearest-first browse of the
    /// map's revealed points, read live from <see cref="GlobalMapModel"/>. PageUp/Down cycle items within a
    /// category, Ctrl+PageUp/Down cycle categories (Everything / Locations / Junctions), I travels to or
    /// enters the selected point (<see cref="GlobalMapActions.Go"/>). Sounds, the review cursor, and armies
    /// arrive with the world-map cursor/sonar increment.
    /// </summary>
    internal static class GlobalMapScanner
    {
        private enum Cat { Everything, Locations, Junctions }
        private static readonly Cat[] Cats = { Cat.Everything, Cat.Locations, Cat.Junctions };

        private static int _catIndex;
        private static int _itemIndex;

        /// <summary>Reset the cursor to the top when the map (re)opens.</summary>
        public static void Reset() { _catIndex = 0; _itemIndex = 0; }

        private static IEnumerable<GlobalMapPointView> Source(Cat c)
        {
            switch (c)
            {
                case Cat.Locations: return GlobalMapModel.Locations;
                case Cat.Junctions: return GlobalMapModel.Junctions;
                default: return GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions);
            }
        }

        // The current category's points, nearest-first from the party. Rebuilt per call (≈60 points, cheap)
        // so reveal/travel changes are always reflected.
        private static List<GlobalMapPointView> CurrentList()
        {
            var from = GlobalMapModel.TravelerPos;
            return Source(Cats[_catIndex]).OrderBy(p => Geo.Distance(from, p.transform.position)).ToList();
        }

        private static string CatLabel(Cat c)
            => Loc.T(c == Cat.Locations ? "worldmap.cat_locations"
                   : c == Cat.Junctions ? "worldmap.cat_junctions" : "worldmap.cat_everything");

        public static void NextItem() => Step(+1);
        public static void PrevItem() => Step(-1);

        private static void Step(int dir)
        {
            var list = CurrentList();
            if (list.Count == 0) { Tts.Speak(Loc.T("worldmap.scan_empty", new { cat = CatLabel(Cats[_catIndex]) })); return; }
            _itemIndex = Mathf.Clamp(_itemIndex + dir, 0, list.Count - 1);
            AnnounceItem(list, _itemIndex);
        }

        public static void NextCategory() => StepCat(+1);
        public static void PrevCategory() => StepCat(-1);

        private static void StepCat(int dir)
        {
            _catIndex = ((_catIndex + dir) % Cats.Length + Cats.Length) % Cats.Length;
            _itemIndex = 0;
            var list = CurrentList();
            Tts.Speak(CatLabel(Cats[_catIndex]));
            if (list.Count == 0) { Tts.Speak(Loc.T("worldmap.scan_empty", new { cat = CatLabel(Cats[_catIndex]) })); return; }
            AnnounceItem(list, 0);
        }

        public static void Interact()
        {
            var list = CurrentList();
            if (_itemIndex < 0 || _itemIndex >= list.Count) { Tts.Speak(Loc.T("worldmap.scan_none")); return; }
            GlobalMapActions.Go(list[_itemIndex]);
        }

        private static void AnnounceItem(List<GlobalMapPointView> list, int i)
            => Tts.Speak(GlobalMapActions.Label(list[i]) + ", " + Loc.T("nav.position", new { index = i + 1, count = list.Count }));
    }
}
