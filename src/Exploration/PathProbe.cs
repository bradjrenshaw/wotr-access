using System.Collections.Generic;
using Kingmaker.View; // ObstacleAnalyzer
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar; // LineOfSightGeometry
using Pathfinding; // ABPath, AstarPath
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>The relationship between two world points: sight and route. Produced by
    /// <see cref="PathProbe.Probe"/>; consumed by the review path-ping and (later) anything that
    /// needs to judge aggro sightlines, charge lanes, or walkability between two spots.</summary>
    internal sealed class PathInfo
    {
        /// <summary>The destination is visible from the start — the game's own fog-wall LOS
        /// convention (EyeShift both ends), the same test enemy perception and spell targeting use.</summary>
        public bool HasLos;

        /// <summary>A walkable route exists: the pathfinder's result actually arrives at the
        /// destination (A* always returns a path to the nearest REACHABLE node, so arrival is
        /// judged by the end gap — the game's own reachability idiom).</summary>
        public bool HasPath;

        /// <summary>The straight-line navmesh walk reaches the destination — the same
        /// <c>TraceAlongNavmesh</c> test the charge ability's target restriction runs.</summary>
        public bool IsStraight;

        /// <summary>The route's waypoints when <see cref="HasPath"/> (a private copy — safe to keep).</summary>
        public List<Vector3> Points;

        /// <summary>Metres along <see cref="Points"/> (0 when no path).</summary>
        public float PathLength;

        /// <summary>Metres between the pathfinder's endpoint and the destination.</summary>
        public float EndGap;
    }

    /// <summary>Probes sight + route between two points using the game's own primitives.</summary>
    internal static class PathProbe
    {
        // How close a result must land to the destination to count as "arrived". Covers off-mesh
        // furniture (wall-hugging chest: nearest reachable point ~0.7m from centre) plus a footprint
        // margin, while staying well under room scale so a wrong-side-of-the-wall clamp still reads
        // unreachable. (The game's own Clockwork QA bot uses 3m for the same judgment.)
        internal const float ReachGap = 1.5f;

        /// <summary>Sight + route from <paramref name="from"/> to <paramref name="to"/>. Synchronous
        /// (the pathfind blocks, keypress-scale work — don't call per frame).</summary>
        public static PathInfo Probe(Vector3 from, Vector3 to)
        {
            var info = new PathInfo();

            // Sight: the fog-wall oracle, called the game's way — EyeShift on both endpoints, ray to
            // the destination point, our usual slack so a door isn't blocked by its own frame.
            var los = LineOfSightGeometry.Instance;
            info.HasLos = los == null || !los.HasObstacle(from + LineOfSightGeometry.EyeShift,
                to + LineOfSightGeometry.EyeShift, ScanItem.LosFudge);

            // Straight line: charge's test — walk the straight segment across the navmesh and see
            // whether it arrives (XZ; the trace keeps the input Y, so height would be noise here).
            var traced = ObstacleAnalyzer.TraceAlongNavmesh(from, to);
            float sdx = traced.x - to.x, sdz = traced.z - to.z;
            info.IsStraight = sdx * sdx + sdz * sdz <= ReachGap * ReachGap;

            // Route: the game's synchronous point-to-point idiom (Clockwork QA bot): a raw ABPath on
            // the unit graph, calculated inline, arrival judged by the endpoint gap. 3D distance for
            // the gap on purpose — a balcony directly overhead is CLOSE in XZ but not arrived-at.
            if (AstarPath.active != null)
            {
                ABPath p = null;
                try
                {
                    p = ABPath.Construct(from, to);
                    p.nnConstraint.graphMask = 1;
                    p.Claim(typeof(PathProbe));
                    AstarPath.StartPath(p, pushToFront: true);
                    AstarPath.BlockUntilCalculated(p);
                    if (!p.error && p.vectorPath != null && p.vectorPath.Count > 0)
                    {
                        info.EndGap = Vector3.Distance(p.endPoint, to);
                        if (info.EndGap <= ReachGap)
                        {
                            info.HasPath = true;
                            // Copy: Release returns the path (and its lists) to the A* pool.
                            info.Points = new List<Vector3>(p.vectorPath);
                            for (int i = 1; i < info.Points.Count; i++)
                                info.PathLength += Vector3.Distance(info.Points[i - 1], info.Points[i]);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Main.Log?.Warning("[pathprobe] pathfind failed: " + e.Message);
                }
                finally
                {
                    try { p?.Release(typeof(PathProbe)); } catch { }
                }
            }

            // Consistency: a straight walk that arrives IS a route, whatever the pathfinder said
            // (it can hiccup during graph updates) — never report "straight but unreachable".
            if (info.IsStraight && !info.HasPath)
            {
                info.HasPath = true;
                info.Points = new List<Vector3> { from, traced };
                info.PathLength = Vector3.Distance(from, traced);
            }
            return info;
        }
    }
}
