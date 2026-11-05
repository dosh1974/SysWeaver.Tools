using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// Page level rules of a new-format HTML page together with its own scripts (traps that pass every file rule but break the page):
    /// a library API used without its include (<see cref="Rule.MissingInclude"/>), an inline script that uses <c>Sw</c> before the
    /// deferred bundles ran (<see cref="Rule.ScriptOrder"/>) and a page body without <c>sw-root</c> (<see cref="Rule.DesignSwRoot"/>).
    /// </summary>
    /// <summary>
    /// Which pages load which scripts and libraries (paths relative to the web root): a shared helper script (ex: ServerManager/smShared.js)
    /// may use a library in a function that only the pages that load the library call, so its uses only count when no other page of the
    /// script loads the library.
    /// </summary>
    public sealed class PageIndex
    {
        readonly Dictionary<String, List<(String Page, HashSet<String> Loaded)>> ByScript = new Dictionary<String, List<(String, HashSet<String>)>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Add a page.
        /// </summary>
        /// <param name="pageWebRel">The page path relative to the web root</param>
        /// <param name="text">The page text</param>
        public void Add(String pageWebRel, String text)
        {
            var loaded = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var scripts = new List<String>();
            foreach (var n in HtmlScanner.Scan(text))
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                var url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                var p = PageChecker.ResolveWebPath(pageWebRel, url?.Value);
                if (p == null)
                    continue;
                loaded.Add(p);
                if (n.Name == "script")
                    scripts.Add(p);
            }
            if (loaded.Contains("app/application.js"))
                loaded.Add("sw/shell.js");
            foreach (var s in scripts)
            {
                if (!ByScript.TryGetValue(s, out var l))
                    ByScript[s] = l = new List<(String, HashSet<String>)>();
                l.Add((pageWebRel, loaded));
            }
            //  Which pages load a file, and whether they are marked as deliberately framework-free
            bool marked = text.IndexOf(FrameworkFreeChecker.NoFramework, StringComparison.Ordinal) >= 0;
            bool ownLook = !marked && PageChecker.IsOwnLook(text);
            foreach (var f in loaded)
            {
                ByFile.TryGetValue(f, out var c);
                ByFile[f] = (c.Pages + 1, c.Marked + (marked ? 1 : 0), c.OwnLook + (ownLook ? 1 : 0));
            }
        }

        readonly Dictionary<String, (int Pages, int Marked, int OwnLook)> ByFile = new Dictionary<String, (int, int, int)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if a script / style sheet is only loaded by pages with their own look (<see cref="PageChecker.IsOwnLook"/>) or marked
        /// "SwWeb:no-framework" (at least one own look page): the Design v2 rules don't apply to it (S6 F6).
        /// </summary>
        /// <param name="webPath">The file path relative to the web root</param>
        public bool OnlyOwnLookPages(String webPath) => ByFile.TryGetValue(webPath, out var c) && (c.OwnLook > 0) && (c.Pages == c.Marked + c.OwnLook);

        /// <summary>
        /// True if a script / style sheet is only loaded by pages marked "SwWeb:no-framework" (render surfaces, pages for a foreign host): the
        /// design and translation rules, which judge pages on the framework, don't apply to it.
        /// </summary>
        /// <param name="webPath">The file path relative to the web root</param>
        public bool OnlyNoFrameworkPages(String webPath) => ByFile.TryGetValue(webPath, out var c) && (c.Pages > 0) && (c.Pages == c.Marked);

        /// <summary>
        /// True if another page that loads a script also loads a library file.
        /// </summary>
        public bool OtherPageLoads(String scriptWebPath, String pageWebRel, String libFile) =>
            ByScript.TryGetValue(scriptWebPath, out var l) && l.Any(x => !String.Equals(x.Page, pageWebRel, StringComparison.OrdinalIgnoreCase) && x.Loaded.Contains(libFile));
    }

    /// <summary>
    /// A script of a page (its path relative to the web root and its text).
    /// </summary>
    public readonly struct PageScriptText
    {
        /// <summary>Create.</summary>
        public PageScriptText(String webPath, String text)
        {
            WebPath = webPath;
            Text = text;
        }

        /// <summary>The path relative to the web root.</summary>
        public readonly String WebPath;
        /// <summary>The text.</summary>
        public readonly String Text;
    }

    public static class PageChecker
    {
        /// <summary>
        /// The marker comment of a page that deliberately has no <c>sw-root</c> body (rule design-sw-root).
        /// </summary>
        public const String NoSwRoot = "SwWeb:no-sw-root";

        /// <summary>
        /// Framework page libraries that put <c>sw-root</c> on the body themselves (the auth pages: Sw.AuthUi), paths relative to the web root.
        /// </summary>
        public static readonly IReadOnlyList<String> SwRootLibraries = new[] { "auth/AuthUi.js" };

        /// <summary>
        /// A library of another folder or project whose API (<c>Sw.&lt;Api&gt;</c>) is only there when the page loads its script.
        /// </summary>
        public sealed class Library
        {
            /// <summary>The member of Sw, ex: "Table" for Sw.Table.</summary>
            public String Api;
            /// <summary>The script, relative to the web root.</summary>
            public String Script;
            /// <summary>The style sheet the page must link too (relative to the web root), or null.</summary>
            public String Css;
            /// <summary>The project that serves it.</summary>
            public String From;
        }

        /// <summary>
        /// The libraries a page must include itself (the bundles sw/core.js and sw/ui.js don't contain them).
        /// </summary>
        public static readonly IReadOnlyList<Library> Libraries = new[]
        {
            new Library { Api = "Table", Script = "explore/table.js", Css = "explore/table.css", From = "SysWeaver.HttpServer.ExploreModule" },
            new Library { Api = "Shell", Script = "sw/shell.js", From = "SysWeaver.HttpServer (the shell bundle; Sw.Shell.ToMenuItems converts a server WebMenu)" },
            new Library { Api = "Edit", Script = "edit/Edit.js", From = "SysWeaver.MicroService.Edit (it loads its own css)" },
            new Library { Api = "Upload", Script = "common/fileUploader.js", From = "SysWeaver.MicroService.FileUploader" },
            new Library { Api = "AuthUi", Script = "auth/AuthUi.js", From = "SysWeaver.MicroService.Auth" },
            new Library { Api = "Chat", Script = "chat/chat.js", From = "SysWeaver.Chat" },
        };

        /// <summary>
        /// Resolve an include url of a page to a path relative to the web root ("../explore/table.js" in "app/x.html" gives "explore/table.js").
        /// A leading server template expression (<c>${@Base Url}</c>) means the web root.
        /// </summary>
        /// <param name="pageWebRel">The page path relative to the web root</param>
        /// <param name="url">The url</param>
        /// <returns>The path, or null for internet / root relative / data urls</returns>
        public static String ResolveWebPath(String pageWebRel, String url)
        {
            if (url == null)
                return null;
            var s = url.Trim();
            bool templated = s.StartsWith("${", StringComparison.Ordinal) || s.StartsWith("$(", StringComparison.Ordinal);
            s = Checker.StripUrlTemplatePrefix(s);
            var q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
                s = s.Substring(0, q);
            if ((s.Length == 0) || s.Contains("://") || s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;
            var parts = new List<String>();
            if (!templated && !s.StartsWith('/'))
            {
                var dir = pageWebRel.Replace('\\', '/');
                var i = dir.LastIndexOf('/');
                if (i > 0)
                    parts.AddRange(dir.Substring(0, i).Split('/'));
            }
            foreach (var p in s.Replace('\\', '/').Split('/'))
            {
                if ((p.Length == 0) || (p == "."))
                    continue;
                if (p == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(p);
            }
            return String.Join("/", parts);
        }

        /// <summary>
        /// A use of <c>Sw.&lt;Api&gt;</c> in a script.
        /// </summary>
        public struct ApiUse
        {
            /// <summary>The api name.</summary>
            public String Api;
            /// <summary>The offset of "Sw".</summary>
            public int Offset;
        }

        /// <summary>
        /// Find the library apis a script uses unconditionally (<c>Sw.Table.Create(...)</c>); an api that the script tests
        /// (<c>typeof Sw.Table</c>, <c>Sw.Table?.</c>, <c>if (Sw.Table)</c>, <c>Sw.Table &amp;&amp;</c>) or defines (<c>Sw.Table = ...</c>),
        /// or whose script file name appears in a string (loaded on demand with Sw.Net.IncludeJs), is optional and left out.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="tokens">The tokens of the script</param>
        /// <param name="optional">Receives the optional apis</param>
        /// <returns>The first unconditional use per api</returns>
        public static List<ApiUse> FindApiUses(String text, List<JsToken> tokens, HashSet<String> optional)
        {
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            var res = new List<ApiUse>();
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Id(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && sig[i].Is(text, p);
            foreach (var t in sig)
                if (t.Kind == JsTokenKind.String)
                {
                    var s = t.GetText(text);
                    foreach (var l in Libraries)
                        if (s.Contains(l.Script.Substring(l.Script.LastIndexOf('/') + 1), StringComparison.OrdinalIgnoreCase))
                            optional.Add(l.Api);
                }
            for (int i = 0; i + 2 < sig.Count; ++i)
            {
                if (!Id(i, "Sw") || P(i - 1, ".") || P(i - 1, "?.") || !(P(i + 1, ".") || P(i + 1, "?.")) || (sig[i + 2].Kind != JsTokenKind.Identifier))
                    continue;
                var api = sig[i + 2].GetText(text);
                if (!Libraries.Any(l => l.Api == api))
                    continue;
                bool guarded = P(i + 1, "?.") || Id(i - 1, "typeof") || P(i - 1, "!") || P(i + 3, "?.") || P(i + 3, "&&") || P(i + 3, "||") || P(i + 3, "??") || P(i + 3, "?")
                    || (P(i + 3, ")") && P(i - 1, "(") && (Id(i - 2, "if") || Id(i - 2, "while")))
                    || P(i + 3, "=") || P(i + 3, "||=") || P(i + 3, "??=");
                if (guarded)
                {
                    optional.Add(api);
                    continue;
                }
                if (!res.Any(x => x.Api == api))
                    res.Add(new ApiUse { Api = api, Offset = sig[i].Start });
            }
            return res;
        }

        /// <summary>
        /// The offset of the first use of the identifier <c>Sw</c> in an inline script that runs while the page is parsed (top level code,
        /// not inside a function, class or arrow body), or -1. A script that tests for Sw (<c>typeof Sw</c>, <c>window.Sw</c>) is not reported.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="tokens">The tokens of the inline script</param>
        /// <returns>The offset or -1</returns>
        public static int FindEarlySwUse(String text, List<JsToken> tokens)
        {
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Id(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && sig[i].Is(text, p);
            for (int i = 0; i < sig.Count; ++i)
                if (Id(i, "Sw") && (Id(i - 1, "typeof") || (P(i - 1, ".") && (Id(i - 2, "window") || Id(i - 2, "self") || Id(i - 2, "globalThis")))))
                    return -1;
            //  Frames: (deferred, pending function, frame is a function body)
            var stack = new Stack<(bool Deferred, bool Pending, bool Body)>();
            bool deferred = false, pending = false, body = false;
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                if (t.Kind == JsTokenKind.Punctuator)
                {
                    if (P(i, "(") || P(i, "[") || P(i, "{") || (t.Kind == JsTokenKind.Punctuator && t.Is(text, "${")))
                    {
                        stack.Push((deferred, pending, body));
                        body = P(i, "{") && pending;
                        deferred = deferred || pending;
                        pending = false;
                        continue;
                    }
                    if (P(i, ")") || P(i, "]") || P(i, "}"))
                    {
                        bool wasBody = body;
                        if (stack.Count > 0)
                            (deferred, pending, body) = stack.Pop();
                        if (wasBody)
                            pending = false;
                        continue;
                    }
                    if (P(i, "=>"))
                        pending = true;
                    else if (P(i, ";") || P(i, ","))
                        pending = false;
                    continue;
                }
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                if (Id(i, "function") || Id(i, "class"))
                {
                    pending = true;
                    continue;
                }
                if (Id(i, "Sw") && !P(i - 1, ".") && !P(i - 1, "?.") && !deferred && !pending)
                    return t.Start;
            }
            return -1;
        }

        /// <summary>
        /// A library a page uses (unconditionally).
        /// </summary>
        public struct LibraryUse
        {
            /// <summary>The api (Library.Api).</summary>
            public String Api;
            /// <summary>The offset of the first use in the page (inline script or event handler), or -1.</summary>
            public int Offset;
            /// <summary>The own script that uses it (web path), or null for a use in the page.</summary>
            public String Script;
        }

        /// <summary>
        /// The libraries a page and its own scripts use unconditionally: inline scripts and event handlers of the page, and its own scripts
        /// unless another page of the script loads the library (a shared helper with a function only those pages call).
        /// </summary>
        /// <param name="pageWebRel">The page path relative to the web root</param>
        /// <param name="text">The page text</param>
        /// <param name="nodes">The scanned page</param>
        /// <param name="scripts">The scripts of the page</param>
        /// <param name="ownScripts">The page's own scripts</param>
        /// <param name="index">The pages of the folder, or null</param>
        /// <returns>The uses, one per api</returns>
        public static List<LibraryUse> UsedLibraries(String pageWebRel, String text, List<HtmlNode> nodes, List<HtmlScript> scripts, IReadOnlyList<PageScriptText> ownScripts, PageIndex index)
        {
            var optional = new HashSet<String>(StringComparer.Ordinal);
            var res = new List<LibraryUse>();
            void AddUses(IEnumerable<ApiUse> uses, String script)
            {
                foreach (var u in uses)
                {
                    if (res.Any(x => x.Api == u.Api))
                        continue;
                    var lib = Libraries.First(l => l.Api == u.Api);
                    if ((script != null) && (index != null) && index.OtherPageLoads(script, pageWebRel, lib.Script))
                        continue;
                    res.Add(new LibraryUse { Api = u.Api, Offset = script == null ? u.Offset : -1, Script = script });
                }
            }
            foreach (var sc in scripts)
                if (sc.IsJavaScript && (sc.Src == null) && (sc.BodyEnd > sc.BodyStart))
                    AddUses(FindApiUses(text, JsLexer.Tokenize(text, sc.BodyStart, sc.BodyEnd), optional), null);
            foreach (var n in nodes)
                if (n.Kind == HtmlNodeKind.StartTag)
                    foreach (var a in n.Attributes)
                        if ((a.Value != null) && (a.Value.Length > 0) && a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && (a.Name.Length > 2))
                            AddUses(FindApiUses(text, JsLexer.Tokenize(text, a.ValueStart, a.ValueStart + a.Value.Length), optional), null);
            foreach (var st in ownScripts)
                AddUses(FindApiUses(st.Text, JsLexer.Tokenize(st.Text), optional), st.WebPath);
            res.RemoveAll(x => optional.Contains(x.Api));
            return res;
        }

        /// <summary>The meta name (&lt;meta name="sw-own-look" content="..."&gt;) and the attribute (data-sw-own-look on html / body) that declare an own look.</summary>
        public const String OwnLookMeta = "sw-own-look";

        /// <summary>The attribute of html / body that declares an own look (see <see cref="OwnLookMeta"/>).</summary>
        public const String OwnLookAttribute = "data-sw-own-look";

        /// <summary>
        /// True for a page with its own look (S6 F6), where the Design v2 rules don't apply: it declares it (&lt;meta name="sw-own-look"&gt;,
        /// or data-sw-own-look on html / body), or it is a messaging only page (sw/session.js, without sw/core.js, common/common.js and
        /// sw/ui.css: no Sw components and no --sw-* tokens, the documented recipe of WebQuickStart 2b).
        /// </summary>
        /// <param name="text">The page text</param>
        /// <returns>True for an own look</returns>
        public static bool IsOwnLook(String text)
        {
            if (String.IsNullOrEmpty(text))
                return false;
            bool session = false, full = false;
            foreach (var n in HtmlScanner.Scan(text))
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (((n.Name == "html") || (n.Name == "body")) && (n.GetAttribute(OwnLookAttribute) != null))
                    return true;
                if ((n.Name == "meta") && String.Equals(n.GetAttribute("name")?.Value, OwnLookMeta, StringComparison.OrdinalIgnoreCase))
                    return true;
                var url = (n.Name == "script") ? n.GetAttribute("src")?.Value : (n.Name == "link") ? n.GetAttribute("href")?.Value : null;
                if (url == null)
                    continue;
                session |= Checker.IsSrc(url, "sw/session.js");
                full |= Checker.IsSrc(url, "sw/core.js") || Checker.IsSrc(url, "common/common.js") || Checker.IsSrc(url, "sw/ui.css") || Checker.IsSrc(url, "sw/ui.js");
            }
            return session && !full;
        }

        /// <summary>The file name of the built-in copy of the framework's bundle manifest (sw/bundles.json of SysWeaver.HttpServer/srcWeb).</summary>
        public const String BundleListFileName = "sw_bundles.json";

        static Bundling.BundleManifest builtInBundles;

        /// <summary>
        /// The framework's bundles (rule bundle-overlap) when the checked folder has no sw/bundles.json: "sw_bundles.json" next to the tool,
        /// else the embedded copy (a test keeps it equal to SysWeaver's sw/bundles.json).
        /// </summary>
        public static Bundling.BundleManifest BuiltInBundles
        {
            get
            {
                if (builtInBundles != null)
                    return builtInBundles;
                String text = null;
                var side = System.IO.Path.Combine(AppContext.BaseDirectory, BundleListFileName);
                if (System.IO.File.Exists(side))
                    text = System.IO.File.ReadAllText(side);
                else
                    using (var s = typeof(PageChecker).Assembly.GetManifestResourceStream("SwWeb." + BundleListFileName))
                        if (s != null)
                            using (var r = new System.IO.StreamReader(s))
                                text = r.ReadToEnd();
                try
                {
                    builtInBundles = text == null ? new Bundling.BundleManifest() : Bundling.BundleManifest.Parse(text);
                }
                catch
                {
                    builtInBundles = new Bundling.BundleManifest();
                }
                return builtInBundles;
            }
        }

        /// <summary>
        /// <see cref="Rule.BundleOverlap"/>: two included bundles with the same sources (see the rule).
        /// </summary>
        /// <param name="ctx">The page</param>
        /// <param name="scripts">Its scripts</param>
        static void CheckBundleOverlap(Checker.FileContext ctx, List<HtmlScript> scripts)
        {
            var m = ctx.Options.Bundles ?? BuiltInBundles;
            var js = m.Bundles.Where(b => (b != null) && !String.IsNullOrWhiteSpace(b.Out) && b.Out.Trim().EndsWith(".js", StringComparison.OrdinalIgnoreCase)).ToList();
            var loaded = new List<(Bundling.BundleDefinition B, HtmlScript S)>();
            foreach (var s in scripts)
            {
                if (s.Src == null)
                    continue;
                var b = js.FirstOrDefault(x => Checker.IsSrc(s.Src, x.Out.Trim()));
                if ((b != null) && !loaded.Any(l => l.B == b))
                    loaded.Add((b, s));
            }
            static String Norm(String f) => (f ?? "").Trim().Replace("\\", "/");
            for (int i = 0; i < loaded.Count; ++i)
                for (int j = i + 1; j < loaded.Count; ++j)
                {
                    var a = loaded[i];
                    var b = loaded[j];
                    var fa = new HashSet<String>(a.B.Files.Select(Norm), StringComparer.OrdinalIgnoreCase);
                    var shared = b.B.Files.Select(Norm).Where(fa.Contains).ToList();
                    if (shared.Count == 0)
                        continue;
                    var what = shared[0] + (shared.Count > 1 ? " and " + (shared.Count - 1) + " more source(s)" : "");
                    if (!a.B.LoadOnce || !b.B.LoadOnce)
                    {
                        var un = b.B.LoadOnce ? a.B : b.B;
                        ctx.Add(Rule.BundleOverlap, b.S.Tag.Start, a.B.Out.Trim() + " and " + b.B.Out.Trim() + " both contain " + what + ", and " + un.Out.Trim()
                            + " is not \"LoadOnce\": the shared sources run twice (two server block overlays, a stack overflow in the focus trap): load only one of them, or set \"LoadOnce\": true for "
                            + un.Out.Trim() + " in sw/bundles.json");
                        continue;
                    }
                    //  The legacy bundle already contains all of the other one (sw/core.js next to it is legacy-include)
                    var legacy = Checker.IsSrc(a.S.Src, "common/common.js") ? a : Checker.IsSrc(b.S.Src, "common/common.js") ? b : default;
                    if (legacy.B == null)
                        continue;
                    var other = legacy.B == a.B ? b : a;
                    if (Checker.IsSrc(other.S.Src, "sw/core.js"))
                        continue;
                    var fl = new HashSet<String>(legacy.B.Files.Select(Norm), StringComparer.OrdinalIgnoreCase);
                    if (other.B.Files.Select(Norm).All(fl.Contains))
                        ctx.Add(Rule.BundleOverlap, other.S.Tag.Start, "common/common.js already contains every source of " + other.B.Out.Trim()
                            + " (each source runs once): the include adds nothing; remove it, or port the page to sw/core.js + sw/ui.js", Severity.Warning);
                }
        }

        /// <summary>
        /// Check a page: <see cref="Rule.MissingInclude"/>, <see cref="Rule.ScriptOrder"/> and (with --design) <see cref="Rule.DesignSwRoot"/>.
        /// Only new-format pages (they include sw/core.js) are checked; the legacy bundles are left alone.
        /// </summary>
        /// <param name="ctx">The page</param>
        /// <param name="ownScripts">The page's own scripts (resolved inside the checked folder, not library files)</param>
        /// <param name="index">The pages of the folder (shared helper scripts), or null</param>
        public static void CheckPage(Checker.FileContext ctx, IReadOnlyList<PageScriptText> ownScripts, PageIndex index)
        {
            var text = ctx.Text;
            var nodes = HtmlScanner.Scan(text);
            var scripts = HtmlScanner.GetScripts(nodes);
            CheckBundleOverlap(ctx, scripts);
            HtmlScript core = scripts.FirstOrDefault(s => (s.Src != null) && (Checker.IsSrc(s.Src, "sw/core.js") || Checker.IsSrc(s.Src, "common/common.js")))
                ?? scripts.FirstOrDefault(s => (s.Src != null) && Checker.IsSrc(s.Src, "sw/session.js"));
            if (core == null)
                return;
            bool newFormat = Checker.IsSrc(core.Src, "sw/core.js");
            bool sessionOnly = Checker.IsSrc(core.Src, "sw/session.js");
            var loaded = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            HtmlNode body = null;
            bool uiCss = false, shellJs = false;
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (n.Name == "body")
                    body ??= n;
                var url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                if (url?.Value == null)
                    continue;
                var p = ResolveWebPath(ctx.WebRel, url.Value);
                if (p != null)
                    loaded.Add(p);
                uiCss |= Checker.IsSrc(url.Value, "sw/ui.css") || Checker.IsSrc(url.Value, "common/common.js");
                shellJs |= Checker.IsSrc(url.Value, "sw/shell.js") || Checker.IsSrc(url.Value, "app/application.js");
            }
            //  The page itself is in the library's folder ("table.js" in explore/x.html)
            bool Loads(String webPath) => loaded.Contains(webPath);

            // ---- script-order: an inline script that uses Sw while the page is parsed, before the deferred bundles ran
            bool coreDeferred = (core.Tag.GetAttribute("defer") != null) || (core.Tag.GetAttribute("async") != null);
            foreach (var sc in scripts)
            {
                if (!sc.IsJavaScript || (sc.Src != null) || (sc.BodyEnd <= sc.BodyStart))
                    continue;
                if (!coreDeferred && (sc.Tag.Start > core.Tag.Start))
                    continue;
                var at = FindEarlySwUse(text, JsLexer.Tokenize(text, sc.BodyStart, sc.BodyEnd));
                if (at >= 0)
                    ctx.Add(Rule.ScriptOrder, at, "this inline <script> uses Sw while the page is parsed, before the " + (coreDeferred ? "deferred " : "") + "bundle \"" + core.Src
                        + "\" ran (ReferenceError: Sw is not defined): move the code to the page's own deferred script (<script defer src=\"page.js\">, after sw/ui.js), or call it from <body onload=\"...\"> / a DOMContentLoaded handler");
            }

            if (sessionOnly)
                CheckSessionPage(ctx, text, nodes, scripts, ownScripts, core, Loads);
            if (!newFormat)
                return;

            // ---- missing-include: a library api used by the page or its own scripts without the library's include
            var anchor = scripts.LastOrDefault(s => (s.Src != null) && (Checker.IsSrc(s.Src, "sw/ui.js") || Checker.IsSrc(s.Src, "sw/core.js")))?.Tag ?? core.Tag;
            foreach (var u in UsedLibraries(ctx.WebRel, text, nodes, scripts, ownScripts, index))
            {
                var lib = Libraries.First(l => l.Api == u.Api);
                bool needJs = (lib.Api == "Shell") ? !shellJs : !Loads(lib.Script);
                bool needCss = (lib.Css != null) && !Loads(lib.Css);
                if (!needJs && !needCss)
                    continue;
                var what = new List<String>();
                if (needJs)
                    what.Add("<script defer src=\"" + IncludeUrl(ctx.WebRel, core.Src, lib.Script) + "\"></script> after sw/ui.js");
                if (needCss)
                    what.Add("<link rel=\"stylesheet\" href=\"" + IncludeUrl(ctx.WebRel, core.Src, lib.Css) + "\"> after sw/ui.css");
                ctx.Add(Rule.MissingInclude, u.Script == null ? u.Offset : anchor.Start, "the page " + (u.Script == null ? "" : ("(its script " + u.Script + ") ")) + "uses Sw." + u.Api + " but doesn't load "
                    + String.Join(" + ", new[] { needJs ? lib.Script : null, needCss ? lib.Css : null }.Where(x => x != null))
                    + " (" + lib.From + "): add " + String.Join(" and ", what) + " (SwWeb migrate adds them); an optional library is tested first: if (Sw." + u.Api + ")");
            }

            // ---- design-sw-root: the Sw components (tables, page layout, cards, editors) are built for border-box sizing and the Sw font
            if (!ctx.Options.Design || !uiCss || (body == null) || shellJs || DesignChecker.IsFramework(ctx.WebRel) || (text.IndexOf(NoSwRoot, StringComparison.Ordinal) >= 0))
                return;
            if (ctx.IsTemplated && ((ctx.WebRel == "index.html") || (ctx.WebRel == "debug.html")))
                return;
            if (HasClass(body, "sw-root") || ownScripts.Any(t => t.Text.Contains("sw-root", StringComparison.Ordinal)) || HasInlineText(text, scripts, "sw-root") || SwRootLibraries.Any(Loads))
                return;
            ctx.Add(Rule.DesignSwRoot, body.Start, "the page body has no class=\"sw-root\": the Sw components (tables, cards, editors, the page layout) are built for its box-sizing: border-box, font and text colour; without it an Sw.Table can grow wider on every refresh and texts use the browser font. Add it: <body class=\"sw-root\"> (SwWeb migrate adds it); a page that must not get it has a \"" + NoSwRoot + "\" comment");
        }

        /// <summary>The theme globals that sw/theme.js defines (sw/session.js has none of them).</summary>
        static readonly HashSet<String> ThemeGlobals = new HashSet<String>(StringComparer.Ordinal) { "getTheme", "applyTheme", "setTheme", "resetTheme", "toggleTheme", "appendTheme" };

        /// <summary>The members of Sw.Net that sw/session.js has (core/include.js); the rest needs sw/net.js.</summary>
        static readonly HashSet<String> SessionNet = new HashSet<String>(StringComparer.Ordinal) { "IncludeJs", "IncludeCss", "FlushCache" };

        /// <summary>
        /// The first use of the theme (a theme global call or Sw.Theme) and of the requests (Sw.Net other than its include members) in a
        /// script, or -1.
        /// </summary>
        static (int Theme, String ThemeName, int Net) FindSessionNeeds(String text, List<JsToken> tokens)
        {
            var s = tokens.Where(t => t.IsSignificant).ToList();
            int theme = -1, net = -1;
            String themeName = null;
            for (int i = 0; i < s.Count; ++i)
            {
                if (s[i].Kind != JsTokenKind.Identifier)
                    continue;
                var w = s[i].GetText(text);
                bool member = (i > 0) && (s[i - 1].Is(text, ".") || s[i - 1].Is(text, "?."));
                if ((theme < 0) && ThemeGlobals.Contains(w) && (i + 1 < s.Count) && s[i + 1].Is(text, "(") && (!member || ((i > 1) && s[i - 2].Is(text, "window"))))
                {
                    theme = s[i].Start;
                    themeName = w + "()";
                }
                if ((w == "Sw") && !member && (i + 2 < s.Count) && s[i + 1].Is(text, "."))
                {
                    var m = s[i + 2].GetText(text);
                    if ((theme < 0) && (m == "Theme"))
                    {
                        theme = s[i].Start;
                        themeName = "Sw.Theme";
                    }
                    if ((net < 0) && (m == "Net") && (i + 4 < s.Count) && s[i + 3].Is(text, ".") && !SessionNet.Contains(s[i + 4].GetText(text)))
                        net = s[i].Start;
                }
            }
            return (theme, themeName, net);
        }

        /// <summary>
        /// A messaging only page (sw/session.js without sw/core.js / common/common.js): <see cref="Rule.MissingInclude"/> when the page or
        /// its own scripts use the theme without sw/theme.js, or the requests (Sw.Net.Send / Get ..., auth/Auth.js) without sw/net.js.
        /// </summary>
        static void CheckSessionPage(Checker.FileContext ctx, String text, List<HtmlNode> nodes, List<HtmlScript> scripts, IReadOnlyList<PageScriptText> ownScripts,
            HtmlScript session, Func<String, bool> loads)
        {
            bool hasTheme = loads("sw/theme.js"), hasNet = loads("sw/net.js");
            bool themeDone = hasTheme, netDone = hasNet;
            //  The fix text copies the loading attributes of the page's sw/session.js include (AS6-3: a classic script after a deferred
            //  sw/session.js runs before it)
            var attrs = (session.Tag.GetAttribute("defer") != null ? " defer" : "") + (session.Tag.GetAttribute("async") != null ? " async" : "");
            void Report(int at, String script, String what, String bundle, String why)
            {
                ctx.Add(Rule.MissingInclude, at, "the messaging only page " + (script == null ? "" : ("(its script " + script + ") ")) + "uses " + what + " but doesn't load " + bundle
                    + " (sw/session.js has no " + why + "): add <script" + attrs + " src=\"" + IncludeUrl(ctx.WebRel, session.Src, bundle) + "\"></script> after sw/session.js, or use sw/core.js");
            }
            void Scan(String t, List<JsToken> tokens, String script, int fallback)
            {
                var (theme, name, net) = FindSessionNeeds(t, tokens);
                if (!themeDone && (theme >= 0))
                {
                    themeDone = true;
                    Report(script == null ? theme : fallback, script, name, "sw/theme.js", "theming");
                }
                if (!netDone && (net >= 0))
                {
                    netDone = true;
                    Report(script == null ? net : fallback, script, "Sw.Net", "sw/net.js", "Sw.Net requests");
                }
            }
            foreach (var sc in scripts)
                if (sc.IsJavaScript && (sc.Src == null) && (sc.BodyEnd > sc.BodyStart))
                    Scan(text, JsLexer.Tokenize(text, sc.BodyStart, sc.BodyEnd), null, 0);
            foreach (var n in nodes)
                if (n.Kind == HtmlNodeKind.StartTag)
                    foreach (var a in n.Attributes)
                        if ((a.Value != null) && (a.Value.Length > 0) && a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && (a.Name.Length > 2))
                            Scan(text, JsLexer.Tokenize(text, a.ValueStart, a.ValueStart + a.Value.Length), null, 0);
            foreach (var st in ownScripts)
                Scan(st.Text, JsLexer.Tokenize(st.Text), st.WebPath, session.Tag.Start);
            //  auth/Auth.js calls the server with Sw.Net
            if (!netDone)
            {
                var auth = scripts.FirstOrDefault(s => (s.Src != null) && Checker.IsSrc(s.Src, "auth/Auth.js"));
                if (auth != null)
                    Report(auth.Tag.Start, null, "auth/Auth.js", "sw/net.js", "Sw.Net requests");
            }
        }

        static bool HasInlineText(String text, List<HtmlScript> scripts, String what) =>
            scripts.Any(s => (s.Src == null) && (s.BodyEnd > s.BodyStart) && (text.IndexOf(what, s.BodyStart, s.BodyEnd - s.BodyStart, StringComparison.Ordinal) >= 0));

        /// <summary>
        /// True if a start tag has a class.
        /// </summary>
        public static bool HasClass(HtmlNode n, String cls)
        {
            var c = n.GetAttribute("class")?.Value;
            return (c != null) && c.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Contains(cls, StringComparer.Ordinal);
        }

        /// <summary>
        /// The url a page uses to include a file of the web root: the prefix of its own bundle include ("../" of "../sw/core.js",
        /// "${@Base Url}/" of a templated one) plus the file, the same prefix that "SwWeb migrate" writes; else the relative url from the page.
        /// </summary>
        /// <param name="pageWebRel">The page path relative to the web root</param>
        /// <param name="coreSrc">The src of the page's sw/core.js (or common/common.js) include</param>
        /// <param name="target">The file, relative to the web root (ex: "edit/Edit.js")</param>
        /// <returns>The url</returns>
        public static String IncludeUrl(String pageWebRel, String coreSrc, String target)
        {
            var src = (coreSrc ?? "").Split('?', '#')[0];
            foreach (var b in new[] { "sw/core.js", "common/common.js" })
                if (src.EndsWith(b, StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = src.Substring(0, src.Length - b.Length);
                    if ((prefix.Length == 0) || prefix.EndsWith("/", StringComparison.Ordinal) || prefix.EndsWith("}", StringComparison.Ordinal))
                        return prefix + target;
                }
            return RelativeUrl(pageWebRel, target);
        }

        /// <summary>
        /// The relative url from a page to a file (both relative to the web root), ex: ("app/x.html", "explore/table.js") gives "../explore/table.js".
        /// </summary>
        public static String RelativeUrl(String pageWebRel, String target)
        {
            var from = pageWebRel.Replace('\\', '/').Split('/');
            var to = target.Split('/');
            int common = 0;
            while ((common < from.Length - 1) && (common < to.Length - 1) && String.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
                ++common;
            var up = Enumerable.Repeat("..", from.Length - 1 - common);
            return String.Join("/", up.Concat(to.Skip(common)));
        }
    }
}
