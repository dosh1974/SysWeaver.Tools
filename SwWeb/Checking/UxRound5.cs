using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ rule families of series 4 round 5
        //
        //  From spellings to facts (the misses of the critics' unseen sets were "the same fact, another syntax"):
        //  - a secret reaches a sink through a helper of any file: a helper whose parameter reaches local storage, a log or a url query
        //    (through any wrapper: btoa, JSON.stringify, a template, encodeURIComponent) makes every call that passes a secret a finding;
        //  - a catch that only logs and returns a constant (null, false, [], {}, "", 0) turns a failure into "nothing" (an empty list
        //    reads as "no items"), also in a helper module of another file, and its callers render that lie;
        //  - a list loaded through a helper and rendered by hand without an empty state;
        //  - focus moved to anything whose Kind is danger (x.Focus(), x.E.focus(), Sw.Focus.Set(x.E), page members);
        //  - a double click whose work no click / key of the element reaches;
        //  - the families planted for the first time in round 5: icon-only buttons without a tool tip, repeated helper texts, pop-ups
        //    on load, "can't be undone" on reversible actions, bulk deletes without type to confirm, status lines without a live
        //    region, time limits, required fields not marked, colour alone, generic failure texts, locks without finally, latest
        //    wins without a read mark, plain controllers aborting writes, a click's own work awaited, a job removed after Run,
        //    swapped Message arguments, unknown icon names, own fixed bars not registered.

        /// <summary>A catch block that says nothing: empty, or only logs (console.x / Sw.Log) and returns nothing or a constant.</summary>
        static readonly Regex LogThenNothing = new Regex(@"^\{\s*(?:(?:(?:console\s*\.\s*\w+|Sw\s*\.\s*Log)\s*\([^;{}]*\)\s*;?\s*)+return(?:\s+(?:null|undefined|false|\[\s*\]|\{\s*\}|""""|''|0|new\s+(?:Map|Set)\s*\(\s*\)))?\s*;?\s*|return(?:\s+(?:null|undefined|false|\[\s*\]|\{\s*\}|""""|''|0|new\s+(?:Map|Set)\s*\(\s*\)))?\s*;?\s*)?\}$", RegexOptions.CultureInvariant);
        /// <summary>A catch block that (only logs and) returns an empty collection: the caller renders "no items".</summary>
        static readonly Regex LogThenEmpty = new Regex(@"^\{\s*(?:(?:console\s*\.\s*\w+|Sw\s*\.\s*Log)\s*\([^;{}]*\)\s*;?\s*)*return\s+(\[\s*\]|\{\s*\}|new\s+(?:Map|Set)\s*\(\s*\))\s*;?\s*\}$", RegexOptions.CultureInvariant);
        /// <summary>A catch callback that returns an empty collection: .catch(() => []) / .catch(e => { console.warn(e); return []; }).</summary>
        static readonly Regex EmptyCallback = new Regex(@"^\s*(?:async\s*)?(?:\(\s*\w*\s*\)|\w+)\s*=>\s*(?:(\[\s*\]|\(\s*\{\s*\}\s*\))|\{\s*(?:(?:console\s*\.\s*\w+|Sw\s*\.\s*Log)\s*\([^;{}]*\)\s*;?\s*)*return\s+(\[\s*\]|\{\s*\})\s*;?\s*\})\s*$", RegexOptions.CultureInvariant);
        /// <summary>An api named as a write (the last path part): ReadOnly on it is wrong.</summary>
        static readonly Regex WriteUrl = new Regex(@"(?:^|[/.])(?:Delete|Remove|Save|Set|Update|Create|Add|Insert|Import|Upload|Write|Put|Post|Send|Submit|Kill|Stop|Start|Restart|Reset|Revoke|Purge|Clear|Move|Rename|Publish|Approve|Reject|Archive|Restore|Apply|Install|Uninstall|Store|Change|Edit|Assign|Grant|Transfer|Pay|Cancel|End|Close|Enable|Disable)(?:[A-Z_]\w*)?(?:[?#].*)?$", RegexOptions.CultureInvariant);

        /// <summary>The call that makes a page member: this.NAME = [new] Sw.X( ... ) (the "(" token), -1 if none.</summary>
        static int MemberMade(Toks t, String name)
        {
            if (String.IsNullOrEmpty(name))
                return -1;
            for (int i = 2; i + 4 < t.N; ++i)
                if (t.Id(i, name) && t.P(i - 1, ".") && t.Id(i - 2, "this") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                {
                    int v = i + 2;
                    if (t.Id(v, "new"))
                        ++v;
                    if (t.Id(v, "Sw") && t.P(v + 1, ".") && t.Id(v + 2) && t.P(v + 3, "("))
                        return v + 3;
                    if (t.Id(v) && t.P(v + 1, "(") && t.SwNames.ContainsKey(t.Tx(v)))
                        return v + 1;
                }
            return -1;
        }

        /// <summary>The parameter names of a function (patterns and rest parameters as null).</summary>
        static List<String> ParamNames(Toks t, FnNode n)
        {
            var r = new List<String>();
            int h = n.Head;
            if (t.Id(h, "async"))
                ++h;
            if (t.Id(h, "function"))
            {
                ++h;
                if (t.P(h, "*"))
                    ++h;
                if (t.Id(h))
                    ++h;
            }
            else if (n.IsMethod)
                ++h;
            if (t.Id(h) && t.P(h + 1, "=>"))
            {
                r.Add(t.Tx(h));
                return r;
            }
            if (!t.P(h, "("))
                return r;
            int c = t.Match(h);
            if (c < h)
                return r;
            bool start = true;
            for (int k = h + 1; k < c; ++k)
            {
                if (start)
                {
                    r.Add(t.Id(k) && (t.P(k + 1, ",") || t.P(k + 1, ")") || t.P(k + 1, "=")) ? t.Tx(k) : null);
                    start = false;
                }
                if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                {
                    int m = t.Match(k);
                    if (m > k)
                        k = m;
                    continue;
                }
                if (t.P(k, ","))
                    start = true;
            }
            return r;
        }

        /// <summary>The key a function is called by: "Obj.Name" for Obj.Name = function, "Name" for a declaration or a method.</summary>
        static String HelperKey(Toks t, FnNode n)
        {
            if (n.Name == null)
                return null;
            int h = n.Head;
            if (t.P(h - 1, "=") && t.Id(h - 2, n.Name) && t.P(h - 3, ".") && t.Id(h - 4))
                return t.Id(h - 4, "this") ? n.Name : t.Tx(h - 4) + "." + n.Name;
            //  A method or a key of an object literal assigned to a name: const H = { save(u, p) { ... }, load: async () => ... }
            int nameTok = n.IsMethod ? (t.Id(h, "async") ? h + 1 : h) : (t.P(h - 1, ":") && t.Id(h - 2, n.Name) ? h - 2 : -1);
            if ((nameTok >= 0) && (EnclosingObject(t, nameTok) is int o) && (o > 1) && t.P(o - 1, "=") && t.Id(o - 2) && !t.P(o - 3, "."))
                return t.Tx(o - 2) + "." + n.Name;
            return n.Name;
        }

        /// <summary>
        /// The facts about the helper functions of a web root, collected before the files are checked: a helper whose parameter reaches
        /// a secret sink (storage, log, url query), a helper that turns a failure into an empty value, a helper that makes a request.
        /// </summary>
        public sealed class HelperFacts
        {
            /// <summary>key ("Bk.RememberAccount", "remember") -> (parameter index, sink, where).</summary>
            internal readonly Dictionary<String, List<(int Param, String Sink, String Where)>> SecretSinks = new Dictionary<String, List<(int, String, String)>>(StringComparer.Ordinal);
            /// <summary>key -> (the constant, where).</summary>
            internal readonly Dictionary<String, (String Const, String Where)> EmptyOnFailure = new Dictionary<String, (String, String)>(StringComparer.Ordinal);
            /// <summary>Keys of functions that make a request.</summary>
            internal readonly HashSet<String> Requesters = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>Icon names an app registers (Sw.Icons.Add / AddLine).</summary>
            internal readonly HashSet<String> Icons = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>Element ids the scripts look up (getElementById / querySelector("#id")): markup driven by script.</summary>
            internal readonly HashSet<String> ScriptIds = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>True if a file of the web root registers an overlay (Sw.Overlay.Register).</summary>
            internal bool RegistersOverlay;

            /// <summary>Collect the facts of one script (page code; the framework's files are not helpers of a page).</summary>
            public void Collect(String rel, String text)
            {
                foreach (Match m in Regex.Matches(text, @"\bIcons\s*\.\s*Add(?:Line)?\s*\(\s*[""'](\w+)[""']"))
                    Icons.Add(m.Groups[1].Value);
                //  an app's icon table registered in a loop: { name: '<path d="..."/>', ... }
                foreach (Match m in Regex.Matches(text, @"^\s*[""']?([A-Za-z][A-Za-z0-9]*)[""']?\s*:\s*['""`]\s*<(?:path|circle|rect|g|polyline|polygon|line|ellipse|svg)\b", RegexOptions.Multiline))
                    Icons.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, @"(?:getElementById\(\s*|querySelector\(\s*[""']#)[""']?([\w-]+)[""']"))
                    ScriptIds.Add(m.Groups[1].Value);
                if (text.Contains("Overlay.Register"))
                    RegistersOverlay = true;
                if (DesignChecker.IsFramework(rel) || Checker.IsBundlePath(rel))
                    return;
                List<JsToken> sig;
                try
                {
                    sig = JsLexer.Tokenize(text).Where(x => x.IsSignificant).ToList();
                }
                catch
                {
                    return;
                }
                var t = new Toks(text, sig);
                t.FindAliases();
                var flow = new Flow(t);
                var src = new SourceText(text);
                String Where(int k) => rel + " line " + src.GetLineCol(t.S[k].Start).Line;
                foreach (var fn in flow.Fns)
                {
                    var key = HelperKey(t, fn);
                    if (key == null)
                        continue;
                    if (Enumerable.Range(fn.F.Start, Math.Max(0, fn.F.End - fn.F.Start + 1)).Any(j => IsRequest(t, j)))
                        Requesters.Add(key);
                    foreach (var (p, sink, at) in ParamSinks(t, fn))
                    {
                        if (!SecretSinks.TryGetValue(key, out var l))
                            SecretSinks[key] = l = new List<(int, String, String)>();
                        l.Add((p, sink, Where(at)));
                    }
                    if (EmptyCatch(t, flow, fn) is var ec && (ec.At >= 0))
                        EmptyOnFailure[key] = (ec.Const, Where(ec.At));
                }
                //  A module object: const M = (() => { function a() ... return { a, b: c }; })() / const M = { a, b }: M.a is the function a
                foreach (Match mm in Regex.Matches(text, @"\b(?:const|let|var)\s+(\w+)\s*=\s*(?:\(\s*(?:function\s*\(\s*\)|\(\s*\)\s*=>)\s*\{(?<body>.*?)\breturn\s*\{(?<exp>[^{}]*)\}\s*;?\s*\}\s*\)\s*\(\s*\)|\{(?<exp>[^{}]*)\})", RegexOptions.Singleline))
                {
                    var mod = mm.Groups[1].Value;
                    foreach (var part in mm.Groups["exp"].Value.Split(','))
                    {
                        var pm = Regex.Match(part.Trim(), @"^(\w+)(?:\s*:\s*(\w+))?$");
                        if (!pm.Success)
                            continue;
                        var exp = pm.Groups[1].Value;
                        var local = pm.Groups[2].Success ? pm.Groups[2].Value : exp;
                        var mk = mod + "." + exp;
                        if (Requesters.Contains(local))
                            Requesters.Add(mk);
                        if (SecretSinks.TryGetValue(local, out var sl))
                            SecretSinks[mk] = sl;
                        if (EmptyOnFailure.TryGetValue(local, out var ef))
                            EmptyOnFailure[mk] = ef;
                    }
                }
            }
        }

        /// <summary>The parameters of a function that reach a secret sink: (index, sink, token).</summary>
        static IEnumerable<(int Param, String Sink, int At)> ParamSinks(Toks t, FnNode fn)
        {
            var ps = ParamNames(t, fn);
            if (ps.All(p => p == null))
                yield break;
            for (int i = fn.F.Start; i <= fn.F.End; ++i)
            {
                (int A, int B) v = (-1, -1);
                String sink = null;
                if ((t.Id(i, "setItem") && t.P(i - 1, ".") && (t.Id(i - 2, "localStorage") || t.Id(i - 2, "sessionStorage")) || (t.Id(i, "Set") && t.P(i - 1, ".") && (t.Id(i - 2, "Local") || t.Id(i - 2, "Session") || t.Id(i - 2, "Storage")))) && t.P(i + 1, "(") && (t.Args(i + 1) is var sa) && (sa.Count > 1))
                {
                    v = sa[1];
                    sink = "local storage";
                }
                else if (t.Id(i, "cookie") && t.Id(i - 2, "document") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                {
                    v = (i + 2, ExprEnd(t, i + 2));
                    sink = "a cookie";
                }
                else if (t.Id(i) && Regex.IsMatch(t.Tx(i), "^(?:log|info|debug|warn|error|trace|dir|table)$") && t.P(i - 1, ".") && t.Id(i - 2, "console") && t.P(i + 1, "(") && (t.Match(i + 1) is int lc) && (lc > i))
                {
                    v = (i + 2, lc - 1);
                    sink = "the console";
                }
                else if (((t.S[i].Kind == JsTokenKind.String) && t.P(i + 1, "+") && Regex.IsMatch(Literals(t, (i, i)), @"[?&;][\w.-]+=$")) || (((t.S[i].Kind == JsTokenKind.TemplateHead) || (t.S[i].Kind == JsTokenKind.TemplateMiddle)) && Regex.IsMatch(t.Tx(i).TrimEnd('{').TrimEnd('$'), @"[?&;][\w.-]+=$")))
                {
                    int a = t.S[i].Kind == JsTokenKind.String ? i + 2 : i + 1;
                    v = (a, ExprEnd(t, a));
                    sink = "a url query";
                }
                if (sink == null)
                    continue;
                for (int k = v.A; (k <= v.B) && (k < t.N); ++k)
                    if (t.Id(k) && !t.P(k - 1, ".") && !t.P(k + 1, "(") && (ps.IndexOf(t.Tx(k)) is int pi) && (pi >= 0))
                        yield return (pi, sink, i);
            }
        }

        /// <summary>A catch of a function that only logs and returns an empty value (an empty list, object, text or 0): (the constant, the catch token).</summary>
        static (String Const, int At) EmptyCatch(Toks t, Flow flow, FnNode fn)
        {
            foreach (var j in flow.Own(fn))
            {
                if (t.Id(j, "catch") && !t.P(j - 1, ".") && t.P(j - 1, "}"))
                {
                    int b = t.P(j + 1, "(") ? t.Match(j + 1) + 1 : j + 1;
                    //  (a load: the try block awaits or makes a request)
                    int to = -1;
                    for (int q = j - 2, d = 0; q >= fn.From; --q)
                        if (t.P(q, "}"))
                            ++d;
                        else if (t.P(q, "{") && (d-- == 0))
                        {
                            to = q;
                            break;
                        }
                    bool loads = (to > 0) && t.Id(to - 1, "try") && Enumerable.Range(to, j - to).Any(q => t.Id(q, "await") || IsRequest(t, q));
                    if (loads && t.P(b, "{") && (t.Match(b) is int be) && (be > b) && (LogThenEmpty.Match(t.Span(b, be)) is Match m) && m.Success)
                        return (Regex.Replace(m.Groups[1].Value, @"\s+", ""), j);
                }
                if (t.Id(j, "catch") && t.P(j - 1, ".") && t.P(j + 1, "(") && (t.Match(j + 1) is int cc) && (cc > j) && (EmptyCallback.Match(t.Span(j + 2, cc - 1)) is Match em) && em.Success)
                {
                    int s0 = StatementStart(t, fn.From, j);
                    if ((t.Id(s0, "return") || !fn.F.Block) && Enumerable.Range(s0, j - s0).Any(q => IsRequest(t, q)))
                        return (Regex.Replace(em.Groups[1].Success ? em.Groups[1].Value : em.Groups[2].Value, @"[\s()]+", ""), j);
                }
            }
            return (null, -1);
        }

        /// <summary>The helper key a call at i (the name before "(") uses: "Obj.Name", "Name" (bare or this.Name), null otherwise.</summary>
        static IEnumerable<String> CallKeys(Toks t, int i)
        {
            if (!t.Id(i) || !t.P(i + 1, "("))
                yield break;
            if (t.P(i - 1, "."))
            {
                if (t.Id(i - 2, "this"))
                    yield return t.Tx(i);
                else if (t.Id(i - 2) && !t.P(i - 3, "."))
                    yield return t.Tx(i - 2) + "." + t.Tx(i);
            }
            else if (!IsDeclaration(t, i) && !t.Id(i - 1, "function"))
                yield return t.Tx(i);
        }

        static readonly Regex RequiredText = new Regex(@"(?i)\brequired\b|must not be empty|can'?t be empty|cannot be empty|is mandatory|\benter (?:a|an|the|your)\b(?![^.]*\bvalid\b)[^.]*|\bis missing\b|\bchoose (?:a|an|the)\b", RegexOptions.CultureInvariant);
        static readonly Regex GenericFail =new Regex(@"^\s*(?:an?\s+)?(?:error|errors|failed|failure|fail|oops|whoops|something went wrong|an error (?:has )?occurred|unknown error|request failed|operation failed|could not complete|not possible|try again|error occurred)\s*[.!]*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex HintPerItem = new Regex(@"^\s*(?:double[- ]?click|click|tap|right[- ]?click|long[- ]?press|press|drag|hover|select)\b.{0,40}\bto\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex NotUndoable = new Regex(@"can'?t be undone|can ?not be undone|cannot be reversed|irreversible|permanently|is permanent|for ever|forever", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ReversibleVerb = new Regex(@"(?i)\b(?:archiv\w*|hide|hidden|disabl\w*|suspend\w*|deactivat\w*|pause\w*|mute\w*|snooze\w*|unpublish\w*|move\w*|lock\w*|stop\w*|close\w*)\b", RegexOptions.CultureInvariant);
        static readonly Regex DeleteVerb = new Regex(@"(?i)\b(?:delet\w*|remov\w*|eras\w*|purg\w*|wip\w*|destroy\w*|drop\w*|kill\w*|revok\w*|clear\w*)\b", RegexOptions.CultureInvariant);

        /// <summary>
        /// The round 5 rules of a script (called by CheckRound4, page code only; the secret scope and the reach are set).
        /// </summary>
        static void CheckRound5(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<Reg> regs)
        {
            var text = ctx.Text;
            var facts = ctx.Options.UxSizes?.Helpers;
            var reported = new HashSet<(String, int)>();
            void Once(Rule rule, int at, String msg)
            {
                if (reported.Add((rule.Id, ctx.Source.GetLineCol(t.S[at].Start).Line)))
                {
                    if (rule.Guide == null)
                        ctx.Add(rule, t.S[at].Start, msg);
                    else
                        AddOnce(ctx, rule, t.S[at].Start, msg);
                }
            }
            //  The helpers of this file too (a single file checked without the folder pass)
            var local = new HelperFacts();
            if ((facts == null) || !facts.Requesters.Any())
                local.Collect(ctx.WebRel, text);
            var all = facts ?? local;
            bool Requester(String key) => all.Requesters.Contains(key) || local.Requesters.Contains(key);
            (String Const, String Where)? Empty(String key) => all.EmptyOnFailure.TryGetValue(key, out var e) ? e : local.EmptyOnFailure.TryGetValue(key, out var e2) ? e2 : null;
            List<(int Param, String Sink, String Where)> Sinks(String key) => all.SecretSinks.TryGetValue(key, out var s) ? s : local.SecretSinks.TryGetValue(key, out var s2) ? s2 : null;

            //  ---- FBK-5: a helper of this file that turns a failure into an empty value (the list then says "none")
            foreach (var fn in flow.Fns)
                if ((EmptyCatch(t, flow, fn) is var ec) && (ec.At >= 0))
                    Once(Rule.UxFbk5, ec.At, "\"" + (fn.Name ?? "this function") + "\" turns a failure into " + ec.Const + " after only logging it: its caller shows an empty list / nothing as if there were no data, and the user never learns that it failed; let the failure reach the caller (no catch: a button shows \"<name>: not done\", a list its error state with Try again), or show it here (Sw.Fail(e))");

            for (int i = 0; i < t.N; ++i)
            {
                //  ================= helpers of any file (facts, not spellings)
                if (t.Id(i) && t.P(i + 1, "(") && (flow.Around(i) is var around) && !((around != null) && (around.Head == i)))
                    foreach (var key in CallKeys(t, i))
                    {
                        //  ---- SEC-1: a secret passed to a helper whose parameter reaches storage / a log / a url query
                        if ((Sinks(key) is var sl) && (sl != null) && (t.Args(i + 1) is var args))
                            foreach (var (p, sink, where) in sl)
                                if ((p < args.Count) && ((SecretValueIn(t, args[p].A, args[p].B) ?? SecretIn(t, args[p].A, args[p].B)) is String sec))
                                {
                                    Once(Rule.UxSec1, i, "a secret (\"" + Short(sec) + "\") passed to " + key + ", which writes it to " + sink + " (" + where + "): " + (sink == "the console" ? "the console is kept in support logs, screen shares and bug reports" : sink == "a url query" ? "urls end up in the server log, the browser history and the Referer" : "the browser keeps it as plain text that every script of the site and anyone at the device can read, also after sign out") + "; keep keys and passwords out of " + sink + " (the session carries them; ask again)");
                                    break;
                                }
                        //  ---- FBK-5: the call of a helper of another file that turns a failure into an empty value
                        if ((Empty(key) is var em) && (em != null) && !em.Value.Where.StartsWith(ctx.WebRel + " ", StringComparison.Ordinal))
                            Once(Rule.UxFbk5, i, key + " turns a failure into " + em.Value.Const + " (" + em.Value.Where + "): here a failed load reads as \"no data\" and nobody says it failed; let the helper throw (or return the error) and show the failure with Try again (Sw.StateView / Sw.Fail)");
                    }

                //  ---- TXT-1: a rejection text made for the user but not translated: return Promise.reject("Nothing selected")
                if (t.Chain(i, "Promise", "reject") && t.P(i + 1, "(") && (t.Str(i + 2) is String rj) && t.P(i + 3, ")") && Regex.IsMatch(rj, @"[A-Za-z]{2,}\s+[A-Za-z]{2,}"))
                    Once(Rule.TrUntranslated, i, "Promise.reject(\"" + Short(rj) + "\"): the button (Sw.Work) shows a rejection's text to the user, untranslated; reject with an Error whose message is _TF(\"" + Short(rj) + "\", \"context\") (or show a field message and return false)");

                //  ---- TIP-1: an icon-only Sw.Button without a tool tip / name
                if (t.SwComponent(i) && (t.Tx(i) == "Button" || (t.SwNames.TryGetValue(t.Tx(i), out var bm) && bm == "Button")) && t.P(i + 1, "(") && t.P(i + 2, "{") && (t.Match(i + 2) is int bo) && (bo > i))
                {
                    bool spread = Enumerable.Range(i + 3, Math.Max(0, bo - i - 3)).Any(k => t.P(k, "..."));
                    //  (a name / tool tip set later: b.SetTitle(...), b.SetLabel(...), b.E.title = ..., aria-label)
                    int bs = StatementStart(t, flow.Around(i)?.From ?? 0, i);
                    var bv = Regex.Match(t.Span(bs, i), @"^(?:(?:const|let|var)\s+|this\s*\.\s*)?(\w+)\s*=");
                    if (bv.Success && Regex.IsMatch(text, @"\b" + Regex.Escape(bv.Groups[1].Value) + @"\s*\.\s*(?:SetTitle|SetLabel|SetContent|SetText)\s*\(|\b" + Regex.Escape(bv.Groups[1].Value) + @"\s*\.\s*E\s*\.\s*(?:title\s*=|setAttribute\(\s*[""'](?:title|aria-label))"))
                        spread = true;
                    if (!spread && (ObjKey(t, i + 2, "Icon") != null) && (ObjKey(t, i + 2, "Text") == null) && (ObjKey(t, i + 2, "Title") == null) && (ObjKey(t, i + 2, "AriaLabel") == null) && (ObjKey(t, i + 2, "Label") == null) && (ObjKey(t, i + 2, "Copy") == null))
                        Once(Rule.UxTip1, i, "an icon-only Sw.Button (Icon " + Short(t.Span(ObjKey(t, i + 2, "Icon").Value.A, ObjKey(t, i + 2, "Icon").Value.B)) + ") without Title: it has no name for screen readers (they read the icon's name) and no tool tip that says what it does; add Title: _TF(\"Reload the list of ...\", \"context\") (the name, the tool tip, the long press hint)");
                }

                //  ---- TXT-30: a generic failure text ("Error", "Something went wrong")
                if ((t.Chain(i, "Sw", "Fail") || t.Chain(i, "Sw", "Toast") || t.Chain(i, "Sw", "Error") || t.Chain(i, "Sw", "Warn") || t.Chain(i, "Dialog", "Alert")) && t.P(i + 1, "(") && (t.Args(i + 1) is var fa) && (fa.Count > 0))
                {
                    var ft = t.Lit(fa[0]) ?? (IsLiteralRange(t, fa[0]) ? Literals(t, fa[0]) : null);
                    if ((ft != null) && GenericFail.IsMatch(ft))
                        Once(Rule.UxTxt30, i, "the failure text \"" + Short(ft) + "\" says nothing: what failed, why and what to do; pass the error (Sw.Fail(e) composes \"<what>: not done. <the server's reason>\") or Sw.Net.Message(_TF(\"Delete backup\", \"...\"), e)");
                }
                //  ---- TXT-30: Sw.Net.Message(error, "action") with the arguments swapped
                if ((t.Chain(i, "Net", "Message") || t.Chain(i, "Net", "MessageParts") || t.Chain(i, "Net", "FailText")) && t.P(i + 1, "(") && (t.Args(i + 1) is var ma) && (ma.Count > 1) && !IsLiteralRange(t, ma[0]) && !IsTrCall(t, ma[0]) && (IsLiteralRange(t, ma[1]) || IsTrCall(t, ma[1])))
                    Once(Rule.UxTxt30, i, "Sw.Net." + t.Tx(i) + "(" + Short(t.Span(ma[0].A, ma[0].B)) + ", " + Short(t.Span(ma[1].A, ma[1].B)) + "): the arguments are swapped: the first is the action's name (a translated text), the second the error (\"Error: ...: Rename.\" otherwise)");

                //  ---- POP-11: a dialog opened by page start code (nobody asked for it)
                if ((t.Chain(i, "Dialog", "Alert") || t.Chain(i, "Dialog", "Open") || t.Chain(i, "Dialog", "Show") || t.Chain(i, "Dialog", "Confirm")) && t.P(i + 1, "(") && StartOnly(t, flow, i) && !InCatchOrErrorBranch(t, flow, i))
                    Once(Rule.UxPop11, i, "Sw.Dialog." + t.Tx(i) + " opened by the page's start code: a pop-up nobody asked for takes the focus and blocks the page on load; show the information in the page (a hint line, an empty state, Sw.Toast for a short note) and open dialogs only from a user's action");

                //  ---- SAF-6 / SAF-4: the wording and strength of a confirmation
                if ((t.Chain(i, "Dialog", "Confirm") || t.Chain(i, "Cells", "ConfirmDanger") || t.Chain(i, "Danger", "Run")) && t.P(i + 1, "(") && (t.Match(i + 1) is int qc) && (qc > i))
                {
                    var all5 = ShownTexts(t, i + 2, qc - 1);
                    var fnName = flow.Around(i)?.Name ?? "";
                    var fnSpan = flow.Around(i) is FnNode qf ? t.Span(qf.F.Start, qf.F.End) : "";
                    var subject = all5 + " " + fnName;
                    if (NotUndoable.IsMatch(all5) && ReversibleVerb.IsMatch(subject) && !DeleteVerb.IsMatch(subject))
                        Once(Rule.UxSaf6, i, "\"" + Short(NotUndoable.Match(all5).Value) + "\" in the question of a reversible action (" + Short(ReversibleVerb.Match(subject).Value) + "): people stop trusting the warning when it is not true, and skip it when it is; say what happens and how to undo it (\"The backup moves to the archive; you can restore it there.\")");
                    bool danger = t.Chain(i, "Cells", "ConfirmDanger") || t.Chain(i, "Danger", "Run") || Regex.IsMatch(t.Span(i, qc), @"\bDanger\s*:\s*true\b");
                    bool bulk = Regex.Matches(all5, @"[^.?!\n]*\b(?:all|every|everything)\b[^.?!\n]*", RegexOptions.IgnoreCase).Cast<Match>().Any(x => DeleteVerb.IsMatch(x.Value));
                    if (danger && bulk && !Regex.IsMatch(fnSpan, @"\b(?:Confirm|TypeToConfirm|ConfirmText)\s*:"))
                        Once(Rule.UxSaf4, i, "a destructive action on everything (\"" + Short(Regex.Match(all5, @"[^.?!]*\b(?:all|every|everything)\b[^.?!]*", RegexOptions.IgnoreCase).Value.Trim()) + "\") is confirmed with one click: a danger button is one Enter away from losing all data; ask the user to type the name or the count (Confirm: \"delete all\") and say how many items go");
                }

                //  ---- ACC-7: a page timer of a minute or more that signs out / leaves / clears the form without a warning
                if (t.Id(i, "setTimeout") && !t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var ta) && (ta.Count > 1) && (ConstMs(t, ta[1]) is double ms) && (ms >= 60000) && (flow.At(ta[0].A) is FnNode tf))
                {
                    var deep = DeepSpan(t, flow, tf);
                    if (Regex.IsMatch(deep, @"(?i)sign\s*out|signout|logout|log\s*out|login\.html|Page\s*\.\s*Navigate|location\s*(?:\.\s*href\s*)?=|\.value\s*=\s*""""|\.reset\s*\(|\bClear\w*\s*\(|\.Close\s*\(") && !Regex.IsMatch(text, @"\bWarnTimeOut\b"))
                        Once(Rule.UxAcc7, i, "a time limit of " + (ms / 60000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " min that " + (Regex.IsMatch(deep, @"(?i)sign|log|login") ? "signs the user out" : "closes / clears what the user works on") + " without a warning: slow readers and people who were interrupted lose their work; warn at least 20 s before with a way to extend (Sw.Session.WarnTimeOut), or don't limit the time");
                }
            }

            //  ---- FOC-2 (already in round 4 for vars) / FRM-2 / ACC-4 / ACC-2 / FRM-8 / TIP-7 / FBK-* model misuses
            CheckRound5More(ctx, t, flow, vars, regs, Once, Requester, Empty);
        }

        /// <summary>The texts a call shows: string literals and the first argument of a translation call (not the translators' context).</summary>
        static String ShownTexts(Toks t, int a, int b)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = a; k <= b; ++k)
            {
                if (t.Id(k) && TrFunctions.Contains(t.Tx(k)) && t.P(k + 1, "(") && (t.Match(k + 1) is int c) && (c > k))
                {
                    if (t.Str(k + 2) is String s)
                        sb.Append(s).Append('\n');
                    k = c;
                    continue;
                }
                if (t.Str(k) is String s2)
                    sb.Append(s2).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>A number of milliseconds written as a constant (120000, 2 * 60 * 1000, 120e3), null if not.</summary>
        static double? ConstMs(Toks t, (int A, int B) r)
        {
            double v = 1;
            bool any = false;
            for (int k = r.A; k <= r.B; ++k)
            {
                if (t.S[k].Kind == JsTokenKind.Number && Double.TryParse(t.Tx(k).Replace("_", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    v *= n;
                    any = true;
                }
                else if (!t.P(k, "*") && !t.P(k, "(") && !t.P(k, ")"))
                    return null;
            }
            return any ? v : null;
        }

        /// <summary>True if a token is only run by the page's start (Sw.Page.Main, Start / Init / constructor, the top level), not by a user's action, a timer or a subscription.</summary>
        static bool StartOnly(Toks t, Flow flow, int at)
        {
            if (InReach(flow, at))
                return false;
            var fn = flow.Around(at);
            if (fn == null)
                return false;   //  (top level code: a snippet or a library, not a page start we can tell)
            //  Inside a timer / listener / subscription callback: not the start
            for (var n = fn; n != null; n = n.Parent)
            {
                if ((n.Name != null) && Regex.IsMatch(n.Name, @"^(?:Start|Init|Initialize|Main|Load|OnLoad|Setup|constructor|init|start|main|setup)$"))
                    return true;
                if (flow.KindOf(n) != CallbackKind.Inline)
                {
                    //  the function given to Sw.Page.Main / DOMContentLoaded / load
                    var before = t.Span(Math.Max(0, n.Head - 6), Math.Max(0, n.Head - 1));
                    if (Regex.IsMatch(before, @"Page\s*\.\s*Main\s*\(\s*$|[""'](?:DOMContentLoaded|load)[""']\s*,\s*$"))
                        return true;
                    //  a named page method called from the start
                    if ((n.Name != null) && Regex.IsMatch(t.T, @"\b(?:Start|Init|Main|init|start)\s*\([^)]*\)\s*\{[^}]*\bthis\s*\.\s*" + Regex.Escape(n.Name) + @"\s*\("))
                        return true;
                    return false;
                }
            }
            return false;
        }

        /// <summary>True if a token is in a catch block, a .catch callback or the branch of an error test.</summary>
        static bool InCatchOrErrorBranch(Toks t, Flow flow, int at)
        {
            var fn = flow.Around(at);
            int from = fn?.From ?? 0;
            int depth = 0;
            for (int k = at - 1; k >= from; --k)
            {
                if (t.P(k, "}"))
                    ++depth;
                else if (t.P(k, "{"))
                {
                    if (depth == 0)
                    {
                        if (t.Id(k - 1, "catch") || (t.P(k - 1, ")") && t.Match(k - 1) is int o && (o > 0) && t.Id(o - 1, "catch")))
                            return true;
                        if (t.P(k - 1, ")") && (t.Match(k - 1) is int io) && (io > 0) && t.Id(io - 1, "if") && Regex.IsMatch(t.Span(io, k - 1), @"(?i)err|fail|!\s*\w*ok\b|offline|!\s*\w+\s*\)|unsupported|missing"))
                            return true;
                    }
                    else
                        --depth;
                }
            }
            for (var n = fn; n != null; n = n.Parent)
                if (t.P(n.Head - 1, "(") && t.Id(n.Head - 2, "catch"))
                    return true;
            return false;
        }

        static void CheckRound5More(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<Reg> regs, Action<Rule, int, String> Once, Func<String, bool> Requester, Func<String, (String Const, String Where)?> Empty)
        {
            var text = ctx.Text;
            //  Names made by a call: x = [new] Sw.X({...}) / this.x = ... -> the "(" token
            int MadeBy(String name)
            {
                if (vars.TryGetValue(name, out var v) && (v.Call > 0))
                    return v.Call;
                return MemberMade(t, name);
            }

            for (int i = 0; i < t.N; ++i)
            {
                //  ---- FRM-2: a field that the page checks as required but doesn't mark (no Required: no marker, no aria-required)
                if (t.Id(i, "SetError") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var ea) && (ea.Count > 0) && ((t.Lit(ea[0]) ?? Literals(t, ea[0])) is String et) && RequiredText.IsMatch(et))
                {
                    var recv = t.Tx(i - 2);
                    int mc = MadeBy(recv);
                    if ((mc > 0) && t.P(mc + 1, "{") && (ObjKey(t, mc + 1, "Required") == null))
                        Once(Rule.UxFrm2, i, "\"" + recv + "\" is checked as required (\"" + Short(et) + "\") but not marked: nobody knows before the error, and screen readers don't say \"required\"; give it Required: true (the marker, aria-required and the empty check; Validate() shows the message)");
                }
                //  ---- FRM-2: a Sw field whose Validate says "required" / "Enter the ..." for an empty value, without Required
                if (t.Id(i, "Validate") && t.Key(i) && (EnclosingObject(t, i) is int vo) && (vo > 0) && IsSwOptions(t, vo) && (ObjKey(t, vo, "Required") == null) && (t.Match(vo) is int vc) && (vc > vo))
                {
                    var vt = ShownTexts(t, i + 2, Math.Min(vc - 1, ExprEnd(t, i + 2)));
                    if (RequiredText.IsMatch(vt))
                        Once(Rule.UxFrm2, i, "a field whose Validate asks for a value (\"" + Short(RequiredText.Match(vt).Value) + "\") but that is not marked Required: nobody knows before the error, and screen readers don't say \"required\"; add Required: true (the marker and aria-required; keep Validate for the rest)");
                }
                //  ---- FRM-8: a lock set by page code around work without a finally (a failure leaves it locked for good)
                if (t.Id(i, "Locked") && t.P(i - 1, ".") && t.P(i + 1, "(") && t.Id(i + 2, "true") && (flow.Around(i) is FnNode lf))
                {
                    var recv = t.Tx(i - 2);
                    var body = t.Span(i, lf.F.End);
                    bool unlocks = Regex.IsMatch(body, @"\b" + Regex.Escape(recv) + @"\s*\.\s*Locked\s*\(\s*false\s*\)");
                    bool work = Regex.IsMatch(body, @"\bawait\b|\.then\s*\(");
                    bool fin = Regex.IsMatch(t.Span(lf.F.Start, lf.F.End), @"\bfinally\b");
                    if (unlocks && work && !fin)
                        Once(Rule.UxFrm8, i, recv + ".Locked(true) ... Locked(false) around work without try / finally: when the work fails the field stays locked for good, and its line says work runs when none does; let the lock end with the work: " + recv + ".Locked(true, work) or Sw.Lock([" + recv + "], work) (always undone), or unlock in finally");
                }
                //  ---- FBK-2 / FBK-4: a click's work that waits for itself; a job removed right after Run
                if (t.Chain(i, "Work", "Current") && t.P(i + 1, "(") && (t.Args(i + 1) is var wa) && (wa.Count > 0) && (flow.Around(i) is FnNode wf))
                {
                    var target = Regex.Replace(t.Span(wa[0].A, wa[0].B), @"\.E$", "");
                    //  the OnClick of the button itself: target = b(.E) and the function is the value of OnClick of b's options
                    for (var n = wf; n != null; n = n.Parent)
                        if (t.Id(n.Head - 2, "OnClick") && (EnclosingObject(t, n.Head - 2) is int oo) && (oo > 0) && Regex.IsMatch(t.Span(Math.Max(0, oo - 8), oo - 1), @"\b" + Regex.Escape(target.Split('.').Last()) + @"\s*=\s*(?:new\s+)?(?:Sw\s*\.\s*)?Button\s*\(\s*$"))
                        {
                            Once(Rule.UxFbk2, i, "Sw.Work.Current(" + Short(t.Span(wa[0].A, wa[0].B)) + ") awaited inside that button's own OnClick: the work waits for itself and the button stays busy for ever; use the result of your own await (the work is this function)");
                            break;
                        }
                }
                if (t.Id(i, "Remove") && t.P(i - 1, ".") && t.Id(i - 2) && t.P(i + 1, "(") && (flow.Around(i) is FnNode rf))
                {
                    var job = t.Tx(i - 2);
                    if (Regex.IsMatch(t.Span(rf.From, i), @"\b" + Regex.Escape(job) + @"\s*=\s*(?:await\s+)?Sw\s*\.\s*Activity\s*\.\s*(?:Run|Start|RunServer|Track)\s*\(") && !Regex.IsMatch(t.Span(rf.From, i), @"\bawait\s+" + Regex.Escape(job) + @"\b|\b" + Regex.Escape(job) + @"\s*\.\s*Finished\b"))
                        Once(Rule.UxFbk4, i, job + ".Remove() right after Sw.Activity.Run: Remove cancels a running job, so the work never runs (and \"Cancelled\" is announced); to keep the list quiet use Quiet: true / EndsWithPage, and remove a job only after it ended");
                }
                //  ---- FBK-7: latest wins with Sw.Cancel.Controller + a Send not marked read (the abort does nothing: the older answer wins)
                if (t.Id(i, "abort") && t.P(i - 1, ".") && t.Id(i - 2) && t.P(i + 1, "(") && (flow.Around(i) is FnNode af))
                {
                    var ctl = t.Tx(i - 2);
                    var fb = t.Span(af.F.Start, af.F.End);
                    var ctlMade = Regex.IsMatch(text, @"\b" + Regex.Escape(ctl) + @"\s*=\s*Sw\s*\.\s*Cancel\s*\.\s*Controller\s*\(");
                    var plainMade = Regex.IsMatch(text, @"\b" + Regex.Escape(ctl) + @"\s*=\s*new\s+AbortController\s*\(");
                    foreach (Match sm in Regex.Matches(fb, @"Net\s*\.\s*Send\s*\((?<a>[^;]*?\bSignal\s*:\s*(?:" + Regex.Escape(ctl) + @"\s*\.\s*signal|\w+)[^;]*)"))
                    {
                        if (Regex.IsMatch(sm.Groups["a"].Value, @"\b(?:ReadOnly|AbortSafe)\s*:\s*true"))
                            continue;
                        if (ctlMade)
                        {
                            Once(Rule.UxFbk7, i, ctl + ".abort() of a Sw.Cancel.Controller before a new Sw.Net.Send that is not marked read: the cancellation model never aborts a sent write, so the older answer still arrives and can replace the newer one (search as you type shows old results); use Sw.Net.Latest(key).Read(url, arg) (an older answer is never delivered), or mark the search a read (Sw.Net.Read)");
                            break;
                        }
                    }
                }
                //  ---- POP-6: a plain AbortController given to a write
                if (t.Chain(i, "Net", "Send") && t.P(i + 1, "(") && (t.Match(i + 1) is int sc) && (sc > i))
                {
                    var call = t.Span(i, sc);
                    var sg = Regex.Match(call, @"\bSignal\s*:\s*(\w+)\s*\.\s*signal\b");
                    if (sg.Success && !Regex.IsMatch(call, @"\b(?:ReadOnly|AbortSafe)\s*:\s*true") && Regex.IsMatch(text, @"\b" + Regex.Escape(sg.Groups[1].Value) + @"\s*=\s*new\s+AbortController\s*\(") && Regex.IsMatch(text, @"\b" + Regex.Escape(sg.Groups[1].Value) + @"\s*\.\s*abort\s*\(|\bsignal\b"))
                    {
                        var u = (t.Args(i + 1) is var sa2) && (sa2.Count > 0) ? Literals(t, sa2[0]).Trim() : "";
                        var lu = Regex.Replace(u.Split(' ').LastOrDefault() ?? "", @"[?#].*$", "");
                        //  (a known api named as a write; an export / download / render is safe to stop)
                        if ((lu.Length > 0) && !ReadUrl.IsMatch(lu) && !Regex.IsMatch(lu, @"(?:^|[/.])(?:Export|Download|Render|Print|Preview|Report)\w*$"))
                            Once(Rule.UxPop6, i, "a plain AbortController's signal on a write (" + Short(u.Length > 0 ? u : "Sw.Net.Send") + "): its abort cuts the request after it left, so the server may have done it while the page says it stopped (and nothing is shown); use Sw.Cancel.Controller (never aborts a sent write: \"finished before it could stop\"), or mark a read ReadOnly");
                    }
                }
                //  ---- TIP-7: a helper text made for every row / item
                if (t.Id(i) && TrFunctions.Contains(t.Tx(i)) && t.P(i + 1, "(") && (t.Str(i + 2) is String ht) && HintPerItem.IsMatch(ht) && (flow.Around(i) is FnNode hf) && PerItem(t, flow, hf)
                    && !Regex.IsMatch(t.Span(StatementStart(t, hf.From, i), i), @"\.title\s*=|\b(?:Title|title|Hint|AriaLabel)\s*:|aria-label|setAttribute\(\s*[""']title|SetTitle\s*\(|\+\s*$"))
                    Once(Rule.UxTip7, i, "the hint \"" + Short(ht) + "\" is made for every row / item: a text repeated on every line costs every reader time and says nothing new after the first; say it once (the list's header, its empty state, a tool tip), or make the item work the expected way (a click opens)");
                //  ---- ACC-2: a state shown by colour alone (the colour chosen by a condition, the text only a symbol)
                if (t.Id(i, "color") && t.P(i - 1, ".") && t.Id(i - 2, "style") && t.P(i + 1, "=") && !t.P(i + 2, "=") && (flow.Around(i) is FnNode cf))
                {
                    int e = ExprEnd(t, i + 2);
                    var el = t.Tx(i - 4);
                    if (Enumerable.Range(i + 2, Math.Max(0, e - i - 1)).Any(k => t.P(k, "?")))
                    {
                        var body = t.Span(cf.F.Start, cf.F.End);
                        var tm = Regex.Match(body, @"\b" + Regex.Escape(el) + @"\s*\.\s*(?:textContent|innerText)\s*=\s*([""'`])([^""'`]*)\1");
                        if (!tm.Success || !Regex.IsMatch(tm.Groups[2].Value, @"\p{L}{2,}"))
                            Once(Rule.UxAcc2, i, "the state of \"" + el + "\" is shown by its colour alone (style.color by a condition" + (tm.Success ? ", the text \"" + tm.Groups[2].Value + "\"" : "") + "): colour blind users, screen readers and a print can't tell ok from failed; add a word and an icon (Sw.Badge({ Kind, Text }) / a status cell)");
                    }
                }
            }

            //  ---- ACC-4: a status line changed by code without a live region
            foreach (Match m in Regex.Matches(text, @"(?:\bthis\s*\.\s*|\b(?:const|let|var)\s+)(?<n>\w*(?:[Ss]tatus|[Mm]essage|[Rr]esult|[Nn]otice|[Ff]eedback)\w*)\s*=\s*(?:Sw\s*\.\s*el|document\s*\.\s*createElement)\s*\(\s*[""'](?<tag>[\w.-]*)[""'](?<rest>[^;]*)"))
            {
                var nm = m.Groups["n"].Value;
                var made = m.Groups["tag"].Value + m.Groups["rest"].Value;
                if (Regex.IsMatch(made, @"aria-live|role\b[""']?\s*[:,]\s*[""'](?:status|alert|log)|sw-status|\bLive\b"))
                    continue;
                if (Regex.IsMatch(text, @"\b" + Regex.Escape(nm) + @"\s*\.\s*setAttribute\s*\(\s*[""'](?:aria-live|role)[""']|\b" + Regex.Escape(nm) + @"\s*\.\s*(?:ariaLive|role)\s*=|Announce\s*\("))
                    continue;
                if (!Regex.IsMatch(text, @"\b" + Regex.Escape(nm) + @"\s*\.\s*(?:textContent|innerText|innerHTML)\s*=|\b" + Regex.Escape(nm) + @"\s*\.\s*replaceChildren\s*\("))
                    continue;
                int tok = t.S.FindIndex(x => x.Start >= m.Groups["n"].Index);
                if (tok >= 0)
                    Once(Rule.UxAcc4, tok, "the status line \"" + nm + "\" is changed by code but is no live region: screen reader users never hear the result (\"5 backups\", \"Restored\"); make it role=\"status\" (Sw.el(\"div\", { attrs: { role: \"status\" } })), or say it with Sw.Page.Announce");
            }

            //  ---- FBK-6: a list loaded through a helper (or into a page member) and rendered by hand without an empty state
            foreach (var fn in flow.Fns)
                foreach (var j in flow.Own(fn))
                {
                    if (!(t.Id(j, "await") && t.Id(j + 1)))
                        continue;
                    //  the called name: await X(...) / await Obj.X(...) / await this.X(...)
                    int c = j + 1;
                    while (t.P(c + 1, ".") && t.Id(c + 2))
                        c += 2;
                    if (!t.P(c + 1, "("))
                        continue;
                    var keys = CallKeys(t, c).ToList();
                    bool loads = IsRequest(t, c) || keys.Any(k => Requester(k) || (Empty(k) != null)) || ((flow.NamedFn(t.Tx(c)) is FnNode nf) && Enumerable.Range(nf.F.Start, Math.Max(0, nf.F.End - nf.F.Start + 1)).Any(q => IsRequest(t, q)));
                    if (!loads)
                        continue;
                    int s0 = StatementStart(t, fn.From, j);
                    String rv = null;
                    if ((t.Id(s0, "const") || t.Id(s0, "let") || t.Id(s0, "var")) && t.Id(s0 + 1) && t.P(s0 + 2, "="))
                        rv = t.Tx(s0 + 1);
                    else if (t.Id(s0, "this") && t.P(s0 + 1, ".") && t.Id(s0 + 2) && t.P(s0 + 3, "="))
                        rv = "this." + t.Tx(s0 + 2);
                    else if (t.Id(s0) && t.P(s0 + 1, "=") && !t.P(s0 + 2, "="))
                        rv = t.Tx(s0);
                    if (rv == null)
                        continue;
                    var after = t.Span(j, fn.F.End);
                    var re = Regex.Escape(rv).Replace(@"\.", @"\s*\.\s*");
                    bool renders = Regex.IsMatch(after, @"\bfor\s*\(\s*(?:const|let|var)\s+\w+\s+of\s+" + re + @"\b|" + re + @"\s*\.\s*(?:forEach|map)\s*\(") && Regex.IsMatch(after, @"\.\s*(?:append|appendChild|replaceChildren|prepend)\s*\(|innerHTML\s*[+]?=");
                    bool empty = Regex.IsMatch(after, @"!\s*" + re + @"\s*(?:\?\.|\.)\s*length\b|" + re + @"\s*(?:\?\.|\.)\s*length\s*(?:===?\s*0|<\s*1|<=\s*0)|!\s*" + re + @"\b|\bEmpty\b|sw-empty|StateView|No\s+\w+\s+yet|\bNothing\b", RegexOptions.IgnoreCase);
                    if (renders && !empty && !Regex.IsMatch(after, @"\bSetRows\b|\bSw\s*\.\s*Table\b"))
                        Once(Rule.UxFbk6, j, "the list \"" + rv + "\" is loaded (" + Short(t.Span(j + 1, c)) + ") and rendered by hand without an empty state: with no rows the area is blank (it looks broken or still loading), and a failed load looks the same; show what would be here when it is empty (Sw.StateView / a \"No ... yet\" line) and a failure with Try again (or use Sw.Table, which has all three)");
                }

            //  ---- helpers that reach a request (any file): a local function / method whose body (2 calls deep) requests
            bool Reaches(int callTok)
            {
                foreach (var k in CallKeys(t, callTok))
                    if (Requester(k))
                        return true;
                if (IsRequest(t, callTok))
                    return true;
                if ((flow.NamedFn(t.Tx(callTok)) is FnNode g) && (!t.P(callTok - 1, ".") || t.Id(callTok - 2, "this")))
                {
                    var d = DeepSpan(t, flow, g);
                    if (Regex.IsMatch(d, @"\bNet\s*\.\s*(?:Send|Get|Read|Live)\s*\(|\bfetch\s*\("))
                        return true;
                    for (int q = g.F.Start; q <= g.F.End; ++q)
                        if (t.Id(q) && t.P(q + 1, "(") && CallKeys(t, q).Any(Requester))
                            return true;
                }
                return false;
            }
            for (int i = 0; i < t.N; ++i)
            {
                //  ---- FBK-5: x().catch(() => {}) on a call that reaches a request through a helper / method (any file)
                if (t.Id(i, "catch") && t.P(i - 1, ".") && t.P(i - 2, ")") && t.P(i + 1, "(") && (t.Match(i + 1) is int cc) && (cc > i)
                    && Regex.IsMatch(t.Span(i + 2, cc - 1), @"^\s*(?:async\s*)?(?:\(\s*\w*\s*\)|\w+)\s*=>\s*\{\s*\}\s*$") && (InReach(flow, i) || regs.Any(g => (g.Fn != null) && (g.Fn.F.Start <= i) && (i <= g.Fn.F.End))
                        || ((flow.Around(i) is FnNode lf0) && Regex.IsMatch(t.Span(StatementStart(t, lf0.Parent?.From ?? 0, lf0.Head), lf0.Head), @"addEventListener\s*\(|\bOn[A-Z]\w*\s*:"))))
                {
                    int open = -1;
                    for (int k = i - 3, d = 0; k >= 0; --k)
                        if (t.P(k, ")"))
                            ++d;
                        else if (t.P(k, "(") && (d-- == 0))
                        {
                            open = k;
                            break;
                        }
                    if ((open > 0) && t.Id(open - 1) && Reaches(open - 1))
                        Once(Rule.UxFbk5, i, ".catch(" + Short(t.Span(i + 2, cc - 1)) + ") swallows the failure of " + Short(t.Span(ChainStartOf(t, open - 1), open - 1)) + "(...), which sends a request: the user never learns that it failed (an old list stays as if it were current); show it (Sw.Fail(e), the list's error state with Try again)");
                }
                //  ---- FBK-2: an OnClick block body that calls a request helper of any file and returns nothing
                if (t.Id(i, "OnClick") && t.Key(i) && (flow.At(i + 2) is FnNode oc) && oc.F.Block && !flow.Own(oc).Any(k => t.Id(k, "return")))
                    foreach (var k in flow.Own(oc))
                        if (t.Id(k) && t.P(k + 1, "(") && t.P(k - 1, ".") && !t.Id(k - 2, "this") && CallKeys(t, k).Any(Requester) && !t.Id(ChainStartOf(t, k) - 1, "await"))
                        {
                            AddNoPromise(ctx, t.S[i].Start, "OnClick calls " + Short(t.Span(ChainStartOf(t, k), k)) + "(...), which sends a request, and returns no promise: the button is never busy, a second click sends again and a failure reads as done; return it (OnClick: () => " + Short(t.Span(ChainStartOf(t, k), k)) + "(...))");
                            break;
                        }
                //  ---- SAF-1: a footer button / OnClick that calls a destructive helper (remove, delete ... of any file) with no confirmation
                if (t.Id(i, "OnClick") && t.Key(i) && (flow.At(i + 2) is FnNode dc))
                {
                    var deep = DeepSpan(t, flow, dc);
                    if (!Regex.IsMatch(deep, @"\bConfirm\w*\s*\(|\bconfirm\s*\(|\bDanger\s*\.\s*Run\b|\bUndo\b|\bAction\s*:"))
                        for (int k = dc.F.Start; k <= dc.F.End; ++k)
                            if (t.Id(k) && t.P(k + 1, "(") && t.P(k - 1, ".") && Regex.IsMatch(t.Tx(k), @"^(?i:delete|remove|purge|erase|wipe|destroy|drop|kill|revoke)\w*$") && CallKeys(t, k).Any(Requester))
                            {
                                Once(Rule.UxSaf1, k, Short(t.Span(ChainStartOf(t, k), k)) + "(...) deletes at once from a click: no confirmation and no Undo (a slip loses the data); ask with Sw.Cells.ConfirmDanger({ Title, Item, OkText }) / Sw.Danger.Run, or remove with an Undo toast");
                                break;
                            }
                }
            }

            //  ---- FBK-6 (through a render function): this.list = await load(); this.render(this.list) -> render(list) { for (const x of list) append }
            foreach (var fn in flow.Fns)
                foreach (var j in flow.Own(fn))
                {
                    if (!(t.Id(j, "await") && t.Id(j + 1)))
                        continue;
                    int c = j + 1;
                    while (t.P(c + 1, ".") && t.Id(c + 2))
                        c += 2;
                    if (!t.P(c + 1, "(") || !Reaches(c))
                        continue;
                    int s0 = StatementStart(t, fn.From, j);
                    String rv = (t.Id(s0, "const") || t.Id(s0, "let") || t.Id(s0, "var")) && t.Id(s0 + 1) && t.P(s0 + 2, "=") ? t.Tx(s0 + 1)
                        : t.Id(s0, "this") && t.P(s0 + 1, ".") && t.Id(s0 + 2) && t.P(s0 + 3, "=") ? "this." + t.Tx(s0 + 2)
                        : t.Id(s0) && t.P(s0 + 1, "=") && !t.P(s0 + 2, "=") ? t.Tx(s0) : null;
                    if (rv == null)
                        continue;
                    var re = Regex.Escape(rv).Replace(@"\.", @"\s*\.\s*");
                    for (int k = j; k <= fn.F.End; ++k)
                    {
                        if (!(t.Id(k) && t.P(k + 1, "(") && (!t.P(k - 1, ".") || t.Id(k - 2, "this")) && (flow.NamedFn(t.Tx(k)) is FnNode rf) && (rf != fn) && (t.Args(k + 1) is var ra)))
                            continue;
                        int ai = ra.FindIndex(a => Regex.IsMatch(t.Span(a.A, a.B), "^" + re + "$"));
                        if (ai < 0)
                            continue;
                        var ps = ParamNames(t, rf);
                        if ((ai >= ps.Count) || (ps[ai] == null))
                            continue;
                        var pre = @"\b" + Regex.Escape(ps[ai]) + @"\b";
                        var body = t.Span(rf.F.Start, rf.F.End);
                        bool renders = Regex.IsMatch(body, @"\bfor\s*\(\s*(?:const|let|var)\s+\w+\s+of\s+" + pre + @"|" + pre + @"\s*\.\s*(?:forEach|map)\s*\(") && Regex.IsMatch(body, @"\.\s*(?:append|appendChild|replaceChildren|prepend)\s*\(|innerHTML\s*[+]?=");
                        bool empty = Regex.IsMatch(body + " " + t.Span(j, fn.F.End), @"!\s*" + pre + @"\s*(?:\?\.|\.)\s*length\b|" + pre + @"\s*(?:\?\.|\.)\s*length\s*(?:===?\s*0|<\s*1|<=\s*0)|!\s*" + re + @"\s*(?:\?\.|\.)\s*length\b|" + re + @"\s*(?:\?\.|\.)\s*length\s*(?:===?\s*0|<\s*1)|\bEmpty\b|sw-empty|StateView|No\s+\w+\s+yet|\bNothing\b", RegexOptions.IgnoreCase);
                        if (renders && !empty && !Regex.IsMatch(body, @"\bSetRows\b|\bSw\s*\.\s*Table\b"))
                            Once(Rule.UxFbk6, k, "the list \"" + rv + "\" is loaded and rendered by hand (" + t.Tx(k) + ") without an empty state: with no rows the area is blank (it looks broken or still loading); show what would be here when it is empty (Sw.StateView / a \"No ... yet\" line) and a failure with Try again (or use Sw.Table)");
                    }
                }

            //  ---- design: an icon name that is not in the icon set (a dashed placeholder shows)
            var icons = ctx.Options.UxSizes?.Helpers?.Icons;
            for (int i = 0; i < t.N; ++i)
            {
                String name = null;
                int at = -1;
                if (t.Id(i, "Icon") && t.Key(i) && (t.Str(i + 2) is String iv) && (t.P(i + 3, ",") || t.P(i + 3, "}")) && (EnclosingObject(t, i) is int io) && (io > 0) && IsSwOptions(t, io))
                {
                    name = iv;
                    at = i + 2;
                }
                else if (t.Chain(i, "Sw", "Icon") && t.P(i + 1, "(") && (t.Str(i + 2) is String iv2))
                {
                    name = iv2;
                    at = i + 2;
                }
                if ((name == null) || !Regex.IsMatch(name, @"^[a-z][A-Za-z0-9]*$") || IconNames.Contains(name) || ((icons != null) && icons.Contains(name)) || Regex.IsMatch(text, @"Icons\s*\.\s*Add(?:Line)?\s*\(\s*[""']" + Regex.Escape(name) + @"[""']"))
                    continue;
                var near = IconNames.OrderBy(x => Levenshtein(x, name)).FirstOrDefault();
                ctx.Add(Rule.DesignIcon, t.S[at].Start, "the icon \"" + name + "\" is not in the icon set: a dashed placeholder shows (in a primary action too)" + (near != null && Levenshtein(near, name) <= Math.Max(2, name.Length / 2) ? "; did you mean \"" + near + "\"?" : "") + " (the names: Sw.Icons.List(), ui/README \"Icons\"; an app's own icon: Sw.Icons.Add(name, svg))");
            }
        }

        /// <summary>True if a function makes one item of a list: named like a row / item, or called in a loop / forEach / map.</summary>
        static bool PerItem(Toks t, Flow flow, FnNode fn)
        {
            for (var n = fn; n != null; n = n.Parent)
            {
                if ((n.Name != null) && Regex.IsMatch(n.Name, @"(?i)row|item|card|cell|entry|line|tile|render\w+|make\w+"))
                    return true;
                if (t.P(n.Head - 1, "(") && t.Id(n.Head - 2) && Regex.IsMatch(t.Tx(n.Head - 2), "^(?:forEach|map|flatMap)$"))
                    return true;
                if (n.Name != null && Regex.IsMatch(t.T, @"\bfor\s*\([^)]*\)\s*[^;{]*\b" + Regex.Escape(n.Name) + @"\s*\(|\.(?:forEach|map)\s*\(\s*[^)]*\b" + Regex.Escape(n.Name) + @"\b"))
                    return true;
            }
            return false;
        }

        static int Levenshtein(String a, String b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; ++i)
                d[i, 0] = i;
            for (int j = 0; j <= b.Length; ++j)
                d[0, j] = j;
            for (int i = 1; i <= a.Length; ++i)
                for (int j = 1; j <= b.Length; ++j)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (Char.ToLowerInvariant(a[i - 1]) == Char.ToLowerInvariant(b[j - 1]) ? 0 : 1));
            return d[a.Length, b.Length];
        }

        /// <summary>The inline icon names of the framework (embedded sw_icons.txt, generated from ui/base.js and ui/page.js; a sw_icons.txt next to the tool overrides it).</summary>
        public static readonly HashSet<String> IconNames = LoadIconNames();

        /// <summary>The file name of the icon list.</summary>
        public const String IconListFileName = "sw_icons.txt";

        static HashSet<String> LoadIconNames()
        {
            var r = new HashSet<String>(StringComparer.Ordinal);
            String text = null;
            try
            {
                var side = System.IO.Path.Combine(AppContext.BaseDirectory, IconListFileName);
                if (System.IO.File.Exists(side))
                    text = System.IO.File.ReadAllText(side);
                else
                    using (var s = typeof(UxChecker).Assembly.GetManifestResourceStream("SwWeb." + IconListFileName))
                        if (s != null)
                            using (var rd = new System.IO.StreamReader(s))
                                text = rd.ReadToEnd();
            }
            catch
            {
            }
            foreach (var l in (text ?? "").Split('\n'))
            {
                var x = l.Trim();
                if ((x.Length > 0) && !x.StartsWith("#", StringComparison.Ordinal))
                    r.Add(x);
            }
            return r;
        }

        /// <summary>
        /// The inline icon names a web root defines (the keys of the icon tables in ui/base.js and ui/page.js, and Sw.Icons.Add names):
        /// "SwWeb icons" writes them to sw_icons.txt.
        /// </summary>
        public static SortedSet<String> CollectIconNames(String webRoot)
        {
            var r = new SortedSet<String>(StringComparer.Ordinal);
            foreach (var f in new[] { "ui/base.js", "ui/page.js" })
            {
                var p = System.IO.Path.Combine(webRoot, f.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(p))
                    continue;
                var text = System.IO.File.ReadAllText(p);
                foreach (Match tm in Regex.Matches(text, @"const\s+(?:IconPaths|LineIcons|ExtraIcons)\s*=\s*(?:Object\.freeze\()?\{(?<b>.*?)\n\s*\}", RegexOptions.Singleline))
                    foreach (Match km in Regex.Matches(tm.Groups["b"].Value, @"^\s*[""']?([A-Za-z][A-Za-z0-9]*)[""']?\s*:", RegexOptions.Multiline))
                        r.Add(km.Groups[1].Value);
                foreach (Match am in Regex.Matches(text, @"Icons\s*\.\s*Add(?:Line)?\s*\(\s*[""']([A-Za-z][A-Za-z0-9]*)[""']"))
                    r.Add(am.Groups[1].Value);
            }
            return r;
        }
    }
}
