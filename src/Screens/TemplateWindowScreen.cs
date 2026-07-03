using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using WrathAccess.UI;
using WrathAccess.UI.Tooltips; // TooltipFlowBuilder

namespace WrathAccess.Screens
{
    /// <summary>
    /// Shared base for the game's persistent template windows on <c>CommonVM.TooltipContextVM</c> — the Info
    /// window (item Details / glossary, <see cref="InfoWindowScreen"/>) and the unit Inspect window
    /// (<see cref="InspectScreen"/>). Each is a real modal we must read and close: a subclass points at the
    /// live window object (<see cref="Window"/>, null = closed), supplies its templates, and says how to
    /// close it; the base renders the templates with <see cref="TooltipFlowBuilder"/> (so headings + glossary
    /// drill-in work) and maps Back to the close. Layer 30, Exclusive, rebuilds on a window swap (e.g.
    /// glossary→info) like <see cref="MessageModalScreen"/>.
    /// </summary>
    public abstract class TemplateWindowScreen : Screen
    {
        protected TemplateWindowScreen() { Wrap = true; }

        public override int Layer => 30;
        public override bool Exclusive => true;

        /// <summary>The live window VM (null = closed) — also the rebuild-on-swap key.</summary>
        protected abstract object Window { get; }
        /// <summary>The templates this window currently shows (usually one).</summary>
        protected abstract IEnumerable<TooltipBaseTemplate> Templates();
        /// <summary>Run the window's own close (dispose + refocus); the reactive then nulls and we pop.</summary>
        protected abstract void CloseWindow();

        public override bool IsActive() => Window != null;

        private object _builtFrom;

        public override void OnPush() { _builtFrom = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; }

        public override void OnUpdate()
        {
            var w = Window;
            if (w != null && !ReferenceEquals(w, _builtFrom))
            {
                // Window VM swapped (e.g. drilled glossary→info) — re-home focus.
                Rebuild();
                Navigation.Attach(this);
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back/Escape runs the window's own close callback; the reactive nulls, IsActive goes false, we pop.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => { if (Window != null) CloseWindow(); });
        }

        private void Rebuild()
        {
            Clear();
            _builtFrom = Window;
            if (Window == null) return;

            // Each template becomes a FlowSheet via our normal tooltip pipeline (incl. glossary drill-in).
            int added = 0;
            var templates = Templates();
            if (templates != null)
                foreach (var t in templates)
                {
                    if (t == null) continue;
                    var sheet = TooltipFlowBuilder.Build(t, includeEmptyNotice: false);
                    if (sheet.RowCount > 0) { Add(sheet); added++; }
                }
            if (added == 0) Add(new TextElement(() => Loc.T("tooltip.empty")));
        }
    }
}
