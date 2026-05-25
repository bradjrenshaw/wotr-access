using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Walks any game TooltipBaseTemplate (header → body → footer) into an ordered list of
    /// navigable brick elements via <see cref="TooltipBrickFactory"/>. Bricks with no readable
    /// text are dropped. Used by the tooltip reader screen.
    /// </summary>
    public static class TooltipReader
    {
        public static List<ProxyTooltipBrick> Build(TooltipBaseTemplate template)
        {
            var result = new List<ProxyTooltipBrick>();
            if (template == null) return result;

            try { template.Prepare(TooltipTemplateType.Tooltip); }
            catch (Exception e) { Main.Log?.Error("TooltipReader.Prepare: " + e.Message); }

            AddSection(() => template.GetHeader(TooltipTemplateType.Tooltip), result);
            AddSection(() => template.GetBody(TooltipTemplateType.Tooltip), result);
            AddSection(() => template.GetFooter(TooltipTemplateType.Tooltip), result);
            return result;
        }

        private static void AddSection(Func<IEnumerable<ITooltipBrick>> get, List<ProxyTooltipBrick> result)
        {
            IEnumerable<ITooltipBrick> bricks;
            try { bricks = get(); }
            catch (Exception e) { Main.Log?.Error("TooltipReader brick fetch: " + e.Message); return; }
            if (bricks == null) return;

            foreach (var brick in bricks)
            {
                if (brick == null) continue;
                ProxyTooltipBrick element;
                try { element = TooltipBrickFactory.Create(brick.GetVM()); }
                catch (Exception e) { Main.Log?.Error("TooltipReader render: " + e.Message); continue; }
                if (element != null && !string.IsNullOrWhiteSpace(element.GetText()))
                    result.Add(element);
            }
        }
    }
}
