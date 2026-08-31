using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The "pulse" sonar core, ported from the gdaccess Grim Dawn mod (shared design, 2026-08):
    /// a field of per-entity metronomes instead of a serialized sweep. Every candidate repeats its
    /// own tone on its own schedule: the PERIOD shrinks as it nears (interpolated LOG-in-distance,
    /// so a step close in swings the rate hard and a step at range barely moves it — distance is
    /// heard as urgency, continuously), and its left/right position offsets the PHASE within that
    /// period (50% from the left = half a period late), so co-distant things stagger instead of
    /// firing as one phantom-centred chord. A newly seen item is SEEDED phase-deep into its first
    /// period (a door opening onto six enemies staggers, not detonates); an overdue pulse advances
    /// by whole periods on its own phase grid (lag never machine-guns catch-up pings).
    ///
    /// Engine-free and allocation-free per tick (the per-frame rule): the caller hands the frame's
    /// candidates and reads the fired list back; both lists and both dictionaries are reused.
    /// </summary>
    internal sealed class SonarField
    {
        public struct Candidate
        {
            public ScanItem Item;
            public float Dist;   // metres
            public float Phase;  // 0..1 = left..right
        }

        public double PeriodNearSec = 0.14;
        public double PeriodFarSec = 0.80;
        public float DistNear = 3f;   // metres; at/under → PeriodNear
        public float DistFar = 12f;   // metres; at/over → PeriodFar

        private Dictionary<ScanItem, double> _nextAt = new Dictionary<ScanItem, double>();
        private Dictionary<ScanItem, double> _keep = new Dictionary<ScanItem, double>();
        private readonly List<ScanItem> _fired = new List<ScanItem>();

        public double PeriodFor(float dist)
        {
            float lo = Mathf.Max(DistNear, 0.01f);
            float hi = Mathf.Max(DistFar, lo + 0.01f);
            float d = Mathf.Clamp(dist, lo, hi);
            // Linear in log(distance): d(log d)/dd = 1/d — the rate swings hardest where d is small.
            double u = System.Math.Log(d / lo) / System.Math.Log(hi / lo);
            return PeriodNearSec + u * (PeriodFarSec - PeriodNearSec);
        }

        /// <summary>Advance to <paramref name="now"/> over this frame's candidates; the returned
        /// list (reused — consume before the next call) holds the items whose tone fires now.
        /// Items absent from <paramref name="items"/> are forgotten.</summary>
        public List<ScanItem> Update(List<Candidate> items, double now)
        {
            _fired.Clear();
            _keep.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                double period = PeriodFor(it.Dist);
                if (period < 0.01) period = 0.01;
                if (!_nextAt.TryGetValue(it.Item, out double due))
                {
                    // First sight: seed phase-offset into the period — stagger, don't fire.
                    due = now + Mathf.Clamp01(it.Phase) * period;
                }
                else if (now >= due)
                {
                    _fired.Add(it.Item);
                    do { due += period; } while (due <= now); // whole periods: keeps the phase grid
                }
                _keep[it.Item] = due;
            }
            var tmp = _nextAt; _nextAt = _keep; _keep = tmp;
            return _fired;
        }

        public void Reset() { _nextAt.Clear(); _keep.Clear(); _fired.Clear(); }
    }
}
