using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    [Collection("Console")]
    public class CheckerTests
    {
        const String Header = "/** @file test */\r\n";

        static List<Finding> Js(String code, String rel = "x.js", CheckOptions o = null)
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(code), o ?? new CheckOptions(), f));
            return f;
        }

        static List<Finding> Css(String code, String rel = "x.css")
        {
            var f = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext(rel, new SourceText(code), new CheckOptions(), f));
            return f;
        }

        static List<Finding> Html(String code, String rel = "x.html", CheckOptions o = null)
        {
            var f = new List<Finding>();
            Checker.CheckHtmlFile(new Checker.FileContext(rel, new SourceText(code), o ?? new CheckOptions(), f));
            return f;
        }

        static String[] Ids(List<Finding> f) => f.Select(x => x.Rule.Id).ToArray();

        [Theory]
        [InlineData("_T(\"Hello\");")]
        [InlineData("_T('Hello');")]
        [InlineData("_TF(\"Hello {0}\");")]
        [InlineData("_T( 'Hello {0}', a(b, c), 'The context');")]
        [InlineData("_TH(\"<b>x</b>\");")]
        [InlineData("_TH('x {0}', 1);")]
        [InlineData("function _T(text) { return text; }")]
        [InlineData("var _T = function (t) { return t; };")]
        [InlineData("if (typeof _T === \"function\") x._T = 1;")]
        [InlineData("var x_T = 1; x_T + 2;")]
        [InlineData("_T(\"it's\");")]
        public void ValidTranslation(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Theory]
        [InlineData("_T(`Hello`);", "tr-not-literal")]
        [InlineData("_T(`Hello ${a}`);", "tr-not-literal")]
        [InlineData("_T(text);", "tr-not-literal")]
        [InlineData("_T(\"a\" + b);", "tr-not-literal")]
        [InlineData("_T();", "tr-not-literal")]
        [InlineData("_T /* c */ (\"x\");", "tr-not-literal")]
        [InlineData("obj._T(\"x\");", "tr-member-call")]
        [InlineData("obj?._TF(\"x\");", "tr-member-call")]
        [InlineData("MAX_T(\"x\");", "tr-identifier-suffix")]
        [InlineData("my_TH (1);", "tr-identifier-suffix")]
        [InlineData("var s = \"use _T('x') here\";", "tr-in-text")]
        [InlineData("// call _TF( here", "tr-in-text")]
        [InlineData("/* _TH (x) */", "tr-in-text")]
        [InlineData("var s = `_T(\"x\")`;", "tr-in-text")]
        [InlineData("_T(\"a\\nb\");", "tr-newline")]
        [InlineData("_T('a\\\r\nb');", "tr-newline")]
        [InlineData("_T(\"x\", \"ctx\\n\");", "tr-newline")]
        [InlineData("_T(\"it's \\\"x\\\"\");", "tr-mixed-quotes")]
        [InlineData("_T('say \"hi\" it\\'s');", "tr-mixed-quotes")]
        [InlineData("var f = _T;", "tr-alias")]
        [InlineData("const t = _T; t(\"x\");", "tr-alias")]
        [InlineData("list.map(_TF);", "tr-alias")]
        public void InvalidTranslation(String code, String rule)
        {
            var f = Js(Header + code);
            Assert.Contains(rule, Ids(f));
        }

        [Fact]
        public void FindingPositionAndFormat()
        {
            var f = Js(Header + "  obj._T(\"x\");");
            var x = Assert.Single(f);
            Assert.Equal(2, x.Line);
            Assert.Equal(7, x.Col);
            Assert.StartsWith("x.js(2,7): tr-member-call: ", x.Format());
        }

        [Theory]
        [InlineData("var s = `${Session.User}`;")]
        [InlineData("var s = `a ${x} b ${ EnvInfo.AppName }`;")]
        [InlineData("var s = `${Color.Background}`;")]
        [InlineData("var s = `${Tr12}`;")]
        [InlineData("var s = \"${Request.Prefix}\";")]
        [InlineData("// ${#Env.Path}")]
        public void ServerVariablesInJs(String code)
        {
            Assert.Contains("template-literal-var", Ids(Js(Header + code)));
        }

        [Theory]
        [InlineData("var s = `${session.User}`;")]
        [InlineData("var s = `${Session}`;")]
        [InlineData("var s = `${a.Session.User}`;")]
        [InlineData("var s = `${x}` + '$' + '{Session.User}';")]
        public void NoServerVariablesInJs(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Theory]
        [InlineData("var a = 1;")]
        [InlineData("// @file x\r\nvar a;")]
        [InlineData("/* @file x */\r\nvar a;")]
        [InlineData("/** No file tag */\r\nvar a;")]
        [InlineData("")]
        public void MissingJsDocHeader(String code)
        {
            Assert.Equal(new[] { "jsdoc-header" }, Ids(Js(code)));
        }

        [Theory]
        [InlineData("/** @file x */")]
        [InlineData("\r\n\r\n/**\r\n * @file x\r\n */\r\nvar a;")]
        [InlineData("\"use strict\";\r\n/** @file x */\r\nvar a;")]
        public void JsDocHeaderPresent(String code)
        {
            Assert.Empty(Js(code));
        }

        [Fact]
        public void DebugMarkers()
        {
            Assert.Empty(Js(Header + "//DEBUG_BEGIN\r\nlog();\r\n  // DEBUG_END\r\n"));
            Assert.Equal(new[] { "debug-markers" }, Ids(Js(Header + "//DEBUG_BEGIN\r\nlog();\r\n")));
            Assert.Equal(new[] { "debug-markers" }, Ids(Js(Header + "//DEBUG_END\r\n")));
            Assert.Equal(new[] { "debug-markers" }, Ids(Js(Header + "//DEBUG_BEGIN\r\n//DEBUG_BEGIN\r\n//DEBUG_END\r\n")));
        }

        [Fact]
        public void SyntaxErrors()
        {
            Assert.Contains("syntax", Ids(Js(Header + "var s = 'abc\r\n")));
            Assert.Contains("syntax", Ids(Js(Header + "var s = `abc")));
        }

        [Fact]
        public void TemplatedHtml()
        {
            var ok = "<html><head><title>${#EnvInfo.AppDisplayName}</title></head><body title=\"${@Session.NickName}\">${#Tr1} ${Env.Path}</body></html>";
            Assert.Empty(Html(ok, "index.html"));
            // Not templated: no template checks (but JS template literals are still checked in scripts)
            Assert.Empty(Html("<p>${Foo}</p>", "page.html"));
            var bad = Html("<p>${Foo} ${EnvInfo.Nope} ${#Session.Lang}</p><script>var a = `${x}`;</script>", "index.html");
            Assert.Equal(new[] { "template-var", "template-var", "template-var" }, Ids(bad));
            var o = new CheckOptions();
            o.ExtraTemplateVars.Add("Foo");
            Assert.Single(Html("<p>${Foo} ${EnvInfo.Nope}</p>", "index.html", o));
            // Case sensitive path: only the root index.html and app/Home.html are templated
            Assert.Empty(Html("<p>${Foo}</p>", "app/home.html"));
            Assert.Single(Html("<p>${Foo}</p>", "app/Home.html"));
        }

        [Fact]
        public void TemplatedCss()
        {
            Assert.Empty(Css(":root {\r\n  --ThemeBackground: ${Color.Background};\r\n  /* c */ --ThemeMain: ${Color.Color};\r\n}", "common/theme.css"));
            Assert.Equal(new[] { "template-css" }, Ids(Css("body { color: ${Color.Color}; }", "common/theme.css")));
            Assert.Equal(new[] { "template-var" }, Ids(Css(":root { --X: ${Nope}; }", "common/theme.css")));
            // Other css files aren't templated
            Assert.Empty(Css("body { color: ${Color.Color}; }", "app/x.css"));
            Assert.Equal(new[] { "tr-in-text" }, Ids(Css(".x::after { content: \"_T(x)\"; }")));
        }

        [Fact]
        public void HtmlTranslationAndScripts()
        {
            Assert.Equal(new[] { "tr-in-text" }, Ids(Html("<button onclick=\"alert(_T('x'))\">b</button>")));
            Assert.Equal(new[] { "tr-in-text" }, Ids(Html("<p>Use _T(\"x\") in scripts</p>")));
            Assert.Equal(new[] { "tr-in-text" }, Ids(Html("<!-- _T('x') -->")));
            Assert.Empty(Html("<script>var a = _T(\"Hello\");</script>"));
            Assert.Equal(new[] { "tr-not-literal" }, Ids(Html("<script>var a = _T(`Hello`);</script>")));
            Assert.Equal(new[] { "tr-in-text" }, Ids(Html("<script type=\"text/template\">_T(x)</script>")));
            var f = Html("<p>\r\n</p><script>\r\n  obj._T('x');</script>");
            Assert.Equal(3, f[0].Line);
            Assert.Equal(7, f[0].Col);
        }

        [Fact]
        public void FileAndFolderNames()
        {
            var f = new List<Finding>();
            Checker.CheckFileName("app/a.b.js", f);
            Checker.CheckFileName("app/x.min.js", f);
            Checker.CheckFileName("app/x.mjs", f);
            Checker.CheckFileName("app/README.md", f);
            Checker.CheckFileName("app/floating-cogs.svg", f);
            Checker.CheckFolderName("my-folder", f);
            Checker.CheckFolderName("a/3d", f);
            Checker.CheckFolderName("ok", f);
            Checker.CheckFolderName("my.pages", f);
            Checker.CheckFolderName("icons/.opt", f);
            Assert.Equal(new[] { "file-name-dots", "mjs-file", "folder-name-dash", "folder-name-digit", "folder-name-dot" }, Ids(f));
            Assert.Contains("served as \"a/b.js\"", f[0].Message);
            Assert.Equal(Severity.Warning, f[3].Severity);
            Assert.Equal("my-folder/: folder-name-dash: folder name \"my-folder\" contains '-' (embedded as 'my_folder', so its URLs change)", f[2].Format());
        }

        [Fact]
        public void PathMatcher()
        {
            var m = new PathMatcher(CheckOptions.DefaultSkip);
            Assert.True(m.IsFileMatch("chart/external/chart.js"));
            Assert.True(m.IsFileMatch("common/marked.js"));
            Assert.True(m.IsFileMatch("common/uzip.js"));
            Assert.True(m.IsFileMatch("x/jquery.min.js"));
            Assert.True(m.IsFolderMatch("icons/.opt"));
            Assert.True(m.IsFolderMatch("edit/external"));
            Assert.False(m.IsFileMatch("common/common.js"));
            Assert.False(m.IsFolderMatch("explore"));
            var p = new PathMatcher(new[] { "app/**/gen*.js", "docs/" });
            Assert.True(p.IsFileMatch("app/gen1.js"));
            Assert.True(p.IsFileMatch("app/a/b/gen2.js"));
            Assert.False(p.IsFileMatch("app/x.js"));
            Assert.True(p.IsFileMatch("a/docs/x.html"));
        }

        [Fact]
        public void LegacyGlobalsList()
        {
            var o = new CheckOptions();
            Assert.Contains("sendRequest", o.LegacyGlobals);
            Assert.Contains("PopUpMenu", o.LegacyGlobals);
            Assert.DoesNotContain("_T", o.LegacyGlobals);
            Assert.DoesNotContain("setTheme", o.LegacyGlobals);
            Assert.Equal(new[] { "A", "B" }, CheckOptions.ParseLegacyGlobals("# comment\r\nA\r\n\r\n B # trailing\r\nappMain\r\n"));
        }

        static TestFolder SetupSite()
        {
            var f = new TestFolder();
            f.Write("sw/core.js", Header + "var Sw = {};");
            f.Write("common/common.js", Header + "function sendRequest() {}");
            f.Write("common/marked.js", "vendored, no header, _T(x)");
            f.Write("chart/external/lib.js", "vendored");
            f.Write("page/new.html", "<html><head><script defer src=\"../sw/core.js\"></script><script defer src=\"new.js\"></script></head><body onload=\"Info('x')\"><script>setTheme('dark'); Fail('y');</script></body></html>");
            f.Write("page/new.js", Header + "function Message() {}\r\nMessage(); sendRequest('a'); Sw.Net.Send('a'); obj.Fail(); var o = { Info: 1, PopUp: 2 }; appMain();");
            f.Write("page/old.html", "<html><head><script defer src=\"../common/common.js\"></script><script defer src=\"old.js\"></script></head><body></body></html>");
            f.Write("page/old.js", Header + "sendRequest('a');");
            f.Write("page/mixed.html", "<html><head><script src=\"../sw/core.js\"></script><script src=\"../common/common.js\"></script></head></html>");
            return f;
        }

        [Fact]
        public void CheckFolderStrictLegacyGlobals()
        {
            using var f = SetupSite();
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true });
            var got = r.Findings.Select(x => x.Path + " " + x.Rule.Id + " " + x.Message.Split('"')[1]).ToArray();
            Assert.Equal(new[]
            {
                "page/mixed.html legacy-include ../common/common.js",
                "page/new.html legacy-global Info",
                "page/new.html legacy-global Fail",
                "page/new.js legacy-global sendRequest",
            }, got);
            Assert.Contains("by the new-format page \"page/new.html\"", r.Findings[3].Message);
            Assert.Equal(1, r.FoldersSkipped);
            Assert.Equal(1, r.FilesSkipped);
        }

        static String[] Legacy(String code)
        {
            var f = new List<Finding>();
            var ctx = new Checker.FileContext("x.js", new SourceText(code), new CheckOptions { Strict = true }, f);
            Checker.CheckLegacyGlobals(ctx, JsLexer.Tokenize(code), null);
            return f.Select(x => x.Message.Split('"')[1] + "@" + x.Line).ToArray();
        }

        [Fact]
        public void LegacyGlobalsIgnoreMemberDeclarations()
        {
            //  Class methods, static / async / get / set / generator members, class fields: declarations, not global references
            var code =
                "class Tools {\n" +
                "    static delay(ms) { return 1; }\n" +
                "    static async waitEvent2(e) { }\n" +
                "    async Cache(k) { }\n" +
                "    IsAttached(e) { return true; }\n" +
                "    get Fail() { return 1; }\n" +
                "    set Info(v) { }\n" +
                "    *Message() { }\n" +
                "    Open = 1;\n" +
                "    static Confirm;\n" +
                "    static { this.x = 1; }\n" +
                "}\n" +
                "const C = class extends Tools { PopUp() { } };\n" +
                "const o = { delay(ms) { }, async Cache() { }, get Info() { return 1; }, Fail: 1 };\n" +
                "this.delay(1); obj.Cache(); a?.Info; t.Open = 2;\n";
            Assert.Empty(Legacy(code));
        }

        [Fact]
        public void LegacyGlobalsStillFlagReferences()
        {
            var code =
                "class Tools {\n" +
                "    static run(ms) { delay(ms); return Fail; }\n" +
                "    x = Info;\n" +
                "    [Message]() { }\n" +
                "}\n" +
                "const o = { k: delay, Open };\n" +
                "if (a) { Cache(1); }\n" +
                "await IsAttached(e);\n" +
                "let f = () => { waitEvent2(x) };\n";
            Assert.Equal(new[] { "delay@2", "Fail@2", "Info@3", "Message@4", "delay@6", "Open@6", "Cache@7", "IsAttached@8", "waitEvent2@9" }, Legacy(code));
        }

        [Fact]
        public void CheckFolderCompatPagesWarn()
        {
            using var f = new TestFolder();
            f.Write("page/m.html", "<html><head><script defer src=\"../sw/core.js\"></script><script defer src=\"../sw/ui.js\"></script><script defer src=\"../sw/compat.js\"></script><script defer src=\"m.js\"></script><script defer src=\"shared.js\"></script></head><body onload=\"Info('x')\"></body></html>");
            f.Write("page/m.js", Header + "Fail('y');");
            f.Write("page/n.html", "<html><head><script defer src=\"../sw/core.js\"></script><script defer src=\"shared.js\"></script></head></html>");
            f.Write("page/shared.js", Header + "PopUp(1);");
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true });
            var got = r.Findings.Select(x => x.Path + " " + x.Rule.Id + " " + x.Severity + " " + x.Message.Split('"')[1]).ToArray();
            Assert.Equal(new[]
            {
                "page/m.html legacy-compat Warning Info",
                "page/m.js legacy-compat Warning Fail",
                "page/shared.js legacy-global Error PopUp",
            }, got);
            Assert.Contains("sw/compat.js", r.Findings[1].Message);
        }

        [Fact]
        public void CoreThemeGlobalsAreNotLegacy()
        {
            //  core/theme.js defines getTheme / applyTheme / appendTheme (ThrowItUp's pages call applyTheme())
            using var f = new TestFolder();
            f.Write("page/m.html", "<html><head><script defer src=\"../sw/core.js\"></script><script defer src=\"m.js\"></script></head><body onload=\"applyTheme()\"></body></html>");
            f.Write("page/m.js", Header + "applyTheme(); getTheme(); appendTheme(\"x.css\"); setTheme(\"dark\");");
            Assert.Empty(Checker.CheckFolder(f.Root, new CheckOptions { Strict = true }).Findings);
            //  The same set as the names migrate never touches
            Assert.Equal(SwWeb.Migrating.MigrationMap.NeverTouch.OrderBy(x => x, StringComparer.Ordinal), CheckOptions.PermanentGlobals.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Theory]
        [InlineData("../sw/core.js", true)]
        [InlineData("sw/core.js", true)]
        [InlineData("${@Base Url}sw/core.js", true)]
        [InlineData("${@Base Url}/sw/core.js?v=1", true)]
        [InlineData("$(BASE)sw/core.js", true)]
        [InlineData("${@Base Url}${Other}sw/core.js", true)]
        [InlineData("${@Base Url}xsw/core.js", false)]
        [InlineData("${@Base Url sw/core.js", false)]
        [InlineData("../sw/core.jsx", false)]
        public void IsSrcSkipsTemplatePrefixes(String src, bool expected)
        {
            Assert.Equal(expected, Checker.IsSrc(src, "sw/core.js"));
        }

        [Fact]
        public void CheckFolderStrictTemplatedPages()
        {
            //  srcData templates migrated by SwWeb migrate: "${@Base Url}" prefixed includes are new-format includes (CR4-Docs)
            using var f = new TestFolder();
            f.Write("us/embed.html",
                "<html><head><title>${#Title Text}</title>\r\n" +
                "<script defer src=\"${@Base Url}sw/core.js\"></script><script defer src=\"${@Base Url}sw/ui.js\"></script><script defer src=\"${@Base Url}sw/compat.js\"></script>\r\n" +
                "<link rel=\"stylesheet\" href=\"${@Base Url}common/theme.css\">" +
                "<link rel=\"stylesheet\" href=\"${@Base Url}sw/ui.css\"><link rel=\"stylesheet\" href=\"${@Base Url}sw/compat.css\">\r\n" +
                "<script>\r\nasync function main() {\r\n    const items = WebMenu.From(await Sw.Net.Send(\"${$Base Url}Api/application/GetMenu\", 1)).Items;\r\n    let delay = 100;\r\n    ColorIcon(delay);\r\n}\r\n</script></head>\r\n" +
                "<body onload=\"main()\"><iframe src=\"${@Url Name}\"></iframe></body></html>");
            f.Write("us/old.html", "<html><head><script defer src=\"${@Base Url}common/common.js\"></script><script>ColorIcon(1);</script></head></html>");
            f.Write("us/mixed.html", "<html><head><script src=\"${@Base Url}sw/core.js\"></script><script src=\"${@Base Url}app/application.js\"></script></head></html>");
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true });
            var got = r.Findings.Select(x => x.Path + " " + x.Rule.Id + " " + x.Severity + " " + x.Message.Split('"')[1]).ToArray();
            Assert.Equal(new[]
            {
                "us/embed.html legacy-compat Warning WebMenu",
                "us/embed.html legacy-compat Warning ColorIcon",
                "us/mixed.html legacy-include Error ${@Base Url}app/application.js",
            }, got);
        }

        [Theory]
        [InlineData("import x from \"./x.js\";", "es-module")]
        [InlineData("import { a, b } from './x.js';", "es-module")]
        [InlineData("import * as m from \"./x.js\";", "es-module")]
        [InlineData("import \"./x.js\";", "es-module")]
        [InlineData("import x from \"https://cdn.example.com/x.js\";", "es-module,external-resource")]
        [InlineData("const m = await import(\"./x.js\");", "es-module")]
        [InlineData("import(\"//cdn.example.com/x.js\").then(m => m);", "es-module,external-resource")]
        [InlineData("const u = import.meta.url;", "es-module")]
        [InlineData("export const a = 1;", "es-module")]
        [InlineData("export default function f() {}", "es-module")]
        [InlineData("export { a };", "es-module")]
        [InlineData("export * from \"./x.js\";", "es-module")]
        public void EsModuleJs(String code, String rules)
        {
            Assert.Equal(rules.Split(','), Ids(Js(Header + code)));
        }

        [Theory]
        [InlineData("const o = { import: 1, export: 2 };")]
        [InlineData("o.import(); o?.export;")]
        [InlineData("class C { import() { return 1; } export(a) { } }")]
        [InlineData("const o = { import(x) { return x; } };")]
        [InlineData("const s = \"import x from 'https://cdn.example.com/x.js'\"; // import(\"x\")")]
        [InlineData("Sw.Net.IncludeJs(\"../chart/chart.js\");")]
        public void NoEsModuleJs(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Theory]
        [InlineData("<script src=\"https://cdn.example.com/x.js\"></script>", "external-resource")]
        [InlineData("<script defer src='//cdn.example.com/x.js'></script>", "external-resource")]
        [InlineData("<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Nunito\">", "external-resource")]
        [InlineData("<link rel=\"shortcut icon\" href=\"http://example.com/favicon.ico\">", "external-resource")]
        [InlineData("<img src=\"https://example.com/a.png\">", "external-resource")]
        [InlineData("<script type=\"module\" src=\"m.js\"></script>", "es-module")]
        [InlineData("<script type=\"importmap\">{}</script>", "es-module")]
        [InlineData("<script type=\"module\">import x from \"./x.js\";</script>", "es-module,es-module")]
        [InlineData("<script>export const a = 1;</script>", "es-module")]
        [InlineData("<script src=\"/sw/core.js\"></script>", "root-relative-url")]
        [InlineData("<style>body { background: url(\"https://example.com/a.png\"); }</style>", "external-resource")]
        public void ExternalAndModuleHtml(String code, String rules)
        {
            Assert.Equal(rules.Split(','), Ids(Html(code)));
        }

        [Theory]
        [InlineData("<script defer src=\"../sw/core.js\"></script><script defer src=\"../explore/table.js\"></script>")]
        [InlineData("<script defer src=\"${@Base Url}sw/core.js\"></script>")]
        [InlineData("<a href=\"https://example.com\">a link is not a resource</a>")]
        [InlineData("<link rel=\"canonical\" href=\"https://example.com/\">")]
        [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
        [InlineData("<style>/* @import url(https://fonts.example.com/f.css); */ a { background: url(../icons/a.svg); }</style>")]
        public void NoExternalHtml(String code)
        {
            Assert.Empty(Html(code));
        }

        [Theory]
        [InlineData("@import url(\"https://fonts.googleapis.com/css2?family=Nunito\");", "external-resource")]
        [InlineData("@import 'https://example.com/a.css';", "external-resource")]
        [InlineData(".a { background: url(//example.com/a.png); }", "external-resource")]
        [InlineData(".a { background: url(/icons/a.svg); }", "root-relative-url")]
        [InlineData("@import url(other.css); .a { background: url('../icons/a.svg'), url(data:image/png;base64,AA==); }", "")]
        [InlineData("/* @import url(https://fonts.example.com/f.css); */ .a { color: red; }", "")]
        public void ExternalCss(String code, String rules)
        {
            Assert.Equal(rules.Length == 0 ? Array.Empty<String>() : rules.Split(','), Ids(Css(code)));
        }

        [Fact]
        public void CheckFolderExternalFailsCheck()
        {
            //  The QuickStart "Rules that bite" example: an external script, an ES module and a font import must fail "check" (S2R1-Docs-1)
            using var f = new TestFolder();
            f.Write("hello/hello.html", "<html><head><script defer src=\"../sw/core.js\"></script><script src=\"https://cdn.example.com/x.js\"></script></head></html>");
            f.Write("hello/hello.js", Header + "import x from \"https://cdn.example.com/x.js\";\r\n");
            f.Write("hello/hello.css", "@import url(\"https://fonts.googleapis.com/css2?family=Nunito\");\r\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Equal(new[]
            {
                "hello/hello.css external-resource",
                "hello/hello.html external-resource",
                "hello/hello.js es-module",
                "hello/hello.js external-resource",
            }, r.Findings.Select(x => x.Path + " " + x.Rule.Id).ToArray());
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "check", f.Root, "--quiet" }));
        }

        [Theory]
        [InlineData("<iframe src=\"https://www.youtube.com/embed/x\"></iframe>", "external-resource")]
        [InlineData("<frame src=\"//example.com/f.html\">", "external-resource")]
        [InlineData("<embed src=\"https://example.com/a.swf\">", "external-resource")]
        [InlineData("<object data=\"https://example.com/a.pdf\"></object>", "external-resource")]
        [InlineData("<video src=\"https://example.com/a.mp4\" poster=\"https://example.com/a.png\"></video>", "external-resource,external-resource")]
        [InlineData("<audio src=\"https://example.com/a.mp3\"></audio>", "external-resource")]
        [InlineData("<video><source src=\"https://example.com/a.webm\"><track src=\"https://example.com/a.vtt\"></video>", "external-resource,external-resource")]
        [InlineData("<img src=\"a.png\" srcset=\"a2.png 2x, https://example.com/a3.png 3x\">", "external-resource")]
        [InlineData("<input type=\"image\" src=\"https://example.com/go.png\">", "external-resource")]
        [InlineData("<iframe src=\"/x.html\"></iframe>", "root-relative-url")]
        public void ExternalMediaHtml(String code, String rules)
        {
            Assert.Equal(rules.Split(','), Ids(Html(code)));
        }

        [Theory]
        [InlineData("<iframe src=\"../mediaView/MapView.html\"></iframe>")]
        [InlineData("<iframe src=\"about:blank\"></iframe><video src=\"blob:x\"></video>")]
        [InlineData("<!-- SwWeb:allow-online (optional map) -->\r\n<iframe src=\"https://maps.example.com/embed\"></iframe>")]
        [InlineData("<iframe src=\"https://maps.example.com/embed\"></iframe> <!-- SwWeb:allow-online -->")]
        public void NoExternalMediaHtml(String code)
        {
            Assert.Empty(Html(code));
        }

        [Theory]
        [InlineData("fetch(\"https://api.example.com/data\");", "external-request")]
        [InlineData("window.fetch('https://api.example.com/data').then(r => r);", "external-request")]
        [InlineData("fetch(\"https://api.example.com/range/\" + id, { mode: \"cors\" });", "external-request")]
        [InlineData("fetch(`https://api.example.com/${id}`);", "external-request")]
        [InlineData("const x = new XMLHttpRequest(); x.open(\"GET\", \"https://api.example.com/x\");", "external-request")]
        [InlineData("const ws = new WebSocket(\"wss://example.com/s\");", "external-request")]
        [InlineData("const es = new EventSource(\"https://example.com/events\");", "external-request")]
        [InlineData("navigator.sendBeacon(\"https://stats.example.com/\", d);", "external-request")]
        [InlineData("importScripts(\"//cdn.example.com/w.js\");", "external-request")]
        [InlineData("img.src = \"https://example.com/a.png\";", "external-request")]
        [InlineData("frame.setAttribute(\"src\", \"https://www.youtube.com/embed/x\");", "external-request")]
        public void ExternalRequestJs(String code, String rules)
        {
            Assert.Equal(rules.Split(','), Ids(Js(Header + code)));
        }

        [Theory]
        [InlineData("fetch(\"../Api/x\"); fetch(url); fetch(base + \"https://x\");")]
        [InlineData("window.open(\"https://example.com/help\", \"_blank\");")]
        [InlineData("a.href = \"https://example.com/\";")]
        [InlineData("const u = \"https://example.com/\"; // a link text, not a request")]
        [InlineData("o.fetch(\"https://example.com/x\");")]
        [InlineData("x.open(\"https://example.com/\");")]
        [InlineData("img.src = \"data:image/png;base64,AA==\"; img.src == \"https://x\";")]
        [InlineData("//  SwWeb:allow-online (optional, degrades offline)\r\nconst r = await fetch(\"https://api.example.com/data\");")]
        [InlineData("const r = await fetch(\"https://api.example.com/data\"); // SwWeb:allow-online")]
        public void NoExternalRequestJs(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Fact]
        public void ExternalRequestInlineScript()
        {
            Assert.Equal(new[] { "external-request" }, Ids(Html("<script>fetch(\"https://api.example.com/data\");</script>")));
        }

        [Theory]
        [InlineData("function f() { return [1, 2; }", "syntax")]
        [InlineData("function f() { if (a) { b(); }", "syntax")]
        [InlineData("const a = (1 + 2));", "syntax")]
        [InlineData("const a = [1, 2);", "syntax")]
        [InlineData("const s = `a ${ (b }`;", "syntax")]
        [InlineData("}", "syntax")]
        [InlineData("const s = \"abc;", "syntax")]
        public void UnbalancedBrackets(String code, String rules)
        {
            Assert.Equal(rules.Split(','), Ids(Js(Header + code)));
        }

        [Theory]
        [InlineData("function f(a) { return { b: [a, (a + 1)], c: `x ${a ? `y ${a}` : \"}\"} z` }; }")]
        [InlineData("const r = /[(]/g.test(s) ? a / (b) : '{';")]
        [InlineData("class C { m() { return this.#x?.[0] ?? {}; } #x = []; }")]
        [InlineData("label: for (const x of y) { if (x) { continue label; } }")]
        public void BalancedBrackets(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Fact]
        public void UnbalancedBracketsReportsOnce()
        {
            var f = Js(Header + "function f() {\r\n  a(;\r\n}\r\n]\r\n");
            Assert.Single(f);
            Assert.Equal("syntax", f[0].Rule.Id);
            Assert.Contains("closes '('", f[0].Message);
            Assert.Equal(4, f[0].Line);
        }

        [Fact]
        public void UnbalancedBracketsInlineScript()
        {
            Assert.Equal(new[] { "syntax" }, Ids(Html("<script>function f() { a(); </script>")));
        }

        [Theory]
        [InlineData("@import url(\"brand2.css\");", "css-import")]
        [InlineData("@import 'x.css'; :root { --a: 1; }", "css-import")]
        [InlineData(":root { --sw-brand-icon: url(\"acme-mark.svg\"); }", "brand-url")]
        [InlineData(":root { --sw-brand-icon: url(./acme-mark.svg); --sw-brand-logo: url('img/logo.svg'); }", "brand-url,brand-url")]
        [InlineData(":root { --sw-brand-icon: url(\"../app/acme-mark.svg\"); --x: url(data:image/svg+xml,AA); --y: url(#a); }", "")]
        [InlineData(".logo { background: url(acme.svg); } /* --a: url(b.svg); */", "")]
        public void BrandCss(String code, String rules)
        {
            Assert.Equal(rules.Length == 0 ? Array.Empty<String>() : rules.Split(','), Ids(Css(code, "app/app.css")));
        }

        [Fact]
        public void BrandRulesOnlyInAppCss()
        {
            Assert.Empty(Css("@import url(\"other.css\"); :root { --sw-brand-icon: url(\"acme-mark.svg\"); }", "mine/mine.css"));
            Assert.Equal(new[] { "css-import" }, Ids(Css("@import url(\"other.css\");", "srcWeb/app/app.css")));
        }

        [Fact]
        public void CheckFolderBrandAndFonts()
        {
            //  A branding probe: a local @import in app.css, a mark url not written as ../app/, a font file (S2R2-Platform)
            using var f = new TestFolder();
            f.Write("app/app.css", "@import url(\"brand2.css\");\r\n:root { --sw-brand-icon: url(\"acme-mark.svg\"); }\r\n@font-face { font-family: B; src: url(fonts/b.woff2); }\r\n");
            f.Write("app/brand2.css", ":root { --a: 1; }\r\n");
            f.Write("app/fonts/b.woff2", "x");
            f.Write("brand/fonts/Barlow-Regular.ttf", "x");
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Equal(new[]
            {
                "app/app.css css-import Error",
                "app/app.css brand-url Error",
                "app/fonts/b.woff2 font-file Warning",
            }, r.Findings.Select(x => x.Path + " " + x.Rule.Id + " " + x.Severity).ToArray());
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "check", f.Root, "--quiet" }));
        }

        [Fact]
        public void CheckFolderArchitectProbe()
        {
            //  An offline probe: an iframe and a literal fetch of the internet
            using var f = new TestFolder();
            f.Write("myfolder/q.html", "<html><body><!-- SwWeb:no-framework (a probe) --><iframe src=\"https://www.youtube.com/embed/x\"></iframe></body></html>");
            f.Write("myfolder/y.js", Header + "fetch(\"https://api.example.com/data\").then(r => r.json());\r\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true });
            Assert.Equal(new[]
            {
                "myfolder/q.html external-resource",
                "myfolder/y.js external-request",
            }, r.Findings.Select(x => x.Path + " " + x.Rule.Id).ToArray());
        }

        [Theory]
        [InlineData("Sw.Net.IncludeJs(null, \"https://cdn.example.com/lib.js\");")]
        [InlineData("Sw.Net.IncludeCss(\"https://cdn.example.com/\", \"lib.css\");")]
        [InlineData("await Sw.Net.IncludeJs(null, '//cdn.example.com/lib.js', true);")]
        [InlineData("N.IncludeCss(null, `https://cdn.example.com/${v}/lib.css`);")]
        [InlineData("includeJs(null, \"https://cdn.example.com/lib.js\");")]
        [InlineData("includeCss(document.currentScript.src, \"http://cdn.example.com/lib.css\");")]
        public void ExternalInclude(String code)
        {
            //  A porting probe
            Assert.Equal(new[] { "external-resource" }, Ids(Js(Header + code)));
        }

        [Theory]
        [InlineData("Sw.Net.IncludeJs(null, \"marked.js\"); Sw.Net.IncludeCss(null, css, true); includeJs(current, \"../x/y.js\");")]
        [InlineData("const IncludeJs = (baseUrl, file) => include(baseUrl, file);")]
        [InlineData("// SwWeb:allow-online (optional, degrades offline)\r\nSw.Net.IncludeJs(null, \"https://cdn.example.com/lib.js\");")]
        public void NoExternalInclude(String code)
        {
            Assert.Empty(Js(Header + code));
        }

        [Theory]
        //  The WebBranding.md step 2 and step 3 examples (a // comment, no JSDoc header): app/app.js is exempt from jsdoc-header
        [InlineData("//  The colors are in app/app.css (no Colors here), the Title adds it to the settings menu's Theme section\r\nSw.Theme.Register(\"acme\", { Title: \"Acme\", Description: \"The Acme brand colors\", Scheme: \"light\" });\r\n")]
        [InlineData("Sw.Theme.Register(\"night\", {\r\n    Title: \"Night\",\r\n    Description: \"Dark blue with an orange accent\",\r\n    Scheme: \"dark\",\r\n    Colors: { Background: \"#0b1a33\", Foreground: \"#d8e1f0\", Accent1: \"#ff6a00\", Accent2: \"#4fc3f7\" },\r\n});\r\n")]
        [InlineData("")]
        public void BrandJsNeedsNoHeader(String code)
        {
            Assert.Empty(Js(code, "app/app.js"));
            Assert.Empty(Js(code, "srcWeb/app/app.js"));
            //  (another file is page code: its theme names are texts for tr-untranslated, the brand hook's are brand names)
            Assert.Equal(new[] { "jsdoc-header" }, Ids(Js(code, "app/other.js")).Where(x => x != "tr-untranslated").ToArray());
        }

        [Fact]
        public void BrandJsOtherRulesStillApply()
        {
            Assert.Equal(new[] { "external-request" }, Ids(Js("fetch(\"https://api.example.com/x\");", "app/app.js")));
        }

        [Theory]
        //  A FakeText porting failure: a page that worked before the port
        [InlineData("button{width:100%} input{border:0} body{overflow:hidden}", "body-overflow,element-css,element-css")]
        [InlineData("button { width: 100%; }", "element-css")]
        [InlineData("button:hover, .x { color: red; }", "element-css")]
        [InlineData("form input[type=text] { border: 0; }", "element-css")]
        [InlineData("html body select { width: 100%; }", "element-css")]
        [InlineData("@media (max-width: 600px) { textarea { height: 10px; } }", "element-css")]
        [InlineData("html, body { height: 100%; overflow: hidden; }", "body-overflow")]
        [InlineData("body { overflow-y: clip !important; }", "body-overflow")]
        [InlineData("body { font-size: 20px; }", "element-css")]
        [InlineData("body { zoom: 1.2; }", "element-css")]
        public void ElementCss(String code, String rules)
        {
            var ids = Ids(Css(code, "mypage/page.css"));
            Array.Sort(ids, StringComparer.Ordinal);
            Assert.Equal(rules.Split(','), ids);
        }

        [Theory]
        [InlineData(".my-page button { width: 100%; } #f input { border: 0; } [data-x] select { width: 1px; }")]
        [InlineData("button.QuickLink { width: 100%; } input.sw-x:focus { border: 0; } button[data-act] { x: 1; }")]
        [InlineData(":where(.layout) button { width: 100%; } body.my-page { overflow: hidden; } html.Embedded body { overflow: hidden; }")]
        [InlineData("html, body { background: transparent; } body { margin: 0; padding: 0 8px; height: 100%; overflow-x: hidden; font-family: var(--sw-sans); }")]
        [InlineData("body { font-size: var(--sw-font-size); } button {} /* button { width: 100%; } */")]
        [InlineData("@keyframes button { from { opacity: 0; } } @font-face { font-family: input; src: url(data:x); }")]
        [InlineData("/* SwWeb:allow-element-css (a full window app, its panes scroll) */\r\nhtml, body { overflow: hidden; }\r\nbutton { x: 1; } /* SwWeb:allow-element-css */")]
        public void NoElementCss(String code)
        {
            Assert.Empty(Css(code, "mypage/page.css"));
        }

        [Fact]
        public void ElementCssNotInFrameworkCss()
        {
            const String code = "button { width: 100%; } body { overflow: hidden; }";
            foreach (var rel in new[] { "ui/base.css", "sw/ui.css", "common/theme.css", "core/x.css", "shell/shell.css", "brand/x.css", "app/application.css" })
                Assert.Empty(Css(code, rel));
            //  The brand style sheet reaches every page: checked
            Assert.Equal(2, Css(code, "app/app.css").Count);
        }

        [Fact]
        public void ElementCssInStyleElement()
        {
            Assert.Equal(new[] { "element-css" }, Ids(Html("<html><head><script defer src=\"../sw/core.js\"></script><style>\r\n  input { border: 0; }\r\n</style></head><body></body></html>")));
            Assert.Equal(new[] { "element-css" }, Ids(Html("<style>\r\n  input { border: 0; }\r\n</style>")));   // a fragment of a page
            Assert.Empty(Html("<html><head><style>input { border: 0; }</style></head><body></body></html>"));   // a document without the bundles (export, mail)
            Assert.Empty(Html("<html><head><style>.p input { border: 0; } html, body { margin: 0; }</style></head></html>"));
        }

        [Theory]
        [InlineData("button", "button")]
        [InlineData("div > button:hover", "button")]
        [InlineData("html body", "body")]
        [InlineData("body.x", null)]
        [InlineData(".a button", null)]
        [InlineData("input[type=\"checkbox\"]", "input")]
        [InlineData("input[data-role]", null)]
        [InlineData("*", null)]
        //  A custom element of the page scopes like a class (ThrowItUp "tiu-display input"); the framework's elements don't
        [InlineData("tiu-display input", null)]
        [InlineData("Fenrir-Panel > div button:hover", null)]
        [InlineData("sw-dialog input", "input")]
        [InlineData("SysWeaver-Button button", "button")]
        [InlineData("tiu-display", "tiu-display")]
        public void BareElement(String selector, String expected)
        {
            Assert.Equal(expected, Checker.GetBareElement(selector));
        }

        [Fact]
        public void CheckFolderNonStrict()
        {
            using var f = SetupSite();
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Empty(r.Findings);
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "check", f.Root }));
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "check", f.Root, "--strict", "--quiet" }));
            // Without the default skip list the vendored file is checked (no header, _T in text)
            Assert.Equal(SwWebProgram.ExitProblems, SwWebProgram.Run(new[] { "check", f.Root, "--no-default-skip", "--quiet" }));
            Assert.Equal(SwWebProgram.ExitSuccess, SwWebProgram.Run(new[] { "check", f.Root, "--no-default-skip", "--skip", "marked.js", "--skip", "external/", "--quiet" }));
        }

        [Fact]
        public void SubFolderIsClassifiedLikeTheWebRoot()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/common/theme.css", "/* framework */\r\ninput[type=password]::-ms-reveal { display: none; }\r\n");
            f.Write("srcWeb/brand/fonts/X.ttf", "font");
            f.Write("srcWeb/page/page.css", "/* page */\r\ninput { color: red; }\r\n");
            Assert.Equal("", Checker.GetWebPrefix(f.PathOf("srcWeb")));
            Assert.Equal("common/", Checker.GetWebPrefix(f.PathOf("srcWeb/common")));
            Assert.Equal("", Checker.GetWebPrefix(f.Root));
            String Ids(String rel) => String.Join(",", Checker.CheckFolder(f.PathOf(rel), new CheckOptions { Strict = true }).Findings.Select(x => x.Rule.Id));
            //  Framework css and the brand font sources are allowed whether the web root or the sub folder is checked
            Assert.Equal("", Ids("srcWeb/common"));
            Assert.Equal("", Ids("srcWeb/brand"));
            //  Page css is still checked from the sub folder
            Assert.Equal("element-css", Ids("srcWeb/page"));
            Assert.Equal("element-css", Ids("srcWeb"));
        }
    }
}
