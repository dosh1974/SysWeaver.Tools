using System;
using System.Collections.Generic;

namespace SwWeb.Migrating
{
    /// <summary>
    /// CSS hook migration: finds old class / element selectors with a mapping (in selector context only, never in comments,
    /// strings or declaration values). Safe entries are rewritten, the others are reported. Custom properties (--Theme*) are never touched.
    /// </summary>
    public static class CssMigrator
    {
        static readonly HashSet<String> GroupRules = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "@media", "@supports", "@container", "@layer", "@document", "@scope", "@starting-style",
        };

        static bool IsIdentChar(char c) => Char.IsLetterOrDigit(c) || (c == '-') || (c == '_') || (c > 127);

        /// <summary>
        /// Analyze a CSS range.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="map">The map</param>
        /// <param name="edits">Receives the edits (safe entries)</param>
        /// <param name="items">Receives the warnings (report only entries)</param>
        /// <param name="ownUi">"upgrade": a page with its own UI (OwnUi entries are drawn there as before: info, not a warning)</param>
        public static void Analyze(String text, int start, int end, MigrationMap map, List<TextEdit> edits, List<OffsetItem> items, bool ownUi = false)
        {
            if (map.Css.Count == 0)
                return;
            var stack = new Stack<char>();
            int prelude = start;
            int firstChar = start - 1;
            int i = start;
            while (i < end)
            {
                var c = text[i];
                if ((c == '/') && (i + 1 < end) && (text[i + 1] == '*'))
                {
                    var e = text.IndexOf("*/", i + 2, end - i - 2, StringComparison.Ordinal);
                    i = e < 0 ? end : e + 2;
                    continue;
                }
                if ((c == '"') || (c == '\''))
                {
                    ++i;
                    while ((i < end) && (text[i] != c) && (text[i] != '\n'))
                    {
                        if (text[i] == '\\')
                            ++i;
                        ++i;
                    }
                    ++i;
                    continue;
                }
                if (c == '{')
                {
                    var p = text.Substring(prelude, i - prelude).Trim();
                    var sp = p.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '(' });
                    var kw = sp < 0 ? p : p.Substring(0, sp);
                    stack.Push(p.StartsWith('@') ? (GroupRules.Contains(kw) ? 'g' : 'd') : 'd');
                    prelude = ++i;
                    continue;
                }
                if (c == '}')
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    prelude = ++i;
                    continue;
                }
                if (c == ';')
                {
                    prelude = ++i;
                    continue;
                }
                bool selector = (stack.Count == 0) || (stack.Peek() == 'g');
                if (!Char.IsWhiteSpace(c) && (firstChar < prelude))
                    firstChar = i;
                if (!selector || ((firstChar >= prelude) && (text[firstChar] == '@')))
                {
                    ++i;
                    continue;
                }
                bool matched = false;
                foreach (var ce in map.Css.Values)
                {
                    var old = ce.Old;
                    bool cls = old.StartsWith('.') || old.StartsWith('#');
                    if (cls)
                    {
                        if (c != old[0])
                            continue;
                    }
                    else if ((i > start) && (IsIdentChar(text[i - 1]) || (text[i - 1] == '.') || (text[i - 1] == '#') || (text[i - 1] == ':') || (text[i - 1] == '@')))
                        continue;
                    if ((i + old.Length > end) || (String.Compare(text, i, old, 0, old.Length, cls ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) != 0))
                        continue;
                    var after = i + old.Length;
                    if ((after < end) && IsIdentChar(text[after]))
                        continue;
                    if (ce.Safe)
                        edits.Add(new TextEdit { Start = i, End = after, Text = ce.New, Note = ce.Note });
                    else
                        items.Add(new OffsetItem
                        {
                            Offset = i,
                            Kind = (ownUi && ce.OwnUi) ? MigrateItemKind.Info : MigrateItemKind.Warning,
                            Message = "CSS hook \"" + old + "\" " + ((ownUi && ce.OwnUi) ? "is still drawn on pages without sw/ui.css (pages with their own UI), with sw/ui.css the new components use \"" + ce.New + "\"" : ("is styled by the legacy compat css only; the new components use \"" + ce.New + "\" (report only, review)")) + (ce.Note == null ? "" : (": " + ce.Note)),
                        });
                    i = after;
                    matched = true;
                    break;
                }
                if (!matched)
                    ++i;
            }
        }
    }
}
