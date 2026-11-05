using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SwWeb.Checking
{
    /// <summary>
    /// Options for <see cref="Checker"/>.
    /// </summary>
    public sealed class CheckOptions
    {
        /// <summary>
        /// Vendored / third party paths that are never checked (see <see cref="PathMatcher"/> for the pattern syntax).
        /// </summary>
        public static readonly IReadOnlyList<String> DefaultSkip = new[]
        {
            // Folders
            "external/",        // chart/external, edit/external (ace, prettier), icons/external
            "prettier/",
            "ace/",
            "node_modules/",
            ".*/",              // hidden folders, ex: icons/.opt
            "bin/",
            "obj/",
            // Files
            "*.min.js",
            "marked.js",        // common/marked.js
            "md5.js",           // common/md5.js
            "UZIP.js",          // common/UZIP.js
            "chart.umd.js",
            "chartjs-*.js",
            "brand/fonts/*.ttf", // the brand font sources (served as data urls by brand/fonts.css)
            "*.br",             // compressed build outputs
            "*.gz",
        };

        /// <summary>
        /// Templated files (relative to the web root, case sensitive): only known template variables may appear in them.
        /// </summary>
        public static readonly IReadOnlyList<String> DefaultTemplatedFiles = new[]
        {
            "index.html",
            "debug.html",
            "app/manifest.json",
            "app/Home.html",
            "common/theme.css",
        };

        /// <summary>
        /// Legacy globals that new-format pages may still use (permanent API).
        /// </summary>
        public static readonly IReadOnlyList<String> PermanentGlobals = new[]
        {
            "_T", "_TF", "_TH", "_TFH", "setTheme", "resetTheme", "toggleTheme", "appendTheme", "getTheme", "applyTheme", "Build", "appMain",
        };

        /// <summary>
        /// Enable strict checks (legacy globals in new-format pages) and fail on warnings.
        /// </summary>
        public bool Strict;

        /// <summary>
        /// Also run the Design v2 porting rules (<see cref="Rule.Design"/>: page header, shadows, gradients, hard-coded colours, old tokens
        /// and class hooks, inline styles, accessible custom controls). Warnings, so they only fail with <see cref="Strict"/>.
        /// </summary>
        public bool Design;

        /// <summary>
        /// Also run the UX guide rules (<see cref="Rule.Guide"/>, "ux-*") on the framework's own files (core/, ui/, shell/, common/, brand/,
        /// app/application.*; never the sw/ bundles), which the design rules skip ("check --design --framework").
        /// </summary>
        public bool Framework;

        /// <summary>
        /// The reserved sizes that the css of the checked web roots gives to classes, ids and (per folder) bare img / iframe / video
        /// selectors (rule ux-lay-1; set by Checker.CheckFolder, null = only the page's own style elements count).
        /// </summary>
        public UxChecker.SizeIndex UxSizes;

        /// <summary>
        /// The mapping table whose "Tokens" / "Css" entries the Design v2 rules use (null = the embedded default, loaded on first use).
        /// </summary>
        public Migrating.MigrationMap Map;

        /// <summary>
        /// Optional file contents that replace the files on disk (full path, case insensitive): "migrate --level design" checks the
        /// migrated texts this way, so its report lists what "check --strict --design" reports after the migration (also in a dry run).
        /// </summary>
        public IReadOnlyDictionary<String, String> Overlay;

        /// <summary>
        /// Paths to skip.
        /// </summary>
        public List<String> Skip = new List<String>(DefaultSkip);

        /// <summary>
        /// Templated files (relative paths, case sensitive).
        /// </summary>
        public List<String> TemplatedFiles = new List<String>(DefaultTemplatedFiles);

        /// <summary>
        /// Extra known template variable names (ex: config "Variables" keys).
        /// </summary>
        public HashSet<String> ExtraTemplateVars = new HashSet<String>(StringComparer.Ordinal);

        /// <summary>
        /// The bundle manifest of rule bundle-overlap: null = the checked web root's sw/bundles.json when it has one (set by
        /// Checker.CheckFolder), else the framework's built-in copy (PageChecker.BuiltInBundles).
        /// </summary>
        public Bundling.BundleManifest Bundles;

        /// <summary>
        /// The --sw-* token names that are defined (rule unknown-token): null = the framework list (sw_tokens.txt) plus the definitions in the
        /// checked folder (set by Checker.CheckFolder).
        /// </summary>
        public HashSet<String> KnownTokens;

        /// <summary>
        /// The values of the page's own custom properties (not --sw-*, not in the framework style sheets) defined in the checked web roots: the
        /// design rules resolve var(--my-shadow) before they judge a shadow, gradient or radius (set by Checker.CheckFolder).
        /// </summary>
        public Dictionary<String, List<String>> OwnProperties;

        /// <summary>
        /// Legacy global names (permanent globals are ignored).
        /// </summary>
        public HashSet<String> LegacyGlobals = new HashSet<String>(LoadLegacyGlobals(null), StringComparer.Ordinal);

        /// <summary>
        /// The file name of the legacy globals list.
        /// </summary>
        public const String LegacyGlobalsFileName = "legacy_globals.txt";

        /// <summary>
        /// Parse a legacy globals list: one name per line, '#' starts a comment, empty lines are ignored.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The names (without the permanent globals)</returns>
        public static List<String> ParseLegacyGlobals(String text)
        {
            var res = new List<String>();
            var perm = new HashSet<String>(PermanentGlobals, StringComparer.Ordinal);
            foreach (var l in text.Split('\n'))
            {
                var x = l;
                var i = x.IndexOf('#');
                if (i >= 0)
                    x = x.Substring(0, i);
                x = x.Trim();
                if ((x.Length > 0) && !perm.Contains(x))
                    res.Add(x);
            }
            return res;
        }

        /// <summary>
        /// Load the legacy globals list: the given file, else "legacy_globals.txt" next to the tool, else the embedded copy.
        /// </summary>
        /// <param name="fileName">An optional file name</param>
        /// <returns>The names</returns>
        public static List<String> LoadLegacyGlobals(String fileName)
        {
            if (fileName != null)
                return ParseLegacyGlobals(File.ReadAllText(fileName));
            var local = Path.Combine(AppContext.BaseDirectory, LegacyGlobalsFileName);
            if (File.Exists(local))
                return ParseLegacyGlobals(File.ReadAllText(local));
            using var s = typeof(CheckOptions).Assembly.GetManifestResourceStream("SwWeb." + LegacyGlobalsFileName);
            if (s == null)
                return new List<String>();
            using var r = new StreamReader(s);
            return ParseLegacyGlobals(r.ReadToEnd());
        }
    }
}
