using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Migrating
{
    /// <summary>
    /// The include file kinds handled by the HTML migration.
    /// </summary>
    public enum IncludeKind
    {
        /// <summary>Not an include of interest.</summary>
        None,
        /// <summary>common/common.js (legacy).</summary>
        CommonJs,
        /// <summary>app/application.js (legacy).</summary>
        AppJs,
        /// <summary>app/application.css (legacy).</summary>
        AppCss,
        /// <summary>common/theme.css (kept).</summary>
        ThemeCss,
        /// <summary>sw/core.js.</summary>
        CoreJs,
        /// <summary>sw/ui.js.</summary>
        UiJs,
        /// <summary>sw/shell.js.</summary>
        ShellJs,
        /// <summary>sw/ui.css.</summary>
        UiCss,
        /// <summary>sw/shell.css.</summary>
        ShellCss,
        /// <summary>sw/compat.js (legacy names in a migrated new-format page).</summary>
        CompatJs,
        /// <summary>sw/compat.css (legacy styles of a migrated new-format page).</summary>
        CompatCss,
    }

    /// <summary>
    /// What is known about a script referenced by a page (resolved by the caller).
    /// </summary>
    public sealed class PageScript
    {
        /// <summary>
        /// The analysis of the script (null if not analyzed).
        /// </summary>
        public JsAnalysis Analysis;
        /// <summary>
        /// The relative path of the script (null if not resolved).
        /// </summary>
        public String Rel;
        /// <summary>
        /// Why it couldn't be analyzed (null = ok or nothing to say).
        /// </summary>
        public String Problem;
    }

    /// <summary>
    /// The result of migrating a HTML page.
    /// </summary>
    public sealed class HtmlResult
    {
        /// <summary>
        /// Edits (JavaScript renames in inline scripts and event handlers, include tags, safe css).
        /// </summary>
        public readonly List<TextEdit> Edits = new List<TextEdit>();
        /// <summary>
        /// Info / warning items.
        /// </summary>
        public readonly List<OffsetItem> Items = new List<OffsetItem>();
        /// <summary>
        /// True if the page includes legacy bundles.
        /// </summary>
        public bool IsLegacyPage;
        /// <summary>
        /// True if the include tags were rewritten.
        /// </summary>
        public bool IncludesMigrated;
        /// <summary>
        /// True if the includes were kept because legacy globals remain (--keep-legacy).
        /// </summary>
        public bool Blocked;
        /// <summary>
        /// True if the page was migrated with the compatibility bundle sw/compat.js (+ sw/compat.css) because legacy globals remain.
        /// </summary>
        public bool UsesCompat;
        /// <summary>
        /// The legacy names that remain (name to where it is used); empty if none.
        /// </summary>
        public readonly SortedDictionary<String, String> Unresolved = new SortedDictionary<String, String>(StringComparer.Ordinal);
        /// <summary>
        /// True if the page needs the shell (sw/shell.js).
        /// </summary>
        public bool UsesShell;
    }

    /// <summary>
    /// HTML page migration: JavaScript renames in inline scripts / on* attributes, and the include tags
    /// (common.js -> sw/core.js + sw/ui.js, application.js -> sw/shell.js or removed, application.css -> sw/ui.css (+ sw/shell.css)).
    /// Pages that still use legacy names after the renames also get sw/compat.js (after sw/ui.js) and sw/compat.css (after sw/ui.css / sw/shell.css),
    /// unless --keep-legacy keeps them on the legacy bundles or --force-includes drops the support of the legacy names.
    /// </summary>
    public static partial class HtmlMigrator
    {
        static readonly (String Suffix, IncludeKind Kind)[] Suffixes =
        {
            ("common/common.js", IncludeKind.CommonJs),
            ("app/application.js", IncludeKind.AppJs),
            ("app/application.css", IncludeKind.AppCss),
            ("common/theme.css", IncludeKind.ThemeCss),
            ("sw/core.js", IncludeKind.CoreJs),
            ("sw/ui.js", IncludeKind.UiJs),
            ("sw/shell.js", IncludeKind.ShellJs),
            ("sw/ui.css", IncludeKind.UiCss),
            ("sw/shell.css", IncludeKind.ShellCss),
            ("sw/compat.js", IncludeKind.CompatJs),
            ("sw/compat.css", IncludeKind.CompatCss),
        };

        /// <summary>
        /// Classify an include url: the kind, the prefix before the known suffix (kept as is, ex: "../", "${@Base Url}") and the query / hash.
        /// </summary>
        /// <param name="url">The raw attribute value</param>
        /// <returns>The kind (None if unknown), prefix and query</returns>
        public static (IncludeKind Kind, String Prefix, String Query) Classify(String url)
        {
            if (url == null)
                return (IncludeKind.None, null, null);
            var v = url.Trim();
            var q = v.IndexOfAny(new[] { '?', '#' });
            var query = "";
            if (q >= 0)
            {
                query = v.Substring(q);
                v = v.Substring(0, q);
            }
            foreach (var (suffix, kind) in Suffixes)
            {
                if (!v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var prefix = v.Substring(0, v.Length - suffix.Length);
                if ((prefix.Length == 0) || prefix.EndsWith('/') || prefix.EndsWith('}') || prefix.EndsWith(')'))
                    return (kind, prefix, query);
            }
            return (IncludeKind.None, null, null);
        }

        /// <summary>
        /// Test if an url contains a template placeholder (<c>${...}</c> or <c>$(...)</c>).
        /// </summary>
        public static bool IsTemplated(String url) => (url != null) && (url.Contains("${") || url.Contains("$("));

        /// <summary>
        /// An include element (tag plus its end tag for scripts).
        /// </summary>
        sealed class Include
        {
            public IncludeKind Kind;
            public HtmlNode Tag;
            public HtmlAttribute Url;
            public String Prefix;
            public String Query;
            public int ElemEnd;
        }

        /// <summary>
        /// Detect the line ending used by a text (CRLF if none).
        /// </summary>
        public static String DetectEol(String text)
        {
            var i = text.IndexOf('\n');
            if (i < 0)
                return text.Contains('\r') ? "\r" : "\r\n";
            return (i > 0) && (text[i - 1] == '\r') ? "\r\n" : "\n";
        }

        static int LineStart(String text, int pos)
        {
            while ((pos > 0) && (text[pos - 1] != '\n') && (text[pos - 1] != '\r'))
                --pos;
            return pos;
        }

        static String IndentOf(String text, int pos)
        {
            var ls = LineStart(text, pos);
            var ind = text.Substring(ls, pos - ls);
            return ind.All(c => (c == ' ') || (c == '\t')) ? ind : "";
        }

        /// <summary>
        /// The range to remove for an element: its whole line (with the line ending) if nothing else is on it.
        /// </summary>
        static (int Start, int End) RemovalRange(String text, int start, int end)
        {
            var ls = LineStart(text, start);
            if (!text.Substring(ls, start - ls).All(c => (c == ' ') || (c == '\t')))
                return (start, end);
            int e = end;
            while ((e < text.Length) && ((text[e] == ' ') || (text[e] == '\t')))
                ++e;
            if ((e < text.Length) && (text[e] == '\r'))
            {
                ++e;
                if ((e < text.Length) && (text[e] == '\n'))
                    ++e;
                return (ls, e);
            }
            if ((e < text.Length) && (text[e] == '\n'))
                return (ls, e + 1);
            if (e >= text.Length)
                return (ls, e);
            return (start, end);
        }

        /// <summary>
        /// Copy an element's text with the url attribute value replaced.
        /// </summary>
        static String WithUrl(String text, Include inc, String newUrl)
        {
            var a = inc.Url;
            return text.Substring(inc.Tag.Start, a.ValueStart - inc.Tag.Start) + newUrl + text.Substring(a.ValueStart + a.Value.Length, inc.ElemEnd - (a.ValueStart + a.Value.Length));
        }

        /// <summary>
        /// Migrate a HTML page.
        /// </summary>
        /// <param name="text">The page text</param>
        /// <param name="map">The map</param>
        /// <param name="options">The options</param>
        /// <param name="runGlobals">Legacy names declared as page globals in the run</param>
        /// <param name="resolve">Resolves a script src to what is known about it</param>
        /// <param name="lineOf">Offset to line (messages)</param>
        /// <returns>The result</returns>
        public static HtmlResult Migrate(String text, MigrationMap map, MigrateOptions options, ISet<String> runGlobals, Func<String, PageScript> resolve, Func<int, int> lineOf)
        {
            var res = new HtmlResult();
            var nodes = HtmlScanner.Scan(text);
            var scripts = HtmlScanner.GetScripts(nodes);
            var level = options.Level;
            var unresolved = res.Unresolved;
            var shell = new SortedSet<String>(StringComparer.Ordinal);

            void Take(JsAnalysis a, String where, bool edits)
            {
                if (edits)
                    res.Edits.AddRange(a.Edits);
                if (where == null)
                    res.Items.AddRange(a.Items);
                foreach (var kv in a.Unresolved)
                    unresolved.TryAdd(kv.Key, where ?? ("line " + lineOf(kv.Value)));
                foreach (var s in a.ShellNames)
                    shell.Add(s);
            }

            // Inline scripts and event handler attributes
            foreach (var sc in scripts)
                if (sc.IsJavaScript && (sc.Src == null) && (sc.BodyEnd > sc.BodyStart))
                {
                    var toks = JsLexer.Tokenize(text, sc.BodyStart, sc.BodyEnd);
                    Take(JsMigrator.Analyze(new JsScope(text, toks), map, level, runGlobals, lineOf, options.FixTranslations), null, true);
                    if (level >= MigrateLevel.Api)
                        TokenMigrator.AnalyzeJs(text, toks, map, res.Edits, options.DefinedTokens);
                    if (level >= MigrateLevel.Design)
                        TokenMigrator.AnalyzeLegacyJs(text, toks, map, res.Edits, options.DefinedTokens);
                }
            foreach (var n in nodes)
            {
                if (n.Kind == HtmlNodeKind.StartTag)
                {
                    foreach (var a in n.Attributes)
                    {
                        if ((a.Value != null) && (a.Value.Length > 0) && a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && (a.Name.Length > 2))
                            Take(JsMigrator.Analyze(new JsScope(text, JsLexer.Tokenize(text, a.ValueStart, a.ValueStart + a.Value.Length)), map, level, runGlobals, lineOf, options.FixTranslations), null, true);
                        //  Design token aliases in style attributes
                        if ((a.Value != null) && (a.Value.Length > 0) && (level >= MigrateLevel.Api) && String.Equals(a.Name, "style", StringComparison.OrdinalIgnoreCase))
                            TokenMigrator.AnalyzeCss(text, a.ValueStart, a.ValueStart + a.Value.Length, map, res.Edits, options.DefinedTokens);
                        if ((a.Value != null) && (a.Value.Length > 0) && (level >= MigrateLevel.Design) && String.Equals(a.Name, "style", StringComparison.OrdinalIgnoreCase))
                        {
                            TokenMigrator.AnalyzeLegacyCss(text, a.ValueStart, a.ValueStart + a.Value.Length, map, res.Edits, options.DefinedTokens);
                            DesignMigrator.AnalyzeCss(text, a.ValueStart, a.ValueStart + a.Value.Length, res.Edits);
                        }
                    }
                }
                else if ((n.Kind == HtmlNodeKind.RawText) && (n.Name == "style") && (level >= MigrateLevel.Api))
                {
                    CssMigrator.Analyze(text, n.Start, n.End, map, res.Edits, res.Items, options.KeepStyles);
                    TokenMigrator.AnalyzeCss(text, n.Start, n.End, map, res.Edits, options.DefinedTokens);
                    if (level >= MigrateLevel.Design)
                    {
                        TokenMigrator.AnalyzeLegacyCss(text, n.Start, n.End, map, res.Edits, options.DefinedTokens);
                        DesignMigrator.AnalyzeCss(text, n.Start, n.End, res.Edits);
                    }
                }
            }

            // Design v2: the viewport meta tag (phones render a page without it 980 px wide, zoomed out)
            if ((level >= MigrateLevel.Design) && DesignChecker.NeedsViewport(nodes, out var vpAt))
            {
                if (vpAt == null)
                    res.Items.Add(new OffsetItem { Offset = 0, Kind = MigrateItemKind.Warning, Message = "design: design-viewport: a UI page without <head> and without <meta name=\"viewport\">: add " + DesignChecker.ViewportMeta + " to its head" });
                else
                {
                    var vpEol = DetectEol(text);
                    var next = nodes.FirstOrDefault(x => (x.Start >= vpAt.End) && (x.Kind == HtmlNodeKind.StartTag));
                    var ind = (next != null) ? IndentOf(text, next.Start) : "";
                    if ((ind.Length == 0) && (vpAt.Name == "meta"))
                        ind = IndentOf(text, vpAt.Start);
                    res.Edits.Add(new TextEdit { Start = vpAt.End, End = vpAt.End, Text = vpEol + ind + DesignChecker.ViewportMeta, ReportAt = vpAt.Start, OldDisplay = "(no viewport meta tag)", NewDisplay = DesignChecker.ViewportMeta, Note = "Design v2: phones render a page without it 980 px wide" });
                }
            }
            //  A viewport that blocks pinch zoom (user-scalable=no, maximum-scale=1)
            if ((level >= MigrateLevel.Design) && DesignChecker.BlocksZoom(nodes, out var vpContent, out var vpFixed))
                res.Edits.Add(new TextEdit { Start = vpContent.ValueStart, End = vpContent.ValueStart + vpContent.Value.Length, Text = vpFixed, Note = "Design v2 / WCAG 1.4.4: pinch zoom stays possible" });

            // Include tags
            var incs = new List<Include>();
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                HtmlAttribute url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                if (url?.Value == null)
                    continue;
                var (kind, prefix, query) = Classify(url.Value);
                bool isScript = n.Name == "script";
                if ((kind == IncludeKind.None) || (isScript != url.Value.Trim().Split('?', '#')[0].EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
                {
                    if (isScript)
                    {
                        if (IsTemplated(url.Value))
                            res.Items.Add(new OffsetItem { Offset = url.ValueStart, Kind = MigrateItemKind.Info, Message = "templated script src \"" + url.Value + "\": not analyzed (the template decides the file; the legacy bundles keep it working)" });
                        else
                        {
                            var ps = resolve(url.Value);
                            if (ps?.Analysis != null)
                                Take(ps.Analysis, ps.Rel, false);
                            if (ps?.Problem != null)
                                res.Items.Add(new OffsetItem { Offset = url.ValueStart, Kind = MigrateItemKind.Info, Message = "script \"" + url.Value + "\": " + ps.Problem });
                        }
                    }
                    continue;
                }
                var inc = new Include { Kind = kind, Tag = n, Url = url, Prefix = prefix, Query = query, ElemEnd = n.End };
                if (isScript)
                {
                    int j = i + 1;
                    if ((j < nodes.Count) && (nodes[j].Kind == HtmlNodeKind.RawText) && String.IsNullOrWhiteSpace(text.Substring(nodes[j].Start, nodes[j].End - nodes[j].Start)))
                        ++j;
                    if ((j < nodes.Count) && (nodes[j].Kind == HtmlNodeKind.EndTag) && (nodes[j].Name == "script"))
                        inc.ElemEnd = nodes[j].End;
                }
                if (IsTemplated(prefix))
                    res.Items.Add(new OffsetItem { Offset = url.ValueStart, Kind = MigrateItemKind.Info, Message = "templated prefix \"" + prefix + "\" kept as is in \"" + url.Value + "\"" });
                incs.Add(inc);
            }
            Include First(IncludeKind k) => incs.FirstOrDefault(x => x.Kind == k);
            bool Has(IncludeKind k) => incs.Any(x => x.Kind == k);
            var common = First(IncludeKind.CommonJs);
            var appJs = First(IncludeKind.AppJs);
            var appCss = First(IncludeKind.AppCss);
            res.UsesShell = shell.Count > 0;
            res.IsLegacyPage = (common != null) || (appJs != null) || (appCss != null);
            if (!res.IsLegacyPage)
            {
                //  A migrated page with the compatibility bundle: tell when it can be removed
                var compatJs = First(IncludeKind.CompatJs);
                if (compatJs != null)
                {
                    res.UsesCompat = true;
                    res.Items.Add(new OffsetItem
                    {
                        Offset = compatJs.Url.ValueStart,
                        Kind = MigrateItemKind.Info,
                        Message = unresolved.Count == 0
                            ? "sw/compat.js is no longer needed (no legacy globals left): remove the sw/compat.js and sw/compat.css includes"
                            : ("sw/compat.js still needed for " + unresolved.Count + " legacy global(s): " + String.Join(", ", unresolved.Keys.Take(40)) + (unresolved.Count > 40 ? ", ..." : "")),
                    });
                }
                return res;
            }
            var anchor = common ?? appJs ?? appCss;
            String Names(int max) => String.Join(", ", unresolved.Take(max).Select(x => x.Key + " (" + x.Value + ")")) + (unresolved.Count > max ? ", ..." : "");
            if ((unresolved.Count > 0) && options.KeepLegacy && !options.ForceIncludes)
            {
                res.Blocked = true;
                res.Items.Add(new OffsetItem
                {
                    Offset = anchor.Url.ValueStart,
                    Kind = MigrateItemKind.Warning,
                    Message = "legacy includes kept (--keep-legacy): the page still uses " + unresolved.Count + " legacy global(s) that weren't migrated: "
                        + Names(12) + (level == MigrateLevel.Includes ? " [--level includes doesn't rename]" : "") + "; fix them and run migrate again (or run without --keep-legacy to use sw/compat.js)",
                });
                return res;
            }
            if (unresolved.Count > 0)
            {
                if (options.ForceIncludes)
                    res.Items.Add(new OffsetItem { Offset = anchor.Url.ValueStart, Kind = MigrateItemKind.Warning, Message = "--force-includes: includes rewritten without sw/compat.js although the page still uses legacy globals: " + String.Join(", ", unresolved.Keys.Take(12)) });
                else
                {
                    res.UsesCompat = true;
                    res.Items.Add(new OffsetItem
                    {
                        Offset = anchor.Url.ValueStart,
                        Kind = MigrateItemKind.Warning,
                        Message = "new format with the compatibility bundle sw/compat.js (+ sw/compat.css): the page still uses " + unresolved.Count + " legacy global(s): "
                            + Names(40) + (level == MigrateLevel.Includes ? " [--level includes doesn't rename]" : "") + "; rewrite them on the Sw.* API (SwWeb check --strict lists them), then remove the sw/compat.js and sw/compat.css includes",
                    });
                }
            }
            res.IncludesMigrated = true;
            bool needShell = res.UsesShell && ((appJs != null) || Has(IncludeKind.ShellJs));
            if (res.UsesShell && !needShell)
                res.Items.Add(new OffsetItem { Offset = anchor.Url.ValueStart, Kind = MigrateItemKind.Warning, Message = "the page uses shell features (" + String.Join(", ", shell) + ") but didn't include app/application.js: sw/shell.js not added, review" });
            if (appJs != null)
                res.Items.Add(new OffsetItem
                {
                    Offset = appJs.Url.ValueStart,
                    Kind = MigrateItemKind.Info,
                    Message = needShell ? ("app/application.js -> sw/shell.js: the page uses shell features (" + String.Join(", ", shell) + ")")
                        : "app/application.js removed: the page uses no shell features (pop-ups and menus are in sw/ui.js; code that converts a server menu with Sw.Shell.ToMenuItems needs sw/shell.js, migrate adds it once the code uses Sw.Shell)",
                });
            var eol = DetectEol(text);
            bool hasCore = Has(IncludeKind.CoreJs), hasUi = Has(IncludeKind.UiJs), hasShellJs = Has(IncludeKind.ShellJs), hasUiCss = Has(IncludeKind.UiCss), hasShellCss = Has(IncludeKind.ShellCss);
            bool needCompatJs = res.UsesCompat && !Has(IncludeKind.CompatJs), needCompatCss = res.UsesCompat && !Has(IncludeKind.CompatCss);

            void Replace(Include inc, List<String> newUrls)
            {
                var old = text.Substring(inc.Tag.Start, inc.ElemEnd - inc.Tag.Start);
                if (newUrls.Count == 0)
                {
                    var (s, e) = RemovalRange(text, inc.Tag.Start, inc.ElemEnd);
                    res.Edits.Add(new TextEdit { Start = s, End = e, Text = "", ReportAt = inc.Tag.Start, OldDisplay = old, NewDisplay = "(removed)" });
                    return;
                }
                var ind = IndentOf(text, inc.Tag.Start);
                var parts = newUrls.Select(u => WithUrl(text, inc, u)).ToList();
                res.Edits.Add(new TextEdit { Start = inc.Tag.Start, End = inc.ElemEnd, Text = String.Join(eol + ind, parts), OldDisplay = old, NewDisplay = String.Join(" + ", parts) });
            }

            // ui.css / shell.css links needed
            var cssNeeded = new List<String>();
            if (!hasUiCss && ((common != null) || (appCss != null)))
                cssNeeded.Add("sw/ui.css");
            if (needShell && !hasShellCss)
                cssNeeded.Add("sw/shell.css");
            if (needCompatCss)
                cssNeeded.Add("sw/compat.css");
            //  A page without the theme link gets it first (before sw/ui.css: the Sw tokens follow its theme variables and [data-theme] variants)
            if ((cssNeeded.Count > 0) && (cssNeeded[0] == "sw/ui.css") && !Has(IncludeKind.ThemeCss))
                cssNeeded.Insert(0, "common/theme.css");
            //  upgrade: a page without any SysWeaver style sheet draws its own UI; the old common.js added none, so the new scripts don't either
            if (options.KeepStyles && (cssNeeded.Count > 0) && !Has(IncludeKind.ThemeCss) && (appCss == null) && !hasUiCss && !hasShellCss)
            {
                res.Items.Add(new OffsetItem { Offset = anchor.Tag.Start, Kind = MigrateItemKind.Info, Message = "the page links no SysWeaver style sheet (its own UI): only the scripts are switched, " + String.Join(", ", cssNeeded) + " not added" });
                cssNeeded.Clear();
            }
            foreach (var inc in incs)
            {
                switch (inc.Kind)
                {
                    case IncludeKind.CommonJs:
                        {
                            var l = new List<String>();
                            if (!hasCore)
                                l.Add(inc.Prefix + "sw/core.js" + inc.Query);
                            if (!hasUi)
                                l.Add(inc.Prefix + "sw/ui.js" + inc.Query);
                            if (needCompatJs)
                                l.Add(inc.Prefix + "sw/compat.js" + inc.Query);
                            hasCore = hasUi = true;
                            needCompatJs = false;
                            Replace(inc, l);
                            break;
                        }
                    case IncludeKind.AppJs:
                        {
                            var l = new List<String>();
                            if (needShell && !hasShellJs)
                                l.Add(inc.Prefix + "sw/shell.js" + inc.Query);
                            if (needCompatJs)
                                l.Add(inc.Prefix + "sw/compat.js" + inc.Query);
                            hasShellJs = true;
                            needCompatJs = false;
                            Replace(inc, l);
                            break;
                        }
                    case IncludeKind.AppCss:
                        Replace(inc, cssNeeded.Select(x => inc.Prefix + x + inc.Query).ToList());
                        cssNeeded.Clear();
                        break;
                }
            }
            if (needCompatJs)
            {
                //  No common.js / application.js script to replace (a page with application.css only): after the last sw/*.js script
                var after = incs.LastOrDefault(x => (x.Kind == IncludeKind.CoreJs) || (x.Kind == IncludeKind.UiJs) || (x.Kind == IncludeKind.ShellJs));
                if (after != null)
                {
                    var ind = IndentOf(text, after.Tag.Start);
                    var part = WithUrl(text, after, after.Prefix + "sw/compat.js");
                    res.Edits.Add(new TextEdit { Start = after.ElemEnd, End = after.ElemEnd, Text = eol + ind + part, ReportAt = after.Tag.Start, OldDisplay = "(after " + text.Substring(after.Tag.Start, after.ElemEnd - after.Tag.Start) + ")", NewDisplay = part });
                }
                else
                    res.Items.Add(new OffsetItem { Offset = anchor.Url.ValueStart, Kind = MigrateItemKind.Warning, Message = "the page needs sw/compat.js but has no sw/core.js / sw/ui.js script to put it after: add sw/core.js, sw/ui.js and sw/compat.js manually" });
            }
            //  The page already links sw/ui.css / sw/shell.css: the missing links go after the last of them
            var swCss = incs.LastOrDefault(x => (x.Kind == IncludeKind.UiCss) || (x.Kind == IncludeKind.ShellCss));
            if ((cssNeeded.Count > 0) && (swCss != null))
            {
                var ind = IndentOf(text, swCss.Tag.Start);
                var parts = cssNeeded.Select(x => WithUrl(text, swCss, swCss.Prefix + x)).ToList();
                res.Edits.Add(new TextEdit { Start = swCss.ElemEnd, End = swCss.ElemEnd, Text = String.Concat(parts.Select(p => eol + ind + p)), ReportAt = swCss.Tag.Start, OldDisplay = "(after " + text.Substring(swCss.Tag.Start, swCss.ElemEnd - swCss.Tag.Start) + ")", NewDisplay = String.Join(" + ", parts) });
                cssNeeded.Clear();
            }
            if (cssNeeded.Count > 0)
            {
                var theme = First(IncludeKind.ThemeCss);
                if (theme != null)
                {
                    var ind = IndentOf(text, theme.Tag.Start);
                    var parts = cssNeeded.Select(x => WithUrl(text, theme, theme.Prefix + x)).ToList();
                    res.Edits.Add(new TextEdit { Start = theme.ElemEnd, End = theme.ElemEnd, Text = String.Concat(parts.Select(p => eol + ind + p)), ReportAt = theme.Tag.Start, OldDisplay = "(after " + text.Substring(theme.Tag.Start, theme.ElemEnd - theme.Tag.Start) + ")", NewDisplay = String.Join(" + ", parts) });
                }
                else
                {
                    var at = common ?? appJs;
                    var ind = IndentOf(text, at.Tag.Start);
                    var parts = cssNeeded.Select(x => "<link rel=\"stylesheet\" href=\"" + at.Prefix + x + "\">").ToList();
                    res.Edits.Add(new TextEdit { Start = at.ElemEnd, End = at.ElemEnd, Text = String.Concat(parts.Select(p => eol + ind + p)), ReportAt = at.Tag.Start, OldDisplay = "(no common/theme.css link)", NewDisplay = String.Join(" + ", parts) });
                    res.Items.Add(new OffsetItem { Offset = at.Tag.Start, Kind = MigrateItemKind.Info, Message = "no style sheet link to put them after: " + String.Join(", ", cssNeeded) + " added after the scripts (common/theme.css first: the Sw tokens follow its themes)" });
                }
            }
            return res;
        }
    }
}
