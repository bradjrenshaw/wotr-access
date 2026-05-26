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
        // Sub/superscripts are decorative (e.g. the per-level BAB shows iterative-attack indices as
        // "<sub><size=125%> 1 </size></sub>"); their content is noise in speech, so drop tag AND text.
        private static readonly Regex SubSup =
            new Regex("<(sub|sup)>.*?</(sub|sup)>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");   // remove sub/superscript blocks entirely (content included)
            // Strip remaining tags to nothing: real spaces in the text are preserved, and tags
            // are usually inline (e.g. a drop-cap "<size=200%>N</size>ew Game"), so a
            // space here would wrongly split words into "N ew Game".
            s = RichTextTag.Replace(s, "");
            s = Whitespace.Replace(s, " ");
            return s.Trim();
        }
    }
}
