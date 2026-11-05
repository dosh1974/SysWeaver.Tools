using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ rule families (security, formatting, pointer events, silent failures)
        //
        //  Each family is judged by what the code does with a value, not by one spelling: a secret is a name or a key whose words say
        //  secret (apiKey, inv.apiKey, refresh_token, sessionId, passphrase), wherever it goes (storage, cookies, url queries, logs); html
        //  of user data is any html sink whose value has a part that is neither a literal nor escaped; a number formatted by hand is a
        //  toFixed / Math.round / a division by 1024 next to a unit, a toLocale* call or Intl; an action on a pointer event is a handler
        //  that does more than visual feedback or a drag set up; a silent failure is a request whose rejection nobody handles.

        /// <summary>The words of an identifier or key: apiKey, api_key, inv.apiKey, X-Api-Key -> api, key.</summary>
        static List<String> Words(String s)
        {
            var r = new List<String>();
            foreach (Match m in Regex.Matches(s ?? "", @"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+", RegexOptions.CultureInvariant))
                r.Add(m.Value.ToLowerInvariant());
            return r;
        }

        static readonly HashSet<String> SecretWordSet = new HashSet<String>(StringComparer.Ordinal)
        {
            "token", "tokens", "secret", "secrets", "password", "passwords", "passwd", "passwort", "pwd", "passphrase", "credential", "credentials",
            "apikey", "bearer", "jwt", "otp", "privatekey", "accesskey", "sessionid", "creds", "cred", "passcode", "losenord", "passord", "contrasena", "motdepasse",
        };

        static readonly (String, String)[] SecretPairs =
        {
            ("api", "key"), ("access", "key"), ("private", "key"), ("secret", "key"), ("session", "id"), ("session", "key"), ("auth", "key"), ("client", "secret"),
            ("refresh", "token"), ("auth", "header"), ("license", "key"), ("signing", "key"), ("master", "key"),
        };

        /// <summary>True if the name (an identifier, a member or a storage key) says secret.</summary>
        static bool IsSecretName(String s)
        {
            var w = Words(s);
            for (int i = 0; i < w.Count; ++i)
            {
                if (SecretWordSet.Contains(w[i]))
                    return true;
                if (i + 1 < w.Count)
                    foreach (var (a, b) in SecretPairs)
                        if ((w[i] == a) && (w[i + 1] == b))
                            return true;
            }
            return false;
        }

        /// <summary>The identifiers (also member names) of a token range that say secret, not the names of calls and not inside a translation call.</summary>
        static String SecretIn(Toks t, int a, int b)
        {
            //  The value of a field whose label / type says secret, or a variable made from it (round 4)
            if (SecretValueIn(t, a, b) is String fv)
                return fv;
            for (int k = a; k <= b; ++k)
            {
                //  (the values of a translation's placeholders count: _T("Key: {0}", apiKey, "...") shows it)
                if (!t.Id(k) || t.P(k + 1, "(") || Keywords.Contains(t.Tx(k)))
                    continue;
                //  A property read of a secret's metadata (token.length, key.Expires, hasToken) is not the secret
                if (t.P(k + 1, ".") && t.Id(k + 2) && Regex.IsMatch(t.Tx(k + 2), @"^(?:length|Length|Expires|ExpiresAt|Name|Id|Kind|Type)$"))
                    continue;
                var x = t.Tx(k);
                if (Regex.IsMatch(x, @"^(?:has|is|can|show|hide|no|use|with)[A-Z]") || Regex.IsMatch(x, @"(?:Label|Text|Title|Hint|Name|Url|Field|Box|Input|El|Btn|Button|Length|Count|Expires|Kind|Type)$"))
                    continue;
                if (IsSecretName(x))
                    return x;
            }
            return null;
        }

        /// <summary>Literal texts of a label that say secret, in several languages (labels of drafted fields).</summary>
        static readonly Regex SecretLabel = new Regex(@"(?i)api.?key|secret|token|passw|passphrase|private.?key|access.?key|\bpin\b|credential|\bkey\b|\bpwd?\b|kennwort|zugangs|l[oö]senord|passord|salasana|wachtwoord|mot de passe|contrase[nñ]a|senha|пароль|密码|パスワード|seed|recovery phrase|mnemonic|\biban\b|card number|kartennummer|\bcvv\b|\bcvc\b|connection string|verbindungszeichenfolge", RegexOptions.CultureInvariant);

        static readonly Regex QuerySecret = new Regex(@"[?&;](?:[\w.-]*[_.-])?(?:token|access_token|id_token|refresh_token|auth_?token|api_?key|apikey|secret|client_secret|password|passwd|pwd|session_?id|jwt|otp|credentials?)=$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex EscapeCall = new Regex(@"(?i)esc|encode|sanitiz|sanitis|htmltext|textof|safe|purify|^Number$|^parseInt$|^parseFloat$|^String$|tofixed|^Format|^_T", RegexOptions.CultureInvariant);
        static readonly Regex UnitText = new Regex(@"^\s?(?:%|[KMGTP]i?B\b|B\b|bytes?\b|kb\b|mb\b|gb\b|ms\b|s\b|sec\b|secs\b|seconds?\b|min\b|mins\b|minutes?\b|h\b|hrs?\b|hours?\b|days?\b|€|\$|£|¥|kr\b|(?:EUR|USD|SEK|GBP|NOK|DKK|CHF|JPY)\b)", RegexOptions.CultureInvariant);
        /// <summary>A unit after a computed number variable (round 4: also "h ", "m", "d" of a duration).</summary>
        static readonly Regex UnitTextR4 = new Regex(@"^\s?(?:%|[KMGTP]i?B\b|bytes?\b|ms\b|s\b|sec\b|secs\b|seconds?\b|min\b|mins\b|minutes?\b|m\b|h\b|hrs?\b|hours?\b|d\b|days?\b|w\b|weeks?\b|€|\$|£|kr\b|(?:EUR|USD|SEK|GBP)\b)", RegexOptions.CultureInvariant);

        /// <summary>True if the variable at i was assigned a computed number in its function (Math.floor / round, toFixed, a division or a remainder).</summary>
        static bool NumberMadeBefore(Toks t, Flow flow, int i)
        {
            var name = t.Tx(i);
            var fn = flow.Around(i);
            int from = fn?.F.Start ?? 0;
            for (int k = i - 1; k >= from; --k)
                if (t.Id(k, name) && t.P(k + 1, "=") && !t.P(k + 2, "=") && !t.P(k - 1, "."))
                {
                    var ex = t.Span(k + 2, ExprEnd(t, k + 2));
                    return Regex.IsMatch(ex, @"\bMath\s*\.\s*(?:floor|round|ceil|trunc)\b|\.toFixed\(|/\s*\(?\s*[\d(]|%\s*\(?\s*\d|\bgetTime\(\)\s*-");
                }
            return false;
        }
        static readonly HashSet<String> DateParts = new HashSet<String>(StringComparer.Ordinal)
        {
            "getHours", "getMinutes", "getSeconds", "getFullYear", "getMonth", "getDate", "getDay", "getUTCHours", "getUTCMinutes", "getUTCFullYear", "getUTCMonth", "getUTCDate",
        };
        static readonly HashSet<String> PassiveCalls = new HashSet<String>(StringComparer.Ordinal)
        {
            "preventDefault", "stopPropagation", "stopImmediatePropagation", "setPointerCapture", "releasePointerCapture", "hasPointerCapture", "focus", "blur",
            "getBoundingClientRect", "addEventListener", "removeEventListener", "add", "remove", "toggle", "contains", "closest", "querySelector", "querySelectorAll",
            "getAttribute", "setAttribute", "removeAttribute", "hasAttribute", "toggleAttribute", "matches", "log", "warn", "error", "info", "debug", "trace", "max", "min", "abs", "round",
            "floor", "ceil", "now", "push", "setData", "getData", "includes", "indexOf", "requestAnimationFrame", "cancelAnimationFrame", "setTimeout", "clearTimeout",
            "IsPureEvent", "BadClick", "ModifierClick", "getComputedStyle", "elementFromPoint", "composedPath", "String", "Number", "parseInt", "parseFloat",
            "setProperty", "getPropertyValue", "clearInterval", "startsWith", "endsWith", "trim", "toLowerCase", "toUpperCase", "get", "has", "set", "delete",
        };
        static readonly HashSet<String> PressEvents = new HashSet<String>(StringComparer.Ordinal) { "pointerdown", "mousedown", "touchstart" };
        static readonly HashSet<String> HoverEvents = new HashSet<String>(StringComparer.Ordinal) { "mouseenter", "mouseover", "pointerenter", "pointerover" };
        static readonly HashSet<String> UserEvents = new HashSet<String>(StringComparer.Ordinal)
        {
            "click", "dblclick", "keydown", "keyup", "keypress", "change", "input", "submit", "drop", "pointerdown", "pointerup", "mousedown", "mouseup", "touchstart", "touchend",
            "mouseenter", "mouseover", "pointerenter", "pointerover", "contextmenu", "paste", "blur", "focusout",
        };

        /// <summary>An event handler registration: addEventListener("x", fn), el.onx = fn, { on: { x: fn } }.</summary>
        sealed class Reg
        {
            public String Evt;
            /// <summary>The token to report at.</summary>
            public int At;
            /// <summary>The receiver as written (li, this.List, document), "" if unknown.</summary>
            public String Recv;
            public FnNode Fn;
        }

        static List<Reg> Registrations(Toks t, Flow flow)
        {
            var r = new List<Reg>();
            for (int i = 0; i < t.N; ++i)
            {
                if (t.Id(i, "addEventListener") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Str(i + 2) is String ev) && t.P(i + 3, ","))
                    r.Add(new Reg { Evt = ev, At = i, Recv = Receiver(t, i - 2), Fn = flow.At(i + 4) });
                else if (t.Id(i) && t.Tx(i).StartsWith("on", StringComparison.Ordinal) && (t.Tx(i).Length > 4) && Char.IsLower(t.Tx(i)[2]) && t.P(i - 1, ".") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                    r.Add(new Reg { Evt = t.Tx(i).Substring(2), At = i, Recv = Receiver(t, i - 2), Fn = flow.At(i + 2) });
                else if (t.Id(i, "on") && t.Key(i) && t.P(i + 2, "{") && (t.Match(i + 2) is int oc) && (oc > i))
                {
                    //  Sw.el(spec, { on: { mousedown: fn } }): the receiver is the variable the element is assigned to
                    String recv = "";
                    int open = EnclosingObject(t, i);
                    for (int k = open - 1; k >= Math.Max(0, open - 12); --k)
                        if (t.P(k, "=") && t.Id(k - 1))
                        {
                            recv = Receiver(t, k - 1);
                            break;
                        }
                    for (int k = i + 3; k < oc; ++k)
                    {
                        if (t.P(k, "{") || t.P(k, "(") || t.P(k, "["))
                        {
                            k = t.Match(k);
                            if (k < 0)
                                break;
                            continue;
                        }
                        if ((t.Id(k) || (t.Str(k) != null)) && t.P(k + 1, ":") && (t.P(k - 1, "{") || t.P(k - 1, ",")))
                            r.Add(new Reg { Evt = t.Id(k) ? t.Tx(k) : t.Str(k), At = k, Recv = recv, Fn = flow.At(k + 2) });
                    }
                }
            }
            return r;
        }

        /// <summary>The receiver text ending at token e (x, this.x, a.b.c), "" for a call or index result.</summary>
        static String Receiver(Toks t, int e)
        {
            //  (round 5) a call result: tr.querySelector(".bk-id").addEventListener(...)
            if (t.P(e, ")"))
            {
                int o = -1;
                for (int k = e - 1, d = 0; k >= 0; --k)
                    if (t.P(k, ")"))
                        ++d;
                    else if (t.P(k, "(") && (d-- == 0))
                    {
                        o = k;
                        break;
                    }
                if ((o > 0) && t.Id(o - 1) && Regex.IsMatch(t.Tx(o - 1), "^(?:querySelector|closest|getElementById|item)$"))
                    return Regex.Replace(t.Span(ChainStartOf(t, o - 1), e), @"^this\.", "");
                return "";
            }
            if (!t.Id(e))
                return "";
            int s = ChainStartOf(t, e);
            var x = t.Span(s, e);
            return x.StartsWith("this.", StringComparison.Ordinal) ? x.Substring(5) : x;
        }

        /// <summary>The source of a function and of the local functions / methods it calls (2 calls deep), for "what does it do" questions.</summary>
        static String DeepSpan(Toks t, Flow flow, FnNode n, int depth = 0, HashSet<FnNode> seen = null)
        {
            seen ??= new HashSet<FnNode>();
            if ((n == null) || !seen.Add(n))
                return "";
            var sb = new StringBuilder(t.Span(n.F.Start, n.F.End));
            if (depth < 2)
                for (int j = n.F.Start; j <= n.F.End; ++j)
                    if (t.Id(j) && t.P(j + 1, "(") && (!t.P(j - 1, ".") || t.Id(j - 2, "this")) && (flow.NamedFn(t.Tx(j)) is FnNode g))
                        sb.Append('\n').Append(DeepSpan(t, flow, g, depth + 1, seen));
            return sb.ToString();
        }

        /// <summary>True if a handler does more than visual feedback: a request, a call of the page's own work (a function, a method, a Sw api), navigation.</summary>
        static bool Acts(Toks t, Flow flow, FnNode n, out String what)
        {
            what = null;
            if (n == null)
                return false;
            var e = flow.Of(n);
            if (e.Reqs.Any(r => !r.Chain))
            {
                what = "a request";
                return true;
            }
            foreach (var j in flow.Own(n))
            {
                if (!t.Id(j))
                    continue;
                var name = t.Tx(j);
                if (t.P(j + 1, "(") && !Keywords.Contains(name) && !PassiveCalls.Contains(name) && !TrFunctions.Contains(name))
                {
                    //  e.dataTransfer.x(), console.x(), Math.x(), classList.x(), style.x() are no work
                    if (t.P(j - 1, ".") && Regex.IsMatch(t.Tx(j - 2), @"^(?:console|Math|classList|style|dataTransfer|dataset|JSON|Object|Array|e|ev|evt|event)$"))
                        continue;
                    what = t.Span(ChainStartOf(t, j), j) + "(...)";
                    return true;
                }
                if ((name == "location") && (t.P(j + 1, "=") || (t.P(j + 1, ".") && (t.Id(j + 2, "href") || t.Id(j + 2, "assign") || t.Id(j + 2, "replace")))))
                {
                    what = "navigation";
                    return true;
                }
            }
            //  Inline callbacks (then, forEach) count too
            foreach (var c in n.Children)
                if ((flow.KindOf(c) == CallbackKind.Inline) && Acts(t, flow, c, out what))
                    return true;
            return false;
        }

        /// <summary>True if a request token's rejection is handled: a .catch / two argument .then in its statement, or an await inside a try with a catch that does something.</summary>
        static bool Handled(Toks t, Flow flow, int at)
        {
            var n = flow.Around(at);
            int from = n?.From ?? 0;
            int s = StatementStart(t, from, at);
            int e = ExprEndStatement(t, at);
            var st = t.Span(s, e);
            //  Promise.allSettled(...) settles every request (its results are judged by CheckSilentFlow)
            for (int k = at - 1, d = 0; k >= Math.Max(0, at - 300); --k)
            {
                if (t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                    ++d;
                else if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                {
                    if (d > 0)
                        --d;
                    else if (t.P(k, "(") && t.Id(k - 1, "allSettled"))
                        return true;
                }
            }
            //  A read with a fallback value (a poll, a probe: .catch(() => null)) is handled: the page shows what it has
            if ((t.Id(at, "Get") || t.Id(at, "Cached")) && Regex.IsMatch(st, @"\.catch\s*\(\s*(?:\(\s*\w*\s*\)|\w+)\s*=>\s*(?:null|undefined|false|\[\s*\]|\(\s*\{\s*\}\s*\)|0)\s*\)"))
                return true;
            if (Regex.IsMatch(st, @"\.catch\s*\(") && !Regex.IsMatch(st, @"\.catch\s*\(\s*(?:\(\s*\w*\s*\)|\w+)\s*=>\s*(?:\{\s*\}|null|undefined|void 0)\s*\)|\.catch\s*\(\s*(?:console\.\w+|noop|\(\)\s*=>\s*\{\s*\}|function\s*\(\w*\)\s*\{\s*\})\s*\)"))
                return true;
            //  Inside a try { } catch (e) { ... } with a non empty catch
            for (int k = at; k >= 0; --k)
            {
                if (!t.P(k, "{") || !t.Id(k - 1, "try"))
                    continue;
                int c = t.Match(k);
                if ((c > at) && t.Id(c + 1, "catch"))
                {
                    int b = t.P(c + 2, "(") ? t.Match(c + 2) + 1 : c + 2;
                    if (t.P(b, "{") && (t.Match(b) is int be) && (be > b + 1))
                        return true;
                    //  An empty catch around a value with a fallback: let ok = false; try { ok = await ... } catch { }
                    if (Regex.IsMatch(t.Span(k + 1, c - 1), @"^\s*\w+\s*=\s*(?:await\s+)?[^;]*;?\s*$"))
                        return true;
                    //  A documented empty catch (a comment says why nothing is shown)
                    if (t.P(b, "{") && (t.Match(b) is int be2) && (be2 == b + 1) && Regex.IsMatch(t.T.Substring(t.S[b].End, t.S[be2].Start - t.S[b].End), @"//|/\*"))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True for a developer's assertion rather than a user text: "Invalid format " + format, "Unknown mode: " + m (a text that ends
        /// in a raw value and says the code was called wrong), not "Select a shipment first".
        /// </summary>
        static bool DeveloperError(Toks t, int throwAt, String text) =>
            Regex.IsMatch(text, @"^\s*(?:Invalid|Unknown|Unsupported|Unexpected|Missing|Bad|Illegal|No such|Not implemented|Internal)\b", RegexOptions.IgnoreCase) && t.P(throwAt + 5, "+");

        /// <summary>True if the token is inside the body of a for / while loop of its function.</summary>
        static bool InLoop(Toks t, Flow flow, int at)
        {
            int from = flow.Around(at)?.From ?? 0;
            for (int k = at - 1, d = 0; k >= from; --k)
            {
                if (t.P(k, "}"))
                    ++d;
                else if (t.P(k, "{") && (d-- == 0))
                {
                    if (t.P(k - 1, ")") && IsControlHead(t, k - 1))
                    {
                        int o = k - 1;
                        for (int dd = 0; o >= 0; --o)
                            if (t.P(o, ")"))
                                ++dd;
                            else if (t.P(o, "(") && (--dd == 0))
                                break;
                        if (t.Id(o - 1, "for") || t.Id(o - 1, "while"))
                            return true;
                    }
                    d = 0;
                }
            }
            return false;
        }

        /// <summary>The last token of the statement that contains token i (up to a ";" or a closing bracket at depth 0).</summary>
        static int ExprEndStatement(Toks t, int i)
        {
            int e = i;
            for (int d = 0; e < t.N; ++e)
            {
                if (t.P(e, "(") || t.P(e, "[") || t.P(e, "{") || t.P(e, "${"))
                {
                    int m = t.Match(e);
                    if (m < 0)
                        return t.N - 1;
                    e = m;
                    continue;
                }
                if ((d == 0) && (t.P(e, ";") || t.P(e, ")") || t.P(e, "]") || t.P(e, "}")))
                    break;
                if ((e > i) && (d == 0) && t.P(e, ","))
                    break;
            }
            return Math.Max(i, e - 1);
        }

        /// <summary>The operands of the "+" chain around token i (each a token range), at the depth of i.</summary>
        static List<(int A, int B)> PlusChain(Toks t, int i, int from)
        {
            //  The expression around i: back and forward to a token that ends an expression at this depth
            bool Stop(int k) => t.P(k, ",") || t.P(k, ";") || t.P(k, "=") || t.P(k, "(") || t.P(k, "[") || t.P(k, "{") || t.P(k, "?") || t.P(k, ":") || t.P(k, "=>")
                || t.P(k, "&&") || t.P(k, "||") || t.P(k, "??") || t.P(k, "+=") || t.P(k, "===") || t.P(k, "!==") || t.P(k, "==") || t.P(k, "!=") || t.Id(k, "return");
            int a = i;
            for (int k = i - 1; k >= from; --k)
            {
                if (t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                {
                    int d = 0, q = k;
                    for (; q >= from; --q)
                    {
                        if (t.P(q, ")") || t.P(q, "]") || t.P(q, "}"))
                            ++d;
                        else if ((t.P(q, "(") || t.P(q, "[") || t.P(q, "{") || t.P(q, "${")) && (--d == 0))
                            break;
                    }
                    k = q;
                    a = k;
                    continue;
                }
                if (Stop(k) || (t.T.IndexOf('\n', t.S[k].End, Math.Max(0, t.S[k + 1].Start - t.S[k].End)) >= 0 && !t.P(k, "+") && !t.P(k + 1, "+") && !t.P(k, ".") && !t.P(k + 1, ".")))
                    break;
                a = k;
            }
            int b = i;
            for (int k = i; k < t.N; ++k)
            {
                if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{") || t.P(k, "${"))
                {
                    int m = t.Match(k);
                    if (m < 0)
                        break;
                    k = m;
                    b = k;
                    continue;
                }
                if (Stop(k) || t.P(k, ")") || t.P(k, "]") || t.P(k, "}"))
                    break;
                b = k;
            }
            var ops = new List<(int, int)>();
            int s0 = a;
            for (int k = a; k <= b; ++k)
            {
                if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{") || t.P(k, "${"))
                {
                    int m = t.Match(k);
                    if (m < 0)
                        break;
                    k = m;
                    continue;
                }
                if (t.P(k, "+"))
                {
                    if (k > s0)
                        ops.Add((s0, k - 1));
                    s0 = k + 1;
                }
            }
            if (s0 <= b)
                ops.Add((s0, b));
            return ops;
        }

        static bool IsLiteralRange(Toks t, (int A, int B) o) => (o.A == o.B) && ((t.S[o.A].Kind == JsTokenKind.String) || (t.S[o.A].Kind == JsTokenKind.Template));
        static bool IsTrCall(Toks t, (int A, int B) o) => t.Id(o.A) && TrFunctions.Contains(t.Tx(o.A)) && t.P(o.A + 1, "(") && (t.Match(o.A + 1) == o.B);

        /// <summary>
        /// The family rules of a script (see the section comment). Called after the flow rules (needs the content editable variables).
        /// </summary>
        static void CheckFamilies(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<(int A, int B)> dialogCalls, bool framework, Dictionary<String, int> editable)
        {
            var text = ctx.Text;
            var regs = Registrations(t, flow);
            Secrets = FindSecrets(t, vars, flow);
            Reach = Reachable(t, flow, regs);
            var nothing = NothingOnFailure(t, flow);
            var reportedLines = new HashSet<(String, int)>();
            void Once(Rule rule, int at, String msg)
            {
                if (reportedLines.Add((rule.Id, ctx.Source.GetLineCol(t.S[at].Start).Line)))
                    Add(ctx, rule, t.S[at].Start, msg);
            }

            for (int i = 0; i < t.N; ++i)
            {
                //  ================= security
                //  ---- secrets in storage: localStorage / sessionStorage / Sw.Storage / cookies
                bool storage = ((t.Id(i, "localStorage") || t.Id(i, "sessionStorage")) && (!t.P(i - 1, ".") || t.Id(i - 2, "window")))
                    || ((Secrets != null) && t.Id(i) && Secrets.Storage.Contains(t.Tx(i)) && !t.P(i - 1, ".") && (t.P(i + 1, ".") || t.P(i + 1, "[")));
                if (storage || (t.Chain(i, "Storage", "Local") || t.Chain(i, "Storage", "Session")))
                {
                    String key = null, sec = null;
                    int vA = -1, vB = -1, kA = -1, kB = -1;
                    if (t.P(i + 1, ".") && (t.Id(i + 2, "setItem") || t.Id(i + 2, "Set")) && t.P(i + 3, "(") && (t.Args(i + 3) is var sa) && (sa.Count > 1))
                    {
                        (kA, kB) = sa[0];
                        (vA, vB) = sa[1];
                    }
                    else if (t.P(i + 1, "[") && (t.Match(i + 1) is int bc) && (bc > i) && t.P(bc + 1, "=") && !t.P(bc + 2, "="))
                    {
                        (kA, kB) = (i + 2, bc - 1);
                        (vA, vB) = (bc + 2, ExprEnd(t, bc + 2));
                    }
                    else if (storage && t.P(i + 1, ".") && t.Id(i + 2) && t.P(i + 3, "=") && !t.P(i + 4, "="))
                    {
                        key = t.Tx(i + 2);
                        (vA, vB) = (i + 4, ExprEnd(t, i + 4));
                    }
                    if (kA >= 0)
                        key = Literals(t, (kA, kB));
                    if ((key != null) || (kA >= 0))
                    {
                        if ((key != null) && IsSecretName(key))
                            sec = key;
                        else if (kA >= 0 && SecretIn(t, kA, kB) is String ks)
                            sec = ks;
                        else if (vA >= 0 && SecretIn(t, vA, vB) is String vs)
                            sec = vs;
                        if (sec != null)
                            Once(Rule.UxSec1, i, "a secret (\"" + Short(sec) + "\") stored in " + t.Tx(i) + ": local storage is plain text that every script of the site and anyone at the device can read, and it stays after sign out; keep keys and tokens on the server (the session) and ask for them again (a password field with Show), never store them in the browser");
                    }
                }
                if (t.Id(i, "cookie") && t.P(i - 1, ".") && t.Id(i - 2, "document") && t.P(i + 1, "=") && !t.P(i + 2, "="))
                {
                    int e = ExprEnd(t, i + 2);
                    var lit = Literals(t, (i + 2, e));
                    var m = Regex.Match(lit, @"([\w.-]+)\s*=");
                    var sec = (m.Success && IsSecretName(m.Groups[1].Value)) ? m.Groups[1].Value : SecretIn(t, i + 2, e);
                    if (sec != null)
                        Once(Rule.UxSec1, i, "a secret (\"" + Short(sec) + "\") written to document.cookie from script: readable by every script of the site and sent with every request; the server sets the session cookie (HttpOnly), page code never stores keys or tokens");
                }
                //  ---- secrets in url queries: "...?token=" + value, `...&key=${value}`, URLSearchParams set / append / an object with a secret key
                if (((t.S[i].Kind == JsTokenKind.String) && (t.P(i + 1, "+"))) || (t.S[i].Kind == JsTokenKind.TemplateHead) || (t.S[i].Kind == JsTokenKind.TemplateMiddle))
                {
                    var raw = t.S[i].Kind == JsTokenKind.String ? Literals(t, (i, i)) : t.Tx(i).TrimEnd('{').TrimEnd('$');
                    //  The parameter's name says secret, or the value put after any "?x=" / "&x=" does (session.SessionId, apiToken)
                    var vs = t.S[i].Kind == JsTokenKind.String ? (i + 2, ExprEnd(t, i + 2)) : (i + 1, ExprEnd(t, i + 1));
                    var qv = Regex.IsMatch(raw, @"[?&;][\w.-]+=$") ? SecretIn(t, vs.Item1, Math.Min(vs.Item2, t.N - 1)) : null;
                    if (QuerySecret.IsMatch(raw) || (qv != null))
                        Once(Rule.UxSec1, i, "a secret in a url query (\"" + Short(QuerySecret.IsMatch(raw) ? QuerySecret.Match(raw).Value : Regex.Match(raw, @"[?&;][\w.-]+=$").Value + qv) + "\"): urls end up in the server log, the browser history, proxies and the Referer of the next page; send the secret in the request body (Sw.Net.Send(url, { Token })) or let the session carry it");
                }
                if ((t.Id(i, "set") || t.Id(i, "append")) && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Str(i + 2) is String qk) && IsSecretName(qk) && (Regex.IsMatch(t.Span(Math.Max(0, i - 6), i), @"(?i)param|query|search|url") || ((Secrets != null) && Secrets.Query.Contains(t.Tx(i - 2)))))
                    Once(Rule.UxSec1, i, "a secret (\"" + qk + "\") put in a url query (URLSearchParams): urls end up in the server log, the history and the Referer; send it in the request body or let the session carry it");
                //  new URLSearchParams({ token: t }) / ({ id, apiKey })
                if (t.Id(i, "URLSearchParams") && t.Id(i - 1, "new") && t.P(i + 1, "(") && t.P(i + 2, "{") && (t.Match(i + 2) is int uo) && (uo > i))
                    for (int k = i + 3; k < uo; ++k)
                        if (t.Id(k) && (t.P(k - 1, "{") || t.P(k - 1, ",")) && (t.P(k + 1, ":") || t.P(k + 1, ",") || t.P(k + 1, "}")) && (IsSecretName(t.Tx(k)) || (t.P(k + 1, ":") && (SecretIn(t, k + 2, ExprEnd(t, k + 2)) != null))))
                        {
                            Once(Rule.UxSec1, i, "a secret (\"" + t.Tx(k) + "\") put in a url query (URLSearchParams): urls end up in the server log, the history and the Referer; send it in the request body or let the session carry it");
                            break;
                        }
                //  ---- secrets in logs: console.log / info / debug / warn / error / trace with a secret value, also through a local alias
                //  (console.x(...), and any call named like a log: U.trace(...), log(...), Logger.debug(...))
                if (t.Id(i) && Regex.IsMatch(t.Tx(i), @"^(?:log|info|debug|warn|error|trace|dir|table|print|dump|logError|logInfo|logDebug)$", RegexOptions.IgnoreCase) && t.P(i + 1, "(")
                    && (t.Id(i - 2, "console") || Regex.IsMatch(t.Tx(i), @"^(?:trace|log|debug|dump|print|logError|logInfo|logDebug)$", RegexOptions.IgnoreCase)) && !(t.Id(i - 1, "function") || IsDeclaration(t, i)))
                {
                    int close = t.Match(i + 1);
                    if ((close > i) && (SecretIn(t, i + 2, close - 1) is String sec))
                        Once(Rule.UxSec1, i, "a secret (\"" + Short(sec) + "\") written to the console / a log: the console is kept in support logs, screen shares and bug reports; log that it happened, never the value");
                }

                //  ---- SEC-2: html sinks with a value that isn't a literal or escaped
                int sinkA = -1, sinkB = -1;
                String sink = null;
                if ((t.Id(i, "innerHTML") || t.Id(i, "outerHTML")) && t.P(i - 1, ".") && (t.P(i + 1, "=") || t.P(i + 1, "+=")) && !t.P(i + 2, "="))
                {
                    sink = t.Tx(i);
                    sinkA = i + 2;
                    sinkB = ExprEnd(t, i + 2);
                }
                else if ((t.Id(i, "insertAdjacentHTML") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var ia) && (ia.Count > 1)))
                {
                    sink = "insertAdjacentHTML";
                    (sinkA, sinkB) = ia[1];
                }
                else if ((t.Chain(i, "document", "write") || t.Chain(i, "document", "writeln")) && t.P(i + 1, "(") && (t.Args(i + 1) is var wa) && (wa.Count > 0))
                {
                    sink = "document.write";
                    (sinkA, sinkB) = wa[0];
                }
                else if ((t.Id(i, "html") || t.Id(i, "Html") || t.Id(i, "innerHTML")) && t.Key(i) && (EnclosingObject(t, i) is int ho) && (ho > 0) && (IsSwOptions(t, ho) || Regex.IsMatch(t.Span(Math.Max(0, ho - 6), ho), @"\bel\s*\(")))
                {
                    sink = "the " + t.Tx(i) + " option";
                    sinkA = i + 2;
                    sinkB = ExprEnd(t, i + 2);
                }
                //  (not a helper that sets html only when its caller says it is html: if (isHtml) e.innerHTML = text)
                if ((sink != null) && (sinkB >= sinkA) && !Regex.IsMatch(t.Span(Math.Max(0, StatementStart(t, flow.Around(i)?.From ?? 0, i) - 8), i), @"(?i)\bif\s*\(\s*!?\s*\w*html\w*\s*\)") && (UnsafeHtml(t, flow, sinkA, sinkB, 0) is String bad))
                    Once(Rule.UxSec2, i, sink + " with \"" + Short(bad) + "\", which is not escaped: user data (a name, a note, a message) is inserted as markup, so <img src=x onerror=...> runs as code in the user's session; build the elements and set the data as text (Sw.el(\"span\", { text: item.Name }), textContent), or escape it");

                //  ================= formatting
                //  ---- toLocaleString / toLocaleDateString / toLocaleTimeString, Intl formats
                //  (a fixed locale: "en-US", ["en-GB"]; the viewer's own locale, [] / undefined, is the guide's "viewer's local time")
                bool FixedLocale(int open) => (t.Str(open + 1) is String loc && Regex.IsMatch(loc, @"^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")) || (t.P(open + 1, "[") && (t.Str(open + 2) is String l2) && Regex.IsMatch(l2, @"^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$"));
                if ((t.Id(i, "toLocaleString") || t.Id(i, "toLocaleDateString") || t.Id(i, "toLocaleTimeString")) && t.P(i - 1, ".") && t.P(i + 1, "(") && FixedLocale(i + 1))
                    Once(Rule.UxTxt20, i, t.Tx(i) + "(" + Short(t.Span(i + 2, Math.Max(i + 1, t.Match(i + 1) - 1))) + "): a fixed locale, not the app's language and the user's settings (an American date for everyone); use Sw.Format.TimeStamp(date) (dates and times: [tool tip, text], the viewer's local time), Sw.Format.Number(n) or Sw.Format.Bytes(n)");
                if (t.Id(i, "Intl") && t.P(i + 1, ".") && t.Id(i + 2) && Regex.IsMatch(t.Tx(i + 2), "^(?:NumberFormat|DateTimeFormat)$") && t.P(i + 3, "("))
                    Once(Rule.UxTxt20, i, "Intl." + t.Tx(i + 2) + "(" + Short(t.Span(i + 4, Math.Max(i + 3, t.Match(i + 3) - 1))) + "): numbers and dates of page code go through Sw.Format (Number, Amount, Bytes, DurationMs, TimeStamp, TimeSpan), so separators, units and times are the same on every page and follow the user's settings");
                //  ---- a number with a unit by hand: toFixed / Math.round / a division by 1024 / * 100 next to " KB", "%", " ms" ...
                if (((t.Id(i, "toFixed") || t.Id(i, "toPrecision")) && t.P(i - 1, ".") && t.P(i + 1, "("))
                    || (t.Chain(i, "Math", "round") || t.Chain(i, "Math", "floor") || t.Chain(i, "Math", "ceil"))
                    || ((t.S[i].Kind == JsTokenKind.Number) && Regex.IsMatch(t.Tx(i), @"^(?:1024|1048576|1073741824|1e3|1000|60000|3600000|86400000)$") && t.P(i - 1, "/")))
                {
                    var fn = flow.Around(i);
                    var ops = PlusChain(t, i, fn?.From ?? 0);
                    var unit = ops.Select(o => IsLiteralRange(t, o) ? Literals(t, o) : null).FirstOrDefault(x => (x != null) && UnitText.IsMatch(x));
                    bool inTemplate = false;
                    if (unit == null)
                    {
                        //  `${(n / 1024).toFixed(1)} KB` / `${Math.round(p * 100)}%`
                        for (int k = i; k >= Math.Max(0, i - 20); --k)
                            if (t.S[k].Kind == JsTokenKind.TemplateHead || t.S[k].Kind == JsTokenKind.TemplateMiddle)
                            {
                                int m = k + 1;
                                for (int d = 0; m < t.N; ++m)
                                {
                                    if (t.P(m, "(") || t.P(m, "[") || t.P(m, "{"))
                                        ++d;
                                    else if (t.P(m, ")") || t.P(m, "]") || t.P(m, "}"))
                                        --d;
                                    else if ((d == 0) && ((t.S[m].Kind == JsTokenKind.TemplateMiddle) || (t.S[m].Kind == JsTokenKind.TemplateTail)))
                                        break;
                                }
                                if ((m > i) && (m < t.N) && Regex.IsMatch(t.Tx(m).TrimStart('}'), @"^\s?(?:%|[KMGT]i?B\b|B\b|bytes?\b|ms\b|s\b|sec\b|min\b|h\b|days?\b)"))
                                {
                                    inTemplate = true;
                                    unit = t.Tx(m).TrimStart('}').Split(' ', '`').FirstOrDefault(x => x.Length > 0) ?? "";
                                }
                                break;
                            }
                            else if (t.P(k, ";") || t.P(k, "{"))
                                break;
                    }
                    bool numeric = t.Id(i, "toFixed") || t.Id(i, "toPrecision") || ops.Any(o => Regex.IsMatch(t.Span(o.A, o.B), @"\.toFixed\(|\bMath\.(?:round|floor|ceil)\(|/\s*(?:1024|1048576|1000)\b|\*\s*100\b"));
                    //  (a style value: el.style.width = Math.round(p) + "%", setProperty, { width: ... } is no text)
                    var stmt = t.Span(StatementStart(t, fn?.From ?? 0, i), ExprEndStatement(t, i));
                    if (Regex.IsMatch(stmt, @"\bstyle\b|setProperty|\b(?:width|height|left|top|right|bottom|flexBasis|transform)\s*[:=]"))
                        unit = null;
                    if ((unit != null) && numeric)
                        Once(Rule.UxTxt20, i, "a number with the unit \"" + Short(unit.Trim()) + "\" formatted by hand" + (inTemplate ? " (in a template)" : "") + ": the decimal separator, the digit grouping and the unit words don't follow the app's language; use Sw.Format.Bytes(n) (sizes), Sw.Format.Number(n, decimals) + a translated unit in one _T(\"{0} %\", ...) sentence, Sw.Format.DurationMs(ms) (durations)");
                }
                //  ---- dates by parts: getHours() + ":" + getMinutes()
                if (t.Id(i) && DateParts.Contains(t.Tx(i)) && t.P(i - 1, ".") && t.P(i + 1, "(") && t.P(i + 2, ")"))
                {
                    var fn = flow.Around(i);
                    var ops = PlusChain(t, i, fn?.From ?? 0);
                    //  (":" "/" "." are the shown separators; "-" in a file name stamp is no text)
                    bool sep = ops.Any(o => IsLiteralRange(t, o) && Regex.IsMatch(Literals(t, o), @"^\s*[:/.]\s*$"));
                    //  Round 4: also around a wrapper (String(d.getHours()).padStart(2, "0") + ":" ...) and joined parts ([d, m, y].join("."))
                    int dss = StatementStart(t, fn?.From ?? 0, i);
                    var dst = t.Span(dss, Math.Max(ExprEndStatement(t, dss), ExprEndStatement(t, i)));
                    if (!sep && (Regex.IsMatch(dst, @"[""']\s*[:/.]\s*[""']") || Regex.IsMatch(dst, @"\.join\(\s*[""'][:/.]\s*[""']\s*\)")) && !Regex.IsMatch(dst, @"(?i)\b(?:file|stamp|name)\w*\s*=|\.(?:csv|txt|json|log|zip|png)\b"))
                        sep = true;
                    if (!sep)
                        for (int k = i; (k < t.N) && (k < i + 12) && !sep; ++k)
                            if ((t.S[k].Kind == JsTokenKind.TemplateMiddle) && Regex.IsMatch(t.Tx(k), @"^\}\s*[:/.]\s*\$\{$"))
                                sep = true;
                    if (sep)
                        Once(Rule.UxTxt20, i, "a date or time built from " + t.Tx(i) + "() and separators: the order, the separators and the 12 / 24 hour clock don't follow the user's settings, and the time zone isn't named; use Sw.Format.TimeStamp(date) ([tool tip, text], the viewer's local time)");
                }
                //  ---- round 4: a UTC date cut from an ISO string, an English date string, a number made by hand next to a unit
                if (t.Id(i, "toISOString") && t.P(i - 1, ".") && t.P(i + 1, "(") && t.P(i + 2, ")") && t.P(i + 3, ".") && Regex.IsMatch(t.Tx(i + 4), "^(?:slice|substring|substr|split|replace)$"))
                {
                    var fn = flow.Around(i);
                    var st = t.Span(StatementStart(t, fn?.From ?? 0, i), ExprEndStatement(t, i));
                    bool payload = Regex.IsMatch(st, @"\b(?!(?:text|Text|title|Title|label|Label|html|Html|placeholder|Placeholder|Description|Message)\b)\w+\s*:\s*(?:new\s+)?[^:;?]*toISOString") || Regex.IsMatch(st, @"(?i)\b(?:file|stamp|name|key|id|url|path)\w*\s*=|\.(?:csv|txt|json|log|zip|png|pdf)\b|\bNet\s*\.|\bfetch\s*\(|Download|[?&]\w+=|\bUTC\b");
                    if (!payload)
                        Once(Rule.UxTxt20, i, "a date cut from toISOString(): it is the UTC day (after 22:00 in Stockholm it shows tomorrow) in the ISO order, whatever the user's language; use Sw.Format.TimeStamp(date) (the viewer's local time, [tool tip, text])");
                }
                if ((t.Id(i, "toUTCString") || t.Id(i, "toDateString") || t.Id(i, "toTimeString") || t.Id(i, "toGMTString")) && t.P(i - 1, ".") && t.P(i + 1, "(") && t.P(i + 2, ")")
                    && !Regex.IsMatch(t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), ExprEndStatement(t, StatementStart(t, flow.Around(i)?.From ?? 0, i))), @"(?i)expires|cookie|header|If-Modified|Last-Modified|toDateString\(\)\s*[!=]==?|[!=]==?[^;]*toDateString\(\)")
                    //  (a comparison of two days is no text: a.toDateString() === b.toDateString())
                    && !(t.P(i + 3, "===") || t.P(i + 3, "==") || t.P(i + 3, "!==") || t.P(i + 3, "!=") || Regex.IsMatch(t.Tx(ChainStartOf(t, i) - (t.Id(ChainStartOf(t, i) - 1, "new") ? 2 : 1)), @"^[!=]==?$")))
                    Once(Rule.UxTxt20, i, t.Tx(i) + "(): an English date text (\"Mon Oct 20 2026\") in a fixed format for every language; use Sw.Format.TimeStamp(date) (dates and times in the user's language and local time)");
                //  A variable made by number arithmetic (Math.floor(ms / 60000), x.toFixed(1)) next to a unit: h + "h " + m + "m", `${mins} min`
                if ((t.S[i].Kind == JsTokenKind.String) && t.P(i - 1, "+") && t.Id(i - 2) && !t.P(i - 3, ".") && UnitTextR4.IsMatch(Literals(t, (i, i))) && NumberMadeBefore(t, flow, i - 2))
                    Once(Rule.UxTxt20, i, "a number with the unit \"" + Short(Literals(t, (i, i)).Trim()) + "\" formatted by hand (" + t.Tx(i - 2) + " is computed): units, separators and the word order don't follow the app's language; use Sw.Format.DurationMs(ms) (durations), Sw.Format.Bytes(n), or Sw.Format.Number(n) in one _T(\"{0} min\", ...) sentence");
                if (((t.S[i].Kind == JsTokenKind.TemplateMiddle) || (t.S[i].Kind == JsTokenKind.TemplateTail)) && t.Id(i - 1) && ((t.S[i - 2].Kind == JsTokenKind.TemplateHead) || (t.S[i - 2].Kind == JsTokenKind.TemplateMiddle)) && UnitTextR4.IsMatch(t.Tx(i).TrimStart('}')) && NumberMadeBefore(t, flow, i - 1))
                    Once(Rule.UxTxt20, i, "a number with the unit \"" + Short(t.Tx(i).TrimStart('}').Trim('`', '$', '{').Trim()) + "\" formatted by hand in a template (" + t.Tx(i - 1) + " is computed): use Sw.Format.DurationMs / Bytes / Number in one translated sentence");
                //  ---- TXT-6 (round 4): [_TF("Removed"), n, _TF("items")].join(" ")
                if (t.Id(i, "join") && t.P(i - 1, ".") && t.P(i - 2, "]") && t.P(i + 1, "(") && (t.Str(i + 2) is String js) && Regex.IsMatch(js, @"^[ \t]+$|^,\s$"))
                {
                    int ao = -1;
                    for (int k = i - 2, d = 0; k >= 0; --k)
                    {
                        if (t.P(k, "]") || t.P(k, ")") || t.P(k, "}"))
                            ++d;
                        else if ((t.P(k, "[") || t.P(k, "(") || t.P(k, "{")) && (--d == 0))
                        {
                            ao = k;
                            break;
                        }
                    }
                    if ((ao >= 0) && t.P(ao, "["))
                    {
                        var els = new List<(int A, int B)>();
                        int s0 = ao + 1;
                        for (int k = ao + 1; k < i - 2; ++k)
                        {
                            if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                            {
                                k = Math.Max(k, t.Match(k));
                                continue;
                            }
                            if (t.P(k, ","))
                            {
                                els.Add((s0, k - 1));
                                s0 = k + 1;
                            }
                        }
                        if (s0 <= i - 3)
                            els.Add((s0, i - 3));
                        if (els.Any(x => IsTrCall(t, x)) && els.Any(x => !IsTrCall(t, x) && !IsLiteralRange(t, x)))
                            Once(Rule.UxTxt6, i, "a sentence joined from translated pieces and values ([_TF(...), n, ...].join(\" \")): the word order differs between languages; write one sentence with placeholders: _T(\"Removed {0} items\", n, \"context\")");
                    }
                }
                //  ---- TXT-6: a translated text concatenated with a value
                if (t.Id(i) && TrFunctions.Contains(t.Tx(i)) && t.P(i + 1, "(") && !t.P(i - 1, ".") && (t.Match(i + 1) is int tc) && (tc > i))
                {
                    var fn = flow.Around(i);
                    var ops = PlusChain(t, i, fn?.From ?? 0);
                    if (ops.Count > 1)
                    {
                        int me = ops.FindIndex(o => o.A == i);
                        bool value = false, glue = false;
                        for (int k = 0; k < ops.Count; ++k)
                        {
                            var o = ops[k];
                            if (IsTrCall(t, o))
                                continue;
                            //  The glue counts next to this translation only (a label elsewhere in the chain is another piece)
                            if (IsLiteralRange(t, o) && (Math.Abs(k - me) != 1))
                                continue;
                            if (IsLiteralRange(t, o))
                            {
                                var l = Literals(t, o);
                                //  a space or a word between the pieces (" ", " items", "s"); not ": ", " (", ")", nor a line break (lines of a tool tip)
                                if (Regex.IsMatch(l, @"^[ \t]+$|\p{L}{1,}") && !Regex.IsMatch(l, @"^\s*[:(\[)\]•·,;|/-]+\s*$") && (l.IndexOf('\n') < 0))
                                    glue = true;
                                continue;
                            }
                            value = true;
                        }
                        var own = t.Str(i + 2) ?? "";
                        //  A whole sentence (or a label) next to a value is fine: "Failed to save." + " " + reason, "Size" + ": " + n
                        bool sentence = Regex.IsMatch(own.TrimEnd(), @"[.!?:;…)]$");
                        bool fragment = (own.EndsWith(" ", StringComparison.Ordinal) || own.StartsWith(" ", StringComparison.Ordinal)) && value;
                        if (value && !sentence && (glue || fragment) && (me >= 0))
                            Once(Rule.UxTxt6, i, "the translated text \"" + Short(own) + "\" is concatenated with values into a sentence: the word order (and the plural) differs between languages, and translators see only the pieces; write one sentence with placeholders: _T(\"Removed {0} items\", n, \"context\")");
                    }
                    //  `${_TF("x")} ${n}`: a template with a translated piece and a value
                    if (t.S[i - 1].Kind == JsTokenKind.TemplateHead || t.S[i - 1].Kind == JsTokenKind.TemplateMiddle)
                    {
                        int k = i - 1;
                        while ((k > 0) && (t.S[k].Kind != JsTokenKind.TemplateHead))
                            --k;
                        int end = k;
                        for (int d = 0; end < t.N; ++end)
                            if (t.S[end].Kind == JsTokenKind.TemplateHead && end != k)
                                ++d;
                            else if (t.S[end].Kind == JsTokenKind.TemplateTail && (d-- == 0))
                                break;
                        bool otherValue = false;
                        for (int q = k + 1; q < end; ++q)
                            if ((t.S[q - 1].Kind == JsTokenKind.TemplateHead || t.S[q - 1].Kind == JsTokenKind.TemplateMiddle) && (q != i) && !(t.Id(q) && TrFunctions.Contains(t.Tx(q))))
                                otherValue = true;
                        if (otherValue && !Regex.IsMatch((t.Str(i + 2) ?? "").TrimEnd(), @"[.!?:;…)]$") && !Regex.IsMatch(t.Span(k, end), @"\\n|\n"))
                            Once(Rule.UxTxt6, i, "a template string that puts a translated piece (" + t.Tx(i) + "(\"" + Short(t.Str(i + 2) ?? "") + "\")) next to values: the word order differs between languages; write one sentence with placeholders: _T(\"Removed {0} items\", n, \"context\")");
                    }
                }
                //  ---- a plural by adding "s": (n === 1 ? "" : "s")
                if ((t.Str(i) == "s") && (t.P(i - 1, ":") || t.P(i + 1, ":")) && (t.Str(i - 2) == "" || t.Str(i + 2) == "") && Regex.IsMatch(t.Span(Math.Max(0, i - 12), i), @"[=!]==?\s*1\b|>\s*1\b|\b1\s*[=!]==?"))
                    Once(Rule.UxTxt6, i, "a plural made by adding \"s\": other languages don't add an \"s\" (or have more forms); write a sentence that works for one and many (\"Files: {0}\") or two translated forms");

                //  ================= untranslated: throw new Error("...") in a handler of a Sw component (shown to the user as the reason)
                if (t.Id(i, "throw") && t.Id(i + 1, "new") && t.Id(i + 2) && t.Tx(i + 2).EndsWith("Error", StringComparison.Ordinal) && t.P(i + 3, "(") && (t.Str(i + 4) is String et) && TextChecker.IsVisibleText(et, false) && Regex.IsMatch(et, @"\p{L}{2,}\s+\p{L}{2,}")
                    && (InSwHandler(t, flow, i) || CaughtAndShown(t, i) || (InReach(flow, i) && !DeveloperError(t, i, et))) && !ctx.HasMarker(t.S[i].Start, TextChecker.NoTr) && !TextChecker.IsExempt(ctx.WebRel, text))
                    ctx.Add(Rule.TrUntranslated, t.S[i + 4].Start, "the error text \"" + Short(et) + "\" is shown to the user (the framework says \"<name>: not done. <reason>\") but is not translated: throw new Error(_T(\"" + Short(et).Replace("\"", "'") + "\", \"context\")) (PORTING.md A9; a text that must stay as it is gets a \"" + TextChecker.NoTr + " (why)\" comment)");

                //  ================= silent failures
                //  ---- an empty catch, or one that only logs, around work that can fail (a request, an await)
                if (t.Id(i, "catch") && !t.P(i - 1, ".") && t.P(i - 1, "}"))
                {
                    int b = t.P(i + 1, "(") ? t.Match(i + 1) + 1 : i + 1;
                    if (t.P(b, "{") && (t.Match(b) is int be) && (be > b))
                    {
                        //  The try block
                        int tcl = i - 1, to = -1;
                        for (int q = tcl - 1, d = 0; q >= 0; --q)
                        {
                            if (t.P(q, "}"))
                                ++d;
                            else if (t.P(q, "{") && (d-- == 0))
                            {
                                to = q;
                                break;
                            }
                        }
                        //  A request of the user's action (a handler of a Sw control, a dialog button, an event listener) inside the try;
                        //  a best effort read with a fallback in a helper (an optional tile, a probe) is left alone
                        //  (also a call of a local function or method that makes a request)
                        bool tryWork = (to > 0) && t.Id(to - 1, "try") && Enumerable.Range(to, tcl - to).Any(q => IsRequest(t, q)
                            || (t.Id(q) && t.P(q + 1, "(") && !Keywords.Contains(t.Tx(q)) && (!t.P(q - 1, ".") || t.Id(q - 2, "this")) && (flow.NamedFn(t.Tx(q)) is FnNode tg) && (tg.F.Start > tcl || tg.F.End < to) && flow.Of(tg).Reqs.Any(r => !r.Chain)));
                        var body = t.Span(b + 1, be - 1);
                        var errVar = t.P(i + 1, "(") && t.Id(i + 2) ? t.Tx(i + 2) : null;
                        //  Round 4: swallowed = says nothing (no message, report, rethrow or hand-over of the error; UI resets and logs don't count),
                        //  in the work of any user action (the handler and every function it calls)
                        bool quiet = SaysNothing(t, b, be, errVar);
                        bool singleAssign = (to > 0) && Regex.IsMatch(t.Span(to + 1, tcl - 1), @"^\s*\w+\s*=\s*(?:!!\s*\(?\s*)?(?:await\s+)?[^;]*;?\s*$");
                        //  A deliberate choice written down: an empty catch with a comment ("no theme menu when signed out: the built in list")
                        bool documented = (be == b + 1) && Regex.IsMatch(text.Substring(t.S[b].End, t.S[be].Start - t.S[b].End), @"//|/\*");
                        //  Best effort per item: a catch that only warns inside a loop (one failed temp file doesn't stop the others)
                        bool perItem = Regex.IsMatch(body, @"^\s*(?:console\.(?:warn|info|debug|log)\([^;]*\)\s*;?\s*)+$") && InLoop(t, flow, to);
                        //  A write (not a read with a fallback) whose failure only logs is silent wherever it runs (a dialog opened from another file)
                        bool writeTry = (to > 0) && Enumerable.Range(to, tcl - to).Any(q => IsRequest(t, q)) && !OnlyReadsIn(t, to, tcl);
                        //  (a read with a fallback in a helper the action calls is best effort: only the handler itself, or a write, counts)
                        if (tryWork && quiet && !singleAssign && !documented && !perItem && (InUserHandler(t, flow, regs, i) || writeTry || (InReach(flow, i) && !OnlyReadsIn(t, to, tcl))))
                            Once(Rule.UxFbk5, i, "a catch that says nothing (" + (be == b + 1 ? "empty" : Short(Regex.Replace(body.Trim(), @"\s+", " "))) + ") around work that a user action runs: the user never learns that it failed (a button then even says \"done\"); show it (Sw.Fail(e), the field's or the row's error, a status text), or let it reach the caller (no catch: Sw.Button and Sw.Work show \"<name>: not done\" with the reason)");
                        bool swallowed = (be == b + 1) || Regex.IsMatch(body, @"^\s*(?:console\.\w+\([^;]*\)\s*;?\s*)+$");
                        //  Anywhere: an empty catch or one that logs an error (console.error: the author knows it is a failure) around a
                        //  request, unless the try only fills a value declared before it (a fallback: let x = null; try { x = await ... } catch { })
                        bool errorOnly = Regex.IsMatch(body, @"^\s*(?:console\.error\([^;]*\)\s*;?\s*)+$");
                        bool fallback = (to > 0) && Regex.IsMatch(t.Span(to + 1, tcl - 1), @"^\s*\w+\s*=\s*(?:!!\s*\(?\s*)?(?:await\s+)?[\s\S]*;?\s*$") && !Regex.IsMatch(t.Span(to + 1, tcl - 1), @";\s*\S");
                        if (tryWork && swallowed && ((InUserHandler(t, flow, regs, i) && !(fallback && (be == b + 1))) || (errorOnly && !(fallback && (be == b + 1)))))
                            Once(Rule.UxFbk5, i, "a catch that " + (be == b + 1 ? "is empty" : "only writes to the console") + " around work that can fail: the user never learns that it failed (an OnClick then even says \"done\"); show it (Sw.Fail(e), the field's or the row's error), or let it reach the caller (no catch: Sw.Button and Sw.Work show \"<name>: not done\" with the reason)");
                    }
                }
                //  ---- .catch(() => {}) / .catch(console.error) / .catch(noop)
                if (t.Id(i, "catch") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Match(i + 1) is int cc) && (cc > i))
                {
                    var arg = t.Span(i + 2, cc - 1);
                    //  On a request chain (Sw.Net.Send(...).catch(...), fetch(...).then(...).catch(...)); a prefetch of a helper object is its own business
                    int cs0 = StatementStart(t, flow.Around(i)?.From ?? 0, i);
                    bool onRequest = Enumerable.Range(cs0, Math.Max(0, i - cs0)).Any(q => IsRequest(t, q));
                    if (onRequest && Regex.IsMatch(arg, @"^\s*(?:(?:\(\s*\w*\s*\)|\w+)\s*=>\s*(?:\{\s*\}|\{\s*console\.\w+\([^;]*\)\s*;?\s*\}|console\.\w+\([^)]*\))|console\.\w+|noop|\(\)\s*=>\s*\{\s*\}|function\s*\(\w*\)\s*\{\s*\})\s*$"))
                        Once(Rule.UxFbk5, i, ".catch(" + Short(arg) + ") swallows the failure: the user never learns that it failed; show it (Sw.Fail(e) / the place of the action), or return the promise to the caller (Sw.Button / Sw.Work show \"<name>: not done\")");
                    //  Round 4: .catch(() => null / false / undefined / []) in the work of a user action (the failure becomes "nothing" and the button says done)
                    else if (onRequest && SwallowCallback.IsMatch(arg) && InReach(flow, i) && !Enumerable.Range(cs0, Math.Max(0, i - cs0)).Any(q => t.Id(q, "Get") && IsRequest(t, q)) && !((flow.Around(i)?.Name is String hn) && nothing.ContainsKey(hn)))
                        Once(Rule.UxFbk5, i, ".catch(" + Short(arg) + ") in the work of a user action turns the failure into \"nothing\": the action goes on as if it worked (the button says done); show it (Sw.Fail(e)) or let it reach the button (no catch)");
                }
                //  ---- .then(ok, () => {}): the rejection handler swallows
                if (t.Id(i, "then") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var tha) && (tha.Count == 2) && SwallowCallback.IsMatch(t.Span(tha[1].A, tha[1].B)))
                {
                    int cs0 = StatementStart(t, flow.Around(i)?.From ?? 0, i);
                    if (Enumerable.Range(cs0, Math.Max(0, i - cs0)).Any(q => IsRequest(t, q) && !t.Id(q, "Get") && !t.Id(q, "Cached")))
                        Once(Rule.UxFbk5, i, ".then(..., " + Short(t.Span(tha[1].A, tha[1].B)) + ") swallows the failure in its rejection handler: the user never learns that it failed; show it (e => Sw.Fail(e))");
                }

                //  ================= FRM-3: an error set while typing (OnInput of a Sw control)
                if (t.Id(i, "OnInput") && t.Key(i) && (EnclosingObject(t, i) is int io) && (io > 0) && IsSwOptions(t, io) && (flow.At(i + 2) is FnNode inp))
                {
                    foreach (var j in flow.Own(inp))
                        if (t.Id(j, "SetError") && t.P(j - 1, ".") && t.P(j + 1, "(") && (t.Args(j + 1) is var ea) && (ea.Count > 0) && !t.Is(ea[0], "null") && !t.Is(ea[0], "undefined") && !t.Is(ea[0], "false") && (t.Str(ea[0].A) != ""))
                        {
                            Once(Rule.UxFrm3, j, "SetError(...) in OnInput shows the error while the user is still typing (\"Enter a valid e-mail address\" after the first letter); validate when the field is left or on submit (OnChange, Sw.Form Validate), and only clear the error while typing");
                            break;
                        }
                }

                //  ================= Sw API misuse (round 3 patterns)
                //  ---- Sw.Draft.Attach on a content editable element (only inputs, textareas and Sw.TextBox are drafted)
                if (t.Chain(i, "Draft", "Attach") && t.P(i + 1, "(") && (t.Args(i + 1) is var da) && (da.Count > 0) && t.Id(da[0].A) && (da[0].A == da[0].B) && editable.ContainsKey(t.Tx(da[0].A)))
                    Once(Rule.UxSaf11, i, "Sw.Draft.Attach(" + t.Tx(da[0].A) + ") on a content editable element: drafts keep inputs, text areas and Sw.TextBox only, so nothing is kept although the handle says Active; use a Sw.TextBox({ Multiline, Draft }) for the text, or keep the editor's text yourself on input (Sw.Page.SetDirty)");
                //  ---- Sw.Work.Report twice for one outcome
                if (t.Chain(i, "Work", "Report") && t.P(i + 1, "(") && (t.Args(i + 1) is var ra) && (ra.Count > 0))
                {
                    var fn = flow.Around(i);
                    var me = t.Span(ra[0].A, ra[0].B);
                    for (int k = (fn?.From ?? 0); k < i; ++k)
                        if (t.Chain(k, "Work", "Report") && t.P(k + 1, "(") && (t.Args(k + 1) is var rb) && (rb.Count > 0) && (t.Span(rb[0].A, rb[0].B) == me) && (flow.Around(k) == fn))
                        {
                            Once(Rule.UxFbk5, i, "Sw.Work.Report(" + Short(me) + ", ...) again for the same outcome (line " + ctx.Source.GetLineCol(t.S[k].Start).Line + "): the failure is shown and announced twice; report an outcome once (one branch, after the if)");
                            break;
                        }
                }
                //  ---- Sw.Activity.Run(o, work) whose work never reads its signal; Sw.Util.DelayAbort whose result is ignored
                if (t.Chain(i, "Activity", "Run") && t.P(i + 1, "(") && (t.Args(i + 1) is var aa) && (aa.Count > 1) && (flow.At(aa[1].A) is FnNode wk))
                {
                    var p = FirstParam(t, wk);
                    var body = t.Span(wk.F.Start, wk.F.End);
                    bool reads = (p != null) && Regex.IsMatch(body, @"(?<![\w.$])" + Regex.Escape(p) + @"\b");
                    if (!reads && Regex.IsMatch(body, @"\bawait\b|\bfor\s*\(|\bwhile\s*\("))
                        Once(Rule.UxFbk4, i, "Sw.Activity.Run(..., " + (p == null ? "() => ..." : "(" + p + ", ...) => ...") + ") never reads the AbortSignal: Cancel marks the job cancelled while the work goes on and finishes (and the button says done); pass the signal to every request (Sw.Net.Send(url, arg, { Signal })) and stop on it (signal.throwIfAborted())");
                }
                //  (an abort used as a wake up signal, DelayAbort(ms, this.Wake), is meant to go on)
                if (t.Chain(i, "Util", "DelayAbort") && t.P(i + 1, "(") && Dropped(t, i, t.Chain(i, "Sw", "Util", "DelayAbort") ? 3 : 2) && (t.Args(i + 1) is var dla) && (dla.Count > 1) && Regex.IsMatch(t.Span(dla[1].A, dla[1].B), @"(?i)signal|abort|cancel"))
                    Once(Rule.UxFbk4, i, "the result of Sw.Util.DelayAbort is ignored: it resolves true on abort instead of throwing, so the loop goes on after Cancel; use await Sw.Sleep(ms, signal) (throws an AbortError on Cancel), or if (await Sw.Util.DelayAbort(ms, signal)) return;");
                //  ---- a job cancelled through its raw cancel function / the job api (the user is told "cancelled" before the server answered)
                //  (not as the fallback of job.Cancel() for an older job object: typeof j.Cancel === "function" ? j.Cancel() : j.CancelFn())
                if ((t.Id(i, "CancelFn") || t.Id(i, "CancelImpl")) && t.P(i - 1, ".") && (t.P(i + 1, "(") || (t.P(i + 1, "&&") && t.P(i + 5, "(")))
                    && !Regex.IsMatch(t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), ExprEndStatement(t, i)), @"\.Cancel\s*\("))
                    Once(Rule.UxFbk4, i, "a job cancelled through ." + t.Tx(i) + "(): the item says cancelled before the server answered (a finished export is still saved); call job.Cancel() (\"Stopping ...\" until the server confirms, \"Too late to cancel\" when it had finished)");
                //  (unless the answer is read: const x = await Send(".../CancelJob", id) decides by the state)
                if (((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && Regex.IsMatch(t.Tx(i), @"\bjobs/CancelJob\b")
                    && !Regex.IsMatch(t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), i), @"^\s*(?:(?:const|let|var)\s+\w+\s*=|\w+\s*=|return\b)"))
                    Once(Rule.UxFbk4, i, "the job api's CancelJob called by hand: the answer (done / cancelled) decides what the user is told; use the job object of Sw.Activity.StartServer / RestoreServer: job.Cancel()");
                //  ---- TXT-30: the raw error message as the whole toast (what failed and what to do are lost); "try again" glued to ErrorText
                if (t.Chain(i, "Sw", "Fail") || t.Chain(i, "Sw", "Toast") || t.Chain(i, "Sw", "Message") || t.Chain(i, "Net", "ErrorText"))
                {
                    if (t.P(i + 1, "(") && (t.Args(i + 1) is var fa) && (fa.Count > 0) && Regex.IsMatch(t.Span(fa[0].A, fa[0].B), @"^\(?\s*\w+\.message\s*(?:\|\|\s*\w+\s*)?\)?$"))
                        Once(Rule.UxTxt30, i, "Sw." + t.Tx(i) + "(" + Short(t.Span(fa[0].A, fa[0].B)) + "): the raw message alone (\"Failed to fetch\", \"409 - Conflict\") says neither what failed nor what to do; pass the error itself (Sw.Fail(e) uses the server's reason) or Sw.Net.FailText(_TF(\"Save the profile\", \"...\"), e)");
                }
                if (t.Id(i, "ErrorText") && t.P(i + 1, "(") && (t.Match(i + 1) is int etc) && (etc > i) && t.P(etc + 1, ".") && t.Id(etc + 2, "Text"))
                {
                    var rest = t.Span(etc + 3, ExprEndStatement(t, etc + 3));
                    if (Regex.IsMatch(rest, @"(?i)try again|reload|later"))
                        Once(Rule.UxTxt30, i, "a sentence glued after Sw.Net.ErrorText(...).Text (\"... try again\"): ErrorText already says what to do for the kind of failure, so the user reads it twice or a contradiction; use Sw.Net.FailText(name, e) for \"<name>: not done. <reason>\"");
                }
                //  ---- Cancellable: true on a dialog button whose work sends a write (Cancel then aborts what the server already has)
                if (t.Id(i, "Cancellable") && t.Key(i) && t.Id(i + 2, "true") && (EnclosingObject(t, i) is int co) && (co > 0) && (ObjKey(t, co, "OnClick") is var ocv) && (ocv != null) && (flow.At(ocv.Value.A) is FnNode cfn))
                {
                    var w = flow.Of(cfn).Reqs.FirstOrDefault(r => !r.Chain && (r.Url.Length == 0 || !ReadUrl.IsMatch(r.Url)) && !t.Id(r.At, "Get") && !t.Id(r.At, "Cached") && !Regex.IsMatch(r.Url, @"Table(?:[?#].*)?$")
                        && !(IsRequest(t, r.At) && Regex.IsMatch(t.Span(r.At, Math.Max(r.At, t.Match(r.At + 1))), @"\b(?:ReadOnly|Read|AbortSafe)\s*:\s*true\b")) && !t.Chain(r.At, "Net", "Read"));
                    if (w != null)
                        Once(Rule.UxPop6, i, "Cancellable: true on a button whose work sends a write (" + Short(w.Url.Length > 0 ? w.Url : "a request") + "): Cancel never aborts a write (the server may have it), so the dialog offers \"Stop waiting\" anyway; drop Cancellable, or mark a request that is safe to abort ({ ReadOnly: true } / Sw.Net.Read(url, arg) / { AbortSafe: true })");
                }
                //  ---- FRM-5: EditWhileBusy on a dialog whose save doesn't read the fields again after it sent them (newer edits are dropped)
                if (t.Id(i, "EditWhileBusy") && t.Key(i) && t.Id(i + 2, "true") && (EnclosingObject(t, i) is int eo) && (eo > 0))
                {
                    var dlg = t.Span(eo, Math.Max(eo, t.Match(eo)));
                    var m = Regex.Match(dlg, @"\bawait\b([\s\S]*)$");
                    if (!m.Success || !Regex.IsMatch(m.Groups[1].Value, @"\.Get\(\s*\)|\.value\b|\bValues?\b"))
                        Once(Rule.UxFrm5, i, "EditWhileBusy: true, but the save doesn't read the fields again after its request: what the user types while it runs is not saved (and the dialog says saved); read the values again after the await and save the newer ones (or leave EditWhileBusy out: the fields are read only while it saves)");
                }
                //  ---- { Detached: true } on the save of the button (not busy, the page stays dirty)
                //  (a save api, or a payload that reads the values of the page's controls: x.Get(), x.value)
                if (IsRequest(t, i) && Regex.IsMatch(t.Span(i, Math.Max(i, t.Match(i + 1))), @"\b(?:Detached|Background)\s*:\s*true\b") && (t.Args(i + 1) is var qa) && (qa.Count > 0)
                    && (SaveApi.IsMatch(Literals(t, qa[0]).Trim().Split(' ').LastOrDefault() ?? "") || ((qa.Count > 1) && Regex.IsMatch(t.Span(qa[1].A, qa[1].B), @"\.Get\(\s*\)|\.value\b|\.checked\b"))) && InOnClick(t, flow, i))
                    Once(Rule.UxFbk2, i, "{ Detached: true } on the save of the button (" + Short(Literals(t, qa[0]).Trim()) + "): Detached is for background requests (telemetry, a live view); the save is the button's work: without it the button isn't busy, a failure isn't shown and the page stays dirty after the save; drop Detached and return the request");
                //  ---- an OnClick that returns a Sw.Activity.Start job (a job is no promise: the button isn't busy, the work isn't tracked)
                if (t.Id(i, "OnClick") && t.Key(i) && (flow.At(i + 2) is FnNode sfn))
                {
                    var bs = t.Span(sfn.F.Start, sfn.F.End);
                    bool start = (!sfn.F.Block && Regex.IsMatch(bs, @"^\s*Sw\.Activity\.Start\s*\(")) || (sfn.F.Block && Regex.IsMatch(bs, @"\b(?:const|let|var)\s+(\w+)\s*=\s*Sw\.Activity\.Start\s*\([\s\S]*\breturn\s+\1\s*;") ) || Regex.IsMatch(bs, @"\breturn\s+Sw\.Activity\.Start\s*\(");
                    if (start)
                        Once(Rule.UxFbk2, i + 2, "OnClick returns a job of Sw.Activity.Start: a job is no promise, so the button isn't busy and the work it does later (and its failure) belongs to nobody on a page without the shell; use Sw.Activity.Run(o, signal => work) (thenable, Cancel aborts the signal) or Sw.Activity.Track(promise, o)");
                }
            }

            //  ---- a footer OnClick that closes its dialog itself and then works (a failure after close() is lost)
            foreach (var d in dialogCalls)
                for (int k = d.A; k < d.B; ++k)
                    if (t.Id(k, "OnClick") && t.Key(k) && (flow.At(k + 2) is FnNode fo) && (FirstParam(t, fo) is String cp))
                    {
                        bool closed = false;
                        foreach (var j in flow.Own(fo))
                        {
                            if (t.Id(j, cp) && t.P(j + 1, "(") && !t.P(j - 1, "."))
                                closed = true;
                            else if (closed && (t.Id(j, "await") || t.Id(j, "throw") || IsRequest(t, j)))
                            {
                                Once(Rule.UxFbk5, j, "the dialog button calls " + cp + "() and then still works: after the dialog closed, a failure has nowhere to go (only the console), so the user believes it worked; return the work and let the dialog close when it succeeds (return true), or show a later failure with Sw.Fail");
                                break;
                            }
                        }
                    }
            //  ---- a Sw control with Draft given to Sw.Form (the form tracks it and says "lost" for a drafted text)
            for (int i = 0; i < t.N; ++i)
                if (t.Chain(i, "Sw", "Form") && t.P(i + 1, "(") && (t.Match(i + 1) is int fc) && (fc > i))
                    for (int k = i; k < fc; ++k)
                        if (t.Id(k, "Control") && t.Key(k) && t.Id(k + 2) && (t.P(k + 3, ",") || t.P(k + 3, "}")) && DraftedIn(t, flow.Around(i), t.Tx(k + 2)))
                            Once(Rule.UxSaf11, k, "\"" + t.Tx(k + 2) + "\" has a Draft and is a field of a Sw.Form: the form tracks it as an unsaved edit and the leave question says it is lost, although the draft keeps it; keep one of the two (drop Draft, or keep the field out of the form's dirty tracking with Dirty: false)");

            //  ================= pointer events other than click
            //  The same element: the same receiver name in the same function (a "li" of another list is another element)
            String ElemKey(Reg r) => (r.Recv ?? "") + "@" + (flow.Around(r.At)?.Head ?? -1);
            var byRecv = regs.GroupBy(ElemKey).ToDictionary(g => g.Key, g => g.Select(x => x.Evt).ToHashSet(StringComparer.Ordinal));
            bool HasPath(Reg r, params String[] evts)
            {
                var recv = r.Recv ?? "";
                if (byRecv.TryGetValue(ElemKey(r), out var s) && evts.Any(s.Contains))
                    return true;
                //  Sw.OnClick(el, fn) / Sw.KeyClick(el) / Sw.Card({ OnClick }) on the same element
                return (recv.Length > 0) && Regex.IsMatch(text, @"\bSw\.(?:OnClick|KeyClick|LongPress)\(\s*(?:this\.)?" + Regex.Escape(recv) + @"\b");
            }
            //  The click / key handlers of the element reach the work of this handler (the functions it calls)
            bool SameWork(Reg r)
            {
                var called = new HashSet<String>(StringComparer.Ordinal);
                for (int j = r.Fn.F.Start; j <= r.Fn.F.End; ++j)
                    if (t.Id(j) && t.P(j + 1, "(") && !Keywords.Contains(t.Tx(j)) && !PassiveCalls.Contains(t.Tx(j)))
                        called.Add(t.Tx(j));
                if (called.Count == 0)
                    return true;
                var recv = r.Recv ?? "";
                if ((recv.Length > 0) && Regex.IsMatch(text, @"\bSw\.(?:OnClick|KeyClick|LongPress)\(\s*(?:this\.)?" + Regex.Escape(recv) + @"\b"))
                    return true;
                var key = ElemKey(r);
                foreach (var o in regs)
                    if ((o != r) && (o.Fn != null) && (ElemKey(o) == key) && ((o.Evt == "click") || (o.Evt == "keydown") || (o.Evt == "keyup") || (o.Evt == "keypress")))
                    {
                        if (o.Fn == r.Fn)
                            return true;
                        var d = DeepSpan(t, flow, o.Fn);
                        if (called.Any(c => Regex.IsMatch(d, @"(?<![\w$])" + Regex.Escape(c) + @"\s*\(")))
                            return true;
                    }
                return false;
            }
            bool fileDrags = regs.Any(r => MoveEvents.Contains(r.Evt)) || Regex.IsMatch(text, @"\bsetPointerCapture\b");
            foreach (var r in regs)
            {
                if (r.Fn == null)
                    continue;
                var deep = DeepSpan(t, flow, r.Fn);
                if (PressEvents.Contains(r.Evt))
                {
                    //  A drag set up on press (move listeners, pointer capture) and a press that only prevents the focus change are fine
                    //  (a press that records where it started, clientX / pageY / a reorder or resize state, starts a drag too)
                    bool drag = Regex.IsMatch(deep, @"(?:pointer|mouse|touch)move|setPointerCapture|\bdrag|reorder|resiz|\bclient[XY]\b|\bpage[XY]\b|\bscreen[XY]\b|\boffset[XY]\b|\bgrab|\bpan\b", RegexOptions.IgnoreCase);
                    //  The same handler also on click / a key (the first gesture of any kind, ex: unlock audio) is no press action
                    bool alsoClick = regs.Any(x => (x != r) && (x.Fn == r.Fn) && (x.Evt == "click" || x.Evt == "keydown" || x.Evt == "keyup"));
                    if (!drag && !alsoClick && !Regex.IsMatch(deep, @"\b(?:IsPureEvent|BadClick)\b") && Acts(t, flow, r.Fn, out var what))
                        Once(Rule.UxInp1, r.At, "a " + r.Evt + " handler that acts (" + Short(what) + "): an action on press can't be cancelled by moving away, runs before a drag or a scroll is told apart, and keyboard, screen readers and voice control never fire it; act in a click handler (Sw.OnClick(el, fn) or a button; Sw.BadClick(ev) skips modifier clicks), a press only starts a drag");
                }
                else if (r.Evt == "dblclick")
                {
                    //  (round 5: a click that only selects is no path: the click / key handlers of the element must reach the same work)
                    if ((!HasPath(r, "click", "keydown", "keyup", "keypress") || !SameWork(r)) && Acts(t, flow, r.Fn, out var what))
                        Once(Rule.UxInp6, r.At, "a dblclick handler (" + Short(what) + ") is the only way" + (r.Recv.Length > 0 ? " on \"" + r.Recv + "\"" : "") + ": touch, keyboard and screen reader users can't double click; give the same result a single click (Sw.OnClick / a button), a menu item or a key (Enter), and keep the double click as a shortcut");
                }
                else if (HoverEvents.Contains(r.Evt))
                {
                    var e = flow.Of(r.Fn);
                    var w = e.Reqs.FirstOrDefault(q => !q.Chain && !t.Id(q.At, "Get") && !t.Id(q.At, "Cached") && ((q.Url.Length == 0) || !ReadUrl.IsMatch(q.Url.Split(' ').Last())));
                    bool nav = Regex.IsMatch(deep, @"\blocation\s*(?:=|\.\s*(?:href\s*=|assign|replace))|\bPage\.Navigate\(|\bMenu\.Show\(|\bDialog\.(?:Open|Show|Confirm)\(|\bwindow\.open\(|\.(?:OpenMenu|ShowMenu|ToggleMenu|OpenPopup)\s*\(");
                    if ((w != null) || nav)
                        Once(Rule.UxInp8, r.At, "a " + r.Evt + " handler that " + (w != null ? "sends a request (" + Short(w.Url.Length > 0 ? w.Url : "a write") + ")" : "navigates or opens a menu / dialog") + ": hover may only show a tool tip or a preview; a passing pointer changes data or takes the focus, and touch and keyboard users never hover; act on a click (or on focus + a key) and keep hover for the preview");
                }
                else if (UpEvents.Contains(r.Evt) && (r.Recv.Length > 0) && !PageObjects.Contains(r.Recv) && !r.Recv.StartsWith("document", StringComparison.Ordinal))
                {
                    //  A release that acts on an element with no press / move of its own (no drag that ends here) and no click
                    //  (a press on the same element that itself acts, select on press, is no drag start: round 4)
                    bool partOfDrag = regs.Any(x => (ElemKey(x) == ElemKey(r)) && (MoveEvents.Contains(x.Evt) || (PressEvents.Contains(x.Evt) && ((x.Fn == null) || !Acts(t, flow, x.Fn, out _) || Regex.IsMatch(DeepSpan(t, flow, x.Fn), @"(?:pointer|mouse|touch)move|setPointerCapture|\bdrag|\bclient[XY]\b|\bpage[XY]\b", RegexOptions.IgnoreCase))))) || Regex.IsMatch(deep, @"(?:pointer|mouse|touch)move|releasePointerCapture|\bdrag", RegexOptions.IgnoreCase);
                    if (!partOfDrag && !HasPath(r, "click") && Acts(t, flow, r.Fn, out var what))
                        Once(Rule.UxInp1, r.At, "a " + r.Evt + " handler that acts (" + Short(what) + ") on \"" + r.Recv + "\", which has no click handler: Enter / Space, screen readers and voice control fire only click, and a release after a scroll or a drag from elsewhere runs it; use a click handler (Sw.OnClick(el, fn); Sw.BadClick(ev) skips modifier clicks)");
                }
            }
            //  ---- drag and drop as the only way
            var drop = regs.FirstOrDefault(r => (r.Evt == "drop") && (r.Fn != null) && Acts(t, flow, r.Fn, out _));
            int dragAt = -1;
            for (int i = 0; (i < t.N) && (dragAt < 0); ++i)
                if ((t.Id(i, "draggable") && ((t.P(i - 1, ".") && t.P(i + 1, "=") && t.Id(i + 2, "true")) || (t.Key(i) && (t.Id(i + 2, "true") || t.Str(i + 2) == "true"))))
                    || (t.Id(i, "setAttribute") && t.P(i + 1, "(") && (t.Str(i + 2) == "draggable")))
                    dragAt = i;
            if ((drop != null) && (dragAt >= 0))
            {
                //  The work of the drop (the functions it calls) also called from a key, a click / menu item, or a "Move" text
                var called = new HashSet<String>(StringComparer.Ordinal);
                foreach (var j in flow.Own(drop.Fn))
                    if (t.Id(j) && t.P(j + 1, "(") && !PassiveCalls.Contains(t.Tx(j)) && !Keywords.Contains(t.Tx(j)) && (flow.NamedFn(t.Tx(j)) != null))
                        called.Add(t.Tx(j));
                //  (a generic drop helper whose work is a callback parameter is judged where it is used)
                if (called.Count == 0)
                    drop = null;
                int uses = 0;
                for (int i = 0; i < t.N; ++i)
                    if (t.Id(i) && called.Contains(t.Tx(i)) && t.P(i + 1, "(") && !(drop.Fn.F.Start <= i && i <= drop.Fn.F.End) && !t.P(EnclosingObject(t, i) - 1, "{") && !(flow.Around(i)?.Name == t.Tx(i)))
                        if (!IsDeclaration(t, i))
                            ++uses;
                //  (the alternative must do the same work: a "Move up" button for another move doesn't count)
                bool keys = false;
                if ((drop != null) && (uses == 0) && !keys)
                    Once(Rule.UxInp20, dragAt, "drag and drop is the only way to move or reorder here (the drop handler's work is called from nowhere else): keyboard and touch users and people who can't drag can't do it; add Alt+ArrowUp / Alt+ArrowDown (Sw.Keys.Add) and a \"Move up\" / \"Move down\" menu item or button that call the same work");
            }

            //  ================= INP-23: a single key shortcut on the page that fires in text fields
            foreach (var r in regs)
            {
                if ((r.Fn == null) || !(r.Evt == "keydown" || r.Evt == "keypress" || r.Evt == "keyup") || !Regex.IsMatch(r.Recv, @"^(?:document|window|document\.body|document\.documentElement|body)$"))
                    continue;
                var body = t.Span(r.Fn.F.Start, r.Fn.F.End);
                if (Regex.IsMatch(body, @"\b(?:target|srcElement|tagName|isContentEditable|activeElement|composedPath|InInputs|IsTyping|IsTextInput|closest)\b"))
                    continue;
                for (int j = r.Fn.F.Start; j <= r.Fn.F.End; ++j)
                {
                    bool single = false;
                    if ((t.Id(j, "key") || t.Id(j, "code") || t.Id(j, "keyCode") || t.Id(j, "which")) && t.P(j - 1, ".") && (t.P(j + 1, "===") || t.P(j + 1, "==")))
                    {
                        var v = t.Str(j + 2);
                        single = (v != null) && (t.Tx(j) == "key" ? (v.Length == 1 && !Char.IsWhiteSpace(v[0])) : (t.Tx(j) == "code" && Regex.IsMatch(v, @"^(?:Key[A-Z]|Digit\d|Slash|Period|Comma)$")));
                        if (!single && (t.Tx(j) == "keyCode" || t.Tx(j) == "which") && (t.S[j + 2].Kind == JsTokenKind.Number) && Int32.TryParse(t.Tx(j + 2), out var kc))
                            single = ((kc >= 48) && (kc <= 90)) || (kc == 191);
                    }
                    if (!single)
                        continue;
                    //  The condition around the comparison: a held modifier (not a negated one) makes it a combination
                    int cs = j, ce = j;
                    for (int d = 0; cs > r.Fn.F.Start; --cs)
                    {
                        if (t.P(cs, ")"))
                            ++d;
                        else if (t.P(cs, "(") && (d-- == 0))
                            break;
                        else if ((d == 0) && (t.P(cs, ";") || t.P(cs, "{") || t.P(cs, "}")))
                            break;
                    }
                    ce = cs > 0 && t.P(cs, "(") ? t.Match(cs) : ExprEndStatement(t, j);
                    var cond = t.Span(cs, Math.Max(cs, ce));
                    if (Regex.IsMatch(cond, @"(?<!!\s*\w*\.?)\b(?:ctrlKey|metaKey|altKey)\b(?!\s*===?\s*false)") && !Regex.IsMatch(cond, @"!\s*\w+\.(?:ctrlKey|metaKey|altKey)\b(?![\s\S]*(?<!!\s*\w{0,8}\.)(?:ctrlKey|metaKey|altKey))"))
                        continue;
                    Once(Rule.UxInp23, j, "a single key shortcut (" + Short(t.Span(j - 2 >= 0 ? j - 2 : j, j + 2)) + ") on " + r.Recv + " without a check of the focused element: it fires while the user types in a text field (\"r\" in a name reloads the list); use Sw.Keys.Add(\"R\", fn, { Title }) (skips text fields, listed in the help), or add a modifier");
                    break;
                }
            }

            //  True if a helper handles the failures of all its requests (also through the helpers it calls)
            bool HelperHandles(FnNode g, int depth)
            {
                foreach (var r in flow.Of(g).Reqs.Where(r => !r.Chain))
                {
                    if (Handled(t, flow, r.At))
                        continue;
                    var sub = r.Via != null ? flow.NamedFn(r.Via) : null;
                    if ((sub != null) && (sub != g) && (depth < 4) && HelperHandles(sub, depth + 1))
                        continue;
                    return false;
                }
                return true;
            }
            //  ================= silent failures: a request in an event listener or a timer whose failure nobody handles
            void Silent(FnNode h, String where, bool writesOnly)
            {
                if (h == null)
                    return;
                foreach (var q in flow.Of(h).Reqs)
                {
                    //  Requests written in the handler (or its inline callbacks); a helper or a component method that handles its own
                    //  failures is fine, one that doesn't (an async helper whose promise the listener drops) is the listener's (round 4)
                    if (q.Chain)
                        continue;
                    if ((q.Via != null) || q.ViaMethod)
                    {
                        var hg = q.Via != null ? flow.NamedFn(q.Via) : null;
                        //  (a helper that turns a failure into null is judged by its result: CheckSilentFlow)
                        if ((hg == null) || q.ViaMethod || nothing.ContainsKey(q.Via) || HelperHandles(hg, 0) || Handled(t, flow, q.At))
                            continue;
                    }
                    bool read = t.Id(q.At, "Get") || t.Id(q.At, "Cached") || ((q.Url.Length > 0) && ReadUrl.IsMatch(q.Url.Split(' ').Last()));
                    if (writesOnly && read)
                        continue;
                    if (PollUrl.IsMatch(q.Url))
                        continue;
                    if (IsRequest(t, q.At) && Regex.IsMatch(t.Span(q.At, Math.Max(q.At, t.Match(q.At + 1))), @"\b(?:Detached|Background)\s*:\s*true\b"))
                        continue;
                    if (Handled(t, flow, q.At))
                        continue;
                    Once(Rule.UxFbk5, q.At, "a request " + where + " whose failure nobody handles (" + Short(q.Url.Length > 0 ? q.Url : (q.Via ?? "a request")) + "): no button shows it, so a failure is only an unhandled rejection in the console; add .catch(e => Sw.Fail(e)) or try / catch with Sw.Fail, or do the work in a button's OnClick (busy, \"not done\" with the reason)");
                    break;
                }
            }
            foreach (var r in regs)
                //  (not the live input of a field: a filter or search per key press is replaced by the next one)
                if (UserEvents.Contains(r.Evt) && (r.Fn != null) && (r.Evt != "input") && (r.Evt != "keyup") && (r.Evt != "keypress"))
                    Silent(r.Fn, "in a " + r.Evt + " listener", r.Evt != "click" && r.Evt != "submit" && r.Evt != "change" && r.Evt != "drop" && r.Evt != "keydown");
            for (int i = 0; i < t.N; ++i)
            {
                if ((t.Id(i, "setInterval") || t.Id(i, "setTimeout")) && !t.P(i - 1, ".") && t.P(i + 1, "(") && (flow.At(i + 2) is FnNode tn) && !InOnClick(t, flow, i))
                    Silent(tn, "in a " + t.Tx(i) + " callback", false);
                //  A shortcut's handler (Sw.Keys.Add): nobody shows its failure
                if (t.Chain(i, "Keys", "Add") && t.P(i + 1, "(") && (t.Args(i + 1) is var kka) && (kka.Count > 1) && (flow.At(kka[1].A) is FnNode kfn))
                    Silent(kfn, "in a shortcut handler", false);
            }
            //  ---- a "Saved" / "Updated" toast after an auto-saving control changed (the control shows its state)
            for (int i = 0; i < t.N; ++i)
                if ((t.Id(i, "OnChange") || t.Id(i, "OnInput")) && t.Key(i) && (EnclosingObject(t, i) is int oo) && (oo > 0) && IsSwOptions(t, oo) && (flow.At(i + 2) is FnNode cn))
                {
                    var e = flow.Of(cn);
                    foreach (var ts in e.Toasts)
                        if (SavedLike.IsMatch(ts.Text) && !Regex.IsMatch(t.Span(ts.At, Math.Max(ts.At, ExprEndStatement(t, ts.At))), @"\bAction\s*:"))
                        {
                            Once(Rule.UxFbk5, ts.At, "a \"" + Short(ts.Text) + "\" toast when an auto-saving control changed: the control shows the new state in place (and a failure at the control); never a toast after every toggle (FBK-5); drop it, return the save's promise from " + t.Tx(i) + " (the control is busy and shows a failure itself)");
                            break;
                        }
                }

            //  ================= requests of the other fixers (S4R3)
            for (int i = 0; i < t.N; ++i)
            {
                //  ---- page keys registered per instance of a component (a class method / constructor) without Scope / Replace
                //  (a single key such as "/", "g", "?": the keys a list takes for itself; a modifier combination of a one-per-page tool is fine)
                if (t.Chain(i, "Keys", "Add") && t.P(i + 1, "(") && (t.Args(i + 1) is var ka) && (ka.Count > 1) && !Regex.IsMatch(t.Span(i, Math.Max(i, t.Match(i + 1))), @"\b(?:Scope|Replace)\s*:")
                    && (t.Lit(ka[0]) is String kc) && !kc.Contains('+'))
                {
                    bool perInstance = false;
                    for (var n = flow.Around(i); (n != null) && !perInstance; n = n.Parent)
                        perInstance = n.IsMethod || (n.Name == "constructor");
                    if (perInstance)
                        Once(Rule.UxInp23, i, "Sw.Keys.Add(" + Short(t.Span(ka[0].A, ka[0].B)) + ", ...) in a method of a component: every instance registers the page key again, so two lists on one page (or in tabs) steal each other's key; pass { Scope: this.E } (it runs only while that element is shown) or register it once per page");
                }
                //  ---- a row action whose 409 / 404 ("already done" / "gone") becomes an error toast that asks to reload
                if ((t.Id(i, "status") || t.Id(i, "Status")) && t.P(i - 1, ".") && (t.P(i + 1, "===") || t.P(i + 1, "==")) && (t.Tx(i + 2) == "409" || t.Tx(i + 2) == "404"))
                {
                    var fn = flow.Around(i);
                    if (fn != null)
                    {
                        var body = t.Span(fn.F.Start, fn.F.End);
                        if (Regex.IsMatch(body, @"\bSw\.(?:Fail|Toast)\s*\([^;]*(?i:reload)") && Regex.IsMatch(body, @"(?i)delete|remove|withdraw|end|kill|revoke|cancel"))
                            Once(Rule.UxFbk5, i, "a " + t.Tx(i + 2) + " after a delete / end / withdraw shown as an error that asks to reload: the goal may be met already (someone else did it, it ended by itself); refresh the list and say what is true now (Sw.Table's row actions do: \"not done: <the server's reason>\" in the row)");
                    }
                }
                //  ---- a file name stamp in a text without Sw.Table.KeepStamp (the time can wrap in the middle)
                if (t.Id(i, "FileStamp") && t.P(i + 1, "(") && !Regex.IsMatch(ctx.Text, @"\bKeepStamp\b"))
                {
                    var st = t.Span(StatementStart(t, flow.Around(i)?.From ?? 0, i), ExprEndStatement(t, i));
                    if (Regex.IsMatch(st, @"\bSw\.(?:Toast|Info|Fail)\b|\btitle\s*=|\bTitle\s*:|\bAnnounce\("))
                        Once(Rule.UxTxt17, i, "a file name stamp (FileStamp()) in a text: the date and time can wrap in the middle (\"2026-10-20 21-\" / \"02 UTC+2\"); wrap it with Sw.Table.KeepStamp(text) (no break inside the stamp)");
                }
            }
            //  ---- the only save is a plain button outside the cards, not named as a save and without Saves: true (its edits aren't tracked)
            if (!Regex.IsMatch(text, @"\bSaves\s*:\s*true\b") && vars.Values.Any(v => FieldControls.Contains(v.Kind)))
                foreach (var kv in vars.Where(v => v.Value.Kind == "Sw.Button" && v.Value.Call > 0 && t.P(v.Value.Call + 1, "{")))
                {
                    int o = kv.Value.Call + 1;
                    var txt = ObjKey(t, o, "Text") is var tv && tv != null ? ValueText(t, tv.Value) ?? "" : "";
                    bool primary = (ObjKey(t, o, "Kind") is var kd) && (kd != null) && (t.Lit(kd.Value) == "primary");
                    var oc = ObjKey(t, o, "OnClick");
                    if (primary || Regex.IsMatch(txt, @"(?i)\b(?:save|apply|submit|send|update|store|keep)\b") || (oc == null) || !(flow.At(oc.Value.A) is FnNode bn))
                        continue;
                    var save = flow.Of(bn).Reqs.FirstOrDefault(r => !r.Chain && r.Url.Split(' ').Any(u => SaveApi.IsMatch(u)));
                    bool inCard = Regex.IsMatch(text, @"\b(?:Card|Group|Form|FormFooter)\s*\(\s*\{[^;]*\b" + Regex.Escape(kv.Key) + @"\b");
                    if ((save != null) && !inCard)
                        Once(Rule.UxSaf10, kv.Value.At, "\"" + kv.Key + "\" (\"" + Short(txt) + "\") saves (" + Short(save.Url) + ") but is no primary button, not named as a save and outside the cards of the fields: the page doesn't count it as a save path, so the edits aren't tracked and leaving loses them without a question; pass Saves: true to the button (or make it the primary Save of the card)");
                }

            //  ================= SAF-4: a danger confirmation weakened by a failure
            foreach (var n in flow.Fns)
            {
                var own = flow.Own(n).ToList();
                bool strong = own.Any(j => (t.Chain(j, "Cells", "ConfirmDanger") || t.Chain(j, "Dialog", "Confirm") || t.Chain(j, "Danger", "Confirm")) && t.P(j + 1, "(") && Regex.IsMatch(t.Span(j, Math.Max(j, t.Match(j + 1))), @"\bConfirm\s*:|\bDanger\s*:\s*true|\bType\s*:"));
                if (!strong)
                    continue;
                //  The values a catch of this function resets
                var reset = new HashSet<String>(StringComparer.Ordinal);
                foreach (var j in own)
                    if (t.Id(j, "catch") && t.P(j - 1, "}"))
                    {
                        int b = t.P(j + 1, "(") ? t.Match(j + 1) + 1 : j + 1;
                        int be = t.P(b, "{") ? t.Match(b) : -1;
                        for (int k = b + 1; k < be; ++k)
                        {
                            if (t.Id(k) && t.P(k + 1, "=") && !t.P(k + 2, "=") && !t.P(k - 1, ".") && (t.Is((k + 2, ExprEnd(t, k + 2)), "null") || t.Is((k + 2, ExprEnd(t, k + 2)), "undefined") || t.Is((k + 2, ExprEnd(t, k + 2)), "false")))
                                reset.Add(t.Tx(k));
                            //  A plain confirmation in the catch itself
                            if ((t.Chain(k, "Dialog", "Confirm") || t.Chain(k, "Cells", "ConfirmDanger") || (t.Id(k, "confirm") && !t.P(k - 1, "."))) && t.P(k + 1, "(") && !Regex.IsMatch(t.Span(k, Math.Max(k, t.Match(k + 1))), @"\bConfirm\s*:"))
                                Once(Rule.UxSaf4, k, "a weaker confirmation in a catch: when the strong one (type to confirm) can't be prepared, the destructive action is asked with a plain question instead; show the failure (Sw.Fail(e): \"The delete can't be prepared right now ... Try again.\") and offer no destructive button");
                        }
                    }
                if (reset.Count == 0)
                    continue;
                foreach (var j in own)
                    if ((t.Chain(j, "Cells", "ConfirmDanger") || t.Chain(j, "Dialog", "Confirm") || t.Chain(j, "Danger", "Confirm")) && t.P(j + 1, "(") && (t.Args(j + 1) is var ca) && (ca.Count > 0))
                    {
                        var arg = t.Span(ca[0].A, ca[0].B);
                        var v = reset.FirstOrDefault(x => Regex.IsMatch(arg, @"^\s*!?\s*\(?\s*" + Regex.Escape(x) + @"\b[^?]*\?"));
                        if ((v != null) && Regex.IsMatch(arg, @"\bConfirm\s*:") && (Regex.Matches(arg, @"\bConfirm\s*:").Count < 2))
                            Once(Rule.UxSaf4, j, "the confirmation is chosen by \"" + v + "\", which a catch resets: when preparing the strong confirmation fails (the delete token, the facts), the user gets the weaker one without the typed word, for the same destructive action; when it fails, show the failure (Sw.Fail) and offer no destructive button");
                    }
            }

            //  ================= silent failures by data flow (round 4)
            CheckSilentFlow(ctx, t, flow, regs, nothing, Once);
            //  ================= the round 4 families
            CheckRound4(ctx, t, flow, vars, regs, dialogCalls);
            Secrets = null;
            Reach = null;
        }

        static readonly HashSet<String> BlinkProps = new HashSet<String>(StringComparer.Ordinal)
        {
            "opacity", "visibility", "color", "background-color", "background", "fill", "stroke", "box-shadow", "filter", "outline-color", "border-color", "text-shadow",
        };
        static readonly Regex ContentSubject = new Regex(@"(?i)status|message|msg|error|note|desc|detail|log|output|result|summary|value|content|text|comment|address|email|path|url|code\b|name\b|size|bytes|count|amount|total|price|cost|qty|quantity|sum\b|date|time\b|stamp|[.#-]id\b|ident|serial|number|ref\b", RegexOptions.CultureInvariant);
        static readonly Regex ChromeSubject = new Regex(@"(?i)btn|button|tab|menu|toolbar|handle|grip|icon|chip|badge|head|title|label|drag|resize|splitter|nav|switch|toggle|thumb|slider|knob|bar\b|caret|arrow|close|tool|gutter|line-?num|progress", RegexOptions.CultureInvariant);

        /// <summary>
        /// The css rules that look at a whole rule block: an endless blink / pulse (ux-mot-6), text clipped without an ellipsis (ux-txt-17),
        /// user-select: none on content (ux-inp-30).
        /// </summary>
        static void CheckCssBlocks(Checker.FileContext ctx, List<CssDecl> decls)
        {
            //  The properties each @keyframes animates
            var kf = new Dictionary<String, HashSet<String>>(StringComparer.Ordinal);
            foreach (var d in decls)
            {
                var k = d.At.LastOrDefault(x => Regex.IsMatch(x, @"^@(?:-webkit-)?keyframes\b", RegexOptions.IgnoreCase));
                if (k == null)
                    continue;
                var name = Regex.Match(k, @"keyframes\s+([\w-]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (!kf.TryGetValue(name, out var set))
                    kf[name] = set = new HashSet<String>(StringComparer.Ordinal);
                set.Add(d.Prop);
            }
            bool stopsReduced = Regex.IsMatch(ctx.Text, @"prefers-reduced-motion\s*:\s*reduce", RegexOptions.IgnoreCase);
            foreach (var g in decls.Where(d => (d.Selector != null) && !d.At.Any(x => Regex.IsMatch(x, "keyframes", RegexOptions.IgnoreCase))).GroupBy(d => d.Block))
            {
                var list = g.ToList();
                var sel = list[0].Selector;
                var subject = Checker.SplitSelectorList(sel).Select(x => Regex.Match(x, @"[^\s>+~]+$").Value).FirstOrDefault() ?? "";
                String Val(String p) => list.LastOrDefault(x => x.Prop == p)?.Value?.Replace("!important", "").Trim().ToLowerInvariant();
                //  round 4: small targets, fixed page widths, faded but present content, smooth scrolling
                CheckCssRound4(ctx, list, sel, subject, decls);
                //  ux-mot-6: an endless animation of opacity / colours (a blink or a pulse) that reduced motion doesn't stop
                var anim = list.FirstOrDefault(x => (x.Prop == "animation") || (x.Prop == "-webkit-animation"));
                bool infinite = ((anim != null) && Regex.IsMatch(anim.Value, @"\binfinite\b", RegexOptions.IgnoreCase)) || (Val("animation-iteration-count") == "infinite");
                if (infinite && !stopsReduced && !list.Any(x => x.At.Any(a => a.IndexOf("prefers-reduced-motion", StringComparison.OrdinalIgnoreCase) >= 0)) && !Regex.IsMatch(sel, @"(?i)spin|skeleton|loading|busy|progress|placeholder|shimmer"))
                {
                    var names = ((anim?.Value ?? "") + " " + (Val("animation-name") ?? "")).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var n = names.FirstOrDefault(kf.ContainsKey);
                    if ((n != null) && kf[n].All(BlinkProps.Contains) && kf[n].Any(p => (p == "opacity") || (p == "visibility") || p.Contains("color")))
                    {
                        var at = anim ?? list.First(x => x.Prop == "animation-iteration-count");
                        Add(ctx, Rule.UxMot6, at.ValueStart, "\"" + Short(sel) + "\" blinks / pulses without end (\"" + Short(at.Value) + "\": " + n + " animates " + String.Join(", ", kf[n]) + "): endless motion is only for a spinner while work runs or an explicit live indicator, and reduced motion stops it; drop the animation (show the state with an icon and a word), or stop it in @media (prefers-reduced-motion: reduce)");
                    }
                }
                //  ux-txt-17: nowrap + overflow hidden without an ellipsis
                var ws = list.LastOrDefault(x => x.Prop == "white-space");
                var ov = Val("overflow") ?? Val("overflow-x");
                //  (not a visually hidden text, clip / 1 px, nor a flex / grid container whose children ellipsize)
                bool hiddenText = (Val("clip") != null) || (Val("clip-path") != null) || (Val("width") == "1px") || (Val("height") == "1px");
                var classes = Regex.Matches(subject, @"\.[\w-]+").Cast<Match>().Select(m => m.Value).ToList();
                bool container = Regex.IsMatch(Val("display") ?? "", "flex|grid") || classes.Any(c => decls.Any(x => (x.Selector != null) && (x.Prop == "display") && Regex.IsMatch(x.Value, "flex|grid") && Regex.IsMatch(x.Selector, Regex.Escape(c) + @"(?![\w-])")));
                if ((ws != null) && !hiddenText && !container && (Val("white-space") is String wv) && (wv == "nowrap") && (ov != null) && Regex.IsMatch(ov, @"^(?:hidden|clip)\b") && (Val("text-overflow") == null)
                    && !Regex.IsMatch(sel, @"(?i)icon|svg|img|track|scroll|marquee|sr-only|visually-hidden|ticker|spark|bar\b|meter|chart|canvas|progress") && !list.Any(x => (x.Prop == "-webkit-line-clamp") || (x.Prop == "line-clamp")))
                    Add(ctx, Rule.UxTxt17, ws.ValueStart, "\"" + Short(sel) + "\": white-space: " + wv + " with overflow: " + ov + " and no text-overflow: ellipsis: a longer translation (often 30-50 % longer) is cut without a sign; add text-overflow: ellipsis and a tool tip with the full text, or let it wrap");
                //  ux-inp-30: user-select: none on content (a status line, a message, a value)
                var us = list.LastOrDefault(x => ((x.Prop == "user-select") || (x.Prop == "-webkit-user-select")) && x.Value.Trim().StartsWith("none", StringComparison.OrdinalIgnoreCase));
                var tokens = String.Join(" ", Regex.Matches(subject, @"[.#][\w-]+").Cast<Match>().Select(m => m.Value));
                if ((us != null) && ContentSubject.IsMatch(tokens) && !ChromeSubject.IsMatch(tokens) && !subject.Contains("::"))
                    Add(ctx, Rule.UxInp30, us.ValueStart, "user-select: none on \"" + Short(subject) + "\", which is content (a status, a message, a value): people copy error texts, ids and values into tickets; chrome is not selectable by the theme already, content stays selectable (.sw-selectable)");
            }
        }

        static readonly Regex SavedLike =new Regex(@"^\s*(?:[\p{L}'-]+\s+){0,3}(?:saved|done|updated|applied|stored|changed|set|success(?:ful(?:ly)?)?)\b[\s.!]*$|^\s*(?:successfully|saved)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>True if the token at i is the name of a function or method declaration (function x(, x(...) {, const x = (.</summary>
        static bool IsDeclaration(Toks t, int i) =>
            t.Id(i - 1, "function") || (t.P(i + 1, "(") && (t.Match(i + 1) is int c) && (c > 0) && t.P(c + 1, "{") && (t.P(i - 1, "{") || t.P(i - 1, "}") || t.P(i - 1, ";") || t.Id(i - 1, "async") || t.P(i - 1, ",")));

        /// <summary>True if the function (or the file) makes the control "name" as a Sw value control with a Draft option.</summary>
        static bool DraftedIn(Toks t, FnNode n, String name)
        {
            int from = n?.F.Start ?? 0, to = n?.F.End ?? t.N - 1;
            for (int k = from; k + 4 <= to; ++k)
                if (t.Id(k, name) && t.P(k + 1, "=") && t.Id(k + 2, "new") && t.SwComponent(k + 5) && t.P(k + 6, "(") && t.P(k + 7, "{") && FieldControls.Contains("Sw." + t.Tx(k + 5))
                    && (ObjKey(t, k + 7, "Draft") is var dv) && (dv != null) && !t.Is(dv.Value, "false"))
                    return true;
            return false;
        }

        /// <summary>The first parameter name of a function (null if none or a pattern).</summary>
        static String FirstParam(Toks t, FnNode n)
        {
            int h = n.Head;
            if (t.Id(h, "async"))
                ++h;
            if (t.Id(h, "function"))
            {
                ++h;
                if (t.Id(h))
                    ++h;
            }
            else if (n.IsMethod)
                ++h;
            if (t.Id(h) && t.P(h + 1, "=>"))
                return t.Tx(h);
            if (t.P(h, "(") && t.Id(h + 1) && (t.P(h + 2, ",") || t.P(h + 2, ")") || t.P(h + 2, "=")))
                return t.Tx(h + 1);
            return null;
        }

        /// <summary>True if the token is inside the OnClick handler of a Sw component (also nested callbacks of it).</summary>
        static bool InOnClick(Toks t, Flow flow, int at)
        {
            for (var n = flow.Around(at); n != null; n = n.Parent)
                if (t.Id(n.Head - 2, "OnClick") && t.P(n.Head - 1, ":"))
                    return true;
            return false;
        }

        /// <summary>True if the token is inside a handler of a Sw component option (OnClick, OnChange, OnInput, OnSubmit ...).</summary>
        static bool InSwHandler(Toks t, Flow flow, int at)
        {
            for (var n = flow.Around(at); n != null; n = n.Parent)
                if (t.Id(n.Head - 2) && Regex.IsMatch(t.Tx(n.Head - 2), @"^On[A-Z]\w*$") && t.P(n.Head - 1, ":"))
                    return true;
            return false;
        }

        /// <summary>True if the token is in a try block whose catch shows the error to the user (Sw.Fail(e), a toast, a field / status error).</summary>
        static bool CaughtAndShown(Toks t, int at)
        {
            for (int k = at; k >= 0; --k)
            {
                if (!t.P(k, "{") || !t.Id(k - 1, "try"))
                    continue;
                int c = t.Match(k);
                if ((c > at) && t.Id(c + 1, "catch"))
                {
                    int b = t.P(c + 2, "(") ? t.Match(c + 2) + 1 : c + 2;
                    if (t.P(b, "{") && (t.Match(b) is int be) && Regex.IsMatch(t.Span(b, be), @"\bSw\.(?:Fail|Toast|Info|Message)\s*\(|\.(?:SetError|SetStatus|ShowError)\s*\(|\bstatus\w*\.Set\s*\("))
                        return true;
                }
            }
            return false;
        }

        /// <summary>True if the innermost function around the token is a user action's handler: a Sw option On*, a dialog button's OnClick, an event listener of a user event.</summary>
        static bool InUserHandler(Toks t, Flow flow, List<Reg> regs, int at)
        {
            var n = flow.Around(at);
            if (n == null)
                return false;
            if (t.Id(n.Head - 2) && Regex.IsMatch(t.Tx(n.Head - 2), @"^On[A-Z]\w*$") && t.P(n.Head - 1, ":"))
                return true;
            return regs.Any(r => (r.Fn == n) && UserEvents.Contains(r.Evt));
        }

        /// <summary>
        /// A part of an html value that is neither a literal nor escaped (null when the value is safe): a variable or member (item.Name), a
        /// call that doesn't escape, a substitution of a template; a local variable or helper that builds such markup (followed once).
        /// </summary>
        static String UnsafeHtml(Toks t, Flow flow, int a, int b, int depth)
        {
            //  A literal, a translation, an empty string: safe
            if ((a == b) && ((t.S[a].Kind == JsTokenKind.String) || (t.S[a].Kind == JsTokenKind.Template)))
                return null;
            if (IsTrCall(t, (a, b)))
                return null;
            var ops = new List<(int A, int B)>();
            {
                int s0 = a;
                for (int k = a; k <= b; ++k)
                {
                    if (t.P(k, "(") || t.P(k, "[") || t.P(k, "{"))
                    {
                        int m = t.Match(k);
                        if ((m < 0) || (m > b))
                            break;
                        k = m;
                        continue;
                    }
                    if (t.P(k, "+") || t.P(k, "?") || t.P(k, ":") || t.P(k, "||") || t.P(k, "&&") || t.P(k, "??"))
                    {
                        if (k > s0)
                            ops.Add((s0, k - 1));
                        s0 = k + 1;
                    }
                }
                if (s0 <= b)
                    ops.Add((s0, b));
            }
            bool markup = false;
            String bad = null;
            foreach (var o in ops)
            {
                if (IsLiteralRange(t, o))
                {
                    if (Literals(t, o).IndexOf('<') >= 0)
                        markup = true;
                    continue;
                }
                //  A template: its literal parts and substitutions
                if (t.S[o.A].Kind == JsTokenKind.TemplateHead)
                {
                    for (int k = o.A; k <= o.B; ++k)
                    {
                        if ((t.S[k].IsTemplate) && t.Tx(k).IndexOf('<') >= 0)
                            markup = true;
                        if ((t.S[k].Kind == JsTokenKind.TemplateHead || t.S[k].Kind == JsTokenKind.TemplateMiddle) && (k + 1 <= o.B))
                        {
                            int e = k + 1;
                            for (int d = 0; e <= o.B; ++e)
                            {
                                if (t.P(e, "(") || t.P(e, "[") || t.P(e, "{"))
                                    ++d;
                                else if (t.P(e, ")") || t.P(e, "]") || t.P(e, "}"))
                                    --d;
                                else if ((d == 0) && (t.S[e].Kind == JsTokenKind.TemplateMiddle || t.S[e].Kind == JsTokenKind.TemplateTail))
                                    break;
                            }
                            if ((e - 1 >= k + 1) && (bad == null) && (UnsafePart(t, flow, k + 1, e - 1, depth) is String u))
                                bad = u;
                        }
                    }
                    continue;
                }
                if ((bad == null) && (UnsafePart(t, flow, o.A, o.B, depth) is String u2))
                {
                    bad = u2;
                    //  A builder (a call / variable that returns markup) counts as markup
                    if (u2.StartsWith("\u0001", StringComparison.Ordinal))
                    {
                        markup = true;
                        bad = u2.Substring(1);
                    }
                }
            }
            //  One part only (el.innerHTML = name): unsafe when it is data; with literals: only when they are markup
            if (bad == null)
                return null;
            //  (a fallback for nothing is still one part: el.innerHTML = msg || "")
            bool oneWithFallback = (ops.Count == 2) && IsLiteralRange(t, ops[1]) && (Literals(t, ops[1]).Trim().Length == 0) && (t.P(ops[1].A - 1, "||") || t.P(ops[1].A - 1, "??"));
            if (((ops.Count == 1) || oneWithFallback) && (t.S[a].Kind != JsTokenKind.TemplateHead))
                return markup || IsDataName(bad) ? bad : null;
            return markup ? bad : null;
        }

        //  (round 4: also a camel case name that ends in such a word: CustomerMessage, userNote, d.ReplyText)
        static bool IsDataName(String s) => Regex.IsMatch(s, @"(?i)\.(?:name|title|note|notes|text|message|msg|comment|description|desc|subject|label|value|user|author|email|address|reason)$|^(?:name|title|note|notes|message|comment|description|text|label|value)$")
            || Regex.IsMatch(s, @"(?:^|[a-z.])(?:Name|Title|Note|Notes|Message|Msg|Comment|Comments|Description|Subject|Reason|Address|Bio|Remark|Remarks)$");

        /// <summary>True if the requests written in a token range are all reads (Get / Cached, a fetch without a write method, a Get* / List* / *Table api).</summary>
        static bool OnlyReadsIn(Toks t, int a, int b)
        {
            bool any = false;
            for (int q = a; q <= b; ++q)
            {
                if (!IsRequest(t, q))
                    continue;
                any = true;
                if (t.Id(q, "Get") || t.Id(q, "Cached"))
                    continue;
                if (t.Id(q, "fetch") && !Regex.IsMatch(t.Span(q, Math.Max(q, t.Match(q + 1))), @"(?i)method\s*:\s*[""'](?:POST|PUT|DELETE|PATCH)"))
                    continue;
                var wa = t.Args(q + 1);
                var wu = IsApiCall(t, q) ? t.Tx(q) : (wa.Count > 0 && (t.S[wa[0].A].Kind == JsTokenKind.String || t.S[wa[0].A].IsTemplate || t.Id(wa[0].A)) ? Literals(t, wa[0]).Trim().Split(' ').LastOrDefault() ?? "" : "");
                if ((wu.Length > 0) && (ReadUrl.IsMatch(wu) || Regex.IsMatch(wu, @"Table(?:[?#].*)?$")))
                    continue;
                return false;
            }
            return any;
        }

        /// <summary>A part of an html value (no "+" at its top level): null when it is safe, else its text ("\u0001" + text for a markup builder).</summary>
        static String UnsafePart(Toks t, Flow flow, int a, int b, int depth)
        {
            if (a > b)
                return null;
            if ((t.P(a, "(") && (t.Match(a) == b)))
                return UnsafeHtml(t, flow, a + 1, b - 1, depth) is String inner ? inner : null;
            if ((a == b) && (t.S[a].Kind == JsTokenKind.Number || t.Id(a, "true") || t.Id(a, "false") || t.Id(a, "null")))
                return null;
            var span = t.Span(a, b);
            //  A call: escaping / formatting / translation calls are safe; a builder is followed
            int last = b;
            if (t.P(b, ")"))
            {
                int open = -1;
                for (int k = b, d = 0; k >= a; --k)
                {
                    if (t.P(k, ")"))
                        ++d;
                    else if (t.P(k, "(") && (--d == 0))
                    {
                        open = k;
                        break;
                    }
                }
                if ((open > a) && t.Id(open - 1))
                {
                    var callee = t.Tx(open - 1);
                    //  The framework's own html (Sw.Markdown.ToHtml, Sw.Format.JsonToHtml, Sw.Icon ...) is its responsibility
                    if (t.Id(ChainStartOf(t, open - 1), "Sw"))
                        return null;
                    if (EscapeCall.IsMatch(callee) || TrFunctions.Contains(callee) || Regex.IsMatch(callee, @"^(?:join|map|toString|trim|toUpperCase|toLowerCase|padStart|padEnd)$") && EscapeCallInside(t, a, b))
                        return null;
                    if (Regex.IsMatch(callee, @"^(?:join|map)$"))
                        return null;
                    //  A local helper that builds markup from its arguments, or one named *Html* / *Markup*
                    var g = flow.NamedFn(callee);
                    bool builder = Regex.IsMatch(callee, @"(?i)html|markup|template|render") || ((g != null) && (depth < 1) && BuildsMarkup(t, flow, g, depth + 1));
                    if (builder && (t.Args(open).Count > 0))
                        return "\u0001" + span;
                    return null;
                }
                return null;
            }
            //  A number-ish member (x.length, x.Count) is safe
            if (Regex.IsMatch(span, @"\.(?:length|Count|size|Id|id|Index|index)$") || Regex.IsMatch(span, @"^(?:i|j|k|n|count|total|index|idx|pct|percent|width|height|size|px)$"))
                return null;
            if (t.Id(b) || t.P(b, "]"))
            {
                //  A local variable assigned markup with data earlier: follow it once
                if ((a == b) && (depth < 2))
                {
                    var nm = t.Tx(a);
                    for (int k = 0; k + 2 < t.N; ++k)
                        if (t.Id(k, nm) && t.P(k + 1, "=") && !t.P(k + 2, "=") && (t.Id(k - 1, "const") || t.Id(k - 1, "let") || t.Id(k - 1, "var") || t.P(k - 1, ";") || t.P(k - 1, "{")))
                        {
                            var r = UnsafeHtml(t, flow, k + 2, ExprEnd(t, k + 2), depth + 1);
                            return r != null ? "\u0001" + nm + " (" + r + ")" : null;
                        }
                }
                //  A value that is html already (html, markup, svg, an icon) or a constant table (LineIcons[name]) is not user text
                if (Regex.IsMatch(t.Tx(b), @"(?i)html|markup|svg|icon") || ((a == b) && Regex.IsMatch(t.Tx(b), @"^(?:body|doc|document)$")) || (t.P(b, "]") && Regex.IsMatch(span, @"^[A-Z][A-Za-z0-9_]*\[")))
                    return null;
                return span;
            }
            return null;
        }

        static bool EscapeCallInside(Toks t, int a, int b)
        {
            for (int k = a; k <= b; ++k)
                if (t.Id(k) && t.P(k + 1, "(") && EscapeCall.IsMatch(t.Tx(k)))
                    return true;
            return false;
        }

        /// <summary>True if a function returns markup built with a value that isn't escaped.</summary>
        static bool BuildsMarkup(Toks t, Flow flow, FnNode g, int depth)
        {
            if (!g.F.Block)
                return UnsafeHtml(t, flow, g.F.Start, g.F.End, depth) != null;
            foreach (var j in flow.Own(g))
                if (t.Id(j, "return") && !t.P(j + 1, ";"))
                {
                    int e = ExprEndStatement(t, j + 1);
                    if (UnsafeHtml(t, flow, j + 1, e, depth) != null)
                        return true;
                }
            return false;
        }

        /// <summary>
        /// The builders of a file: a function or method named *Html* / *Markup* / *Template* / *Render* (or Row / Item / Card) whose result
        /// concatenates markup with a value that isn't escaped (reported at the return: the markup is made from data there).
        /// </summary>
        static void CheckBuilders(Checker.FileContext ctx, Toks t, Flow flow)
        {
            foreach (var g in flow.Fns)
            {
                if ((g.Name == null) || !Regex.IsMatch(g.Name, @"(?i)html|markup|template|render"))
                    continue;
                int at = -1;
                String bad = null;
                if (!g.F.Block)
                {
                    bad = UnsafeHtml(t, flow, g.F.Start, g.F.End, 1);
                    at = g.F.Start;
                }
                else
                    foreach (var j in flow.Own(g))
                        if (t.Id(j, "return") && !t.P(j + 1, ";") && (UnsafeHtml(t, flow, j + 1, ExprEndStatement(t, j + 1), 1) is String u))
                        {
                            bad = u;
                            at = j;
                            break;
                        }
                if ((bad != null) && (at >= 0))
                    Add(ctx, Rule.UxSec2, t.S[at].Start, "\"" + g.Name + "\" builds markup with \"" + Short(bad) + "\", which is not escaped: user data becomes markup when the result is set as innerHTML, so <img src=x onerror=...> in a name runs as code; build the elements with Sw.el(tag, { text: value }) (or escape every value)");
            }
        }
    }
}
