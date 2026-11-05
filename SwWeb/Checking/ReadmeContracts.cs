using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// Contract tests generated from the API rows of the framework READMEs (core / ui / shell / explore README.md): every documented
    /// name, its documented options and its documented return type. Checked statically here (the name is defined in the framework's
    /// sources, every documented option is read by them, a JSDoc @returns of the definition agrees with the README) and written as a
    /// manifest (sw/readme_contracts.js) that a contract page runs in the browser (the name exists, its registered option schema has
    /// every documented option, the documented return type of the pure functions it can call safely).
    /// </summary>
    public static class ReadmeContracts
    {
        /// <summary>One documented api.</summary>
        public sealed class Contract
        {
            /// <summary>The full name ("Sw.Net.FailText"), or the bare name of an instance member / legacy global.</summary>
            public String Name;
            /// <summary>"constructor", "function" (documented with parentheses) or "member".</summary>
            public String Kind;
            /// <summary>The README and the 1-based line ("core/README.md:253").</summary>
            public String Where;
            /// <summary>The documented parameter names.</summary>
            public readonly List<String> Params = new List<String>();
            /// <summary>The documented option names (an options object in the signature, or "Options: `A`, `B`" in the row).</summary>
            public readonly List<String> Options = new List<String>();
            /// <summary>The documented return type ("string", "boolean", "Promise", ...), null if none.</summary>
            public String Returns;
            /// <summary>True for a member of an instance or a legacy global (not reachable from Sw in a page: checked statically only).</summary>
            public bool Instance;

            /// <summary>The text.</summary>
            public override String ToString() => Where + " " + Name + (Kind == "member" ? "" : "(" + String.Join(", ", Params) + ")") + (Options.Count > 0 ? " {" + String.Join(", ", Options) + "}" : "") + (Returns != null ? " -> " + Returns : "");
        }

        /// <summary>The READMEs with api rows, relative to the web root (srcWeb of SysWeaver.HttpServer; explore is in its own module).</summary>
        public static readonly String[] Readmes = { "core/README.md", "ui/README.md", "shell/README.md", "explore/README.md" };

        static readonly Regex Heading = new Regex(@"^#{2,4}\s+(.*)$", RegexOptions.CultureInvariant);
        static readonly Regex CodeSpan = new Regex(@"`([^`]+)`", RegexOptions.CultureInvariant);
        static readonly Regex Item = new Regex(@"^(?<new>new\s+)?(?<name>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*(?:\((?<args>.*)\))?\s*(?:→\s*(?<ret>.+))?$", RegexOptions.CultureInvariant);
        static readonly Regex ReturnAfter = new Regex(@"^\s*→\s*\**`?(?<t>[A-Za-z][\w.<>\[\]|]*)", RegexOptions.CultureInvariant);
        static readonly HashSet<String> NotApi = new HashSet<String>(StringComparer.Ordinal)
        {
            "true", "false", "null", "undefined", "this", "window", "document", "Error", "Promise", "Object", "Array", "String", "Number", "Boolean", "Date", "Map", "Set",
            "fetch", "localStorage", "sessionStorage", "location", "history", "navigator", "console", "JSON", "Math", "Intl", "AbortController", "AbortSignal", "Request", "Response",
        };

        /// <summary>Parse a README.</summary>
        /// <param name="text">The README text</param>
        /// <param name="rel">Its path relative to the web root ("core/README.md")</param>
        /// <returns>The documented apis (one per name; options merged)</returns>
        public static List<Contract> Parse(String text, String rel)
        {
            var byName = new Dictionary<String, Contract>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            String ns = "";
            bool inCode = false, api = false, apiPart = false, listTable = true;
            for (int li = 0; li < lines.Length; ++li)
            {
                var line = lines[li];
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inCode = !inCode;
                    continue;
                }
                if (inCode)
                    continue;
                var h = Heading.Match(line);
                if (h.Success)
                {
                    var ht = h.Groups[1].Value;
                    int level = line.TakeWhile(ch => ch == '#').Count();
                    ns = Regex.Match(ht, @"\bSw(?:\.[A-Z][\w]*)*").Value;
                    //  (the reference parts of a README: an api / javascript reference, components, helpers, a namespace heading;
                    //  a sub heading of such a part is one too)
                    bool match = (ns.Length > 0) || Regex.IsMatch(ht, @"(?i)api|reference|helpers|components|\.js\b|legacy globals|translation|common methods|value cells|page anatomy|forms|wizards|list / detail|dialog|toasts|popup|menu|overlay|motion");
                    if (level <= 2)
                        apiPart = match;
                    api = match || apiPart;
                    if (ns == "Sw")
                        ns = "";
                    continue;
                }
                if (!api)
                    continue;
                if (line.StartsWith("|", StringComparison.Ordinal))
                {
                    var cells = SplitRow(line);
                    if ((cells.Count < 2) || Regex.IsMatch(cells[0], @"^\s*-+\s*$"))
                        continue;
                    //  (a table header: tables of states, files, tokens, keys ... list no apis)
                    if (Regex.IsMatch(lines[Math.Min(lines.Length - 1, li + 1)], @"^\|\s*-{3}"))
                    {
                        listTable = !Regex.IsMatch(cells[0], @"(?i)^\s*(?:state|inconsistency|file|token|key|kind|class|property|event|theme|variable|storage|css|level|status|setting|rule|value|density|layout|case|what|attribute|when|step)");
                        continue;
                    }
                    if (!listTable)
                        continue;
                    var first = cells[0];
                    var desc = String.Join(" | ", cells.Skip(1));
                    //  (the api spans of the first cell: a list item at the top level, not the return type after "→" nor a name
                    //  inside an explanation in parentheses)
                    var spans = CodeSpan.Matches(first).Cast<Match>().Where(x =>
                    {
                        var before = first.Substring(0, x.Index);
                        if (Regex.IsMatch(before, @"→\s*\**\s*$"))
                            return false;
                        var plain = CodeSpan.Replace(before, "");
                        if (plain.Count(ch => ch == '(') > plain.Count(ch => ch == ')'))
                            return false;
                        return Regex.IsMatch(plain, @"(?:^|[,;/+]|\band|\bor|\balso)\s*\**\s*$");
                    }).ToList();
                    //  (the row's own namespace: `Sw.Frame.PostParent(...)`, `PostTop` → Sw.Frame.PostTop)
                    var rowNs = ns;
                    foreach (Match cs in spans)
                    {
                        var after = first.Substring(cs.Index + cs.Length);
                        var nm = Item.Match(cs.Groups[1].Value.Trim()).Groups["name"].Value;
                        var c = Add(byName, cs.Groups[1].Value.Trim(), rowNs, rel, li + 1, ReturnAfter.Match(after) is Match ra && ra.Success ? ra.Groups["t"].Value : null);
                        if ((cs == spans[0]) && nm.StartsWith("Sw.", StringComparison.Ordinal) && (nm.Split('.').Length > 2))
                            rowNs = nm.Substring(0, nm.LastIndexOf('.'));
                        if ((c != null) && (spans.Count == 1))
                        {
                            //  "Options: `Reload`, `Post` (...), ..." in the description of a single api row (not the names inside the explanations)
                            var om = Regex.Match(desc, @"\bOptions:\s*(?<o>.*?)(?:\.\s+(?:Returns|A |An |The |It |Use |In |See )|$)");
                            var olist = om.Success ? om.Groups["o"].Value : "";
                            for (int guard = 0; guard < 5 && Regex.IsMatch(olist, @"\([^()]*\)"); ++guard)
                                olist = Regex.Replace(olist, @"\([^()]*\)", "");
                            foreach (Match oc in CodeSpan.Matches(olist))
                                    if (Regex.IsMatch(oc.Groups[1].Value.Trim(), @"^[A-Z][A-Za-z0-9]*$") && !c.Options.Contains(oc.Groups[1].Value.Trim()))
                                        c.Options.Add(oc.Groups[1].Value.Trim());
                            if ((c.Returns == null) && (ReturnAfter.Match(" " + desc.TrimStart()) is Match rd) && rd.Success)
                                c.Returns = Norm(rd.Groups["t"].Value);
                        }
                    }
                    //  (one name documented twice in a row: overloads with different results, ex: Sw.Focus.Keep(container) / (container, work))
                    foreach (var dup in spans.Select(x => Item.Match(x.Groups[1].Value.Trim()).Groups["name"].Value).GroupBy(x => x).Where(g => g.Count() > 1))
                    {
                        var full = dup.Key.StartsWith("Sw.", StringComparison.Ordinal) || (ns.Length == 0) ? dup.Key : ns + "." + dup.Key;
                        if (byName.TryGetValue(full, out var dc))
                            dc.Returns = null;
                    }
                    continue;
                }
                //  A signature line of a component: `new Sw.TextBox({ ... })`, `Sw.Badge({ ... })` → span
                foreach (Match cs in CodeSpan.Matches(line))
                {
                    var s = cs.Groups[1].Value.Trim();
                    if (!Regex.IsMatch(s, @"^(?:new\s+)?Sw\.[A-Z][\w.]*\s*\("))
                        continue;
                    var after = line.Substring(cs.Index + cs.Length);
                    Add(byName, s, "", rel, li + 1, ReturnAfter.Match(after) is Match ra && ra.Success ? ra.Groups["t"].Value : null);
                }
            }
            return byName.Values.ToList();
        }

        static List<String> SplitRow(String line)
        {
            //  Split a table row at | outside code spans
            var cells = new List<String>();
            var sb = new StringBuilder();
            bool code = false;
            for (int i = 1; i < line.Length; ++i)
            {
                var ch = line[i];
                if (ch == '`')
                    code = !code;
                if ((ch == '|') && !code && ((i == 0) || (line[i - 1] != '\\')))
                {
                    cells.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.ToString().Trim().Length > 0)
                cells.Add(sb.ToString());
            return cells;
        }

        static String Norm(String t)
        {
            t = t.Trim('*', '`', ' ', '.', ',');
            var l = t.ToLowerInvariant();
            if (l.StartsWith("promise", StringComparison.Ordinal))
                return "Promise";
            if (l == "bool")
                return "boolean";
            if ((l == "span") || (l == "div") || (l == "element") || (l == "node") || (l == "svg") || l.StartsWith("html", StringComparison.Ordinal))
                return "Element";
            if (l.EndsWith("[]", StringComparison.Ordinal) || l.StartsWith("array", StringComparison.Ordinal))
                return "array";
            if ((l == "string") || (l == "boolean") || (l == "number") || (l == "function") || (l == "object"))
                return l;
            return t;
        }

        static Contract Add(Dictionary<String, Contract> byName, String code, String ns, String rel, int line, String retAfter)
        {
            var m = Item.Match(code);
            if (!m.Success)
                return null;
            var name = m.Groups["name"].Value;
            var first = name.Split('.')[0];
            if (NotApi.Contains(first) || name.StartsWith("--", StringComparison.Ordinal) || name.Contains("..") || (name.Length < 2))
                return null;
            //  Only api-like names: a Sw path, a capitalized member, or a call
            bool call = m.Groups["args"].Success || m.Groups["new"].Success;
            if (!name.StartsWith("Sw.", StringComparison.Ordinal) && (name != "Sw") && !call && !Char.IsUpper(name[0]))
                return null;
            bool instance = false;
            if (!name.StartsWith("Sw.", StringComparison.Ordinal) && (name != "Sw"))
            {
                if (ns.Length > 0)
                    name = ns + "." + name;
                else
                    instance = true;
            }
            if (!byName.TryGetValue(name, out var c))
            {
                byName[name] = c = new Contract
                {
                    Name = name,
                    Kind = m.Groups["new"].Success ? "constructor" : (m.Groups["args"].Success ? "function" : "member"),
                    Where = rel + ":" + line,
                    Instance = instance,
                };
            }
            else if ((c.Kind == "member") && m.Groups["args"].Success)
                c.Kind = m.Groups["new"].Success ? "constructor" : "function";
            if (m.Groups["args"].Success)
            {
                var args = m.Groups["args"].Value;
                if (c.Params.Count == 0)
                    foreach (var p in TopLevel(args))
                    {
                        var pn = Regex.Match(p.Trim(), @"^(?:\.\.\.)?([A-Za-z_$][\w$]*)").Groups[1].Value;
                        if (pn.Length > 0)
                            c.Params.Add(pn);
                        else if (p.Trim().StartsWith("{", StringComparison.Ordinal))
                            c.Params.Add("o");
                    }
                //  { A, B: x, C(...) } of a parameter (also "ms or { Ms, ... }"; not the destructured parameters of a callback: (ev, { Signal }) => ...)
                foreach (var arg in TopLevel(args))
                {
                    int open = -1, pd = 0;
                    for (int q = 0; q < arg.Length; ++q)
                        if (arg[q] == '(')
                            ++pd;
                        else if (arg[q] == ')')
                            --pd;
                        else if ((arg[q] == '{') && (pd == 0))
                        {
                            open = q;
                            break;
                        }
                    if ((open < 0) || Regex.IsMatch(arg, @"=>"))
                        continue;
                    int close = -1, bd = 0;
                    for (int q = open; q < arg.Length; ++q)
                        if (arg[q] == '{')
                            ++bd;
                        else if ((arg[q] == '}') && (--bd == 0))
                        {
                            close = q;
                            break;
                        }
                    if (close < 0)
                        continue;
                    foreach (var part in TopLevel(arg.Substring(open + 1, close - open - 1)))
                    {
                        var on = Regex.Match(part.Trim(), @"^([A-Z][A-Za-z0-9]*)\b").Groups[1].Value;
                        if ((on.Length > 0) && !c.Options.Contains(on))
                            c.Options.Add(on);
                    }
                }
            }
            var ret = m.Groups["ret"].Success ? m.Groups["ret"].Value : retAfter;
            if ((ret != null) && (c.Returns == null))
                c.Returns = Norm(Regex.Match(ret.Trim('*', ' ', '`'), @"^[\w.<>\[\]|]+").Value);
            return c;
        }

        static IEnumerable<String> TopLevel(String s)
        {
            int d = 0, from = 0;
            for (int i = 0; i < s.Length; ++i)
            {
                var ch = s[i];
                if ((ch == '(') || (ch == '{') || (ch == '['))
                    ++d;
                else if ((ch == ')') || (ch == '}') || (ch == ']'))
                    --d;
                else if ((ch == ',') && (d == 0))
                {
                    yield return s.Substring(from, i - from);
                    from = i + 1;
                }
            }
            if (from < s.Length)
                yield return s.Substring(from);
        }

        // ------------------------------------------------------------------ static checks

        /// <summary>The framework sources of the documented folders, as significant tokens (comments removed).</summary>
        public sealed class Sources
        {
            /// <summary>The code (significant tokens joined by spaces).</summary>
            public readonly StringBuilder Code = new StringBuilder();
            /// <summary>Every identifier and string literal value.</summary>
            public readonly HashSet<String> Words = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>The raw texts (for JSDoc).</summary>
            public readonly List<String> Raw = new List<String>();

            /// <summary>Add a script.</summary>
            public void Add(String text)
            {
                Raw.Add(text);
                foreach (var t in JsLexer.Tokenize(text).Where(x => x.IsSignificant))
                {
                    var s = t.GetText(text);
                    Code.Append(s).Append(' ');
                    if (t.Kind == JsTokenKind.Identifier)
                        Words.Add(s);
                    else if (t.Kind == JsTokenKind.String)
                    {
                        var v = JsLexer.DecodeStringLiteral(s);
                        Words.Add(v);
                        foreach (Match w in Regex.Matches(v, @"[A-Za-z_$][\w$]*"))
                            Words.Add(w.Value);
                    }
                }
                Code.Append('\n');
            }
        }

        /// <summary>Load the scripts of the documented folders of the web roots (core, ui, shell, explore; not their sub folders, not bundles).</summary>
        public static Sources LoadSources(IEnumerable<String> webRoots)
        {
            var s = new Sources();
            foreach (var wr in webRoots)
                foreach (var f in new[] { "core", "ui", "shell", "explore", "common" })
                {
                    var dir = Path.Combine(wr, f);
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (var js in Directory.EnumerateFiles(dir, "*.js", SearchOption.TopDirectoryOnly))
                        s.Add(File.ReadAllText(js));
                }
            return s;
        }

        /// <summary>
        /// The static problems of the contracts: a documented name that no source defines, a documented option that no source reads, a
        /// JSDoc @returns that disagrees with the documented type.
        /// </summary>
        public static List<String> CheckStatic(IEnumerable<Contract> contracts, Sources src)
        {
            var problems = new List<String>();
            var code = src.Code.ToString();
            var defCache = new Dictionary<String, bool>(StringComparer.Ordinal);
            bool Defined(String last)
            {
                if (defCache.TryGetValue(last, out var b))
                    return b;
                var e = Regex.Escape(last);
                b = Regex.IsMatch(code, @"\bfunction\s*\*?\s*" + e + @"\b|(?<![\w$])" + e + @"\s*[:=](?![=>])|\bclass\s+" + e + @"\b|[{,]\s*" + e + @"\s*[,}]|(?<![\w$.])" + e + @"\s*\([^()]*(?:\([^()]*\)[^()]*)*\)\s*\{|\.\s*" + e + @"\s*=(?!=)|[""']" + e + @"[""']\s*[:\]]|\bget\s+" + e + @"\b");
                defCache[last] = b;
                return b;
            }
            foreach (var c in contracts)
            {
                var last = c.Name.Split('.').Last();
                if (!Defined(last))
                    problems.Add(c.Where + ": " + c.Name + " is documented, but no framework source defines \"" + last + "\"");
                foreach (var o in c.Options)
                    if (!src.Words.Contains(o))
                        problems.Add(c.Where + ": " + c.Name + " documents the option \"" + o + "\", but no framework source reads it");
                if ((c.Returns != null) && (JsDocReturns(src, last) is String jd))
                {
                    var r = c.Returns;
                    //  (the top level of the type: an object type {{ Text: string }} is no string)
                    var top = jd;
                    for (int g = 0; (g < 5) && Regex.IsMatch(top, @"\{[^{}]*\}"); ++g)
                        top = Regex.Replace(top, @"\{[^{}]*\}", "{}");
                    bool ok = (r == "Promise") ? Regex.IsMatch(top, @"Promise|thenable|\*") : (r == "Element") ? Regex.IsMatch(top, @"Element|Node|\*|object|\{\}") : (r == "array") ? Regex.IsMatch(top, @"Array|\[\]|\*") : Regex.IsMatch(top, @"(?i)\b" + Regex.Escape(r) + @"\b|\*");
                    if (!ok && Regex.IsMatch(r, "^(?:string|boolean|number|Promise|Element|array|function)$"))
                        problems.Add(c.Where + ": " + c.Name + " is documented to return " + r + ", but its JSDoc says @returns {" + jd + "}");
                }
            }
            return problems;
        }

        /// <summary>The JSDoc @returns type of a definition named so (the comment right before it), null if none or ambiguous.</summary>
        static String JsDocReturns(Sources src, String name)
        {
            String found = null;
            var e = Regex.Escape(name);
            var re = new Regex(@"/\*\*(?<doc>(?:(?!\*/).)*)\*/\s*(?:(?:static|async|get|const|let|var|function)\s+)*(?:[\w$]+\s*\.\s*)*" + e + @"\s*(?:[:=]\s*(?:async\s+)?(?:function\b|\()|\()", RegexOptions.Singleline);
            foreach (var raw in src.Raw)
                foreach (Match m in re.Matches(raw))
                {
                    //  @returns {type} with balanced braces ({{ Text: string }})
                    var doc = m.Groups["doc"].Value;
                    var at = Regex.Match(doc, @"@returns?\s*\{");
                    if (!at.Success)
                        continue;
                    int depth = 1, k = at.Index + at.Length;
                    for (; (k < doc.Length) && (depth > 0); ++k)
                        if (doc[k] == '{')
                            ++depth;
                        else if (doc[k] == '}')
                            --depth;
                    var t = doc.Substring(at.Index + at.Length, Math.Max(0, k - 1 - at.Index - at.Length));
                    if ((found != null) && (found != t))
                        return null;
                    found = t;
                }
            return found;
        }

        /// <summary>
        /// Parse the documented READMEs of a repository (or of a web root) and run the static checks.
        /// </summary>
        /// <param name="root">The SysWeaver repository, or a web root</param>
        /// <returns>The contracts and the problems</returns>
        public static (List<Contract> Contracts, List<String> Problems) Run(String root)
        {
            var webRoots = (Checker.FindWebRoots(root) ?? new List<String> { "" }).Select(r => Path.Combine(root, r)).ToList();
            var contracts = new List<Contract>();
            var problems = new List<String>();
            foreach (var rel in Readmes)
            {
                var path = webRoots.Select(w => Path.Combine(w, rel.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists);
                if (path == null)
                    continue;
                var parsed = Parse(File.ReadAllText(path), rel);
                if (parsed.Count == 0)
                    problems.Add(rel + ": no api rows found");
                contracts.AddRange(parsed);
            }
            //  The sources: the documented folders of every web root (explore lives in its own module)
            problems.AddRange(CheckStatic(contracts, LoadSources(webRoots)));
            return (contracts, problems);
        }

        // ------------------------------------------------------------------ the browser manifest

        /// <summary>Pure functions the contract page may call with the probe arguments (no request, no navigation, no ui).</summary>
        static readonly Regex Pure = new Regex(@"^Sw\.(?:Net\.(?:Message|MessageText|MessageParts|FailText|Compose|ShouldReport|IsNetworkError|OutcomeKind)|ActionName)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The manifest script for the browser contract page (sw/readme_contracts.js): window.SwReadmeContracts = { Rows, Run() }.
        /// Run() → { Problems, Passed, Skipped }: every reachable documented name exists (a function where documented with parentheses),
        /// every documented option is in the api's registered schema (Sw.DevOptions.Schema), and the pure functions return their
        /// documented type for each probe argument (an Error, a Sw.Net.Error with and without a reason, a text, null).
        /// </summary>
        public static String Manifest(IEnumerable<Contract> contracts)
        {
            var sb = new StringBuilder();
            sb.Append("/**\r\n * @file sw/readme_contracts.js - GENERATED by \"SwWeb contracts\" from the api rows of core / ui / shell / explore README.md: do not edit.\r\n");
            sb.Append(" * Purpose: a contract test per documented api, run by core/contracttest.html (window.SwReadmeContracts.Run()). The texts are test data (SwWeb:no-tr-file: generated).\r\n");
            sb.Append(" * Dependencies: the core (Sw). Globals defined: window.SwReadmeContracts.\r\n */\r\n");
            sb.Append("(function () {\r\n    \"use strict\";\r\n    const rows = [\r\n");
            //  (one row per name: the options of every README that documents it, the first documented return type)
            var merged = contracts.Where(x => !x.Instance).GroupBy(x => x.Name).Select(g =>
            {
                var m = new Contract { Name = g.Key, Kind = g.Any(x => x.Kind == "constructor") ? "constructor" : g.Any(x => x.Kind == "function") ? "function" : "member", Where = g.First().Where, Returns = g.Select(x => x.Returns).FirstOrDefault(x => x != null) };
                foreach (var o in g.SelectMany(x => x.Options).Distinct())
                    m.Options.Add(o);
                return m;
            });
            foreach (var c in merged.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                sb.Append("        { Name: ").Append(Js(c.Name)).Append(", Kind: ").Append(Js(c.Kind)).Append(", Where: ").Append(Js(c.Where));
                if (c.Options.Count > 0)
                    sb.Append(", Options: [").Append(String.Join(", ", c.Options.Select(Js))).Append(']');
                if (c.Returns != null)
                    sb.Append(", Returns: ").Append(Js(c.Returns));
                if (Pure.IsMatch(c.Name) && (c.Kind == "function"))
                    sb.Append(", Pure: true");
                sb.Append(" },\r\n");
            }
            sb.Append("    ];\r\n");
            sb.Append(RunnerJs);
            sb.Append("})();\r\n");
            return sb.ToString();
        }

        static String Js(String s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        const String RunnerJs = @"    /** @returns {*} The value at a dotted path from window, undefined when a part is missing. */
    function resolve(path) {
        let v = window;
        for (const p of path.split(""."")) {
            if ((v === null) || (v === undefined))
                return undefined;
            try {
                v = v[p];
            }
            catch {
                return undefined;
            }
        }
        return v;
    }
    /** @returns {boolean|string} True, or what is wrong. */
    function typeOk(t, v) {
        switch (t) {
            case ""string"": return typeof v === ""string"" || ""a string, got "" + typeof v;
            case ""boolean"": return typeof v === ""boolean"" || ""a boolean, got "" + typeof v;
            case ""number"": return typeof v === ""number"" || ""a number, got "" + typeof v;
            case ""function"": return typeof v === ""function"" || ""a function, got "" + typeof v;
            case ""Promise"": return (!!v && typeof v.then === ""function"") || ""a promise, got "" + typeof v;
            case ""Element"": return v instanceof Element || ""an element, got "" + typeof v;
            case ""array"": return Array.isArray(v) || ""an array, got "" + typeof v;
            case ""object"": return (!!v && typeof v === ""object"") || ""an object, got "" + typeof v;
            default: return true;
        }
    }
    /** The probe arguments of a pure function (what the READMEs promise to handle). */
    function probes() {
        const r = [[""Save the note""], [""Save the note"", new Error(""disk full"")], [""Save the note"", null]];
        if (window.Sw && Sw.Net && Sw.Net.Error) {
            try {
                const withReason = new Sw.Net.Error(""409 - Conflict"", 409, ""Conflict"", ""The note was changed by someone else."", ""x"");
                withReason.Reason = ""The note was changed by someone else."";
                r.push([""Save the note"", withReason], [""Save the note"", new Sw.Net.Error(""500 - Internal Server Error"", 500, ""Internal Server Error"", """", ""x"")]);
            }
            catch {
                //  (another constructor shape: the probe is left out)
            }
        }
        return r;
    }
    /** Run every row. @returns {{Problems: Array<string>, Passed: number, Skipped: Array<string>}} The result. */
    function run() {
        const res = { Problems: [], Passed: 0, Skipped: [] };
        for (const r of rows) {
            const root = r.Name.split(""."").slice(0, 2).join(""."");
            if (resolve(root) === undefined) {
                res.Skipped.push(r.Name + "" (library not loaded)"");
                continue;
            }
            const v = resolve(r.Name);
            if (v === undefined) {
                res.Problems.push(r.Where + "": "" + r.Name + "" is documented but not defined"");
                continue;
            }
            if ((r.Kind !== ""member"") && (typeof v !== ""function"")) {
                res.Problems.push(r.Where + "": "" + r.Name + "" is documented as a call but is a "" + typeof v);
                continue;
            }
            const schema = r.Options && Sw.DevOptions && Sw.DevOptions.Schema ? Sw.DevOptions.Schema(r.Name) : null;
            if (schema)
                for (const o of r.Options)
                    if (!(o in schema))
                        res.Problems.push(r.Where + "": "" + r.Name + "" documents the option "" + o + "", its schema doesn't accept it"");
            if (r.Pure && r.Returns)
                for (const a of probes()) {
                    let out;
                    try {
                        out = v.apply(resolve(r.Name.split(""."").slice(0, -1).join(""."")), a.slice(0, Math.max(1, v.length)));
                    }
                    catch (e) {
                        res.Problems.push(r.Where + "": "" + r.Name + "" throws for the probe ("" + a.length + "" arguments): "" + (e && e.message));
                        continue;
                    }
                    const ok = typeOk(r.Returns, out);
                    if (ok !== true)
                        res.Problems.push(r.Where + "": "" + r.Name + "" should return "" + r.Returns + "": "" + ok);
                    else if ((r.Returns === ""string"") && /undefined|\[object/.test(out))
                        res.Problems.push(r.Where + "": "" + r.Name + "" returned "" + JSON.stringify(out));
                }
            ++res.Passed;
        }
        return res;
    }
    window.SwReadmeContracts = { Rows: rows, Run: run };
";
    }
}
