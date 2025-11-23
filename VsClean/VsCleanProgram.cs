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
                        Console.WriteLine(" - " + ex.Message);
                    else
                        Console.WriteLine();
                }else
                {
                    CleanPathRec(d);
                }
            }
        }

        static void Main(string[] args)
        {
            if (args.Length <= 0)
            {
                Console.WriteLine("VsClean.exe [Directory] <Directory1> <Directory...>");
                Console.WriteLine("Directories can contain wild cards.");
                return;
            }
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
    }
}
