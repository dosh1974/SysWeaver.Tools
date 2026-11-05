using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwWeb.Bundling;
using SwWeb.Checking;

namespace SwWeb
{
    /// <summary>
    /// SwWeb: build, lint and migration tool for the SysWeaver web front end (bundle, check, migrate).
    /// </summary>
    internal static class SwWebProgram
    {

        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.

        /// <summary>
        /// Exit code: success (also returned when help was requested).
        /// </summary>
        internal const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, an exception (ex: invalid json in the bundle manifest, IO error).
        /// </summary>
        internal const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line (unknown command or option, missing arguments, missing folder or manifest).
        /// </summary>
        internal const int ExitInvalidArguments = 2;
        /// <summary>
        /// Exit code: problems found: "check" found errors (or warnings with --strict), "bundle --check" found an out of date bundle,
        /// or a bundle definition / source is invalid.
        /// </summary>
        internal const int ExitProblems = 3;
        /// <summary>
        /// Exit code: "bundle": one or more source files listed in the manifest don't exist.
        /// </summary>
        internal const int ExitMissingFiles = 4;

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help" or "help";

        const String UsageText =
@"SwWeb - build and lint tool for the SysWeaver web front end.

Use:
  SwWeb bundle <srcWebRoot> [--check] [--manifest <path>] [--no-verify]
      Builds the bundles listed in <srcWebRoot>/sw/bundles.json (or --manifest) by concatenating the source files.
      --check            Don't write anything, fail (exit code 3) if any bundle is out of date.
      --manifest <path>  Use another manifest (paths in it are still relative to <srcWebRoot>).
      --no-verify        Skip the source verification (by default a .js bundle is not written when a source doesn't parse
                         or calls a name that no bundle source declares: the previous bundle stays).


  SwWeb check <folder> [--strict] [--design] [--framework] [--skip <pattern>].. [--no-default-skip] [--template-var <name>]..
                       [--legacy <file>] [--full-paths] [--quiet]
      Lints .js / .css / .html (and templated) files for the SysWeaver server contract rules. A folder above the
      srcWeb / srcData folders (a repository) checks each of them and nothing else (not web/ build outputs).
      --strict              Also check legacy globals in new-format pages and fail on warnings.
      --design              Also run the Design v2 porting rules (design-*, a11y-*: page header, shadows, gradients,
                            hard-coded colours, old tokens / class hooks, inline styles, accessible custom controls)
                            and the UX guide rules (ux-*: _docs/UX_Guide.md of SysWeaver).
      --framework           Also run the UX guide rules on the framework's own files (core/, ui/, shell/, common/ ...).
      --skip <pattern>      Skip more paths: ""name/"" = folder name, ""*.ext"" = file name, ""a/**/b.js"" = path.
      --no-default-skip     Don't skip the vendored paths (external/, marked.js, md5.js, UZIP.js, *.min.js ..).
      --template-var <name> An extra known template variable (ex: a config ""Variables"" key).
      --legacy <file>       Use another legacy globals list (default: legacy_globals.txt next to the tool).
      --full-paths          Print full paths instead of paths relative to <folder>.
      --quiet               Only print the summary.

  SwWeb migrate <folder> [--dry-run] [--backup <dir>] [--report <file>] [--include <glob>].. [--exclude <glob>]..
                         [--level includes|api|design] [--map <file>] [--legacy <file>] [--keep-legacy] [--force-includes] [--no-jsdoc-header] [--quiet]
      Migrates old format pages (common.js / application.js / application.css) to the new Sw format:
      include tags, token level renames of legacy globals and old design tokens (--level api, the default), css hook
      report; --level design adds the Design v2 rewrites and lists everything ""check --strict --design"" reports
      after the run as manual follow-up warnings (the porting worksheet, also with --dry-run).
      Pages that still use legacy globals after the renames get sw/compat.js (after sw/ui.js) and sw/compat.css
      (after sw/ui.css), with a warning listing the names; remove both includes once the names are rewritten.
      --dry-run          Write nothing (the report shows what would change).
      --backup <dir>     Copy the original of every changed file to <dir> (mirrored relative paths) first.
      --report <file>    Write a report (markdown if the name ends with .md, else text).
      --include <glob>   Only migrate matching paths (repeatable, same patterns as --skip).
      --exclude <glob>   Don't migrate matching paths (repeatable).
      --level <level>    ""includes"": only include tags; ""api"": also rename legacy globals and tokens (default);
                         ""design"": api plus the Design v2 rewrites and the full porting worksheet.
      --map <file>       Extend / override the default mapping table (json, see ""SwWeb mapping"").
      --legacy <file>    Use another legacy globals list (names without a mapping are reported).
      --keep-legacy      Keep pages that still use legacy globals on common.js / application.js (the old safety gate).
      --force-includes   Rewrite the includes without sw/compat.js even if the page still uses legacy globals.
      --no-jsdoc-header  Don't add a JSDoc file header to .js files without one (added by default, check rule jsdoc-header).
      --quiet            Only print the summary.

  SwWeb upgrade <folder> [--dry-run] [--backup <dir>] [--report <file>] [--include <glob>].. [--exclude <glob>]..
                         [--level includes|api] [--map <file>] [--legacy <file>] [--keep-legacy] [--force-includes] [--quiet]
      Moves pages from the old common.js / application.js to the new scripts with the least change: ""migrate""
      (--level api), except that a page that links no SysWeaver style sheet (a page with its own UI) gets no style
      sheet added (no common/theme.css, sw/ui.css, sw/compat.css), and no JSDoc headers or translation literal fixes
      are added. The pages work as before; ""migrate --level design"" is the later port to Design v2.

  SwWeb mapping
      Prints the default migrate mapping table (json).

  SwWeb rules
      Lists the check rules.

  SwWeb tokens <folder>
      Prints the --sw-* token names the web roots of <folder> define (regenerates sw_tokens.txt, the framework token
      list of the rule unknown-token, from the SysWeaver repository).

  SwWeb icons <folder>
      Prints the inline icon names of the framework (regenerates sw_icons.txt, the icon list of the rule design-icon).

  SwWeb contracts <folder> [--out <file>]
      Contract tests from every api row of core / ui / shell / explore README.md: each documented name is defined in the
      framework's sources, each documented option is read by them, a JSDoc @returns agrees with the documented type
      (exit code 3 on a problem). --out writes the browser manifest (srcWeb/sw/readme_contracts.js) that
      core/contracttest.html runs: the name exists, the api's option schema accepts the documented options, the
      documented return type of the message composers.

  SwWeb verify-build <folder> [--full-paths] [--quiet]
      Checks the BUILT web assets below <folder> (the web/**/*.br / *.gz outputs of Compress.exe that a project embeds
      and ships, not srcWeb): an empty output of a non-empty source, a minified .js (or a .html script) that doesn't
      lex or reads a name before its declaration (the minifier reorder bug), a .glsl that defines a function twice
      (shader minifier overloads) or has unbalanced brackets. Run it after a build (exit code 3 on a problem).

Exit codes: 0 = success or help, 1 = error, 2 = invalid command line,
            3 = problems found (check errors, out of date or invalid bundles, migrate warnings, verify-build problems),
            4 = missing bundle source files.";

        static int Usage(String err = null, int ret = ExitSuccess)
        {
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(err);
                Console.WriteLine();
                Console.ResetColor();
            }
            Console.WriteLine(UsageText);
            return ret;
        }

        static void WriteColor(ConsoleColor c, String text)
        {
            Console.ForegroundColor = c;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                WriteColor(ConsoleColor.Red, Environment.GetEnvironmentVariable("SWWEB_STACK") == "1" ? ex.ToString() : ex.Message);
                return ExitGenericError;
            }
        }

        /// <summary>
        /// Run a command line (also used by tests).
        /// </summary>
        /// <param name="args">The arguments</param>
        /// <returns>The exit code</returns>
        internal static int Run(string[] args)
        {
            if (args.Length == 0)
                return Usage(null, ExitInvalidArguments);
            var cmd = args[0].ToLowerInvariant();
            if (IsHelp(cmd))
                return Usage();
            var rest = args.Skip(1).ToArray();
            if (rest.Any(IsHelp))
                return Usage();
            switch (cmd)
            {
                case "bundle":
                    return RunBundle(rest);
                case "check":
                    return RunCheck(rest);
                case "rules":
                    foreach (var r in Rule.All)
                        Console.WriteLine(r.Id.PadRight(22) + (r.Severity == Severity.Warning ? "warning " : "error   ") + (r.StrictOnly ? "strict " : r.Design ? "design " : "       ") + r.Description);
                    return ExitSuccess;
                case "migrate":
                    return RunMigrate(rest);
                case "upgrade":
                    return RunMigrate(rest, true);
                case "tokens":
                    return RunTokens(rest);
                case "contracts":
                    return RunContracts(rest);
                case "icons":
                    return RunIcons(rest);
                case "verify-build":
                    return RunVerifyBuild(rest);
                case "mapping":
                    Console.WriteLine(Migrating.MigrationMap.GetDefaultText());
                    return ExitSuccess;
                default:
                    return Usage("Unknown command \"" + args[0] + "\"", ExitInvalidArguments);
            }
        }

        /// <summary>
        /// Simple option parser: positional arguments and "--name [value]" options (also "-name").
        /// </summary>
        sealed class Options
        {
            public readonly List<String> Positional = new List<String>();
            public readonly List<(String Name, String Value)> Values = new List<(String, String)>();
            public String Error;

            public Options(String[] args, IReadOnlySet<String> flags, IReadOnlySet<String> valued)
            {
                for (int i = 0; i < args.Length; ++i)
                {
                    var a = args[i];
                    if (a.StartsWith('-') && (a.Length > 1))
                    {
                        var n = a.TrimStart('-').ToLowerInvariant();
                        if (flags.Contains(n))
                        {
                            Values.Add((n, null));
                            continue;
                        }
                        if (valued.Contains(n))
                        {
                            if (i + 1 >= args.Length)
                            {
                                Error = "Option \"" + a + "\" requires a value";
                                return;
                            }
                            Values.Add((n, args[++i]));
                            continue;
                        }
                        Error = "Unknown option \"" + a + "\"";
                        return;
                    }
                    Positional.Add(a);
                }
            }

            public bool Has(String name) => Values.Any(x => x.Name == name);
            public String Get(String name) => Values.LastOrDefault(x => x.Name == name).Value;
            public IEnumerable<String> GetAll(String name) => Values.Where(x => x.Name == name).Select(x => x.Value);
        }

        static int RunBundle(String[] args)
        {
            var o = new Options(args, new HashSet<String> { "check", "no-verify" }, new HashSet<String> { "manifest" });
            if (o.Error != null)
                return Usage(o.Error, ExitInvalidArguments);
            if (o.Positional.Count != 1)
                return Usage("Expected one <srcWebRoot> folder", ExitInvalidArguments);
            var root = Path.GetFullPath(o.Positional[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var manifestArg = o.Get("manifest");
            var manifestPath = manifestArg != null ? Path.GetFullPath(manifestArg) : Path.Combine(root, Bundler.DefaultManifest.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(manifestPath))
                return Usage("Bundle manifest \"" + manifestPath + "\" doesn't exist!", ExitInvalidArguments);
            var manifestName = Path.GetRelativePath(root, manifestPath).Replace('\\', '/');
            if (manifestName.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(manifestName))
                manifestName = Path.GetFileName(manifestPath);
            bool check = o.Has("check");
            var manifest = BundleManifest.Load(manifestPath);
            var results = Bundler.BuildAll(root, manifest, manifestName, check, !o.Has("no-verify"));
            int missing = 0, failed = 0, outOfDate = 0, written = 0, upToDate = 0;
            int overBudget = 0;
            foreach (var r in results)
            {
                var name = r.Bundle.Out ?? "(no Out)";
                //  (the size; with the budget when the manifest sets one: AS6-6)
                String size = r.Size > 0 ? FormattableString.Invariant($", {r.Size / 1024.0:0.0} KB{(r.Bundle.MaxKB.HasValue ? " of " + r.Bundle.MaxKB.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}, {r.GzipSize / 1024.0:0.0} KB gzip{(r.Bundle.MaxGzipKB.HasValue ? " of " + r.Bundle.MaxGzipKB.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}") : "";
                foreach (var e in r.OverBudget)
                    WriteColor(ConsoleColor.Red, name + ": error: " + e);
                if (r.OverBudget.Count > 0)
                    ++overBudget;
                switch (r.Status)
                {
                    case BundleStatus.UpToDate:
                        ++upToDate;
                        Console.WriteLine(name + ": up to date (" + r.Bundle.Files.Count + " files" + size + ")");
                        break;
                    case BundleStatus.Written:
                        ++written;
                        WriteColor(ConsoleColor.Green, name + ": written (" + r.Bundle.Files.Count + " files" + size + ")");
                        break;
                    case BundleStatus.OutOfDate:
                        ++outOfDate;
                        WriteColor(ConsoleColor.Yellow, name + ": OUT OF DATE, run \"SwWeb bundle " + o.Positional[0] + "\" (generated: " + r.Bundle.Files.Count + " files" + size + ")");
                        break;
                    default:
                        ++failed;
                        foreach (var m in r.MissingFiles)
                            WriteColor(ConsoleColor.Red, name + ": error: missing source file \"" + m + "\" (" + Path.Combine(root, m.Replace('/', Path.DirectorySeparatorChar)) + ")");
                        foreach (var e in r.Errors)
                            WriteColor(ConsoleColor.Red, name + ": error: " + e);
                        WriteColor(ConsoleColor.Red, name + ": FAILED, not " + (check ? "checked" : "written"));
                        missing += r.MissingFiles.Count;
                        break;
                }
            }
            Console.WriteLine();
            Console.WriteLine(results.Count + " bundle(s): " + upToDate + " up to date, " + written + " written, " + outOfDate + " out of date, " + failed + " failed (" + missing + " missing source file(s))" + (overBudget > 0 ? ", " + overBudget + " over the size budget" : ""));
            if (missing > 0)
                return ExitMissingFiles;
            if ((failed > 0) || (outOfDate > 0) || (overBudget > 0))
                return ExitProblems;
            return ExitSuccess;
        }

        static int RunMigrate(String[] args, bool upgrade = false)
        {
            var o = new Options(args,
                upgrade ? new HashSet<String> { "dry-run", "force-includes", "keep-legacy", "quiet" } : new HashSet<String> { "dry-run", "force-includes", "keep-legacy", "no-jsdoc-header", "quiet" },
                new HashSet<String> { "backup", "report", "include", "exclude", "level", "map", "legacy" });
            if (o.Error != null)
                return Usage(o.Error, ExitInvalidArguments);
            if (o.Positional.Count != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(o.Positional[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var opt = new Migrating.MigrateOptions { DryRun = o.Has("dry-run"), ForceIncludes = o.Has("force-includes"), KeepLegacy = o.Has("keep-legacy"), JsDocHeaders = !o.Has("no-jsdoc-header"), BackupDir = o.Get("backup") };
            //  upgrade: the same code on the new framework and nothing else (the page keeps its own styles, no contract fixes)
            if (upgrade)
            {
                opt.KeepStyles = true;
                opt.FixTranslations = false;
                opt.JsDocHeaders = false;
            }
            var level = o.Get("level");
            if (level != null)
            {
                if (!Enum.TryParse<Migrating.MigrateLevel>(level, true, out var lv) || !Enum.IsDefined(lv) || Char.IsDigit(level[0]))
                    return Usage("Invalid --level \"" + level + "\" (includes, api or design)", ExitInvalidArguments);
                if (upgrade && (lv == Migrating.MigrateLevel.Design))
                    return Usage("upgrade: --level design is a port to Design v2, use \"migrate --level design\"", ExitInvalidArguments);
                opt.Level = lv;
            }
            opt.Include.AddRange(o.GetAll("include"));
            opt.Exclude.AddRange(o.GetAll("exclude"));
            var map = o.Get("map");
            if ((map != null) && !File.Exists(map))
                return Usage("Map file \"" + map + "\" doesn't exist!", ExitInvalidArguments);
            var legacy = o.Get("legacy");
            if ((legacy != null) && !File.Exists(legacy))
                return Usage("Legacy globals file \"" + legacy + "\" doesn't exist!", ExitInvalidArguments);
            opt.Map = Migrating.MigrationMap.Load(map, legacy == null ? null : CheckOptions.LoadLegacyGlobals(legacy));
            if (opt.BackupDir != null)
            {
                var b = Path.GetFullPath(opt.BackupDir);
                if (String.Equals(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return Usage("The backup folder can't be the migrated folder", ExitInvalidArguments);
            }
            var res = Migrating.Migrator.MigrateFolder(root, opt);
            if (!o.Has("quiet"))
            {
                foreach (var it in res.Items)
                    WriteColor(it.Kind == Migrating.MigrateItemKind.Warning ? ConsoleColor.Yellow : it.Kind == Migrating.MigrateItemKind.Change ? ConsoleColor.Green : ConsoleColor.Gray, it.Format());
                if (res.Items.Count > 0)
                    Console.WriteLine();
            }
            var report = o.Get("report");
            if (report != null)
            {
                var rd = Path.GetDirectoryName(Path.GetFullPath(report));
                if (!String.IsNullOrEmpty(rd))
                    Directory.CreateDirectory(rd);
                Migrating.Migrator.WriteReport(res, root, report);
                Console.WriteLine("Report written to " + Path.GetFullPath(report));
            }
            Console.WriteLine(Migrating.Migrator.Summary(res) + (opt.DryRun ? " [dry run]" : ""));
            return res.Warnings > 0 ? ExitProblems : ExitSuccess;
        }

        static int RunCheck(String[] args)
        {
            var o = new Options(args,
                new HashSet<String> { "strict", "design", "framework", "no-default-skip", "full-paths", "quiet" },
                new HashSet<String> { "skip", "template-var", "legacy" });
            if (o.Error != null)
                return Usage(o.Error, ExitInvalidArguments);
            if (o.Positional.Count != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(o.Positional[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var opt = new CheckOptions { Strict = o.Has("strict"), Design = o.Has("design"), Framework = o.Has("framework") };
            if (o.Has("no-default-skip"))
                opt.Skip.Clear();
            opt.Skip.AddRange(o.GetAll("skip"));
            foreach (var v in o.GetAll("template-var"))
                opt.ExtraTemplateVars.Add(v);
            var legacy = o.Get("legacy");
            if (legacy != null)
            {
                if (!File.Exists(legacy))
                    return Usage("Legacy globals file \"" + legacy + "\" doesn't exist!", ExitInvalidArguments);
                opt.LegacyGlobals = new HashSet<String>(CheckOptions.LoadLegacyGlobals(legacy), StringComparer.Ordinal);
            }
            var res = Checker.CheckFolder(root, opt);
            bool full = o.Has("full-paths");
            if (!o.Has("quiet"))
            {
                foreach (var f in res.Findings)
                {
                    var p = full ? Path.Combine(root, f.Path.Replace('/', Path.DirectorySeparatorChar)) : f.Path;
                    WriteColor(f.Severity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow, f.Format(p));
                }
                if (res.Findings.Count > 0)
                    Console.WriteLine();
            }
            foreach (var g in res.Findings.GroupBy(x => x.Rule.Id).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal))
                Console.WriteLine("  " + g.Key.PadRight(22) + g.Count());
            var errors = res.Errors;
            var warnings = res.Warnings;
            Console.WriteLine(res.FilesChecked + " file(s) checked, " + res.FilesSkipped + " file(s) and " + res.FoldersSkipped + " folder(s) skipped (vendored): " + errors + " error(s), " + warnings + " warning(s)" + (opt.Strict ? " [strict]" : "") + (opt.Design ? " [design]" : ""));
            if ((errors > 0) || (opt.Strict && (warnings > 0)))
                return ExitProblems;
            return ExitSuccess;
        }


        /// <summary>
        /// "SwWeb contracts &lt;repository&gt; [--out &lt;file&gt;]": the api rows of core / ui / shell / explore README.md as contract
        /// tests: the static checks (exit code 3 on a problem) and, with --out, the browser manifest (sw/readme_contracts.js).
        /// </summary>
        static int RunContracts(String[] args)
        {
            String outFile = null;
            var rest = new List<String>();
            for (int i = 0; i < args.Length; ++i)
                if ((args[i] == "--out") && (i + 1 < args.Length))
                    outFile = args[++i];
                else
                    rest.Add(args[i]);
            if (rest.Count != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(rest[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var (contracts, problems) = ReadmeContracts.Run(root);
            foreach (var p in problems)
                Console.WriteLine(p);
            Console.WriteLine(contracts.Count + " documented apis (" + contracts.Count(x => x.Options.Count > 0) + " with options, " + contracts.Count(x => x.Returns != null) + " with a return type, " + contracts.Count(x => x.Instance) + " instance / legacy members checked statically only): " + problems.Count + " problem(s)");
            if (outFile != null)
                File.WriteAllText(outFile, ReadmeContracts.Manifest(contracts));
            return problems.Count > 0 ? ExitProblems : ExitSuccess;
        }

        /// <summary>
        /// "SwWeb verify-build &lt;folder&gt;": check the built (compressed, minified) web outputs below a folder (<see cref="BuildVerifier"/>).
        /// </summary>
        static int RunVerifyBuild(String[] args)
        {
            var o = new Options(args, new HashSet<String> { "full-paths", "quiet" }, new HashSet<String>());
            if (o.Error != null)
                return Usage(o.Error, ExitInvalidArguments);
            if (o.Positional.Count != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(o.Positional[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var res = BuildVerifier.Run(root);
            var rel = o.Has("full-paths") ? null : root;
            if (!o.Has("quiet"))
                foreach (var p in res.Problems)
                    WriteColor(ConsoleColor.Red, p.ToString(rel));
            var total = res.Checked.Values.Sum();
            Console.WriteLine(total + " built file(s) checked (" + String.Join(", ", res.Checked.Select(x => x.Value + " " + x.Key)) + "): " + res.Problems.Count + " problem(s)");
            if (total == 0)
                WriteColor(ConsoleColor.Yellow, "No built outputs (web/**/*.br, *.gz) found below \"" + root + "\": build the projects first");
            return res.Problems.Count > 0 ? ExitProblems : ExitSuccess;
        }

        /// <summary>"SwWeb icons &lt;repository&gt;": print the inline icon names of the framework (the source of sw_icons.txt).</summary>
        static int RunIcons(String[] args)
        {
            if (args.Length != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(args[0]);
            var names = new SortedSet<String>(StringComparer.Ordinal);
            foreach (var r in Checker.FindWebRoots(root) ?? new List<String> { "" })
                names.UnionWith(UxChecker.CollectIconNames(Path.Combine(root, r)));
            Console.WriteLine("# The inline icon names of the framework (ui/base.js, ui/page.js): rule design-icon. Generated by: SwWeb icons <SysWeaver repository> > sw_icons.txt");
            foreach (var n in names)
                Console.WriteLine(n);
            return ExitSuccess;
        }
        /// <summary>
        /// "SwWeb tokens &lt;folder&gt;": print the --sw-* names the web roots of a folder define (the source of sw_tokens.txt).
        /// </summary>
        static int RunTokens(String[] args)
        {
            if (args.Length != 1)
                return Usage("Expected one <folder>", ExitInvalidArguments);
            var root = Path.GetFullPath(args[0]);
            if (!Directory.Exists(root))
                return Usage("Folder \"" + root + "\" doesn't exist!", ExitInvalidArguments);
            var names = new SortedSet<String>(StringComparer.Ordinal);
            var roots = Checker.FindWebRoots(root);
            if (roots == null)
                TokenChecker.CollectFolder(root, null, names);
            else
                foreach (var r in roots)
                    TokenChecker.CollectFolder(Path.Combine(root, r), null, names);
            Console.WriteLine("# The --sw-* tokens the SysWeaver framework defines (rule unknown-token).");
            Console.WriteLine("# Generated by: SwWeb tokens <SysWeaver repository> > sw_tokens.txt");
            foreach (var n in names)
                Console.WriteLine(n);
            return ExitSuccess;
        }
    }
}
