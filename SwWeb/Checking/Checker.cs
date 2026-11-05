using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// The result of checking a folder.
    /// </summary>
    public sealed class CheckResult
    {
        /// <summary>
        /// All findings, sorted by path and position.
        /// </summary>
        public readonly List<Finding> Findings = new List<Finding>();
        /// <summary>
        /// The number of files whose content was checked.
        /// </summary>
        public int FilesChecked;
        /// <summary>
        /// The number of files skipped (vendored paths).
        /// </summary>
        public int FilesSkipped;
        /// <summary>
        /// The number of folders skipped (vendored paths, not searched).
        /// </summary>
        public int FoldersSkipped;
        /// <summary>
        /// The web roots that were checked when the folder is above them (relative paths; empty when the folder itself was checked).
        /// </summary>
        public readonly List<String> WebRoots = new List<String>();
        /// <summary>
        /// The number of error findings.
        /// </summary>
        public int Errors => Findings.Count(x => x.Severity == Severity.Error);
        /// <summary>
        /// The number of warning findings.
        /// </summary>
        public int Warnings => Findings.Count(x => x.Severity == Severity.Warning);
    }

    /// <summary>
    /// Lints web source files (.js, .css, .html and templated files) for the SysWeaver server contract rules
    /// (translation patterns, template variables, build / serving rules, JSDoc headers, legacy globals).
    /// </summary>
    public static class Checker
    {
        /// <summary>
        /// The translation function names the server translation rewrites.
        /// </summary>
        public static readonly IReadOnlyList<String> TranslationFunctions = new[] { "_T", "_TF", "_TH", "_TFH" };

        /// <summary>
        /// The marker of an optional online feature: in a comment on the line of an internet url (or the line before) it allows the
        /// url (rules external-resource and external-request), ex: <c>// SwWeb:allow-online (optional, degrades offline)</c>.
        /// </summary>
        public const String AllowOnline = "SwWeb:allow-online";

        /// <summary>
        /// The marker of deliberate element css: in a comment on the line of a selector (or the line before) it allows a bare element rule
        /// (rules element-css and body-overflow), ex: <c>/* SwWeb:allow-element-css (a full window app, its panes scroll) */</c>.
        /// </summary>
        public const String AllowElementCss = "SwWeb:allow-element-css";

        /// <summary>
        /// File extensions of fonts (not embedded by the build).
        /// </summary>
        public static readonly HashSet<String> FontExtensions = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { ".woff", ".woff2", ".ttf", ".otf", ".eot" };

        static readonly HashSet<String> TrNames = new HashSet<String>(TranslationFunctions, StringComparer.Ordinal);

        /// <summary>
        /// The server translation pattern (matches anywhere, as the server does).
        /// </summary>
        static readonly Regex TrPattern = new Regex(@"_T(?:FH|F|H)?\s*\(", RegexOptions.CultureInvariant);

        /// <summary>
        /// Server template variable prefixes that must not appear after "${" in JS.
        /// </summary>
        static readonly HashSet<String> ServerVarPrefixes = new HashSet<String>(StringComparer.Ordinal) { "Session", "Env", "EnvInfo", "Color", "Server", "Request" };

        static readonly Regex TextServerVar = new Regex(@"\$\{[_^~]*[@#%$\u00a3\u00a4*!/]*(?:(?:Session|Env|EnvInfo|Color|Server|Request)\.|(?:Tr|Vr)\d+\})", RegexOptions.CultureInvariant);

        static readonly Regex TrVarName = new Regex(@"^(?:Tr|Vr)\d+$", RegexOptions.CultureInvariant);

        static readonly HashSet<String> KnownEnvInfo = new HashSet<String>(StringComparer.Ordinal)
        {
            "AppName", "AppDisplayName", "AppDescription", "AppVersion", "AppBuildTime", "AppStart", "AppAssemblyName", "AppGuid", "UserName", "Is64BitProcess", "OSVersion", "Platform",
            "MachineName", "MachineNameCased", "ExeAppName", "Executable", "ExecutableDir", "ExecutableBase", "KeyFolder",
        };

        static readonly HashSet<String> KnownFixedVars = new HashSet<String>(StringComparer.Ordinal)
        {
            "Color.Background", "Color.Color", "Color.Acc1", "Color.Acc2", "Color.Focus", "Color.Scheme",
            "Client.OptimizedSend",
            "Server.UTC", "Request.Prefix", "Request.IP",
            "Session.Lang", "Session.User", "Session.UserName", "Session.Email", "Session.Domain", "Session.NickName",
        };

        /// <summary>
        /// Test if a template variable name (without case / encoding prefixes) is known by the server.
        /// </summary>
        /// <param name="name">The name</param>
        /// <param name="extra">Extra known names (config variables)</param>
        /// <returns>True if known</returns>
        public static bool IsKnownTemplateVar(String name, ICollection<String> extra = null)
        {
            if (KnownFixedVars.Contains(name))
                return true;
            if (name.StartsWith("Env.", StringComparison.Ordinal) && (name.Length > 4))
                return true;
            if (name.StartsWith("EnvInfo.", StringComparison.Ordinal) && KnownEnvInfo.Contains(name.Substring(8)))
                return true;
            if (TrVarName.IsMatch(name))
                return true;
            return (extra != null) && extra.Contains(name);
        }

        /// <summary>
        /// Remove the case transform and encoding prefixes of a template variable.
        /// </summary>
        /// <param name="raw">The text between "${" and "}"</param>
        /// <returns>The variable name</returns>
        public static String StripTemplatePrefix(String raw)
        {
            int i = 0;
            while ((i < raw.Length) && ("_^~".IndexOf(raw[i]) >= 0))
                ++i;
            while ((i < raw.Length) && ("@#%$\u00a3\u00a4*!/".IndexOf(raw[i]) >= 0))
                ++i;
            return raw.Substring(i);
        }

        /// <summary>
        /// A file being checked.
        /// </summary>
        public sealed class FileContext
        {
            /// <summary>
            /// Create a context.
            /// </summary>
            /// <param name="rel">The relative path (forward slashes)</param>
            /// <param name="source">The source</param>
            /// <param name="options">The options</param>
            /// <param name="findings">Receives the findings</param>
            /// <param name="webRel">The path relative to the web root (srcWeb) when the checked folder is below it; defaults to rel</param>
            public FileContext(String rel, SourceText source, CheckOptions options, List<Finding> findings, String webRel = null)
            {
                Rel = rel;
                WebRel = webRel ?? rel;
                Source = source;
                Text = source.Text;
                Options = options;
                Findings = findings;
                IsTemplated = options.TemplatedFiles.Contains(WebRel);
            }

            /// <summary>The relative path.</summary>
            public readonly String Rel;
            /// <summary>The path relative to the web root (srcWeb): classifies framework, brand and templated files, so checking a sub folder gives the same result as checking the whole web root.</summary>
            public readonly String WebRel;
            /// <summary>The source.</summary>
            public readonly SourceText Source;
            /// <summary>The text.</summary>
            public readonly String Text;
            /// <summary>The options.</summary>
            public readonly CheckOptions Options;
            /// <summary>The findings.</summary>
            public readonly List<Finding> Findings;
            /// <summary>True if the file is a server template.</summary>
            public readonly bool IsTemplated;
            /// <summary>True if the file is a new-format page that loads the compatibility bundle sw/compat.js (set by CheckHtmlFile).</summary>
            public bool IsCompatPage;

            /// <summary>True for a html document that loads no Sw bundle (an export, a redirect, a framework-free page reported by no-framework): its inline
            /// scripts are not judged as page code of a UI page (own fetch).</summary>
            public bool NotUiPage;

            /// <summary>True for a script / style sheet that only pages marked \"SwWeb:no-framework\" load: no design rules (they judge pages on the framework).</summary>
            public bool SkipDesign;

            /// <summary>
            /// True for a page with its own look (<see cref="PageChecker.IsOwnLook"/>: a messaging only page without the Sw components, or
            /// one marked with &lt;meta name="sw-own-look"&gt; / data-sw-own-look) and for a script / style sheet that only such pages load: the
            /// Design v2 rules and element-css don't apply (there are no Sw components and no --sw-* tokens to use); the UX guide rules do (S6 F6).
            /// </summary>
            public bool OwnLook;

            readonly HashSet<(int, String)> Reported = new HashSet<(int, String)>();

            /// <summary>
            /// True if the line of an offset, or the line before it, contains <see cref="AllowOnline"/>.
            /// </summary>
            /// <param name="offset">The offset</param>
            /// <returns>True if allowed</returns>
            public bool IsAllowedOnline(int offset) => HasMarker(offset, AllowOnline);

            /// <summary>
            /// True if the line of an offset, or the line before it, contains a marker.
            /// </summary>
            /// <param name="offset">The offset</param>
            /// <param name="marker">The marker text</param>
            /// <returns>True if found</returns>
            public bool HasMarker(int offset, String marker)
            {
                var ls = Source.LineStarts;
                var (l, _) = Source.GetLineCol(offset);
                var from = ls[Math.Max(0, l - 2)];
                var to = l < ls.Length ? ls[l] : Text.Length;
                return Text.IndexOf(marker, from, to - from, StringComparison.Ordinal) >= 0;
            }

            /// <summary>
            /// Add a finding (duplicates at the same offset and rule are ignored; an internet url on a line marked
            /// <see cref="AllowOnline"/>, or below such a line, is allowed).
            /// </summary>
            public void Add(Rule rule, int offset, String message) => Add(rule, offset, message, rule.Severity);

            /// <summary>
            /// Add a finding with a severity other than the rule's default (see <see cref="Add(Rule, int, String)"/>).
            /// </summary>
            public void Add(Rule rule, int offset, String message, Severity severity)
            {
                if (((rule == Rule.ExternalResource) || (rule == Rule.ExternalRequest)) && IsAllowedOnline(offset))
                    return;
                if (((rule == Rule.ElementCss) || (rule == Rule.BodyOverflow)) && HasMarker(offset, AllowElementCss))
                    return;
                if (rule.Design && (!Options.Design || SkipDesign))
                    return;
                if (OwnLook && ((rule.Design && (rule.Guide == null)) || (rule == Rule.ElementCss)))
                    return;
                //  A UX guide rule: "SwWeb:allow ux-..." names the rule (the rules that were design rules before also keep "SwWeb:allow-design")
                if (rule.Design && ((rule.Guide == null) || UxChecker.KeepsDesignAllow(rule)) && HasMarker(offset, DesignChecker.AllowDesign))
                    return;
                if ((rule.Guide != null) && UxChecker.IsAllowed(this, offset, rule))
                    return;
                if (!Reported.Add((offset, rule.Id)))
                    return;
                var (l, c) = Source.GetLineCol(offset);
                Findings.Add(new Finding { Path = Rel, Line = l, Col = c, Rule = rule, Severity = severity, Message = message });
            }
        }

        /// <summary>
        /// Get the path of a checked folder relative to its web root: the nearest enclosing folder named "srcWeb" or "srcData"
        /// (ex: ".../srcWeb/brand" gives "brand/"), or "" when the folder is a web root itself or isn't below one.
        /// </summary>
        /// <param name="rootFull">The full path of the checked folder</param>
        /// <returns>The prefix ("" or ending with "/")</returns>
        public static String GetWebPrefix(String rootFull)
        {
            var dir = new DirectoryInfo(rootFull);
            for (var d = dir; d != null; d = d.Parent)
            {
                if (String.Equals(d.Name, "srcWeb", StringComparison.OrdinalIgnoreCase) || String.Equals(d.Name, "srcData", StringComparison.OrdinalIgnoreCase))
                {
                    if (d.FullName.TrimEnd('\\', '/').Length >= dir.FullName.TrimEnd('\\', '/').Length)
                        return "";
                    return Path.GetRelativePath(d.FullName, dir.FullName).Replace('\\', '/').TrimEnd('/') + "/";
                }
            }
            return "";
        }

        /// <summary>
        /// Check a folder recursively.
        /// </summary>
        /// <param name="root">The folder</param>
        /// <param name="options">The options</param>
        /// <returns>The result</returns>
        public static CheckResult CheckFolder(String root, CheckOptions options)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            //  A folder above the web roots (a repository, a solution folder): check each srcWeb / srcData folder in it, nothing else
            //  (build outputs such as web/**, *.Services.json manifests and the project folder names are not web files)
            var webRoots = FindWebRoots(rootFull, options);
            //  The bundle manifest of rule bundle-overlap: the checked web root's (a changed sw/bundles.json is judged as it is), else built in
            if (options.Bundles == null)
            {
                IEnumerable<String> roots = webRoots?.Select(r => Path.Combine(rootFull, r.Replace('/', Path.DirectorySeparatorChar)));
                if (roots == null)
                {
                    var wr = rootFull;
                    for (var d = new DirectoryInfo(rootFull); d != null; d = d.Parent)
                        if (String.Equals(d.Name, "srcWeb", StringComparison.OrdinalIgnoreCase))
                        {
                            wr = d.FullName;
                            break;
                        }
                    roots = new[] { wr };
                }
                foreach (var r in roots)
                {
                    var mf = Path.Combine(r, "sw", "bundles.json");
                    if (!File.Exists(mf))
                        continue;
                    try
                    {
                        options.Bundles = Bundling.BundleManifest.Load(mf);
                        break;
                    }
                    catch
                    {
                    }
                }
            }
            if (options.KnownTokens == null)
            {
                //  The framework tokens plus every definition in the checked web roots (a folder inside a web root: the whole web root)
                var known = new HashSet<String>(TokenChecker.FrameworkTokens, StringComparer.Ordinal);
                var own = options.OwnProperties ??= new Dictionary<String, List<String>>(StringComparer.Ordinal);
                var sizes = options.UxSizes ??= new UxChecker.SizeIndex();
                void Own(String rel, String text)
                {
                    sizes.Collect(rel, text);
                    if (!IsFrameworkCss(rel) && !IsLibraryPath(rel))
                        DesignChecker.CollectOwnProperties(text, own);
                }
                if (webRoots == null)
                {
                    var wr = rootFull;
                    for (var d = new DirectoryInfo(rootFull); d != null; d = d.Parent)
                        if (String.Equals(d.Name, "srcWeb", StringComparison.OrdinalIgnoreCase) || String.Equals(d.Name, "srcData", StringComparison.OrdinalIgnoreCase))
                        {
                            wr = d.FullName;
                            break;
                        }
                    TokenChecker.CollectFolder(wr, options, known, Own);
                }
                else
                    foreach (var r in webRoots)
                        TokenChecker.CollectFolder(Path.Combine(rootFull, r.Replace('/', Path.DirectorySeparatorChar)), options, known, Own);
                if (options.Overlay != null)
                    foreach (var t in options.Overlay.Values)
                    {
                        TokenChecker.CollectDefinitions(t, known);
                        DesignChecker.CollectOwnProperties(t, own);
                    }
                options.KnownTokens = known;
            }
            else if (options.Design && (options.UxSizes == null))
            {
                //  The reserved sizes of the css (rule ux-lay-1) when the token list was given
                var sizes = options.UxSizes = new UxChecker.SizeIndex();
                foreach (var r in webRoots ?? new List<String> { "" })
                    TokenChecker.CollectFolder(r.Length == 0 ? rootFull : Path.Combine(rootFull, r.Replace('/', Path.DirectorySeparatorChar)), options, new HashSet<String>(), sizes.Collect);
            }
            if (webRoots == null)
                return CheckWebFolder(rootFull, options);
            var res = new CheckResult();
            foreach (var wr in webRoots)
            {
                var sub = CheckWebFolder(Path.Combine(rootFull, wr.Replace('/', Path.DirectorySeparatorChar)), options);
                foreach (var f in sub.Findings)
                    f.Path = wr + "/" + f.Path;
                res.Findings.AddRange(sub.Findings);
                res.FilesChecked += sub.FilesChecked;
                res.FilesSkipped += sub.FilesSkipped;
                res.FoldersSkipped += sub.FoldersSkipped;
            }
            res.WebRoots.AddRange(webRoots);
            return res;
        }

        /// <summary>
        /// The web roots (folders named srcWeb or srcData) below a folder that is neither a web root nor inside one, as relative paths
        /// (forward slashes, sorted). Skipped folders (bin/, obj/, hidden ...) are not searched and a web root is not searched further.
        /// </summary>
        /// <param name="rootFull">The full path of the folder</param>
        /// <param name="options">The options (skip list), null for the default skip list</param>
        /// <returns>The web roots, or null if the folder is (inside) a web root or has none below it</returns>
        public static List<String> FindWebRoots(String rootFull, CheckOptions options = null)
        {
            static bool IsWebRootName(String n) => String.Equals(n, "srcWeb", StringComparison.OrdinalIgnoreCase) || String.Equals(n, "srcData", StringComparison.OrdinalIgnoreCase);
            for (var d = new DirectoryInfo(rootFull); d != null; d = d.Parent)
                if (IsWebRootName(d.Name))
                    return null;
            var skip = new PathMatcher(options?.Skip ?? (IEnumerable<String>)CheckOptions.DefaultSkip);
            var found = new List<String>();
            void Find(String dir, String relDir, int depth)
            {
                if (depth > 8)
                    return;
                String[] dirs;
                try
                {
                    dirs = Directory.GetDirectories(dir);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                foreach (var d in dirs.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(d);
                    var rel = relDir.Length == 0 ? name : (relDir + "/" + name);
                    if (skip.IsFolderMatch(rel) || skip.IsFolderMatch(name))
                        continue;
                    if (IsWebRootName(name))
                        found.Add(rel);
                    else
                        Find(d, rel, depth + 1);
                }
            }
            Find(rootFull, "", 0);
            return found.Count == 0 ? null : found;
        }

        /// <summary>
        /// True if a folder is a srcData folder or inside one (server side data and templates, loaded as resources).
        /// </summary>
        static bool IsBelowDataRoot(String rootFull)
        {
            for (var d = new DirectoryInfo(rootFull); d != null; d = d.Parent)
            {
                if (String.Equals(d.Name, "srcData", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (String.Equals(d.Name, "srcWeb", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return false;
        }

        /// <summary>
        /// Read a file, or its <see cref="CheckOptions.Overlay"/> text.
        /// </summary>
        static SourceText ReadSource(String full, CheckOptions options) =>
            (options.Overlay != null) && options.Overlay.TryGetValue(full, out var t) ? new SourceText(t, full) : SourceText.FromFile(full);

        static CheckResult CheckWebFolder(String rootFull, CheckOptions options)
        {
            var res = new CheckResult();
            var skip = new PathMatcher(options.Skip);
            //  Path of the checked folder below its web root (srcWeb), so a sub folder is classified like a full check
            var webPrefix = GetWebPrefix(rootFull);
            //  A srcData root (or a folder below one): server side data, see the file walk
            bool isDataRoot = IsBelowDataRoot(rootFull);
            var pageScripts = new SortedDictionary<String, String>(StringComparer.Ordinal);
            //  Scripts only used by pages that load sw/compat.js (legacy names are warnings there)
            var compatScripts = new Dictionary<String, bool>(StringComparer.Ordinal);
            var findings = res.Findings;
            //  Port steps of framework-free pages already reported (a script / style sheet shared by pages): (path, offset)
            var ffReported = new HashSet<(String, int)>();

            //  Which pages load which scripts (shared helper scripts, rule missing-include)
            var index = new PageIndex();
            void IndexPages(String dir, String relDir)
            {
                foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var rel = relDir.Length == 0 ? Path.GetFileName(d) : (relDir + "/" + Path.GetFileName(d));
                    if (!skip.IsFolderMatch(webPrefix + rel))
                        IndexPages(d, rel);
                }
                foreach (var f in Directory.GetFiles(dir, "*.htm*"))
                {
                    var rel = relDir.Length == 0 ? Path.GetFileName(f) : (relDir + "/" + Path.GetFileName(f));
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (((ext == ".html") || (ext == ".htm")) && !skip.IsFileMatch(webPrefix + rel))
                        index.Add(webPrefix + rel, ReadSource(f, options).Text);
                }
            }
            IndexPages(rootFull, "");
            void Walk(String dir, String relDir)
            {
                foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(d);
                    var rel = relDir.Length == 0 ? name : (relDir + "/" + name);
                    if (skip.IsFolderMatch(webPrefix + rel))
                    {
                        ++res.FoldersSkipped;
                        continue;
                    }
                    CheckFolderName(rel, findings);
                    Walk(d, rel);
                }
                foreach (var f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(f);
                    var rel = relDir.Length == 0 ? name : (relDir + "/" + name);
                    if (skip.IsFileMatch(webPrefix + rel))
                    {
                        ++res.FilesSkipped;
                        continue;
                    }
                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    //  A srcData folder holds server side data (fonts for the svg renderer, models ...) loaded as resources, not served by url:
                    //  only its web files (templates) follow the url naming rules
                    bool webFile = !isDataRoot || (ext == ".js") || (ext == ".css") || (ext == ".html") || (ext == ".htm");
                    if (webFile)
                        CheckFileName(rel, findings);
                    if (webFile && FontExtensions.Contains(ext))
                        findings.Add(new Finding { Path = rel, Rule = Rule.FontFile, Severity = Rule.FontFile.Severity, Message = "\"" + name + "\" is a font file: the build doesn't embed it, so it is missing in the deployed app (embed the font as a data: url in a .css file, see brand/fonts.css)" });
                    bool templated = options.TemplatedFiles.Contains(webPrefix + rel);
                    //  Documents of a web folder (README.md ...): only the internal id rule (strict)
                    if (options.Strict && ((ext == ".md") || (ext == ".txt")) && !templated && !IsBundlePath(webPrefix + rel))
                        CheckInternalIds(new FileContext(rel, ReadSource(f, options), options, findings, webPrefix + rel));
                    if ((ext != ".js") && (ext != ".css") && (ext != ".html") && (ext != ".htm") && !templated)
                        continue;
                    ++res.FilesChecked;
                    var src = ReadSource(f, options);
                    var ctx = new FileContext(rel, src, options, findings, webPrefix + rel);
                    if ((ext == ".js") || (ext == ".css"))
                    {
                        ctx.SkipDesign = index.OnlyNoFrameworkPages(webPrefix + rel);
                        ctx.OwnLook = index.OnlyOwnLookPages(webPrefix + rel);
                    }
                    else if ((ext == ".html") || (ext == ".htm"))
                        ctx.OwnLook = PageChecker.IsOwnLook(src.Text);
                    if (options.Strict && !IsBundlePath(webPrefix + rel))
                        CheckInternalIds(ctx);
                    if (!IsBundlePath(webPrefix + rel))
                        TokenChecker.Check(ctx, options.KnownTokens);
                    switch (ext)
                    {
                        case ".js":
                            CheckJsFile(ctx);
                            break;
                        case ".css":
                            CheckCssFile(ctx);
                            break;
                        case ".html":
                        case ".htm":
                            foreach (var s in CheckHtmlFile(ctx))
                            {
                                var full = ResolveScript(rootFull, Path.GetDirectoryName(f), s);
                                if (full == null)
                                    continue;
                                var srel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
                                if (skip.IsFileMatch(webPrefix + srel) || IsLibraryPath(srel))
                                    continue;
                                pageScripts.TryAdd(srel, rel);
                                compatScripts[srel] = (!compatScripts.TryGetValue(srel, out var wasCompat) || wasCompat) && ctx.IsCompatPage;
                            }
                            {
                                //  The page and its own scripts: Design v2 page header, includes, script order, sw-root
                                var own = new List<PageScriptText>();
                                foreach (var sc in HtmlScanner.GetScripts(HtmlScanner.Scan(ctx.Text)))
                                {
                                    var full = sc.Src == null ? null : ResolveScript(rootFull, Path.GetDirectoryName(f), sc.Src);
                                    if (full == null)
                                        continue;
                                    var srel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
                                    if (!skip.IsFileMatch(webPrefix + srel) && !IsLibraryPath(srel))
                                        own.Add(new PageScriptText(webPrefix + srel, ReadSource(full, options).Text));
                                }
                                if (options.Design)
                                    DesignChecker.CheckPageHeader(ctx, own.Select(x => x.Text));
                                TextChecker.CheckPage(ctx, HtmlScanner.Scan(ctx.Text), own);
                                PageChecker.CheckPage(ctx, own, index);
                            }
                            if (!isDataRoot)
                                CheckFrameworkFree(ctx, rootFull, Path.GetDirectoryName(f), webPrefix, skip, options, findings, ffReported);
                            break;
                        default:
                            CheckTemplated(ctx);
                            break;
                    }
                }
            }
            Walk(rootFull, "");
            if (options.Strict)
            {
                foreach (var kv in pageScripts)
                {
                    var src = ReadSource(Path.Combine(rootFull, kv.Key.Replace('/', Path.DirectorySeparatorChar)), options);
                    var ctx = new FileContext(kv.Key, src, options, findings, webPrefix + kv.Key);
                    CheckLegacyGlobals(ctx, JsLexer.Tokenize(ctx.Text), "the new-format page \"" + kv.Value + "\"", compatScripts[kv.Key]);
                }
            }
            res.Findings.Sort((a, b) =>
            {
                var x = String.CompareOrdinal(a.Path, b.Path);
                if (x != 0)
                    return x;
                x = a.Line.CompareTo(b.Line);
                if (x != 0)
                    return x;
                x = a.Col.CompareTo(b.Col);
                return x != 0 ? x : String.CompareOrdinal(a.Rule.Id, b.Rule.Id);
            });
            return res;
        }

        /// <summary>
        /// Paths of the shared library / bundle files (not page scripts).
        /// </summary>
        /// <summary>
        /// The rule no-framework for one HTML page: a UI page that loads no SysWeaver bundle (see <see cref="FrameworkFreeChecker"/>) is reported,
        /// an error with --strict inside a srcWeb folder, plus its port steps at their lines (warnings) in the page and its own scripts / style sheets.
        /// </summary>
        static void CheckFrameworkFree(FileContext ctx, String rootFull, String pageDir, String webPrefix, PathMatcher skip, CheckOptions options, List<Finding> findings, HashSet<(String, int)> reported)
        {
            var nodes = HtmlScanner.Scan(ctx.Text);
            if (!FrameworkFreeChecker.IsFrameworkFree(ctx.WebRel, ctx.Text, nodes, out var anchor))
                return;
            var own = new List<FrameworkFreeChecker.OwnFile>();
            var ctxs = new List<FileContext>();
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                bool css = (n.Name == "link") && String.Equals(n.GetAttribute("rel")?.Value?.Trim(), "stylesheet", StringComparison.OrdinalIgnoreCase);
                var url = (n.Name == "script") ? n.GetAttribute("src") : css ? n.GetAttribute("href") : null;
                if (url?.Value == null)
                    continue;
                var full = ResolveScript(rootFull, pageDir, url.Value);
                if (full == null)
                    continue;
                var srel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
                if (skip.IsFileMatch(webPrefix + srel) || IsLibraryPath(webPrefix + srel) || IsFrameworkCss(webPrefix + srel))
                    continue;
                var src = ReadSource(full, options);
                own.Add(new FrameworkFreeChecker.OwnFile(webPrefix + srel, src.Text, css));
                ctxs.Add(new FileContext(srel, src, options, findings, webPrefix + srel));
            }
            var details = FrameworkFreeChecker.GetDetails(ctx.Text, nodes, own);
            //  An error with --strict in a srcWeb folder (a page of a site / service), else a warning
            bool inSrcWeb = false;
            for (var d = new DirectoryInfo(rootFull); (d != null) && !inSrcWeb; d = d.Parent)
                inSrcWeb = String.Equals(d.Name, "srcWeb", StringComparison.OrdinalIgnoreCase);
            ctx.Add(Rule.NoFramework, anchor.Start, FrameworkFreeChecker.PageMessage(details.Count), options.Strict && inSrcWeb ? Severity.Error : Severity.Warning);
            foreach (var d in details)
            {
                var c = d.File < 0 ? ctx : ctxs[d.File];
                if (reported.Add((c.Rel, d.Offset)))
                    c.Add(Rule.NoFramework, d.Offset, "port step: " + d.Message, Severity.Warning);
            }
        }

        /// <summary>The generated bundles (sw/): their sources are checked instead.</summary>
        internal static bool IsBundlePath(String rel) => rel.StartsWith("sw/", StringComparison.Ordinal);

        internal static bool IsLibraryPath(String rel) =>
            rel.StartsWith("sw/", StringComparison.Ordinal) || rel.StartsWith("core/", StringComparison.Ordinal) || rel.StartsWith("ui/", StringComparison.Ordinal) || rel.StartsWith("shell/", StringComparison.Ordinal)
            || (rel == "common/common.js") || (rel == "app/application.js");

        internal static String ResolveScript(String rootFull, String htmlDir, String src)
        {
            var s = src.Trim();
            var q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
                s = s.Substring(0, q);
            if ((s.Length == 0) || s.StartsWith('/') || s.Contains("://") || s.Contains("${") || s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;
            var full = Path.GetFullPath(Path.Combine(htmlDir, s));
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            return File.Exists(full) ? full : null;
        }

        /// <summary>
        /// Check a folder name (relative path, forward slashes).
        /// </summary>
        /// <param name="rel">The relative folder path</param>
        /// <param name="findings">Receives the findings</param>
        public static void CheckFolderName(String rel, List<Finding> findings)
        {
            var name = rel.Substring(rel.LastIndexOf('/') + 1);
            if (name.Contains('-'))
                findings.Add(new Finding { Path = rel + "/", Rule = Rule.FolderNameDash, Severity = Rule.FolderNameDash.Severity, Message = "folder name \"" + name + "\" contains '-' (embedded as '" + name.Replace('-', '_') + "', so its URLs change)" });
            if ((name.Length > 0) && Char.IsDigit(name[0]))
                findings.Add(new Finding { Path = rel + "/", Rule = Rule.FolderNameDigit, Severity = Rule.FolderNameDigit.Severity, Message = "folder name \"" + name + "\" starts with a digit (embedded as '_" + name + "')" });
            if (name.IndexOf('.', 1) > 0)
                findings.Add(new Finding { Path = rel + "/", Rule = Rule.FolderNameDot, Severity = Rule.FolderNameDot.Severity, Message = "folder name \"" + name + "\" contains a dot, its files would be served below \"" + name.Replace('.', '/') + "/\"" });
        }

        /// <summary>
        /// Check a file name (relative path, forward slashes).
        /// </summary>
        /// <param name="rel">The relative file path</param>
        /// <param name="findings">Receives the findings</param>
        public static void CheckFileName(String rel, List<Finding> findings)
        {
            var name = rel.Substring(rel.LastIndexOf('/') + 1);
            if (name.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
                findings.Add(new Finding { Path = rel, Rule = Rule.MjsFile, Severity = Rule.MjsFile.Severity, Message = "\"" + name + "\": .mjs files are not embedded, use a classic .js script" });
            var dots = name.Count(x => x == '.');
            if ((dots > 1) && !name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) && !((dots == 2) && name.StartsWith('.')))
            {
                var i = name.LastIndexOf('.');
                var served = name.Substring(0, i).Replace('.', '/') + name.Substring(i);
                findings.Add(new Finding { Path = rel, Rule = Rule.FileNameDots, Severity = Rule.FileNameDots.Severity, Message = "\"" + name + "\" has more than one dot, it would be served as \"" + served + "\"" });
            }
        }

        static void CheckEncoding(FileContext ctx)
        {
            if (ctx.Source.InvalidUtf8)
                ctx.Add(Rule.Encoding, 0, "the file is not valid UTF-8");
        }

        /// <summary>
        /// Check a .js file.
        /// </summary>
        /// <param name="ctx">The file</param>
        public static void CheckJsFile(FileContext ctx)
        {
            CheckEncoding(ctx);
            var tokens = JsLexer.Tokenize(ctx.Text);
            // JSDoc file header
            // The header must be the first comment / code, a leading "use strict" directive is allowed
            JsToken? first = null;
            bool directive = false;
            foreach (var t in tokens)
            {
                if (t.Kind == JsTokenKind.Whitespace)
                    continue;
                if (!directive && (t.Kind == JsTokenKind.String))
                {
                    var d = t.GetText(ctx.Text);
                    if ((d == "\"use strict\"") || (d == "'use strict'"))
                    {
                        directive = true;
                        continue;
                    }
                }
                if (directive && (t.Kind == JsTokenKind.Punctuator) && t.Is(ctx.Text, ";"))
                    continue;
                first = t;
                break;
            }
            //  The brand hook app/app.js is a deployment config file (WebBranding.md), not a library or page script: no header required
            if (IsBrandJs(ctx.WebRel))
            {
            }
            else if (first == null)
                ctx.Add(Rule.JsDocHeader, 0, "empty file, expected a JSDoc file header (/** @file ... */)");
            else
            {
                var ft = first.Value;
                var txt = ft.GetText(ctx.Text);
                if ((ft.Kind != JsTokenKind.BlockComment) || !txt.StartsWith("/**", StringComparison.Ordinal) || !txt.Contains("@file", StringComparison.Ordinal))
                    ctx.Add(Rule.JsDocHeader, ft.Start, "the file must start with a JSDoc file header (/** @file ... */)");
            }
            CheckDebugMarkers(ctx);
            CheckJsTokens(ctx, tokens);
            TextChecker.CheckJs(ctx, tokens);
            if (ctx.Options.Design)
            {
                DesignChecker.CheckJs(ctx, tokens);
                UxChecker.CheckJs(ctx, tokens);
            }
            if (ctx.IsTemplated)
                CheckTemplated(ctx);
        }

        /// <summary>
        /// The internal change tracking ids that shipped files must not name (rule internal-id): fix round ids of the form
        /// C&lt;n&gt;R&lt;n&gt;[-Name[-n]], F&lt;nn&gt;-Name[-n], tracker ids CR-S&lt;n&gt;, CR-B&lt;n&gt;, XX-B&lt;n&gt; (CH, EX, SM, ME), B-U&lt;n&gt;,
        /// RD-&lt;n | Name&gt;, BB-&lt;n&gt;, S2F-Name; every tracker family &lt;area&gt;-&lt;letter&gt;&lt;n&gt; (B-C1, B-L3, B-S1, CR-S18, UM-S2, AU-B2, ED-B1 ...,
        /// also with a BB- prefix) and the critic round form CR&lt;n&gt;-A&lt;n&gt;; a number may have a letter suffix (CR-S18a, C3R1-Core-4b).
        /// Not matched: standard names such as UTF-8, SHA-256, ISO-8601, RFC-7231 (only digits after the dash).
        /// </summary>
        public static readonly Regex InternalIdPattern = new Regex(
            @"(?<![\w-])(?:C\dR\d(?:-[A-Z][A-Za-z]+(?:-\d+[a-z]?)?)?|F\d\d-[A-Z][A-Za-z]+(?:-\d+[a-z]?)?"
            + @"|(?:BB-)?(?:B|CR|EX|ME|UM|CH|ED|AU|AC|SM|LV|TB|PG)-[A-Z]\d+[a-z]?|CR\d-[A-Z]\d+[a-z]?"
            + @"|RD-(?:\d+|[A-Z][A-Za-z]+)|BB-\d+|S2F-[A-Z][A-Za-z]+)(?![\w-])",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// The internal-id rule (strict): a comment, string or document text names an internal change tracking id.
        /// </summary>
        static void CheckInternalIds(FileContext ctx)
        {
            foreach (Match m in InternalIdPattern.Matches(ctx.Text))
                ctx.Add(Rule.InternalId, m.Index, "\"" + m.Value + "\" is an internal change tracking id: readers of this file can't look it up; describe the rule or the reason itself");
        }

        static void CheckDebugMarkers(FileContext ctx)
        {
            var src = ctx.Source;
            var text = ctx.Text;
            var ls = src.LineStarts;
            int open = -1;
            for (int i = 0; i < ls.Length; ++i)
            {
                var s = ls[i];
                var e = i + 1 < ls.Length ? ls[i + 1] : text.Length;
                var line = text.Substring(s, e - s);
                var x = new String(line.Where(c => (c != ' ') && (c != '\t') && (c != '\r') && (c != '\n')).ToArray());
                if (open >= 0)
                {
                    if (x.StartsWith("//DEBUG_END", StringComparison.Ordinal))
                        open = -1;
                    else if (x.StartsWith("//DEBUG_BEGIN", StringComparison.Ordinal))
                        ctx.Add(Rule.DebugMarkers, s, "nested //DEBUG_BEGIN (the first //DEBUG_END ends both)");
                    continue;
                }
                if (x.StartsWith("//DEBUG_BEGIN", StringComparison.Ordinal))
                    open = s;
                else if (x.StartsWith("//DEBUG_END", StringComparison.Ordinal))
                    ctx.Add(Rule.DebugMarkers, s, "//DEBUG_END without //DEBUG_BEGIN");
            }
            if (open >= 0)
                ctx.Add(Rule.DebugMarkers, open, "//DEBUG_BEGIN without //DEBUG_END (the release build removes the rest of the file)");
        }

        static String Article(String s) => ("aeiou".IndexOf(s[0]) >= 0 ? "an " : "a ") + s;

        static String Describe(JsTokenKind k) => k switch
        {
            JsTokenKind.String => "string literal",
            JsTokenKind.Regex => "regular expression",
            JsTokenKind.BlockComment => "block comment",
            JsTokenKind.Template or JsTokenKind.TemplateHead or JsTokenKind.TemplateMiddle or JsTokenKind.TemplateTail => "template literal",
            _ => k.ToString(),
        };

        /// <summary>
        /// Check JavaScript tokens (a .js file or a script element body).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens (offsets into the file text)</param>
        public static void CheckJsTokens(FileContext ctx, List<JsToken> tokens)
        {
            var text = ctx.Text;
            var sig = new List<int>(tokens.Count);
            bool unterminated = false;
            for (int i = 0; i < tokens.Count; ++i)
            {
                var t = tokens[i];
                if (t.Unterminated)
                {
                    ctx.Add(Rule.Syntax, t.Start, "unterminated " + Describe(t.Kind));
                    unterminated = true;
                }
                if (t.IsSignificant)
                    sig.Add(i);
                // Translation patterns and server variables inside text
                if (t.IsComment || (t.Kind == JsTokenKind.String) || t.IsTemplate || (t.Kind == JsTokenKind.Regex))
                {
                    var tt = t.GetText(text);
                    foreach (Match m in TrPattern.Matches(tt))
                        ctx.Add(Rule.TrInText, t.Start + m.Index, "\"" + m.Value + "\" inside a " + (t.IsComment ? "comment" : Describe(t.Kind)) + " (the server rewrites it as a translation call)");
                    if (!ctx.IsTemplated)
                        foreach (Match m in TextServerVar.Matches(tt))
                            ctx.Add(Rule.TemplateLiteralVar, t.Start + m.Index, "\"" + m.Value + "\" inside a " + (t.IsComment ? "comment" : Describe(t.Kind)) + " is replaced by the server template engine");
                }
            }
            var sc = sig.Count;
            JsToken? Sig(int si) => (si >= 0) && (si < sc) ? tokens[sig[si]] : null;
            bool IsPunct(int si, String p)
            {
                var t = Sig(si);
                return (t != null) && (t.Value.Kind == JsTokenKind.Punctuator) && t.Value.Is(text, p);
            }
            CheckEsModule(ctx, tokens, sig);
            //  An unterminated token already reports the problem (the brackets after it are meaningless)
            if (!unterminated)
                CheckBrackets(ctx, tokens, sig);
            CheckExternalRequests(ctx, tokens, sig);
            MinifierChecker.Check(ctx, tokens, sig);
            for (int si = 0; si < sc; ++si)
            {
                var ti = sig[si];
                var t = tokens[ti];
                if ((t.Kind == JsTokenKind.TemplateHead) || (t.Kind == JsTokenKind.TemplateMiddle))
                {
                    var n = Sig(si + 1);
                    if ((n != null) && (n.Value.Kind == JsTokenKind.Identifier))
                    {
                        var name = n.Value.GetText(text);
                        if (ServerVarPrefixes.Contains(name) && IsPunct(si + 2, "."))
                            ctx.Add(Rule.TemplateLiteralVar, t.End - 2, "\"${" + name + ".\" in a template literal is replaced by the server template engine (use a variable)");
                        else if (TrVarName.IsMatch(name))
                        {
                            var nn = Sig(si + 2);
                            if ((nn != null) && ((nn.Value.Kind == JsTokenKind.TemplateMiddle) || (nn.Value.Kind == JsTokenKind.TemplateTail)))
                                ctx.Add(Rule.TemplateLiteralVar, t.End - 2, "\"${" + name + "}\" in a template literal is replaced by the server translation");
                        }
                    }
                    continue;
                }
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                var id = t.GetText(text);
                bool isTr = TrNames.Contains(id);
                bool suffix = !isTr && TranslationFunctions.Any(x => id.EndsWith(x, StringComparison.Ordinal));
                if (!(isTr || suffix))
                    continue;
                // The server matches name + white space + '(' (no comments in between)
                if ((ti + 1 >= tokens.Count))
                    continue;
                int pi = ti + 1;
                if (tokens[pi].Kind == JsTokenKind.Whitespace)
                    ++pi;
                if ((pi >= tokens.Count) || (tokens[pi].Kind != JsTokenKind.Punctuator) || !tokens[pi].Is(text, "("))
                {
                    // Called with a comment in between: not seen by the server
                    if (isTr && IsPunct(si + 1, "("))
                        ctx.Add(Rule.TrNotLiteral, t.Start, "comment between \"" + id + "\" and '(' (not translated by the server)");
                    //  Used as a value (const t = _T; list.map(_T)): the aliased calls are never translated. Not: the definition
                    //  (function _T, var _T =, _T = ...), a member of the same name (x._T, { _T: ... }), typeof _T
                    else if (isTr)
                    {
                        var pv = Sig(si - 1);
                        bool decl = (pv != null) && (pv.Value.Kind == JsTokenKind.Identifier) && (pv.Value.Is(text, "function") || pv.Value.Is(text, "var") || pv.Value.Is(text, "let") || pv.Value.Is(text, "const") || pv.Value.Is(text, "typeof"));
                        bool assigned = IsPunct(si + 1, "=") && !IsPunct(si - 1, "=");
                        bool member = IsPunct(si - 1, ".") || IsPunct(si - 1, "?.") || (IsPunct(si + 1, ":") && (IsPunct(si - 1, "{") || IsPunct(si - 1, ",")));
                        if (!decl && !assigned && !member)
                            ctx.Add(Rule.TrAlias, t.Start, "\"" + id + "\" used as a value: the server only translates direct calls " + id + "(\"literal\" ...), so the texts of an alias / callback are never translated; call " + id + " directly with each literal (PORTING.md A9)");
                    }
                    continue;
                }
                if (suffix)
                {
                    ctx.Add(Rule.TrIdentifierSuffix, t.Start, "\"" + id + "(\" ends with a translation function name, the server rewrites it (rename it)");
                    continue;
                }
                // The runtime definition "function _T(text, ...)" (the server skips it, the first argument isn't a literal)
                var prev = Sig(si - 1);
                if ((prev != null) && (prev.Value.Kind == JsTokenKind.Identifier) && prev.Value.Is(text, "function"))
                    continue;
                if (IsPunct(si - 1, ".") || IsPunct(si - 1, "?."))
                {
                    ctx.Add(Rule.TrMemberCall, t.Start, "\"." + id + "(\" is rewritten by the server into invalid JS (call the global " + id + " directly)");
                    continue;
                }
                // Arguments
                var args = SplitArgs(tokens, sig, si + 1, text);
                if (args.Count == 0)
                {
                    ctx.Add(Rule.TrNotLiteral, t.Start, "\"" + id + "()\" without arguments");
                    continue;
                }
                var a0 = args[0];
                if ((a0.Count != 1) || (tokens[a0[0]].Kind != JsTokenKind.String))
                {
                    var f = tokens[a0[0]];
                    var why = f.IsTemplate ? "a backtick template literal" : (a0.Count == 1 ? Article(Describe(f.Kind).ToLowerInvariant()) : "an expression");
                    ctx.Add(Rule.TrNotLiteral, f.Start, "the first argument of \"" + id + "\" is " + why + ", it must be a '...' or \"...\" literal");
                }
                else
                    CheckTranslatedLiteral(ctx, tokens[a0[0]], id);
                // Context argument: the last argument if it is a quoted literal
                if (args.Count > 1)
                {
                    var al = args[args.Count - 1];
                    if ((al.Count == 1) && (tokens[al[0]].Kind == JsTokenKind.String))
                        CheckTranslatedLiteral(ctx, tokens[al[0]], id);
                }
                if ((a0.Count == 1) && (tokens[a0[0]].Kind == JsTokenKind.String))
                    CheckTrFormat(ctx, tokens, t, id, args);
            }
        }

        /// <summary>A placeholder of a translated format ({0}, {_1}, {^2}, {$3}, see core/i18n.js).</summary>
        static readonly Regex TrPlaceholder = new Regex(@"\{[_^$]?(\d+)\}", RegexOptions.CultureInvariant);

        /// <summary>
        /// The tr-format rule: _TF / _TFH never format (extra arguments are dropped at run time and, once translated, passed to _T with
        /// the context as {0}); a placeholder of _T / _TH needs an argument (a trailing quoted literal is the translation context, not an argument).
        /// </summary>
        static void CheckTrFormat(FileContext ctx, List<JsToken> tokens, JsToken call, String id, List<List<int>> args)
        {
            var text = ctx.Text;
            var lit = JsLexer.DecodeStringLiteral(tokens[args[0][0]].GetText(text));
            var al = args[args.Count - 1];
            bool hasContext = (args.Count > 1) && (al.Count == 1) && (tokens[al[0]].Kind == JsTokenKind.String);
            int fmtArgs = args.Count - 1 - (hasContext ? 1 : 0);
            bool isFixed = id.StartsWith("_TF", StringComparison.Ordinal);
            if (isFixed)
            {
                if (fmtArgs > 0)
                    ctx.Add(Rule.TrFormat, call.Start, "\"" + id + "\" never formats, its " + fmtArgs + " format argument(s) are dropped (the page shows the placeholders; once translated they are passed on with the context as {0}): use _T" + (id.EndsWith("H", StringComparison.Ordinal) ? "H" : "") + "(\"...{0}...\", value, \"context\"), see PORTING.md A9");
                return;
            }
            //  A spread argument: the count is unknown
            for (int i = 1; i < args.Count; ++i)
                if ((args[i].Count > 0) && tokens[args[i][0]].Is(text, "..."))
                    return;
            int max = -1;
            foreach (Match m in TrPlaceholder.Matches(lit))
                if (int.TryParse(m.Groups[1].Value, out var n))
                    max = Math.Max(max, n);
            if (max >= fmtArgs)
            {
                var why = hasContext && (max == fmtArgs)
                    ? " (the trailing literal " + tokens[al[0]].GetText(text) + " is the translation context, not {" + max + "}: pass the value as a variable or expression, then the context)"
                    : "";
                if (fmtArgs > 0)
                    ctx.Add(Rule.TrFormat, call.Start, "\"" + id + "\" uses {" + max + "} but has " + fmtArgs + " format argument(s): the page shows \"{" + max + "}\"" + why);
                else
                    //  No argument at all: maybe a format formatted later (then it should be _TF), maybe a missing argument
                    ctx.Add(Rule.TrFormat, call.Start, "\"" + id + "\" uses {" + max + "} without format arguments: the page shows \"{" + max + "}\"" + why
                        + "; a text that is formatted later (Sw.I18n.Format(text, ...)) is marked with _TF", Severity.Warning);
            }
            else if ((max < 0) && (fmtArgs > 0))
                ctx.Add(Rule.TrFormat, call.Start, "\"" + id + "\" has " + fmtArgs + " format argument(s) but no {0} placeholder: the values are never shown (add the placeholders, or drop the arguments)", Severity.Warning);
        }

        /// <summary>
        /// Report unbalanced brackets of a script: (), [], {} and template substitutions ${}. Only the first problem is reported
        /// (the rest would follow from it).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="sig">The indices of the significant tokens</param>
        static void CheckBrackets(FileContext ctx, List<JsToken> tokens, List<int> sig)
        {
            var text = ctx.Text;
            var open = new Stack<(char C, int Offset)>();
            static String Name(char c) => c == '$' ? "\"${\"" : ("'" + c + "'");
            static char Closer(char c) => c switch { '(' => ')', '[' => ']', '{' => '}', _ => '}' };
            String At(int offset)
            {
                var (l, c) = ctx.Source.GetLineCol(offset);
                return "(" + l + "," + c + ")";
            }
            foreach (var ti in sig)
            {
                var t = tokens[ti];
                switch (t.Kind)
                {
                    case JsTokenKind.TemplateHead:
                        open.Push(('$', t.Start));
                        continue;
                    case JsTokenKind.TemplateMiddle:
                    case JsTokenKind.TemplateTail:
                        if ((open.Count == 0) || (open.Peek().C != '$'))
                        {
                            var why = open.Count == 0 ? "nothing is open" : (Name(open.Peek().C) + " at " + At(open.Peek().Offset) + " is still open");
                            ctx.Add(Rule.Syntax, t.Start, "unbalanced brackets: the end of a template substitution, but " + why);
                            return;
                        }
                        if (t.Kind == JsTokenKind.TemplateTail)
                            open.Pop();
                        continue;
                    case JsTokenKind.Punctuator:
                        break;
                    default:
                        continue;
                }
                if (t.Length != 1)
                    continue;
                var ch = text[t.Start];
                switch (ch)
                {
                    case '(':
                    case '[':
                    case '{':
                        open.Push((ch, t.Start));
                        break;
                    case ')':
                    case ']':
                    case '}':
                        if (open.Count == 0)
                        {
                            ctx.Add(Rule.Syntax, t.Start, "unbalanced brackets: '" + ch + "' without an opening bracket");
                            return;
                        }
                        var o = open.Pop();
                        if (Closer(o.C) != ch)
                        {
                            ctx.Add(Rule.Syntax, t.Start, "unbalanced brackets: '" + ch + "' closes " + Name(o.C) + " at " + At(o.Offset) + " (expected '" + Closer(o.C) + "')");
                            return;
                        }
                        break;
                }
            }
            if (open.Count > 0)
            {
                var o = open.Peek();
                ctx.Add(Rule.Syntax, o.Offset, "unbalanced brackets: " + Name(o.C) + " is never closed");
            }
        }

        /// <summary>
        /// The url text at the start of a string or template token (null for other tokens).
        /// </summary>
        static String LiteralStart(JsToken t, String text)
        {
            if (t.Unterminated)
                return null;
            switch (t.Kind)
            {
                case JsTokenKind.String:
                    return JsLexer.DecodeStringLiteral(t.GetText(text));
                case JsTokenKind.Template:
                case JsTokenKind.TemplateHead:
                    var s = t.GetText(text);
                    return s.Length > 1 ? s.Substring(1) : "";
                default:
                    return null;
            }
        }

        /// <summary>Functions whose first argument is an url that is requested.</summary>
        static readonly HashSet<String> RequestFunctions = new HashSet<String>(StringComparer.Ordinal) { "fetch", "importScripts" };

        /// <summary>Script / style sheet include helpers (Sw.Net.IncludeJs(baseUrl, file), the legacy includeJs(current, file)): both url arguments load.</summary>
        static readonly HashSet<String> IncludeFunctions = new HashSet<String>(StringComparer.Ordinal) { "IncludeJs", "IncludeCss", "includeJs", "includeCss" };

        /// <summary>Constructors whose first argument is an url that is requested.</summary>
        static readonly HashSet<String> RequestConstructors = new HashSet<String>(StringComparer.Ordinal) { "WebSocket", "EventSource", "Worker", "SharedWorker", "Request" };

        /// <summary>Global objects that a request function may be called on (window.fetch).</summary>
        static readonly HashSet<String> GlobalObjects = new HashSet<String>(StringComparer.Ordinal) { "window", "self", "globalThis", "top", "parent" };

        /// <summary>Element properties / attributes that load an url when set.</summary>
        static readonly HashSet<String> LoadingProperties = new HashSet<String>(StringComparer.Ordinal) { "src", "srcset", "poster", "data" };

        /// <summary>HTTP methods (the first argument of XMLHttpRequest.open).</summary>
        static readonly HashSet<String> HttpMethods = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "DELETE", "HEAD", "PATCH", "OPTIONS" };

        /// <summary>
        /// Report string literal internet urls that a script requests: <c>fetch("https://...")</c>, <c>importScripts</c>,
        /// <c>new WebSocket / EventSource / Worker / Request("...")</c>, <c>xhr.open("GET", "https://...")</c>, <c>navigator.sendBeacon</c>,
        /// <c>x.src = "https://..."</c> (also srcset, poster, data) and <c>x.setAttribute("src", "https://...")</c>. Only the start of the
        /// first literal is looked at (<c>"https://x/" + id</c> counts, an url built from variables doesn't).
        /// Also reports (as external-resource) an internet url in either argument of <c>Sw.Net.IncludeJs / IncludeCss</c> and the legacy
        /// <c>includeJs / includeCss</c>.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="sig">The indices of the significant tokens</param>
        static void CheckExternalRequests(FileContext ctx, List<JsToken> tokens, List<int> sig)
        {
            var text = ctx.Text;
            var sc = sig.Count;
            JsToken? Sig(int si) => (si >= 0) && (si < sc) ? tokens[sig[si]] : null;
            bool P(int si, String p)
            {
                var t = Sig(si);
                return (t != null) && (t.Value.Kind == JsTokenKind.Punctuator) && t.Value.Is(text, p);
            }
            bool Id(int si, String name)
            {
                var t = Sig(si);
                return (t != null) && (t.Value.Kind == JsTokenKind.Identifier) && t.Value.Is(text, name);
            }
            void Report(int si, String what)
            {
                var t = Sig(si);
                if (t == null)
                    return;
                var u = LiteralStart(t.Value, text);
                if ((u == null) || (GetUrlKind(u) != UrlKind.External))
                    return;
                var shown = u.Length > 80 ? (u.Substring(0, 80) + "...") : u;
                ctx.Add(Rule.ExternalRequest, t.Value.Start, what + " requests the internet url \"" + shown.Trim() + "\" (pages must work offline: serve the data from the app; an optional online feature that degrades without internet gets a \"" + AllowOnline + "\" comment)");
            }
            for (int si = 0; si < sc; ++si)
            {
                var t = tokens[sig[si]];
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                var name = t.GetText(text);
                bool member = P(si - 1, ".") || P(si - 1, "?.");
                if (P(si + 1, "("))
                {
                    if (IncludeFunctions.Contains(name))
                    {
                        //  Sw.Net.IncludeJs(baseUrl, file) / IncludeCss and the legacy includeJs / includeCss: an internet url in either argument
                        var args = SplitArgs(tokens, sig, si + 1, text);
                        for (int a = 0; (a < 2) && (a < args.Count); ++a)
                        {
                            if (args[a].Count == 0)
                                continue;
                            var at = tokens[args[a][0]];
                            var u = LiteralStart(at, text);
                            if ((u == null) || (GetUrlKind(u) != UrlKind.External))
                                continue;
                            var shown = u.Length > 80 ? (u.Substring(0, 80) + "...") : u;
                            ctx.Add(Rule.ExternalResource, at.Start, name + "() loads the internet resource \"" + shown.Trim() + "\" (pages must work offline: serve a copy from srcWeb and include it with a relative url)");
                        }
                        continue;
                    }
                    if (RequestFunctions.Contains(name) && (!member || ((Sig(si - 2)?.Kind == JsTokenKind.Identifier) && GlobalObjects.Contains(Sig(si - 2).Value.GetText(text)))))
                        Report(si + 2, name + "()");
                    else if (RequestConstructors.Contains(name) && Id(si - 1, "new"))
                        Report(si + 2, "new " + name + "()");
                    else if (member && (name == "sendBeacon"))
                        Report(si + 2, "sendBeacon()");
                    else if (member && (name == "open"))
                    {
                        //  XMLHttpRequest.open(method, url): only with a literal http method first (window.open(url) opens a page, like a link)
                        var m = Sig(si + 2);
                        if ((m != null) && (m.Value.Kind == JsTokenKind.String) && HttpMethods.Contains(LiteralStart(m.Value, text) ?? "") && P(si + 3, ","))
                            Report(si + 4, "XMLHttpRequest.open()");
                    }
                    else if (member && (name == "setAttribute"))
                    {
                        var a = Sig(si + 2);
                        if ((a != null) && (a.Value.Kind == JsTokenKind.String) && LoadingProperties.Contains(LiteralStart(a.Value, text) ?? "") && P(si + 3, ","))
                            Report(si + 4, "setAttribute(\"" + LiteralStart(a.Value, text) + "\")");
                    }
                    continue;
                }
                if (member && LoadingProperties.Contains(name) && P(si + 1, "="))
                    Report(si + 2, "." + name + " =");
            }
        }

        /// <summary>
        /// The kind of a resource url.
        /// </summary>
        public enum UrlKind
        {
            /// <summary>A relative url (or a scheme that loads nothing from the network: data:, blob:, javascript: ...).</summary>
            Relative,
            /// <summary>An url starting with a single '/' (root relative).</summary>
            RootRelative,
            /// <summary>An internet url (http:, https:, ftp:, ws:, wss: or protocol relative "//").</summary>
            External,
        }

        static readonly Regex ExternalScheme = new Regex(@"^(?:https?|ftp|wss?):", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Classify a resource url (script src, link href, CSS url, import specifier).
        /// Urls that start with a server template expression (<c>${@Base Url}sw/core.js</c>) are relative.
        /// </summary>
        /// <param name="url">The raw url</param>
        /// <returns>The kind</returns>
        public static UrlKind GetUrlKind(String url)
        {
            if (url == null)
                return UrlKind.Relative;
            var s = url.Trim();
            if (s.StartsWith("${", StringComparison.Ordinal) || s.StartsWith("$(", StringComparison.Ordinal))
                return UrlKind.Relative;
            if (s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("\\\\", StringComparison.Ordinal) || ExternalScheme.IsMatch(s))
                return UrlKind.External;
            if (s.StartsWith('/'))
                return UrlKind.RootRelative;
            return UrlKind.Relative;
        }

        /// <summary>
        /// Report an external or root relative resource url.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="offset">The offset of the url</param>
        /// <param name="url">The url</param>
        /// <param name="where">Where it is used, ex: "&lt;script src&gt;"</param>
        static void CheckResourceUrl(FileContext ctx, int offset, String url, String where)
        {
            switch (GetUrlKind(url))
            {
                case UrlKind.External:
                    ctx.Add(Rule.ExternalResource, offset, where + " loads the internet resource \"" + url.Trim() + "\" (pages must work offline: serve a copy from srcWeb)");
                    break;
                case UrlKind.RootRelative:
                    ctx.Add(Rule.RootRelativeUrl, offset, where + " uses the root relative url \"" + url.Trim() + "\" (breaks below a path prefix, ex: a reverse proxy; use a relative url)");
                    break;
            }
        }

        /// <summary>Tokens that can follow "export" in an export declaration.</summary>
        static readonly HashSet<String> ExportFollowers = new HashSet<String>(StringComparer.Ordinal) { "{", "*", "default", "const", "let", "var", "function", "class", "async" };

        /// <summary>
        /// Report ES module syntax: import / export declarations, dynamic import() and import.meta
        /// (also reports internet module specifiers as external resources).
        /// "import" and "export" are reserved words, so outside member names (<c>x.import</c>), object keys (<c>{ import: 1 }</c>) and
        /// method declarations (<c>import() { }</c>) they are always module syntax.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="sig">The indices of the significant tokens</param>
        static void CheckEsModule(FileContext ctx, List<JsToken> tokens, List<int> sig)
        {
            var text = ctx.Text;
            var sc = sig.Count;
            JsToken? Sig(int si) => (si >= 0) && (si < sc) ? tokens[sig[si]] : null;
            bool P(int si, String p)
            {
                var t = Sig(si);
                return (t != null) && (t.Value.Kind == JsTokenKind.Punctuator) && t.Value.Is(text, p);
            }
            //  True if the "(" at si starts a parameter list followed by a "{" (a method declaration, not a call)
            bool IsMethod(int si)
            {
                int depth = 0;
                for (int j = si; j < sc; ++j)
                {
                    if (P(j, "("))
                        ++depth;
                    else if (P(j, ")") && (--depth == 0))
                        return P(j + 1, "{");
                }
                return false;
            }
            void CheckSpecifier(int si)
            {
                var s = Sig(si);
                if ((s != null) && (s.Value.Kind == JsTokenKind.String) && !s.Value.Unterminated)
                    CheckResourceUrl(ctx, s.Value.Start, JsLexer.DecodeStringLiteral(s.Value.GetText(text)), "a JS import");
            }
            for (int si = 0; si < sc; ++si)
            {
                var t = tokens[sig[si]];
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                bool isImport = t.Is(text, "import");
                if (!isImport && !t.Is(text, "export"))
                    continue;
                if (P(si - 1, ".") || P(si - 1, "?.") || P(si + 1, ":") || (Sig(si + 1) == null))
                    continue;
                if (P(si + 1, "(") && IsMethod(si + 1))
                    continue;
                var name = t.GetText(text);
                if (!isImport)
                {
                    var n = Sig(si + 1).Value;
                    if (!ExportFollowers.Contains(n.GetText(text)))
                        continue;
                    ctx.Add(Rule.EsModule, t.Start, "\"export\" declaration (ES module syntax; classic scripts publish globals, ex: window.MyLib = ... or a Sw.* member)");
                    continue;
                }
                if (P(si + 1, "("))
                {
                    ctx.Add(Rule.EsModule, t.Start, "dynamic \"import()\" (ES modules are not supported; load classic scripts with Sw.Net.IncludeJs)");
                    CheckSpecifier(si + 2);
                    continue;
                }
                if (P(si + 1, "."))
                {
                    ctx.Add(Rule.EsModule, t.Start, "\"import.meta\" (ES module syntax; only classic scripts are supported)");
                    continue;
                }
                ctx.Add(Rule.EsModule, t.Start, "\"import\" declaration (ES modules are not supported; include classic scripts with <script defer src> or Sw.Net.IncludeJs)");
                //  The module specifier: import "x" or ... from "x"
                if (Sig(si + 1).Value.Kind == JsTokenKind.String)
                    CheckSpecifier(si + 1);
                else
                    for (int j = si + 1; (j < sc) && (j < si + 200) && !P(j, ";"); ++j)
                    {
                        var x = Sig(j).Value;
                        if ((x.Kind == JsTokenKind.Identifier) && x.Is(text, "from"))
                        {
                            CheckSpecifier(j + 1);
                            break;
                        }
                    }
            }
        }

        static readonly Regex CssUrl = new Regex(@"@import\s+(?:url\(\s*)?(?<q>['""]?)(?<u>[^'""()\s;]+)\k<q>|\burl\(\s*(?<q2>['""]?)(?<u2>[^'""()\s]+)\k<q2>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Report internet and root relative urls in CSS (<c>@import</c> and <c>url()</c>, comments are ignored).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="start">The start offset of the CSS</param>
        /// <param name="end">The end offset of the CSS</param>
        static void CheckCssUrls(FileContext ctx, int start, int end)
        {
            if (end <= start)
                return;
            //  Blank out comments (keeps the offsets)
            var css = Regex.Replace(ctx.Text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);
            foreach (Match m in CssUrl.Matches(css))
            {
                var g = m.Groups["u"].Success ? m.Groups["u"] : m.Groups["u2"];
                CheckResourceUrl(ctx, start + g.Index, g.Value, m.Value.StartsWith("@", StringComparison.Ordinal) ? "a CSS @import" : "a CSS url()");
            }
        }

        /// <summary>Link relations that load a resource (other relations, ex: "canonical" or "help", are plain links).</summary>
        static readonly HashSet<String> LoadingLinkRels = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "stylesheet", "icon", "shortcut", "apple-touch-icon", "mask-icon", "manifest", "preload", "modulepreload", "prefetch", "prerender", "preconnect", "dns-prefetch",
        };

        /// <summary>
        /// Split call arguments, returns a list of token index lists (significant tokens only) per top level argument.
        /// </summary>
        static List<List<int>> SplitArgs(List<JsToken> tokens, List<int> sig, int openSi, String text)
        {
            var res = new List<List<int>>();
            var cur = new List<int>();
            int depth = 0;
            for (int si = openSi + 1; si < sig.Count; ++si)
            {
                var ti = sig[si];
                var t = tokens[ti];
                if (t.Kind == JsTokenKind.Punctuator)
                {
                    var c = text[t.Start];
                    if ((t.Length == 1) && ((c == '(') || (c == '[') || (c == '{')))
                        ++depth;
                    else if ((t.Length == 1) && ((c == ')') || (c == ']') || (c == '}')))
                    {
                        if (depth == 0)
                        {
                            if (cur.Count > 0)
                                res.Add(cur);
                            return res;
                        }
                        --depth;
                    }
                    else if ((depth == 0) && (t.Length == 1) && (c == ','))
                    {
                        res.Add(cur);
                        cur = new List<int>();
                        continue;
                    }
                }
                else if (t.Kind == JsTokenKind.TemplateHead)
                    ++depth;
                else if (t.Kind == JsTokenKind.TemplateTail)
                    --depth;
                cur.Add(ti);
            }
            if (cur.Count > 0)
                res.Add(cur);
            return res;
        }

        static void CheckTranslatedLiteral(FileContext ctx, JsToken t, String fn)
        {
            if (t.Unterminated)
                return;
            var raw = t.GetText(ctx.Text);
            for (int i = 1; i < raw.Length - 1; ++i)
            {
                if (raw[i] != '\\')
                    continue;
                var n = raw[i + 1];
                if ((n == 'n') || (n == 'r') || JsLexer.IsLineTerminator(n))
                {
                    ctx.Add(Rule.TrNewline, t.Start + i, "the " + fn + " literal contains " + (n == 'n' ? "\\n" : n == 'r' ? "\\r" : "a line continuation") + " (minified to a backtick literal, not translated)");
                    break;
                }
                ++i;
            }
            var v = JsLexer.DecodeStringLiteral(raw);
            if (v.Contains('\'') && v.Contains('"'))
                ctx.Add(Rule.TrMixedQuotes, t.Start, "the " + fn + " literal contains both ' and \" (minified to a backtick literal, not translated)");
        }

        static void CheckTrInText(FileContext ctx, int start, int end, String where)
        {
            if (end <= start)
                return;
            var s = ctx.Text.Substring(start, end - start);
            foreach (Match m in TrPattern.Matches(s))
                ctx.Add(Rule.TrInText, start + m.Index, "\"" + m.Value + "\" in " + where + " (not a translated script call; the server may rewrite it)");
        }

        /// <summary>
        /// True for the brand style sheet app/app.css (relative path, forward slashes; also below a folder, ex: "srcWeb/app/app.css").
        /// </summary>
        /// <param name="rel">The relative path</param>
        /// <returns>True for app/app.css</returns>
        public static bool IsBrandCss(String rel) =>
            String.Equals(rel, "app/app.css", StringComparison.OrdinalIgnoreCase) || rel.EndsWith("/app/app.css", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True for the brand script app/app.js (relative path, forward slashes; also below a folder, ex: "srcWeb/app/app.js").
        /// </summary>
        /// <param name="rel">The relative path</param>
        /// <returns>True for app/app.js</returns>
        public static bool IsBrandJs(String rel) =>
            String.Equals(rel, "app/app.js", StringComparison.OrdinalIgnoreCase) || rel.EndsWith("/app/app.js", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True for the framework's own style sheets (component and theme definitions), which may style bare elements:
        /// sw/, core/, ui/, shell/, common/, brand/ and the legacy shell style sheet app/application.css.
        /// </summary>
        /// <param name="rel">The relative path</param>
        /// <returns>True for a framework style sheet</returns>
        public static bool IsFrameworkCss(String rel) =>
            rel.StartsWith("sw/", StringComparison.Ordinal) || rel.StartsWith("core/", StringComparison.Ordinal) || rel.StartsWith("ui/", StringComparison.Ordinal)
            || rel.StartsWith("shell/", StringComparison.Ordinal) || rel.StartsWith("common/", StringComparison.Ordinal) || rel.StartsWith("brand/", StringComparison.Ordinal)
            || (rel == "app/application.css");

        /// <summary>Elements that Sw components are built from (form controls, the Sw.Table grid, icons / pictures, embedded frames).</summary>
        static readonly HashSet<String> ComponentElements = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "button", "input", "select", "textarea", "table", "thead", "tbody", "tr", "th", "td", "img", "iframe" };

        /// <summary>The form controls of <see cref="ComponentElements"/> (always reported).</summary>
        static readonly HashSet<String> FormElements = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "button", "input", "select", "textarea" };

        /// <summary>Attributes on a subject element that don't scope it to the page (ex: input[type=text] is every text box).</summary>
        static readonly HashSet<String> GenericAttributes = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "type", "disabled", "readonly", "required", "multiple", "hidden", "name", "value", "checked", "placeholder" };

        /// <summary>Body properties that scale every component of the page.</summary>
        static readonly HashSet<String> BodyScaleProperties = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "font", "font-size", "zoom" };

        /// <summary>
        /// Split a css selector list (or a compound sequence) at top level separators (outside brackets, parentheses and strings).
        /// </summary>
        /// <param name="s">The text</param>
        /// <param name="combinators">True to split at combinators (white space, &gt;, +, ~), false to split at commas</param>
        /// <returns>The parts (trimmed, non empty)</returns>
        internal static List<String> SplitSelectorList(String s) => SplitSelector(s, false);

        internal static List<String> SplitCompounds(String s) => SplitSelector(s, true);

        static List<String> SplitSelector(String s, bool combinators)
        {
            var res = new List<String>();
            int depth = 0, start = 0;
            char q = '\0';
            void Flush(int end)
            {
                var p = s.Substring(start, end - start).Trim();
                if (p.Length > 0)
                    res.Add(p);
            }
            for (int i = 0; i < s.Length; ++i)
            {
                var c = s[i];
                if (q != '\0')
                {
                    if (c == '\\')
                        ++i;
                    else if (c == q)
                        q = '\0';
                    continue;
                }
                if ((c == '"') || (c == '\''))
                    q = c;
                else if ((c == '(') || (c == '['))
                    ++depth;
                else if (((c == ')') || (c == ']')) && (depth > 0))
                    --depth;
                else if ((depth == 0) && (combinators ? (Char.IsWhiteSpace(c) || (c == '>') || (c == '+') || (c == '~')) : (c == ',')))
                {
                    Flush(i);
                    start = i + 1;
                }
            }
            Flush(s.Length);
            return res;
        }

        /// <summary>
        /// The element name of a compound selector (ex: "button" for "button:hover"), or null.
        /// </summary>
        static String CompoundElement(String compound)
        {
            int i = 0;
            while ((i < compound.Length) && (Char.IsLetterOrDigit(compound[i]) || (compound[i] == '-') || (compound[i] == '_')))
                ++i;
            return i > 0 ? compound.Substring(0, i) : null;
        }

        /// <summary>
        /// True if an ancestor compound scopes a selector to a page (a class, id or attribute anywhere in it, also inside :is() / :where()).
        /// </summary>
        static bool IsScopingCompound(String compound) => (compound.IndexOf('.') >= 0) || (compound.IndexOf('#') >= 0) || (compound.IndexOf('[') >= 0)
            || IsPageCustomElement(CompoundElement(compound));

        /// <summary>
        /// True for the name of a custom element of the page (a name with a '-', ex: "tiu-display", "Fenrir-Panel"): the Sw components
        /// never live inside one, so it scopes like a class. The framework's own elements ("sw-*", the old "SysWeaver-*") don't.
        /// </summary>
        static bool IsPageCustomElement(String name) => (name != null) && (name.IndexOf('-') > 0)
            && !name.StartsWith("sw-", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("sysweaver-", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True if the subject compound is scoped by itself: a class or id, or an attribute other than the generic ones (type, disabled ...).
        /// </summary>
        static bool IsScopedSubject(String compound)
        {
            if ((compound.IndexOf('.') >= 0) || (compound.IndexOf('#') >= 0))
                return true;
            foreach (Match m in Regex.Matches(compound, @"\[\s*(?<a>[A-Za-z_][\w-]*)", RegexOptions.CultureInvariant))
                if (!GenericAttributes.Contains(m.Groups["a"].Value))
                    return true;
            return false;
        }

        /// <summary>
        /// Get the bare element a selector restyles: the element name of its subject when neither the subject nor an ancestor is page scoped
        /// (ex: "button", "form input:focus", "html body"), else null.
        /// </summary>
        /// <param name="selector">One selector (no commas)</param>
        /// <returns>The lower case element name or null</returns>
        public static String GetBareElement(String selector)
        {
            var parts = SplitSelector(selector, true);
            if (parts.Count == 0)
                return null;
            var subject = parts[parts.Count - 1];
            var el = CompoundElement(subject);
            if ((el == null) && !IsScopedSubject(subject))
            {
                //  ":where(button)", ":is(input, select)": the element inside the pseudo class (no specificity, the same elements)
                var w = Regex.Match(subject, @"^:(?:where|is|matches|-webkit-any)\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (w.Success)
                {
                    int depth = 1, k = w.Length;
                    for (; (k < subject.Length) && (depth > 0); ++k)
                        if (subject[k] == '(')
                            ++depth;
                        else if (subject[k] == ')')
                            --depth;
                    foreach (var inner in SplitSelector(subject.Substring(w.Length, Math.Max(0, k - 1 - w.Length)), false))
                    {
                        var ie = GetBareElement(inner);
                        if (ie != null)
                        {
                            el = ie;
                            break;
                        }
                    }
                }
                //  "[type=text]", "[placeholder]": an attribute that only form controls have, on any element = every such control
                else if (Regex.IsMatch(subject, @"^\[\s*(?:type|placeholder|readonly|required|checked|multiple)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                    el = "input";
            }
            if ((el == null) || IsScopedSubject(subject))
                return null;
            for (int i = 0; i < parts.Count - 1; ++i)
                if (IsScopingCompound(parts[i]))
                    return null;
            return el.ToLowerInvariant();
        }

        /// <summary>
        /// Visit the style rules of a css text (also inside @media / @supports / @layer / @container blocks; @keyframes, @font-face and
        /// other at-rules are skipped). Comments must already be blanked out.
        /// </summary>
        /// <param name="css">The css (comments blanked)</param>
        /// <param name="start">The start offset</param>
        /// <param name="end">The end offset</param>
        /// <param name="rule">Receives the prelude offset, the prelude (selector list) and the declarations (nested blocks removed)</param>
        static void VisitCssRules(String css, int start, int end, Action<int, String, String> rule)
        {
            int i = start;
            while (i < end)
            {
                while ((i < end) && Char.IsWhiteSpace(css[i]))
                    ++i;
                if (i >= end)
                    break;
                //  The prelude, up to '{' or ';' at top level
                int ps = i, depth = 0;
                char q = '\0';
                for (; i < end; ++i)
                {
                    var c = css[i];
                    if (q != '\0')
                    {
                        if (c == '\\')
                            ++i;
                        else if (c == q)
                            q = '\0';
                        continue;
                    }
                    if ((c == '"') || (c == '\''))
                        q = c;
                    else if ((c == '(') || (c == '['))
                        ++depth;
                    else if (((c == ')') || (c == ']')) && (depth > 0))
                        --depth;
                    else if ((depth == 0) && ((c == '{') || (c == ';') || (c == '}')))
                        break;
                }
                if ((i >= end) || (css[i] != '{'))
                {
                    ++i;
                    continue;
                }
                var prelude = css.Substring(ps, i - ps).Trim();
                //  The block, up to the matching '}'
                int bs = i + 1, d = 1;
                q = '\0';
                var decl = new System.Text.StringBuilder();
                for (i = bs; (i < end) && (d > 0); ++i)
                {
                    var c = css[i];
                    if (q != '\0')
                    {
                        if (c == '\\')
                            ++i;
                        else if (c == q)
                            q = '\0';
                        if (d == 1)
                            decl.Append(c);
                        continue;
                    }
                    if ((c == '"') || (c == '\''))
                        q = c;
                    if (c == '{')
                        ++d;
                    else if (c == '}')
                    {
                        if (--d == 0)
                            break;
                        continue;
                    }
                    if (d == 1)
                        decl.Append(c);
                }
                var be = Math.Min(i, end);
                ++i;
                if (prelude.StartsWith("@", StringComparison.Ordinal))
                {
                    var name = Regex.Match(prelude, @"^@[\w-]+").Value.ToLowerInvariant();
                    if ((name == "@media") || (name == "@supports") || (name == "@layer") || (name == "@container") || (name == "@scope") || (name == "@document"))
                        VisitCssRules(css, bs, be, rule);
                    continue;
                }
                rule(ps, prelude, decl.ToString());
            }
        }

        /// <summary>A css declaration: property and value.</summary>
        static readonly Regex CssDeclaration = new Regex(@"(?<p>-?[A-Za-z][\w-]*)\s*:(?<v>[^;]*)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Report page css that restyles bare form elements (element-css), scales the text of a bare body (element-css) or hides the overflow
        /// of a bare body / html (body-overflow). The framework's own style sheets (<see cref="IsFrameworkCss"/>) are not checked.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="start">The start offset of the css</param>
        /// <param name="end">The end offset of the css</param>
        static void CheckElementCss(FileContext ctx, int start, int end, bool swPage = true)
        {
            if ((end <= start) || IsFrameworkCss(ctx.WebRel) || !swPage)
                return;
            //  Blank out comments (keeps the offsets)
            var css = ctx.Text.Substring(0, start) + Regex.Replace(ctx.Text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);
            VisitCssRules(css, start, end, (offset, prelude, decls) =>
            {
                if (Regex.IsMatch(decls, @"^\s*$"))
                    return;
                foreach (var sel in SplitSelector(prelude, false))
                {
                    var el = GetBareElement(sel);
                    if (el == null)
                        continue;
                    //  (the table, picture and frame elements only matter in a page with Sw components: a .css file, or a page that loads the bundles)
                    if (ComponentElements.Contains(el) && (swPage || FormElements.Contains(el)))
                    {
                        ctx.Add(Rule.ElementCss, offset, "\"" + sel + "\" restyles every " + el + " of the page, also inside the Sw components (tables, pagers, dialogs, menus, text boxes, pictures): scope it to your page, ex: \".my-page " + sel + "\" (or mark a deliberate rule with a \"" + AllowElementCss + "\" comment)");
                        continue;
                    }
                    if ((el != "body") && (el != "html"))
                        continue;
                    foreach (Match m in CssDeclaration.Matches(decls))
                    {
                        var p = m.Groups["p"].Value.ToLowerInvariant();
                        var v = m.Groups["v"].Value.Trim();
                        if (((p == "overflow") || (p == "overflow-y")) && Regex.IsMatch(v, @"^(?:hidden|clip)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            ctx.Add(Rule.BodyOverflow, offset, "\"" + sel + " { " + p + ": " + v + " }\" clips everything below the fold, on phones also the pager and the buttons: let the page scroll, or scroll an inner element (or mark a deliberate full window layout with a \"" + AllowElementCss + "\" comment)");
                        else if (BodyScaleProperties.Contains(p) && !v.StartsWith("var(--sw-", StringComparison.Ordinal) && !v.StartsWith("var(--Theme", StringComparison.Ordinal) && (v != "inherit"))
                            ctx.Add(Rule.ElementCss, offset, "\"" + sel + " { " + p + ": " + v + " }\" rescales every Sw component of the page: set it on your own page elements (or mark a deliberate rule with a \"" + AllowElementCss + "\" comment)");
                    }
                }
            });
        }

        /// <summary>A custom property declaration: name and value (up to ';' or '}').</summary>
        static readonly Regex CssCustomProperty = new Regex(@"(?<n>--[A-Za-z0-9_-]+)\s*:(?<v>[^;{}]*)", RegexOptions.CultureInvariant);

        /// <summary>A url() in a css value.</summary>
        static readonly Regex CssUrlValue = new Regex(@"\burl\(\s*(?<q>['""]?)(?<u>[^'""()\s]+)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The rules of the brand style sheet app/app.css (see WebBranding.md): no @import (it is adopted as a constructable style
        /// sheet in every page, which ignores imports), and relative urls in custom properties written as "../app/&lt;file&gt;" (a url in a
        /// variable is resolved against the style sheet that uses the variable, ex: sw/ui.css).
        /// </summary>
        /// <param name="ctx">The file</param>
        static void CheckBrandCss(FileContext ctx)
        {
            var css = Regex.Replace(ctx.Text, @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(css, @"@import\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                ctx.Add(Rule.CssImport, m.Index, "@import in app/app.css is not supported (every page adopts the file without its imports): put the rules in app/app.css itself");
            foreach (Match m in CssCustomProperty.Matches(css))
            {
                var v = m.Groups["v"];
                foreach (Match u in CssUrlValue.Matches(v.Value))
                {
                    var g = u.Groups["u"];
                    var url = g.Value;
                    if ((GetUrlKind(url) != UrlKind.Relative) || url.StartsWith("../", StringComparison.Ordinal) || url.StartsWith("${", StringComparison.Ordinal) || url.StartsWith("#", StringComparison.Ordinal)
                        || Regex.IsMatch(url, @"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant))
                        continue;
                    var file = url.StartsWith("./", StringComparison.Ordinal) ? url.Substring(2) : url;
                    ctx.Add(Rule.BrandUrl, v.Index + g.Index, "the url \"" + url + "\" in " + m.Groups["n"].Value + " is resolved against the style sheet that uses the variable (ex: sw/ui.css), not app/app.css: write it as \"../app/" + file + "\"");
                }
            }
        }

        /// <summary>
        /// Check a .css file.
        /// </summary>
        /// <param name="ctx">The file</param>
        public static void CheckCssFile(FileContext ctx)
        {
            CheckEncoding(ctx);
            CheckTrInText(ctx, 0, ctx.Text.Length, "CSS");
            CheckCssUrls(ctx, 0, ctx.Text.Length);
            CheckElementCss(ctx, 0, ctx.Text.Length);
            if (ctx.Options.Design)
            {
                DesignChecker.CheckCss(ctx, 0, ctx.Text.Length, false);
                UxChecker.CheckCss(ctx, 0, ctx.Text.Length, false);
            }
            if (IsBrandCss(ctx.WebRel))
                CheckBrandCss(ctx);
            if (!ctx.IsTemplated)
                return;
            CheckTemplated(ctx);
            var text = ctx.Text;
            int p = 0;
            while ((p = text.IndexOf("${", p, StringComparison.Ordinal)) >= 0)
            {
                var b = text.LastIndexOfAny(new[] { '{', ';', '}' }, Math.Max(0, p - 1));
                var decl = text.Substring(b + 1, p - b - 1);
                // Remove comments
                decl = Regex.Replace(decl, @"/\*.*?\*/", "", RegexOptions.Singleline).Trim();
                if (!(decl.StartsWith("--", StringComparison.Ordinal) && decl.Contains(':')))
                    ctx.Add(Rule.TemplateCss, p, "template variable outside a --custom-property value (the minifier drops the ';' after it)");
                p += 2;
            }
        }

        /// <summary>
        /// Check the template variables of a templated file.
        /// </summary>
        /// <param name="ctx">The file</param>
        public static void CheckTemplated(FileContext ctx)
        {
            CheckEncoding(ctx);
            var text = ctx.Text;
            int p = 0;
            while ((p = text.IndexOf("${", p, StringComparison.Ordinal)) >= 0)
            {
                var e = text.IndexOf('}', p + 2);
                if (e < 0)
                {
                    ctx.Add(Rule.TemplateVar, p, "\"${\" without a closing '}' in a templated file");
                    break;
                }
                var raw = text.Substring(p + 2, e - p - 2);
                var name = StripTemplatePrefix(raw);
                if (!IsKnownTemplateVar(name, ctx.Options.ExtraTemplateVars))
                {
                    var show = raw.Length > 40 ? (raw.Substring(0, 40) + "...") : raw;
                    ctx.Add(Rule.TemplateVar, p, "\"${" + show + "}\" is not a known template variable (only template variables may use \"${\" in a templated file; use --template-var for config variables)");
                }
                p = e + 1;
            }
        }

        /// <summary>
        /// Check the resources a HTML start tag loads: module scripts (<c>type="module"</c> / <c>"importmap"</c>) and
        /// internet or root relative urls in <c>&lt;script src&gt;</c>, resource loading <c>&lt;link href&gt;</c>, <c>&lt;img src / srcset&gt;</c>,
        /// <c>&lt;iframe&gt;</c>, <c>&lt;frame&gt;</c>, <c>&lt;embed&gt;</c>, <c>&lt;audio&gt;</c>, <c>&lt;video src / poster&gt;</c>, <c>&lt;source src / srcset&gt;</c>, <c>&lt;track&gt;</c> src, <c>&lt;object data&gt;</c> and <c>&lt;input type="image" src&gt;</c>.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="n">The start tag</param>
        static void CheckHtmlResources(FileContext ctx, HtmlNode n)
        {
            HtmlAttribute url = null;
            switch (n.Name)
            {
                case "script":
                    var type = n.GetAttribute("type");
                    var tv = type?.Value?.Trim();
                    if ((tv != null) && (tv.Equals("module", StringComparison.OrdinalIgnoreCase) || tv.Equals("importmap", StringComparison.OrdinalIgnoreCase)))
                        ctx.Add(Rule.EsModule, type.ValueStart, "<script type=\"" + tv + "\"> (ES modules are not supported; use <script defer src=\"...\"> with a classic script)");
                    url = n.GetAttribute("src");
                    break;
                case "link":
                    var rel = n.GetAttribute("rel")?.Value;
                    if ((rel != null) && rel.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Any(x => LoadingLinkRels.Contains(x)))
                        url = n.GetAttribute("href");
                    break;
                case "img":
                case "iframe":
                case "frame":
                case "embed":
                case "audio":
                case "track":
                    url = n.GetAttribute("src");
                    break;
                case "video":
                    url = n.GetAttribute("src");
                    CheckAttributeUrl(ctx, n, "poster");
                    break;
                case "source":
                    url = n.GetAttribute("src");
                    break;
                case "object":
                    url = n.GetAttribute("data");
                    break;
                case "input":
                    if (String.Equals(n.GetAttribute("type")?.Value?.Trim(), "image", StringComparison.OrdinalIgnoreCase))
                        url = n.GetAttribute("src");
                    break;
            }
            if (url?.Value != null)
                CheckResourceUrl(ctx, url.ValueStart, url.Value, "<" + n.Name + " " + url.Name + ">");
            if ((n.Name == "img") || (n.Name == "source"))
            {
                //  srcset: comma separated "url [descriptor]" candidates
                var ss = n.GetAttribute("srcset");
                if (ss?.Value != null)
                {
                    int p = 0;
                    foreach (var part in ss.Value.Split(','))
                    {
                        var c = part.TrimStart();
                        var u = c.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
                        if (u.Length > 0)
                            CheckResourceUrl(ctx, ss.ValueStart + p + (part.Length - c.Length), u, "<" + n.Name + " srcset>");
                        p += part.Length + 1;
                    }
                }
            }
        }

        /// <summary>
        /// Check one more url attribute of a start tag (ex: video poster).
        /// </summary>
        static void CheckAttributeUrl(FileContext ctx, HtmlNode n, String name)
        {
            var a = n.GetAttribute(name);
            if (a?.Value != null)
                CheckResourceUrl(ctx, a.ValueStart, a.Value, "<" + n.Name + " " + a.Name + ">");
        }

        /// <summary>
        /// The rule theme-include: a page that links sw/ui.css or sw/shell.css must link common/theme.css before it (the templated theme
        /// variables and the [data-theme] variants that the Sw tokens follow).
        /// </summary>
        /// <param name="ctx">The page</param>
        /// <param name="nodes">The scanned page</param>
        static void CheckThemeInclude(FileContext ctx, List<HtmlNode> nodes)
        {
            HtmlAttribute sw = null;
            bool theme = false, themeAfter = false;
            foreach (var n in nodes)
            {
                if ((n.Kind != HtmlNodeKind.StartTag) || (n.Name != "link"))
                    continue;
                var href = n.GetAttribute("href");
                if (href?.Value == null)
                    continue;
                if (IsSrc(href.Value, "common/theme.css"))
                {
                    if (sw == null)
                        theme = true;
                    else
                        themeAfter = true;
                }
                else if ((sw == null) && (IsSrc(href.Value, "sw/ui.css") || IsSrc(href.Value, "sw/shell.css")))
                    sw = href;
            }
            if ((sw == null) || theme)
                return;
            var url = Regex.Replace(sw.Value.Trim(), @"sw/(?:ui|shell)\.css(?:[?#].*)?$", "common/theme.css");
            ctx.Add(Rule.ThemeInclude, sw.ValueStart, themeAfter
                ? "common/theme.css is linked after \"" + sw.Value + "\": link it before (the theme variables first, then the components), else the components keep their light defaults in some themes"
                : "the page links \"" + sw.Value + "\" without common/theme.css: it stays light in the Dark, Pro, Application and Hacker themes while the shell around it switches; add <link rel=\"stylesheet\" href=\"" + url + "\"> before it (SwWeb migrate does it)");
        }

        /// <summary>
        /// Check a .html file, returns the script src values of a new-format page (empty for other pages).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <returns>The script sources to check for legacy globals (strict mode, new-format pages)</returns>
        public static List<String> CheckHtmlFile(FileContext ctx)
        {
            CheckEncoding(ctx);
            var text = ctx.Text;
            var nodes = HtmlScanner.Scan(text);
            var scripts = HtmlScanner.GetScripts(nodes);
            bool newFormat = scripts.Any(s => s.Src != null && IsSrc(s.Src, "sw/core.js"));
            bool strictNew = newFormat && ctx.Options.Strict;
            ctx.IsCompatPage = newFormat && scripts.Any(s => s.Src != null && IsSrc(s.Src, "sw/compat.js"));
            //  A page with Sw components (it loads the bundles or the legacy bundles)
            //  A UI page: it loads the bundles, or it is a fragment (no html / body) that a page includes; full documents without the
            //  bundles (exports, redirects) and mail templates (*Mail.html) are not, so the page css rules (element-css, design) skip them
            bool loadsSw = scripts.Any(s => (s.Src != null) && (IsSrc(s.Src, "sw/core.js") || IsSrc(s.Src, "common/common.js") || IsSrc(s.Src, "sw/ui.js")));
            bool document = nodes.Any(x => (x.Kind == HtmlNodeKind.StartTag) && ((x.Name == "html") || (x.Name == "body")));
            bool mail = ctx.Rel.EndsWith("Mail.html", StringComparison.OrdinalIgnoreCase) || ctx.Rel.EndsWith("Mail.htm", StringComparison.OrdinalIgnoreCase);
            bool swPage = loadsSw || (!document && !mail);
            ctx.NotUiPage = !swPage;
            var byBody = scripts.Where(x => x.BodyEnd > x.BodyStart).ToDictionary(x => x.BodyStart);
            foreach (var n in nodes)
            {
                if (n.Unterminated)
                    ctx.Add(Rule.Syntax, n.Start, "unterminated " + (n.Kind == HtmlNodeKind.RawText ? ("<" + n.Name + "> element") : n.Kind.ToString().ToLowerInvariant()));
                switch (n.Kind)
                {
                    case HtmlNodeKind.Text:
                        CheckTrInText(ctx, n.Start, n.End, "HTML text");
                        break;
                    case HtmlNodeKind.Comment:
                    case HtmlNodeKind.Declaration:
                        CheckTrInText(ctx, n.Start, n.End, "a HTML comment");
                        break;
                    case HtmlNodeKind.StartTag:
                        foreach (var a in n.Attributes)
                        {
                            if (a.Value == null)
                                continue;
                            CheckTrInText(ctx, a.ValueStart, a.ValueStart + a.Value.Length, "the attribute \"" + a.Name + "\" (attributes are not translated)");
                            if (strictNew && a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && (a.Value.Length > 0))
                                CheckLegacyGlobals(ctx, JsLexer.Tokenize(text, a.ValueStart, a.ValueStart + a.Value.Length), null, ctx.IsCompatPage);
                        }
                        CheckHtmlResources(ctx, n);
                        if (strictNew)
                        {
                            var src = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                            if ((src?.Value != null) && (IsSrc(src.Value, "common/common.js") || IsSrc(src.Value, "app/application.js") || IsSrc(src.Value, "app/application.css")))
                                ctx.Add(Rule.LegacyInclude, src.ValueStart, "a new-format page (includes sw/core.js) must not include the legacy bundle \"" + src.Value + "\"");
                        }
                        break;
                    case HtmlNodeKind.RawText:
                        if ((n.Name == "script") && byBody.TryGetValue(n.Start, out var sc) && sc.IsJavaScript)
                        {
                            var tokens = JsLexer.Tokenize(text, n.Start, n.End);
                            CheckJsTokens(ctx, tokens);
                            TextChecker.CheckJs(ctx, tokens);
                            if (strictNew)
                                CheckLegacyGlobals(ctx, tokens, null, ctx.IsCompatPage);
                            if (ctx.Options.Design)
                            {
                                DesignChecker.CheckJs(ctx, tokens);
                                UxChecker.CheckJs(ctx, tokens);
                            }
                        }
                        else
                        {
                            CheckTrInText(ctx, n.Start, n.End, "a <" + n.Name + "> element");
                            if (n.Name == "style")
                            {
                                CheckCssUrls(ctx, n.Start, n.End);
                                CheckElementCss(ctx, n.Start, n.End, swPage);
                                if (ctx.Options.Design && swPage)
                                {
                                    DesignChecker.CheckCss(ctx, n.Start, n.End, false);
                                    UxChecker.CheckCss(ctx, n.Start, n.End, false);
                                }
                            }
                        }
                        break;
                }
            }
            if (ctx.Options.Design)
            {
                DesignChecker.CheckHtml(ctx, nodes, swPage);
                UxChecker.CheckHtml(ctx, nodes, swPage);
            }
            CheckThemeInclude(ctx, nodes);
            if (ctx.IsTemplated)
                CheckTemplated(ctx);
            var res = new List<String>();
            if (strictNew)
                foreach (var s in scripts)
                    if ((s.Src != null) && !IsSrc(s.Src, "sw/core.js"))
                        res.Add(s.Src);
            return res;
        }

        /// <summary>
        /// Test if an include url (script src / link href) points to a known file, ex: "sw/core.js".
        /// Leading server template expressions (<c>${@Base Url}</c>, <c>$(BASE)</c>, as written by SwWeb migrate in srcData templates) are
        /// skipped, so "${@Base Url}sw/core.js" is the same include as "../sw/core.js".
        /// </summary>
        /// <param name="src">The raw attribute value</param>
        /// <param name="name">The file (path from the site root)</param>
        /// <returns>True if the url ends with that file</returns>
        public static bool IsSrc(String src, String name)
        {
            var s = StripUrlTemplatePrefix(src.Trim());
            var q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
                s = s.Substring(0, q);
            return (s == name) || s.EndsWith("/" + name, StringComparison.Ordinal);
        }

        /// <summary>
        /// Remove leading server template expressions (<c>${...}</c> or <c>$(...)</c>) from an url.
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>The url without the template prefix (unchanged if it has none or an expression is not closed)</returns>
        public static String StripUrlTemplatePrefix(String url)
        {
            var s = url;
            while ((s.Length > 2) && (s[0] == '$') && ((s[1] == '{') || (s[1] == '(')))
            {
                var e = s.IndexOf(s[1] == '{' ? '}' : ')', 2);
                if (e < 0)
                    break;
                s = s.Substring(e + 1);
            }
            return s;
        }

        static readonly HashSet<String> DeclKeywords = new HashSet<String>(StringComparer.Ordinal) { "function", "class", "var", "let", "const" };

        /// <summary>The global objects through which old pages read the window state (window.Mobile).</summary>
        static readonly HashSet<String> WindowObjects = new HashSet<String>(StringComparer.Ordinal) { "window", "self", "globalThis" };

        /// <summary>
        /// The old window state globals (PORTING.md A8) that are also reported as <c>window.X</c> (migrate never rewrites <c>window.X</c>).
        /// </summary>
        public static readonly HashSet<String> WindowStateGlobals = new HashSet<String>(StringComparer.Ordinal)
        {
            "IsTop", "Mobile", "Narrow", "Portrait", "SelfPortrait", "SelfNarrow", "SwWidth", "SwHeight", "IsDarkMode", "UseTheme", "DidInteract",
        };

        /// <summary>
        /// Report legacy globals used in JavaScript tokens (identifiers that aren't members, object keys or declared in the same code).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        /// <param name="usedBy">An optional description of the page that uses the script</param>
        /// <param name="compat">True if the code belongs to pages that load sw/compat.js (reported as legacy-compat warnings)</param>
        public static void CheckLegacyGlobals(FileContext ctx, List<JsToken> tokens, String usedBy, bool compat = false)
        {
            var legacy = ctx.Options.LegacyGlobals;
            if (legacy.Count == 0)
                return;
            var text = ctx.Text;
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            var declared = new HashSet<String>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < sig.Count; ++i)
                if ((sig[i].Kind == JsTokenKind.Identifier) && DeclKeywords.Contains(sig[i].GetText(text)) && (sig[i + 1].Kind == JsTokenKind.Identifier))
                    declared.Add(sig[i + 1].GetText(text));
            bool Punct(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            //  Destructuring declarations: const { Button, Dialog: D } = Sw / const [a, b] = x (the bound names, not the keys)
            for (int i = 0; i + 1 < sig.Count; ++i)
                if ((sig[i].Kind == JsTokenKind.Identifier) && DeclKeywords.Contains(sig[i].GetText(text)) && (Punct(i + 1, "{") || Punct(i + 1, "[")))
                {
                    int close = UxChecker.MatchBracket(sig, text, i + 1);
                    for (int k = i + 2; (k < close) && (close > 0); ++k)
                        if ((sig[k].Kind == JsTokenKind.Identifier) && !Punct(k + 1, ":") && !Punct(k - 1, "="))
                            declared.Add(sig[k].GetText(text));
                }
            var classBodies = FindClassBodyTokens(sig, text);
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                var id = t.GetText(text);
                if ((!legacy.Contains(id) && !WindowStateGlobals.Contains(id)) || declared.Contains(id))
                    continue;
                //  Member access: obj.x, this.x, obj?.x (but window.Mobile reads the old window state, A8 of PORTING.md)
                if (Punct(i - 1, ".") || Punct(i - 1, "?."))
                {
                    bool viaWindow = (i >= 2) && (sig[i - 2].Kind == JsTokenKind.Identifier) && WindowObjects.Contains(sig[i - 2].GetText(text)) && !Punct(i - 3, ".")
                        && WindowStateGlobals.Contains(id) && !Punct(i + 1, "=");
                    if (!viaWindow)
                        continue;
                    var wn = sig[i - 2].GetText(text) + "." + id;
                    if (compat)
                        ctx.Add(Rule.LegacyCompat, t.Start, "legacy window state \"" + wn + "\" used" + (usedBy == null ? "" : (" by " + usedBy)) + " (provided by sw/compat.js; use the Sw.* property, PORTING.md A8)");
                    else
                        ctx.Add(Rule.LegacyGlobal, t.Start, "legacy window state \"" + wn + "\" used" + (usedBy == null ? "" : (" by " + usedBy)) + " (undefined without sw/compat.js: use the Sw.* property, PORTING.md A8)");
                    continue;
                }
                if (!legacy.Contains(id))
                    continue;
                //  Object key: { x: 1 }, { a, x: 1 }
                if (Punct(i + 1, ":") && (Punct(i - 1, "{") || Punct(i - 1, ",")))
                    continue;
                //  Class / object member declarations: methods, accessors, class fields
                if (IsMemberDeclaration(sig, text, i, classBodies, Punct))
                    continue;
                if (compat)
                    ctx.Add(Rule.LegacyCompat, t.Start, "legacy global \"" + id + "\" used" + (usedBy == null ? "" : (" by " + usedBy)) + " (provided by sw/compat.js; use the Sw.* API, then remove sw/compat.js / sw/compat.css)");
                else
                    ctx.Add(Rule.LegacyGlobal, t.Start, "legacy global \"" + id + "\" used" + (usedBy == null ? "" : (" by " + usedBy)) + " (use the Sw.* API)");
            }
        }

        /// <summary>
        /// Find the significant tokens that are directly inside a class body (not nested in a method body, static block, brackets or parentheses).
        /// A class body is the first "{" at the same bracket depth after a "class" keyword (declarations and expressions, also with "extends").
        /// </summary>
        /// <param name="sig">The significant tokens</param>
        /// <param name="text">The source text</param>
        /// <returns>The indices of the tokens directly at class body level</returns>
        static HashSet<int> FindClassBodyTokens(List<JsToken> sig, String text)
        {
            var res = new HashSet<int>();
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            //  Stack of open brackets: true = a class body "{"
            var stack = new Stack<bool>();
            //  Bracket depths at which "class" keywords wait for their body
            var pending = new Stack<int>();
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                if ((stack.Count > 0) && stack.Peek() && !P(i, "}"))
                    res.Add(i);
                if (t.Kind == JsTokenKind.Identifier)
                {
                    if (t.Is(text, "class") && !P(i - 1, ".") && !P(i - 1, "?.") && !P(i + 1, ":"))
                        pending.Push(stack.Count);
                    continue;
                }
                if (t.Kind != JsTokenKind.Punctuator)
                    continue;
                if (P(i, "{") || P(i, "(") || P(i, "["))
                {
                    bool isClass = (pending.Count > 0) && P(i, "{") && (pending.Peek() == stack.Count);
                    if (isClass)
                        pending.Pop();
                    stack.Push(isClass);
                    continue;
                }
                if (P(i, "}") || P(i, ")") || P(i, "]"))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    //  A class keyword waiting at a depth that was just closed was not a class (malformed / unsupported): forget it
                    while ((pending.Count > 0) && (pending.Peek() > stack.Count))
                        pending.Pop();
                }
            }
            return res;
        }

        /// <summary>Keywords that may precede a class / object member name</summary>
        static readonly HashSet<String> MemberPrefixes = new HashSet<String>(StringComparer.Ordinal) { "static", "async", "get", "set" };

        /// <summary>
        /// Test if an identifier is the name of a member declaration (not a reference to a global): a class or object literal method
        /// ("name(...) {", also after static / async / get / set / *), or a class field ("name = ...", "name;", "static name" directly in a class body).
        /// </summary>
        /// <param name="sig">The significant tokens</param>
        /// <param name="text">The source text</param>
        /// <param name="i">The identifier token index</param>
        /// <param name="classBodies">Indices of the tokens directly inside a class body</param>
        /// <param name="punct">Punctuator test</param>
        /// <returns>True if the identifier declares a member</returns>
        static bool IsMemberDeclaration(List<JsToken> sig, String text, int i, HashSet<int> classBodies, Func<int, String, bool> punct)
        {
            //  Member prefix keywords (static x, async x(), get x(), set x(v), *x(), static async *x())
            int p = i - 1;
            while ((p >= 0) && (((sig[p].Kind == JsTokenKind.Identifier) && MemberPrefixes.Contains(sig[p].GetText(text))) || punct(p, "*")))
            {
                --p;
            }
            bool atMemberStart = (p < 0) || punct(p, "{") || punct(p, "}") || punct(p, ";") || punct(p, ",");
            if (!atMemberStart)
                return false;
            //  Class field / method directly in a class body
            if (classBodies.Contains(i))
                return true;
            //  Method shorthand in an object literal: name(...) { ... }  (a call is never directly followed by "{")
            if (!punct(i + 1, "("))
                return false;
            int depth = 0;
            for (int j = i + 1; j < sig.Count; ++j)
            {
                if (punct(j, "("))
                    ++depth;
                else if (punct(j, ")") && (--depth == 0))
                    return punct(j + 1, "{");
            }
            return false;
        }
    }
}
