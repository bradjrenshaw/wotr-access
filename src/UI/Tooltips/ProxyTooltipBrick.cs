using System.Linq;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// One brick of a tooltip, as a navigable <see cref="TextElement"/> — the reader screen lists
    /// these and you arrow through them. Each game brick VM type gets its own subclass (registered
    /// in <see cref="TooltipBrickFactory"/>), so adding/refactoring a brick is local. Text reading,
    /// focusability, and (later) glossary-link handling are inherited from TextElement; a brick
    /// only has to map its VM's fields to text via <see cref="GetText"/>.
    /// </summary>
    public abstract class ProxyTooltipBrick : TextElement
    {
        public abstract override string GetText();

        // ---- shared formatting helpers for brick subclasses ----

        /// <summary>A stat line "Label: Value". When there's no text label, falls back to the
        /// icon's sprite name so icon-only stats aren't lost (proper icon→label map comes later).</summary>
        protected static string Stat(string name, string value, Sprite icon)
        {
            string label = !string.IsNullOrEmpty(name) ? name : IconLabel(icon);
            if (string.IsNullOrEmpty(label)) return value;
            if (string.IsNullOrEmpty(value)) return label;
            return label + ": " + value;
        }

        protected static string IconLabel(Sprite icon) => icon != null ? icon.name : null;

        protected static string Join(params string[] parts) =>
            string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Typed base: holds the concrete brick VM so subclasses read it without casting.</summary>
    public abstract class ProxyTooltipBrick<TVM> : ProxyTooltipBrick where TVM : TooltipBaseBrickVM
    {
        protected readonly TVM Vm;
        protected ProxyTooltipBrick(TVM vm) { Vm = vm; }
    }
}
