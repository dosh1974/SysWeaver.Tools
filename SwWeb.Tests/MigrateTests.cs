using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    [Collection("Console")]
    public class MigrateTests
    {
        static readonly MigrationMap Map = MigrationMap.Load();

        static MigrateResult Run(TestFolder f, Action<MigrateOptions> setup = null)
        {
            var o = new MigrateOptions { Map = Map, JsDocHeaders = false };
            setup?.Invoke(o);
            return Migrator.MigrateFolder(f.Root, o);
        }

        /// <summary>
        /// Migrate one JavaScript file, returns the new text.
        /// </summary>
        static String Js(String code, out MigrateResult res, String name = "page/x.js")
        {
            using var f = new TestFolder();
            f.Write(name, code);
            res = Run(f);
            return f.Read(name);
        }

        static String Js(String code) => Js(code, out _);

        static String Html(String html, out MigrateResult res, Dictionary<String, String> more = null, Action<MigrateOptions> setup = null)
        {
            using var f = new TestFolder();
            f.Write("page/p.html", html);
            if (more != null)
                foreach (var kv in more)
                    f.Write(kv.Key, kv.Value);
            res = Run(f, setup);
            return f.Read("page/p.html");
        }

        static String Html(String html) => Html(html, out _);

        static int Warnings(MigrateResult r, String contains = null) => r.Items.Count(x => x.Kind == MigrateItemKind.Warning && (contains == null || x.Message.Contains(contains)));

        // ---- HTML includes ----

        [Fact]
        public void HtmlBasicCommonPage()
        {
            var src = "<html>\r\n<head>\r\n    <script defer src=\"../common/common.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"page.css\">\r\n</head>\r\n</html>\r\n";
            var exp = "<html>\r\n<head>\r\n    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n    <link rel=\"stylesheet\" href=\"page.css\">\r\n</head>\r\n</html>\r\n";
            Assert.Equal(exp, Html(src, out var r));
            Assert.Equal(1, r.PagesMigrated);
            Assert.Equal(0, r.Warnings);
        }

        [Fact]
        public void HtmlTagShapesKeepAttributesQuotesAndCase()
        {
            Assert.Equal("<SCRIPT SRC='../common/../sw/core.js' DEFER></SCRIPT>\n<SCRIPT SRC='../common/../sw/ui.js' DEFER></SCRIPT>\n<link rel=\"stylesheet\" href=\"../common/../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../common/../sw/ui.css\">\n",
                Html("<SCRIPT SRC='../common/../common/common.js' DEFER></SCRIPT>\n"));
            Assert.Equal("\t<script src=../sw/core.js></script>\n\t<script src=../sw/ui.js></script>\n<link href='../common/theme.css' rel=stylesheet>\n<link href='../sw/ui.css' rel=stylesheet>\n",
                Html("\t<script src=../common/common.js></script>\n<link href='../common/theme.css' rel=stylesheet>\n"));
            Assert.Equal("<script async type=\"text/javascript\" src=\"common/core.js?v=2\" data-x=\"1\"></script>",
                Html("<script async type=\"text/javascript\" src=\"common/core.js?v=2\" data-x=\"1\"></script>"));
            // query kept, LF kept
            Assert.Equal("<script defer src=\"sw/core.js?v=2\"></script>\n<script defer src=\"sw/ui.js?v=2\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n<link rel=\"stylesheet\" href=\"sw/ui.css\">\n",
                Html("<script defer src=\"common/common.js?v=2\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n"));
        }

        [Fact]
        public void HtmlCommentedTagsAreNotTouched()
        {
            var src = "<!-- <script src=\"../common/common.js\"></script> -->\n<p>common/common.js</p>\n";
            Assert.Equal(src, Html(src, out var r));
            Assert.Equal(0, r.LegacyPages);
        }

        [Fact]
        public void HtmlApplicationWithoutShellIsRemoved()
        {
            var src = "  <script defer src=\"../common/common.js\"></script>\n  <script defer src=\"../app/application.js\"></script>\n  <link rel=\"stylesheet\" href=\"../common/theme.css\">\n  <link rel=\"stylesheet\" href=\"../app/application.css\">\n";
            var exp = "  <script defer src=\"../sw/core.js\"></script>\n  <script defer src=\"../sw/ui.js\"></script>\n  <link rel=\"stylesheet\" href=\"../common/theme.css\">\n  <link rel=\"stylesheet\" href=\"../sw/ui.css\">\n";
            Assert.Equal(exp, Html(src, out var r));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("app/application.js removed"));
        }

        [Fact]
        public void HtmlApplicationWithShell()
        {
            var src = "<script defer src=\"common/common.js\"></script>\n<script defer src=\"app/application.js\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n<link rel=\"stylesheet\" href=\"app/application.css\">\n<body onload=\"appMain()\"></body>\n";
            var exp = "<script defer src=\"sw/core.js\"></script>\n<script defer src=\"sw/ui.js\"></script>\n<script defer src=\"sw/shell.js\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n<link rel=\"stylesheet\" href=\"sw/ui.css\">\n<link rel=\"stylesheet\" href=\"sw/shell.css\">\n<body onload=\"appMain()\"></body>\n";
            Assert.Equal(exp, Html(src, out var r));
            Assert.Contains(r.Items, x => x.Message.Contains("sw/shell.js: the page uses shell features (appMain)"));
        }

        [Fact]
        public void HtmlShellDetectedInPageScript()
        {
            var src = "<script defer src=\"../app/application.js\"></script>\n<script defer src=\"main.js\"></script>\n";
            var res = Html(src, out var r, new Dictionary<String, String> { ["page/main.js"] = "window.onload = () => appMain(new AppParams());\n" });
            Assert.Equal("<script defer src=\"../sw/shell.js\"></script>\n<link rel=\"stylesheet\" href=\"../sw/shell.css\">\n<script defer src=\"main.js\"></script>\n", res);
        }

        [Fact]
        public void HtmlTemplatedSources()
        {
            var src = "<script defer src=\"${@Base Url}common/common.js\"></script>\n<script defer src=\"$(COMMONJSFILE)\"></script>\n<link rel=\"stylesheet\" href=\"${@Base Url}common/theme.css\">\n";
            var exp = "<script defer src=\"${@Base Url}sw/core.js\"></script>\n<script defer src=\"${@Base Url}sw/ui.js\"></script>\n<script defer src=\"$(COMMONJSFILE)\"></script>\n<link rel=\"stylesheet\" href=\"${@Base Url}common/theme.css\">\n<link rel=\"stylesheet\" href=\"${@Base Url}sw/ui.css\">\n";
            Assert.Equal(exp, Html(src, out var r));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("$(COMMONJSFILE)"));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("templated prefix"));
        }

        [Fact]
        public void HtmlBlockedByRemainingLegacyGlobals()
        {
            var src = "<script defer src=\"../common/common.js\"></script>\n<script>\nfunction go() { Fail(\"x\"); PopUp(b => b); }\n</script>\n";
            // --keep-legacy: the old safety gate
            var res = Html(src, out var r, null, o => o.KeepLegacy = true);
            Assert.Equal("<script defer src=\"../common/common.js\"></script>\n<script>\nfunction go() { Sw.Fail(\"x\"); PopUp(b => b); }\n</script>\n", res);
            Assert.Equal(1, r.PagesBlocked);
            Assert.Equal(0, r.PagesCompat);
            Assert.Equal(1, Warnings(r, "legacy includes kept (--keep-legacy)"));
            // forced (no compat bundle)
            res = Html(src, out r, null, o => o.ForceIncludes = true);
            Assert.StartsWith("<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../sw/ui.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n<script>", res);
            Assert.Equal(1, r.PagesMigrated);
            Assert.Equal(0, r.PagesCompat);
            Assert.DoesNotContain("compat", res);
        }

        [Fact]
        public void HtmlRemainingLegacyGlobalsUseCompatBundle()
        {
            var src = "<script defer src=\"../common/common.js\"></script>\n<script defer src=\"../app/application.js\"></script>\n<script defer src=\"p.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../app/application.css\">\n<link rel=\"stylesheet\" href=\"p.css\">\n<script>\nfunction go() { Fail(\"x\"); PopUp(b => b); }\n</script>\n";
            var exp = "<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../sw/ui.js\"></script>\n<script defer src=\"../sw/compat.js\"></script>\n<script defer src=\"p.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n<link rel=\"stylesheet\" href=\"../sw/compat.css\">\n<link rel=\"stylesheet\" href=\"p.css\">\n<script>\nfunction go() { Sw.Fail(\"x\"); PopUp(b => b); }\n</script>\n";
            using var f = new TestFolder();
            f.Write("page/p.html", src);
            f.Write("page/p.js", "new ColorIcon(\"IconX\");\n");
            var r = Run(f);
            Assert.Equal(exp, f.Read("page/p.html"));
            Assert.Equal(1, r.PagesMigrated);
            Assert.Equal(1, r.PagesCompat);
            Assert.Equal(0, r.PagesBlocked);
            var w = Assert.Single(r.Items, x => x.Kind == MigrateItemKind.Warning && x.Message.Contains("sw/compat.js"));
            Assert.Contains("ColorIcon (page/p.js)", w.Message);
            Assert.Contains("PopUp (line 8)", w.Message);
            // idempotent; the info tells the bundle is still needed
            var r2 = Run(f);
            Assert.Equal(exp, f.Read("page/p.html"));
            Assert.Equal(0, r2.Changes);
            Assert.Contains(r2.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("sw/compat.js still needed for 2 legacy global(s): ColorIcon, PopUp"));
            // once the names are gone the info says the bundle can be removed
            f.Write("page/p.js", "Sw.Icon(\"IconX\");\n");
            f.Write("page/p.html", exp.Replace("PopUp(b => b)", "Sw.Popup.Show(b)"));
            var r3 = Run(f);
            Assert.Contains(r3.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("sw/compat.js is no longer needed"));
        }

        [Fact]
        public void HtmlCompatWithShellAndExistingSwCss()
        {
            //  application.js with shell features: sw/shell.js and sw/compat.js after it; existing sw/ui.css: compat.css after it
            var src = "<script defer src=\"common/common.js\"></script>\n<script defer src=\"app/application.js\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n<link rel=\"stylesheet\" href=\"sw/ui.css\">\n<body onload=\"appMain(); PopUpMenu(document.body, () => null)\"></body>\n";
            var exp = "<script defer src=\"sw/core.js\"></script>\n<script defer src=\"sw/ui.js\"></script>\n<script defer src=\"sw/compat.js\"></script>\n<script defer src=\"sw/shell.js\"></script>\n<link rel=\"stylesheet\" href=\"common/theme.css\">\n<link rel=\"stylesheet\" href=\"sw/ui.css\">\n<link rel=\"stylesheet\" href=\"sw/shell.css\">\n<link rel=\"stylesheet\" href=\"sw/compat.css\">\n<body onload=\"appMain(); PopUpMenu(document.body, () => null)\"></body>\n";
            Assert.Equal(exp, Html(src, out var r));
            Assert.Equal(1, r.PagesCompat);
        }

        [Fact]
        public void HtmlBlockedByPageScript()
        {
            var src = "<script defer src=\"../common/common.js\"></script>\n<script defer src=\"x.js\"></script>\n";
            Html(src, out var r, new Dictionary<String, String> { ["page/x.js"] = "new ColorIcon(\"IconX\");\n" }, o => o.KeepLegacy = true);
            Assert.Equal(1, r.PagesBlocked);
            Assert.Contains(r.Items, x => x.Message.Contains("ColorIcon (page/x.js)"));
            //  default: compat bundle
            var res = Html(src, out r, new Dictionary<String, String> { ["page/x.js"] = "new ColorIcon(\"IconX\");\n" });
            Assert.Equal(1, r.PagesCompat);
            Assert.Contains("<script defer src=\"../sw/compat.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n<link rel=\"stylesheet\" href=\"../sw/compat.css\">\n<script defer src=\"x.js\">", res);
        }

        [Fact]
        public void HtmlExistingNewIncludesDeduplicated()
        {
            var src = "<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../common/common.js\"></script>\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n";
            //  (the theme link the page lacked comes before sw/ui.css, rule theme-include)
            Assert.Equal("<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../sw/ui.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n", Html(src));
        }

        [Fact]
        public void HtmlInlineScriptAndHandlers()
        {
            var src = "<script src=\"../sw/core.js\"></script>\n<button onclick=\"if (badClick(event)) return; Info('hi')\">x</button>\n<script>\n  // Fail(\n  const s = \"Fail\"; Fail(s);\n</script>\n<script type=\"text/template\">Fail(1)</script>\n<p>Fail(1)</p>\n";
            var exp = "<script src=\"../sw/core.js\"></script>\n<button onclick=\"if (Sw.BadClick(event)) return; Sw.Info('hi')\">x</button>\n<script>\n  // Fail(\n  const s = \"Fail\"; Sw.Fail(s);\n</script>\n<script type=\"text/template\">Fail(1)</script>\n<p>Fail(1)</p>\n";
            Assert.Equal(exp, Html(src));
        }

        [Fact]
        public void HtmlNoThemeLinkAddsUiCssAfterScripts()
        {
            var src = "<script defer src=\"../common/common.js\"></script>\n";
            Assert.Equal("<script defer src=\"../sw/core.js\"></script>\n<script defer src=\"../sw/ui.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../sw/ui.css\">\n", Html(src, out var r));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("common/theme.css first"));
            Assert.Equal(0, Warnings(r, "theme.css"));
        }

        [Fact]
        public void HtmlBomAndCrlfPreserved()
        {
            using var f = new TestFolder();
            f.Write("a.html", "<script src=\"common/common.js\"></script>\r\n<link rel=\"stylesheet\" href=\"common/theme.css\">\r\n", true);
            Run(f);
            var b = f.ReadBytes("a.html");
            Assert.True(b[0] == 0xef && b[1] == 0xbb && b[2] == 0xbf);
            Assert.Equal("<script src=\"sw/core.js\"></script>\r\n<script src=\"sw/ui.js\"></script>\r\n<link rel=\"stylesheet\" href=\"common/theme.css\">\r\n<link rel=\"stylesheet\" href=\"sw/ui.css\">\r\n", f.Read("a.html").TrimStart('﻿'));
        }

        [Fact]
        public void HtmlClassify()
        {
            Assert.Equal(IncludeKind.CommonJs, HtmlMigrator.Classify("../../common/common.js").Kind);
            Assert.Equal("../../", HtmlMigrator.Classify("../../common/common.js").Prefix);
            Assert.Equal(IncludeKind.None, HtmlMigrator.Classify("../mycommon/common.js").Kind);
            Assert.Equal(IncludeKind.None, HtmlMigrator.Classify("../xcommon/common.js").Kind);
            Assert.Equal(IncludeKind.AppCss, HtmlMigrator.Classify("app/application.css#x").Kind);
            Assert.Equal("$(X)", HtmlMigrator.Classify("$(X)common/common.js").Prefix);
        }

        // ---- JavaScript renames ----

        [Fact]
        public void JsSimpleRenames()
        {
            Assert.Equal("Sw.Fail(\"x\"); await Sw.Util.Delay(5); Sw.Page.SignalLoaded();", Js("Fail(\"x\"); await delay(5); PageLoaded();"));
            Assert.Equal("const p = Sw.Url.Params(); new Sw.Util.AbortHandler();", Js("const p = getUrlParams(); new AbortHandler();"));
            Assert.Equal("list.map(Sw.Format.HtmlSafe)", Js("list.map(makeHtmlSafe)"));
        }

        [Fact]
        public void JsOpenKeepsItsNewTab()
        {
            //  The old Open defaulted to a new tab, Sw.Page.Navigate to the same tab
            Assert.Equal("Sw.Page.Navigate(url, \"_blank\");", Js("Open(url);"));
            Assert.Equal("Sw.Page.Navigate(url, \"_self\");", Js("Open(url, \"_self\");"));
        }

        [Fact]
        public void JsStringsCommentsRegexTemplatesUntouched()
        {
            var code = "const a = \"Fail(1)\" + 'delay' + `Fail ${Fail(\"t\")} delay`; // Fail(2)\n/* delay(3) */ const r = /Fail\\(/g; x = y / Fail / 2;";
            var exp = "const a = \"Fail(1)\" + 'delay' + `Fail ${Sw.Fail(\"t\")} delay`; // Fail(2)\n/* delay(3) */ const r = /Fail\\(/g; x = y / Sw.Fail / 2;";
            Assert.Equal(exp, Js(code));
        }

        [Fact]
        public void JsMemberAccessAndKeysUntouched()
        {
            Assert.Equal("obj.Fail(1); obj?.delay; const o = { Fail: 1, Info: Sw.Info }; o['Fail'];", Js("obj.Fail(1); obj?.delay; const o = { Fail: 1, Info: Info }; o['Fail'];"));
            Assert.Equal("class A { Fail() { return 1; } static Info(x) { } get Message() { } }", Js("class A { Fail() { return 1; } static Info(x) { } get Message() { } }"));
            Assert.Equal("const o = { Info(a) { return a; }, b: 1 };", Js("const o = { Info(a) { return a; }, b: 1 };"));
            Assert.Equal("x = a ? Sw.Fail : Sw.Info;", Js("x = a ? Fail : Info;"));
        }

        [Fact]
        public void JsWindowMemberIsReported()
        {
            //  A plain rename read through the global object is rewritten; a file of the run that defines window.X keeps it
            Assert.Equal("Sw.Fail(1);", Js("window.Fail(1);", out var r));
            Assert.Equal(0, Warnings(r));
            Assert.Equal("window.Fail = f; window.Fail(1);", Js("window.Fail = f; window.Fail(1);", out r));
            Assert.Equal(2, Warnings(r, "window.Fail"));
        }

        [Fact]
        public void JsShorthandProperty()
        {
            Assert.Equal("const o = { delay: Sw.Util.Delay, x };", Js("const o = { delay, x };"));
        }

        [Fact]
        public void JsShadowingAndDeclarations()
        {
            // parameter / local / destructuring shadow: not renamed, reported
            //  (the use after the function is outside the parameter's exact scope: the global)
            Assert.Equal("function f(Fail) { Fail(); }\nSw.Fail();", Js("function f(Fail) { Fail(); }\nFail();", out var r));
            Assert.Equal(0, Warnings(r));
            Assert.Equal("const g = (Info, x) => Info(x);", Js("const g = (Info, x) => Info(x);"));
            Assert.Equal("const h = delay => delay + 1;", Js("const h = delay => delay + 1;"));
            Assert.Equal("function a() { const { delay, b: Info, ...Message } = o; delay(); Info(); Message(); }", Js("function a() { const { delay, b: Info, ...Message } = o; delay(); Info(); Message(); }"));
            Assert.Equal("function a() { let [Fail, , Info = 1] = o; Fail(); }", Js("function a() { let [Fail, , Info = 1] = o; Fail(); }"));
            Assert.Equal("try { } catch (Fail) { Fail.x; }", Js("try { } catch (Fail) { Fail.x; }"));
            // declarator list with initializers
            Assert.Equal("function q() { var a = Sw.Util.Delay(1), Info = 2; Info(); }", Js("function q() { var a = delay(1), Info = 2; Info(); }"));
        }

        [Fact]
        public void JsLocalsInScopeAreNotWarned()
        {
            //  Ordinary local variables / parameters that shadow a legacy global: no warning for uses inside their scope (CR4-Docs)
            String[] silent =
            {
                "function f() {\n    let delay = 100;\n    setTimeout(g, delay);\n    delay *= 2;\n}\n",
                "const g = (Info, x) => Info(x);",
                "const h = delay => delay + 1;",
                "function a() { const { delay, b: Info, ...Message } = o; delay(); Info(); Message(); }",
                "try { } catch (Fail) { Fail.x; }",
                "for (let delay = 0; delay < 3; ++delay) total += delay;",
                "for (const Info of list) { Info.x(); }",
                "class K { run(delay) { return delay * 2; } }",
                "const o = { m(Fail) { return Fail; } };",
                "if (a) { var delay = 1; delay++; }",
            };
            foreach (var code in silent)
            {
                Assert.Equal(code, Js(code, out var r));
                Assert.Equal(0, Warnings(r));
                Assert.DoesNotContain(r.Items, x => x.Message.Contains("declared locally"));
            }
        }

        [Fact]
        public void JsLocalUsedOutsideItsScope()
        {
            //  let / const / parameters / function expression names have exact scopes: a use outside them is the legacy global (renamed)
            Assert.Equal("function f() { let delay = 100; return delay; }\nfunction g() { return Sw.Util.Delay(5); }\n",
                Js("function f() { let delay = 100; return delay; }\nfunction g() { return delay(5); }\n", out var r));
            Assert.Equal(0, Warnings(r));
            //  Block scoped: after the block is outside
            Assert.Equal("function f() { if (a) { let Fail = 1; Fail++; } Sw.Fail(\"x\"); }", Js("function f() { if (a) { let Fail = 1; Fail++; } Fail(\"x\"); }", out var r2));
            Assert.Equal(0, Warnings(r2));
            //  A named function expression is only visible inside itself
            Assert.Equal("const x = function delay(n) { return n ? delay(n - 1) : 0; };\nSw.Util.Delay(1);", Js("const x = function delay(n) { return n ? delay(n - 1) : 0; };\ndelay(1);", out var r3));
            Assert.Equal(0, Warnings(r3));
            //  A var (function scoped) or a function declaration may be visible beyond its block: may mean the legacy global, warned once
            var code4 = "function f() { if (a) { var delay = 100; } return delay; }\nfunction g() { return delay(5); }\n";
            Assert.Equal(code4, Js(code4, out var r4));
            Assert.Equal(1, Warnings(r4, "used outside that declaration's scope"));
            Assert.Contains(r4.Items, x => x.Message.Contains("first declaration at line 1"));
        }

        [Fact]
        public void JsPageGlobalDeclaredInAnotherFile()
        {
            using var f = new TestFolder();
            f.Write("a.js", "function Message(t) { alert(t); }\n");
            f.Write("b.js", "Message(\"x\"); Fail(\"y\");\n");
            var r = Run(f);
            Assert.Equal("function Message(t) { alert(t); }\n", f.Read("a.js"));
            Assert.Equal("Message(\"x\"); Sw.Fail(\"y\");\n", f.Read("b.js"));
            Assert.Contains(r.Items, x => x.Path == "b.js" && x.Kind == MigrateItemKind.Info && x.Message.Contains("another file"));
            Assert.Contains(r.Items, x => x.Path == "a.js" && x.Kind == MigrateItemKind.Info && x.Message.Contains("in this file"));
        }

        [Fact]
        public void JsPropertyKind()
        {
            Assert.Equal("if (Sw.Platform.IsMobileDevice) x = Sw.Platform.Name;", Js("if (isMobile()) x = getPlatform();"));
            Assert.Equal("const f = isMobile;", Js("const f = isMobile;", out var r));
            Assert.Equal(1, Warnings(r, "property"));
        }

        [Fact]
        public void JsOptionsConversion()
        {
            Assert.Equal("Sw.Net.Send(u, d)", Js("sendRequest(u, d)"));
            Assert.Equal("Sw.Net.Send(u, d, { Reload: true, ErrHandler: null, Blob: Sw.Format.HtmlSafe(x) })", Js("sendRequest(u, d, true, null, makeHtmlSafe(x))"));
            Assert.Equal("Sw.Page.Loading({ Element: el, Text: \"t\" })", Js("AddLoading(el, \"t\")"));
            Assert.Equal("Sw.Page.Loading()", Js("AddLoading()"));
            Assert.Equal("Sw.Net.Get(url,\n  { Reload: true,\n  Blob: false })",Js("getRequest(url,\n  true,\n  false)"));
            // too many / spread / not a call: reported
            Assert.Equal("getRequest(a, b, c, d, e, f)", Js("getRequest(a, b, c, d, e, f)", out var r));
            Assert.Equal(1, Warnings(r, "called with 6"));
            Assert.Equal("sendRequest(...args)", Js("sendRequest(...args)", out r));
            Assert.Equal(1, Warnings(r, "spread"));
            Assert.Equal("const s = sendRequest;", Js("const s = sendRequest;", out r));
            Assert.Equal(1, Warnings(r, "used as a value"));
        }

        [Fact]
        public void JsReportKind()
        {
            //  waitEvent's fn moves after the timeout: a call (side effects?) as the moved argument is reported
            Assert.Equal("await waitEvent(el, \"load\", make(), 100);", Js("await waitEvent(el, \"load\", make(), 100);", out var r));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Warning && x.Message.Contains("Sw.Util.WaitEvent(target, name, timeout, fn)") && x.Message.Contains("side effects") && x.Line == 1);
            Assert.Equal("downloadFile(\"a.txt\", url);", Js("downloadFile(\"a.txt\", url);", out r));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Warning && x.Message.Contains("downloadFile"));
        }

        [Fact]
        public void JsUnsafeContextsReported()
        {
            Assert.Equal("if (typeof Fail === \"function\") Sw.Fail(1);", Js("if (typeof Fail === \"function\") Fail(1);", out var r));
            Assert.Equal(1, Warnings(r, "typeof"));
            Assert.Equal("Fail = x; Fail += 1; Fail++;", Js("Fail = x; Fail += 1; Fail++;", out r));
            Assert.Equal(3, Warnings(r, "assigned"));
            Assert.Equal("if (Sw.Fail == x) y(Sw.Fail);", Js("if (Fail == x) y(Fail);"));
        }

        [Fact]
        public void JsObjectMembers()
        {
            Assert.Equal("Sw.Format.Bytes(1) + Sw.Session.Subscribe(\"x\", f) + Sw.InterOp.Post(\"t\")", Js("ValueFormat.formatByteSize(1) + SessionManager.AddMessageHandler(\"x\", f) + InterOp.Post(\"t\")"));
            Assert.Equal("Sw.Clipboard.CopyOnClick(el, v); ValueFormat.nope(1); const v = ValueFormat;", Js("ValueFormat.copyOnClick(el, v); ValueFormat.nope(1); const v = ValueFormat;", out var r));
            Assert.Equal(2, Warnings(r));
        }

        [Fact]
        public void JsClickHelpersRenamed()
        {
            //  Found porting MediaTest (Fenrir.js): the one argument keyboardClick and copyOnClick with options are rewritten
            Assert.Equal("Sw.KeyClick(p); Sw.RemoveKeyClick(p); Sw.Clipboard.CopyOnClick(p, v, { Keyboard: true, AddTitle: false });",
                Js("keyboardClick(p); removeKeyboardClick(p); ValueFormat.copyOnClick(p, v, true, false);", out var r));
            Assert.Equal(0, Warnings(r));
            Assert.Equal("keyboardClick(p, 1);", Js("keyboardClick(p, 1);", out r));
            Assert.Equal(1, Warnings(r));
        }

        [Fact]
        public void JsUploaderWrappersRenamed()
        {
            //  Found porting ThrowItUp (upload.js): the uploader's same-argument wrappers are renamed like fileUploaderClick
            Assert.Equal("e.addEventListener(\"dragover\", Sw.Upload.Handlers.DragOver); Sw.Upload.Handlers.DragEnter(ev); Sw.Upload.Pick({ Multiple: true }); Sw.Upload.Files(\"R\", f, { ApiBase: \"../\" });",
                Js("e.addEventListener(\"dragover\", fileUploaderDrag); fileUploaderDragEnter(ev); selectFiles(true); fileUploader(\"R\", f, \"../\");", out var r));
            Assert.Equal(0, Warnings(r));
        }

        [Fact]
        public void EveryLegacyGlobalHasAMapEntry()
        {
            //  a legacy name without an entry gets only the generic "rewrite manually"; every name of the default list has a
            //  rename, an options mapping or a reported suggestion (the compat-only helpers: PORTING.md A11)
            var m = MigrationMap.Load();
            var missing = SwWeb.Checking.CheckOptions.LoadLegacyGlobals(null).Where(n => !m.Globals.ContainsKey(n)).ToArray();
            Assert.Empty(missing);
        }

        [Fact]
        public void JsNeverTouchesPermanentGlobals()
        {
            var code = "_T(\"a\"); _TF(\"b {0}\", 1); _TH(\"c\"); _TFH(\"d\", 1); setTheme(\"x\"); resetTheme(); toggleTheme(); appendTheme(u); getTheme(); applyTheme(); Build(d); appMain();";
            Assert.Equal(code, Js(code, out var r));
            Assert.Empty(r.Items);
            var m = MigrationMap.FromJson("{ \"Sections\": { \"x\": { \"Globals\": { \"_T\": \"Sw.T\", \"setTheme\": \"Sw.Theme.Set\" } } } }");
            Assert.False(m.IsLegacy("_T"));
            Assert.False(m.IsLegacy("setTheme"));
            Assert.Equal(2, m.LoadWarnings.Count);
        }

        [Fact]
        public void JsUnmappedLegacyReported()
        {
            Assert.Equal("SetTemporaryInnerText(el, \"x\");", Js("SetTemporaryInnerText(el, \"x\");", out var r));
            Assert.Equal(1, Warnings(r, "setTimeout"));
            Assert.Equal("setMediaSrcAndWait(v, u);", Js("setMediaSrcAndWait(v, u);", out r));
            Assert.Equal(1, Warnings(r, "canplaythrough"));
            Assert.Equal(1, Warnings(r, "PORTING.md A11"));
        }

        // ---- CSS ----

        [Fact]
        public void CssHooksReportedNotChanged()
        {
            using var f = new TestFolder();
            var css = "/* SysWeaver-Button */\nSysWeaver-Button, .KeyClick:hover { color: var(--ThemeMain); content: \".KeyClick\"; }\n@media (max-width: 10px) { div > SysWeaver-Button { x: 1 } }\n.KeyClickX, .MySysWeaver-Button { }\n";
            f.Write("a.css", css);
            var r = Run(f);
            Assert.Equal(css, f.Read("a.css"));
            Assert.Equal(3, Warnings(r, "CSS hook"));
        }

        [Fact]
        public void CssSafeMappingFromMapFile()
        {
            using var f = new TestFolder();
            f.Write("a.css", "SysWeaver-Button { } .Old, .Older { }\n");
            f.Write("map.json", "{ \"Sections\": { \"mine\": { \"Css\": { \".Old\": { \"New\": \".sw-new\", \"Safe\": true } } } } }");
            var map = MigrationMap.Load(f.PathOf("map.json"));
            var r = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = map });
            Assert.Equal("SysWeaver-Button { } .sw-new, .Older { }\n", f.Read("a.css"));
            Assert.Equal(1, Warnings(r, "CSS hook"));
        }

        // ---- folder behavior ----

        [Fact]
        public void IdempotentSecondRunChangesNothing()
        {
            using var f = new TestFolder();
            f.Write("app/p.html", "<script defer src=\"../common/common.js\"></script>\n<script defer src=\"../app/application.js\"></script>\n<script defer src=\"p.js\"></script>\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\n<link rel=\"stylesheet\" href=\"../app/application.css\">\n");
            f.Write("app/p.js", "Fail(1); sendRequest(a, b, true); const o = { delay };\n");
            var r1 = Run(f);
            Assert.Equal(2, r1.ChangedFiles.Count);
            var t1 = f.Read("app/p.html") + f.Read("app/p.js");
            var r2 = Run(f);
            Assert.Empty(r2.ChangedFiles);
            Assert.Equal(0, r2.Changes);
            Assert.Equal(t1, f.Read("app/p.html") + f.Read("app/p.js"));
        }

        [Fact]
        public void VendoredGeneratedAndLibraryFilesSkipped()
        {
            using var f = new TestFolder();
            var code = "Fail(1);\n";
            foreach (var p in new[] { "chart/external/x.js", "common/marked.js", "common/md5.js", "common/UZIP.js", "a/b.min.js", "edit/ace/x.js", "edit/prettier/x.js", "chart/chart.umd.js", "core/net.js", "ui/base.js", "shell/shell.js", "sw/core.js", "common/common.js", "app/application.js" })
                f.Write(p, code);
            f.Write("gen.js", "/**\n * @file gen.js: GENERATED by SwWeb bundle from sw/bundles.json, do not edit!\n */\nFail(1);\n");
            f.Write("mine.js", code);
            var r = Run(f);
            Assert.Equal(new[] { "mine.js" }, r.ChangedFiles.ToArray());
            Assert.Equal(1, r.FilesScanned);
        }

        [Fact]
        public void IncludeExclude()
        {
            using var f = new TestFolder();
            f.Write("a/x.js", "Fail(1);");
            f.Write("b/x.js", "Fail(1);");
            var r = Run(f, o => o.Include.Add("a/"));
            Assert.Equal(new[] { "a/x.js" }, r.ChangedFiles.ToArray());
            r = Run(f, o => o.Exclude.Add("b/**"));
            Assert.Empty(r.ChangedFiles);
            Assert.Equal("Fail(1);", f.Read("b/x.js"));
        }

        [Fact]
        public void DryRunWritesNothing()
        {
            using var f = new TestFolder();
            f.Write("x.js", "Fail(1);");
            f.Write("p.html", "<script src=\"common/common.js\"></script>");
            var r = Run(f, o => o.DryRun = true);
            Assert.Equal(2, r.ChangedFiles.Count);
            Assert.True(r.Changes >= 2);
            Assert.Equal("Fail(1);", f.Read("x.js"));
            Assert.Equal("<script src=\"common/common.js\"></script>", f.Read("p.html"));
        }

        [Fact]
        public void BackupCopiesOriginals()
        {
            using var f = new TestFolder();
            using var b = new TestFolder();
            f.Write("deep/dir/x.js", "Fail(1);");
            f.Write("same.js", "x();");
            Run(f, o => o.BackupDir = b.Root);
            Assert.Equal("Sw.Fail(1);", f.Read("deep/dir/x.js"));
            Assert.Equal("Fail(1);", b.Read("deep/dir/x.js"));
            Assert.False(File.Exists(b.PathOf("same.js")));
        }

        [Fact]
        public void ReportFormats()
        {
            using var f = new TestFolder();
            f.Write("src/x.js", "Fail(1);\nwaitFor(r);\n");
            var r = Run(f);
            var md = Migrator.FormatReport(r, f.Root, true);
            Assert.Contains("# SwWeb migrate report", md);
            Assert.Contains("- L1: Fail -> Sw.Fail", md);
            Assert.Contains("## Warnings (manual follow-up) (1)", md);
            var txt = Migrator.FormatReport(r, f.Root, false);
            Assert.Contains("src/x.js(1,1): change: Fail -> Sw.Fail", txt);
            Assert.Contains("src/x.js(2,1): warning: \"waitFor\"", txt);
        }

        [Fact]
        public void CommandLineExitCodes()
        {
            using var f = new TestFolder();
            f.Write("x.js", "Fail(1);");
            var report = f.PathOf("out/report.md");
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "migrate", f.Root, "--dry-run", "--report", report, "--quiet" }));
            Assert.True(File.Exists(report));
            Assert.Equal("Fail(1);", f.Read("x.js"));
            f.Write("y.js", "waitFor(r);");
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "migrate", f.Root, "--no-jsdoc-header", "--quiet" }));
            Assert.Equal("Sw.Fail(1);", f.Read("x.js"));
            Assert.Equal(SwWebProgram.ExitInvalidArguments, SwWebProgram.Run(new[] { "migrate", f.Root, "--level", "bogus" }));
            Assert.Equal(SwWebProgram.ExitInvalidArguments, SwWebProgram.Run(new[] { "migrate", f.PathOf("nope") }));
            Assert.Equal(SwWebProgram.ExitInvalidArguments, SwWebProgram.Run(new[] { "migrate", f.Root, "--map", f.PathOf("nope.json") }));
            Assert.Equal(SwWebProgram.ExitInvalidArguments, SwWebProgram.Run(new[] { "migrate", f.Root, "--backup", f.Root }));
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "mapping" }));
        }

        [Fact]
        public void JsDocHeaderAdded()
        {
            using var f = new TestFolder();
            f.Write("app/x.js", "\r\nFail(1);\r\n");
            f.Write("app/y.js", "/** @file app/y.js - documented */\nFail(1);\n");
            f.Write("app/z.js", "/* plain */\nlet a = 1;\n");
            var r = Run(f, o => o.JsDocHeaders = true);
            Assert.Equal("/**\r\n * @file app/x.js - (added by SwWeb migrate: describe the purpose of this file).\r\n */\r\n\r\nSw.Fail(1);\r\n", f.Read("app/x.js"));
            Assert.Equal("/** @file app/y.js - documented */\nSw.Fail(1);\n", f.Read("app/y.js"));
            Assert.StartsWith("/**\n * @file app/z.js", f.Read("app/z.js"));
            // idempotent
            Assert.Equal(0, Run(f, o => o.JsDocHeaders = true).Changes);
            Assert.Equal(0, SwWeb.Checking.Checker.CheckFolder(f.Root, new SwWeb.Checking.CheckOptions()).Findings.Count(x => x.Rule.Id == "jsdoc-header"));
        }

        [Fact]
        public void TranslationNewlinesFixed()
        {
            Assert.Equal("Sw.Fail(_T(\"Failed.{1}{0}\", e, String.fromCharCode(10), \"ctx\"));", Js("Fail(_T(\"Failed.\\n{0}\", e, \"ctx\"));"));
            Assert.Equal("x = _T('A{0}B{0}C', String.fromCharCode(10));", Js("x = _T('A\\nB\\nC');"));
            Assert.Equal("x = _T(\"A{0}B\", String.fromCharCode(10), \"ctx\");", Js("x = _TF(\"A\\nB\", \"ctx\");"));
            Assert.Equal("x = _TH(\"A{1}{0}B{2}\", y, String.fromCharCode(13, 10), String.fromCharCode(10));", Js("x = _TH(\"A\\r\\n{0}B\\n\", y);"));
            Assert.Equal("x = _T(\"a\\\\nb\");", Js("x = _T(\"a\\\\nb\");"));
            // not fixable: reported
            var src = "x = _TF(\"A{\\nB\"); y = _T(\"A\\n{1}\", a); z = _T(\"A\\\nB\");";
            Assert.Equal(src, Js(src, out var r));
            Assert.Equal(3, Warnings(r, "translated literal with a newline"));
            // the result passes the checker
            using var f = new TestFolder();
            f.Write("a.js", "/** @file a.js */\nx = _T(\"A\\n{0}\", y, \"c\");\n");
            Run(f);
            Assert.Empty(SwWeb.Checking.Checker.CheckFolder(f.Root, new SwWeb.Checking.CheckOptions()).Findings);
        }

        [Fact]
        public void LibraryAliasesDontNeedCompat()
        {
            //  A library alias that can't be renamed is reported but keeps working: no sw/compat.js for it
            var src = "<script defer src=\"../common/common.js\"></script>\n<script defer src=\"../explore/table.js\"></script>\n<script>\nTable.isReverseSort(q); Edit.Create(1); var t = new TableDataState(); var C = class { Edit() { } };\n</script>\n";
            var res = Html(src, out var r);
            Assert.Contains("Table.isReverseSort(q); Sw.Edit.Create(1); var t = new Sw.Table.State();", res);
            Assert.DoesNotContain("compat", res);
            Assert.Equal(0, r.PagesCompat);
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Warning && x.Message.Contains("Table.isReverseSort") && x.Message.Contains("legacy alias still provided by explore/table.js"));
            // pure renames of the other libraries; non pure ones are reported
            Assert.Equal("Sw.Chart.Add(e, u); Sw.Chat.Add(c, 1, a); new Sw.Media.Player(); Sw.Anim.MathTools.x; Sw.Upload.StatusText(s); Sw.UserManager.AddEmail(e); Sw.PassKey.Get();",
                Js("CanvasChart.addChart(e, u); Chat.addChat(c, 1, a); new MediaPlayer(); MathTools.x; fileUploaderStatusText(s); changeEmail(e); PassKey.Get();"));
            Assert.Equal("Sw.Upload.Target(e, { Repo: r }); CanvasChart.noLabels(c); CreateTagsGallery(a, b); Chat;", Js("fileUploaderSetup(e, r); CanvasChart.noLabels(c); CreateTagsGallery(a, b); Chat;", out var r2));
            Assert.Equal(3, Warnings(r2, "legacy alias still provided by"));
            Assert.Empty(MigrationMap.Load().LoadWarnings);
        }

        [Fact]
        public void LevelIncludesDoesNotRename()
        {
            using var f = new TestFolder();
            f.Write("p.html", "<script src=\"common/common.js\"></script>\n<script src=\"p.js\"></script>\n");
            f.Write("p.js", "Fail(1);");
            var r = Run(f, o => { o.Level = MigrateLevel.Includes; o.KeepLegacy = true; });
            Assert.Equal("Fail(1);", f.Read("p.js"));
            Assert.Equal(1, r.PagesBlocked);
            f.Write("p.js", "Sw.Fail(1);");
            r = Run(f, o => { o.Level = MigrateLevel.Includes; o.KeepLegacy = true; });
            Assert.Equal(1, r.PagesMigrated);
            Assert.StartsWith("<script src=\"sw/core.js\"></script>\n<script src=\"sw/ui.js\"></script>", f.Read("p.html"));
        }

        // ---- map ----

        [Fact]
        public void MapFileExtendsSections()
        {
            using var f = new TestFolder();
            f.Write("map.json", "// comment\n{ \"Sections\": { \"table\": { \"Description\": \"Table library\", \"Globals\": { \"Table\": { \"Members\": { \"addTable\": \"Sw.Table.Add\" } }, \"Fail\": { \"Kind\": \"Report\", \"Suggest\": \"Sw.Toast\" }, }, }, }, }");
            var m = MigrationMap.Load(f.PathOf("map.json"));
            Assert.Contains(m.Sections, x => x.Name == "table");
            Assert.Contains(m.Sections, x => x.Name == "core");
            f.Write("x.js", "Table.addTable(a); Fail(1); delay(1);");
            var r = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = m, JsDocHeaders = false });
            Assert.Equal("Sw.Table.Add(a); Fail(1); Sw.Util.Delay(1);", f.Read("x.js"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"a\": { \"Globals\": { \"x\": { \"Kind\": \"Bogus\" } } } } }"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"a\": { \"Globals\": { \"x\": \"Sw.\\\"x\" } } } }"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"a\": { \"Globals\": { \"x\": { \"Kind\": \"Rename\" } } } } }"));
        }

        [Fact]
        public void DefaultMapIsValid()
        {
            var m = MigrationMap.Load();
            Assert.Empty(m.LoadWarnings);
            Assert.True(m.Globals.Count > 150);
            foreach (var n in MigrationMap.NeverTouch)
                Assert.False(m.IsLegacy(n));
            Assert.True(m.IsShell("appMain"));
            Assert.True(m.IsLegacy("Fail"));
        }

        // ---- integration: the whole old front end (baseline copy) ----

        static String BaselineRoot() =>
            "C:/Users/dh/AppData/Local/Temp/claude/D--SysWeaver-Tests/9a8afb3a-cd8c-4285-bf69-087f9a3cd8b4/scratchpad/baseline_B0";

        static void CopyDir(String from, String to)
        {
            Directory.CreateDirectory(to);
            foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }

        static String HashTree(String root)
        {
            using var sha = SHA256.Create();
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                var b = File.ReadAllBytes(file);
                sha.TransformBlock(b, 0, b.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash);
        }

        // ---- upgrade: argument templates, options, window reads, block scopes, own UI pages ----

        [Fact]
        public void JsArgsTemplate()
        {
            //  waitEvent2(el, a, b, fn [, ms]) -> Sw.Util.WaitEvent(el, [a, b], ms, fn): in order, argument texts (and renames in them) stay
            Assert.Equal("await Sw.Util.WaitEvent(f, [\"load\", \"error\"], undefined, () => {\n    Sw.Fail(1);\n});",
                Js("await waitEvent2(f, \"load\", \"error\", () => {\n    Fail(1);\n});"));
            //  a timeout makes the order change: functions and names may move
            Assert.Equal("Sw.Util.WaitEvent(el, [\"a\", \"b\"], 500, x => x.src = u);", Js("waitEvent2(el, \"a\", \"b\", x => x.src = u, 500);"));
            Assert.Equal("Sw.Util.WaitEvent(el, \"load\", t.ms, function (x) { go(x); });", Js("waitEvent(el, \"load\", function (x) { go(x); }, t.ms);"));
            //  only the needed arguments: a missing trailing reference is left out, a missing inner one is undefined
            Assert.Equal("Sw.Util.WaitEvent(el, \"load\");", Js("waitEvent(el, \"load\");"));
            //  a moved argument keeps the renames inside it (one run does all)
            Assert.Equal("Sw.Util.WaitEvent(el, \"load\", 5, () => Sw.Fail(1, Sw.Util.Delay));", Js("waitEvent(el, \"load\", () => Fail(1, delay), 5);", out var r));
            Assert.Equal(0, r.Warnings);
            //  idempotent
            Assert.Equal("Sw.Util.WaitEvent(el, [\"a\", \"b\"], 500, x => x.src = u);", Js("Sw.Util.WaitEvent(el, [\"a\", \"b\"], 500, x => x.src = u);"));
        }

        [Fact]
        public void JsArgsTemplateMap()
        {
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"s\": { \"Globals\": { \"f\": { \"New\": \"Sw.F\", \"Args\": \"$0, $0\" } } } } }"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"s\": { \"Globals\": { \"f\": { \"New\": \"Sw.F\", \"Args\": \"$x\" } } } } }"));
            Assert.Throws<Exception>(() => MigrationMap.FromJson("{ \"Sections\": { \"s\": { \"Globals\": { \"f\": { \"New\": \"Sw.F\", \"Args\": \"$0, \\\"a\\\"\" } } } } }"));
            var m = MigrationMap.FromJson("{ \"Sections\": { \"s\": { \"Globals\": { \"f\": { \"New\": \"Sw.F\", \"Args\": \"{ A: $1 }, $0\" } } } } }");
            Assert.Equal("{ A: $1 }, $0", m.Globals["f"].Args);
        }

        [Fact]
        public void JsOptionsOfOldHelpers()
        {
            Assert.Equal("Sw.SetImageSource(img, u, { Fallback: null, FlushCache: false, OnLoaded: () => img.hidden = false });", Js("SetImageSource(img, u, null, false, () => img.hidden = false);"));
            Assert.Equal("Sw.SetImageSource(img, u);", Js("SetImageSource(img, u);"));
            Assert.Equal("const f = Sw.FadeSrc(document.getElementById(\"x\"));", Js("const f = fixElementWithSrc(document.getElementById(\"x\"));"));
            Assert.Equal("Sw.FadeSrc(e, { ShowError: true, FadeIn: 300 });", Js("fixElementWithSrc(e, true, 300);"));
            //  the api prefix of the old auth calls is dropped (a simple value: a member chain of names is one too)
            Assert.Equal("await Sw.Auth.Login(u, p); await Sw.Auth.Login(u, p); await Sw.Auth.Logout(); await Sw.Auth.Logout();",
                Js("await loginRequest(u, p, ROUTES.VALIDATION.PREFIX); await loginRequest(u, p); await logoutRequest('./'); await logoutRequest();"));
            Assert.Equal("await loginRequest(u, p, prefix());", Js("await loginRequest(u, p, prefix());", out var r));
            Assert.Equal(1, Warnings(r, "side effects"));
            Assert.Equal("await Sw.Auth.ValidatePassword(u, p, c, null, null, null);", Js("await ValidatePassword(u, p, c, null, null, null);"));
        }

        [Fact]
        public void JsWindowReadOfPlainRename()
        {
            Assert.Equal("if (!Sw.Layout.Narrow) return; const m = Sw.Layout.Mobile;", Js("if (!window.Narrow) return; const m = window.Mobile;"));
            //  writes, typeof and library aliases stay reported
            Assert.Equal("window.Narrow = 1; typeof window.Narrow; delete window.Narrow;", Js("window.Narrow = 1; typeof window.Narrow; delete window.Narrow;", out var r));
            Assert.Equal(3, Warnings(r, "window.Narrow"));
        }

        [Fact]
        public void JsBlockScopedLocalOutsideIsTheGlobal()
        {
            //  const isMobile in one method, isMobile() in another: the second is the legacy global
            Assert.Equal("class A {\n    a() { const isMobile = this.m(); if (isMobile) go(); }\n    m() { return Sw.Platform.IsMobileDevice; }\n}",
                Js("class A {\n    a() { const isMobile = this.m(); if (isMobile) go(); }\n    m() { return isMobile(); }\n}", out var r));
            Assert.Equal(0, r.Warnings);
            //  a var may be function scoped beyond its block: still reported
            Assert.Equal("function f() { if (a) { var delay = 1; } setTimeout(g, delay); }", Js("function f() { if (a) { var delay = 1; } setTimeout(g, delay); }", out r));
            Assert.Equal(1, Warnings(r, "declared locally"));
        }

        [Fact]
        public void UpgradeKeepsOwnStyles()
        {
            //  a page with its own UI (no SysWeaver style sheet): the scripts only
            var own = "<html>\r\n<head>\r\n    <script defer src=\"../common/common.js\"></script>\r\n    <link href=\"../styles/my.css\" rel=\"stylesheet\" />\r\n</head>\r\n</html>\r\n";
            Assert.Equal(own.Replace("<script defer src=\"../common/common.js\"></script>", "<script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>"),
                Html(own, out var r, null, o => { o.KeepStyles = true; o.FixTranslations = false; }));
            Assert.Contains(r.Items, x => x.Kind == MigrateItemKind.Info && x.Message.Contains("its own UI"));
            //  migrate adds the theme and the component styles
            Assert.Contains("sw/ui.css", Html(own));
            //  a page on the SysWeaver theme gets sw/ui.css also with upgrade
            var themed = "<script defer src=\"../common/common.js\"></script>\r\n<link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n";
            Assert.Contains("../sw/ui.css", Html(themed, out _, null, o => o.KeepStyles = true));
            //  upgrade leaves translation literals alone
            Assert.Equal("_T(\"a\\nb\");", Js2("_T(\"a\\nb\");", o => o.FixTranslations = false));
        }

        static String Js2(String code, Action<MigrateOptions> setup)
        {
            using var f = new TestFolder();
            f.Write("page/x.js", code);
            Run(f, setup);
            return f.Read("page/x.js");
        }

        [Fact]
        public void BaselineDryRun()
        {
            var baseline = BaselineRoot();
            if (!Directory.Exists(baseline))
                return;
            using var f = new TestFolder();
            CopyDir(baseline, f.Root);
            var before = HashTree(f.Root);
            var r = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, DryRun = true });
            Assert.Equal(before, HashTree(f.Root));
            Assert.True(r.FilesScanned > 100, "files " + r.FilesScanned);
            Assert.True(r.LegacyPages > 40, "legacy pages " + r.LegacyPages);
            Assert.True(r.PagesMigrated >= 3, "migrated " + r.PagesMigrated);
            Assert.True(r.Changes > 300, "changes " + r.Changes);
            Assert.True(r.ChangedFiles.Count > 30, "changed files " + r.ChangedFiles.Count);
            Assert.True(r.Warnings > 50, "warnings " + r.Warnings);
            Assert.DoesNotContain(r.Items, x => x.Message.Contains("overlapping"));
            // the shell entry is migrated with sw/shell.js
            Assert.Contains(r.Items, x => x.Path.EndsWith("HttpServer/srcWeb/index.html", StringComparison.Ordinal) && x.Message.Contains("sw/shell.js"));
            // nothing vendored / legacy bundle is touched
            Assert.DoesNotContain(r.ChangedFiles, x => x.EndsWith("common/common.js", StringComparison.Ordinal) || x.EndsWith("marked.js", StringComparison.Ordinal) || x.Contains("/external/"));
            // a real run followed by a second run changes nothing (idempotent)
            var r1 = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map });
            Assert.Equal(r.ChangedFiles, r1.ChangedFiles);
            var r2 = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map });
            Assert.Empty(r2.ChangedFiles);
        }
    }
}
