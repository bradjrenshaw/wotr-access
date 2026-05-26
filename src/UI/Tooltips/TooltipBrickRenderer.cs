using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // CharInfoStatVM
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Converts one game tooltip-brick VM into nav <see cref="UIElement"/>s. Each brick type has
    /// its own renderer (registered in <see cref="TooltipBrickRegistry"/>), keeping its logic in
    /// one place. Two forms: <c>Expanded</c> (granular — e.g. one line per skill) and <c>Flat</c>
    /// (condensed — e.g. all skills in a single line); the caller picks per context. Flat defaults
    /// to expanded, so single-element bricks only implement one method.
    /// </summary>
    public abstract class TooltipBrickRenderer
    {
        public abstract Type BrickType { get; }
        public abstract IEnumerable<UIElement> GetExpandedElements(TooltipBaseBrickVM vm);
        public abstract IEnumerable<UIElement> GetFlatElements(TooltipBaseBrickVM vm);

        /// <summary>
        /// The tree nodes for this brick (the new tooltip model). Default bridges the legacy element
        /// output — each element becomes a leaf carrying its text + any drill-in tooltip — so a
        /// renderer works in the tree before it's converted. Override to emit real structure (a group
        /// with children, a row node, a feature node with a lazy write-up, …).
        /// </summary>
        public virtual IEnumerable<TooltipNode> GetNodes(TooltipBaseBrickVM vm)
        {
            foreach (var el in GetExpandedElements(vm))
            {
                if (el == null) continue;
                var text = el.GetLabelText();
                if (string.IsNullOrWhiteSpace(text)) continue;
                yield return TooltipNode.Leaf(text, drillIn: el.GetTooltipTemplate());
            }
        }

        // ---- shared formatting helpers ----

        /// <summary>"Label: Value"; falls back to the icon's sprite name when there's no text label.</summary>
        protected static string Stat(string name, string value, Sprite icon)
        {
            string label = !string.IsNullOrEmpty(name) ? name : (icon != null ? icon.name : null);
            if (string.IsNullOrEmpty(label)) return value;
            if (string.IsNullOrEmpty(value)) return label;
            return label + ": " + value;
        }

        protected static string Join(params string[] parts) =>
            string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        protected static string StatLine(CharInfoStatVM s)
        {
            if (s == null) return null;
            string name = s.Name != null ? s.Name.Value : null;
            string val = (s.StringValue != null && !string.IsNullOrEmpty(s.StringValue.Value))
                ? s.StringValue.Value
                : s.StatValue.Value.ToString();
            if (string.IsNullOrEmpty(name)) return val;
            return name + ": " + val;
        }

        protected static IEnumerable<UIElement> One(string text, TooltipBaseTemplate tooltip = null)
        {
            yield return new TextElement(text, null, tooltip);
        }

        protected static readonly IEnumerable<UIElement> None = Array.Empty<UIElement>();

        /// <summary>One leaf node per non-empty line of <paramref name="text"/> — game text bricks
        /// often pack a list (e.g. class skills) into a single brick separated by newlines, which a
        /// single node would flatten into one spoken run.</summary>
        protected static IEnumerable<TooltipNode> Lines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var part in text.Split('\n'))
            {
                var line = part.Trim();
                if (line.Length > 0) yield return TooltipNode.Leaf(line);
            }
        }
    }

    /// <summary>Typed base: subclasses read the concrete VM without casting.</summary>
    public abstract class TooltipBrickRenderer<TVM> : TooltipBrickRenderer where TVM : TooltipBaseBrickVM
    {
        public sealed override Type BrickType => typeof(TVM);
        public sealed override IEnumerable<UIElement> GetExpandedElements(TooltipBaseBrickVM vm) => GetExpandedElements((TVM)vm);
        public sealed override IEnumerable<UIElement> GetFlatElements(TooltipBaseBrickVM vm) => GetFlatElements((TVM)vm);

        public abstract IEnumerable<UIElement> GetExpandedElements(TVM vm);
        public virtual IEnumerable<UIElement> GetFlatElements(TVM vm) => GetExpandedElements(vm);
    }
}
