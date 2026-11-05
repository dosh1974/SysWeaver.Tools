using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SwWeb.Lexing;

namespace SwWeb.Bundling
{
    /// <summary>
    /// The result of building (or checking) one bundle.
    /// </summary>
    public enum BundleStatus
    {
        /// <summary>
        /// The output file already has the generated content (nothing written).
        /// </summary>
        UpToDate,
        /// <summary>
        /// The output file was (re)written.
        /// </summary>
        Written,
        /// <summary>
        /// Check mode: the output file is missing or differs from the generated content.
        /// </summary>
        OutOfDate,
        /// <summary>
        /// The bundle couldn't be generated (invalid definition, missing or invalid source files).
        /// </summary>
        Failed,
    }

    /// <summary>
    /// The result of one bundle.
    /// </summary>
    public sealed class BundleResult
    {
        /// <summary>
        /// The bundle definition.
        /// </summary>
        public BundleDefinition Bundle;
        /// <summary>
        /// The full path of the output file (null if the definition is invalid).
        /// </summary>
        public String OutPath;
        /// <summary>
        /// The status.
        /// </summary>
        public BundleStatus Status;
        /// <summary>
        /// Source files (relative, as in the manifest) that don't exist.
        /// </summary>
        public readonly List<String> MissingFiles = new List<String>();
        /// <summary>
        /// Other errors (invalid definition, invalid source files).
        /// </summary>
        public readonly List<String> Errors = new List<String>();
        /// <summary>
        /// The size of the generated output in bytes (as written; 0 when it wasn't generated).
        /// </summary>
        public long Size;
        /// <summary>
        /// The gzip compressed size of the generated output in bytes (smallest size; 0 when it wasn't generated).
        /// </summary>
        public long GzipSize;
        /// <summary>
        /// The size budgets the output is over (<see cref="BundleDefinition.MaxKB"/>, <see cref="BundleDefinition.MaxGzipKB"/>): the bundle
        /// is generated (and written when not checking), but the run fails.
        /// </summary>
        public readonly List<String> OverBudget = new List<String>();
    }

    /// <summary>
    /// A source file to concatenate.
    /// </summary>
    public sealed class BundleSource
    {
        /// <summary>
        /// Create a source.
        /// </summary>
        /// <param name="name">The name (relative path with forward slashes) used in the header and separators</param>
        /// <param name="text">The decoded text (a leading BOM is removed)</param>
        public BundleSource(String name, String text)
        {
            Name = name;
            Text = text;
        }

        /// <summary>
        /// The name (relative path with forward slashes).
        /// </summary>
        public readonly String Name;

        /// <summary>
        /// The decoded text.
        /// </summary>
        public readonly String Text;
    }

    /// <summary>
    /// Builds bundles: concatenates source files in manifest order into one output file.
    /// Output: UTF-8 with a BOM (the convention of the SysWeaver source files), CRLF line endings, inner BOMs removed,
    /// a generated header comment and a separator comment per source file. The output only depends on the manifest
    /// and the source contents (no time stamps), so it is deterministic.
    /// </summary>
    public static class Bundler
    {
        /// <summary>
        /// The default manifest location, relative to the web root.
        /// </summary>
        public const String DefaultManifest = "sw/bundles.json";

        /// <summary>
        /// The line ending used in bundles.
        /// </summary>
        public const String NewLine = "\r\n";

        static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true, true);

        /// <summary>
        /// Normalize a source: remove BOM chars at the start, convert all line endings to CRLF, remove leading empty lines and trailing white space.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The normalized text (without a final new line)</returns>
        public static String Normalize(String text)
        {
            var sb = new StringBuilder(text.Length + 64);
            int i = 0;
            var l = text.Length;
            while ((i < l) && (text[i] == '\ufeff'))
                ++i;
            for (; i < l; ++i)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if ((i + 1 < l) && (text[i + 1] == '\n'))
                        ++i;
                    sb.Append(NewLine);
                    continue;
                }
                if (c == '\n')
                {
                    sb.Append(NewLine);
                    continue;
                }
                sb.Append(c);
            }
            var s = sb.ToString();
            // Remove leading empty lines
            int ls = 0;
            for (int p = 0; p < s.Length;)
            {
                int e = p;
                while ((e < s.Length) && ((s[e] == ' ') || (s[e] == '\t')))
                    ++e;
                if ((e + 1 < s.Length) && (s[e] == '\r') && (s[e + 1] == '\n'))
                {
                    p = ls = e + 2;
                    continue;
                }
                break;
            }
            return s.Substring(ls).TrimEnd();
        }

        static String CommentSafe(String s) => s.Replace("*/", "* /");

        /// <summary>
        /// A JavaScript string literal (double quoted) of a text.
        /// </summary>
        static String JsString(String s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                if ((c == '"') || (c == '\\'))
                    sb.Append('\\').Append(c);
                else if (c < ' ' || c == (char)0x2028 || c == (char)0x2029)
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>
        /// The names a JavaScript source declares at its top level with let, const, class or function (a load once guard would make them
        /// local to its block). A function or class expression (ex: an IIFE, "!function () {}()") is not a declaration.
        /// </summary>
        /// <param name="text">The source</param>
        /// <returns>The declared names (in order)</returns>
        public static List<String> TopLevelDeclarations(String text)
        {
            var res = new List<String>();
            var s = JsLexer.Tokenize(text).Where(t => t.IsSignificant).ToList();
            int depth = 0;
            String prev = null;
            for (int i = 0; i < s.Count; ++i)
            {
                var t = s[i];
                var x = t.GetText(text);
                if (t.Kind == JsTokenKind.Punctuator)
                {
                    if ((x == "(") || (x == "[") || (x == "{"))
                        ++depth;
                    else if ((x == ")") || (x == "]") || (x == "}"))
                        --depth;
                }
                else if ((depth == 0) && (t.Kind == JsTokenKind.Identifier) && ((prev == null) || (prev == ";") || (prev == "}")))
                {
                    var w = x;
                    int k = i + 1;
                    if ((w == "async") && (k < s.Count) && s[k].Is(text, "function"))
                    {
                        w = "function";
                        ++k;
                    }
                    if ((w == "let") || (w == "const") || (w == "class") || (w == "function"))
                    {
                        if ((k < s.Count) && s[k].Is(text, "*"))
                            ++k;
                        res.Add(w + " " + ((k < s.Count) ? s[k].GetText(text) : ""));
                    }
                }
                prev = x;
            }
            return res;
        }

        /// <summary>
        /// Test if a bundle output is JavaScript (else CSS).
        /// </summary>
        /// <param name="outName">The output file name</param>
        /// <returns>True for ".js"</returns>
        public static bool IsJs(String outName) => outName.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Get the first and last significant JavaScript token text of a source (null if none), and an error if the source ends inside a comment, string, template or regex.
        /// </summary>
        static (String First, String Last, String Error) JsEdges(String text)
        {
            var tokens = JsLexer.Tokenize(text);
            String first = null, last = null, err = null;
            foreach (var t in tokens)
            {
                // Unterminated strings and regex literals end at the line end, block comments and templates would continue into the next file
                if (t.Unterminated && (t.Kind == JsTokenKind.BlockComment || t.IsTemplate))
                    err = "unterminated " + (t.IsTemplate ? "template literal" : "block comment") + " (would swallow the following files)";
                if (!t.IsSignificant)
                    continue;
                var x = t.GetText(text);
                first ??= x;
                last = x;
            }
            return (first, last, err);
        }

        /// <summary>
        /// Test if a CSS source ends inside a comment or a string.
        /// </summary>
        static String CssError(String text)
        {
            var l = text.Length;
            for (int i = 0; i < l; ++i)
            {
                var c = text[i];
                if ((c == '/') && (i + 1 < l) && (text[i + 1] == '*'))
                {
                    var e = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (e < 0)
                        return "unterminated comment (would swallow the following files)";
                    i = e + 1;
                    continue;
                }
                if ((c == '"') || (c == '\''))
                {
                    for (++i; i < l; ++i)
                    {
                        var d = text[i];
                        if (d == '\\')
                        {
                            ++i;
                            continue;
                        }
                        if ((d == c) || (d == '\r') || (d == '\n'))
                            break;
                    }
                }
            }
            return null;
        }

        static readonly HashSet<String> AsiHazards = new HashSet<String>(StringComparer.Ordinal) { "(", "[", "+", "-", "/", "*", "%", ",", ".", "?.", "=", "<", ">", "&&", "||", "??" };

        /// <summary>
        /// Concatenate sources into bundle text (without a BOM).
        /// </summary>
        /// <param name="outName">The output name (relative path, ex: "sw/core.js")</param>
        /// <param name="sources">The sources in order</param>
        /// <param name="header">Optional header text</param>
        /// <param name="manifestName">The manifest name shown in the header</param>
        /// <param name="errors">Receives errors (sources ending inside a comment etc)</param>
        /// <param name="loadOnce">JavaScript only: wrap every source in a load once guard (see <see cref="BundleDefinition.LoadOnce"/>)</param>
        /// <returns>The bundle text</returns>
        public static String Concatenate(String outName, IReadOnlyList<BundleSource> sources, String header, String manifestName, List<String> errors = null, bool loadOnce = false)
        {
            bool js = IsJs(outName);
            loadOnce &= js;
            var nl = NewLine;
            var sb = new StringBuilder();
            sb.Append("/**").Append(nl);
            sb.Append(" * @file ").Append(outName).Append(": GENERATED by SwWeb bundle from ").Append(CommentSafe(manifestName)).Append(", do not edit!").Append(nl);
            sb.Append(" * Edit the source files and run \"SwWeb bundle <srcWebRoot>\" to rebuild (\"SwWeb bundle <srcWebRoot> --check\" verifies).").Append(nl);
            if (!String.IsNullOrWhiteSpace(header))
            {
                sb.Append(" *").Append(nl);
                foreach (var line in Normalize(header).Split(nl))
                    sb.Append(line.Length > 0 ? " * " : " *").Append(CommentSafe(line)).Append(nl);
            }
            sb.Append(" *").Append(nl);
            if (loadOnce)
            {
                sb.Append(" * Load once: each source runs once per page, also when another bundle of the page contains it (Sw.Sources lists the").Append(nl);
                sb.Append(" * sources that ran and the bundle that ran them).").Append(nl);
                sb.Append(" *").Append(nl);
            }
            sb.Append(" * Sources (in order):").Append(nl);
            foreach (var s in sources)
                sb.Append(" *   ").Append(CommentSafe(s.Name)).Append(nl);
            sb.Append(" */").Append(nl);

            var texts = sources.Select(x => Normalize(x.Text)).ToArray();
            var edges = js ? texts.Select(JsEdges).ToArray() : null;
            var c = texts.Length;
            for (int i = 0; i < c; ++i)
            {
                var name = sources[i].Name;
                sb.Append(nl);
                if (js)
                    sb.Append("// ---- ").Append(name).Append(" ----").Append(nl);
                else
                    sb.Append("/* ---- ").Append(CommentSafe(name)).Append(" ---- */").Append(nl);
                var t = texts[i];
                if (t.Length == 0)
                    continue;
                //  A source that declares global functions at its top level (core/i18n.js: the translation markers) is not wrapped: a function
                //  declaration must stay a plain global function, and declaring it again is harmless (the rest of such a file guards itself)
                bool guard = loadOnce;
                if (guard)
                {
                    var decl = TopLevelDeclarations(t);
                    if ((decl.Count > 0) && decl.All(d => d.StartsWith("function ", StringComparison.Ordinal)))
                    {
                        guard = false;
                        sb.Append("// (not guarded: it declares global functions, its other code runs once by itself)").Append(nl);
                        sb.Append(t).Append(nl);
                        var ue = edges[i];
                        if (ue.Error != null)
                            errors?.Add(name + ": " + ue.Error);
                        if ((ue.Last != null) && (ue.Last != ";") && (ue.Last != "}"))
                            sb.Append(';').Append(nl);
                        continue;
                    }
                }
                if (guard)
                {
                    var key = JsString(name);
                    sb.Append("if (!((window.Sw = window.Sw || {}).Sources = window.Sw.Sources || {})[").Append(key).Append("]) {").Append(nl);
                    sb.Append("window.Sw.Sources[").Append(key).Append("] = ").Append(JsString(outName)).Append(';').Append(nl);
                }
                sb.Append(t).Append(nl);
                if (guard)
                {
                    var e = edges[i];
                    if (e.Error != null)
                        errors?.Add(name + ": " + e.Error);
                    //  The source ends inside the guard block: a statement without a final ';' is closed before the block ends
                    if ((e.Last != null) && (e.Last != ";") && (e.Last != "}"))
                        sb.Append(';').Append(nl);
                    sb.Append('}').Append(nl);
                    continue;
                }
                if (js)
                {
                    var e = edges[i];
                    if (e.Error != null)
                        errors?.Add(name + ": " + e.Error);
                    var last = e.Last;
                    if ((last == null) || (last == ";"))
                        continue;
                    // Find the first token of the next non-empty source
                    String next = null;
                    for (int j = i + 1; (j < c) && (next == null); ++j)
                        next = edges[j].First;
                    bool needSemi = (last != "}") || ((next != null) && (AsiHazards.Contains(next) || next.StartsWith('`')));
                    if (needSemi)
                        sb.Append(';').Append(nl);
                }
                else
                {
                    var e = CssError(t);
                    if (e != null)
                        errors?.Add(name + ": " + e);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The gzip compressed length of data (smallest size, about gzip -9: what a page downloads from a compressing server).
        /// </summary>
        /// <param name="data">The data</param>
        /// <returns>The compressed length in bytes</returns>
        public static long GzipLength(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true))
                gz.Write(data, 0, data.Length);
            return ms.Length;
        }

        /// <summary>
        /// The sources that a LoadOnce JavaScript bundle and a bundle without LoadOnce both contain (AS6-1): on a page that loads both
        /// (an old page with common/common.js that adds sw/ui.js) the unguarded copy runs the source a second time. Every bundle that
        /// shares a source with a LoadOnce bundle must be LoadOnce too.
        /// </summary>
        /// <param name="manifest">The manifest</param>
        /// <returns>Per bundle without LoadOnce (by its Out): the errors</returns>
        public static Dictionary<String, List<String>> LoadOnceConflicts(BundleManifest manifest)
        {
            var res = new Dictionary<String, List<String>>(StringComparer.OrdinalIgnoreCase);
            var js = manifest.Bundles.Where(b => (b != null) && !String.IsNullOrWhiteSpace(b.Out) && IsJs(b.Out.Trim())).ToList();
            foreach (var b in js.Where(x => !x.LoadOnce))
                foreach (var f in b.Files.Select(x => ToDisplay(x?.Trim() ?? "")).Where(x => x.Length > 0))
                {
                    var guarded = js.FirstOrDefault(x => x.LoadOnce && x.Files.Any(y => String.Equals(ToDisplay(y?.Trim() ?? ""), f, StringComparison.OrdinalIgnoreCase)));
                    if (guarded == null)
                        continue;
                    var key = ToDisplay(b.Out.Trim());
                    if (!res.TryGetValue(key, out var list))
                        res[key] = list = new List<String>();
                    list.Add(f + " is also in " + ToDisplay(guarded.Out.Trim()) + " (LoadOnce): set \"LoadOnce\": true here too, or a page that loads both runs it twice");
                }
            return res;
        }

        /// <summary>
        /// Encode bundle text as written to disc (UTF-8 with BOM).
        /// </summary>
        /// <param name="text">The bundle text</param>
        /// <returns>The bytes</returns>
        public static byte[] Encode(String text)
        {
            var pre = Utf8Bom.GetPreamble();
            var data = Utf8Bom.GetBytes(text);
            var res = new byte[pre.Length + data.Length];
            pre.CopyTo(res, 0);
            data.CopyTo(res, pre.Length);
            return res;
        }

        static String ToDisplay(String rel) => rel.Replace('\\', '/');

        /// <summary>
        /// Resolve a manifest relative path, returns null if it's absolute or outside the root.
        /// </summary>
        static String Resolve(String rootFull, String rel)
        {
            if (String.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.StartsWith('/') || rel.StartsWith('\\'))
                return null;
            var full = Path.GetFullPath(Path.Combine(rootFull, rel));
            var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : (rootFull + Path.DirectorySeparatorChar);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return full;
        }

        /// <summary>
        /// Get the actual name of an existing file (to detect case mismatches on case insensitive file systems), null if missing.
        /// </summary>
        static String ActualFileName(String full)
        {
            var dir = Path.GetDirectoryName(full);
            if (!Directory.Exists(dir))
                return null;
            var name = Path.GetFileName(full);
            foreach (var f in Directory.EnumerateFiles(dir, name))
            {
                var n = Path.GetFileName(f);
                if (String.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return null;
        }

        /// <summary>
        /// Build (or check) one bundle.
        /// </summary>
        /// <param name="root">The web root folder</param>
        /// <param name="bundle">The bundle definition</param>
        /// <param name="manifestName">The manifest name shown in the header</param>
        /// <param name="check">True to only check that the output is current (nothing is written)</param>
        /// <returns>The result</returns>
        /// <param name="declared">The names that the JavaScript sources of all bundles declare (null: only this bundle's sources)</param>
        /// <param name="verify">False to skip the source verification (<see cref="BundleVerifier"/>)</param>
        public static BundleResult Build(String root, BundleDefinition bundle, String manifestName, bool check, ISet<String> declared = null, bool verify = true)
        {
            var rootFull = Path.GetFullPath(root);
            var r = new BundleResult { Bundle = bundle, Status = BundleStatus.Failed };
            var outName = bundle.Out?.Trim();
            if (String.IsNullOrEmpty(outName))
            {
                r.Errors.Add("\"Out\" is missing");
                return r;
            }
            outName = ToDisplay(outName);
            var ext = Path.GetExtension(outName).ToLowerInvariant();
            if ((ext != ".js") && (ext != ".css"))
                r.Errors.Add("\"Out\" must be a .js or .css file");
            if (Path.GetFileName(outName).Count(x => x == '.') > 1)
                r.Errors.Add("\"Out\" must have a single dot in the file name");
            var outPath = Resolve(rootFull, outName);
            if (outPath == null)
                r.Errors.Add("\"Out\" must be a relative path inside the web root");
            r.OutPath = outPath;
            if (bundle.Files.Count == 0)
                r.Errors.Add("\"Files\" is empty");
            var seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<BundleSource>();
            foreach (var f in bundle.Files)
            {
                var name = ToDisplay(f?.Trim() ?? "");
                if (name.Length == 0)
                {
                    r.Errors.Add("Empty file name in \"Files\"");
                    continue;
                }
                if (!String.Equals(Path.GetExtension(name), ext, StringComparison.OrdinalIgnoreCase))
                    r.Errors.Add(name + ": must have the same extension as the output (" + ext + ")");
                if (!seen.Add(name))
                    r.Errors.Add(name + ": listed more than once");
                if (String.Equals(name, outName, StringComparison.OrdinalIgnoreCase))
                    r.Errors.Add(name + ": the output can't be a source");
                var full = Resolve(rootFull, name);
                if (full == null)
                {
                    r.Errors.Add(name + ": must be a relative path inside the web root");
                    continue;
                }
                if (!File.Exists(full))
                {
                    r.MissingFiles.Add(name);
                    continue;
                }
                var actual = ActualFileName(full);
                if ((actual != null) && (actual != Path.GetFileName(full)))
                    r.Errors.Add(name + ": the file name case differs on disc (\"" + actual + "\"), URLs are case sensitive");
                var st = SourceText.FromFile(full);
                if (st.InvalidUtf8)
                    r.Errors.Add(name + ": not valid UTF-8");
                sources.Add(new BundleSource(name, st.Text));
            }
            if ((r.Errors.Count > 0) || (r.MissingFiles.Count > 0))
                return r;
            bool loadOnce = bundle.LoadOnce && IsJs(outName);
            if (bundle.LoadOnce && !IsJs(outName))
                r.Errors.Add("\"LoadOnce\" is only valid for a .js bundle");
            //  A load once guard puts each source in a block: a top level declaration would become local to it (other sources lose it)
            if (loadOnce)
                foreach (var s in sources)
                {
                    var decl = TopLevelDeclarations(s.Text);
                    //  Only function declarations: the source is not wrapped (see Concatenate)
                    if (decl.All(d => d.StartsWith("function ", StringComparison.Ordinal)))
                        continue;
                    foreach (var d in decl)
                        r.Errors.Add(s.Name + ": top level declaration \"" + d + "\" in a LoadOnce bundle (wrap the file in an IIFE)");
                }
            if (r.Errors.Count > 0)
                return r;
            //  A half edited source must not reach every page: verify the sources (syntax, undefined calls) and the result
            bool verifyJs = verify && IsJs(outName);
            HashSet<String> known = null;
            if (verifyJs)
            {
                known = new HashSet<String>(StringComparer.Ordinal);
                if (declared != null)
                    known.UnionWith(declared);
                foreach (var s in sources)
                    BundleVerifier.CollectDeclared(s.Text, known);
                foreach (var s in sources)
                    r.Errors.AddRange(BundleVerifier.Verify(s.Name, s.Text, known));
                if (r.Errors.Count > 0)
                {
                    r.Errors.Add("not written: the previous " + outName + " stays in place");
                    return r;
                }
            }
            var text = Concatenate(outName, sources, bundle.Header, manifestName, r.Errors, loadOnce);
            if (r.Errors.Count > 0)
                return r;
            if (verifyJs)
            {
                //  The joined bundle parses too (brackets across the file borders)
                r.Errors.AddRange(BundleVerifier.Verify(outName, text, known).Where(x => x.Contains("): syntax: ", StringComparison.Ordinal)));
                if (r.Errors.Count > 0)
                {
                    r.Errors.Add("not written: the previous " + outName + " stays in place");
                    return r;
                }
            }
            var data = Encode(text);
            //  The size budgets (AS6-6): reported, and a bundle over one fails the run (it is still generated)
            r.Size = data.Length;
            r.GzipSize = GzipLength(data);
            if (bundle.MaxKB.HasValue && (r.Size > bundle.MaxKB.Value * 1024))
                r.OverBudget.Add(FormattableString.Invariant($"{r.Size / 1024.0:0.0} KB is over its budget \"MaxKB\": {bundle.MaxKB.Value} (raise it in the manifest only when the growth is intended)"));
            if (bundle.MaxGzipKB.HasValue && (r.GzipSize > bundle.MaxGzipKB.Value * 1024))
                r.OverBudget.Add(FormattableString.Invariant($"{r.GzipSize / 1024.0:0.0} KB gzip is over its budget \"MaxGzipKB\": {bundle.MaxGzipKB.Value} (raise it in the manifest only when the growth is intended)"));
            bool same = File.Exists(outPath) && File.ReadAllBytes(outPath).AsSpan().SequenceEqual(data);
            if (same)
            {
                r.Status = BundleStatus.UpToDate;
                return r;
            }
            if (check)
            {
                r.Status = BundleStatus.OutOfDate;
                return r;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            //  Written next to it, then moved over it: a reader never sees half a bundle
            var tmp = outPath + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, outPath, true);
            r.Status = BundleStatus.Written;
            return r;
        }

        /// <summary>
        /// Build (or check) all bundles of a manifest.
        /// </summary>
        /// <param name="root">The web root folder</param>
        /// <param name="manifest">The manifest</param>
        /// <param name="manifestName">The manifest name shown in the headers</param>
        /// <param name="check">True to only check that the outputs are current (nothing is written)</param>
        /// <returns>One result per bundle</returns>
        /// <param name="verify">False to skip the source verification (<see cref="BundleVerifier"/>)</param>
        public static List<BundleResult> BuildAll(String root, BundleManifest manifest, String manifestName, bool check, bool verify = true)
        {
            var res = new List<BundleResult>();
            //  The names that all JavaScript sources declare (a bundle may call what another bundle defines)
            HashSet<String> declared = null;
            if (verify)
            {
                declared = new HashSet<String>(StringComparer.Ordinal);
                var rootFull = Path.GetFullPath(root);
                foreach (var b in manifest.Bundles.Where(x => (x != null) && IsJs(x.Out ?? "")))
                    foreach (var f in b.Files)
                    {
                        var full = Resolve(rootFull, ToDisplay(f?.Trim() ?? ""));
                        if ((full != null) && File.Exists(full))
                            BundleVerifier.CollectDeclared(SourceText.FromFile(full).Text, declared);
                    }
            }
            var counts = manifest.Bundles
                .Where(b => !String.IsNullOrWhiteSpace(b?.Out))
                .GroupBy(b => b.Out.Trim().Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var conflicts = LoadOnceConflicts(manifest);
            foreach (var b in manifest.Bundles)
            {
                if (b == null)
                    continue;
                if (!String.IsNullOrWhiteSpace(b.Out) && (counts[b.Out.Trim().Replace('\\', '/')] > 1))
                {
                    var d = new BundleResult { Bundle = b, Status = BundleStatus.Failed };
                    d.Errors.Add("\"Out\" is used by more than one bundle");
                    res.Add(d);
                    continue;
                }
                if (!String.IsNullOrWhiteSpace(b.Out) && conflicts.TryGetValue(b.Out.Trim().Replace('\\', '/'), out var lo))
                {
                    var d = new BundleResult { Bundle = b, Status = BundleStatus.Failed };
                    d.Errors.AddRange(lo);
                    res.Add(d);
                    continue;
                }
                res.Add(Build(root, b, manifestName, check, declared, verify));
            }
            return res;
        }
    }
}
