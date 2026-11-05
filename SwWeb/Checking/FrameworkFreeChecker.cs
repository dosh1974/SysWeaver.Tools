using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// Framework-free pages (rule no-framework): an HTML page of a web folder that loads neither the Sw bundles (sw/core.js, sw/ui.js,
    /// sw/ui.css) nor the old bundles (common/common.js, app/application.js): no SysWeaver theme, density, translation, toast, dialog
    /// or shell title reaches it, and every other rule passes it. Such a page is reported with the port steps (PORTING.md A0), and the
    /// things it does itself that the framework does (own fetch, native confirm, own dialog, own light / dark palette, own icons,
    /// no translation ...) are listed at their lines, as the porting worksheet. "SwWeb migrate --level design" adds the includes and
    /// sw-root. An intentional exception (a render surface, a page for a foreign host) is marked with a "SwWeb:no-framework" comment.
    /// </summary>
    public static class FrameworkFreeChecker
    {
        /// <summary>The marker comment of a page that deliberately does not use the framework.</summary>
        public const String NoFramework = "SwWeb:no-framework";

        /// <summary>Includes that put a page on the framework (paths from the web root).</summary>
        static readonly String[] FrameworkIncludes =
        {
            "sw/core.js", "sw/ui.js", "sw/ui.css", "sw/shell.js", "sw/compat.js", "common/common.js", "app/application.js", "app/application.css", "auth/AuthUi.js",
            //  The messaging only bundle: a page that renders its own UI on purpose and takes the SysWeaver messaging (core/README "Messaging only")
            "sw/session.js",
        };

        /// <summary>
        /// A thing found in a framework-free page or one of its own files.
        /// </summary>
        public sealed class Detail
        {
            /// <summary>The index of the file in the own files list, -1 for the page.</summary>
            public int File;
            /// <summary>The offset in that file.</summary>
            public int Offset;
            /// <summary>The message.</summary>
            public String Message;
        }

        /// <summary>
        /// An own file of a page (a script or style sheet inside the checked folder).
        /// </summary>
        public readonly struct OwnFile
        {
            /// <summary>Create.</summary>
            public OwnFile(String webPath, String text, bool css)
            {
                WebPath = webPath;
                Text = text;
                Css = css;
            }

            /// <summary>The path relative to the web root.</summary>
            public readonly String WebPath;
            /// <summary>The text.</summary>
            public readonly String Text;
            /// <summary>True for a style sheet.</summary>
            public readonly bool Css;
        }

        /// <summary>
        /// True if a page is a UI document that is not on the framework: an html / body document (not a fragment), not a mail template
        /// (*Mail.html), not a redirect (meta refresh), not a server template of the shell (index.html, debug.html), without any include of
        /// <see cref="FrameworkIncludes"/> and without the <see cref="NoFramework"/> marker.
        /// </summary>
        /// <param name="webRel">The page path relative to its web root</param>
        /// <param name="text">The page text</param>
        /// <param name="nodes">The scanned page</param>
        /// <param name="anchor">Receives the node to report at (body, html or the first tag)</param>
        /// <returns>True for a framework-free UI page</returns>
        public static bool IsFrameworkFree(String webRel, String text, List<HtmlNode> nodes, out HtmlNode anchor)
        {
            anchor = null;
            if (webRel.EndsWith("Mail.html", StringComparison.OrdinalIgnoreCase) || webRel.EndsWith("Mail.htm", StringComparison.OrdinalIgnoreCase)
                || (webRel == "index.html") || (webRel == "debug.html") || (text.IndexOf(NoFramework, StringComparison.Ordinal) >= 0))
                return false;
            HtmlNode html = null, body = null;
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (n.Name == "html")
                    html ??= n;
                else if (n.Name == "body")
                    body ??= n;
                else if ((n.Name == "meta") && String.Equals(n.GetAttribute("http-equiv")?.Value, "refresh", StringComparison.OrdinalIgnoreCase))
                    return false;
                var url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                if ((url?.Value != null) && FrameworkIncludes.Any(x => Checker.IsSrc(url.Value, x)))
                    return false;
            }
            if ((html == null) && (body == null))
                return false;
            //  A page that loads the core from a script (the ui gallery: the sources one by one, or the bundles with ?bundle)
            if (DynamicCore.IsMatch(text))
                return false;
            anchor = html ?? body;
            return true;
        }

        static readonly Regex DynamicCore = new Regex(@"[""'](?:[^""'\s]*/)?(?:sw/core\.js|core/base\.js)[""']", RegexOptions.CultureInvariant);
        static readonly Regex CssDarkScheme = new Regex(@"\(\s*prefers-color-scheme\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex DarkScheme = new Regex(@"prefers-color-scheme", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ColorProperty = new Regex(@"(?<![\w-])--[\w-]+\s*:\s*(?:#[0-9a-fA-F]{3,8}\b|(?:rgba?|hsla?|oklch|lab|lch)\s*\()", RegexOptions.CultureInvariant);
        static readonly Regex OwnFont = new Regex(@"(?<![\w-])font-family\s*:\s*(?!\s*(?:var\(|(?:inherit|initial|unset)\b))[^;}]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ToastWord = new Regex(@"\btoast", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly HashSet<String> TrNames = new HashSet<String>(StringComparer.Ordinal) { "_T", "_TF", "_TH", "_TFH" };

        /// <summary>
        /// The things a framework-free page and its own files do that the framework does, as port steps (one per kind and file, at the first place).
        /// </summary>
        /// <param name="text">The page text</param>
        /// <param name="nodes">The scanned page</param>
        /// <param name="own">The page's own scripts and style sheets (inside the checked folder)</param>
        /// <returns>The details</returns>
        public static List<Detail> GetDetails(String text, List<HtmlNode> nodes, IReadOnlyList<OwnFile> own)
        {
            var res = new List<Detail>();
            var seen = new HashSet<(int, String)>();
            void Add(int file, int offset, String kind, String message)
            {
                if (seen.Add((file, kind)))
                    res.Add(new Detail { File = file, Offset = offset, Message = message });
            }
            bool translated = false, texts = false;
            int htmlUrlAt = -1;
            void Js(int file, String t, int start, int end)
            {
                //  Texts made by code (the server translates the page's html text itself)
                texts |= TextChecker.FirstSentence(t.Substring(start, end - start)) >= 0;
                var sig = JsLexer.Tokenize(t, start, end).Where(x => x.IsSignificant).ToList();
                if (htmlUrlAt < 0)
                    foreach (var x in sig)
                        if (((x.Kind == JsTokenKind.String) || x.IsTemplate) && Regex.IsMatch(x.GetText(t).Trim('"', '\'', '`'), @"^(?!\w+:|//)[\w./-]*\.html?(?:[?#]|$)", RegexOptions.IgnoreCase))
                        {
                            htmlUrlAt = 1;
                            break;
                        }
                bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(t, p);
                bool Id(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && sig[i].Is(t, p);
                //  A global call: name( or window.name(
                bool Global(int i) => !(P(i - 1, ".") || P(i - 1, "?.")) || Id(i - 2, "window") || Id(i - 2, "self") || Id(i - 2, "globalThis");
                for (int i = 0; i < sig.Count; ++i)
                {
                    var s = sig[i];
                    if (s.Kind == JsTokenKind.String || s.IsTemplate)
                    {
                        var v = s.GetText(t);
                        if (DarkScheme.IsMatch(v))
                            Add(file, s.Start, "theme", "own light / dark handling (prefers-color-scheme): drop it, the SysWeaver themes (Light, Dark, Pro, Application, Hacker; the user's choice) style the Sw components and the role tokens (PORTING.md A0, B16)");
                        else if (v.Contains("data-theme", StringComparison.Ordinal))
                            Add(file, s.Start, "theme", "own theme switch (data-theme): drop it, the shell's theme menu and Sw.Theme set the theme for every page (PORTING.md A0, B16)");
                        if (ToastWord.IsMatch(v))
                            Add(file, s.Start, "toast", "own toasts: Sw.Toast(text, { Kind }) / Sw.Fail(error) (PORTING.md A4)");
                        continue;
                    }
                    if (s.Kind != JsTokenKind.Identifier)
                        continue;
                    var id = s.GetText(t);
                    //  A frame of a page of this site set from code: x.src = "page.html?..." (+ ...), x.setAttribute("src", "page.html")
                    {
                        int v = -1;
                        if ((id == "src") && P(i - 1, ".") && P(i + 1, "="))
                            v = i + 2;
                        else if ((id == "setAttribute") && P(i + 1, "(") && (i + 4 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String) && (JsLexer.DecodeStringLiteral(sig[i + 2].GetText(t)) == "src") && P(i + 3, ","))
                            v = i + 4;
                        if ((v > 0) && (v < sig.Count) && ((sig[v].Kind == JsTokenKind.String) || sig[v].IsTemplate))
                        {
                            var url = sig[v].GetText(t).Trim('"', '\'', '`');
                            if (Regex.IsMatch(url, @"^(?!\w+:|//)[\w./-]*\.html?(?:[?#]|$)", RegexOptions.IgnoreCase))
                                Add(file, sig[v].Start, "frame", "frames a page of this site from code (\"" + url + "\"): a page in a frame of a page gets the page padding twice and its own title / breadcrumb is lost; open the page itself (a link or Sw.Page.Navigate(url, \"_self\")), or build the dashboard from Sw components (Sw.Card per chart) (PORTING.md A0)");
                        }
                    }
                    if (TrNames.Contains(id) && P(i + 1, "("))
                    {
                        translated = true;
                        continue;
                    }
                    switch (id)
                    {
                        case "fetch":
                            if (P(i + 1, "(") && Global(i))
                                Add(file, s.Start, "fetch", "own fetch(): call the server with Sw.Net.Send (session, retries, errors in plain words through Sw.Fail) (PORTING.md A0, A3)");
                            break;
                        case "XMLHttpRequest":
                            Add(file, s.Start, "fetch", "own XMLHttpRequest: call the server with Sw.Net.Send (PORTING.md A0, A3)");
                            break;
                        case "confirm":
                            if (P(i + 1, "(") && Global(i))
                                Add(file, s.Start, "confirm", "native confirm(): Sw.Dialog.Confirm (Danger: true for a destructive action) or Sw.Cells.ConfirmDanger (PORTING.md A5)");
                            break;
                        case "alert":
                            if (P(i + 1, "(") && Global(i))
                                Add(file, s.Start, "alert", "native alert(): Sw.Toast / Sw.Dialog.Alert (PORTING.md A4, A5)");
                            break;
                        case "prompt":
                            if (P(i + 1, "(") && Global(i))
                                Add(file, s.Start, "prompt", "native prompt(): Sw.Dialog.Prompt (PORTING.md A5)");
                            break;
                        case "showModal":
                            if (P(i - 1, "."))
                                Add(file, s.Start, "dialog", "own <dialog> (showModal): Sw.Dialog.Open / Show (focus trap, scrim, Escape, phone layout, the dialog rules) (PORTING.md A5)");
                            break;
                    }
                }
            }
            void Css(int file, String t, int start, int end)
            {
                var s = t.Substring(start, end - start);
                var m = CssDarkScheme.Match(s);
                if (m.Success)
                    Add(file, start + m.Index, "theme", "own light / dark palette (@media (prefers-color-scheme)): drop it, the SysWeaver themes and the role tokens do it (PORTING.md A0, B16)");
                var colors = ColorProperty.Matches(s);
                if (colors.Count >= 3)
                    Add(file, start + colors[0].Index, "palette", "own colour palette (" + colors.Count + " literal colours in custom properties): use the role tokens (--sw-surface, --sw-text, --sw-text-2, --sw-border, --sw-accent1, --sw-danger ...) so the page follows the five themes (PORTING.md A0, B4)");
                var f = OwnFont.Match(s);
                if (f.Success)
                    Add(file, start + f.Index, "font", "own font (" + f.Value.Trim() + "): the theme font comes from sw-root (--sw-sans, --sw-font-mono) (PORTING.md B7)");
            }
            //  The page
            HtmlNode body = null;
            int svgs = 0, firstSvg = -1;
            String title = null;
            int dynFrame = -1;
            int titleAt = -1;
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if (n.Kind == HtmlNodeKind.StartTag)
                {
                    if (n.Name == "body")
                        body ??= n;
                    else if (n.Name == "dialog")
                        Add(-1, n.Start, "dialog", "own <dialog>: Sw.Dialog.Open / Show (focus trap, scrim, Escape, phone layout, the dialog rules) (PORTING.md A5)");
                    else if (n.Name == "svg")
                    {
                        ++svgs;
                        if (firstSvg < 0)
                            firstSvg = n.Start;
                    }
                    else if ((n.Name == "title") && (i + 1 < nodes.Count) && ((nodes[i + 1].Kind == HtmlNodeKind.Text) || (nodes[i + 1].Kind == HtmlNodeKind.RawText)))
                    {
                        title ??= text.Substring(nodes[i + 1].Start, nodes[i + 1].End - nodes[i + 1].Start).Trim();
                        titleAt = nodes[i + 1].Start;
                    }
                    else if (n.Name == "iframe")
                    {
                        //  A page of this site in a frame (an app Home that shows a library page): two nested frames
                        var src = n.GetAttribute("src")?.Value?.Trim();
                        if ((src != null) && (src.Length > 0) && !src.Contains("://") && !src.StartsWith("//", StringComparison.Ordinal) && !src.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                            Add(-1, n.Start, "frame", "frames a page of this site (\"" + src + "\"): forward to it (<meta http-equiv=\"refresh\" content=\"0; url=" + src + "\">) or point the menu item at it instead of framing it: one frame keeps the title, breadcrumb and deep links right (PORTING.md A0)");
                        else if (String.IsNullOrWhiteSpace(src) && (dynFrame < 0))
                            dynFrame = n.Start;
                    }
                    foreach (var a in n.Attributes)
                        if ((a.Value != null) && a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && (a.Value.Length > 0))
                            Js(-1, text, a.ValueStart, a.ValueStart + a.Value.Length);
                }
                else if (n.Kind == HtmlNodeKind.RawText)
                {
                    if (n.Name == "script")
                    {
                        var type = (i > 0) && (nodes[i - 1].Kind == HtmlNodeKind.StartTag) ? nodes[i - 1].GetAttribute("type")?.Value : null;
                        if ((type == null) || type.Contains("javascript", StringComparison.OrdinalIgnoreCase))
                            Js(-1, text, n.Start, n.End);
                    }
                    else if (n.Name == "style")
                        Css(-1, text, n.Start, n.End);
                }
            }
            if (svgs > 0)
                Add(-1, firstSvg, "svg", "own svg icons (" + svgs + " inline svg): Sw.Icon(\"name\") / Sw.Button({ Icon }) follow the theme and the icon size (Sw.Icons.List() lists the names) (PORTING.md A6)");
            for (int f = 0; f < own.Count; ++f)
                if (own[f].Css)
                    Css(f, own[f].Text, 0, own[f].Text.Length);
                else
                    Js(f, own[f].Text, 0, own[f].Text.Length);
            //  A frame whose src code sets to a page of this site (src = "page.html?..." + ...)
            if ((dynFrame >= 0) && (htmlUrlAt >= 0))
                Add(-1, dynFrame, "frame", "frames pages of this site (the src is set from code to a .html page): a page in a frame of a page gets the page padding twice and loses its title and breadcrumb; open the page itself (a link, Sw.Page.Navigate(url, \"_self\")) or build the dashboard from Sw components (one Sw.Card per chart) (PORTING.md A0)");
            //  (the server translates the page's html text and title / placeholder / alt attributes; texts made by code need _T)
            if (!translated && texts)
                Add(-1, body?.Start ?? 0, "tr", "no translated text: the scripts make texts but never call _T: write every user visible text as _T(\"text\", \"context\") / _TF(\"text\", \"context\") (literal first argument; never rename these functions) (PORTING.md A9)");
            var t2 = String.IsNullOrWhiteSpace(title) ? "<the page title>" : title;
            Add(-1, titleAt >= 0 ? titleAt : (body?.Start ?? 0) + 1, "anatomy", "no page header / container: build the page on Sw.PageLayout({ Type: \"dash\" | \"form\" | \"read\" | \"fill\", Title: _TF(\"" + t2 + "\", \"...\"), Actions: [...] }) and Sw components (Sw.Card, Sw.Split, Sw.TextBox, Sw.Select, Sw.Table ...), then delete the own layout css (PORTING.md A0, B10, B11)");
            res.Sort((a, b) => a.File != b.File ? a.File.CompareTo(b.File) : a.Offset.CompareTo(b.Offset));
            return res;
        }

        /// <summary>
        /// The message of the page finding.
        /// </summary>
        /// <param name="details">The number of port steps found</param>
        /// <param name="migrate">True to say what migrate does</param>
        public static String PageMessage(int details, bool migrate = true) =>
            "the page is not on the framework: it loads neither the Sw bundles (sw/core.js, sw/ui.js, sw/ui.css) nor the old common/common.js, so no SysWeaver theme, density, translation, toast, dialog, page header or shell title reaches it and the other rules can't judge it. Port it (PORTING.md A0): "
            + (migrate ? "\"SwWeb migrate <folder> --level design\" adds the bundle includes, sw-root and the viewport; " : "")
            + "then replace what the page does itself (" + details + " step(s) listed at their lines). A page that must stay without the framework (a render surface, a page for a foreign host) gets a \"" + NoFramework + " (reason)\" comment";
    }
}
