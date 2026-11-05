using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ usage patterns (JavaScript)
        //
        //  The misuse of the framework APIs that runs without an error and breaks a guide rule silently: an OnClick that forgets to return
        //  its promise, a click handler on a card without the modifier check, a danger button with a plain confirmation, a native disabled
        //  without a reason, a hand-made table, own leave prompts and drafts, dropped handles. Each check looks at one call or handler
        //  and only reports what it can see in the file (a handler given by name is looked up in the file, else skipped).

        /// <summary>
        /// The significant tokens of a script with helpers.
        /// </summary>
        sealed class Toks
        {
            public readonly String T;
            public readonly List<JsToken> S;

            public Toks(String text, List<JsToken> sig)
            {
                T = text;
                S = sig;
            }

            public int N => S.Count;

            /// <summary>Local names of Sw members taken by destructuring (const { Button, Dialog: D } = Sw): local name -> member.</summary>
            public readonly Dictionary<String, String> SwNames = new Dictionary<String, String>(StringComparer.Ordinal);

            /// <summary>Local names bound to a request function (const send = Sw.Net.Send, const { Send } = Sw.Net).</summary>
            public readonly HashSet<String> ReqNames = new HashSet<String>(StringComparer.Ordinal);

            /// <summary>Local functions that make a Sw component from their arguments: name -> (component, index of the options argument, index of the OnClick argument), -1 when none.</summary>
            public readonly Dictionary<String, (String Component, int Options, int OnClick)> Wrappers = new Dictionary<String, (String, int, int)>(StringComparer.Ordinal);

            /// <summary>Find the aliases (call once).</summary>
            public void FindAliases()
            {
                for (int i = 0; i + 2 < N; ++i)
                {
                    //  const { A, B: c } = Sw / = Sw.Net / = Net
                    if (P(i, "{") && (Match(i) is int c) && (c > i) && P(c + 1, "=") && !P(c + 2, "="))
                    {
                        bool sw = Id(c + 2, "Sw") && !P(c + 3, ".");
                        bool net = (Id(c + 2, "Sw") && P(c + 3, ".") && Id(c + 4, "Net")) || (Id(c + 2, "Net") && SwNames.ContainsKey("Net"));
                        if (sw || net)
                            for (int k = i + 1; k < c; ++k)
                                if (Id(k) && (P(k - 1, "{") || P(k - 1, ",")))
                                {
                                    var local = P(k + 1, ":") && Id(k + 2) ? Tx(k + 2) : Tx(k);
                                    if (sw)
                                        SwNames[local] = Tx(k);
                                    else if (Tx(k) == "Send" || Tx(k) == "Get" || Tx(k) == "Cached")
                                        ReqNames.Add(local);
                                }
                    }
                    //  A local wrapper of a Sw component: const B = (id, o) => new Sw.Button(o) / function mk(name, fn) { new Sw.Button({ Text: name, OnClick: fn }) }
                    if (Id(i) && !Keywords.Contains(Tx(i)) && ((P(i + 1, "=") && (Id(i - 1, "const") || Id(i - 1, "let") || Id(i - 1, "var"))) || Id(i - 1, "function")))
                    {
                        int h = Id(i - 1, "function") ? i - 1 : i + 2;
                        bool isAsync = Id(h, "async") && !Id(i - 1, "function");
                        int fh = h;
                        if (isAsync)
                            ++h;
                        int po = Id(h, "function") ? h + 1 + (Id(h + 1) ? 1 : 0) : h;
                        if (P(po, "(") && ParseFn(this, fh, out var wf))
                        {
                            var ps = new List<String>();
                            int pc = Match(po);
                            for (int k = po + 1; (k < pc) && (pc > 0); ++k)
                                if (Id(k) && (P(k - 1, "(") || P(k - 1, ",")))
                                    ps.Add(Tx(k));
                            for (int k = wf.Start; (k <= wf.End) && (ps.Count > 0); ++k)
                                if (Id(k, "Sw") && P(k + 1, ".") && Id(k + 2) && Char.IsUpper(Tx(k + 2)[0]) && P(k + 3, "("))
                                {
                                    int a = k + 4;
                                    int oi = Id(a) && P(a + 1, ")") ? ps.IndexOf(Tx(a)) : -1;
                                    int ci = -1;
                                    int mc = P(a, "{") ? Match(a) : -1;
                                    for (int m = a + 1; m < mc; ++m)
                                        if (Id(m, "OnClick") && P(m + 1, ":") && Id(m + 2) && ps.Contains(Tx(m + 2)))
                                            ci = ps.IndexOf(Tx(m + 2));
                                    if ((oi >= 0) || (ci >= 0))
                                    {
                                        Wrappers[Tx(i)] = (Tx(k + 2), oi, ci);
                                        if (oi >= 0)
                                            SwNames[Tx(i)] = Tx(k + 2);
                                        break;
                                    }
                                }
                        }
                    }
                    //  const send = Sw.Net.Send / Net.Send (also .bind(...))
                    if (Id(i) && P(i + 1, "=") && !P(i - 1, "."))
                    {
                        int k = i + 2;
                        if (Id(k, "Sw") && P(k + 1, "."))
                            k += 2;
                        if (Id(k, "Net") && P(k + 1, ".") && (Id(k + 2, "Send") || Id(k + 2, "Get") || Id(k + 2, "Cached")) && !P(k + 3, "("))
                            ReqNames.Add(Tx(i));
                    }
                }
            }

            /// <summary>True if the name at i is a Sw component (Sw.X, or a destructured X) with an upper case name: the call / new at i + 1 makes it.</summary>
            public bool SwComponent(int i) => Id(i) && ((P(i - 1, ".") && Id(i - 2, "Sw") && Char.IsUpper(Tx(i)[0])) || (!P(i - 1, ".") && SwNames.ContainsKey(Tx(i))));

            public bool P(int i, String p) => (i >= 0) && (i < S.Count) && (S[i].Kind == JsTokenKind.Punctuator) && S[i].Is(T, p);

            public bool Id(int i, String p = null) => (i >= 0) && (i < S.Count) && (S[i].Kind == JsTokenKind.Identifier) && ((p == null) || S[i].Is(T, p));

            public String Str(int i) => (i >= 0) && (i < S.Count) && (S[i].Kind == JsTokenKind.String) ? JsLexer.DecodeStringLiteral(S[i].GetText(T)) : null;

            public String Tx(int i) => (i >= 0) && (i < S.Count) ? S[i].GetText(T) : "";

            public int Match(int open) => MatchBrace(S, T, open);

            public bool Key(int i) => Id(i) && P(i + 1, ":") && (P(i - 1, "{") || P(i - 1, ","));

            /// <summary>The source text from token a to token b (inclusive).</summary>
            public String Span(int a, int b) => (a < 0) || (b < a) || (b >= S.Count) ? "" : T.Substring(S[a].Start, S[b].End - S[a].Start);

            /// <summary>True if token i is a call of a member chain ending in the given names: Call(i, "Sw", "Dialog", "Confirm") for "Sw.Dialog.Confirm(" at the last name.</summary>
            public bool Chain(int i, params String[] names)
            {
                for (int k = names.Length - 1, j = i; k >= 0; --k, j -= 2)
                {
                    if (!Id(j, names[k]))
                        return false;
                    if ((k > 0) && !P(j - 1, ".") && !P(j - 1, "?."))
                        return false;
                }
                return true;
            }

            /// <summary>The arguments of a call whose "(" is at open: (first token, last token) per argument.</summary>
            public List<(int A, int B)> Args(int open)
            {
                var r = new List<(int, int)>();
                int close = Match(open);
                if (close < 0)
                    return r;
                int s = open + 1;
                for (int j = open + 1; j < close; ++j)
                {
                    if (P(j, "(") || P(j, "[") || P(j, "{"))
                    {
                        int m = Match(j);
                        if (m < 0)
                            return r;
                        j = m;
                        continue;
                    }
                    if (P(j, ","))
                    {
                        if (j > s)
                            r.Add((s, j - 1));
                        s = j + 1;
                    }
                }
                if (s < close)
                    r.Add((s, close - 1));
                return r;
            }

            /// <summary>The literal of the token range (a string, or the literal of a _TF("...") call), null otherwise.</summary>
            public String Lit((int A, int B) a) => LiteralAt(S, T, a.A);

            /// <summary>True if the range is just the identifier (true, false, null).</summary>
            public bool Is((int A, int B) a, String id) => (a.A == a.B) && Id(a.A, id);
        }

        /// <summary>
        /// A function (an arrow or a function expression / declaration) in the tokens.
        /// </summary>
        struct Fn
        {
            public bool Async;
            /// <summary>True for a block body ("{ ... }"), false for an arrow with an expression body.</summary>
            public bool Block;
            /// <summary>The first token of the body ("{" of a block body).</summary>
            public int Start;
            /// <summary>The last token of the body ("}" of a block body).</summary>
            public int End;
        }

        /// <summary>
        /// Parse the function whose first token is at k (async? function name? (...) { } | async? (...) => body | async? x => body).
        /// </summary>
        static bool ParseFn(Toks t, int k, out Fn f)
        {
            f = default;
            int i = k;
            if (t.Id(i, "async") && !t.P(i + 1, "=>"))
            {
                f.Async = true;
                ++i;
            }
            int arrow;
            if (t.Id(i, "function"))
            {
                ++i;
                if (t.P(i, "*"))
                    ++i;
                if (t.Id(i))
                    ++i;
                if (!t.P(i, "("))
                    return false;
                int c = t.Match(i);
                if ((c < 0) || !t.P(c + 1, "{"))
                    return false;
                f.Block = true;
                f.Start = c + 1;
                f.End = t.Match(c + 1);
                return f.End > f.Start;
            }
            if (t.P(i, "("))
            {
                int c = t.Match(i);
                if ((c < 0) || !t.P(c + 1, "=>"))
                    return false;
                arrow = c + 1;
            }
            else if (t.Id(i) && t.P(i + 1, "=>"))
                arrow = i + 1;
            else
                return false;
            int b = arrow + 1;
            if (t.P(b, "{"))
            {
                f.Block = true;
                f.Start = b;
                f.End = t.Match(b);
                return f.End > f.Start;
            }
            //  An expression body: up to a ",", ";" or a closing bracket at depth 0
            int e = b;
            for (; e < t.N; ++e)
            {
                if (t.P(e, "(") || t.P(e, "[") || t.P(e, "{"))
                {
                    int m = t.Match(e);
                    if (m < 0)
                        break;
                    e = m;
                    continue;
                }
                if (t.P(e, ",") || t.P(e, ";") || t.P(e, ")") || t.P(e, "]") || t.P(e, "}"))
                    break;
            }
            f.Start = b;
            f.End = e - 1;
            return f.End >= f.Start;
        }

        /// <summary>
        /// The tokens of a function body that belong to it (not to functions nested inside it).
        /// </summary>
        static IEnumerable<int> Own(Toks t, Fn f)
        {
            int from = f.Block ? f.Start + 1 : f.Start, to = f.Block ? f.End - 1 : f.End;
            for (int j = from; j <= to; ++j)
            {
                if (t.Id(j, "function") && ParseFn(t, j, out var inner) && inner.Block)
                {
                    j = inner.End;
                    continue;
                }
                if (t.P(j, "=>") && t.P(j + 1, "{"))
                {
                    var m = t.Match(j + 1);
                    if (m > 0)
                    {
                        j = m;
                        continue;
                    }
                }
                yield return j;
            }
        }

        /// <summary>All functions of the file (for "the innermost function around a token").</summary>
        static List<Fn> AllFunctions(Toks t)
        {
            var r = new List<Fn>();
            for (int i = 0; i < t.N; ++i)
            {
                bool start = t.Id(i, "function") || (t.Id(i, "async") && (t.P(i + 1, "(") || t.Id(i + 1, "function") || (t.Id(i + 1) && t.P(i + 2, "=>"))))
                    || (t.P(i, "(") && !t.Id(i - 1, "async") && (t.Match(i) is int c) && (c > 0) && t.P(c + 1, "=>"))
                    || (t.Id(i) && t.P(i + 1, "=>") && !t.Id(i - 1, "async"));
                if (start && ParseFn(t, i, out var f))
                    r.Add(f);
            }
            return r;
        }

        static Fn? Innermost(List<Fn> fns, int at)
        {
            Fn? best = null;
            foreach (var f in fns)
                if ((f.Start <= at) && (f.End >= at) && ((best == null) || (f.End - f.Start < best.Value.End - best.Value.Start)))
                    best = f;
            return best;
        }

        /// <summary>
        /// The function given at k: an inline function, or a name declared in the file (function x() / const x = () =>).
        /// </summary>
        static bool HandlerAt(Toks t, int k, out Fn f)
        {
            if (ParseFn(t, k, out f))
                return true;
            if (t.Id(k) && (t.P(k + 1, ")") || t.P(k + 1, ",") || t.P(k + 1, ";") || (k + 1 >= t.N)))
            {
                var name = t.Tx(k);
                for (int i = 0; i + 1 < t.N; ++i)
                {
                    if (t.Id(i, "function") && t.Id(i + 1, name) && ParseFn(t, i, out f))
                        return true;
                    if (t.Id(i, name) && t.P(i + 1, "=") && (t.Id(i - 1, "const") || t.Id(i - 1, "let") || t.Id(i - 1, "var")) && ParseFn(t, i + 2, out f))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The variables made by document.createElement("tag") / Sw.el("tag.class") (the tag) and by new Sw.X(...) / Sw.X(...) ("Sw.X").
        /// </summary>
        static Dictionary<String, (String Kind, int At, int Call)> Variables(Toks t)
        {
            var r = new Dictionary<String, (String, int, int)>(StringComparer.Ordinal);
            for (int i = 1; i + 3 < t.N; ++i)
            {
                if (!t.Id(i) || !t.P(i + 1, "="))
                    continue;
                if (!(t.Id(i - 1, "const") || t.Id(i - 1, "let") || t.Id(i - 1, "var") || t.P(i - 1, ";") || t.P(i - 1, "{") || t.P(i - 1, "}") || t.P(i - 1, ",")))
                    continue;
                int v = i + 2;
                if (t.Id(v, "await"))
                    ++v;
                String kind = null;
                int call = -1;
                if (t.Id(v, "document") && t.P(v + 1, ".") && t.Id(v + 2, "createElement") && t.P(v + 3, "(") && (t.Str(v + 4) is String tag))
                {
                    kind = tag.ToLowerInvariant();
                    call = v + 3;
                }
                else if (t.Id(v, "Sw") && t.P(v + 1, ".") && t.Id(v + 2, "el") && t.P(v + 3, "(") && (t.Str(v + 4) is String spec))
                {
                    kind = Regex.Match(spec, @"^[A-Za-z][A-Za-z0-9-]*").Value.ToLowerInvariant();
                    if (kind.Length == 0)
                        kind = "div";
                    call = v + 3;
                }
                else if (t.Chain(v + 2, "Object", "assign") && t.P(v + 3, "(") && t.Id(v + 4, "document") && t.Id(v + 6, "createElement") && (t.Str(v + 8) is String atag))
                {
                    //  Object.assign(document.createElement("input"), { ... }): the options are the properties
                    kind = atag.ToLowerInvariant();
                    call = v + 3;
                }
                else
                {
                    int s = t.Id(v, "new") ? v + 1 : v;
                    if (t.Id(s, "Sw") && t.P(s + 1, ".") && t.Id(s + 2) && Char.IsUpper(t.Tx(s + 2)[0]) && t.P(s + 3, "("))
                    {
                        kind = "Sw." + t.Tx(s + 2);
                        call = s + 3;
                    }
                    else if (t.Id(s) && t.SwNames.TryGetValue(t.Tx(s), out var member) && Char.IsUpper(member[0]) && t.P(s + 1, "("))
                    {
                        kind = "Sw." + member;
                        call = s + 1;
                    }
                }
                if (kind != null)
                    r[t.Tx(i)] = (kind, i, call);
            }
            //  Aliases: const a = b; const a = b.E (the element of a Sw control); const a = x.querySelector("tr") / closest("li")
            bool EndsHere(int k) => (k + 1 >= t.N) || t.P(k + 1, ";") || t.P(k + 1, ",") || t.P(k + 1, ")") || t.P(k + 1, "}") || (t.T.IndexOf('\n', t.S[k].End, t.S[k + 1].Start - t.S[k].End) >= 0);
            for (int pass = 0; pass < 2; ++pass)
                for (int i = 1; i + 2 < t.N; ++i)
                {
                    if (!t.Id(i) || !t.P(i + 1, "=") || t.P(i + 2, "=") || r.ContainsKey(t.Tx(i)) || t.P(i - 1, "."))
                        continue;
                    int v = i + 2;
                    if (t.Id(v) && EndsHere(v) && r.TryGetValue(t.Tx(v), out var same))
                        r[t.Tx(i)] = (same.Item1, i, same.Item3);
                    else if (t.Id(v) && t.P(v + 1, ".") && t.Id(v + 2, "E") && EndsHere(v + 2) && r.TryGetValue(t.Tx(v), out var ctl) && ctl.Item1.StartsWith("Sw.", StringComparison.Ordinal))
                        r[t.Tx(i)] = (ctl.Item1 == "Sw.Button" ? "button" : FieldControls.Contains(ctl.Item1) ? ctl.Item1 : "div", i, ctl.Item3);
                    else
                        for (int k = v; (k < t.N) && (k <= v + 12); ++k)
                        {
                            if ((t.Id(k, "querySelector") || t.Id(k, "closest")) && t.P(k - 1, ".") && t.P(k + 1, "(") && (t.Str(k + 2) is String sel) && t.P(k + 3, ")") && EndsHere(k + 3))
                            {
                                var last = sel.Split(new[] { ' ', '>', '+', '~' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                                var tag = Regex.Match(last, @"^[A-Za-z][A-Za-z0-9-]*").Value.ToLowerInvariant();
                                if (tag.Length > 0)
                                    r[t.Tx(i)] = (tag, i, -2);   //  -2: found in the document, not made here
                                break;
                            }
                            if (EndsHere(k))
                                break;
                        }
                }
            return r;
        }

        static readonly HashSet<String> NotControls = new HashSet<String>(StringComparer.Ordinal)
        {
            "div", "span", "li", "ul", "ol", "tr", "td", "th", "tbody", "thead", "table", "section", "article", "img", "p", "figure", "header",
            "footer", "aside", "nav", "main", "dl", "dt", "dd", "svg", "canvas", "pre", "code", "h1", "h2", "h3", "h4", "h5", "h6",
            "Sw.Card", "Sw.Tile", "Sw.Kpi", "Sw.Group", "Sw.Facts", "Sw.Badge",
        };
        static readonly Regex ItemName = new Regex(@"(?:^(?:tr|td|li|div|span|row|card|tile|item|cell|entry)s?$|(?:Row|Card|Tile|Item|Cell|Entry)(?:El|E|Elem|Element|Div|Node)?$|^(?:row|card|tile|item|cell|entry)(?:El|E|Elem|Element|Div|Node)$)", RegexOptions.CultureInvariant);
        static readonly Regex ControlName = new Regex(@"(?:btn|button|link|anchor|input|field|select|toggle|check|radio|menu)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ModifierCheck = new Regex(@"\b(?:BadClick|ModifierClick|IsPureEvent|ctrlKey|metaKey|shiftKey|altKey)\b", RegexOptions.CultureInvariant);
        static readonly Regex DestructiveApi = new Regex(@"(?:^|[/.])(?:Delete|Remove|Kill|Purge|Revoke|Erase|Wipe|Destroy|Withdraw|Reboot|Terminate|Uninstall|Reset|EndSession|EndProcess|Drop|Clear|Truncate|Discard|Empty|Deactivate|Unregister|Unlink|Overwrite|Shutdown|Expunge|Obliterate|Nuke|Ban|Block)(?:[A-Z_]\w*)?(?:[?#].*)?$", RegexOptions.CultureInvariant);
        static readonly Regex AsksFirst = new Regex(@"\b(?:Confirm|ConfirmDanger|Undo|confirm|Prompt|DangerTitle)\b|\bDanger\s*\.\s*(?:Run|Confirm)\b", RegexOptions.CultureInvariant);
        static readonly Regex SaveApi = new Regex(@"(?:^|[/.])(?:Save|Set|Update|Create|Add|Put|Write|Store|Submit|Change|Edit|Insert|Upsert|Modify|Rename|Register|Invite)[A-Z_]?\w*(?:[?#].*)?$", RegexOptions.CultureInvariant);
        const String HandMadeTable = "a hand-made table: no sorting, counts, search, row actions, modifier clicks, keyboard cell focus or copy (LST-2 to LST-7, INP-2, INP-31); use Sw.Table.Create({ Element, Rows, Cols }) for local rows (SetRows(rows, true) for live ones) or { Url } for a server table (explore/README.md \"Lists of rows: use Sw.Table, not a hand-made table\"); a small fixed key / value list is Sw.Facts";
        static readonly Regex DraftKey = new Regex(@"draft|unsent|autosave|auto-save", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex SecretText = new Regex(@"(?i:api_?key|secret|token|passw|pwd|private_?key|access_?key)|\.(?:Key|Hash)\b", RegexOptions.CultureInvariant);
        static readonly HashSet<String> FieldControls = new HashSet<String>(StringComparer.Ordinal)
        {
            "Sw.TextBox", "Sw.NumberBox", "Sw.Select", "Sw.Toggle", "Sw.Slider", "Sw.Segmented", "Sw.DateBox", "Sw.DatePicker", "Sw.ColorPicker",
            "Sw.CheckBox", "Sw.Checkbox", "Sw.TagBox", "Sw.ComboBox", "Sw.Radio", "Sw.RadioGroup", "Sw.TimeBox",
        };
        static readonly HashSet<String> FieldTags = new HashSet<String>(StringComparer.Ordinal) { "input", "textarea", "select" };

        /// <summary>The literal texts of a token range (strings and template parts), joined.</summary>
        static String Literals(Toks t, (int A, int B) a)
        {
            var sb = new System.Text.StringBuilder();
            for (int j = a.A; j <= a.B; ++j)
            {
                if (t.S[j].Kind == JsTokenKind.String)
                    sb.Append(JsLexer.DecodeStringLiteral(t.Tx(j)));
                else if (t.S[j].IsTemplate)
                    sb.Append(t.Tx(j).Trim('`', '}', '$', '{'));
            }
            return sb.ToString();
        }

        /// <summary>A request at token i: Sw.Net.Send / Get / Cached (also Sw.Activity.Send), fetch(...), the legacy sendRequest(...).</summary>
        static bool IsRequest(Toks t, int i) =>
            (t.P(i + 1, "(") && (t.Id(i, "Send") || t.Id(i, "Get") || t.Id(i, "Cached") || (t.Id(i, "Read") && t.Id(i - 2, "Net"))) && t.P(i - 1, ".") && (t.Id(i - 2, "Net") || t.Id(i - 2, "Activity")))
            || (t.Id(i, "fetch") && t.P(i + 1, "(") && !t.P(i - 1, "."))
            || (t.Id(i, "sendRequest") && t.P(i + 1, "(") && !t.P(i - 1, "."))
            || (t.Id(i) && t.P(i + 1, "(") && !t.P(i - 1, ".") && t.ReqNames.Contains(t.Tx(i)))
            || IsApiCall(t, i);

        /// <summary>A receiver named like a server api object (api, Api, ReturnsApi, server, backend): its methods are requests (also when the object is made in another file).</summary>
        static readonly Regex ApiReceiver = new Regex(@"^(?:api|Api|API|server|backend|remote|[A-Za-z]+Api|[A-Za-z]+API)$", RegexOptions.CultureInvariant);

        /// <summary>A method call on a server api object: api.Remove(id), this.api.Approve(id), ReturnsApi.List(f) (round 4: requests made in a helper module of another file).</summary>
        static bool IsApiCall(Toks t, int i) =>
            t.Id(i) && t.P(i + 1, "(") && t.P(i - 1, ".") && t.Id(i - 2) && ApiReceiver.IsMatch(t.Tx(i - 2)) && Char.IsUpper(t.Tx(i)[0])
            && !Regex.IsMatch(t.Tx(i), @"^(?:Api|Url|Path|Base|Root|Abs|Full|Make\w*|Build\w*|To\w*|Format\w*|Parse\w*|Is\w*|Has\w*|Can\w*|On\w*|Label\w*|\w*Text|\w*Date|\w*Name)$")
            && !(t.P(i - 3, ".") && t.Id(i - 4, "Sw")) && !t.Id(i - 2, "Sw");

        /// <summary>The start of the statement around token i inside [from, i] (after the last ";", "{" or "}" at the same depth).</summary>
        static int StatementStart(Toks t, int from, int i)
        {
            int depth = 0;
            for (int k = i - 1; k >= from; --k)
            {
                if (t.P(k, ")") || t.P(k, "]"))
                    ++depth;
                else if (t.P(k, "(") || t.P(k, "["))
                {
                    if (depth == 0)
                        continue;
                    --depth;
                }
                else if ((depth == 0) && (t.P(k, ";") || t.P(k, "{") || t.P(k, "}")))
                    return k + 1;
            }
            return from;
        }

        /// <summary>True if the call at i (its name token) is a statement of its own: its value is dropped.</summary>
        static bool Dropped(Toks t, int nameAt, int chainLength)
        {
            int s = nameAt - (chainLength - 1) * 2;
            if (t.Id(s - 1, "await") || t.Id(s - 1, "void"))
                --s;
            return (s == 0) || t.P(s - 1, ";") || t.P(s - 1, "{") || t.P(s - 1, "}") || (t.P(s - 1, ")") && IsControlHead(t, s - 1)) || t.Id(s - 1, "else");
        }

        /// <summary>True if the ")" at i closes the head of if / for / while (so the next token starts a statement).</summary>
        static bool IsControlHead(Toks t, int close)
        {
            int depth = 0;
            for (int k = close; k >= 0; --k)
            {
                if (t.P(k, ")"))
                    ++depth;
                else if (t.P(k, "(") && (--depth == 0))
                    return t.Id(k - 1, "if") || t.Id(k - 1, "for") || t.Id(k - 1, "while");
            }
            return false;
        }

        /// <summary>The "{" of the object literal that contains the key at i, -1 if none.</summary>
        static int EnclosingObject(Toks t, int i)
        {
            int depth = 0;
            for (int k = i - 1; k >= 0; --k)
            {
                if (t.P(k, "}") || t.P(k, ")") || t.P(k, "]"))
                    ++depth;
                else if (t.P(k, "{") || t.P(k, "(") || t.P(k, "["))
                {
                    if (depth == 0)
                        return t.P(k, "{") ? k : -1;
                    --depth;
                }
            }
            return -1;
        }

        /// <summary>The value range of the key "name" at depth 0 of the object literal at open, null if not there.</summary>
        static (int A, int B)? ObjKey(Toks t, int open, String name)
        {
            int close = t.Match(open);
            if (close < 0)
                return null;
            int depth = 0;
            for (int k = open + 1; k < close; ++k)
            {
                if (t.P(k, "{") || t.P(k, "(") || t.P(k, "["))
                    ++depth;
                else if (t.P(k, "}") || t.P(k, ")") || t.P(k, "]"))
                    --depth;
                else if ((depth == 0) && t.Id(k, name) && (t.P(k - 1, "{") || t.P(k - 1, ",")))
                {
                    if (!t.P(k + 1, ":"))
                        return (k, k);  //  shorthand { Value }
                    int e = k + 2;
                    for (int d = 0; e < close; ++e)
                    {
                        if (t.P(e, "{") || t.P(e, "(") || t.P(e, "["))
                            ++d;
                        else if (t.P(e, "}") || t.P(e, ")") || t.P(e, "]"))
                            --d;
                        else if ((d == 0) && t.P(e, ","))
                            break;
                    }
                    return (k + 2, e - 1);
                }
            }
            return null;
        }

        /// <summary>
        /// The options of Sw.Dialog.Confirm (its 4th argument), in the order of the run time schema (ui/overlay.js ConfirmSchema; the
        /// SwWeb tests compare both).
        /// </summary>
        public static readonly IReadOnlyList<String> ConfirmOptions = new[] { "CancelText", "Danger", "Safe", "OkIcon", "CancelIcon", "OkTitle", "CancelTitle", "Icon", "Target", "Id", "Owner" };

        /// <summary>The members the options form of Sw.Dialog.Confirm({ ... }) adds to <see cref="ConfirmOptions"/>.</summary>
        public static readonly IReadOnlyList<String> ConfirmFormOptions = new[] { "Title", "Text", "OkText" };

        /// <summary>Names an author may write for OkText (the message says the button then reads "OK").</summary>
        static readonly HashSet<String> ConfirmOkWords = new(StringComparer.OrdinalIgnoreCase) { "Confirm", "Ok", "OkLabel", "OkButton", "Button", "Action", "Verb", "Yes", "YesText", "ConfirmText" };

        /// <summary>
        /// The keys at depth 0 of the object literal at open (name: value, "name": value, shorthand name, method name() {}), with the
        /// token of each; null when a key can't be known (a spread ...x or a computed [key]).
        /// </summary>
        static List<(int At, String Name)> ObjKeys(Toks t, int open)
        {
            int close = t.Match(open);
            if (close < 0)
                return null;
            var r = new List<(int, String)>();
            int depth = 0;
            for (int k = open + 1; k < close; ++k)
            {
                bool keyPos = (depth == 0) && (t.P(k - 1, "{") && (k - 1 == open) || t.P(k - 1, ","));
                if (keyPos && t.P(k, "..."))
                    return null;
                if (keyPos && t.P(k, "["))
                    return null;
                if (t.P(k, "{") || t.P(k, "(") || t.P(k, "["))
                    ++depth;
                else if (t.P(k, "}") || t.P(k, ")") || t.P(k, "]"))
                    --depth;
                else if (keyPos)
                {
                    if (t.Id(k))
                        r.Add((k, t.Tx(k)));
                    else if (t.Str(k) is String s)
                        r.Add((k, s));
                }
            }
            return r;
        }

        /// <summary>True if the object literal at open is the options of a Sw component: new Sw.X({ ... }) / Sw.X({ ... }) / Sw.X(a, { ... }).</summary>
        static bool IsSwOptions(Toks t, int open)
        {
            //  Back to the "(" of the call
            int depth = 0;
            for (int k = open - 1; k >= 0; --k)
            {
                if (t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                    ++depth;
                else if (t.P(k, "{") || t.P(k, "["))
                {
                    if (depth == 0)
                        return false;
                    --depth;
                }
                else if (t.P(k, "("))
                {
                    if (depth > 0)
                    {
                        --depth;
                        continue;
                    }
                    return t.SwComponent(k - 1);
                }
            }
            return false;
        }

        /// <summary>
        /// The usage pattern rules of a script (see the section comment).
        /// </summary>
        static void CheckPatterns(Checker.FileContext ctx, List<JsToken> sig)
        {
            var t = new Toks(ctx.Text, sig);
            t.FindAliases();
            var text = ctx.Text;
            var vars = Variables(t);
            var flow = new Flow(t);
            List<Fn> fns = null;
            List<Fn> Fns() => fns ??= AllFunctions(t);
            //  The dialogs of the file: Sw.Dialog.Open / Show (...) calls
            var dialogCalls = new List<(int A, int B)>();
            for (int i = 0; i < t.N; ++i)
                if ((t.Chain(i, "Dialog", "Open") || t.Chain(i, "Dialog", "Show")) && t.P(i + 1, "(") && (t.Match(i + 1) is int dc) && (dc > i))
                    dialogCalls.Add((i, dc));
            var dialogIds = new Dictionary<String, int>(StringComparer.Ordinal);
            //  The OnClick bodies of the file (a dialog busy from a Sw.Button click is cancellable: Cancel aborts the button)
            var onClicks = new List<(int A, int B)>();
            for (int i = 0; i < t.N; ++i)
                if (t.Id(i, "OnClick") && t.Key(i) && ParseFn(t, i + 2, out var ocf))
                    onClicks.Add((ocf.Start, ocf.End));
            //  Variables made as an overlay: Sw.el("div.sw-popup") / a role dialog / tooltip / menu, also set later (className, classList, setAttribute)
            var overlays = new HashSet<String>(StringComparer.Ordinal);
            var overlayMark = new Regex(@"\bsw-(?:popup|dialog|layer|toast|menu|tooltip|hint|sheet|popover)\b|\brole\b[""']?\s*[:,]\s*[""'](?:dialog|alertdialog|tooltip|menu)[""']", RegexOptions.CultureInvariant);
            foreach (var kv in vars)
            {
                var (_, vat, vcall) = kv.Value;
                var made = vcall > 0 ? t.Span(vcall - 2, Math.Max(vcall, t.Match(vcall))) : "";
                var later = Regex.Matches(text, @"\b" + Regex.Escape(kv.Key) + @"\s*\.\s*(?:className\s*[+]?=\s*[^;\n]*|classList\.add\([^)]*\)|setAttribute\(\s*[""']role[""']\s*,\s*[""'][^""']*[""']\s*\))").Cast<Match>().Select(m => m.Value);
                if (overlayMark.IsMatch(made) || later.Any(x => overlayMark.IsMatch(x) || Regex.IsMatch(x, @"[""'](?:dialog|alertdialog|tooltip|menu)[""']")))
                    overlays.Add(kv.Key);
            }
            //  True for a token inside a dialog call (a footer button's save), or for a field that a dialog of its function shows
            bool InDialog(int at, String name = null)
            {
                if (dialogCalls.Any(d => (d.A <= at) && (at <= d.B)))
                    return true;
                if (name == null)
                    return false;
                var f = Innermost(Fns(), at);
                var word = new Regex(@"(?<![\w.$])" + Regex.Escape(name) + @"\b");
                return dialogCalls.Any(d => ((f == null) || ((d.A >= f.Value.Start) && (d.B <= f.Value.End))) && word.IsMatch(t.Span(d.A, d.B)));
            }
            String KindOf(int recvEnd, out String name)
            {
                //  x / x.E / this.x / a.b.x (the last name)
                name = t.Tx(recvEnd);
                if (t.Id(recvEnd, "E") && t.P(recvEnd - 1, ".") && t.Id(recvEnd - 2))
                {
                    name = t.Tx(recvEnd - 2);
                    return vars.TryGetValue(name, out var v) ? v.Kind : "Sw.?";
                }
                if (!t.P(recvEnd - 1, ".") && vars.TryGetValue(name, out var w))
                    return w.Kind;
                return null;
            }
            bool fileHasLabel = Regex.IsMatch(text, @"createElement\(\s*[""']label[""']|\bel\(\s*[""']label\b|\bSw\.Field\b|\bSw\.Form\b");
            bool modules = Regex.IsMatch(text, @"^\s*(?:import|export)\b", RegexOptions.Multiline);
            //  The framework files implement the leave prompt and own the activity list (checked with --framework)
            bool framework = DesignChecker.IsFramework(ctx.WebRel);
            //  The table library itself, and a tbody of a table the file reports already
            bool tableLibrary = Regex.IsMatch(text, @"\bSw\.Table\s*=\s*\w");
            bool fileMakesTable = Regex.IsMatch(text, @"(?:createElement|\bel)\(\s*[""']table\b");
            bool exportsTracked = Regex.IsMatch(text, @"\bSw\.Activity\.(?:Track|Start)\b|\.Export\(|\bWorking\(\s*\{[^}]*\bCancel\b");

            for (int i = 0; i < t.N; ++i)
            {
                //  ---- ux-fbk-2 / ux-saf-3 / ux-saf-1: OnClick handlers
                if (t.Id(i, "OnClick") && t.Key(i) && (flow.At(i + 2) is FnNode ocn))
                    CheckOnClick(ctx, t, flow, i, ocn, dialogCalls);
                //  A handler given to a local wrapper of a Sw component (mk("Save", async () => ...)) is its OnClick
                if (WrapperClick(t, flow, i) is (int wk, FnNode wn))
                    CheckOnClick(ctx, t, flow, wk, wn, dialogCalls);
                //  Sw.OnClick(el, fn) and Sw.Keys.Add(combo, fn): the same destructive check
                if ((t.Chain(i, "Sw", "OnClick") || t.Chain(i, "Keys", "Add")) && t.P(i + 1, "(") && (t.Args(i + 1) is var oa) && (oa.Count > 1) && (flow.At(oa[1].A) is FnNode okn))
                    ReportDestructive(ctx, t, flow.Of(okn));

                //  ---- click / key handlers: addEventListener("click", fn), x.onclick = fn
                int hAt = -1, recv = -1;
                String evt = null;
                if (t.Id(i, "addEventListener") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Str(i + 2) is String ev) && t.P(i + 3, ","))
                {
                    evt = ev;
                    hAt = i + 4;
                    recv = i - 2;
                }
                else if (t.Id(i) && (t.Tx(i) == "onclick" || t.Tx(i) == "onkeydown") && t.P(i - 1, ".") && t.P(i + 1, "="))
                {
                    evt = t.Tx(i).Substring(2);
                    hAt = i + 2;
                    recv = i - 2;
                }
                if ((evt != null) && (flow.At(hAt) is FnNode hn))
                {
                    var hf = hn.F;
                    var heff = flow.Of(hn);
                    var body = t.Span(hf.Start, hf.End);
                    if (evt == "click" && (recv >= 0) && (t.Id(recv) || t.P(recv, ")") || t.P(recv, "]")))
                    {
                        String name = null;
                        var kind = t.Id(recv) ? KindOf(recv, out name) : null;
                        if (name == null)
                        {
                            //  A call or index receiver: document.querySelector("nav")
                            int o = recv;
                            for (int d = 0; o > 0; --o)
                            {
                                if (t.P(o, ")") || t.P(o, "]"))
                                    ++d;
                                else if ((t.P(o, "(") || t.P(o, "[")) && (--d == 0))
                                    break;
                            }
                            while ((o > 1) && (t.Id(o - 1) || t.P(o - 1, ".")))
                                --o;
                            name = Short(t.Span(o, recv));
                        }
                        bool page = t.Id(recv) && !t.P(recv - 1, ".") && PageObjects.Contains(name);
                        bool delegated = Regex.IsMatch(body, @"\.closest\(\s*[""'`]");
                        bool item = (kind != null) ? NotControls.Contains(kind) : (ItemName.IsMatch(name) && !ControlName.IsMatch(name));
                        if ((kind != null) && !NotControls.Contains(kind))
                            item = false;
                        if (!page && (item || (delegated && (kind == null || NotControls.Contains(kind)) && !ControlName.IsMatch(name))) && !ModifierCheck.IsMatch(body) && !heff.Modifier)
                            Add(ctx, Rule.UxInp2, t.S[i].Start, "a click handler on " + (kind != null ? "the " + kind.Replace("Sw.", "Sw.") + " " : "") + "\"" + name + "\"" + (delegated ? " (a delegated item click)" : "") + " runs on a Ctrl / Shift / Alt click too: use Sw.OnClick(el, fn) (pure click, keyboard, modifier click, busy) or Sw.Card({ OnClick | Href }), or start the handler with if (Sw.BadClick(ev)) return; (a modifier click only moves and shows the focus, and Ctrl+click never opens two things)");
                    }
                    if ((evt == "click") || (evt == "keydown") || (evt == "keyup"))
                        ReportDestructive(ctx, t, heff);
                    //  ux-saf-10: an own beforeunload
                }
                if (!framework && ((evt == "beforeunload") && (hAt > 0)) || (!framework && t.Id(i, "onbeforeunload") && t.P(i - 1, ".") && t.P(i + 1, "=") && !t.Id(i + 2, "null")))
                    Add(ctx, Rule.UxSaf10, t.S[i].Start, "an own beforeunload prompt: call Sw.Page.SetDirty(\"key\", _TF(\"Your changes to ... are lost.\", \"...\")) while there are unsaved edits and SetDirty(\"key\", false) after the save: the shell then asks on the menu, links and Back too, the tab title shows ●, and the browser prompt comes for free");

                //  ---- ux-inp-12: a disabled without a reason
                if (t.Id(i, "disabled") && t.P(i - 1, ".") && t.P(i + 1, "=") && !t.Id(i + 2, "false") && !t.P(i + 2, "="))
                {
                    var kind = KindOf(i - 2, out var name);
                    bool shim = t.Id(i - 2, "E") && t.P(i - 3, ".");
                    if (!ButtonName.IsMatch(t.Tx(i - 2)) && ((kind == "button") || shim))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, (shim ? name + ".E" : name) + ".disabled = ...: " + (shim ? "it becomes aria-disabled but gives no reason (\"Not available right now\"); use " + name + ".SetDisabled(true, \"Select a row first\")" : "a disabled button leaves the tab order and its reason can't be read; use Sw.Disabled.Set(" + name + ", true, \"Select a row first\") (aria-disabled, it stays focusable, ignores the activation and says why)") + "; while work runs, return its promise from OnClick instead (Sw.Button is busy, not disabled)");
                }
                if ((t.Id(i, "setAttribute") || t.Id(i, "toggleAttribute")) && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Str(i + 2)?.ToLowerInvariant() == "disabled"))
                {
                    var kind = KindOf(i - 2, out var name);
                    bool shim = t.Id(i - 2, "E") && t.P(i - 3, ".");
                    if (!ButtonName.IsMatch(t.Tx(i - 2)) && ((kind == "button") || shim))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, (shim ? name + ".E" : name) + "." + t.Tx(i) + "(\"disabled\"): the native disabled leaves the tab order, bypasses the control's disabled state and gives no reason; use " + (shim ? name + ".SetDisabled(true, \"Select a row first\")" : "Sw.Disabled.Set(" + name + ", true, \"Select a row first\")"));
                }
                if (t.Id(i, "Disabled") && t.Key(i) && t.Id(i + 2, "true") && (t.P(i + 3, ",") || t.P(i + 3, "}")))
                {
                    int open = EnclosingObject(t, i);
                    if ((open > 0) && IsSwOptions(t, open))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, "Disabled: true without a reason: the hint and the announcement say only \"Not available right now\"; give the reason and what enables it: Disabled: _TF(\"Select a row first\", \"...\")");
                }
                if (t.Id(i, "SetDisabled") && t.P(i - 1, ".") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count == 1) && !t.Is(a[0], "false"))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, "SetDisabled(" + Short(t.Span(a[0].A, a[0].B)) + ") without a reason: when it disables, the hint and the announcement say only \"Not available right now\"; pass the reason: SetDisabled(" + Short(t.Span(a[0].A, a[0].B)) + ", _TF(\"Select a row first\", \"...\"))");
                }
                if (t.Chain(i, "Disabled", "Set") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count == 2) && !t.Is(a[1], "false"))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, "Sw.Disabled.Set(el, " + Short(t.Span(a[1].A, a[1].B)) + ") without a reason: pass the third argument (\"Select a row first\"): it is the tool tip and the announcement of an attempt");
                }

                //  ---- ux-pop-9: positional OK labels of Alert / Confirm
                if ((t.Chain(i, "Dialog", "Alert") || t.Chain(i, "Dialog", "Confirm")) && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count > 2) && (t.Lit(a[2]) is String ok) && Regex.IsMatch(ok.Trim(), @"^(?:ok|okay)[.!]?$", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxPop9, t.S[a[2].A].Start, "Sw.Dialog." + t.Tx(i) + "(..., \"" + ok + "\"): buttons are verbs that say what happens (\"Delete\", \"Save\", \"Close\"); " + (t.Tx(i) == "Alert" ? "leave the text out (\"Close\")" : "name the action"));
                    //  A Confirm without its verb reads "OK"
                    else if ((t.Tx(i) == "Confirm") && ((a.Count == 2) || ((a.Count > 2) && (t.Is(a[2], "null") || t.Is(a[2], "undefined")))))
                        Add(ctx, Rule.UxPop9, t.S[i].Start, "Sw.Dialog.Confirm(title, text) without the verb: its button reads \"OK\"; pass the action as the third argument (\"Delete\", \"Send\", \"Restart\")");
                }

                //  ---- design-confirm-options: an unknown option of Sw.Dialog.Confirm (FD S5b N2: Confirm: "Clear view" for OkText passed)
                if (t.Chain(i, "Dialog", "Confirm") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    int open = -1;
                    bool form = false;
                    if ((a.Count == 1) && t.P(a[0].A, "{") && (t.Match(a[0].A) == a[0].B))
                    {
                        open = a[0].A;
                        form = true;
                    }
                    else if ((a.Count >= 4) && t.P(a[3].A, "{") && (t.Match(a[3].A) == a[3].B))
                        open = a[3].A;
                    if ((open >= 0) && (ObjKeys(t, open) is List<(int At, String Name)> keys))
                        foreach (var (at, name) in keys)
                            if (!ConfirmOptions.Contains(name) && !(form && ConfirmFormOptions.Contains(name)))
                                ctx.Add(Rule.DesignConfirmOptions, t.S[at].Start, "Sw.Dialog.Confirm: unknown option \"" + name + "\" (ignored at run time" + (form && ConfirmOkWords.Contains(name) ? "; the button reads \"OK\": the verb is OkText" : "") + "). Valid " + (form ? "options of the options form: Title, Text, OkText, " : "options (4th argument): ") + String.Join(", ", ConfirmOptions));
                }

                //  ---- ux-pop-7: one Id for two different dialogs (they share the remembered position)
                if ((t.Chain(i, "Dialog", "Open") || t.Chain(i, "Dialog", "Show")) && t.P(i + 1, "(") && t.P(i + 2, "{") && (ObjKey(t, i + 2, "Id") is var idv) && (idv != null) && (t.Lit(idv.Value) is String did))
                {
                    if (dialogIds.TryGetValue(did, out var first) && (first != i))
                        Add(ctx, Rule.UxPop7, t.S[idv.Value.A].Start, "the dialog Id \"" + did + "\" is also given to another dialog in this file (line " + ctx.Source.GetLineCol(t.S[first].Start).Line + "): two kinds of dialog share one remembered position and size; give each kind its own Id (or leave Id out: the default id comes from the page and the title)");
                    else
                        dialogIds[did] = i;
                }

                //  ---- ux-mot-1: an overlay removed in one frame
                if (t.Id(i, "remove") && t.P(i - 1, ".") && t.P(i + 1, "(") && t.P(i + 2, ")") && t.Id(i - 2) && !t.P(i - 3, ".") && overlays.Contains(t.Tx(i - 2)))
                    Add(ctx, Rule.UxMot1, t.S[i].Start, "\"" + t.Tx(i - 2) + "\" is an overlay (a popup, dialog, layer, toast, menu or tool tip) removed in one frame: it vanishes instead of leaving; use Sw.Motion.Out(" + t.Tx(i - 2) + ") (the theme's exit animation, at once in Hacker and with reduced motion, then removed)");

                //  ---- ux-lst-3: a hand-made table
                //  (not a captioned document table, such as the table view of a chart: Sw.el("table", null, Sw.el("caption", ...), ...))
                if (((t.Id(i, "createElement") && t.P(i + 1, "(") && (t.Str(i + 2)?.ToLowerInvariant() is "table" or "tbody"))
                    || ((t.Chain(i, "Sw", "el") || (t.Id(i, "el") && !t.P(i - 1, "."))) && t.P(i + 1, "(") && (t.Str(i + 2) is String sp) && Regex.IsMatch(sp, @"^t(?:able|body)(?![\w-])", RegexOptions.IgnoreCase)))
                    && !Regex.IsMatch(t.Span(i, Math.Max(i, t.Match(i + 1))), @"[""']caption\b") && !tableLibrary
                    && (!Regex.IsMatch(t.Str(i + 2) ?? "", @"^tbody", RegexOptions.IgnoreCase) || !fileMakesTable))
                    Add(ctx, Rule.UxLst3, t.S[i].Start, HandMadeTable);
                if (!tableLibrary && ((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && Regex.IsMatch(t.Tx(i), @"<table\b", RegexOptions.IgnoreCase))
                    Add(ctx, Rule.UxLst3, t.S[i].Start, "an html string with a <table>: " + HandMadeTable);

                //  ---- ux-saf-11: own drafts, dropped draft handles
                if (t.Id(i, "setItem") && t.P(i - 1, ".") && (t.Id(i - 2, "localStorage") || t.Id(i - 2, "sessionStorage")) && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count > 0) && DraftKey.IsMatch(Literals(t, a[0])))
                        Add(ctx, Rule.UxSaf11, t.S[i].Start, "an own draft in " + t.Tx(i - 2) + ": use Sw.Draft.Attach(field, { Context: \"...\" }) (debounced, per user, 7 days, never secrets, the restore notice with Discard, cleared on sign out) and Clear() it after the send / save");
                }
                if (t.Chain(i, "Draft", "Attach") && t.P(i + 1, "("))
                {
                    if (Dropped(t, i, t.Chain(i, "Sw", "Draft", "Attach") ? 3 : 2) && !InDialog(i) && !InDialog(i, t.Tx(i + 2)))
                        Add(ctx, Rule.UxSaf11, t.S[i].Start, "the handle of Sw.Draft.Attach is dropped: keep it (const draft = Sw.Draft.Attach(...)), call draft.Clear() after the text is sent / saved or discarded and draft.Dispose() when the field goes away (a dialog), else the draft comes back after Discard and the page stays dirty");
                    else
                    {
                        int s = i - (t.Chain(i, "Sw", "Draft", "Attach") ? 4 : 2);
                        if (t.P(s - 1, "=") && t.Id(s - 2) && !t.P(s - 3, "."))
                        {
                            var h = t.Tx(s - 2);
                            if (!Regex.IsMatch(text, @"\b" + Regex.Escape(h) + @"\s*(?:\?\.|\.)\s*(?:Clear|Dispose)\b") && !Regex.IsMatch(text, @"\breturn\s+" + Regex.Escape(h) + @"\b"))
                                Add(ctx, Rule.UxSaf11, t.S[i].Start, "the draft \"" + h + "\" is never cleared: call " + h + ".Clear() after the text is sent / saved (and " + h + ".Dispose() when the field goes away), else the old text is restored again");
                        }
                    }
                }


                //  ---- ux-sec-1 / ux-inp-31: copy
                if (t.Chain(i, "Clipboard", "Copy") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count > 0) && SecretText.IsMatch(t.Span(a[0].A, a[0].B)) && Regex.IsMatch(t.Span(i, t.Match(i + 1)), @"\bShow\s*:\s*true\b"))
                        Add(ctx, Rule.UxSec1, t.S[i].Start, "Sw.Clipboard.Copy(" + Short(t.Span(a[0].A, a[0].B)) + ", { Show: true }) of a secret: the confirmation and the announcement say the value aloud; drop Show and pass What (\"API key copied\")");
                }
                if (t.Chain(i, "Sw", "CopyButton") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count == 0) || (t.P(a[0].A, "{") && (ObjKey(t, a[0].A, "Value") == null) && (ObjKey(t, a[0].A, "Get") == null)))
                        Add(ctx, Rule.UxInp31, t.S[i].Start, "Sw.CopyButton without Value or Get: it copies an empty text and confirms \"Copied\"; pass Value (the text) or Get (a function, async allowed) and What");
                    else if (t.P(a[0].A, "{") && (ObjKey(t, a[0].A, "Show") is var sh) && (sh != null) && t.Is(sh.Value, "true") && (ObjKey(t, a[0].A, "Value") is var cv) && (cv != null) && SecretText.IsMatch(t.Span(cv.Value.A, cv.Value.B)))
                        Add(ctx, Rule.UxSec1, t.S[i].Start, "Sw.CopyButton({ Show: true }) of a secret: the confirmation says the value aloud; drop Show and pass What (\"API key copied\")");
                }

                //  ---- ux-pop-6 / ux-fbk-4: busy without a way out
                if (t.Id(i, "SetBusy") && t.P(i - 1, ".") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    bool off = (a.Count == 0) || t.Is(a[0], "false") || t.Is(a[0], "true") || t.Is(a[0], "null");
                    //  A work function SetBusy(({ Signal }) => ...) is cancellable: Cancel aborts its signal
                    bool work = (a.Count > 0) && ParseFn(t, a[0].A, out _);
                    bool way = (a.Count > 1) && Regex.IsMatch(t.Span(a[1].A, a[1].B), @"\b(?:Cancel|Abort|Background|CloseText|Stop)\s*:");
                    bool inClick = onClicks.Any(c => (c.A <= i) && (i <= c.B));
                    if (!off && !work && !way && !inClick && !t.Id(a[0].A, "busy") && (a.Count < 2 || t.P(a[1].A, "{")))
                        Add(ctx, Rule.UxPop6, t.S[i].Start, "SetBusy(" + Short(t.Span(a[0].A, a[0].B)) + ") without a way out: the user can only wait (\"Close without waiting\" appears after 10 s); pass the work as a function that takes the signal (SetBusy(({ Signal }) => Sw.Net.Send(url, arg, { Signal }))) or { Abort / Cancel } when it can stop, Background to continue it in the activity list, or CloseText that names the consequence");
                }
                if (t.Chain(i, "Dialog", "Working") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    //  Work (a function with the signal) and Promise (offers "Continue in background") are ways out too
                    if ((a.Count > 0) && t.P(a[0].A, "{") && new[] { "Cancel", "Background", "Work", "Promise", "CloseText" }.All(k => ObjKey(t, a[0].A, k) == null))
                        Add(ctx, Rule.UxFbk4, t.S[i].Start, "Sw.Dialog.Working without Work, Promise, Cancel, Background or CloseText: an unbounded spinner the user can neither stop nor send to the activity list; pass Work: ({ Signal }) => ... (cancellable) or Promise (offers \"Continue in background\")");
                }

                //  ---- ux-fbk-4: a table export by hand (the table's own Export is background work with Cancel)
                if (((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && Regex.IsMatch(t.Tx(i), @"\bExportTable(?:Data|Api)\b") && !exportsTracked)
                {
                    Add(ctx, Rule.UxFbk4, t.S[i].Start, "an own table export (" + Short(t.Tx(i)) + "): use table.Export(mode, exporter) (busy Download button, an activity job with Cancel, \"<file> is ready\") or Sw.Activity.Track(promise, { Title, Cancel }) / Sw.Dialog.Working({ Promise, Cancel })");
                    exportsTracked = true;
                }

                //  ---- ux-fbk-7: a timer that rebuilds rows (the order jumps under the pointer)
                if ((t.Id(i, "setInterval") && !t.P(i - 1, ".") || (t.Id(i, "setInterval") && t.Id(i - 2, "window"))) && t.P(i + 1, "(") && HandlerAt(t, i + 2, out var tf))
                {
                    var tb = t.Span(tf.Start, tf.End);
                    if (Regex.IsMatch(tb, @"\b(?:replaceChildren|innerHTML\s*=)") && Regex.IsMatch(tb, @"tbody|<tr\b|createElement\(\s*[""']tr[""']|\bel\(\s*[""']tr\b|rows?\b|list\b", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxFbk7, t.S[i].Start, "a timer that rebuilds the rows: the order jumps under the pointer and the focus is lost on every tick; use Sw.Table with SetRows(rows, true) (the order is held while the pointer, a finger or the focus is on the rows, moved rows are marked)");
                }

                //  ---- syntax: await at the top level of a classic script
                if (!NoPromiseOnly && !modules && t.Id(i, "await") && !t.P(i - 1, ".") && !t.P(i + 1, ":") && TopLevel(t, i))
                    ctx.Add(Rule.Syntax, t.S[i].Start, "\"await\" at the top level of a classic script: a SyntaxError in the browser (the whole file doesn't run); put the code in Sw.Page.Main(async () => { ... }) or an async function");
            }

            //  ---- the rules on the call structure and the value flow (UxFlowRules.cs)
            var editable = CheckFlow(ctx, t, flow, vars, dialogCalls, framework);

            //  ---- the families: security, formatting, pointer events other than click, silent failures, Sw api misuse (UxFamilies.cs)
            if (!framework)
            {
                CheckFamilies(ctx, t, flow, vars, dialogCalls, framework, editable);
                CheckBuilders(ctx, t, flow);
            }

            //  ---- file level: dirty keys never cleared, edit forms without dirty tracking, multi-line text without a draft, inputs without labels
            CheckDirtyKeys(ctx, t);
            bool tracks = Regex.IsMatch(text, @"\bSetDirty\b|\bSw\.Form\b|\bSw\.Draft\b|\bFormFooter\b|\bPropertyEditor\b|\bSw\.Edit\b|\bsw-nodirty\b");
            int firstField = -1;
            String firstName = null;
            bool hasDraft = Regex.IsMatch(text, @"\bSw\.Draft\b|\bDraft\s*:");
            foreach (var kv in vars.OrderBy(x => x.Value.At))
            {
                var (kind, at, call) = kv.Value;
                bool field = FieldTags.Contains(kind) || FieldControls.Contains(kind);
                if (!field || (call == -2))
                    continue;
                int close = call > 0 ? t.Match(call) : -1;
                var opts = close > call ? t.Span(call, close) : "";
                //  The Sw value controls (TextBox, NumberBox, Select, Toggle, Segmented ...) mark the page dirty by themselves
                bool selfDirty = FieldControls.Contains(kind) && !Regex.IsMatch(opts, @"\bDirty\s*:\s*false\b");
                var nm = kv.Key;
                bool search = Regex.IsMatch(nm, @"search|filter|find|query", RegexOptions.IgnoreCase) || Regex.IsMatch(opts, @"\bSearch\s*:|[Ss]earch|[Ff]ilter|type\s*:\s*[""']search");
                bool readOnly = Regex.IsMatch(opts, @"\bReadOnly\s*:\s*true|\breadonly\b|\bdisabled\b", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"\b" + Regex.Escape(nm) + @"\s*\.\s*(?:readOnly\s*=\s*true|setAttribute\(\s*[""']readonly)");
                bool hidden = Regex.IsMatch(text, @"\b" + Regex.Escape(nm) + @"\s*\.\s*type\s*=\s*[""'](?:hidden|file|submit|button|reset|image)[""']") || Regex.IsMatch(opts, @"type\s*:\s*[""'](?:hidden|file|submit|button)[""']");
                if (search || hidden)
                    continue;
                bool dialog = InDialog(at, nm);
                //  ux-frm-1: a field made in script without a label
                if (FieldTags.Contains(kind) && !fileHasLabel && !Regex.IsMatch(opts, @"aria-label|ariaLabel|\btitle\b|\bid\b")
                    && !Regex.IsMatch(text, @"\b" + Regex.Escape(nm) + @"\s*\.\s*(?:title|ariaLabel|ariaLabelledByElements|id|labels)\s*=|\b" + Regex.Escape(nm) + @"\s*\.\s*setAttribute\(\s*[""'](?:aria-label|aria-labelledby|title|id)[""']"))
                    Add(ctx, Rule.UxFrm1, t.S[at].Start, "the " + kind + " \"" + nm + "\" made in script has no label (a placeholder is not one: it disappears while typing): use a Sw control with Label (new Sw.TextBox({ Label: _TF(\"Name\", \"...\") })), Sw.Field, or a <label> linked to it; aria-label only when the purpose is shown otherwise");
                if (readOnly)
                    continue;
                //  ux-saf-11: multi-line text without a draft
                bool multi = (kind == "textarea") || ((kind == "Sw.TextBox") && Regex.IsMatch(opts, @"\bMultiline\s*:\s*true|\bRows\s*:"));
                if (multi && !hasDraft && !dialog)
                    Add(ctx, Rule.UxSaf11, t.S[at].Start, "the multi-line text \"" + nm + "\" has no draft: text typed and not yet saved is lost on a reload or a crash; attach one: const draft = Sw.Draft.Attach(" + (kind == "textarea" ? nm : nm + ".Input") + ", { Context: \"...\" }) and draft.Clear() after the save");
                if (!dialog && !selfDirty && (firstField < 0))
                {
                    firstField = at;
                    firstName = nm;
                }
            }
            if ((firstField >= 0) && !tracks)
            {
                //  A save: a request to an api named Save*, Set*, Update*, Create* ...
                for (int i = 0; i < t.N; ++i)
                    if (IsRequest(t, i))
                    {
                        var a = t.Args(i + 1);
                        if ((a.Count > 0) && SaveApi.IsMatch(Literals(t, a[0]).Trim()) && !InDialog(i))
                        {
                            Add(ctx, Rule.UxSaf10, t.S[firstField].Start, "the page edits \"" + firstName + "\" and saves it (" + Short(Literals(t, a[0]).Trim()) + ") without dirty tracking: leaving with unsaved edits loses them without a question; call Sw.Page.SetDirty(\"form\", _TF(\"Your changes to ... are lost.\", \"...\")) on change and SetDirty(\"form\", false) after the save (or use Sw.Form)");
                            break;
                        }
                    }
            }
        }

        /// <summary>
        /// True if token i is at the top level of the script (not in a function, block, call or array) and its statement has no arrow / function.
        /// </summary>
        static bool TopLevel(Toks t, int i)
        {
            int depth = 0;
            for (int k = 0; k < i; ++k)
            {
                if (t.P(k, "{") || t.P(k, "(") || t.P(k, "[") || t.P(k, "${"))
                    ++depth;
                else if (t.P(k, "}") || t.P(k, ")") || t.P(k, "]"))
                    --depth;
            }
            if (depth != 0)
                return false;
            for (int k = i - 1; k >= 0; --k)
            {
                if (t.P(k, ";") || t.P(k, "}"))
                    break;
                if (t.P(k, "=>") || t.Id(k, "function") || t.Id(k, "async"))
                    return false;
            }
            return true;
        }

        /// <summary>The OnClick handler given to a call of a local wrapper of a Sw component at i: (the token two before the argument, the function), or null.</summary>
        static (int Key, FnNode Node)? WrapperClick(Toks t, Flow flow, int i)
        {
            if (!t.Id(i) || t.P(i - 1, ".") || !t.P(i + 1, "(") || !t.Wrappers.TryGetValue(t.Tx(i), out var w) || (w.OnClick < 0))
                return null;
            //  Not the wrapper's own declaration
            if (t.Id(i - 1, "function"))
                return null;
            var a = t.Args(i + 1);
            if ((a.Count > w.OnClick) && (flow.At(a[w.OnClick].A) is FnNode n))
                return (a[w.OnClick].A - 2, n);
            return null;
        }

        /// <summary>
        /// ux-fbk-2 (no promise returned), ux-saf-3 (danger button with a plain Confirm), ux-saf-1 (a destructive call without a question).
        /// </summary>
        static void CheckOnClick(Checker.FileContext ctx, Toks t, Flow flow, int key, FnNode node, List<(int A, int B)> dialogCalls)
        {
            var f = node.F;
            var body = t.Span(f.Start, f.End);
            var eff = flow.Of(node);
            //  A handler given by name (it may be shared): report at the OnClick that uses it
            bool named = (node.Head != key + 2) && (node.Head != key + 3);
            //  ux-fbk-2: every request the click starts (also in a local helper, an inline callback or a timer) is returned or awaited
            //  (a long poll / live view is not the button's work)
            bool anyResult = flow.Own(node).Any(j => (t.Id(j, "return") && !t.P(j + 1, ";") && !t.P(j + 1, "}")) || t.Id(j, "await")) || !f.Block;
            var dropped = eff.Reqs.FirstOrDefault(r => !(r.Chain && anyResult) && !r.Returned && !r.Deferred && !PollUrl.IsMatch(r.Url) && ((r.Via == null) || r.Url.Split(' ').Any(u => (u.Length > 0) && !ReadUrl.IsMatch(u)) || (r.ViaMethod && !r.Chain && (r.Url.Trim().Length == 0) && WorkVerb.IsMatch(r.Via))));
            var later = eff.Reqs.FirstOrDefault(r => r.Deferred && !r.Chain && !r.ViaMethod && !PollUrl.IsMatch(r.Url));
            if (dropped != null)
            {
                var what = dropped.Via != null ? dropped.Via + "(...), which starts a request," : IsRequest(t, dropped.At) ? t.Span(Math.Max(f.Start, ChainStartOf(t, dropped.At)), dropped.At).TrimStart('.', ' ') + "(...)" : "a ." + t.Tx(dropped.At) + "(...) chain";
                AddNoPromise(ctx, t.S[named ? key + 2 : dropped.At].Start, "OnClick starts " + Short(what) + " but returns no promise: the button can't tell when the work ends or whether it failed (no \"not done\" for a failure, work started after a callback is not busy, a request it doesn't see can be sent twice); return it: OnClick: () => Sw.Net.Send(...).then(...) (or make it async and await the request" + (dropped.Via != null ? ", and let " + dropped.Via + " return its promise" : "") + ")");
            }
            else if (later != null)
                AddNoPromise(ctx, t.S[named ? key + 2 : later.At].Start, "OnClick starts a request in a timer (setTimeout / requestAnimationFrame / queueMicrotask): it runs after the click, so the button is never busy, a second click sends again and a failure is not shown; start the request in the handler and return it (await Sw.Util.Delay(ms) first if it must wait)");
            //  ux-saf-3: the options object of a danger control (Sw.Dialog.Confirm focuses Cancel and never confirms on Enter by default;
            //  Safe: true turns that off, which contradicts a destructive opener)
            int open = EnclosingObject(t, key);
            if (open >= 0)
            {
                var kind = ObjKey(t, open, "Kind");
                if ((kind != null) && (t.Lit(kind.Value) == "danger"))
                    for (int j = f.Start; j <= f.End; ++j)
                        if (t.Chain(j, "Dialog", "Confirm") && t.P(j + 1, "("))
                        {
                            var call = t.Span(j, t.Match(j + 1));
                            if (Regex.IsMatch(call, @"\bSafe\s*:\s*true\b") && !Regex.IsMatch(call, @"\bDanger\s*:\s*true\b"))
                                Add(ctx, Rule.UxSaf3, t.S[j].Start, "a Kind: \"danger\" button asks with Sw.Dialog.Confirm(..., { Safe: true }): OK gets the focus and Enter confirms the destructive action; drop Safe and pass { Danger: true } (a danger verb button, Cancel focused) or use Sw.Cells.ConfirmDanger");
                        }
            }
            //  ux-pop-6: a dialog footer button whose async work ignores the signal, in a dialog with Cancel
            if (open >= 0 && (dialogCalls.Where(d => (d.A < key) && (key < d.B)).OrderBy(d => d.B - d.A).Cast<(int A, int B)?>().FirstOrDefault() is (int A, int B) dlg) && Regex.IsMatch(t.Span(dlg.A, dlg.B), @"\bText\s*:\s*(?:_TF?H?\(\s*)?[""']Cancel[""']|\bDismiss\s*:\s*true\b") && !Regex.IsMatch(body, @"\bSignal\b"))
            {
                //  Requests started at once get the click scope's signal (Sw.Work.Scope); a request after an await does not
                bool afterAwait = false, awaited = false;
                foreach (var j in flow.Own(node))
                {
                    if (t.Id(j, "await"))
                        awaited = true;
                    else if (awaited && t.P(j, ";"))
                        afterAwait = true;
                    else if (afterAwait && IsRequest(t, j))
                    {
                        //  A write can't be undone by an abort: the dialog offers "Stop waiting" then, by design; a read can be cancelled
                        var ra = t.Args(j + 1);
                        var ru = ra.Count > 0 ? Literals(t, ra[0]).Trim() : "";
                        bool read = t.Id(j, "Get") || t.Id(j, "Cached") || ReadUrl.IsMatch(ru) || Regex.IsMatch(ru, @"Table(?:[?#].*)?$");
                        if (read && !Regex.IsMatch(body, @"\bCancellable\s*:"))
                            Add(ctx, Rule.UxPop6, t.S[j].Start, "the dialog button's read starts after an await, outside the click scope, so the dialog's Cancel can't stop it (it offers \"Stop waiting\"): pass the signal on (OnClick: async (close, value, { Signal }) => { ...; await Sw.Net.Send(url, arg, { Signal }); }) or mark the button Cancellable: true");
                        break;
                    }
                }
            }
            //  ux-saf-1: a destructive request without a question; a dialog's own footer button is the question (its dialog asked)
            //  (round 4: only a dialog that asks: its title is a question, or it has two buttons, the action and Cancel; a Remove button in
            //  a details dialog with other work (Save note) is no question)
            bool footer = dialogCalls.Where(d => (d.A < key) && (key < d.B)).Any(d =>
            {
                var ds = t.Span(d.A, d.B);
                var title = Regex.Match(ds, @"\bTitle\s*:\s*(?:_T[F]?H?\(\s*)?(?<q>""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')");
                bool question = title.Success && Regex.IsMatch(title.Groups["q"].Value, @"\?\s*['""]$");
                return question || Regex.Matches(ds, @"\bText\s*:").Count <= 2;
            });
            if (!footer && !AsksFirst.IsMatch(body))
                ReportDestructive(ctx, t, eff, named ? key + 2 : -1);
        }

        /// <summary>
        /// ux-saf-10: Sw.Page.SetDirty("key", text) whose key this file never clears (SetDirty("key", false / null), ClearDirty("key") / ClearDirty()).
        /// </summary>
        static void CheckDirtyKeys(Checker.FileContext ctx, Toks t)
        {
            var sets = new Dictionary<String, int>(StringComparer.Ordinal);
            var cleared = new HashSet<String>(StringComparer.Ordinal);
            bool clearsAll = false;
            for (int i = 0; i < t.N; ++i)
            {
                if (t.Chain(i, "Page", "SetDirty") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if (a.Count == 0)
                        continue;
                    var k = (a[0].A == a[0].B) ? t.Str(a[0].A) : null;
                    bool clear = (a.Count > 1) && (t.Is(a[1], "false") || t.Is(a[1], "null") || t.Is(a[1], "undefined") || (t.Str(a[1].A) == ""));
                    bool set = !clear;   //  SetDirty(key) alone marks dirty with the generic text
                    if (k == null)
                    {
                        if (clear || (a.Count > 1 && !t.Is(a[1], "true") && t.Lit(a[1]) == null))
                            clearsAll = true;   //  a dynamic key or value: can't tell
                        continue;
                    }
                    if (clear)
                        cleared.Add(k);
                    else if (set)
                    {
                        if ((a.Count > 1) && !t.Is(a[1], "true") && (t.Lit(a[1]) == null) && !t.P(a[1].A, "{"))
                            cleared.Add(k); //  SetDirty(k, isDirty): toggles both ways
                        else if (!sets.ContainsKey(k))
                            sets[k] = i;
                    }
                }
                else if (t.Chain(i, "Page", "ClearDirty") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if (a.Count == 0)
                        clearsAll = true;
                    else if (t.Str(a[0].A) is String k)
                        cleared.Add(k);
                    else
                        clearsAll = true;
                }
            }
            if (clearsAll)
                return;
            foreach (var kv in sets)
                if (!cleared.Contains(kv.Key))
                    Add(ctx, Rule.UxSaf10, t.S[kv.Value].Start, "Sw.Page.SetDirty(\"" + kv.Key + "\", ...) is never cleared in this file: after the save succeeds (and when the edits are undone or discarded) call Sw.Page.SetDirty(\"" + kv.Key + "\", false), else the page keeps asking \"Leave without saving?\" after a save");
        }

        // ------------------------------------------------------------------ usage patterns (css)

        static readonly Regex BusySelector = new Regex(@"\[aria-busy(?:\s*=\s*[""']?true[""']?)?\s*\]|\.[\w-]*busy(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex WholePageCompound = new Regex(@"^(?:html|body|:root|main)(?:[.#\[:][^\s>+~]*)?$|^[a-z]*(?:[.#\[:][^\s>+~]*)?\.sw-(?:page|root|shell|app|main|content)(?![\w-])[^\s>+~]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        //  A literal move of more than 8 px (a small 2-8 px shift is left alone: dist-1 size, cheap in every theme)
        static readonly Regex PxTranslate = new Regex(@"(?<![\w.-])-?(?:9|[1-9]\d+)(?:\.\d+)?px\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The css usage pattern rules of one declaration: a busy look made of opacity / pointer-events (ux-fbk-2), a literal distance in
        /// a keyframes animation (ux-mot-2).
        /// </summary>
        static void CheckCssPatterns(Checker.FileContext ctx, CssDecl d, String keyframes, bool reduced, HashSet<String> movedKeyframes)
        {
            var v = d.Value.Replace("!important", "").Trim().ToLowerInvariant();
            if ((d.Selector != null) && BusySelector.IsMatch(d.Selector))
            {
                bool fades = (d.Prop == "opacity") && Double.TryParse(v.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o) && ((v.EndsWith("%") ? o / 100 : o) < 1);
                bool blocks = (d.Prop == "pointer-events") && (v == "none");
                //  A hidden label (the spinner replaces the icon, never the label: FBK-2 keeps the label, FBK-3 the width)
                var subject = Checker.SplitSelectorList(d.Selector).Where(x => BusySelector.IsMatch(x)).Select(x => Regex.Match(x, @"[^\s>+~]+$").Value).FirstOrDefault() ?? "";
                bool hides = (((d.Prop == "visibility") && (v == "hidden")) || ((d.Prop == "display") && (v == "none")) || ((d.Prop == "color") && (v == "transparent")))
                    && !Regex.IsMatch(subject, @"icon|spin|svg|img|badge|count|shortcut|kbd|::?(?:before|after)", RegexOptions.IgnoreCase);
                if (hides)
                    Add(ctx, Rule.UxFbk2, d.PropStart, "\"" + Short(d.Selector) + " { " + d.Prop + ": " + d.Value + " }\": a busy control keeps its label (FBK-2) and its width (FBK-3); the spinner replaces the icon or overlays the label: use Sw.Button with a promise from OnClick instead of hiding the text");
                if (fades || blocks)
                    Add(ctx, Rule.UxFbk2, d.PropStart, "\"" + Short(Checker.SplitSelectorList(d.Selector).FirstOrDefault(x => BusySelector.IsMatch(x)) ?? d.Selector) + " { " + d.Prop + ": " + d.Value + " }\": busy is not disabled: a busy control keeps its label, colour and focus and absorbs repeats itself; use Sw.Button with a promise from OnClick (the spinner after 250 ms, aria-busy, cursor: progress) instead of " + (fades ? "fading it" : "blocking the pointer"));
            }
            if ((keyframes != null) && !reduced && ((d.Prop == "transform") || (d.Prop == "translate")) && !movedKeyframes.Contains(keyframes))
            {
                var m = PxTranslate.Match(StripVar(d.Value));
                if (m.Success && ((d.Prop == "translate") || Regex.IsMatch(d.Value, @"\btranslate", RegexOptions.IgnoreCase)) && movedKeyframes.Add(keyframes))
                    Add(ctx, Rule.UxMot2, d.ValueStart, "\"" + Short(keyframes) + "\" moves by a literal distance (" + Short(d.Value) + "): use the distance tokens var(--sw-dist-1) (2-4 px shifts), var(--sw-dist-2) (menus, tool tips), var(--sw-dist-3) (dialogs, sheets): themes without motion set them to 0");
            }
        }

        /// <summary>True if a selector list has a whole page subject (body, html, :root, *, also body.busy / html[data-x]).</summary>
        static bool IsWholePage(String selector) =>
            Checker.SplitSelectorList(selector).Any(x => WholePage.IsMatch(x.Trim()) || WholePageCompound.IsMatch(x.Trim()));
    }
}
