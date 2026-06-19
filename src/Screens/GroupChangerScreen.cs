using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized Accept label)
using Kingmaker.UI.MVVM._VM.GroupChanger;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The Group manager (<see cref="GroupChangerVM"/>) — party selection shown when leaving an area for
    /// the world map. Two arrow-navigated lists (Current Party / Companions); Enter on a character moves it
    /// between them (<c>MoveCharacter</c>, mirroring the portrait click), respecting the locked main
    /// character and the 6-slot cap. An Accept stop commits the party and leaves (<c>Go</c>); Escape cancels
    /// and stays in the area (<c>Close</c>, only when the party is already valid — like the game's X, which
    /// hides otherwise). Driven, like the loot window, off the in-game static HUD
    /// (<c>GroupChangerContextVM.GroupChangerVM</c>), not a RootUIContext service window.
    /// </summary>
    public sealed class GroupChangerScreen : Screen
    {
        public override string Key => "ctx.groupchanger";
        public override string ScreenName => Loc.T("screen.group_manager");
        public override int Layer => 16; // a hard modal over the in-game context (and the global map)

        // A true modal. Unlike loot/dialogue/rest (which lose control, so the in-game screen drops its
        // Exploration/InGame categories), the group changer keeps control — so block the categories below
        // it explicitly, or exploration hotkeys would still fire under the modal.
        public override bool Exclusive => true;

        private GroupChangerVM _builtVm;
        private ListContainer _party;
        private ListContainer _remote;
        private ProxyActionButton _accept;

        private static GroupChangerVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.GroupChangerContextVM?.GroupChangerVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { _builtVm = null; }
        public override void OnPop() { Clear(); _builtVm = null; _party = _remote = null; _accept = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // A new GroupChangerVM instance = a fresh window. Within one window the lists only change via our
            // own moves (handled in Activate), so no per-frame resync is needed.
            if (vm != _builtVm) { _builtVm = vm; Rebuild(vm); }
        }

        private void Rebuild(GroupChangerVM vm)
        {
            Clear();
            _party = new ListContainer(vm.PartyHeader);   // "Current Party"  (game-localized)
            _remote = new ListContainer(vm.RemoteHeader);  // "Companions"
            Populate(vm);
            Add(_party);
            Add(_remote);
            // Accept = commit the chosen party and leave (Go); greys out until the selection is valid.
            _accept = new ProxyActionButton(
                () => TextUtil.StripRichText((string)UIStrings.Instance.CommonTexts.Accept),
                () => vm.AcceptEnabled.Value,
                () => vm.Go());
            Add(_accept);
            Navigation.Attach(this);
        }

        private void Populate(GroupChangerVM vm)
        {
            _party.Clear();
            foreach (var ch in vm.PartyCharacter) _party.Add(CharButton(vm, ch, _party));
            _remote.Clear();
            foreach (var ch in vm.RemoteCharacter) _remote.Add(CharButton(vm, ch, _remote));
        }

        private ProxyActionButton CharButton(GroupChangerVM vm, GroupChangerCharacterVM ch, ListContainer list)
            => new ProxyActionButton(() => CharLabel(ch), () => true, () => Activate(vm, ch, list));

        // Name plus the badges the portrait shows (lock / level-up / mythic level-up / overload).
        private static string CharLabel(GroupChangerCharacterVM ch)
        {
            var parts = new List<string> { ch.UnitRef.Value.CharacterName };
            if (ch.IsLock) parts.Add(Loc.T("group.locked_tag"));
            if (ch.IsLevelUp) parts.Add(Loc.T("group.levelup_tag"));
            if (ch.IsMythicLevelUp) parts.Add(Loc.T("group.mythic_tag"));
            if (ch.IsCharacterOverload) parts.Add(Loc.T("group.overload_tag"));
            return string.Join(", ", parts);
        }

        private void Activate(GroupChangerVM vm, GroupChangerCharacterVM ch, ListContainer fromList)
        {
            if (ch.IsLock) { Tts.Speak(Loc.T("group.cant_move")); return; } // main / required / pinned: pinned in party
            bool inParty = fromList == _party;
            if (!inParty && vm.PartyCharacter.Count >= 6) { Tts.Speak(Loc.T("group.party_full")); return; }

            int idx = fromList.IndexOf(Navigation.Current); // the activated row, so we can stay near it after
            vm.MoveCharacter(ch.UnitRef);                   // Party <-> Companions (mirrors the portrait click)
            Populate(vm);
            Tts.Speak(Loc.T("group.moved", new { name = ch.UnitRef.Value.CharacterName,
                dest = inParty ? vm.RemoteHeader : vm.PartyHeader }));
            FocusAfterMove(fromList, idx);
        }

        // Stay in the list the player was working in, on the row the moved character vacated (now the next
        // one), clamped. If that list emptied (all companions added), fall through to the other list / Accept.
        private void FocusAfterMove(ListContainer fromList, int idx)
        {
            if (fromList.Children.Count > 0)
            {
                if (idx < 0) idx = 0;
                else if (idx >= fromList.Children.Count) idx = fromList.Children.Count - 1;
                Navigation.Focus(fromList.Children[idx]);
                return;
            }
            var other = fromList == _party ? _remote : _party;
            if (other != null && other.Children.Count > 0) Navigation.Focus(other.Children[0]);
            else if (_accept != null) Navigation.Focus(_accept);
        }

        // Escape = cancel and stay in the area (Close), but only when the current party is valid — mirroring
        // the game's X, which is shown only then; otherwise tell the player to pick a valid party.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                if (vm.CloseCondition()) vm.Close();
                else Tts.Speak(Loc.T("group.cant_close"));
            });
        }
    }
}
