using System;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    /// <summary>
    /// The gate must not pass pages that break at run time or are not on the framework: framework-free pages (no-framework, design-own-ui),
    /// translation calls whose arguments don't match their placeholders (tr-format), unknown --sw-* tokens (unknown-token), internal ids
    /// with suffixes and the tracker families (internal-id), and the dialog wording that migrate reports (design-confirm).
    /// </summary>
    [Collection("Console")]
    public class GateTests
    {
        const String Header = "/** @file test */\r\n";

        static readonly MigrationMap Map = MigrationMap.Load();

        static CheckOptions Opt(bool design = true, bool strict = true) => new CheckOptions { Design = design, Strict = strict, Map = Map };

        static String[] Ids(CheckResult r, String path = null) => r.Findings.Where(x => (path == null) || (x.Path == path)).Select(x => x.Rule.Id).ToArray();

        const String FreePage = "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"
            + "    <title>Mail</title>\r\n    <link rel=\"stylesheet\" href=\"mail.css\">\r\n    <script defer src=\"mail.js\"></script>\r\n</head>\r\n<body>\r\n"
            + "    <h1>Mail</h1>\r\n    <svg viewBox=\"0 0 24 24\"><path d=\"M3 3h18\"/></svg>\r\n    <dialog id=\"composer\"></dialog>\r\n</body>\r\n</html>\r\n";
        const String FreeJs = Header + "async function load() {\r\n    const r = await fetch(\"Api/GetMails\");\r\n    if (!confirm(\"Delete all?\"))\r\n        return;\r\n"
            + "    document.getElementById(\"composer\").showModal();\r\n    const dark = matchMedia(\"(prefers-color-scheme: dark)\").matches;\r\n}\r\n";
        const String FreeCss = ":root { --bg: #fff; --fg: #111; --line: #ddd; }\r\n@media (prefers-color-scheme: dark) { :root { --bg: #111; } }\r\nbody { font-family: system-ui; }\r\n";

        [Fact]
        public void FrameworkFreePageIsReportedWithItsPortSteps()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/mail/index.html", FreePage);
            f.Write("srcWeb/mail/mail.js", FreeJs);
            f.Write("srcWeb/mail/mail.css", FreeCss);
            //  Not strict: a warning (the page and every port step)
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt(design: false, strict: false));
            var page = r.Findings.Single(x => (x.Rule == Rule.NoFramework) && x.Message.StartsWith("the page is not on the framework", StringComparison.Ordinal));
            Assert.Equal(Severity.Warning, page.Severity);
            var steps = r.Findings.Where(x => (x.Rule == Rule.NoFramework) && x.Message.StartsWith("port step:", StringComparison.Ordinal)).ToList();
            foreach (var what in new[] { "own fetch()", "native confirm()", "own <dialog>", "own light / dark", "own colour palette", "own font", "own svg icons", "no translated text", "no page header" })
                Assert.Contains(steps, x => x.Message.Contains(what, StringComparison.Ordinal));
            Assert.Contains(steps, x => (x.Path == "mail/mail.js") && x.Message.Contains("own fetch()", StringComparison.Ordinal) && (x.Line == 3));
            //  Strict in a srcWeb folder: the page is an error
            var s = Checker.CheckFolder(f.PathOf("srcWeb"), Opt(design: false, strict: true));
            Assert.Equal(Severity.Error, s.Findings.Single(x => (x.Rule == Rule.NoFramework) && (x.Severity == Severity.Error)).Severity);
        }

        [Fact]
        public void IntentionalFrameworkFreePagesAndOtherDocumentsPass()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/fx/index.html", FreePage.Replace("<head>", "<head>\r\n    <!-- SwWeb:no-framework (a render surface for the media player) -->"));
            f.Write("srcWeb/fx/mail.js", FreeJs);
            f.Write("srcWeb/fx/mail.css", FreeCss);
            f.Write("srcWeb/mail/InviteMail.html", "<!DOCTYPE html>\r\n<html><body><p>Hello</p></body></html>\r\n");
            f.Write("srcWeb/r/go.html", "<!DOCTYPE html>\r\n<html><head><meta http-equiv=\"refresh\" content=\"0; url=../index.html\"></head><body></body></html>\r\n");
            f.Write("srcWeb/part/frag.html", "<div class=\"x\">a fragment</div>\r\n");
            f.Write("srcWeb/g/gallery.html", "<!DOCTYPE html>\r\n<html><head><script>\r\nvar js = [\"../sw/core.js\", \"../sw/ui.js\"];\r\n</script></head><body></body></html>\r\n");
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt(design: false));
            Assert.DoesNotContain("no-framework", Ids(r));
        }

        [Fact]
        public void MigrateDesignPutsAFrameworkFreePageOnTheFramework()
        {
            using var f = new TestFolder();
            f.Write("srcWeb/mail/index.html", FreePage);
            f.Write("srcWeb/mail/mail.js", FreeJs);
            f.Write("srcWeb/mail/mail.css", FreeCss);
            var dry = Migrator.MigrateFolder(f.PathOf("srcWeb"), new MigrateOptions { Map = Map, JsDocHeaders = false, Level = MigrateLevel.Design, DryRun = true });
            Assert.Equal(1, dry.FrameworkFreePages);
            Assert.Contains(dry.Items, x => (x.Path == "mail/mail.js") && x.Message.StartsWith("no-framework: port step: own fetch()", StringComparison.Ordinal));
            Assert.Contains("framework-free pages: 1", Migrator.Summary(dry), StringComparison.Ordinal);
            Migrator.MigrateFolder(f.PathOf("srcWeb"), new MigrateOptions { Map = Map, JsDocHeaders = false, Level = MigrateLevel.Design });
            var html = f.Read("srcWeb/mail/index.html");
            //  The bundles go before the page's own css / scripts (the page css still wins), sw-root on the body
            Assert.True(html.IndexOf("<link rel=\"stylesheet\" href=\"../sw/ui.css\">", StringComparison.Ordinal) < html.IndexOf("mail.css", StringComparison.Ordinal));
            Assert.Contains("<script defer src=\"../sw/core.js\"></script>", html);
            Assert.Contains("<script defer src=\"../sw/ui.js\"></script>", html);
            Assert.Contains("<body class=\"sw-root\">", html);
            //  Now a new-format page: no no-framework any more, the gate still names what the page does itself and the missing header
            var r = Checker.CheckFolder(f.PathOf("srcWeb"), Opt());
            Assert.DoesNotContain("no-framework", Ids(r));
            Assert.Contains("design-page-header", Ids(r));
            //  Own requests, light / dark and palette (design-own-ui), the native confirm (ux-pop-9) and the own dialog (ux-pop-1)
            Assert.True(Ids(r).Count(x => (x == "design-own-ui") || (x == "ux-pop-9") || (x == "ux-pop-1")) >= 4);
            Assert.Contains("ux-pop-9", Ids(r));
            Assert.Contains("ux-pop-1", Ids(r));
            //  Idempotent
            var again = Migrator.MigrateFolder(f.PathOf("srcWeb"), new MigrateOptions { Map = Map, JsDocHeaders = false, Level = MigrateLevel.Design });
            Assert.Equal(0, again.Changes);
            Assert.Equal(0, again.FrameworkFreePages);
        }

        [Fact]
        public void OwnUiInANewFormatPage()
        {
            using var f = new TestFolder();
            f.Write("p/a.js", Header + "function a(confirm) {\r\n    if (confirm && confirm())\r\n        return;\r\n}\r\nfunction b() {\r\n    window.alert(\"x\");\r\n    prompt(\"y\");\r\n}\r\n");
            var r = Checker.CheckFolder(f.Root, Opt());
            var own = r.Findings.Where(x => x.Rule == Rule.UxPop9).ToList();
            //  confirm is a parameter here (not the native one); alert and prompt are native
            Assert.Equal(2, own.Count);
            Assert.DoesNotContain(own, x => x.Message.Contains("confirm()", StringComparison.Ordinal));
        }

        [Fact]
        public void TranslationArgumentsMatchThePlaceholders()
        {
            using var f = new TestFolder();
            f.Write("a.js", Header
                + "var a = _TF(\"{0} emails, sent from {1}\", \"context\", n, from);\r\n"   // 2: error, _TF never formats
                + "var b = _T(\"{0} is {1}\", name, \"on\");\r\n"                           // 3: error, "on" is the context
                + "var c = _T(\"{0} items\", n, \"ctx\");\r\n"                              // 4: fine
                + "var d = _T(\"Hello\", n);\r\n"                                           // 5: warning, no placeholder
                + "var e = _T(\"Open {0}\");\r\n"                                           // 6: warning, no argument at all
                + "var g = _TF('Remove \"{0}\"', \"ctx\");\r\n"                             // 7: fine, formatted later
                + "var h = _T(\"{0} {1}\", ...xs);\r\n"                                     // 8: fine, spread
                + "var i = _TFH(\"x {0}\", v);\r\n");                                       // 9: error
            var r = Checker.CheckFolder(f.Root, Opt(design: false, strict: false));
            var tf = r.Findings.Where(x => x.Rule == Rule.TrFormat).Select(x => (x.Line, x.Severity)).ToArray();
            Assert.Equal(new[] { (2, Severity.Error), (3, Severity.Error), (5, Severity.Warning), (6, Severity.Warning), (9, Severity.Error) }, tf);
            Assert.Contains("the trailing literal \"on\" is the translation context", r.Findings.Single(x => (x.Rule == Rule.TrFormat) && (x.Line == 3)).Message);
        }

        [Fact]
        public void UnknownTokensAreReportedWithTheClosestName()
        {
            using var f = new TestFolder();
            f.Write("p/a.css", ".a { font-size: var(--sw-fs-sm); color: var(--sw-accent); }\r\n.b { background: var(--sw-accent-soft, var(--sw-selected)); }\r\n"
                + ".c { width: var(--sw-my-hook, 10px); color: var(--sw-text-2); margin: var(--my-own); }\r\n.d { --sw-mine: 1px; padding: var(--sw-mine); }\r\n/* var(--sw-in-a-comment) */\r\n");
            f.Write("p/a.js", Header + "el.style.cssText = \"color: var(--sw-txt)\";\r\nel.style.setProperty(\"--sw-set-by-js\", \"1px\");\r\nvar x = \"var(--sw-set-by-js)\";\r\n");
            var r = Checker.CheckFolder(f.Root, Opt(design: false, strict: false));
            var u = r.Findings.Where(x => x.Rule == Rule.UnknownToken).ToList();
            Assert.Contains(u, x => x.Message.Contains("\"--sw-fs-sm\"", StringComparison.Ordinal) && x.Message.Contains("did you mean \"--sw-text-sm\"", StringComparison.Ordinal));
            Assert.Contains(u, x => x.Message.Contains("\"--sw-accent\"", StringComparison.Ordinal) && x.Message.Contains("\"--sw-accent1\"", StringComparison.Ordinal));
            //  With a fallback: only a near miss of a real token
            Assert.Contains(u, x => x.Message.Contains("\"--sw-accent-soft\"", StringComparison.Ordinal) && x.Message.Contains("\"--sw-accent1-soft\"", StringComparison.Ordinal));
            Assert.DoesNotContain(u, x => x.Message.Contains("--sw-my-hook", StringComparison.Ordinal));
            Assert.Contains(u, x => (x.Path == "p/a.js") && x.Message.Contains("\"--sw-txt\"", StringComparison.Ordinal));
            Assert.Equal(4, u.Count);
        }

        [Fact]
        public void FrameworkTokenListIsEmbedded()
        {
            var t = TokenChecker.FrameworkTokens;
            foreach (var n in new[] { "--sw-accent1", "--sw-text-sm", "--sw-surface", "--sw-radius", "--sw-elev-1", "--sw-control-h", "--sw-chart-1" })
                Assert.Contains(n, t);
        }

        [Fact]
        public void InternalIdsWithSuffixesAndTrackerFamilies()
        {
            foreach (var id in new[] { "CR-S18a", "B-C5", "B-L3", "B-U1", "UM-S2", "AU-B2", "ED-B1", "AC-B2", "BB-CR-S7", "C3R1-Core-4b", "CR6-A1", "C3R2-DocsTool-7" })
                Assert.True(Checker.InternalIdPattern.IsMatch("see (" + id + ") here"), id);
            foreach (var s in new[] { "UTF-8", "SHA-256", "ISO-8601", "RFC-7231", "A1-A13", "B10", "x-B1", "sw-h1", "--sw-chart-1", "CR-Sx" })
                Assert.False(Checker.InternalIdPattern.IsMatch("see " + s + " here"), s);
            //  Framework sources (core/, ui/, shell/, common.js) are checked too, the generated bundles (sw/) not
            using var f = new TestFolder();
            f.Write("core/a.js", Header + "// asked once (CR-S18a)\r\n");
            f.Write("sw/core.js", Header + "// asked once (CR-S18a)\r\n");
            var r = Checker.CheckFolder(f.Root, Opt(design: false));
            Assert.Equal(new[] { "core/a.js" }, r.Findings.Where(x => x.Rule == Rule.InternalId).Select(x => x.Path).ToArray());
        }

        [Fact]
        public void CheckReportsTheDialogWordingThatMigrateReports()
        {
            using var f = new TestFolder();
            f.Write("p/a.js", Header + "async function del(n) {\r\n    await Sw.Dialog.Confirm(\"Delete\", \"Are you sure?\", \"Delete\", { Danger: true });\r\n"
                + "    await Sw.Dialog.Confirm(\"Delete the key?\", \"It is gone for good.\", \"Delete\", { Danger: true });\r\n"
                + "    await Sw.Dialog.Confirm(\"Discard\", \"\", \"Discard\", { Danger: true, Target: n });\r\n}\r\n");
            var r = Checker.CheckFolder(f.Root, Opt());
            var c = r.Findings.Where(x => x.Rule == Rule.DesignConfirm).ToList();
            var one = Assert.Single(c);
            Assert.Equal(3, one.Line);
            Assert.Contains("should ask a question", one.Message);
            Assert.Contains("drop \"Are you sure?\"", one.Message);
        }
    }
}
