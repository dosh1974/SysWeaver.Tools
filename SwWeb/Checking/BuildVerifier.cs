using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// "SwWeb verify-build": checks the BUILT web assets (the compressed <c>web/**/*.br</c> / <c>*.gz</c> outputs of Compress.exe that a
    /// project embeds and ships), not the srcWeb sources. The scratch apps and every browser test serve srcWeb from disk, so a bug that
    /// only the build's minifiers make (the chart.js "Cannot access 'o' before initialization", OWN3-Chart-1; the shader_minifier
    /// overload collisions and the empty TwinklingTunnel.glsl, OWN4-Demo-1 / 2) is invisible to them (AS7-2).
    /// <list type="bullet">
    /// <item><b>build-empty</b>: a .js / .mjs / .css / .html / .htm / .glsl output is empty while its source isn't (a failed transform).</item>
    /// <item><b>build-read</b>: the output can't be decompressed.</item>
    /// <item><b>build-js-syntax</b>: a .js / .mjs output (or a script element of a .html output) has an unterminated token or unbalanced brackets.</item>
    /// <item><b>build-js-tdz</b>: minified code reads a name before the declarator of the same declaration that declares it
    /// (<see cref="MinifierChecker.CheckOutput"/>, the shape of the minifier reorder bug).</item>
    /// <item><b>build-glsl-duplicate</b>: a .glsl output defines two functions with the same name and parameter types (in the same
    /// preprocessor branch): the shader doesn't compile ("function already has a body").</item>
    /// <item><b>build-glsl-brackets</b>: a .glsl output has unbalanced brackets.</item>
    /// </list>
    /// A real compile of every built shader in a browser is done by the parity gate (ParityTests plugin "release-assets").
    /// </summary>
    public static class BuildVerifier
    {
        /// <summary>
        /// A problem in a built file.
        /// </summary>
        /// <param name="File">The compressed output file</param>
        /// <param name="Line">1-based line in the decompressed text (0 = the whole file)</param>
        /// <param name="Col">1-based column</param>
        /// <param name="Rule">The rule id (build-*)</param>
        /// <param name="Message">What is wrong</param>
        public sealed record Problem(String File, int Line, int Col, String Rule, String Message)
        {
            /// <summary>
            /// Format as "file(line,col): error rule: message".
            /// </summary>
            /// <param name="root">Print the path relative to this folder (null = full path)</param>
            /// <returns>The text</returns>
            public String ToString(String root)
            {
                var f = root == null ? File : Path.GetRelativePath(root, File);
                return f + (Line > 0 ? "(" + Line + "," + Col + ")" : "") + ": error " + Rule + ": " + Message;
            }
        }

        /// <summary>
        /// The result of a run.
        /// </summary>
        public sealed class Result
        {
            /// <summary>
            /// The problems found.
            /// </summary>
            public readonly List<Problem> Problems = new List<Problem>();
            /// <summary>
            /// Number of checked outputs per inner extension (".js", ".glsl" ...).
            /// </summary>
            public readonly SortedDictionary<String, int> Checked = new SortedDictionary<String, int>(StringComparer.Ordinal);
        }

        static readonly HashSet<String> SkipFolders = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs", "srcWeb", "srcData",
        };

        static readonly HashSet<String> TextExt = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".mjs", ".css", ".html", ".htm", ".glsl",
        };

        /// <summary>
        /// Find the built outputs below a folder: every *.br / *.gz file whose decompressed name is a .js / .mjs / .css / .html / .htm / .glsl
        /// file, outside bin / obj / srcWeb / srcData / hidden folders.
        /// </summary>
        /// <param name="root">The folder (a repository, a project or a web output folder)</param>
        /// <returns>The files</returns>
        public static List<String> FindOutputs(String root)
        {
            var res = new List<String>();
            void Walk(String dir)
            {
                IEnumerable<String> files, dirs;
                try
                {
                    files = Directory.EnumerateFiles(dir);
                    dirs = Directory.EnumerateDirectories(dir);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f);
                    if (!ext.Equals(".br", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".gz", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (TextExt.Contains(Path.GetExtension(Path.GetFileNameWithoutExtension(f))))
                        res.Add(f);
                }
                foreach (var d in dirs)
                {
                    var n = Path.GetFileName(d);
                    if (SkipFolders.Contains(n) || n.StartsWith('.'))
                        continue;
                    Walk(d);
                }
            }
            Walk(Path.GetFullPath(root));
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        /// <summary>
        /// Decompress a .br / .gz file to text (UTF-8).
        /// </summary>
        /// <param name="file">The file</param>
        /// <returns>The text</returns>
        public static String ReadOutput(String file)
        {
            using var fs = File.OpenRead(file);
            using Stream ds = Path.GetExtension(file).Equals(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(fs, CompressionMode.Decompress)
                : new BrotliStream(fs, CompressionMode.Decompress);
            using var r = new StreamReader(ds, Encoding.UTF8, true);
            return r.ReadToEnd();
        }

        /// <summary>
        /// The source of a build output by the convention of the SysWeaver projects (Compress .\srcWeb\*+ .\web, .\srcData\*+ .\data):
        /// the file with the same relative path below "src" + the output folder's name (or srcWeb / srcData) next to an output folder.
        /// </summary>
        /// <param name="file">The output (.br / .gz)</param>
        /// <returns>The source file name, or null if not found</returns>
        public static String FindSource(String file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var dir = Path.GetDirectoryName(file);
            var rel = new List<String> { name };
            while (dir != null)
            {
                var n = Path.GetFileName(dir);
                var parent = Path.GetDirectoryName(dir);
                if (parent != null)
                    foreach (var s in new[] { "src" + n, "srcWeb", "srcData" })
                    {
                        var src = Path.Combine(new[] { parent, s }.Concat(rel).ToArray());
                        if (File.Exists(src))
                            return src;
                    }
                rel.Insert(0, n);
                dir = parent;
            }
            return null;
        }

        /// <summary>
        /// Verify every built output below a folder.
        /// </summary>
        /// <param name="root">The folder</param>
        /// <returns>The result</returns>
        public static Result Run(String root)
        {
            var res = new Result();
            foreach (var f in FindOutputs(root))
                VerifyFile(f, res);
            return res;
        }

        /// <summary>
        /// Verify one built output.
        /// </summary>
        /// <param name="file">The compressed file</param>
        /// <param name="res">Add the problems here</param>
        public static void VerifyFile(String file, Result res)
        {
            var ext = Path.GetExtension(Path.GetFileNameWithoutExtension(file)).ToLowerInvariant();
            res.Checked[ext] = res.Checked.GetValueOrDefault(ext) + 1;
            String text;
            try
            {
                text = ReadOutput(file);
            }
            catch (Exception ex)
            {
                res.Problems.Add(new Problem(file, 0, 0, "build-read", "can't decompress: " + ex.Message));
                return;
            }
            VerifyText(file, ext, text, res.Problems, () =>
            {
                var src = FindSource(file);
                return (src == null) || HasContent(File.ReadAllText(src), ext);
            });
        }

        /// <summary>
        /// True if a source has something besides white space, a BOM and comments (a comment-only .css minifies to nothing).
        /// </summary>
        /// <param name="text">The source text</param>
        /// <param name="ext">The extension (".js", ".css", ".html" ...)</param>
        /// <returns>True if the minified output should not be empty</returns>
        public static bool HasContent(String text, String ext)
        {
            text = text.Replace("﻿", "");
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            if (ext is ".html" or ".htm")
                text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
            else if (ext != ".css")
                text = Regex.Replace(text, @"(?m)^\s*//.*$", "");
            return !String.IsNullOrWhiteSpace(text);
        }

        /// <summary>
        /// Verify the decompressed text of a built output.
        /// </summary>
        /// <param name="file">The file name (for the problems)</param>
        /// <param name="ext">The inner extension (".js", ".glsl" ...)</param>
        /// <param name="text">The decompressed text</param>
        /// <param name="into">Add the problems here</param>
        /// <param name="sourceHasContent">Called for an empty output: true if the source isn't empty (null = treat as true)</param>
        public static void VerifyText(String file, String ext, String text, List<Problem> into, Func<bool> sourceHasContent = null)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                if ((sourceHasContent == null) || sourceHasContent())
                    into.Add(new Problem(file, 0, 0, "build-empty", "the built " + ext + " file is empty, its source isn't (a failed minify / transform step shipped nothing)"));
                return;
            }
            var st = new SourceText(text);
            switch (ext)
            {
                case ".js":
                case ".mjs":
                    VerifyJs(file, st, 0, text.Length, into);
                    break;
                case ".html":
                case ".htm":
                    foreach (var sc in HtmlScanner.GetScripts(HtmlScanner.Scan(text)).Where(x => x.IsJavaScript && (x.BodyEnd > x.BodyStart)))
                        VerifyJs(file, st, sc.BodyStart, sc.BodyEnd, into);
                    break;
                case ".glsl":
                    VerifyGlsl(file, st, into);
                    break;
            }
        }

        static void VerifyJs(String file, SourceText st, int start, int end, List<Problem> into)
        {
            var text = st.Text;
            var tokens = JsLexer.Tokenize(text, start, end);
            if (tokens.Any(t => t.Unterminated))
            {
                var t = tokens.First(x => x.Unterminated);
                var (l, c) = st.GetLineCol(t.Start);
                into.Add(new Problem(file, l, c, "build-js-syntax", "unterminated " + t.Kind));
                return;
            }
            //  Brackets
            var stack = new Stack<JsToken>();
            foreach (var t in tokens)
            {
                if (!t.IsSignificant)
                    continue;
                String open = null;
                if (t.Kind == JsTokenKind.TemplateHead)
                {
                    stack.Push(t);
                    continue;
                }
                if (t.Kind == JsTokenKind.TemplateTail)
                    open = "`";
                else if (t.Kind == JsTokenKind.Punctuator)
                {
                    if (t.Is(text, "(") || t.Is(text, "[") || t.Is(text, "{"))
                    {
                        stack.Push(t);
                        continue;
                    }
                    if (t.Is(text, ")"))
                        open = "(";
                    else if (t.Is(text, "]"))
                        open = "[";
                    else if (t.Is(text, "}"))
                        open = "{";
                }
                if (open == null)
                    continue;
                bool ok = stack.Count > 0;
                if (ok)
                {
                    var o = stack.Pop();
                    ok = open == "`" ? o.Kind == JsTokenKind.TemplateHead : (o.Kind == JsTokenKind.Punctuator) && o.Is(text, open);
                }
                if (!ok)
                {
                    var (l, c) = st.GetLineCol(t.Start);
                    into.Add(new Problem(file, l, c, "build-js-syntax", "unbalanced \"" + t.GetText(text) + "\""));
                    return;
                }
            }
            if (stack.Count > 0)
            {
                var (l, c) = st.GetLineCol(stack.Peek().Start);
                into.Add(new Problem(file, l, c, "build-js-syntax", "\"" + stack.Peek().GetText(text) + "\" is never closed"));
                return;
            }
            foreach (var f in MinifierChecker.CheckOutput(text, start, end))
            {
                var (l, c) = st.GetLineCol(f.Offset);
                into.Add(new Problem(file, l, c, "build-js-tdz", "\"" + f.Name + "\" is read before the declarator of the same " + f.Kind + " declaration that declares it (" + (f.Kind == "var" ? "a silent undefined" : "ReferenceError \"Cannot access '" + f.Name + "' before initialization\"") + "): the minifier reordered the declaration, see SwWeb rule minifier-reorder in the source"));
            }
        }

        static readonly HashSet<String> GlslQualifiers = new HashSet<String>(StringComparer.Ordinal)
        {
            "in", "out", "inout", "const", "highp", "mediump", "lowp", "precise", "flat", "smooth", "noperspective",
        };

        static readonly Regex GlslFunctionHead = new Regex(@"(?:^|[\s;}])(?:(?:highp|mediump|lowp|precise|inline)\s+)*(\w+)\s+(\w+)\s*\(([^()]*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        static void VerifyGlsl(String file, SourceText st, List<Problem> into)
        {
            var text = st.Text;
            var n = text.Length;
            //  Blank comments (keep the offsets)
            var sb = new StringBuilder(text);
            for (int i = 0; i < n; ++i)
            {
                if ((text[i] == '/') && (i + 1 < n) && (text[i + 1] == '/'))
                {
                    while ((i < n) && (text[i] != '\n'))
                        sb[i++] = ' ';
                }
                else if ((text[i] == '/') && (i + 1 < n) && (text[i + 1] == '*'))
                {
                    var e = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    e = e < 0 ? n : e + 2;
                    for (; i < e; ++i)
                        if (text[i] != '\n')
                            sb[i] = ' ';
                    --i;
                }
            }
            var code = sb.ToString();
            //  Preprocessor branches: a path of (group, branch) pairs; two definitions conflict if one path is a prefix of the other
            var path = new List<(int Group, int Branch)>();
            int groups = 0;
            var defs = new List<(String Sig, (int, int)[] Path, int Offset)>();
            var brackets = new Stack<(char C, int Offset)>();
            int segStart = 0;
            for (int i = 0; i < n; ++i)
            {
                var c = code[i];
                if ((c == '#') && IsLineStart(code, i))
                {
                    var e = code.IndexOf('\n', i);
                    if (e < 0)
                        e = n;
                    var d = code.Substring(i + 1, e - i - 1).Trim();
                    var w = d.Split(new[] { ' ', '\t', '(' }, 2)[0];
                    switch (w)
                    {
                        case "if":
                        case "ifdef":
                        case "ifndef":
                            path.Add((++groups, 0));
                            break;
                        case "elif":
                        case "else":
                            if (path.Count > 0)
                                path[^1] = (path[^1].Group, path[^1].Branch + 1);
                            break;
                        case "endif":
                            if (path.Count > 0)
                                path.RemoveAt(path.Count - 1);
                            break;
                    }
                    i = e;
                    segStart = e;
                    continue;
                }
                switch (c)
                {
                    case '(':
                    case '[':
                        brackets.Push((c, i));
                        break;
                    case '{':
                        if (brackets.Count == 0)
                        {
                            var m = GlslFunctionHead.Match(code.Substring(segStart, i - segStart));
                            if (m.Success && (m.Groups[1].Value != "struct") && (m.Groups[1].Value != "return") && (m.Groups[1].Value != "else"))
                            {
                                var ps = m.Groups[3].Value.Split(',').Select(p =>
                                {
                                    var words = Regex.Replace(p, @"\[[^\]]*\]", " ").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(x => !GlslQualifiers.Contains(x)).ToList();
                                    return words.Count > 0 ? words[0] : "";
                                }).Where(x => (x.Length > 0) && (x != "void")).ToList();
                                defs.Add((m.Groups[2].Value + "(" + String.Join(",", ps) + ")", path.ToArray(), segStart + m.Groups[2].Index));
                            }
                        }
                        brackets.Push((c, i));
                        break;
                    case ')':
                    case ']':
                    case '}':
                        {
                            var open = c == ')' ? '(' : c == ']' ? '[' : '{';
                            if ((brackets.Count == 0) || (brackets.Peek().C != open))
                            {
                                var (l, col) = st.GetLineCol(i);
                                into.Add(new Problem(file, l, col, "build-glsl-brackets", "unbalanced \"" + c + "\""));
                                return;
                            }
                            brackets.Pop();
                            if ((c == '}') && (brackets.Count == 0))
                                segStart = i + 1;
                        }
                        break;
                    case ';':
                        if (brackets.Count == 0)
                            segStart = i + 1;
                        break;
                }
            }
            if (brackets.Count > 0)
            {
                var (l, col) = st.GetLineCol(brackets.Peek().Offset);
                into.Add(new Problem(file, l, col, "build-glsl-brackets", "\"" + brackets.Peek().C + "\" is never closed"));
                return;
            }
            static bool Prefix((int, int)[] a, (int, int)[] b)
            {
                if (a.Length > b.Length)
                    return false;
                for (int i = 0; i < a.Length; ++i)
                    if (a[i] != b[i])
                        return false;
                return true;
            }
            for (int i = 1; i < defs.Count; ++i)
                for (int j = 0; j < i; ++j)
                {
                    if (defs[i].Sig != defs[j].Sig)
                        continue;
                    if (!Prefix(defs[i].Path, defs[j].Path) && !Prefix(defs[j].Path, defs[i].Path))
                        continue;
                    var (l, col) = st.GetLineCol(defs[i].Offset);
                    var (l0, c0) = st.GetLineCol(defs[j].Offset);
                    into.Add(new Problem(file, l, col, "build-glsl-duplicate", "function " + defs[i].Sig + " is defined twice (also at " + l0 + "," + c0 + "): the shader doesn't compile (a shader minifier renamed two functions to the same name, see Compress --no-overloading)"));
                    break;
                }
        }

        static bool IsLineStart(String s, int i)
        {
            for (--i; i >= 0; --i)
            {
                var c = s[i];
                if (c == '\n')
                    return true;
                if ((c != ' ') && (c != '\t') && (c != '\r'))
                    return false;
            }
            return true;
        }
    }
}
