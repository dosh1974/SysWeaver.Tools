using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysWeaver
{
    /// <summary>
    /// Finds an app seed (AppSeed in the SysWeaver.AppInfo service parameters) whose generated application theme
    /// is as close as possible to some given colors (ex: the colors of a logo).
    /// Random seeds are tested, the theme colors of each seed are compared with the input colors (perceptual distance in OKLab), the closest seeds are kept.
    /// </summary>
    internal static class SeedMatchProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  The same values as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError.

        /// <summary>
        /// Exit code: success (also returned when help was requested).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error (ex: an invalid argument).
        /// </summary>
        const int ExitGenericError = 1;

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help";

        /// <summary>
        /// The names of the four colors of the application theme, in the order of <see cref="ThemeColors(HashColors)"/>
        /// (the same as SeedColors of the explore module and the "application" theme of common/theme.css).
        /// </summary>
        static readonly String[] Names = ["Background", "Main", "Acc1", "Acc2"];

        /// <summary>
        /// The four colors of the application theme of a seed: --ThemeBackground, --ThemeMain, --ThemeAcc1 and --ThemeAcc2
        /// (as set up by HttpServerBase: Background, Acc1, Acc3 and Acc4 of the HashColors).
        /// </summary>
        static String[] ThemeColors(HashColors c) => [c.Background, c.Acc1, c.Acc3, c.Acc4];

        static void Help()
        {
            Console.WriteLine("SeedMatch.exe [Name=]#rrggbb[*weight] [..] [-tries:N] [-top:N] [-start:N] [-name:AppName]");
            Console.WriteLine();
            Console.WriteLine("Finds an AppSeed whose generated application theme is closest to the given colors.");
            Console.WriteLine("  Name=#rrggbb     Compare with that theme color, Name is one of: " + String.Join(", ", Names));
            Console.WriteLine("  #rrggbb          Compare with the closest of the four theme colors (any one)");
            Console.WriteLine("  *weight          Optional weight of the color (default 1), ex: Main=#e522a4*2");
            Console.WriteLine("  -tries:N         The number of random seeds to test (default 1000000)");
            Console.WriteLine("  -top:N           The number of distinct themes to list (default 5)");
            Console.WriteLine("  -start:N         The seed of the random generator that picks the seeds to test (default 1, the same input gives the same result)");
            Console.WriteLine("  -name:AppName    The app name (only used for seed 0, which is never tested)");
            Console.WriteLine();
            Console.WriteLine("The distance is the perceptual (OKLab) distance * 100, weighted and averaged over the given colors.");
            Console.WriteLine("Exit codes: 0 = success or help, 1 = error");
        }

        sealed class Target
        {
            /// <summary>
            /// Index in <see cref="Names"/>, -1 = compare with the closest theme color
            /// </summary>
            public int Index;
            public String Text;
            public Lab Color;
            public double Weight;
        }

        sealed class Match
        {
            public int Seed;
            public double Distance;
            public String[] Colors;
            public double[] PerTarget;
        }

        static int Main(string[] args)
        {
            if ((args.Length == 0) || args.Any(IsHelp))
            {
                Help();
                return ExitSuccess;
            }
            try
            {
                var targets = new List<Target>();
                long tries = 1000000;
                int top = 5;
                int start = 1;
                String name = "App";
                var inv = CultureInfo.InvariantCulture;
                foreach (var a in args)
                {
                    if (a.StartsWith('-') || a.StartsWith('/'))
                    {
                        var i = a.IndexOf(':');
                        var key = (i < 0 ? a.Substring(1) : a.Substring(1, i - 1)).ToLowerInvariant();
                        var val = i < 0 ? "" : a.Substring(i + 1);
                        switch (key)
                        {
                            case "tries":
                                tries = long.Parse(val, inv);
                                break;
                            case "top":
                                top = int.Parse(val, inv);
                                break;
                            case "start":
                                start = int.Parse(val, inv);
                                break;
                            case "name":
                                name = val;
                                break;
                            default:
                                throw new Exception("Unknown option \"" + a + "\"");
                        }
                        continue;
                    }
                    var t = new Target { Index = -1, Weight = 1 };
                    var s = a;
                    var e = s.IndexOf('=');
                    if (e >= 0)
                    {
                        var n = s.Substring(0, e).Trim();
                        t.Index = Array.FindIndex(Names, x => String.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                        if (t.Index < 0)
                            throw new Exception("Unknown theme color \"" + n + "\", expected one of: " + String.Join(", ", Names));
                        s = s.Substring(e + 1);
                    }
                    var w = s.IndexOf('*');
                    if (w >= 0)
                    {
                        t.Weight = double.Parse(s.Substring(w + 1), inv);
                        if (!(t.Weight > 0))
                            throw new Exception("The weight must be greater than 0 in \"" + a + "\"");
                        s = s.Substring(0, w);
                    }
                    t.Text = s.Trim().ToLowerInvariant();
                    t.Color = Lab.FromWeb(t.Text);
                    targets.Add(t);
                }
                if (targets.Count == 0)
                    throw new Exception("No colors given");
                if (tries < 1)
                    throw new Exception("-tries must be at least 1");
                if (top < 1)
                    top = 1;

                //  The theme only depends on a few random values (18 hues * 10 saturations * 5 complements * 3 spreads),
                //  so many seeds share a theme: the distance is computed once per distinct theme, the first seed found is kept for it
                var themes = new Dictionary<String, Match>(StringComparer.Ordinal);
                var labCache = new Dictionary<String, Lab>(StringComparer.Ordinal);
                Lab ToLab(String c)
                {
                    if (!labCache.TryGetValue(c, out var l))
                        labCache[c] = l = Lab.FromWeb(c);
                    return l;
                }
                var rng = new Random(start);
                double wsum = targets.Sum(x => x.Weight);
                for (long i = 0; i < tries; ++i)
                {
                    //  Seed 0 means "from the app name": never tested
                    var seed = rng.Next(1, int.MaxValue);
                    var cols = ThemeColors(new HashColors(name, seed));
                    var key = String.Join(",", cols);
                    if (themes.ContainsKey(key))
                        continue;
                    var per = new double[targets.Count];
                    double sum = 0;
                    for (int k = 0; k < targets.Count; ++k)
                    {
                        var t = targets[k];
                        double d;
                        if (t.Index >= 0)
                            d = t.Color.Distance(ToLab(cols[t.Index]));
                        else
                            d = cols.Min(x => t.Color.Distance(ToLab(x)));
                        per[k] = d;
                        sum += d * t.Weight;
                    }
                    themes[key] = new Match { Seed = seed, Distance = sum / wsum, Colors = cols, PerTarget = per };
                }

                var best = themes.Values.OrderBy(x => x.Distance).ThenBy(x => x.Seed).Take(top).ToList();
                Console.WriteLine(String.Concat("Tested ", tries.ToString(inv), " seeds, ", themes.Count.ToString(inv), " distinct themes."));
                Console.WriteLine();
                int rank = 0;
                foreach (var m in best)
                {
                    ++rank;
                    Console.WriteLine(String.Concat("#", rank.ToString(inv), "  AppSeed: ", m.Seed.ToString(inv), "  distance: ", m.Distance.ToString("0.00", inv)));
                    for (int k = 0; k < Names.Length; ++k)
                        Console.WriteLine(String.Concat("    ", Names[k].PadRight(11), m.Colors[k]));
                    for (int k = 0; k < targets.Count; ++k)
                    {
                        var t = targets[k];
                        var against = t.Index >= 0 ? Names[t.Index] : "closest";
                        Console.WriteLine(String.Concat("      ", t.Text, " vs ", against, ": ", m.PerTarget[k].ToString("0.00", inv)));
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Best AppSeed: " + best[0].Seed.ToString(inv));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                return ExitGenericError;
            }
            return ExitSuccess;
        }

        /// <summary>
        /// A color in the OKLab color space (perceptually uniform)
        /// </summary>
        readonly struct Lab
        {
            readonly double L, A, B;

            Lab(double l, double a, double b)
            {
                L = l;
                A = a;
                B = b;
            }

            /// <summary>
            /// The perceptual distance * 100 (about 1 = just noticeable)
            /// </summary>
            public double Distance(Lab o)
            {
                var dl = L - o.L;
                var da = A - o.A;
                var db = B - o.B;
                return Math.Sqrt(dl * dl + da * da + db * db) * 100.0;
            }

            static double Linear(int c)
            {
                var v = c / 255.0;
                return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            /// <summary>
            /// Parse a "#rrggbb" or "#rgb" color
            /// </summary>
            public static Lab FromWeb(String c)
            {
                var s = c.Trim().TrimStart('#');
                if (s.Length == 3)
                    s = String.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
                if ((s.Length != 6) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    throw new Exception("Invalid color \"" + c + "\", expected #rrggbb");
                var r = Linear((int)(v >> 16) & 255);
                var g = Linear((int)(v >> 8) & 255);
                var b = Linear((int)v & 255);
                var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
                var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
                var q = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
                return new Lab(
                    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * q,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * q,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * q);
            }
        }
    }
}
