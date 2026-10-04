using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints; // GetComponent(s) extensions on blueprints
using Kingmaker.Designers; // GameHelper.GetTargetsAround (the spells' own LOS-checked enumeration)
using Kingmaker.Designers.EventConditionActionSystem.Actions; // Conditional (nested action lists)
using Kingmaker.ElementsSystem; // ActionList
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions; // ContextActionSpawnAreaEffect
using Kingmaker.Utility; // Feet
using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Area-of-effect preview while AIMING: the instant the cursor stops, speak the spell's shape and
    /// who it would catch there — "Cone: Kobold, Kobold" — the audio twin of the ground decal sighted
    /// players aim with. Shape sources, in resolution order:
    ///  - the aggregated <c>AoERadius</c>/<c>AoETargets</c> (bursts, cylinders: fireball, prayer);
    ///  - the projectile component (90-degree cones, corridor lines — anchored at the CASTER, or at
    ///    its live CONTROLLED PROJECTILE, exactly like the game's AbilityRange.GetCastPosition);
    ///  - chains (potential arc targets within the jump radius of the aim point);
    ///  - Clashing Rocks' corridor;
    ///  - spawned persistent areas (the spawning ability often reports no radius itself): Cylinder →
    ///    circle at the point; Wall → a Size-long, 5-ft-thick box CENTERED at the point and standing
    ///    PERPENDICULAR to the caster→point direction (the spawn's own orientation rule — walls pivot
    ///    as the caster moves, and so does this readout).
    /// Only units the player can currently see are listed — the decal reveals nothing hidden, and
    /// neither do we. A contribution to the cursor's readout pipeline (which owns the stop), plus an
    /// immediate readout when aiming begins (the cursor may already sit on the target).
    /// </summary>
    internal sealed class AoePreviewSystem : OverlaySystem
    {
        public override string Name => "Area preview";
        public override string Key => "aoe";

        // A readout (no movement-timed playback) — Off/Continuous only.
        public override IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        private bool _wasAiming;
        private const float WallHalfThickness = 0.762f; // the spawn's box is 5 ft thick

        public override void OnExit(Overlay overlay) { _wasAiming = false; }

        // The stop itself belongs to the readout pipeline (Overlay.TickSettle); this only notices aiming
        // BEGIN and asks for an immediate readout (the cursor may already sit on the target).
        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !ShouldPlay(overlay)) { _wasAiming = false; return; }
            var ability = Game.Instance?.SelectedAbilityHandler?.SelectedAbility;
            bool aiming = Resolve(ability) != null;
            if (aiming && !_wasAiming) overlay.RequestSettle();
            _wasAiming = aiming;
        }

        /// <summary>What the aimed ability would hit at the cursor, for the readout pipeline (every
        /// trigger while aiming).</summary>
        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled) yield break;
            var ability = Game.Instance?.SelectedAbilityHandler?.SelectedAbility;
            var info = Resolve(ability);
            if (info == null) yield break;
            var line = Line(ability, info, ctx.Cursor);
            if (line != null) yield return new OverlayAnnouncement(ctx.Want, Message.Raw(line), OverlayAnnouncement.OrderEffect);
        }

        // ---- shape resolution (cached per aimed ability — Tick asks every frame) ----

        private enum Shape { Circle, Cone, Line, Wall, Chain }

        private sealed class ShapeInfo
        {
            public Shape Kind;
            public Feet Radius;                        // circle / chain jump radius
            public TargetType Targets = TargetType.Any; // circle faction filter
            public float LengthMeters, WidthMeters;    // cone/line/wall (wall: Length = the long side)
            public AbilityDeliverProjectile Projectile; // cone/line (controlled-projectile origin)
            public bool IncludeDead;                   // chains can arc to corpses
            public bool LengthToCursor;                // Clashing Rocks: corridor runs to the cursor
        }

        private static AbilityData _cachedFor;
        private static ShapeInfo _cached;

        private static ShapeInfo Resolve(AbilityData ability)
        {
            if (ability == null) { _cachedFor = null; _cached = null; return null; }
            if (ability == _cachedFor) return _cached;
            _cachedFor = ability;
            _cached = ResolveUncached(ability);
            return _cached;
        }

        private static ShapeInfo ResolveUncached(AbilityData ability)
        {
            var bp = ability.GetDeliverBlueprint(checkAdditionalAoeForMagicHack: true);
            if (bp == null) return null;

            if (bp.AoERadius.Meters > 0f)
                return new ShapeInfo { Kind = Shape.Circle, Radius = bp.AoERadius, Targets = bp.AoETargets };

            var proj = bp.GetComponent<AbilityDeliverProjectile>();
            if (proj != null && proj.Type == AbilityProjectileType.Cone)
                return new ShapeInfo { Kind = Shape.Cone, LengthMeters = proj.Length.Meters, Projectile = proj };
            if (proj != null && proj.Type == AbilityProjectileType.Line)
                return new ShapeInfo { Kind = Shape.Line, LengthMeters = proj.Length.Meters,
                    WidthMeters = proj.LineWidth.Meters, Projectile = proj };

            var chain = bp.GetComponent<AbilityDeliverChain>();
            if (chain != null)
                return new ShapeInfo { Kind = Shape.Chain, Radius = chain.Radius, IncludeDead = chain.TargetDead };

            var rocks = bp.GetComponent<AbilityDeliverClashingRocks>();
            if (rocks != null)
                return new ShapeInfo { Kind = Shape.Line, WidthMeters = rocks.Width.Meters, LengthToCursor = true };

            var area = FindSpawnedArea(bp);
            if (area != null)
            {
                if (area.Shape == AreaEffectShape.Cylinder)
                    return new ShapeInfo { Kind = Shape.Circle, Radius = area.Size };
                if (area.Shape == AreaEffectShape.Wall)
                    return new ShapeInfo { Kind = Shape.Wall, LengthMeters = area.Size.Meters };
            }
            return null;
        }

        // Walls and clouds live on a spawned BlueprintAbilityAreaEffect, usually at the top of the
        // ability's run-action list (recursing conditionals covers the "empowered variant" pattern).
        private static BlueprintAbilityAreaEffect FindSpawnedArea(BlueprintAbility bp)
        {
            foreach (var run in bp.GetComponents<AbilityEffectRunAction>())
            {
                var found = FindSpawnedArea(run.Actions);
                if (found != null) return found;
            }
            return null;
        }

        private static BlueprintAbilityAreaEffect FindSpawnedArea(ActionList list)
        {
            if (list?.Actions == null) return null;
            foreach (var a in list.Actions)
            {
                if (a is ContextActionSpawnAreaEffect spawn && spawn.AreaEffect != null) return spawn.AreaEffect;
                if (a is Conditional c)
                {
                    var f = FindSpawnedArea(c.IfTrue) ?? FindSpawnedArea(c.IfFalse);
                    if (f != null) return f;
                }
            }
            return null;
        }

        // ---- announcing ----

        private static string Line(AbilityData ability, ShapeInfo info, Vector3 point)
        {
            var caster = ability.Caster != null ? ability.Caster.Unit : null;
            if (caster == null) return null;

            string shapeKey;
            List<UnitEntityData> hits;
            switch (info.Kind)
            {
                case Shape.Circle:
                    shapeKey = "aoe.circle";
                    hits = AroundHits(caster, point, info.Radius, info.Targets, includeDead: false);
                    break;
                case Shape.Chain:
                    // The arc CANDIDATES: everything within jump radius of the aim point (the real
                    // sequence depends on hit rolls and proximity — this is what could be reached).
                    shapeKey = "aoe.chain";
                    hits = AroundHits(caster, point, info.Radius, TargetType.Any, info.IncludeDead);
                    break;
                case Shape.Wall:
                    shapeKey = "aoe.wall";
                    hits = WallHits(caster, point, info.LengthMeters);
                    break;
                default:
                {
                    bool cone = info.Kind == Shape.Cone;
                    shapeKey = cone ? "aoe.cone" : "aoe.line";
                    // The game's own anchoring (AbilityRange.GetCastPosition): a CONTROLLED
                    // PROJECTILE's live position when one is in flight (such lines re-fire from the
                    // projectile), else the caster with the apex pushed out by corpulence.
                    Vector3 origin = caster.Position;
                    float originPad = caster.Corpulence;
                    var controlled = info.Projectile?.FindControlledProjectileRuntime(ability.Caster)?.GetActiveProjectilePosition();
                    if (controlled.HasValue) { origin = controlled.Value; originPad = 0f; }
                    float length = info.LengthToCursor
                        ? Vector3.Distance(origin, point) : info.LengthMeters;
                    hits = BeamHits(caster, origin, originPad, point, length, info.WidthMeters, cone);
                    break;
                }
            }

            string shape = Loc.T(shapeKey);
            if (hits.Count == 0) return Loc.T("aoe.none", new { shape });
            hits.Sort((a, b) => (a.Position - caster.Position).sqrMagnitude
                .CompareTo((b.Position - caster.Position).sqrMagnitude));
            var names = new List<string>();
            foreach (var u in hits) names.Add(u.CharacterName);
            return Loc.T("aoe.hits", new { shape, names = string.Join(", ", names) });
        }

        // Burst/cylinder/chain-radius at the cursor point: the spells' own enumeration (LOS-checked,
        // like AbilityTargetsAround.Select), filtered by the ability's declared target kind so an
        // enemies-only burst doesn't list your own party.
        private static List<UnitEntityData> AroundHits(UnitEntityData caster, Vector3 point,
            Feet radius, TargetType targets, bool includeDead)
        {
            var hits = new List<UnitEntityData>();
            foreach (var u in GameHelper.GetTargetsAround(point, radius, checkLOS: true, includeDead))
            {
                if (u == null || !Listable(u, includeDead)) continue;
                if (targets == TargetType.Enemy && !caster.IsEnemy(u)) continue;
                if (targets == TargetType.Ally && !caster.IsAlly(u)) continue;
                hits.Add(u);
            }
            return hits;
        }

        // Cone (90 degrees, per the game's ConeAngle constant) or line corridor — apex at the cast
        // origin, pointed at the cursor, apex pushed out by originPad exactly like the preview decal.
        // Corpulence pads the per-unit tests so a body clipping the edge counts.
        private static List<UnitEntityData> BeamHits(UnitEntityData caster, Vector3 origin, float originPad,
            Vector3 point, float lengthMeters, float widthMeters, bool cone)
        {
            var hits = new List<UnitEntityData>();
            var dir = point - origin; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return hits;
            dir.Normalize();
            origin += dir * originPad;

            var units = Game.Instance?.State?.Units;
            if (units == null) return hits;
            foreach (var u in units)
            {
                if (u == null || u == caster || !u.IsInGame || !Listable(u, includeDead: false)) continue;
                var v = u.Position - origin; v.y = 0f;
                float along = Vector3.Dot(v, dir);
                float pad = u.Corpulence;
                if (along < 0f || along > lengthMeters + pad) continue;
                if (cone)
                {
                    if (v.sqrMagnitude > 0.01f && Vector3.Angle(v, dir) > 45f + (pad > 0f ? 5f : 0f)) continue;
                }
                else
                {
                    float side = Vector3.Cross(dir, v).magnitude;
                    if (side > widthMeters * 0.5f + pad) continue;
                }
                hits.Add(u);
            }
            return hits;
        }

        // Wall: a lengthMeters-long, 5-ft-thick box CENTERED at the point, its long axis PERPENDICULAR
        // to the caster→point direction — the spawn's own orientation rule (TargetWrapper.Orientation =
        // caster→point yaw; the box's long side is local X). Walk around the point and it pivots.
        private static List<UnitEntityData> WallHits(UnitEntityData caster, Vector3 point, float lengthMeters)
        {
            var hits = new List<UnitEntityData>();
            var dir = point - caster.Position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return hits;
            dir.Normalize();
            var perp = Vector3.Cross(Vector3.up, dir);

            var units = Game.Instance?.State?.Units;
            if (units == null) return hits;
            foreach (var u in units)
            {
                if (u == null || !u.IsInGame || !Listable(u, includeDead: false)) continue;
                var v = u.Position - point; v.y = 0f;
                float pad = u.Corpulence;
                if (Mathf.Abs(Vector3.Dot(v, perp)) > lengthMeters * 0.5f + pad) continue;
                if (Mathf.Abs(Vector3.Dot(v, dir)) > WallHalfThickness + pad) continue;
                hits.Add(u);
            }
            return hits;
        }

        // What the decal shows: units the player can currently see — never hidden ones.
        private static bool Listable(UnitEntityData u, bool includeDead)
            => (includeDead || !u.Descriptor.State.IsDead) && u.IsVisibleForPlayer;

    }
}
