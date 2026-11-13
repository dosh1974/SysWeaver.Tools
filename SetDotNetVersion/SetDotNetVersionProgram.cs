using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SetDotNetVersion
{
    internal static class SetDotNetVersionProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.

        /// <summary>
        /// Exit code: success (also returned when help was requested).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, an exception (ex: the folder doesn't exist).
        /// </summary>
        const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line (no arguments, unknown option, no folder etc).
        /// </summary>
        const int ExitInvalidArguments = 2;
        /// <summary>
        /// Exit code: one or more files couldn't be processed (the others are).
        /// </summary>
        const int ExitFileFailed = 3;

        /// <summary>
        /// The supported target frameworks (the option is "-" + the name, ex: "-net10.0" or "-net10").
        /// </summary>
        static readonly String[] Frameworks = ["net8.0", "net9.0", "net10.0", "net11.0"];

        /// <summary>
        /// The first major version that supports runtime async.
        /// </summary>
        const int RuntimeAsyncMinMajor = 11;

        /// <summary>
        /// Folders that are never searched.
        /// </summary>
        static readonly HashSet<String> SkipFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

        /// <summary>
        /// Files to process: project files, shared MSBuild files (ex: Directory.Build.props) and publish profiles.
        /// </summary>
        static readonly String[] FileMasks = ["*.csproj", "*.props", "*.targets", "*.pubxml"];

        enum RuntimeAsync
        {
            Unchanged,
            On,
            Off,
        }

        sealed class Options
        {
            public String Framework;
            public int FrameworkMajor;
            public RuntimeAsync RuntimeAsync;
            public bool DryRun;
            public String Folder;
        }

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help";

        static void WriteHelp()
        {
            Console.WriteLine("SetDotNetVersion.exe [Options] <Folder>");
            Console.WriteLine("Changes the target framework of all projects in a folder and it's sub folders.");
            Console.WriteLine("Processes *.csproj, *.props, *.targets and *.pubxml files (bin, obj, .git, .vs and node_modules are skipped).");
            Console.WriteLine();
            Console.WriteLine("Options:");
            foreach (var f in Frameworks)
                Console.WriteLine("  -" + f.PadRight(12) + "Set the target framework to " + f + " (also -" + f.Substring(0, f.Length - 2) + ").");
            Console.WriteLine("  -rasync      Enable runtime async (<Features>runtime-async=on</Features>), requires net11.0 or later.");
            Console.WriteLine("               Without a framework option it's applied to projects that already target net11.0 or later.");
            Console.WriteLine("  -rasync-     Disable (remove) runtime async.");
            Console.WriteLine("  -dry         Only show what would be changed, no files are written.");
            Console.WriteLine();
            Console.WriteLine("Only .NET (5+) target frameworks are changed, platform suffixes are kept (net9.0-windows -> net10.0-windows).");
            Console.WriteLine("Others, ex: netstandard2.0 or net48, are left as is. Runtime async is removed when a project no longer targets net11.0+.");
            Console.WriteLine("Exit codes: 0 = success or help, 1 = error, 2 = invalid command line, 3 = failed to process some files");
        }

        static int Main(string[] args)
        {
            var al = args.Length;
            if ((al <= 0) || ((al == 1) && IsHelp(args[0])))
            {
                WriteHelp();
                return al <= 0 ? ExitInvalidArguments : ExitSuccess;
            }
            var o = ParseArgs(args, out var error);
            if (o == null)
            {
                WriteError(error);
                Console.WriteLine("Use -? for help.");
                return ExitInvalidArguments;
            }
            try
            {
                var folder = Path.GetFullPath(o.Folder);
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException("The folder \"" + folder + "\" doesn't exist!");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Processing \"" + folder + "\"");
                if (o.Framework != null)
                    Console.Write(", framework: " + o.Framework);
                if (o.RuntimeAsync != RuntimeAsync.Unchanged)
                    Console.Write(", runtime async: " + (o.RuntimeAsync == RuntimeAsync.On ? "on" : "off"));
                if (o.DryRun)
                    Console.Write(" (dry run)");
                Console.WriteLine();
                Console.ResetColor();
                ProcessFolder(o, folder);
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
                return ExitGenericError;
            }
            Console.WriteLine();
            Console.ForegroundColor = FailedCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine(FileCount + " file(s) found, " + ChangedCount + (o.DryRun ? " would be changed" : " changed") + ", " + FailedCount + " failed.");
            Console.ResetColor();
            return FailedCount > 0 ? ExitFileFailed : ExitSuccess;
        }

        static Options ParseArgs(String[] args, out String error)
        {
            var o = new Options();
            foreach (var a in args)
            {
                if (a.Length > 1 && a[0] == '-')
                {
                    var key = a.TrimStart('-').ToLowerInvariant();
                    switch (key)
                    {
                        case "rasync":
                            o.RuntimeAsync = RuntimeAsync.On;
                            continue;
                        case "rasync-":
                            o.RuntimeAsync = RuntimeAsync.Off;
                            continue;
                        case "dry":
                        case "dryrun":
                        case "whatif":
                            o.DryRun = true;
                            continue;
                    }
                    var fw = Frameworks.FirstOrDefault(x => x == key || x == key + ".0");
                    if (fw == null)
                    {
                        error = "Unknown option \"" + a + "\"!";
                        return null;
                    }
                    if (o.Framework != null && o.Framework != fw)
                    {
                        error = "Only one framework option can be used!";
                        return null;
                    }
                    o.Framework = fw;
                    TryParseTfm(fw, out o.FrameworkMajor, out _);
                    continue;
                }
                if (o.Folder != null)
                {
                    error = "Only one folder can be given!";
                    return null;
                }
                o.Folder = a;
            }
            if (o.Folder == null)
            {
                error = "No folder given!";
                return null;
            }
            if (o.Framework == null && o.RuntimeAsync == RuntimeAsync.Unchanged)
            {
                error = "Nothing to do, specify a framework and/or -rasync / -rasync-!";
                return null;
            }
            if (o.Framework != null && o.RuntimeAsync == RuntimeAsync.On && o.FrameworkMajor < RuntimeAsyncMinMajor)
            {
                error = "Runtime async (-rasync) requires net" + RuntimeAsyncMinMajor + ".0 or later!";
                return null;
            }
            error = null;
            return o;
        }

        static int FileCount;
        static int ChangedCount;
        static int FailedCount;

        static void ProcessFolder(Options o, String folder)
        {
            foreach (var mask in FileMasks)
            {
                foreach (var f in Directory.GetFiles(folder, mask, SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
                {
                    //  "*.props" also matches "*.propsx" etc on Windows
                    if (!f.EndsWith(mask.Substring(1), StringComparison.OrdinalIgnoreCase))
                        continue;
                    ++FileCount;
                    ProcessFile(o, f);
                }
            }
            foreach (var d in Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (SkipFolders.Contains(Path.GetFileName(d)))
                    continue;
                ProcessFolder(o, d);
            }
        }

        static void ProcessFile(Options o, String file)
        {
            try
            {
                var data = File.ReadAllBytes(file);
                var enc = DetectEncoding(data, out var preambleLength);
                var text = enc.GetString(data, preambleLength, data.Length - preambleLength);
                var changes = new List<String>();
                var newText = Process(o, text, Path.GetExtension(file).Equals(".pubxml", StringComparison.OrdinalIgnoreCase), changes);
                if (newText == text)
                    return;
                if (!o.DryRun)
                {
                    var outData = enc.GetBytes(newText);
                    using var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                    fs.Write(data, 0, preambleLength);
                    fs.Write(outData);
                }
                ++ChangedCount;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  \"" + file + "\"");
                Console.ResetColor();
                foreach (var c in changes)
                    Console.WriteLine("      " + c);
            }
            catch (Exception ex)
            {
                ++FailedCount;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  \"" + file + "\"");
                Console.ResetColor();
                Console.WriteLine(" - " + ex.Message);
            }
        }

        /// <summary>
        /// Detect the encoding using the BOM, defaults to UTF-8 (without a BOM).
        /// </summary>
        static Encoding DetectEncoding(byte[] data, out int preambleLength)
        {
            var l = data.Length;
            if (l >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf)
            {
                preambleLength = 3;
                return new UTF8Encoding(false, true);
            }
            if (l >= 2 && data[0] == 0xff && data[1] == 0xfe)
            {
                preambleLength = 2;
                return new UnicodeEncoding(false, false, true);
            }
            if (l >= 2 && data[0] == 0xfe && data[1] == 0xff)
            {
                preambleLength = 2;
                return new UnicodeEncoding(true, false, true);
            }
            preambleLength = 0;
            return new UTF8Encoding(false, true);
        }

        #region Text processing

        /// <summary>
        /// Parse a .NET (5+) target framework moniker, ex: "net10.0" or "net10.0-windows".
        /// </summary>
        /// <param name="tfm">The target framework moniker</param>
        /// <param name="major">The major version</param>
        /// <param name="suffix">The platform suffix including the dash (ex: "-windows") or an empty string</param>
        /// <returns>True if this is a .NET 5+ target framework</returns>
        static bool TryParseTfm(String tfm, out int major, out String suffix)
        {
            var m = TfmRegex.Match(tfm);
            if (m.Success && int.TryParse(m.Groups[1].ValueSpan, out major) && major >= 5)
            {
                suffix = m.Groups[3].Value;
                return true;
            }
            major = 0;
            suffix = null;
            return false;
        }

        static readonly Regex TfmRegex = new(@"^net(\d+)\.(\d+)(-.+)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex TfmElementRegex = new(@"(<(TargetFrameworks?)(?:\s[^>]*)?>)([^<]*)(</\2\s*>)", RegexOptions.CultureInvariant);

        static readonly Regex PublishDirRegex = new(@"(<(PublishDir|PublishUrl)(?:\s[^>]*)?>)([^<]*)(</\2\s*>)", RegexOptions.CultureInvariant);

        static readonly Regex PathTfmRegex = new(@"(?<=^|[\\/])net\d+\.\d+(?:-[^\\/]+)?(?=[\\/]|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex FeaturesRegex = new(@"(<Features(?:\s[^>]*)?>)([^<]*)(</Features\s*>)", RegexOptions.CultureInvariant);

        /// <summary>
        /// A property group that only contains runtime async (as added by this tool), including the line and any preceding empty line.
        /// </summary>
        static readonly Regex RuntimeAsyncGroupRegex = new(@"(?:\r?\n[ \t]*)?\r?\n[ \t]*<PropertyGroup>\s*<Features>\s*runtime-async\s*=\s*[^<;]*</Features>\s*</PropertyGroup>[ \t]*(?=\r?\n|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// A Features element that only contains runtime async, including the line.
        /// </summary>
        static readonly Regex RuntimeAsyncLineRegex = new(@"\r?\n[ \t]*<Features(?:\s[^>]*)?>\s*runtime-async\s*=\s*[^<;]*</Features\s*>[ \t]*(?=\r?\n|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        const String RuntimeAsyncOn = "runtime-async=on";

        static bool IsRuntimeAsync(String feature) => feature.Trim().StartsWith("runtime-async", StringComparison.OrdinalIgnoreCase);

        static String Process(Options o, String text, bool isPublishProfile, List<String> changes)
        {
            //  Change target frameworks, collect all .NET 5+ majors found (after any change)
            var majors = new List<int>();
            bool hasOther = false;
            text = TfmElementRegex.Replace(text, m =>
            {
                var value = m.Groups[3].Value;
                var tfms = value.Split(';');
                var result = new List<String>(tfms.Length);
                foreach (var raw in tfms)
                {
                    var t = raw.Trim();
                    if (t.Length == 0)
                        continue;
                    if (TryParseTfm(t, out var major, out var suffix))
                    {
                        if (o.Framework != null)
                        {
                            t = o.Framework + suffix;
                            major = o.FrameworkMajor;
                        }
                        majors.Add(major);
                    }
                    else
                    {
                        hasOther = true;
                    }
                    if (!result.Contains(t, StringComparer.OrdinalIgnoreCase))
                        result.Add(t);
                }
                var newValue = String.Join(';', result);
                if (newValue == value.Trim())
                    return m.Value;
                changes.Add(m.Groups[2].Value + ": " + value.Trim() + " -> " + newValue);
                return m.Groups[1].Value + newValue + m.Groups[4].Value;
            });

            //  Publish profiles: change the framework folder in the publish path (ex: bin\Release\net10.0\publish\)
            if (isPublishProfile && o.Framework != null)
            {
                text = PublishDirRegex.Replace(text, m =>
                {
                    var value = m.Groups[3].Value;
                    var newValue = PathTfmRegex.Replace(value, pm => TryParseTfm(pm.Value, out _, out var suffix) ? o.Framework + suffix : pm.Value);
                    if (newValue == value)
                        return m.Value;
                    changes.Add(m.Groups[2].Value + ": " + value + " -> " + newValue);
                    return m.Groups[1].Value + newValue + m.Groups[4].Value;
                });
            }

            //  Runtime async isn't applied to publish profiles, or to files without a .NET target framework
            if (isPublishProfile || majors.Count <= 0)
                return text;
            var supportsRuntimeAsync = !hasOther && majors.All(x => x >= RuntimeAsyncMinMajor);
            var ra = o.RuntimeAsync;
            if (!supportsRuntimeAsync)
            {
                if (ra == RuntimeAsync.On)
                {
                    //  Only a warning if the user asked for net11+ (some target framework in the file isn't .NET 11+)
                    changes.Add("Runtime async NOT enabled, all target frameworks must be net" + RuntimeAsyncMinMajor + ".0 or later!");
                    if (o.Framework == null)
                        return text;
                }
                ra = RuntimeAsync.Off;
            }
            if (ra == RuntimeAsync.On)
                return EnableRuntimeAsync(text, changes);
            if (ra == RuntimeAsync.Off)
                return DisableRuntimeAsync(text, changes);
            return text;
        }

        static String EnableRuntimeAsync(String text, List<String> changes)
        {
            //  Existing Features element(s)?
            var features = FeaturesRegex.Matches(text);
            if (features.Count > 0)
            {
                //  Already have a runtime-async feature, make sure it's on
                bool found = false;
                var newText = FeaturesRegex.Replace(text, m =>
                {
                    var value = m.Groups[2].Value;
                    var items = value.Split(';');
                    bool changed = false;
                    for (int i = 0; i < items.Length; ++i)
                    {
                        if (!IsRuntimeAsync(items[i]))
                            continue;
                        found = true;
                        if (String.Equals(items[i].Trim().Replace(" ", ""), RuntimeAsyncOn, StringComparison.OrdinalIgnoreCase))
                            continue;
                        items[i] = RuntimeAsyncOn;
                        changed = true;
                    }
                    if (!changed)
                        return m.Value;
                    var newValue = String.Join(';', items);
                    changes.Add("Features: " + value + " -> " + newValue);
                    return m.Groups[1].Value + newValue + m.Groups[3].Value;
                });
                if (found)
                    return newText;
                //  Append to the first unconditional Features element
                var first = features.FirstOrDefault(x => !x.Groups[1].Value.Contains("Condition", StringComparison.OrdinalIgnoreCase));
                if (first != null)
                {
                    var value = first.Groups[2].Value.Trim();
                    var newValue = value.Length == 0 ? RuntimeAsyncOn : (value.TrimEnd(';') + ";" + RuntimeAsyncOn);
                    changes.Add("Features: " + value + " -> " + newValue);
                    return text.Substring(0, first.Index) + first.Groups[1].Value + newValue + first.Groups[3].Value + text.Substring(first.Index + first.Length);
                }
            }

            //  Add a new property group after the first property group that defines the target framework(s)
            var tfm = TfmElementRegex.Match(text);
            var groupEnd = text.IndexOf("</PropertyGroup>", tfm.Index, StringComparison.Ordinal);
            if (groupEnd < 0)
                throw new Exception("Can't find the end of the PropertyGroup with the target framework!");
            groupEnd += "</PropertyGroup>".Length;
            var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lineStart = text.LastIndexOf('\n', groupEnd - 1) + 1;
            var indent = new String(text.Skip(lineStart).TakeWhile(c => c is ' ' or '\t').ToArray());
            var childIndent = GetChildIndent(text, tfm.Index, indent);
            var insert = nl + nl
                + indent + "<PropertyGroup>" + nl
                + childIndent + "<Features>" + RuntimeAsyncOn + "</Features>" + nl
                + indent + "</PropertyGroup>";
            changes.Add("Features: added " + RuntimeAsyncOn);
            return text.Substring(0, groupEnd) + insert + text.Substring(groupEnd);
        }

        /// <summary>
        /// Get the indentation of the line at some position (the target framework element), falls back to the group indent + 2 spaces.
        /// </summary>
        static String GetChildIndent(String text, int pos, String groupIndent)
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
            var indent = text.Substring(lineStart, pos - lineStart);
            return (indent.Length > groupIndent.Length && indent.All(c => c is ' ' or '\t')) ? indent : (groupIndent + "  ");
        }

        static String DisableRuntimeAsync(String text, List<String> changes)
        {
            //  Remove whole groups / elements that only enable runtime async
            var newText = RuntimeAsyncGroupRegex.Replace(text, "");
            newText = RuntimeAsyncLineRegex.Replace(newText, "");
            //  Remove runtime-async from Features elements that contain other features
            newText = FeaturesRegex.Replace(newText, m =>
            {
                var value = m.Groups[2].Value;
                var items = value.Split(';');
                if (!items.Any(IsRuntimeAsync))
                    return m.Value;
                return m.Groups[1].Value + String.Join(';', items.Where(x => !IsRuntimeAsync(x))) + m.Groups[3].Value;
            });
            if (newText != text)
                changes.Add("Features: removed runtime-async");
            return newText;
        }

        #endregion

        static void WriteError(String message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
