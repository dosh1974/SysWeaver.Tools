using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Lexing;

namespace SwWeb.Migrating
{
    /// <summary>
    /// Scope-light declaration analysis of a JavaScript token range: which names are declared (function, class, var, let, const,
    /// parameters, catch bindings, destructuring patterns), which of them at the top level (page globals), and which identifier
    /// tokens are declaration names, method names or object keys (not uses of a global).
    /// </summary>
    public sealed class JsScope
    {
        /// <summary>
        /// The text.
        /// </summary>
        public readonly String Text;
        /// <summary>
        /// The significant tokens.
        /// </summary>
        public readonly List<JsToken> Sig;
        /// <summary>
        /// True if a line terminator precedes the significant token (whitespace or comment with a new line).
        /// </summary>
        public readonly bool[] NewlineBefore;
        /// <summary>
        /// The index of the matching bracket for each bracket token (-1 = none).
        /// </summary>
        public readonly int[] Match;
        /// <summary>
        /// The index of the innermost open bracket containing each token (-1 = none).
        /// </summary>
        public readonly int[] Parent;
        /// <summary>
        /// For "{" tokens: 'c' = class body, 'o' = object literal (expression context), 'b' = block / other.
        /// </summary>
        public readonly char[] BraceKind;
        /// <summary>
        /// All declared names with the offset of the first declaration.
        /// </summary>
        public readonly Dictionary<String, int> Declared = new Dictionary<String, int>(StringComparer.Ordinal);
        /// <summary>
        /// Names declared at the top level (outside any bracket): page globals.
        /// </summary>
        public readonly HashSet<String> TopLevel = new HashSet<String>(StringComparer.Ordinal);
        /// <summary>
        /// Indices of tokens that declare a name or are method names (not uses).
        /// </summary>
        public readonly HashSet<int> NotUses = new HashSet<int>();
        /// <summary>
        /// The scope of every local (not top level) declaration: name to the significant token index ranges (inclusive) where the
        /// local declaration shadows a global of the same name. Ranges are conservative (never larger than the real JavaScript scope).
        /// </summary>
        public readonly Dictionary<String, List<(int From, int To)>> LocalScopes = new Dictionary<String, List<(int From, int To)>>(StringComparer.Ordinal);

        /// <summary>
        /// Test if a name is a local declaration at a significant token index (the use refers to the local, not to a global).
        /// </summary>
        /// <param name="name">The name</param>
        /// <param name="i">The significant token index of the use</param>
        /// <returns>True if a local declaration of that name is in scope at the token</returns>
        public bool IsLocalAt(String name, int i)
        {
            if (!LocalScopes.TryGetValue(name, out var l))
                return false;
            foreach (var (from, to) in l)
                if ((i >= from) && (i <= to))
                    return true;
            return false;
        }

        static readonly HashSet<String> NonMethodKeywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "catch", "function", "with", "return", "typeof", "new", "await", "yield", "do", "else", "in", "of", "instanceof", "void", "delete", "throw", "case",
        };

        static readonly HashSet<String> ExpressionBefore = new HashSet<String>(StringComparer.Ordinal)
        {
            "=", "(", ",", ":", "[", "?", "=>", "||", "&&", "??", "return", "+", "-", "*", "/", "%", "!", "==", "===", "!=", "!==", "<", ">", "<=", ">=", "||=", "&&=", "??=", "+=", "-=", "typeof", "yield", "await", "new", "in", "of", "case", "throw", "...", "void", "delete",
        };

        /// <summary>
        /// Analyze tokens.
        /// </summary>
        /// <param name="text">The source text</param>
        /// <param name="tokens">All tokens (from <see cref="JsLexer.Tokenize"/>)</param>
        public JsScope(String text, List<JsToken> tokens)
        {
            Text = text;
            Sig = new List<JsToken>();
            var nl = new List<bool>();
            bool pendingNl = false;
            foreach (var t in tokens)
            {
                if (!t.IsSignificant)
                {
                    if (t.HasNewline || (t.Kind == JsTokenKind.LineComment) || (t.Kind == JsTokenKind.HtmlComment))
                        pendingNl = true;
                    continue;
                }
                Sig.Add(t);
                nl.Add(pendingNl);
                pendingNl = false;
            }
            NewlineBefore = nl.ToArray();
            int n = Sig.Count;
            Match = new int[n];
            Parent = new int[n];
            BraceKind = new char[n];
            var stack = new Stack<int>();
            int pendingClass = -1; // paren depth (stack count) where a class header started
            for (int i = 0; i < n; ++i)
            {
                Match[i] = -1;
                Parent[i] = stack.Count > 0 ? stack.Peek() : -1;
                var t = Sig[i];
                if (t.Kind == JsTokenKind.Identifier && t.Is(text, "class") && !IsPunct(i - 1, "."))
                    pendingClass = stack.Count;
                if (t.Kind == JsTokenKind.TemplateHead)
                {
                    stack.Push(i);
                    continue;
                }
                if (t.Kind == JsTokenKind.TemplateMiddle)
                    continue;
                if (t.Kind == JsTokenKind.TemplateTail)
                {
                    if (stack.Count > 0 && Sig[stack.Peek()].Kind == JsTokenKind.TemplateHead)
                    {
                        var o = stack.Pop();
                        Match[o] = i;
                        Match[i] = o;
                        Parent[i] = stack.Count > 0 ? stack.Peek() : -1;
                    }
                    continue;
                }
                if (t.Kind != JsTokenKind.Punctuator)
                    continue;
                var c = text[t.Start];
                if ((t.Length == 1) && ((c == '(') || (c == '[') || (c == '{')))
                {
                    if (c == '{')
                    {
                        if ((pendingClass >= 0) && (pendingClass == stack.Count))
                        {
                            BraceKind[i] = 'c';
                            pendingClass = -1;
                        }
                        else
                            BraceKind[i] = IsObjectContext(i) ? 'o' : 'b';
                    }
                    stack.Push(i);
                }
                else if ((t.Length == 1) && ((c == ')') || (c == ']') || (c == '}')))
                {
                    if (stack.Count > 0 && Sig[stack.Peek()].Kind == JsTokenKind.Punctuator)
                    {
                        var o = stack.Pop();
                        Match[o] = i;
                        Match[i] = o;
                        Parent[i] = stack.Count > 0 ? stack.Peek() : -1;
                    }
                }
            }
            CollectDeclarations();
        }

        bool IsObjectContext(int i)
        {
            if (i == 0)
                return false;
            var p = Sig[i - 1];
            if ((p.Kind == JsTokenKind.Punctuator) || (p.Kind == JsTokenKind.Identifier))
                return ExpressionBefore.Contains(p.GetText(Text));
            return p.Kind == JsTokenKind.TemplateHead || p.Kind == JsTokenKind.TemplateMiddle;
        }

        /// <summary>
        /// Test if a significant token is a punctuator with the given text.
        /// </summary>
        public bool IsPunct(int i, String p) => (i >= 0) && (i < Sig.Count) && (Sig[i].Kind == JsTokenKind.Punctuator) && Sig[i].Is(Text, p);

        /// <summary>
        /// Test if a significant token is an identifier with the given text.
        /// </summary>
        public bool IsIdent(int i, String p) => (i >= 0) && (i < Sig.Count) && (Sig[i].Kind == JsTokenKind.Identifier) && Sig[i].Is(Text, p);

        /// <summary>
        /// Test if a significant token is an identifier.
        /// </summary>
        public bool IsIdent(int i) => (i >= 0) && (i < Sig.Count) && (Sig[i].Kind == JsTokenKind.Identifier);

        /// <summary>
        /// Get the text of a significant token ("" if out of range).
        /// </summary>
        public String TextOf(int i) => (i >= 0) && (i < Sig.Count) ? Sig[i].GetText(Text) : "";

        /// <summary>The scope range of the declarations being parsed (used by <see cref="Declare"/> for local names).</summary>
        (int From, int To) CurScope;

        /// <summary>True when <see cref="CurScope"/> is the exact JavaScript scope of the declarations being parsed (let, const, class,
        /// parameters, catch bindings, a function expression name); false when the real scope may be larger (var, function declarations).</summary>
        bool CurExact;

        /// <summary>
        /// Names with at least one local declaration whose real scope may be larger than its range (var, function declarations).
        /// </summary>
        public readonly HashSet<String> InexactLocals = new HashSet<String>(StringComparer.Ordinal);

        /// <summary>
        /// True if every declaration of a name is local with an exact scope (let, const, class, parameters, catch bindings): a use outside
        /// all of these scopes is none of them, so it is the global.
        /// </summary>
        /// <param name="name">The name</param>
        /// <returns>True if the name only has exactly scoped local declarations</returns>
        public bool OnlyExactLocals(String name) => LocalScopes.ContainsKey(name) && !InexactLocals.Contains(name) && !TopLevel.Contains(name);

        void Declare(int i, bool top)
        {
            var name = Sig[i].GetText(Text);
            NotUses.Add(i);
            Declared.TryAdd(name, Sig[i].Start);
            if (top)
            {
                TopLevel.Add(name);
                return;
            }
            if (!CurExact)
                InexactLocals.Add(name);
            if (!LocalScopes.TryGetValue(name, out var l))
                LocalScopes[name] = l = new List<(int From, int To)>();
            l.Add(CurScope);
        }

        /// <summary>
        /// The scope of a block scoped declaration at token i: the innermost enclosing bracket (a "for (...)" header extends to the loop body).
        /// A "var" is function scoped, so this is smaller than (or equal to) its real scope: uses after the block are still reported.
        /// </summary>
        (int From, int To) BlockScope(int i)
        {
            int n = Sig.Count;
            var p = Parent[i];
            if (p < 0)
                return (0, n - 1);
            var end = Match[p] >= 0 ? Match[p] : n - 1;
            if (IsPunct(p, "(") && IsIdent(p - 1, "for") && (Match[p] >= 0))
                end = BodyEnd(Match[p] + 1);
            return (p, end);
        }

        /// <summary>
        /// The last token of a body / statement starting at token j: a "{...}" block, else an expression / statement (up to "," ";" or a closing bracket).
        /// </summary>
        int BodyEnd(int j)
        {
            int n = Sig.Count;
            if (j >= n)
                return n - 1;
            if (IsPunct(j, "{"))
                return Match[j] >= 0 ? Match[j] : n - 1;
            SkipInitializer(ref j);
            return Math.Min(j, n - 1);
        }

        /// <summary>
        /// The end of a function body after its parameter list closing parenthesis (functions, methods, arrow functions, catch clauses).
        /// </summary>
        int FunctionEnd(int close)
        {
            if (close < 0)
                return Sig.Count - 1;
            var j = close + 1;
            if (IsPunct(j, "=>"))
                ++j;
            return BodyEnd(j);
        }

        bool IsTop(int i) => Parent[i] < 0;

        void CollectDeclarations()
        {
            int n = Sig.Count;
            for (int i = 0; i < n; ++i)
            {
                var t = Sig[i];
                if (t.Kind == JsTokenKind.Identifier)
                {
                    if (IsPunct(i - 1, ".") || IsPunct(i - 1, "?."))
                        continue;
                    var s = t.GetText(Text);
                    switch (s)
                    {
                        case "var":
                        case "let":
                        case "const":
                            {
                                bool top = IsTop(i);
                                int j = i + 1;
                                if (!(IsIdent(j) || IsPunct(j, "{") || IsPunct(j, "[")))
                                    break;
                                CurScope = BlockScope(i);
                                CurExact = s != "var";
                                for (; ; )
                                {
                                    ParsePattern(ref j, top);
                                    if (IsPunct(j, "="))
                                    {
                                        ++j;
                                        SkipInitializer(ref j);
                                    }
                                    if (IsPunct(j, ","))
                                    {
                                        ++j;
                                        continue;
                                    }
                                    break;
                                }
                                break;
                            }
                        case "function":
                            {
                                int j = i + 1;
                                if (IsPunct(j, "*"))
                                    ++j;
                                if (IsIdent(j))
                                {
                                    // a function expression name is local, a declaration at the top level is a page global
                                    bool expr = IsObjectContext(i);
                                    // a function expression name is only visible inside the function
                                    var pj = IsPunct(j + 1, "(") ? j + 1 : -1;
                                    CurScope = expr ? (i, FunctionEnd(pj >= 0 ? Match[pj] : -1)) : BlockScope(i);
                                    CurExact = expr;
                                    Declare(j, IsTop(i) && !expr);
                                    ++j;
                                }
                                if (IsPunct(j, "("))
                                    ParseParams(j);
                                break;
                            }
                        case "class":
                            if (IsIdent(i + 1) && !IsIdent(i + 1, "extends"))
                            {
                                CurScope = BlockScope(i);
                                CurExact = true;
                                Declare(i + 1, IsTop(i) && !IsObjectContext(i));
                            }
                            break;
                        case "catch":
                            if (IsPunct(i + 1, "("))
                                ParseParams(i + 1);
                            break;
                        default:
                            if (IsPunct(i + 1, "=>"))
                            {
                                CurScope = (i, BodyEnd(i + 2));
                                CurExact = true;
                                Declare(i, false);
                                break;
                            }
                            // method definition: name(params) { in a class body or object literal
                            if (IsPunct(i + 1, "(") && !NonMethodKeywords.Contains(s))
                            {
                                var close = Match[i + 1];
                                if ((close > 0) && IsPunct(close + 1, "{") && IsMethodPosition(i))
                                {
                                    NotUses.Add(i);
                                    ParseParams(i + 1);
                                }
                            }
                            break;
                    }
                }
                else if (IsPunct(i, ")") && IsPunct(i + 1, "=>") && (Match[i] >= 0))
                    ParseParams(Match[i]);
            }
        }

        bool IsMethodPosition(int i)
        {
            int p = i - 1;
            while (IsIdent(p, "static") || IsIdent(p, "async") || IsIdent(p, "get") || IsIdent(p, "set") || IsPunct(p, "*"))
                --p;
            var parent = Parent[i];
            if (parent < 0 || !IsPunct(parent, "{"))
                return false;
            var kind = BraceKind[parent];
            if (IsPunct(p, "{") || IsPunct(p, ","))
                return (kind == 'o') || (kind == 'c');
            if (IsPunct(p, "}") || IsPunct(p, ";"))
                return kind == 'c';
            return false;
        }

        void ParseParams(int open)
        {
            var close = Match[open];
            if (close < 0)
                return;
            CurScope = (open, FunctionEnd(close));
            CurExact = true;
            int j = open + 1;
            while (j < close)
            {
                if (IsPunct(j, ",") || IsPunct(j, "..."))
                {
                    ++j;
                    continue;
                }
                int before = j;
                ParsePattern(ref j, false);
                if (IsPunct(j, "="))
                {
                    ++j;
                    SkipExpression(ref j, close);
                }
                if (j == before)
                    ++j;
            }
        }

        /// <summary>
        /// Parse a binding pattern (identifier, object or array pattern) and declare its names.
        /// </summary>
        void ParsePattern(ref int j, bool top)
        {
            int n = Sig.Count;
            if (j >= n)
                return;
            if (IsIdent(j))
            {
                Declare(j, top);
                ++j;
                return;
            }
            bool obj = IsPunct(j, "{");
            if (!obj && !IsPunct(j, "["))
                return;
            var close = Match[j];
            if (close < 0)
            {
                ++j;
                return;
            }
            ++j;
            while (j < close)
            {
                if (IsPunct(j, ",") || IsPunct(j, "..."))
                {
                    ++j;
                    continue;
                }
                if (IsPunct(j, "{") || IsPunct(j, "["))
                {
                    if (obj && IsPunct(j, "[") && (Match[j] > 0) && IsPunct(Match[j] + 1, ":"))
                    {
                        // computed key: [expr]: pattern
                        j = Match[j] + 2;
                        ParsePattern(ref j, top);
                    }
                    else
                        ParsePattern(ref j, top);
                }
                else if (obj && IsPunct(j + 1, ":"))
                {
                    // key: pattern
                    NotUses.Add(j);
                    j += 2;
                    ParsePattern(ref j, top);
                }
                else if (IsIdent(j))
                {
                    Declare(j, top);
                    ++j;
                }
                else
                    ++j;
                if (IsPunct(j, "="))
                {
                    ++j;
                    SkipExpression(ref j, close);
                }
            }
            j = close + 1;
        }

        static bool IsOpen(String s) => s == "(" || s == "[" || s == "{";

        /// <summary>
        /// Skip an expression up to a "," at depth 0 or the given limit.
        /// </summary>
        void SkipExpression(ref int j, int limit)
        {
            while (j < limit)
            {
                if (IsPunct(j, ","))
                    return;
                var m = Match[j];
                if ((m > j) && ((Sig[j].Kind == JsTokenKind.Punctuator && IsOpen(TextOf(j))) || Sig[j].Kind == JsTokenKind.TemplateHead))
                {
                    j = m + 1;
                    continue;
                }
                ++j;
            }
        }

        /// <summary>
        /// Skip a declarator initializer: up to a "," or ";" at depth 0, a closing bracket, or a new statement after a line break (ASI).
        /// </summary>
        void SkipInitializer(ref int j)
        {
            int n = Sig.Count;
            int start = j;
            while (j < n)
            {
                var t = Sig[j];
                if (t.Kind == JsTokenKind.Punctuator)
                {
                    var s = TextOf(j);
                    if (s == "," || s == ";" || s == ")" || s == "]" || s == "}")
                        return;
                    var m = Match[j];
                    if ((m > j) && IsOpen(s))
                    {
                        j = m + 1;
                        continue;
                    }
                }
                else if ((t.Kind == JsTokenKind.TemplateHead) && (Match[j] > j))
                {
                    j = Match[j] + 1;
                    continue;
                }
                if ((j > start) && NewlineBefore[j] && (t.Kind == JsTokenKind.Identifier) && EndsExpression(j - 1) && !IsBinaryKeyword(TextOf(j)))
                    return;
                ++j;
            }
        }

        static bool IsBinaryKeyword(String s) => s == "in" || s == "of" || s == "instanceof";

        bool EndsExpression(int i)
        {
            var t = Sig[i];
            switch (t.Kind)
            {
                case JsTokenKind.Identifier:
                case JsTokenKind.Number:
                case JsTokenKind.String:
                case JsTokenKind.Template:
                case JsTokenKind.TemplateTail:
                case JsTokenKind.Regex:
                case JsTokenKind.PrivateName:
                    return true;
                case JsTokenKind.Punctuator:
                    var s = TextOf(i);
                    return s == ")" || s == "]" || s == "}" || s == "++" || s == "--";
            }
            return false;
        }
    }

    /// <summary>
    /// The result of migrating one JavaScript unit (file, inline script or event handler attribute).
    /// </summary>
    public sealed class JsAnalysis
    {
        /// <summary>
        /// The edits (offsets in the text the tokens came from).
        /// </summary>
        public readonly List<TextEdit> Edits = new List<TextEdit>();
        /// <summary>
        /// Info / warning items.
        /// </summary>
        public readonly List<OffsetItem> Items = new List<OffsetItem>();
        /// <summary>
        /// Legacy names that remain after the edits (name to the first offset): the page still needs the legacy bundles.
        /// </summary>
        public readonly SortedDictionary<String, int> Unresolved = new SortedDictionary<String, int>(StringComparer.Ordinal);
        /// <summary>
        /// Shell feature names used.
        /// </summary>
        public readonly SortedSet<String> ShellNames = new SortedSet<String>(StringComparer.Ordinal);

        /// <summary>
        /// A call whose arguments change their order (an <see cref="MapEntry.Args"/> template): replaced as a whole at the end of the
        /// analysis, with the edits inside the moved arguments applied to their texts.
        /// </summary>
        public sealed class Reorder
        {
            /// <summary>The call's range (offsets).</summary>
            public int Start, End;
            /// <summary>The text before the first argument (the new callee, "(" and the template's first glue).</summary>
            public String Head;
            /// <summary>The ranges of the old arguments in their new order.</summary>
            public List<(int Start, int End)> Args;
            /// <summary>The text after each argument (the last one ends with ")").</summary>
            public List<String> Glue;
            /// <summary>The report part of the edit (displays, note).</summary>
            public TextEdit Edit;
        }

        /// <summary>
        /// The pending reordered calls.
        /// </summary>
        public readonly List<Reorder> Reorders = new List<Reorder>();
    }

    /// <summary>
    /// Token level JavaScript migration: renames legacy globals to the new Sw API using a <see cref="MigrationMap"/>.
    /// Only identifier tokens that are used as globals are touched (never strings, comments, template text or regex literals;
    /// never member accesses, object keys, method names or names declared in the same code).
    /// </summary>
    public static class JsMigrator
    {
        static readonly HashSet<String> AssignOps = new HashSet<String>(StringComparer.Ordinal)
        {
            "=", "+=", "-=", "*=", "/=", "%=", "**=", "<<=", ">>=", ">>>=", "&=", "|=", "^=", "&&=", "||=", "??=",
        };

        static readonly HashSet<String> GlobalObjects = new HashSet<String>(StringComparer.Ordinal) { "window", "self", "globalThis" };

        /// <summary>
        /// Analyze a JavaScript unit.
        /// </summary>
        /// <param name="scope">The scope analysis of the unit</param>
        /// <param name="map">The mapping table</param>
        /// <param name="level">The level (Includes: no edits, every legacy use is unresolved)</param>
        /// <param name="runGlobals">Legacy names declared at the top level of any unit of the run (page globals): never renamed</param>
        /// <returns>The analysis</returns>
        /// <param name="lineOf">Maps an offset to a 1-based line (for messages), null = show offsets</param>
        /// <param name="fixTranslations">Fix newline escapes in translated literals (rule tr-newline)</param>
        public static JsAnalysis Analyze(JsScope scope, MigrationMap map, MigrateLevel level, ISet<String> runGlobals, Func<int, int> lineOf = null, bool fixTranslations = true)
        {
            var res = new JsAnalysis();
            var text = scope.Text;
            var sig = scope.Sig;
            var reportedDecl = new HashSet<String>(StringComparer.Ordinal);
            int n = sig.Count;

            void Warn(int i, String name, String msg)
            {
                var lib = map.LibraryOf(name);
                res.Items.Add(new OffsetItem { Offset = sig[i].Start, Kind = MigrateItemKind.Warning, Message = msg + LibraryText(lib) });
                if (lib == null)
                    res.Unresolved.TryAdd(name, sig[i].Start);
            }

            //  Names this code assigns to the global object (window.Edit = Edit): a library publishing its own legacy alias
            var windowDefined = new HashSet<String>(StringComparer.Ordinal);
            for (int i = 2; i + 1 < n; ++i)
                if ((sig[i].Kind == JsTokenKind.Identifier) && scope.IsPunct(i - 1, ".") && scope.IsIdent(i - 2) && GlobalObjects.Contains(scope.TextOf(i - 2)) && scope.IsPunct(i + 1, "="))
                    windowDefined.Add(sig[i].GetText(text));

            foreach (var name in scope.TopLevel.Where(map.IsLegacy).OrderBy(x => scope.Declared[x]))
            {
                reportedDecl.Add(name);
                res.Items.Add(new OffsetItem { Offset = scope.Declared[name], Kind = MigrateItemKind.Info, Message = "declares the legacy name \"" + name + "\" as a page global in this file: its uses are not renamed in any file of the folder" });
            }

            for (int i = 0; i < n; ++i)
            {
                var t = sig[i];
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                var name = t.GetText(text);
                bool legacy = map.IsLegacy(name);
                bool shell = map.IsShell(name);
                if (!legacy && !shell)
                    continue;
                if (scope.IsPunct(i - 1, ".") || scope.IsPunct(i - 1, "?."))
                {
                    // window.Fail(...) etc: a legacy global used through the global object
                    if (legacy && scope.IsIdent(i - 2) && GlobalObjects.Contains(scope.TextOf(i - 2)) && !scope.IsPunct(i - 3, ".") && !runGlobals.Contains(name) && !scope.TopLevel.Contains(name))
                    {
                        if (shell)
                            res.ShellNames.Add(name);
                        //  The library itself publishing its legacy alias (window.Edit = Edit, if (!window.Edit) ...): nothing to port
                        if ((map.LibraryOf(name) != null) && windowDefined.Contains(name))
                        {
                            if (reportedDecl.Add("window." + name))
                                res.Items.Add(new OffsetItem { Offset = sig[i].Start, Kind = MigrateItemKind.Info, Message = "defines the legacy alias \"window." + name + "\" of " + map.LibraryOf(name) + " (kept for old pages; nothing to migrate)" });
                            continue;
                        }
                        //  A read of a plain rename of the core / ui api (window.Narrow -> Sw.Layout.Narrow): the page loads sw/core.js, so Sw is there
                        var we = map.Globals.GetValueOrDefault(name);
                        if ((level >= MigrateLevel.Api) && IsPlainRename(we) && !windowDefined.Contains(name) && scope.IsPunct(i - 1, ".") && !scope.IsIdent(i - 3, "typeof") && !IsWritten(scope, i))
                        {
                            res.Edits.Add(new TextEdit { Start = sig[i - 2].Start, End = sig[i].End, Text = we.New, Note = we.Note });
                            continue;
                        }
                        Warn(i, name, "legacy global \"" + name + "\" used as \"" + scope.TextOf(i - 2) + "." + name + "\": not rewritten, use " + Describe(we));
                    }
                    continue;
                }
                if (scope.NotUses.Contains(i))
                    continue;
                // object literal key
                if (scope.IsPunct(i + 1, ":") && (scope.IsPunct(i - 1, "{") || scope.IsPunct(i - 1, ",")) && (scope.Parent[i] >= 0) && scope.IsPunct(scope.Parent[i], "{"))
                    continue;
                // a local declaration in scope (parameter, let / const / var, catch binding ...): the local, not the legacy global
                if (!scope.TopLevel.Contains(name) && scope.IsLocalAt(name, i))
                    continue;
                //  Only let / const / class / parameter declarations of the name, none in scope here: this use is the global
                bool exactOutside = scope.OnlyExactLocals(name) && !runGlobals.Contains(name);
                if (!exactOutside && (scope.Declared.TryGetValue(name, out var declAt) || runGlobals.Contains(name)))
                {
                    if (reportedDecl.Add(name))
                    {
                        if (scope.TopLevel.Contains(name) || (runGlobals.Contains(name) && !scope.Declared.ContainsKey(name)))
                            res.Items.Add(new OffsetItem { Offset = t.Start, Kind = MigrateItemKind.Info, Message = "\"" + name + "\" is a legacy name but declared as a page global " + (scope.TopLevel.Contains(name) ? "in this file" : "in another file of the folder") + ": not renamed" });
                        else
                        {
                            res.Items.Add(new OffsetItem { Offset = t.Start, Kind = map.LibraryOf(name) == null ? MigrateItemKind.Warning : MigrateItemKind.Info, Message = "\"" + name + "\" is a legacy " + (map.LibraryOf(name) == null ? "global" : ("alias of " + map.LibraryOf(name))) + " but also declared locally in this code (first declaration at " + (lineOf == null ? ("offset " + declAt) : ("line " + lineOf(declAt))) + ") and used outside that declaration's scope: not renamed, check if this use means the legacy global" });
                            if (map.LibraryOf(name) == null)
                                res.Unresolved.TryAdd(name, t.Start);
                        }
                    }
                    continue;
                }
                if (shell)
                    res.ShellNames.Add(name);
                if (!legacy)
                    continue;
                if (level == MigrateLevel.Includes)
                {
                    if (map.LibraryOf(name) == null)
                        res.Unresolved.TryAdd(name, t.Start);
                    continue;
                }
                if (!map.Globals.TryGetValue(name, out var e))
                {
                    Warn(i, name, "legacy global \"" + name + "\" has no automatic mapping and no Sw counterpart: rewrite it with the platform API or a small local helper (PORTING.md A11)");
                    continue;
                }
                int last = i;
                var display = name;
                if (e.Members != null)
                {
                    if ((scope.IsPunct(i + 1, ".") || scope.IsPunct(i + 1, "?.")) && scope.IsIdent(i + 2))
                    {
                        var member = scope.TextOf(i + 2);
                        display = name + "." + member;
                        last = i + 2;
                        if (e.Members.TryGetValue(member, out var me))
                            e = me;
                        else if (e.New != null)
                            e = new MapEntry { Name = member, New = e.New + "." + member, Section = e.Section, Note = e.Note };
                        else
                        {
                            Warn(i, name, "\"" + display + "\" has no mapping: rewrite manually");
                            i = last;
                            continue;
                        }
                    }
                    else if (e.New == null)
                    {
                        Warn(i, name, "legacy object \"" + name + "\" used without a member: rewrite manually (members map to " + String.Join(", ", e.Members.Values.Where(x => x.New != null).Select(x => x.New).Distinct().Take(3)) + " ...)");
                        continue;
                    }
                }
                ApplyEntry(scope, res, e, i, last, display, name);
                i = last;
            }
            if (fixTranslations && (level >= MigrateLevel.Api))
                FixTranslationNewlines(scope, res);
            ApplyReorders(text, res);
            return res;
        }

        /// <summary>
        /// Replace each reordered call as a whole: the texts of its arguments with the edits inside them applied (those edits are taken out).
        /// </summary>
        static void ApplyReorders(String text, JsAnalysis res)
        {
            foreach (var ro in res.Reorders)
            {
                var sb = new System.Text.StringBuilder(ro.Head);
                for (int a = 0; a < ro.Args.Count; ++a)
                {
                    var (s, en) = ro.Args[a];
                    var inner = res.Edits.Where(x => (x.Start >= s) && (x.End <= en)).OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
                    int pos = s;
                    foreach (var ie in inner)
                    {
                        if (ie.Start < pos)
                            continue;
                        sb.Append(text, pos, ie.Start - pos).Append(ie.Text);
                        pos = ie.End;
                    }
                    sb.Append(text, pos, en - pos).Append(ro.Glue[a]);
                    res.Edits.RemoveAll(x => inner.Contains(x));
                }
                var e = ro.Edit;
                e.Start = ro.Start;
                e.End = ro.End;
                e.Text = sb.ToString();
                res.Edits.Add(e);
            }
        }

        /// <summary>The translation functions (permanent names, never renamed).</summary>
        static readonly HashSet<String> TrFunctions = new HashSet<String>(StringComparer.Ordinal) { "_T", "_TH", "_TF", "_TFH" };

        /// <summary>
        /// Fix translated literals that contain newline escapes (contract rule tr-newline: the minifier turns them into backtick literals that
        /// the server doesn't translate). Every \n / \r\n / \r escape becomes a new format placeholder whose argument is a newline built at run
        /// time (String.fromCharCode, not a quoted literal: a trailing quoted literal would be taken as the translation context), inserted
        /// after the format arguments, before the context. _TF / _TFH (no formatting) become _T / _TH when the text has no "{".
        /// Calls that can't be fixed safely (line continuations, missing format arguments, spread) are reported.
        /// </summary>
        /// <param name="scope">The code</param>
        /// <param name="res">Receives the edits and items</param>
        static void FixTranslationNewlines(JsScope scope, JsAnalysis res)
        {
            var sig = scope.Sig;
            var text = scope.Text;
            for (int i = 0; i + 2 < sig.Count; ++i)
            {
                if ((sig[i].Kind != JsTokenKind.Identifier) || !TrFunctions.Contains(scope.TextOf(i)) || !scope.IsPunct(i + 1, "(") || scope.IsPunct(i - 1, ".") || scope.IsPunct(i - 1, "?."))
                    continue;
                var lit = sig[i + 2];
                if (lit.Kind != JsTokenKind.String)
                    continue;
                var raw = lit.GetText(text);
                var body = raw.Substring(1, raw.Length - 2);
                if ((body.IndexOf("\\n", StringComparison.Ordinal) < 0) && (body.IndexOf("\\r", StringComparison.Ordinal) < 0) && (body.IndexOf("\\\n", StringComparison.Ordinal) < 0) && (body.IndexOf("\\\r", StringComparison.Ordinal) < 0))
                    continue;
                var fn = scope.TextOf(i);
                void Report(String why) => res.Items.Add(new OffsetItem { Offset = lit.Start, Kind = MigrateItemKind.Warning, Message = "translated literal with a newline (" + fn + "): " + why + "; split the text manually (the server doesn't translate it)" });
                //  Collect the escapes (skipping escaped backslashes)
                var parts = new List<(int Start, int Len, String Kind)>();
                bool continuation = false;
                for (int p = 0; p < body.Length; ++p)
                {
                    if (body[p] != '\\')
                        continue;
                    if (p + 1 >= body.Length)
                        break;
                    var c = body[p + 1];
                    if ((c == '\n') || (c == '\r'))
                        continuation = true;
                    else if ((c == 'r') && (p + 3 < body.Length) && (body[p + 2] == '\\') && (body[p + 3] == 'n'))
                    {
                        parts.Add((p, 4, "13, 10"));
                        p += 3;
                        continue;
                    }
                    else if (c == 'n')
                        parts.Add((p, 2, "10"));
                    else if (c == 'r')
                        parts.Add((p, 2, "13"));
                    ++p;
                }
                if (continuation)
                {
                    Report("a line continuation");
                    continue;
                }
                if (parts.Count == 0)
                    continue;
                var args = GetArgs(scope, i + 1);
                if (args == null)
                {
                    Report("spread arguments");
                    continue;
                }
                bool format = (fn == "_T") || (fn == "_TH");
                if (!format && body.Contains('{'))
                {
                    Report("the " + fn + " text contains \"{\" (no formatting)");
                    continue;
                }
                //  Trailing quoted literal = context (server rule)
                bool hasContext = (args.Count >= 2) && (args[^1].Start == args[^1].End) && (sig[args[^1].Start].Kind == JsTokenKind.String);
                int fmtCount = format ? (args.Count - 1 - (hasContext ? 1 : 0)) : 0;
                if (!format && (args.Count > 2))
                {
                    Report(fn + " called with " + args.Count + " arguments");
                    continue;
                }
                int maxIdx = -1;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body, @"\{(\d+)\}"))
                    maxIdx = Math.Max(maxIdx, int.Parse(m.Groups[1].Value));
                if (maxIdx + 1 > fmtCount)
                {
                    Report("uses {" + maxIdx + "} but has " + fmtCount + " format argument(s)");
                    continue;
                }
                //  New placeholders, one per newline kind
                var kinds = parts.Select(x => x.Kind).Distinct().ToList();
                var sb = new System.Text.StringBuilder();
                int last = 0;
                foreach (var (ps, pl, k) in parts)
                {
                    sb.Append(body, last, ps - last).Append('{').Append(fmtCount + kinds.IndexOf(k)).Append('}');
                    last = ps + pl;
                }
                sb.Append(body, last, body.Length - last);
                var newLit = raw[0] + sb.ToString() + raw[^1];
                var insert = String.Concat(kinds.Select(k => ", String.fromCharCode(" + k + ")"));
                //  Insert after the last format argument (or the literal)
                int afterTok = (fmtCount > 0) ? args[fmtCount].End : (i + 2);
                var newFn = format ? fn : (fn == "_TF" ? "_T" : "_TH");
                if (newFn != fn)
                    res.Edits.Add(new TextEdit { Start = sig[i].Start, End = sig[i].End, Text = newFn, Silent = true });
                res.Edits.Add(new TextEdit
                {
                    Start = lit.Start,
                    End = lit.End,
                    Text = newLit,
                    ReportAt = sig[i].Start,
                    OldDisplay = fn + "(" + raw + ", ...)",
                    NewDisplay = newFn + "(" + newLit + ", ..." + insert + ")",
                    Note = "newline escapes in a translated literal (tr-newline) became format placeholders",
                });
                if (afterTok == i + 2)
                {
                    //  Same token as the literal: append the arguments to the literal edit
                    res.Edits[^1].Text = newLit + insert;
                }
                else
                    res.Edits.Add(new TextEdit { Start = sig[afterTok].End, End = sig[afterTok].End, Text = insert, Silent = true });
            }
        }

        static String Describe(MapEntry e) => e == null ? "the Sw API" : e.SuggestText;

        /// <summary>
        /// True for an entry that renames the name and nothing else (no members, argument rules or library alias): the old global is the
        /// same value as the new expression, so a read through the global object can use the new expression too.
        /// </summary>
        static bool IsPlainRename(MapEntry e) => (e != null) && (e.Kind == MapKind.Rename) && (e.New != null) && (e.Members == null) && (e.MaxArgs < 0) && (e.Options == null) && (e.Args == null) && (e.Library == null);

        /// <summary>
        /// True if the (member) expression ending at token i is written: x = .., x += .., x++, ++x, delete x.
        /// </summary>
        static bool IsWritten(JsScope scope, int i)
        {
            var sig = scope.Sig;
            bool nextPunct = (i + 1 < sig.Count) && (sig[i + 1].Kind == JsTokenKind.Punctuator);
            var next = scope.TextOf(i + 1);
            if (nextPunct && (AssignOps.Contains(next) || next == "++" || next == "--"))
                return true;
            //  the first token of the member expression (window in window.X)
            int first = scope.IsPunct(i - 1, ".") && scope.IsIdent(i - 2) ? i - 2 : i;
            return scope.IsPunct(first - 1, "++") || scope.IsPunct(first - 1, "--") || scope.IsIdent(first - 1, "delete");
        }

        /// <summary>
        /// The warning suffix for a legacy alias that a library still provides (it keeps working, no sw/compat.js needed).
        /// </summary>
        static String LibraryText(String library) => library == null ? "" : (" [legacy alias still provided by " + library + ": keeps working, rewrite when convenient]");

        /// <summary>
        /// Rewrite a call with an <see cref="MapEntry.Args"/> template (the arguments are reordered / regrouped).
        /// When the old arguments keep their order the argument texts stay in place (only the text between them changes), so renames inside
        /// them still apply; a call that changes the order is rewritten as a whole and needs arguments without side effects and legacy names.
        /// </summary>
        /// <returns>Null when rewritten, else why not</returns>
        static String ApplyArgs(JsScope scope, JsAnalysis res, MapEntry e, int first, int last, int open, List<(int Start, int End)> args)
        {
            var sig = scope.Sig;
            var text = scope.Text;
            var close = scope.Match[open];
            String ArgText(int k) => text.Substring(sig[args[k].Start].Start, sig[args[k].End].End - sig[args[k].Start].Start);
            //  The template as glue texts and references: glue[0] $r[0] glue[1] $r[1] ... glue[m]
            var refs = MigrationMap.ArgRefs(e.Args);
            var glue = new List<String>();
            {
                var sb = new System.Text.StringBuilder();
                for (int p = 0; p < e.Args.Length; ++p)
                {
                    if (e.Args[p] == '$')
                    {
                        glue.Add(sb.ToString());
                        sb.Clear();
                        ++p;
                        continue;
                    }
                    sb.Append(e.Args[p]);
                }
                glue.Add(sb.ToString());
            }
            //  Trailing references to arguments that weren't passed are left out (with their comma)
            while ((refs.Count > 0) && (refs[^1] >= args.Count) && String.IsNullOrWhiteSpace(glue[^1]) && glue[^2].TrimEnd().EndsWith(",", StringComparison.Ordinal))
            {
                refs.RemoveAt(refs.Count - 1);
                glue.RemoveAt(glue.Count - 1);
                var g = glue[^1].TrimEnd();
                glue[^1] = g.Substring(0, g.Length - 1);
            }
            //  Old arguments the template doesn't use are dropped (only simple values: no side effect is lost)
            for (int k = 0; k < args.Count; ++k)
                if (!refs.Contains(k) && !IsSimpleArgument(scope, args[k].Start, args[k].End))
                    return "argument " + (k + 1) + " is not used by the new call but isn't a simple value (side effects?)";
            //  A reference to a missing argument becomes undefined (part of the glue)
            var present = new List<int>();
            var pieces = new List<String> { glue[0] };
            for (int r = 0; r < refs.Count; ++r)
            {
                if (refs[r] < args.Count)
                {
                    present.Add(refs[r]);
                    pieces.Add(glue[r + 1]);
                }
                else
                    pieces[^1] += "undefined" + glue[r + 1];
            }
            String Short(String s) => s.Length <= 40 ? s : (s.Substring(0, 37).TrimEnd() + "...");
            var preview = pieces[0] + String.Concat(present.Select((k, x) => Short(ArgText(k)) + pieces[x + 1]));
            var oldDisplay = text.Substring(sig[first].Start, sig[open].End - sig[first].Start) + String.Join(", ", args.Select((_, k) => Short(ArgText(k)))) + ")";
            bool ordered = present.Zip(present.Skip(1), (a, b) => a < b).All(x => x);
            if (!ordered)
            {
                foreach (var k in present)
                    if (!IsPureArgument(scope, args[k].Start, args[k].End))
                        return "argument " + (k + 1) + " moves before an earlier one but may have side effects";
                //  The whole call is replaced once all edits of the unit are known (renames inside the arguments move with them)
                res.Reorders.Add(new JsAnalysis.Reorder
                {
                    Start = sig[first].Start,
                    End = sig[close].End,
                    Head = e.New + "(" + pieces[0],
                    Args = present.Select(k => (sig[args[k].Start].Start, sig[args[k].End].End)).ToList(),
                    Glue = pieces.Skip(1).Select((g, x) => x == present.Count - 1 ? g + ")" : g).ToList(),
                    Edit = new TextEdit { OldDisplay = oldDisplay, NewDisplay = e.New + "(" + preview + ")", Note = e.Note },
                });
                return null;
            }
            //  In order: replace the callee and the text between the kept arguments (whitespace / new lines after a comma are kept)
            String Gap(int from, int to, String g)
            {
                var old = text.Substring(from, to - from);
                var comma = old.LastIndexOf(',');
                if ((comma >= 0) && g.EndsWith(", ", StringComparison.Ordinal) && String.IsNullOrWhiteSpace(old.Substring(comma + 1)) && old.Substring(comma + 1).Contains('\n'))
                    return g.Substring(0, g.Length - 1) + old.Substring(comma + 1);
                return g;
            }
            if (present.Count == 0)
                res.Edits.Add(new TextEdit { Start = sig[open].End, End = sig[close].Start, Text = pieces[0], Silent = true });
            else
            {
                res.Edits.Add(new TextEdit { Start = sig[open].End, End = sig[args[present[0]].Start].Start, Text = Gap(sig[open].End, sig[args[present[0]].Start].Start, pieces[0]), Silent = true });
                for (int x = 0; x + 1 < present.Count; ++x)
                {
                    int from = sig[args[present[x]].End].End, to = sig[args[present[x + 1]].Start].Start;
                    res.Edits.Add(new TextEdit { Start = from, End = to, Text = Gap(from, to, pieces[x + 1]), Silent = true });
                }
                res.Edits.Add(new TextEdit { Start = sig[args[present[^1]].End].End, End = sig[close].Start, Text = pieces[^1], Silent = true });
            }
            res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = e.New, OldDisplay = oldDisplay, NewDisplay = e.New + "(" + preview + ")", Note = e.Note });
            return null;
        }

        static void ApplyEntry(JsScope scope, JsAnalysis res, MapEntry e, int first, int last, String display, String name)
        {
            var sig = scope.Sig;
            var text = scope.Text;

            void Warn(String msg)
            {
                res.Items.Add(new OffsetItem { Offset = sig[first].Start, Kind = MigrateItemKind.Warning, Message = msg + LibraryText(e.Library) });
                if (e.Library == null)
                    res.Unresolved.TryAdd(name, sig[first].Start);
            }
            String NoteText() => e.Note == null ? "" : (" (" + e.Note + ")");

            switch (e.Kind)
            {
                case MapKind.Ignore:
                    return;
                case MapKind.Report:
                    Warn("\"" + display + "\" is not rewritten automatically: use " + e.SuggestText + NoteText());
                    return;
            }
            // contexts where a rewrite is not safe
            if (scope.IsIdent(first - 1, "typeof"))
            {
                Warn("\"typeof " + display + "\" not rewritten (Sw may not be loaded where this is tested): use typeof Sw === \"object\" or " + e.SuggestText);
                return;
            }
            var next = scope.TextOf(last + 1);
            bool nextPunct = (last + 1 < sig.Count) && (sig[last + 1].Kind == JsTokenKind.Punctuator);
            if ((nextPunct && (AssignOps.Contains(next) || next == "++" || next == "--")) || scope.IsPunct(first - 1, "++") || scope.IsPunct(first - 1, "--"))
            {
                Warn("\"" + display + "\" is assigned: not rewritten, review (new: " + e.SuggestText + ")");
                return;
            }
            bool isCall = scope.IsPunct(last + 1, "(") || (scope.IsPunct(last + 1, "?.") && scope.IsPunct(last + 2, "("));
            int open = scope.IsPunct(last + 1, "(") ? last + 1 : last + 2;
            if (e.Kind == MapKind.Property)
            {
                if (isCall && (scope.Match[open] == open + 1))
                {
                    var close = open + 1;
                    res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[close].End, Text = e.New, Note = e.Note });
                    return;
                }
                Warn("\"" + display + "\" is now the property " + e.New + ": rewrite this use manually" + NoteText());
                return;
            }
            if (e.Args != null)
            {
                var args = isCall ? GetArgs(scope, open) : null;
                if (args == null)
                {
                    Warn("\"" + display + "\" " + (isCall ? "called with spread arguments" : "used as a value") + ": not rewritten, use " + e.SuggestText + NoteText());
                    return;
                }
                var why = ApplyArgs(scope, res, e, first, last, open, args);
                if (why != null)
                    Warn("\"" + display + "\": " + why + ": not rewritten, use " + e.SuggestText + NoteText());
                return;
            }
            if ((e.MaxArgs >= 0) || (e.Options != null))
            {
                var args = isCall ? GetArgs(scope, open) : null;
                int max = e.Options != null ? (Math.Max(e.Positional, 0) + e.Options.Length) : e.MaxArgs;
                if ((args == null) || (args.Count > max))
                {
                    Warn("\"" + display + "\" " + (isCall ? ((args == null ? "called with spread / unknown" : ("called with " + args.Count)) + " argument(s)") : "used as a value") + ": not rewritten, use " + e.SuggestText + NoteText());
                    return;
                }
                if ((e.Options != null) && (args.Count > e.Positional))
                {
                    // positional arguments after Positional become an options object: f(a, b, c) -> New(a, { K1: b, K2: c });
                    // an option named "-" drops its argument (only a simple one: a literal or a name, so no side effect is lost)
                    var close = scope.Match[open];
                    bool Dropped(int k) => (k >= e.Positional) && (e.Options[k - e.Positional] == MigrationMap.DropOption);
                    for (int k = 0; k < args.Count; ++k)
                        if (Dropped(k) && !IsSimpleArgument(scope, args[k].Start, args[k].End))
                        {
                            Warn("\"" + display + "\": argument " + (k + 1) + " is ignored by the new API but isn't a simple value (side effects?): not rewritten, use " + e.SuggestText + NoteText());
                            return;
                        }
                    int lastKept = -1, firstOpt = -1, lastOpt = -1;
                    for (int k = 0; k < args.Count; ++k)
                        if (!Dropped(k))
                        {
                            lastKept = k;
                            if (k >= e.Positional)
                            {
                                if (firstOpt < 0)
                                    firstOpt = k;
                                lastOpt = k;
                            }
                        }
                    var preview = new List<String>();
                    for (int k = 0; k < args.Count; ++k)
                    {
                        var (s, en) = args[k];
                        var argText = text.Substring(sig[s].Start, sig[en].End - sig[s].Start);
                        if (Dropped(k))
                        {
                            //  Inside: remove the argument with its comma; trailing ones: below, from the end of the last kept argument
                            if (k < lastKept)
                                res.Edits.Add(new TextEdit { Start = sig[s].Start, End = sig[args[k + 1].Start].Start, Text = "", Silent = true });
                            continue;
                        }
                        if (k < e.Positional)
                        {
                            preview.Add(argText);
                            continue;
                        }
                        var key = e.Options[k - e.Positional];
                        var ins = (k == firstOpt ? "{ " : "") + key + ": ";
                        res.Edits.Add(new TextEdit { Start = sig[s].Start, End = sig[s].Start, Text = ins, Silent = true });
                        preview.Add(ins + argText);
                    }
                    if (lastKept < args.Count - 1)
                    {
                        int from = lastKept >= 0 ? sig[args[lastKept].End].End : sig[args[lastKept + 1].Start].Start;
                        res.Edits.Add(new TextEdit { Start = from, End = sig[args[args.Count - 1].End].End, Text = "", Silent = true });
                    }
                    //  A destructive confirmation (Delete, Kill ...) becomes a danger dialog, "Yes, Delete!" / "No, Keep it!" verb labels
                    bool danger = IsConfirm(e) && ConfirmWording(scope, res, args, first, out _);
                    var closeOpt = danger ? ", Danger: true }" : " }";
                    if (lastOpt >= 0)
                        res.Edits.Add(new TextEdit { Start = sig[args[lastOpt].End].End, End = sig[args[lastOpt].End].End, Text = closeOpt, Silent = true });
                    res.Edits.Add(new TextEdit
                    {
                        Start = sig[first].Start,
                        End = sig[last].End,
                        Text = e.New,
                        OldDisplay = text.Substring(sig[first].Start, sig[close].End - sig[first].Start),
                        NewDisplay = e.New + "(" + String.Join(", ", preview) + (lastOpt >= 0 ? (closeOpt + ")") : ")"),
                        Note = e.Note,
                    });
                    return;
                }
                //  Open(url) opened a new tab; Sw.Page.Navigate(url) goes there in the same tab: keep the old behaviour
                if ((e.Name == "Open") && (e.New == "Sw.Page.Navigate") && (args.Count == 1))
                {
                    int at = sig[args[0].End].End;
                    res.Edits.Add(new TextEdit { Start = at, End = at, Text = ", \"_blank\"", Silent = true });
                    res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = e.New, NewDisplay = e.New + "(url, \"_blank\")", Note = "the old Open opened a new tab, Sw.Page.Navigate defaults to the same tab (\"_self\"): drop \"_blank\" where the page should go there" });
                    return;
                }
                if (IsConfirm(e) && (args.Count > 0) && ConfirmWording(scope, res, args, first, out var verbOk))
                {
                    //  Confirm(title [, text [, okText]]): the options object { Danger: true } after the OK text (the title's verb if none)
                    var add = (args.Count == 1 ? ", null" : "") + (args.Count < 3 ? (", " + (verbOk ?? "null")) : "") + ", { Danger: true }";
                    int at = sig[args[args.Count - 1].End].End;
                    res.Edits.Add(new TextEdit { Start = at, End = at, Text = add, Silent = true });
                    res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = e.New, NewDisplay = e.New + "(..." + add + ")", Note = "a destructive action: a danger dialog (danger button, Cancel focused, Enter never confirms)" });
                    return;
                }
                res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = e.New });
                return;
            }
            // shorthand property { delay } -> { delay: Sw.Util.Delay }
            var parent = scope.Parent[first];
            if ((first == last) && (parent >= 0) && scope.IsPunct(parent, "{") && (scope.IsPunct(first - 1, "{") || scope.IsPunct(first - 1, ",")) && (scope.IsPunct(last + 1, ",") || scope.IsPunct(last + 1, "}")))
            {
                var close = scope.Match[parent];
                if ((scope.BraceKind[parent] == 'o') && !((close > 0) && scope.IsPunct(close + 1, "=")))
                {
                    res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = display + ": " + e.New, Note = e.Note });
                    return;
                }
                Warn("\"" + display + "\" in a shorthand property / pattern: not rewritten, use " + e.SuggestText);
                return;
            }
            res.Edits.Add(new TextEdit { Start = sig[first].Start, End = sig[last].End, Text = e.New, Note = e.Note });
        }

        /// <summary>
        /// True for the map entry of the old <c>Confirm(title, text, okText, cancelText, ...)</c> (rewritten to Sw.Dialog.Confirm).
        /// </summary>
        static bool IsConfirm(MapEntry e) => (e.Name == "Confirm") && (e.New == "Sw.Dialog.Confirm");

        /// <summary>
        /// The dialog wording rules (ui/README.md "Dialog rules") for an old <c>Confirm(title, text, okText, cancelText, ...)</c> call that
        /// is rewritten: "Yes, Delete!" becomes "Delete", "No, Keep it!" becomes "Cancel" (literal texts, also inside _T / _TF), and the
        /// rest of the wording that needs a person (a title that is no question, "Are you sure?" in the text, labels that are not literals)
        /// is reported as one warning.
        /// </summary>
        /// <param name="scope">The scope</param>
        /// <param name="res">Receives the edits and the warning</param>
        /// <param name="args">The call arguments</param>
        /// <param name="first">The first token of the callee (the report position)</param>
        /// <param name="verbOk">Receives an OK text made from the title's verb (source text, ex: <c>"Delete"</c>) for a call without one</param>
        /// <returns>True if the action is destructive (the call gets <c>{ Danger: true }</c>)</returns>
        static bool ConfirmWording(JsScope scope, JsAnalysis res, List<(int Start, int End)> args, int first, out String verbOk)
        {
            var sig = scope.Sig;
            var text = scope.Text;
            verbOk = null;
            DialogWording.Literal title = default, ok = default, cancel = default;
            bool hasTitle = (args.Count > 0) && DialogWording.TryLiteral(text, sig, args[0].Start, args[0].End, out title);
            bool hasOk = (args.Count > 2) && DialogWording.TryLiteral(text, sig, args[2].Start, args[2].End, out ok);
            bool hasCancel = (args.Count > 3) && DialogWording.TryLiteral(text, sig, args[3].Start, args[3].End, out cancel);
            bool danger = (hasTitle && DialogWording.IsDestructive(title.Value)) || (hasOk && DialogWording.IsDestructive(ok.Value));
            var todo = new List<String>();
            if (hasOk)
            {
                var nl = DialogWording.OkLabel(ok.Value);
                if ((nl == null) && DialogWording.IsYesNo(ok.Value) && hasTitle && title.Plain)
                    nl = VerbOf(title.Value);
                if ((nl != null) && ok.Plain && (nl.IndexOf('"') < 0) && (nl.IndexOf('\'') < 0))
                    res.Edits.Add(ok.Replace(nl, "dialog buttons are verbs (ui/README.md Dialog rules)"));
                else if (DialogWording.IsYesNo(ok.Value))
                    todo.Add("the OK text \"" + ok.Value + "\" should be a verb (\"Delete\")");
            }
            else if (args.Count > 2)
                todo.Add("check that the OK text is a verb (\"Delete\", not \"Yes, Delete!\")");
            if (hasCancel)
            {
                var nl = DialogWording.CancelLabel(cancel.Value);
                if ((nl != null) && cancel.Plain)
                    res.Edits.Add(cancel.Replace(nl, "the way out of a dialog is \"Cancel\" (ui/README.md Dialog rules)"));
            }
            else if (args.Count > 3)
                todo.Add("the cancel text should be \"Cancel\"");
            if (danger)
            {
                if (hasTitle && !title.Value.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                    todo.Add("the title \"" + title.Value + "\" should ask a question that names the item (\"Delete the version?\"), or pass Target (the title is then made from the OK text and the item)");
                if (args.Count < 3)
                {
                    var v = hasTitle && title.Plain ? VerbOf(title.Value) : null;
                    verbOk = v == null ? null : (v == title.Value ? text.Substring(sig[args[0].Start].Start, sig[args[0].End].End - sig[args[0].Start].Start) : ("\"" + v + "\""));
                }
            }
            if ((args.Count > 1) && DialogWording.HasAreYouSure(text.Substring(sig[args[1].Start].Start, sig[args[1].End].End - sig[args[1].Start].Start)))
                todo.Add("drop \"Are you sure?\" from the text (the title asks)");
            if (!danger && !hasTitle && !hasOk)
                todo.Add("if the action is destructive (delete, remove, kill ...) add Danger: true to the options (or use Sw.Cells.ConfirmDanger)");
            if (todo.Count > 0)
                res.Items.Add(new OffsetItem
                {
                    Offset = sig[first].Start,
                    Kind = MigrateItemKind.Warning,
                    Message = "Sw.Dialog.Confirm" + (danger ? " (a destructive action, Danger: true added)" : "") + ": " + String.Join("; ", todo) + " (ui/README.md Dialog rules)",
                });
            return danger;
        }

        /// <summary>
        /// The verb of a destructive title ("Delete", "Delete the file?" and "Yes, Delete!" give "Delete"), null if none.
        /// </summary>
        static String VerbOf(String title)
        {
            if (!DialogWording.IsDestructive(title))
                return null;
            var m = System.Text.RegularExpressions.Regex.Match(title, @"^\s*(?:yes\s*[,!.]?\s*)?(?<v>[A-Za-z]+)");
            if (!m.Success)
                return null;
            var v = m.Groups["v"].Value;
            return Char.ToUpperInvariant(v[0]) + v.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// True if a call argument (significant token range) is a simple value without side effects: one number, string or name token
        /// (null, undefined, true, a variable), or a signed number.
        /// </summary>
        static bool IsSimpleArgument(JsScope scope, int first, int last)
        {
            var sig = scope.Sig;
            if (first == last)
                return (sig[first].Kind == JsTokenKind.Number) || (sig[first].Kind == JsTokenKind.String) || (sig[first].Kind == JsTokenKind.Identifier);
            if ((last == first + 1) && (scope.IsPunct(first, "-") || scope.IsPunct(first, "+")) && (sig[last].Kind == JsTokenKind.Number))
                return true;
            //  A member chain of names: ROUTES.VALIDATION.PREFIX, this.x, a?.b (reads, no call)
            if (!scope.IsIdent(first) || ((last - first) % 2 != 0))
                return false;
            for (int k = first + 1; k < last; k += 2)
                if (!(scope.IsPunct(k, ".") || scope.IsPunct(k, "?.")) || !scope.IsIdent(k + 1))
                    return false;
            return true;
        }

        /// <summary>
        /// True if evaluating a call argument has no side effect, so it may move before or after other arguments: a simple value
        /// (<see cref="IsSimpleArgument"/>), an arrow function or a function expression (creating a function runs none of its code).
        /// </summary>
        static bool IsPureArgument(JsScope scope, int first, int last)
        {
            if (IsSimpleArgument(scope, first, last))
                return true;
            int j = first;
            if (scope.IsIdent(j, "async"))
                ++j;
            if (scope.IsIdent(j, "function"))
            {
                //  function [name] (params) { body } and nothing after it
                ++j;
                if (scope.IsPunct(j, "*"))
                    ++j;
                if (scope.IsIdent(j))
                    ++j;
                if (!scope.IsPunct(j, "(") || (scope.Match[j] < 0) || !scope.IsPunct(scope.Match[j] + 1, "{"))
                    return false;
                return scope.Match[scope.Match[j] + 1] == last;
            }
            //  x => body, (a, b) => body: the arrow function spans the whole argument (the argument ends at a "," of its own level)
            if (scope.IsIdent(j) && scope.IsPunct(j + 1, "=>"))
                return true;
            return scope.IsPunct(j, "(") && (scope.Match[j] > 0) && scope.IsPunct(scope.Match[j] + 1, "=>");
        }

        /// <summary>
        /// Get the arguments of a call as significant token index ranges (first, last), null if a spread argument makes them unknown.
        /// </summary>
        static List<(int Start, int End)> GetArgs(JsScope scope, int open)
        {
            var close = scope.Match[open];
            if (close < 0)
                return null;
            var res = new List<(int, int)>();
            int j = open + 1;
            int argStart = j;
            while (j < close)
            {
                if (scope.IsPunct(j, "..."))
                    return null;
                if (scope.IsPunct(j, ","))
                {
                    if (j > argStart)
                        res.Add((argStart, j - 1));
                    argStart = ++j;
                    continue;
                }
                var m = scope.Match[j];
                j = (m > j) ? m + 1 : j + 1;
            }
            if (close > argStart)
                res.Add((argStart, close - 1));
            return res;
        }
    }
}
