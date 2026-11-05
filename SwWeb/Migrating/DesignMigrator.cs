using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SwWeb.Checking;

namespace SwWeb.Migrating
{
    /// <summary>
    /// The automatic Design v2 rewrites of "migrate --level design" (the report of the other design rules is manual follow-up):
    /// a <c>box-shadow</c> that combines lengths with the complete shadow token (<c>0 0 32px var(--sw-shadow)</c>, invalid since
    /// <c>--sw-shadow</c> became a complete shadow, so the browser already ignores it) becomes <c>none</c>, the Design v2 rule for
    /// permanent surfaces. A floating layer then gets <c>var(--sw-elev-1)</c> by hand (the note says so).
    /// </summary>
    public static class DesignMigrator
    {
        static readonly Regex ShadowDecl = new Regex(@"(?<![\w-])(?:-webkit-)?box-shadow\s*:\s*(?<v>[^;{}]*?)\s*(?=;|}|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex Important = new Regex(@"\s*!important\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The marker text of the design-shadow finding that this rewrite fixes (left out of the migrate design report).
        /// </summary>
        public const String InvalidShadowMarker = "is invalid since Design v2";

        /// <summary>
        /// Rewrite the invalid elevation shadows of a css range (a .css file, a style element or a style attribute value).
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="edits">Receives the edits</param>
        public static void AnalyzeCss(String text, int start, int end, List<TextEdit> edits)
        {
            if (end <= start)
                return;
            var s = Regex.Replace(text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);
            if (s.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            foreach (Match m in ShadowDecl.Matches(s))
            {
                var g = m.Groups["v"];
                var v = g.Value;
                if (!DesignChecker.IsInvalidElevationShadow(v))
                    continue;
                var imp = Important.Match(v);
                int vs = start + g.Index, ve = start + g.Index + g.Length;
                //  The value is replaced as a whole: drop the token renames inside it (added before)
                edits.RemoveAll(e => (e.Start >= vs) && (e.End <= ve));
                edits.Add(new TextEdit
                {
                    Start = vs,
                    End = ve,
                    Text = "none" + (imp.Success ? " !important" : ""),
                    Note = "Design v2: this shadow was invalid (var(--sw-shadow) is a complete shadow), the browser ignored it; permanent surfaces have none - a floating layer (menu, popover) uses var(--sw-elev-1), a dialog / drawer / toast var(--sw-elev-2)",
                });
            }
        }
    }
}
