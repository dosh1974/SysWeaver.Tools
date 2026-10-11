using System;
using System.IO;

namespace Touch
{
    internal class TouchProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  The same values as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError.

        /// <summary>
        /// Exit code: success (also returned when help was requested, or if no arguments are given).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, an exception (ex: the folder doesn't exist or a file couldn't be touched).
        /// </summary>
        const int ExitGenericError = 1;

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help";

        static int Main(string[] args)
        {
            if ((args.Length == 1) && IsHelp(args[0]))
            {
                Console.WriteLine("Touch.exe <[Path\\]FileMask[+]> [[Path\\]FileMask[+]..]");
                Console.WriteLine("A mask without a path is relative to the current directory.");
                Console.WriteLine("Sets the last write time of the matching files to now, a trailing + searches sub folders.");
                Console.WriteLine("Exit codes: 0 = success or help, 1 = error");
                return ExitSuccess;
            }
            Char[] c = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
            var now = DateTime.UtcNow;
            try
            {
                foreach (var x in args)
                {
                    var d = x.LastIndexOfAny(c) + 1;
                    var fileMask = x.Substring(d);
                    //  No folder part: the current directory; keep the trailing separator (trimming it turns "C:\" into "C:", the current directory of drive C)
                    var path = d <= 0 ? Environment.CurrentDirectory : Path.GetFullPath(x.Substring(0, d));
                    bool rec = fileMask.EndsWith('+');
                    if (rec)
                        fileMask = fileMask.Substring(0, fileMask.Length - 1);
                    foreach (var f in Directory.GetFiles(path, fileMask, rec ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    {
                        var fi = new FileInfo(f);
                        fi.LastWriteTimeUtc = now;
                        Console.WriteLine(fi.Name);
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
            return ExitSuccess;
        }
    }
}
