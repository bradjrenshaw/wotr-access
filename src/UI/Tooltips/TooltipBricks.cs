using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    // One class per game brick VM type. Each just maps the brick's fields to a readable line.
    // Register new ones in TooltipBrickFactory.RegisterDefaults.

    public sealed class TextTooltipBrick : ProxyTooltipBrick<TooltipBrickTextVM>
    {
        public TextTooltipBrick(TooltipBrickTextVM vm) : base(vm) { }
        public override string GetText() => Vm?.Text;
    }

    public sealed class ColorizedTextTooltipBrick : ProxyTooltipBrick<TooltipBrickColorizedTextVM>
    {
        public ColorizedTextTooltipBrick(TooltipBrickColorizedTextVM vm) : base(vm) { }
        public override string GetText() => Vm?.Text; // TODO: convey color (penalty/bonus) later
    }

    public sealed class TitleTooltipBrick : ProxyTooltipBrick<TooltipBrickTitleVM>
    {
        public TitleTooltipBrick(TooltipBrickTitleVM vm) : base(vm) { }
        public override string GetText() => Vm?.Title;
    }

    public sealed class DoubleTextTooltipBrick : ProxyTooltipBrick<TooltipBrickDoubleTextVM>
    {
        public DoubleTextTooltipBrick(TooltipBrickDoubleTextVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null : Join(Vm.LeftLine, Vm.RightLine);
    }

    public sealed class TripleTextTooltipBrick : ProxyTooltipBrick<TooltipBrickTripleTextVM>
    {
        public TripleTextTooltipBrick(TooltipBrickTripleTextVM vm) : base(vm) { }
        public override string GetText() => Vm?.MiddleLine; // only MiddleLine is exposed publicly
    }

    public sealed class IconAndNameTooltipBrick : ProxyTooltipBrick<TooltipBrickIconAndNameVM>
    {
        public IconAndNameTooltipBrick(TooltipBrickIconAndNameVM vm) : base(vm) { }
        public override string GetText() => Vm?.Line;
    }

    public sealed class PortraitAndNameTooltipBrick : ProxyTooltipBrick<TooltipBrickPortraitAndNameVM>
    {
        public PortraitAndNameTooltipBrick(TooltipBrickPortraitAndNameVM vm) : base(vm) { }
        public override string GetText() => Vm?.Line;
    }

    public sealed class IconNameDescTooltipBrick : ProxyTooltipBrick<TooltipBrickIconNameDescVM>
    {
        public IconNameDescTooltipBrick(TooltipBrickIconNameDescVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null : Join(Vm.Name, Vm.Desc);
    }

    public sealed class FeatureShortDescriptionTooltipBrick : ProxyTooltipBrick<TooltipBrickFeatureShortDescriptionVM>
    {
        public FeatureShortDescriptionTooltipBrick(TooltipBrickFeatureShortDescriptionVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null : Join(Vm.Name, Vm.Description);
    }

    public sealed class EntityHeaderTooltipBrick : ProxyTooltipBrick<TooltipBrickEntityHeaderVM>
    {
        public EntityHeaderTooltipBrick(TooltipBrickEntityHeaderVM vm) : base(vm) { }
        public override string GetText() =>
            Vm == null ? null : Join(Vm.MainTitle, Vm.Title, Vm.LeftLabel, Vm.RightLabel);
    }

    public sealed class IconValueStatTooltipBrick : ProxyTooltipBrick<TooltipBrickIconValueStatVM>
    {
        public IconValueStatTooltipBrick(TooltipBrickIconValueStatVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null : Stat(Vm.Name, Vm.Value, Vm.Icon);
    }

    public sealed class MultipleIconValueStatTooltipBrick : ProxyTooltipBrick<TooltipBrickMultipleIconValueStatVM>
    {
        public MultipleIconValueStatTooltipBrick(TooltipBrickMultipleIconValueStatVM vm) : base(vm) { }
        public override string GetText()
        {
            var stats = Vm?.TooltipBrickIconValueStats;
            if (stats == null) return null;
            return Join(stats.Where(s => s != null).Select(s => Stat(s.Name, s.Value, s.Icon)).ToArray());
        }
    }

    public sealed class ValueStatFormulaTooltipBrick : ProxyTooltipBrick<TooltipBrickValueStatFormulaVM>
    {
        public ValueStatFormulaTooltipBrick(TooltipBrickValueStatFormulaVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null : Stat(Vm.Name, Vm.Value, null);
    }

    public sealed class TwoColumnsStatTooltipBrick : ProxyTooltipBrick<TooltipBrickTwoColumnsStatVM>
    {
        public TwoColumnsStatTooltipBrick(TooltipBrickTwoColumnsStatVM vm) : base(vm) { }
        public override string GetText() => Vm == null ? null :
            Join(Stat(Vm.NameLeft, Vm.ValueLeft, Vm.IconLeft), Stat(Vm.NameRight, Vm.ValueRight, Vm.IconRight));
    }

    public sealed class PictureTooltipBrick : ProxyTooltipBrick<TooltipBrickPictureVM>
    {
        public PictureTooltipBrick(TooltipBrickPictureVM vm) : base(vm) { }
        // Image-only brick (encyclopedia art, etc.) — surface the sprite name so it isn't silent.
        public override string GetText() => Vm?.Picture != null ? "Image: " + Vm.Picture.name : null;
    }

    /// <summary>Pure layout bricks (separator/space) — nothing to read.</summary>
    public sealed class SkipTooltipBrick : ProxyTooltipBrick
    {
        public override string GetText() => null;
    }

    /// <summary>
    /// Fallback for brick types without a dedicated reader yet: reflects over the VM's public
    /// string fields/properties so the content still surfaces, while the factory logs the type so
    /// we add a proper brick class for it.
    /// </summary>
    public sealed class FallbackTooltipBrick : ProxyTooltipBrick
    {
        private readonly TooltipBaseBrickVM _vm;
        public FallbackTooltipBrick(TooltipBaseBrickVM vm) { _vm = vm; }

        public override string GetText()
        {
            if (_vm == null) return null;
            var parts = new List<string>();
            var t = _vm.GetType();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(string)) Add(parts, f.GetValue(_vm) as string);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                    Add(parts, SafeGet(p, _vm));
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        private static void Add(List<string> parts, string s)
        {
            if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
        }

        private static string SafeGet(PropertyInfo p, object o)
        {
            try { return p.GetValue(o) as string; } catch { return null; }
        }
    }
}
