// The status-check helper Loadstar.Net keeps in TileContext.cs (navigation, not vendored here).
namespace Loadstar
{
    internal static class StatusExtensions
    {
        internal static void ThrowIfError(this Status status, string call)
        {
            if (status != Status.Ok) throw new LoadstarException(status, call);
        }
    }
}
