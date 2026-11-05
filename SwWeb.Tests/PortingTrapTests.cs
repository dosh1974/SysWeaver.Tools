using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    /// <summary>
    /// Porting traps found by porting real apps: the dry run worksheet equals check, check / migrate on a folder above
    /// the web roots, sw-root, inline Sw before the deferred bundles, library includes (Sw.Table, Sw.Shell ...), window state, zoom blocking
    /// viewports, pills, element rules on tables and pictures.
    /// </summary>
    [Collection("Console")]
    public class PortingTrapTests
    {
        const String Header = "/** @file test */\r\n";

        static readonly MigrationMap Map = MigrationMap.Load();

        static CheckOptions Opt(bool design = true, bool strict = true) => new CheckOptions { Design = design, Strict = strict, Map = Map };

        static MigrateResult Migrate(TestFolder f, MigrateLevel level = MigrateLevel.Api, bool dryRun = false) =>
            Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = false, Level = level, DryRun = dryRun });

        static String Page(String body, String head = "", String bodyTag = "<body>") =>
            "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"
            + "    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n" + head
            + "    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n</head>\r\n" + bodyTag + "\r\n" + body + "\r\n</body>\r\n</html>\r\n";

        static String[] Ids(CheckResult r, String path = null) => r.Findings.Where(x => (path == null) || (x.Path == path)).Select(x => x.Rule.Id).ToArray();

        // ---- the worksheet: migrate --dry-run --level design reports what check --strict --design reports after the migration ----

        const String OldPage = "<!DOCTYPE html>\r\n<html>\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <script defer src=\"../common/common.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n"
            + "    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../app/application.css\">\r\n    <link rel=\"stylesheet\" href=\"page.css\">\r\n</head>\r\n<body onload=\"pageMain()\">\r\n</body>\r\n</html>\r\n";
        const String OldJs = "function pageMain() {\r\n    const h = document.createElement(\"h1\");\r\n    h.textContent = _T(\"Report\");\r\n    document.body.append(h);\r\n    if (window.Mobile) h.className = \"m\";\r\n}\r\n";
        const String OldCss = "button { width: 100%; }\r\nbody { overflow: hidden; }\r\n.rp-pill { border-radius: 999px; padding: 2px 8px; background: #393; }\r\n";

        [Fact]
        public void DryRunWorksheetListsEverythingCheckReports()
        {
            using var f = new TestFolder();
            f.Write("rp/report.html", OldPage);
            f.Write("rp/page.js", Header + OldJs);
            f.Write("rp/page.css", OldCss);
            var dry = Migrate(f, MigrateLevel.Design, dryRun: true);
            Assert.Equal(OldCss, f.Read("rp/page.css"));
            var sheet = dry.Items.Where(x => x.Kind == MigrateItemKind.Warning).Select(x => x.Message).ToList();
            //  The items that the old report left out: element-css, body-overflow, the page header (the page becomes new format), window.Mobile, the pill
            foreach (var rule in new[] { "check: element-css", "check: body-overflow", "design: design-page-header", "design: design-pill-facts", "design: design-color" })
                Assert.Contains(sheet, m => m.StartsWith(rule, StringComparison.Ordinal));
            //  (window.Mobile, a plain rename read through window, is rewritten to Sw.Layout.Mobile: no follow-up)
            Assert.DoesNotContain(sheet, m => m.Contains("window.Mobile", StringComparison.Ordinal));
            Assert.Contains(dry.Items, x => (x.Kind == MigrateItemKind.Change) && x.Message.Contains("window.Mobile -> Sw.Layout.Mobile", StringComparison.Ordinal));
            //  Lines are the lines of the files before the run
            var bo = dry.Items.First(x => x.Message.StartsWith("check: body-overflow", StringComparison.Ordinal));
            Assert.Equal(("rp/page.css", 2), (bo.Path, bo.Line));
            //  The real run, then check: the same rules (every finding of check is in the worksheet)
            Migrate(f, MigrateLevel.Design);
            var after = Checker.CheckFolder(f.Root, Opt());
            Assert.NotEmpty(after.Findings);
            //  (a legacy name that migrate couldn't rename is its own warning on that line, check's legacy-* line for it is left out)
            foreach (var fd in after.Findings.Where(x => (x.Rule != Rule.LegacyGlobal) && (x.Rule != Rule.LegacyCompat)))
                Assert.Contains(dry.Items, x => (x.Kind == MigrateItemKind.Warning) && (x.Path == fd.Path) && x.Message.Contains(": " + fd.Rule.Id + ": ", StringComparison.Ordinal));
        }

        [Fact]
        public void WorksheetMapsLinesOfMigratedFilesBack()
        {
            using var f = new TestFolder();
            //  The JSDoc header that migrate adds moves every line by 3: the worksheet still names the line of the original file
            f.Write("p/x.html", Page("", "    <script defer src=\"x.js\"></script>\r\n"));
            f.Write("p/x.js", "function a() {\r\n    return 1;\r\n}\r\nconst b = \"<span style='box-shadow: 0 2px 8px #000'>\";\r\n");
            var r = Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = true, Level = MigrateLevel.Design, DryRun = true });
            var it = r.Items.Single(x => x.Message.StartsWith("design: design-inline-style", StringComparison.Ordinal));
            Assert.Equal(4, it.Line);
        }

        // ---- check / migrate on a folder above the web roots ----

        [Fact]
        public void CheckAboveWebRootsOnlyChecksThem()
        {
            using var f = new TestFolder();
            f.Write("SysWeaver.Acme/srcWeb/acme/a.js", Header + "const a = 1;\r\n");
            f.Write("SysWeaver.Acme/web/acme/a.js.br", "x");
            f.Write("SysWeaver.Acme/web/acme/a.js", "no header\r\n");
            f.Write("SysWeaver.Acme/bin/Acme.Services.json", "{}");
            f.Write("SysWeaver.Acme/Acme.Services.json", "{}");
            f.Write("Other.App/srcData/b.js", "no header\r\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Equal(new[] { "SysWeaver.Acme/srcWeb", "Other.App/srcData" }.OrderBy(x => x, StringComparer.Ordinal), r.WebRoots.OrderBy(x => x, StringComparer.Ordinal));
            var only = Assert.Single(r.Findings);
            Assert.Equal(("Other.App/srcData/b.js", "jsdoc-header"), (only.Path, only.Rule.Id));
            //  Inside a web root nothing changes, compressed outputs are skipped by default
            f.Write("SysWeaver.Acme/srcWeb/acme/a.js.br", "x");
            Assert.Empty(Checker.CheckFolder(f.PathOf("SysWeaver.Acme/srcWeb"), new CheckOptions()).Findings);
        }

        [Fact]
        public void DataFilesInSrcDataKeepTheirNames()
        {
            using var f = new TestFolder();
            f.Write("App/srcData/lid.176.ftz", "x");
            f.Write("App/srcData/Font.ttf", "x");
            f.Write("App/srcData/a.b.html", "<html></html>");
            f.Write("App/srcWeb/x/Font.ttf", "x");
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Equal(new[] { ("App/srcData/a.b.html", "file-name-dots"), ("App/srcWeb/x/Font.ttf", "font-file") }, r.Findings.Select(x => (x.Path, x.Rule.Id)).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void MigrateAboveWebRootsLeavesBuildOutputs()
        {
            using var f = new TestFolder();
            const String js = Header + "Fail(\"x\");\r\n";
            f.Write("App/srcWeb/p/x.js", js);
            f.Write("App/web/p/x.js", js);
            var r = Migrate(f);
            Assert.Contains("Sw.Fail", f.Read("App/srcWeb/p/x.js"));
            Assert.Equal(js, f.Read("App/web/p/x.js"));
            Assert.Equal(new[] { "App/srcWeb/p/x.js" }, r.ChangedFiles);
        }

        // ---- sw-root ----

        [Fact]
        public void SwRootIsReportedAndAddedByMigrate()
        {
            using var f = new TestFolder();
            f.Write("p/a.html", Page("<main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>"));
            f.Write("p/b.html", Page("<main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>", "", "<body class=\"Top\" onload=\"x()\">"));
            f.Write("p/c.html", Page("<main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>", "", "<body class=\"sw-root\">"));
            f.Write("p/d.html", Page("<!-- SwWeb:no-sw-root (a print page) --><main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>"));
            var r = Checker.CheckFolder(f.Root, Opt());
            Assert.Equal(new[] { "p/a.html", "p/b.html" }, r.Findings.Where(x => x.Rule == Rule.DesignSwRoot).Select(x => x.Path).ToArray());
            Assert.Empty(Checker.CheckFolder(f.Root, Opt(design: false)).Findings.Where(x => x.Rule == Rule.DesignSwRoot));
            //  Not at the default level, added with --level design, idempotent
            Migrate(f);
            Assert.Contains("<body>", f.Read("p/a.html"));
            var m = Migrate(f, MigrateLevel.Design);
            Assert.Contains("<body class=\"sw-root\">", f.Read("p/a.html"));
            Assert.Contains("<body class=\"Top sw-root\" onload=\"x()\">", f.Read("p/b.html"));
            Assert.DoesNotContain("sw-root\">", f.Read("p/d.html").Replace("no-sw-root", ""));
            Assert.Empty(Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.DesignSwRoot));
            Assert.Empty(Migrate(f, MigrateLevel.Design).ChangedFiles);
        }

        [Fact]
        public void SwRootIsSetByTheAuthLayoutLibrary()
        {
            using var f = new TestFolder();
            f.Write("auth/x.html", Page("<!-- SwWeb:no-page-header -->", "    <script defer src=\"AuthUi.js\"></script>\r\n"));
            Assert.DoesNotContain(Checker.CheckFolder(f.Root, Opt()).Findings, x => x.Rule == Rule.DesignSwRoot);
            Assert.Empty(Migrate(f, MigrateLevel.Design).ChangedFiles);
        }

        [Fact]
        public void SwRootIsAddedToAMigratedOldPage()
        {
            using var f = new TestFolder();
            f.Write("rp/report.html", OldPage);
            f.Write("rp/page.js", Header + "function pageMain() { }\r\n");
            Migrate(f, MigrateLevel.Design);
            var t = f.Read("rp/report.html");
            Assert.Contains("sw/ui.css", t);
            Assert.Contains("<body class=\"sw-root\" onload=\"pageMain()\">", t);
        }

        // ---- script-order: inline Sw before the deferred bundles ----

        [Theory]
        [InlineData("<script>Sw.Page.Main(async () => { });</script>", true)]
        [InlineData("<script>\r\n    const x = 1;\r\n    Sw.Page.Main(main);\r\n</script>", true)]
        [InlineData("<script>function main() { Sw.Page.Main(); }</script>", false)]
        [InlineData("<script>document.addEventListener(\"DOMContentLoaded\", () => Sw.Page.Main(main));</script>", false)]
        [InlineData("<script>window.addEventListener(\"load\", function () { Sw.Info(\"x\"); });</script>", false)]
        [InlineData("<script>const f = () => { Sw.Info(1); };\r\nf;</script>", false)]
        [InlineData("<script>function a() { }\r\nSw.Info(1);</script>", true)]
        [InlineData("<script>if (typeof Sw !== \"undefined\") Sw.Info(1);</script>", false)]
        [InlineData("<script>class A { m() { Sw.Info(1); } }</script>", false)]
        public void InlineSwBeforeDeferredBundles(String script, bool reported)
        {
            using var f = new TestFolder();
            f.Write("p/x.html", Page("<main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>", "    " + script + "\r\n", "<body class=\"sw-root\">"));
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            Assert.Equal(reported, Ids(r).Contains("script-order"));
        }

        [Fact]
        public void InlineSwAfterABlockingCoreIsFine()
        {
            using var f = new TestFolder();
            f.Write("p/x.html", "<html><head><script src=\"../sw/core.js\"></script><script src=\"../sw/ui.js\"></script><script>Sw.Page.Main(() => 1);</script></head><body></body></html>");
            Assert.DoesNotContain("script-order", Ids(Checker.CheckFolder(f.Root, new CheckOptions())));
            //  ... but not before it
            f.Write("p/x.html", "<html><head><script>Sw.Page.Main(() => 1);</script><script src=\"../sw/core.js\"></script></head><body></body></html>");
            Assert.Contains("script-order", Ids(Checker.CheckFolder(f.Root, new CheckOptions())));
        }

        // ---- missing-include: library apis ----

        [Fact]
        public void TableWithoutIncludesIsReportedAndMigrateAddsThem()
        {
            using var f = new TestFolder();
            f.Write("ft/text.html", Page("", "    <script defer src=\"text.js\"></script>\r\n", "<body class=\"sw-root\">"));
            f.Write("ft/text.js", Header + "Sw.Page.Main(async () => {\r\n    const p = Sw.PageLayout({ Type: \"fill\", Title: _T(\"Texts\") });\r\n    await Sw.Table.Create({ Url: \"../Api/T\", Element: p.E });\r\n});\r\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions());
            var mi = Assert.Single(r.Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("explore/table.js + explore/table.css", mi.Message);
            Assert.Contains("../explore/table.js", mi.Message);
            Assert.Equal(Severity.Error, mi.Severity);
            //  migrate adds both (after sw/ui.js, so before the page script, and after sw/ui.css), with the same prefix
            Migrate(f);
            var t = f.Read("ft/text.html");
            Assert.Contains("<script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"../explore/table.js\"></script>\r\n    <script defer src=\"text.js\"></script>", t);
            Assert.Contains("<link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n    <link rel=\"stylesheet\" href=\"../explore/table.css\">", t);
            Assert.Empty(Checker.CheckFolder(f.Root, new CheckOptions()).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Empty(Migrate(f).ChangedFiles);
        }

        [Fact]
        public void TableIncludesInTheTableFolderAndTemplatedPrefixes()
        {
            using var f = new TestFolder();
            //  A page in explore/ loading "table.js" relatively is fine
            f.Write("srcWeb/explore/t.html", Page("<script>function m() { Sw.Table.Create({}); }</script>", "    <script defer src=\"table.js\"></script>\r\n    <link rel=\"stylesheet\" href=\"table.css\">\r\n", "<body class=\"sw-root\">"));
            Assert.DoesNotContain("missing-include", Ids(Checker.CheckFolder(f.PathOf("srcWeb"), new CheckOptions())));
            //  A srcData template keeps the ${@Base Url} prefix
            f.Write("srcData/t.html", "<html><head><script defer src=\"${@Base Url}sw/core.js\"></script><script defer src=\"${@Base Url}sw/ui.js\"></script><link rel=\"stylesheet\" href=\"${@Base Url}sw/ui.css\"></head><body><script>function m() { Sw.Table.Create({}); }</script></body></html>");
            Migrator.MigrateFolder(f.PathOf("srcData"), new MigrateOptions { Map = Map, JsDocHeaders = false });
            var t = f.Read("srcData/t.html");
            Assert.Contains("<script defer src=\"${@Base Url}explore/table.js\"></script>", t);
            Assert.Contains("<link rel=\"stylesheet\" href=\"${@Base Url}explore/table.css\">", t);
        }

        [Fact]
        public void OptionalAndSharedLibraryUsesAreFine()
        {
            using var f = new TestFolder();
            //  A guarded use
            f.Write("p/a.html", Page("<script>function m() { if (Sw.Table) Sw.Table.Create({}); }</script>", "", "<body class=\"sw-root\">"));
            //  A shared helper with a table function that only the page with the includes calls
            f.Write("p/shared.js", Header + "function fill(h) { return Sw.Table.Create({ Element: h }); }\r\n");
            f.Write("p/b.html", Page("", "    <script defer src=\"shared.js\"></script>\r\n", "<body class=\"sw-root\">"));
            f.Write("p/c.html", Page("", "    <script defer src=\"../explore/table.js\"></script>\r\n    <script defer src=\"shared.js\"></script>\r\n    <link rel=\"stylesheet\" href=\"../explore/table.css\">\r\n", "<body class=\"sw-root\">"));
            Assert.Empty(Checker.CheckFolder(f.Root, new CheckOptions()).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Empty(Migrate(f).ChangedFiles);
            //  Without the page that loads the library the helper's use counts
            File.Delete(f.PathOf("p/c.html"));
            Assert.Single(Checker.CheckFolder(f.Root, new CheckOptions()).Findings.Where(x => x.Rule == Rule.MissingInclude && x.Path == "p/b.html"));
        }

        [Fact]
        public void ShellApiNeedsTheShellBundle()
        {
            using var f = new TestFolder();
            f.Write("st/img.html", Page("", "    <script defer src=\"img.js\"></script>\r\n", "<body class=\"sw-root\">"));
            f.Write("st/img.js", Header + "async function menu(b) {\r\n    const m = await Sw.Net.Send(\"../Api/GetMenu\");\r\n    Sw.Menu.Show(b, Sw.Shell.ToMenuItems(m));\r\n}\r\n");
            var mi = Assert.Single(Checker.CheckFolder(f.Root, new CheckOptions()).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("sw/shell.js", mi.Message);
            Assert.Contains("st/img.js", mi.Message);
            Migrate(f);
            Assert.Contains("<script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"../sw/shell.js\"></script>", f.Read("st/img.html"));
        }

        [Fact]
        public void InlineTableAddTableGetsTheIncludesWhenMigrated()
        {
            using var f = new TestFolder();
            f.Write("p/x.html", "<html><head><script defer src=\"../common/common.js\"></script><link rel=\"stylesheet\" href=\"../common/theme.css\"><link rel=\"stylesheet\" href=\"../app/application.css\"></head>"
                + "<body onload=\"m()\"><script>function m() { Table.addTable(\"../Api/T\", document.body); }</script></body></html>");
            Migrate(f);
            var t = f.Read("p/x.html");
            Assert.Contains("Sw.Table.Create", t);
            Assert.Contains("../explore/table.js", t);
            Assert.Contains("../explore/table.css", t);
        }

        // ---- window state, viewport zoom, pills, element rules ----

        [Fact]
        public void WindowStateIsReportedAfterMigration()
        {
            using var f = new TestFolder();
            f.Write("p/x.html", Page("", "    <script defer src=\"x.js\"></script>\r\n", "<body class=\"sw-root\">"));
            f.Write("p/x.js", Header + "if (window.Mobile || window.IsTop) document.title = \"m\";\r\nwindow.SomethingElse = 1;\r\nconst o = { Mobile: 1 }; o.Mobile;\r\n");
            var r = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true });
            var lg = r.Findings.Where(x => x.Rule == Rule.LegacyGlobal).Select(x => x.Message).ToList();
            Assert.Equal(2, lg.Count);
            Assert.Contains(lg, m => m.Contains("window.Mobile"));
            Assert.Contains(lg, m => m.Contains("window.IsTop"));
            Assert.Empty(Checker.CheckFolder(f.Root, new CheckOptions()).Findings.Where(x => x.Rule == Rule.LegacyGlobal));
        }

        [Fact]
        public void ZoomBlockingViewportIsReportedAndFixed()
        {
            using var f = new TestFolder();
            var page = Page("<main class=\"sw-page-form\"><header class=\"sw-ph\"></header></main>", "", "<body class=\"sw-root\">")
                .Replace("width=device-width, initial-scale=1", "width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover");
            f.Write("p/x.html", page);
            var vp = Assert.Single(Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.UxInp25));
            Assert.Contains("blocks pinch zoom", vp.Message);
            Migrate(f);
            Assert.Contains("user-scalable=no", f.Read("p/x.html"));
            Migrate(f, MigrateLevel.Design);
            Assert.Contains("content=\"width=device-width, initial-scale=1, viewport-fit=cover\"", f.Read("p/x.html"));
            Assert.Empty(Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.UxInp25));
        }

        [Fact]
        public void LegacyTokenAsRoleTokenFallbackIsFine()
        {
            var fd = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext("p/x.css", new SourceText(".a { color: var(--sw-text-2, var(--ThemeMain)); }\r\n.b { color: var(--ThemeMain); }\r\n.c { border: 1px solid var(--x, var(--ThemeMain)); }\r\n"), Opt(), fd));
            Assert.Equal(new[] { 2, 3 }, fd.Where(x => x.Rule == Rule.DesignLegacyToken).Select(x => x.Line).ToArray());
        }

        [Fact]
        public void RoundPillIsAPill()
        {
            var fd = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext("p/x.css", new SourceText(".rp-pill { border-radius: 999px; padding: 2px 8px; background: var(--sw-ok-soft); }\r\n.rp-tag { border-radius: 999px; }\r\n"), Opt(), fd));
            Assert.Equal(new[] { "design-pill-facts", "design-radius", "design-radius" }, fd.Select(x => x.Rule.Id).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void ElementRulesOnTablesAndPictures()
        {
            List<Finding> Css(String code, String rel)
            {
                var fd = new List<Finding>();
                if (rel.EndsWith(".css", StringComparison.Ordinal))
                    Checker.CheckCssFile(new Checker.FileContext(rel, new SourceText(code), new CheckOptions(), fd));
                else
                    Checker.CheckHtmlFile(new Checker.FileContext(rel, new SourceText(code), new CheckOptions(), fd));
                return fd;
            }
            Assert.Equal(4, Css("td { padding: 0; }\r\nth { color: red; }\r\nimg { width: 100%; }\r\niframe { border: 0; }\r\n.page td, td.mine { padding: 0; }\r\n", "p/x.css").Count(x => x.Rule == Rule.ElementCss));
            //  A page without the Sw bundles (an export document, a mail) may style its own table
            Assert.Empty(Css("<html><head><style>td { padding: 0; } table { width: 100%; }</style></head><body><table></table></body></html>", "p/export.html"));
            Assert.Single(Css("<html><head><script defer src=\"../sw/core.js\"></script><style>td { padding: 0; }</style></head><body></body></html>", "p/x.html").Where(x => x.Rule == Rule.ElementCss));
        }

        [Fact]
        public void NewRulesAreListed()
        {
            Assert.Contains(Rule.All, r => r.Id == "missing-include" && r.Severity == Severity.Error && !r.Design && !r.StrictOnly);
            Assert.Contains(Rule.All, r => r.Id == "script-order" && r.Severity == Severity.Error && !r.Design);
            Assert.Contains(Rule.All, r => r.Id == "design-sw-root" && r.Design);
            Assert.Equal(PageChecker.RelativeUrl("app/x.html", "explore/table.js"), "../explore/table.js");
            Assert.Equal(PageChecker.RelativeUrl("explore/x.html", "explore/table.js"), "table.js");
            Assert.Equal(PageChecker.RelativeUrl("x.html", "sw/shell.js"), "sw/shell.js");
            Assert.Equal("explore/table.js", PageChecker.ResolveWebPath("explore/t.html", "table.js?v=1"));
            Assert.Equal("explore/table.js", PageChecker.ResolveWebPath("a/b/t.html", "${@Base Url}explore/table.js"));
            Assert.Null(PageChecker.ResolveWebPath("a/t.html", "https://cdn.example.com/x.js"));
        }
    }
}
