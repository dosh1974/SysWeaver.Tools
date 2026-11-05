using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SwWeb.Lexing;
using SwWeb.Migrating;

namespace SwWeb.Checking
{
    /// <summary>
    /// The Design v2 porting rules (<see cref="Rule.Design"/>, "check --design", "migrate --level design"): page anatomy, elevation,
    /// gradients, hard-coded colours, old tokens and class hooks, inline styles and accessible custom controls. They only look at page
    /// files: the framework's own folders (sw/, core/, ui/, shell/, common/, brand/ and the legacy bundles) are not checked.
    /// A finding on a line marked with <see cref="AllowDesign"/> (or below such a line) is allowed. See PORTING.md in
    /// SysWeaver.HttpServer/srcWeb for the rules an app author follows.
    /// </summary>
    public static class DesignChecker
    {
        /// <summary>
        /// The marker of a deliberate design exception: in a comment on the line (or the line before) it allows a design rule finding,
        /// ex: <c>/* SwWeb:allow-design (a colour swatch shows the real colour) */</c>.
        /// </summary>
        public const String AllowDesign = "SwWeb:allow-design";

        /// <summary>
        /// The marker of a page that deliberately has no page header (a dialog, an embed, a full window app): anywhere in the HTML file.
        /// </summary>
        public const String NoPageHeader = "SwWeb:no-page-header";

        static MigrationMap DefaultMap;

        /// <summary>
        /// The map whose "Tokens" and "Css" entries the rules use (the options' map, else the embedded default).
        /// </summary>
        static MigrationMap MapOf(CheckOptions o)
        {
            if (o.Map != null)
                return o.Map;
            return DefaultMap ??= MigrationMap.Load();
        }

        /// <summary>
        /// True for the framework's own files, which the design rules don't check (they define the design).
        /// </summary>
        /// <param name="webRel">The path relative to the web root</param>
        /// <returns>True for a framework file</returns>
        public static bool IsFramework(String webRel) =>
            Checker.IsFrameworkCss(webRel) || (webRel == "app/application.js") || webRel.EndsWith("/app/application.js", StringComparison.Ordinal);

        // ------------------------------------------------------------------ css declarations

        /// <summary>
        /// A css declaration found by <see cref="VisitDeclarations"/>.
        /// </summary>
        sealed class Decl
        {
            public String Prop;
            public int PropStart;
            public String Value;
            public int ValueStart;
            public String Selector;
            public bool InForcedColors;
            public bool InFontFace;
            public bool InPrint;
            public bool InSizeMedia;
            public bool Indirect;
            /// <summary>Appended to the messages of a resolved own custom property (" (through var(--x) = ...)").</summary>
            public String Note = "";
        }

        /// <summary>
        /// Blank css comments (same length, offsets stay).
        /// </summary>
        static String Blank(String text, int start, int end) =>
            Regex.Replace(text.Substring(start, end - start), @"/\*.*?(?:\*/|$)", m => new String(' ', m.Length), RegexOptions.Singleline);

        static readonly Regex PropName = new Regex(@"^\s*(?<p>-{0,2}[A-Za-z_][\w-]*)\s*:", RegexOptions.CultureInvariant);

        /// <summary>
        /// Visit the declarations of a css range (a file, a style element, or an inline style attribute when <paramref name="inline"/>).
        /// </summary>
        static void VisitDeclarations(String text, int start, int end, bool inline, Action<Decl> visit)
        {
            var s = Blank(text, start, end);
            var stack = new List<String>();
            int seg = 0, depth = 0;
            char q = '\0';
            void Segment(int e, char term)
            {
                var part = s.Substring(seg, e - seg);
                if (term == '{')
                {
                    stack.Add(part.Trim());
                    return;
                }
                if (!inline && (stack.Count == 0))
                    return;
                var m = PropName.Match(part);
                if (!m.Success)
                    return;
                var colon = m.Index + m.Length;
                var value = part.Substring(colon);
                var trimmed = value.TrimEnd();
                int lead = value.Length - value.TrimStart().Length;
                var d = new Decl
                {
                    Prop = m.Groups["p"].Value.StartsWith("--", StringComparison.Ordinal) ? m.Groups["p"].Value : m.Groups["p"].Value.ToLowerInvariant(),
                    PropStart = start + seg + m.Groups["p"].Index,
                    Value = trimmed.TrimStart(),
                    ValueStart = start + seg + colon + lead,
                };
                foreach (var p in stack)
                {
                    if (p.StartsWith("@", StringComparison.Ordinal))
                    {
                        if (p.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase))
                            d.InFontFace = true;
                        if (p.IndexOf("forced-colors", StringComparison.OrdinalIgnoreCase) >= 0)
                            d.InForcedColors = true;
                        if (Regex.IsMatch(p, @"^@media\b[^{]*\bprint\b", RegexOptions.IgnoreCase))
                            d.InPrint = true;
                        if (Regex.IsMatch(p, @"^@media[^{]*max-(?:width|height)", RegexOptions.IgnoreCase))
                            d.InSizeMedia = true;
                    }
                    else
                        d.Selector = p;
                }
                visit(d);
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
                        stack.RemoveAt(stack.Count - 1);
                    seg = i + 1;
                }
            }
            if (seg < s.Length)
                Segment(s.Length, ';');
        }

        // ------------------------------------------------------------------ css rules

        static readonly HashSet<String> ShadowProps = new HashSet<String>(StringComparer.Ordinal) { "box-shadow", "-webkit-box-shadow", "text-shadow" };
        static readonly HashSet<String> FilterProps = new HashSet<String>(StringComparer.Ordinal) { "filter", "-webkit-filter", "backdrop-filter", "-webkit-backdrop-filter" };
        static readonly HashSet<String> GradientProps = new HashSet<String>(StringComparer.Ordinal) { "background", "background-image", "border-image", "border-image-source", "list-style-image" };
        static readonly HashSet<String> CssWideKeywords = new HashSet<String>(StringComparer.Ordinal) { "none", "initial", "inherit", "unset", "revert", "revert-layer" };

        /// <summary>Properties whose named colour keywords (red, white ...) are reported (hex and colour functions are reported in every property).</summary>
        static readonly HashSet<String> ColorProps = new HashSet<String>(StringComparer.Ordinal)
        {
            "color", "background", "background-color", "border", "border-color", "border-top", "border-right", "border-bottom", "border-left",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "border-block", "border-block-color",
            "border-block-start", "border-block-end", "border-inline", "border-inline-color", "border-inline-start", "border-inline-end",
            "outline", "outline-color", "fill", "stroke", "box-shadow", "-webkit-box-shadow", "text-shadow", "caret-color", "accent-color",
            "text-decoration", "text-decoration-color", "column-rule", "column-rule-color", "scrollbar-color", "stop-color", "flood-color",
            "lighting-color", "-webkit-text-stroke", "-webkit-text-stroke-color", "-webkit-text-fill-color", "-webkit-tap-highlight-color",
            "text-emphasis-color",
        };

        /// <summary>The css named colours (transparent and currentColor are not hard-coded colours).</summary>
        static readonly HashSet<String> NamedColors = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond", "blue", "blueviolet", "brown",
            "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
            "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred",
            "darksalmon", "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue",
            "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
            "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush",
            "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
            "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow", "lime",
            "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen",
            "mediumslateblue", "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
            "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
            "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple", "red", "rosybrown",
            "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna", "silver", "skyblue", "slateblue", "slategray",
            "slategrey", "snow", "springgreen", "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white",
            "whitesmoke", "yellow", "yellowgreen",
        };

        static readonly HashSet<String> ColorFunctions = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color",
        };

        static readonly Regex VarRef = new Regex(@"var\(\s*(?<n>--[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        static readonly Regex ThemeDerived = new Regex(@"^--Theme(?<b>Background|Main|Acc1|Acc2)(?<k>25|50|75|RGB|R|G|B)$", RegexOptions.CultureInvariant);
        static readonly Regex Gradient = new Regex(@"(?<![\w-])(?:linear|radial|conic)-gradient\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex RingItem = new Regex(@"^(?:inset\s+)?0(?:px)?\s+0(?:px)?\s+0(?:px)?\s+[\d.]+(?:px|rem|em)?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex InsetLineItem = new Regex(@"^inset\s+-?[\d.]+(?:px|rem|em)?\s+-?[\d.]+(?:px|rem|em)?(?:\s+0(?:px)?)?(?:\s+(?![\d.+-])|\s*$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex SingleVar = new Regex(@"^var\(\s*--[A-Za-z0-9_-]+\s*(?:,[^()]*(?:\([^()]*\)[^()]*)*)?\)$", RegexOptions.CultureInvariant);
        static readonly Regex ElevToken = new Regex(@"^var\(\s*--sw-(?:elev-[0-2]|focus-ring)\s*(?:,[^)]*)?\)$", RegexOptions.CultureInvariant);

        static readonly Regex HardStop = new Regex(@"(?<![\w.])(?<l>-?\d*\.?\d+(?:px|%|em|rem))\s*,\s*[^,()]*?(?:\([^()]*\))?[^,()]*?\s\k<l>(?![\w.])", RegexOptions.CultureInvariant);

        /// <summary>
        /// True if a gradient value draws a pattern, not a shading: a checkerboard (45deg with 25% / 75% stops) or hard colour stops
        /// (the same position twice in a row: grid lines "rgba(0, 0, 0, 0.06) 1px, transparent 1px", stripes).
        /// </summary>
        static bool IsPattern(String v) =>
            (v.Contains("45deg", StringComparison.Ordinal) && v.Contains("25%", StringComparison.Ordinal) && v.Contains("75%", StringComparison.Ordinal))
            || HardStop.IsMatch(v);

        static readonly Regex RadiusProp =new Regex(@"^border-(?:(?:top|bottom|start|end)-(?:left|right|start|end)-)?radius$", RegexOptions.CultureInvariant);
        static readonly Regex FixedLength = new Regex(@"(?<![\w.-])(?!0*\.?0+(?:px|rem|em)\b)\d*\.?\d+(?:px|rem|em)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex PillClass = new Regex(@"\.(?:[\w-]+[-_])?(?:stat|pill|fact)s?(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The last compound of the last selector of a selector list (the subject), without pseudo classes / elements.
        /// </summary>
        static String LastCompound(String selector)
        {
            var parts = selector.Split(',');
            var last = parts[parts.Length - 1].Trim();
            var sp = last.LastIndexOfAny(new[] { ' ', '>', '+', '~', '\t', '\n' });
            var c = sp < 0 ? last : last.Substring(sp + 1);
            var p = c.IndexOf(':');
            return p < 0 ? c : c.Substring(0, p);
        }

        /// <summary>
        /// Selectors on framework structure that changed in the fix pass (a class rename is a map "Css" entry instead): the table frame
        /// moved its rows into div.sw-table-area and the search / tools into the tool bar (PORTING.md B13).
        /// </summary>
        static readonly (Regex, String)[] StaleSelectors =
        {
            (new Regex(@"\.sw-table(?:-page)?\s*>\s*\.sw-table-(?:scroll|skel|empty|error|denied)\b", RegexOptions.CultureInvariant),
                "no longer matches: the rows, skeleton and states of a table are inside div.sw-table-area now (write \".sw-table-area > ...\" or drop the \">\")"),
            (new Regex(@"\.sw-table-foot\s*>\s*\.sw-table-(?:search|tools)\b", RegexOptions.CultureInvariant),
                "no longer matches: the search field and the table tools moved from the footer to the tool bar (.sw-table-toolbar, .sw-table-ttools > .sw-table-tbtn)"),
            //  explore/api.html is a Design v2 page (PORTING.md B14): its old boxed sections are gone
            (new Regex(@"\.api-(?:section|head|tags|send|desc|lock|message-title|message-text|message-actions)(?![\w-])", RegexOptions.CultureInvariant),
                "no longer exists: explore/api.html has a page header (.sw-ph), a meta line (.api-meta with .api-tag badges), the signature (.api-signature) and two cards (.api-card.api-input / .api-output) now; style those or the Sw tokens"),
        };

        /// <summary>
        /// Split a css value at top level commas.
        /// </summary>
        static List<String> SplitTop(String v)
        {
            var res = new List<String>();
            int depth = 0, st = 0;
            for (int i = 0; i < v.Length; ++i)
            {
                var c = v[i];
                if (c == '(')
                    ++depth;
                else if ((c == ')') && (depth > 0))
                    --depth;
                else if ((c == ',') && (depth == 0))
                {
                    res.Add(v.Substring(st, i - st).Trim());
                    st = i + 1;
                }
            }
            res.Add(v.Substring(st).Trim());
            return res;
        }

        static String Short(String v) => v.Length > 70 ? (v.Substring(0, 67) + "...") : v;

        /// <summary>
        /// Run the css design rules on a range (a .css file, a style element or a style attribute value).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="start">Start offset</param>
        /// <param name="end">End offset</param>
        /// <param name="inline">True for a style attribute (a declaration list)</param>
        public static void CheckCss(Checker.FileContext ctx, int start, int end, bool inline)
        {
            if ((end <= start) || IsFramework(ctx.WebRel))
                return;
            {
                //  An own palette: many literal colours in custom property definitions (not the brand style sheet, whose themes do that)
                if (!Checker.IsBrandCss(ctx.WebRel))
                {
                    var pm = OwnPalette.Matches(ctx.Text.Substring(start, end - start));
                    if (pm.Count >= 8)
                        ctx.Add(Rule.DesignOwnUi, start + pm[0].Index, "an own colour palette (" + pm.Count + " literal colours in custom properties): page css uses the role tokens (--sw-surface, --sw-text, --sw-text-2, --sw-border, --sw-accent1, --sw-danger ...) so it follows the five themes; a brand palette belongs in app/app.css (PORTING.md B4, B16)");
                }
                var dm = OwnDarkScheme.Match(ctx.Text, start, end - start);
                if (dm.Success)
                    ctx.Add(Rule.DesignOwnUi, dm.Index, "own light / dark css (@media (prefers-color-scheme)): the SysWeaver themes (Light, Dark, Pro, Application, Hacker; the user's choice, not only the OS) set the role tokens, use them instead (PORTING.md B16)");
            }
            var map = MapOf(ctx.Options);
            //  Per rule (selector): pill like declarations (design-pill-facts)
            var pills = new Dictionary<String, (bool Frame, bool Flex, int At)>(StringComparer.Ordinal);
            //  Per rule with :focus: an outline removed (offset) and a replacement (a11y-focus)
            var focus = new Dictionary<String, (int At, bool Replaced)>(StringComparer.Ordinal);
            VisitDeclarations(ctx.Text, start, end, inline, d =>
            {
                CheckTokens(ctx, map, d);
                if (d.InFontFace)
                    return;
                //  An own custom property with a literal colour in a style attribute: an inline colour with an extra step
                if (inline && d.Prop.StartsWith("--", StringComparison.Ordinal) && !d.Prop.StartsWith("--sw-", StringComparison.Ordinal))
                {
                    var (co, cw) = FindColor(d.Value, true);
                    if (co >= 0)
                        ctx.Add(Rule.DesignColor, d.ValueStart + co, "\"" + d.Prop + ": " + Short(d.Value) + "\" in a style attribute: the hard-coded colour \"" + cw + "\" doesn't follow the themes; an own token is defined in the page css (per theme if needed), the markup uses classes");
                }
                CheckFont(ctx, d);
                if (!inline && (d.Selector != null))
                {
                    CheckHiddenHeader(ctx, d, start, end);
                    if (d.Selector.IndexOf(":focus", StringComparison.Ordinal) >= 0)
                    {
                        focus.TryGetValue(d.Selector, out var fr);
                        if (((d.Prop == "outline") || (d.Prop == "outline-style") || (d.Prop == "outline-width")) && Regex.IsMatch(d.Value, @"^(?:none|0(?:px)?|hidden)\b", RegexOptions.IgnoreCase))
                            fr.At = fr.At == 0 ? d.PropStart : fr.At;
                        else if (((d.Prop == "outline") || (d.Prop == "outline-color") || (d.Prop == "box-shadow") || d.Prop.StartsWith("border", StringComparison.Ordinal) || (d.Prop == "text-decoration") || d.Prop.StartsWith("background", StringComparison.Ordinal))
                            && !Regex.IsMatch(d.Value, @"^(?:none|0|transparent|initial|unset)\b", RegexOptions.IgnoreCase))
                            fr.Replaced = true;
                        focus[d.Selector] = fr;
                    }
                }
                bool reported = JudgeShape(ctx, d, out bool shadow);
                //  An own custom property (--my-shadow: 0 8px 24px ...; box-shadow: var(--my-shadow)): judge its values too
                if (!reported && (ctx.Options.OwnProperties != null) && (RadiusProp.IsMatch(d.Prop) || ShadowProps.Contains(d.Prop) || FilterProps.Contains(d.Prop) || GradientProps.Contains(d.Prop)))
                    foreach (var r in Resolve(d, ctx.Options.OwnProperties))
                        if (JudgeShape(ctx, r, out shadow))
                            break;
                if (!inline && (d.Selector != null) && PillClass.IsMatch(LastCompound(d.Selector)))
                {
                    pills.TryGetValue(d.Selector, out var pr);
                    if (pr.At == 0)
                        pr.At = d.PropStart;
                    if ((d.Prop.StartsWith("border", StringComparison.Ordinal) && !Regex.IsMatch(d.Value, @"^\s*(?:none|0)\b")) || (d.Prop == "background") || (d.Prop == "background-color"))
                        pr.Frame = true;
                    //  A chip shape: a flex / inline-block box, or a fully rounded one (border-radius: 999px / 50vh / the pill token)
                    if (((d.Prop == "display") && Regex.IsMatch(d.Value, @"\b(?:inline-flex|flex|inline-block)\b"))
                        || ((d.Prop == "border-radius") && Regex.IsMatch(d.Value, @"(?:(?<![\d.])\d{3,}(?:px|rem|em)|\d+vh|--sw-radius-pill)")))
                        pr.Flex = true;
                    pills[d.Selector] = pr;
                }
                //  (the colour of a reported shadow is part of that finding)
                if (!shadow && !d.Prop.StartsWith("--", StringComparison.Ordinal) && !d.InForcedColors && !d.Prop.StartsWith("mask", StringComparison.Ordinal) && !d.Prop.StartsWith("-webkit-mask", StringComparison.Ordinal))
                {
                    var (off, what) = FindColor(d.Value, ColorProps.Contains(d.Prop));
                    if (off >= 0)
                        ctx.Add(Rule.DesignColor, d.ValueStart + off, "\"" + d.Prop + ": " + Short(d.Value) + "\": the hard-coded colour \"" + what + "\" doesn't follow the themes (light, dark, pro, application, hacker), app colours or dark mode: use " + SuggestColor(d.Prop, what));
                }
            });
            foreach (var kv in focus)
                if ((kv.Value.At > 0) && !kv.Value.Replaced)
                    ctx.Add(Rule.A11yFocus, kv.Value.At, "\"" + Short(kv.Key) + " { outline: none }\" removes the focus indicator without a replacement: keyboard users can't see where they are (WCAG 2.4.7); remove it (the Sw components and sw-root draw var(--sw-focus-ring)), or draw another indicator in the same rule (box-shadow: var(--sw-focus-ring))");
            foreach (var kv in pills)
                if (kv.Value.Frame && kv.Value.Flex)
                    ctx.Add(Rule.DesignPillFacts, kv.Value.At, "\"" + Short(kv.Key) + "\" looks like a pill / stat chip: facts (host, version, counts) belong in a facts strip (Sw.Facts({ Items: [{ Label, Value, Href }] }), dl.sw-facts) and one value of a total in a KPI tile (Sw.Kpi with a meter); pills only for status and filters (Sw.Badge, Sw.Chip)");
            if (!inline)
            {
                //  Selectors on framework structure that changed (child combinators that no longer match)
                var blanked = Blank(ctx.Text, start, end);
                foreach (var (re, msg) in StaleSelectors)
                    foreach (Match m in re.Matches(blanked))
                        ctx.Add(Rule.DesignLegacyClass, start + m.Index, "\"" + Regex.Replace(m.Value, @"\s+", " ") + "\" " + msg);
                //  Old element / class hooks in selectors (the map's Css entries)
                var edits = new List<TextEdit>();
                var items = new List<OffsetItem>();
                CssMigrator.Analyze(ctx.Text, start, end, map, edits, items);
                foreach (var it in items)
                    ctx.Add(Rule.DesignLegacyClass, it.Offset, it.Message.Replace("(report only, review)", "(only old code creates it)"));
                foreach (var e in edits)
                    ctx.Add(Rule.DesignLegacyClass, e.Start, "the old hook \"" + ctx.Text.Substring(e.Start, e.End - e.Start) + "\" is renamed to \"" + e.Text + "\" (SwWeb migrate does it)");
            }
        }

        /// <summary>
        /// Old tokens: alias uses (design-token-alias), alias overrides and legacy theme variable uses (design-legacy-token).
        /// </summary>
        static void CheckTokens(Checker.FileContext ctx, MigrationMap map, Decl d)
        {
            if (d.Prop.StartsWith("--", StringComparison.Ordinal) && map.Tokens.TryGetValue(d.Prop, out var def) && def.Safe)
                ctx.Add(Rule.DesignLegacyToken, d.PropStart, "\"" + d.Prop + ": " + Short(d.Value) + "\" overrides an old alias: the components read " + def.New + " since Design v2, so this no longer restyles them; set " + def.New + " (and use var(" + def.New + "))");
            foreach (Match m in VarRef.Matches(d.Value))
            {
                var n = m.Groups["n"].Value;
                var at = d.ValueStart + m.Groups["n"].Index;
                //  A legacy variable as the fallback of a role token (var(--sw-text-2, var(--ThemeMain))) is never used where sw/ui.css is loaded
                if (!n.StartsWith("--sw-", StringComparison.Ordinal) && IsSwFallback(d.Value, m.Index))
                    continue;
                if (map.Tokens.TryGetValue(n, out var te))
                {
                    if (te.Safe)
                        ctx.Add(Rule.DesignTokenAlias, at, "var(" + n + ") is the old alias of var(" + te.New + ") (SwWeb migrate renames it)" + (te.Note == null ? "" : ("; " + te.Note)));
                    else if (te.AppliesTo(d.Prop))
                        ctx.Add(Rule.DesignLegacyToken, at, "var(" + n + ") in \"" + d.Prop + "\": use var(" + te.NewFor(d.Prop) + ") (SwWeb migrate --level design renames it)");
                    else
                        ctx.Add(Rule.DesignLegacyToken, at, "var(" + n + ") in \"" + d.Prop + "\": use " + SuggestTheme(n, te, d.Prop));
                    continue;
                }
                var dm = ThemeDerived.Match(n);
                if (dm.Success)
                {
                    var b = BaseToken(dm.Groups["b"].Value);
                    var k = dm.Groups["k"].Value;
                    var pct = (k == "25") || (k == "50") || (k == "75") ? (k + "%") : "N%";
                    ctx.Add(Rule.DesignLegacyToken, at, "var(" + n + ") in \"" + d.Prop + "\": use a role token (--sw-hover, --sw-selected, --sw-border, --sw-surface-sunken ...) or color-mix(in srgb, var(" + b + ") " + pct + ", transparent)");
                }
            }
        }

        /// <summary>
        /// True if the var() at an index of a css value is (inside) the fallback of a <c>var(--sw-*)</c> role token.
        /// </summary>
        internal static bool IsSwFallback(String value, int index)
        {
            var open = new Stack<bool>();
            for (int i = 0; i < index; ++i)
            {
                var c = value[i];
                if (c == '(')
                {
                    bool swVar = (i >= 3) && (String.CompareOrdinal(value, i - 3, "var", 0, 3) == 0) && (String.CompareOrdinal(value, i + 1, "--sw-", 0, 5) == 0);
                    open.Push(swVar);
                }
                else if ((c == ')') && (open.Count > 0))
                    open.Pop();
            }
            return open.Contains(true);
        }

        static String BaseToken(String b) => b switch { "Background" => "--sw-bg", "Main" => "--sw-fg", "Acc1" => "--sw-accent1", _ => "--sw-accent2" };

        /// <summary>
        /// A property aware suggestion for a legacy theme variable.
        /// </summary>
        static String SuggestTheme(String n, TokenEntry te, String prop)
        {
            bool text = (prop == "color") || (prop == "fill") || (prop == "-webkit-text-fill-color") || (prop == "caret-color");
            bool back = prop.StartsWith("background", StringComparison.Ordinal);
            bool line = prop.StartsWith("border", StringComparison.Ordinal) || prop.StartsWith("outline", StringComparison.Ordinal) || (prop == "stroke");
            String s = null;
            switch (n)
            {
                case "--ThemeMain":
                    s = text ? "var(--sw-text) (secondary text var(--sw-text-2))" : line ? "var(--sw-border) or var(--sw-border-strong)" : back ? "a surface token (var(--sw-surface-strong), var(--sw-surface-inverse))" : null;
                    break;
                case "--ThemeBackground":
                    s = back ? "var(--sw-canvas) (page) or var(--sw-surface) (cards, tables, forms)" : text ? "var(--sw-on-accent) (text on an accent fill)" : null;
                    break;
                case "--ThemeAcc1":
                    s = text ? "var(--sw-text-accent), and only for meaning (links, the current item); headings and labels use var(--sw-text)" : back ? "var(--sw-accent1) for the one primary action, var(--sw-selected) for selected / current items" : null;
                    break;
            }
            //  The property aware text replaces the generic note of the map entry (not both)
            return s ?? ("var(" + te.New + ")" + (te.Note == null ? "" : (" (" + te.Note + ")")));
        }

        /// <summary>
        /// True for a box-shadow value that combines lengths (or other parts) with the complete shadow tokens <c>var(--sw-shadow)</c> /
        /// <c>var(--sw-elev-N)</c>, ex: <c>0 0 32px var(--sw-shadow)</c>: invalid, the browser ignores the declaration.
        /// </summary>
        /// <param name="value">The value (an optional !important is ignored)</param>
        /// <returns>True if invalid</returns>
        public static bool IsInvalidElevationShadow(String value)
        {
            var v = Regex.Replace(value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();
            foreach (var item in SplitTop(v))
                if (!ElevToken.IsMatch(item) && !SingleVar.IsMatch(item) && Regex.IsMatch(item, @"var\(\s*--sw-(?:shadow|elev-[0-2])\b"))
                    return true;
            return false;
        }

        static readonly Regex OwnPropertyDef = new Regex(@"(?<![\w-])(?<n>--(?!sw-|Theme)[A-Za-z_][\w-]*)\s*:\s*(?<v>[^;{}""'`]+)", RegexOptions.CultureInvariant);
        static readonly Regex OwnVar = new Regex(@"var\(\s*(?<n>--(?!sw-)[A-Za-z_][\w-]*)\s*(?:,[^()]*(?:\([^()]*\)[^()]*)*)?\)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Collect the definitions of own custom properties (<c>--my-shadow: 0 8px 24px ...</c>; not <c>--sw-*</c> or the legacy <c>--Theme*</c>) of a
        /// css / html / js text, for <see cref="CheckOptions.OwnProperties"/>.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="into">Receives name -> values</param>
        public static void CollectOwnProperties(String text, Dictionary<String, List<String>> into)
        {
            foreach (Match m in OwnPropertyDef.Matches(text))
            {
                var v = Regex.Replace(m.Groups["v"].Value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();
                if ((v.Length == 0) || (v.Length > 300))
                    continue;
                var n = m.Groups["n"].Value;
                if (!into.TryGetValue(n, out var l))
                    into[n] = l = new List<String>();
                if (!l.Contains(v) && (l.Count < 8))
                    l.Add(v);
            }
        }

        /// <summary>
        /// The declaration with each own custom property replaced by each of its definitions (two levels: a property that refers to another).
        /// </summary>
        static IEnumerable<Decl> Resolve(Decl d, Dictionary<String, List<String>> own, int level = 0)
        {
            foreach (Match m in OwnVar.Matches(d.Value))
            {
                var n = m.Groups["n"].Value;
                if (!own.TryGetValue(n, out var values))
                    continue;
                foreach (var v in values)
                {
                    var r = new Decl
                    {
                        Prop = d.Prop, PropStart = d.PropStart, ValueStart = d.ValueStart, Selector = d.Selector, InForcedColors = d.InForcedColors, InPrint = d.InPrint,
                        Indirect = d.Indirect || !SingleVar.IsMatch(d.Value.Trim()),
                        Value = d.Value.Substring(0, m.Index) + v + d.Value.Substring(m.Index + m.Length),
                        Note = d.Note + " (through var(" + n + "), defined as \"" + Short(v) + "\": an own custom property doesn't make a value a token)",
                    };
                    yield return r;
                    if (level < 1)
                        foreach (var rr in Resolve(r, own, level + 1))
                            yield return rr;
                }
            }
        }

        /// <summary>
        /// The radius, shadow and gradient rules for one declaration.
        /// </summary>
        /// <returns>True if a finding was added (shadow: a shadow finding)</returns>
        static bool JudgeShape(Checker.FileContext ctx, Decl d, out bool shadow)
        {
            bool any = false;
            if (RadiusProp.IsMatch(d.Prop) && !d.Indirect)
            {
                //  (a radius derived from a token, ex: calc(var(--sw-radius) - 2px) for a nested part, follows the scale)
                var rv = d.Value.Contains("var(--sw-radius", StringComparison.Ordinal) || ((d.Note.Length > 0) && d.Value.Contains("var(--sw-", StringComparison.Ordinal)) ? "" : Regex.Replace(d.Value, @"var\([^()]*(?:\([^()]*\)[^()]*)*\)", "");
                var rm = FixedLength.Match(rv);
                if (rm.Success)
                {
                    ctx.Add(Rule.DesignRadius, d.ValueStart, "\"" + d.Prop + ": " + Short(d.Value) + "\": use a radius token (var(--sw-radius-sm) 4, var(--sw-radius) 6 for controls, var(--sw-radius-md) 8 for cards / menus / tables, var(--sw-radius-lg) 12 for dialogs, var(--sw-radius-pill) for badges / chips); Pro and Hacker change the scale" + d.Note);
                    any = true;
                }
            }
            shadow = CheckShadow(ctx, d);
            any |= shadow;
            if (GradientProps.Contains(d.Prop))
            {
                var g = Gradient.Match(d.Value);
                if (g.Success && !IsPattern(d.Value))
                {
                    ctx.Add(Rule.DesignGradient, d.ValueStart + (d.Note.Length > 0 ? 0 : g.Index), "\"" + d.Prop + ": " + Short(d.Value) + "\": Design v2 surfaces are flat (no gradients on chrome, cards, bars or buttons): use a tonal token (--sw-surface, --sw-surface-sunken, --sw-selected) and a 1 px --sw-border; a pattern or a colour swatch that needs it gets a \"" + AllowDesign + "\" comment" + d.Note);
                    any = true;
                }
            }
            return any;
        }

        static readonly HashSet<String> SystemFonts = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "caption", "icon", "menu", "message-box", "small-caption", "status-bar" };

        /// <summary>
        /// The rule design-font: an own font-family (or a family in the font shorthand) instead of the theme fonts.
        /// </summary>
        static void CheckFont(Checker.FileContext ctx, Decl d)
        {
            if ((d.Prop != "font-family") && (d.Prop != "font"))
                return;
            var v = Regex.Replace(d.Value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (CssWideKeywords.Contains(v.ToLowerInvariant()) || SystemFonts.Contains(v))
                return;
            String family = null;
            if (d.Prop == "font-family")
            {
                if (!v.StartsWith("var(", StringComparison.Ordinal))
                    family = v;
            }
            else
            {
                //  "12px Verdana", "bold 1em/1.2 'Segoe UI', sans-serif" (a family after the size; var() parts are tokens)
                var nv = Regex.Replace(v, @"var\([^()]*(?:\([^()]*\)[^()]*)*\)", " ").Trim();
                var m = Regex.Match(nv, @"\d[\d.]*(?:px|em|rem|%|pt|vw|vh|ex|ch)?(?:\s*/\s*[\w.%]+)?\s+(?<f>[""']?[A-Za-z][^;]*)$", RegexOptions.CultureInvariant);
                if (m.Success)
                    family = m.Groups["f"].Value.Trim();
            }
            if (family == null)
                return;
            bool mono = Regex.IsMatch(family, @"mono", RegexOptions.IgnoreCase) || Regex.IsMatch(family, @"\b(?:Consolas|Courier|Menlo)\b", RegexOptions.IgnoreCase);
            ctx.Add(Rule.DesignFont, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\": an own font; the themes set the fonts (Barlow / Saira, Hacker a terminal font): " + (mono ? "use var(--sw-font-mono) for code" : "leave it out (sw-root gives var(--sw-sans)) or use var(--sw-sans) / var(--sw-font-mono)") + " (PORTING.md B7)" + d.Note);
        }

        static readonly Regex HeaderPart = new Regex(@"\.sw-ph(?:-title|-text|-titlerow|-desc)?(?![\w-])", RegexOptions.CultureInvariant);

        /// <summary>
        /// The page header rule for css that hides it: <c>.sw-ph { display: none }</c>, <c>.sw-ph h1 { visibility: hidden }</c> (not for print).
        /// </summary>
        static void CheckHiddenHeader(Checker.FileContext ctx, Decl d, int start, int end)
        {
            if (d.InPrint)
                return;
            bool hide = ((d.Prop == "display") && Regex.IsMatch(d.Value, @"^none\b", RegexOptions.IgnoreCase))
                || ((d.Prop == "visibility") && Regex.IsMatch(d.Value, @"^(?:hidden|collapse)\b", RegexOptions.IgnoreCase));
            if (!hide || (ctx.Text.IndexOf(NoPageHeader, StringComparison.Ordinal) >= 0))
                return;
            foreach (var part in d.Selector.Split(','))
            {
                var sel = part.Trim();
                var last = LastCompound(sel);
                bool header = HeaderPart.IsMatch(last) || (Regex.IsMatch(sel, @"\.sw-ph(?![\w-])[^,]*\s+h1\b") && Regex.IsMatch(last, @"^h1\b"));
                //  (a small screen layout may fold a part of the header away when the shell bar names the page; the header itself stays)
                if (!header || (d.InSizeMedia && !Regex.IsMatch(last, @"\.sw-ph$")))
                    continue;
                ctx.Add(Rule.DesignPageHeader, d.PropStart, "\"" + Short(sel) + " { " + d.Prop + ": " + Short(d.Value) + " }\" hides the page header (title, description, actions; the shell and screen readers rely on the h1): keep it; for an app-like page use Sw.PageLayout({ Compact: true, MenuTitle: false }), a page that deliberately has none (a dialog, an embed) builds none and carries a \"" + NoPageHeader + " (why)\" comment" + d.Note);
                return;
            }
        }

        /// <summary>
        /// The shadow rule (design-shadow).
        /// </summary>
        /// <returns>True if a finding was added</returns>
        static bool CheckShadow(Checker.FileContext ctx, Decl d)
        {
            String value;
            if (ShadowProps.Contains(d.Prop))
                value = d.Value;
            else if (FilterProps.Contains(d.Prop) && (d.Value.IndexOf("drop-shadow(", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ctx.Add(Rule.DesignShadow, d.ValueStart, "\"" + d.Prop + ": " + Short(d.Value) + "\": a drop shadow; Design v2 has no shadows on content (icons, charts, images): remove it" + d.Note);
                return true;
            }
            else
                return false;
            var v = Regex.Replace(value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (CssWideKeywords.Contains(v.ToLowerInvariant()))
                return false;
            if (d.Prop == "text-shadow")
            {
                ctx.Add(Rule.DesignShadow, d.ValueStart, "\"text-shadow: " + Short(v) + "\": no text glows or shadows in Design v2 (the hacker theme's glow is part of the theme): remove it" + d.Note);
                return true;
            }
            foreach (var item in SplitTop(v))
            {
                //  Fine: elevation tokens, focus rings, 0 0 0 Npx rings, inset lines without blur, a single custom property (a token:
                //  --sw-shadow is the alias of --sw-elev-1, an app's own token is judged where it is defined)
                if (item.Length == 0 || ElevToken.IsMatch(item) || SingleVar.IsMatch(item) || item.IndexOf("--sw-focus", StringComparison.Ordinal) >= 0 || item.IndexOf("--ThemeFocus", StringComparison.Ordinal) >= 0 || RingItem.IsMatch(item) || InsetLineItem.IsMatch(item) || Regex.IsMatch(item, @"(?:^|\s)transparent$"))
                    continue;
                if (Regex.IsMatch(item, @"var\(\s*--sw-(?:shadow|elev-[0-2])\b"))
                {
                    ctx.Add(Rule.DesignShadow, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\" is invalid since Design v2 (the browser ignores it): var(--sw-shadow) is now a complete shadow (= var(--sw-elev-1)), not a colour. A floating layer (menu, popover) uses \"box-shadow: var(--sw-elev-1)\" (dialogs, drawers, toasts var(--sw-elev-2)); a permanent surface (card, panel, header) uses none and a 1 px var(--sw-border)" + d.Note);
                    return true;
                }
                ctx.Add(Rule.DesignShadow, d.ValueStart, "\"" + d.Prop + ": " + Short(v) + "\": Design v2 surfaces are flat. A permanent surface (card, panel, header, bar, tile) uses none and a 1 px var(--sw-border); only a floating layer gets var(--sw-elev-1) (menus, popovers, tooltips) or var(--sw-elev-2) (dialogs, drawers, toasts); focus: var(--sw-focus-ring)" + d.Note);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Find the first hard-coded colour in a css value: <c>#hex</c>, a colour function with literal arguments (one with a var() is a
        /// legacy theme variable, reported by design-legacy-token), or a named colour (only in colour properties). var() / env() references
        /// (their fallbacks are not used when the token exists) and url() are skipped.
        /// </summary>
        /// <param name="v">The value</param>
        /// <param name="named">True to report named colours</param>
        /// <returns>The offset in the value and the colour text, or (-1, null)</returns>
        static (int, String) FindColor(String v, bool named)
        {
            int i = 0;
            while (i < v.Length)
            {
                var c = v[i];
                if ((c == '"') || (c == '\''))
                {
                    var e = v.IndexOf(c, i + 1);
                    i = e < 0 ? v.Length : e + 1;
                    continue;
                }
                if ((c == '#') && (i + 1 < v.Length) && Uri.IsHexDigit(v[i + 1]))
                {
                    int j = i + 1;
                    while ((j < v.Length) && Uri.IsHexDigit(v[j]))
                        ++j;
                    int len = j - i - 1;
                    bool identAfter = (j < v.Length) && (Char.IsLetterOrDigit(v[j]) || (v[j] == '-') || (v[j] == '_'));
                    if (!identAfter && ((len == 3) || (len == 4) || (len == 6) || (len == 8)))
                        return (i, v.Substring(i, j - i));
                    i = j;
                    continue;
                }
                if (Char.IsLetter(c) || (c == '-') || (c == '_'))
                {
                    int j = i;
                    while ((j < v.Length) && (Char.IsLetterOrDigit(v[j]) || (v[j] == '-') || (v[j] == '_')))
                        ++j;
                    var word = v.Substring(i, j - i);
                    bool prevIdent = (i > 0) && (Char.IsLetterOrDigit(v[i - 1]) || (v[i - 1] == '.') || (v[i - 1] == '#'));
                    if ((j < v.Length) && (v[j] == '('))
                    {
                        //  A function: find its end
                        int depth = 0, k = j;
                        for (; k < v.Length; ++k)
                        {
                            if (v[k] == '(')
                                ++depth;
                            else if ((v[k] == ')') && (--depth == 0))
                                break;
                        }
                        var inner = v.Substring(j + 1, Math.Max(0, Math.Min(k, v.Length) - j - 1));
                        var lw = word.ToLowerInvariant();
                        if ((lw == "var") || (lw == "env") || (lw == "url") || (lw == "attr"))
                        {
                            i = k + 1;
                            continue;
                        }
                        if (ColorFunctions.Contains(word))
                        {
                            if (inner.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0)
                                return (i, word + "(" + Short(inner) + ")");
                            i = k + 1;
                            continue;
                        }
                        //  Other functions (color-mix, calc, gradients): look inside
                        var (o, w) = FindColor(inner, true);
                        if (o >= 0)
                            return (j + 1 + o, w);
                        i = k + 1;
                        continue;
                    }
                    if (named && !prevIdent && NamedColors.Contains(word))
                        return (i, word);
                    i = j;
                    continue;
                }
                ++i;
            }
            return (-1, null);
        }

        /// <summary>
        /// A token suggestion for a hard-coded colour in a property.
        /// </summary>
        static String SuggestColor(String prop, String what)
        {
            var w = what.ToLowerInvariant().Replace(" ", "");
            bool overlay = w.StartsWith("rgba(0,0,0", StringComparison.Ordinal) || w.StartsWith("rgba(255,255,255", StringComparison.Ordinal) || w.StartsWith("rgb(00", StringComparison.Ordinal);
            if (overlay)
                return "var(--sw-hover) / var(--sw-pressed) (state overlays), var(--sw-scrim) (behind a dialog) or a surface token";
            if ((prop == "color") || (prop == "fill") || (prop == "-webkit-text-fill-color"))
                return "var(--sw-text), var(--sw-text-2), var(--sw-text-3) or a status colour (var(--sw-danger), var(--sw-ok), var(--sw-warning))";
            if (prop.StartsWith("background", StringComparison.Ordinal))
                return "a surface token (var(--sw-canvas), var(--sw-surface), var(--sw-surface-sunken), var(--sw-selected), var(--sw-danger-soft) ...)";
            if (prop.StartsWith("border", StringComparison.Ordinal) || prop.StartsWith("outline", StringComparison.Ordinal) || (prop == "stroke"))
                return "var(--sw-border), var(--sw-border-strong), var(--sw-border-input) or var(--sw-focus)";
            return "a token (ui/README.md \"Theming\"; your own token in a --custom-property, ex: --my-app-badge: #4FC3F7, defined per scheme)";
        }

        // ------------------------------------------------------------------ JavaScript

        static readonly Regex StyleShadow = new Regex(@"(?:box|text)-shadow\s*:\s*(?!\s*(?:none|var\(\s*--sw-(?:elev|focus))|\s*[;""'`])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex StyleColor = new Regex(@"(?<![\w-])(?:color|background(?:-color)?|border(?:-color)?)\s*:\s*(?:#[0-9a-fA-F]{3,8}\b|(?:rgba?|hsla?)\(\s*[\d.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly HashSet<String> StyleShadowProps = new HashSet<String>(StringComparer.Ordinal) { "boxShadow", "textShadow", "filter" };
        static readonly HashSet<String> StyleColorProps = new HashSet<String>(StringComparer.Ordinal) { "color", "background", "backgroundColor", "borderColor", "border", "outlineColor", "fill", "stroke" };

        /// <summary>Elements that are not controls (a click handler on them needs a role and a tabindex).</summary>
        static readonly HashSet<String> NonInteractive = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "span", "li", "td", "th", "tr", "img", "p", "i", "b", "em", "strong", "small", "section", "article", "header", "footer", "main",
            "nav", "aside", "ul", "ol", "dl", "dt", "dd", "svg", "h1", "h2", "h3", "h4", "h5", "h6", "figure", "table", "tbody", "pre", "code",
        };

        /// <summary>
        /// The design-own-ui rule in a script: native confirm() / alert() / prompt() (a name that the file also uses otherwise, ex: a
        /// parameter named confirm, is not the native one) and an own &lt;dialog&gt; (showModal).
        /// </summary>
        static void CheckOwnUi(Checker.FileContext ctx, List<JsToken> sig)
        {
            var text = ctx.Text;
            bool P(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Id(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && sig[i].Is(text, p);
            foreach (var name in NativeDialogs)
            {
                var calls = new List<int>();
                bool other = false;
                for (int i = 0; i < sig.Count; ++i)
                {
                    if (!Id(i, name))
                        continue;
                    bool member = P(i - 1, ".") || P(i - 1, "?.");
                    bool global = !member || Id(i - 2, "window") || Id(i - 2, "self") || Id(i - 2, "globalThis");
                    if (global && P(i + 1, "("))
                        calls.Add(i);
                    else if (!member)
                        other = true;
                }
                if (other)
                    continue;
                foreach (var i in calls)
                    ctx.Add(Rule.UxPop9, sig[i].Start, "native " + name + "(): " + NativeAdvice(name) + UxChecker.Cite(Rule.UxPop9));
            }
            //  Indirect: window["confirm"](...), globalThis['alert'], const ask = window.confirm (a reference, called later)
            bool G(int i) => Id(i, "window") || Id(i, "self") || Id(i, "globalThis") || Id(i, "top") || Id(i, "parent");
            for (int i = 0; i < sig.Count; ++i)
            {
                String name = null;
                if (G(i) && P(i + 1, "[") && (i + 3 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String) && P(i + 3, "]") && !P(i + 4, "="))
                    name = JsLexer.DecodeStringLiteral(sig[i + 2].GetText(text));
                else if (G(i) && P(i + 1, ".") && (i + 2 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.Identifier) && !P(i + 3, "(") && !P(i + 3, "=") && !P(i - 1, "."))
                {
                    //  A reference (not a call, not an assignment, not a typeof test)
                    var n = sig[i + 2].GetText(text);
                    if (NativeDialogs.Contains(n) && !Id(i - 1, "typeof") && !P(i + 3, "===") && !P(i + 3, "!==") && !P(i + 3, "&&") && !P(i + 3, "?"))
                        name = n;
                }
                if ((name != null) && NativeDialogs.Contains(name))
                    ctx.Add(Rule.UxPop9, sig[i].Start, "native " + name + " through \"" + text.Substring(sig[i].Start, sig[Math.Min(sig.Count - 1, i + 3)].End - sig[i].Start) + "\": " + NativeAdvice(name) + UxChecker.Cite(Rule.UxPop9));
                else if ((name != null) && ((name == "fetch") || (name == "XMLHttpRequest")))
                    ctx.Add(Rule.DesignOwnUi, sig[i].Start, "native " + name + " through \"" + text.Substring(sig[i].Start, sig[Math.Min(sig.Count - 1, i + 3)].End - sig[i].Start) + "\": " + NativeAdvice(name));
            }
            //  Own requests: fetch(...) (not a member of another object, not a local binding) and XMLHttpRequest
            {
                var calls = new List<int>();
                bool other = false;
                for (int i = 0; i < sig.Count; ++i)
                {
                    if (Id(i, "XMLHttpRequest") && !P(i - 1, ".") && Id(i - 1, "new"))
                        ctx.Add(Rule.DesignOwnUi, sig[i].Start, "own XMLHttpRequest: " + NativeAdvice("XMLHttpRequest"));
                    if (!Id(i, "fetch"))
                        continue;
                    bool member = P(i - 1, ".") || P(i - 1, "?.");
                    bool global = !member || Id(i - 2, "window") || Id(i - 2, "self") || Id(i - 2, "globalThis");
                    if (global && P(i + 1, "("))
                        calls.Add(i);
                    else if (!member && !P(i + 1, ":"))
                        other = true;
                }
                //  Not: a request that Sw.Net doesn't make (credentials, mode, redirect, an abort signal, an Authorization header: a login
                //  probe, a foreign api, a binary stream), an optional online feature (SwWeb:allow-online), a document that is no UI page
                if (!other && !ctx.NotUiPage)
                    foreach (var i in calls)
                    {
                        int close = MatchBrace(sig, text, i + 1);
                        var call = close > i ? text.Substring(sig[i].Start, sig[close].End - sig[i].Start) : "";
                        if (SpecialRequest.IsMatch(call) || ctx.HasMarker(sig[i].Start, Checker.AllowOnline))
                            continue;
                        //  fetch(r) of a Request made before with such options; a download of the bytes (blob / arrayBuffer)
                        if ((close == i + 3) && (sig[i + 2].Kind == JsTokenKind.Identifier))
                        {
                            var before = text.Substring(Math.Max(0, sig[i].Start - 600), Math.Min(600, sig[i].Start));
                            var rq = Regex.Match(before, @"\b" + Regex.Escape(sig[i + 2].GetText(text)) + @"\s*=\s*new\s+Request\s*\(", RegexOptions.RightToLeft);
                            if (rq.Success && SpecialRequest.IsMatch(before.Substring(rq.Index)))
                                continue;
                        }
                        var rest = text.Substring(sig[i].Start, Math.Min(400, text.Length - sig[i].Start));
                        if (Regex.IsMatch(rest, @"\.(?:blob|arrayBuffer|body\.getReader)\(\)"))
                            continue;
                        ctx.Add(Rule.DesignOwnUi, sig[i].Start, "own fetch(): " + NativeAdvice("fetch"));
                    }
            }
            foreach (var s in sig)
                if (((s.Kind == JsTokenKind.String) || s.IsTemplate) && OwnDarkScheme.IsMatch(s.GetText(text)))
                {
                    ctx.Add(Rule.DesignOwnUi, s.Start, "own light / dark handling (matchMedia(\"(prefers-color-scheme: dark)\")): the SysWeaver themes set the role tokens (the user's choice, not only the OS); for a value that must follow the theme read Sw.Theme (PORTING.md B16)");
                    break;
                }
            for (int i = 1; i < sig.Count; ++i)
                if (Id(i, "showModal") && P(i - 1, "."))
                    ctx.Add(Rule.UxPop1, sig[i].Start, "an own <dialog> (showModal): Sw.Dialog.Open / Show (focus trap, scrim over the shell, Escape, phone layout, the dialog rules) (PORTING.md A5)" + UxChecker.Cite(Rule.UxPop1));
        }

        static readonly Regex OwnPalette = new Regex(@"(?<![\w-])--(?!sw-)[\w-]+\s*:\s*(?:#[0-9a-fA-F]{3,8}\b|(?:rgba?|hsla?|oklch|lab|lch)\s*\()", RegexOptions.CultureInvariant);
        static readonly Regex OwnDarkScheme =new Regex(@"\(\s*prefers-color-scheme\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly String[] NativeDialogs = { "confirm", "alert", "prompt" };
        static readonly Regex SpecialRequest = new Regex(@"\b(?:credentials|mode|redirect|referrerPolicy|signal|integrity|keepalive)\s*:|[""']Authorization[""']", RegexOptions.CultureInvariant);

        static readonly Regex StyleRadius = new Regex(@"(?<![\w-])border(?:-[a-z]+)*-radius\s*:\s*(?!\s*var\()[^;""'`]*?(?<![\w.])(?!0+(?:px|rem|em)\b)\d*\.?\d+(?:px|rem|em)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex StyleFont = new Regex(@"(?<![\w-])font-family\s*:\s*(?!\s*(?:var\(|inherit\b|initial\b|unset\b))[""']?[A-Za-z]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// "boxShadow" -> "box-shadow".
        /// </summary>
        static String Kebab(String camel) => Regex.Replace(camel, @"[A-Z]", m => "-" + Char.ToLowerInvariant(m.Value[0]));

        /// <summary>
        /// Judge a style value set from code (style.x = , setProperty, Object.assign(style, ...)): shadows, gradients, hard-coded colours, fixed
        /// radii (design-inline-style) and fonts (design-font); an own custom property set from code with a shadow, gradient or colour value too.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="prop">The css property (kebab case)</param>
        /// <param name="val">The value (literal text)</param>
        /// <param name="at">The offset to report at</param>
        /// <param name="shown">The code as shown in the message</param>
        static void JudgeStyle(Checker.FileContext ctx, String prop, String val, int at, String shown)
        {
            if ((val.Length == 0) || CssWideKeywords.Contains(val.ToLowerInvariant()) || val.StartsWith("var(--sw-", StringComparison.Ordinal) && (val.IndexOf(' ') < 0))
                return;
            String what = null;
            bool custom = prop.StartsWith("--", StringComparison.Ordinal);
            if (((prop == "box-shadow") || (prop == "text-shadow")) && !val.StartsWith("var(--sw-elev", StringComparison.Ordinal) && !val.StartsWith("var(--sw-focus", StringComparison.Ordinal))
                what = "a shadow";
            else if (FilterProps.Contains(prop) && val.Contains("drop-shadow", StringComparison.OrdinalIgnoreCase))
                what = "a drop shadow";
            else if ((prop.StartsWith("background", StringComparison.Ordinal) || custom) && Gradient.IsMatch(val) && !IsPattern(val))
                what = "a gradient";
            else if (custom && Regex.IsMatch(val, @"^(?:inset\s+)?-?[\d.]+(?:px|rem|em)?\s+-?[\d.]+(?:px|rem|em)?\s+[\d.]+(?:px|rem|em)", RegexOptions.IgnoreCase) && (FindColor(val, true).Item1 >= 0))
                what = "a shadow";
            else if (RadiusProp.IsMatch(prop) && !val.Contains("var(--sw-radius", StringComparison.Ordinal) && FixedLength.IsMatch(Regex.Replace(val, @"var\([^()]*(?:\([^()]*\)[^()]*)*\)", "")))
                what = "a fixed radius (use the radius tokens; Pro and Hacker change the scale)";
            else if ((ColorProps.Contains(prop) || custom) && (FindColor(val, true).Item1 >= 0))
                what = "a hard-coded colour";
            if (what != null)
            {
                ctx.Add(Rule.DesignInlineStyle, at, "\"" + shown + "\": " + what + " set from code" + (custom ? " through an own custom property" : "") + "; put it in the page css on tokens (or use a Sw component)");
                return;
            }
            if (((prop == "font-family") && !val.StartsWith("var(", StringComparison.Ordinal)) || ((prop == "font") && StyleFont.IsMatch("font-family: " + Regex.Replace(val, @"^[^A-Za-z""']*", ""))))
                ctx.Add(Rule.DesignFont, at, "\"" + shown + "\": an own font set from code; the themes set the fonts (var(--sw-sans), var(--sw-font-mono)) (PORTING.md B7)");
        }

        static readonly Regex ShowText = new Regex(@"\b(?<k>Text|Content|Title)\s*:\s*(?:_TF?H?\(\s*)?(?<q>[""'])(?<v>(?:(?!\k<q>).)*)\k<q>", RegexOptions.CultureInvariant);

        /// <summary>
        /// The dialog wording rules (design-confirm) for <c>Sw.Dialog.Show({ ... })</c>: "Are you sure?" in the content, "Yes ..." / "No ..." button texts.
        /// </summary>
        static void CheckDialogShow(Checker.FileContext ctx, List<JsToken> sig, int open)
        {
            var text = ctx.Text;
            int close = MatchBrace(sig, text, open);
            if (close < 0)
                return;
            var body = text.Substring(sig[open].Start, sig[close].End - sig[open].Start);
            var what = new List<String>();
            foreach (Match m in ShowText.Matches(body))
            {
                var v = m.Groups["v"].Value;
                if ((m.Groups["k"].Value == "Text") && DialogWording.IsYesNo(v))
                    what.Add("the button text \"" + v + "\" is not a verb: \"" + (DialogWording.OkLabel(v) ?? v.Replace("Yes, ", "").Replace("No, ", "").TrimEnd('!')) + "\" (the way out is \"Cancel\")");
                else if ((m.Groups["k"].Value != "Title") && DialogWording.HasAreYouSure(v))
                    what.Add("drop \"Are you sure?\" from the text (the title asks the question)");
            }
            if (what.Count > 0)
                ctx.Add(Rule.DesignConfirm, sig[open].Start, "Sw.Dialog.Show: " + String.Join("; ", what.Distinct()) + " (ui/README.md Dialog rules; a confirmation is Sw.Dialog.Confirm with Danger: true for a destructive action)");
        }

        /// <summary>
        /// The replacement of a native dialog or request function.
        /// </summary>
        static String NativeAdvice(String name) => name switch
        {
            "confirm" => "Sw.Dialog.Confirm (Danger: true for a destructive action) or Sw.Cells.ConfirmDanger: it blocks the page, ignores the theme and the dialog rules (PORTING.md A5)",
            "alert" => "Sw.Toast / Sw.Dialog.Alert: it blocks the page, ignores the theme and the dialog rules (PORTING.md A5)",
            "prompt" => "Sw.Dialog.Prompt: it blocks the page, ignores the theme and the dialog rules (PORTING.md A5)",
            _ => "call the server with Sw.Net.Send(url, arg) / Sw.Net.Get (the session, retries, the server's errors in plain words through Sw.Fail, the busy state) (PORTING.md A0, A3); a request Sw.Net can't make (a binary stream, a foreign api) gets a \"" + AllowDesign + " (why)\" comment",
        };

        /// <summary>
        /// Run the JavaScript design rules: inline styles with shadows, gradients or hard-coded colours (design-inline-style) and clickable
        /// non-control elements made with <c>Sw.el</c> (a11y-custom-control).
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="tokens">The tokens (of the file or an inline script)</param>
        public static void CheckJs(Checker.FileContext ctx, List<JsToken> tokens)
        {
            if (IsFramework(ctx.WebRel))
                return;
            var text = ctx.Text;
            var map = MapOf(ctx.Options);
            var sig = tokens.Where(t => t.IsSignificant).ToList();
            CheckOwnUi(ctx, sig);
            bool Punct(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Punctuator) && sig[i].Is(text, p);
            bool Ident(int i, String p) => (i >= 0) && (i < sig.Count) && (sig[i].Kind == JsTokenKind.Identifier) && ((p == null) || sig[i].Is(text, p));
            for (int i = 0; i < sig.Count; ++i)
            {
                var t = sig[i];
                if ((t.Kind == JsTokenKind.String) || t.IsTemplate)
                {
                    var s = t.GetText(text);
                    //  The value of x.style.prop = "..." is judged below (with its property)
                    bool styleValue = Punct(i - 1, "=") && Ident(i - 2, null) && !Ident(i - 2, "cssText") && Punct(i - 3, ".") && Ident(i - 4, "style");
                    var m = StyleShadow.Match(s);
                    if (styleValue)
                    {
                    }
                    else if (m.Success)
                        ctx.Add(Rule.DesignInlineStyle, t.Start + m.Index, "an inline \"" + Short(s.Trim('"', '\'', '`')) + "\": Design v2 surfaces are flat; put the style in the page css on tokens (var(--sw-elev-1) only for floating layers)");
                    else if ((m = Gradient.Match(s)).Success && !Regex.IsMatch(s, @"repeating-", RegexOptions.IgnoreCase))
                        ctx.Add(Rule.DesignInlineStyle, t.Start + m.Index, "an inline gradient \"" + Short(s.Trim('"', '\'', '`')) + "\": Design v2 uses flat tonal surfaces (page css on tokens)");
                    else if ((m = StyleColor.Match(s)).Success)
                        ctx.Add(Rule.DesignInlineStyle, t.Start + m.Index, "an inline hard-coded colour \"" + Short(s.Trim('"', '\'', '`')) + "\": use a token (var(--sw-text), var(--sw-surface), var(--sw-border) ...) in the page css");
                    else if ((m = StyleRadius.Match(s)).Success)
                        ctx.Add(Rule.DesignInlineStyle, t.Start + m.Index, "an inline radius \"" + Short(s.Trim('"', '\'', '`')) + "\": use a radius token (var(--sw-radius), var(--sw-radius-md) ...) in the page css; Pro and Hacker change the scale");
                    if (!styleValue && (m = StyleFont.Match(s)).Success)
                        ctx.Add(Rule.DesignFont, t.Start + m.Index, "an inline font \"" + Short(s.Trim('"', '\'', '`')) + "\": the themes set the fonts; leave it out or use var(--sw-sans) / var(--sw-font-mono) (PORTING.md B7)");
                    foreach (Match v in VarRef.Matches(s))
                        if (map.Tokens.TryGetValue(v.Groups["n"].Value, out var te) && te.Safe)
                            ctx.Add(Rule.DesignTokenAlias, t.Start + v.Groups["n"].Index, "var(" + te.Old + ") is the old alias of var(" + te.New + ") (SwWeb migrate renames it)");
                    continue;
                }
                //  x.style.boxShadow = "...", x.style.color = "#..."
                if (Ident(i, "style") && Punct(i - 1, ".") && Punct(i + 1, ".") && Ident(i + 2, null) && Punct(i + 3, "=") && (i + 4 < sig.Count) && ((sig[i + 4].Kind == JsTokenKind.String) || sig[i + 4].IsTemplate))
                {
                    var prop = sig[i + 2].GetText(text);
                    var val = sig[i + 4].GetText(text).Trim('"', '\'', '`').Trim();
                    JudgeStyle(ctx, Kebab(prop), val, sig[i + 2].Start, "style." + prop + " = " + Short(sig[i + 4].GetText(text)));
                }
                //  x.style.setProperty("box-shadow", "..."), x.style.setProperty("--my-shadow", "...")
                if (Ident(i, "setProperty") && Punct(i - 1, ".") && Punct(i + 1, "(") && (i + 4 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String) && Punct(i + 3, ",")
                    && ((sig[i + 4].Kind == JsTokenKind.String) || (sig[i + 4].Kind == JsTokenKind.Template)))
                {
                    var prop = JsLexer.DecodeStringLiteral(sig[i + 2].GetText(text)) ?? "";
                    var val = sig[i + 4].GetText(text).Trim('"', '\'', '`').Trim();
                    JudgeStyle(ctx, prop, val, sig[i + 2].Start, "setProperty(" + sig[i + 2].GetText(text) + ", " + Short(sig[i + 4].GetText(text)) + ")");
                }
                //  Object.assign(x.style, { boxShadow: "...", borderRadius: "14px" })
                if (Ident(i, "assign") && Punct(i - 1, ".") && Ident(i - 2, "Object") && Punct(i + 1, "("))
                {
                    int j = i + 2, comma = -1, depth = 0;
                    for (; j < sig.Count; ++j)
                    {
                        if (Punct(j, "(") || Punct(j, "[") || Punct(j, "{"))
                            ++depth;
                        else if (Punct(j, ")") || Punct(j, "]") || Punct(j, "}"))
                        {
                            if (depth-- == 0)
                                break;
                        }
                        else if ((depth == 0) && Punct(j, ","))
                        {
                            comma = j;
                            break;
                        }
                    }
                    if ((comma > 0) && Ident(comma - 1, "style") && Punct(comma + 1, "{"))
                    {
                        int close = MatchBrace(sig, text, comma + 1);
                        for (int k = comma + 2; (k + 2 <= close); ++k)
                            if ((Punct(k - 1, "{") || Punct(k - 1, ",")) && ((sig[k].Kind == JsTokenKind.Identifier) || (sig[k].Kind == JsTokenKind.String)) && Punct(k + 1, ":")
                                && ((sig[k + 2].Kind == JsTokenKind.String) || (sig[k + 2].Kind == JsTokenKind.Template)))
                            {
                                var key = sig[k].Kind == JsTokenKind.String ? (JsLexer.DecodeStringLiteral(sig[k].GetText(text)) ?? "") : sig[k].GetText(text);
                                var val = sig[k + 2].GetText(text).Trim('"', '\'', '`').Trim();
                                JudgeStyle(ctx, key.StartsWith("--", StringComparison.Ordinal) ? key : Kebab(key), val, sig[k].Start, "Object.assign(style, { " + key + ": " + Short(sig[k + 2].GetText(text)) + " })");
                            }
                    }
                }
                //  Sw.Page.Navigate(url) with one argument for another site or a file: the same tab now (design-navigate)
                if (Ident(i, "Navigate") && Punct(i - 1, ".") && Ident(i - 2, "Page") && Punct(i + 1, "("))
                {
                    int close = MatchBrace(sig, text, i + 1), commas = 0, depth = 0;
                    for (int j = i + 2; (close > 0) && (j < close); ++j)
                    {
                        if (Punct(j, "(") || Punct(j, "[") || Punct(j, "{"))
                            ++depth;
                        else if (Punct(j, ")") || Punct(j, "]") || Punct(j, "}"))
                            --depth;
                        else if ((depth == 0) && Punct(j, ","))
                            ++commas;
                    }
                    if ((close > i + 2) && (commas == 0))
                    {
                        var arg = text.Substring(sig[i + 2].Start, sig[close - 1].End - sig[i + 2].Start);
                        if (Regex.IsMatch(arg, @"[""'`](?:https?:)?//|\.(?:xlsx?|csv|json|pdf|zip|docx?|png|jpe?g|txt|log)(?:[?#""'`]|$)", RegexOptions.IgnoreCase))
                            ctx.Add(Rule.DesignNavigate, sig[i].Start, "Sw.Page.Navigate(" + Short(arg) + ") goes there in the same tab (the default target is \"_self\", the old Open opened a new tab): for another site or a file pass \"_blank\": Sw.Page.Navigate(url, \"_blank\") (core/README.md Page)");
                    }
                }
                //  Sw.Dialog.Show({ Title, Content: "Are you sure?", Buttons: [{ Text: "Yes, Delete!" }] }): the dialog rules as for Confirm
                if (Ident(i, "Show") && Punct(i - 1, ".") && Ident(i - 2, "Dialog") && Punct(i + 1, "(") && Punct(i + 2, "{"))
                    CheckDialogShow(ctx, sig, i + 2);
                //  document.querySelector(".sw-ph").remove() / .hidden = true / .style.display = "none": a hidden page header
                if (((t.Kind == JsTokenKind.Identifier) && (t.Is(text, "querySelector") || t.Is(text, "querySelectorAll"))) && Punct(i + 1, "(") && (i + 2 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String)
                    && HeaderPart.IsMatch(sig[i + 2].GetText(text)) && (text.IndexOf(NoPageHeader, StringComparison.Ordinal) < 0))
                {
                    var after = text.Substring(sig[i + 2].End, Math.Min(120, text.Length - sig[i + 2].End));
                    if (Regex.IsMatch(after, @"^[^;\n]*?(?:\.remove\(\)|\.hidden\s*=\s*true|\.style\.display\s*=\s*[""']none|\.style\.visibility\s*=\s*[""']hidden)"))
                        ctx.Add(Rule.DesignPageHeader, sig[i + 2].Start, "code hides the page header (" + Short(text.Substring(sig[i].Start, Math.Min(80, text.Length - sig[i].Start)).Split('\n')[0].Trim()) + "): keep it (the shell and screen readers rely on the h1); for an app-like page use Sw.PageLayout({ Compact: true, MenuTitle: false }), a page that deliberately has none builds none and carries a \"" + NoPageHeader + " (why)\" comment");
                }
                //  Sw.Dialog.Confirm("Delete", text, "Yes, Delete!", { CancelText: "No, Keep it!" }): Danger and verb labels (Dialog rules)
                if (Ident(i, "Confirm") && Punct(i - 1, ".") && Ident(i - 2, "Dialog") && Punct(i + 1, "("))
                    CheckConfirm(ctx, sig, i);
                //  await ed.Validate(true) ... Sw.Fail(err[1]) in the same block: the editor already showed the error (under the field)
                if (Ident(i, "Validate") && Punct(i - 1, ".") && Punct(i + 1, "(") && (i + 3 < sig.Count) && sig[i + 2].Is(text, "true") && Punct(i + 3, ")"))
                {
                    int depth = 0;
                    for (int j = i + 4; (j < sig.Count) && (j < i + 60); ++j)
                    {
                        if (Punct(j, "{"))
                            ++depth;
                        else if (Punct(j, "}") && (--depth < 0))
                            break;
                        if ((Ident(j, "Fail") || Ident(j, "Toast")) && Punct(j - 1, ".") && Ident(j - 2, "Sw") && Punct(j + 1, "(")
                            && (MatchBrace(sig, text, j + 1) is int argEnd) && (argEnd > j + 1)
                            && Regex.IsMatch(text.Substring(sig[j + 1].Start, sig[argEnd].End - sig[j + 1].Start), @"\[\s*1\s*\]"))
                        {
                            ctx.Add(Rule.DesignErrorTwice, sig[j].Start, "Sw." + sig[j].GetText(text) + "(...) after Validate(true): the editor shows the error itself (under the field, a toast only when the field can't be seen); remove this toast");
                            break;
                        }
                    }
                }
                //  el("fieldset"), document.createElement("fieldset")
                if ((Ident(i, "el") || Ident(i, "createElement")) && Punct(i + 1, "(") && (i + 2 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String)
                    && Regex.IsMatch(sig[i + 2].GetText(text), @"^['""]fieldset(?:[.#'""])", RegexOptions.IgnoreCase))
                    ctx.Add(Rule.DesignFieldset, sig[i + 2].Start, "a fieldset as a group frame: use a card (Sw.Card, Sw.Group, section.sw-card) or a flat section title (Sw.SectionTitle)");
                //  Sw.Kpi({ Meter: true / { Max } }) and Sw.Meter({ ... }) without Warn / Danger / Levels (the old default levels are gone)
                {
                    int open = -1;
                    if (Ident(i, "Meter") && Punct(i + 1, ":") && (i + 2 < sig.Count) && sig[i + 2].Is(text, "true"))
                        ctx.Add(Rule.DesignMeterLevels, t.Start, "\"Meter: true\" is a plain bar (no warn / danger colours without thresholds): give { Max, Warn, Danger } for a use (cpu: Warn 0.8, Danger 0.92), Good: \"high\" when more is better, or { Levels: false } for a plain bar");
                    else if (Ident(i, "Meter") && Punct(i + 1, ":") && Punct(i + 2, "{"))
                        open = i + 2;
                    else if (Ident(i, "Meter") && Punct(i - 1, ".") && Ident(i - 2, "Sw") && Punct(i + 1, "(") && Punct(i + 2, "{"))
                        open = i + 2;
                    if (open > 0)
                    {
                        int close = MatchBrace(sig, text, open);
                        if (close > open)
                        {
                            var props = text.Substring(sig[open].Start, sig[close].End - sig[open].Start);
                            if (!Regex.IsMatch(props, @"\b(?:Warn|Danger|Levels)\s*:"))
                                ctx.Add(Rule.DesignMeterLevels, t.Start, "a meter without Warn / Danger is a plain bar (the old default levels 0.8 / 0.92 are gone): give Warn / Danger for a use (cpu: Warn 0.8, Danger 0.92), Good: \"high\" when more is better (on time %), or Levels: false for a plain bar");
                        }
                    }
                }
                //  Sw.el("div.x", { on: { click: ... } }) without role / tabindex
                if (Ident(i, "el") && Punct(i + 1, "(") && (i + 2 < sig.Count) && (sig[i + 2].Kind == JsTokenKind.String) && Punct(i + 3, ",") && Punct(i + 4, "{"))
                {
                    var spec = sig[i + 2].GetText(text).Trim('"', '\'');
                    var tag = Regex.Match(spec, @"^[A-Za-z][A-Za-z0-9]*").Value;
                    if (!NonInteractive.Contains(tag))
                        continue;
                    int close = MatchBrace(sig, text, i + 4);
                    if (close < 0)
                        continue;
                    var props = text.Substring(sig[i + 4].Start, sig[close].End - sig[i + 4].Start);
                    bool click = Regex.IsMatch(props, @"\bon\s*:\s*\{[^}]*\b(?:click|pointerup|pointerdown|mousedown|mouseup)\s*[:(]", RegexOptions.Singleline) || Regex.IsMatch(props, @"\bonclick\s*:");
                    if (click && !Regex.IsMatch(props, @"\brole\b") && !Regex.IsMatch(props, @"\btab[iI]ndex\b"))
                        ctx.Add(Rule.A11yCustomControl, sig[i + 2].Start, "el(\"" + spec + "\", { on: { click } }) is clickable but not a control: keyboard and screen reader users can't use it; use a button (el(\"button.x\"), Sw.Button) or an <a href>, or add attrs: { role: \"button\", tabindex: \"0\" } plus Sw.KeyClick(element)");
                }
            }
        }

        static readonly Regex TargetOption = new Regex(@"\bTarget\s*[:,}]", RegexOptions.CultureInvariant);
        static readonly Regex DangerOption = new Regex(@"\bDanger\s*[:,}]", RegexOptions.CultureInvariant);
        static readonly Regex NoCancelText = new Regex(@"\bCancelText\s*:\s*(?:_TF?H?\(\s*)?(?<q>[""'])(?<v>No\b[^""']*)\k<q>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The design-confirm rule for a <c>Sw.Dialog.Confirm(title, text, okText, options)</c> call (sig[at] is "Confirm").
        /// </summary>
        static void CheckConfirm(Checker.FileContext ctx, List<JsToken> sig, int at)
        {
            var text = ctx.Text;
            int open = at + 1;
            int close = MatchBrace(sig, text, open);
            if (close < 0)
                return;
            var args = new List<(int Start, int End)>();
            int s = open + 1;
            for (int j = open + 1; j < close; ++j)
            {
                var t = sig[j];
                if (t.Kind == JsTokenKind.Punctuator && (t.Is(text, "(") || t.Is(text, "[") || t.Is(text, "{")))
                {
                    int m = MatchBrace(sig, text, j);
                    if (m < 0)
                        return;
                    j = m;
                    continue;
                }
                if (t.Kind == JsTokenKind.Punctuator && t.Is(text, ","))
                {
                    args.Add((s, j - 1));
                    s = j + 1;
                }
            }
            if (s < close)
                args.Add((s, close - 1));
            DialogWording.Literal title = default, ok = default;
            bool hasTitle = (args.Count > 0) && DialogWording.TryLiteral(text, sig, args[0].Start, args[0].End, out title);
            bool hasOk = (args.Count > 2) && DialogWording.TryLiteral(text, sig, args[2].Start, args[2].End, out ok);
            var opt = args.Count > 3 ? text.Substring(sig[args[3].Start].Start, sig[args[3].End].End - sig[args[3].Start].Start) : "";
            bool danger = (hasTitle && DialogWording.IsDestructive(title.Value)) || (hasOk && DialogWording.IsDestructive(ok.Value));
            var what = new List<String>();
            if (danger && !DangerOption.IsMatch(opt))
                what.Add("a destructive action (\"" + (hasTitle && DialogWording.IsDestructive(title.Value) ? title.Value : ok.Value) + "\") without Danger: true: add it to the options (danger button, Cancel focused, Enter never confirms) or use Sw.Cells.ConfirmDanger");
            if (hasOk && DialogWording.IsYesNo(ok.Value))
                what.Add("the OK text \"" + ok.Value + "\" is not a verb: \"" + (DialogWording.OkLabel(ok.Value) ?? "Delete") + "\"");
            var nc = NoCancelText.Match(opt);
            if (nc.Success)
                what.Add("the cancel text \"" + nc.Groups["v"].Value + "\": use \"Cancel\" (or leave CancelText out)");
            //  The wording that migrate reports for a person (so the worksheet and the gate agree)
            if (danger && hasTitle && !title.Value.TrimEnd().EndsWith("?", StringComparison.Ordinal) && !TargetOption.IsMatch(opt))
                what.Add("the title \"" + title.Value + "\" of a destructive action should ask a question that names the item (\"Delete the version?\"), or pass Target (the title is then made from the OK text and the item)");
            if ((args.Count > 1) && DialogWording.HasAreYouSure(text.Substring(sig[args[1].Start].Start, sig[args[1].End].End - sig[args[1].Start].Start)))
                what.Add("drop \"Are you sure?\" from the text (the title asks the question)");
            if (what.Count > 0)
                ctx.Add(Rule.DesignConfirm, sig[at].Start, "Sw.Dialog.Confirm: " + String.Join("; ", what) + " (ui/README.md Dialog rules)");
        }

        /// <summary>
        /// The index of the matching closing brace / bracket / paren of the significant token at <paramref name="open"/>, -1 if none.
        /// </summary>
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

        // ------------------------------------------------------------------ HTML

        /// <summary>
        /// Run the HTML design rules: old format pages (design-legacy-page), style attributes (the css rules) and accessible custom controls
        /// (a11y-custom-control, a11y-name). Style elements and inline scripts are checked by <see cref="Checker.CheckHtmlFile"/>.
        /// </summary>
        /// <param name="ctx">The file</param>
        /// <param name="nodes">The scanned nodes</param>
        public static void CheckHtml(Checker.FileContext ctx, List<HtmlNode> nodes, bool uiPage = true)
        {
            if (IsFramework(ctx.WebRel))
                return;
            bool legacyReported = false;
            for (int i = 0; i < nodes.Count; ++i)
            {
                var n = nodes[i];
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (!legacyReported && (n.Name == "script"))
                {
                    var src = n.GetAttribute("src");
                    if ((src?.Value != null) && (Checker.IsSrc(src.Value, "common/common.js") || Checker.IsSrc(src.Value, "app/application.js")))
                    {
                        legacyReported = true;
                        ctx.Add(Rule.DesignLegacyPage, src.ValueStart, "an old format page (\"" + src.Value + "\"): run \"SwWeb migrate <folder>\" (includes, legacy globals, tokens), then port it to the Design v2 page anatomy (PORTING.md)");
                    }
                }
                if (uiPage && (n.Name == "dialog"))
                    ctx.Add(Rule.UxPop1, n.Start, "an own <dialog>: Sw.Dialog.Open / Show (focus trap, scrim over the shell, Escape, phone layout, the dialog rules) (PORTING.md A5)" + UxChecker.Cite(Rule.UxPop1));
                var style = n.GetAttribute("style");
                //  <header class="sw-ph" hidden> / style="display: none": a hidden page header
                var hcls = n.GetAttribute("class")?.Value;
                if (uiPage && (hcls != null) && HeaderPart.IsMatch("." + String.Join(" .", hcls.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                    && ((n.GetAttribute("hidden") != null) || ((style?.Value != null) && Regex.IsMatch(style.Value, @"(?:display\s*:\s*none|visibility\s*:\s*hidden)", RegexOptions.IgnoreCase)))
                    && (ctx.Text.IndexOf(NoPageHeader, StringComparison.Ordinal) < 0))
                    ctx.Add(Rule.DesignPageHeader, n.Start, "a hidden page header (<" + n.Name + " class=\"" + hcls + "\"" + (n.GetAttribute("hidden") != null ? " hidden" : " style=\"display: none\"") + ">): keep it visible (the shell and screen readers rely on the h1); a page that deliberately has none builds none and carries a \"" + NoPageHeader + " (why)\" comment");
                if (uiPage && (style?.Value != null) && (style.Value.Length > 0))
                    CheckCss(ctx, style.ValueStart, style.ValueStart + style.Value.Length, true);
                var cls = n.GetAttribute("class");
                if (cls?.Value != null)
                    foreach (Match m in ThemeUtilityClass.Matches(cls.Value))
                        ctx.Add(Rule.DesignLegacyClass, cls.ValueStart + m.Index, "the old theme colour class \"" + m.Value + "\""
                            + (n.Name == "body" ? ": remove it, sw/ui.css gives the body var(--sw-canvas) and var(--sw-text)" : ": use a class of your page whose css uses the role tokens (var(--sw-text), var(--sw-surface), var(--sw-text-accent) ...)")
                            + (m.Value == "ThemeBaseBg" ? " (ThemeBaseBg doesn't exist any more, it does nothing)" : ""));
                CheckControl(ctx, nodes, i);
            }
            if (NeedsViewport(nodes, out var at))
                ctx.Add(Rule.DesignViewport, at?.Start ?? 0, "a UI page without <meta name=\"viewport\">: phones render it 980 px wide and zoomed out (the Design v2 phone layouts never apply): add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"> (SwWeb migrate --level design does it)");
            if (BlocksZoom(nodes, out var vc, out var fixedVc))
                ctx.Add(Rule.UxInp25, vc.ValueStart, "the viewport \"" + vc.Value + "\" blocks pinch zoom (user-scalable=no / maximum-scale=1): people with low vision can't enlarge the page (WCAG 1.4.4), and the Design v2 layouts don't need it: use \"" + fixedVc + "\" (SwWeb migrate --level design does it)" + UxChecker.Cite(Rule.UxInp25));
        }

        /// <summary>
        /// True if the viewport meta tag of a page blocks pinch zoom (<c>user-scalable=no</c> / <c>0</c>, <c>maximum-scale=1</c>).
        /// </summary>
        /// <param name="nodes">The scanned nodes</param>
        /// <param name="content">The content attribute</param>
        /// <param name="fixedValue">The content without the zoom blocking parts</param>
        /// <returns>True if it blocks zoom</returns>
        public static bool BlocksZoom(List<HtmlNode> nodes, out HtmlAttribute content, out String fixedValue)
        {
            content = null;
            fixedValue = null;
            foreach (var n in nodes)
            {
                if ((n.Kind != HtmlNodeKind.StartTag) || (n.Name != "meta") || !String.Equals(n.GetAttribute("name")?.Value?.Trim(), "viewport", StringComparison.OrdinalIgnoreCase))
                    continue;
                var c = n.GetAttribute("content");
                if ((c?.Value == null) || (c.ValueStart < 0))
                    return false;
                var parts = c.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var keep = parts.Where(p => !Regex.IsMatch(p, @"^(?:user-scalable\s*=\s*(?:no|0|false)|maximum-scale\s*=\s*(?:1(?:\.0*)?|0?\.\d+))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();
                if (keep.Count == parts.Count)
                    return false;
                content = c;
                fixedValue = String.Join(", ", keep);
                return true;
            }
            return false;
        }

        /// <summary>The old theme colour utility classes of common/theme.css (ThemeMainFg ...) and the removed ThemeBaseBg.</summary>
        static readonly Regex ThemeUtilityClass = new Regex(@"(?<![\w-])Theme(?:Base|Main|Background|Acc|Acc2)(?:Bg|Fg)(?![\w-])", RegexOptions.CultureInvariant);

        /// <summary>The viewport meta tag that SwWeb migrate --level design adds.</summary>
        public const String ViewportMeta = "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">";

        /// <summary>
        /// True if a page is a SysWeaver UI page (it loads sw/core.js or common/common.js and has a body) without a viewport meta tag.
        /// </summary>
        /// <param name="nodes">The scanned nodes</param>
        /// <param name="anchor">Where the tag goes: after the charset meta tag, else after the head start tag (null if the page has no head)</param>
        /// <returns>True if the tag is missing</returns>
        public static bool NeedsViewport(List<HtmlNode> nodes, out HtmlNode anchor)
        {
            anchor = null;
            bool ui = false, body = false;
            HtmlNode head = null, charset = null;
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                switch (n.Name)
                {
                    case "meta":
                        if (String.Equals(n.GetAttribute("name")?.Value?.Trim(), "viewport", StringComparison.OrdinalIgnoreCase))
                            return false;
                        if ((charset == null) && (n.GetAttribute("charset") != null))
                            charset = n;
                        break;
                    case "head":
                        head ??= n;
                        break;
                    case "body":
                        body = true;
                        break;
                    case "script":
                        var src = n.GetAttribute("src")?.Value;
                        if ((src != null) && (Checker.IsSrc(src, "sw/core.js") || Checker.IsSrc(src, "common/common.js")))
                            ui = true;
                        break;
                }
            }
            if (!ui || !body)
                return false;
            anchor = charset ?? head;
            return true;
        }

        static bool Has(HtmlNode n, String a) => n.GetAttribute(a) != null;

        static bool HasValue(HtmlNode n, String a)
        {
            var v = n.GetAttribute(a);
            return (v != null) && !String.IsNullOrWhiteSpace(v.Value);
        }

        /// <summary>
        /// The accessibility rules for one start tag.
        /// </summary>
        static void CheckControl(Checker.FileContext ctx, List<HtmlNode> nodes, int index)
        {
            var n = nodes[index];
            var name = n.Name;
            if (name == "fieldset")
                ctx.Add(Rule.DesignFieldset, n.Start, "<fieldset> as a group frame (title in the border): use a card (section.sw-card with div.sw-card-head, Sw.Card, Sw.Group) or a flat section title; a real radio group may keep a fieldset without the frame look");
            bool hidden = String.Equals(n.GetAttribute("aria-hidden")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var click = n.GetAttribute("onclick");
            bool linkWithoutHref = (name == "a") && !Has(n, "href");
            if ((click != null) && !hidden && (NonInteractive.Contains(name) || linkWithoutHref))
            {
                if (!Has(n, "role"))
                    ctx.Add(Rule.A11yCustomControl, n.Start, "<" + name + " onclick> is clickable but not a control: keyboard and screen reader users can't use it; use a <button> (or an <a href>), or add role=\"button\" tabindex=\"0\" and a key handler (Sw.KeyClick)");
                else if (!Has(n, "tabindex") && (name != "a" || linkWithoutHref))
                    ctx.Add(Rule.A11yCustomControl, n.Start, "<" + name + " role=\"" + n.GetAttribute("role").Value + "\" onclick> can't be reached with the keyboard: add tabindex=\"0\" and a key handler (Sw.KeyClick)");
            }
            if ((name == "img") && !hidden && !Has(n, "alt") && !String.Equals(n.GetAttribute("role")?.Value, "presentation", StringComparison.OrdinalIgnoreCase))
                ctx.Add(Rule.A11yName, n.Start, "<img> without alt: add a text that says what the picture shows, or alt=\"\" for a decorative picture");
            if (((name == "button") || ((name == "a") && Has(n, "href"))) && !hidden && !n.SelfClosing
                && !HasValue(n, "aria-label") && !HasValue(n, "aria-labelledby") && !HasValue(n, "title"))
            {
                //  Icon only: child elements but no text (an empty element is filled by script, not reported)
                int depth = 0;
                bool text = false, child = false;
                for (int j = index + 1; j < nodes.Count; ++j)
                {
                    var c = nodes[j];
                    if ((c.Kind == HtmlNodeKind.EndTag) && (c.Name == name))
                    {
                        if (depth == 0)
                            break;
                        --depth;
                    }
                    else if (c.Kind == HtmlNodeKind.StartTag)
                    {
                        if (c.Name == name)
                            ++depth;
                        child = true;
                        if (HasValue(c, "aria-label") || ((c.Name == "img") && HasValue(c, "alt")))
                            text = true;
                    }
                    else if ((c.Kind == HtmlNodeKind.Text) && !String.IsNullOrWhiteSpace(ctx.Text.Substring(c.Start, c.End - c.Start).Replace("&nbsp;", "")))
                        text = true;
                }
                if (child && !text)
                    ctx.Add(Rule.A11yName, n.Start, "an icon only <" + name + "> without a name: add aria-label=\"...\" (what it does) and a title for the tooltip");
            }
        }

        // ------------------------------------------------------------------ page header

        //  Sw.PageHeader / .sw-ph, a component page header (the table page: header.sw-table-ph), or a table page (Sw.Table.Create({ Page: true }) makes it)
        //  (framework page helpers that build the page layout: Sw.AuthUi.Page of the auth pages, SmShared.FillTable of ServerManager)
        static readonly Regex HeaderMarker = new Regex(@"\bPage(?:Header|Layout)\b|\bAuthUi\.Page\(|\bFillTable\(|\bsw-ph\b|\bsw-[a-z]+-ph\b|Table\.Create\(\s*\{[^}]*\bPage\s*:\s*true", RegexOptions.CultureInvariant);
        /// <summary>Framework page libraries that build the page layout (page header and container) of the pages that load them.</summary>
        static readonly String[] LayoutLibraries = { "auth/AuthUi.js" };
        static readonly Regex OwnH1 =new Regex(@"<h1\b|[""'`]h1[.#""'`]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        //  A page container: main.sw-page-*, Sw.PageLayout, or a table page (the table is the fill container)
        static readonly Regex Container = new Regex(@"\bsw-page-(?:dash|form|read|fill)\b|\bPageLayout\b|\bAuthUi\.Page\(|\bFillTable\(|\bsw-table-page\b|Table\.Create\(\s*\{[^}]*\bPage\s*:\s*true", RegexOptions.CultureInvariant);

        /// <summary>
        /// The page header rule (design-page-header) for one HTML page: a new-format UI page (sw/core.js and sw/ui.css, not the shell)
        /// whose markup and own scripts never create a page header (<c>Sw.PageHeader</c> or the class <c>sw-ph</c>), unless the page is
        /// marked with <see cref="NoPageHeader"/>.
        /// </summary>
        /// <param name="ctx">The page</param>
        /// <param name="scriptTexts">The texts of the page's own scripts (resolved inside the checked folder, not library files)</param>
        public static void CheckPageHeader(Checker.FileContext ctx, IEnumerable<String> scriptTexts)
        {
            if (IsFramework(ctx.WebRel) || ctx.IsTemplated && ((ctx.WebRel == "index.html") || (ctx.WebRel == "debug.html")))
                return;
            var text = ctx.Text;
            if (text.IndexOf(NoPageHeader, StringComparison.Ordinal) >= 0)
                return;
            var nodes = HtmlScanner.Scan(text);
            bool core = false, ui = false, shell = false;
            HtmlNode body = null;
            foreach (var n in nodes)
            {
                if (n.Kind != HtmlNodeKind.StartTag)
                    continue;
                if (n.Name == "body")
                    body ??= n;
                var url = (n.Name == "script") ? n.GetAttribute("src") : (n.Name == "link") ? n.GetAttribute("href") : null;
                if (url?.Value == null)
                    continue;
                core |= Checker.IsSrc(url.Value, "sw/core.js");
                ui |= Checker.IsSrc(url.Value, "sw/ui.css");
                shell |= Checker.IsSrc(url.Value, "sw/shell.js");
                //  A framework page library that builds the page layout itself (the auth pages: Sw.AuthUi.Page, also from other projects)
                shell |= LayoutLibraries.Any(l => Checker.IsSrc(url.Value, l) || (url.Value.Trim() == l.Substring(l.IndexOf('/') + 1)));
            }
            if (!core || !ui || shell)
                return;
            var all = new List<String> { text };
            all.AddRange(scriptTexts);
            bool container = all.Any(t => Container.IsMatch(t));
            if (all.Any(t => HeaderMarker.IsMatch(t)))
            {
                if (!container)
                    ctx.Add(Rule.DesignPageContainer, body?.Start ?? 0, "the page has a page header but no page container: put the header and the content in <main class=\"sw-page-form\"> (or sw-page-dash / sw-page-read / sw-page-fill), or build both with Sw.PageLayout({ Type, Title, Description, Actions }); the container sets the max width per page type and the page padding");
                return;
            }
            bool h1 = all.Any(t => OwnH1.IsMatch(t));
            ctx.Add(Rule.DesignPageHeader, body?.Start ?? 0, "a UI page without a Design v2 page header" + (h1 ? " (it makes its own <h1>)" : "") + ": build it with Sw.PageLayout({ Type: \"form\" | \"dash\" | \"read\" | \"fill\", Title, Description, Actions }) (or start the page with Sw.PageHeader({ Title, Description, Status, Actions }).E"
                + (container ? ")" : " inside a page container <main class=\"sw-page-form\"> / sw-page-dash / sw-page-read / sw-page-fill)")
                + "; a page that deliberately has none (dialog, embed, full window app) gets a \"" + NoPageHeader + "\" comment");
        }
    }
}
