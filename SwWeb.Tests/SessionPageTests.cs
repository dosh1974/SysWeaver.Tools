using System;
using System.Linq;
using SwWeb.Checking;

namespace SwWeb.Tests
{
    /// <summary>
    /// Messaging only pages (sw/session.js without sw/core.js, core/README "Messaging only"): check accepts them (no no-framework report:
    /// a page that renders its own UI on purpose), and reports missing-include when the page or its own scripts use the theme without
    /// sw/theme.js, or Sw.Net requests / auth/Auth.js without sw/net.js.
    /// </summary>
    [Collection("Console")]
    public class SessionPageTests
    {
        const String Header = "/** @file test */\r\n";

        static String Page(String scripts, String inline = "") =>
            "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"
            + "    <title>Editor</title>\r\n" + scripts + "    <link rel=\"stylesheet\" href=\"own.css\">\r\n</head>\r\n<body>\r\n    <canvas id=\"view\"></canvas>\r\n" + inline + "</body>\r\n</html>\r\n";

        static CheckResult Check(TestFolder f, bool strict = true) => Checker.CheckFolder(f.PathOf("srcWeb"), new CheckOptions { Strict = strict, Design = true });

        [Fact]
        public void MessagingOnlyPageIsOnTheFramework()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            f.Write("srcWeb/ed/own.js", Header + "Sw.Session.Subscribe(\"ed.changed\", m => draw(m.Value));\r\nSessionManager.AddMessageHandler(\"user.logout\", () => draw(null), true);\r\nfunction draw(v) {\r\n}\r\n");
            f.Write("srcWeb/ed/own.css", "canvas { width: 100%; }\r\n");
            var r = Check(f);
            Assert.DoesNotContain(r.Findings, x => x.Rule == Rule.NoFramework);
            Assert.DoesNotContain(r.Findings, x => x.Rule == Rule.MissingInclude);
            //  The same page without any SysWeaver script is framework-free
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"own.js\"></script>\r\n"));
            Assert.Contains(Check(f).Findings, x => x.Rule == Rule.NoFramework);
        }

        [Fact]
        public void ThemeWithoutThemeScriptIsAMissingInclude()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            f.Write("srcWeb/ed/own.js", Header + "function start() {\r\n    applyTheme();\r\n}\r\n");
            f.Write("srcWeb/ed/own.css", "canvas { width: 100%; }\r\n");
            var mi = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("(its script ed/own.js) uses applyTheme() but doesn't load sw/theme.js", mi.Message);
            Assert.Contains("<script defer src=\"../sw/theme.js\"></script> after sw/session.js", mi.Message);
            Assert.Equal(Severity.Error, mi.Severity);
            //  An inline use of Sw.Theme, at its line
            f.Write("srcWeb/ed/own.js", Header + "function start() {\r\n}\r\n");
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n", "    <script>function t() { return Sw.Theme.Current; }</script>\r\n"));
            mi = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("uses Sw.Theme but doesn't load sw/theme.js", mi.Message);
            //  With sw/theme.js (or sw/core.js) it is fine; Sw.Session.RequestedTheme needs no theme script
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"../sw/theme.js\"></script>\r\n", "    <script>function t() { return Sw.Theme.Current; }</script>\r\n"));
            Assert.DoesNotContain(Check(f).Findings, x => x.Rule == Rule.MissingInclude);
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n", "    <script>function t() { return Sw.Session.RequestedTheme || Sw.Session.StoredTheme; }</script>\r\n"));
            Assert.DoesNotContain(Check(f).Findings, x => x.Rule == Rule.MissingInclude);
        }

        [Fact]
        public void RequestsAndAuthNeedTheNetScript()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/ed/own.css", "canvas { width: 100%; }\r\n");
            f.Write("srcWeb/ed/own.js", Header + "async function load() {\r\n    return await Sw.Net.Send(\"../Api/ed/Get\", null);\r\n}\r\n");
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            var mi = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("uses Sw.Net but doesn't load sw/net.js", mi.Message);
            //  The include members of Sw.Net are in sw/session.js
            f.Write("srcWeb/ed/own.js", Header + "async function load() {\r\n    await Sw.Net.IncludeCss(null, \"x.css\", true);\r\n}\r\n");
            Assert.DoesNotContain(Check(f).Findings, x => x.Rule == Rule.MissingInclude);
            //  auth/Auth.js calls Sw.Net
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"../auth/Auth.js\"></script>\r\n"));
            mi = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("uses auth/Auth.js but doesn't load sw/net.js", mi.Message);
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"../sw/net.js\"></script>\r\n    <script defer src=\"../auth/Auth.js\"></script>\r\n"));
            Assert.DoesNotContain(Check(f).Findings, x => x.Rule == Rule.MissingInclude);
        }

        /// <summary>The own look of WebQuickStart 2b recipe 1 (S6 F6): plain colours and a font, no Sw components, no --sw-* tokens.</summary>
        const String OwnCss = "body { font: 14px system-ui, sans-serif; background: #fff; color: #222; }\r\nbutton { font-size: 15px; }\r\n";

        [Fact]
        public void AMessagingOnlyPageHasItsOwnLook()
        {
            //  S6 F6: the documented messaging only page (sw/session.js, no sw/ui.css) gets no Design v2 warnings for its own css
            using var f = new TestFolder();
            f.Write("srcWeb/ed/own.css", OwnCss);
            f.Write("srcWeb/ed/own.js", Header + "function draw(v) {\r\n}\r\n");
            f.Write("srcWeb/ed/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            var r = Check(f);
            Assert.DoesNotContain(r.Findings, x => x.Path.EndsWith("own.css", StringComparison.Ordinal) && ((x.Rule.Design && (x.Rule.Guide == null)) || (x.Rule == Rule.ElementCss)));
            Assert.True(PageChecker.IsOwnLook(f.Read("srcWeb/ed/index.html")));
            //  The same css on a page with the Sw components (sw/core.js + sw/ui.css) is judged
            f.Write("srcWeb/ed/index.html", Page("    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            Assert.Contains(Check(f).Findings, x => x.Path.EndsWith("own.css", StringComparison.Ordinal) && ((x.Rule == Rule.DesignColor) || (x.Rule == Rule.ElementCss)));
            //  ... unless the page declares its own look (a meta, or data-sw-own-look on html / body)
            f.Write("srcWeb/ed/index.html", Page("    <meta name=\"sw-own-look\" content=\"a canvas editor with its own colours\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"own.js\"></script>\r\n"));
            Assert.DoesNotContain(Check(f).Findings, x => x.Path.EndsWith("own.css", StringComparison.Ordinal) && ((x.Rule == Rule.DesignColor) || (x.Rule == Rule.ElementCss) || (x.Rule == Rule.DesignFont)));
            Assert.True(PageChecker.IsOwnLook("<html data-sw-own-look><body></body></html>"));
            Assert.False(PageChecker.IsOwnLook("<html><body><script src=\"../sw/core.js\"></script></body></html>"));
        }

        [Fact]
        public void OverlappingBundlesAreReported()
        {
            //  AS6-1: common/common.js + sw/ui.js: with the framework manifest (every bundle LoadOnce) the ui include adds nothing (a warning)
            using var f = new TestFolder();
            f.Write("srcWeb/old/page.js", Header + "function go() {\r\n}\r\n");
            f.Write("srcWeb/old/index.html", Page("    <script defer src=\"../common/common.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n"));
            var bo = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.BundleOverlap));
            Assert.Equal(Severity.Warning, bo.Severity);
            Assert.Contains("common/common.js already contains every source of sw/ui.js", bo.Message);
            //  A manifest in the checked web root where sw/ui.js is not load once: an error (the ui runs twice)
            f.Write("srcWeb/sw/bundles.json", "{ \"Bundles\": [ { \"Out\": \"common/common.js\", \"LoadOnce\": true, \"Files\": [\"core/kernel.js\", \"ui/base.js\"] }, { \"Out\": \"sw/ui.js\", \"Files\": [\"ui/base.js\"] } ] }");
            bo = Assert.Single(Check(f).Findings.Where(x => x.Rule == Rule.BundleOverlap));
            Assert.Equal(Severity.Error, bo.Severity);
            Assert.Contains("common/common.js and sw/ui.js both contain ui/base.js, and sw/ui.js is not \"LoadOnce\"", bo.Message);
            //  Intended pairs are fine: sw/session.js + sw/net.js (a shared kernel, both LoadOnce)
            System.IO.File.Delete(f.PathOf("srcWeb/sw/bundles.json"));
            f.Write("srcWeb/old/index.html", Page("    <script defer src=\"../sw/session.js\"></script>\r\n    <script defer src=\"../sw/net.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n"));
            Assert.DoesNotContain(Check(f).Findings, x => x.Rule == Rule.BundleOverlap);
        }
    }
}
