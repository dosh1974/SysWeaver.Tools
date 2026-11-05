using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Migrating
{
    public static partial class HtmlMigrator
    {
        /// <summary>
        /// The page parts a migrated (or new-format) page needs besides the bundles: the includes of the libraries whose api the page and
        /// its own scripts use (<c>Sw.Table</c> needs explore/table.js + explore/table.css, <c>Sw.Shell</c> sw/shell.js ..., rule missing-include),
        /// and with <c>--level design</c> the class <c>sw-root</c> on the body (rule design-sw-root: the Sw components are built for it).
        /// </summary>
        /// <param name="text">The page text (before this run)</param>
        /// <param name="hr">The include migration of the page</param>
        /// <param name="options">The options</param>
        /// <param name="webRel">The page path relative to its web root</param>
        /// <param name="ownScripts">The page's own scripts as they are after this run</param>
        /// <param name="index">The pages of the run (shared helper scripts)</param>
        /// <param name="edits">The edits of the page (read for overlaps, receives the new ones)</param>
        /// <param name="items">Receives warnings</param>
        public static void AddPageParts(String text, HtmlResult hr, MigrateOptions options, String webRel, List<PageScriptText> ownScripts, PageIndex index, List<TextEdit> edits, List<OffsetItem> items)
        {
            var nodes = HtmlScanner.Scan(text);
            var scripts = HtmlScanner.GetScripts(nodes);
            bool hasCore = false, hasUiCss = false, hasShellJs = false, hasAppJs = false, hasCommon = false, hasAppCss = false;
            HtmlNode body = null;
            var loaded = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            Include jsAnchor = null, cssAnchor = null;
            HtmlNode uiCssTag = null;
            String uiCssPrefix = null;
            bool hasThemeCss = false;
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (n.Name == "body")
                    body ??= n;
                HtmlAttribute url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                if (url?.Value == null)
                    continue;
                var p = PageChecker.ResolveWebPath(webRel, url.Value);
                if (p != null)
                    loaded.Add(p);
                var (kind, prefix, query) = Classify(url.Value);
                switch (kind)
                {
                    case IncludeKind.CoreJs: hasCore = true; break;
                    case IncludeKind.UiCss: hasUiCss = true; uiCssTag ??= n; uiCssPrefix ??= prefix; break;
                    case IncludeKind.ThemeCss: hasThemeCss = true; break;
                    case IncludeKind.ShellJs: hasShellJs = true; break;
                    case IncludeKind.AppJs: hasAppJs = true; break;
                    case IncludeKind.CommonJs: hasCommon = true; break;
                    case IncludeKind.AppCss: hasAppCss = true; break;
                }
                var inc = new Include { Kind = kind, Tag = n, Url = url, Prefix = prefix, Query = query, ElemEnd = n.End };
                if (n.Name == "script")
                {
                    int j = i + 1;
                    if ((j < nodes.Count) && (nodes[j].Kind == HtmlNodeKind.RawText) && String.IsNullOrWhiteSpace(text.Substring(nodes[j].Start, nodes[j].End - nodes[j].Start)))
                        ++j;
                    if ((j < nodes.Count) && (nodes[j].Kind == HtmlNodeKind.EndTag) && (nodes[j].Name == "script"))
                        inc.ElemEnd = nodes[j].End;
                    if (kind is IncludeKind.CoreJs or IncludeKind.UiJs or IncludeKind.CommonJs or IncludeKind.CompatJs or IncludeKind.ShellJs)
                        jsAnchor = inc;
                }
                else if (kind is IncludeKind.UiCss or IncludeKind.ShellCss or IncludeKind.CompatCss or IncludeKind.ThemeCss or IncludeKind.AppCss)
                    cssAnchor = inc;
            }
            bool newFormat = hasCore || hr.IncludesMigrated;
            if (!newFormat)
                return;
            bool uiCss = hasUiCss || (hr.IncludesMigrated && (hasCommon || hasAppCss));
            bool shell = hasShellJs || (hasAppJs && hr.UsesShell);
            var eol = DetectEol(text);

            bool Overlaps(int at) => edits.Any(e => (e.Start < at) && (e.End > at));
            void Insert(Include anchor, String tag, String what, String note)
            {
                if ((anchor == null) || Overlaps(anchor.ElemEnd))
                {
                    items.Add(new OffsetItem { Offset = (anchor?.Tag ?? body)?.Start ?? 0, Kind = MigrateItemKind.Warning, Message = "add " + tag + " (" + note + "): no include tag to put it after" });
                    return;
                }
                var ind = IndentOf(text, anchor.Tag.Start);
                edits.Add(new TextEdit { Start = anchor.ElemEnd, End = anchor.ElemEnd, Text = eol + ind + tag, ReportAt = anchor.Tag.Start, OldDisplay = "(no " + what + ")", NewDisplay = tag, Note = note });
            }
            //  The url keeps the prefix of the bundle include it goes after ("../", "${@Base Url}"), else it is relative to the page
            String UrlOf(Include anchor, String webPath) =>
                (anchor?.Prefix != null) && (anchor.Kind != IncludeKind.None) ? (anchor.Prefix + webPath) : PageChecker.RelativeUrl(webRel, webPath);

            // ---- library includes (rule missing-include)
            var used = new HashSet<String>(PageChecker.UsedLibraries(webRel, text, nodes, scripts, ownScripts, index).Select(x => x.Api), StringComparer.Ordinal);
            //  An inline Table.addTable(...) becomes Sw.Table.Create in this run
            if (scripts.Any(sc => sc.IsJavaScript && (sc.Src == null) && (sc.BodyEnd > sc.BodyStart) && (text.IndexOf("Table.addTable", sc.BodyStart, sc.BodyEnd - sc.BodyStart, StringComparison.Ordinal) >= 0)))
                used.Add("Table");
            var optional = new HashSet<String>(StringComparer.Ordinal);
            bool deferJs = jsAnchor?.Tag.GetAttribute("defer") != null;
            foreach (var lib in PageChecker.Libraries)
            {
                if (!used.Contains(lib.Api) || optional.Contains(lib.Api))
                    continue;
                bool needJs = (lib.Api == "Shell") ? !shell : !loaded.Contains(lib.Script);
                if (needJs)
                    Insert(jsAnchor, "<script " + (deferJs ? "defer " : "") + "src=\"" + UrlOf(jsAnchor, lib.Script) + "\"></script>", lib.Script + " include",
                        "the page uses Sw." + lib.Api + " (" + lib.From + "), rule missing-include");
                if ((lib.Css != null) && !loaded.Contains(lib.Css))
                    Insert(cssAnchor, "<link rel=\"stylesheet\" href=\"" + UrlOf(cssAnchor, lib.Css) + "\">", lib.Css + " link", "the page uses Sw." + lib.Api + ", rule missing-include");
            }

            // ---- common/theme.css before sw/ui.css (rule theme-include: else the page stays light in the other themes)
            if (hasUiCss && !hasThemeCss && (uiCssTag != null) && !DesignChecker.IsFramework(webRel) && !Overlaps(uiCssTag.Start))
            {
                var tind = IndentOf(text, uiCssTag.Start);
                var tag = "<link rel=\"stylesheet\" href=\"" + (uiCssPrefix ?? "") + "common/theme.css\">";
                edits.Add(new TextEdit { Start = uiCssTag.Start, End = uiCssTag.Start, Text = tag + eol + tind, ReportAt = uiCssTag.Start, OldDisplay = "(no common/theme.css link)", NewDisplay = tag, Note = "rule theme-include: the Sw tokens follow the theme variables of common/theme.css; without it the page stays light in the Dark, Pro, Application and Hacker themes" });
            }

            // ---- sw-root on the body (--level design, rule design-sw-root)
            if ((options.Level < MigrateLevel.Design) || !uiCss || (body == null) || shell || DesignChecker.IsFramework(webRel)
                || (webRel == "index.html") || (webRel == "debug.html") || (text.IndexOf(PageChecker.NoSwRoot, StringComparison.Ordinal) >= 0)
                || PageChecker.HasClass(body, "sw-root") || ownScripts.Any(t => t.Text.Contains("sw-root", StringComparison.Ordinal)) || PageChecker.SwRootLibraries.Any(loaded.Contains))
                return;
            var cls = body.GetAttribute("class");
            const String rootNote = "Design v2: the Sw components (tables, cards, editors, page layout) are built for its border-box sizing, font and colour; check own fixed widths and paddings";
            if ((cls != null) && (cls.Value != null) && (cls.ValueStart >= 0) && ((cls.Quote == '"') || (cls.Quote == '\'')))
            {
                var at = cls.ValueStart + cls.Value.Length;
                var empty = cls.Value.Trim().Length == 0;
                if (!Overlaps(at))
                    edits.Add(new TextEdit { Start = at, End = at, Text = (empty ? "" : " ") + "sw-root", ReportAt = body.Start, OldDisplay = "<body class=\"" + cls.Value + "\">", NewDisplay = "<body class=\"" + (empty ? "" : cls.Value + " ") + "sw-root\">", Note = rootNote });
            }
            else if (cls == null)
            {
                var at = body.Start + 5;
                if (!Overlaps(at))
                    edits.Add(new TextEdit { Start = at, End = at, Text = " class=\"sw-root\"", ReportAt = body.Start, OldDisplay = "<body>", NewDisplay = "<body class=\"sw-root\">", Note = rootNote });
            }
            else
                items.Add(new OffsetItem { Offset = body.Start, Kind = MigrateItemKind.Warning, Message = "add the class sw-root to the body (" + rootNote + ")" });
        }

        /// <summary>
        /// Put a framework-free page on the framework (--level design, rule no-framework): the bundle includes sw/ui.css, sw/core.js and
        /// sw/ui.js (relative to the page, deferred) before the page's own first style sheet / style / script in the head (so the page css
        /// still wins), and the class sw-root on the body. The page's own code keeps working; the port steps are listed separately.
        /// </summary>
        /// <param name="text">The page text</param>
        /// <param name="nodes">The scanned page</param>
        /// <param name="webRel">The page path relative to its web root</param>
        /// <param name="edits">Receives the edits</param>
        /// <param name="items">Receives warnings</param>
        public static void AddFrameworkIncludes(String text, List<HtmlNode> nodes, String webRel, List<TextEdit> edits, List<OffsetItem> items)
        {
            HtmlNode anchor = null, headEnd = null, body = null;
            bool inHead = false;
            foreach (var n in nodes)
            {
                if ((n.Kind == HtmlNodeKind.StartTag) && (n.Name == "head"))
                    inHead = true;
                else if ((n.Kind == HtmlNodeKind.EndTag) && (n.Name == "head"))
                {
                    headEnd ??= n;
                    inHead = false;
                }
                else if ((n.Kind == HtmlNodeKind.StartTag) && (n.Name == "body"))
                {
                    body ??= n;
                    inHead = false;
                }
                else if (inHead && (anchor == null) && (n.Kind == HtmlNodeKind.StartTag)
                    && ((n.Name == "script") || (n.Name == "style") || ((n.Name == "link") && String.Equals(n.GetAttribute("rel")?.Value?.Trim(), "stylesheet", StringComparison.OrdinalIgnoreCase))))
                    anchor = n;
            }
            anchor ??= headEnd;
            var eol = DetectEol(text);
            if (anchor == null)
                items.Add(new OffsetItem { Offset = body?.Start ?? 0, Kind = MigrateItemKind.Warning, Message = "no-framework: the page has no <head>: add <link rel=\"stylesheet\" href=\"" + PageChecker.RelativeUrl(webRel, "sw/ui.css") + "\"> and the deferred scripts sw/core.js, sw/ui.js to its head" });
            else
            {
                var ind = IndentOf(text, anchor.Start);
                //  The theme first (the templated theme variables and the [data-theme] variants the Sw tokens follow), unless the page has it
                bool hasTheme = nodes.Any(x => (x.Kind == HtmlNodeKind.StartTag) && (x.Name == "link") && (x.GetAttribute("href")?.Value is String h) && Checker.IsSrc(h, "common/theme.css"));
                var tags = new List<String>();
                if (!hasTheme)
                    tags.Add("<link rel=\"stylesheet\" href=\"" + PageChecker.RelativeUrl(webRel, "common/theme.css") + "\">");
                tags.Add("<link rel=\"stylesheet\" href=\"" + PageChecker.RelativeUrl(webRel, "sw/ui.css") + "\">");
                tags.Add("<script defer src=\"" + PageChecker.RelativeUrl(webRel, "sw/core.js") + "\"></script>");
                tags.Add("<script defer src=\"" + PageChecker.RelativeUrl(webRel, "sw/ui.js") + "\"></script>");
                bool atEnd = anchor == headEnd;
                var ins = atEnd ? ("    " + String.Join(eol + ind + "    ", tags) + eol + ind) : (String.Join(eol + ind, tags) + eol + ind);
                edits.Add(new TextEdit
                {
                    Start = anchor.Start, End = anchor.Start, Text = ins, ReportAt = anchor.Start, OldDisplay = "(no SysWeaver bundles)", NewDisplay = String.Join(" ", tags),
                    Note = "rule no-framework: the page gets the theme, the Sw core and components (its own css comes after sw/ui.css and still wins); then do the port steps (PORTING.md A0)",
                });
                //  The viewport in the same run (phones render a page without it 980 px wide): after the charset meta, else first in the head
                if (!nodes.Any(x => (x.Kind == HtmlNodeKind.StartTag) && (x.Name == "meta") && String.Equals(x.GetAttribute("name")?.Value?.Trim(), "viewport", StringComparison.OrdinalIgnoreCase)))
                {
                    var charset = nodes.FirstOrDefault(x => (x.Kind == HtmlNodeKind.StartTag) && (x.Name == "meta") && (x.GetAttribute("charset") != null) && (x.Start < anchor.Start));
                    var head = nodes.FirstOrDefault(x => (x.Kind == HtmlNodeKind.StartTag) && (x.Name == "head"));
                    var after = charset ?? head;
                    if ((after != null) && (after.End <= anchor.Start))
                    {
                        var vind = charset != null ? IndentOf(text, charset.Start) : ind;
                        edits.Add(new TextEdit { Start = after.End, End = after.End, Text = eol + vind + DesignChecker.ViewportMeta, ReportAt = after.Start, OldDisplay = "(no viewport meta tag)", NewDisplay = DesignChecker.ViewportMeta, Note = "Design v2: phones render a page without it 980 px wide" });
                    }
                    else
                        items.Add(new OffsetItem { Offset = anchor.Start, Kind = MigrateItemKind.Warning, Message = "design: design-viewport: add " + DesignChecker.ViewportMeta + " to the head" });
                }
            }
            if ((body == null) || PageChecker.HasClass(body, "sw-root"))
                return;
            const String rootNote = "rule no-framework / design-sw-root: the Sw components are built for its border-box sizing, font and colour; check the page's own fixed widths, paddings and colours";
            var cls = body.GetAttribute("class");
            if ((cls != null) && (cls.Value != null) && (cls.ValueStart >= 0) && ((cls.Quote == '"') || (cls.Quote == '\'')))
            {
                var empty = cls.Value.Trim().Length == 0;
                var at = cls.ValueStart + cls.Value.Length;
                edits.Add(new TextEdit { Start = at, End = at, Text = (empty ? "" : " ") + "sw-root", ReportAt = body.Start, OldDisplay = "<body class=\"" + cls.Value + "\">", NewDisplay = "<body class=\"" + (empty ? "" : cls.Value + " ") + "sw-root\">", Note = rootNote });
            }
            else if (cls == null)
                edits.Add(new TextEdit { Start = body.Start + 5, End = body.Start + 5, Text = " class=\"sw-root\"", ReportAt = body.Start, OldDisplay = "<body>", NewDisplay = "<body class=\"sw-root\">", Note = rootNote });
            else
                items.Add(new OffsetItem { Offset = body.Start, Kind = MigrateItemKind.Warning, Message = "add the class sw-root to the body (" + rootNote + ")" });
        }

        /// <summary>
        /// Libraries that pages load from a CDN and that the framework serves itself (rule external-resource): (pattern of the CDN url, the
        /// path in the web root, the project that serves it).
        /// </summary>
        static readonly (System.Text.RegularExpressions.Regex Url, String Local, String From)[] CdnLibraries =
        {
            (new System.Text.RegularExpressions.Regex(@"^(?:https?:)?//(?:cdn\.jsdelivr\.net/npm|unpkg\.com|cdnjs\.cloudflare\.com/ajax/libs)/chart\.?js(?:@[\w.-]+)?(?:/(?:dist/)?chart(?:\.umd)?(?:\.min)?\.js)?/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "chart/external/chart.js", "SysWeaver.MicroService.ChartJs (Chart.js 4)"),
            (new System.Text.RegularExpressions.Regex(@"^(?:https?:)?//(?:cdn\.jsdelivr\.net/npm|unpkg\.com|cdnjs\.cloudflare\.com/ajax/libs)/chartjs-plugin-datalabels(?:@[\w.-]+)?(?:/.*\.js)?/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "chart/external/chartjs_plugin_datalabels.js", "SysWeaver.MicroService.ChartJs"),
            (new System.Text.RegularExpressions.Regex(@"^(?:https?:)?//(?:cdn\.jsdelivr\.net/npm|unpkg\.com|cdnjs\.cloudflare\.com/ajax/libs)/hammer(?:js|\.js)(?:@[\w.-]+)?(?:/.*\.js)?/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "chart/external/hammerjs.js", "SysWeaver.MicroService.ChartJs"),
        };

        /// <summary>
        /// Replace a CDN script of a library that the framework serves (Chart.js, its data labels plugin, hammer.js) with the framework's copy
        /// (--level design, rule external-resource): pages must work offline. The app must include the project that serves it.
        /// </summary>
        /// <param name="nodes">The scanned page</param>
        /// <param name="webRel">The page path relative to its web root</param>
        /// <param name="edits">Receives the edits</param>
        public static void ReplaceCdnScripts(List<HtmlNode> nodes, String webRel, List<TextEdit> edits)
        {
            foreach (var n in nodes)
            {
                if ((n.Kind != HtmlNodeKind.StartTag) || (n.Name != "script"))
                    continue;
                var src = n.GetAttribute("src");
                if ((src?.Value == null) || (src.ValueStart < 0))
                    continue;
                foreach (var (re, local, from) in CdnLibraries)
                {
                    if (!re.IsMatch(src.Value.Trim()))
                        continue;
                    var url = PageChecker.RelativeUrl(webRel, local);
                    edits.Add(new TextEdit
                    {
                        Start = src.ValueStart, End = src.ValueStart + src.Value.Length, Text = url, ReportAt = n.Start, OldDisplay = src.Value, NewDisplay = url,
                        Note = "rule external-resource: pages work offline; the framework serves this library (" + from + "): the app must include that project; check the version your code needs",
                    });
                    break;
                }
            }
        }
    }
}
