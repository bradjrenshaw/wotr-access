using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Maps each game tooltip-brick VM type to the mod brick that reads it. Adding support for a
    /// new brick = write a ProxyTooltipBrick subclass and add one Register line here. Unregistered
    /// types fall back to reflection (and are logged once) so nothing is silently dropped.
    /// </summary>
    public static class TooltipBrickFactory
    {
        private static readonly Dictionary<Type, Func<TooltipBaseBrickVM, ProxyTooltipBrick>> _map =
            new Dictionary<Type, Func<TooltipBaseBrickVM, ProxyTooltipBrick>>();
        private static readonly HashSet<Type> _loggedUnknown = new HashSet<Type>();

        static TooltipBrickFactory() { RegisterDefaults(); }

        public static void Register<TVM>(Func<TVM, ProxyTooltipBrick> create) where TVM : TooltipBaseBrickVM
            => _map[typeof(TVM)] = vm => create((TVM)vm);

        public static ProxyTooltipBrick Create(TooltipBaseBrickVM vm)
        {
            if (vm == null) return null;
            if (_map.TryGetValue(vm.GetType(), out var create)) return create(vm);

            if (_loggedUnknown.Add(vm.GetType()))
                Main.Log?.Log("TooltipBrickFactory: no reader for " + vm.GetType().Name + " — using reflection fallback.");
            return new FallbackTooltipBrick(vm);
        }

        private static void RegisterDefaults()
        {
            Register<TooltipBrickTextVM>(vm => new TextTooltipBrick(vm));
            Register<TooltipBrickColorizedTextVM>(vm => new ColorizedTextTooltipBrick(vm));
            Register<TooltipBrickTitleVM>(vm => new TitleTooltipBrick(vm));
            Register<TooltipBrickDoubleTextVM>(vm => new DoubleTextTooltipBrick(vm));
            Register<TooltipBrickTripleTextVM>(vm => new TripleTextTooltipBrick(vm));
            Register<TooltipBrickIconAndNameVM>(vm => new IconAndNameTooltipBrick(vm));
            Register<TooltipBrickPortraitAndNameVM>(vm => new PortraitAndNameTooltipBrick(vm));
            Register<TooltipBrickIconNameDescVM>(vm => new IconNameDescTooltipBrick(vm));
            Register<TooltipBrickFeatureShortDescriptionVM>(vm => new FeatureShortDescriptionTooltipBrick(vm));
            Register<TooltipBrickEntityHeaderVM>(vm => new EntityHeaderTooltipBrick(vm));
            Register<TooltipBrickIconValueStatVM>(vm => new IconValueStatTooltipBrick(vm));
            Register<TooltipBrickMultipleIconValueStatVM>(vm => new MultipleIconValueStatTooltipBrick(vm));
            Register<TooltipBrickValueStatFormulaVM>(vm => new ValueStatFormulaTooltipBrick(vm));
            Register<TooltipBrickTwoColumnsStatVM>(vm => new TwoColumnsStatTooltipBrick(vm));
            Register<TooltipBrickPictureVM>(vm => new PictureTooltipBrick(vm));

            // Pure layout — nothing to read.
            Register<TooltipBrickSeparatorVM>(_ => new SkipTooltipBrick());
            Register<TooltipBrickSpaceVM>(_ => new SkipTooltipBrick());
        }
    }
}
