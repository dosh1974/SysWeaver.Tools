using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Migrating;
using Xunit.Abstractions;

namespace SwWeb.Tests
{
    /// <summary>
    /// The frozen corpus of planted UX mistakes (Corpus/expected.txt): every page the critics planted mistakes in (series 4 rounds 1-2) and
    /// the held-out set written for round 2 without seeing the checker. Each planted mistake that the checker finds is a regression test,
    /// the right patterns of the same pages must stay without a finding, and the catch rate per set is printed. New sets are added per
    /// round, so a rule tuned to one spelling shows up as a low rate on the next unseen set.
    /// </summary>
    public class CorpusTests
    {
        readonly ITestOutputHelper Out;

        public CorpusTests(ITestOutputHelper output)
        {
            Out = output;
        }

        static readonly MigrationMap Map = MigrationMap.Load();

        sealed class Item
        {
            public String Set, Id, File, Kind;
            public int From, To;
            /// <summary>Other places that count as the same mistake (file:line[-line];file:line ...), ex: the sink or the helper that builds the html.</summary>
            public List<(String File, int From, int To)> Alt = new List<(String, int, int)>();
            public String[] Rules;
            public bool Runtime;
            public override String ToString() => Set + " " + Id + " " + File + ":" + From + " " + String.Join("|", Rules ?? Array.Empty<String>());
        }

        static String CorpusDir()
        {
            var d = Path.Combine(AppContext.BaseDirectory, "Corpus");
            if (Directory.Exists(d))
                return d;
            for (var p = new DirectoryInfo(AppContext.BaseDirectory); p != null; p = p.Parent)
                if (Directory.Exists(Path.Combine(p.FullName, "Corpus")) && File.Exists(Path.Combine(p.FullName, "SwWeb.Tests.csproj")))
                    return Path.Combine(p.FullName, "Corpus");
            throw new DirectoryNotFoundException("Corpus");
        }

        static List<Item> Load()
        {
            var r = new List<Item>();
            foreach (var raw in File.ReadAllLines(Path.Combine(CorpusDir(), "expected.txt")))
            {
                var line = raw;
                var h = line.IndexOf('#');
                if (h >= 0)
                    line = line.Substring(0, h);
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3)
                    continue;
                var it = new Item { Set = p[0], Id = p[1] };
                if (p[1] == "clean")
                {
                    it.Kind = "clean";
                    it.File = p[2];
                    r.Add(it);
                    continue;
                }
                var places = p[2].Split(';');
                foreach (var at in places)
                {
                    var c = at.LastIndexOf(':');
                    var lines = at.Substring(c + 1).Split('-');
                    var pl = (at.Substring(0, c), Int32.Parse(lines[0]), lines.Length > 1 ? Int32.Parse(lines[1]) : Int32.Parse(lines[0]));
                    if (it.File == null)
                        (it.File, it.From, it.To) = pl;
                    else
                        it.Alt.Add(pl);
                }
                it.Kind = p[1] == "-" ? "not" : p[1] == "ok" ? "ok" : "planted";
                it.Rules = p.Length > 3 ? p[3].Split('|') : Array.Empty<String>();
                it.Runtime = p.Contains("runtime");
                r.Add(it);
            }
            return r;
        }

        static readonly Dictionary<String, List<Finding>> Cache = new Dictionary<String, List<Finding>>();

        static List<Finding> Check(String set)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(set, out var f))
                {
                    var res = Checker.CheckFolder(Path.Combine(CorpusDir(), set), new CheckOptions { Design = true, Strict = true, Map = Map });
                    Cache[set] = f = res.Findings.Where(x => (x.Rule.Guide != null) || x.Rule.Id.StartsWith("a11y-", StringComparison.Ordinal) || (x.Rule.Id == "design-confirm") || (x.Rule.Id == "tr-untranslated") || (x.Rule.Id == "external-resource") || (x.Rule.Id == "design-font") || (x.Rule.Id == "design-icon") || (x.Rule.Id == "tr-not-literal")).ToList();
                }
                return f;
            }
        }

        static bool Hit(Item it, Finding f) => (String.Equals(f.Path, it.File, StringComparison.OrdinalIgnoreCase) && (f.Line >= it.From) && (f.Line <= it.To))
            || it.Alt.Any(a => String.Equals(f.Path, a.File, StringComparison.OrdinalIgnoreCase) && (f.Line >= a.From) && (f.Line <= a.To));

        public static IEnumerable<object[]> Planted() => Load().Where(x => (x.Kind == "planted") && !x.Runtime).Select(x => new object[] { x.ToString() });

        public static IEnumerable<object[]> Right() => Load().Where(x => x.Kind != "planted").Select(x => new object[] { x.Set + " " + x.Kind + " " + x.File + ":" + x.From + " " + String.Join("|", x.Rules ?? Array.Empty<String>()) });

        static Item Find(String key) => Load().First(x => (x.ToString() == key) || ((x.Set + " " + x.Kind + " " + x.File + ":" + x.From + " " + String.Join("|", x.Rules ?? Array.Empty<String>())) == key));

        [Theory]
        [MemberData(nameof(Planted))]
        public void PlantedMistakeIsFound(String key)
        {
            var it = Find(key);
            var f = Check(it.Set).Where(x => Hit(it, x)).ToList();
            Assert.True(f.Any(x => it.Rules.Contains(x.Rule.Id)), key + ": found " + (f.Count == 0 ? "nothing" : String.Join(", ", f.Select(x => x.Rule.Id))));
        }

        [Theory]
        [MemberData(nameof(Right))]
        public void RightPatternIsLeftAlone(String key)
        {
            var it = Find(key);
            var all = Check(it.Set);
            if (it.Kind == "clean")
                Assert.Empty(all.Where(x => String.Equals(x.Path, it.File, StringComparison.OrdinalIgnoreCase) && (x.Rule.Id != "tr-untranslated")).Select(x => x.Line + " " + x.Rule.Id));
            else if (it.Kind == "ok")
                Assert.Empty(all.Where(x => Hit(it, x) && x.Rule.Guide != null).Select(x => x.Line + " " + x.Rule.Id));
            else
                Assert.Empty(all.Where(x => Hit(it, x) && it.Rules.Contains(x.Rule.Id)).Select(x => x.Line + " " + x.Rule.Id + ": " + x.Message));
        }

        /// <summary>The catch rate per set (printed; the per item tests above are the assertions).</summary>
        [Fact]
        public void CatchRate()
        {
            var items = Load().Where(x => x.Kind == "planted").ToList();
            int all = 0, caught = 0;
            foreach (var g in items.GroupBy(x => x.Set))
            {
                var stat = g.Where(x => !x.Runtime).ToList();
                var f = Check(g.Key);
                int c = stat.Count(it => f.Any(x => Hit(it, x) && it.Rules.Contains(x.Rule.Id)));
                Out.WriteLine(g.Key + ": " + c + " of " + stat.Count + " static" + (g.Count() > stat.Count ? " (+" + (g.Count() - stat.Count) + " run time only)" : ""));
                all += stat.Count;
                caught += c;
            }
            Out.WriteLine("corpus: " + caught + " of " + all);
            Assert.Equal(all, caught);
        }
    }
}
