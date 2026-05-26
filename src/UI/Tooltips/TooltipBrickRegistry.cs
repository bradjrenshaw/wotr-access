using System;
using System.Collections.Generic;
using System.Reflection;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Maps each game tooltip-brick VM type to its renderer. Adding a brick = write a
    /// <see cref="TooltipBrickRenderer"/> and register it here. Unregistered types fall back to
    /// reflection over their public string fields (logged once) so nothing is silently dropped.
    /// </summary>
    public static class TooltipBrickRegistry
    {
        private static readonly Dictionary<Type, TooltipBrickRenderer> _map = new Dictionary<Type, TooltipBrickRenderer>();
        private static readonly HashSet<Type> _loggedUnknown = new HashSet<Type>();

        static TooltipBrickRegistry() { RegisterDefaults(); }

        public static void Register(TooltipBrickRenderer renderer) => _map[renderer.BrickType] = renderer;

        /// <summary>The nav elements for a brick — expanded (granular) or flat (condensed).</summary>
        public static IEnumerable<UIElement> Elements(TooltipBaseBrickVM vm, bool expanded)
        {
            if (vm == null) return Array.Empty<UIElement>();
            if (_map.TryGetValue(vm.GetType(), out var renderer))
                return expanded ? renderer.GetExpandedElements(vm) : renderer.GetFlatElements(vm);

            if (_loggedUnknown.Add(vm.GetType()))
                Main.Log?.Log("TooltipBrickRegistry: no renderer for " + vm.GetType().Name + " — reflection fallback.");
            return Fallback(vm);
        }

        /// <summary>The tree nodes for a brick (the new model). Renderer's GetNodes, or a leaf from the
        /// reflection fallback for unconverted/unknown types.</summary>
        public static IEnumerable<TooltipNode> Nodes(TooltipBaseBrickVM vm)
        {
            if (vm == null) return Array.Empty<TooltipNode>();
            if (_map.TryGetValue(vm.GetType(), out var renderer))
                return renderer.GetNodes(vm);

            if (_loggedUnknown.Add(vm.GetType()))
                Main.Log?.Log("TooltipBrickRegistry: no renderer for " + vm.GetType().Name + " — reflection fallback (node).");
            return FallbackNodes(vm);
        }

        private static IEnumerable<TooltipNode> FallbackNodes(TooltipBaseBrickVM vm)
        {
            foreach (var el in Fallback(vm))
            {
                var t = el.GetLabelText();
                if (!string.IsNullOrWhiteSpace(t)) yield return TooltipNode.Leaf(t);
            }
        }

        private static IEnumerable<UIElement> Fallback(TooltipBaseBrickVM vm)
        {
            var parts = new List<string>();
            var t = vm.GetType();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(string)) Add(parts, f.GetValue(vm) as string);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                    try { Add(parts, p.GetValue(vm) as string); } catch { }
            return parts.Count == 0 ? Array.Empty<UIElement>() : new UIElement[] { new TextElement(string.Join(", ", parts)) };
        }

        private static void Add(List<string> parts, string s)
        {
            if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
        }

        private static void RegisterDefaults()
        {
            Register(new TextBrickRenderer());
            Register(new ColorizedTextBrickRenderer());
            Register(new TitleBrickRenderer());
            Register(new DoubleTextBrickRenderer());
            Register(new TripleTextBrickRenderer());
            Register(new IconAndNameBrickRenderer());
            Register(new PortraitAndNameBrickRenderer());
            Register(new IconNameDescBrickRenderer());
            Register(new FeatureBrickRenderer());
            Register(new MultipleFeatureBrickRenderer());
            Register(new FeatureShortDescriptionBrickRenderer());
            Register(new EntityHeaderBrickRenderer());
            Register(new IconValueStatBrickRenderer());
            Register(new MultipleIconValueStatBrickRenderer());
            Register(new ValueStatFormulaBrickRenderer());
            Register(new TwoColumnsStatBrickRenderer());
            Register(new PictureBrickRenderer());
            Register(new ShortClassDescriptionBrickRenderer());
            Register(new AbilityScoresBlockBrickRenderer());
            Register(new SkillsBrickRenderer());
            Register(new SeparatorBrickRenderer());
            Register(new SpaceBrickRenderer());
        }
    }
}
