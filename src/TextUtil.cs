using System.Text.RegularExpressions;

namespace WrathAccess
{
    /// <summary>
    /// Cleans game-sourced strings for speech. WotR UI text is TMP rich text —
    /// labels come pre-wrapped in tags (color/size/sprite/style, e.g. the main
    /// menu's "saber book" formatting), so we strip tags before speaking.
    /// </summary>
    public static class TextUtil
    {
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Strip tags to nothing: real spaces in the text are preserved, and tags
            // are usually inline (e.g. a drop-cap "<size=200%>N</size>ew Game"), so a
            // space here would wrongly split words into "N ew Game".
            s = RichTextTag.Replace(s, "");
            s = Whitespace.Replace(s, " ");
            return s.Trim();
        }
    }
}
