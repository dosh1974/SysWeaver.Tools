using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ the call structure of a script
        //
        //  The usage rules judge what a handler does, not how it is spelled: the functions of a file with their nesting, the local
        //  functions a handler calls (followed a few calls deep), the callbacks it runs at once (forEach, then, an IIFE, a Promise
        //  executor) or later (timers), the requests it starts and whether their promise is returned / awaited, whether it asks first,
        //  checks the modifier keys or rebuilds a list. Variables are followed through aliases (const b = btn), destructuring
        //  (const { confirm: ask } = window) and the .E of a Sw control.

        /// <summary>A function of the script with its place in the nesting.</summary>
        sealed class FnNode
        {
            public Fn F;
            /// <summary>The first token (async, function, the parameter list or the parameter, the method name).</summary>
            public int Head;
            /// <summary>The name it is declared or assigned as (function x / const x = / x: / a method), null if anonymous.</summary>
            public String Name;
            /// <summary>True for a method of a class or an object literal (name(...) { ... }).</summary>
            public bool IsMethod;
            public FnNode Parent;
            public readonly List<FnNode> Children = new List<FnNode>();
            public int From => F.Block ? F.Start + 1 : F.Start;
            public int To => F.Block ? F.End - 1 : F.End;
        }

        enum CallbackKind
        {
            /// <summary>Stored or registered for later (a handler, an option): not part of the function's own work.</summary>
            Detached,
            /// <summary>Run as part of the work: array callbacks, then / catch / finally, an IIFE, a Promise executor.</summary>
            Inline,
            /// <summary>Run later by a timer (setTimeout, requestAnimationFrame, queueMicrotask ...): outside the click.</summary>
            Deferred,
        }

        /// <summary>A request (or an unknown promise chain) started by a function.</summary>
        sealed class Req
        {
            /// <summary>Where to report it (the request, or the call of the local helper that makes it).</summary>
            public int At;
            /// <summary>The literal url text (also the literal arguments of a helper call), "" if unknown.</summary>
            public String Url = "";
            /// <summary>True if its promise is returned or awaited by the function.</summary>
            public bool Returned;
            /// <summary>True if it starts in a timer callback.</summary>
            public bool Deferred;
            /// <summary>The helper that makes it, null for a request written in the function.</summary>
            public String Via;
            /// <summary>True for an unknown promise chain (x.then(...)), not a known request.</summary>
            public bool Chain;
            /// <summary>True when it is made through a method called on an object (this.x(), obj.x()): judged by the busy rule only.</summary>
            public bool ViaMethod;
        }

        /// <summary>What a function does (its own body, its inline callbacks and the local functions it calls).</summary>
        sealed class Effects
        {
            public readonly List<Req> Reqs = new List<Req>();
            /// <summary>Asks first: a confirmation, a prompt, a dialog (whose buttons decide), Undo, Sw.Danger.</summary>
            public bool Asks;
            /// <summary>Tests the modifier keys (Sw.BadClick, ModifierClick, IsPureEvent, ctrlKey ...).</summary>
            public bool Modifier;
            /// <summary>Rebuilds a list (innerHTML =, replaceChildren, textContent = "" with rows / items).</summary>
            public bool Rebuilds;
            /// <summary>Toasts with a literal text: (token, text).</summary>
            public readonly List<(int At, String Text)> Toasts = new List<(int, String)>();
            /// <summary>Calls Sw.Page.SetDirty / ClearDirty.</summary>
            public bool Dirty;
            /// <summary>Shows a message of its own (Sw.Fail / Toast / Info / Warn / Alert / a status line).</summary>
            public bool Says;
        }

        static readonly HashSet<String> InlineCallbacks = new HashSet<String>(StringComparer.Ordinal)
        {
            "forEach", "map", "filter", "some", "every", "reduce", "reduceRight", "find", "findIndex", "findLast", "flatMap", "sort",
            "then", "catch", "finally", "Promise", "all", "allSettled", "any", "race",
        };
        static readonly HashSet<String> DeferredCallbacks = new HashSet<String>(StringComparer.Ordinal)
        {
            "setTimeout", "setInterval", "requestAnimationFrame", "queueMicrotask", "requestIdleCallback", "Delay",
        };
        static readonly HashSet<String> Keywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "catch", "function", "with", "return", "typeof", "new", "await", "void", "delete", "in", "of",
            "do", "else", "try", "finally", "case", "throw", "yield", "class", "super", "this", "import", "export", "async",
        };
        static readonly HashSet<String> Trackers = new HashSet<String>(StringComparer.Ordinal) { "Working", "Track", "SetBusy", "Busy", "Run", "Start", "Send" };
        static readonly Regex PollUrl = new Regex(@"(?i)poll|live|watch|stream|subscribe|listen|wait|GetMessages", RegexOptions.CultureInvariant);
        /// <summary>A read (a refresh of what is shown, not the work of a button) when a helper starts it: GetX, ListX, CheckX ...</summary>
        static readonly Regex ReadUrl = new Regex(@"(?:^|[/.])(?:Get|List|Check|Find|Search|Query|Load|Read|Is|Has|Have|Can|Count|Ping|Translate|Fetch|Lookup|Validate|Preview|Compute)(?:[A-Z_]\w*)?(?:[?#].*)?$", RegexOptions.CultureInvariant);
        static readonly Regex AskToken = new Regex(@"^(?:Confirm|ConfirmDanger|Prompt|DangerTitle|Undo|ConfirmLeave)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The functions, bindings and effects of one script.
        /// </summary>
        sealed class Flow
        {
            readonly Toks t;
            public readonly List<FnNode> Fns = new List<FnNode>();
            readonly Dictionary<int, FnNode> ByHead = new Dictionary<int, FnNode>();
            readonly Dictionary<String, List<FnNode>> Named = new Dictionary<String, List<FnNode>>(StringComparer.Ordinal);
            readonly Dictionary<FnNode, Effects> Memo = new Dictionary<FnNode, Effects>();
            /// <summary>Names bound to the native confirm / alert / prompt (const ask = window.confirm, const { confirm: ask } = window).</summary>
            public readonly Dictionary<String, String> NativeAliases = new Dictionary<String, String>(StringComparer.Ordinal);

            public Flow(Toks toks)
            {
                t = toks;
                for (int i = 0; i < t.N; ++i)
                {
                    bool start = t.Id(i, "function") && !t.Id(i - 1, "async")
                        || (t.Id(i, "async") && (t.P(i + 1, "(") || t.Id(i + 1, "function") || (t.Id(i + 1) && t.P(i + 2, "=>"))))
                        || (t.P(i, "(") && !t.Id(i - 1, "async") && (t.Match(i) is int c) && (c > 0) && t.P(c + 1, "=>"))
                        || (t.Id(i) && t.P(i + 1, "=>") && !t.Id(i - 1, "async"));
                    Fn f = default;
                    bool ok = start && ParseFn(t, i, out f);
                    String name = null;
                    bool method = false;
                    if (!ok && IsMethod(i, out f))
                    {
                        ok = true;
                        method = true;
                        name = t.Tx(i);
                    }
                    if (!ok)
                        continue;
                    var n = new FnNode { F = f, Head = (t.Id(i - 1, "async") && !start) ? i - 1 : i, Name = name ?? NameOf(i), IsMethod = method };
                    Fns.Add(n);
                    ByHead[i] = n;
                    if (n.Name != null)
                    {
                        if (!Named.TryGetValue(n.Name, out var l))
                            Named[n.Name] = l = new List<FnNode>();
                        l.Add(n);
                    }
                }
                //  Nesting: the innermost function whose body contains the head
                foreach (var n in Fns)
                {
                    FnNode best = null;
                    foreach (var p in Fns)
                        if ((p != n) && (p.F.Start <= n.Head) && (n.F.End <= p.F.End) && (p.Head < n.Head) && ((best == null) || (p.F.End - p.F.Start < best.F.End - best.F.Start)))
                            best = p;
                    n.Parent = best;
                    best?.Children.Add(n);
                }
                foreach (var n in Fns)
                    n.Children.Sort((a, b) => a.Head.CompareTo(b.Head));
                //  Native dialogs reached by an alias
                for (int i = 0; i + 3 < t.N; ++i)
                {
                    //  const ask = window.confirm
                    if (t.Id(i) && t.P(i + 1, "=") && IsGlobal(i + 2) && t.P(i + 3, ".") && (t.Tx(i + 4) is String nm) && NativeNames.Contains(nm) && !t.P(i + 5, "("))
                        NativeAliases[t.Tx(i)] = nm;
                    //  const { confirm: ask, alert } = window
                    if (t.P(i, "{") && (t.Match(i) is int c) && (c > i) && t.P(c + 1, "=") && IsGlobal(c + 2) && !t.P(c + 3, ".") && !t.P(c + 3, "["))
                        for (int k = i + 1; k < c; ++k)
                            if (t.Id(k) && NativeNames.Contains(t.Tx(k)) && (t.P(k - 1, "{") || t.P(k - 1, ",")))
                                NativeAliases[t.P(k + 1, ":") && t.Id(k + 2) ? t.Tx(k + 2) : t.Tx(k)] = t.Tx(k);
                }
            }

            static readonly HashSet<String> NativeNames = new HashSet<String>(StringComparer.Ordinal) { "confirm", "alert", "prompt" };

            bool IsGlobal(int i) => t.Id(i, "window") || t.Id(i, "self") || t.Id(i, "globalThis") || t.Id(i, "top") || t.Id(i, "parent");

            /// <summary>A method of an object literal or class: name(...) { ... } (also async / static / get / set name).</summary>
            bool IsMethod(int i, out Fn f)
            {
                f = default;
                if (!t.Id(i) || Keywords.Contains(t.Tx(i)) || !t.P(i + 1, "(") || t.P(i - 1, "."))
                    return false;
                int c = t.Match(i + 1);
                if ((c < 0) || !t.P(c + 1, "{"))
                    return false;
                if (!(t.P(i - 1, "{") || t.P(i - 1, ",") || t.P(i - 1, "}") || t.P(i - 1, ";") || t.Id(i - 1, "async") || t.Id(i - 1, "static") || t.Id(i - 1, "get") || t.Id(i - 1, "set") || t.P(i - 1, "*")))
                    return false;
                f.Async = t.Id(i - 1, "async");
                f.Block = true;
                f.Start = c + 1;
                f.End = t.Match(c + 1);
                return f.End > f.Start;
            }

            /// <summary>The name of the function whose head is at i: function x(, const x = ..., x = ..., x: ...</summary>
            String NameOf(int i)
            {
                int h = i;
                if (t.Id(h, "async"))
                    ++h;
                if (t.Id(h, "function"))
                {
                    int k = t.P(h + 1, "*") ? h + 2 : h + 1;
                    if (t.Id(k))
                        return t.Tx(k);
                }
                int b = i - 1;
                if ((t.P(b, "=") || t.P(b, ":")) && t.Id(b - 1) && !t.P(b - 2, "."))
                    return t.Tx(b - 1);
                //  A function assigned to a member of a helper module or of the page object: Bk.Load = async function (...),
                //  this.Save = () => ... (not an event property: x.onclick = ...)
                if (t.P(b, "=") && t.Id(b - 1) && t.P(b - 2, ".") && t.Id(b - 3) && !t.P(b - 4, ".") && !t.Tx(b - 1).StartsWith("on", StringComparison.Ordinal))
                    return t.Tx(b - 1);
                return null;
            }

            /// <summary>The first function or method declared with the name, null if none.</summary>
            public FnNode NamedFn(String name) => Named.TryGetValue(name, out var l) ? l[0] : null;

            /// <summary>The function whose head is at k, or a name declared in the file.</summary>
            public FnNode At(int k)
            {
                if (ByHead.TryGetValue(k, out var n))
                    return n;
                if (t.Id(k, "async") && ByHead.TryGetValue(k + 1, out n))
                    return n;
                if (t.Id(k) && !t.P(k + 1, "(") && !t.P(k + 1, ".") && !t.P(k - 1, ".") && Named.TryGetValue(t.Tx(k), out var l))
                    return l[0];
                return null;
            }

            /// <summary>The innermost function around a token (null at the top level).</summary>
            public FnNode Around(int at)
            {
                FnNode best = null;
                foreach (var n in Fns)
                    if ((n.F.Start <= at) && (at <= n.F.End) && ((best == null) || (n.F.End - n.F.Start < best.F.End - best.F.Start)))
                        best = n;
                return best;
            }

            /// <summary>The tokens of a function body that belong to it (not to the functions nested in it).</summary>
            public IEnumerable<int> Own(FnNode n)
            {
                int ci = 0;
                for (int j = n.From; j <= n.To; ++j)
                {
                    while ((ci < n.Children.Count) && (n.Children[ci].F.End < j))
                        ++ci;
                    if ((ci < n.Children.Count) && (n.Children[ci].Head <= j) && (j <= n.Children[ci].F.End))
                    {
                        j = n.Children[ci].F.End;
                        continue;
                    }
                    yield return j;
                }
            }

            /// <summary>How a nested function runs: the call whose argument it is (or an IIFE).</summary>
            public CallbackKind KindOf(FnNode c)
            {
                if (t.P(c.F.End + 1, ")") && t.P(c.F.End + 2, "(") && t.P(c.Head - 1, "("))
                    return CallbackKind.Inline;
                int depth = 0;
                for (int k = c.Head - 1; k >= 0; --k)
                {
                    if (t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                        ++depth;
                    else if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                    {
                        if (depth > 0)
                        {
                            --depth;
                            continue;
                        }
                        if (!t.P(k, "("))
                            return CallbackKind.Detached;
                        var name = t.Tx(k - 1);
                        if (DeferredCallbacks.Contains(name))
                            return CallbackKind.Deferred;
                        if (InlineCallbacks.Contains(name))
                            return CallbackKind.Inline;
                        return CallbackKind.Detached;
                    }
                    else if ((depth == 0) && (t.P(k, ";") || t.P(k, "=>")))
                        return CallbackKind.Detached;
                }
                return CallbackKind.Detached;
            }

            /// <summary>The first token of the member chain whose last name is at i (a.b.c( -> a; also new / await prefixes are not included).</summary>
            int ChainStart(int i)
            {
                int s = i;
                while ((s >= 2) && (t.P(s - 1, ".") || t.P(s - 1, "?.")) && (t.Id(s - 2) || t.P(s - 2, ")") || t.P(s - 2, "]")))
                {
                    if (t.Id(s - 2))
                        s -= 2;
                    else
                    {
                        //  a call / index in the chain: back to its opening bracket and the name before it
                        int d = 0, k = s - 2;
                        for (; k >= 0; --k)
                        {
                            if (t.P(k, ")") || t.P(k, "]"))
                                ++d;
                            else if ((t.P(k, "(") || t.P(k, "[")) && (--d == 0))
                                break;
                        }
                        if (k <= 0 || !t.Id(k - 1))
                            return Math.Max(0, k);
                        s = k - 1;
                    }
                }
                return s;
            }

            /// <summary>True if the value of the expression at i reaches the function's result (returned, awaited, or kept in a variable that is).</summary>
            public bool Returned(FnNode n, int i)
            {
                if (!n.F.Block)
                    return !t.Id(ChainStart(i) - 1, "void");
                int s = ChainStart(i);
                var own = new HashSet<int>(Own(n));
                //  The statement around s (brackets of any kind are skipped; a ";", a block start or the end of a block ends it), and on the
                //  way: handed to a tracker that shows the work (Sw.Dialog.Working({ Promise }), Sw.Activity.Track(p), h.SetBusy(p))
                bool BlockBrace(int open) => t.P(open - 1, ")") || t.P(open - 1, "=>") || t.Id(open - 1, "else") || t.Id(open - 1, "try") || t.Id(open - 1, "do") || t.Id(open - 1, "finally") || t.P(open - 1, ";") || t.P(open - 1, "{") || t.P(open - 1, "}");
                int st = n.From;
                for (int k = s - 1, d = 0; k >= n.From; --k)
                {
                    if ((d == 0) && t.P(k, ";"))
                    {
                        st = k + 1;
                        break;
                    }
                    if (t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                    {
                        if ((d == 0) && t.P(k, "}"))
                        {
                            //  The end of a block statement before this one
                            int o = -1;
                            for (int q = k - 1, dd = 0; q >= n.From; --q)
                            {
                                if (t.P(q, "}"))
                                    ++dd;
                                else if (t.P(q, "{") && (dd-- == 0))
                                {
                                    o = q;
                                    break;
                                }
                            }
                            if ((o >= 0) && BlockBrace(o) && !t.P(o - 1, "=>"))
                            {
                                st = k + 1;
                                break;
                            }
                        }
                        ++d;
                    }
                    else if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                    {
                        if (d > 0)
                        {
                            --d;
                            continue;
                        }
                        if (t.P(k, "{") && BlockBrace(k) && !t.P(k - 1, "=>") && !t.P(k - 1, "("))
                        {
                            st = k + 1;
                            break;
                        }
                        if (t.P(k, "(") && Trackers.Contains(t.Tx(k - 1)))
                            return true;
                    }
                }
                for (int x = st; x < s; ++x)
                    if ((t.Id(x, "return") || t.Id(x, "await") || t.Id(x, "yield")) && own.Contains(x))
                        return true;
                //  const p = request(...) / p = request(...): returned or awaited later
                String v = null;
                if ((t.Id(st, "const") || t.Id(st, "let") || t.Id(st, "var")) && t.Id(st + 1) && t.P(st + 2, "="))
                    v = t.Tx(st + 1);
                else if (t.Id(st) && t.P(st + 1, "=") && !t.P(st - 1, "."))
                    v = t.Tx(st);
                if (v == null)
                    return false;
                foreach (var x in own)
                    if ((x > s) && t.Id(x, v) && !t.P(x - 1, "."))
                    {
                        int st2 = StatementStart(t, n.From, x);
                        for (int y = st2; y < x; ++y)
                            if ((t.Id(y, "return") || t.Id(y, "await")) && own.Contains(y))
                                return true;
                    }
                //  Also a value returned by a nested inline callback of the function (rare): not followed
                return false;
            }

            /// <summary>The literal texts of the arguments of the call at open (for a helper whose url is a parameter).</summary>
            String ArgLiterals(int open)
            {
                //  (not a translated text: _TF("Remove stop", ...) names the action for a failure line, it is no api)
                var parts = t.Args(open).Where(a => !IsTrCall(t, a)).Select(a => Literals(t, a).Trim()).Where(x => x.Length > 0);
                return String.Join(" ", parts);
            }

            /// <summary>What a function does (memoized; local functions followed up to 4 calls deep).</summary>
            public Effects Of(FnNode n) => Of(n, 0, new HashSet<FnNode>());

            Effects Of(FnNode n, int depth, HashSet<FnNode> visiting)
            {
                if (Memo.TryGetValue(n, out var m))
                    return m;
                var e = new Effects();
                if (!visiting.Add(n))
                    return e;
                foreach (var j in Own(n))
                {
                    if (IsRequest(t, j))
                    {
                        //  { Detached: true } / { Background: true }: not the click's work (telemetry, a live view, a long poll)
                        if (Regex.IsMatch(t.Span(j, Math.Max(j, t.Match(j + 1))), @"\b(?:Detached|Background)\s*:\s*true\b"))
                            continue;
                        var a = t.Args(j + 1);
                        //  (a method of a server api object: its name says what it does, api.Remove(id))
                        var url = a.Count > 0 ? Literals(t, a[0]).Trim() : "";
                        if (IsApiCall(t, j))
                            url = (t.Tx(j) + " " + String.Join(" ", a.Where(x => !IsTrCall(t, x)).Select(x => Literals(t, x).Trim()))).Trim();
                        e.Reqs.Add(new Req { At = j, Url = url, Returned = Returned(n, j) });
                        continue;
                    }
                    if ((t.Id(j, "then") || t.Id(j, "finally")) && t.P(j - 1, ".") && t.P(j + 1, "("))
                    {
                        //  An unknown promise chain (not the one of a dialog: busy while a dialog waits for the user is wrong too)
                        var st = t.Span(StatementStart(t, n.From, j), j);
                        if (!Regex.IsMatch(st, @"\bDialog\s*\.|\bConfirm\w*\s*\(|\bPrompt\s*\(|\bAlert\s*\(|\bNet\s*\.|\bfetch\s*\(|\bActivity\s*\."))
                            e.Reqs.Add(new Req { At = j, Chain = true, Returned = Returned(n, j) });
                    }
                    if (!t.Id(j))
                        continue;
                    var name = t.Tx(j);
                    if (AskToken.IsMatch(name) && (t.P(j - 1, ".") || t.P(j + 1, ":") || t.P(j + 1, "(")))
                        e.Asks = true;
                    else if ((name == "Run" || name == "Confirm") && t.P(j - 1, ".") && t.Id(j - 2, "Danger"))
                        e.Asks = true;
                    else if ((name == "Show" || name == "Open") && t.P(j - 1, ".") && t.Id(j - 2, "Dialog"))
                        e.Asks = true;
                    else if (NativeNames.Contains(name) && t.P(j + 1, "(") && (!t.P(j - 1, ".") || IsGlobal(j - 2)))
                        e.Asks = name != "alert";
                    else if (NativeAliases.TryGetValue(name, out var nat) && t.P(j + 1, "("))
                        e.Asks |= nat != "alert";
                    if (ModifierCheck.IsMatch(name) || (name == "getModifierState"))
                        e.Modifier = true;
                    if ((name == "replaceChildren" && t.P(j - 1, ".")) || ((name == "innerHTML" || name == "textContent") && t.P(j - 1, ".") && t.P(j + 1, "=") && !t.P(j + 2, "=")))
                        e.Rebuilds = true;
                    if ((name == "SetDirty" || name == "ClearDirty") && t.P(j - 1, "."))
                        e.Dirty = true;
                    if (ToastFunctions.Contains(name) && t.P(j - 1, ".") && t.Id(j - 2, "Sw") && t.P(j + 1, "("))
                    {
                        e.Says = true;
                        //  A toast with an action (Undo) offers the way back: the guide's alternative to a confirmation (SAF-5)
                        if (Regex.IsMatch(t.Span(j, Math.Max(j, t.Match(j + 1))), @"\bAction\s*:"))
                            e.Asks = true;
                        if (LiteralAt(t.S, t.T, j + 2) is String lit)
                            e.Toasts.Add((j, lit));
                    }
                    if ((name == "Alert" && t.P(j - 1, ".")) || (name == "SetStatus") || (name == "Announce" && t.P(j - 1, ".")))
                        e.Says = true;
                    //  A local function called here
                    //  (also a method of the file through this.x(...) / obj.x(...): a class or helper object of the page)
                    FnNode g = null;
                    if (t.P(j + 1, "(") && !Keywords.Contains(name) && (depth < 4) && Named.TryGetValue(name, out var gs))
                    {
                        if (!t.P(j - 1, "."))
                            g = gs[0];
                        else if ((t.Id(j - 2, "this") || (t.Id(j - 2) && !t.Id(j - 2, "Sw") && !t.P(j - 3, ".") && !t.P(j - 3, "?."))) && (name.Length > 2))
                            g = gs.FirstOrDefault(x => x.IsMethod);
                    }
                    if (g != null)
                    {
                        var sub = Of(g, depth + 1, visiting);
                        var lits = ArgLiterals(j + 1);
                        var now = sub.Reqs.Where(r => !r.Deferred).ToList();
                        if (now.Count > 0)
                            e.Reqs.Add(new Req { At = j, Url = String.Join(" ", now.Select(r => r.Url).Where(x => x.Length > 0).Append(lits)).Trim(), Returned = Returned(n, j) && (g.F.Async || now.Any(r => r.Returned)), Via = name, Chain = now.All(r => r.Chain), ViaMethod = t.P(j - 1, ".") || now.Any(r => r.ViaMethod) });
                        if (sub.Reqs.Any(r => r.Deferred))
                            e.Reqs.Add(new Req { At = j, Url = String.Join(" ", sub.Reqs.Where(r => r.Deferred).Select(r => r.Url).Append(lits)).Trim(), Deferred = true, Via = name, ViaMethod = t.P(j - 1, ".") || sub.Reqs.Any(r => r.Deferred && r.ViaMethod) });
                        Merge(e, sub);
                    }
                }
                foreach (var c in n.Children)
                {
                    var kind = KindOf(c);
                    if (kind == CallbackKind.Detached)
                        continue;
                    var sub = Of(c, depth, visiting);
                    bool ret = (kind == CallbackKind.Inline) && Returned(n, c.Head);
                    foreach (var r in sub.Reqs)
                        e.Reqs.Add(new Req { At = r.At, Url = r.Url, Returned = (kind == CallbackKind.Inline) && (ret || (r.Returned && IsThenOf(c))), Deferred = r.Deferred || (kind == CallbackKind.Deferred), Via = r.Via, Chain = r.Chain, ViaMethod = r.ViaMethod });
                    Merge(e, sub);
                }
                visiting.Remove(n);
                if (depth == 0)
                    Memo[n] = e;
                return e;
            }

            /// <summary>True if the callback is the argument of a .then / .catch / .finally (its result joins the chain).</summary>
            bool IsThenOf(FnNode c) => t.P(c.Head - 1, "(") && (t.Id(c.Head - 2, "then") || t.Id(c.Head - 2, "catch") || t.Id(c.Head - 2, "finally"));

            static void Merge(Effects e, Effects sub)
            {
                e.Asks |= sub.Asks;
                e.Modifier |= sub.Modifier;
                e.Rebuilds |= sub.Rebuilds;
                e.Dirty |= sub.Dirty;
                e.Says |= sub.Says;
                e.Toasts.AddRange(sub.Toasts);
            }

            /// <summary>True if the function n (or the function it calls itself through) is rescheduled by a timer in its own body: a polling loop.</summary>
            public bool Reschedules(FnNode n)
            {
                if (n.Name == null)
                    return false;
                foreach (var j in Own(n).Concat(n.Children.Where(c => KindOf(c) != CallbackKind.Detached).SelectMany(Own)))
                    if ((t.Id(j, "setTimeout") || t.Id(j, "setInterval")) && t.P(j + 1, "("))
                    {
                        var a = t.Args(j + 1);
                        if ((a.Count > 0) && Regex.IsMatch(t.Span(a[0].A, a[0].B), @"(?<![\w.$])" + Regex.Escape(n.Name) + @"\b"))
                            return true;
                    }
                return false;
            }
        }
    }
}
