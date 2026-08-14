using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// EXPERIMENTAL (an <see cref="Enhancements"/> toggle): when the gliding cursor dead-stops against
    /// something and the movement keys are released, name what's in the way by raycasting the scene's
    /// physics colliders in the pushed direction and speaking the hit object's cleaned-up asset name
    /// ("tavern_indoor_wall_window_02_Collider" → "tavern indoor wall window"). Raw Unity names, but
    /// live-sampled ones proved intelligible — sighted players see the furniture they bump into; this
    /// restores a sliver of that. Units are skipped (their collider names are rig junk, and the
    /// scanner/sonar already owns them).
    /// </summary>
    internal static class CollisionNamer
    {
        // How far past the navmesh stop to look: the navmesh edge is eroded ~0.5m+ from the visual
        // geometry, and the object's collider sits beyond that (live-measured up to ~3.3m for a chest
        // whose navmesh cut was at 2.7m).
        private const float Reach = 3.5f;

        // Two ray heights: knee (low crates/furniture) and chest (tables, wall panels).
        private static readonly float[] Heights = { 0.35f, 1.1f };

        /// <summary>Speak the nearest named blocker in <paramref name="dir"/>, if any — else fall back
        /// to a level-change readout ("raised ground"/"drop") when the blocker is an elevation edge:
        /// thin elevated floor sheets sit between the two ray heights, so no ray ever names them.
        /// Queued speech — this is passive context, never worth cutting off navigation for.</summary>
        public static void Announce(Vector3 from, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            dir = dir.normalized;
            string name = NameOf(from, dir);
            if (!string.IsNullOrEmpty(name)) { Tts.Speak(name, interrupt: false); return; }
            string levelKey = LevelChange(from, dir);
            if (levelKey != null) Tts.Speak(Loc.T(levelKey), interrupt: false);
        }

        // What the walkable surface does just past the block: probe a few steps beyond the edge
        // (the navmesh is eroded on BOTH sides of a riser, so the far tier starts well past it) and
        // report a clear height change. Same-level ground beyond the gap = an unnamed thin cut — stay
        // silent rather than guess.
        private static string LevelChange(Vector3 from, Vector3 dir)
        {
            for (float t = 0.8f; t <= 2.4f; t += 0.8f)
            {
                var s = NavmeshProbe.Sample(from.x + dir.x * t, from.z + dir.z * t, from.y);
                if (!s.OnNavmesh) continue;
                float dy = s.Point.y - from.y;
                if (dy >= 0.3f) return "collision.rise";
                if (dy <= -0.5f) return "collision.drop";
                return null;
            }
            return null;
        }

        private static string NameOf(Vector3 from, Vector3 dir)
        {
            string bestName = null;
            float bestDist = float.MaxValue;

            // Only candidates with a speakable name compete — an anonymous "GameObject" collider
            // must not shadow a farther, properly named one.
            void Consider(GameObject go, float dist)
            {
                if (go == null || dist >= bestDist) return;
                string n = CleanName(go.name);
                if (n == null) return;
                bestName = n;
                bestDist = dist;
            }

            // 1. The block-edge probe: the directional candidate at the navmesh stop — catches wall
            // corners and slabs a few cm off the ray lines (thin rays clip straight past those),
            // and already prefers what's actually in front of the push.
            var probed = BlockProbe.Find(from, from, dir);
            if (probed.Object != null) Consider(probed.Object, probed.Distance);

            // 2. Horizontal rays: still the best reach for walls whose face sits past the probe
            // sphere (navmesh erosion + wall thickness).
            foreach (float h in Heights)
            {
                var hits = Physics.RaycastAll(from + Vector3.up * h, dir, Reach);
                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;
                    // Units: rig collider names are junk and the scanner already names units.
                    if (hit.collider.GetComponentInParent<Kingmaker.View.UnitEntityView>() != null) continue;
                    Consider(hit.collider.gameObject, hit.distance);
                }
            }

            // 3. Colliderless blockers: much decor (tavern tables, pillars) cuts the navmesh via A*
            // NavmeshCut components with NO physics collider — invisible to everything above. The
            // clipper registry is a cheap live list; take any cut roughly along the pushed direction.
            var cuts = Pathfinding.NavmeshClipper.allEnabled;
            if (cuts != null)
                foreach (var cut in cuts)
                {
                    if (cut == null) continue;
                    var p = cut.transform.position;
                    float ox = p.x - from.x, oz = p.z - from.z;
                    float along = ox * dir.x + oz * dir.z;              // progress along the ray (XZ)
                    // Tighter cap than the rays: the cursor dead-stops AT the cut's eroded edge, so
                    // its centre is at most ~a table-half away — farther cuts are a different object.
                    if (along <= 0.05f || along > 3f) continue;
                    float qx = ox - along * dir.x, qz = oz - along * dir.z;
                    if (qx * qx + qz * qz > 4f) continue;               // > 2m off-axis: not what we hit
                    Consider(cut.gameObject, along);
                }

            return bestName;
        }

        /// <summary>Asset name → speakable text: drop "(Clone)"/"(3)" tails, asset-pipeline noise words
        /// (collider, visual), and variant numbers; underscores become spaces.
        /// "tavern_table_01_small (5)" → "tavern table small".</summary>
        internal static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw;
            int paren = s.IndexOf('(');
            if (paren >= 0) s = s.Substring(0, paren);
            s = s.Replace('_', ' ').Replace('-', ' ').Trim();
            var tokens = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var kept = new System.Collections.Generic.List<string>(tokens.Length);
            foreach (var raw2 in tokens)
            {
                var t = raw2.Trim('!');                     // asset-pipeline markers: "!Visual"
                if (t.Length == 0 || IsDigits(t)) continue;
                if (t.Equals("collider", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Equals("visual", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Equals("GameObject", System.StringComparison.OrdinalIgnoreCase)) continue; // anonymous
                kept.Add(t);
            }
            return kept.Count == 0 ? null : string.Join(" ", kept);
        }

        private static bool IsDigits(string t)
        {
            foreach (char c in t) if (c < '0' || c > '9') return false;
            return t.Length > 0;
        }
    }
}
