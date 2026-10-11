
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysWeaver
{

    class Params
    {

    }

    internal class OnDuplicatesProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.

        /// <summary>
        /// Exit code: success (also returned when help was requested), finding duplicates is not an error.
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, an exception while searching.
        /// </summary>
        const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line (no arguments, an unknown option or missing arguments).
        /// </summary>
        const int ExitInvalidArguments = 2;

        static async Task<int> Main(string[] args)
        {
            var start = DateTime.UtcNow;
            CommandLineArgument[] ca =
            [
                CommandLineArgument.Make<String>("Folders", false, null, "The folders to search, separated by a ';'"),
                CommandLineArgument.Make<String>("Action", true, null, "An optional action to perform (shell execute). {0} is the full path to the duplicate."),
            ];
            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                foreach (var x in CommandLine.SyntaxObject<Params>(ca))
                    Console.WriteLine(x);
                Console.ResetColor();
                return ExitInvalidArguments;
            }
            var failCode = ExitInvalidArguments;
            try
            {
                var cmd = CommandLine.ParseObject<Params>(out var opt, args, ca);
                if (cmd == null)
                {
                    //  Help requested
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    foreach (var x in CommandLine.SyntaxObject<Params>(ca))
                        Console.WriteLine(x);
                    Console.ResetColor();
                    return ExitSuccess;
                }
                failCode = ExitGenericError;
                var srcFolder = cmd.Arguments[0].Item2 as String;
                var action = cmd.Arguments.Length > 1 ? (cmd.Arguments[1].Item2 as String)?.Trim() : null;
                Action<String, String> onDup;
                if (String.IsNullOrEmpty(action))
                {
                    onDup = (dup, org) => Console.WriteLine(String.Concat(dup.ToQuoted(), " same as ", org.ToQuoted()));
                }else
                {
                    var f = action[0];
                    var end = ' ';
                    if ((f == '"') || (f == 39))
                        end = f;
                    var exe = action.SplitFirst(end, out var rest).Trim(end);
                    rest = rest.Trim(end).Trim();



                    onDup = (dup, org) =>
                    {
                        ExternalProcess.Run(exe, String.Format(rest, dup, org), (m, e) => Console.WriteLine(m));
                    };
                }
                var max = Math.Max(1, Environment.ProcessorCount - 1);
                var throttler = new AsyncLock(max);
                Console.Write("Analysing");
                ConcurrentDictionary<String, ConcurrentDictionary<String, int>> dups = new (StringComparer.OrdinalIgnoreCase);
                foreach (var folder in srcFolder.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    await Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ProcessAsyncValue(async file =>
                    {
                        using var _ = await throttler.Lock().ConfigureAwait(false);
                        var hash = await FileHash.GetHashAsync(file).ConfigureAwait(false);
                        if (!dups.TryGetValue(hash, out var d))
                        {
                            d = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
                            if (!dups.TryAdd(hash, d))
                                if (!dups.TryGetValue(hash, out d))
                                    throw new Exception("Internal error!");
                        }
                        d.TryAdd(file, 0);
                        Console.Write(".");
                    });
                }
                Console.WriteLine(".done!");
                foreach (var x in dups.Values)
                {
                    var l = x.Count;
                    if (l <= 1)
                        continue;
                    var p = x.Keys.ToList();
                    p.Sort();
                    var org = p[0];
                    for (int i = 1; i < l; ++i)
                        onDup(p[i], org);
                }
                return ExitSuccess;
           }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                foreach (var x in CommandLine.SyntaxObject<Params>(ca))
                    Console.WriteLine(x);
                Console.ResetColor();
                return failCode;
            }
            finally
            {
                Console.WriteLine();
                Console.ResetColor();
                var took = DateTime.UtcNow - start;
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("All Done - Took: " + took);
                Console.ResetColor();
            }



        }
    }
}
