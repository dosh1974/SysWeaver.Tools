using System;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Migrating;

namespace SwWeb.Tests
{
    /// <summary>
    /// "migrate --level design" applies the context-free legacy theme variable renames and the dialog wording of old Confirm calls;
    /// check reports what is left (design-legacy-token, design-confirm); the missing-include hint uses the page's own include prefix.
    /// </summary>
    [Collection("Console")]
    public class DesignMigrateTests
    {
        const String Header = "/** @file test */\r\n";

        static readonly MigrationMap Map = MigrationMap.Load();

        static CheckOptions Opt() => new CheckOptions { Design = true, Strict = true, Map = Map };

        static MigrateResult Migrate(TestFolder f, MigrateLevel level, bool dryRun = false) =>
            Migrator.MigrateFolder(f.Root, new MigrateOptions { Map = Map, JsDocHeaders = false, Level = level, DryRun = dryRun });

        static String Page(String body, String head = "") =>
            "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n"
            + "    <script defer src=\"../sw/core.js\"></script>\r\n    <script defer src=\"../sw/ui.js\"></script>\r\n" + head
            + "    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n    <link rel=\"stylesheet\" href=\"../sw/ui.css\">\r\n</head>\r\n<body class=\"sw-root\">\r\n" + body + "\r\n</body>\r\n</html>\r\n";

        // ---- legacy theme variables with one context-free role token ----

        const String LegacyCss = ".a { color: var(--ThemeAcc1); background: var(--ThemeAcc1); border-color: var(--ThemeMain); }\r\n"
            + ".b { transition: opacity var(--ThemeFadeDuration); border-radius: var(--ThemeRad); font-family: var(--ThemeFontMono); }\r\n"
            + ".c { color: var(--ThemeError); outline-color: var(--ThemeFocus); fill: var(--ThemeAcc2); }\r\n";

        [Fact]
        public void LegacyThemeTokensAreRenamedAtTheDesignLevel()
        {
            using var f = new TestFolder();
            f.Write("p/p.css", LegacyCss);
            //  The default level only renames the aliases
            Migrate(f, MigrateLevel.Api);
            Assert.Equal(LegacyCss, f.Read("p/p.css"));
            //  The dry run lists the renames, the real run writes them
            var dry = Migrate(f, MigrateLevel.Design, dryRun: true);
            Assert.Contains(dry.Items, x => (x.Kind == MigrateItemKind.Change) && x.Message.Contains("var(--sw-text-accent)", StringComparison.Ordinal));
            Assert.Equal(LegacyCss, f.Read("p/p.css"));
            Migrate(f, MigrateLevel.Design);
            var t = f.Read("p/p.css");
            Assert.Contains(".a { color: var(--sw-text-accent); background: var(--sw-accent1); border-color: var(--ThemeMain); }", t);
            Assert.Contains("transition: opacity var(--sw-dur-2); border-radius: var(--sw-radius-md); font-family: var(--sw-mono);", t);
            Assert.Contains(".c { color: var(--sw-danger); outline-color: var(--sw-focus); fill: var(--sw-accent2); }", t);
            //  What is left is the role dependent variable (a person decides: text, border or border-strong)
            var left = Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.DesignLegacyToken).ToList();
            var one = Assert.Single(left);
            Assert.Contains("--ThemeMain", one.Message);
            Assert.Empty(Migrate(f, MigrateLevel.Design).ChangedFiles);
        }

        [Fact]
        public void CheckSaysWhichLegacyTokensMigrateRenames()
        {
            using var f = new TestFolder();
            f.Write("p/p.css", ".a { color: var(--ThemeAcc1); }\r\n");
            var fd = Assert.Single(Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.DesignLegacyToken));
            Assert.Contains("use var(--sw-text-accent) (SwWeb migrate --level design renames it)", fd.Message);
        }

        [Fact]
        public void LegacyTokenRenamesInStylesScriptsAndFallbacks()
        {
            using var f = new TestFolder();
            f.Write("p/x.html", Page("<div style=\"border-radius: var(--ThemeRadInp)\"></div>\r\n<style>.x { color: var(--sw-text-2, var(--ThemeAcc1)); }</style>", "    <script defer src=\"x.js\"></script>\r\n"));
            f.Write("p/x.js", Header + "const s = \"color: var(--ThemeOk); background: var(--ThemeWarning)\";\r\n");
            Migrate(f, MigrateLevel.Design);
            var h = f.Read("p/x.html");
            Assert.Contains("style=\"border-radius: var(--sw-radius)\"", h);
            //  A legacy variable as the fallback of a role token is left as is (it is never used where sw/ui.css is loaded)
            Assert.Contains("var(--sw-text-2, var(--ThemeAcc1))", h);
            Assert.Contains("\"color: var(--sw-ok); background: var(--sw-warning)\"", f.Read("p/x.js"));
        }

        [Fact]
        public void ScopedDefinitionsBlockTheRenameRootDefinitionsDoNot()
        {
            using var f = new TestFolder();
            //  An app theme on the root: --sw-accent2 reads it too, so the uses are renamed
            f.Write("app/app.css", ":root, :root[data-theme=\"x\"] { --ThemeAcc2: #123456; }\r\n.a { color: var(--ThemeAcc2); }\r\n");
            //  A scoped override: var(--ThemeAcc2) inside .card reads it, var(--sw-accent2) would not: no rename in the whole run
            f.Write("p/p.css", ".card { --ThemeFont: serif; }\r\n.card h2 { font-family: var(--ThemeFont); }\r\n");
            Migrate(f, MigrateLevel.Design);
            Assert.Contains(".a { color: var(--sw-accent2); }", f.Read("app/app.css"));
            Assert.Contains("font-family: var(--ThemeFont);", f.Read("p/p.css"));
        }

        // ---- old Confirm calls: Danger and verb labels ----

        const String OldPage = "<!DOCTYPE html>\r\n<html>\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <script defer src=\"../common/common.js\"></script>\r\n    <script defer src=\"page.js\"></script>\r\n"
            + "    <link rel=\"stylesheet\" href=\"../common/theme.css\">\r\n</head>\r\n<body onload=\"pageMain()\">\r\n</body>\r\n</html>\r\n";

        [Fact]
        public void DestructiveConfirmBecomesADangerDialogWithVerbLabels()
        {
            using var f = new TestFolder();
            f.Write("v/v.html", OldPage);
            f.Write("v/page.js", Header + "async function pageMain() {\r\n"
                + "    if (await Confirm(\"Delete\",\r\n        \"Delete this version:\\n\\nThe folder will be removed!\\nAre you sure ?\",\r\n        \"Yes, Delete!\",\r\n        \"No, Keep it!\",\r\n        \"../icons/close.svg\")) {\r\n        go();\r\n    }\r\n"
                + "    if (await Confirm(_T(\"Kill\"), \"Kill it?\", _T(\"Yes, Kill!\"))) go();\r\n"
                + "    if (await Confirm(\"Remove the key?\", \"Key: x\")) go();\r\n"
                + "    if (await Confirm(\"Save\", \"Save the changes?\", \"Yes, Save!\", \"No!\")) go();\r\n"
                + "}\r\nfunction go() {}\r\n");
            var r = Migrate(f, MigrateLevel.Api);
            var t = f.Read("v/page.js");
            Assert.Contains("Sw.Dialog.Confirm(\"Delete\",", t);
            Assert.Contains("\"Delete\",\r\n        { CancelText: \"Cancel\",\r\n        OkIcon: \"../icons/close.svg\", Danger: true })", t);
            Assert.Contains("Sw.Dialog.Confirm(_T(\"Kill\"), \"Kill it?\", _T(\"Kill\"), { Danger: true })", t);
            //  Without an OK text the title's verb is the danger button
            Assert.Contains("Sw.Dialog.Confirm(\"Remove the key?\", \"Key: x\", \"Remove\", { Danger: true })", t);
            //  Not destructive: verb labels only
            Assert.Contains("Sw.Dialog.Confirm(\"Save\", \"Save the changes?\", \"Save\", { CancelText: \"Cancel\" })", t);
            //  What needs a person is one warning per call: the title is no question, "Are you sure?" in the text
            var w = r.Items.Single(x => (x.Kind == MigrateItemKind.Warning) && x.Message.StartsWith("Sw.Dialog.Confirm (a destructive action", StringComparison.Ordinal) && x.Message.Contains("\"Delete\" should ask", StringComparison.Ordinal));
            Assert.Contains("Are you sure?", w.Message);
            //  check reports the same wording that migrate left for a person (the titles "Delete" and "Kill" are no questions), and the run is idempotent
            var dc = Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.DesignConfirm).ToList();
            Assert.Equal(2, dc.Count);
            Assert.All(dc, x => Assert.Contains("should ask a question", x.Message));
            Assert.Contains("Are you sure?", dc[0].Message);
            Assert.Empty(Migrate(f, MigrateLevel.Api).ChangedFiles);
        }

        [Fact]
        public void CheckFlagsDestructiveConfirmWithoutDangerAndYesNoLabels()
        {
            using var f = new TestFolder();
            f.Write("p/x.html", Page("", "    <script defer src=\"x.js\"></script>\r\n"));
            f.Write("p/x.js", Header + "async function a() {\r\n"
                + "    await Sw.Dialog.Confirm(\"Delete the file?\", \"x\", \"Delete\");\r\n"
                + "    await Sw.Dialog.Confirm(\"Delete the file?\", \"x\", \"Delete\", { Danger: true });\r\n"
                + "    await Sw.Dialog.Confirm(\"Continue?\", \"x\", \"Yes, Go!\", { CancelText: _T(\"No, Stay!\") });\r\n"
                + "    await Sw.Dialog.Confirm(title, text, ok, o);\r\n"
                + "}\r\n");
            var fs = Checker.CheckFolder(f.Root, Opt()).Findings.Where(x => x.Rule == Rule.DesignConfirm).ToList();
            Assert.Equal(2, fs.Count);
            Assert.Contains("without Danger: true", fs[0].Message);
            Assert.Equal(3, fs[0].Line);
            Assert.Contains("\"Yes, Go!\" is not a verb: \"Go\"", fs[1].Message);
            Assert.Contains("\"No, Stay!\": use \"Cancel\"", fs[1].Message);
            Assert.Equal(Severity.Warning, fs[1].Severity);
        }

        // ---- internal-id: shipped files describe rules, not tracker ids ----

        [Fact]
        public void InternalIdsAreReportedInStrictMode()
        {
            using var f = new TestFolder();
            f.Write("p/x.js", Header + "//  C3R2-Core-1: the time rule\r\nconst a = 1;   // UTF-8, SHA-256, ISO-8601, B-tree, RFC-7231 are no ids\r\n");
            f.Write("p/x.css", "/* the frame (F49-Left, CR-S20) */\r\n.a { color: var(--sw-text); }\r\n");
            f.Write("p/README.md", "# X\r\n\r\nFixed in C3R1 (RD-Tables, EX-B2, SM-B1, B-U1, BB-3, S2F-Tool).\r\n");
            var fs = Checker.CheckFolder(f.Root, new CheckOptions { Strict = true }).Findings.Where(x => x.Rule == Rule.InternalId).ToList();
            Assert.Equal(new[] { "C3R1", "RD-Tables", "EX-B2", "SM-B1", "B-U1", "BB-3", "S2F-Tool", "F49-Left", "CR-S20", "C3R2-Core-1" },
                fs.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Line).ThenBy(x => x.Col).Select(x => System.Text.RegularExpressions.Regex.Match(x.Message, "^\"([^\"]+)\"").Groups[1].Value).ToArray());
            Assert.All(fs, x => Assert.Equal(Severity.Warning, x.Severity));
            //  Not without --strict
            Assert.DoesNotContain(Checker.CheckFolder(f.Root, new CheckOptions()).Findings, x => x.Rule == Rule.InternalId);
        }

        // ---- missing-include: the hint uses the page's include prefix ----

        [Fact]
        public void MissingIncludeHintUsesThePagePrefix()
        {
            using var f = new TestFolder();
            f.Write("desk/orders.html", Page("", "    <script defer src=\"orders.js\"></script>\r\n"));
            f.Write("desk/orders.js", Header + "Sw.Page.Main(() => { new Sw.Edit(\"T\"); });\r\n");
            //  Checked as the page folder itself (the web root is not known): the include still gets "../" like sw/core.js
            var mi = Assert.Single(Checker.CheckFolder(f.PathOf("desk"), new CheckOptions()).Findings.Where(x => x.Rule == Rule.MissingInclude));
            Assert.Contains("<script defer src=\"../edit/Edit.js\">", mi.Message);
            Assert.Equal("../edit/Edit.js", PageChecker.IncludeUrl("orders.html", "../sw/core.js", "edit/Edit.js"));
            Assert.Equal("${@Base Url}explore/table.css", PageChecker.IncludeUrl("t.html", "${@Base Url}sw/core.js?v=1", "explore/table.css"));
            Assert.Equal("../explore/table.js", PageChecker.IncludeUrl("app/x.html", "https://cdn/x.js", "explore/table.js"));
        }
    }
}
