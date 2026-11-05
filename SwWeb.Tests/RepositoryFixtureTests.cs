using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// Runs the lexers on every web file of the SysWeaver repository (a sibling of SysWeaver.Tools), including vendored libraries (ace, prettier, chart.js, marked).
    /// Does nothing if the repository isn't found.
    /// </summary>
    public class RepositoryFixtureTests
    {
        static String RepoRoot([CallerFilePath] String file = null) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), "..", "..", "SysWeaver"));

        static IEnumerable<String> WebFiles(String ext)
        {
            var root = RepoRoot();
            if (!Directory.Exists(root))
                return Array.Empty<String>();
            return Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories)
                .Where(x => x.Contains(Path.DirectorySeparatorChar + "srcWeb" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AllJsFilesLexWithoutErrors()
        {
            var bad = new List<String>();
            int count = 0;
            foreach (var f in WebFiles(".js"))
            {
                ++count;
                var s = SourceText.FromFile(f);
                var tokens = JsLexer.Tokenize(s.Text);
                int pos = 0;
                foreach (var t in tokens)
                {
                    if (t.Start != pos)
                    {
                        bad.Add(f + ": gap at " + pos);
                        break;
                    }
                    pos = t.End;
                    if (t.Unterminated)
                    {
                        var (l, c) = s.GetLineCol(t.Start);
                        bad.Add(f + "(" + l + "," + c + "): unterminated " + t.Kind);
                        break;
                    }
                }
                if (pos != s.Text.Length)
                    bad.Add(f + ": not fully covered");
            }
            Assert.Empty(bad);
        }

        [Fact]
        public void AllHtmlFilesScanWithoutErrors()
        {
            var bad = new List<String>();
            foreach (var f in WebFiles(".html"))
            {
                var s = SourceText.FromFile(f);
                var nodes = HtmlScanner.Scan(s.Text);
                int pos = 0;
                foreach (var n in nodes)
                {
                    if ((n.Start != pos) || n.Unterminated)
                    {
                        bad.Add(f + ": at " + n);
                        break;
                    }
                    pos = n.End;
                }
                foreach (var sc in HtmlScanner.GetScripts(nodes).Where(x => x.IsJavaScript && x.BodyEnd > x.BodyStart))
                    foreach (var t in JsLexer.Tokenize(s.Text, sc.BodyStart, sc.BodyEnd).Where(t => t.Unterminated))
                        bad.Add(f + "(" + s.GetLineCol(t.Start) + "): unterminated " + t.Kind);
            }
            Assert.Empty(bad);
        }
        [Fact]
        public void EmbeddedTokenListHasEveryFrameworkToken()
        {
            //  sw_tokens.txt (rule unknown-token) must know every --sw-* token the framework defines: regenerate it with
            //  "SwWeb tokens <SysWeaver repository> > SwWeb/sw_tokens.txt" after adding a token
            var root = RepoRoot();
            if (!Directory.Exists(root))
                return;
            var defined = new SortedSet<String>(StringComparer.Ordinal);
            foreach (var wr in SwWeb.Checking.Checker.FindWebRoots(root) ?? new List<String>())
                SwWeb.Checking.TokenChecker.CollectFolder(Path.Combine(root, wr), null, defined);
            var missing = defined.Where(x => !SwWeb.Checking.TokenChecker.FrameworkTokens.Contains(x)).ToList();
            Assert.Empty(missing);
        }

        [Fact]
        public void EmbeddedTokenListHasNoRemovedToken()
        {
            //  The other direction: a token the framework no longer defines must leave sw_tokens.txt, else
            //  var(--sw-removed) passes the unknown-token rule silently (ex: --sw-brand-logo-ratio after the banner change).
            //  Regenerate the list with "SwWeb tokens <SysWeaver repository> > SwWeb/sw_tokens.txt"
            var root = RepoRoot();
            if (!Directory.Exists(root))
                return;
            var defined = new SortedSet<String>(StringComparer.Ordinal);
            foreach (var wr in SwWeb.Checking.Checker.FindWebRoots(root) ?? new List<String>())
                SwWeb.Checking.TokenChecker.CollectFolder(Path.Combine(root, wr), null, defined);
            Assert.True(defined.Count > 100, defined.Count + " tokens");
            var stale = SwWeb.Checking.TokenChecker.FrameworkTokens.Where(x => !defined.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Empty(stale);
        }

        [Fact]
        public void EmbeddedIconListHasEveryFrameworkIcon()
        {
            //  sw_icons.txt (rule design-icon) must know every inline icon of the framework: regenerate it with
            //  "SwWeb icons <SysWeaver repository> > SwWeb/sw_icons.txt" after adding an icon
            var root = RepoRoot();
            if (!Directory.Exists(root))
                return;
            var names = new SortedSet<String>(StringComparer.Ordinal);
            foreach (var wr in SwWeb.Checking.Checker.FindWebRoots(root) ?? new List<String>())
                names.UnionWith(SwWeb.Checking.UxChecker.CollectIconNames(Path.Combine(root, wr)));
            Assert.True(names.Count > 100, names.Count + " icons");
            Assert.Empty(names.Where(x => !SwWeb.Checking.UxChecker.IconNames.Contains(x)));
        }
    }
}
