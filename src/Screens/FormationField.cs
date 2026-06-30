using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Formation; // FormationCharacterVM
using UnityEngine; // Vector2, Mathf
using WrathAccess.Exploration; // Geo (feet/metres)
using WrathAccess.UI;
using WrathAccess.UI.Announcements;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The formation editor — one Tab stop holding a 2-D cursor over the formation's layout space. WASD step
    /// the cursor one grid cell (wired in Main via the Formation input category, live only while this is
    /// focused); each step announces the party member there + its position, or the empty cell's position.
    /// Enter picks up the member at the cursor and drops the held one where you press it again. Editing only
    /// applies to a Custom formation (the Auto one arranges itself).
    ///
    /// The cursor lives in the formation OFFSET space (metres) — the same value GetOffset/MoveCharacter use
    /// and the game adds to the destination on a party move — so placement is 1:1 with the layout. (Shift+WASD
    /// continuous gliding with enter/exit cues + release-to-read is the next increment.)
    /// </summary>
    public sealed class FormationField : UIElement
    {
        private const float GridStep = 23f / 40f;        // one cell: 23 UI px at 40 px-per-metre ≈ 0.58 m
        private const float FieldHalf = 388f / 2f / 40f; // the draggable field's half-extent ≈ 4.85 m
        private const float GrabRadius = GridStep;        // "on" a member when within ~one cell

        private Vector2 _cursor;            // offset metres; +x = east (right), +y = north (forward)
        private FormationCharacterVM _held; // the picked-up member, or null

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(Loc.T("formation.field")));
            yield return new ValueAnnouncement(Message.Raw(CellReadout()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "formation.pick_drop"),
                _ => PickOrDrop());
        }

        /// <summary>Step the cursor one grid cell (called by the Formation input actions while focused).</summary>
        public void MoveStep(int dx, int dy)
        {
            _cursor = new Vector2(
                Mathf.Clamp(_cursor.x + dx * GridStep, -FieldHalf, FieldHalf),
                Mathf.Clamp(_cursor.y + dy * GridStep, -FieldHalf, FieldHalf));
            Tts.Speak(CellReadout(), interrupt: true);
        }

        private void PickOrDrop()
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }
            if (_held == null)
            {
                var who = MemberAt(_cursor, requireInteractable: true);
                if (who == null) { Tts.Speak(Loc.T("formation.nothing_here"), interrupt: true); return; }
                _held = who;
                Tts.Speak(Loc.T("formation.picked_up", new { name = who.Unit.CharacterName }), interrupt: true);
            }
            else
            {
                _held.MoveCharacter(_cursor);
                Tts.Speak(Loc.T("formation.placed", new { name = _held.Unit.CharacterName }) + ", " + PositionStr(_cursor),
                    interrupt: true);
                _held = null;
            }
        }

        // The party member at/near the cursor (nearest within the grab radius), or null.
        private static FormationCharacterVM MemberAt(Vector2 at, bool requireInteractable = false)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return null;
            FormationCharacterVM best = null;
            float bestSq = GrabRadius * GrabRadius;
            foreach (var c in vm.Characters)
            {
                if (c == null || c.Unit == null) continue;
                if (requireInteractable ? !c.IsInteractable.Value : !c.IsVisible.Value) continue;
                float sq = (c.GetOffset() - at).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        // "<member or empty>, <position>" — what the cursor is over.
        private string CellReadout()
        {
            var who = MemberAt(_cursor);
            string name = who != null ? who.Unit.CharacterName : Loc.T("formation.empty");
            return name + ", " + PositionStr(_cursor);
        }

        // The offset as "X feet east/west, Y feet north/south" (or "center" near the origin).
        private static string PositionStr(Vector2 off)
        {
            const float eps = GridStep / 2f;
            if (Mathf.Abs(off.x) < eps && Mathf.Abs(off.y) < eps) return Loc.T("formation.center");
            var parts = new List<string>(2);
            if (Mathf.Abs(off.x) >= eps)
                parts.Add(Feet(Mathf.Abs(off.x)) + " " + Loc.T(off.x > 0 ? "formation.east" : "formation.west"));
            if (Mathf.Abs(off.y) >= eps)
                parts.Add(Feet(Mathf.Abs(off.y)) + " " + Loc.T(off.y > 0 ? "formation.north" : "formation.south"));
            return string.Join(", ", parts);
        }

        // Feet to 2 decimals — the grid step is ~1.9 ft, so whole-foot rounding (the scanner's FeetStr) would
        // read adjacent cells as 2/4/6 ft and feel jumpy; the editor needs the finer precision.
        private static string Feet(float metres)
            => Loc.T("geo.feet", new
            {
                feet = Geo.Feet(metres).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}
