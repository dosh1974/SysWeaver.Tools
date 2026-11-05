using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Bundling
{
    /// <summary>
    /// Verifies the sources of a JavaScript bundle before it is written, so a half edited source can't break every page that loads the
    /// bundle: each source must lex and balance its brackets (the "syntax" rule of check), and every bare function call / constructor
    /// (<c>name(...)</c>, <c>new Name(...)</c>) must name something that the bundles declare (function, class, var / let / const, a
    /// parameter, a catch binding, an import of a destructuring) or a known browser / JavaScript global. A bundle that fails is not
    /// written: the previous bundle stays in place. A name of another script (loaded by the pages) is known when a comment of the
    /// source names it: "/* global Edit, EditOptions */" or a description ("needs the edit/ scripts: Edit, EditOptions").
    /// </summary>
    public static class BundleVerifier
    {
        /// <summary>
        /// The browser and language globals that sources may call without declaring them.
        /// </summary>
        public static readonly HashSet<String> KnownGlobals = new HashSet<String>(StringComparer.Ordinal)
        {
            //  Language
            "Array", "ArrayBuffer", "BigInt", "Boolean", "DataView", "Date", "Error", "EvalError", "Function", "Intl", "JSON", "Map", "Math",
            "Number", "Object", "Promise", "Proxy", "RangeError", "ReferenceError", "Reflect", "RegExp", "Set", "String", "Symbol", "SyntaxError",
            "TypeError", "URIError", "WeakMap", "WeakSet", "WeakRef", "FinalizationRegistry", "AggregateError", "SharedArrayBuffer", "Atomics",
            "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "escape", "unescape", "eval", "isFinite", "isNaN", "parseFloat",
            "parseInt", "structuredClone", "queueMicrotask",
            //  Browser
            "alert", "confirm", "prompt", "atob", "btoa", "fetch", "setTimeout", "clearTimeout", "setInterval", "clearInterval",
            "requestAnimationFrame", "cancelAnimationFrame", "requestIdleCallback", "cancelIdleCallback", "getComputedStyle", "getSelection",
            "matchMedia", "open", "close", "print", "focus", "blur", "scrollTo", "scrollBy", "scroll", "postMessage", "addEventListener",
            "removeEventListener", "dispatchEvent", "importScripts", "reportError", "createImageBitmap",
            "AbortController", "AbortSignal", "Audio", "Blob", "BroadcastChannel", "ClipboardItem", "CSS", "CSSStyleSheet", "CustomEvent",
            "DOMParser", "DOMRect", "DOMException", "Document", "DocumentFragment", "Element", "Event", "EventSource", "EventTarget", "File",
            "FileReader", "FormData", "Headers", "HTMLElement", "Image", "ImageData", "IntersectionObserver", "KeyboardEvent", "MessageChannel",
            "MouseEvent", "MutationObserver", "Node", "Notification", "Option", "Path2D", "PerformanceObserver", "PointerEvent", "Range",
            "Request", "ResizeObserver", "Response", "SharedWorker", "TextDecoder", "TextEncoder", "URL", "URLSearchParams", "WebSocket",
            "Worker", "XMLHttpRequest", "XMLSerializer", "XPathEvaluator", "FocusEvent", "InputEvent", "WheelEvent", "TouchEvent", "Touch",
            "OffscreenCanvas", "ReadableStream", "WritableStream", "TransformStream", "CompressionStream", "DecompressionStream",
            "PublicKeyCredential", "CredentialsContainer", "SpeechRecognition", "webkitSpeechRecognition", "SpeechSynthesisUtterance",
            "MediaRecorder", "MediaStream", "AudioContext", "webkitAudioContext", "VideoFrame", "Animation", "KeyframeEffect",
            //  The translation functions (the server rewrites them) and keywords that look like calls
            "_T", "_TF", "_TH", "_TFH", "super", "import", "require", "define",
        };

        static readonly HashSet<String> Keywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "catch", "function", "return", "typeof", "delete", "void", "await", "yield", "new", "in", "of",
            "instanceof", "do", "else", "case", "throw", "with", "class", "extends", "async", "get", "set", "static", "this", "constructor",
            "true", "false", "null", "undefined",
        };

        static bool P(List<JsToken> s, String text, int i, String p) => (i >= 0) && (i < s.Count) && (s[i].Kind == JsTokenKind.Punctuator) && s[i].Is(text, p);

        static bool Id(List<JsToken> s, int i) => (i >= 0) && (i < s.Count) && (s[i].Kind == JsTokenKind.Identifier);

        static int Match(List<JsToken> s, String text, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Count; ++i)
            {
                if (s[i].Kind != JsTokenKind.Punctuator)
                    continue;
                if (s[i].Is(text, "(") || s[i].Is(text, "[") || s[i].Is(text, "{"))
                    ++depth;
                else if ((s[i].Is(text, ")") || s[i].Is(text, "]") || s[i].Is(text, "}")) && (--depth == 0))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Add the names a source declares (over-approximated: every identifier of a declaration head, a parameter list or a catch binding).
        /// </summary>
        /// <param name="text">The source</param>
        /// <param name="into">Receives the names</param>
        public static void CollectDeclared(String text, ISet<String> into)
        {
            var s = JsLexer.Tokenize(text).Where(t => t.IsSignificant).ToList();
            for (int i = 0; i < s.Count; ++i)
            {
                if (!Id(s, i))
                    continue;
                var w = s[i].GetText(text);
                if ((w == "function") || (w == "class"))
                {
                    int k = i + 1;
                    if (P(s, text, k, "*"))
                        ++k;
                    if (Id(s, k))
                        into.Add(s[k].GetText(text));
                    if (w == "function")
                    {
                        while ((k < s.Count) && !P(s, text, k, "("))
                            ++k;
                        AddAll(s, text, k, Match(s, text, k), into);
                    }
                }
                else if ((w == "var") || (w == "let") || (w == "const"))
                {
                    //  The head up to "=", ";", "in" / "of" at depth 0 (destructuring included); a list "a = 1, b = 2" continues after ","
                    int depth = 0;
                    bool name = true;
                    for (int k = i + 1; k < s.Count; ++k)
                    {
                        if (P(s, text, k, "(") || P(s, text, k, "[") || P(s, text, k, "{"))
                            ++depth;
                        else if (P(s, text, k, ")") || P(s, text, k, "]") || P(s, text, k, "}"))
                        {
                            if (--depth < 0)
                                break;
                        }
                        else if ((depth == 0) && P(s, text, k, ";"))
                            break;
                        else if ((depth == 0) && P(s, text, k, "="))
                            name = false;
                        else if ((depth == 0) && P(s, text, k, ","))
                            name = true;
                        else if (name && Id(s, k))
                        {
                            var n = s[k].GetText(text);
                            if ((depth == 0) && ((n == "in") || (n == "of")))
                                break;
                            into.Add(n);
                        }
                        if (s[k].HasNewline && (depth == 0) && !name && !P(s, text, k - 1, ",") && !P(s, text, k, ","))
                        {
                            //  An initializer ended by a new line (no ";"): stop at the next statement keyword
                            if (Id(s, k) && Keywords.Contains(s[k].GetText(text)))
                                break;
                        }
                    }
                }
                else if ((w == "catch") && P(s, text, i + 1, "("))
                    AddAll(s, text, i + 1, Match(s, text, i + 1), into);
                if (P(s, text, i + 1, "=>"))
                    into.Add(w);
            }
            //  Arrow parameter lists: ( ... ) =>
            for (int i = 0; i < s.Count; ++i)
                if (P(s, text, i, "=>") && P(s, text, i - 1, ")"))
                {
                    int depth = 0, k = i - 1;
                    for (; k >= 0; --k)
                    {
                        if (P(s, text, k, ")") || P(s, text, k, "]") || P(s, text, k, "}"))
                            ++depth;
                        else if ((P(s, text, k, "(") || P(s, text, k, "[") || P(s, text, k, "{")) && (--depth == 0))
                            break;
                    }
                    if (k >= 0)
                        AddAll(s, text, k, i - 1, into);
                }
            //  Method shorthand parameters: name(...) { (their identifiers are bindings)
            for (int i = 0; i + 1 < s.Count; ++i)
                if (Id(s, i) && P(s, text, i + 1, "(") && (Match(s, text, i + 1) is int c) && (c > 0) && P(s, text, c + 1, "{"))
                    AddAll(s, text, i + 1, c, into);
        }

        static void AddAll(List<JsToken> s, String text, int from, int to, ISet<String> into)
        {
            for (int k = from; (k >= 0) && (k <= to) && (k < s.Count); ++k)
                if (Id(s, k))
                    into.Add(s[k].GetText(text));
        }

        /// <summary>
        /// The problems of one JavaScript source of a bundle: syntax errors (unterminated tokens, unbalanced brackets) and calls of names
        /// that nothing declares.
        /// </summary>
        /// <param name="name">The source name (for the messages)</param>
        /// <param name="text">The source text</param>
        /// <param name="declared">The names the bundles declare (see <see cref="CollectDeclared"/>)</param>
        /// <returns>"name(line,col): message" texts</returns>
        public static List<String> Verify(String name, String text, ISet<String> declared)
        {
            var res = new List<String>();
            var src = new SourceText(text);
            var findings = new List<Finding>();
            var ctx = new Checker.FileContext(name, src, new CheckOptions(), findings);
            var tokens = JsLexer.Tokenize(text);
            Checker.CheckJsTokens(ctx, tokens);
            foreach (var f in findings.Where(x => x.Rule == Rule.Syntax))
                res.Add(name + "(" + f.Line + "," + f.Col + "): syntax: " + f.Message);
            if (res.Count > 0)
                return res;
            var s = tokens.Where(t => t.IsSignificant).ToList();
            var reported = new HashSet<String>(StringComparer.Ordinal);
            //  Names of other scripts that a source documents: a "/* global A, B */" comment, or a word of any comment of the
            //  source ("needs the edit/ scripts: Edit, EditOptions")
            var documented = new HashSet<String>(StringComparer.Ordinal);
            foreach (var t in tokens.Where(t => t.IsComment))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t.GetText(text), @"[A-Za-z_$][\w$]*"))
                    documented.Add(m.Value);
            for (int i = 0; i + 1 < s.Count; ++i)
            {
                if (!Id(s, i) || !P(s, text, i + 1, "("))
                    continue;
                if (P(s, text, i - 1, ".") || P(s, text, i - 1, "?.") || P(s, text, i - 1, "#"))
                    continue;
                var n = s[i].GetText(text);
                if (Keywords.Contains(n) || declared.Contains(n) || KnownGlobals.Contains(n) || documented.Contains(n) || n.EndsWith("Array", StringComparison.Ordinal))
                    continue;
                //  A definition, not a call: function n(, a method shorthand n(...) {
                if (Id(s, i - 1) && (s[i - 1].GetText(text) == "function"))
                    continue;
                var c = Match(s, text, i + 1);
                if ((c > 0) && P(s, text, c + 1, "{"))
                    continue;
                if (!reported.Add(n))
                    continue;
                var (l, col) = src.GetLineCol(s[i].Start);
                res.Add(name + "(" + l + "," + col + "): undefined: \"" + n + "(...)\" calls a name that no bundle source declares (a half edited file? a typo?); declare it, or call it as a member (window." + n + ") if a page defines it");
            }
            res.AddRange(Redeclared(name, text, s, src));
            return res;
        }

        /// <summary>
        /// A let / const / class name declared twice in one block (an early SyntaxError: the whole script, and so the whole bundle, does
        /// not run, although every token is fine; the incident of series 4 round 5: two "const why" in one block of ui/cells.js).
        /// </summary>
        static List<String> Redeclared(String name, String text, List<JsToken> s, SourceText src)
        {
            var res = new List<String>();
            var scopes = new Stack<Dictionary<String, int>>();
            scopes.Push(new Dictionary<String, int>(StringComparer.Ordinal));
            void Declare(int at)
            {
                var n = s[at].GetText(text);
                var scope = scopes.Peek();
                if (scope.TryGetValue(n, out var first))
                {
                    var (l, c) = src.GetLineCol(s[at].Start);
                    var (fl, _) = src.GetLineCol(s[first].Start);
                    res.Add(name + "(" + l + "," + c + "): syntax: \"" + n + "\" is declared twice in one block (also on line " + fl + "): a SyntaxError before the script runs, so the whole bundle fails to load; rename one of them");
                }
                else
                    scope[n] = at;
            }
            for (int i = 0; i < s.Count; ++i)
            {
                if (P(s, text, i, "{") || P(s, text, i, "${"))
                    scopes.Push(new Dictionary<String, int>(StringComparer.Ordinal));
                else if (P(s, text, i, "}") && (scopes.Count > 1))
                    scopes.Pop();
                if (!Id(s, i))
                    continue;
                var kw = s[i].GetText(text);
                if ((kw == "class") && Id(s, i + 1) && !P(s, text, i - 1, "."))
                {
                    //  (a class expression's name is local to it: const A = class B {})
                    if (!P(s, text, i - 1, "=") && !P(s, text, i - 1, "(") && !P(s, text, i - 1, ",") && !P(s, text, i - 1, ":") && !P(s, text, i - 1, "return") && (s[i + 1].GetText(text) != "extends"))
                        Declare(i + 1);
                    continue;
                }
                if (((kw != "let") && (kw != "const")) || P(s, text, i - 1, ".") || P(s, text, i + 1, ".") || P(s, text, i + 1, "(") || P(s, text, i + 1, "="))
                    continue;
                //  for (let i ...) / for (const x of ...): the loop's own scope
                if (P(s, text, i - 1, "(") && Id(s, i - 2) && (s[i - 2].GetText(text) == "for"))
                    continue;
                int k = i + 1;
                if (Id(s, k))
                {
                    Declare(k);
                    //  let a = 1, b = 2 (the names after a top level ",")
                    for (int j = k + 1, d = 0; j < s.Count; ++j)
                    {
                        if (P(s, text, j, "(") || P(s, text, j, "[") || P(s, text, j, "{") || P(s, text, j, "${"))
                            ++d;
                        else if (P(s, text, j, ")") || P(s, text, j, "]") || P(s, text, j, "}"))
                        {
                            if (--d < 0)
                                break;
                        }
                        else if ((d == 0) && P(s, text, j, ";"))
                            break;
                        else if ((d == 0) && P(s, text, j, ",") && Id(s, j + 1) && (P(s, text, j + 2, "=") || P(s, text, j + 2, ",") || P(s, text, j + 2, ";")))
                            Declare(j + 1);
                        else if ((d == 0) && Id(s, j) && (s[j].GetText(text) is "let" or "const" or "var" or "function" or "class" or "return" or "if" or "for" or "while"))
                            break;
                    }
                }
                else if (P(s, text, k, "{") || P(s, text, k, "["))
                {
                    //  const { a, b: c, d = 1 } = x / const [a, b] = x: the bound names (one level)
                    int c = Match(s, text, k);
                    for (int j = k + 1; (c > k) && (j < c); ++j)
                        if (Id(s, j) && (P(s, text, j - 1, "{") || P(s, text, j - 1, ",") || P(s, text, j - 1, ":") || P(s, text, j - 1, "[") || P(s, text, j - 1, "...")) && (P(s, text, j + 1, ",") || P(s, text, j + 1, "}") || P(s, text, j + 1, "]") || P(s, text, j + 1, "=")))
                            Declare(j);
                }
            }
            return res;
        }
    }
}
