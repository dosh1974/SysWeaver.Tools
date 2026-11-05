using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    public static partial class UxChecker
    {
        // ------------------------------------------------------------------ rules on the call structure and the value flow
        //
        //  One rule per meaning, whatever the spelling: a write of a property reaches the same check through x.p = v, x["p"] = v,
        //  Object.assign(x, { p }), setAttribute("p") or the attributes of Sw.el; a style through style.p, style["p"], setProperty,
        //  cssText, setAttribute("style") or Object.assign(x.style, ...); markup in a string is scanned as markup.

        static readonly Regex SavedText = new Regex(@"^\s*(?:(?:changes?|settings?|all|everything)\s+)?(?:saved|done|updated|applied|stored|success(?:ful(?:ly)?)?|ok)\b[\s.!]*$|^\s*(?:successfully|saved)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex SecretField = new Regex(@"(?i)api.?key|secret|token|passw|passphrase|private.?key|access.?key|\bpin\b|credential|\bkey\b", RegexOptions.CultureInvariant);
        static readonly Regex SecretLiteral = new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----|\bsk_(?:live|test)_[0-9A-Za-z]{10,}|\bghp_[0-9A-Za-z]{20,}|\bAKIA[0-9A-Z]{16}\b|\bxox[abprs]-[0-9A-Za-z-]{10,}", RegexOptions.CultureInvariant);
        static readonly Regex BadReason = new Regex(@"^(?:no|n/?a|x|-|\?|disabled|not available|unavailable|off|false|none|nope|locked|later)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ReservedCombo = new Regex(@"^(?:(?:Mod|Ctrl|Control|Meta|Cmd)\+(?:Shift\+)?(?:W|T|N|Q|Tab|PageUp|PageDown)|Alt\+(?:ArrowLeft|ArrowRight|Left|Right|Home|F4))$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly HashSet<String> PageFlowObjects = new HashSet<String>(StringComparer.Ordinal) { "document", "window", "body", "documentElement", "html" };

        /// <summary>camelCase style property to css: transitionDuration -> transition-duration.</summary>
        static String Kebab(String p) => p.StartsWith("--", StringComparison.Ordinal) ? p : Regex.Replace(p, "[A-Z]", m => "-" + m.Value.ToLowerInvariant()).TrimStart('-').ToLowerInvariant();

        /// <summary>The end (last token) of the expression that starts at a: up to a "," / ";" / closing bracket at depth 0, or a new line that starts a statement.</summary>
        static int ExprEnd(Toks t, int a)
        {
            int e = a;
            for (; e < t.N; ++e)
            {
                if (t.P(e, "(") || t.P(e, "[") || t.P(e, "{") || t.P(e, "${"))
                {
                    int m = t.Match(e);
                    if (m < 0)
                        return t.N - 1;
                    e = m;
                    continue;
                }
                if (t.P(e, ",") || t.P(e, ";") || t.P(e, ")") || t.P(e, "]") || t.P(e, "}"))
                    break;
            }
            return Math.Max(a, e - 1);
        }

        /// <summary>The literal text of a value range: a string / _TF literal, else the joined literal parts (null if there are none).</summary>
        static String ValueText(Toks t, (int A, int B) v)
        {
            if (t.Lit(v) is String s)
                return s;
            var l = Literals(t, v);
            return l.Length > 0 ? l : null;
        }

        /// <summary>The css declarations of a style text ("a: b; c: d").</summary>
        static IEnumerable<(String Prop, String Value)> StyleDecls(String css)
        {
            foreach (var part in css.Split(';'))
            {
                int c = part.IndexOf(':');
                if (c > 0)
                    yield return (part.Substring(0, c).Trim().ToLowerInvariant(), part.Substring(c + 1).Trim());
            }
        }

        /// <summary>The motion and cursor rules of one style value set from script (style.x, setProperty, cssText, Object.assign).</summary>
        static void CheckStyleValue(Checker.FileContext ctx, int at, String prop, String value, bool page, bool styleString = false)
        {
            if (value == null)
                return;
            var v = value.Trim();
            bool reduced = false;
            //  (a whole style string, cssText / setAttribute("style"), is checked for durations and cursor: wait as a string literal already)
            if (!styleString && MotionProps.Contains(prop) && !Regex.IsMatch(v, @"\binfinite\b", RegexOptions.IgnoreCase))
            {
                var tm = TimeValue.Matches(StripVar(v)).Cast<Match>().FirstOrDefault(x => Ms(x) > 0);
                if (tm != null)
                    Add(ctx, Rule.UxMot2, at, "the style " + prop + ": \"" + Short(v) + "\" set from script hard-codes " + tm.Value + ": use var(--sw-dur-1 / -2 / -3) (each theme sets them, reduced motion makes them 1 ms)");
                else if (Easing.IsMatch(StripVar(v)) && !Regex.IsMatch(prop, "duration|delay"))
                    Add(ctx, Rule.UxMot2, at, "the style " + prop + ": \"" + Short(v) + "\" set from script hard-codes an easing: use var(--sw-ease)");
            }
            if (!reduced && ((prop == "transition") || (prop == "transition-property")))
            {
                var lm = LayoutProp.Match(StripVar(v));
                if (lm.Success)
                    Add(ctx, Rule.UxMot4, at, "the style " + prop + ": \"" + Short(v) + "\" set from script animates " + lm.Groups["p"].Value + ": the layout moves every frame; animate transform / opacity (or grid-template-rows 0fr to 1fr for an expanding section)");
                else if (Regex.IsMatch(StripVar(v), @"(?:^|,)\s*all\b", RegexOptions.IgnoreCase))
                    Add(ctx, Rule.UxMot4, at, "the style " + prop + ": \"" + Short(v) + "\" set from script animates all properties, the layout ones too; name them: opacity var(--sw-dur-1), background-color var(--sw-dur-1)");
            }
            if (prop == "cursor")
            {
                var cv = v.Replace("!important", "").Trim().ToLowerInvariant();
                if ((cv == "wait") && !styleString)
                    Add(ctx, Rule.UxInp10, at, "cursor \"wait\" set from script: never used (it says the whole app is blocked); a busy control shows cursor: progress on itself (Sw.Button does it)");
                else if ((cv == "progress") && page)
                    Add(ctx, Rule.UxInp10, at, "cursor \"progress\" on the whole page set from script: only the busy control shows it (Sw.Button with a promise from OnClick), the page stays usable");
            }
        }

        /// <summary>True if the token range is a page wide element: document.body, document.documentElement, html, body, a variable made as one.</summary>
        static bool IsPageReceiver(Toks t, int recvEnd, Dictionary<String, (String Kind, int At, int Call)> vars)
        {
            if (!t.Id(recvEnd))
                return false;
            var n = t.Tx(recvEnd);
            if (PageFlowObjects.Contains(n))
                return true;
            return !t.P(recvEnd - 1, ".") && vars.TryGetValue(n, out var v) && ((v.Kind == "body") || (v.Kind == "html") || (v.Kind == "main"));
        }

        /// <summary>
        /// The rules that follow values and calls (see the section comment). Returns the variables made content editable.
        /// </summary>
        static Dictionary<String, int> CheckFlow(Checker.FileContext ctx, Toks t, Flow flow, Dictionary<String, (String Kind, int At, int Call)> vars, List<(int A, int B)> dialogCalls, bool framework)
        {
            var text = ctx.Text;
            var editable = new Dictionary<String, int>(StringComparer.Ordinal);
            bool pageHeader = Regex.IsMatch(text, @"\bSw\.(?:PageLayout|PageHeader)\s*\(");
            bool hasDraft = Regex.IsMatch(text, @"\bSw\.Draft\b|\bDraft\s*:");
            var draftContexts = new Dictionary<String, int>(StringComparer.Ordinal);
            var combos = new Dictionary<String, int>(StringComparer.OrdinalIgnoreCase);
            var twiceReported = new HashSet<int>();
            //  Sw value controls with a Draft option: name -> context
            var draftedControls = new Dictionary<String, String>(StringComparer.Ordinal);
            foreach (var kv in vars)
                if (FieldControls.Contains(kv.Value.Kind) && (kv.Value.Call > 0) && t.P(kv.Value.Call + 1, "{") && (ObjKey(t, kv.Value.Call + 1, "Draft") is var dv) && (dv != null))
                    draftedControls[kv.Key] = ValueText(t, dv.Value) ?? "";

            bool IsButton(int recvEnd, out String name, out bool shim)
            {
                shim = t.Id(recvEnd, "E") && t.P(recvEnd - 1, ".");
                name = shim ? t.Tx(recvEnd - 2) : t.Tx(recvEnd);
                if (!t.Id(recvEnd))
                    return false;
                if (shim)
                    return vars.TryGetValue(name, out var sv) && sv.Kind.StartsWith("Sw.", StringComparison.Ordinal);
                if (t.P(recvEnd - 1, "."))
                    return ButtonName.IsMatch(name);
                return (vars.TryGetValue(name, out var v) && (v.Kind == "button" || (v.Kind == "input" && false))) || ButtonName.IsMatch(name);
            }
            void DisabledWrite(int recvEnd, int at, String how)
            {
                if (!IsButton(recvEnd, out var name, out var shim))
                    return;
                Add(ctx, Rule.UxInp12, t.S[at].Start, (shim ? name + ".E" : name) + ": " + how + " sets the native disabled: " + (shim ? "it gives no reason (\"Not available right now\"); use " + name + ".SetDisabled(true, \"Select a row first\")" : "the button leaves the tab order and its reason can't be read; use Sw.Disabled.Set(" + name + ", true, \"Select a row first\")"));
            }
            bool Truthy((int A, int B) v) => !(t.Is(v, "false") || t.Is(v, "null") || t.Is(v, "undefined") || (t.Str(v.A) == "false"));

            for (int i = 0; i < t.N; ++i)
            {
                //  ---- property writes: x["p"] = v and Object.assign(x, { p: v })
                if ((t.Str(i) is String bp) && t.P(i - 1, "[") && t.P(i + 1, "]") && t.P(i + 2, "=") && !t.P(i + 3, "="))
                {
                    var val = (i + 3, ExprEnd(t, i + 3));
                    if ((bp == "disabled") && Truthy(val))
                        DisabledWrite(i - 2, i, "[\"disabled\"] = ...");
                    if (t.Id(i - 2, "style"))
                        CheckStyleValue(ctx, t.S[i].Start, Kebab(bp), ValueText(t, val), IsPageReceiver(t, i - 4, vars));
                    if (bp.Equals("contentEditable", StringComparison.OrdinalIgnoreCase) && t.Id(i - 2))
                        editable.TryAdd(t.Tx(i - 2), i);
                }
                if (t.Chain(i, "Object", "assign") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count >= 2) && t.P(a[1].A, "{"))
                    {
                        int recv = a[0].B;
                        bool style = t.Id(recv, "style") && t.P(recv - 1, ".");
                        int close = t.Match(a[1].A);
                        for (int k = a[1].A + 1; k < close; ++k)
                        {
                            if (t.P(k, "{") || t.P(k, "(") || t.P(k, "["))
                            {
                                k = t.Match(k);
                                if (k < 0)
                                    break;
                                continue;
                            }
                            if (!(t.P(k - 1, "{") || t.P(k - 1, ",")) || !t.P(k + 1, ":"))
                                continue;
                            var key = t.Id(k) ? t.Tx(k) : t.Str(k);
                            if (key == null)
                                continue;
                            var val = (k + 2, ExprEnd(t, k + 2));
                            if (style)
                                CheckStyleValue(ctx, t.S[k].Start, Kebab(key), ValueText(t, val), IsPageReceiver(t, recv - 2, vars));
                            else if ((key == "disabled") && Truthy(val))
                                DisabledWrite(recv, k, "Object.assign(..., { disabled })");
                            else if (key.Equals("contentEditable", StringComparison.OrdinalIgnoreCase) && t.Id(recv))
                                editable.TryAdd(t.Tx(recv), k);
                        }
                    }
                }
                //  ---- styles: style.setProperty("p", v), style.cssText = "...", setAttribute("style", "...")
                if (t.Id(i, "setProperty") && t.P(i - 1, ".") && t.Id(i - 2, "style") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count >= 2) && (t.Str(a[0].A) is String sp))
                        CheckStyleValue(ctx, t.S[i].Start, sp.ToLowerInvariant(), ValueText(t, a[1]), IsPageReceiver(t, i - 4, vars));
                }
                if (t.Id(i, "cssText") && t.P(i - 1, ".") && t.Id(i - 2, "style") && (t.P(i + 1, "=") || t.P(i + 1, "+=")))
                {
                    if (ValueText(t, (i + 2, ExprEnd(t, i + 2))) is String css)
                        foreach (var (p, v) in StyleDecls(css))
                            CheckStyleValue(ctx, t.S[i].Start, p, v, IsPageReceiver(t, i - 4, vars), true);
                }
                if (t.Id(i, "setAttribute") && t.P(i - 1, ".") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    var an = (a.Count >= 2) ? t.Str(a[0].A)?.ToLowerInvariant() : null;
                    if ((an == "style") && (ValueText(t, a[1]) is String css))
                        foreach (var (p, v) in StyleDecls(css))
                            CheckStyleValue(ctx, t.S[i].Start, p, v, IsPageReceiver(t, i - 2, vars), true);
                    if ((an == "contenteditable") && t.Id(i - 2))
                        editable.TryAdd(t.Tx(i - 2), i);
                }
                if (t.Id(i, "contentEditable") && t.P(i - 1, ".") && t.P(i + 1, "=") && t.Id(i - 2) && (t.Str(i + 2) != "false"))
                    editable.TryAdd(t.Tx(i - 2), i);

                //  ---- Sw.el(spec, { attrs }): contenteditable, click handlers on elements that are not controls, a dialog element
                if ((t.Chain(i, "Sw", "el") || (t.Id(i, "el") && !t.P(i - 1, "."))) && t.P(i + 1, "(") && (t.Str(i + 2) is String spec))
                {
                    var tag = Regex.Match(spec, @"^[A-Za-z][A-Za-z0-9-]*").Value.ToLowerInvariant();
                    if (tag.Length == 0)
                        tag = "div";
                    var a = t.Args(i + 1);
                    String target = (t.P(i - 1, "=") || t.P(i - 1, ":")) && t.Id(i - 2) ? t.Tx(i - 2) : (t.Chain(i, "Sw", "el") && t.P(i - 3, "=") && t.Id(i - 4) ? t.Tx(i - 4) : null);
                    if ((a.Count > 1) && t.P(a[1].A, "{"))
                    {
                        var span = t.Span(a[1].A, a[1].B);
                        if ((target != null) && Regex.IsMatch(span, @"(?i)[""']?contenteditable[""']?\s*:\s*(?![""']?false)"))
                            editable.TryAdd(target, i);
                        //  { onclick: fn } / { on: { click: fn } } on an element that is not a control
                        int hk = -1;
                        for (int k = a[1].A + 1; k < a[1].B; ++k)
                            if ((t.Id(k, "onclick") || (t.Id(k, "click") && Regex.IsMatch(t.Span(a[1].A, k), @"\bon\s*:\s*\{[^}]*$"))) && t.P(k + 1, ":"))
                            {
                                hk = k;
                                break;
                            }
                        if ((hk > 0) && NotControls.Contains(tag) && (flow.At(hk + 2) is FnNode hn))
                        {
                            var e = flow.Of(hn);
                            if (!e.Modifier && !ModifierCheck.IsMatch(t.Span(hn.F.Start, hn.F.End)))
                                Add(ctx, Rule.UxInp2, t.S[hk].Start, "a click handler in the attributes of the " + tag + " \"" + spec + "\" runs on a Ctrl / Shift / Alt click too (and the element is no control): use a button (Sw.Button / Sw.OnClick(el, fn)) or Sw.Card({ OnClick }), or start the handler with if (Sw.BadClick(ev)) return;");
                            if (!e.Asks)
                                ReportDestructive(ctx, t, e);
                        }
                    }
                    if (tag == "dialog")
                        Add(ctx, Rule.UxPop1, t.S[i].Start, "an own <dialog> made in script (Sw.el(\"" + spec + "\")): use Sw.Dialog.Open / Show (focus trap, scrim over the shell, Escape, phone layout, the dialog rules)");
                    if ((tag == "h1") && pageHeader && SameFunctionAsHeader(t, flow, i))
                        Add(ctx, Rule.UxAcc3, t.S[i].Start, "an <h1> made in script on a page whose page header already shows the title (Sw.PageLayout / Sw.PageHeader make the h1): one h1 per page; use h2 for sections");
                }
                if (t.Id(i, "createElement") && t.P(i + 1, "(") && (t.Str(i + 2)?.ToLowerInvariant() is String ct))
                {
                    if (ct == "dialog")
                        Add(ctx, Rule.UxPop1, t.S[i].Start, "an own <dialog> made in script (createElement(\"dialog\")): use Sw.Dialog.Open / Show (focus trap, scrim over the shell, Escape, phone layout, the dialog rules)");
                    if ((ct == "h1") && pageHeader && SameFunctionAsHeader(t, flow, i))
                        Add(ctx, Rule.UxAcc3, t.S[i].Start, "an <h1> made in script on a page whose page header already shows the title: one h1 per page; use h2 for sections");
                }
                //  role: "heading" with aria-level 1 in script attributes
                if (pageHeader && (t.Str(i) is String rv) && (rv == "heading") && t.P(i - 1, ":") && SameFunctionAsHeader(t, flow, i))
                {
                    int open = EnclosingObject(t, i);
                    if ((open >= 0) && Regex.IsMatch(t.Span(open, Math.Max(open, t.Match(open))), @"[""']?aria-level[""']?\s*:\s*[""']?1\b|ariaLevel\s*:\s*[""']?1\b"))
                        Add(ctx, Rule.UxAcc3, t.S[i].Start, "a role=\"heading\" aria-level=\"1\" made in script on a page whose page header already shows the title: one level 1 heading per page");
                }

                //  ---- markup in strings (innerHTML, insertAdjacentHTML, templates): fields, dialogs, disabled buttons, tab order, headings
                if (((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && t.Tx(i).IndexOf('<') >= 0)
                    CheckMarkupString(ctx, t, i, hasDraft, pageHeader);

                //  ---- ux-inp-12: Disabled: <a boolean expression> (no reason), a reason that says nothing
                if (t.Id(i, "Disabled") && t.Key(i))
                {
                    int open = EnclosingObject(t, i);
                    var v = (i + 2, ExprEnd(t, i + 2));
                    bool inDialog = (open > 0) && dialogCalls.Any(d => (d.A < open) && (open < d.B));
                    if ((open > 0) && (IsSwOptions(t, open) || inDialog) && !(t.Id(v.Item1, "true") && v.Item1 == v.Item2) && !(v.Item1 == v.Item2))
                    {
                        var span = t.Span(v.Item1, v.Item2);
                        //  !!x passes a caller's flag through (a legacy wrapper); in a dialog button spec it drops the reason too
                        bool boolean = (t.P(v.Item1, "!") && (inDialog || !t.P(v.Item1 + 1, "!"))) || Regex.IsMatch(span, @"===|!==|==|!=|<=|>=|(?<![=])[<>](?!=)|&&|\|\|") || Regex.IsMatch(span, @"\.(?:length|size|Count)\b|\b(?:is|has|can)[A-Z]\w*\(");
                        bool reason = Regex.IsMatch(span, @"[""'`]|\b_T[FH]*\s*\(|\?");
                        if (boolean && !reason)
                            Add(ctx, Rule.UxInp12, t.S[i].Start, "Disabled: " + Short(span) + " gives no reason: when it disables, the hint and the announcement say only \"Not available right now\"; pass the reason when disabled: Disabled: " + Short(span) + " ? _TF(\"Select a row first\", \"...\") : false (or SetDisabled(on, reason) later)");
                    }
                    if ((open > 0) && (t.Lit(v) is String why) && BadReason.IsMatch(why.Trim()))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, "Disabled: \"" + why + "\" is no reason: say why and what enables it (\"Select a row first\", \"Your role doesn't allow this\")");
                }
                if ((t.Id(i, "SetDisabled") && t.P(i - 1, ".") && t.P(i + 1, "(")) || (t.Chain(i, "Disabled", "Set") && t.P(i + 1, "(")))
                {
                    var a = t.Args(i + 1);
                    int ri = t.Tx(i) == "SetDisabled" ? 1 : 2;
                    if ((a.Count > ri) && (t.Lit(a[ri]) is String why) && BadReason.IsMatch(why.Trim()))
                        Add(ctx, Rule.UxInp12, t.S[i].Start, t.Tx(i) + "(..., \"" + why + "\"): \"" + why + "\" is no reason: say why and what enables it (\"Select a row first\", \"Your role doesn't allow this\")");
                }

                //  ---- ux-pop-9: the native dialogs by destructuring (const { confirm: ask } = window)
                if (t.Id(i) && NativeNamesPublic.Contains(t.Tx(i)) && (t.P(i - 1, "{") || t.P(i - 1, ",")) && (t.P(i + 1, ":") || t.P(i + 1, ",") || t.P(i + 1, "}")))
                {
                    int open = EnclosingObject(t, i);
                    int close = open >= 0 ? t.Match(open) : -1;
                    if ((close > 0) && t.P(close + 1, "=") && (t.Id(close + 2, "window") || t.Id(close + 2, "self") || t.Id(close + 2, "globalThis")) && !t.P(close + 3, "."))
                        Add(ctx, Rule.UxPop9, t.S[i].Start, "the native " + t.Tx(i) + "() taken from " + t.Tx(close + 2) + " by destructuring: " + (t.Tx(i) == "confirm" ? "Sw.Dialog.Confirm (Danger: true for a destructive action) or Sw.Cells.ConfirmDanger" : t.Tx(i) == "prompt" ? "Sw.Dialog.Prompt" : "Sw.Toast / Sw.Dialog.Alert") + ": it blocks the page, ignores the theme and the dialog rules (\"OK\" buttons, no title)");
                }

                //  ---- ux-inp-23: Alt+Left / Alt+Right in any spelling, browser shortcuts, a combo registered twice
                if ((t.Id(i, "altKey") && !t.P(i - 1, "!") && !t.P(i - 3, "!")) || (t.Id(i, "getModifierState") && t.P(i + 1, "(") && (t.Str(i + 2) == "Alt")))
                {
                    if (t.Id(i, "getModifierState") || !t.Id(i - 1, "!"))
                    {
                        var fnode = flow.Around(i);
                        int st = StatementStart(t, fnode?.From ?? 0, i), en = i;
                        for (int d = 0; en < t.N; ++en)
                        {
                            if (t.P(en, "(") || t.P(en, "["))
                                ++d;
                            else if (t.P(en, ")") || t.P(en, "]"))
                            {
                                if (--d < 0)
                                    break;
                            }
                            else if ((d == 0) && (t.P(en, ";") || t.P(en, "{") || t.P(en, "}")))
                                break;
                        }
                        bool negated = t.Id(i, "getModifierState") ? t.P(ChainStartOf(t, i) - 1, "!") : false;
                        for (int k = st; !negated && (k < en); ++k)
                        {
                            var s = t.Str(k);
                            bool arrow = (s == "ArrowLeft") || (s == "ArrowRight") || (s == "Left") || (s == "Right")
                                || ((t.S[k].Kind == JsTokenKind.Number) && (t.Tx(k) == "37" || t.Tx(k) == "39") && Regex.IsMatch(t.Span(Math.Max(st, k - 3), k), @"\b(?:keyCode|which)\b"));
                            if (arrow)
                            {
                                Add(ctx, Rule.UxInp23, t.S[i].Start, "a shortcut on Alt+" + (s ?? (t.Tx(k) == "37" ? "Left" : "Right")) + ": Alt+Left / Alt+Right are the browser's Back / Forward; use another key combination (and name it in the tool tip and aria-keyshortcuts)");
                                break;
                            }
                        }
                    }
                }
                if (t.Chain(i, "Keys", "Add") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if ((a.Count > 0) && (t.Lit(a[0]) is String combo))
                    {
                        //  Ctrl / Cmd / Meta / Control are one modifier for Sw.Keys ("Mod"): "Ctrl+S" and "Mod+S" are the same combination
                        var norm = Regex.Replace(Regex.Replace(combo, @"\s+", ""), @"(?i)\b(?:Ctrl|Control|Cmd|Meta|Command)\+", "Mod+");
                        if (ReservedCombo.IsMatch(norm))
                            Add(ctx, Rule.UxInp23, t.S[a[0].A].Start, "the shortcut \"" + combo + "\" belongs to the browser (" + (Regex.IsMatch(norm, "^Alt", RegexOptions.IgnoreCase) ? "Back / Forward" : "close / new tab or window") + "): the page never gets it, or takes it from the user; use another combination");
                        else if (combos.TryGetValue(norm, out var first) && !Regex.IsMatch(t.Span(i, Math.Max(i, t.Match(i + 1))), @"\bReplace\s*:\s*true\b"))
                            Add(ctx, Rule.UxInp23, t.S[a[0].A].Start, "the shortcut \"" + combo + "\" is registered again (line " + ctx.Source.GetLineCol(t.S[first].Start).Line + "): only the last handler runs, the first one is dead but still listed in the help; one handler per combination (or pass { Replace: true } when that is intended)");
                        else
                            combos[norm] = a[0].A;
                    }
                }

                //  ---- ux-saf-11: own drafts by bracket / property, one Context for two drafts, a control drafted twice
                if ((t.Id(i, "localStorage") || t.Id(i, "sessionStorage")) && !t.P(i - 1, "."))
                {
                    String key = null;
                    int vAt = -1;
                    if (t.P(i + 1, "[") && (t.Match(i + 1) is int bc) && (bc > i) && t.P(bc + 1, "=") && !t.P(bc + 2, "="))
                    {
                        key = Literals(t, (i + 2, bc - 1));
                        vAt = bc + 2;
                    }
                    else if (t.P(i + 1, ".") && t.Id(i + 2) && t.P(i + 3, "=") && !t.P(i + 4, "=") && !Regex.IsMatch(t.Tx(i + 2), "^(?:setItem|getItem|removeItem|clear|key|length)$"))
                    {
                        key = t.Tx(i + 2);
                        vAt = i + 4;
                    }
                    //  setItem(key, value) too: a key named *draft*, or the typed text of a field as the value
                    if ((key == null) && t.P(i + 1, ".") && t.Id(i + 2, "setItem") && t.P(i + 3, "(") && (t.Args(i + 3) is var sa) && (sa.Count > 1))
                    {
                        key = Literals(t, sa[0]);
                        vAt = sa[1].A;
                    }
                    bool typed = (vAt > 0) && Regex.IsMatch(t.Span(vAt, ExprEnd(t, vAt)), @"\.(?:value|textContent|innerText|innerHTML)\b|\.Get\(\s*\)");
                    bool viaSetItem = t.P(i + 1, ".") && t.Id(i + 2, "setItem");
                    if ((key != null) && (viaSetItem ? (typed && !DraftKey.IsMatch(key)) : (DraftKey.IsMatch(key) || typed)))
                        Add(ctx, Rule.UxSaf11, t.S[i].Start, "an own draft in " + t.Tx(i) + " (\"" + Short(key) + "\"" + (typed && !DraftKey.IsMatch(key) ? ", the typed text of a field" : "") + "): use Sw.Draft.Attach(field, { Context: \"...\" }) (debounced, per user, 7 days, never secrets, the restore notice with Discard, cleared on sign out) and Clear() it after the send / save");
                }
                if (t.Chain(i, "Draft", "Attach") && t.P(i + 1, "("))
                {
                    var a = t.Args(i + 1);
                    if (a.Count > 0)
                    {
                        //  The field: x, x.Input, x.E (a Sw control), or an element made with a label / name
                        String fname = t.Id(a[0].A) ? t.Tx(a[0].A) : null;
                        var ctxv = (a.Count > 1) && t.P(a[1].A, "{") ? ObjKey(t, a[1].A, "Context") : null;
                        var dctx = ctxv != null ? ValueText(t, ctxv.Value) : null;
                        if ((dctx != null) && (ctxv.Value.A == ctxv.Value.B || t.Lit(ctxv.Value) != null))
                        {
                            if (draftContexts.TryGetValue(dctx, out var first))
                                Add(ctx, Rule.UxSaf11, t.S[ctxv.Value.A].Start, "the draft Context \"" + dctx + "\" is also used by another draft (line " + ctx.Source.GetLineCol(t.S[first].Start).Line + "): one Context per field, else the last field written wins and the other text is lost on a reload");
                            else
                                draftContexts[dctx] = i;
                        }
                        if ((fname != null) && draftedControls.ContainsKey(fname))
                            Add(ctx, Rule.UxSaf11, t.S[a[0].A].Start, "\"" + fname + "\" has a Draft option already and gets a second Sw.Draft.Attach: the restore notice is lost and the two drafts overwrite each other; keep one (the Draft option)");
                        if ((fname != null) && vars.TryGetValue(fname, out var fv) && (fv.Call > 0) && (t.Match(fv.Call) is int fc) && (fc > fv.Call) && SecretField.IsMatch(Literals(t, (fv.Call + 1, fc - 1))) && !Regex.IsMatch(t.Span(fv.Call, fc), @"(?i)type[""']?\s*:\s*[""']password"))
                            Add(ctx, Rule.UxSec1, t.S[a[0].A].Start, "a draft of the secret field \"" + fname + "\" (\"" + Short(SecretField.Match(Literals(t, (fv.Call + 1, fc - 1))).Value) + "\"): drafts are kept in local storage for 7 days in plain text; never draft keys, tokens or passwords (no Sw.Draft on this field)");
                    }
                }
                //  A Sw control whose label says secret, with a Draft option
                if (t.Id(i, "Draft") && t.Key(i) && (EnclosingObject(t, i) is int dopen) && (dopen > 0) && IsSwOptions(t, dopen) && (ObjKey(t, dopen, "Label") is var lab) && (lab != null) && (ValueText(t, lab.Value) is String lt) && SecretField.IsMatch(lt))
                    Add(ctx, Rule.UxSec1, t.S[i].Start, "a draft of the secret field \"" + lt + "\": drafts are kept in local storage for 7 days in plain text; never draft keys, tokens or passwords (drop Draft)");
                //  A Sw control with a Draft option whose AriaLabel / Placeholder / Type / Context says secret (the Label case is above)
                if (t.Id(i, "Draft") && t.Key(i) && (EnclosingObject(t, i) is int sopen) && (sopen > 0) && IsSwOptions(t, sopen) && !t.Is((i + 2, ExprEnd(t, i + 2)), "false"))
                {
                    var said = new[] { "AriaLabel", "Placeholder", "Name" }.Select(k => ObjKey(t, sopen, k)).Where(x => x != null).Select(x => ValueText(t, x.Value)).FirstOrDefault(x => (x != null) && SecretField.IsMatch(x));
                    var dval = ValueText(t, (i + 2, ExprEnd(t, i + 2)));
                    bool pwd = (ObjKey(t, sopen, "Type") is var ty) && (ty != null) && (t.Lit(ty.Value) == "password");
                    var lab2 = ObjKey(t, sopen, "Label");
                    bool labelled = (lab2 != null) && (ValueText(t, lab2.Value) is String l2) && SecretField.IsMatch(l2);
                    if (!labelled && ((said != null) || pwd || ((dval != null) && SecretField.IsMatch(dval))))
                        Add(ctx, Rule.UxSec1, t.S[i].Start, "a draft of a secret field (" + (pwd ? "Type: \"password\"" : "\"" + Short(said ?? dval) + "\"") + "): drafts are kept in local storage for 7 days in plain text and the framework refuses them; never draft keys, tokens or passwords (drop Draft)");
                }
                //  ---- ux-fbk-9: ShouldReportAsync without await in a condition, .then on the boolean ShouldReport
                if (t.Chain(i, "Net", "ShouldReportAsync") && t.P(i + 1, "("))
                {
                    int s0 = ChainStartOf(t, i);
                    int e0 = t.Match(i + 1);
                    bool awaited = t.Id(s0 - 1, "await") || (t.P(s0 - 1, "(") && t.Id(s0 - 2, "await"));
                    bool cond = t.P(s0 - 1, "!") || (t.P(s0 - 1, "(") && (t.Id(s0 - 2, "if") || t.Id(s0 - 2, "while"))) || t.P(e0 + 1, "?") || t.P(e0 + 1, "&&") || t.P(e0 + 1, "||") || t.P(s0 - 1, "&&") || t.P(s0 - 1, "||");
                    if (!awaited && cond)
                        Add(ctx, Rule.UxFbk9, t.S[i].Start, "Sw.Net.ShouldReportAsync(e) is a Promise, always true in a condition: use the boolean Sw.Net.ShouldReport(e), or await ShouldReportAsync(e)");
                }
                if (t.Chain(i, "Net", "ShouldReport") && t.P(i + 1, "(") && (t.Match(i + 1) is int src) && (src > 0) && t.P(src + 1, ".") && t.Id(src + 2, "then"))
                    Add(ctx, Rule.UxFbk9, t.S[i].Start, "Sw.Net.ShouldReport(e) returns a boolean now (no .then): if (!Sw.Net.ShouldReport(e)) return; (or await Sw.Net.ShouldReportAsync(e) to wait for the connection check)");
                //  ---- ux-fbk-4: an own loop on the server job api (Sw.Activity.StartServer / RestoreServer show it in the activity list)
                if (!framework && ((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && Regex.IsMatch(t.Tx(i), @"\bjobs/(?:WaitJob|GetJob)\b"))
                    Add(ctx, Rule.UxFbk4, t.S[i].Start, "an own loop on the server job api (" + Short(t.Tx(i)) + "): start and follow server jobs with Sw.Activity.StartServer(url, arg, { Save, DoneText }) (activity list with progress, Cancel, the result after a reload; Sw.Activity.RestoreServer() rebuilds the list)");
                //  ---- ux-saf-10: work that sends a request marked { Work: true } without its Kind (the leave text says only "... stops.")
                if (t.Chain(i, "Page", "SetDirty") && t.P(i + 1, "(") && (t.Args(i + 1) is var sda) && (sda.Count > 2) && t.P(sda[2].A, "{")
                    && (ObjKey(t, sda[2].A, "Work") is var wv) && (wv != null) && t.Is(wv.Value, "true") && (ObjKey(t, sda[2].A, "Kind") == null)
                    && (t.Is(sda[1], "true") || ((ValueText(t, sda[1]) is String wtx) && Regex.IsMatch(wtx, @"\bstops?\b|\bwill stop\b", RegexOptions.IgnoreCase)))
                    && (flow.Around(i) is FnNode wfn) && flow.Of(wfn).Reqs.Any(r => !r.Chain))
                    Add(ctx, Rule.UxSaf10, t.S[i].Start, "Sw.Page.SetDirty(..., { Work: true }) for work that sends a request: the leave question says it stops, but the server may finish it; pass { Work: true, Name, Kind: \"server\" } (the true consequence: \"<name> may already have been done on the server\")");
                //  ---- ux-saf-10: an own SetDirty in an input listener of a Sw value control's field (the control tracks it already)
                if (t.Id(i, "addEventListener") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Str(i + 2) is String iev) && ((iev == "input") || (iev == "change") || (iev == "keyup"))
                    && (t.Id(i - 2, "Input") || t.Id(i - 2, "E")) && t.P(i - 3, ".") && t.Id(i - 4) && vars.TryGetValue(t.Tx(i - 4), out var cv) && FieldControls.Contains(cv.Kind)
                    && !((cv.Call > 0) && t.P(cv.Call + 1, "{") && (ObjKey(t, cv.Call + 1, "Dirty") != null)) && (t.Args(i + 1) is var la) && (la.Count > 1) && (flow.At(la[1].A) is FnNode lfn) && flow.Of(lfn).Dirty)
                    Add(ctx, Rule.UxSaf10, t.S[i].Start, "an own Sw.Page.SetDirty in an " + iev + " listener of " + t.Tx(i - 4) + "." + t.Tx(i - 2) + ": the " + cv.Kind + " marks the page dirty by itself, so the leave question names the change twice; remove the own SetDirty (or pass Dirty: false to the control when the page tracks it)");
                //  A secret said aloud: a toast / announcement / alert whose text includes a key, token, secret or password value
                if ((ToastFunctions.Contains(t.Tx(i)) || t.Tx(i) == "Announce" || t.Tx(i) == "Alert") && t.P(i - 1, ".") && t.P(i + 1, "(") && (t.Args(i + 1) is var ta) && (ta.Count > 0))
                {
                    var exprs = new List<String>();
                    for (int k = ta[0].A; k <= ta[0].B; ++k)
                        if (t.Id(k) && !TrFunctions.Contains(t.Tx(k)) && !t.P(k + 1, "("))
                            exprs.Add(t.Tx(k));
                    var sec = exprs.FirstOrDefault(x => SecretText.IsMatch(x) || Regex.IsMatch(x, @"(?i)^(?:api_?key|key|token|secret|password|pwd|pin)$"));
                    if (sec != null)
                        Add(ctx, Rule.UxSec1, t.S[i].Start, "the " + t.Tx(i) + " text includes the secret \"" + sec + "\": toasts and announcements are read aloud and stay on screen; say what happened without the value (\"API key copied\", Sw.Clipboard.Copy(v, { What }))");
                }
                //  A secret written into the page code
                if (((t.S[i].Kind == JsTokenKind.String) || t.S[i].IsTemplate) && SecretLiteral.Match(t.Tx(i)) is Match sm && sm.Success)
                    Add(ctx, Rule.UxSec1, t.S[i].Start, "a secret in the page code (\"" + Short(sm.Value) + "...\"): everyone who opens the page can read it; keys and tokens come from the server for the signed in user, never from the source");

                //  ---- ux-saf-10: own SetDirty in the input handler of a control that tracks itself; navigation that skips the leave question
                if ((t.Id(i, "OnInput") || t.Id(i, "OnChange")) && t.Key(i) && (EnclosingObject(t, i) is int iopen) && (iopen > 0) && IsSwOptions(t, iopen)
                    && Regex.IsMatch(t.Span(iopen - 2, iopen - 1), @"(?:TextBox|NumberBox|Select|Toggle|Segmented)\s*\($") && (ObjKey(t, iopen, "Dirty") == null) && (flow.At(i + 2) is FnNode inode) && flow.Of(inode).Dirty && twiceReported.Add(iopen))
                    Add(ctx, Rule.UxSaf10, t.S[i].Start, "an own Sw.Page.SetDirty in the " + t.Tx(i) + " of a Sw." + t.Tx(iopen - 2) + ", which marks the page dirty by itself: the leave question names the change twice; remove the own SetDirty (or pass Dirty: false to the control when the page tracks it)");
                if (!framework && t.Id(i, "location") && (t.P(i - 1, ".") ? (t.Id(i - 2, "window") || t.Id(i - 2, "document") || t.Id(i - 2, "self")) : !t.P(i + 1, ":")))
                {
                    bool assign = (t.P(i + 1, "=") && !t.P(i + 2, "=")) || (t.P(i + 1, ".") && t.Id(i + 2, "href") && t.P(i + 3, "=") && !t.P(i + 4, "="))
                        || (t.P(i + 1, ".") && (t.Id(i + 2, "assign") || t.Id(i + 2, "replace")) && t.P(i + 3, "("));
                    bool editsHere = vars.Values.Any(v => FieldTags.Contains(v.Kind) || FieldControls.Contains(v.Kind)) || editable.Count > 0 || Regex.IsMatch(text, @"\bSw\.Form\b|\bSetDirty\b|\bFormFooter\b|\bnew\s+Sw\.(?:TextBox|NumberBox|Select|Toggle|Slider|Segmented|DateBox|CheckBox|Checkbox|TagBox|ComboBox|RadioGroup|TimeBox)\s*\(");
                    if (assign && editsHere)
                        Add(ctx, Rule.UxSaf10, t.S[i].Start, "navigation by location" + (t.P(i + 1, ".") ? "." + t.Tx(i + 2) : "") + " on a page with fields: unsaved edits get only the browser's generic prompt (inside the shell none of the app's \"Leave without saving?\"); navigate with Sw.Page.Navigate(url), which asks first");
                }

                //  ---- ux-pop-1: an unowned menu / pop-up level opened from a dialog (it stays when the dialog closes, and the dialog behind still acts)
                if ((t.Chain(i, "Menu", "Show") || t.Chain(i, "Popup", "Show")) && t.P(i + 1, "(") && Regex.IsMatch(t.Span(i, Math.Max(i, t.Match(i + 1))), @"\bOwner\s*:\s*null\b") && Regex.IsMatch(text, @"\bSw\.Dialog\b"))
                    Add(ctx, Rule.UxPop1, t.S[i].Start, "Sw." + t.Tx(i - 2) + ".Show(..., { Owner: null }) on a page with dialogs: an unowned level over a dialog doesn't close with it and lets a press reach the dialog behind; leave Owner out (the open pop-up / dialog the anchor is in owns it)");
                //  ---- ux-saf-12: page code that rewrites its history entry (the frame url, Sw.History, Back)
                if (!framework && t.Id(i, "history") && t.P(i + 1, ".") && (t.Id(i + 2, "replaceState") || t.Id(i + 2, "pushState")) && t.P(i + 3, "(") && (!t.P(i - 1, ".") || t.Id(i - 2, "window"))
                    //  Not: a replace that keeps the entry's state (history.state: a hash for the selected item), or one right before a reload
                    && !(t.Id(i + 2, "replaceState") && (Regex.IsMatch(t.Span(i + 4, Math.Min(t.N - 1, i + 6)), @"^history\s*\.\s*state\b") || Regex.IsMatch(t.Span(i, Math.Min(t.N - 1, Math.Max(i, t.Match(i + 3)) + 8)), @"\breload\s*\("))))
                    Add(ctx, Rule.UxSaf12, t.S[i].Start, "history." + t.Tx(i + 2) + " in page code: it rewrites the frame url behind the shell's back (Back, reload and Sw.History state break); keep view state with Sw.History (tabs, search, scroll) and navigate with Sw.Page.Navigate");
                //  ---- ux-saf-3: a hand-made danger dialog that makes the destructive button the default
                if ((t.Id(i, "Default") || t.Id(i, "Primary") || t.Id(i, "Focus") || t.Id(i, "AutoFocus") || t.Id(i, "Autofocus")) && t.Key(i) && t.Id(i + 2, "true"))
                {
                    int open = EnclosingObject(t, i);
                    if ((open > 0) && (ObjKey(t, open, "Kind") is var k2) && (k2 != null) && (t.Lit(k2.Value) == "danger") && dialogCalls.Any(d => (d.A < i) && (i < d.B)))
                        Add(ctx, Rule.UxSaf3, t.S[i].Start, t.Tx(i) + ": true on the danger button of a dialog: the destructive button must never be the default (in a destructive confirmation Cancel is focused and Enter never confirms; " + t.Tx(i) + " is no dialog button option and is ignored); use Sw.Dialog.Confirm(title, text, verb, { Danger: true }) or Sw.Cells.ConfirmDanger");
                }
            }

            //  ---- handlers: FBK-5 (a "Saved" toast for an in-place result, an async OnClick that resolves false silently)
            for (int i = 0; i < t.N; ++i)
            {
                FnNode n = (t.Id(i, "OnClick") && t.Key(i)) ? flow.At(i + 2) : (WrapperClick(t, flow, i) is (int, FnNode) wc ? wc.Item2 : null);
                if (n == null)
                    continue;
                var e = flow.Of(n);
                if (e.Reqs.Any(r => !r.Chain) && e.Toasts.FirstOrDefault(x => SavedText.IsMatch(x.Text)) is var ts && ts.Text != null)
                    Add(ctx, Rule.UxFbk5, t.S[ts.At].Start, "a \"" + Short(ts.Text) + "\" toast for a result the user sees in place: the button announces \"<name>: done\" and the page shows the new state (FBK-2); a toast only when the result isn't visible there (a download, a background job, another page); drop it or say what is not visible");
                //  The same failure twice: a toast and a status / field error in one handler
                var own = t.Span(n.F.Start, n.F.End);
                if (Regex.IsMatch(own, @"\bSw\.Fail\s*\(") && Regex.IsMatch(own, @"\.(?:SetError|SetStatus|ShowError)\s*\(|\bstatus\w*\.Set\s*\([^;]*[""'](?:error|fail)") && e.Toasts.Count + 1 > 0 && (flow.Own(n).FirstOrDefault(j => t.Chain(j, "Sw", "Fail")) is int fj) && t.Chain(fj, "Sw", "Fail"))
                    Add(ctx, Rule.UxFbk5, t.S[fj].Start, "the handler shows a failure twice: Sw.Fail(...) and a status line / field error for the same failure; show it once, at the place of the action (the status line / field), and drop the toast");
                if (n.F.Async && !e.Says && !Regex.IsMatch(t.Span(n.F.Start, n.F.End), @"\.(?:Commit|Validate|SetError|ShowError|SetStatus|Set|Error|Invalid|Hint|Show|Announce|Reject|focus|Focus)\s*\("))
                {
                    foreach (var j in flow.Own(n))
                        if (t.Id(j, "return") && t.Id(j + 1, "false") && (t.P(j + 2, ";") || t.P(j + 2, "}")))
                        {
                            Add(ctx, Rule.UxFbk5, t.S[j].Start, "the OnClick resolves false (\"not done\") without saying why: the user sees the button stop and nothing else; show the reason (Sw.Fail(...), a status line or the field's error) before returning false");
                            break;
                        }
                    if (!n.F.Block && t.Id(n.F.Start, "false") && (n.F.Start == n.F.End))
                        Add(ctx, Rule.UxFbk5, t.S[n.F.Start].Start, "the OnClick resolves false (\"not done\") without saying why; show the reason (Sw.Fail(...), a status line) first");
                }
            }

            //  ---- ux-fbk-7: a polling loop (a function that reschedules itself) that rebuilds rows
            foreach (var n in flow.Fns)
            {
                if (!flow.Reschedules(n))
                    continue;
                var e = flow.Of(n);
                //  A polling loop asks the server for the rows
                if (!e.Reqs.Any(r => !r.Chain && !r.ViaMethod))
                    continue;
                var body = t.Span(n.F.Start, n.F.End);
                if (e.Rebuilds && Regex.IsMatch(body, @"tbody|<tr\b|<li\b|createElement\(\s*[""'](?:tr|li)[""']|\bel\(\s*[""'](?:tr|li)\b|rows?\b|list\b|items?\b", RegexOptions.IgnoreCase))
                    Add(ctx, Rule.UxFbk7, t.S[n.Head].Start, "a polling loop (\"" + n.Name + "\" reschedules itself) that rebuilds the rows: the order jumps under the pointer and the focus is lost on every tick; use Sw.Table with SetRows(rows, true) (the order is held while the pointer, a finger or the focus is on the rows, moved rows are marked)");
            }
            //  ---- ux-saf-11: drafts per subject (a Context built from an id) on a page that deletes subjects, without Sw.Draft.RemoveFor
            if (!framework && !Regex.IsMatch(text, @"\bRemoveFor\b"))
            {
                bool perSubject = false;
                for (int i = 0; (i < t.N) && !perSubject; ++i)
                    if (t.Id(i, "Context") && t.Key(i))
                    {
                        var cv2 = (i + 2, ExprEnd(t, i + 2));
                        perSubject = (cv2.Item2 > cv2.Item1) && (t.Lit(cv2) == null) && Regex.IsMatch(t.Span(Math.Max(0, i - 40), i), @"\bDraft\b");
                    }
                    else if (t.Id(i, "Draft") && t.Key(i) && (ExprEnd(t, i + 2) > i + 2) && (t.Lit((i + 2, ExprEnd(t, i + 2))) == null) && !t.P(i + 2, "{"))
                        perSubject = true;
                if (perSubject)
                    for (int i = 0; i < t.N; ++i)
                    {
                        bool del = (IsRequest(t, i) && (t.Args(i + 1) is var da) && (da.Count > 0) && Literals(t, da[0]).Split(' ').Any(u => Regex.IsMatch(u, @"(?:^|[/.])(?:Delete|Remove|Purge|Erase|Destroy)[A-Z_]?\w*$"))) || t.Chain(i, "Danger", "Run");
                        if (del)
                        {
                            Add(ctx, Rule.UxSaf11, t.S[i].Start, "the page deletes a subject and drafts per subject (a draft Context made from an id), but never removes the drafts of a deleted subject: its old text comes back for a new one with the same id; call Sw.Draft.RemoveFor(\"<context prefix>\" + id + \".\") after the delete");
                            break;
                        }
                    }
            }
            //  ---- ux-saf-10: a content editable element whose text the page saves, without dirty tracking or a draft for that element
            if (!framework && (editable.Count > 0))
            {
                var urls = flow.Fns.SelectMany(n => flow.Of(n).Reqs).Select(r => r.Url).SelectMany(u => u.Split(' ')).ToList();
                for (int i = 0; i < t.N; ++i)
                    if (IsRequest(t, i) && (t.Args(i + 1) is var ra) && (ra.Count > 0))
                        urls.Add(Literals(t, ra[0]).Trim());
                var save = urls.FirstOrDefault(u => (u.Length > 0) && SaveApi.IsMatch(u));
                if (save != null)
                    foreach (var (ed, edAt) in editable)
                    {
                        var esc = Regex.Escape(ed);
                        bool tracked = Regex.IsMatch(text, @"\bDraft\s*\.\s*Attach\(\s*" + esc + @"\b|\bSetDirty\b[^;]*\b" + esc + @"\b")
                            || Regex.IsMatch(text, @"\b" + esc + @"\s*\.\s*(?:addEventListener\(\s*[""'](?:input|keyup|keydown|change|beforeinput)[""']|on(?:input|keyup|change)\s*=)[^\n]*\bSetDirty\b");
                        if (!tracked)
                            Add(ctx, Rule.UxSaf10, t.S[edAt].Start, "the page edits \"" + ed + "\" (content editable) and saves it (" + Short(save) + ") without dirty tracking: leaving with unsaved edits loses them without a question; call Sw.Page.SetDirty(\"notes\", _TF(\"Your changes to ... are lost.\", \"...\")) on input and SetDirty(\"notes\", false) after the save (a draft: Sw.Draft.Attach(" + ed + ", { Context }))");
                    }
            }
            return editable;
        }

        /// <summary>True if the function of the token made the page header before it (Sw.PageLayout / Sw.PageHeader), or both are at the top level.</summary>
        static bool SameFunctionAsHeader(Toks t, Flow flow, int at)
        {
            var f = flow.Around(at);
            for (int k = 0; k < t.N; ++k)
                if ((k < at) && (t.Chain(k, "Sw", "PageLayout") || t.Chain(k, "Sw", "PageHeader")) && t.P(k + 1, "(") && (flow.Around(k) == f))
                    return true;
            return false;
        }

        static readonly HashSet<String> NativeNamesPublic = new HashSet<String>(StringComparer.Ordinal) { "confirm", "alert", "prompt" };

        /// <summary>The first token of the member chain ending at i.</summary>
        static int ChainStartOf(Toks t, int i)
        {
            int s = i;
            while ((s >= 2) && t.P(s - 1, ".") && t.Id(s - 2))
                s -= 2;
            return s;
        }

        /// <summary>
        /// ux-saf-1 for a handler's effects: a destructive request (also through a local helper) without a question first.
        /// </summary>
        static void ReportDestructive(Checker.FileContext ctx, Toks t, Effects e, int at = -1)
        {
            if (e.Asks)
                return;
            foreach (var r in e.Reqs)
            {
                if (r.Chain || (r.Url.Length == 0) || (r.ViaMethod && !t.Id(r.At - 2, "this")))
                    continue;
                var hit = r.Url.Split(' ').FirstOrDefault(u => DestructiveApi.IsMatch(u.Trim()));
                if (hit == null)
                    continue;
                Add(ctx, Rule.UxSaf1, t.S[at >= 0 ? at : r.At].Start, "the handler calls the destructive api \"" + Short(hit) + "\"" + (r.Via != null ? " (through " + r.Via + "(...))" : "") + " without asking: confirm first (Sw.Cells.ConfirmDanger({ Action, Target }) or Sw.Dialog.Confirm(title, text, verb, { Danger: true }), or Sw.Danger.Run), or offer Undo when the result stays reversible (SAF-5)");
                return;
            }
        }

        /// <summary>
        /// Markup in a script string: fields without a label (FRM-1), a text area without a draft (SAF-11), an own dialog (POP-1), a native
        /// disabled button (INP-12), a positive tabindex (FOC-4), an h1 on a page with a page header (ACC-3).
        /// </summary>
        static void CheckMarkupString(Checker.FileContext ctx, Toks t, int i, bool hasDraft, bool pageHeader)
        {
            var raw = t.Tx(i);
            var s = t.S[i].Kind == JsTokenKind.String ? JsLexer.DecodeStringLiteral(raw) : raw.Trim('`', '}', '$', '{');
            if (!Regex.IsMatch(s, @"<[A-Za-z]"))
                return;
            var nodes = HtmlScanner.Scan(s);
            bool labelled = Regex.IsMatch(s, @"<label\b", RegexOptions.IgnoreCase);
            int at = t.S[i].Start;
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                var name = n.Name?.ToLowerInvariant();
                var type = (Attr(n, "type") ?? "text").Trim().ToLowerInvariant();
                if (((name == "input") && !FieldSkipTypes.Contains(type) && (type != "file")) || (name == "textarea") || (name == "select"))
                {
                    bool named = HasValue(n, "aria-label") || HasValue(n, "aria-labelledby") || HasValue(n, "title") || labelled;
                    if (!named && !Has(n, "hidden"))
                        Add(ctx, Rule.UxFrm1, at, "the <" + name + "> in this markup string has no label (a placeholder is not one): use a Sw control with Label (new Sw.TextBox({ Label, Multiline })), Sw.Field, or a <label> linked to it");
                    if ((name == "textarea") && !hasDraft && !Has(n, "readonly") && !Has(n, "disabled"))
                        Add(ctx, Rule.UxSaf11, at, "the <textarea> in this markup string has no draft: text typed and not yet saved is lost on a reload or a crash; use new Sw.TextBox({ Label, Multiline: true, Draft: \"...\" }) or Sw.Draft.Attach(field, { Context })");
                }
                if (name == "dialog")
                    Add(ctx, Rule.UxPop1, at, "an own <dialog> in a markup string: use Sw.Dialog.Open / Show (focus trap, scrim over the shell, Escape, phone layout, the dialog rules)");
                if ((name == "button") && Has(n, "disabled"))
                    Add(ctx, Rule.UxInp12, at, "<button disabled> in a markup string: it leaves the tab order and its reason can't be read; use Sw.Disabled.Set(el, true, \"Select a row first\")");
                if ((Attr(n, "tabindex") is String ti) && Int32.TryParse(ti.Trim(), out var tv) && (tv > 0))
                    Add(ctx, Rule.UxFoc4, at, "tabindex=\"" + ti + "\" in a markup string moves the element ahead of the visual order; use 0 or -1");
                if (pageHeader && ((name == "h1") || ((Attr(n, "role") == "heading") && (Attr(n, "aria-level")?.Trim() == "1"))))
                    Add(ctx, Rule.UxAcc3, at, "a level 1 heading in a markup string on a page whose page header already shows the title (Sw.PageLayout / Sw.PageHeader make the h1): one h1 per page; use h2 for sections");
            }
        }
    }
}
