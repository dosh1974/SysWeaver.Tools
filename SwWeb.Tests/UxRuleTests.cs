using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// The UX guide rules (ux-*, check --design): each rule's findings, the code it leaves alone, the allow comment and the guide citation.
    /// </summary>
    public class UxRuleTests
    {
        const String Header = "/** @file test */\r\n";

        static CheckOptions Opt(bool design = true, bool framework = false) => new CheckOptions { Design = design, Framework = framework };

        static List<Finding> Css(String code, String rel = "page/x.css", bool design = true, bool framework = false)
        {
            var f = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext(rel, new SourceText(code), Opt(design, framework), f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static List<Finding> Js(String code, String rel = "page/x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), Opt(), f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static List<Finding> Html(String code, String rel = "page/x.html")
        {
            var f = new List<Finding>();
            Checker.CheckHtmlFile(new Checker.FileContext(rel, new SourceText(code), Opt(), f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static String[] Ids(List<Finding> f) => f.Select(x => x.Rule.Id).ToArray();

        [Fact]
        public void RulesAreListedCiteTheGuideAndAreDesignWarnings()
        {
            var ux = Rule.All.Where(r => r.Guide != null).ToList();
            Assert.Equal(72, ux.Count);
            Assert.All(ux, r =>
            {
                Assert.True(r.Design);
                Assert.Equal(Severity.Warning, r.Severity);
                Assert.Equal("ux-" + r.Guide.ToLowerInvariant(), r.Id);
                Assert.Contains("(UX guide " + r.Guide + ")", r.Description);
                Assert.Matches(@"^[A-Z]{3}-\d+$", r.Guide);
            });
            Assert.Equal(ux.Count, ux.Select(r => r.Id).Distinct().Count());
            //  Every message names the guide rule and its section
            var f = Css(".a:hover { color: red; }");
            Assert.EndsWith("(UX guide INP-11: _docs/UX_Guide.md#1-input-pointer-touch-and-keyboard)", Assert.Single(f).Message);
            Assert.EndsWith("_docs/UX_Guide.md#83-css-no-translation-not-supported)", Assert.Single(Css(".a::after { content: \"New\"; }")).Message);
        }

        [Fact]
        public void OnlyWithDesignAndNotInTheFrameworkByDefault()
        {
            const String code = ".a:hover { color: red; }";
            Assert.Empty(Css(code, design: false));
            Assert.Single(Css(code));
            Assert.Empty(Css(code, "ui/x.css"));
            Assert.Single(Css(code, "ui/x.css", framework: true));
            //  Never the generated bundles
            Assert.Empty(Css(code, "sw/ui.css", framework: true));
        }

        [Fact]
        public void AllowComment()
        {
            Assert.Empty(Css(".a:hover { color: red; } /* SwWeb:allow ux-inp-11 (a drag handle) */"));
            Assert.Empty(Css("/* SwWeb:allow ux-mot-2 ux-inp-11 (why) */\n.a:hover { color: red; }"));
            //  Another rule's id, a prefix of the id, the design marker: still reported
            Assert.Single(Css(".a:hover { color: red; } /* SwWeb:allow ux-mot-2 */"));
            Assert.Single(Css(".a:hover { color: red; } /* SwWeb:allow ux-inp-1 */"));
            Assert.Single(Css(".a:hover { color: red; } /* SwWeb:allow-design */"));
            //  The rules that were design rules before keep the design marker
            Assert.Empty(Js("alert(\"x\"); // SwWeb:allow-design (a test page)\n"));
            Assert.Empty(Js("alert(\"x\"); // SwWeb:allow ux-pop-9 (a test page)\n"));
        }

        [Theory]
        //  INP-1
        [InlineData("b.addEventListener(\"pointerup\", go);", "ux-inp-1")]
        [InlineData("Sw.el(\"button\", { on: { mouseup: go } });", "ux-inp-1")]
        [InlineData("b.ontouchend = go;", "ux-inp-1")]
        //  INP-4
        [InlineData("document.addEventListener(\"contextmenu\", e => e.preventDefault());", "ux-inp-4")]
        [InlineData("document.oncontextmenu = () => false;", "ux-inp-4")]
        //  INP-10
        [InlineData("document.body.style.cursor = \"progress\";", "ux-inp-10")]
        [InlineData("x.style.cursor = \"wait\";", "ux-inp-10")]
        [InlineData("x.style.cssText = \"cursor: wait\";", "ux-inp-10")]
        //  INP-12
        [InlineData("saveButton.disabled = true;", "ux-inp-12")]
        [InlineData("this.okBtn.disabled = !ok;", "ux-inp-12")]
        [InlineData("btn.setAttribute(\"disabled\", \"\");", "ux-inp-12")]
        //  INP-23
        [InlineData("if (e.altKey && e.key === \"ArrowLeft\") back();", "ux-inp-23")]
        //  INP-25
        [InlineData("m.content = \"width=device-width, initial-scale=1, maximum-scale=1\";", "ux-inp-25")]
        [InlineData("m.setAttribute(\"content\", \"width=device-width, user-scalable=no\");", "ux-inp-25")]
        //  INP-30
        [InlineData("x.addEventListener(\"selectstart\", e => e.preventDefault());", "ux-inp-30")]
        //  POP-9
        [InlineData("Sw.Dialog.Show({ Title: _TF(\"Done\", \"c\"), Buttons: [{ Text: _TF(\"OK\", \"c\") }] });", "ux-pop-9")]
        [InlineData("if (confirm(\"Delete?\")) go();", "ux-pop-9")]
        [InlineData("window[\"alert\"](\"x\");", "ux-pop-9")]
        //  POP-1
        [InlineData("dlg.showModal();", "ux-pop-1")]
        //  FOC-4
        [InlineData("x.tabIndex = 2;", "ux-foc-4")]
        [InlineData("x.setAttribute(\"tabindex\", \"3\");", "ux-foc-4")]
        [InlineData("Sw.el(\"div\", { attrs: { tabindex: \"1\" } });", "ux-foc-4")]
        //  FRM-3
        [InlineData("Sw.Fail(_TF(\"The name is required\", \"c\"));", "ux-frm-3")]
        [InlineData("Sw.Toast(_TF(\"Please enter a valid email\", \"c\"), \"error\");", "ux-frm-3")]
        [InlineData("Sw.Info(\"Port must be a number\");", "ux-frm-3")]
        //  MOT-2
        [InlineData("x.style.transition = \"opacity 200ms\";", "ux-mot-2")]
        [InlineData("x.style.cssText = \"transition: opacity .2s ease\";", "ux-mot-2")]
        [InlineData("x.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 300 });", "ux-mot-2")]
        [InlineData("x.addEventListener(\"transitionend\", done);", "ux-mot-2")]
        //  TIP-2
        [InlineData("Sw.Button({ Text: _TF(\"Save\", \"c\"), Title: _TF(\"Save\", \"c\") });", "ux-tip-2")]
        public void JsFindings(String code, String rule)
        {
            Assert.Equal(new[] { rule }, Ids(Js(code)));
        }

        [Theory]
        //  INP-1: a drag (press, move, release), a press-and-hold repeat (press, release), click
        [InlineData("h.addEventListener(\"pointerdown\", start); h.addEventListener(\"pointermove\", move); h.addEventListener(\"pointerup\", end);")]
        [InlineData("b.addEventListener(\"pointerdown\", start); b.addEventListener(\"pointerup\", stop);")]
        [InlineData("b.addEventListener(\"click\", go);")]
        //  INP-4: a menu of its own, conditional
        [InlineData("row.addEventListener(\"contextmenu\", e => { e.preventDefault(); openMenu(); });")]
        [InlineData("document.addEventListener(\"contextmenu\", e => { if (e.target.closest(\".x\")) e.preventDefault(); });")]
        //  INP-10: progress on a control
        [InlineData("b.style.cursor = \"progress\";")]
        //  INP-12: other elements, enabling
        [InlineData("input.disabled = true;")]
        [InlineData("saveButton.disabled = false;")]
        [InlineData("btn.removeAttribute(\"disabled\");")]
        [InlineData("btn.setAttribute(\"aria-disabled\", \"true\");")]
        //  INP-23: Alt excluded, other keys
        [InlineData("if (!e.altKey && e.key === \"ArrowLeft\") prev();")]
        [InlineData("if (e.altKey && e.key === \"ArrowUp\") up();")]
        [InlineData("if (e.altKey) return; if (e.key === \"ArrowLeft\") prev();")]
        //  INP-25
        [InlineData("m.content = \"width=device-width, initial-scale=1, maximum-scale=5\";")]
        //  POP-9: verbs, a status word, a parameter named confirm
        [InlineData("Sw.Dialog.Show({ Buttons: [{ Text: _TF(\"Save\", \"c\") }] });")]
        [InlineData("const s = _TF(\"OK\", \"Health status\");")]
        [InlineData("function f(confirm) { if (confirm()) go(); }")]
        //  FOC-4
        [InlineData("x.tabIndex = 0; y.tabIndex = -1; z.setAttribute(\"tabindex\", \"-1\");")]
        //  FRM-3: not validation
        [InlineData("Sw.Fail(_TF(\"The server could not be reached\", \"c\"));")]
        [InlineData("Sw.Toast(_TF(\"Saved\", \"c\"));")]
        //  MOT-2: tokens, a spinner, a fallback timer, zero
        [InlineData("x.style.transition = \"opacity var(--sw-dur-2) var(--sw-ease)\";")]
        [InlineData("x.style.animation = \"spin 1s linear infinite\";")]
        [InlineData("x.addEventListener(\"transitionend\", done); setTimeout(done, 300);")]
        [InlineData("x.style.transition = \"none 0s\";")]
        [InlineData("x.style.cssText = \"transition:opacity var(--sw-dur-1,90ms\" + \")\";")]
        //  TIP-2: a title that adds
        [InlineData("Sw.Button({ Text: _TF(\"Save\", \"c\"), Title: _TF(\"Save changes to the profile (Ctrl+S)\", \"c\") });")]
        [InlineData("Sw.Card({ Title: _TF(\"Save\", \"c\"), Content: Sw.Button({ Text: _TF(\"Save\", \"c\") }) });")]
        public void JsFine(String code)
        {
            Assert.Empty(Js(code));
        }

        [Theory]
        //  INP-10
        [InlineData(".a { cursor: wait; }", "ux-inp-10")]
        [InlineData("body.busy, html { cursor: progress; }", "ux-inp-10")]
        //  INP-11
        [InlineData(".a:hover { background: var(--sw-hover); }", "ux-inp-11")]
        [InlineData("@media (max-width: 760px) { .a:hover { color: red; } }", "ux-inp-11")]
        //  MOT-2
        [InlineData(".a { transition: opacity 200ms; }", "ux-mot-2")]
        [InlineData(".a { transition: opacity var(--sw-dur-1) cubic-bezier(0.2, 0, 0, 1); }", "ux-mot-2")]
        [InlineData(".a { animation-duration: .3s; }", "ux-mot-2")]
        [InlineData(".a { transition-delay: 1s; }", "ux-mot-2")]
        //  MOT-4
        [InlineData(".a { transition: width var(--sw-dur-2); }", "ux-mot-4")]
        [InlineData(".a { transition-property: max-height, opacity; }", "ux-mot-4")]
        [InlineData("@keyframes grow { from { height: 0; } to { height: 10px; } }", "ux-mot-4")]
        //  MOT-6
        [InlineData(".a { text-decoration: blink; }", "ux-mot-6")]
        //  TXT-15
        [InlineData(".req::after { content: \" (required)\"; }", "ux-txt-15")]
        [InlineData(".new::before { content: \"Neu\"; }", "ux-txt-15")]
        public void CssFindings(String code, String rule)
        {
            Assert.Equal(new[] { rule }, Ids(Css(code)));
        }

        [Theory]
        [InlineData("@media (hover: hover) { .a:hover { background: var(--sw-hover); } }")]
        [InlineData("@media (hover: hover) and (pointer: fine) { .a:hover { color: red; } }")]
        [InlineData("@media (any-hover: hover) { .a:hover { color: red; } }")]
        [InlineData(".a { cursor: progress; } .b { cursor: pointer; }")]
        [InlineData(".a { transition: opacity var(--sw-dur-1) var(--sw-ease), transform var(--sw-dur-2, 160ms); }")]
        [InlineData(".spin { animation: spin 0.8s linear infinite; }")]
        [InlineData(".spin { animation: spin 0.8s linear; animation-iteration-count: infinite; }")]
        [InlineData("@media (prefers-reduced-motion: reduce) { * { transition-duration: 1ms !important; } }")]
        [InlineData(".a { transition: none; transition-duration: 0s; }")]
        [InlineData(".a { transition: grid-template-rows var(--sw-dur-2) var(--sw-ease); }")]
        [InlineData("@keyframes fade { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; } }")]
        [InlineData(".a::after { content: \"\"; } .b::before { content: \"\\2014\"; } .c::after { content: \"\\e5cd\"; } .d::after { content: \"…\"; } .e::after { content: attr(data-x); } .f::after { content: \"x\"; }")]
        [InlineData(".a { --sw-x: 200ms; }")]
        [InlineData(".logo::before { content: var(--sw-brand-icon, url(\"../logo.svg\")); } .b::after { content: url(\"x.svg\") attr(title); }")]
        public void CssFine(String code)
        {
            Assert.Empty(Css(code));
        }

        [Theory]
        [InlineData("<div onmouseup=\"go()\">x</div>", "ux-inp-1")]
        [InlineData("<button onmousedown=\"go()\">Go</button>", "ux-inp-1")]
        [InlineData("<script defer src=\"../sw/core.js\"></script><body oncontextmenu=\"return false\"></body>", "ux-inp-4")]
        [InlineData("<div onselectstart=\"return false\"></div>", "ux-inp-30")]
        [InlineData("<button disabled>Save</button>", "ux-inp-12")]
        [InlineData("<input type=\"submit\" value=\"Send\" disabled>", "ux-inp-12")]
        [InlineData("<div tabindex=\"2\"></div>", "ux-foc-4")]
        [InlineData("<span translate=\"No\">SysWeaver</span>", "ux-txt-12")]
        [InlineData("<marquee>News</marquee>", "ux-mot-6")]
        [InlineData("<h1>A</h1><h1>B</h1>", "ux-acc-3")]
        [InlineData("<input type=\"text\" placeholder=\"Name\">", "ux-frm-1")]
        [InlineData("<label>Name</label><select id=\"s\"></select>", "ux-frm-1")]
        [InlineData("<label for=\"p\">Password</label><input type=\"password\" id=\"p\">", "ux-frm-6")]
        [InlineData("<img src=\"x.png\" alt=\"\">", "ux-lay-1")]
        [InlineData("<img src=\"x.png\" alt=\"\" width=\"20\">", "ux-lay-1")]
        [InlineData("<iframe src=\"a.html\" title=\"a\"></iframe>", "ux-lay-1")]
        [InlineData("<button title=\"Save\">Save</button>", "ux-tip-2")]
        [InlineData("<button aria-label=\"Store configuration\">Save</button>", "ux-acc-10")]
        [InlineData("<button>OK</button>", "ux-pop-9")]
        [InlineData("<dialog></dialog>", "ux-pop-1")]
        [InlineData("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, user-scalable=no\">", "ux-inp-25")]
        [InlineData("<div style=\"transition: opacity 300ms\"></div>", "ux-mot-2")]
        [InlineData("<script defer src=\"../sw/core.js\"></script><body style=\"cursor: progress\"></body>", "ux-inp-10")]
        [InlineData("<style>.a:hover { color: red; }</style>", "ux-inp-11")]
        [InlineData("<script>btn.disabled = true;</script>", "ux-inp-12")]
        public void HtmlFindings(String html, String rule)
        {
            Assert.Equal(new[] { rule }, Ids(Html(html)));
        }

        [Theory]
        [InlineData("<div onmousedown=\"event.preventDefault()\" onclick=\"go()\">x</div>")]
        [InlineData("<div onpointerdown=\"a()\" onpointermove=\"b()\" onpointerup=\"c()\">x</div>")]
        [InlineData("<button aria-disabled=\"true\" title=\"Select a row first\">Delete</button>")]
        [InlineData("<input aria-label=\"Name\" disabled>")]
        [InlineData("<div tabindex=\"0\"></div><div tabindex=\"-1\"></div>")]
        [InlineData("<span translate=\"no\">SysWeaver</span>")]
        [InlineData("<h1>A</h1><h2>B</h2>")]
        [InlineData("<label for=\"n\">Name</label><input id=\"n\" type=\"text\">")]
        [InlineData("<label>Name <input type=\"text\"></label>")]
        [InlineData("<input type=\"search\" aria-label=\"Search\" placeholder=\"Search\">")]
        [InlineData("<input type=\"hidden\" name=\"x\"><input type=\"file\" hidden>")]
        [InlineData("<label for=\"p\">Password</label><input type=\"password\" id=\"p\" autocomplete=\"current-password\">")]
        [InlineData("<img src=\"x.png\" alt=\"\" width=\"20\" height=\"20\">")]
        [InlineData("<img src=\"x.png\" alt=\"\" style=\"aspect-ratio: 16 / 9; width: 100%\">")]
        [InlineData("<style>.thumb { width: 4rem; height: 4rem; }</style><img class=\"thumb\" src=\"x.png\" alt=\"\">")]
        [InlineData("<style>.frame iframe { position: absolute; inset: 0; }</style><div class=\"frame\"><iframe src=\"a.html\" title=\"a\"></iframe></div>")]
        [InlineData("<img src=\"x.png\" alt=\"\" hidden>")]
        [InlineData("<button title=\"Save changes to the profile (Ctrl+S)\">Save</button>")]
        [InlineData("<button aria-label=\"Save the profile\">Save</button>")]
        [InlineData("<a href=\"x\">No</a><button>Close</button>")]
        [InlineData("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")]
        public void HtmlFine(String html)
        {
            Assert.Empty(Html(html));
        }

        [Fact]
        public void ReservedSizesComeFromTheFolderCss()
        {
            using var f = new TestFolder();
            f.Write("p/x.css", ".avatar { width: 2rem; height: 2rem; }\n.pic img { aspect-ratio: 4 / 3; }\n.half { width: 50%; }\n");
            f.Write("p/x.html", "<!DOCTYPE html>\n<html><head><script defer src=\"../sw/core.js\"></script></head><body>\n"
                + "<img class=\"avatar\" src=\"a.png\" alt=\"\">\n<div class=\"pic\"><img src=\"b.png\" alt=\"\"></div>\n<img class=\"half\" src=\"c.png\" alt=\"\">\n</body></html>\n");
            //  A bare element selector of another folder doesn't count
            f.Write("q/y.css", "img { width: 1px; height: 1px; }\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Design = true });
            var lay = r.Findings.Where(x => x.Rule == Rule.UxLay1).ToList();
            var one = Assert.Single(lay);
            Assert.Equal(5, one.Line);
            Assert.Contains("a width but no height", one.Message);
        }

        [Fact]
        public void NativeDialogsAndZoomMovedFromTheDesignRules()
        {
            //  Not reported twice: the native dialog is ux-pop-9, no longer design-own-ui
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext("page/x.js", new SourceText(Header + "if (confirm(\"x\")) go();\nfetch(\"Api/X\");\n"), Opt(), f));
            Assert.Equal(new[] { "ux-pop-9", "design-own-ui" }, f.Select(x => x.Rule.Id).ToArray());
            Assert.Contains("own fetch()", f[1].Message);
            Assert.Contains("(UX guide POP-9", f[0].Message);
        }
    }
}
