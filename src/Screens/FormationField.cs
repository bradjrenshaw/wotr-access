using System.Collections.Generic;
using System.IO; // Path (cue wav)
using Kingmaker; // Game (party members for Ctrl+1..6)
using Kingmaker.UI.MVVM._VM.Formation; // FormationCharacterVM
using UnityEngine; // Vector2, Mathf, Time
using WrathAccess.Audio; // AudioEngines (enter/exit cue)
using WrathAccess.Exploration; // Geo (feet/metres)
using WrathAccess.Exploration.Overlays; // OverlayAudio (cue dir/volume)
using WrathAccess.Input; // InputManager (glide-key Held polling)
using WrathAccess.Settings; // ModSettings / IntSetting (cue volume)
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
        private const float GlideSpeedFeet = 5f;          // Shift+WASD continuous speed (small field → slow)

        private Vector2 _cursor;            // offset metres; +x = east (right), +y = north (forward)
        private FormationCharacterVM _held; // the picked-up member, or null
        private FormationCharacterVM _cueInside; // member the glide cursor is currently over (for enter/exit cue)
        private bool _wasGliding;           // last frame's glide state (to fire read-on-release)
        private int _cycleIndex = -1;       // last member reached by the Comma cycle (-1 = not yet)

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

        // Continuous mode (Shift+WASD), ticked while focused: glide the cursor freely (no grid snap), play an
        // enter/exit cue as it crosses a member, and on key release read where it landed. Gliding is too fast
        // to narrate per-frame, so it stays silent on move (the cue carries it) and speaks once on release —
        // the same feel as the exploration cursor's continuous mode (duplicated, not shared). The discrete
        // WASD path is unaffected (it isn't gliding, so this no-ops then).
        public override void OnUpdate()
        {
            int ix = (InputManager.Held("formation.glideRight") ? 1 : 0) - (InputManager.Held("formation.glideLeft") ? 1 : 0);
            int iz = (InputManager.Held("formation.glideUp") ? 1 : 0) - (InputManager.Held("formation.glideDown") ? 1 : 0);
            bool gliding = ix != 0 || iz != 0;

            if (gliding)
            {
                if (!_wasGliding) _cueInside = MemberAt(_cursor); // baseline on glide start (no cue this frame)
                var dir = new Vector2(ix, iz).normalized;
                float step = GlideSpeedFeet * Geo.MetresPerFoot * Time.unscaledDeltaTime;
                _cursor = new Vector2(
                    Mathf.Clamp(_cursor.x + dir.x * step, -FieldHalf, FieldHalf),
                    Mathf.Clamp(_cursor.y + dir.y * step, -FieldHalf, FieldHalf));
                var inside = MemberAt(_cursor);
                if (inside != _cueInside) { PlayCue(inside != null); _cueInside = inside; }
            }
            else if (_wasGliding)
            {
                Tts.Speak(CellReadout(), interrupt: true); // released → read where the cursor landed
            }
            _wasGliding = gliding;
        }

        // The cursor-crossed-a-member cue, duplicating the object overlay's enter/exit sound + volume.
        private static void PlayCue(bool enter)
        {
            float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
            AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, enter ? "object_enter.wav" : "object_exit.wav"), vol);
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

        /// <summary>Comma / Shift+Comma: jump the cursor to the next/previous member in the formation
        /// (cursor-relative when already on one), reading where it lands. Works on Auto too (to hear the
        /// layout); on Custom these are the ones you can then pick up.</summary>
        public void CycleMember(int dir)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            var members = new List<FormationCharacterVM>();
            foreach (var c in vm.Characters)
                if (c != null && c.Unit != null && c.IsVisible.Value) members.Add(c);
            if (members.Count == 0) { Tts.Speak(Loc.T("formation.no_members"), interrupt: true); return; }

            var cur = MemberAt(_cursor);
            int idx;
            if (cur != null && members.Contains(cur)) idx = Wrap(members.IndexOf(cur) + dir, members.Count);
            else if (_cycleIndex >= 0 && _cycleIndex < members.Count) idx = Wrap(_cycleIndex + dir, members.Count);
            else idx = dir > 0 ? 0 : members.Count - 1; // first cycle → first (next) / last (prev)
            _cycleIndex = idx;
            _cursor = members[idx].GetOffset();
            Tts.Speak(CellReadout(), interrupt: true);
        }

        /// <summary>Ctrl+1..6: grab the Nth party member straight away (start dragging) and move the cursor
        /// to them — so Ctrl+1 then Enter places member 1. Custom formations only.</summary>
        public void PickMember(int index)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }
            var party = Game.Instance?.Player?.PartyCharacters;
            var unit = (party != null && index >= 0 && index < party.Count) ? party[index].Value : null;
            var c = unit != null ? vm.Characters.Find(x => x != null && x.Unit == unit) : null;
            if (c == null || !c.IsInteractable.Value)
            {
                Tts.Speak(Loc.T("party.no_member", new { index = index + 1 }), interrupt: true);
                return;
            }
            _held = c;
            _cursor = c.GetOffset(); // move the cursor onto them so the next step/place is relative to here
            Tts.Speak(Loc.T("formation.picked_up", new { name = c.Unit.CharacterName }), interrupt: true);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

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
