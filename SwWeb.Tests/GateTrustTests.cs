using System;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    /// <summary>
    /// The gate must not be fooled: a page that is on the bundles but not themed (theme-include), untranslated texts (tr-untranslated),
    /// aliases of the translation functions (tr-alias), own requests and indirect native dialogs (design-own-ui), shadows / gradients / radii
    /// through own custom properties or set from code, a hidden page header, a removed focus ring (a11y-focus), bare element rules behind
    /// :where() or an attribute (element-css), own fonts (design-font) and Sw.Dialog.Show wording (design-confirm). And migrate does the safe
    /// part in one pass, so the dry run is the worksheet.
    /// </summary>
    [Collection("Console")]
    public class GateTrustTests
    {
        const String Header = "/** @file test */\r\n";

        static readonly MigrationMap Map = MigrationMap.Load();

        static CheckOptions Opt() => new CheckOptions { Design = true, Strict = true, Map = Map };

        static MigrateOptions MOpt(bool dry = false) => new MigrateOptions { Map = Map, JsDocHeaders = false, Level = MigrateLevel.Design, DryRun = dry };

        static String[] Ids(CheckResult r, String path = null) => r.Findings.Where(x => (path == null) || (x.Path == path)).Select(x => x.Rule.Id).ToArray();

        const String Page = "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"
            + "    <title>Plain</title>\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n    <link rel=\"stylesheet\" href=\"plain.css\">\r\n"
            + "    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"plain.js\"></script>\r\n</head>\r\n"
            + "<body class=\"sw-root\">\r\n    <p class=\"intro\">Welcome!</p>\r\n</body>\r\n</html>\r\n";

        //  The Architect's "sneak2" page: no _T, own fetch, window["confirm"], shadow / radius set from code, own custom property shapes, a hidden header
        const String PageJs = Header + "Sw.Page.Main(async () => {\r\n"
            + "    const page = Sw.PageLayout({ Type: \"dash\", Title: \"Performance results\", Description: \"All runs of the web bench\" });\r\n"
            + "    const data = await fetch(\"../Api/bench/GetRuns\").then(r => r.json());\r\n"
            + "    const card = Sw.Card({ Title: \"Latest runs\", Body: Sw.el(\"p\", { text: \"No runs yet\" }) });\r\n"
            + "    card.E.style.setProperty(\"box-shadow\", \"0 10px 30px rgba(0,0,0,.5)\");\r\n"
            + "    card.E.style.cssText += \";border-radius:14px\";\r\n"
            + "    Object.assign(card.E.style, { fontFamily: \"Verdana\" });\r\n"
            + "    if (window[\"confirm\"](\"Delete all old runs?\")) Sw.Toast(\"Deleted!\");\r\n"
            + "    const ask = window.confirm;\r\n"
            + "});\r\n";

        const String PageCss = ":root { --p-shadow: 0 12px 40px rgba(0, 0, 0, .45); --p-radius: 18px; --p-grad: linear-gradient(135deg, #0a84ff, #5e5ce6); }\r\n"
            + ".intro { box-shadow: var(--p-shadow); border-radius: var(--p-radius); background: var(--p-grad); }\r\n"
            + ".sw-ph { display: none; }\r\n"
            + ".sw-card :focus-visible { outline: none; }\r\n"
            + ":where(button) { width: 100%; }\r\n"
            + "[type=\"text\"] { border-radius: 0; }\r\n";

        [Fact]
        public void EverySneakPatternIsReported()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/plain/index.html", Page);
            f.Write("srcWeb/plain/plain.js", PageJs);
            f.Write("srcWeb/plain/plain.css", PageCss);
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            String Msg(String rule, String path, int line) => r.Findings.Where(x => (x.Rule.Id == rule) && (x.Path == path) && (x.Line == line)).Select(x => x.Message).FirstOrDefault() ?? "";
            //  Not themed: the page stays light in the other themes
            Assert.Contains("without common/theme.css", Msg("theme-include", "plain/index.html", 7));
            //  Untranslated texts, each at its line, and the migrate hint
            Assert.Contains("the Title option \"Performance results\"", Msg("tr-untranslated", "plain/plain.js", 3));
            Assert.Contains("the Description option", r.Findings.Where(x => x.Line == 3 && x.Rule == Rule.TrUntranslated).Select(x => x.Message).Last());
            Assert.Contains("the text option \"No runs yet\"", r.Findings.Where(x => x.Line == 5 && x.Rule == Rule.TrUntranslated).Select(x => x.Message).Last());
            Assert.Contains("Sw.Toast text \"Deleted!\"", Msg("tr-untranslated", "plain/plain.js", 9));
            Assert.Contains("SwWeb migrate --level design wraps it", Msg("tr-untranslated", "plain/plain.js", 9));
            //  Own request, indirect confirm (bracket and reference)
            Assert.Contains("own fetch()", Msg("design-own-ui", "plain/plain.js", 4));
            Assert.Contains("window[\"confirm\"]", Msg("ux-pop-9", "plain/plain.js", 9));
            Assert.Contains("window.confirm", Msg("ux-pop-9", "plain/plain.js", 10));
            //  Set from code
            Assert.Contains("a shadow set from code", Msg("design-inline-style", "plain/plain.js", 6));
            Assert.Contains("an inline radius", Msg("design-inline-style", "plain/plain.js", 7));
            Assert.Contains("an own font set from code", Msg("design-font", "plain/plain.js", 8));
            //  Through own custom properties
            Assert.Contains("through var(--p-shadow)", Msg("design-shadow", "plain/plain.css", 2));
            Assert.Contains("through var(--p-radius)", Msg("design-radius", "plain/plain.css", 2));
            Assert.Contains("through var(--p-grad)", Msg("design-gradient", "plain/plain.css", 2));
            //  The hidden header, the removed focus ring, bare elements behind :where() / an attribute
            Assert.Contains("hides the page header", Msg("design-page-header", "plain/plain.css", 3));
            Assert.Contains("removes the focus indicator", Msg("a11y-focus", "plain/plain.css", 4));
            Assert.Contains("every button", Msg("element-css", "plain/plain.css", 5));
            Assert.Contains("every input", Msg("element-css", "plain/plain.css", 6));
        }

        [Fact]
        public void HiddenHeaderInMarkupOrCodeIsReported()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/p/index.html", Page.Replace("<p class=\"intro\">Welcome!</p>", "<header class=\"sw-ph\" hidden><h1>X</h1></header>").Replace("plain", "p"));
            f.Write("srcWeb/p/p.js", Header + "document.querySelector(\".sw-ph\").remove();\r\n");
            f.Write("srcWeb/p/p.css", ".x { color: var(--sw-text); }\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.Contains(r.Findings, x => (x.Rule == Rule.DesignPageHeader) && (x.Path == "p/index.html") && x.Message.StartsWith("a hidden page header", StringComparison.Ordinal));
            Assert.Contains(r.Findings, x => (x.Rule == Rule.DesignPageHeader) && (x.Path == "p/p.js") && x.Message.StartsWith("code hides the page header", StringComparison.Ordinal));
        }

        [Theory]
        //  A request Sw.Net doesn't make, an optional online feature, a download of bytes
        [InlineData("const r = await fetch(url, { credentials: \"include\", headers: { \"Authorization\": \"Basic x\" } });")]
        [InlineData("// SwWeb:allow-online (optional)\r\nconst r = await fetch(\"https://api.example.com/x\", { mode: \"cors\" });")]
        [InlineData("const r = new Request(u, { mode: \"cors\" });\r\nconst res = await fetch(r);")]
        [InlineData("const blob = await (await fetch(u)).blob();")]
        [InlineData("function f(fetch) { return fetch(1); }")]
        //  Texts that are not texts: keys, classes, urls, css, key combinations, translated, marked
        [InlineData("Sw.Button({ Text: _T(\"Delete\"), Icon: \"trash\", Hint: \"Ctrl+C\", Class: \"my-x\" });")]
        [InlineData("el.textContent = \"html{visibility:hidden !important}\"; el.title = \"page.html\";")]
        [InlineData("// SwWeb:no-tr (a product name)\r\nSw.Card({ Title: \"SysWeaver\" });")]
        [InlineData("Sw.Toast(_TF(\"Saved\", \"toast\")); x.innerHTML = \"<b></b>\";")]
        //  Values in a shadow / radius that come from tokens
        [InlineData("el.style.setProperty(\"box-shadow\", \"var(--sw-elev-1)\"); el.style.borderRadius = \"var(--sw-radius)\";")]
        public void NoFalsePositivesInCode(String code)
        {
            using var f = new TestFolder();
            f.Write("srcWeb/p/index.html", Page.Replace("plain", "p").Replace("<link rel=\"stylesheet\" href=\"../sw/ui.css\">", "<link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">"));
            f.Write("srcWeb/p/p.js", Header + "Sw.Page.Main(async () => {\r\n    Sw.PageLayout({ Type: \"dash\", Title: _T(\"Runs\") });\r\n" + code + "\r\n});\r\n");
            f.Write("srcWeb/p/p.css", ".x { color: var(--sw-text); }\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.Empty(r.Findings.Select(x => x.Format()));
        }

        [Theory]
        [InlineData(".x:focus-visible { outline: none; box-shadow: var(--sw-focus-ring); }")]
        [InlineData("@media (max-height: 500px) { .t > .sw-ph > .sw-ph-text { display: none; } }")]
        [InlineData("@media print { .sw-ph { display: none; } }")]
        [InlineData(".x { font-family: var(--sw-font-mono); font: inherit; }")]
        [InlineData(":root { --my-size: 40px; } .x { border-radius: calc(var(--my-size) * 0.1); }")]
        [InlineData(":root { --my-elev: var(--sw-elev-1); } .menu { box-shadow: var(--my-elev); }")]
        [InlineData(".my-page :where(.a, .b) button { width: 100%; }")]
        public void NoFalsePositivesInCss(String css)
        {
            using var f = new TestFolder();
            f.Write("srcWeb/p/p.css", css + "\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.Empty(r.Findings.Select(x => x.Format()));
        }

        [Fact]
        public void DialogShowWordingAndTranslationAliases()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/x.js", Header + "Sw.Dialog.Show({ Title: _T(\"Delete\"), Content: _T(\"Are you sure?\"), Buttons: [{ Text: _T(\"Yes, Delete!\") }, { Text: _T(\"No, keep it!\") }] });\r\nconst t = _T;\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            var c = Assert.Single(r.Findings, x => x.Rule == Rule.DesignConfirm);
            Assert.Contains("drop \"Are you sure?\"", c.Message);
            Assert.Contains("\"Yes, Delete!\" is not a verb", c.Message);
            Assert.Contains(r.Findings, x => (x.Rule == Rule.TrAlias) && (x.Line == 3) && (x.Severity == Severity.Error));
        }

        [Fact]
        public void NavigateToAnotherSiteOrAFileNeedsATarget()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/x.js", Header + "Sw.Page.Navigate(\"https://example.com/x\");\r\nSw.Page.Navigate(base + run + \".xlsx\");\r\nSw.Page.Navigate(\"overview.html?x=1\");\r\nSw.Page.Navigate(\"https://example.com\", \"_blank\");\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.Equal(new[] { 2, 3 }, r.Findings.Where(x => x.Rule == Rule.DesignNavigate).Select(x => x.Line).ToArray());
        }

        [Fact]
        public void UnknownTokenPrefersTheHyphenatedName()
        {
            Assert.Equal("--sw-text-2", TokenChecker.Closest("--sw-text2", TokenChecker.FrameworkTokens.ToHashSet(), out var close));
            Assert.True(close);
        }

        const String FreePage = "<!DOCTYPE html>\r\n<html>\r\n<head>\r\n    <meta charset=\"utf-8\" />\r\n    <title>Bench</title>\r\n    <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>\r\n"
            + "    <style>\r\n        body { font-family: Verdana; }\r\n    </style>\r\n    <script>\r\n        var titles = [\"High quality\", \"Low quality\"];\r\n"
            + "        function main() { var fr = document.getElementsByTagName(\"iframe\")[0]; fr.src = \"chart.html?x=\" + titles[0]; }\r\n    </script>\r\n</head>\r\n"
            + "<body onload=\"main()\">\r\n    <iframe></iframe>\r\n</body>\r\n</html>\r\n";

        [Fact]
        public void MigrateThemesAFrameworkFreePageInOnePassAndTheDryRunIsTheWorksheet()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/bench/index.html", FreePage);
            var dry = Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt(true));
            //  The port steps (frames from code, no translated text, own font) are in the worksheet
            Assert.Contains(dry.Items, x => x.Message.StartsWith("no-framework: port step: frames a", StringComparison.Ordinal));
            Assert.Contains(dry.Items, x => x.Message.StartsWith("no-framework: port step: no translated text", StringComparison.Ordinal));
            Assert.Contains(dry.Items, x => x.Message.StartsWith("no-framework: port step: own font", StringComparison.Ordinal));
            var run = Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt());
            Assert.Equal(dry.Changes, run.Changes);
            var html = f.Read("srcWeb/bench/index.html");
            //  Theme first, then the bundles, the viewport, sw-root, the framework's Chart.js instead of the CDN: all in this run
            Assert.Contains("<link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">", html);
            Assert.Contains("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">", html);
            Assert.Contains("<body class=\"sw-root\" onload=\"main()\">", html);
            Assert.Contains("<script src=\"../chart/external/chart.js\"></script>", html);
            //  The second run changes nothing; the steps that remain are check rules now (the worksheet = check after the run)
            var again = Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt());
            Assert.Equal(0, again.Changes);
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.Contains("tr-untranslated", Ids(r));
            Assert.Contains("design-font", Ids(r));
            Assert.DoesNotContain("theme-include", Ids(r));
            Assert.DoesNotContain("design-viewport", Ids(r));
            Assert.DoesNotContain("external-resource", Ids(r));
            var worksheet = dry.Items.Where(x => (x.Kind == MigrateItemKind.Warning) && !x.Message.StartsWith("no-framework:", StringComparison.Ordinal)).Count();
            Assert.Equal(r.Findings.Count, worksheet);
        }

        [Fact]
        public void MigrateThemesAndTranslatesAPageOnTheBundles()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/plain/index.html", Page);
            f.Write("srcWeb/plain/plain.js", PageJs);
            var r = Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt());
            var html = f.Read("srcWeb/plain/index.html");
            Assert.Contains("<link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">", html);
            var js = f.Read("srcWeb/plain/plain.js");
            Assert.Contains("Title: _T(\"Performance results\"), Description: _T(\"All runs of the web bench\")", js);
            Assert.Contains("{ text: _T(\"No runs yet\") }", js);
            Assert.Contains("Sw.Toast(_T(\"Deleted!\"))", js);
            //  Not wrapped: the argument of a native dialog (that is a design-own-ui step), urls
            Assert.Contains("window[\"confirm\"](\"Delete all old runs?\")", js);
            Assert.Contains("fetch(\"../Api/bench/GetRuns\")", js);
            Assert.Equal(0, Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt()).Changes);
            Assert.DoesNotContain("tr-untranslated", Ids(Checker.CheckFolder(f.PathOf("srcWeb"), Opt())));
        }

        [Fact]
        public void MigrateDoesNotTranslateScriptsOfFrameworkFreeRenderSurfaces()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/r/index.html", "<!DOCTYPE html>\r\n<!-- SwWeb:no-framework (a render surface for a player) -->\r\n<html><head><meta charset=\"utf-8\"><script src=\"r.js\"></script></head><body></body></html>\r\n");
            f.Write("srcWeb/r/r.js", Header + "el.textContent = \"Hello world\";\r\nfetch(\"Api/X\").then(r => r.json());\r\n");
            Assert.Equal(0, Migrator.MigrateFolder(f.PathOf("srcWeb"), MOpt()).Changes);
            Assert.Empty(Checker.CheckFolder(f.PathOf("srcWeb"), Opt()).Findings.Select(x => x.Format()));
        }
    }
}
