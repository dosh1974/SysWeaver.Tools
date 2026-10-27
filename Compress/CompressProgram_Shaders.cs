

using SysWeaver.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SysWeaver
{

    /// <summary>
    /// Shader optimization (.glsl / .frag).
    /// <para>
    /// A minimized shader must work exactly like the original in Effect Weaver (Effect Saver, Effect Vj, the shader editor), so everything
    /// its shader parser (EffectWeaver.Common ShaderParser) reads from the source is kept (unless RemoveShaderVars is set):
    /// </para>
    /// <list type="bullet">
    /// <item>The tunable variable candidates: "#define NAME literal" (top level or in an "#ifndef NAME" guard) and "const T NAME = literal;" with
    /// a comment, each on its own line with its comment (comment continuation lines are joined, "var: {...}" annotations are compacted).
    /// The candidates are found with the parser's rules (mirrored here), names defined more than once are no candidates.</item>
    /// <item>The iMouse channel comments ("// iMouse.x = Name | Description | Default"), as compact lines at the top.</item>
    /// <item>The entry points (main, mainImage and vertexMain, the optional vertex stage).</item>
    /// </list>
    /// <para>
    /// The shader is stripped: comments and white space removed, short number literals, nothing renamed or evaluated (exactly the same code).
    /// With MinifyShaders it's also preprocessed (comments removed, macros expanded, conditionals evaluated) and minified (shader_minifier), with
    /// the candidates hidden from both and put back afterwards, and that is used if it's smaller. Not possible if a kept define is used by a
    /// preprocessor conditional (directly or through another macro, evaluating it would remove the variable's effect), for a local candidate
    /// const, short candidate names (the minifier's names could clash) or if a candidate is lost on the way. shader_minifier's rewrites can
    /// break a shader (renders black / hangs the GPU), so MinifyShaders results must be verified.
    /// </para>
    /// </summary>
    partial class CompressProgram
    {

#pragma warning disable CS0649

        sealed class VarDef
        {
            public decimal min = decimal.MinValue;
            public decimal max = decimal.MaxValue;
            public decimal step;
            public string type;
            public string desc;
            public string name;
        }

#pragma warning restore CS0649

        #region Effect Weaver shader parser rules (keep in sync with EffectWeaver.Common/Shaders/ShaderParser.cs)

        static readonly Regex DefineRx = new Regex(@"^#\s*define\s+([A-Za-z_]\w*)\b(?!\()(.*)$", RegexOptions.Compiled);
        static readonly Regex ConstRx = new Regex(@"^const\s+(?:(highp|mediump|lowp)\s+)?(float|int|bool|vec2|vec3|vec4)\s+([A-Za-z_]\w*)\s*=\s*([^;]+);", RegexOptions.Compiled);
        static readonly Regex DirectiveRx = new Regex(@"^#\s*(\w+)\s*(.*)$", RegexOptions.Compiled);
        static readonly Regex NumRx = new Regex(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?[fF]?$", RegexOptions.Compiled);
        static readonly Regex VecRx = new Regex(@"^(vec[234])\s*\((.*)\)$", RegexOptions.Compiled);

        /// <summary>
        /// An iMouse channel comment line (comment markers included): "// iMouse.x = Name | Description | Default"
        /// </summary>
        static readonly Regex MouseCommentRx = new Regex(@"^(?://+|/\*+|\*+)?\s*iMouse\s*\.\s*([xyzw])\s*[=:]\s*(.*?)\s*(?:\*/)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The value and the comment ("//" or "/* */") of the text after a declaration
        /// </summary>
        static (String Value, String Comment) SplitComment(String text)
        {
            var ci = text.IndexOf("//", StringComparison.Ordinal);
            var bi = text.IndexOf("/*", StringComparison.Ordinal);
            if ((bi >= 0) && ((ci < 0) || (bi < ci)))
            {
                var be = text.IndexOf("*/", bi + 2, StringComparison.Ordinal);
                var comment = be > 0 ? text.Substring(bi + 2, be - bi - 2) : text.Substring(bi + 2);
                return (text.Substring(0, bi), comment.Trim());
            }
            if (ci < 0)
                return (text, "");
            return (text.Substring(0, ci), text.Substring(ci + 2).Trim());
        }

        /// <summary>
        /// The type of a literal value (number, bool or vecN(numbers)), null if it isn't a literal
        /// </summary>
        static String LiteralType(String text)
        {
            var t = text.Trim();
            while ((t.Length > 1) && (t[0] == '(') && (t[t.Length - 1] == ')'))
                t = t.Substring(1, t.Length - 2).Trim();
            if ((t == "true") || (t == "false"))
                return "bool";
            if (NumRx.IsMatch(t))
                return ((t.IndexOf('.') >= 0) || (t.IndexOfAny(new[] { 'e', 'E' }) >= 0)) ? "float" : "int";
            var m = VecRx.Match(t);
            if (!m.Success)
                return null;
            var vt = m.Groups[1].Value;
            var parts = m.Groups[2].Value.Split(',');
            if ((parts.Length != 1) && (parts.Length != (vt[3] - '0')))
                return null;
            return parts.All(x => NumRx.IsMatch(x.Trim())) ? vt : null;
        }

        /// <summary>
        /// A tunable variable candidate (as found by the parser)
        /// </summary>
        sealed class ShaderVar
        {
            public String Name;
            public bool IsDefine;
            /// <summary>Const: precision qualifier (or null) and type</summary>
            public String Precision;
            public String Type;
            public String Value;
            /// <summary>The comment of the line itself</summary>
            public String LineComment;
            /// <summary>The comment with the continuation lines (as the parser sees it)</summary>
            public String Comment;
            public int CommentCol;
            public int Line;
            /// <summary>The "#ifndef NAME" and "#endif" lines of a guarded define, else -1</summary>
            public int GuardStart = -1;
            public int GuardEnd = -1;
            /// <summary>Const: the brace depth (0 = top level)</summary>
            public int Depth;
            public readonly List<int> Continuations = new List<int>();
        }

        sealed class Guard
        {
            public String Name;
            public int Line;
            public ShaderVar Guarded;
        }

        /// <summary>
        /// A macro the effect defines for the snippets (ex: "#define EW_SDF(p) map(p)", the raymarch snippets read the scene through it).
        /// Snippets are inserted when the effect is built, so these are kept (with the names they use).
        /// </summary>
        sealed class SnippetMacro
        {
            public String Name;
            public int Line;
            /// <summary>The directive without comments</summary>
            public String Text;
            /// <summary>The identifiers of the replacement (not the parameters or fields after a '.')</summary>
            public String[] Ids;
            /// <summary>Top level and on one line (else the shader is only stripped)</summary>
            public bool Simple;
        }

        /// <summary>
        /// A top level conditional using a macro of the compiler (ex: "#if __VERSION__ >= 300"): it differs between the builds (GLSL ES 3.00 / 1.00)
        /// so it must not be evaluated when minimizing. Kept as is if it only contains directives (ex: "#define ZERO min(iFrame, 0)" / "#define ZERO 0").
        /// </summary>
        sealed class TargetBlock
        {
            public int Start;
            public int End = -1;
            public bool DirectivesOnly = true;
            /// <summary>The macros defined in it and the identifiers of their replacements</summary>
            public readonly HashSet<String> Defined = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<String> Ids = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// True for a macro defined by the compiler that depends on the build target (GL_ES is always defined in WebGL)
        /// </summary>
        static bool IsTargetMacro(String name) => (name != "GL_ES") && (name.StartsWith("GL_") || (name == "__VERSION__") || (name == "__LINE__") || (name == "__FILE__"));

        sealed class ShaderScan
        {
            public readonly List<ShaderVar> Vars = new List<ShaderVar>();
            public readonly List<SnippetMacro> SnippetMacros = new List<SnippetMacro>();
            public readonly List<TargetBlock> TargetBlocks = new List<TargetBlock>();
            public readonly Dictionary<String, int> DefineCount = new Dictionary<string, int>(StringComparer.Ordinal);
            /// <summary>Conditional directives (#if, #elif, #ifdef, #ifndef): the line and the identifiers of the condition</summary>
            public readonly List<(int Line, String[] Ids)> Conditionals = new List<(int, String[])>();
            /// <summary>All macros: name => the identifiers of the replacement</summary>
            public readonly Dictionary<String, HashSet<String>> Macros = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            /// <summary>The lines with comments replaced by spaces</summary>
            public String[] Masked;
        }

        static readonly Regex IdentifierRx = new Regex(@"[A-Za-z_]\w*", RegexOptions.Compiled);

        /// <summary>A snippet macro: name, parameters (if function like), replacement</summary>
        static readonly Regex SnippetMacroRx = new Regex(@"^#\s*define\s+(EW_\w+)(\([^)]*\))?(.*)$", RegexOptions.Compiled);

        /// <summary>A macro: name, parameters (if function like), replacement</summary>
        static readonly Regex AnyMacroRx = new Regex(@"^#\s*define\s+([A-Za-z_]\w*)(\([^)]*\))?(.*)$", RegexOptions.Compiled);

        /// <summary>An identifier in a macro replacement (not a field after a '.')</summary>
        static readonly Regex BodyIdRx = new Regex(@"(?<![\w.])[A-Za-z_]\w*", RegexOptions.Compiled);

        /// <summary>A function named like a snippet ("Ew..."), definitions (calls followed by a block may match too, harmless)</summary>
        static readonly Regex EwFunctionRx = new Regex(@"\b(Ew[A-Z]\w*)\s*\([^;{}]*\)\s*\{", RegexOptions.Compiled);

        /// <summary>
        /// The text with all comments replaced by spaces (new lines are kept)
        /// </summary>
        static String MaskComments(String text)
        {
            var a = text.ToCharArray();
            var n = a.Length;
            for (int i = 0; i < n;)
            {
                if ((a[i] == '/') && (i + 1 < n) && (a[i + 1] == '/'))
                {
                    while ((i < n) && (a[i] != '\n'))
                        a[i++] = ' ';
                    continue;
                }
                if ((a[i] == '/') && (i + 1 < n) && (a[i + 1] == '*'))
                {
                    var e = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    e = e < 0 ? n : e + 2;
                    for (; i < e; ++i)
                        if (a[i] != '\n')
                            a[i] = ' ';
                    continue;
                }
                ++i;
            }
            return new String(a);
        }

        /// <summary>
        /// Find the variable candidates like the parser does
        /// </summary>
        static ShaderScan ScanShader(String[] lines)
        {
            var s = new ShaderScan
            {
                Masked = MaskComments(String.Join("\n", lines)).Split('\n'),
            };
            var guards = new Stack<Guard>();
            bool inBlockComment = false;
            ShaderVar last = null;
            int braces = 0;
            //  The current top level conditional and if it uses a compiler macro
            TargetBlock cond = null;
            bool condTarget = false;
            var lc = lines.Length;
            for (int i = 0; i < lc; ++i)
            {
                var raw = lines[i];
                var t = raw.Trim();
                var depth = braces;
                var masked = s.Masked[i];
                if (!masked.TrimStart().StartsWith("#"))
                    braces += masked.Count(x => x == '{') - masked.Count(x => x == '}');
                if (inBlockComment)
                {
                    if (t.Contains("*/"))
                        inBlockComment = false;
                    last = null;
                    continue;
                }
                if (t.StartsWith("/*") && !t.Contains("*/"))
                {
                    inBlockComment = true;
                    last = null;
                    continue;
                }
                //  Comment continuation lines (aligned with the comment of the previous variable)
                if (t.StartsWith("//"))
                {
                    if ((last != null) && (last.CommentCol >= 0))
                    {
                        var col = raw.IndexOf("//", StringComparison.Ordinal);
                        if (col >= last.CommentCol - 2)
                        {
                            last.Comment += " " + t.Substring(2).Trim();
                            last.Continuations.Add(i);
                            continue;
                        }
                    }
                    last = null;
                    continue;
                }
                last = null;
                if (t.StartsWith("#"))
                {
                    var dm = DirectiveRx.Match(t);
                    if (!dm.Success)
                        continue;
                    var directive = dm.Groups[1].Value;
                    var arg = SplitComment(dm.Groups[2].Value).Value.Trim();
                    switch (directive)
                    {
                        case "if":
                        case "elif":
                        case "ifdef":
                        case "ifndef":
                            {
                                var ids = directive.StartsWith("ifn") || directive.StartsWith("ifd") ? new[] { arg } : IdentifierRx.Matches(arg).Select(x => x.Value).Where(x => x != "defined").ToArray();
                                s.Conditionals.Add((i, ids));
                                if ((directive != "elif") && (guards.Count == 0))
                                {
                                    cond = new TargetBlock { Start = i };
                                    condTarget = false;
                                }
                                condTarget |= ids.Any(IsTargetMacro);
                                if (directive != "elif")
                                    guards.Push(new Guard { Name = directive == "ifndef" ? arg : null, Line = i });
                                continue;
                            }
                        case "endif":
                            if (guards.Count > 0)
                            {
                                var g = guards.Pop();
                                if (g.Guarded != null)
                                    g.Guarded.GuardEnd = i;
                                if ((guards.Count == 0) && (cond != null))
                                {
                                    cond.End = i;
                                    if (condTarget)
                                        s.TargetBlocks.Add(cond);
                                    cond = null;
                                }
                            }
                            continue;
                        case "define":
                            break;
                        default:
                            continue;
                    }
                    //  Macros defined in a top level conditional (with the identifiers they use)
                    var cm = cond == null ? null : AnyMacroRx.Match(SplitComment(t).Value.Trim());
                    if ((cm != null) && cm.Success)
                    {
                        var pars = cm.Groups[2].Success ? IdentifierRx.Matches(cm.Groups[2].Value).Select(x => x.Value).ToHashSet(StringComparer.Ordinal) : new HashSet<String>();
                        cond.Defined.Add(cm.Groups[1].Value);
                        cond.Ids.UnionWith(BodyIdRx.Matches(cm.Groups[3].Value).Select(x => x.Value).Where(x => !pars.Contains(x)));
                    }
                    //  Macros for the snippets ("EW_...") defined by the effect
                    var sm = SnippetMacroRx.Match(SplitComment(t).Value.Trim());
                    if (sm.Success)
                    {
                        var pars = sm.Groups[2].Success ? IdentifierRx.Matches(sm.Groups[2].Value).Select(x => x.Value).ToHashSet(StringComparer.Ordinal) : new HashSet<String>();
                        s.SnippetMacros.Add(new SnippetMacro
                        {
                            Name = sm.Groups[1].Value,
                            Line = i,
                            Text = sm.Value,
                            Ids = BodyIdRx.Matches(sm.Groups[3].Value).Select(x => x.Value).Where(x => !pars.Contains(x)).Distinct().ToArray(),
                            Simple = (guards.Count == 0) && !t.EndsWith("\\"),
                        });
                    }
                    var m = DefineRx.Match(t);
                    if (!m.Success)
                    {
                        //  A function like macro (only its identifiers are needed)
                        var fm = Regex.Match(SplitComment(t).Value, @"^#\s*define\s+([A-Za-z_]\w*)(.*)$");
                        if (fm.Success)
                            s.Macros[fm.Groups[1].Value] = IdentifierRx.Matches(fm.Groups[2].Value).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
                        continue;
                    }
                    var name = m.Groups[1].Value;
                    s.DefineCount[name] = s.DefineCount.GetValueOrDefault(name) + 1;
                    var (valueText, comment) = SplitComment(m.Groups[2].Value);
                    s.Macros[name] = IdentifierRx.Matches(valueText).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
                    //  Only top level defines (or the "#ifndef X / #define X" pattern)
                    var gd = guards.Count;
                    if ((gd > 1) || ((gd == 1) && (guards.Peek().Name != name)))
                        continue;
                    if (LiteralType(valueText) == null)
                        continue;
                    var nameEnd = raw.IndexOf(name, raw.IndexOf("define", StringComparison.Ordinal), StringComparison.Ordinal) + name.Length;
                    var v = new ShaderVar
                    {
                        Name = name,
                        IsDefine = true,
                        Value = valueText.Trim(),
                        LineComment = comment,
                        Comment = comment,
                        CommentCol = comment.Length > 0 ? raw.IndexOf("//", nameEnd, StringComparison.Ordinal) : -1,
                        Line = i,
                    };
                    if (gd == 1)
                    {
                        var g = guards.Peek();
                        v.GuardStart = g.Line;
                        g.Guarded = v;
                    }
                    s.Vars.Add(v);
                    last = v;
                    continue;
                }
                if ((cond != null) && (masked.Trim().Length > 0))
                    cond.DirectivesOnly = false;
                if ((guards.Count == 0) && t.StartsWith("const"))
                {
                    var m = ConstRx.Match(t);
                    if (!m.Success)
                        continue;
                    var glslType = m.Groups[2].Value;
                    var name = m.Groups[3].Value;
                    var valueText = m.Groups[4].Value.Trim();
                    s.DefineCount[name] = s.DefineCount.GetValueOrDefault(name) + 1;
                    //  Must be a single vector constructor, not multiple declarations
                    if (valueText.Contains(',') && !valueText.StartsWith("vec"))
                        continue;
                    //  GLSL ES 1.0 has no implicit conversions
                    if (LiteralType(valueText) != glslType)
                        continue;
                    var (_, comment) = SplitComment(t.Substring(m.Index + m.Length));
                    var eq = raw.IndexOf('=', raw.IndexOf(name, StringComparison.Ordinal));
                    var v = new ShaderVar
                    {
                        Name = name,
                        Precision = m.Groups[1].Success ? m.Groups[1].Value : null,
                        Type = glslType,
                        Value = valueText,
                        LineComment = comment,
                        Comment = comment,
                        CommentCol = comment.Length > 0 ? raw.IndexOf("//", eq, StringComparison.Ordinal) : -1,
                        Line = i,
                        Depth = depth,
                    };
                    s.Vars.Add(v);
                    last = v;
                }
            }
            return s;
        }

        /// <summary>
        /// The iMouse channel comments of a shader as compact comment lines ("//iMouse.x=Name|Description|Default", x, y, z, w order),
        /// the first comment of a channel is used (like the parser).
        /// </summary>
        static List<String> GetMouseComments(String[] lines)
        {
            var found = new Dictionary<char, String>();
            bool inBlockComment = false;
            foreach (var line in lines)
            {
                var t = line.Trim();
                if ((inBlockComment || t.StartsWith("//") || t.StartsWith("/*")) && (t.IndexOf("iMouse", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var m = MouseCommentRx.Match(t);
                    if (m.Success)
                        found.TryAdd(Char.ToLowerInvariant(m.Groups[1].Value[0]), String.Join("|", m.Groups[2].Value.Split('|').Select(x => x.Trim())));
                }
                if (inBlockComment)
                {
                    if (t.Contains("*/"))
                        inBlockComment = false;
                    continue;
                }
                if (t.StartsWith("/*") && !t.Contains("*/"))
                    inBlockComment = true;
            }
            var res = new List<String>(found.Count);
            foreach (var c in "xyzw")
                if (found.TryGetValue(c, out var text))
                    res.Add(String.Concat("//iMouse.", c, "=", text));
            return res;
        }

        #endregion

        /// <summary>
        /// A "var: {...}" annotation with the keys the parser reads (name, desc, type, min, max, step) in compact form, null if it can't be parsed
        /// </summary>
        static String CompactAnnotation(String comment)
        {
            try
            {
                var ser = NewtonsoftJsonSerializer.Instance;
                var json = comment.Substring(4).Trim();
                var desc = json.Length > 0 ? ser.FromString<VarDef>(json) : new VarDef();
                var nl = new StringBuilder("var:{");
                var l = nl.Length;
                void app<T>(String key, T value)
                {
                    if (nl.Length > l)
                        nl.Append(',');
                    nl.Append(key);
                    var v = ser.ToString(value);
                    if (typeof(T) != typeof(String))
                        if (v.EndsWith(".0"))
                            v = v.Substring(0, v.Length - 2);
                    nl.Append(v);
                }
                if (desc.min != Decimal.MinValue)
                    app("min:", desc.min);
                if (desc.max != Decimal.MaxValue)
                    app("max:", desc.max);
                if (desc.step > 0)
                    app("step:", desc.step);
                if (!String.IsNullOrEmpty(desc.type))
                    app("type:", desc.type);
                if (!String.IsNullOrEmpty(desc.name))
                    app("name:", desc.name);
                if (!String.IsNullOrEmpty(desc.desc))
                    app("desc:", desc.desc);
                nl.Append('}');
                return nl.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The line(s) of a kept variable: the declaration with its comment (an annotation compacted, the parser reads it from the line's own comment,
        /// else the comment with the continuation lines joined), a guarded define with its guard
        /// </summary>
        static IEnumerable<String> VarLines(ShaderVar v)
        {
            var comment = v.Comment;
            if (v.LineComment.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
                comment = CompactAnnotation(v.LineComment) ?? v.LineComment;
            var code = v.IsDefine
                ? String.Concat("#define ", v.Name, " ", v.Value)
                : String.Concat("const ", v.Precision == null ? "" : v.Precision + " ", v.Type, " ", v.Name, "=", v.Value, ";");
            if (comment.Length > 0)
                code = code + "//" + comment;
            if (v.GuardStart < 0)
                return new[] { code };
            return new[] { "#ifndef " + v.Name, code, "#endif" };
        }

        static readonly Regex WhiteSpaceRx = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// White space around these can be removed without joining tokens (not + and -: "a - -b")
        /// </summary>
        static readonly Regex TightRx = new Regex(@"\s*([(){}\[\];,=?:*/<>&|!%^~])\s*", RegexOptions.Compiled);

        /// <summary>
        /// Strip a shader: comments and white space removed, the kept variables written like when minimizing, nothing renamed or evaluated
        /// </summary>
        static List<String> StripShader(String[] lines, ShaderScan scan, Dictionary<String, ShaderVar> keep)
        {
            var varAt = new Dictionary<int, ShaderVar>();
            var skip = new HashSet<int>();
            foreach (var v in keep.Values)
            {
                varAt[v.Line] = v;
                skip.UnionWith(v.Continuations);
                if (v.GuardStart >= 0)
                {
                    skip.Add(v.GuardStart);
                    skip.Add(v.GuardEnd);
                }
            }
            var res = new List<String>(lines.Length);
            bool continued = false;
            //  Code lines inside a function body are joined (top level declarations, directives and variables stay on their own lines)
            bool join = false;
            int depth = 0;
            var masked = scan.Masked;
            for (int i = 0; i < lines.Length; ++i)
            {
                if (varAt.TryGetValue(i, out var v))
                {
                    res.AddRange(VarLines(v));
                    continued = false;
                    join = false;
                    continue;
                }
                if (skip.Contains(i))
                    continue;
                var t = WhiteSpaceRx.Replace(masked[i].Trim(), " ");
                if (t.Length == 0)
                {
                    if (continued)
                        res.Add("");
                    continued = false;
                    continue;
                }
                //  Directives (and their continuation lines) only lose white space: "#define A (1.0)" is not "#define A(1.0)"
                var directive = continued || t.StartsWith("#");
                continued = directive && t.EndsWith("\\");
                if (directive)
                {
                    res.Add(t);
                    join = false;
                    continue;
                }
                t = NumberRx.Replace(TightRx.Replace(t, "$1"), ShortNumber);
                if (join && (res.Count > 0))
                {
                    var p = res[res.Count - 1];
                    res[res.Count - 1] = String.Concat(p, NeedSpace(p[p.Length - 1], t[0]) ? " " : "", t);
                }
                else
                {
                    res.Add(t);
                }
                depth += t.Count(x => x == '{') - t.Count(x => x == '}');
                join = depth > 0;
            }
            return res;
        }

        /// <summary>A float literal (not part of an identifier, a swizzle or an exponent / suffix form)</summary>
        static readonly Regex NumberRx = new Regex(@"(?<![\w.])(\d+)\.(\d*)(?![\w.])", RegexOptions.Compiled);

        /// <summary>The shortest form of a float literal: "1.0" => "1.", "0.50" => ".5"</summary>
        static String ShortNumber(Match m)
        {
            var i = m.Groups[1].Value.TrimStart('0');
            var f = m.Groups[2].Value.TrimEnd('0');
            if ((i.Length == 0) && (f.Length == 0))
                return "0.";
            return String.Concat(i, ".", f);
        }

        static bool IsWordChar(char c) => Char.IsLetterOrDigit(c) || (c == '_') || (c == '.');

        static bool IsOperatorChar(char c) => "+-*/%<>=!&|^".IndexOf(c) >= 0;

        /// <summary>True if two tokens would join without a space between them ("a b", "a- -b", "a/ *b")</summary>
        static bool NeedSpace(char a, char b) => (IsWordChar(a) && IsWordChar(b)) || (IsOperatorChar(a) && IsOperatorChar(b));

        static int ExtPreProcessShader(String source, String dest)
        {
            return ExternalProcess.Run(ToolPreProcessorShaders, source.ToQuoted() + " " + ("-o" + dest).ToQuoted() + " -DGL_ES");
        }

        static int OptimizeShaders(String source, String dest, CompressParams p)
        {
            String temp1 = dest + "_temp";
            String temp2 = dest + "_temp2";
            try
            {
                var lines = File.ReadAllLines(source);
                var keepVars = !p.RemoveShaderVars;
                //  The iMouse channel comments (names, descriptions and defaults of the controls), the preprocessor removes all comments
                var header = keepVars ? GetMouseComments(lines) : new List<String>();
                var scan = ScanShader(lines);
                var keep = new Dictionary<String, ShaderVar>(StringComparer.Ordinal);
                if (keepVars)
                    foreach (var v in scan.Vars)
                        if (scan.DefineCount.GetValueOrDefault(v.Name) == 1)
                            keep[v.Name] = v;
                //  Conditionals using compiler macros (differ between the builds) are kept as is if they only contain directives, else the shader is stripped
                var blocks = scan.TargetBlocks.Where(x => x.DirectivesOnly && (x.End >= 0)).ToList();
                var targetCode = blocks.Count != scan.TargetBlocks.Count;
                //  Macros that depend on a kept define (or on one defined in a kept conditional): a conditional using one of them must not be evaluated
                var tainted = keep.Values.Where(x => x.IsDefine).Select(x => x.Name).Concat(blocks.SelectMany(x => x.Defined)).ToHashSet(StringComparer.Ordinal);
                for (bool changed = true; changed;)
                {
                    changed = false;
                    foreach (var (name, ids) in scan.Macros)
                        if (!tainted.Contains(name) && ids.Overlaps(tainted))
                            changed = tainted.Add(name);
                }
                var strip = targetCode ||
                    keep.Values.Any(x => (!x.IsDefine) && (x.Depth > 0)) ||
                    keep.Keys.Any(x => x.Length <= 2) ||
                    scan.Conditionals.Any(c => c.Ids.Any(id => tainted.Contains(id) && !(keep.TryGetValue(id, out var g) && (g.GuardStart == c.Line))));
                //  The snippets are inserted when the effect is built: their macros (ex: EW_SDF) and the effect's own functions named like snippets
                //  (renamed, the library would insert its version) are kept, with the functions the macros use
                //  (snippet macros in a kept conditional are kept with it)
                var macros = scan.SnippetMacros.Where(x => !keep.ContainsKey(x.Name) && !blocks.Any(b => (x.Line > b.Start) && (x.Line < b.End))).ToList();
                var masked = String.Join("\n", scan.Masked);
                var keepNames = EwFunctionRx.Matches(masked).Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                strip |= macros.Any(x => !x.Simple && !scan.TargetBlocks.Any(b => (x.Line > b.Start) && (x.Line < b.End)));
                foreach (var ids in macros.Select(x => (IEnumerable<String>)x.Ids).Concat(blocks.Select(x => (IEnumerable<String>)x.Ids)))
                {
                    foreach (var id in ids)
                    {
                        if (keep.ContainsKey(id))
                            continue;
                        var e = Regex.Escape(id);
                        if (Regex.IsMatch(masked, @"\b" + e + @"\s*\([^;{}]*\)\s*\{"))
                            keepNames.Add(id);
                        //  A variable or type of the effect would be renamed (the minifier keeps functions only)
                        else if (Regex.IsMatch(masked, @"\b(?:float|int|uint|bool|[biu]?vec[234]|mat[234](?:x[234])?|struct)\s+" + e + @"\b(?!\s*\()"))
                            strip = true;
                    }
                }
                //  Stripped (always possible), with MinifyShaders the smallest of that and minimized with / without aggressive inlining (it can grow a shader),
                //  never larger than the source (ex: an already minimized one)
                static long Size(IEnumerable<String> l) => l.Sum(x => x.Length + 1L);
                var res = StripShader(lines, scan, keep);
                if (p.MinifyShaders && !strip)
                {
                    //  The kept conditionals (directives without comments)
                    var blockLines = new HashSet<int>();
                    var blockText = new List<String>();
                    foreach (var b in blocks)
                    {
                        for (int i = b.Start; i <= b.End; ++i)
                        {
                            blockLines.Add(i);
                            var t = WhiteSpaceRx.Replace(scan.Masked[i].Trim(), " ");
                            if (t.Length > 0)
                                blockText.Add(t);
                        }
                    }
                    foreach (var aggressive in new[] { true, false })
                    {
                        var m = MinimizeShader(lines, keep, macros, blockLines, blockText, keepNames, temp1, temp2, aggressive);
                        if ((m != null) && (Size(m) < Size(res)))
                            res = m;
                    }
                }
                res = header.Concat(res).ToList();
                if (Size(res) >= Size(lines))
                    File.Copy(source, dest, true);
                else
                    File.WriteAllLines(dest, res);
                return 0;
            }
            finally
            {
                try
                {
                    File.Delete(temp2);
                }
                catch
                {
                }
                try
                {
                    File.Delete(temp1);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Preprocess and minify a shader, the kept variables hidden from both and put back afterwards.
        /// Returns null if a kept variable got lost (or the preprocessor failed), then the shader should be stripped instead.
        /// </summary>
        static List<String> MinimizeShader(String[] lines, Dictionary<String, ShaderVar> keep, List<SnippetMacro> macros, HashSet<int> blockLines, List<String> blockText,
            HashSet<String> keepNames, String temp1, String temp2, bool aggressive)
        {
            //  Kept defines are removed: the preprocessor leaves their names alone (they are added again for the minifier and put back afterwards),
            //  the snippet macros and the kept conditionals are put back after the variables
            var src = (String[])lines.Clone();
            foreach (var i in blockLines)
                src[i] = "";
            foreach (var v in keep.Values.Where(x => x.IsDefine))
            {
                src[v.Line] = "";
                if (v.GuardStart >= 0)
                {
                    src[v.GuardStart] = "";
                    src[v.GuardEnd] = "";
                }
            }
            foreach (var m in macros)
                src[m.Line] = "";
            File.WriteAllLines(temp1, src);
            if (ExtPreProcessShader(temp1, temp2) != 0)
                return null;
            var pp = File.ReadAllLines(temp2).Select(x => x.CodeSanitize()).ToList();
            //  Kept consts become macros: the minifier doesn't rename, inline or remove them
            int braces = 0;
            for (int i = 0; i < pp.Count; ++i)
            {
                var t = pp[i].Trim();
                if (braces == 0)
                {
                    var m = ConstRx.Match(t);
                    if (m.Success && keep.TryGetValue(m.Groups[3].Value, out var v) && !v.IsDefine)
                        pp[i] = String.Concat("#define ", v.Name, " ", v.Value);
                }
                if (!t.StartsWith("#"))
                    braces += t.Count(x => x == '{') - t.Count(x => x == '}');
            }
            pp.InsertRange(0, keep.Values.Where(x => x.IsDefine).Select(x => String.Concat("#define ", x.Name, " ", x.Value)));
            File.WriteAllLines(temp1, pp);
            File.Delete(temp2);
            var r = ExtMinimizeShader(temp1, temp2, keepNames, aggressive);
            //  The minifier can't handle every shader (ex: a struct field named like a vector field "s") and may exit with 0 anyway,
            //  then the preprocessed shader is used (no comments or inactive code, still much smaller than the original)
            var min = File.Exists(temp2) ? File.ReadAllLines(temp2) : null;
            var res = ((r == 0) && (min != null) && min.Any(x => x.Length > 0)) ? min.ToList() : pp;
            //  Put the variables back, all of them at the start in the original order (the order of the settings): literal values, no dependencies.
            //  After the leading #version / #extension lines (they must come first in GLSL ES 1.00)
            var found = new HashSet<String>(StringComparer.Ordinal);
            var outL = new List<String>(res.Count + keep.Count * 3);
            int at = 0;
            foreach (var x in res)
            {
                if (x.StartsWith("#define "))
                {
                    var s = x.IndexOf(' ', 8);
                    var name = s < 0 ? x.Substring(8) : x.Substring(8, s - 8);
                    if (keep.ContainsKey(name) && found.Add(name))
                        continue;
                }
                if ((at == outL.Count) && (x.StartsWith("#version") || x.StartsWith("#extension")))
                    ++at;
                outL.Add(x);
            }
            if (found.Count != keep.Count)
                return null;
            //  (split before the variables are inserted: their comments must stay on their lines)
            outL = SplitDeclarations(outL);
            outL.InsertRange(at, keep.Values.OrderBy(x => x.Line).SelectMany(VarLines).Concat(macros.Select(x => x.Text)).Concat(blockText));
            return outL;
        }

        /// <summary>
        /// One top level declaration per line: snippets are inserted (at build time) right before the line of the first declaration using them,
        /// a snippet calling one of the effect's functions through a macro (EW_SDF) must still come after that function
        /// </summary>
        static List<String> SplitDeclarations(List<String> lines)
        {
            var res = new List<String>(lines.Count * 4);
            var bodies = new Stack<bool>();
            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                {
                    res.Add(line);
                    continue;
                }
                int start = 0;
                char prev = ' ';
                for (int i = 0; i < line.Length; ++i)
                {
                    var c = line[i];
                    bool split = false;
                    if (c == '{')
                        bodies.Push(prev == ')');
                    else if (c == '}')
                        split = (bodies.Count > 0) && bodies.Pop() && (bodies.Count == 0);
                    else if (c == ';')
                        split = bodies.Count == 0;
                    if (!Char.IsWhiteSpace(c))
                        prev = c;
                    if (!split)
                        continue;
                    res.Add(line.Substring(start, i + 1 - start).Trim());
                    start = i + 1;
                }
                var rest = line.Substring(start).Trim();
                if (rest.Length > 0)
                    res.Add(rest);
            }
            return res;
        }

    }

}


