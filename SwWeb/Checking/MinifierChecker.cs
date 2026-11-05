using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// Rule <see cref="Rule.MinifierReorder"/>: code that the build's minifier (Compress.exe -> minify.exe, tdewolff minify v2.24.19)
    /// breaks by moving a destructuring declarator in front of the declarator it reads.
    /// <para>What the minifier does (OWN3-Min, _ai/FrontendRewrite/74_OwnerReport3Notes.md "MINIFIER"):</para>
    /// <list type="number">
    /// <item>It merges consecutive declarations of the same kind (<c>const a = 1; const { b } = c;</c> -> <c>const a=1,{b}=c</c>; any other
    /// statement in between, or a different kind, stops the merge).</item>
    /// <item>When the first declarator of the merged declaration is a plain name, it moves the first destructuring declarator whose
    /// INITIALIZER doesn't name an earlier declarator to the front (it saves the space in <c>const{</c>).</item>
    /// <item>It doesn't look at the pattern itself: a default value or a computed key that reads an earlier declarator then runs before
    /// that declarator: <c>const s = t.length, { end: o = s - 1 } = i</c> -> <c>const{end:o=s-1}=i,s=t.length</c>, a
    /// "Cannot access 's' before initialization" ReferenceError for const / let and a silent undefined for var.</item>
    /// </list>
    /// The check simulates the same steps on the tokens and reports a default value / computed key of the moved pattern that names an
    /// earlier declarator. Fix: compute the default after the destructuring (<c>const { end } = i; const o = end === undefined ? s - 1 : end;</c>),
    /// or keep a non-declaration statement (or a different declaration kind) between the two. A deliberate case (ex: a function
    /// default that is only called later) can be marked with <see cref="Allow"/> on the line or the line before.
    /// </summary>
    public static class MinifierChecker
    {
        /// <summary>
        /// A comment marker that allows a finding of <see cref="Rule.MinifierReorder"/> on its line or the line below.
        /// </summary>
        public const String Allow = "SwWeb:allow minifier-reorder";

        sealed class Declarator
        {
            public int IdStart;
            public int IdEnd;       // exclusive (sig index)
            public int InitStart = -1;
            public int InitEnd = -1;  // exclusive (sig index)
            public bool IsPattern;
        }

        static readonly HashSet<String> DeclKeywords = new HashSet<String>(StringComparer.Ordinal) { "const", "let", "var" };

        /// <summary>
        /// Check the tokens of a script.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">All tokens</param>
        /// <param name="sig">The indices of the significant tokens</param>
        public static void Check(Checker.FileContext ctx, List<JsToken> tokens, List<int> sig)
        {
            Scan(ctx.Text, tokens, sig, false, (off, kind, name) =>
            {
                if (!ctx.HasMarker(off, Allow))
                    ctx.Add(Rule.MinifierReorder, off, "\"" + name + "\" is read by a destructuring default / computed key and declared earlier in the same " + kind + " declaration (consecutive " + kind + " statements count as one): the build's minifier moves the destructuring in front of it (\"Cannot access '" + name + "' before initialization\"" + (kind == "var" ? ", for var a silent undefined" : "") + "); compute the default after the destructuring (const { key } = o; const v = key === undefined ? " + name + " : key;) or put another statement between the declarations");
            });
        }

        /// <summary>
        /// A use before declaration found in minified (built) code by <see cref="CheckOutput"/>.
        /// </summary>
        /// <param name="Offset">Offset of the reading identifier in the text</param>
        /// <param name="Kind">The declaration keyword (const, let or var)</param>
        /// <param name="Name">The name that is read before the declarator that declares it</param>
        public readonly record struct OutputFinding(int Offset, String Kind, String Name);

        /// <summary>
        /// Check built (minified) JavaScript, the output of the minifier, not its input: reports an identifier that a declarator of a
        /// var / let / const declaration reads while it runs (its initializer, or a default value / computed key of its pattern) and that
        /// a LATER declarator of the same declaration (or of a directly following declaration of the same kind) declares.
        /// That is the shape the minifier bug of <see cref="Rule.MinifierReorder"/> produces (<c>const{end:o=s-1}=i,s=t.length</c>):
        /// a ReferenceError for const / let, a silent undefined for var.
        /// Reads inside a nested function, arrow, method or class body are not reported (they run later), except a function / arrow that
        /// is called at once (<c>(()=>s)()</c>).
        /// </summary>
        /// <param name="text">The script text</param>
        /// <param name="start">Start offset (ex: the body of a script element)</param>
        /// <param name="end">End offset (exclusive), -1 = the end of the text</param>
        /// <returns>The findings (empty if none)</returns>
        public static List<OutputFinding> CheckOutput(String text, int start = 0, int end = -1)
        {
            var res = new List<OutputFinding>();
            var tokens = JsLexer.Tokenize(text, start, end);
            var sig = new List<int>(tokens.Count);
            for (int i = 0; i < tokens.Count; ++i)
                if (tokens[i].IsSignificant)
                    sig.Add(i);
            Scan(text, tokens, sig, true, (off, kind, name) => res.Add(new OutputFinding(off, kind, name)));
            return res;
        }

        static void Scan(String text, List<JsToken> tokens, List<int> sig, bool output, Action<int, String, String> report)
        {
            var n = sig.Count;
            var tok = new JsToken[n];
            for (int i = 0; i < n; ++i)
                tok[i] = tokens[sig[i]];
            bool Punct(int i, String p) => (i >= 0) && (i < n) && (tok[i].Kind == JsTokenKind.Punctuator) && tok[i].Is(text, p);
            bool Ident(int i) => (i >= 0) && (i < n) && (tok[i].Kind == JsTokenKind.Identifier);
            String Text(int i) => tok[i].GetText(text);
            bool NewlineBefore(int i)
            {
                if (i <= 0)
                    return true;
                return text.IndexOf('\n', tok[i - 1].End, tok[i].Start - tok[i - 1].End) >= 0;
            }
            bool IsOpen(int i) => Punct(i, "(") || Punct(i, "[") || Punct(i, "{") || ((i < n) && (tok[i].Kind == JsTokenKind.TemplateHead));
            bool IsClose(int i) => Punct(i, ")") || Punct(i, "]") || Punct(i, "}") || ((i < n) && (tok[i].Kind == JsTokenKind.TemplateTail));
            bool EndsExpression(int i)
            {
                if (i < 0)
                    return false;
                var k = tok[i].Kind;
                if ((k == JsTokenKind.Identifier) || (k == JsTokenKind.Number) || (k == JsTokenKind.String) || (k == JsTokenKind.Template) || (k == JsTokenKind.TemplateTail) || (k == JsTokenKind.Regex) || (k == JsTokenKind.PrivateName))
                    return true;
                return Punct(i, ")") || Punct(i, "]") || Punct(i, "}");
            }
            bool StartsStatement(int i)
            {
                var k = tok[i].Kind;
                return (k == JsTokenKind.Identifier) || (k == JsTokenKind.Number) || (k == JsTokenKind.String) || (k == JsTokenKind.Template) || (k == JsTokenKind.TemplateHead) || (k == JsTokenKind.PrivateName) || Punct(i, "{");
            }
            // A declaration keyword that starts a declaration statement
            bool IsDeclAt(int i)
            {
                if (!Ident(i) || !DeclKeywords.Contains(Text(i)))
                    return false;
                if (!(Ident(i + 1) || Punct(i + 1, "{") || Punct(i + 1, "[")))
                    return false;
                if (i == 0)
                    return true;
                return Punct(i - 1, ";") || Punct(i - 1, "{") || Punct(i - 1, "}") || Punct(i - 1, "(") || NewlineBefore(i);
            }
            // Parse the declarators of one declaration statement starting at keyword index k; returns the index where it stopped, nextStatement is the start of the following statement (-1: none, ex: a closing bracket)
            int Parse(int k, List<Declarator> into, out int nextStatement)
            {
                nextStatement = -1;
                var j = k + 1;
                var d = new Declarator { IdStart = j };
                int depth = 0;
                while (j < n)
                {
                    if (depth == 0)
                    {
                        if (Punct(j, ")") || Punct(j, "]") || Punct(j, "}") || (tok[j].Kind == JsTokenKind.TemplateTail))
                            break;
                        if (Punct(j, ";"))
                        {
                            nextStatement = j + 1;
                            break;
                        }
                        if (Punct(j, ","))
                        {
                            Close(d, j, into);
                            d = new Declarator { IdStart = j + 1 };
                            ++j;
                            continue;
                        }
                        if (Punct(j, "=") && (d.InitStart < 0))
                        {
                            d.IdEnd = j;
                            d.InitStart = j + 1;
                            ++j;
                            continue;
                        }
                        // for (const x of / in ...)
                        if ((d.InitStart < 0) && Ident(j) && (j > d.IdStart) && ((Text(j) == "of") || (Text(j) == "in")))
                            break;
                        // ASI: a new statement on the next line
                        if ((j > d.IdStart) && NewlineBefore(j) && EndsExpression(j - 1) && StartsStatement(j))
                        {
                            var w = tok[j].Kind == JsTokenKind.Identifier ? Text(j) : null;
                            if ((w != "in") && (w != "of") && (w != "instanceof"))
                            {
                                nextStatement = j;
                                break;
                            }
                        }
                    }
                    if (IsOpen(j))
                        ++depth;
                    else if (IsClose(j))
                        --depth;
                    ++j;
                }
                Close(d, j, into);
                return j;
            }
            void Close(Declarator d, int end, List<Declarator> into)
            {
                if (d.InitStart >= 0)
                    d.InitEnd = end;
                else
                    d.IdEnd = end;
                if (d.IdEnd <= d.IdStart)
                    return;
                d.IsPattern = Punct(d.IdStart, "{") || Punct(d.IdStart, "[");
                into.Add(d);
            }
            // Identifier references in [from, to) (not property names after '.', not object literal keys)
            void Refs(int from, int to, List<int> into)
            {
                for (int i = from; i < to; ++i)
                {
                    if (!Ident(i))
                        continue;
                    if (Punct(i - 1, ".") || Punct(i - 1, "?."))
                        continue;
                    if (Punct(i + 1, ":") && (Punct(i - 1, "{") || Punct(i - 1, ",")))
                        continue;
                    into.Add(i);
                }
            }
            // Walk a binding pattern [i, end): collect bound names, and the identifier tokens of default values / computed keys
            bool WalkTarget(ref int i, int end, List<String> binds, List<int> exprRefs)
            {
                if (i >= end)
                    return false;
                if (Punct(i, "{") || Punct(i, "["))
                    return WalkPattern(ref i, end, binds, exprRefs);
                if (!Ident(i))
                    return false;
                binds?.Add(Text(i));
                ++i;
                return true;
            }
            // Skip an expression up to a ',' or a closer at its own level, collecting references
            void Expr(ref int i, int end, List<int> exprRefs)
            {
                var s = i;
                int depth = 0;
                while (i < end)
                {
                    if (depth == 0 && (Punct(i, ",") || IsClose(i)))
                        break;
                    if (IsOpen(i))
                        ++depth;
                    else if (IsClose(i))
                        --depth;
                    ++i;
                }
                if (exprRefs != null)
                    Refs(s, i, exprRefs);
            }
            bool WalkPattern(ref int i, int end, List<String> binds, List<int> exprRefs)
            {
                if (Punct(i, "{"))
                {
                    ++i;
                    while (i < end && !Punct(i, "}"))
                    {
                        if (Punct(i, "..."))
                        {
                            ++i;
                            if (!WalkTarget(ref i, end, binds, exprRefs))
                                return false;
                        }
                        else
                        {
                            bool shorthand = false;
                            String key = null;
                            if (Punct(i, "["))
                            {
                                ++i;
                                Expr(ref i, end, exprRefs);
                                if (!Punct(i, "]"))
                                    return false;
                                ++i;
                            }
                            else
                            {
                                if (Ident(i))
                                    key = Text(i);
                                ++i;
                                shorthand = !Punct(i, ":");
                            }
                            if (shorthand)
                            {
                                if (key == null)
                                    return false;
                                binds?.Add(key);
                            }
                            else
                            {
                                if (!Punct(i, ":"))
                                    return false;
                                ++i;
                                if (!WalkTarget(ref i, end, binds, exprRefs))
                                    return false;
                            }
                            if (Punct(i, "="))
                            {
                                ++i;
                                Expr(ref i, end, exprRefs);
                            }
                        }
                        if (Punct(i, ","))
                            ++i;
                        else if (!Punct(i, "}"))
                            return false;
                    }
                    if (!Punct(i, "}"))
                        return false;
                    ++i;
                    return true;
                }
                if (Punct(i, "["))
                {
                    ++i;
                    while (i < end && !Punct(i, "]"))
                    {
                        if (Punct(i, ","))
                        {
                            ++i;
                            continue;
                        }
                        if (Punct(i, "..."))
                            ++i;
                        if (!WalkTarget(ref i, end, binds, exprRefs))
                            return false;
                        if (Punct(i, "="))
                        {
                            ++i;
                            Expr(ref i, end, exprRefs);
                        }
                        if (Punct(i, ","))
                            ++i;
                        else if (!Punct(i, "]"))
                            return false;
                    }
                    if (!Punct(i, "]"))
                        return false;
                    ++i;
                    return true;
                }
                return false;
            }
            void Bound(Declarator d, List<String> into)
            {
                if (!d.IsPattern)
                {
                    if (Ident(d.IdStart))
                        into.Add(Text(d.IdStart));
                    return;
                }
                var i = d.IdStart;
                WalkPattern(ref i, d.IdEnd, into, null);
            }

            //  Output mode: for each token the start of the innermost nested function body (with its parameters) around it, which runs later (-1: none)
            int[] deferStart = null;
            //  Output mode: the functions that are called at once, with the names they bind themselves
            var invoked = new List<(int From, int To, HashSet<String> Names)>();
            if (output)
            {
                var match = new int[n];
                var stack = new Stack<int>();
                for (int i = 0; i < n; ++i)
                {
                    match[i] = -1;
                    if (IsOpen(i))
                        stack.Push(i);
                    else if (IsClose(i) && (stack.Count > 0))
                    {
                        var o = stack.Pop();
                        match[o] = i;
                        match[i] = o;
                    }
                }
                deferStart = Enumerable.Repeat(-1, n).ToArray();
                int Matching(int i) => (i >= 0) && (i < n) ? match[i] : -1;
                void Mark(int from, int to)
                {
                    if ((from < 0) || (to < from))
                        return;
                    // Called at once: "function(){..}()", "(()=>x)()", "(function(){..})()": it runs now, but the names it binds
                    // itself (parameters, declarations) are its own, not the outer declaration's (approximation: by name)
                    if (Punct(to + 1, "(") || (Punct(to + 1, ")") && Punct(to + 2, "(")))
                    {
                        var names = new HashSet<String>(StringComparer.Ordinal);
                        int body = from;
                        while ((body <= to) && !Punct(body, "{") && !Punct(body, "=>"))
                            ++body;
                        for (int i = from; i < body; ++i)
                            if (Ident(i))
                                names.Add(Text(i));
                        for (int i = body; i <= to; ++i)
                        {
                            if (IsDeclAt(i))
                            {
                                var ds = new List<Declarator>();
                                Parse(i, ds, out _);
                                var tmp = new List<String>();
                                foreach (var d in ds)
                                    Bound(d, tmp);
                                names.UnionWith(tmp);
                            }
                            else if (Ident(i) && ((Text(i) == "function") || (Text(i) == "class")) && Ident(i + 1))
                                names.Add(Text(i + 1));
                            else if (Ident(i) && (Text(i) == "catch") && Punct(i + 1, "(") && Ident(i + 2))
                                names.Add(Text(i + 2));
                        }
                        invoked.Add((from, to, names));
                        return;
                    }
                    for (int i = from; (i <= to) && (i < n); ++i)
                        deferStart[i] = Math.Max(deferStart[i], from);
                }
                for (int i = 0; i < n; ++i)
                {
                    if (Ident(i) && (Text(i) == "function") && !Punct(i - 1, "."))
                    {
                        var p = i + 1;
                        while ((p < n) && !Punct(p, "("))
                            ++p;
                        var pe = Matching(p);
                        if ((pe > p) && Punct(pe + 1, "{"))
                            Mark(i, Matching(pe + 1));
                        continue;
                    }
                    if (Ident(i) && (Text(i) == "class") && !Punct(i - 1, "."))
                    {
                        var p = i + 1;
                        int depth = 0;
                        while ((p < n) && !(Punct(p, "{") && (depth == 0)))
                        {
                            if (Punct(p, "(") || Punct(p, "["))
                                ++depth;
                            else if (Punct(p, ")") || Punct(p, "]"))
                                --depth;
                            ++p;
                        }
                        Mark(i, Matching(p));
                        continue;
                    }
                    if (Punct(i, "=>"))
                    {
                        var ps = Punct(i - 1, ")") ? Matching(i - 1) : i - 1;
                        if (Ident(ps - 1) && (Text(ps - 1) == "async"))
                            --ps;
                        int be;
                        if (Punct(i + 1, "{"))
                            be = Matching(i + 1);
                        else
                        {
                            be = i + 1;
                            int depth = 0;
                            while (be < n)
                            {
                                if ((depth == 0) && (Punct(be, ",") || Punct(be, ";") || IsClose(be)))
                                    break;
                                if (IsOpen(be))
                                    ++depth;
                                else if (IsClose(be))
                                    --depth;
                                ++be;
                            }
                            --be;
                        }
                        Mark(ps, be);
                        continue;
                    }
                    // A method: name(params){ ... } in an object literal or a class (not if / for / while ...)
                    if (Punct(i, "(") && (Ident(i - 1) || Punct(i - 1, "]")))
                    {
                        var w = Ident(i - 1) ? Text(i - 1) : null;
                        if ((w == "if") || (w == "for") || (w == "while") || (w == "switch") || (w == "catch") || (w == "with") || (w == "function"))
                            continue;
                        var pe = Matching(i);
                        if ((pe > i) && Punct(pe + 1, "{"))
                        {
                            var ms = Ident(i - 1) ? i - 1 : Matching(i - 1);
                            var be = Matching(pe + 1);
                            if ((ms >= 0) && (be > pe))
                                for (int k = ms; k <= be; ++k)
                                    deferStart[k] = Math.Max(deferStart[k], ms);
                        }
                    }
                }
            }

            //  Every declaration keyword is visited (also the ones inside the initializers of an outer declaration: function bodies);
            //  a declaration merged into the group of the one before it doesn't start a group of its own
            var merged = new HashSet<int>();
            for (int si = 0; si < n; ++si)
            {
                if (merged.Contains(si) || !IsDeclAt(si))
                    continue;
                var kind = Text(si);
                var decls = new List<Declarator>();
                Parse(si, decls, out var nextStmt);
                // The minifier merges the following declarations of the same kind (after a ";" or a line break)
                while ((nextStmt >= 0) && IsDeclAt(nextStmt) && (Text(nextStmt) == kind))
                {
                    merged.Add(nextStmt);
                    Parse(nextStmt, decls, out nextStmt);
                }
                if (output)
                {
                    if (decls.Count < 2)
                        continue;
                    var bound = new List<String>[decls.Count];
                    for (int k = 0; k < decls.Count; ++k)
                    {
                        bound[k] = new List<String>();
                        Bound(decls[k], bound[k]);
                    }
                    for (int k = 0; k < decls.Count - 1; ++k)
                    {
                        var d = decls[k];
                        var reads = new List<int>();
                        // var: the name may be a parameter / an earlier var of the function that the declaration declares again
                        // (legal, common in vendored code), so only the minifier bug's shape is reported: a pattern default / computed key
                        if ((d.InitStart >= 0) && (kind != "var"))
                            Refs(d.InitStart, d.InitEnd, reads);
                        if (d.IsPattern)
                        {
                            var p = d.IdStart;
                            WalkPattern(ref p, d.IdEnd, null, reads);
                        }
                        var dEnd = d.InitStart >= 0 ? d.InitEnd : d.IdEnd;
                        foreach (var r in reads)
                        {
                            if (deferStart[r] >= d.IdStart)
                                continue;
                            var name = Text(r);
                            // A name that a function called at once inside this declarator binds itself
                            bool local = false;
                            foreach (var (f, t, names) in invoked)
                                if ((f >= d.IdStart) && (t < dEnd) && (r >= f) && (r <= t) && names.Contains(name))
                                {
                                    local = true;
                                    break;
                                }
                            if (local)
                                continue;
                            bool declaredLater = false;
                            for (int m = k + 1; (m < decls.Count) && !declaredLater; ++m)
                                declaredLater = bound[m].Contains(name);
                            if (declaredLater && (kind == "var"))
                                for (int m = 0; (m <= k) && declaredLater; ++m)
                                    declaredLater = !bound[m].Contains(name);
                            if (declaredLater)
                            {
                                report(tok[r].Start, kind, name);
                                break;
                            }
                        }
                    }
                    continue;
                }
                if ((decls.Count < 2) || decls[0].IsPattern)
                    continue;
                var earlier = new List<String>();
                for (int k = 0; k < decls.Count; ++k)
                {
                    var d = decls[k];
                    if ((k > 0) && d.IsPattern)
                    {
                        var initRefs = new List<int>();
                        if (d.InitStart >= 0)
                            Refs(d.InitStart, d.InitEnd, initRefs);
                        bool initReadsEarlier = false;
                        foreach (var r in initRefs)
                            if (earlier.Contains(Text(r)))
                                initReadsEarlier = true;
                        if (!initReadsEarlier)
                        {
                            // This declarator is moved to the front: its defaults / computed keys must not read an earlier declarator
                            var exprRefs = new List<int>();
                            var p = d.IdStart;
                            if (WalkPattern(ref p, d.IdEnd, null, exprRefs))
                            {
                                foreach (var r in exprRefs)
                                {
                                    var name = Text(r);
                                    if (!earlier.Contains(name))
                                        continue;
                                    report(tok[r].Start, kind, name);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                    Bound(d, earlier);
                }
            }
        }
    }
}
