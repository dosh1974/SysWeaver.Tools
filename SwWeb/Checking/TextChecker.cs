using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// The rule tr-untranslated: user visible literal texts in code without a translation call (the server translates html text, the title /
    /// placeholder / alt attributes and the literals of _T / _TF / _TH / _TFH calls, nothing else), and new-format pages whose own scripts make
    /// texts but never translate. A text that must stay as it is (a product name, a code) is marked with <see cref="NoTr"/>.
    /// </summary>
    public static class TextChecker
    {
        /// <summary>
        /// The marker of a literal that is deliberately not translated, on its line or the line before: <c>// SwWeb:no-tr (a product name)</c>.
        /// </summary>
        public const String NoTr = "SwWeb:no-tr";

        /// <summary>Option names of the Sw components (and own objects passed to them) whose value is shown as text.</summary>
        static readonly HashSet<String> TextOptions = new HashSet<String>(StringComparer.Ordinal)
        {
            "Title", "Text", "Description", "Label", "Placeholder", "Tooltip", "Hint", "Content", "Message", "EmptyText", "Caption", "Heading",
            "Subtitle", "OkText", "CancelText", "AriaLabel", "Help", "Html",
            //  Sw.el options / attribute objects
            "text", "title", "placeholder", "html", "aria-label", "alt",
        };

        /// <summary>Calls whose first arguments are shown as text: (object, method, number of text arguments).</summary>
        static readonly (String Obj, String Name, int Args)[] TextCalls =
        {
            ("Sw", "Toast", 1), ("Sw", "Info", 1), ("Sw", "Fail", 1), ("Sw", "Warn", 1), ("Dialog", "Alert", 2), ("Dialog", "Confirm", 3), ("Dialog", "Prompt", 2),
            ("Sw", "Message", 1), ("Page", "Announce", 1), ("Dialog", "DangerTitle", 1), ("document", "createTextNode", 1),
        };

        /// <summary>Properties whose assigned value is shown as text.</summary>
        static readonly HashSet<String> TextProperties = new HashSet<String>(StringComparer.Ordinal) { "textContent", "innerText", "title", "placeholder", "ariaLabel", "alt", "ariaDescription" };

        /// <summary>Properties / calls whose value is html (the words between the tags are shown).</summary>
        static readonly HashSet<String> HtmlProperties = new HashSet<String>(StringComparer.Ordinal) { "innerHTML", "outerHTML" };

        /// <summary>Attributes whose value is shown as text (setAttribute).</summary>
        static readonly HashSet<String> TextAttributes = new HashSet<String>(StringComparer.Ordinal) { "title", "aria-label", "placeholder", "alt", "aria-description" };

        static readonly HashSet<String> TrNames = new HashSet<String>(StringComparer.Ordinal) { "_T", "_TF", "_TH", "_TFH" };

        static readonly Regex Words = new Regex(@"\p{L}{2,}", RegexOptions.CultureInvariant);
        static readonly Regex Sentence = new Regex(@"\p{L}{2,}[,.!?:]?\s+\p{L}{2,}", RegexOptions.CultureInvariant);
        static readonly Regex Tag = new Regex(@"<[^<>]*>", RegexOptions.CultureInvariant);
        static readonly Regex Entity = new Regex(@"&(?:#\d+|#x[0-9a-fA-F]+|[A-Za-z]+);", RegexOptions.CultureInvariant);
        static readonly Regex KeyCombo = new Regex(@"^(?:(?:Ctrl|Control|Alt|Shift|Meta|Cmd|Option|Win)\s*\+\s*)+\S{1,12}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex CssRule = new Regex(@"\{[^{}]*:[^{}]*\}|^[\w-]+\s*:\s*[^:]+;", RegexOptions.CultureInvariant);
        static readonly Regex ServerVar =new Regex(@"\$\{[^}]*\}", RegexOptions.CultureInvariant);
        static readonly Regex NotText = new Regex(@"^(?:\s*-{0,2}[a-z][a-z0-9-]*\s*:[^:]*;?)+$|^[./#]|^\w+://|^[a-z][a-z0-9]*(?:[-_.][a-z0-9]+)*$|^[\w./-]+\.(?:html?|js|css|png|svg|json|ico|jpe?g|gif|webp)(?:[?#].*)?$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The static text of a literal token (strings decoded, template parts joined; substitutions and server template variables removed).
        /// </summary>
        static String LiteralText(String text, List<JsToken> sig, int i, out int last)
        {
            last = i;
            var t = sig[i];
            if (t.Kind == JsTokenKind.String)
                return JsLexer.DecodeStringLiteral(t.GetText(text)) ?? "";
            if (t.Kind == JsTokenKind.Template)
            {
                var s = t.GetText(text);
                return s.Length >= 2 ? s.Substring(1, s.Length - 2) : "";
            }
            if (t.Kind != JsTokenKind.TemplateHead)
                return null;
            var sb = new System.Text.StringBuilder();
            int depth = 0;
            for (int j = i; j < sig.Count; ++j)
            {
                var k = sig[j];
                if (k.Kind == JsTokenKind.TemplateHead)
                {
                    if (depth++ == 0)
                    {
                        var s = k.GetText(text);
                        sb.Append(s, 1, Math.Max(0, s.Length - 3)).Append(' ');
                    }
                }
                else if ((k.Kind == JsTokenKind.TemplateMiddle) && (depth == 1))
                {
                    var s = k.GetText(text);
                    sb.Append(s, 1, Math.Max(0, s.Length - 3)).Append(' ');
                }
                else if (k.Kind == JsTokenKind.TemplateTail)
                {
                    if (--depth == 0)
                    {
                        var s = k.GetText(text);
                        sb.Append(s, 1, Math.Max(0, s.Length - 2));
                        last = j;
                        return sb.ToString();
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// True if a literal value reads as a text for people (words, not a key, class, url, css or a placeholder only format).
        /// </summary>
        /// <param name="value">The static text</param>
        /// <param name="html">True if the value is html (only the text between the tags counts)</param>
        public static bool IsVisibleText(String value, bool html)
        {
            if (String.IsNullOrWhiteSpace(value))
                return false;
            var v = ServerVar.Replace(value, " ");
            if (html || (v.IndexOf('<') >= 0 && Tag.IsMatch(v)))
            {
                v = Entity.Replace(Tag.Replace(v, " "), " ");
                return Words.IsMatch(v) && !Regex.IsMatch(v.Trim(), @"^[\w-]+$");
            }
            v = v.Trim();
            if (!Words.IsMatch(v) || NotText.IsMatch(v) || KeyCombo.IsMatch(v) || CssRule.IsMatch(v))
                return false;
            //  A single word: a text when it starts with a capital letter (Delete, OK, Settings), not a key (dash, auto, sw-root)
            if (!Regex.IsMatch(v, @"\s"))
                return Char.IsUpper(v[0]);
            return true;
        }

        /// <summary>
        /// Report the user visible literal texts of a script (a .js file or an inline script).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        public static void CheckJs(Checker.FileContext ctx, List<JsToken> tokens)
        {
            var text = ctx.Text;
            if (IsExempt(ctx.WebRel, text) || ctx.SkipDesign)
                return;
            foreach (var s in FindTexts(text, tokens))
            {
                if (ctx.HasMarker(s.Start, NoTr))
                    continue;
                var shown = Regex.Replace(s.Value.Trim(), @"\s+", " ");
                if (shown.Length > 50)
                    shown = shown.Substring(0, 47) + "...";
                ctx.Add(Rule.TrUntranslated, s.Start, s.Where + " \"" + shown + "\" is not translated: write _T(\"" + (s.Html ? "text" : shown.Replace("\"", "'")) + "\", \"context\")"
                    + (s.Html ? " for the words and build the html around them (or Sw.el / textContent)" : (s.Wrappable ? " (SwWeb migrate --level design wraps it)" : "")) + " (PORTING.md A9; a text that must stay as it is gets a \"" + NoTr + " (why)\" comment)");
            }
        }

        /// <summary>
        /// A user visible literal text found by <see cref="FindTexts"/>.
        /// </summary>
        public sealed class TextSite
        {
            /// <summary>The offset of the literal (its first token).</summary>
            public int Start;
            /// <summary>The end offset of the literal (its last token).</summary>
            public int End;
            /// <summary>Where it is shown ("the Title option", "Sw.Toast text" ...).</summary>
            public String Where;
            /// <summary>The static text.</summary>
            public String Value;
            /// <summary>True if the value is html.</summary>
            public bool Html;
            /// <summary>True if SwWeb migrate can wrap it as it is: one '...' / "..." literal that is the whole value, no {n}, no escapes, not both quote kinds.</summary>
            public bool Wrappable;
        }

        /// <summary>
        /// Find the user visible literal texts of a script: Sw options (Title, Text ...), Sw.Toast / Sw.Info / Sw.Fail / Sw.Dialog.* texts,
        /// createTextNode, textContent / innerText / title / placeholder / aria-label, setAttribute of those, and words in html strings.
        /// </summary>
        /// <param name="text">The source</param>
        /// <param name="tokens">The tokens of the script (a range of the source)</param>
        /// <returns>The texts</returns>
        public static List<TextSite> FindTexts(String text, List<JsToken> tokens)
        {
            var res = new List<TextSite>();
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Id(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && ((p == null) || sig[i].Is(text, p));
            bool Lit(int i) => (i >= 0) && (i < sig.Count) && ((sig[i].Kind == JsTokenKind.String) || (sig[i].Kind == JsTokenKind.Template) || (sig[i].Kind == JsTokenKind.TemplateHead));
            void Report(int i, String where, bool html)
            {
                var v = LiteralText(text, sig, i, out var last);
                //  A literal that code uses as a value ("x".repeat(3)) or compares is not the text itself
                if ((v == null) || P(last + 1, ".") || P(last + 1, "[") || P(last + 1, "===") || P(last + 1, "!==") || P(last + 1, "==") || P(last + 1, "!="))
                    return;
                if (!IsVisibleText(v, html))
                    return;
                var raw = sig[i].GetText(text);
                bool whole = (last == i) && (sig[i].Kind == JsTokenKind.String) && (last + 1 < sig.Count)
                    && (P(last + 1, ",") || P(last + 1, ")") || P(last + 1, "}") || P(last + 1, ";") || P(last + 1, "]"));
                bool safe = whole && !html && (v.IndexOf('{') < 0) && (raw.IndexOf('\\') < 0) && !(v.Contains('"') && v.Contains('\''));
                res.Add(new TextSite { Start = sig[i].Start, End = sig[last].End, Where = where, Value = v, Html = html, Wrappable = safe });
            }
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                //  { Title: "...", text: "...", "aria-label": "..." }
                if (P(i + 1, ":") && (P(i - 1, "{") || P(i - 1, ",")) && Lit(i + 2))
                {
                    String key = null;
                    if (t.Kind == JsTokenKind.Identifier)
                        key = t.GetText(text);
                    else if (t.Kind == JsTokenKind.String)
                        key = JsLexer.DecodeStringLiteral(t.GetText(text));
                    if ((key != null) && TextOptions.Contains(key))
                        Report(i + 2, "the " + key + " option", (key == "Html") || (key == "html"));
                    continue;
                }
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                //  Sw.Toast("..."), Sw.Dialog.Confirm("...", "...", "..."), document.createTextNode("...")
                foreach (var (obj, name, args) in TextCalls)
                {
                    if (!t.Is(text, name) || !P(i - 1, ".") || !Id(i - 2, obj) || !P(i + 1, "("))
                        continue;
                    int a = i + 2;
                    for (int n = 0; (n < args) && (a < sig.Count); ++n)
                    {
                        if (Lit(a))
                        {
                            LiteralText(text, sig, a, out var last);
                            if (P(last + 1, ",") || P(last + 1, ")"))
                                Report(a, obj + "." + name + " text", false);
                            a = last + 1;
                        }
                        else
                        {
                            //  Skip the argument
                            int depth = 0;
                            for (; a < sig.Count; ++a)
                            {
                                if (P(a, "(") || P(a, "[") || P(a, "{"))
                                    ++depth;
                                else if (P(a, ")") || P(a, "]") || P(a, "}"))
                                {
                                    if (depth-- == 0)
                                        break;
                                }
                                else if ((depth == 0) && P(a, ","))
                                    break;
                            }
                        }
                        if (!P(a, ","))
                            break;
                        ++a;
                    }
                }
                //  x.textContent = "...", x.title = "...", el.innerHTML = "<p>...</p>"
                if (P(i - 1, ".") && (P(i + 1, "=") || P(i + 1, "+=")) && Lit(i + 2))
                {
                    var prop = t.GetText(text);
                    if (TextProperties.Contains(prop) && !Id(i - 2, "style") && !Id(i - 2, "dataset"))
                        Report(i + 2, "the " + prop + " text", false);
                    else if (HtmlProperties.Contains(prop))
                        Report(i + 2, "the " + prop + " text", true);
                    continue;
                }
                //  x.setAttribute("title", "..."), x.insertAdjacentHTML("beforeend", "<p>...</p>")
                if (P(i - 1, ".") && P(i + 1, "(") && Lit(i + 2) && P(i + 3, ",") && Lit(i + 4))
                {
                    if (t.Is(text, "setAttribute") && (sig[i + 2].Kind == JsTokenKind.String) && TextAttributes.Contains(JsLexer.DecodeStringLiteral(sig[i + 2].GetText(text)) ?? ""))
                        Report(i + 4, "the " + JsLexer.DecodeStringLiteral(sig[i + 2].GetText(text)) + " text", false);
                    else if (t.Is(text, "insertAdjacentHTML"))
                        Report(i + 4, "the html", true);
                }
            }
            return res;
        }

        /// <summary>
        /// The marker of a whole file whose texts are deliberately not translated (a developer demo), in its header comment:
        /// <c>SwWeb:no-tr-file (a developer gallery, English only)</c>.
        /// </summary>
        public const String NoTrFile = "SwWeb:no-tr-file";

        /// <summary>
        /// True for files the rule doesn't check: the generated bundles (their sources are checked), the brand hook app/app.js (theme names are brand names), the frozen legacy bundles and the
        /// legacy compatibility layer (ui/compat*.js), and files marked with <see cref="NoTrFile"/> in their first 2000 characters.
        /// </summary>
        internal static bool IsExempt(String webRel, String text) =>
            Checker.IsBundlePath(webRel) || Checker.IsBrandJs(webRel) || (webRel == "common/common.js") || (webRel == "app/application.js") || Regex.IsMatch(webRel, @"^ui/compat[\w]*\.js$")
            || (text.IndexOf(NoTrFile, 0, Math.Min(text.Length, 2000), StringComparison.Ordinal) >= 0);

        /// <summary>
        /// True if a script calls a translation function.
        /// </summary>
        public static bool Translates(String text)
        {
            foreach (var t in JsLexer.Tokenize(text))
                if ((t.Kind == JsTokenKind.Identifier) && TrNames.Contains(t.GetText(text)))
                    return true;
            return false;
        }

        /// <summary>
        /// The first literal of a script that reads as a sentence (two words), or -1.
        /// </summary>
        public static int FirstSentence(String text)
        {
            var tokens = JsLexer.Tokenize(text);
            for (int i = 0; i < tokens.Count; ++i)
            {
                var t = tokens[i];
                if ((t.Kind != JsTokenKind.String) && (t.Kind != JsTokenKind.Template) && (t.Kind != JsTokenKind.TemplateHead) && (t.Kind != JsTokenKind.TemplateMiddle) && (t.Kind != JsTokenKind.TemplateTail))
                    continue;
                var raw = t.GetText(text);
                var v = t.Kind == JsTokenKind.String ? (JsLexer.DecodeStringLiteral(raw) ?? "") : raw;
                v = Entity.Replace(Tag.Replace(ServerVar.Replace(v, " "), " "), " ");
                if (Regex.IsMatch(v.Trim(), @"^use (?:strict|asm)$"))
                    continue;
                //  css ("color: red; margin: 0 4px"), selectors, urls and sql-ish keys are not texts
                if (v.IndexOf(':') >= 0 && v.IndexOf(';') >= 0)
                    continue;
                if (Sentence.IsMatch(v) && !Regex.IsMatch(v, @"^\s*[.#\[]") && !v.Contains("://", StringComparison.Ordinal))
                    return t.Start;
            }
            return -1;
        }

        /// <summary>
        /// The page part of tr-untranslated: a new-format page whose own scripts (and inline scripts) make texts but never call a translation
        /// function (the typical framework-free page after migrate: its texts are in data arrays and string concatenations).
        /// </summary>
        /// <param name="ctx">The page</param>
        /// <param name="nodes">The scanned page</param>
        /// <param name="own">The page's own scripts</param>
        public static void CheckPage(Checker.FileContext ctx, List<HtmlNode> nodes, IReadOnlyList<PageScriptText> own)
        {
            bool core = false;
            HtmlNode body = null;
            var inline = new List<(int Start, int End)>();
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if ((n.Kind == HtmlNodeKind.StartTag) && (n.Name == "body"))
                    body ??= n;
                if ((n.Kind == HtmlNodeKind.StartTag) && (n.Name == "script"))
                {
                    var src = n.GetAttribute("src")?.Value;
                    if ((src != null) && Checker.IsSrc(src, "sw/core.js"))
                        core = true;
                }
                if ((n.Kind == HtmlNodeKind.RawText) && (n.Name == "script"))
                    inline.Add((n.Start, n.End));
            }
            if (!core || (body == null) || (ctx.Text.IndexOf(NoTr, StringComparison.Ordinal) >= 0))
                return;
            var texts = own.Select(x => (x.WebPath, x.Text, 0)).Concat(inline.Select(x => ((String)null, ctx.Text.Substring(x.Start, x.End - x.Start), x.Start))).ToList();
            if (texts.Any(x => Translates(x.Item2)))
                return;
            foreach (var (path, t, offset) in texts)
            {
                var at = FirstSentence(t);
                if (at < 0)
                    continue;
                var line = path == null ? ctx.Source.GetLineCol(offset + at).Line : 1 + t.Take(at).Count(c => c == '\n');
                var shown = Regex.Match(t.Substring(at), @"^.[^\r\n]{0,48}").Value;
                ctx.Add(Rule.TrUntranslated, body.Start, "no translated text: the page's scripts make texts (" + (path ?? "the inline script") + " line " + line + ": " + shown + ") but never call _T / _TF: write every user visible text as _T(\"text\", \"context\") (PORTING.md A9)");
                return;
            }
        }
    }
}
