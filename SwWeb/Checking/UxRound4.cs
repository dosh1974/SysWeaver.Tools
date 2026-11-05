using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ rule families of series 4 round 4
        //
        //  Generalized by family, measured with generated variants (SwWeb.Tests/UxVariantTests.cs) and the critics' unseen sets:
        //  - the work of a user action is every function a handler reaches (a helper, a page method, an object method, a then callback):
        //    a swallowed failure or an untranslated thrown text there is the user's;
        //  - a secret is also the value of a field whose label, placeholder or type says secret (pass.Get(), pw.value) and every variable
        //    made from it; storage and query objects are followed through aliases;
        //  - a failure is silent when nothing says it: a catch that neither shows, reports, rethrows nor hands the error on; a result that
        //    turns a failure into null / false and is used (or dropped) without showing it; Promise.allSettled results never read;
        //  - the families planted for the first time (vague / weak confirmations, the focus on a danger button, a change that navigates,
        //    smooth scrolling, a click that copies, focusable rows without keys, lists without an empty state, objects shown as text,
        //    activity misuse, drafts cleared before the send, small targets, fixed page widths, faded but present content).

        /// <summary>The secret values of a script: fields whose label / placeholder / type says secret, variables made from them, storage and query aliases.</summary>
        sealed class SecretScope
        {
            /// <summary>Fields (Sw value controls, inputs) whose value is a secret: name -> where it is made.</summary>
            public readonly Dictionary<String, int> Controls = new Dictionary<String, int>(StringComparer.Ordinal);
            /// <summary>Variables whose value is a secret (made from a secret field's value): name -> the functions that declare it (null: the top level).</summary>
            public readonly Dictionary<String, List<FnNode>> Vars = new Dictionary<String, List<FnNode>>(StringComparer.Ordinal);
            public Flow Flow;

            /// <summary>True if the name at token k is a secret variable declared in a function around it (or at the top level).</summary>
            public bool IsVar(String name, int k)
            {
                if (!Vars.TryGetValue(name, out var fs))
                    return false;
                var around = new HashSet<FnNode>();
                for (var n = Flow?.Around(k); n != null; n = n.Parent)
                    around.Add(n);
                return fs.Any(f => (f == null) || around.Contains(f));
            }
            /// <summary>Names bound to localStorage / sessionStorage.</summary>
            public readonly HashSet<String> Storage = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>Names bound to url query parameters (new URLSearchParams, x.searchParams).</summary>
            public readonly HashSet<String> Query = new HashSet<String>(StringComparer.Ordinal);
            /// <summary>Secret fields shown in clear text: (token, name, label).</summary>
            public readonly List<(int At, String Name, String Label)> Unmasked = new List<(int, String, String)>();
        }

        [ThreadStatic]
        static SecretScope Secrets;

        /// <summary>The functions a user action runs (handlers and every local function / method they call): set per file by CheckFamilies.</summary>
        [ThreadStatic]
        static HashSet<FnNode> Reach;

        static readonly Regex SecretFieldLabel = new Regex(@"(?i)passw|kennwort|l[oö]senord|passord|salasana|wachtwoord|mot de passe|contrase[nñ]a|senha|has[lł]o|parola|secret|api.?key|access.?key|private.?key|\btoken\b|\bpin\b|passphrase|credential|пароль|密码|パスワード", RegexOptions.CultureInvariant);
        static readonly Regex NotSecretLabel = new Regex(@"(?i)hint|polic|\brules?\b|strength|expir|\bname\b|\blength\b|\bcount\b|reset link|forgot|\blabel\b|\bid\b|descri|\bnote\b|\btype\b|\bscope\b|\bowner\b|\bcreated\b|\bused\b|\bage\b|\bprefix\b", RegexOptions.CultureInvariant);

        /// <summary>The text of the options that name a field (Label, Placeholder, AriaLabel, Name, Title, aria-label, placeholder, name, id).</summary>
        static String FieldNaming(Toks t, int open)
        {
            var parts = new List<String>();
            foreach (var k in new[] { "Label", "Placeholder", "AriaLabel", "Name", "Title", "placeholder", "name", "id", "title" })
                if ((ObjKey(t, open, k) is var v) && (v != null) && (ValueText(t, v.Value) is String s))
                    parts.Add(s);
            //  Sw.el("input", { attrs: { "aria-label": ..., placeholder: ... } })
            if ((ObjKey(t, open, "attrs") is var at) && (at != null) && t.P(at.Value.A, "{"))
                for (int k = at.Value.A + 1; k < at.Value.B; ++k)
                {
                    var key = t.Str(k) ?? (t.Id(k) ? t.Tx(k) : null);
                    if ((key != null) && t.P(k + 1, ":") && Regex.IsMatch(key, @"^(?:aria-label|placeholder|name|id|title)$") && (ValueText(t, (k + 2, ExprEnd(t, k + 2))) is String s2))
                        parts.Add(s2);
                }
            return String.Join(" | ", parts);
        }

        static String TypeOption(Toks t, int open)
        {
            if ((ObjKey(t, open, "Type") ?? ObjKey(t, open, "type")) is var ty && (ty != null))
                return t.Lit(ty.Value);
            if ((ObjKey(t, open, "attrs") is var at) && (at != null) && t.P(at.Value.A, "{") && (ObjKey(t, at.Value.A, "type") is var ty2) && (ty2 != null))
                return t.Lit(ty2.Value);
            return null;
        }

        /// <summary>Find the secret fields, the variables made from them and the storage / query aliases of a script.</summary>
        static SecretScope FindSecrets(Toks t, Dictionary<String, (String Kind, int At, int Call)> vars, Flow flow)
        {
            var s = new SecretScope { Flow = flow };
            var text = t.T;
            for (int i = 1; i + 3 < t.N; ++i)
            {
                //  NAME = [new] Sw.TextBox({ ... }) / this.NAME = ... / NAME = Sw.el("input", { ... }) / document.createElement("input")
                //  (round 5: also a field made in an object literal: { pass: new Sw.TextBox({ ... }) })
                if (!(t.Id(i) && ((t.P(i + 1, "=") && !t.P(i + 2, "=")) || t.Key(i))))
                    continue;
                var name = t.Tx(i);
                int v = i + 2;
                if (t.Id(v, "new"))
                    ++v;
                int call = -1, open = -1;
                bool field = false, input = false;
                if (t.SwComponent(v + (t.Id(v, "Sw") ? 2 : 0)) && t.P(v + (t.Id(v, "Sw") ? 3 : 1), "("))
                {
                    int ni = v + (t.Id(v, "Sw") ? 2 : 0);
                    var comp = t.SwNames.TryGetValue(t.Tx(ni), out var m) ? m : t.Tx(ni);
                    field = (comp == "TextBox") || (comp == "Field") || (comp == "PasswordBox");
                    call = ni + 1;
                    open = t.P(call + 1, "{") ? call + 1 : -1;
                }
                else if (t.Chain(v + 2, "Sw", "el") && t.P(v + 3, "(") && (t.Str(v + 4) is String spec) && spec.StartsWith("input", StringComparison.OrdinalIgnoreCase))
                {
                    input = true;
                    call = v + 3;
                    var a = t.Args(call);
                    open = (a.Count > 1) && t.P(a[1].A, "{") ? a[1].A : -1;
                }
                else if (t.Id(v, "document") && t.Id(v + 2, "createElement") && (t.Str(v + 4) is String tag) && tag.Equals("input", StringComparison.OrdinalIgnoreCase))
                {
                    input = true;
                    call = v + 3;
                }
                else if (Regex.IsMatch(t.Span(v, Math.Min(t.N - 1, v + 8)), @"(?:querySelector|getElementById)\s*\(\s*[""'`][^""'`]*(?i:passw|secret|token|api.?key|pin\b)"))
                {
                    s.Controls[name] = i;
                    continue;
                }
                //  storage and query aliases
                if (Regex.IsMatch(t.Span(v, Math.Min(t.N - 1, v + 3)), @"^(?:window\s*\.\s*)?(?:localStorage|sessionStorage)\b") && (t.P(v + 1, ";") || t.P(v + 3, ";") || !t.P(v + 1, ".") || t.Id(v, "window")))
                {
                    if (!Regex.IsMatch(t.Span(v, Math.Min(t.N - 1, v + 4)), @"(?:localStorage|sessionStorage)\s*\.\s*\w"))
                        s.Storage.Add(name);
                }
                if ((t.Id(v - 1, "new") && t.Id(v, "URLSearchParams")) || Regex.IsMatch(t.Span(v, ExprEnd(t, v)), @"\.searchParams$"))
                    s.Query.Add(name);
                if (!field && !input)
                    continue;
                var naming = open > 0 ? FieldNaming(t, open) : "";
                var type = open > 0 ? TypeOption(t, open) : null;
                if (input)
                {
                    //  Set later: name.type = "password", name.setAttribute("aria-label", ...), name.placeholder = ...
                    foreach (Match lm in Regex.Matches(text, @"\b" + Regex.Escape(name) + @"\s*\.\s*(?:(?<p>type|placeholder|title|name|id|ariaLabel)\s*=\s*(?<v>[^;\r\n]+)|setAttribute\(\s*[""'](?<p>type|aria-label|placeholder|title|name)[""']\s*,\s*(?<v>[^)]+)\))"))
                    {
                        var val = Regex.Match(lm.Groups["v"].Value, @"[""'`]([^""'`]*)[""'`]").Groups[1].Value;
                        if (lm.Groups["p"].Value == "type")
                            type = val;
                        else
                            naming += " | " + val;
                    }
                }
                bool readOnly = (open > 0) && Regex.IsMatch(t.Span(open, Math.Max(open, t.Match(open))), @"\bReadOnly\s*:\s*true|\breadonly\b|\bdisabled\s*:\s*true");
                bool secretLabel = SecretFieldLabel.IsMatch(naming) && !NotSecretLabel.IsMatch(naming);
                bool password = String.Equals(type, "password", StringComparison.OrdinalIgnoreCase);
                if (secretLabel || password)
                    s.Controls[name] = i;
                if (secretLabel && !password && !readOnly && !(field && (open > 0) && Regex.IsMatch(t.Span(open, Math.Max(open, t.Match(open))), @"\bMultiline\s*:\s*true")))
                    s.Unmasked.Add((i, name, SecretFieldLabel.Match(naming).Value));
            }
            //  Variables made from a secret value: const p = pass.Get(); const k = pw.value.trim()
            for (int pass = 0; pass < 2; ++pass)
                for (int i = 1; i + 3 < t.N; ++i)
                    if (t.Id(i) && t.P(i + 1, "=") && !t.P(i + 2, "=") && !t.P(i - 1, ".") && (t.Id(i - 1, "const") || t.Id(i - 1, "let") || t.Id(i - 1, "var") || t.P(i - 1, ";") || t.P(i - 1, "{") || t.P(i - 1, "}")))
                    {
                        int e = ExprEnd(t, i + 2);
                        for (int k = i + 2; k <= e; ++k)
                            if (t.Id(k) && (!t.P(k - 1, ".") || t.Id(k - 2, "this")) && ((s.Controls.ContainsKey(t.Tx(k)) && t.P(k + 1, ".") && Regex.IsMatch(t.Tx(k + 2), "^(?:Get|value|Value|Input)$")) || s.IsVar(t.Tx(k), k)))
                            {
                                { if (!s.Vars.TryGetValue(t.Tx(i), out var vl)) s.Vars[t.Tx(i)] = vl = new List<FnNode>(); vl.Add(flow.Around(i)); }
                                break;
                            }
                    }
            return s;
        }

        /// <summary>The secret value of a field or a variable made from it in a token range (pass.Get(), pw.value, p), null if none.</summary>
        static String SecretValueIn(Toks t, int a, int b)
        {
            var s = Secrets;
            if (s == null)
                return null;
            for (int k = a; k <= b; ++k)
            {
                //  (a secret field read through a member counts too: this.Pass.Get(), f.pass.Get() of a field handed on in an object)
                if (!t.Id(k))
                    continue;
                var x = t.Tx(k);
                if (s.Controls.ContainsKey(x) && t.P(k + 1, ".") && Regex.IsMatch(t.Tx(k + 2), "^(?:Get|value|Value|Input)$"))
                {
                    //  (its length is metadata, not the secret: pass.Get().length)
                    int end = t.P(k + 3, "(") ? t.Match(k + 3) : k + 2;
                    if ((end > 0) && t.P(end + 1, ".") && Regex.IsMatch(t.Tx(end + 2), "^(?:length|Length)$"))
                        continue;
                    return x + "." + t.Tx(k + 2) + (t.P(k + 3, "(") ? "()" : "");
                }
                if (t.P(k - 1, ".") && !t.Id(k - 2, "this"))
                    continue;
                if (s.IsVar(x, k) && !t.P(k + 1, "("))
                    return x;
            }
            return null;
        }

        /// <summary>
        /// The functions a user action runs: the handlers (Sw options On*, footer buttons, listeners of user events, Sw.OnClick, Sw.Keys.Add)
        /// and every local function or method they call, followed to a fixed point.
        /// </summary>
        static HashSet<FnNode> Reachable(Toks t, Flow flow, List<Reg> regs)
        {
            var r = new HashSet<FnNode>();
            var work = new Stack<FnNode>();
            void Root(FnNode n)
            {
                if ((n != null) && r.Add(n))
                    work.Push(n);
            }
            for (int i = 0; i < t.N; ++i)
            {
                if (t.Id(i) && Regex.IsMatch(t.Tx(i), @"^On[A-Z]\w*$") && t.Key(i))
                    Root(flow.At(i + 2));
                if ((t.Chain(i, "Sw", "OnClick") || t.Chain(i, "Keys", "Add") || t.Chain(i, "Sw", "LongPress")) && t.P(i + 1, "(") && (t.Args(i + 1) is var a) && (a.Count > 1))
                    Root(flow.At(a[1].A));
            }
            foreach (var g in regs)
                if (UserEvents.Contains(g.Evt))
                    Root(g.Fn);
            while (work.Count > 0)
            {
                var n = work.Pop();
                foreach (var c in n.Children)
                    Root(c);
                for (int j = n.F.Start; j <= n.F.End; ++j)
                {
                    if (!t.Id(j) || !t.P(j + 1, "(") || Keywords.Contains(t.Tx(j)))
                        continue;
                    var name = t.Tx(j);
                    var g = flow.NamedFn(name);
                    if (g == null)
                        continue;
                    if (!t.P(j - 1, ".") || ((t.Id(j - 2, "this") || (t.Id(j - 2) && !t.Id(j - 2, "Sw"))) && (name.Length > 2) && g.IsMethod))
                        Root(g);
                }
                //  A handler given by name: OnClick: save / addEventListener("click", save)
            }
            return r;
        }

        /// <summary>True if the token is in the work of a user action (see <see cref="Reachable"/>).</summary>
        static bool InReach(Flow flow, int at)
        {
            if (Reach == null)
                return false;
            for (var n = flow.Around(at); n != null; n = n.Parent)
                if (Reach.Contains(n))
                    return true;
            return false;
        }

        static readonly Regex SaysText = new Regex(@"\bSw\s*\.\s*(?:Fail|Toast|Info|Warn|Message|Alert)\s*\(|\bDialog\s*\.\s*Alert\s*\(|\.(?:SetError|SetStatus|ShowError|ShowFailure|Fail|Report|Commit|Reject)\s*\(|\bAnnounce\s*\(|\bFailText\s*\(|\bErrorText\s*\(|\bthrow\b|\breject\s*\(", RegexOptions.CultureInvariant);

        /// <summary>
        /// True if a catch body says nothing: it neither shows the failure (Sw.Fail, a toast, a field / status error, a status text that
        /// is not empty), reports, rethrows, nor hands the error to a function (showError(e)); console calls and UI resets don't count.
        /// </summary>
        static bool SaysNothing(Toks t, int b, int be, String errVar)
        {
            var body = t.Span(b + 1, be - 1);
            if (SaysText.IsMatch(body))
                return false;
            for (int k = b + 1; k < be; ++k)
            {
                //  The error handed on: f(e), x.f(e), this.Fail(e)
                if ((errVar != null) && t.Id(k, errVar) && !t.P(k - 1, ".") && (t.P(k - 1, "(") || t.P(k - 1, ",")) && !InConsoleCall(t, k, b))
                    return false;
                //  The error kept for later: error = e / this.Error = e (shown after the finally)
                if ((errVar != null) && t.Id(k, errVar) && (t.P(k - 1, "=") || t.P(k - 1, ":")) && !t.P(k - 2, "=") && !t.P(k + 1, "."))
                    return false;
                //  A result flag the code reads after the try: ok = false (a probe, "is it reachable")
                if (t.Id(k) && !t.P(k - 1, ".") && t.P(k + 1, "=") && !t.P(k + 2, "=") && (t.Id(k + 2, "false") || t.Id(k + 2, "true")) && (t.P(k - 1, "{") || t.P(k - 1, ";")))
                    return false;
                //  A text set: x.textContent = <not empty> / Sw.el(..., { text: ... })
                if ((t.Id(k, "textContent") || t.Id(k, "innerText") || t.Id(k, "innerHTML")) && t.P(k + 1, "=") && !(t.Str(k + 2) == "") && !t.Id(k + 2, "null"))
                    return false;
                //  A call of a local function that shows (a helper named like it): showError(), fail(), notify()
                if (t.Id(k) && t.P(k + 1, "(") && !(t.P(k - 1, ".") && t.Id(k - 2, "console")) && Regex.IsMatch(t.Tx(k), @"(?i)^(?:show|notify|report|fail|alert|warn|toast|announce)"))
                    return false;
            }
            return true;
        }

        /// <summary>A Show toggle: show ? secret : "•".repeat(n) (the secret is masked by default and shown on request).</summary>
        static bool MaskedToggle(Toks t, int a, int b) => Regex.IsMatch(t.Span(a, b), @"\?[\s\S]*:") && Regex.IsMatch(t.Span(a, b), @"•|\*{2,}|\.repeat\(|mask", RegexOptions.IgnoreCase);

        /// <summary>True if a variable declared at decl is in scope at the token use (declared in a function around it, or at the top level).</summary>
        static bool InScope(Flow flow, int decl, int use)
        {
            var d = flow.Around(decl);
            if (d == null)
                return true;
            for (var n = flow.Around(use); n != null; n = n.Parent)
                if (n == d)
                    return true;
            return false;
        }

        /// <summary>True if every request of a function is a read (Sw.Net.Get / Cached, a fetch without a write method, a Get* / List* ... api).</summary>
        static bool OnlyReads(Toks t, FnNode g)
        {
            var any = false;
            for (int k = g.F.Start; k <= g.F.End; ++k)
            {
                if (!IsRequest(t, k))
                    continue;
                any = true;
                if (t.Id(k, "Get") || t.Id(k, "Cached"))
                    continue;
                var a = t.Args(k + 1);
                if (t.Id(k, "fetch") && !Regex.IsMatch(t.Span(k, Math.Max(k, t.Match(k + 1))), @"(?i)method\s*:\s*[""'](?:POST|PUT|DELETE|PATCH)"))
                    continue;
                var u = a.Count > 0 ? Literals(t, a[0]).Trim().Split(' ').LastOrDefault() ?? "" : "";
                if ((u.Length > 0) && ReadUrl.IsMatch(u))
                    continue;
                return false;
            }
            return any;
        }

        /// <summary>True if the token is an argument of a console call (console.warn("x", e)): a log, not a message.</summary>
        static bool InConsoleCall(Toks t, int k, int from)
        {
            for (int q = k - 1, d = 0; q > from; --q)
            {
                if (t.P(q, ")") || t.P(q, "]") || t.P(q, "}"))
                    ++d;
                else if (t.P(q, "(") || t.P(q, "[") || t.P(q, "{"))
                {
                    if (d-- > 0)
                        continue;
                    return t.P(q, "(") && t.Id(q - 1) && t.P(q - 2, ".") && t.Id(q - 3, "console");
                }
            }
            return false;
        }

        /// <summary>The value a catch callback returns when it swallows: () => null / undefined / false / [] / {} / "" / { } (null if it does something).</summary>
        static readonly Regex SwallowCallback = new Regex(@"^\s*(?:(?:async\s*)?(?:\(\s*\w*\s*\)|\w+)\s*=>\s*(?:\{\s*\}|\{\s*return(?:\s+(?:null|undefined|false|\[\s*\]|\{\s*\}|""""|''|0))?\s*;?\s*\}|null|undefined|false|void 0|\[\s*\]|\(\s*\{\s*\}\s*\)|""""|''|0|\{\s*console\.\w+\([^;]*\)\s*;?\s*\}|console\.\w+\([^)]*\))|console\.\w+|noop|function\s*\(\w*\)\s*\{\s*(?:return(?:\s+(?:null|undefined|false))?\s*;?)?\s*\})\s*$", RegexOptions.CultureInvariant);

        /// <summary>The functions whose failure path gives "nothing" (null / false / undefined): a returned request chain with a swallowing .catch, or a try whose catch returns nothing.</summary>
        static Dictionary<String, int> NothingOnFailure(Toks t, Flow flow)
        {
            var r = new Dictionary<String, int>(StringComparer.Ordinal);
            foreach (var g in flow.Fns)
            {
                if (g.Name == null)
                    continue;
                foreach (var j in flow.Own(g))
                {
                    //  return <request chain>.catch(() => null)
                    if (t.Id(j, "catch") && t.P(j - 1, ".") && t.P(j + 1, "(") && (t.Match(j + 1) is int cc) && (cc > j) && SwallowCallback.IsMatch(t.Span(j + 2, cc - 1)))
                    {
                        int s0 = StatementStart(t, g.From, j);
                        bool returned = t.Id(s0, "return") || !g.F.Block;
                        if (returned && Enumerable.Range(s0, j - s0).Any(q => IsRequest(t, q)))
                            r[g.Name] = j;
                    }
                    //  try { return await <request> } catch { return null; }
                    if (t.Id(j, "catch") && !t.P(j - 1, ".") && t.P(j - 1, "}"))
                    {
                        int b = t.P(j + 1, "(") ? t.Match(j + 1) + 1 : j + 1;
                        if (t.P(b, "{") && (t.Match(b) is int be) && (be > b) && LogThenNothing.IsMatch(t.Span(b, be)))
                        {
                            int to = -1;
                            for (int q = j - 2, d = 0; q >= g.From; --q)
                            {
                                if (t.P(q, "}"))
                                    ++d;
                                else if (t.P(q, "{") && (d-- == 0))
                                {
                                    to = q;
                                    break;
                                }
                            }
                            if ((to > 0) && t.Id(to - 1, "try") && Regex.IsMatch(t.Span(to, j - 1), @"\breturn\b") && Enumerable.Range(to, j - to).Any(q => IsRequest(t, q)))
                                r[g.Name] = j;
                        }
                    }
                }
            }
            return r;
        }

        /// <summary>How the value of a variable is used after its statement: (unguarded member use, guarded, the guard says something).</summary>
        static (bool Unguarded, bool Guarded, bool GuardSays) UseOf(Toks t, FnNode fn, String v, int after)
        {
            bool unguarded = false, guarded = false, says = false;
            int to = fn?.F.End ?? t.N - 1;
            for (int k = after; k <= to; ++k)
            {
                if (!t.Id(k, v) || t.P(k - 1, "."))
                    continue;
                //  if (!v) ... / v ? / v && / v || / v == null / v?.x / typeof v
                bool guard = t.P(k - 1, "!") || t.P(k + 1, "?") || t.P(k + 1, "&&") || t.P(k + 1, "||") || t.P(k + 1, "??") || t.P(k + 1, "?.") || t.P(k + 1, "==") || t.P(k + 1, "===") || t.P(k + 1, "!=") || t.P(k + 1, "!==") || t.Id(k - 1, "typeof") || (t.P(k - 1, "(") && t.Id(k - 2, "if") && t.P(k + 1, ")"));
                if (guard)
                {
                    guarded = true;
                    //  The branch the guard runs: the statement after if (...) or the rest of the expression
                    int s = k;
                    for (int q = k; q <= to; ++q)
                        if (t.P(q, ")") && IsControlHead(t, q))
                        {
                            s = q + 1;
                            break;
                        }
                        else if (t.P(q, ";"))
                            break;
                    int e = t.P(s, "{") ? t.Match(s) : ExprEndStatement(t, s);
                    if ((e > s) && SaysText.IsMatch(t.Span(s, e)))
                        says = true;
                    if (t.P(k + 1, "?.") || t.P(k + 1, "??") || t.P(k + 1, "||"))
                        says |= false;
                    continue;
                }
                if ((t.P(k + 1, ".") || t.P(k + 1, "[")) && !guarded)
                    unguarded = true;
            }
            return (unguarded, guarded, says);
        }

        static bool AlreadyReported(Checker.FileContext ctx, Rule rule, int offset)
        {
            var line = ctx.Source.GetLineCol(offset).Line;
            return ctx.Findings.Any(f => (f.Rule == rule) && (f.Line == line) && (f.Path == ctx.Rel));
        }

        static void AddOnce(Checker.FileContext ctx, Rule rule, int offset, String msg)
        {
            if (!AlreadyReported(ctx, rule, offset))
                Add(ctx, rule, offset, msg);
        }

        static readonly HashSet<String> ComponentFactories = new HashSet<String>(StringComparer.Ordinal) { "Card", "ListCard", "Tile", "Kpi", "Group", "Form", "PageLayout", "PageHeader", "Tabs", "Toolbar" };
        static readonly Regex NavigateCode = new Regex(@"\blocation\s*(?:=(?!=)|\.\s*(?:href\s*=(?!=)|assign\s*\(|replace\s*\())|\bPage\s*\.\s*Navigate\s*\(|\bwindow\s*\.\s*open\s*\(|\.submit\s*\(\s*\)|\bHistory\s*\.\s*Go\s*\(", RegexOptions.CultureInvariant);
        static readonly Regex OpensCode = new Regex(@"\bDialog\s*\.\s*(?:Open|Show|Confirm|Alert|Prompt)\s*\(|\bMenu\s*\.\s*Show\s*\(|\bPopup\s*\.\s*Show\s*\(", RegexOptions.CultureInvariant);
        static readonly Regex CopyCode = new Regex(@"\bclipboard\s*\.\s*write(?:Text)?\s*\(|\bClipboard\s*\.\s*Copy\s*\(|execCommand\s*\(\s*[""']copy[""']", RegexOptions.CultureInvariant);
        static readonly Regex VagueTitle = new Regex(@"^\s*(?:are you (?:really )?sure(?: you want to (?:continue|do this|proceed))?|really|confirm(?:ation)?|continue|proceed|do you want to (?:continue|proceed)|sure|warning|attention|please confirm)\s*[?!.]*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex DestructiveVerb = new Regex(@"^\s*(?:delete|remove|kill|terminate|destroy|erase|purge|wipe|drop|revoke|uninstall|discard|clear|reset|end|cancel\s+\w+|withdraw|ban|block|overwrite|empty|deactivate|disable\s+account)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The round 4 rules of a script (called by CheckFamilies, page code only).
        /// </summary>
        static void CheckRound4(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<Reg> regs, List<(int A, int B)> dialogCalls)
        {
            var text = ctx.Text;
            var sec = Secrets;
            CheckRound4More(ctx, t, flow, vars, regs);
            CheckRound5(ctx, t, flow, vars, regs);

            //  ---- SEC-1: a secret field shown in clear text (masked by default, with a Show toggle)
            if (sec != null)
                foreach (var (at, name, label) in sec.Unmasked)
                    AddOnce(ctx, Rule.UxSec1, t.S[at].Start, "the field \"" + name + "\" holds a secret (\"" + label + "\") but shows it in clear text: anyone behind the user (and every screen share) reads it; make it Type: \"password\" (Sw.TextBox shows a Show toggle) and never fill it with the stored value");

            for (int i = 0; i < t.N; ++i)
            {
                //  ---- SEC-1: a secret shown on screen (a status text, an element's text, a toast or announcement through a translation)
                if ((t.Id(i, "textContent") || t.Id(i, "innerText")) && t.P(i - 1, ".") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                {
                    int e = ExprEnd(t, i + 2);
                    if (!MaskedToggle(t, i + 2, e) && ((SecretValueIn(t, i + 2, e) ?? SecretIn(t, i + 2, e)) is String sv))
                        AddOnce(ctx, Rule.UxSec1, t.S[i].Start, "the secret \"" + Short(sv) + "\" is shown on the page: anyone behind the user and every screen share reads it; show that it is set (\"Key saved\"), mask it, or copy it without showing it (Sw.CopyButton)");
                }
                if (t.Id(i, "text") && t.Key(i) && (EnclosingObject(t, i) is int to) && (to > 0) && Regex.IsMatch(t.Span(Math.Max(0, to - 8), to), @"\bel\s*\([^;]*$"))
                {
                    int e = ExprEnd(t, i + 2);
                    if (!MaskedToggle(t, i + 2, e) && ((SecretValueIn(t, i + 2, e) ?? SecretIn(t, i + 2, e)) is String sv))
                        AddOnce(ctx, Rule.UxSec1, t.S[i].Start, "the secret \"" + Short(sv) + "\" is shown as the text of an element: mask it or say only that it is set");
                }
                if ((ToastFunctions.Contains(t.Tx(i)) || t.Tx(i) == "Announce" || t.Tx(i) == "Alert") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var ta) && (ta.Count > 0))
                    if ((SecretValueIn(t, ta[0].A, ta[0].B) ?? SecretIn(t, ta[0].A, ta[0].B)) is String sv)
                        AddOnce(ctx, Rule.UxSec1, t.S[i].Start, "the " + t.Tx(i) + " text includes the secret \"" + Short(sv) + "\": toasts and announcements are read aloud and stay on screen; say what happened without the value");

                //  ---- SAF-2: a confirmation that names nothing / a destructive one without the danger button
                if (t.Chain(i, "Dialog", "Confirm") && t.P(i + 1, "(") && (t.Args(i + 1) is var ca) && (ca.Count > 0))
                {
                    var title = t.Lit(ca[0]);
                    var body = ca.Count > 1 ? t.Lit(ca[1]) : null;
                    var ok = ca.Count > 2 ? t.Lit(ca[2]) : null;
                    var opts = ca.Count > 3 ? t.Span(ca[3].A, ca[3].B) : "";
                    bool danger = Regex.IsMatch(opts, @"\bDanger\s*:\s*(?!false)");
                    var fn = flow.Around(i);
                    //  Destructive: the verb, a danger button that opens it, or a destructive request after it in the function
                    bool destructive = ((ok != null) && DestructiveVerb.IsMatch(ok)) || ((title != null) && DestructiveVerb.IsMatch(title));
                    String why = destructive ? "its verb \"" + (ok ?? title) + "\"" : null;
                    if (!destructive)
                        for (var n = fn; (n != null) && !destructive; n = n.Parent)
                            if (t.Id(n.Head - 2, "OnClick") && (EnclosingObject(t, n.Head - 2) is int bo) && (bo > 0) && (ObjKey(t, bo, "Kind") is var kd) && (kd != null) && (t.Lit(kd.Value) == "danger"))
                            {
                                destructive = true;
                                why = "a danger button opens it";
                            }
                    if (!destructive && (fn != null))
                        foreach (var q in flow.Of(fn).Reqs)
                            if ((q.At > i) && !q.Chain && q.Url.Split(' ').Any(u => DestructiveApi.IsMatch(u)))
                            {
                                destructive = true;
                                why = "it guards " + Short(q.Url);
                                break;
                            }
                    if ((title != null) && VagueTitle.IsMatch(title))
                        AddOnce(ctx, Rule.UxSaf2, t.S[i].Start, "a confirmation titled \"" + title + "\": it names neither the action nor the target, so people confirm without reading; ask the real question (\"Cancel 3 shipments?\"), say the consequence in the text and put the verb on the button (Sw.Cells.ConfirmDanger({ Action, Target }) for a destructive one)");
                    else if (destructive && (body == "") )
                        AddOnce(ctx, Rule.UxSaf2, t.S[i].Start, "a destructive confirmation without a text: say the consequence (\"The customers are told by e-mail. This can't be undone.\")");
                    if (destructive && !danger && !AlreadyReported(ctx, Rule.DesignConfirm, t.S[i].Start))
                        AddOnce(ctx, Rule.UxSaf2, t.S[i].Start, "a destructive confirmation (" + why + ") without Danger: true: its button is the plain primary one; pass { Danger: true } (the danger button, Cancel focused, Enter never confirms) or use Sw.Cells.ConfirmDanger");
                }

                //  ---- ACC-5: the input id of a Sw control changed (its label no longer names it)
                if (t.Id(i, "id") && t.P(i - 1, ".") && t.Id(i - 2, "Input") && t.P(i - 3, ".") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                    Add(ctx, Rule.UxAcc5, t.S[i].Start, "the id of a Sw control's input is changed after it was made: its <label for> keeps the old id, so the field has no accessible name (and checks that read the label, such as the secret check of drafts, no longer see it); leave the id alone (select the control by its own element, x.E) or pass Id in the options");

                //  ---- MOT-3: smooth scrolling without a reduced motion check
                if (t.Id(i, "behavior") && t.Key(i) && (t.Str(i + 2) == "smooth") && (t.P(i + 3, ",") || t.P(i + 3, "}")) && !Regex.IsMatch(text, @"prefers-reduced-motion|\bReducedMotion\b|\bMotion\s*\.\s*Reduced\b|\breduce[dD]?Motion\b"))
                    Add(ctx, Rule.UxMot3, t.S[i].Start, "behavior: \"smooth\" scrolls with an animation also when the user asked for reduced motion; use Sw.Motion's setting or test matchMedia(\"(prefers-reduced-motion: reduce)\") and jump (behavior: \"auto\") then");

                //  ---- FBK-4: progress in percent (Progress takes 0..1)
                if (t.Id(i, "Progress") && t.Key(i))
                {
                    int e = ExprEnd(t, i + 2);
                    var pv = t.Span(i + 2, e);
                    if (((e == i + 2) && (t.S[i + 2].Kind == JsTokenKind.Number) && Double.TryParse(t.Tx(i + 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pn) && (pn > 1)) || Regex.IsMatch(pv, @"\*\s*100\b"))
                        Add(ctx, Rule.UxFbk4, t.S[i].Start, "Progress: " + Short(pv) + " looks like percent: Progress is a fraction 0..1 (40 shows 100 %); pass done / total (0.4)");
                }
                //  ---- FBK-4: a promise that never settles (an executor that never calls resolve / reject)
                if (t.Id(i, "new") && t.Id(i + 1, "Promise") && t.P(i + 2, "(") && (flow.At(i + 3) is FnNode pe))
                {
                    var p0 = FirstParam(t, pe);
                    var pb = t.Span(pe.F.Start, pe.F.End);
                    if ((p0 == null || !Regex.IsMatch(pb, @"(?<![\w.$])" + Regex.Escape(p0) + @"\b")) && !Regex.IsMatch(t.Span(pe.Head, pe.F.Start), @"\(\s*\{|\(\s*\w+\s*,\s*\w+") && !Regex.IsMatch(pb, @"\breject\b|\bresolve\b"))
                        Add(ctx, Rule.UxFbk4, t.S[i].Start, "a Promise whose executor never calls resolve / reject: it never settles, so the button or control that waits for it stays busy for ever and nothing is said; resolve it (or return the real request)");
                }
                //  ---- POP-6: Read: true on a write (Cancel would abort what the server already has)
                if (IsRequest(t, i) && (t.Args(i + 1) is var ra) && (ra.Count > 0) && (Regex.Match(t.Span(i, Math.Max(i, t.Match(i + 1))), @"\b(ReadOnly|Read|AbortSafe)\s*:\s*true\b") is Match rm) && rm.Success)
                {
                    var u = Literals(t, ra[0]).Trim().Split(' ').LastOrDefault() ?? "";
                    //  (ReadOnly is the author's statement: flagged only for an api named as a write; AbortSafe also when it is not named as a read)
                    if ((u.Length > 0) && !ReadUrl.IsMatch(u) && !Regex.IsMatch(u, @"Table(?:[?#].*)?$") && !t.Id(i, "Get") && ((rm.Groups[1].Value != "ReadOnly") || WriteUrl.IsMatch(u)))
                        Add(ctx, Rule.UxPop6, t.S[i].Start, "{ " + rm.Groups[1].Value + ": true } on " + Short(u) + ", which writes: Cancel then aborts a request the server may already have done, and the dialog says it was cancelled; mark only reads (or leave it: a write gets \"Stop waiting\" and its late result)");
                    else if (rm.Groups[1].Value == "Read")
                        Add(ctx, Rule.UxPop6, t.S[i].Start, "{ Read: true }: Read is Sw.Net's body reader (a function); the read mark is { ReadOnly: true } (or Sw.Net.Read(url, arg)), so Cancel aborts this read for real");
                }
                //  ---- FBK-2: Sw.Net.Live for the button's own save
                if (t.Chain(i, "Net", "Live") && t.P(i + 1, "(") && (t.Args(i + 1) is var la) && (la.Count > 0) && InOnClick(t, flow, i)
                    && (SaveApi.IsMatch(Literals(t, la[0]).Trim().Split(' ').LastOrDefault() ?? "") || ((la.Count > 1) && Regex.IsMatch(t.Span(la[1].A, la[1].B), @"\.Get\(\s*\)|\.value\b|\.checked\b"))))
                    Add(ctx, Rule.UxFbk2, t.S[i].Start, "Sw.Net.Live for the save of the button (" + Short(Literals(t, la[0]).Trim()) + "): Live is a detached request (a live view, telemetry): the dialog's cancel model and the page's dirty state ignore it; use Sw.Net.Send and return it");

                //  ---- a Sw component appended with the native DOM (it renders "[object Object]"; the control is not in the page)
                if ((t.Id(i, "append") || t.Id(i, "appendChild") || t.Id(i, "prepend") || t.Id(i, "before") || t.Id(i, "after") || t.Id(i, "replaceChildren") || t.Id(i, "replaceWith") || t.Id(i, "insertBefore")) && t.P(i - 1, ".") && t.P(i + 1, "(") && !t.Id(i - 2, "page") && !Regex.IsMatch(t.Tx(i - 2), @"^(?:Body|Actions|Tools)$"))
                    foreach (var a in t.Args(i + 1))
                    {
                        bool comp = false;
                        int last = a.B;
                        if (t.P(last, ")") && !t.P(last + 1, "."))
                        {
                            int s0 = a.A;
                            if (t.Id(s0, "new") && t.Chain(s0 + 3, "Sw", t.Tx(s0 + 3)) && t.Id(s0 + 1, "Sw") && Char.IsUpper(t.Tx(s0 + 3).FirstOrDefault()) && (t.Match(s0 + 4) == last))
                                comp = true;
                            else if (t.Id(s0, "Sw") && t.P(s0 + 1, ".") && ComponentFactories.Contains(t.Tx(s0 + 2)) && t.P(s0 + 3, "(") && (t.Match(s0 + 3) == last))
                                comp = true;
                        }
                        else if ((a.A == a.B) && t.Id(a.A) && vars.TryGetValue(t.Tx(a.A), out var cv) && cv.Kind.StartsWith("Sw.", StringComparison.Ordinal) && (t.Id(cv.At + 2, "new") || ComponentFactories.Contains(cv.Kind.Substring(3))) && (cv.Kind != "Sw.el")
                            && InScope(flow, cv.At, a.A) && !((flow.Around(a.A) is FnNode af) && (af != flow.Around(cv.At)) && Regex.IsMatch(t.Span(af.Head, Math.Max(af.Head, af.F.Start - 1)), @"(?<![\w.$])" + Regex.Escape(t.Tx(a.A)) + @"\b")))
                            comp = true;
                        if (comp)
                        {
                            Add(ctx, Rule.UxAcc3, t.S[a.A].Start, "a Sw component given to the native " + t.Tx(i) + "(): the DOM shows the text \"[object Object]\" and the control is not in the page (no name, no focus, no keyboard); append its element: " + Short(t.Span(a.A, a.B)) + ".E (or Sw.el / page.Add, which take components)");
                            break;
                        }
                    }
                //  ---- SAF-11: a draft cleared before the send is known to have worked
                //  (also Sw.Draft.Sent(...) before the send)
                bool sentCall = t.Chain(i, "Draft", "Sent") && t.P(i + 1, "(");
                if ((sentCall || (t.Id(i, "Clear") && t.P(i - 1, ".") && t.Id(i - 2) && t.P(i + 1, "(") && (t.P(i + 2, ")") || t.P(i + 2, "{")) && (Regex.IsMatch(text, @"\b" + Regex.Escape(t.Tx(i - 2)) + @"\s*=\s*(?:Sw\s*\.\s*)?Draft\s*\.\s*Attach\b") || Regex.IsMatch(t.Tx(i - 2), "(?i)draft")))) && (flow.Around(i) is FnNode cfn))
                {
                    //  A request later in the same function, not awaited before the Clear
                    bool awaitedBefore = flow.Own(cfn).Any(k => (k < i) && t.Id(k, "await"));
                    var later = flow.Of(cfn).Reqs.FirstOrDefault(r => (r.At > i) && !r.Chain);
                    if ((later != null) && !awaitedBefore)
                        Add(ctx, Rule.UxSaf11, t.S[i].Start, (sentCall ? "Sw.Draft.Sent(...)" : t.Tx(i - 2) + ".Clear()") + " before the send: when the request fails (offline, a server error) the text is gone and the page isn't dirty, so a reload loses it; clear the draft after the send succeeded (await the request, then Clear())");
                }
                //  ---- TXT-30: Sw.Net.Message(...).Text (Message returns the text itself since series 4 round 4: .Text is undefined)
                if ((t.Chain(i, "Net", "Message") || t.Chain(i, "Net", "MessageText")) && t.P(i + 1, "(") && (t.Match(i + 1) is int mc) && (mc > i) && t.P(mc + 1, ".") && t.Id(mc + 2, "Text"))
                    Add(ctx, Rule.UxTxt30, t.S[mc + 2].Start, "Sw.Net." + t.Tx(i) + "(...).Text is undefined: " + t.Tx(i) + " returns the text itself (a string); drop .Text (use Sw.Net.MessageParts(...) for What / Why / Do)");
                //  ---- FOC-2: focus moved to a destructive button
                if ((t.Id(i, "focus") || t.Id(i, "Focus")) && t.P(i - 1, ".") && t.P(i + 1, "("))
                {
                    var recv = t.Id(i - 2, "E") && t.P(i - 3, ".") ? t.Tx(i - 4) : t.Tx(i - 2);
                    if (IsDangerButton(t, vars, recv))
                        Add(ctx, Rule.UxFoc2, t.S[i].Start, "the focus is moved to the destructive button \"" + recv + "\": Enter or Space then starts the destructive action; focus the first field, the search box or the page heading (FOC-2), never a danger button");
                }
                //  (the focus helpers: Sw.Focus.Set / Show / Restore / Initial (x, x.E, this.x.E))
                if ((t.Chain(i, "Focus", "Set") || t.Chain(i, "Focus", "Show") || t.Chain(i, "Focus", "Restore") || t.Chain(i, "Focus", "Initial")) && t.P(i + 1, "(") && (t.Args(i + 1) is var fa) && (fa.Count > 0))
                {
                    var arg = fa[t.Id(i, "Initial") && (fa.Count > 1) ? 1 : 0];
                    int last = arg.B;
                    if (t.Id(last, "E") && t.P(last - 1, "."))
                        last -= 2;
                    if (t.Id(last) && IsDangerButton(t, vars, t.Tx(last)))
                        Add(ctx, Rule.UxFoc2, t.S[i].Start, "the focus is moved to the destructive button \"" + t.Tx(last) + "\" (Sw.Focus." + t.Tx(i) + "): Enter or Space then starts the destructive action; focus the first field, the search box or the page heading (FOC-2), never a danger button");
                }
            }

            //  ---- ACC-11: a change that navigates / a focus that opens
            foreach (var g in regs)
            {
                if (g.Fn == null)
                    continue;
                var deep = DeepSpan(t, flow, g.Fn);
                if (((g.Evt == "change") || (g.Evt == "input")) && NavigateCode.IsMatch(deep))
                    AddOnce(ctx, Rule.UxAcc11, t.S[g.At].Start, "a " + g.Evt + " handler that navigates (" + Short(NavigateCode.Match(deep).Value) + "): changing a value (a select, a field) must never take the user elsewhere (keyboard users move through the options with the arrow keys); update in place, or navigate from a \"Go\" / \"Open\" button");
                if (((g.Evt == "focus") || (g.Evt == "focusin")) && (OpensCode.IsMatch(deep) || NavigateCode.IsMatch(deep)))
                    AddOnce(ctx, Rule.UxAcc11, t.S[g.At].Start, "a " + g.Evt + " handler that opens a dialog / menu or navigates: moving the focus must never open or go anywhere (Tab would trap the user); open it on a click or a key");
            }
            for (int i = 0; i < t.N; ++i)
                if ((t.Id(i, "OnChange") || t.Id(i, "OnInput")) && t.Key(i) && (EnclosingObject(t, i) is int oo) && (oo > 0) && IsSwOptions(t, oo) && Regex.IsMatch(t.Span(Math.Max(0, oo - 2), oo - 1), @"(?:TextBox|NumberBox|Select|Toggle|Segmented|CheckBox|Checkbox|Radio\w*|ComboBox|DateBox|Slider)\s*\($") && (flow.At(i + 2) is FnNode cn) && NavigateCode.IsMatch(DeepSpan(t, flow, cn)))
                    AddOnce(ctx, Rule.UxAcc11, t.S[i].Start, t.Tx(i) + " of a value control navigates: changing a value must never take the user elsewhere; navigate from a \"Go\" / \"Open\" button, or use links / Sw.Tabs for navigation");

            //  ---- LST-4: a click on a cell / value that copies
            //  (cells, rows and list items: LST-4 is the list rule; a code block or a gallery tile that says "Click to copy" is a copy button)
            foreach (var g in regs)
                if ((g.Evt == "click") && (g.Fn != null) && CopyCode.IsMatch(DeepSpan(t, flow, g.Fn)) && IsItemName(vars, g.Recv, text))
                    AddOnce(ctx, Rule.UxLst4, t.S[g.At].Start, "a click on \"" + g.Recv + "\" copies its value: a plain click selects or focuses (people click to select text, or to focus the row), and a copy nobody asked for overwrites the clipboard; offer a copy button (Sw.CopyButton) or Ctrl+C on the focused cell");
            for (int i = 0; i < t.N; ++i)
                if (t.Chain(i, "Sw", "OnClick") && t.P(i + 1, "(") && (t.Args(i + 1) is var oa) && (oa.Count > 1) && (flow.At(oa[1].A) is FnNode on) && CopyCode.IsMatch(DeepSpan(t, flow, on)) && IsItemName(vars, t.Span(oa[0].A, oa[0].B), text))
                    AddOnce(ctx, Rule.UxLst4, t.S[i].Start, "a click on \"" + Short(t.Span(oa[0].A, oa[0].B)) + "\" copies its value: copy is a copy button (Sw.CopyButton) or Ctrl+C on the focused cell, a click only selects / focuses");

            //  ---- INP-20: a focusable item (tabindex 0) that acts on click / pointer but has no key
            var focusable = new Dictionary<String, int>(StringComparer.Ordinal);
            for (int i = 0; i < t.N; ++i)
            {
                //  Sw.el("tr", { attrs: { tabindex: "0" } }), x.tabIndex = 0, x.setAttribute("tabindex", "0")
                if ((t.Id(i, "tabindex") || t.Str(i) == "tabindex") && t.P(i + 1, ":") && ((t.Str(i + 2) == "0") || (t.Tx(i + 2) == "0")))
                {
                    for (int k = i; k > Math.Max(0, i - 40); --k)
                        if (t.P(k, "=") && t.Id(k - 1) && !t.P(k + 1, "="))
                        {
                            focusable[t.Tx(k - 1)] = i;
                            break;
                        }
                        else if (t.P(k, ";"))
                            break;
                }
                if ((t.Id(i, "tabIndex") && t.P(i - 1, ".") && t.P(i + 1, "=") && (t.Tx(i + 2) == "0")) || (t.Id(i, "setAttribute") && t.P(i + 1, "(") && (t.Str(i + 2)?.ToLowerInvariant() == "tabindex") && ((t.Str(i + 4) == "0") || (t.Tx(i + 4) == "0"))))
                    if (t.Id(i - 2) && t.P(i - 1, "."))
                        focusable[t.Tx(i - 2)] = i;
            }
            foreach (var kv in focusable)
            {
                var nm = kv.Key;
                if (IsControlName(t, vars, nm))
                    continue;
                bool acts = regs.Any(g => (g.Recv == nm) && (g.Evt == "click" || PressEvents.Contains(g.Evt) || UpEvents.Contains(g.Evt) || g.Evt == "dblclick")) || Regex.IsMatch(text, @"\bSw\.(?:OnClick|LongPress)\(\s*" + Regex.Escape(nm) + @"\b");
                bool keys = regs.Any(g => (g.Recv == nm) && (g.Evt == "keydown" || g.Evt == "keyup" || g.Evt == "keypress")) || Regex.IsMatch(text, @"\bSw\.KeyClick\(\s*" + Regex.Escape(nm) + @"\b|\brole\b[""']?\s*[:,]\s*[""'](?:button|link|option|row|gridcell|treeitem|menuitem|tab)\b");
                if (acts && !keys)
                    AddOnce(ctx, Rule.UxInp20, t.S[kv.Value].Start, "\"" + nm + "\" is focusable (tabindex 0) and acts on a click / pointer, but no key does it: a keyboard user reaches it and Enter / Space do nothing; add Sw.KeyClick(" + nm + ") (Enter and Space click it) and a role, or use a button");
            }

            //  ---- FBK-6: a list loaded from the server and rendered by hand without an empty state
            foreach (var fn in flow.Fns)
            {
                foreach (var j in flow.Own(fn))
                {
                    if (!IsRequest(t, j))
                        continue;
                    int s0 = StatementStart(t, fn.From, j);
                    int nv = (t.Id(s0, "const") || t.Id(s0, "let") || t.Id(s0, "var")) ? s0 + 1 : s0;
                    if (!(t.Id(nv) && t.P(nv + 1, "=")))
                        continue;
                    var rv = t.Tx(nv);
                    //  The function's own code after the request and its inline callbacks (forEach / map), not a render function it calls
                    var sbb = new System.Text.StringBuilder();
                    foreach (var k in flow.Own(fn))
                        if (k > j)
                            sbb.Append(t.Tx(k)).Append(' ');
                    foreach (var c in fn.Children)
                        if ((c.Head > j) && (flow.KindOf(c) == CallbackKind.Inline))
                            sbb.Append(t.Span(c.F.Start, c.F.End)).Append(' ');
                    var body = sbb.ToString();
                    bool renders = Regex.IsMatch(body, @"\bfor\s*\(\s*(?:const|let|var)\s+\w+\s+of\s+" + Regex.Escape(rv) + @"\b|\b" + Regex.Escape(rv) + @"\s*\.\s*(?:forEach|map)\s*\(") && Regex.IsMatch(body, @"\.\s*(?:append|appendChild|replaceChildren|prepend)\s*\(|innerHTML\s*[+]?=");
                    //  (an empty state: a test for no rows, not a truthy length test such as if (rows.length) scroll)
                    var re = Regex.Escape(rv);
                    bool empty = Regex.IsMatch(body, @"!\s*" + re + @"\s*(?:\?\.|\.)\s*length\b|\b" + re + @"\s*(?:\?\.|\.)\s*length\s*(?:===?\s*0|<\s*1|<=\s*0)|!\s*" + re + @"\b|\bEmpty\b|sw-empty|StateView|No\s+\w+\s+yet|\bNothing\b", RegexOptions.IgnoreCase);
                    if (renders && !empty && !Regex.IsMatch(body, @"\bSetRows\b|\bSw\s*\.\s*Table\b"))
                        AddOnce(ctx, Rule.UxFbk6, t.S[j].Start, "the list \"" + rv + "\" is loaded and rendered by hand without an empty state: with no rows the area is blank (it looks broken or still loading); show what would be here and how to add it when it is empty (Sw.StateView / a \"No ... yet\" line), and a failure with Try again (or use Sw.Table, which has all three)");
                }
            }

            //  ---- FBK-4: one Key for two different works (the second Run returns the first job: its work never runs)
            var runKeys = new Dictionary<String, (String Title, int At)>(StringComparer.Ordinal);
            for (int i = 0; i < t.N; ++i)
                if ((t.Chain(i, "Activity", "Run") || t.Chain(i, "Activity", "Start") || t.Chain(i, "Activity", "Track")) && t.P(i + 1, "(") && (t.Args(i + 1) is var aa) && (aa.Count > 0))
                {
                    var oil = aa.Where(x => t.P(x.A, "{")).ToList();
                    if (oil.Count == 0)
                        continue;
                    var oi = oil[0];
                    var kv = ObjKey(t, oi.A, "Key");
                    var tv = ObjKey(t, oi.A, "Title");
                    var key = kv != null ? t.Lit(kv.Value) : null;
                    var title = tv != null ? (ValueText(t, tv.Value) ?? t.Span(tv.Value.A, tv.Value.B)) : "";
                    if (key == null)
                        continue;
                    if (runKeys.TryGetValue(key, out var first) && (first.Title != title))
                        Add(ctx, Rule.UxFbk4, t.S[kv.Value.A].Start, "the Key \"" + key + "\" is also used for other work (\"" + Short(first.Title) + "\", line " + ctx.Source.GetLineCol(t.S[first.At].Start).Line + "): a Run with the Key of a running job returns that job, so this work never runs and the user sees the other one finish; one Key per kind of work");
                    else if (!runKeys.ContainsKey(key))
                        runKeys[key] = (title, kv.Value.A);
                }
            //  ---- FBK-4: a cancellation swallowed inside the work of Sw.Activity.Run (a catch around the wait / request that goes on)
            for (int i = 0; i < t.N; ++i)
                if (t.Chain(i, "Activity", "Run") && t.P(i + 1, "(") && (t.Args(i + 1) is var aa) && (aa.Count > 1) && (flow.At(aa[1].A) is FnNode wk) && (FirstParam(t, wk) is String sig))
                    for (int k = wk.F.Start; k <= wk.F.End; ++k)
                        if (t.Id(k, "catch") && t.P(k - 1, "}") && !t.P(k + 1, "."))
                        {
                            int b = t.P(k + 1, "(") ? t.Match(k + 1) + 1 : k + 1;
                            int be = t.P(b, "{") ? t.Match(b) : -1;
                            int to = -1;
                            for (int q = k - 2, d = 0; q >= wk.F.Start; --q)
                            {
                                if (t.P(q, "}"))
                                    ++d;
                                else if (t.P(q, "{") && (d-- == 0))
                                {
                                    to = q;
                                    break;
                                }
                            }
                            if ((be > b) && (to > 0) && Regex.IsMatch(t.Span(to, k), @"(?<![\w.$])" + Regex.Escape(sig) + @"\b") && !Regex.IsMatch(t.Span(b, be), @"\bthrow\b|\bIsAbort\b|AbortError|\baborted\b|\breturn\b|\bbreak\b"))
                            {
                                Add(ctx, Rule.UxFbk4, t.S[k].Start, "a catch around the cancellable wait / request of Sw.Activity.Run swallows the cancel (AbortError): after Cancel the loop goes on and the job ends \"done\"; rethrow it (if (Sw.Cancel.IsAbort(e)) throw e;) or let it reach the job");
                                break;
                            }
                        }
        }

        /// <summary>
        /// Silent failures by data flow: Promise.allSettled results never read; a helper that turns a failure into "nothing" (a swallowing
        /// .catch / a catch returning null) whose result is used as if it worked, ignored, or tested without saying anything.
        /// </summary>
        static void CheckSilentFlow(Checker.FileContext ctx, Toks t, Flow flow, List<Reg> regs, Dictionary<String, int> nothing, Action<Rule, int, String> once)
        {
            for (int i = 0; i < t.N; ++i)
            {
                //  ---- Promise.allSettled whose rejections nobody reads
                if (t.Chain(i, "Promise", "allSettled") && t.P(i + 1, "(") && (t.Match(i + 1) is int ac) && (ac > i))
                {
                    var fn = flow.Around(i);
                    int s0 = StatementStart(t, fn?.From ?? 0, i);
                    String v = null;
                    if ((t.Id(s0, "const") || t.Id(s0, "let") || t.Id(s0, "var")) && t.Id(s0 + 1) && t.P(s0 + 2, "="))
                        v = t.Tx(s0 + 1);
                    else if (t.Id(s0) && t.P(s0 + 1, "=") && !t.P(s0 + 2, "="))
                        v = t.Tx(s0);
                    bool read;
                    if (t.P(ac + 1, ".") && t.Id(ac + 2, "then") && (t.Args(ac + 3) is var tha) && (tha.Count > 0) && (flow.At(tha[0].A) is FnNode tf))
                        read = Regex.IsMatch(t.Span(tf.F.Start, tf.F.End), @"\bstatus\b|rejected|\breason\b|fulfilled");
                    else if (v != null)
                    {
                        var rest = t.Span(ac, fn?.F.End ?? t.N - 1);
                        read = Regex.IsMatch(rest, @"\b" + Regex.Escape(v) + @"\b[\s\S]*?(?:\bstatus\b|rejected|\breason\b|fulfilled)") || Regex.IsMatch(rest, @"\breturn\s+" + Regex.Escape(v) + @"\b|\(\s*" + Regex.Escape(v) + @"\s*[,)]");
                    }
                    else
                        read = t.Id(s0, "return");
                    if (!read)
                        once(Rule.UxFbk5, i, "Promise.allSettled(...) whose results are never read: the requests that failed are dropped and the page says all of them worked; look at the results (const failed = res.filter(r => r.status === \"rejected\")) and say how many failed and why (Sw.Fail(failed[0].reason)), or use Promise.all and let the button show the failure");
                }
            }
            //  ---- a helper that turns a failure into "nothing"
            foreach (var kv in nothing)
            {
                int calls = 0;
                bool silent = false;
                int silentAt = -1;
                for (int i = 0; i < t.N; ++i)
                {
                    if (!t.Id(i, kv.Key) || !t.P(i + 1, "(") || IsDeclaration(t, i) || (t.P(i - 1, ".") && !t.Id(i - 2)))
                        continue;
                    var g = flow.NamedFn(kv.Key);
                    if ((g != null) && (g.F.Start <= i) && (i <= g.F.End))
                        continue;
                    var fn = flow.Around(i);
                    if ((fn == g) || ((g != null) && (g.Head == i || g.Head == i - 1)))
                        continue;
                    ++calls;
                    int cs = ChainStartOf(t, i);
                    int s0 = StatementStart(t, fn?.From ?? 0, cs);
                    int close = t.Match(i + 1);
                    if (close < 0)
                        continue;
                    //  F(...).then(v => ...): the callback's parameter is the value
                    String v = null;
                    FnNode scope = fn;
                    int after = close + 1;
                    if (t.P(close + 1, ".") && t.Id(close + 2, "then") && (t.Args(close + 3) is var tha) && (tha.Count > 0) && (flow.At(tha[0].A) is FnNode tf))
                    {
                        v = FirstParam(t, tf);
                        scope = tf;
                        after = tf.F.Start;
                    }
                    else if (!t.Id(cs - 1, "await"))
                        continue;   //  a promise kept or handed on (Promise.all, a variable awaited later): judged where it is awaited
                    else if ((t.Id(s0, "const") || t.Id(s0, "let") || t.Id(s0, "var")) && t.Id(s0 + 1) && t.P(s0 + 2, "="))
                        v = t.Tx(s0 + 1);
                    else if (t.Id(s0) && t.P(s0 + 1, "=") && !t.P(s0 + 2, "="))
                        v = t.Tx(s0);
                    else if (t.Id(s0, "return") || t.Id(s0 - 1, "return") || (fn != null && !fn.F.Block))
                        continue;   //  handed to the caller
                    else if (!t.P(ExprEnd(t, cs) + 1, ";") && !t.P(close + 1, ";") && (cs != s0 + 1))
                        continue;   //  used in an expression (if (await F()) ...): the caller tests it
                    if (v == null)
                    {
                        //  The result dropped: await F(...); / F(...);
                        silent = true;
                        silentAt = i;
                        break;
                    }
                    var (unguarded, guarded, says) = UseOf(t, scope, v, after);
                    if (unguarded || (guarded && !says && InReach(flow, i)) || (!guarded && !unguarded && InReach(flow, i)))
                    {
                        silent = true;
                        silentAt = i;
                        break;
                    }
                }
                if (silent)
                    once(Rule.UxFbk5, silentAt, "\"" + kv.Key + "\" turns a failure into null / false (its catch, line " + ctx.Source.GetLineCol(t.S[kv.Value].Start).Line + "), and this call uses the result as if it worked (or stops without a word): the user never learns that it failed; let the failure reach the caller (no catch: the button shows \"<name>: not done\" with the reason), or test the result and show why (Sw.Fail)");
                //  (no caller in the file: only a write is reported; a read that gives null on a failure is optional data by its contract)
                else if ((calls == 0) && !((flow.NamedFn(kv.Key) is FnNode hf) && OnlyReads(t, hf)))
                    once(Rule.UxFbk5, kv.Value, "\"" + kv.Key + "\" turns a failure into null / false: every caller must test the result and show the failure, and a caller that doesn't continues as if it worked; let the failure reach the caller (no catch: a button shows \"<name>: not done\" with the reason), or show it here (Sw.Fail(e)) and say so in the name / doc");
            }
        }

        /// <summary>
        /// More round 4 rules (from the held-out set's misses, generalized): a context menu blocked on any element, a single key shortcut
        /// in a method the page key listener calls (also written as an early return: if (ev.key !== "a" || ev.ctrlKey) return;), the raw
        /// status of an error as the text, a number next to a currency variable, live items inserted at the top, a request chain dropped
        /// without a rejection handler.
        /// </summary>
        static void CheckRound4More(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<Reg> regs)
        {
            foreach (var g in regs)
            {
                if (g.Fn == null)
                    continue;
                //  ---- INP-4: an element's context menu blocked without a menu of its own
                if ((g.Evt == "contextmenu") && !PageObjects.Contains(g.Recv) && !g.Recv.StartsWith("document", StringComparison.Ordinal))
                {
                    var body = t.Span(g.Fn.F.Start, g.Fn.F.End);
                    if (Regex.IsMatch(body, @"preventDefault\s*\(|return\s+false") && !Regex.IsMatch(DeepSpan(t, flow, g.Fn), @"\bif\b|\?|&&|\|\||\bclosest\b|\btarget\b|[Mm]enu|\bShow\w*\(|\bOpen\w*\(|\bPopup\b"))
                        AddOnce(ctx, Rule.UxInp4, t.S[g.At].Start, "a contextmenu handler on \"" + g.Recv + "\" that only blocks the browser's menu: copy, look up and spell check are gone there and nothing replaces them; remove it, or open the element's own menu (Sw.LongPress: right click, long press, the Menu key)");
                }
                //  ---- INP-23: a page key listener (also through the method it calls) that acts on a single key without a check of the focused element
                if (((g.Evt == "keydown") || (g.Evt == "keypress") || (g.Evt == "keyup")) && Regex.IsMatch(g.Recv, @"^(?:document|window|document\.body|document\.documentElement|body)$"))
                {
                    var deep = DeepSpan(t, flow, g.Fn);
                    if (!Regex.IsMatch(deep, @"\b(?:target|srcElement|tagName|isContentEditable|activeElement|composedPath|InInputs|IsTyping|IsTextInput|closest)\b"))
                    {
                        //  if (ev.key !== "a" || ev.ctrlKey ...) return;  -> the action runs for a plain "a"
                        var m = Regex.Match(deep, @"\.key\s*!==?\s*[""'](?<k>[^""'\s])[""'](?<rest>[^)]*)\)\s*(?:\{\s*)?return\b");
                        if (m.Success && !Regex.IsMatch(m.Groups["rest"].Value, @"&&\s*!?\s*\w+\.(?:ctrlKey|metaKey|altKey)"))
                            AddOnce(ctx, Rule.UxInp23, t.S[g.At].Start, "a single key shortcut (\"" + m.Groups["k"].Value + "\") on " + g.Recv + " without a check of the focused element: it fires while the user types that letter in a text field; use Sw.Keys.Add(\"" + m.Groups["k"].Value.ToUpperInvariant() + "\", fn, { Title }) (skips text fields, listed in the help), or add a modifier");
                    }
                }
            }
            for (int i = 0; i < t.N; ++i)
            {
                //  ---- TXT-30: the raw status of an error as the text ("500 - Internal Server Error")
                if ((t.Id(i, "Status") || t.Id(i, "status")) && t.P(i - 1, ".") && t.Id(i - 2) && t.P(i + 1, "+") && Regex.IsMatch(t.Span(i, ExprEnd(t, i)), @"\.(?:StatusText|statusText|message|Message)\b"))
                {
                    var st = t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), ExprEndStatement(t, i));
                    if (!Regex.IsMatch(st, @"console\.|\blog\w*\(|\bthrow\b"))
                        AddOnce(ctx, Rule.UxTxt30, t.S[i].Start, "the raw status of the error as the text (\"" + Short(t.Span(i - 2, ExprEnd(t, i))) + "\" = \"500 - Internal Server Error\"): it says neither what failed nor what to do; use Sw.Net.FailText(name, e) (the server's reason and the advice), or pass the error to Sw.Fail(e)");
                }
                //  ---- TXT-20: a number made by hand next to a currency / unit variable: x.toFixed(2) + " " + currency
                if ((t.Id(i, "toFixed") || t.Chain(i, "Math", "round")) && t.P(i + 1, "("))
                {
                    var ops = PlusChain(t, i, flow.Around(i)?.From ?? 0);
                    var cur = ops.Select(o => t.Span(o.A, o.B)).FirstOrDefault(x => Regex.IsMatch(x, @"(?i)^[\w.]*(?:currency|curr|unit|symbol)$"));
                    var stt = t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), ExprEndStatement(t, i));
                    if ((cur != null) && !Regex.IsMatch(stt, @"\bstyle\b|setProperty"))
                        AddOnce(ctx, Rule.UxTxt20, t.S[i].Start, "an amount or a value formatted by hand next to \"" + Short(cur) + "\": the decimal separator, the grouping and where the currency goes differ between languages; use Sw.Format.Amount(currency, value) (or Sw.Format.Number with the unit in one translated sentence)");
                }
            }
            //  ---- LAY-5: items that arrive by themselves inserted at the top of a list
            var live = new HashSet<FnNode>();
            var stack = new Stack<FnNode>();
            void Root(FnNode n)
            {
                if ((n != null) && live.Add(n))
                    stack.Push(n);
            }
            for (int i = 0; i < t.N; ++i)
            {
                if ((t.Id(i, "Subscribe") || t.Id(i, "setInterval") || t.Id(i, "OnMessage")) && t.P(i + 1, "("))
                    foreach (var a in t.Args(i + 1))
                        Root(flow.At(a.A));
                if (t.Id(i, "onmessage") && t.P(i + 1, "="))
                    Root(flow.At(i + 2));
            }
            foreach (var g in regs)
                if (g.Evt == "message")
                    Root(g.Fn);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                foreach (var c in n.Children)
                    Root(c);
                for (int j = n.F.Start; j <= n.F.End; ++j)
                    if (t.Id(j) && t.P(j + 1, "(") && !Keywords.Contains(t.Tx(j)) && (flow.NamedFn(t.Tx(j)) is FnNode h) && (!t.P(j - 1, ".") || t.Id(j - 2, "this")))
                        Root(h);
            }
            foreach (var n in live)
                foreach (var j in flow.Own(n))
                    if ((t.Id(j, "prepend") && t.P(j - 1, ".") && t.P(j + 1, "(")) || (t.Id(j, "insertBefore") && t.P(j + 1, "(") && Regex.IsMatch(t.Span(j, Math.Max(j, t.Match(j + 1))), @"\.(?:firstChild|firstElementChild)\b|children\[0\]")) || (t.Id(j, "insertAdjacentElement") && (t.Str(j + 2) == "afterbegin")))
                    {
                        AddOnce(ctx, Rule.UxLay5, t.S[j].Start, "an item that arrives by itself (a subscription, a timer, a message) is inserted at the top of the list: what the user reads or is about to click moves down; add it at the end, or keep the scroll position and offer \"1 new\" to show it (Sw.Table SetRows(rows, true) holds the order while the pointer or the focus is on the rows)");
                        break;
                    }
            //  ---- FBK-5: a request chain dropped without a rejection handler (the page's own setup code: nobody else shows the failure)
            foreach (var n in flow.Fns)
                foreach (var j in flow.Own(n))
                    if (t.Id(j, "then") && t.P(j - 1, ".") && t.P(j + 1, "(") && (t.Args(j + 1).Count == 1) && n.F.Block)
                    {
                        int s0 = StatementStart(t, n.From, j);
                        int e0 = ExprEndStatement(t, j);
                        var st = t.Span(s0, e0);
                        bool dropped = !Regex.IsMatch(t.Tx(s0), @"^(?:return|await|const|let|var|yield|if|while|for)$") && !(t.Id(s0) && t.P(s0 + 1, "=")) && !t.P(e0 + 1, ")") && !t.P(e0 + 1, ",");
                        //  (a write, or an api object's call that doesn't say read; a Get of the shown data and a poll are judged by the page's states)
                        if (dropped && !Regex.IsMatch(st, @"\.catch\s*\(|\.finally\s*\(|\b(?:Detached|Background)\s*:\s*true\b") && !PollUrl.IsMatch(st) && !OnlyReadsIn(t, s0, j) && Enumerable.Range(s0, j - s0).Any(q => IsRequest(t, q)) && !InOnClick(t, flow, j))
                        {
                            AddOnce(ctx, Rule.UxFbk5, t.S[j].Start, "a request chain (" + Short(st) + ") whose promise is dropped without a rejection handler: a failure is only an unhandled rejection in the console and the page waits for ever; add .catch(e => Sw.Fail(e)) (or show an error state with Try again), or return / await it where a caller shows failures");
                            break;
                        }
                    }
        }

        /// <summary>True if the name is a button / link / field (by its kind or its name): clicks on controls may act.</summary>
        static bool IsControlName(Toks t, Dictionary<String, (String Kind, int At, int Call)> vars, String name)
        {
            if (String.IsNullOrEmpty(name))
                return false;
            var last = name.Split('.').Last();
            if (vars.TryGetValue(last, out var v))
                return (v.Kind == "button") || (v.Kind == "a") || (v.Kind == "Sw.Button") || FieldTags.Contains(v.Kind) || (FieldControls.Contains(v.Kind));
            return ControlName.IsMatch(last) && !ItemName.IsMatch(last);
        }

        /// <summary>True if the name is a cell, row or list item (by the tag it was made with or its name): td, th, tr, li, a *cell* / *row* / *item*.</summary>
        static bool IsItemName(Dictionary<String, (String Kind, int At, int Call)> vars, String name, String text)
        {
            if (String.IsNullOrEmpty(name))
                return false;
            var last = name.Split('.').Last();
            if (vars.TryGetValue(last, out var v) && Regex.IsMatch(v.Kind, "^(?:td|th|tr|li|dd|dt)$"))
                return true;
            //  (round 5) the element found in a row: tr.querySelector(".bk-id") / closest("td")
            if (Regex.IsMatch(name, @"(?:querySelector|closest)\(\s*[""'`][^""'`]*(?:\btd\b|\btr\b|\bli\b|cell|row|item|-id\b|\bid\b|value|code|ref\b|number|serial)", RegexOptions.IgnoreCase))
                return true;
            //  const ref = tr.querySelector(".ship-ref-cell") / ("td.ref")
            if (Regex.IsMatch(text, @"\b" + Regex.Escape(last) + @"\s*=\s*[^;\n]*(?:querySelector|closest)\(\s*[""'`][^""'`]*(?:\btd\b|\btr\b|\bli\b|cell|row|item)", RegexOptions.IgnoreCase))
                return true;
            return Regex.IsMatch(last, @"(?i)^(?:td|th|tr|li)$|cell|row|item") && !ControlName.IsMatch(last);
        }

        /// <summary>True if the variable is a destructive button (Sw.Button with Kind "danger" / Danger: true), or the element of one.</summary>
        static bool IsDangerButton(Toks t, Dictionary<String, (String Kind, int At, int Call)> vars, String name)
        {
            int call;
            if (vars.TryGetValue(name, out var v) && (v.Call > 0))
                call = v.Call;
            else if ((MemberMade(t, name) is int mc) && (mc > 0))
                call = mc;      //  a page member: this.DeleteAll = new Sw.Button({ ... })
            else
                return false;
            if (!t.P(call + 1, "{"))
                return false;
            var kd = ObjKey(t, call + 1, "Kind");
            var dg = ObjKey(t, call + 1, "Danger");
            return ((kd != null) && (t.Lit(kd.Value) == "danger")) || ((dg != null) && t.Is(dg.Value, "true"));
        }

        // ------------------------------------------------------------------ css (round 4)

        static double CssPx(String v)
        {
            var m = Regex.Match(v ?? "", @"^\s*(\d*\.?\d+)\s*(px|rem|em)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success)
                return -1;
            var n = Double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? n : n * 16;
        }

        /// <summary>A subject that is a control: a button element / role, or a class / id with a word btn, ico, icon-button, close, toggle (dash separated: "ship-ico", "row-btn", not "iconcol").</summary>
        static readonly Regex ClickableSubject = new Regex(@"(?i)(?:^|[\s>+~])button\b|\[role=[""']?button|[.#](?:[\w]+-)*(?:btn|ico|icon-?btn|icon-?button|close|toggle)(?:-(?!label|text|wrap|group|bar|track|thumb|knob|dot|mark|icon)[\w]+)*(?![\w-])", RegexOptions.CultureInvariant);

        /// <summary>The round 4 css rules of one rule block: small targets (INP-24), fixed page widths (LAY-8), faded but present content (MOT-7), smooth scrolling (MOT-3).</summary>
        static void CheckCssRound4(Checker.FileContext ctx, List<CssDecl> list, String sel, String subject, List<CssDecl> all)
        {
            String Val(String p) => list.LastOrDefault(x => x.Prop == p)?.Value?.Replace("!important", "").Trim().ToLowerInvariant();
            var media = list[0].At;
            //  ---- INP-24: a clickable element smaller than 24 px
            bool pointer = Val("cursor") == "pointer";
            bool clickable = pointer || ClickableSubject.IsMatch(subject.Split(':')[0]);
            if (clickable && !Regex.IsMatch(subject, @"(?i)\b(?:svg|img|i)\b|::?(?:before|after)|badge|dot|indicator|icon$") && !Regex.IsMatch(sel, @"(?i)\bsvg\b|\bimg\b|::?(?:before|after)"))
            {
                var w = CssPx(Val("width") ?? Val("inline-size"));
                var h = CssPx(Val("height") ?? Val("block-size"));
                var mw = CssPx(Val("min-width") ?? Val("min-inline-size"));
                var mh = CssPx(Val("min-height") ?? Val("min-block-size"));
                bool small = ((w >= 0) && (w < 24) && !(mw >= 24)) || ((h >= 0) && (h < 24) && !(mh >= 24));
                if (small && ((w > 0) || (h > 0)))
                {
                    var at = list.LastOrDefault(x => (x.Prop == "width" || x.Prop == "height" || x.Prop == "inline-size" || x.Prop == "block-size") && (CssPx(x.Value) >= 0) && (CssPx(x.Value) < 24)) ?? list[0];
                    Add(ctx, Rule.UxInp24, at.ValueStart, "\"" + Short(sel) + "\" is a clickable target of " + at.Value.Trim() + ": below 24 x 24 css px it is hard to hit (also with a mouse) and impossible on touch (44 px in the touch density); keep the icon small but give the target min-width / min-height 24px (padding), as Sw.Button Small does");
                }
            }
            //  ---- LAY-8: a fixed width over 760 px outside a min-width media query and outside a horizontal scroller
            bool inWideMedia = media.Any(x => Regex.IsMatch(x, @"min-width\s*:", RegexOptions.IgnoreCase));
            //  (an absolutely positioned layer, a highlight bar of a code editor, takes no page width)
            if (!inWideMedia && (Val("position") is not ("absolute" or "fixed")) && !Regex.IsMatch(sel, @"(?i)(?:^|[\s>+~,])(?:table|pre|code|canvas|svg|img|video)\b|scroll|chart|print"))
                foreach (var p in new[] { "min-width", "min-inline-size", "width", "inline-size" })
                {
                    var d = list.LastOrDefault(x => x.Prop == p);
                    var px = CssPx(d?.Value?.Replace("!important", ""));
                    if ((d != null) && (px > 760) && !((p == "width" || p == "inline-size") && (Val("max-width") != null)))
                    {
                        //  (a child of a horizontal scroller: .x-scroll > table { min-width: 900px })
                        bool scroller = all.Any(x => (x.Prop == "overflow-x" || x.Prop == "overflow") && Regex.IsMatch(x.Value, "auto|scroll") && (x.Selector != null) && Regex.IsMatch(sel, Regex.Escape(Checker.SplitSelectorList(x.Selector).First().Trim()) + @"\s"));
                        if (!scroller)
                            Add(ctx, Rule.UxLay8, d.ValueStart, "\"" + Short(sel) + "\" has " + p + ": " + d.Value.Trim() + ": on a phone (and at 200 % zoom) the page scrolls sideways and text is cut; let it shrink (max-width with width: 100%), and put wide content in a horizontal scroller of its own");
                        break;
                    }
                }
            //  ---- MOT-7: opacity 0 without visibility: hidden / display: none
            var op = list.LastOrDefault(x => x.Prop == "opacity");
            if ((op != null) && Regex.IsMatch(op.Value.Replace("!important", "").Trim(), @"^0*\.?0*$") && !media.Any(x => Regex.IsMatch(x, "keyframes|starting-style|prefers-reduced-motion", RegexOptions.IgnoreCase))
                && (Val("visibility") != "hidden") && (Val("display") != "none") && (Val("pointer-events") != "none") && (Val("content-visibility") != "hidden")
                //  (a collapsed section that fades as it opens: grid-template-rows 0fr, height / max-height 0; its expanded state is judged by the page)
                && !Regex.IsMatch(Val("grid-template-rows") ?? "", @"^0fr") && (Val("max-height") is not ("0" or "0px")) && (Val("height") is not ("0" or "0px"))
                && !Regex.IsMatch(sel, @"(?i)\[hidden\]|\[inert\]|\[aria-hidden|:not\(|enter|appear|start|from|before-|loading|skeleton|fade-?in|sr-only|visually-hidden|input|file|\boption\b|::?(?:before|after)|placeholder")
                //  (decorative media and custom media elements: an image, an icon, a canvas, <Media-Overlay>; nothing to click or focus)
                && !Regex.IsMatch(subject, @"(?i)^(?:img|svg|i|video|canvas|picture|path|use)\b|(?:^|[>\s])[A-Za-z]+-[A-Za-z][\w-]*(?![\w-]*[.#])|icon"))
            {
                //  The same selector made hidden elsewhere (a separate rule with visibility / display for the faded state)
                bool hiddenElsewhere = all.Any(x => (x.Selector == sel) && (((x.Prop == "visibility") && x.Value.Contains("hidden")) || ((x.Prop == "display") && x.Value.Contains("none"))));
                if (!hiddenElsewhere)
                    Add(ctx, Rule.UxMot7, op.ValueStart, "\"" + Short(sel) + "\" fades to opacity: 0 but stays: it still takes clicks and keyboard focus, and screen readers read it; also hide it once faded (visibility: hidden with a delayed transition, display: none, inert, or remove it with Sw.Motion.Out)");
            }
            //  ---- MOT-3: scroll-behavior: smooth outside a reduced motion query
            var sb = list.LastOrDefault(x => x.Prop == "scroll-behavior");
            if ((sb != null) && sb.Value.Trim().StartsWith("smooth", StringComparison.OrdinalIgnoreCase) && !media.Any(x => Regex.IsMatch(x, @"prefers-reduced-motion\s*:\s*no-preference", RegexOptions.IgnoreCase))
                && !all.Any(x => (x.Prop == "scroll-behavior") && x.At.Any(a => Regex.IsMatch(a, @"prefers-reduced-motion\s*:\s*reduce", RegexOptions.IgnoreCase))))
                Add(ctx, Rule.UxMot3, sb.ValueStart, "scroll-behavior: smooth animates every jump, also for people who asked for reduced motion; put it in @media (prefers-reduced-motion: no-preference) { ... }");
        }
    }
}
