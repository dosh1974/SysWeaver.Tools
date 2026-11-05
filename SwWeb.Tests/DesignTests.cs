using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    /// <summary>
    /// The Design v2 porting rules (check --design, migrate --level design), the design token renames and the Table.addTable conversion.
    /// </summary>
    [Collection("Console")]
    public class DesignTests
    {
        const String Header = "/** @file test */\r\n";

        static readonly MigrationMap Map = MigrationMap.Load();

        static CheckOptions Opt(bool design = true) => new CheckOptions { Design = design, Map = Map };

        static List<Finding> Css(String code, String rel = "page/x.css", bool design = true)
        {
            var f = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext(rel, new SourceText(code), Opt(design), f));
            return f;
        }

        static List<Finding> Js(String code, String rel = "page/x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), Opt(), f));
            return f;
        }

        static List<Finding> Html(String code, String rel = "page/x.html")
        {
            var f = new List<Finding>();
            Checker.CheckHtmlFile(new Checker.FileContext(rel, new SourceText(code), Opt(), f));
            return f;
        }

        //  The design rules (the UX guide rules, also design rules, have their own tests: UxRuleTests)
        static String[] Ids(List<Finding> f) => f.Where(x => x.Rule.Guide == null).Select(x => x.Rule.Id).ToArray();

        static MigrateResult Migrate(TestFolder f, MigrateLevel level = MigrateLevel.Api) =>
            Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = false, Level = level });

        // ---- rules ----

        [Fact]
        public void DesignRulesAreListedAndOptIn()
        {
            var design = Rule.All.Where(r => r.Design && (r.Guide == null)).Select(r => r.Id).ToArray();
            Assert.Equal(new[] { "design-legacy-page", "design-page-header", "design-shadow", "design-gradient", "design-color", "design-token-alias", "design-legacy-token", "design-legacy-class", "design-inline-style", "a11y-custom-control", "a11y-name", "design-page-container", "design-pill-facts", "design-radius", "design-fieldset", "design-viewport", "design-sw-root", "design-meter-levels", "design-confirm", "design-confirm-options", "design-own-ui", "design-error-twice", "a11y-focus", "design-font", "design-navigate", "design-icon" }, design);
            Assert.All(Rule.All.Where(r => r.Design), r => Assert.Equal(Severity.Warning, r.Severity));
            //  Without --design nothing is reported
            Assert.Empty(Css(".a { color: #fff; box-shadow: 0 4px 8px #000; background: var(--sw-bg-1); }", design: false));
            Assert.NotEmpty(Css(".a { color: #fff; box-shadow: 0 4px 8px #000; background: var(--sw-bg-1); }"));
        }

        /// <summary>FD S5b N2: an unknown option of Sw.Dialog.Confirm (the options form and the 4th argument) is reported.</summary>
        [Fact]
        public void ConfirmOptions()
        {
            static String[] Ids2(List<Finding> f) => f.Where(x => x.Rule.Id == "design-confirm-options").Select(x => x.Message).ToArray();
            //  The options form: Title, Text and OkText are fine there, a wrong name for the verb is reported (the button reads "OK")
            var m = Ids2(Js("Sw.Dialog.Confirm({ Title: _T(\"Clear?\", \"c\"), Text: \"x\", Confirm: _T(\"Clear view\", \"c\") });"));
            Assert.Single(m);
            Assert.Contains("unknown option \"Confirm\"", m[0]);
            Assert.Contains("the verb is OkText", m[0]);
            Assert.Contains("Title, Text, OkText", m[0]);
            Assert.Empty(Ids2(Js("Sw.Dialog.Confirm({ Title: \"a\", Text: \"b\", OkText: \"Clear\", Danger: true, \"Id\": \"x\", Owner });")));
            //  The 4th argument: Title / Text / OkText are not options there
            Assert.Single(Ids2(Js("Sw.Dialog.Confirm(\"a\", \"b\", \"Clear\", { Danger: true, OkText: \"Clear\" });")));
            Assert.Empty(Ids2(Js("Sw.Dialog.Confirm(\"a\", \"b\", \"Clear\", { Danger: true, CancelText: \"Keep\" });")));
            //  A spread or a computed key: the keys are not known, nothing is said; nested objects are not keys
            Assert.Empty(Ids2(Js("Sw.Dialog.Confirm({ ...o, Title: \"a\" });")));
            Assert.Empty(Ids2(Js("Sw.Dialog.Confirm({ [k]: 1, Title: \"a\" });")));
            Assert.Empty(Ids2(Js("Sw.Dialog.Confirm(\"a\", \"b\", \"Go\", { Owner: { Foo: 1 }, Target: f({ Bar: 2 }) });")));
            //  Only with --design
            var plain = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext("page/x.js", new SourceText(Header + "Sw.Dialog.Confirm({ Confirm: \"x\" });"), Opt(false), plain));
            Assert.DoesNotContain(plain, x => x.Rule.Id == "design-confirm-options");
        }

        static String SysWeaverRoot([System.Runtime.CompilerServices.CallerFilePath] String file = null) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file), "..", "..", "SysWeaver"));

        /// <summary>The Confirm options of SwWeb are the run time schema of ui/overlay.js (one source of truth: they can't drift).</summary>
        [Fact]
        public void ConfirmOptionsMatchTheRunTimeSchema()
        {
            //  (the SysWeaver repository is a sibling of SysWeaver.Tools, as in RepositoryFixtureTests; nothing to compare without it)
            var path = System.IO.Path.Combine(SysWeaverRoot(), "SysWeaver.HttpServer", "srcWeb", "ui", "overlay.js");
            if (!System.IO.File.Exists(path))
                return;
            var src = System.IO.File.ReadAllText(path);
            var m = System.Text.RegularExpressions.Regex.Match(src, @"const ConfirmSchema = \{(?<b>[^}]*)\}");
            Assert.True(m.Success, "ConfirmSchema not found in ui/overlay.js");
            var names = System.Text.RegularExpressions.Regex.Matches(m.Groups["b"].Value, @"(\w+)\s*:").Select(x => x.Groups[1].Value).ToArray();
            Assert.Equal(UxChecker.ConfirmOptions.ToArray(), names);
            Assert.Contains("\"Sw.Dialog.Confirm({...})\": Object.assign({ Title: \"string\", Text: \"string\", OkText: \"string\" }, ConfirmSchema)", src);
        }

        [Fact]
        public void FrameworkFilesAreNotChecked()
        {
            foreach (var rel in new[] { "ui/x.css", "core/x.css", "shell/x.css", "sw/ui.css", "common/common.css", "brand/fonts.css", "app/application.css" })
                Assert.Empty(Css(".a { color: #fff; box-shadow: 0 4px 8px #000; }", rel));
        }

        [Fact]
        public void AllowMarker()
        {
            Assert.Empty(Css(".swatch { background: #f00; } /* SwWeb:allow-design (shows the real colour) */"));
            Assert.Empty(Css("/* SwWeb:allow-design */\n.swatch { background: #f00; }"));
            Assert.Single(Css(".swatch { background: #f00; }\n\n.x { }"));
        }

        // ---- tokens ----

        [Theory]
        [InlineData(".a { color: var(--sw-muted); }", "design-token-alias")]
        [InlineData(".a { border: 1px solid var(--sw-line, #ccc); }", "design-token-alias")]
        [InlineData(".a { box-shadow: var(--sw-shadow); }", "design-token-alias")]
        [InlineData(".a { --sw-touch: 44px; }", "design-legacy-token")]
        [InlineData(".a { color: var(--ThemeAcc1); }", "design-legacy-token")]
        [InlineData(".a { background: rgba(var(--ThemeMainRGB), 0.1); }", "design-legacy-token")]
        [InlineData(".a { border-radius: var(--ThemeRad); }", "design-legacy-token")]
        [InlineData(".a { --my: var(--ThemeMain50); }", "design-legacy-token")]
        public void Tokens(String css, String rule)
        {
            var f = Css(css);
            Assert.Equal(new[] { rule }, Ids(f));
        }

        [Fact]
        public void TokenSuggestionsDependOnTheProperty()
        {
            Assert.Contains("--sw-text-accent", Css(".a { color: var(--ThemeAcc1); }")[0].Message);
            Assert.Contains("--sw-canvas", Css(".a { background-color: var(--ThemeBackground); }")[0].Message);
            Assert.Contains("--sw-border", Css(".a { border-color: var(--ThemeMain); }")[0].Message);
            Assert.Contains("--sw-control-h", Css(".a { --sw-touch: 44px; }")[0].Message);
            Assert.Contains("color-mix(in srgb, var(--sw-fg) 50%, transparent)", Css(".a { color: var(--ThemeMain50); }")[0].Message);
        }

        [Fact]
        public void ThemeDefinitionsAreFine()
        {
            //  Defining the base variables (an app theme in app/app.css) and own tokens is fine
            Assert.Empty(Css(":root { --ThemeAcc1: #c2185b; --ThemeBackground: #101418; color-scheme: dark; --acme-badge: #4fc3f7; }", "app/app.css"));
        }

        // ---- shadows ----

        [Theory]
        [InlineData(".a { box-shadow: var(--sw-elev-1); }")]
        [InlineData(".a { box-shadow: var(--sw-elev-2), var(--sw-focus-ring); }")]
        [InlineData(".a { box-shadow: 0 0 0 2px var(--sw-focus); }")]
        [InlineData(".a { box-shadow: inset 0 0 0 1px var(--sw-border); }")]
        [InlineData(".a { box-shadow: inset 0 -1px 0 var(--sw-border); }")]
        [InlineData(".a { box-shadow: inset 4px 0 0 var(--sw-danger); }")]
        [InlineData(".a { box-shadow: none !important; }")]
        [InlineData(".a { box-shadow: var(--app-shadow); }")]
        [InlineData(".a { transition: box-shadow 0.2s; }")]
        [InlineData(".a { box-shadow: inset 0 -32px 20px -20px transparent; }")]
        public void FineShadows(String css)
        {
            Assert.DoesNotContain("design-shadow", Ids(Css(css)));
        }

        [Theory]
        [InlineData(".a { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35); }", "flat")]
        [InlineData(".a { box-shadow: 0 0 8px var(--sw-accent1-soft); }", "flat")]
        [InlineData(".a { box-shadow: inset 0 1em 1em -1em var(--sw-fg); }", "flat")]
        [InlineData(".a { box-shadow: 0 0 32px var(--sw-shadow); }", "invalid")]
        [InlineData(".a { box-shadow: -4px 0 6px -4px var(--sw-shadow, rgba(0, 0, 0, 0.35)); }", "invalid")]
        [InlineData(".a { text-shadow: 0 0 4px var(--sw-accent1); }", "glow")]
        [InlineData(".a { filter: drop-shadow(0 0 5px var(--sw-accent1)); }", "drop shadow")]
        public void BadShadows(String css, String text)
        {
            var f = Css(css).Where(x => x.Rule == Rule.DesignShadow).ToList();
            Assert.Single(f);
            Assert.Contains(text, f[0].Message);
        }

        [Fact]
        public void ShadowColourIsOneFinding()
        {
            //  The colour of a reported shadow is not reported again
            Assert.Equal(new[] { "design-shadow" }, Ids(Css(".a { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35); }")));
            //  ... but a fine ring with a hard-coded colour is
            Assert.Equal(new[] { "design-color" }, Ids(Css(".a { box-shadow: 0 0 0 2px #00f; }")));
        }

        // ---- gradients ----

        [Theory]
        [InlineData(".a { background: linear-gradient(90deg, transparent, var(--sw-accent1), transparent); }", true)]
        [InlineData(".a { background-image: radial-gradient(circle, var(--sw-accent1), transparent); }", true)]
        [InlineData(".a { border-image: conic-gradient(red, blue) 1; }", true)]
        [InlineData(".a { background: repeating-linear-gradient(45deg, transparent 0 4px, var(--sw-border) 4px 6px); }", false)]
        [InlineData(".a { mask-image: linear-gradient(to right, #000, transparent); }", false)]
        [InlineData(".a { -webkit-mask: linear-gradient(#000, transparent); }", false)]
        [InlineData(".a { background-image: linear-gradient(45deg, var(--d) 25%, transparent 25%, transparent 75%, var(--d) 75%); }", false)]
        [InlineData(".a { background-image: linear-gradient(rgba(0, 0, 0, 0.06) 1px, transparent 1px), linear-gradient(90deg, rgba(0, 0, 0, 0.06) 1px, transparent 1px); }", false)]
        [InlineData(".a { background: linear-gradient(to right, transparent, var(--sw-bg) 2em); }", true)]
        public void Gradients(String css, bool reported)
        {
            Assert.Equal(reported, Ids(Css(css)).Contains("design-gradient"));
        }

        // ---- colours ----

        [Theory]
        [InlineData(".a { color: #fff; }", "#fff")]
        [InlineData(".a { background-color: #1D2A33AA; }", "#1D2A33AA")]
        [InlineData(".a { border: 1px solid red; }", "red")]
        [InlineData(".a { color: rgb(0 0 0 / 0.5); }", "rgb(")]
        [InlineData(".a { background: hsl(200, 50%, 40%); }", "hsl(")]
        [InlineData(".a { background: color-mix(in srgb, #fff 20%, transparent); }", "#fff")]
        [InlineData(".a { outline: 2px solid white; }", "white")]
        public void HardCodedColours(String css, String what)
        {
            var f = Css(css).Where(x => x.Rule == Rule.DesignColor).ToList();
            Assert.Single(f);
            Assert.Contains("\"" + what, f[0].Message);
        }

        [Theory]
        [InlineData(".a { color: var(--sw-text); }")]
        [InlineData(".a { color: var(--sw-text, #000); }")]
        [InlineData(".a { --brand: #4fc3f7; }")]
        [InlineData(".a { color: transparent; background: currentColor; }")]
        [InlineData(".a { transition: color 1s, background-color 1s; }")]
        [InlineData(".a { grid-area: red; }")]
        [InlineData(".a { background: url(\"data:image/svg+xml;utf8,<svg fill='%23fff'/>\"); }")]
        [InlineData(".a { background: url(#fff); }")]
        [InlineData(".a { color: color-mix(in srgb, var(--sw-fg) 20%, transparent); }")]
        [InlineData("@media (forced-colors: active) { .a { color: Highlight; border-color: red; } }")]
        [InlineData("@font-face { font-family: x; src: url(x.woff2); }")]
        [InlineData(".a { font: 500 1rem/1.4 var(--sw-sans); }")]
        public void NotHardCodedColours(String css)
        {
            Assert.DoesNotContain("design-color", Ids(Css(css)));
        }

        [Fact]
        public void ColourSuggestions()
        {
            Assert.Contains("--sw-text-2", Css(".a { color: #777; }")[0].Message);
            Assert.Contains("--sw-surface", Css(".a { background: #fafafa; }")[0].Message);
            Assert.Contains("--sw-border", Css(".a { border-color: #ddd; }")[0].Message);
            Assert.Contains("--sw-scrim", Css(".a { background: rgba(0, 0, 0, 0.5); }")[0].Message);
        }

        [Fact]
        public void NestedRulesAndMedia()
        {
            var f = Css("@media (max-width: 500px) { .a { color: #fff; } }\n.b { &:hover { background: #000; } }\n@supports (display: grid) { .c { box-shadow: 0 2px 4px var(--sw-fg); } }");
            Assert.Equal(new[] { "design-color", "design-color", "design-shadow" }, Ids(f));
        }

        // ---- legacy class hooks ----

        [Fact]
        public void LegacyClassHooks()
        {
            var f = Css("SysWeaver-Button { color: var(--sw-text); }\n.KeyClick:focus { outline: none; box-shadow: var(--sw-focus-ring); }\n.sw-button { }");
            Assert.Equal(new[] { "design-legacy-class", "design-legacy-class" }, Ids(f));
            Assert.Contains(".sw-button", f[0].Message);
        }

        [Theory]
        [InlineData(".sw-table > .sw-table-scroll { max-height: 50vh; }", "sw-table-area")]
        [InlineData(".my .sw-table-page>.sw-table-empty { padding: 0; }", "sw-table-area")]
        [InlineData(".sw-table-foot > .sw-table-search { width: 10em; }", "tool bar")]
        [InlineData(".sw-table-tools { gap: 0; }", ".sw-table-ttools")]
        public void ChangedTableStructure(String css, String text)
        {
            var f = Css(css);
            Assert.Equal(new[] { "design-legacy-class" }, Ids(f));
            Assert.Contains(text, f[0].Message);
            Assert.Empty(Css(".sw-table-area > .sw-table-scroll { max-height: 50vh; }"));
        }

        // ---- radii, pills, fieldsets ----

        [Theory]
        [InlineData(".a { border-radius: 12px; }", true)]
        [InlineData(".a { border-radius: 0.5rem 0.5rem 0 0; }", true)]
        [InlineData(".a { border-top-left-radius: 999px; }", true)]
        [InlineData(".a { border-radius: 0; }", false)]
        [InlineData(".a { border-radius: 0px; }", false)]
        [InlineData(".a { border-radius: 50%; }", false)]
        [InlineData(".a { border-radius: var(--sw-radius-md); }", false)]
        [InlineData(".a { border-radius: var(--sw-radius, 6px); }", false)]
        [InlineData(".a { border-radius: calc(var(--sw-radius) - 2px); }", false)]
        [InlineData(".a { border-radius: calc(var(--x) + 2px); }", true)]
        public void Radius(String css, bool reported)
        {
            Assert.Equal(reported, Ids(Css(css)).Contains("design-radius"));
        }

        [Fact]
        public void PillFacts()
        {
            var f = Css(".app-stat { display: inline-flex; gap: 4px; border: 1px solid var(--sw-border); border-radius: var(--sw-radius-pill); }\n.home .home-pill:hover { display: flex; background: var(--sw-surface); }\n.sm-statusbar { display: flex; border: 1px solid var(--sw-border); }\n.x-fact { color: var(--sw-text); }");
            Assert.Equal(new[] { "design-pill-facts", "design-pill-facts" }, Ids(f));
            Assert.Contains("Sw.Facts", f[0].Message);
        }

        [Theory]
        [InlineData("<fieldset><legend>Network</legend></fieldset>", "html")]
        [InlineData("const g = el(\"fieldset.grp\", null, el(\"legend\", { text: t }));", "js")]
        [InlineData("const g = document.createElement(\"fieldset\");", "js")]
        public void Fieldsets(String code, String kind)
        {
            Assert.Equal(new[] { "design-fieldset" }, Ids(kind == "html" ? Html(code) : Js(code)));
        }

        // ---- JavaScript ----

        [Theory]
        [InlineData("x.style.boxShadow = \"0 2px 4px #000\";", "design-inline-style")]
        [InlineData("x.style.background = \"linear-gradient(red, blue)\";", "design-inline-style")]
        [InlineData("x.style.color = \"#ff0000\";", "design-inline-style")]
        [InlineData("x.setAttribute(\"style\", \"box-shadow: 0 0 8px red\");", "design-inline-style")]
        [InlineData("el(\"div\", { style: \"color: #fff; padding: 4px\" });", "design-inline-style")]
        [InlineData("el(\"span\", { style: \"color: var(--sw-muted)\" });", "design-token-alias")]
        [InlineData("const b = el(\"div.tile\", { on: { click: () => go() } });", "a11y-custom-control")]
        [InlineData("const b = Sw.el(\"li.item\", { text: t, onclick: go });", "a11y-custom-control")]
        public void JsFindings(String code, String rule)
        {
            Assert.Equal(new[] { rule }, Ids(Js(code)));
        }

        [Theory]
        [InlineData("x.style.boxShadow = \"none\";")]
        [InlineData("x.style.boxShadow = \"var(--sw-elev-1)\";")]
        [InlineData("x.style.color = \"var(--sw-text)\";")]
        [InlineData("x.style.width = \"100px\";")]
        [InlineData("const s = \"box-shadow: none\";")]
        [InlineData("const b = el(\"button.tile\", { on: { click: go } });")]
        [InlineData("const b = el(\"div.tile\", { attrs: { role: \"button\", tabindex: \"0\" }, on: { click: go } });")]
        [InlineData("const b = el(\"div.tile\", { on: { keydown: go } });")]
        [InlineData("const c = \"#fff\";")]
        public void JsFine(String code)
        {
            Assert.Empty(Js(code));
        }

        // ---- HTML ----

        [Theory]
        [InlineData("<div onclick=\"go()\">x</div>", "a11y-custom-control")]
        [InlineData("<span role=\"button\" onclick=\"go()\">x</span>", "a11y-custom-control")]
        [InlineData("<a onclick=\"go()\">x</a>", "a11y-custom-control")]
        [InlineData("<img src=\"x.png\">", "a11y-name")]
        [InlineData("<button><span class=\"IconClose\"></span></button>", "a11y-name")]
        [InlineData("<a href=\"x.html\"><span class=\"IconOpen\"></span></a>", "a11y-name")]
        [InlineData("<div style=\"box-shadow: 0 2px 8px var(--sw-fg)\"></div>", "design-shadow")]
        [InlineData("<div style=\"color: #333\"></div>", "design-color")]
        [InlineData("<p style=\"color: var(--sw-faint)\"></p>", "design-token-alias")]
        [InlineData("<style>.a { background: linear-gradient(var(--sw-surface), var(--sw-canvas)); }</style>", "design-gradient")]
        [InlineData("<script defer src=\"../common/common.js\"></script>", "design-legacy-page")]
        [InlineData("<script>x.style.boxShadow = '0 0 2px red';</script>", "design-inline-style")]
        public void HtmlFindings(String html, String rule)
        {
            Assert.Equal(new[] { rule }, Ids(Html(html)));
        }

        [Theory]
        [InlineData("<div role=\"button\" tabindex=\"0\" onclick=\"go()\">x</div>")]
        [InlineData("<button onclick=\"go()\">Save</button>")]
        [InlineData("<a href=\"#\" onclick=\"go()\">x</a>")]
        [InlineData("<img src=\"x.png\" alt=\"\">")]
        [InlineData("<img src=\"x.png\" aria-hidden=\"true\">")]
        [InlineData("<button aria-label=\"Close\"><span class=\"IconClose\"></span></button>")]
        [InlineData("<button title=\"Close\"><span class=\"IconClose\"></span></button>")]
        [InlineData("<button><span class=\"IconSave\"></span> Save</button>")]
        [InlineData("<button><img src=\"x.svg\" alt=\"Save\"></button>")]
        [InlineData("<button id=\"b\"></button>")]
        [InlineData("<body onclick=\"x()\"></body>")]
        public void HtmlFine(String html)
        {
            Assert.Empty(Html(html).Where(x => x.Rule.Guide == null));
        }

        // ---- page header (folder level) ----

        const String NewPage = "<!DOCTYPE html>\n<html>\n<head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../sw/ui.js\"></script>\n<script defer src=\"page.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n</head>\n<body class=\"sw-root\">\n</body>\n</html>\n";

        static List<Finding> CheckFolder(TestFolder f, bool design = true) =>
            Checker.CheckFolder(f.Root, Opt(design)).Findings.Where(x => x.Rule.Design).ToList();

        [Fact]
        public void PageHeaderMissing()
        {
            using var f = new TestFolder();
            f.Write("page/p.html", NewPage);
            f.Write("page/page.js", Header + "Sw.Page.Main(() => document.body.append(Sw.el(\"h1.title\", { text: \"x\" })));");
            var fd = CheckFolder(f);
            Assert.Equal(new[] { "design-page-header" }, Ids(fd));
            Assert.Equal("page/p.html", fd[0].Path);
            Assert.Equal(10, fd[0].Line);
            Assert.Contains("own <h1>", fd[0].Message);
            Assert.Contains("sw-page-form", fd[0].Message);
            Assert.Empty(CheckFolder(f, false));
        }

        [Theory]
        [InlineData("const h = Sw.PageHeader({ Title: \"x\" }); document.body.append(Sw.el(\"main.sw-page-form\", null, h.E));")]
        [InlineData("document.body.innerHTML = '<main class=\"sw-page-read\"><header class=\"sw-ph\"><h1 class=\"sw-ph-title\">x</h1></header></main>';")]
        [InlineData("Sw.Page.Main(() => Sw.Table.Create({ Url: \"../Api/X\", Page: true }));")]
        [InlineData("Sw.Page.Main(() => Sw.PageLayout({ Type: \"form\", Title: \"x\" }));")]
        public void PageHeaderPresent(String js)
        {
            using var f = new TestFolder();
            f.Write("page/p.html", NewPage);
            f.Write("page/page.js", Header + js);
            Assert.Empty(CheckFolder(f));
        }

        [Fact]
        public void PageContainerMissing()
        {
            using var f = new TestFolder();
            f.Write("page/p.html", NewPage);
            f.Write("page/page.js", Header + "document.body.append(Sw.PageHeader({ Title: \"x\" }).E);");
            Assert.Equal(new[] { "design-page-container" }, Ids(CheckFolder(f)));
            f.Write("page/page.js", Header + "document.body.append(Sw.el(\"main.sw-page-form\", null, Sw.PageHeader({ Title: \"x\" }).E));");
            Assert.Empty(CheckFolder(f));
        }

        [Fact]
        public void PageHeaderExemptions()
        {
            using var f = new TestFolder();
            f.Write("page/page.js", Header + "x();");
            //  marked, the shell, core only pages and old format pages are not reported
            f.Write("page/a.html", NewPage.Replace("<body", "<!-- SwWeb:no-page-header (an embedded frame) -->\n<body"));
            f.Write("page/b.html", NewPage.Replace("<script defer src=\"page.js\">", "<script defer src=\"../sw/shell.js\">"));
            f.Write("page/c.html", NewPage.Replace("<link rel=\"stylesheet\" href=\"../sw/ui.css\">", ""));
            //  the auth page library builds the layout (Sw.AuthUi.Page), also when it comes from another project
            f.Write("auth/d.html", NewPage.Replace("<script defer src=\"page.js\">", "<script defer src=\"AuthUi.js\">"));
            f.Write("page/e.html", NewPage.Replace("<script defer src=\"page.js\">", "<script defer src=\"../auth/AuthUi.js\">"));
            Assert.Empty(CheckFolder(f));
        }

        // ---- migrate: design token renames ----

        [Fact]
        public void MigrateRenamesAliasTokens()
        {
            using var f = new TestFolder();
            f.Write("page/x.css", "/* var(--sw-muted) in a comment */\n.a { color: var(--sw-muted); border: 1px solid var(--sw-line, #ccc); box-shadow: var(--sw-shadow); }\n.b { min-height: var(--sw-touch); transition: background var(--sw-speed); background: var(--sw-bg-1); }\n");
            f.Write("page/x.js", "const s = \"color: var(--sw-faint)\"; const t = `background: var(--sw-bg-2)`; const v = muted; // var(--sw-muted)\n");
            f.Write("page/p.html", "<html><head><style>.c { color: var(--sw-muted); }</style></head><body><p style=\"color: var(--sw-muted)\">x</p></body></html>\n");
            var r = Migrate(f);
            Assert.Equal("/* var(--sw-muted) in a comment */\n.a { color: var(--sw-text-2); border: 1px solid var(--sw-border, #ccc); box-shadow: var(--sw-elev-1); }\n.b { min-height: var(--sw-control-h); transition: background var(--sw-dur-2); background: var(--sw-surface); }\n", f.Read("page/x.css"));
            Assert.Equal("const s = \"color: var(--sw-text-3)\"; const t = `background: var(--sw-surface-sunken)`; const v = muted; // var(--sw-muted)\n", f.Read("page/x.js"));
            Assert.Equal("<html><head><style>.c { color: var(--sw-text-2); }</style></head><body><p style=\"color: var(--sw-text-2)\">x</p></body></html>\n", f.Read("page/p.html"));
            Assert.Equal(0, r.Items.Count(x => (x.Kind == MigrateItemKind.Warning) && !x.Message.StartsWith("no-framework", StringComparison.Ordinal)));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Change && x.Message.StartsWith("var(--sw-muted) -> var(--sw-text-2)", StringComparison.Ordinal));
            //  idempotent
            var r2 = Migrate(f);
            Assert.Empty(r2.ChangedFiles);
        }

        [Fact]
        public void MigrateKeepsOverriddenAliases()
        {
            //  An override of an alias anywhere in the run: its uses read the override, the new name wouldn't
            using var f = new TestFolder();
            f.Write("a/a.css", ".big { --sw-touch: 52px; }\n.big .b { min-height: var(--sw-touch); color: var(--sw-muted); }\n");
            f.Write("b/b.css", ".c { height: var(--sw-touch); }\n");
            Migrate(f);
            Assert.Equal(".big { --sw-touch: 52px; }\n.big .b { min-height: var(--sw-touch); color: var(--sw-text-2); }\n", f.Read("a/a.css"));
            Assert.Equal(".c { height: var(--sw-touch); }\n", f.Read("b/b.css"));
        }

        [Fact]
        public void MigrateIncludesLevelDoesntRenameTokens()
        {
            using var f = new TestFolder();
            f.Write("page/x.css", ".a { color: var(--sw-muted); }\n");
            Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, Level = MigrateLevel.Includes });
            Assert.Equal(".a { color: var(--sw-muted); }\n", f.Read("page/x.css"));
        }

        [Fact]
        public void MigrateDesignFixesInvalidElevationShadows()
        {
            const String Src = ".a { box-shadow: 0 0 32px var(--sw-shadow); }\n.b { box-shadow: -4px 0 6px -4px var(--sw-shadow, rgba(0, 0, 0, 0.35)) !important; }\n.c { box-shadow: var(--sw-shadow); }\n";
            using var f = new TestFolder();
            f.Write("page/x.css", Src);
            f.Write("page/p.html", "<div style=\"box-shadow: 0 2px 8px var(--sw-elev-1)\"></div>\n");
            //  api: only the alias renames
            Migrate(f);
            Assert.Equal(".a { box-shadow: 0 0 32px var(--sw-elev-1); }\n.b { box-shadow: -4px 0 6px -4px var(--sw-elev-1, rgba(0, 0, 0, 0.35)) !important; }\n.c { box-shadow: var(--sw-elev-1); }\n", f.Read("page/x.css"));
            //  design: the invalid ones become none (no overlapping edits, no warning for them)
            f.Write("page/x.css", Src);
            var r = Migrate(f, MigrateLevel.Design);
            Assert.Equal(".a { box-shadow: none; }\n.b { box-shadow: none !important; }\n.c { box-shadow: var(--sw-elev-1); }\n", f.Read("page/x.css"));
            Assert.Equal("<div style=\"box-shadow: none\"></div>\n", f.Read("page/p.html"));
            Assert.Equal(0, r.Warnings);
            Assert.Empty(Migrate(f, MigrateLevel.Design).ChangedFiles);
        }

        [Fact]
        public void LibraryPublishingItsAliasIsNotAWarning()
        {
            using var f = new TestFolder();
            f.Write("edit/Edit.js", "(() => { const Edit = {}; Sw.Edit = Edit; if (!window.Edit) window.Edit = Edit; })();");
            f.Write("page/x.js", "const e = window.Edit;");
            var r = Migrate(f);
            Assert.DoesNotContain(r.Items, x => x.Path == "edit/Edit.js" && x.Kind == MigrateItemKind.Warning);
            Assert.Single(r.Items, x => x.Path == "edit/Edit.js" && x.Kind == MigrateItemKind.Info && x.Message.Contains("defines the legacy alias \"window.Edit\""));
            //  a page reading it is still a follow-up
            Assert.Contains(r.Items, x => x.Path == "page/x.js" && x.Kind == MigrateItemKind.Warning);
        }

        [Fact]
        public void LegacyCompatCssIsNotMigrated()
        {
            using var f = new TestFolder();
            f.Write("common/common.css", "SysWeaver-Button { color: var(--sw-muted); }\n");
            var r = Migrate(f, MigrateLevel.Design);
            Assert.Empty(r.ChangedFiles);
            Assert.Equal(0, r.Warnings);
        }

        // ---- migrate: Table.addTable ----

        [Theory]
        [InlineData("Table.addTable(url, el);", "Sw.Table.Create({ Url: url, Element: el });")]
        [InlineData("Table.addTable(url, el, 20, p);", "Sw.Table.Create({ Url: url, Element: el, Param: p });")]
        [InlineData("Table.addTable(url, el, 20);", "Sw.Table.Create({ Url: url, Element: el });")]
        [InlineData("Table.addTable(url, el, -1, p, () => x(), \"Name\");", "Sw.Table.Create({ Url: url, Element: el, Param: p, OnFirstLoad: () => x(), Name: \"Name\" });")]
        [InlineData("Table.addTable(url, el, rows, null, null, null, s, f, flt);", "Sw.Table.Create({ Url: url, Element: el, Param: null, OnFirstLoad: null, Name: null, State: s, OnFirstData: f, Filter: flt });")]
        [InlineData("Table.addTable(url);", "Sw.Table.Create({ Url: url });")]
        [InlineData("const t = await Table.addTable(\"../Api/X\", document.body, undefined, { Id: 1 });", "const t = await Sw.Table.Create({ Url: \"../Api/X\", Element: document.body, Param: { Id: 1 } });")]
        public void AddTableToOptions(String code, String expected)
        {
            using var f = new TestFolder();
            f.Write("page/x.js", code);
            var r = Migrate(f);
            Assert.Equal(expected, f.Read("page/x.js"));
            Assert.Equal(0, r.Warnings);
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Change && x.Message.Contains("rowsPerPage"));
            //  idempotent
            Assert.Empty(Migrate(f).ChangedFiles);
        }

        [Fact]
        public void AddTableWithASideEffectInTheDroppedArgumentIsReported()
        {
            using var f = new TestFolder();
            f.Write("page/x.js", "Table.addTable(url, el, rows(), p);");
            var r = Migrate(f);
            Assert.Equal("Table.addTable(url, el, rows(), p);", f.Read("page/x.js"));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Warning && x.Message.Contains("argument 3 is ignored by the new API"));
        }

        [Fact]
        public void DropOptionInAMapFile()
        {
            var m = MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Globals\": { \"oldF\": { \"New\": \"Sw.F\", \"Positional\": 1, \"Options\": [ \"-\", \"B\" ] } } } } }");
            using var f = new TestFolder();
            f.Write("x.js", "oldF(a, 1, b); oldF(a, 2); oldF(a);");
            Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = m, JsDocHeaders = false });
            Assert.Equal("Sw.F(a, { B: b }); Sw.F(a); Sw.F(a);", f.Read("x.js"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Globals\": { \"oldF\": { \"New\": \"Sw.F\", \"Options\": [ \"a-b\" ] } } } } }"));
        }

        [Fact]
        public void TokenMapValidation()
        {
            var m = MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Tokens\": { \"--old-a\": \"--new-a\", \"--old-b\": { \"New\": \"--new-b\", \"Safe\": true, \"Note\": \"n\" } } } } }");
            Assert.False(m.Tokens["--old-a"].Safe);
            Assert.True(m.Tokens["--old-b"].Safe);
            Assert.Equal("--new-b", m.Tokens["--old-b"].New);
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Tokens\": { \"old\": \"--new\" } } } }"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Tokens\": { \"--old\": { \"New\": \"use x\", \"Safe\": true } } } } }"));
            //  the default map: every safe entry is an alias that ui/tokens.css defines
            Assert.All(Map.Tokens.Values.Where(x => x.Safe), x => Assert.StartsWith("--sw-", x.New, StringComparison.Ordinal));
            Assert.Equal("--sw-surface", Map.Tokens["--sw-bg-1"].New);
            Assert.DoesNotContain("--sw-gap", Map.Tokens.Keys);
        }

        // ---- migrate --level design ----

        [Fact]
        public void MigrateDesignLevelReportsManualFollowUps()
        {
            using var f = new TestFolder();
            f.Write("page/p.html", NewPage);
            f.Write("page/page.js", Header + "x.style.boxShadow = \"0 2px 4px #000\";");
            f.Write("page/x.css", ".a { color: var(--sw-muted); box-shadow: 0 4px 12px var(--sw-fg); }\n");
            var api = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = false, DryRun = true });
            Assert.Equal(0, api.Warnings);
            var r = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = false, DryRun = true, Level = MigrateLevel.Design });
            var w = r.Items.Where(x => x.Kind == MigrateItemKind.Warning).Select(x => x.Path + ": " + x.Message.Split(':')[1].Trim()).ToArray();
            Assert.Equal(new[] { "page/p.html: design-page-header", "page/page.js: design-inline-style", "page/x.css: design-shadow" }, w);
            //  the alias use is a change, not a warning
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Change && x.Message.StartsWith("var(--sw-muted)", StringComparison.Ordinal));
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "migrate", f.Root, "--dry-run", "--level", "design", "--quiet" }));
            Assert.Equal(SwWebProgram.ExitInvalidArguments, SwWebProgram.Run(new[] { "migrate", f.Root, "--dry-run", "--level", "fancy" }));
        }

        [Fact]
        public void CheckCommandLine()
        {
            using var f = new TestFolder();
            f.Write("page/x.css", ".a { color: #fff; }\n");
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "check", f.Root, "--design", "--quiet" }));
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "check", f.Root, "--design", "--strict", "--quiet" }));
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "check", f.Root, "--strict", "--quiet" }));
        }

        // ---- viewport, old theme colour classes ----

        [Fact]
        public void ViewportMissingOnUiPages()
        {
            Assert.Equal(new[] { "design-viewport" }, Ids(Html("<html><head><meta charset=\"utf-8\"><script src=\"../sw/core.js\"></script></head><body></body></html>")));
            Assert.Equal(new[] { "design-legacy-page", "design-viewport" }, Ids(Html("<html><head><script src=\"../common/common.js\"></script></head><body></body></html>")));
            //  Has one, or not a UI page (a mail template, a fragment)
            Assert.Empty(Html("<html><head><meta name=\"viewport\" content=\"width=device-width\"><script src=\"../sw/core.js\"></script></head><body></body></html>"));
            Assert.Empty(Html("<html><head><title>Mail</title></head><body><p>Hi</p></body></html>"));
            Assert.Empty(Html("<script src=\"../sw/core.js\"></script><div></div>"));
        }

        [Fact]
        public void ViewportAddedByDesignMigrate()
        {
            const String Src = "<!doctype html>\r\n<html>\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <title>X</title>\r\n    <script src=\"../sw/core.js\"></script>\r\n</head>\r\n<body></body>\r\n</html>\r\n";
            using var f = new TestFolder();
            f.Write("page/p.html", Src);
            //  api: unchanged
            Migrate(f);
            Assert.Equal(Src, f.Read("page/p.html"));
            var r = Migrate(f, MigrateLevel.Design);
            Assert.Equal(Src.Replace("<meta charset=\"utf-8\">\r\n", "<meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"), f.Read("page/p.html"));
            Assert.Equal(0, r.Warnings);
            Assert.Empty(Migrate(f, MigrateLevel.Design).ChangedFiles);
            //  No charset: after <head>
            f.Write("page/p.html", "<html><head><script src=\"../sw/core.js\"></script></head><body></body></html>");
            Migrate(f, MigrateLevel.Design);
            Assert.Equal("<html><head>\r\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><script src=\"../sw/core.js\"></script></head><body></body></html>", f.Read("page/p.html"));
        }

        [Fact]
        public void OldThemeColourClasses()
        {
            var f = Html("<html><head><meta name=\"viewport\" content=\"x\"><script src=\"../sw/core.js\"></script></head><body class=\"ThemeBaseBg ThemeMainFg x\"><div class=\"ThemeAccFg MyThemeMainFg\"></div></body></html>");
            Assert.Equal(new[] { "design-legacy-class", "design-legacy-class", "design-legacy-class" }, Ids(f));
            Assert.Contains("does nothing", f[0].Message);
            Assert.Contains("sw/ui.css gives the body", f[1].Message);
            Assert.Contains("role tokens", f[2].Message);
        }
    }
}
