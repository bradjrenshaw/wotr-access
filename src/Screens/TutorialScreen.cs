using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Tutorial; // TutorialData
using Kingmaker.UI.MVVM._VM.Tutorial;
using UnityEngine;
using WrathAccess.UI;
using WrathAccess.UI.Proxies;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A tutorial popup: reads the current page's text, offers a "don't show" checkbox and a Dismiss
    /// button. Handles BOTH window kinds off the shared <see cref="TutorialWindowVM"/> base — the modal
    /// "big" window (<see cref="TutorialModalWindowVM"/>, multi-page) and the "small"/hint window
    /// (<see cref="TutorialHintWindowVM"/>). The basic-controls tutorials (Movement/camera) are actually
    /// the *small* kind despite rendering full-size (their blueprint's Windowed flag is false).
    ///
    /// It announces the text on appearance even when Focus Mode is off (a blocking popup shouldn't be
    /// silent); navigating to the checkbox/Dismiss still needs Focus Mode. Dismiss mirrors the game's
    /// close/Esc (<c>ShowWindow.Value = false</c>); the checkbox applies <c>BanTutor()</c> on dismiss.
    /// </summary>
    public sealed class TutorialScreen : Screen
    {
        public override string Key => "ctx.tutorial";
        public override int Layer => 28; // modal popup, above gameplay/windows/settings

        private TutorialWindowVM _builtVm;
        private TutorialData.Page _builtPage;
        private bool _banOnClose;

        private static TutorialWindowVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var tv = rc?.CommonVM?.TutorialVM?.Value;
            if (tv == null) return null;
            var big = tv.BigWindowVM;
            if (big != null && big.ShowWindow.Value) return big;
            var small = tv.SmallWindowVM;
            if (small != null && small.ShowWindow.Value) return small;
            return null;
        }

        public override bool IsActive() { Diagnose(); return Vm() != null; }

        public override void OnPush()
        {
            Build();
            Main.Log?.Log("[tut] OnPush vm=" + (_builtVm != null) + " bannable=" + (_builtVm != null && _builtVm.CanBeBanned)
                + " textLen=" + PageText(_builtVm).Length);
        }
        public override void OnPop() { Clear(); _builtVm = null; _builtPage = null; _banOnClose = false; }

        public override void OnFocus()
        {
            Main.Log?.Log("[tut] OnFocus focusMode=" + FocusMode.Active);
            // ScreenManager calls AnnounceCurrent after this when focus mode is on; cover the off case
            // so a blocking tutorial is never silent.
            if (!FocusMode.Active) SpeakText();
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            if (vm != _builtVm) { _banOnClose = false; Build(); Announce(); }
            else if (CurrentPageOf(vm) != _builtPage) { Build(); Announce(); }
        }

        private void Build()
        {
            Clear();
            var vm = Vm();
            _builtVm = vm;
            _builtPage = CurrentPageOf(vm);
            if (vm == null) return;

            var list = new ListContainer("Tutorial");
            list.Add(new TextElement(() => PageText(Vm()))); // full current-page text — focus to re-read
            if (Vm() is TutorialModalWindowVM modal && modal.MultiplePages)
            {
                list.Add(new ProxyActionButton("Previous page", CanPrev, () => StepPage(-1)));
                list.Add(new ProxyActionButton("Next page", CanNext, () => StepPage(1)));
            }
            if (vm.CanBeBanned)
                list.Add(new ProxyBoolToggle((string)UIStrings.Instance.Tutorial.DontShowThisTutorial,
                    () => _banOnClose, () => _banOnClose = !_banOnClose));
            list.Add(new ProxyActionButton("Dismiss", null, Dismiss));
            Add(list);
        }

        private void Announce()
        {
            if (FocusMode.Active) { Navigation.Attach(this); Navigation.AnnounceCurrent(); }
            else SpeakText();
        }

        private void SpeakText()
        {
            var vm = Vm();
            if (vm != null) Tts.Speak("Tutorial. " + PageText(vm), interrupt: true);
        }

        private static TutorialData.Page CurrentPageOf(TutorialWindowVM vm)
        {
            if (vm is TutorialModalWindowVM m) return m.CurrentPage.Value;
            var pages = vm != null ? vm.Pages : null;
            return (pages != null && pages.Count > 0) ? pages[0] : null;
        }

        private static bool CanPrev() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value > 0;
        private static bool CanNext() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value < m.PageCount - 1;

        private static void StepPage(int dir)
        {
            if (Vm() is TutorialModalWindowVM m)
                m.CurrentPageIndex.Value = Mathf.Clamp(m.CurrentPageIndex.Value + dir, 0, m.PageCount - 1);
        }

        private void Dismiss()
        {
            var vm = Vm();
            if (vm == null) return;
            if (_banOnClose) vm.BanTutor();
            vm.ShowWindow.Value = false;
        }

        private static string PageText(TutorialWindowVM vm)
        {
            if (vm == null) return "";
            if (vm is TutorialModalWindowVM m)
            {
                var prefix = m.MultiplePages ? "Page " + (m.CurrentPageIndex.Value + 1) + " of " + m.PageCount + ". " : "";
                return prefix + FormatPage(m.CurrentPage.Value);
            }
            var pages = vm.Pages;
            if (pages == null || pages.Count == 0) return "";
            var parts = new List<string>();
            foreach (var p in pages) { var t = FormatPage(p); if (!string.IsNullOrEmpty(t)) parts.Add(t); }
            return string.Join(". ", parts.ToArray());
        }

        private static string FormatPage(TutorialData.Page page)
        {
            if (page == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(page.Title)) parts.Add(page.Title);
            if (!string.IsNullOrEmpty(page.TriggerText)) parts.Add(page.TriggerText);
            if (!string.IsNullOrEmpty(page.Description)) parts.Add(page.Description);
            if (!string.IsNullOrEmpty(page.SolutionText)) parts.Add(page.SolutionText);
            return string.Join(". ", parts.ToArray());
        }

        // Temporary: log the tutorial VM state (on change) to confirm detection.
        private static string _dbg;
        private static void Diagnose()
        {
            string sig;
            try
            {
                var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
                var cv = rc != null ? rc.CommonVM : null;
                if (rc == null) sig = "rcNull";
                else if (cv == null) sig = "commonNull";
                else
                {
                    var tv = cv.TutorialVM != null ? cv.TutorialVM.Value : null;
                    if (tv == null) sig = "tvNull";
                    else
                    {
                        var big = tv.BigWindowVM;
                        var small = tv.SmallWindowVM;
                        sig = "bigShow=" + (big != null && big.ShowWindow.Value) + " smallShow=" + (small != null && small.ShowWindow.Value)
                            + " -> active=" + (Vm() != null);
                    }
                }
            }
            catch (System.Exception e) { sig = "threw: " + e.Message; }
            if (sig != _dbg) { _dbg = sig; Main.Log?.Log("[tut] " + sig); }
        }
    }
}
