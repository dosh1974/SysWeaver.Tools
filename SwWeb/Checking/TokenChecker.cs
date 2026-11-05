using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwWeb.Checking
{
    /// <summary>
    /// The unknown-token rule: a var(--sw-*) use (without a fallback) of a name that neither the framework (the embedded list
    /// sw_tokens.txt, generated from the SysWeaver web roots with "SwWeb tokens") nor the checked folders define. Css drops such a
    /// declaration silently, so a typo (--sw-accent for --sw-accent1, --sw-fs-sm for --sw-text-sm) is a run time breaker.
    /// </summary>
    public static class TokenChecker
    {
        /// <summary>The file name of the embedded framework token list.</summary>
        public const String TokenListFileName = "sw_tokens.txt";

        /// <summary>A definition: "--sw-x:" (css, style strings) or a quoted "--sw-x" (setProperty, getPropertyValue, style objects).</summary>
        static readonly Regex Definition = new Regex(@"(?<![\w-])(--sw-[A-Za-z0-9_-]+)(?=\s*:|[""'`])", RegexOptions.CultureInvariant);

        /// <summary>A use: var(--sw-x) or var(--sw-x, fallback) (group 2 = ")" or ",").</summary>
        static readonly Regex Use = new Regex(@"var\(\s*(--sw-[A-Za-z0-9_-]+)\s*([,)])", RegexOptions.CultureInvariant);

        /// <summary>Css / html comments (blanked before looking for uses).</summary>
        static readonly Regex Comment = new Regex(@"/\*.*?\*/|<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        /// <summary>Common wrong name parts and the framework's word for them.</summary>
        static readonly (String From, String To)[] Synonyms =
        {
            ("-fs-", "-text-"), ("-font-size-", "-text-"), ("-size-", "-text-"), ("-accent-", "-accent1-"), ("-primary-", "-accent1-"),
            ("-error-", "-danger-"), ("-success-", "-ok-"), ("-warn-", "-warning-"), ("-bg-", "-surface-"), ("-background-", "-surface-"),
            ("-fg-", "-text-"), ("-color-", "-text-"), ("-shadow-", "-elev-"), ("-rounded-", "-radius-"), ("-spacing-", "-space-"),
        };

        static HashSet<String> embedded;

        /// <summary>
        /// The --sw-* names the framework defines (embedded copy of sw_tokens.txt; a sw_tokens.txt next to the tool overrides it).
        /// </summary>
        public static HashSet<String> FrameworkTokens
        {
            get
            {
                if (embedded != null)
                    return embedded;
                String text = null;
                var local = Path.Combine(AppContext.BaseDirectory, TokenListFileName);
                if (File.Exists(local))
                    text = File.ReadAllText(local);
                else
                {
                    using var s = typeof(TokenChecker).Assembly.GetManifestResourceStream("SwWeb." + TokenListFileName);
                    if (s != null)
                    {
                        using var r = new StreamReader(s);
                        text = r.ReadToEnd();
                    }
                }
                var res = new HashSet<String>(StringComparer.Ordinal);
                foreach (var l in (text ?? "").Split('\n'))
                {
                    var x = l;
                    var i = x.IndexOf('#');
                    if (i >= 0)
                        x = x.Substring(0, i);
                    x = x.Trim();
                    if (x.StartsWith("--sw-", StringComparison.Ordinal))
                        res.Add(x);
                }
                return embedded = res;
            }
        }

        /// <summary>
        /// Add the --sw-* names that a text defines.
        /// </summary>
        /// <param name="text">A css, js or html text</param>
        /// <param name="into">Receives the names</param>
        public static void CollectDefinitions(String text, ISet<String> into)
        {
            foreach (Match m in Definition.Matches(text))
                into.Add(m.Groups[1].Value);
        }

        /// <summary>
        /// The --sw-* names defined by the web files (.css, .js, .html) below a folder (skipped folders / files excluded).
        /// </summary>
        /// <param name="folder">The folder</param>
        /// <param name="options">The options (skip list), null for the default skip list</param>
        /// <param name="into">Receives the names</param>
        public static void CollectFolder(String folder, CheckOptions options, ISet<String> into, Action<String, String> onFile = null)
        {
            var skip = new PathMatcher(options?.Skip ?? (IEnumerable<String>)CheckOptions.DefaultSkip);
            void Walk(String dir, String rel)
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    var r = rel + Path.GetFileName(d);
                    if (!skip.IsFolderMatch(r))
                        Walk(d, r + "/");
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext != ".css") && (ext != ".js") && (ext != ".html") && (ext != ".htm"))
                        continue;
                    if (skip.IsFileMatch(rel + Path.GetFileName(f)))
                        continue;
                    var text = File.ReadAllText(f);
                    CollectDefinitions(text, into);
                    onFile?.Invoke(rel + Path.GetFileName(f), text);
                }
            }
            if (Directory.Exists(folder))
                Walk(folder, "");
        }

        /// <summary>
        /// Report the var(--sw-*) uses without a fallback of names that are not defined (rule unknown-token).
        /// </summary>
        /// <param name="ctx">The file (css, js or html)</param>
        /// <param name="known">The defined names (framework + checked folders)</param>
        public static void Check(Checker.FileContext ctx, ISet<String> known)
        {
            if ((known == null) || (known.Count == 0))
                return;
            var text = ctx.Text;
            //  Blank the comments (same length, so the offsets stay)
            var t = Comment.Replace(text, m => new String(' ', m.Length));
            foreach (Match m in Use.Matches(t))
            {
                var name = m.Groups[1].Value;
                if (known.Contains(name) || ctx.HasMarker(m.Index, AllowToken))
                    continue;
                var near = Closest(name, known, out var close);
                bool fallback = m.Groups[2].Value == ",";
                //  With a fallback the name may be a deliberate optional hook: only a near miss of a real token is reported
                if (fallback && !close)
                    continue;
                ctx.Add(Rule.UnknownToken, m.Groups[1].Index, "\"" + name + "\" is not a SysWeaver token ("
                    + (fallback ? "the fallback is always used" : "css drops the declaration silently") + ")"
                    + (near != null ? ": did you mean \"" + near + "\"?" : "") + " The tokens are listed in ui/tokens.css / ui/README.md Theming; your own token needs a definition" + (fallback ? "" : " (or a fallback: var(" + name + ", ...))"));
            }
        }

        /// <summary>A comment that allows an unknown token on the line or the line below (ex: a token defined by a host page).</summary>
        public const String AllowToken = "SwWeb:allow-token";

        /// <summary>
        /// The closest known name: a synonym rewrite (fs -> text, accent -> accent1 ...), else the smallest edit distance (at most 1/3 of the name).
        /// </summary>
        /// <param name="name">The unknown name</param>
        /// <param name="known">The known names</param>
        /// <returns>The suggestion or null</returns>
        public static String Closest(String name, ISet<String> known) => Closest(name, known, out _);

        /// <summary>
        /// The closest known name, and whether it is a near miss (a synonym rewrite or one edit away).
        /// </summary>
        public static String Closest(String name, ISet<String> known, out bool close)
        {
            close = false;
            //  A missing hyphen before a number ("--sw-text2" -> "--sw-text-2")
            var hy = Regex.Replace(name, @"(?<=[a-z])(?=\d+$)", "-");
            if ((hy != name) && known.Contains(hy))
            {
                close = true;
                return hy;
            }
            var padded = name + "-";
            foreach (var (from, to) in Synonyms)
            {
                var i = padded.IndexOf(from, 4, StringComparison.Ordinal);
                if (i < 0)
                    continue;
                var x = (padded.Substring(0, i) + to + padded.Substring(i + from.Length)).TrimEnd('-');
                if (known.Contains(x))
                {
                    close = true;
                    return x;
                }
            }
            String best = null;
            int bestD = int.MaxValue;
            foreach (var k in known.OrderBy(x => x, StringComparer.Ordinal))
            {
                var d = Distance(name, k);
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            close = bestD <= 1;
            return bestD <= Math.Max(2, (name.Length - 5) / 3) ? best : null;
        }

        static int Distance(String a, String b)
        {
            var p = new int[b.Length + 1];
            var c = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; ++j)
                p[j] = j;
            for (int i = 1; i <= a.Length; ++i)
            {
                c[0] = i;
                for (int j = 1; j <= b.Length; ++j)
                    c[j] = Math.Min(Math.Min(c[j - 1] + 1, p[j] + 1), p[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (p, c) = (c, p);
            }
            return p[b.Length];
        }
    }
}
