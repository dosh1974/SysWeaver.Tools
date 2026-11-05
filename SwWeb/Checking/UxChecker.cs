using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using SwWeb.Lexing;

namespace SwWeb.Checking
{
    /// <summary>
    /// The UX guide rules ("ux-*", <see cref="Rule.Guide"/>): the rules of the SysWeaver UX guide (_docs/UX_Guide.md in SysWeaver) that a
    /// static check can decide with few false positives. They are design rules (only with --design, so "check --strict --design" is the
    /// gate of the guide's page review checklist), they look at page files like the other design rules (the framework's own files only
    /// with --framework), and every message names the guide rule. A deliberate exception gets a comment on the line (or the line before)
    /// that names the rule: <c>/* SwWeb:allow ux-inp-11 (a drag handle, also hovered on touch) */</c>, several ids may follow each other.
    /// </summary>
    public static partial class UxChecker
    {
        /// <summary>
        /// The marker of a deliberate exception to a UX rule, followed by one or more rule ids: <c>SwWeb:allow ux-mot-2 ux-mot-4 (why)</c>.
        /// </summary>
        public const String AllowPrefix = "SwWeb:allow";

        /// <summary>
        /// The guide, relative to the SysWeaver repository.
        /// </summary>
        public const String GuidePath = "_docs/UX_Guide.md";

        static readonly Regex AllowMarker = new Regex(@"SwWeb:allow((?:[ \t]+ux-[a-z]+-\d+)+)", RegexOptions.CultureInvariant);

        /// <summary>The guide section (GitHub anchor) of each rule area.</summary>
        static readonly Dictionary<String, String> Anchors = new Dictionary<String, String>(StringComparer.Ordinal)
        {
            ["INP"] = "1-input-pointer-touch-and-keyboard",
            ["POP"] = "2-pop-ups-menus-dialogs-pickers-popovers",
            ["FOC"] = "3-keyboard-focus",
            ["FBK"] = "4-feedback-for-actions-and-async-work",
            ["MOT"] = "5-motion",
            ["LAY"] = "6-layout-stability",
            ["TIP"] = "7-tool-tips-labels-and-help",
            ["TXT"] = "8-text-and-localization",
            ["TXT-12"] = "82-html",
            ["TXT-15"] = "83-css-no-translation-not-supported",
            ["SAF"] = "9-data-safety",
            ["FRM"] = "10-forms-and-validation",
            ["LST"] = "11-lists-tables-and-navigation",
            ["ACC"] = "12-accessibility",
            ["PRF"] = "13-performance-as-perceived",
            ["SEC"] = "14-security-in-the-ui",
            ["MOB"] = "15-phones-and-touch",
        };

        /// <summary>
        /// The citation that ends every UX message: " (UX guide INP-25: _docs/UX_Guide.md#1-input-pointer-touch-and-keyboard)".
        /// </summary>
        /// <param name="rule">A UX rule</param>
        /// <returns>The text</returns>
        public static String Cite(Rule rule)
        {
            var g = rule.Guide;
            if (!Anchors.TryGetValue(g, out var a))
                Anchors.TryGetValue(g.Substring(0, g.IndexOf('-')), out a);
            return " (UX guide " + g + ": " + GuidePath + "#" + a + ")";
        }

        /// <summary>
        /// True for the UX rules whose findings were design-own-ui / design-viewport findings before, so an existing "SwWeb:allow-design"
        /// comment keeps allowing them.
        /// </summary>
        public static bool KeepsDesignAllow(Rule rule) => (rule == Rule.UxPop1) || (rule == Rule.UxPop9) || (rule == Rule.UxInp25);

        /// <summary>
        /// True if the line of an offset (or the line before) has a "SwWeb:allow" comment that names the rule.
        /// </summary>
        public static bool IsAllowed(Checker.FileContext ctx, int offset, Rule rule)
        {
            var ls = ctx.Source.LineStarts;
            var (l, _) = ctx.Source.GetLineCol(offset);
            var from = ls[Math.Max(0, l - 2)];
            var to = l < ls.Length ? ls[l] : ctx.Text.Length;
            var at = ctx.Text.IndexOf(AllowPrefix, from, to - from, StringComparison.Ordinal);
            if (at < 0)
                return false;
            foreach (Match m in AllowMarker.Matches(ctx.Text.Substring(from, to - from)))
                foreach (var id in m.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    if (id == rule.Id)
                        return true;
            return false;
        }

        /// <summary>
        /// True if the UX rules check a file: page files, and the framework's files with --framework (never the generated sw/ bundles).
        /// </summary>
        static bool Applies(Checker.FileContext ctx) =>
            !Checker.IsBundlePath(ctx.WebRel) && (ctx.Options.Framework || !DesignChecker.IsFramework(ctx.WebRel));

        static void Add(Checker.FileContext ctx, Rule rule, int offset, String message)
        {
            if (!NoPromiseOnly)
                ctx.Add(rule, offset, message + Cite(rule));
        }

        /// <summary>True while the framework's own files are checked for the one rule that applies to them without --framework (see <see cref="AddNoPromise"/>).</summary>
        [ThreadStatic]
        static bool NoPromiseOnly;

        /// <summary>
        /// ux-fbk-2 "an OnClick that starts work and returns no promise": an error (the button is never busy, a failure reads as done), in page
        /// files and in the framework's own files (also without --framework), so the framework's pages can't ship it either.
        /// </summary>
        static void AddNoPromise(Checker.FileContext ctx, int offset, String message)
        {
            //  (not in the frozen legacy bundle common/common.js: its compat buttons predate the busy model)
            if (ctx.WebRel != "common/common.js")
                ctx.Add(Rule.UxFbk2, offset, message + Cite(Rule.UxFbk2), Severity.Error);
        }

        /// <summary>A method name that does work (a page method called from an OnClick whose urls are not literals: t.Delete(url)).</summary>
        static readonly Regex WorkVerb = new Regex(@"^(?:Delete|Remove|Save|Send|Submit|Create|Add|Update|Upload|Import|Export|Kill|Stop|Start|Restart|Reset|Run|Rename|Move|Copy|Apply|Publish|Archive|Restore|Revoke|Invite|Approve|Reject|Sync|Purge|Clear|Write|Post|Put|Set)(?:[A-Z_]\w*)?$", RegexOptions.CultureInvariant);

        static String Short(String v) => v.Length > 60 ? (v.Substring(0, 57) + "...") : v;

        // ------------------------------------------------------------------ css

        /// <summary>
        /// A css declaration (of a file, a style element or a style attribute).
        /// </summary>
        sealed class CssDecl
        {
            public String Prop;
            public int PropStart;
            public String Value;
            public int ValueStart;
            /// <summary>The selector list of the rule (null for a style attribute: see <see cref="InlineElement"/>).</summary>
            public String Selector;
            /// <summary>The element of a style attribute.</summary>
            public String InlineElement;
            /// <summary>The enclosing at-rule preludes (outermost first).</summary>
            public List<String> At;
            /// <summary>The index of the rule block (declarations of one rule share it).</summary>
            public int Block;
        }

        static String Blank(String text, int start, int end) =>
            Regex.Replace(text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);

        static readonly Regex PropName = new Regex(@"^\s*(?<p>-{0,2}[A-Za-z_][\w-]*)\s*:", RegexOptions.CultureInvariant);

        /// <summary>
        /// Visit the rules and declarations of a css range.
        /// </summary>
        /// <param name="text">The file text</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="inline">True for a style attribute (a declaration list)</param>
        /// <param name="rule">Receives each style rule: the prelude, its offset and the enclosing at-rules (may be null)</param>
        /// <param name="decl">Receives each declaration (may be null)</param>
        static void VisitCss(String text, int start, int end, bool inline, Action<String, int, List<String>> rule, Action<CssDecl> decl)
        {
            var s = Blank(text, start, end);
            var stack = new List<String>();
            int seg = 0, depth = 0, block = 0;
            var blocks = new List<int>();
            char q = '\0';
            void Segment(int e, char term)
            {
                var part = s.Substring(seg, e - seg);
                if (term == '{')
                {
                    var pre = part.Trim();
                    var at = stack.Where(x => x.StartsWith("@", StringComparison.Ordinal)).ToList();
                    if (!pre.StartsWith("@", StringComparison.Ordinal) && (rule != null) && !at.Any(x => x.StartsWith("@keyframes", StringComparison.OrdinalIgnoreCase) || x.StartsWith("@-webkit-keyframes", StringComparison.OrdinalIgnoreCase)))
                        rule(pre, start + seg + (part.Length - part.TrimStart().Length), at);
                    stack.Add(pre);
                    blocks.Add(++block);
                    return;
                }
                if ((decl == null) || (!inline && (stack.Count == 0)))
                    return;
                var m = PropName.Match(part);
                if (!m.Success)
                    return;
                var colon = m.Index + m.Length;
                var value = part.Substring(colon);
                int lead = value.Length - value.TrimStart().Length;
                var d = new CssDecl
                {
                    Prop = m.Groups["p"].Value.StartsWith("--", StringComparison.Ordinal) ? m.Groups["p"].Value : m.Groups["p"].Value.ToLowerInvariant(),
                    PropStart = start + seg + m.Groups["p"].Index,
                    Value = value.Trim(),
                    ValueStart = start + seg + colon + lead,
                    At = stack.Where(x => x.StartsWith("@", StringComparison.Ordinal)).ToList(),
                    Selector = stack.LastOrDefault(x => !x.StartsWith("@", StringComparison.Ordinal)),
                    Block = blocks.Count > 0 ? blocks[blocks.Count - 1] : 0,
                };
                decl(d);
            }
            for (int i = 0; i < s.Length; ++i)
            {
                var c = s[i];
                if (q != '\0')
                {
                    if (c == '\\')
                        ++i;
                    else if ((c == q) || (c == '\n'))
                        q = '\0';
                    continue;
                }
                if ((c == '"') || (c == '\''))
                    q = c;
                else if (c == '(')
                    ++depth;
                else if ((c == ')') && (depth > 0))
                    --depth;
                else if ((depth == 0) && ((c == '{') || (c == ';') || (c == '}')))
                {
                    Segment(i, c);
                    if ((c == '}') && (stack.Count > 0))
                    {
                        stack.RemoveAt(stack.Count - 1);
                        blocks.RemoveAt(blocks.Count - 1);
                    }
                    seg = i + 1;
                }
            }
            if (seg < s.Length)
                Segment(s.Length, ';');
        }

        /// <summary>Remove var(...) (also nested) from a css value, so fallbacks don't count.</summary>
        static String StripVar(String v)
        {
            for (int guard = 0; guard < 20; ++guard)
            {
                var m = Regex.Match(v, @"var\(", RegexOptions.CultureInvariant);
                if (!m.Success)
                    break;
                int depth = 0, k = m.Index + 3;
                for (; k < v.Length; ++k)
                {
                    if (v[k] == '(')
                        ++depth;
                    else if ((v[k] == ')') && (--depth == 0))
                        break;
                }
                v = v.Substring(0, m.Index) + " " + (k + 1 < v.Length ? v.Substring(k + 1) : "");
            }
            return v;
        }

        static readonly HashSet<String> MotionProps = new HashSet<String>(StringComparer.Ordinal)
        {
            "transition", "transition-duration", "transition-delay", "transition-timing-function",
            "animation", "animation-duration", "animation-delay", "animation-timing-function",
            "-webkit-transition", "-webkit-animation", "-webkit-transition-duration", "-webkit-animation-duration",
        };

        static readonly Regex TimeValue = new Regex(@"(?<![\w.-])(?<n>\d*\.?\d+)(?<u>ms|s)(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex Easing = new Regex(@"\b(?:cubic-bezier|steps|linear)\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        //  The properties that move the layout when they change (MOT-4): sizes, offsets, margins and paddings, gaps, flex sizes, the text size,
        //  border widths and grid columns (grid-template-rows is the guide's way to expand a section)
        static readonly Regex LayoutProp = new Regex(@"(?<![\w-])(?<p>(?:min-|max-)?(?:width|height|inline-size|block-size)|top|left|right|bottom|inset(?:-(?:block|inline)(?:-(?:start|end))?)?|(?:margin|padding)(?:-(?:top|right|bottom|left|block|inline)(?:-(?:start|end))?)?|gap|row-gap|column-gap|grid-gap|flex(?:-basis|-grow|-shrink)?|font-size|line-height|letter-spacing|border(?:-(?:top|right|bottom|left))?-width|grid-template-columns)(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly HashSet<String> LayoutProps = new HashSet<String>(StringComparer.Ordinal)
        {
            "width", "height", "min-width", "max-width", "min-height", "max-height", "top", "left", "right", "bottom", "inset",
            "margin", "margin-top", "margin-right", "margin-bottom", "margin-left", "margin-block", "margin-inline",
            "margin-block-start", "margin-block-end", "margin-inline-start", "margin-inline-end",
            "padding", "padding-top", "padding-right", "padding-bottom", "padding-left", "padding-block", "padding-inline",
            "padding-block-start", "padding-block-end", "padding-inline-start", "padding-inline-end",
            "inline-size", "block-size", "min-inline-size", "max-inline-size", "min-block-size", "max-block-size",
            "inset-block", "inset-inline", "inset-block-start", "inset-block-end", "inset-inline-start", "inset-inline-end",
            "gap", "row-gap", "column-gap", "grid-gap", "flex", "flex-basis", "flex-grow", "flex-shrink", "font-size", "line-height", "letter-spacing",
            "border-width", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "grid-template-columns",
        };
        static readonly Regex HoverMedia = new Regex(@"\(\s*(?:any-)?hover\s*(?::\s*hover\s*)?\)|\(\s*(?:any-)?pointer\s*:\s*fine\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex HoverPseudo = new Regex(@":hover(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex WholePage = new Regex(@"^(?:html|body|:root|\*|html\s+body)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The time of a css time value in ms.
        /// </summary>
        static double Ms(Match m) => Double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) * (m.Groups["u"].Value.Equals("s", StringComparison.OrdinalIgnoreCase) ? 1000 : 1);

        /// <summary>
        /// Decode the strings of a css content value (escapes become characters), without attr() / counter() / url() parts.
        /// </summary>
        static IEnumerable<(String Text, int Offset)> CssStrings(String v)
        {
            foreach (Match m in Regex.Matches(v, @"(?<q>[""'])(?<s>(?:\\.|(?!\k<q>).)*)\k<q>", RegexOptions.CultureInvariant | RegexOptions.Singleline))
            {
                var raw = m.Groups["s"].Value;
                var dec = Regex.Replace(raw, @"\\(?:(?<h>[0-9a-fA-F]{1,6})\s?|(?<c>.))", x =>
                {
                    if (x.Groups["h"].Success)
                    {
                        var cp = Convert.ToInt32(x.Groups["h"].Value, 16);
                        return ((cp > 0) && (cp <= 0x10FFFF) && ((cp < 0xD800) || (cp > 0xDFFF))) ? Char.ConvertFromUtf32(cp) : "";
                    }
                    return x.Groups["c"].Value;
                });
                yield return (dec, m.Index);
            }
        }

        /// <summary>True if a text has a word: two letters in a row.</summary>
        static bool HasWord(String s)
        {
            for (int i = 1; i < s.Length; ++i)
                if (Char.IsLetter(s[i]) && Char.IsLetter(s[i - 1]))
                    return true;
            return false;
        }

        /// <summary>
        /// Run the css UX rules on a range: a .css file, a style element or a style attribute value.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="inline">True for a style attribute</param>
        /// <param name="element">The element of a style attribute</param>
        public static void CheckCss(Checker.FileContext ctx, int start, int end, bool inline, String element = null)
        {
            if ((end <= start) || !Applies(ctx))
                return;
            var decls = new List<CssDecl>();
            VisitCss(ctx.Text, start, end, inline, (prelude, at, media) =>
            {
                //  ux-inp-11: :hover outside @media (hover: hover)
                if (HoverPseudo.IsMatch(prelude) && !media.Any(x => HoverMedia.IsMatch(x)))
                {
                    var sel = Checker.SplitSelectorList(prelude).FirstOrDefault(x => HoverPseudo.IsMatch(x)) ?? prelude;
                    //  (round 5) A part shown only while the pointer hovers its container (row actions): @media (hover: hover) would hide it on touch for good
                    int ob = ctx.Text.IndexOf('{', at), cb = ob >= 0 ? ctx.Text.IndexOf('}', ob) : -1;
                    var block = (ob >= 0) && (cb > ob) && (cb <= end) ? ctx.Text.Substring(ob, cb - ob) : "";
                    if (Regex.IsMatch(sel, @":hover\s*(?:[>+~]\s*|\s+)[.#\w\[]") && Regex.IsMatch(block, @"visibility\s*:\s*visible|opacity\s*:\s*1(?:\.0*)?\s*[;}]|display\s*:\s*(?!none)\w", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxInp8, at, "\"" + Short(sel) + "\" shows a part only while the pointer is over its container (hover-only actions): touch, keyboard and screen reader users can't reach it, and moving it into @media (hover: hover) would hide it on touch for good; show it always (muted), or also on :focus-within and in the touch layout, and keep hover only as an emphasis");
                    else
                    Add(ctx, Rule.UxInp11, at, "\"" + Short(sel) + "\" is outside @media (hover: hover): on a touch screen the hover look sticks after a tap (and on hybrid laptops while the finger rests); put the hover rules in @media (hover: hover) { ... } as the components do");
                }
            }, d =>
            {
                if (inline)
                    d.InlineElement = element;
                decls.Add(d);
            });
            var infinite = new HashSet<int>(decls.Where(d => ((d.Prop == "animation") || (d.Prop == "animation-iteration-count") || (d.Prop == "-webkit-animation")) && Regex.IsMatch(d.Value, @"\binfinite\b", RegexOptions.IgnoreCase)).Select(d => d.Block));
            var keyframesReported = new HashSet<String>(StringComparer.Ordinal);
            var movedKeyframes = new HashSet<String>(StringComparer.Ordinal);
            foreach (var d in decls)
            {
                if (d.Prop.StartsWith("--", StringComparison.Ordinal))
                    continue;
                var v = d.Value;
                bool reduced = d.At.Any(x => x.IndexOf("prefers-reduced-motion", StringComparison.OrdinalIgnoreCase) >= 0);
                var keyframes = d.At.LastOrDefault(x => Regex.IsMatch(x, @"^@(?:-webkit-)?keyframes\b", RegexOptions.IgnoreCase));
                //  ux-inp-10: cursor wait / a page wide busy cursor
                if (d.Prop == "cursor")
                {
                    var cv = v.Replace("!important", "").Trim().ToLowerInvariant();
                    if (cv == "wait")
                        Add(ctx, Rule.UxInp10, d.ValueStart, "\"cursor: wait\": never used (it says the whole app is blocked); a busy control shows cursor: progress on itself (Sw.Button does it), everything else keeps its cursor");
                    else if ((cv == "progress") && (((d.Selector != null) && IsWholePage(d.Selector)) || ((d.InlineElement == "body") || (d.InlineElement == "html"))))
                        Add(ctx, Rule.UxInp10, d.ValueStart, "\"cursor: progress\" on the whole page: only a busy control shows progress (Sw.Button with a promise from OnClick), the page stays usable");
                }
                //  ux-mot-2: hard-coded durations, delays and easings
                if (MotionProps.Contains(d.Prop) && !reduced && !infinite.Contains(d.Block))
                {
                    var sv = StripVar(v);
                    var tm = TimeValue.Matches(sv).Cast<Match>().FirstOrDefault(x => Ms(x) > 0);
                    //  (an animation of no duration only switches a state after its delay: a timer, no motion: "sw-boot-show 0s 8s forwards")
                    var first = TimeValue.Matches(sv).Cast<Match>().FirstOrDefault();
                    if ((d.Prop == "animation") && (first != null) && (Ms(first) == 0))
                        tm = null;
                    if (tm != null)
                        Add(ctx, Rule.UxMot2, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\" hard-codes " + tm.Value + ": use the theme's motion tokens (var(--sw-dur-1) hover and small state changes, var(--sw-dur-2) menus, switches and tool tips, var(--sw-dur-3) dialogs, drawers and sheets) so themes without motion and reduced motion apply");
                    else if (Easing.Match(sv) is Match em && em.Success && !Regex.IsMatch(d.Prop, "duration|delay"))
                        Add(ctx, Rule.UxMot2, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\" hard-codes an easing: use var(--sw-ease) (each theme sets it)");
                }
                //  ux-mot-4: layout properties animated
                if (((d.Prop == "transition") || (d.Prop == "transition-property") || (d.Prop == "-webkit-transition")) && !reduced)
                {
                    var lm = LayoutProp.Match(StripVar(v));
                    if (lm.Success)
                        Add(ctx, Rule.UxMot4, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\" animates " + lm.Groups["p"].Value + ": the layout moves every frame (slow, and the content jumps); animate transform / opacity (or grid-template-rows 0fr to 1fr for an expanding section)");
                    else if (Regex.IsMatch(StripVar(v), @"(?:^|,)\s*all\b", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxMot4, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\" animates all properties, the layout ones too (a width, height or margin that changes moves the content every frame); name the properties: transition: opacity var(--sw-dur-1), background-color var(--sw-dur-1)");
                }
                if ((keyframes != null) && LayoutProps.Contains(d.Prop) && keyframesReported.Add(keyframes))
                    Add(ctx, Rule.UxMot4, d.PropStart, "\"" + Short(keyframes) + "\" animates " + d.Prop + ": the layout moves every frame; animate transform / opacity instead");
                //  ux-mot-6: blinking text
                if (((d.Prop == "text-decoration") || (d.Prop == "text-decoration-line")) && Regex.IsMatch(v, @"\bblink\b", RegexOptions.IgnoreCase))
                    Add(ctx, Rule.UxMot6, d.ValueStart, "\"" + d.Prop + ": " + v + "\": no blinking text; show the state with an icon and a word (ACC-2)");
                //  ux-fbk-2 (a busy look), ux-mot-2 (literal distances in keyframes)
                CheckCssPatterns(ctx, d, keyframes, reduced, movedKeyframes);
                //  ux-txt-15: words in content
                if (d.Prop == "content")
                    foreach (var (s, o) in CssStrings(Regex.Replace(v, @"\b(?:url|attr|counters?|var)\((?:[^()]|\([^()]*\))*\)", x => new String(' ', x.Length), RegexOptions.IgnoreCase)))
                        if (HasWord(s))
                        {
                            Add(ctx, Rule.UxTxt15, d.ValueStart + o, "\"content: " + Short(v) + "\" shows the words \"" + Short(s) + "\": css is not translated (every language sees English); content holds only symbols (icons, \"…\", separators), put the text in the markup or set it from JS with _TF(\"...\", \"context\")");
                            break;
                        }
            }
            CheckCssBlocks(ctx, decls);
        }

        // ------------------------------------------------------------------ reserved sizes (ux-lay-1)

        const int SizeW = 1, SizeH = 2, SizeA = 4;

        /// <summary>
        /// The reserved sizes that style sheets give to classes, ids and bare img / iframe / video selectors (rule ux-lay-1).
        /// </summary>
        public sealed class SizeIndex
        {
            readonly Dictionary<String, int> Tokens = new Dictionary<String, int>(StringComparer.Ordinal);
            readonly Dictionary<String, int> Elements = new Dictionary<String, int>(StringComparer.Ordinal);

            /// <summary>The facts about the helper functions of the web root (round 5: secrets through helpers, empty values on failure, loads).</summary>
            public readonly HelperFacts Helpers = new HelperFacts();

            /// <summary>
            /// Collect a file of a web root: a .css file or the style elements of a .html file; the helper facts of a .js file.
            /// </summary>
            /// <param name="rel">The path relative to the web root (forward slashes)</param>
            /// <param name="text">The text</param>
            public void Collect(String rel, String text)
            {
                var dir = DirOf(rel);
                if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    Helpers.Collect(rel, text);
                if (rel.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text, @"\bSw\.(?:PageLayout|PageHeader)\s*\("))
                    HeaderScripts.Add(rel);
                if (rel.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    CollectCss(dir, text, 0, text.Length);
                else if (rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                    foreach (var n in HtmlScanner.Scan(text))
                        if ((n.Kind == HtmlNodeKind.RawText) && (n.Name == "style"))
                            CollectCss(dir, text, n.Start, n.End);
            }

            internal void CollectCss(String dir, String text, int start, int end)
            {
                var byBlock = new Dictionary<int, (String Selector, int Flags)>();
                VisitCss(text, start, end, false, null, d =>
                {
                    if (d.Selector == null)
                        return;
                    var f = SizeFlags(d.Prop, d.Value);
                    byBlock.TryGetValue(d.Block, out var cur);
                    byBlock[d.Block] = (d.Selector, cur.Flags | f);
                });
                foreach (var (sel, flags) in byBlock.Values)
                {
                    if (flags == 0)
                        continue;
                    foreach (var one in Checker.SplitSelectorList(sel))
                    {
                        var parts = Checker.SplitCompounds(one);
                        if (parts.Count == 0)
                            continue;
                        var subject = Regex.Replace(parts[parts.Count - 1], @":.*$", "");
                        var ids = Regex.Matches(subject, @"[.#]-?[_A-Za-z][\w-]*").Cast<Match>().Select(m => m.Value).ToList();
                        if (ids.Count > 0)
                            foreach (var t in ids)
                                Tokens[t] = (Tokens.TryGetValue(t, out var x) ? x : 0) | flags;
                        else
                        {
                            var el = Regex.Match(subject, @"^[A-Za-z][\w-]*").Value.ToLowerInvariant();
                            if ((el != "img") && (el != "iframe") && (el != "video"))
                                continue;
                            //  "img { }": every img of the folder's pages; ".pic img { }": the img inside an element with the class pic
                            String scope = null;
                            for (int k = parts.Count - 2; (k >= 0) && (scope == null); --k)
                                scope = Regex.Matches(Regex.Replace(parts[k], @":.*$", ""), @"[.#]-?[_A-Za-z][\w-]*").Cast<Match>().Select(m => m.Value).LastOrDefault();
                            var key = scope == null ? (dir + "|" + el) : (scope + "|" + el);
                            var into = scope == null ? Elements : Scoped;
                            into[key] = (into.TryGetValue(key, out var x) ? x : 0) | flags;
                        }
                    }
                }
            }

            readonly Dictionary<String, int> Scoped = new Dictionary<String, int>(StringComparer.Ordinal);

            /// <summary>The scripts (paths relative to the web root) that make a page header (Sw.PageLayout / Sw.PageHeader: the page's h1).</summary>
            internal readonly HashSet<String> HeaderScripts = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

            /// <summary>The flags of a class (".x") or id ("#x").</summary>
            internal int Get(String token) => Tokens.TryGetValue(token, out var f) ? f : 0;

            /// <summary>The flags of bare element selectors in a folder's style sheets.</summary>
            internal int GetElement(String dir, String el) => Elements.TryGetValue(dir + "|" + el, out var f) ? f : 0;

            /// <summary>The flags of element selectors scoped by a class / id of an ancestor (".pic img").</summary>
            internal int GetScoped(IEnumerable<String> ancestorTokens, String el)
            {
                int f = 0;
                foreach (var t in ancestorTokens)
                    if (Scoped.TryGetValue(t + "|" + el, out var x))
                        f |= x;
                return f;
            }
        }

        static readonly HashSet<String> VoidElements = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
        };

        static IEnumerable<String> TokensOf(HtmlNode n)
        {
            foreach (var c in (Attr(n, "class") ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                yield return "." + c;
            if (HasValue(n, "id"))
                yield return "#" + Attr(n, "id").Trim();
        }

        /// <summary>A relative src resolved against a folder ("lab" + "../x/y.js" -> "x/y.js"), without a query.</summary>
        static String ResolveRel(String dir, String src)
        {
            var parts = new List<String>(dir.Length == 0 ? Array.Empty<String>() : dir.Split('/'));
            foreach (var p in src.Split('?', '#')[0].Split('/'))
            {
                if ((p == ".") || (p.Length == 0))
                    continue;
                if (p == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                }
                else
                    parts.Add(p);
            }
            return String.Join("/", parts);
        }

        static String DirOf(String rel)
        {
            var i = rel.LastIndexOf('/');
            return i < 0 ? "" : rel.Substring(0, i);
        }

        static readonly Regex NoSize = new Regex(@"^(?:auto|none|fit-content|max-content|min-content|initial|unset|inherit|revert|revert-layer)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static int SizeFlags(String prop, String value)
        {
            var v = value.Replace("!important", "").Trim();
            if ((v.Length == 0) || NoSize.IsMatch(v))
                return 0;
            switch (prop)
            {
                case "width":
                case "inline-size":
                    return SizeW;
                case "height":
                case "block-size":
                    return SizeH;
                case "aspect-ratio":
                    return SizeA;
                case "inset":
                    return SizeW | SizeH;
                case "flex":
                case "flex-grow":
                    return Regex.IsMatch(v, @"^0*\.?0+(?:\s|$)") ? 0 : (SizeW | SizeH);
            }
            return 0;
        }

        // ------------------------------------------------------------------ html

        static bool Has(HtmlNode n, String a) => n.GetAttribute(a) != null;

        static String Attr(HtmlNode n, String a) => n.GetAttribute(a)?.Value;

        static bool HasValue(HtmlNode n, String a) => !String.IsNullOrWhiteSpace(Attr(n, a));

        /// <summary>
        /// The text inside an element (entities decoded, white space collapsed), null if it contains a template variable.
        /// </summary>
        static String InnerText(String text, List<HtmlNode> nodes, int index)
        {
            var n = nodes[index];
            if (n.SelfClosing)
                return "";
            var sb = new System.Text.StringBuilder();
            int depth = 0;
            for (int j = index + 1; j < nodes.Count; ++j)
            {
                var c = nodes[j];
                if ((c.Kind == HtmlNodeKind.EndTag) && (c.Name == n.Name))
                {
                    if (depth == 0)
                        break;
                    --depth;
                }
                else if ((c.Kind == HtmlNodeKind.StartTag) && (c.Name == n.Name) && !c.SelfClosing)
                    ++depth;
                else if (c.Kind == HtmlNodeKind.Text)
                    sb.Append(' ').Append(text, c.Start, c.End - c.Start);
            }
            var s = sb.ToString();
            if (s.Contains("${", StringComparison.Ordinal))
                return null;
            return Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();
        }

        /// <summary>"Save changes." -> "save changes" (for name / tool tip comparisons).</summary>
        static String Norm(String s) => Regex.Replace(s ?? "", @"\s+", " ").Trim().TrimEnd('.', '!', ':', '…', ' ').ToLowerInvariant();

        static readonly HashSet<String> OkYesNo = new HashSet<String>(StringComparer.Ordinal) { "ok", "okay", "yes", "no" };

        static readonly HashSet<String> FieldSkipTypes = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "hidden", "submit", "button", "reset", "image" };

        static readonly Regex OnlyPrevent = new Regex(@"^(?:\s*(?:(?:event|e|ev)\.(?:preventDefault|stopPropagation|stopImmediatePropagation)\(\)|return\s+false|this\.focus\(\))\s*;?)+\s*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Run the HTML UX rules of a page (style elements and inline scripts are checked by <see cref="Checker.CheckHtmlFile"/>).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="nodes">The scanned nodes</param>
        /// <param name="uiPage">False for documents that are not UI pages (mail templates, exports): only the zoom rule applies</param>
        public static void CheckHtml(Checker.FileContext ctx, List<HtmlNode> nodes, bool uiPage = true)
        {
            if (!Applies(ctx) || !uiPage)
                return;
            var text = ctx.Text;
            var dir = DirOf(ctx.WebRel);
            //  The page's own style elements (also without a folder index)
            var local = new SizeIndex();
            foreach (var n in nodes)
                if ((n.Kind == HtmlNodeKind.RawText) && (n.Name == "style"))
                    local.CollectCss(dir, text, n.Start, n.End);
            var global = ctx.Options.UxSizes;
            var forIds = new HashSet<String>(nodes.Where(n => (n.Kind == HtmlNodeKind.StartTag) && (n.Name == "label") && HasValue(n, "for")).Select(n => Attr(n, "for").Trim()), StringComparer.Ordinal);
            int labelDepth = 0, h1 = 0;
            //  A local script of the page makes the page header (its h1): a level 1 heading in the markup is a second one
            bool headerScript = (global != null) && (text.IndexOf(DesignChecker.NoPageHeader, StringComparison.Ordinal) < 0) && nodes.Any(n => (n.Kind == HtmlNodeKind.StartTag) && (n.Name == "script") && (Attr(n, "src") is String src)
                && !Regex.IsMatch(src, @"^(?:[a-z]+:)?//|^/|\$\{", RegexOptions.IgnoreCase) && global.HeaderScripts.Contains(ResolveRel(dir, src)));
            //  (round 5) Heading levels in order: the last level seen (the page header's h1 counts when a script makes it)
            int lastLevel = headerScript ? 1 : 0;
            //  The open elements (for ".pic img" sizes)
            var open = new List<(String Name, List<String> Tokens)>();
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if ((n.Kind == HtmlNodeKind.EndTag) && (n.Name == "label"))
                    labelDepth = Math.Max(0, labelDepth - 1);
                if (n.Kind == HtmlNodeKind.EndTag)
                {
                    var k = open.FindLastIndex(x => x.Name == n.Name);
                    if (k >= 0)
                        open.RemoveRange(k, open.Count - k);
                }
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                var name = n.Name;
                if (!n.SelfClosing && !VoidElements.Contains(name))
                    open.Add((name, TokensOf(n).ToList()));
                if ((name == "label") && !n.SelfClosing)
                    ++labelDepth;
                bool hidden = String.Equals(Attr(n, "aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) || Has(n, "hidden")
                    || Regex.IsMatch(Attr(n, "style") ?? "", @"display\s*:\s*none", RegexOptions.IgnoreCase);
                var style = n.GetAttribute("style");
                if ((style?.Value != null) && (style.Value.Length > 0) && (style.ValueStart >= 0))
                    CheckCss(ctx, style.ValueStart, style.ValueStart + style.Value.Length, true, name);
                //  ux-inp-1: inline release / press actions
                var ups = n.Attributes.Where(a => Regex.IsMatch(a.Name, @"^on(?:pointerup|mouseup|touchend)$", RegexOptions.IgnoreCase)).ToList();
                var downs = n.Attributes.Where(a => Regex.IsMatch(a.Name, @"^on(?:pointerdown|mousedown|touchstart)$", RegexOptions.IgnoreCase)).ToList();
                bool moves = n.Attributes.Any(a => Regex.IsMatch(a.Name, @"^on(?:pointermove|mousemove|touchmove)$", RegexOptions.IgnoreCase));
                if ((ups.Count > 0) && (downs.Count == 0) && !moves)
                    Add(ctx, Rule.UxInp1, ups[0].NameStart, "<" + name + " " + ups[0].Name + "> acts on release instead of click: keyboard (Enter / Space), screen readers and voice control never fire it; use onclick (with Sw.BadClick(event) for modifier clicks)");
                foreach (var a in downs)
                    if ((a.Value != null) && !OnlyPrevent.IsMatch(a.Value) && !Has(n, "onclick") && !moves && (ups.Count == 0))
                        Add(ctx, Rule.UxInp1, a.NameStart, "<" + name + " " + a.Name + "=\"" + Short(a.Value) + "\"> acts on press: it can't be cancelled by moving away and keyboard / assistive technology never fire it; act in onclick (a drag starts on press only after the dead zone, INP-7)");
                //  ux-inp-4: context menu blocked
                var cm = n.GetAttribute("oncontextmenu");
                if ((cm?.Value != null) && OnlyPrevent.IsMatch(cm.Value))
                    Add(ctx, Rule.UxInp4, cm.NameStart, "<" + name + " oncontextmenu=\"" + cm.Value + "\"> blocks the browser's context menu (copy, spell check, Inspect) without a menu of its own; remove it, or open a menu (Sw.LongPress opens it on right click, long press, Menu key and Shift+F10)");
                //  ux-inp-30: selectstart
                var ss = n.GetAttribute("onselectstart");
                if (ss != null)
                    Add(ctx, Rule.UxInp30, ss.NameStart, "<" + name + " onselectstart> takes text selection from the browser: chrome is not selectable by the theme (user-select: none), content opts in with .sw-selectable; drag, double click and Shift+click must select text as usual");
                //  ux-inp-12: native disabled on a button
                var dis = n.GetAttribute("disabled");
                if ((dis != null) && ((name == "button") || ((name == "input") && Regex.IsMatch(Attr(n, "type") ?? "", @"^\s*(?:button|submit|reset|image)\s*$", RegexOptions.IgnoreCase))))
                    Add(ctx, Rule.UxInp12, dis.NameStart, "<" + name + " disabled>: a disabled button leaves the tab order, so its reason can't be read; use Sw.Disabled.Set(el, true, \"Select a row first\") (Sw controls: SetDisabled(true, reason) or the option Disabled: \"reason\"): aria-disabled, it stays focusable, ignores the activation and shows the reason as its tool tip and on an activation attempt");
                //  ux-foc-4: positive tabindex
                var ti = n.GetAttribute("tabindex");
                if ((ti?.Value != null) && Int32.TryParse(ti.Value.Trim(), out var tv) && (tv > 0))
                    Add(ctx, Rule.UxFoc4, ti.NameStart, "tabindex=\"" + ti.Value + "\" moves the element ahead of the visual order for keyboard users: use 0 (in the order of the markup) or -1 (focus from code only), and order the markup as it is shown");
                //  ux-txt-12: translate="no" (lower case)
                var tr = n.GetAttribute("translate");
                if ((tr?.Value != null) && (tr.Value != "no") && (tr.Value != "yes"))
                    Add(ctx, Rule.UxTxt12, tr.NameStart, "translate=\"" + tr.Value + "\": the server translator only skips translate=\"no\" (lower case); write \"no\" (or \"yes\")");
                //  ux-inp-21: a link without href that acts or has text (not a link: not focusable, no Enter, not announced)
                if ((name == "a") && !Has(n, "href") && !Has(n, "role") && !hidden && (n.Attributes.Any(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) || !String.IsNullOrWhiteSpace(InnerText(text, nodes, i))))
                    Add(ctx, Rule.UxInp21, n.Start, "<a> without href (\"" + Short(InnerText(text, nodes, i) ?? "") + "\"): it is no link: not focusable, Enter does nothing and screen readers don't announce it; give it href (navigation) or make it a <button type=\"button\"> (an action)");
                //  ux-txt-13: words in attributes the server translator serves as written
                foreach (var a in n.Attributes)
                    if ((a.Value != null) && (a.Name.Equals("aria-description", StringComparison.OrdinalIgnoreCase) || a.Name.Equals("aria-valuetext", StringComparison.OrdinalIgnoreCase) || (a.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase) && !a.Name.Equals("data-translation_context", StringComparison.OrdinalIgnoreCase)))
                        && Regex.IsMatch(a.Value, @"^[^{}<>=;$]*$") && Regex.IsMatch(a.Value, @"\p{L}{2,}\s+\p{L}{2,}")
                        && (Regex.IsMatch(a.Value, @"^\s*\p{Lu}\p{Ll}|[.?!]\s*$") || Regex.IsMatch(a.Value, @"\p{L}{2,}\s+\p{L}{2,}\s+\p{L}{2,}")))
                    {
                        Add(ctx, Rule.UxTxt13, a.NameStart, a.Name + "=\"" + Short(a.Value) + "\": the server translator serves this attribute as written, so every language reads it in English; set it from JS with _TF(\"...\", \"context\") (title, placeholder, alt and aria-label may stay in the html)");
                        break;
                    }
                //  ux-mot-6
                if ((name == "marquee") || (name == "blink"))
                    Add(ctx, Rule.UxMot6, n.Start, "<" + name + ">: no endless or blinking motion; show the text still");
                //  ux-acc-3: one h1
                bool level1 = (name == "h1") || ((Attr(n, "role")?.Trim() == "heading") && (Attr(n, "aria-level")?.Trim() == "1"));
                bool inHeader = open.Any(x => (x.Name == "header") || x.Tokens.Any(c => c.StartsWith(".sw-ph", StringComparison.Ordinal))) || TokensOf(n).Any(c => c.StartsWith(".sw-ph", StringComparison.Ordinal));
                if (level1 && ((++h1 == 2) || ((h1 == 1) && headerScript && !inHeader)))
                    Add(ctx, Rule.UxAcc3, n.Start, (h1 == 1 ? "a level 1 heading (<" + name + (name == "h1" ? "" : " role=\"heading\" aria-level=\"1\"") + ">) in the markup of a page whose script makes the page header (Sw.PageLayout / Sw.PageHeader: the h1)" : "a second level 1 heading (<" + name + (name == "h1" ? "" : " role=\"heading\" aria-level=\"1\"") + ">) in the page markup") + ": one h1 per page (the page title); use h2 for sections");
                //  ux-acc-3 (round 5): heading levels that skip (h1 then h4)
                if (n.Kind == HtmlNodeKind.StartTag)
                {
                    int hl = (name.Length == 2) && (name[0] == 'h') && (name[1] >= '1') && (name[1] <= '6') ? name[1] - '0'
                        : ((Attr(n, "role")?.Trim() == "heading") && Int32.TryParse(Attr(n, "aria-level")?.Trim(), out var al) ? al : 0);
                    if (hl > 0)
                    {
                        if ((lastLevel > 0) && (hl > lastLevel + 1))
                            Add(ctx, Rule.UxAcc3, n.Start, "heading levels skip: <" + name + "> (level " + hl + ") after a level " + lastLevel + " heading: screen reader users jump by headings and miss the levels between (they think content is missing); use the next level (h" + (lastLevel + 1) + ") and style it with a class");
                        lastLevel = hl;
                    }
                }
                //  ux-acc-4 (round 5): a status / message element that a script fills, without a live region
                if ((n.Kind == HtmlNodeKind.StartTag) && ((name == "div") || (name == "p") || (name == "span") || (name == "output")) && (name != "output") && (Attr(n, "id")?.Trim() is String sid) && (sid.Length > 0)
                    && (global?.Helpers.ScriptIds.Contains(sid) == true) && Regex.IsMatch(sid + " " + (Attr(n, "class") ?? ""), @"(?i)(?:^|[\s_-])(?:status|message|msg|result|notice|feedback)(?:$|[\s_-])")
                    && !HasValue(n, "role") && !HasValue(n, "aria-live") && !open.Any(x => x.Tokens.Any(c => c.StartsWith("[aria-live", StringComparison.Ordinal))))
                    Add(ctx, Rule.UxAcc4, n.Start, "<" + name + " id=\"" + sid + "\"> is a status line a script fills, but no live region: screen reader users never hear the result; add role=\"status\" (role=\"alert\" for errors), or say it with Sw.Page.Announce");
                //  ux-frm-6 (round 5): a text field for an e-mail address, a phone number or a web address without its input type
                if ((name == "input") && (n.Kind == HtmlNodeKind.StartTag) && (((Attr(n, "type") ?? "text").Trim().ToLowerInvariant()) == "text"))
                {
                    var naming = String.Join(" ", new[] { Attr(n, "id"), Attr(n, "name"), Attr(n, "placeholder"), Attr(n, "aria-label"), Attr(n, "autocomplete") }.Where(x => x != null));
                    var want = Regex.IsMatch(naming, @"(?i)e-?mail|\bmail\b|mail$|^mail|Mail\b") ? "email" : Regex.IsMatch(naming, @"(?i)phone|\btel\b|mobile") ? "tel" : Regex.IsMatch(naming, @"(?i)\burl\b|website|homepage") ? "url" : null;
                    if (want != null)
                        Add(ctx, Rule.UxFrm6, n.Start, "<input type=\"text\"> for " + (want == "email" ? "an e-mail address" : want == "tel" ? "a phone number" : "a web address") + " (\"" + Short(naming) + "\"): phones show the wrong keyboard, autofill and the browser's check don't work; use type=\"" + want + "\" (and autocomplete=\"" + (want == "url" ? "url" : want) + "\" for the user's own data)");
                }
                //  ux-frm-1, ux-frm-6: fields
                if (((name == "input") && !FieldSkipTypes.Contains((Attr(n, "type") ?? "text").Trim())) || (name == "select") || (name == "textarea"))
                {
                    var type = (Attr(n, "type") ?? "text").Trim().ToLowerInvariant();
                    var id = Attr(n, "id")?.Trim();
                    bool named = HasValue(n, "aria-label") || HasValue(n, "aria-labelledby") || HasValue(n, "title") || (labelDepth > 0) || ((id != null) && forIds.Contains(id));
                    if (!named && !hidden && (type != "file"))
                        Add(ctx, Rule.UxFrm1, n.Start, "<" + name + (name == "input" ? " type=\"" + type + "\"" : "") + (id != null ? " id=\"" + id + "\"" : "") + "> has no label" + (HasValue(n, "placeholder") ? " (the placeholder \"" + Short(Attr(n, "placeholder")) + "\" is not one: it disappears while typing and is often not read)" : "") + ": add a visible <label for=\"...\"> (Sw.Form / Sw.Field make it), or aria-label for a field whose purpose is shown otherwise (a search box with a search icon)");
                    if ((name == "input") && (type == "password") && !Has(n, "autocomplete"))
                        Add(ctx, Rule.UxFrm6, n.Start, "<input type=\"password\"> without autocomplete: add autocomplete=\"current-password\" (sign in) or \"new-password\" (set / change), so password managers fill and save it (FRM-10)");
                }
                //  ux-lay-1: media without a reserved size
                if (((name == "img") || (name == "iframe") || (name == "video") || (name == "canvas") || (name == "embed") || (name == "object")) && !Has(n, "hidden") && !Regex.IsMatch(Attr(n, "style") ?? "", @"display\s*:\s*none", RegexOptions.IgnoreCase))
                {
                    int f = 0;
                    if (HasValue(n, "width"))
                        f |= SizeW;
                    if (HasValue(n, "height"))
                        f |= SizeH;
                    if ((style?.Value != null) && (style.ValueStart >= 0))
                        VisitCss(text, style.ValueStart, style.ValueStart + style.Value.Length, true, null, d => f |= SizeFlags(d.Prop, d.Value));
                    foreach (var t in TokensOf(n))
                        f |= local.Get(t) | (global?.Get(t) ?? 0);
                    f |= local.GetElement(dir, name) | (global?.GetElement(dir, name) ?? 0);
                    var anc = open.Where(x => x.Name != name).SelectMany(x => x.Tokens).ToList();
                    f |= local.GetScoped(anc, name) | (global?.GetScoped(anc, name) ?? 0);
                    if (((f & SizeA) == 0) && ((f & (SizeW | SizeH)) != (SizeW | SizeH)))
                    {
                        var what = (f & SizeW) != 0 ? "a width but no height" : (f & SizeH) != 0 ? "a height but no width" : "no size";
                        Add(ctx, Rule.UxLay1, n.Start, "<" + name + (HasValue(n, "class") ? " class=\"" + Attr(n, "class") + "\"" : "") + "> has " + what + " before it loads: the content below jumps when it arrives; give it width and height attributes (the intrinsic size, css may still scale it), a css width and height, or an aspect-ratio" + (name == "img" ? " (and fade it in with Sw.FadeSrc, LAY-3)" : ""));
                    }
                }
                //  ux-tip-2, ux-acc-10, ux-pop-9: buttons and links
                if ((name == "button") || ((name == "a") && Has(n, "href")) || ((name == "input") && Regex.IsMatch(Attr(n, "type") ?? "", @"^\s*(?:button|submit|reset)\s*$", RegexOptions.IgnoreCase)))
                {
                    var vis = name == "input" ? (Attr(n, "value") ?? "").Trim() : InnerText(text, nodes, i);
                    if (!String.IsNullOrEmpty(vis) && !vis.Contains("${", StringComparison.Ordinal))
                    {
                        var title = n.GetAttribute("title");
                        if ((title?.Value != null) && (Norm(title.Value) == Norm(vis)))
                            Add(ctx, Rule.UxTip2, title.NameStart, "the tool tip \"" + Short(title.Value) + "\" repeats the label: screen readers read both; say what the label doesn't (the target, the consequence or the shortcut: \"Save changes to the profile (Ctrl+S)\"), or leave the title out");
                        var al = n.GetAttribute("aria-label");
                        if ((al?.Value != null) && (al.Value.Trim().Length > 0) && !Norm(al.Value).StartsWith(Norm(vis), StringComparison.Ordinal))
                            Add(ctx, Rule.UxAcc10, al.NameStart, "aria-label=\"" + Short(al.Value) + "\" replaces the visible text \"" + Short(vis) + "\": voice control users say what they see (\"click " + Short(vis) + "\"); start the aria-label with the visible text, or drop it (put extra words in the title)");
                        if ((name != "a") && OkYesNo.Contains(Norm(vis)))
                            Add(ctx, Rule.UxPop9, n.Start, "a button labelled \"" + vis + "\": buttons are verbs that say what happens (\"Save\", \"Delete\", \"Close\"); the way out is \"Cancel\"");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ JavaScript

        static readonly HashSet<String> PointerEvents = new HashSet<String>(StringComparer.Ordinal)
        {
            "pointerup", "mouseup", "touchend", "pointerdown", "mousedown", "touchstart", "pointermove", "mousemove", "touchmove",
            "selectstart", "contextmenu", "transitionend", "animationend",
        };
        static readonly HashSet<String> UpEvents = new HashSet<String>(StringComparer.Ordinal) { "pointerup", "mouseup", "touchend" };
        static readonly HashSet<String> DownEvents = new HashSet<String>(StringComparer.Ordinal) { "pointerdown", "mousedown", "touchstart" };
        static readonly HashSet<String> MoveEvents = new HashSet<String>(StringComparer.Ordinal) { "pointermove", "mousemove", "touchmove" };
        static readonly HashSet<String> PageObjects = new HashSet<String>(StringComparer.Ordinal) { "document", "window", "body", "documentElement" };
        static readonly HashSet<String> ToastFunctions = new HashSet<String>(StringComparer.Ordinal) { "Toast", "Fail", "Info", "Warn" };
        static readonly HashSet<String> LabelKeys = new HashSet<String>(StringComparer.Ordinal) { "Text", "OkText", "CancelText", "YesText", "NoText", "Label" };
        static readonly HashSet<String> NameKeys = new HashSet<String>(StringComparer.Ordinal) { "Text", "Label" };
        static readonly HashSet<String> TrFunctions = new HashSet<String>(StringComparer.Ordinal) { "_T", "_TF", "_TH", "_TFH" };
        static readonly HashSet<String> MotionStyleProps = new HashSet<String>(StringComparer.Ordinal) { "transition", "transitionDuration", "transitionDelay", "animation", "animationDuration", "animationDelay" };
        static readonly Regex ButtonName = new Regex(@"(?:btn|button)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ZoomLock = new Regex(@"user-scalable\s*=\s*(?:no|0|false)\b|maximum-scale\s*=\s*(?:1(?:\.0*)?|0?\.\d+)(?![\d.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex StyleMotion = new Regex(@"(?<![\w-])(?:transition|animation)(?:-duration|-delay)?\s*:\s*[^;""'`]*?(?<![\w.-])(?!0*\.?0+m?s\b)\d*\.?\d+m?s\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex StyleWait = new Regex(@"(?<![\w-])cursor\s*:\s*wait\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Validation wording in a toast: "Name is required", "must be", "invalid email", "please enter", "can't be empty", "too long" ...
        /// </summary>
        static readonly Regex ValidationText = new Regex(
            @"\b(?:is|are)\s+required\b|\brequired\s+field|\bmust\s+(?:be|not\s+be|contain|have|start|end|match)\b|\b(?:is|are)\s+(?:invalid|not\s+valid)\b|\binvalid\s+(?:value|input|email|e-mail|number|name|format|date|time|url|address|password|characters?)\b"
            + @"|\bplease\s+(?:enter|fill|select|choose|provide|type|specify)\b|\b(?:can(?:'|no)t|cannot|may\s+not)\s+be\s+(?:empty|blank)\b|\b(?:is|are)\s+(?:empty|missing)\b|\btoo\s+(?:long|short)\b|\bat\s+least\s+\d+\s+characters\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The index of the bracket that closes the one at open ("(", "[" or "{"), -1 if none.</summary>
        internal static int MatchBracket(List<JsToken> sig, String text, int open) => MatchBrace(sig, text, open);

        static int MatchBrace(List<JsToken> sig, String text, int open)
        {
            int depth = 0;
            for (int i = open; i < sig.Count; ++i)
            {
                var t = sig[i];
                if (t.Kind != JsTokenKind.Punctuator)
                    continue;
                if (t.Is(text, "{") || t.Is(text, "(") || t.Is(text, "["))
                    ++depth;
                else if ((t.Is(text, "}") || t.Is(text, ")") || t.Is(text, "]")) && (--depth == 0))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The literal text of a value at sig[i]: a string literal, or the literal of a translation call (_TF("...", ...)); null otherwise.
        /// </summary>
        static String LiteralAt(List<JsToken> sig, String text, int i)
        {
            if (i >= sig.Count)
                return null;
            if (sig[i].Kind == JsTokenKind.String)
                return JsLexer.DecodeStringLiteral(sig[i].GetText(text));
            if ((sig[i].Kind == JsTokenKind.Identifier) && TrFunctions.Contains(sig[i].GetText(text)) && (i + 2 < sig.Count)
                && (sig[i + 1].Kind == JsTokenKind.Punctuator) && sig[i + 1].Is(text, "(") && (sig[i + 2].Kind == JsTokenKind.String))
                return JsLexer.DecodeStringLiteral(sig[i + 2].GetText(text));
            return null;
        }

        /// <summary>
        /// Run the JavaScript UX rules (a .js file or an inline script). The native dialogs (ux-pop-9) and own dialogs (ux-pop-1) of a script
        /// are reported by the design rules (<see cref="DesignChecker.CheckJs"/>).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens</param>
        public static void CheckJs(Checker.FileContext ctx, List<JsToken> tokens)
        {
            if (!Applies(ctx))
            {
                //  The framework's own files: only the "returns no promise" error (ux-fbk-2)
                if (!Checker.IsBundlePath(ctx.WebRel) && DesignChecker.IsFramework(ctx.WebRel) && !ctx.Options.Framework && (ctx.WebRel != "common/common.js") && !ctx.WebRel.EndsWith("app/application.js", StringComparison.Ordinal))
                {
                    NoPromiseOnly = true;
                    try
                    {
                        CheckPatterns(ctx, tokens.Where(t => t.IsSignificant).ToList());
                    }
                    finally
                    {
                        NoPromiseOnly = false;
                    }
                }
                return;
            }
            var text = ctx.Text;
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Id(int i, String p = null) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && ((p == null) || sig[i].Is(text, p));
            bool Key(int i) => Id(i) && P(i + 1, ":") && (P(i - 1, "{") || P(i - 1, ","));
            String S(int i) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.String) ? JsLexer.DecodeStringLiteral(sig[i].GetText(text)) : null;
            //  Event handlers: addEventListener("x", ...), on: { x: ... }, el.onx = ...
            var events = new List<(String Evt, int At, int Recv, int Open)>();
            int scriptStart = sig.Count > 0 ? sig[0].Start : 0, scriptEnd = sig.Count > 0 ? sig[sig.Count - 1].End : 0;
            bool hasTimer = Regex.IsMatch(text.Substring(scriptStart, scriptEnd - scriptStart), @"\b(?:setTimeout|requestAnimationFrame)\b");
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                var s = S(i);
                if ((s != null) && PointerEvents.Contains(s) && P(i - 1, "(") && (Id(i - 2, "addEventListener") || Id(i - 2, "on")))
                    events.Add((s, i, P(i - 3, ".") ? i - 4 : -1, i - 1));
                else if ((t.Kind == JsTokenKind.Identifier) && PointerEvents.Contains(t.GetText(text)) && Key(i))
                    events.Add((t.GetText(text), i, -1, -1));
                else if ((t.Kind == JsTokenKind.Identifier) && t.GetText(text).StartsWith("on", StringComparison.Ordinal) && PointerEvents.Contains(t.GetText(text).Substring(2)) && P(i - 1, ".") && P(i + 1, "="))
                    events.Add((t.GetText(text).Substring(2), i, i - 2, -1));
            }
            bool anyDown = events.Any(e => DownEvents.Contains(e.Evt)), anyMove = events.Any(e => MoveEvents.Contains(e.Evt));
            foreach (var e in events)
            {
                //  ux-inp-1: a lone release handler
                if (UpEvents.Contains(e.Evt) && !anyDown && !anyMove)
                    Add(ctx, Rule.UxInp1, sig[e.At].Start, "a " + e.Evt + " handler without a press / move handler acts on release instead of click: Enter / Space, screen readers and voice control never fire it; use a click handler (Sw.BadClick(ev) skips modifier clicks)");
                //  ux-inp-30
                if (e.Evt == "selectstart")
                    Add(ctx, Rule.UxInp30, sig[e.At].Start, "a selectstart handler takes text selection from the browser: chrome is not selectable by the theme (user-select: none), content opts in with .sw-selectable");
                //  ux-inp-4: contextmenu blocked on the whole page
                if ((e.Evt == "contextmenu") && (e.Recv >= 0) && Id(e.Recv) && PageObjects.Contains(sig[e.Recv].GetText(text)))
                {
                    int end = e.Open >= 0 ? MatchBrace(sig, text, e.Open) : Math.Min(sig.Count - 1, e.At + 12);
                    if (end > e.At)
                    {
                        var h = text.Substring(sig[e.At].End, sig[end].End - sig[e.At].End);
                        if ((Regex.IsMatch(h, @"\bpreventDefault\b") || Regex.IsMatch(h, @"=\s*(?:\(\s*\w*\s*\)\s*=>|function\s*\(\s*\w*\s*\)\s*\{\s*return)\s*false\b")) && !Regex.IsMatch(h, @"\bif\b|\?|&&|\|\||\bclosest\b|\btarget\b|[Mm]enu\b|\bswitch\b"))
                            Add(ctx, Rule.UxInp4, sig[e.At].Start, "a contextmenu handler on " + sig[e.Recv].GetText(text) + " blocks the browser's context menu everywhere (copy, spell check in fields, Inspect); block it only on elements that open a menu of their own (Sw.LongPress)");
                    }
                }
            }
            //  ux-mot-2: a transitionend wait without a fallback timer
            var te = events.FirstOrDefault(e => (e.Evt == "transitionend") || (e.Evt == "animationend"));
            if ((te.Evt != null) && !hasTimer)
                Add(ctx, Rule.UxMot2, sig[te.At].Start, "waits for " + te.Evt + " without a fallback timer: with a zero duration theme (Hacker) or reduced motion the event may never come; also finish in a setTimeout of the token duration");
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                if ((t.Kind == JsTokenKind.String) || t.IsTemplate)
                {
                    var raw = t.GetText(text);
                    Match m;
                    //  ux-inp-25: zoom lock in a script
                    if ((m = ZoomLock.Match(raw)).Success && Regex.IsMatch(raw, @"\b(?:width\s*=|initial-scale|user-scalable|maximum-scale)", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxInp25, t.Start + m.Index, "\"" + m.Value + "\" in a viewport blocks pinch zoom: people with low vision can't enlarge the page (WCAG 1.4.4); use \"width=device-width, initial-scale=1\"");
                    //  ux-mot-2: style strings with a time
                    //  (a token with a fallback, also cut by a concatenation: "transition: opacity var(--sw-dur-1, 90ms" + ...)
                    var noVar = Regex.Replace(raw, @"var\(\s*--[\w-]+\s*(?:,[^()""'`]*(?:\([^()]*\)[^()""'`]*)*)?\)?", x => new String(' ', x.Length));
                    if ((m = StyleMotion.Match(noVar)).Success && !Regex.IsMatch(raw, @"\binfinite\b", RegexOptions.IgnoreCase))
                        Add(ctx, Rule.UxMot2, t.Start + m.Index, "the style \"" + Short(m.Value) + "\" hard-codes a duration: use var(--sw-dur-1 / -2 / -3) (each theme sets them, reduced motion makes them 1 ms)");
                    //  ux-inp-10: cursor wait in a style string
                    if ((m = StyleWait.Match(raw)).Success)
                        Add(ctx, Rule.UxInp10, t.Start + m.Index, "\"" + m.Value + "\": never used; a busy control shows cursor: progress on itself (Sw.Button does it)");
                    continue;
                }
                if (t.Kind != JsTokenKind.Identifier)
                    continue;
                var id = t.GetText(text);
                switch (id)
                {
                    case "disabled":
                        //  ux-inp-12: saveButton.disabled = true
                        if (P(i - 1, ".") && P(i + 1, "=") && Id(i - 2) && ButtonName.IsMatch(sig[i - 2].GetText(text)) && !Id(i + 2, "false"))
                            Add(ctx, Rule.UxInp12, t.Start, sig[i - 2].GetText(text) + ".disabled = ...: a disabled button leaves the tab order and its reason can't be read; use Sw.Disabled.Set(el, true, \"Select a row first\") (Sw controls: SetDisabled(true, reason) or the option Disabled: \"reason\"): aria-disabled, the button stays focusable, ignores the activation and says why");
                        break;
                    case "setAttribute":
                    case "toggleAttribute":
                        if (P(i - 1, ".") && P(i + 1, "(") && Id(i - 2))
                        {
                            var an = S(i + 2)?.ToLowerInvariant();
                            if ((an == "disabled") && ButtonName.IsMatch(sig[i - 2].GetText(text)))
                                Add(ctx, Rule.UxInp12, t.Start, sig[i - 2].GetText(text) + "." + id + "(\"disabled\"): a disabled button leaves the tab order and its reason can't be read; use Sw.Disabled.Set(el, true, \"Select a row first\") (Sw controls: SetDisabled(true, reason) or the option Disabled: \"reason\")");
                            if ((an == "tabindex") && P(i + 3, ","))
                            {
                                var v = S(i + 4) ?? ((i + 4 < sig.Count) && (sig[i + 4].Kind == JsTokenKind.Number) ? sig[i + 4].GetText(text) : null);
                                if ((v != null) && Int32.TryParse(v.Trim(), out var n) && (n > 0))
                                    Add(ctx, Rule.UxFoc4, t.Start, "setAttribute(\"tabindex\", " + v + "): a positive tabindex moves the element ahead of the visual order; use 0 or -1");
                            }
                        }
                        break;
                    case "tabIndex":
                    case "tabindex":
                        {
                            int vi = (P(i - 1, ".") && P(i + 1, "=")) ? i + 2 : (Key(i) ? i + 2 : -1);
                            if (vi > 0)
                            {
                                var v = S(vi) ?? ((vi < sig.Count) && (sig[vi].Kind == JsTokenKind.Number) ? sig[vi].GetText(text) : null);
                                if ((v != null) && Int32.TryParse(v.Trim(), out var n) && (n > 0))
                                    Add(ctx, Rule.UxFoc4, t.Start, id + " " + (Key(i) ? ":" : "=") + " " + v + ": a positive tabindex moves the element ahead of the visual order for keyboard users; use 0 or -1 and order the markup as it is shown");
                            }
                        }
                        break;
                    case "altKey":
                        {
                            //  ux-inp-23: e.altKey && e.key === "ArrowLeft" (not "!e.altKey")
                            bool negated = (P(i - 1, ".") && P(i - 3, "!")) || P(i - 1, "!");
                            if (negated)
                                break;
                            var (line, _) = ctx.Source.GetLineCol(t.Start);
                            for (int k = Math.Max(0, i - 12); (k < sig.Count) && (k <= i + 12); ++k)
                            {
                                var a = S(k);
                                if (((a == "ArrowLeft") || (a == "ArrowRight") || (a == "Left") || (a == "Right")) && (ctx.Source.GetLineCol(sig[k].Start).Line == line)
                                    && !Enumerable.Range(Math.Min(i, k), Math.Abs(i - k)).Any(x => Id(x, "return") || P(x, ";")))
                                {
                                    Add(ctx, Rule.UxInp23, t.Start, "a shortcut on Alt+" + a + ": Alt+Left / Alt+Right are the browser's Back / Forward; use another key combination (and name it in the tool tip and aria-keyshortcuts)");
                                    break;
                                }
                            }
                        }
                        break;
                    case "cursor":
                        //  ux-inp-10: document.body.style.cursor = "progress" / "wait"
                        if (P(i - 1, ".") && P(i + 1, "=") && Id(i - 2, "style"))
                        {
                            var v = S(i + 2);
                            if (v == "wait")
                                Add(ctx, Rule.UxInp10, t.Start, "style.cursor = \"wait\": never used; a busy control shows cursor: progress on itself (Sw.Button does it)");
                            else if ((v == "progress") && P(i - 3, ".") && Id(i - 4) && PageObjects.Contains(sig[i - 4].GetText(text)))
                                Add(ctx, Rule.UxInp10, t.Start, sig[i - 4].GetText(text) + ".style.cursor = \"progress\": a busy cursor on the whole page; only the busy control shows it (Sw.Button with a promise from OnClick), the page stays usable");
                        }
                        break;
                    case "animate":
                        //  ux-mot-2: el.animate(frames, { duration: 200 })
                        if (P(i - 1, ".") && P(i + 1, "("))
                        {
                            int close = MatchBrace(sig, text, i + 1);
                            for (int k = i + 2; (k > 0) && (k < close); ++k)
                                if (Id(k, "duration") && P(k + 1, ":") && (k + 2 < sig.Count) && (sig[k + 2].Kind == JsTokenKind.Number) && Double.TryParse(sig[k + 2].GetText(text), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms) && (ms > 0)
                                    && !Regex.IsMatch(text.Substring(sig[i].Start, sig[close].End - sig[i].Start), @"\bInfinity\b"))
                                {
                                    Add(ctx, Rule.UxMot2, sig[k].Start, "animate(..., { duration: " + sig[k + 2].GetText(text) + " }) hard-codes the duration: read the theme's token (parseFloat(getComputedStyle(el).getPropertyValue(\"--sw-dur-2\")) ...) so themes without motion and reduced motion apply");
                                    break;
                                }
                            //  ux-mot-4: keyframes of a layout property (animate([{ width: "0%" }, { width: "40%" }]))
                            for (int k = i + 2; (k > 0) && (k < close); ++k)
                                if (Key(k) && Regex.IsMatch(sig[k].GetText(text), @"^(?:width|height|top|left|right|bottom|inset|margin\w*|padding\w*|maxHeight|maxWidth|minHeight|minWidth|fontSize)$"))
                                {
                                    Add(ctx, Rule.UxMot4, sig[k].Start, "animate(...) of " + sig[k].GetText(text) + ": the layout moves every frame (slow, and the content jumps); animate transform (scaleX for a bar) or opacity");
                                    break;
                                }
                        }
                        break;
                    default:
                        //  ux-mot-2: el.style.transition = "opacity 200ms"
                        if (MotionStyleProps.Contains(id) && P(i - 1, ".") && Id(i - 2, "style") && P(i + 1, "="))
                        {
                            var v = S(i + 2);
                            if ((v != null) && !Regex.IsMatch(v, @"\binfinite\b") && (TimeValue.Matches(StripVar(v)).Cast<Match>().FirstOrDefault(x => Ms(x) > 0) is Match tm))
                                Add(ctx, Rule.UxMot2, t.Start, "style." + id + " = \"" + Short(v) + "\" hard-codes " + tm.Value + ": use var(--sw-dur-1 / -2 / -3) (each theme sets them, reduced motion makes them 1 ms)");
                        }
                        //  ux-frm-3: Sw.Toast("Name is required")
                        if (ToastFunctions.Contains(id) && P(i - 1, ".") && Id(i - 2, "Sw") && P(i + 1, "("))
                        {
                            var lit = LiteralAt(sig, text, i + 2);
                            if ((lit != null) && ValidationText.IsMatch(lit))
                                Add(ctx, Rule.UxFrm3, t.Start, "Sw." + id + "(\"" + Short(lit) + "\") shows a validation error as a toast: it disappears and is far from the field; show it under the field (Sw.Form / Validate() set aria-invalid and aria-describedby) and move the focus to the first invalid field");
                        }
                        //  ux-pop-9: { Text: _TF("OK") }
                        if (LabelKeys.Contains(id) && Key(i))
                        {
                            var lit = LiteralAt(sig, text, i + 2);
                            if ((lit != null) && OkYesNo.Contains(Norm(lit)))
                                Add(ctx, Rule.UxPop9, t.Start, id + ": \"" + lit + "\": buttons are verbs that say what happens (\"Save\", \"Delete\", \"Close\"); the way out is \"Cancel\"");
                        }
                        //  ux-tip-2: { Text: "Save", Title: "Save" }
                        if ((id == "Title") && Key(i))
                        {
                            var title = LiteralAt(sig, text, i + 2);
                            if (String.IsNullOrWhiteSpace(title))
                                break;
                            //  The enclosing object literal: back to its "{" at depth 0
                            int depth = 0, open = -1;
                            for (int k = i - 1; k >= 0; --k)
                            {
                                if (P(k, "}") || P(k, ")") || P(k, "]"))
                                    ++depth;
                                else if (P(k, "{") || P(k, "(") || P(k, "["))
                                {
                                    if (depth == 0)
                                    {
                                        open = P(k, "{") ? k : -1;
                                        break;
                                    }
                                    --depth;
                                }
                            }
                            if (open < 0)
                                break;
                            int close = MatchBrace(sig, text, open);
                            depth = 0;
                            for (int k = open + 1; k < close; ++k)
                            {
                                if (P(k, "{") || P(k, "(") || P(k, "["))
                                    ++depth;
                                else if (P(k, "}") || P(k, ")") || P(k, "]"))
                                    --depth;
                                else if ((depth == 0) && Id(k) && NameKeys.Contains(sig[k].GetText(text)) && Key(k))
                                {
                                    var name = LiteralAt(sig, text, k + 2);
                                    if ((name != null) && (Norm(name) == Norm(title)))
                                        Add(ctx, Rule.UxTip2, t.Start, "Title: \"" + Short(title) + "\" repeats the " + sig[k].GetText(text) + ": screen readers read both; say what the label doesn't (the target, the consequence or the shortcut: \"Save changes to the profile (Ctrl+S)\"), or leave the Title out");
                                    break;
                                }
                            }
                        }
                        break;
                }
            }
            CheckPatterns(ctx, sig);
        }
    }
}
