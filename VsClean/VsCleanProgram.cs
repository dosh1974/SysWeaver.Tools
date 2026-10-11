using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SysWeaver;

namespace VsClean
{
    internal class VsCleanProgram
    {

        static readonly IReadOnlyDictionary<String, String> RemoveFolders = new Dictionary<String, String>(StringComparer.Ordinal)
        {
            { "bin", "*.csproj" },
            { "obj", "*.csproj" },
            { "web", "srcweb/" },
            { "data", "srcdata/" },
        }.Freeze();




        static bool IsOk(String path, String condition)
        {
            if (String.IsNullOrEmpty(condition))
                return true;
            var p = Path.GetDirectoryName(path);
            if (condition.EndsWith('/'))
            {
                var f = condition.Substring(0, condition.Length - 1)    ;
                return Directory.GetDirectories(p).FirstOrDefault(x => Path.GetFileName(x).FastToLower().FastEquals(f)) != null;
            }
            return Directory.GetFiles(p, condition).Length > 0;
        }

        static void CleanPathRec(String path)
        {
            var folders = RemoveFolders;
            foreach (var d in Directory.GetDirectories(path))
            {
                var key = Path.GetFileName(d).FastToLower();
                if (folders.TryGetValue(key, out var cond) && IsOk(d, cond))
                {
                    var ex = PathExt.TryDeleteDirectory(d, false);
                    Console.ForegroundColor = ex == null ? ConsoleColor.Green : ConsoleColor.Yellow;
                    Console.Write("  \"" + d + '"');
                    Console.ResetColor();
                    if (ex != null)
                    {
                        ++FailedCount;
                        Console.WriteLine(" - " + ex.Message);
                    }
                    else
                        Console.WriteLine();
                }else
                {
                    CleanPathRec(d);
                }
            }
        }

        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.

        /// <summary>
        /// Exit code: success (also returned when help was requested).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, an exception.
        /// </summary>
        const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line (no arguments).
        /// </summary>
        const int ExitInvalidArguments = 2;
        /// <summary>
        /// Exit code: one or more folders couldn't be deleted (the others are deleted).
        /// </summary>
        const int ExitDeleteFailed = 3;

        static int FailedCount;

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help";

        static int Main(string[] args)
        {
            var al = args.Length;
            if ((al <= 0) || ((al == 1) && IsHelp(args[0])))
            {
                Console.WriteLine("VsClean.exe [Directory] <Directory1> <Directory...>");
                Console.WriteLine("Directories can contain wild cards.");
                Console.WriteLine("Exit codes: 0 = success or help, 1 = error, 2 = invalid command line, 3 = failed to delete some folders");
                return al <= 0 ? ExitInvalidArguments : ExitSuccess;
            }
            try
            {
                foreach (var x in args)
                {
                    var path = PathExt.GetDirectoryAndMask(x, out var mask);
                    if (Directory.Exists(path))
                    {
                        foreach (var d in Directory.GetDirectories(path, mask))
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("Cleaning \"" + d + "\"");
                            Console.ResetColor();
                            CleanPathRec(d);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                return ExitGenericError;
            }
            return FailedCount > 0 ? ExitDeleteFailed : ExitSuccess;
        }
    }
}
