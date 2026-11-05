using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Migrating
{
    /// <summary>
    /// Design token migration: <c>var(--old)</c> uses of the old token names that Design v2 keeps as aliases (map "Tokens" entries with
    /// "Safe": true, ex: <c>--sw-bg-1</c> = <c>--sw-surface</c>, <c>--sw-muted</c> = <c>--sw-text-2</c>) become the new names. The value is
    /// identical (the alias is defined as <c>var(--new)</c>), so the rename is safe. Only <c>var()</c> references are touched: in css
    /// (comments ignored), <c>style</c> attributes and JavaScript string / template literals; definitions (<c>--old: value</c>, an app
    /// override of an alias) are left to the design report (they no longer restyle the components).
    /// </summary>
    public static class TokenMigrator
    {
        static readonly Regex VarUse = new Regex(@"var\(\s*(?<n>--[A-Za-z0-9_-]+)\s*(?=[,)])", RegexOptions.CultureInvariant);

        /// <summary>
        /// Rename the alias token uses in a css range (a .css file, a style element or a style attribute value).
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="map">The map</param>
        /// <param name="edits">Receives the edits</param>
        public static void AnalyzeCss(String text, int start, int end, MigrationMap map, List<TextEdit> edits, ISet<String> skip = null, bool legacy = false)
        {
            if ((map.Tokens.Count == 0) || (end <= start))
                return;
            //  Comments blanked (same length, so offsets stay)
            var s = Regex.Replace(text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);
            if (legacy)
                RenameLegacy(s, start, map, edits, skip);
            else
                Rename(s, start, map, edits, skip);
        }

        /// <summary>
        /// "migrate --level design": rename the uses of the legacy theme variables that have one context-free role token (map "Tokens"
        /// entries with "Apply": true, ex: <c>var(--ThemeAcc2)</c> to <c>var(--sw-accent2)</c>, <c>var(--ThemeAcc1)</c> in <c>color</c> to
        /// <c>var(--sw-text-accent)</c>) in a css range.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="start">Start offset of the css range</param>
        /// <param name="end">End offset of the css range</param>
        /// <param name="map">The map</param>
        /// <param name="edits">Receives the edits</param>
        /// <param name="skip">Old names that the run defines below the root (not renamed)</param>
        public static void AnalyzeLegacyCss(String text, int start, int end, MigrationMap map, List<TextEdit> edits, ISet<String> skip = null) =>
            AnalyzeCss(text, start, end, map, edits, skip, true);

        /// <summary>
        /// "migrate --level design": <see cref="AnalyzeLegacyCss"/> for the string and template literals of a script.
        /// </summary>
        /// <param name="text">The text the tokens come from</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="map">The map</param>
        /// <param name="edits">Receives the edits</param>
        /// <param name="skip">Old names that the run defines below the root (not renamed)</param>
        public static void AnalyzeLegacyJs(String text, IEnumerable<JsToken> tokens, MigrationMap map, List<TextEdit> edits, ISet<String> skip = null)
        {
            if (map.Tokens.Count == 0)
                return;
            foreach (var t in tokens)
                if (((t.Kind == JsTokenKind.String) || t.IsTemplate) && (t.Length > 6))
                    RenameLegacy(text.Substring(t.Start, t.Length), t.Start, map, edits, skip);
        }

        /// <summary>
        /// The css property that a position of a css text belongs to ("color" for "a { color: var(--x) }"), null if none.
        /// </summary>
        static String PropertyAt(String s, int index)
        {
            int b = index - 1;
            while ((b >= 0) && (";{}\"'`\n".IndexOf(s[b]) < 0))
                --b;
            var seg = s.Substring(b + 1, index - b - 1);
            int colon = seg.IndexOf(':');
            if (colon <= 0)
                return null;
            var p = seg.Substring(0, colon).Trim().ToLowerInvariant();
            return Regex.IsMatch(p, @"^-?[a-z][a-z0-9-]*$") ? p : null;
        }

        static void RenameLegacy(String s, int offset, MigrationMap map, List<TextEdit> edits, ISet<String> skip)
        {
            if (s.IndexOf("var(", StringComparison.Ordinal) < 0)
                return;
            foreach (Match m in VarUse.Matches(s))
            {
                var g = m.Groups["n"];
                if (!map.Tokens.TryGetValue(g.Value, out var te) || ((skip != null) && skip.Contains(te.Old)))
                    continue;
                var prop = PropertyAt(s, m.Index);
                if (!te.AppliesTo(prop))
                    continue;
                //  The fallback of a role token (var(--sw-accent1, var(--ThemeAcc1))) is never used where sw/ui.css is loaded: left as is
                if (Checking.DesignChecker.IsSwFallback(s, m.Index))
                    continue;
                var nn = te.NewFor(prop);
                edits.Add(new TextEdit
                {
                    Start = offset + g.Index,
                    End = offset + g.Index + g.Length,
                    Text = nn,
                    OldDisplay = "var(" + te.Old + ")",
                    NewDisplay = "var(" + nn + ")",
                    Note = "Design v2 role token of the legacy theme variable" + (prop == null ? "" : (" in \"" + prop + "\"")),
                });
            }
        }

        /// <summary>
        /// Rename the alias token uses inside JavaScript string and template literals (inline style text such as
        /// <c>"color: var(--sw-muted)"</c>).
        /// </summary>
        /// <param name="text">The text the tokens come from</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="map">The map</param>
        /// <param name="edits">Receives the edits</param>
        public static void AnalyzeJs(String text, IEnumerable<JsToken> tokens, MigrationMap map, List<TextEdit> edits, ISet<String> skip = null)
        {
            if (map.Tokens.Count == 0)
                return;
            foreach (var t in tokens)
                if (((t.Kind == JsTokenKind.String) || t.IsTemplate) && (t.Length > 6))
                    Rename(text.Substring(t.Start, t.Length), t.Start, map, edits, skip);
        }

        static readonly Regex Definition = new Regex(@"(?<![\w-])(?<n>--[A-Za-z0-9_-]+)\s*:|setProperty\(\s*['""`](?<n>--[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Add the safe alias tokens that a text defines (<c>--sw-touch: 44px</c> in css or a style attribute,
        /// <c>style.setProperty("--sw-touch", ...)</c> in a script). Their uses are not renamed in the whole run: a use of the alias inside
        /// the scope of such an override reads the override, the new name wouldn't (reported by the design rules instead).
        /// </summary>
        /// <param name="text">The text (any file kind)</param>
        /// <param name="map">The map</param>
        /// <param name="defined">Receives the old names</param>
        public static void FindDefinitions(String text, MigrationMap map, ISet<String> defined)
        {
            if ((map.Tokens.Count == 0) || (text.IndexOf("--", StringComparison.Ordinal) < 0))
                return;
            foreach (Match m in Definition.Matches(text))
            {
                var n = m.Groups["n"].Value;
                if (!map.Tokens.TryGetValue(n, out var te))
                    continue;
                //  A legacy theme variable set on the root (an app theme: ":root { --ThemeAcc1: ... }") is read by the role token as well
                //  (--sw-accent1 = var(--ThemeAcc1) on :root), so only a definition below the root blocks the renames
                if (te.Safe || ((te.Apply || (te.TextNew != null)) && !IsRootDefinition(text, m.Index)))
                    defined.Add(n);
            }
        }

        static readonly Regex RootSelector = new Regex(@"^(?:(?::root|html)(?:\[[^\]]*\]|:[a-z-]+(?:\([^)]*\))?)*\s*(?:,\s*|$))+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// True if a css declaration at an offset is in a rule whose selector is only the root (<c>:root</c>, <c>html</c>, with attribute /
        /// pseudo class parts like <c>:root[data-theme="x"]</c>).
        /// </summary>
        static bool IsRootDefinition(String text, int at)
        {
            if (text[at] == 's')
                return false;   // style.setProperty: unknown element
            int open = text.LastIndexOf('{', at);
            if ((open < 0) || (text.LastIndexOf('}', at) > open))
                return false;
            int b = open - 1;
            while ((b >= 0) && ("{};".IndexOf(text[b]) < 0))
                --b;
            var sel = Regex.Replace(text.Substring(b + 1, open - b - 1), @"/\*.*?\*/", " ", RegexOptions.Singleline).Trim();
            return (sel.Length > 0) && RootSelector.IsMatch(sel);
        }

        static void Rename(String s, int offset, MigrationMap map, List<TextEdit> edits, ISet<String> skip)
        {
            if (s.IndexOf("var(", StringComparison.Ordinal) < 0)
                return;
            foreach (Match m in VarUse.Matches(s))
            {
                var g = m.Groups["n"];
                if (!map.Tokens.TryGetValue(g.Value, out var te) || !te.Safe || ((skip != null) && skip.Contains(te.Old)))
                    continue;
                edits.Add(new TextEdit
                {
                    Start = offset + g.Index,
                    End = offset + g.Index + g.Length,
                    Text = te.New,
                    OldDisplay = "var(" + te.Old + ")",
                    NewDisplay = "var(" + te.New + ")",
                    Note = "Design v2 token" + (te.Note == null ? "" : (": " + te.Note)),
                });
            }
        }
    }
}
