using System;
using System.Collections.Generic;

namespace SwWeb.Checking
{
    /// <summary>
    /// The severity of a finding.
    /// </summary>
    public enum Severity
    {
        /// <summary>
        /// Breaks a contract rule (makes "check" fail).
        /// </summary>
        Error,
        /// <summary>
        /// Suspicious, only fails "check" with --strict.
        /// </summary>
        Warning,
    }

    /// <summary>
    /// A rule checked by <see cref="Checker"/>.
    /// </summary>
    public sealed class Rule
    {
        Rule(String id, Severity severity, bool strictOnly, String description, bool design = false, String guide = null)
        {
            Id = id;
            Severity = severity;
            StrictOnly = strictOnly;
            Description = description;
            Design = design;
            Guide = guide;
        }

        /// <summary>
        /// A UX guide rule (_docs/UX_Guide.md in SysWeaver): the id of the guide rule it checks ("INP-25"), null for the other rules.
        /// The UX rules are design rules (only with --design), their ids are "ux-" + the lower case guide id, and a finding is allowed
        /// by a "SwWeb:allow ux-..." comment (see <see cref="UxChecker.AllowPrefix"/>).
        /// </summary>
        public readonly String Guide;

        static Rule Ux(String guide, String description) => new Rule("ux-" + guide.ToLowerInvariant(), Severity.Warning, false, description + " (UX guide " + guide + ")", true, guide);

        /// <summary>
        /// True for the Design v2 porting rules (page anatomy, tokens, elevation, colours, accessible custom controls): only checked with
        /// --design (check) or --level design (migrate, as manual follow-up warnings).
        /// </summary>
        public readonly bool Design;

        /// <summary>
        /// The rule id (as printed).
        /// </summary>
        public readonly String Id;
        /// <summary>
        /// The default severity.
        /// </summary>
        public readonly Severity Severity;
        /// <summary>
        /// True if only checked with --strict.
        /// </summary>
        public readonly bool StrictOnly;
        /// <summary>
        /// A one line description.
        /// </summary>
        public readonly String Description;

        /// <summary>A file name has more than one dot (only ".min.js" is allowed), it would be served at a different URL ("a.b.js" as "a/b.js").</summary>
        public static readonly Rule FileNameDots = new Rule("file-name-dots", Severity.Error, false, "A file name has more than one dot (except *.min.js); \"a.b.js\" is served as \"a/b.js\"");
        /// <summary>A folder name contains a '-' (MSBuild turns it into '_' in the resource name).</summary>
        public static readonly Rule FolderNameDash = new Rule("folder-name-dash", Severity.Error, false, "A folder name contains '-' (embedded as '_', so the URL changes)");
        /// <summary>A folder name starts with a digit (MSBuild prefixes '_').</summary>
        public static readonly Rule FolderNameDigit = new Rule("folder-name-digit", Severity.Warning, false, "A folder name starts with a digit (embedded with a '_' prefix)");
        /// <summary>A folder name contains a dot (the embedded resource name joins folders with dots, so "a.b/x.js" is served as "a/b/x.js").</summary>
        public static readonly Rule FolderNameDot = new Rule("folder-name-dot", Severity.Error, false, "A folder name contains a dot (not at the start); \"a.b/x.js\" is served as \"a/b/x.js\"");
        /// <summary>A .mjs file (not embedded, and ES modules are not used).</summary>
        public static readonly Rule MjsFile = new Rule("mjs-file", Severity.Error, false, "A .mjs file (not embedded; use classic .js scripts)");
        /// <summary>Not valid UTF-8.</summary>
        public static readonly Rule Encoding = new Rule("encoding", Severity.Error, false, "The file is not valid UTF-8");
        /// <summary>The lexer found an unterminated token, or the brackets of a script don't balance.</summary>
        public static readonly Rule Syntax = new Rule("syntax", Severity.Error, false, "Unterminated string, template, regex, comment or tag, or unbalanced (), [], {} or ${} in a script");
        /// <summary>The first argument of a translation call isn't a ' or " string literal.</summary>
        public static readonly Rule TrNotLiteral = new Rule("tr-not-literal", Severity.Error, false, "The first argument of _T/_TF/_TH/_TFH is not a '...' or \"...\" literal (not translated by the server)");
        /// <summary>A translation call on an object (obj._T(...)), the server rewrites it into invalid JS.</summary>
        public static readonly Rule TrMemberCall = new Rule("tr-member-call", Severity.Error, false, "A translation function called as a member (obj._T(...)); the server turns it into invalid JS");
        /// <summary>An identifier ending with _T/_TF/_TH/_TFH followed by '(' (the server treats it as a translation call).</summary>
        public static readonly Rule TrIdentifierSuffix = new Rule("tr-identifier-suffix", Severity.Error, false, "An identifier ending with _T, _TF, _TH or _TFH is called (the server rewrites it as a translation)");
        /// <summary>A translation call pattern inside a string, template, regex, comment, HTML text/attribute or CSS.</summary>
        public static readonly Rule TrInText = new Rule("tr-in-text", Severity.Error, false, "\"_T(\" (or _TF/_TH/_TFH) appears in a string, comment, template, HTML text/attribute or CSS (the server rewrites it)");
        /// <summary>A translated literal contains a \n escape or a line continuation (the minifier turns it into a backtick literal).</summary>
        public static readonly Rule TrNewline = new Rule("tr-newline", Severity.Error, false, "A translated literal contains \\n, \\r or a line continuation (minified to a backtick literal, not translated)");
        /// <summary>A translated literal contains both quote kinds (the minifier turns it into a backtick literal).</summary>
        public static readonly Rule TrMixedQuotes = new Rule("tr-mixed-quotes", Severity.Error, false, "A translated literal contains both ' and \" (minified to a backtick literal, not translated)");
        /// <summary>A translation call whose arguments don't match its placeholders (_TF with format arguments, _T with a placeholder without argument).</summary>
        public static readonly Rule TrFormat = new Rule("tr-format", Severity.Error, false, "_TF / _TFH called with format arguments (they never format: the page shows \"{0}\"), or _T / _TH with a {n} placeholder that has no argument (a trailing '...' literal is the translation context, not an argument); a warning for _T with arguments but no placeholder, or a placeholder and no arguments at all");
        /// <summary>A var(--sw-*) use (no fallback) of a token that the framework and the checked folder don't define.</summary>
        public static readonly Rule UnknownToken = new Rule("unknown-token", Severity.Warning, false, "var(--sw-...) without a fallback names a token that neither the framework (sw_tokens.txt, ui/tokens.css) nor the checked folder defines: css drops the declaration silently (a typo such as --sw-accent for --sw-accent1 or --sw-fs-sm for --sw-text-sm); the message names the closest token. A token defined elsewhere (a host page) gets a \"SwWeb:allow-token\" comment");
        /// <summary>An unknown ${...} in a templated file.</summary>
        public static readonly Rule TemplateVar = new Rule("template-var", Severity.Error, false, "A \"${\" in a templated file that is not a known template variable");
        /// <summary>A ${...} in templated CSS outside a custom property value.</summary>
        public static readonly Rule TemplateCss = new Rule("template-css", Severity.Error, false, "A template variable in CSS outside a --custom-property value (the minifier drops the ';')");
        /// <summary>A server template variable name inside a JS template literal or string.</summary>
        public static readonly Rule TemplateLiteralVar = new Rule("template-literal-var", Severity.Error, false, "\"${Session.\", \"${Env.\", \"${EnvInfo.\", \"${Color.\", \"${Server.\", \"${Request.\" or \"${TrN}\" in JS (the server replaces it)");
        /// <summary>A .js file without a JSDoc file header (the brand hook app/app.js is exempt).</summary>
        public static readonly Rule JsDocHeader = new Rule("jsdoc-header", Severity.Error, false, "A .js file doesn't start with a JSDoc file header (/** @file ... */); the brand hook app/app.js is exempt");
        /// <summary>Unbalanced //DEBUG_BEGIN //DEBUG_END markers.</summary>
        public static readonly Rule DebugMarkers = new Rule("debug-markers", Severity.Error, false, "Unbalanced //DEBUG_BEGIN / //DEBUG_END (Compress removes the lines between them)");
        /// <summary>A destructuring default / computed key reads an earlier declarator of the same declaration: the build's minifier moves the destructuring in front of it (see <see cref="MinifierChecker"/>).</summary>
        public static readonly Rule MinifierReorder = new Rule("minifier-reorder", Severity.Error, false, "A destructuring default value or computed key reads a name declared earlier in the same var / let / const declaration (consecutive declarations of the same kind count as one): the build's minifier (minify v2.24.19) moves the destructuring in front of that name, so the release build throws \"Cannot access 'x' before initialization\" (var: a silent undefined) while the unminified srcWeb works; compute the default after the destructuring, or put another statement between. \"SwWeb:allow minifier-reorder\" marks a deliberate case");
        /// <summary>A static reference to an internet resource (script, stylesheet, icon, image, CSS import / url, JS import).</summary>
        public static readonly Rule ExternalResource = new Rule("external-resource", Severity.Error, false, "An internet resource (http:, https: or // url) in <script src>, <link href>, <img src / srcset>, <iframe src>, <frame src>, <embed src>, <object data>, <video src / poster>, <audio src>, <source src / srcset>, <track src>, a CSS @import / url(), a JS import or an argument of Sw.Net.IncludeJs / IncludeCss (also the legacy includeJs / includeCss): pages must work offline and must not leak requests");
        /// <summary>A string literal internet url that a script requests (fetch, XMLHttpRequest.open, WebSocket, EventSource, sendBeacon, importScripts, Worker, .src = / setAttribute("src")).</summary>
        public static readonly Rule ExternalRequest = new Rule("external-request", Severity.Error, false, "A script requests a literal internet url (fetch, XMLHttpRequest.open, WebSocket, EventSource, sendBeacon, importScripts, new Worker, .src = or setAttribute(\"src\")); pages must work offline. An optional online feature that degrades without internet is marked with a \"SwWeb:allow-online\" comment on the line or the line before");
        /// <summary>An @import in app/app.css (the brand style sheet is adopted as a constructable style sheet, which ignores @import).</summary>
        public static readonly Rule CssImport = new Rule("css-import", Severity.Error, false, "An @import in app/app.css (not supported: the brand style sheet is adopted without its imports; put everything in the one file)");
        /// <summary>A relative url() in a custom property of app/app.css that doesn't start with "../" (resolved against the style sheet that uses the variable).</summary>
        public static readonly Rule BrandUrl = new Rule("brand-url", Severity.Error, false, "A relative url() in a --custom-property of app/app.css that is not written as \"../app/<file>\" (a url in a variable is resolved against the style sheet that uses it, one folder below the root)");
        /// <summary>A font file in a web folder (the build doesn't embed font files).</summary>
        public static readonly Rule FontFile = new Rule("font-file", Severity.Warning, false, "A font file (.woff, .woff2, .ttf, .otf, .eot) in a web folder: the build doesn't embed it, so it is missing in the deployed app; embed the font as a data: url in a .css file");
        /// <summary>Page css that restyles bare form elements (button, input, select, textarea) or the text size of a bare body: it also restyles the Sw components of the page.</summary>
        public static readonly Rule ElementCss = new Rule("element-css", Severity.Warning, false, "Page css restyles a bare button, input, select or textarea (or the font size / zoom of a bare body) outside a page scoped selector: it also changes the Sw components of the page (pagers, dialogs, menus); scope it, ex: \".my-page button\"");
        /// <summary>Page css that hides the overflow of a bare body / html (clips content and pagers on phones).</summary>
        public static readonly Rule BodyOverflow = new Rule("body-overflow", Severity.Warning, false, "Page css sets overflow (or overflow-y) hidden / clip on a bare body or html: content and pagers below the fold are clipped on phones; let the page scroll, or scroll an inner element");
        /// <summary>A root relative include ("/sw/core.js"): breaks when the site is served below a path (reverse proxy).</summary>
        public static readonly Rule RootRelativeUrl = new Rule("root-relative-url", Severity.Warning, false, "A <script src>, <link href>, <img src> or CSS url() starting with '/': it breaks when the site is served below a path (reverse proxy), use a relative url");
        /// <summary>ES module syntax (type="module" / "importmap", import / export declarations, import(), import.meta).</summary>
        public static readonly Rule EsModule = new Rule("es-module", Severity.Error, false, "ES module syntax: <script type=\"module\"> / \"importmap\", import / export declarations, dynamic import() or import.meta (only classic scripts are supported)");
        /// <summary>A legacy global used by a new-format page.</summary>
        public static readonly Rule LegacyGlobal = new Rule("legacy-global", Severity.Error, true, "A new-format page (includes sw/core.js) uses a legacy global (see legacy_globals.txt)");
        /// <summary>A new-format page also includes a legacy bundle.</summary>
        public static readonly Rule LegacyInclude = new Rule("legacy-include", Severity.Error, true, "A new-format page (includes sw/core.js) also includes common/common.js, app/application.js or app/application.css");
        /// <summary>A legacy global used by a migrated new-format page that loads the compatibility bundle sw/compat.js (a warning: it works, but should be cleaned up).</summary>
        public static readonly Rule LegacyCompat = new Rule("legacy-compat", Severity.Warning, true, "A migrated new-format page that loads sw/compat.js still uses a legacy global; use the Sw.* API, then remove the sw/compat.js / sw/compat.css includes");
        /// <summary>
        /// A page includes two bundles that contain the same source files (AS6-1): an error when one of them has no load once guard (the
        /// shared sources run twice: two server block overlays, a focus trap stack overflow); a warning when the legacy bundle
        /// common/common.js already contains the other one (the include adds nothing).
        /// </summary>
        public static readonly Rule BundleOverlap = new Rule("bundle-overlap", Severity.Error, false, "A page includes two bundles of sw/bundles.json with the same source files: an error when one of them is not \"LoadOnce\" (the shared sources run twice, ex: two server block overlays and a stack overflow in the focus trap); a warning (--strict) when common/common.js already contains every source of the other bundle (sw/ui.js, sw/core.js ...: it adds nothing; port the page to sw/core.js + sw/ui.js instead)");

        /// <summary>
        /// A new-format page uses a library api (Sw.Table, Sw.Shell, Sw.Edit, Sw.Upload, Sw.AuthUi, Sw.Chat) without including the library (always).
        /// </summary>
        public static readonly Rule MissingInclude = new Rule("missing-include", Severity.Error, false, "A new-format page (or its own scripts) uses a library api that the bundles don't contain without including it: Sw.Table needs explore/table.js + explore/table.css, Sw.Shell (ToMenuItems ...) sw/shell.js, Sw.Edit edit/Edit.js, Sw.Upload common/fileUploader.js, Sw.AuthUi auth/AuthUi.js, Sw.Chat chat/chat.js (SwWeb migrate adds them); an optional library is tested first (if (Sw.Table)). A messaging only page (sw/session.js without sw/core.js) that uses the theme (getTheme() ... appendTheme(), Sw.Theme) needs sw/theme.js, and Sw.Net requests (Sw.Net.Send / Get ..., auth/Auth.js) need sw/net.js");
        /// <summary>
        /// An inline script uses Sw at parse time while the bundles are deferred (always).
        /// </summary>
        public static readonly Rule ScriptOrder = new Rule("script-order", Severity.Error, false, "An inline <script> uses Sw at top level (ex: Sw.Page.Main(...)) while sw/core.js is deferred (or comes later): it runs before the bundle, \"Sw is not defined\"; put the code in the page's own deferred script or call it from <body onload> / DOMContentLoaded");

        /// <summary>
        /// A UI page that loads no SysWeaver bundle (a warning; an error with --strict in a srcWeb folder), plus its port steps (warnings).
        /// </summary>
        public static readonly Rule NoFramework = new Rule("no-framework", Severity.Warning, false, "A UI page (html / body document, not a mail template or redirect) that loads neither the Sw bundles (sw/core.js, sw/ui.js, sw/ui.css; or the messaging only bundle sw/session.js, a page that renders its own UI on purpose) nor the old common/common.js: not on the framework (no theme, density, translation, toasts, dialogs, page header); an error with --strict for a page in a srcWeb folder. The port steps are listed at their lines (own fetch, native confirm / alert / prompt, own <dialog>, own light / dark palette or theme switch, own svg icons, own font, no _T, no page header; PORTING.md A0); SwWeb migrate --level design adds the includes and sw-root. A page that must stay without the framework gets a \"SwWeb:no-framework (reason)\" comment");

        //  Design v2 porting rules (--design; PORTING.md in SysWeaver.HttpServer/srcWeb)

        /// <summary>An old format page (common/common.js / app/application.js): run SwWeb migrate.</summary>
        public static readonly Rule DesignLegacyPage = new Rule("design-legacy-page", Severity.Warning, false, "An old format page (includes common/common.js or app/application.js): run SwWeb migrate (includes, legacy globals, tokens), then port it to the Design v2 page anatomy", true);
        /// <summary>A new-format UI page without the Design v2 page header (Sw.PageHeader / .sw-ph).</summary>
        public static readonly Rule DesignPageHeader = new Rule("design-page-header", Severity.Warning, false, "A new-format UI page (sw/core.js + sw/ui.css) without a Design v2 page header: Sw.PageLayout({ Type, Title, Description, Actions }), or Sw.PageHeader({ Title, Description, Actions }) first in a page container (.sw-page-dash / -form / -read / -fill); a page that deliberately has none (dialog, embed, full window app) gets a \"SwWeb:no-page-header\" comment", true);
        /// <summary>A shadow in page css that isn't an elevation token, a focus ring or a hairline ring (Design v2: flat surfaces).</summary>
        public static readonly Rule DesignShadow = new Rule("design-shadow", Severity.Warning, false, "A box-shadow / text-shadow / drop-shadow in page css that is not var(--sw-elev-1 / -2), a focus ring or a 1 px ring: Design v2 surfaces are flat (hairline + tone), only floating layers get an elevation token; \"<lengths> var(--sw-shadow)\" is invalid since --sw-shadow became a complete shadow", true);
        /// <summary>A decorative gradient in page css (Design v2: no gradients on chrome).</summary>
        public static readonly Rule DesignGradient = new Rule("design-gradient", Severity.Warning, false, "A linear / radial / conic gradient in page css (background, border-image): Design v2 uses flat tonal surfaces; repeating patterns, masks and marked exceptions (\"SwWeb:allow-design\") are allowed", true);
        /// <summary>A hard-coded colour in page css instead of a token.</summary>
        public static readonly Rule DesignColor = new Rule("design-color", Severity.Warning, false, "A hard-coded colour (#hex, rgb() / hsl() ..., or a named colour) in page css instead of a token (--sw-text, --sw-text-2, --sw-surface, --sw-border, --sw-accent1, --sw-danger ...): it doesn't follow the five themes, dark mode or app colours. Custom property definitions (your own tokens, themes in app/app.css) are fine", true);
        /// <summary>An old alias token (var(--sw-bg-1), --sw-muted, --sw-shadow ...): SwWeb migrate renames it.</summary>
        public static readonly Rule DesignTokenAlias = new Rule("design-token-alias", Severity.Warning, false, "An old token name kept as an alias (var(--sw-bg-1), --sw-line, --sw-muted, --sw-faint, --sw-shadow, --sw-touch ...): use the Design v2 role token (SwWeb migrate renames the uses automatically)", true);
        /// <summary>A legacy theme variable use (var(--ThemeMain) ...) or an override of an alias token that no longer restyles the components.</summary>
        public static readonly Rule DesignLegacyToken = new Rule("design-legacy-token", Severity.Warning, false, "A legacy theme variable used in page css (var(--ThemeMain), var(--ThemeAcc1), rgba(var(--ThemeMainRGB), a), var(--ThemeRad) ...: use the role tokens), or an override of an alias token (--sw-touch: 44px) that no longer restyles the components (set the new token, ex: --sw-control-h)", true);
        /// <summary>A selector on an old element / class hook that only the legacy compat layer creates (SysWeaver-Button, .KeyClick ...).</summary>
        public static readonly Rule DesignLegacyClass = new Rule("design-legacy-class", Severity.Warning, false, "Page css styles an old element / class hook (SysWeaver-Button, SysWeaver-PopUpBackground, .KeyClick ...) that only the legacy compat layer creates; the Sw components use .sw-* classes", true);
        /// <summary>A shadow, gradient or hard-coded colour set from JavaScript (inline style).</summary>
        public static readonly Rule DesignInlineStyle = new Rule("design-inline-style", Severity.Warning, false, "JavaScript sets an inline box-shadow, gradient or hard-coded colour (el.style.boxShadow = \"...\", a \"box-shadow: ...\" / \"color: #...\" style string): move it to the page css on tokens (or a Sw component)", true);
        /// <summary>A clickable element that isn't a control (div / span / li / img ... with a click handler) without role and tabindex.</summary>
        public static readonly Rule A11yCustomControl = new Rule("a11y-custom-control", Severity.Warning, false, "A clickable element that is not a control (div, span, li, td, img, an <a> without href ... with onclick / on: { click }) and has no role and tabindex: keyboard and screen reader users can't use it; use a <button> / Sw.Button / an <a href>, or add role=\"button\", tabindex=\"0\" and Sw.KeyClick", true);
        /// <summary>A control or image without an accessible name.</summary>
        public static readonly Rule A11yName = new Rule("a11y-name", Severity.Warning, false, "A <button> / <a> without text, aria-label, aria-labelledby or title (an icon only control), or an <img> without alt: screen readers announce nothing useful", true);
        /// <summary>A new-format UI page with a page header but without a page container (.sw-page-*).</summary>
        public static readonly Rule DesignPageContainer = new Rule("design-page-container", Severity.Warning, false, "A new-format UI page with a page header but without a Design v2 page container: wrap the content in main.sw-page-dash / -form / -read / -fill (Sw.PageLayout does it), which sets the max width and the page padding", true);
        /// <summary>Facts drawn as a row of bordered pills (Design v2: a facts strip; pills only for status and filters).</summary>
        public static readonly Rule DesignPillFacts = new Rule("design-pill-facts", Severity.Warning, false, "Page css draws pills / stat chips (a class named *-stat, *-pill or *-fact with a border or radius in a flex row): facts belong in a facts strip (Sw.Facts, dl.sw-facts), pills / badges only for status and filters (Sw.Badge, Sw.Chip)", true);
        /// <summary>A px / rem radius in page css instead of the radius tokens.</summary>
        public static readonly Rule DesignRadius = new Rule("design-radius", Severity.Warning, false, "A fixed border-radius in page css (px / rem / em other than 0 and 50%): use the radius tokens --sw-radius-sm / --sw-radius / --sw-radius-md / --sw-radius-lg / --sw-radius-pill (Pro and Hacker have their own scale)", true);
        /// <summary>A fieldset used as a group frame (Design v2: cards).</summary>
        /// <summary>A meter (Sw.Kpi Meter / Sw.Meter) without thresholds: a plain bar (the old default warn / danger levels are gone).</summary>
        public static readonly Rule DesignMeterLevels = new Rule("design-meter-levels", Severity.Warning, false, "A meter (Sw.Kpi({ Meter: true / { Max } }), Sw.Meter({ ... })) without Warn / Danger / Levels: it is a plain bar (the old default levels 0.8 / 0.92 are gone); give Warn / Danger for a use, Good: \"high\" when more is better, or Levels: false for a plain bar", true);
        public static readonly Rule DesignFieldset = new Rule("design-fieldset", Severity.Warning, false, "A <fieldset> / <legend> (or el(\"fieldset\")) used as a group frame: the most dated look; use a card (Sw.Card, Sw.Group, section.sw-card) or a flat section title", true);

        /// <summary>
        /// A UI page without a viewport meta tag (phones render it 980 px wide, zoomed out).
        /// </summary>
        public static readonly Rule DesignViewport = new Rule("design-viewport", Severity.Warning, false, "A UI page (sw/core.js or common/common.js) without <meta name=\"viewport\">: phones render it 980 px wide and zoomed out, so the Design v2 phone layouts never apply; add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"> (SwWeb migrate --level design does it)", true);

        /// <summary>
        /// A new-format UI page whose body doesn't have the class sw-root (design).
        /// </summary>
        public static readonly Rule DesignSwRoot = new Rule("design-sw-root", Severity.Warning, false, "A new-format UI page (sw/core.js + sw/ui.css, not the shell) whose <body> has no class=\"sw-root\": the Sw components are built for its border-box sizing, font and colour (an Sw.Table can grow on every refresh without it); <body class=\"sw-root\"> (SwWeb migrate adds it), or a \"SwWeb:no-sw-root\" comment", true);

        /// <summary>
        /// A confirmation (Sw.Dialog.Confirm) whose title or OK text is a destructive action without Danger, or with "Yes ..." / "No ..." button labels (design).
        /// </summary>
        public static readonly Rule DesignConfirm = new Rule("design-confirm", Severity.Warning, false, "A Sw.Dialog.Confirm whose title or OK text is a destructive action (Delete, Remove, Kill ...) without Danger: true (a danger button, Cancel focused, Enter never confirms; or use Sw.Cells.ConfirmDanger), or with \"Yes, Delete!\" / \"No, Keep it!\" button labels: buttons are verbs, the way out is \"Cancel\" (ui/README.md Dialog rules; SwWeb migrate fixes the old Confirm calls)", true);

        /// <summary>
        /// An options object literal of Sw.Dialog.Confirm (the options form or the 4th argument) names an option Confirm doesn't have (design).
        /// </summary>
        public static readonly Rule DesignConfirmOptions = new Rule("design-confirm-options", Severity.Warning, false, "An options object of Sw.Dialog.Confirm names an option it doesn't have (a typo or another api's option, ex: Confirm: \"Clear\" for OkText: the button reads \"OK\"); it is ignored at run time. Options form: Sw.Dialog.Confirm({ Title, Text, OkText, CancelText, Danger, Safe, OkIcon, CancelIcon, OkTitle, CancelTitle, Icon, Target, Id, Owner }); 4th argument: the same without Title, Text and OkText (ui/README.md; the same schema as the run time check, ui/overlay.js)", true);

        /// <summary>
        /// Page code that toasts the error of an editor's Validate(true), which shows the error itself (design).
        /// </summary>
        public static readonly Rule DesignErrorTwice = new Rule("design-error-twice", Severity.Warning, false, "Page code shows the error of Sw.Edit's Validate(true) again (Sw.Fail / Sw.Toast right after it): Validate(true) shows it once itself, under the field (a toast only when the field can't be seen; the result has Shown: true); drop the own toast (edit/README.md \"Widths, hints, validation messages\")", true);

        /// <summary>
        /// An internal change tracking id (a fix round or tracker id, see Checker.InternalIdPattern) in a shipped source file or document (strict).
        /// </summary>
        public static readonly Rule InternalId = new Rule("internal-id", Severity.Warning, true, "A comment or document names an internal change tracking id (a fix round or tracker id of the form C<n>R<n>-Name-<n>, F<nn>-Name-<n>, CR-S<n> ...): readers of the framework can't look it up; describe the rule or the reason itself");

        /// <summary>
        /// Page code that does what the framework does: own requests, own light / dark css, an own palette (design; native dialogs and an own
        /// &lt;dialog&gt; are <see cref="UxPop9"/> / <see cref="UxPop1"/>).
        /// </summary>
        public static readonly Rule DesignOwnUi = new Rule("design-own-ui", Severity.Warning, false, "Page code does itself what the framework does: an own fetch() / XMLHttpRequest for the server (Sw.Net.Send; a request Sw.Net can't make: credentials, mode, redirect, an abort signal, an Authorization header, a download of bytes, is not reported), own light / dark css with @media (prefers-color-scheme) (the SysWeaver themes and role tokens), an own colour palette: it ignores the theme and the shell (native confirm() / alert() / prompt() are ux-pop-9, an own <dialog> ux-pop-1)", true);

        /// <summary>
        /// A page that links sw/ui.css without common/theme.css (before it): the Sw tokens keep their light defaults, the page ignores the
        /// themes (Dark, Pro, Application, Hacker) and the user's choice.
        /// </summary>
        public static readonly Rule ThemeInclude = new Rule("theme-include", Severity.Warning, false, "A page links sw/ui.css (or sw/shell.css) without <link rel=\"stylesheet\" href=\"../common/theme.css\"> before it: the page keeps the light look in the Dark, Pro, Application and Hacker themes (the shell around it switches, the page does not); link common/theme.css first (SwWeb migrate adds it)");

        /// <summary>
        /// A user visible literal text in code that is not translated (no _T / _TF around it), or a page whose own scripts make texts and never translate.
        /// </summary>
        public static readonly Rule TrUntranslated = new Rule("tr-untranslated", Severity.Warning, false, "A user visible literal text in code without _T / _TF: a Sw option (Title, Text, Description, Label, Placeholder, Tooltip, Content ...), Sw.Toast / Sw.Info / Sw.Dialog.* texts, textContent / innerText / title / placeholder / aria-label, createTextNode, or words between the tags of an html string (innerHTML, insertAdjacentHTML); and a new-format page whose own scripts make texts but never call _T. Html text and the title / placeholder / alt / aria-label attributes of a page are translated by the server. A text that must not be translated (a product name, a code) gets a \"SwWeb:no-tr (why)\" comment");

        /// <summary>
        /// A translation function used as a value (aliased, passed or stored): the server only translates direct calls with a literal.
        /// </summary>
        public static readonly Rule TrAlias = new Rule("tr-alias", Severity.Error, false, "A translation function (_T, _TF, _TH, _TFH) used as a value (const t = _T; list.map(_T); obj.f = _T): the server only translates direct calls _T(\"literal\" ...), the aliased calls show the untranslated text (PORTING.md A9: never rename, wrap or alias them)");

        /// <summary>
        /// Page css that removes the focus indicator (outline: none / 0 on :focus / :focus-visible) without a replacement (design).
        /// </summary>
        public static readonly Rule A11yFocus = new Rule("a11y-focus", Severity.Warning, false, "Page css removes the focus indicator (outline: none / 0 on :focus or :focus-visible) without a replacement in the same rule (box-shadow var(--sw-focus-ring), an outline or a border colour): keyboard users lose their place (WCAG 2.4.7); the Sw components draw the focus ring, leave it", true);

        /// <summary>
        /// An own font family in page css or code (design).
        /// </summary>
        public static readonly Rule DesignFont = new Rule("design-font", Severity.Warning, false, "Page css (or an inline style from code) sets an own font-family (Verdana, Arial, monospace ...): the themes set the fonts (sw-root: var(--sw-sans), code: var(--sw-font-mono), Hacker its own monospace); use the tokens or inherit", true);

        /// <summary>
        /// Sw.Page.Navigate(url) with one argument for another site or a file: it goes there in the same tab now (design).
        /// </summary>
        public static readonly Rule DesignNavigate = new Rule("design-navigate", Severity.Warning, false, "Sw.Page.Navigate(url) without a target for another site or a file download (http(s) url, .xlsx, .csv, .json, .pdf, .zip ...): Navigate goes there in the same tab (the default target is \"_self\"; the old Open opened a new tab), so the page (and the shell frame) is replaced; pass \"_blank\" (Sw.Page.Navigate(url, \"_blank\")), or use a link (Sw.Cells.ExternalLink)", true);

        //  UX guide rules (--design; _docs/UX_Guide.md in SysWeaver, "How to read and audit"); see UxChecker

        /// <summary>UX-INP-1: an action bound to a pointer / mouse / touch release or press instead of click.</summary>
        public static readonly Rule UxInp1 = Ux("INP-1", "An action bound to pointerup / mouseup / touchend without a press or move handler (a release action instead of click), or an inline onmousedown / onpointerdown / ontouchstart action: keyboard, screen readers and voice control fire only click; use a click handler guarded by Sw.BadClick(ev)");
        /// <summary>UX-INP-4: the browser context menu blocked without a menu of its own.</summary>
        public static readonly Rule UxInp4 = Ux("INP-4", "The browser context menu is blocked without a menu of its own: oncontextmenu=\"return false\", or an unconditional preventDefault() in a contextmenu handler of the document, window or body");
        /// <summary>UX-INP-10: cursor: wait, or a busy cursor on the whole page.</summary>
        public static readonly Rule UxInp10 = Ux("INP-10", "cursor: wait (never used), or cursor: progress on the whole page (body, html, :root, *, document.body.style.cursor); progress belongs on a busy control only");
        /// <summary>UX-INP-11: a :hover rule outside @media (hover: hover).</summary>
        public static readonly Rule UxInp11 = Ux("INP-11", "A :hover rule outside @media (hover: hover): on touch screens the hover state sticks after a tap");
        /// <summary>UX-INP-12: the native disabled attribute on a button.</summary>
        public static readonly Rule UxInp12 = Ux("INP-12", "A button disabled with the native disabled attribute / property (<button disabled>, saveButton.disabled = ...): it leaves the tab order and its reason can't be read; use Sw.Disabled.Set(el, true, reason) / SetDisabled(true, reason) of the Sw controls (aria-disabled, an activation guard and a reason tool tip)");
        /// <summary>UX-INP-23: a page shortcut on Alt+Left / Alt+Right.</summary>
        public static readonly Rule UxInp23 = Ux("INP-23", "A key handler that acts on Alt+ArrowLeft / Alt+ArrowRight (the browser's Back / Forward)");
        /// <summary>UX-INP-25: a zoom locking viewport.</summary>
        public static readonly Rule UxInp25 = Ux("INP-25", "A viewport that blocks zoom (user-scalable=no / 0, maximum-scale=1) in a meta tag or a script string");
        /// <summary>UX-INP-30: a selectstart handler.</summary>
        public static readonly Rule UxInp30 = Ux("INP-30", "A selectstart handler (onselectstart, addEventListener(\"selectstart\")): text selection belongs to the browser; chrome uses user-select: none, content .sw-selectable");
        /// <summary>UX-POP-1: an own dialog element.</summary>
        public static readonly Rule UxPop1 = Ux("POP-1", "An own <dialog> (or showModal()): it doesn't block the shell and other frames, isn't movable and misses the dialog rules; use Sw.Dialog");
        /// <summary>UX-POP-9: a native confirm / alert / prompt, or OK / Yes / No button labels.</summary>
        public static readonly Rule UxPop9 = Ux("POP-9", "A native confirm() / alert() / prompt() (no title, \"OK\" buttons, ignores the theme; also through window[\"confirm\"] or a reference), or a button labelled \"OK\" / \"Yes\" / \"No\" (buttons are verbs)");
        /// <summary>UX-FOC-4: a positive tabindex.</summary>
        public static readonly Rule UxFoc4 = Ux("FOC-4", "A positive tabindex (tabindex=\"1\", el.tabIndex = 2): the focus order no longer follows the visual order");
        /// <summary>UX-FRM-1: a form field without a label.</summary>
        public static readonly Rule UxFrm1 = Ux("FRM-1", "An <input> / <select> / <textarea> in the page markup without a label (<label for>, a wrapping <label>, aria-label, aria-labelledby or title); a placeholder is not a label");
        /// <summary>UX-FRM-3: a validation error shown as a toast.</summary>
        public static readonly Rule UxFrm3 = Ux("FRM-3", "A validation error shown as a toast (Sw.Toast / Sw.Fail / Sw.Info / Sw.Warn with \"... is required\", \"must be\", \"invalid ...\", \"please enter\" ...): validation errors are inline, under the field (also TIP-4)");
        /// <summary>UX-FRM-6: a password field without autocomplete.</summary>
        public static readonly Rule UxFrm6 = Ux("FRM-6", "An <input type=\"password\"> without autocomplete (current-password / new-password): password managers can't fill it");
        /// <summary>UX-MOT-2: a hard-coded duration, delay or easing.</summary>
        public static readonly Rule UxMot2 = Ux("MOT-2", "A hard-coded transition / animation duration, delay or cubic-bezier easing (css, style strings, el.animate({ duration })), or a transitionend / animationend wait without a fallback timer; use --sw-dur-1/2/3 and --sw-ease (infinite animations such as spinners, and reduced motion blocks, are exempt)");
        /// <summary>UX-MOT-4: a transition or animation of a layout property.</summary>
        public static readonly Rule UxMot4 = Ux("MOT-4", "A transition or @keyframes animation of width, height, top, left, right, bottom, inset or margins (animate only opacity, transform and colours; grid-template-rows for an expanding section)");
        /// <summary>UX-MOT-6: blinking or scrolling text.</summary>
        public static readonly Rule UxMot6 = Ux("MOT-6", "Blinking or scrolling text (<marquee>, <blink>, text-decoration: blink)");
        /// <summary>UX-LAY-1: an image / frame / video without a reserved size.</summary>
        public static readonly Rule UxLay1 = Ux("LAY-1", "An <img>, <iframe> or <video> in the page markup without a reserved size (width and height attributes, a css width and height, or an aspect-ratio, through its style, class or id): the page shifts when it loads");
        /// <summary>UX-TIP-2: a tool tip that repeats the name.</summary>
        public static readonly Rule UxTip2 = Ux("TIP-2", "A tool tip that repeats the visible name (<button title=\"Save\">Save</button>, { Text: _TF(\"Save\"), Title: _TF(\"Save\") }): screen readers read both; the tool tip adds the target, the consequence or the shortcut (TIP-1)");
        /// <summary>UX-ACC-3: more than one h1.</summary>
        public static readonly Rule UxAcc3 = Ux("ACC-3", "More than one <h1> in a page's markup (one h1 per page, headings in order)");
        /// <summary>UX-ACC-10: an aria-label that doesn't start with the visible text.</summary>
        public static readonly Rule UxAcc10 = Ux("ACC-10", "A button or link with visible text whose aria-label doesn't start with that text (label in name, WCAG 2.5.3: voice control users say what they see)");
        /// <summary>UX-TXT-12: a translate attribute that isn't lower case no / yes.</summary>
        public static readonly Rule UxTxt12 = Ux("TXT-12", "A translate attribute whose value is not \"no\" or \"yes\" (lower case): the server translator only skips translate=\"no\"");
        /// <summary>UX-TXT-15: words in css content.</summary>
        public static readonly Rule UxTxt15 = Ux("TXT-15", "Words in a css content: value (\"New\", \"(required)\"): css is not translated; content holds only symbols, texts are set from JS with _TF");

        //  Usage patterns of the framework APIs (the misuse that compiles and fails silently; see UxChecker.CheckPatterns)

        /// <summary>UX-INP-2: a click handler on a card / row / div that ignores modifier clicks.</summary>
        public static readonly Rule UxInp2 = Ux("INP-2", "A click handler on an element that is not a control (a div, row, cell, list item or Sw.Card) or a delegated item click (e.target.closest(...)) without Sw.BadClick / Sw.ModifierClick / Sw.IsPureEvent: a Ctrl / Shift click runs the action instead of only moving the focus");
        /// <summary>UX-FBK-2: an OnClick that starts a request without returning the promise; a busy look made of opacity / pointer-events.</summary>
        public static readonly Rule UxFbk2 = Ux("FBK-2", "An OnClick with a block body that starts a request (Sw.Net.Send / Get, fetch, .then) and returns no promise (no return, no await): the button never gets busy and a second click sends again; or css that fades / blocks a busy control (opacity, pointer-events: none on [aria-busy] / *busy): busy is not disabled");
        /// <summary>UX-FBK-4: long work without a way to stop or leave it.</summary>
        public static readonly Rule UxFbk4 = Ux("FBK-4", "Sw.Dialog.Working({ ... }) with none of Work, Promise, Cancel, Background or CloseText: an unbounded spinner the user can neither stop nor send to the activity list");
        /// <summary>UX-POP-6: a busy dialog without a way out.</summary>
        public static readonly Rule UxPop6 = Ux("POP-6", "A dialog handle's SetBusy(promise) without Cancel / Abort / Background / CloseText (a work function SetBusy(({ Signal }) => ...) is fine), or an async footer OnClick of a dialog with Cancel that sends a request and never takes the Signal: the user can only wait");
        /// <summary>UX-SAF-1: a destructive request in a click handler without a confirmation.</summary>
        public static readonly Rule UxSaf1 = Ux("SAF-1", "A click / key handler that calls a destructive api (Delete*, Remove*, Kill*, Revoke*, Reset*, Purge* ...) with no confirmation (Sw.Dialog.Confirm / Sw.Cells.ConfirmDanger) and no Undo in the handler");
        /// <summary>UX-SAF-3: a danger button that asks with a plain confirmation.</summary>
        public static readonly Rule UxSaf3 = Ux("SAF-3", "The OnClick of a Kind: \"danger\" control asks with Sw.Dialog.Confirm(..., { Safe: true }) (without Danger): OK gets the focus and Enter confirms the destructive action");
        /// <summary>UX-SAF-10: own leave prompts, dirty keys never cleared, edit forms without dirty tracking.</summary>
        public static readonly Rule UxSaf10 = Ux("SAF-10", "An own beforeunload handler (Sw.Page.SetDirty asks on menu, links and Back too), a Sw.Page.SetDirty key that the file never clears, or a page that edits fields and saves them without Sw.Page.SetDirty / Sw.Form / Sw.Edit");
        /// <summary>UX-SAF-11: own drafts, dropped draft handles, multi-line text without a draft.</summary>
        public static readonly Rule UxSaf11 = Ux("SAF-11", "Own local storage drafts (a key named *draft*), a Sw.Draft.Attach handle that is dropped or never cleared, or a page multi-line text field (textarea, Sw.TextBox Multiline) without Sw.Draft");
        /// <summary>UX-LST-3: a hand-made table.</summary>
        public static readonly Rule UxLst3 = Ux("LST-3", "A hand-made <table> in script (createElement(\"table\"), Sw.el(\"table\"), \"<table\" in html strings): no sorting, counts, search, row actions, modifier clicks or copy (LST-2 to LST-7); use Sw.Table.Create");
        /// <summary>UX-SEC-1: a secret copied with an announcement of its value.</summary>
        public static readonly Rule UxSec1 = Ux("SEC-1", "Sw.Clipboard.Copy / Sw.CopyButton of a key, token, secret or password without What: the announcement and the confirmation say the value aloud");
        /// <summary>UX-INP-31: a copy button with nothing to copy.</summary>
        public static readonly Rule UxInp31 = Ux("INP-31", "Sw.CopyButton without Value or Get: it copies an empty text and still confirms \"Copied\"");
        /// <summary>UX-POP-7: one Id for two kinds of dialog.</summary>
        public static readonly Rule UxPop7 = Ux("POP-7", "The same literal dialog Id given to two different Sw.Dialog.Open / Show calls in one file: two kinds of dialog share one remembered position and size");
        /// <summary>UX-MOT-1: an overlay removed in one frame.</summary>
        public static readonly Rule UxMot1 = Ux("MOT-1", "An element made as an overlay (sw-popup / sw-dialog / sw-layer / sw-toast / sw-menu / sw-hint, role dialog / tooltip / menu) removed with .remove(): it vanishes in one frame; use Sw.Motion.Out(el)");
        /// <summary>UX-FBK-7: a timer that rebuilds rows.</summary>
        public static readonly Rule UxFbk7 = Ux("FBK-7", "A timer that rebuilds rows (a setInterval callback, or a polling function that reschedules itself with setTimeout, that sets innerHTML / replaceChildren of a tbody or a row list): the order jumps under the pointer and the focus is lost; use Sw.Table with SetRows(rows, true)");

        public static readonly Rule UxSaf12 = Ux("SAF-12", "Page code that rewrites its history entry (history.replaceState / pushState): the frame url, Back and Sw.History state break; keep view state with Sw.History");

        public static readonly Rule UxFbk9 = Ux("FBK-9", "Sw.Net.ShouldReportAsync(e) used as a condition without await (a Promise is always true), or .then(...) on the boolean Sw.Net.ShouldReport(e)");

        public static readonly Rule UxFbk5 = Ux("FBK-5", "A failure the user never sees or a result said twice: a \"Saved\" / \"Updated\" toast for a result shown in place (an OnClick, the OnChange of an auto-saving control), an async OnClick that resolves false without saying why, a catch that is empty or only logs, .catch(() => {}) / .catch(console.error), a request in an event listener or timer whose failure nobody handles, Sw.Work.Report twice, a dialog button that closes its dialog and then fails");

        //  The families of series 4 round 3: security, formatting, pointer events other than click, silent failures

        /// <summary>UX-SEC-2: user content inserted as raw html.</summary>
        public static readonly Rule UxSec2 = Ux("SEC-2", "User data inserted as raw html: innerHTML / outerHTML / insertAdjacentHTML / document.write / an html option with a value that is not a literal or escaped, or a helper that builds markup by concatenating values (item.Name): a name like <img onerror> runs as code; set text (Sw.el(tag, { text }), textContent) and build the elements");
        /// <summary>UX-TXT-20: numbers, dates and sizes formatted by hand.</summary>
        public static readonly Rule UxTxt20 = Ux("TXT-20", "A number, date, time, size or percentage formatted by hand (toFixed(1) + \" KB\", Math.round(x * 100) + \"%\", toLocaleString / toLocaleDateString, Intl.NumberFormat / DateTimeFormat, getHours() + \":\" + ...): the app's language, separators and time zone are ignored; use Sw.Format (Number, Bytes, DurationMs, TimeStamp, TimeSpan)");
        /// <summary>UX-TXT-6: translated text built by concatenation.</summary>
        public static readonly Rule UxTxt6 = Ux("TXT-6", "A sentence built by concatenating translated pieces and values (_TF(\"Removed\") + \" \" + n + \" \" + _TF(\"items\"), a template with ${_TF(...)} and ${value}, a plural \"s\"): the word order differs between languages; write one sentence with placeholders: _T(\"Removed {0} items\", n, \"context\")");
        /// <summary>UX-INP-6: a double click as the only way.</summary>
        public static readonly Rule UxInp6 = Ux("INP-6", "A dblclick handler that acts, on an element without a click / key handler: a double click is never the only way (touch, keyboard and screen readers can't double click); give the element a single pure click, a menu item or a key");
        /// <summary>UX-INP-8: an action on hover.</summary>
        public static readonly Rule UxInp8 = Ux("INP-8", "A mouseenter / mouseover / pointerenter handler that acts (a write request, navigation, a menu or dialog): hover may only show a tool tip or a preview; touch and keyboard users never hover, and a passing pointer changes data");
        /// <summary>UX-INP-20: drag and drop as the only way.</summary>
        public static readonly Rule UxInp20 = Ux("INP-20", "Drag and drop (draggable + a drop handler) as the only way to reorder or move: no key (Alt+ArrowUp / Down, Sw.Keys) and no single pointer alternative (a \"Move up / down\" menu item or button) does the same work");
        /// <summary>UX-TXT-17: text clipped without an ellipsis.</summary>
        public static readonly Rule UxTxt17 = Ux("TXT-17", "white-space: nowrap with overflow: hidden / clip and no text-overflow: ellipsis: a longer translation is cut without a sign; add text-overflow: ellipsis and a tool tip, or let it wrap");
        /// <summary>UX-SAF-4: a danger confirmation that falls back to a weaker one on an error.</summary>
        public static readonly Rule UxSaf4 = Ux("SAF-4", "A danger confirmation that falls back to a weaker one when something fails (a plain Confirm in a catch, or type-to-confirm options chosen by a value that a catch resets): the destructive action is then confirmed by less; show the failure and offer no destructive button instead");
        /// <summary>UX-FRM-5: typing while a save runs.</summary>
        public static readonly Rule UxFrm5 = Ux("FRM-5", "A dialog with EditWhileBusy: true whose save does not read the fields again after its request: what the user types while it saves is lost");
        /// <summary>UX-TXT-30: a raw error message as the whole user text.</summary>
        public static readonly Rule UxTxt30 = Ux("TXT-30", "A raw error message shown as the whole text (Sw.Fail(e.message), Sw.Toast(e.message)), or a sentence glued after Sw.Net.ErrorText(...).Text: the user learns neither what failed nor what to do; pass the error (Sw.Fail(e)) or use Sw.Net.FailText(name, e)");

        //  The families of series 4 round 4 (the critics' unseen sets: sneak7, misuse4)

        /// <summary>UX-SAF-2: a confirmation that names nothing, or a destructive one without the danger button.</summary>
        public static readonly Rule UxSaf2 = Ux("SAF-2", "A confirmation that names nothing (\"Are you sure?\", \"Really?\", \"Confirm\", an empty text) or a destructive one (a danger button's, before a destructive request, OK text \"Cancel shipments\" / \"Delete ...\") without Danger: true: the title names the action and the target, the text the consequence, the button the verb, and a destructive one is the danger button with Cancel focused");
        /// <summary>UX-FOC-2: the initial focus on a destructive button.</summary>
        public static readonly Rule UxFoc2 = Ux("FOC-2", "Focus moved from code to a destructive button (Kind: \"danger\", Danger: true): Enter or Space then starts the destructive action; focus the first field, the search box or the page heading");
        /// <summary>UX-ACC-11: a value change or a focus that navigates or opens.</summary>
        public static readonly Rule UxAcc11 = Ux("ACC-11", "A change / input handler (a select, a field, a Sw value control's OnChange) that navigates, opens a window or submits, or a focus handler that opens a dialog / menu: changing a value or moving the focus must never take the user elsewhere; use a \"Go\" / \"Open\" button");
        /// <summary>UX-MOT-3: smooth scrolling that ignores reduced motion.</summary>
        public static readonly Rule UxMot3 = Ux("MOT-3", "Smooth scrolling (scrollIntoView / scrollTo / scrollBy with behavior: \"smooth\", css scroll-behavior: smooth) without a reduced motion check: with prefers-reduced-motion it must jump");
        /// <summary>UX-LST-4: a click that copies.</summary>
        public static readonly Rule UxLst4 = Ux("LST-4", "A plain click on a cell / value that copies it (navigator.clipboard, Sw.Clipboard.Copy, execCommand(\"copy\") in a click handler of a non-button element): a click selects or focuses; copy is Ctrl+C, a copy button (Sw.CopyButton) or the cell menu");
        /// <summary>UX-FBK-6: a list without empty / error states.</summary>
        public static readonly Rule UxFbk6 = Ux("FBK-6", "A list loaded from the server and rendered by hand without an empty state (no test of the row count): an empty list looks broken; use Sw.Table (empty, loading and error states) or show \"No ... yet\" and a failure with Try again");
        /// <summary>UX-ACC-5: a Sw control whose label is cut off its input.</summary>
        public static readonly Rule UxAcc5 = Ux("ACC-5", "The id of a Sw control's input changed after it was made (x.Input.id = ...): its <label for> no longer names it (no accessible name), and the secret-label checks no longer see it");
        /// <summary>UX-INP-24: small touch targets.</summary>
        public static readonly Rule UxInp24 = Ux("INP-24", "A clickable element (a button, a *btn* / *ico* / *close* class, cursor: pointer) sized below 24 x 24 css px without a min size of 24 px: too small to hit, also with a mouse");
        /// <summary>UX-LAY-8: a fixed page width.</summary>
        public static readonly Rule UxLay8 = Ux("LAY-8", "A min-width / width over 760 px (48 rem) outside a min-width media query and outside a horizontal scroller: the page scrolls sideways on phones and at 200 % zoom");
        /// <summary>UX-MOT-7: faded out but still there.</summary>
        public static readonly Rule UxMot7 = Ux("MOT-7", "opacity: 0 in a rule without visibility: hidden / display: none / inert: the invisible content still takes clicks, keyboard focus and is read by screen readers");
        /// <summary>UX-LAY-5: live items inserted above what the user reads.</summary>
        public static readonly Rule UxLay5 = Ux("LAY-5", "Items that arrive by themselves (a message subscription, a timer, a socket) inserted at the top of a list (prepend, insertBefore(x, first), afterbegin): what the user reads moves down; add them at the end, or keep the scroll position and show \"n new\" to reveal them");
        /// <summary>UX-INP-21: a link without href.</summary>
        public static readonly Rule UxInp21 = Ux("INP-21", "An <a> without href: it is no link, not focusable, Enter does nothing and screen readers don't announce it; use <a href> for navigation or a <button> for an action");
        /// <summary>UX-TXT-13: words in attributes the html translator doesn't translate.</summary>
        public static readonly Rule UxTxt13 = Ux("TXT-13", "Words in aria-description, aria-valuetext or data-* in the html: the server translator serves them as written; set them from JS with _TF");


        //  The families of series 4 round 5 (the critics' unseen sets: sneak8, misuse5)

        /// <summary>UX-TIP-1: an icon-only button without a tool tip.</summary>
        public static readonly Rule UxTip1 = Ux("TIP-1", "An icon-only Sw.Button (Icon, no Text) without Title / AriaLabel: no name for screen readers and no tool tip that says what it does");
        /// <summary>UX-TIP-7: a helper text repeated on every item.</summary>
        public static readonly Rule UxTip7 = Ux("TIP-7", "A helper text (\"Double-click to open\", \"Click to ...\") made for every row / item of a list: say it once (header, empty state, tool tip)");
        /// <summary>UX-POP-11: a pop-up nobody asked for.</summary>
        public static readonly Rule UxPop11 = Ux("POP-11", "A dialog opened by the page's start code (Sw.Dialog.Alert / Open / Show / Confirm in Sw.Page.Main, Start / Init, the top level), not by a user's action or an error");
        /// <summary>UX-SAF-6: "can't be undone" on a reversible action.</summary>
        public static readonly Rule UxSaf6 = Ux("SAF-6", "\"Can't be undone\" / \"permanently\" in the question of a reversible action (Archive, Hide, Disable, Suspend, Move ...)");
        /// <summary>UX-ACC-4: a status line that is no live region.</summary>
        public static readonly Rule UxAcc4 = Ux("ACC-4", "A status / message / result element whose text is changed by code, without role=\"status\" / aria-live (or Sw.Page.Announce): screen readers never hear the result");
        /// <summary>UX-ACC-7: a time limit without a warning.</summary>
        public static readonly Rule UxAcc7 = Ux("ACC-7", "A page timer of a minute or more that signs out, leaves or clears the user's work without a warning and a way to extend (Sw.Session.WarnTimeOut)");
        /// <summary>UX-FRM-2: a required field that is not marked.</summary>
        public static readonly Rule UxFrm2 = Ux("FRM-2", "A field the page checks as required (SetError(\"... required\")) whose options have no Required: no marker, no aria-required");
        /// <summary>UX-ACC-2: colour alone.</summary>
        public static readonly Rule UxAcc2 = Ux("ACC-2", "A state shown by colour alone: style.color chosen by a condition on an element whose text is only a symbol (or nothing)");
        /// <summary>UX-FRM-8: a lock that a failure never releases.</summary>
        public static readonly Rule UxFrm8 = Ux("FRM-8", "x.Locked(true) ... x.Locked(false) around work without try / finally: a failure leaves the field locked for good");
        /// <summary>An icon name that is not in the icon set.</summary>
        public static readonly Rule DesignIcon = new Rule("design-icon", Severity.Warning, false, "An icon name (Icon: \"...\", Sw.Icon(\"...\")) that is not in the icon set (Sw.Icons.List()) nor registered by the app (Sw.Icons.Add): a dashed placeholder shows", true);

        /// <summary>
        /// All rules.
        /// </summary>
        public static readonly IReadOnlyList<Rule> All = new[]
        {
            FileNameDots, FolderNameDash, FolderNameDigit, FolderNameDot, MjsFile, Encoding, Syntax,
            TrNotLiteral, TrMemberCall, TrIdentifierSuffix, TrInText, TrNewline, TrMixedQuotes, TrFormat,
            TemplateVar, TemplateCss, TemplateLiteralVar,
            JsDocHeader, DebugMarkers,
            ExternalResource, ExternalRequest, RootRelativeUrl, EsModule, UnknownToken, CssImport, BrandUrl, FontFile, ElementCss, BodyOverflow, MissingInclude, ScriptOrder,
            LegacyGlobal, LegacyInclude, LegacyCompat, BundleOverlap, NoFramework,
            DesignLegacyPage, DesignPageHeader, DesignShadow, DesignGradient, DesignColor, DesignTokenAlias, DesignLegacyToken, DesignLegacyClass,
            DesignInlineStyle, A11yCustomControl, A11yName, DesignPageContainer, DesignPillFacts, DesignRadius, DesignFieldset, DesignViewport, DesignSwRoot,
            DesignMeterLevels, DesignConfirm, DesignConfirmOptions, DesignOwnUi, InternalId, DesignErrorTwice,
            ThemeInclude, TrUntranslated, TrAlias, A11yFocus, DesignFont, DesignNavigate,
            UxInp1, UxInp4, UxInp10, UxInp11, UxInp12, UxInp23, UxInp25, UxInp30, UxPop1, UxPop9, UxFoc4, UxFrm1, UxFrm3, UxFrm6,
            UxMot2, UxMot4, UxMot6, UxLay1, UxTip2, UxAcc3, UxAcc10, UxTxt12, UxTxt15,
            UxInp2, UxFbk2, UxFbk4, UxPop6, UxSaf1, UxSaf3, UxSaf10, UxSaf11, UxLst3, UxSec1, UxInp31, UxPop7, UxMot1, UxFbk7, UxFbk5, UxFbk9, UxSaf12,
            UxSec2, UxTxt20, UxTxt6, UxInp6, UxInp8, UxInp20, UxTxt17, UxSaf4, UxTxt30, UxFrm5,
            UxSaf2, UxFoc2, UxAcc11, UxMot3, UxLst4, UxFbk6, UxAcc5, UxInp24, UxLay8, UxMot7, UxInp21, UxTxt13, UxLay5,
            UxTip1, UxTip7, UxPop11, UxSaf6, UxAcc4, UxAcc7, UxFrm2, UxAcc2, UxFrm8, DesignIcon,
        };
    }

    /// <summary>
    /// A finding of the checker.
    /// </summary>
    public sealed class Finding
    {
        /// <summary>
        /// The path (relative to the checked folder, forward slashes).
        /// </summary>
        public String Path;
        /// <summary>
        /// The 1-based line (0 = not a text position, ex: a folder name).
        /// </summary>
        public int Line;
        /// <summary>
        /// The 1-based column.
        /// </summary>
        public int Col;
        /// <summary>
        /// The rule.
        /// </summary>
        public Rule Rule;
        /// <summary>
        /// The severity.
        /// </summary>
        public Severity Severity;
        /// <summary>
        /// The message.
        /// </summary>
        public String Message;

        /// <summary>
        /// Format as "path(line,col): rule: message" ("path: rule: message" without a position), warnings get a "warning: " message prefix.
        /// </summary>
        /// <param name="path">The path to print (default: <see cref="Path"/>)</param>
        /// <returns>The text</returns>
        public String Format(String path = null)
        {
            path ??= Path;
            var pos = Line > 0 ? ("(" + Line + "," + Col + ")") : "";
            return path + pos + ": " + Rule.Id + ": " + (Severity == Severity.Warning ? "warning: " : "") + Message;
        }

        /// <summary>
        /// The formatted text.
        /// </summary>
        public override string ToString() => Format();
    }
}
