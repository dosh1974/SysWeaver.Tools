using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SysWeaver.Converter.Js;

namespace SysWeaver
{
    internal static class JsConvertProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0 = success or help requested, 1 = an exception while processing, 2 = invalid command line (from SysWeaver.CommandLine FilesToFolderTool).

        /// <summary>
        /// Exit code: one or more files couldn't be converted (errors, or warnings with -WarningsAsErrors), the other files are converted.
        /// </summary>
        const int ExitConvertFailed = 3;

        static int Failed;
        static readonly Object Lock = new Object();
        static readonly HashSet<String> UsedHelpers = new HashSet<String>(StringComparer.Ordinal);
        static readonly HashSet<String> UsedPolyfills = new HashSet<String>(StringComparer.Ordinal);
        static String LowestDestFolder;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static bool IsHtml(String file)
        {
            var e = Path.GetExtension(file).ToLowerInvariant();
            return e == ".html" || e == ".htm" || e == ".xhtml";
        }

        static MessageLevels Level(JsDiagnosticLevel l) => l switch
        {
            JsDiagnosticLevel.Error => MessageLevels.Error,
            JsDiagnosticLevel.Warning => MessageLevels.Warning,
            _ => MessageLevels.Info,
        };

        /// <summary>
        /// Write a file if the content changed (or if forced)
        /// </summary>
        static async Task<bool> WriteIfChanged(String file, String text, bool force)
        {
            if (!force && File.Exists(file))
            {
                var old = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                if (old == text)
                    return false;
            }
            await File.WriteAllTextAsync(file, text, Utf8).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// The pages to clone, by destination root folder
        /// </summary>
        static readonly Dictionary<String, List<String>> SitePages = new Dictionary<String, List<String>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Write diagnostics in the MSBuild / Visual Studio format: file(line,col): warning: message
        /// </summary>
        static void Report(IMessageHost msg, JsConvertParams p, String file, IEnumerable<JsDiagnostic> diags)
        {
            foreach (var d in diags)
            {
                if (d.Level == JsDiagnosticLevel.Info && !p.Verbose)
                    continue;
                var where = d.Line > 0 ? "(" + d.Line + "," + d.Column + ")" : "";
                var kind = d.Level switch
                {
                    JsDiagnosticLevel.Error => "error",
                    JsDiagnosticLevel.Warning => "warning",
                    _ => "info",
                };
                msg.AddMessage((d.File ?? file) + where + ": " + kind + ": " + d.Message, Level(d.Level));
            }
        }

        /// <summary>
        /// Clone the sites of the html files (site mode)
        /// </summary>
        static async Task CloneSites(IMessageHost msg, JsConvertParams p)
        {
            foreach (var kv in SitePages)
            {
                var sp = new JsSiteParams
                {
                    SiteRoot = String.IsNullOrEmpty(p.SiteRoot) ? null : p.SiteRoot,
                    Assets = p.SiteAssets,
                    ScanStrings = p.SiteScanStrings,
                    HelpersFile = p.HelpersFile,
                    Overwrite = p.Overwrite,
                    Force = p.Force,
                };
                var r = await JsSite.CloneAsync(kv.Value, kv.Key, p, sp, (f, cr, written) =>
                {
                    var sb = new StringBuilder();
                    sb.Append(f.RelativePath.ToQuoted()).Append(" [").Append(f.Kind).Append(written ? ", written] " : ", unchanged] ");
                    if (cr != null)
                    {
                        if (cr.Code == null)
                            sb.Append("conversion failed, copied unchanged");
                        else
                        {
                            sb.Append(cr.Code.Length).Append(" chars");
                            if (cr.Transformed.Count > 0)
                                sb.Append(", transformed: ").Append(String.Join(", ", cr.Transformed));
                            lock (Lock)
                            {
                                UsedHelpers.UnionWith(cr.Helpers);
                                UsedPolyfills.UnionWith(cr.Polyfills);
                            }
                        }
                    }
                    else
                        sb.Append("copied");
                    msg.AddMessage(sb.ToString(), cr?.Code == null && cr != null ? MessageLevels.Error : MessageLevels.Info);
                }).ConfigureAwait(false);
                Report(msg, p, null, r.Diagnostics);
                Interlocked.Add(ref Failed, r.Failed);
                msg.AddMessage("Site " + r.Root.ToQuoted() + " => " + kv.Key.ToQuoted() + ": " + r.Files.Count + " files (" + kv.Value.Count + " pages), " + r.Written + " written" + (r.Failed > 0 ? ", " + r.Failed + " failed" : ""));
            }
        }

        /// <summary>
        /// The message host of the files to folder tool (used for the site cloning after all files are processed)
        /// </summary>
        static IMessageHost LastMessageHost;

        static async Task<int> ProcessFile(IMessageHost msg, JsConvertParams p, String sourceFile, String relSource, String destFolder)
        {
            LastMessageHost = msg;
            var fileName = Path.GetFileName(sourceFile);
            var destName = String.IsNullOrEmpty(p.Suffix)
                ? fileName
                : Path.GetFileNameWithoutExtension(fileName) + p.Suffix + Path.GetExtension(fileName);
            var dest = Path.GetFullPath(Path.Combine(destFolder, destName));
            //  The root destination folder (destFolder includes the sub folder of a recursive search)
            var root = destFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relDir = Path.GetDirectoryName(relSource)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!String.IsNullOrEmpty(relDir) && root.EndsWith(relDir, StringComparison.OrdinalIgnoreCase))
                root = root.Substring(0, root.Length - relDir.Length).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            lock (Lock)
            {
                if (LowestDestFolder == null || root.Length < LowestDestFolder.Length)
                    LowestDestFolder = root;
            }
            //  Site mode: the pages are processed together after all files (the dependencies are shared)
            if (p.Site && IsHtml(sourceFile))
            {
                lock (Lock)
                {
                    if (!SitePages.TryGetValue(root, out var l))
                        SitePages[root] = l = new List<String>();
                    l.Add(Path.GetFullPath(sourceFile));
                }
                return 0;
            }
            if (!p.Overwrite && String.Equals(dest, Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase))
            {
                msg.AddMessage(fileName.ToQuoted() + ": the output would overwrite the source, specify a destination folder, a -Suffix or use -Overwrite", MessageLevels.Error);
                Interlocked.Increment(ref Failed);
                return 0;
            }
            String source;
            try
            {
                source = await File.ReadAllTextAsync(sourceFile).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                msg.AddMessage(fileName.ToQuoted() + ": failed to read", ex, MessageLevels.Error);
                Interlocked.Increment(ref Failed);
                return 0;
            }
            var r = IsHtml(sourceFile) ? JsHtmlConverter.Convert(source, p) : JsConverter.Convert(source, p);
            Report(msg, p, sourceFile, r.Diagnostics);
            if (r.Code == null)
            {
                msg.AddMessage(fileName.ToQuoted() + " [failed!]", MessageLevels.Error);
                Interlocked.Increment(ref Failed);
                return 0;
            }
            lock (Lock)
            {
                UsedHelpers.UnionWith(r.Helpers);
                UsedPolyfills.UnionWith(r.Polyfills);
            }
            bool written;
            try
            {
                written = await WriteIfChanged(dest, r.Code, p.Force).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                msg.AddMessage(destName.ToQuoted() + ": failed to write", ex, MessageLevels.Error);
                Interlocked.Increment(ref Failed);
                return 0;
            }
            var sb = new StringBuilder();
            sb.Append(fileName.ToQuoted());
            sb.Append(written ? " [converted] " : " [unchanged] ");
            sb.Append(source.Length).Append(" => ").Append(r.Code.Length).Append(" chars");
            if (r.Transformed.Count > 0)
                sb.Append(", transformed: ").Append(String.Join(", ", r.Transformed));
            var w = r.Diagnostics.Count(x => x.Level == JsDiagnosticLevel.Warning);
            if (w > 0)
                sb.Append(" (").Append(w).Append(w == 1 ? " warning)" : " warnings)");
            msg.AddMessage(sb.ToString());
            return 0;
        }

        static int Setup(JsConvertParams p)
        {
            JsTarget target;
            HashSet<JsFeature> features;
            try
            {
                features = JsConverter.GetFeatures(p, out target);
            }
            catch (ArgumentException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                return FilesToFolderTool.ExitInvalidArguments;
            }
            if (!String.IsNullOrEmpty(p.HelpersFile))
                p.Helpers = JsHelperMode.External;
            if (p.ShowTarget)
            {
                Console.WriteLine("Target: " + target + " (from \"" + p.Target + "\")");
                Console.WriteLine("Transforms: " + (features.Count == 0 ? "none" : String.Join(", ", JsFeatureOrder(features))));
            }
            Params = p;
            return 0;
        }

        static IEnumerable<JsFeature> JsFeatureOrder(HashSet<JsFeature> f) => Enum.GetValues<JsFeature>().Where(f.Contains);

        static JsConvertParams Params;

        /// <summary>
        /// Process exit codes, 0 = success, non-zero (1-255) = failure:
        /// 0 = success or help requested, 1 = an exception while processing, 2 = invalid command line, 3 = one or more files couldn't be converted.
        /// </summary>
        static async Task<int> Main(string[] args)
        {
            int ret;
#if DEBUG
            ret = await FilesToFolderTool.OnFilesAsync<JsConvertParams>(args, ProcessFile, Setup).ConfigureAwait(false);
#else//DEBUG
            ret = await FilesToFolderTool.OnFilesParallelAsync<JsConvertParams>(args, ProcessFile, Setup).ConfigureAwait(false);
#endif//DEBUG
            var p = Params;
            //  Site mode: clone the sites of the html files
            if (ret == 0 && p != null && SitePages.Count > 0)
            {
                try
                {
                    await CloneSites(LastMessageHost, p).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LastMessageHost.AddMessage("Failed to clone the site", ex, MessageLevels.Error);
                    ret = FilesToFolderTool.ExitGenericError;
                }
            }
            //  p is null if help was requested or the command line is invalid
            if (ret == 0 && p != null && !String.IsNullOrEmpty(p.HelpersFile))
            {
                var hf = p.HelpersFile;
                if (String.IsNullOrEmpty(Path.GetDirectoryName(hf)) && LowestDestFolder != null)
                    hf = Path.Combine(LowestDestFolder, hf);
                hf = Path.GetFullPath(hf);
                try
                {
                    var dir = Path.GetDirectoryName(hf);
                    if (!String.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    var src = JsConverter.GetHelpersSource(UsedHelpers.OrderBy(x => x, StringComparer.Ordinal), p.Minify, UsedPolyfills.OrderBy(x => x, StringComparer.Ordinal));
                    var written = await WriteIfChanged(hf, src, p.Force).ConfigureAwait(false);
                    Console.WriteLine("Helpers file " + hf.ToQuoted() + (written ? " [written] " : " [unchanged] ") + UsedHelpers.Count + " helpers, " + UsedPolyfills.Count + " polyfills");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to write the helpers file " + hf.ToQuoted() + ": " + ex.Message);
                    Console.ResetColor();
                    ret = FilesToFolderTool.ExitGenericError;
                }
            }
            if (ret == 0 && Failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(Failed + (Failed == 1 ? " file" : " files") + " couldn't be converted");
                Console.ResetColor();
                ret = ExitConvertFailed;
            }
            return ret;
        }
    }
}
