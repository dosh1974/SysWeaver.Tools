using SysWeaver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SearchAndReplaceAll
{
    internal static class SearchAndReplaceAllProgram
    {


        static void Usage(String err = null, int ret = 0)
        {
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(err);
                Console.WriteLine();
                Console.ResetColor();
            }
            Console.WriteLine("Use: SearchAndReplaceAll.exe <Folder> <Key1=Value1> [Key2=Value2] .. [KeyN=ValueN]");
            Environment.Exit(0);
        }


        static void OnFoldersRec(String folder, Action<String> onFolder, bool first = true)
        {
            foreach (var x in Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly))
                OnFoldersRec(x, onFolder, false);
            if (!first)
                onFolder(folder);
        }


        static readonly HashSet<String> TextExtensions = new HashSet<string>
        {
            "txt",
            "log",
            "config",
            "xml",
            "html",
            "css",
            "js",
            "json",
            "h",
            "c",
            "hpp",
            "cpp",
            "cs",
            "vbs",
            "vb",
            "sln",
            "csproj",
            "csxproj",
            "bat",
        };


        static void Main(string[] args)
        {
            var al = args.Length;
            if (al < 2)
                Usage();
            var folder = new DirectoryInfo(args[0]);
            var d = folder.FullName;
            if (!folder.Exists)
                Usage("Folder \"" + d + "\" doesn't exist!", -1);

            Dictionary<String, string> kvs = new ();
            for (int i = 1; i < al; ++ i)
            {
                var kv = args[i];
                var ie = kv.IndexOf('=');  
                if (ie < 0)
                    Usage("Expected a Key=Value pair, found \"" + kv + "\"", -2);
                var key = kv.Substring(0, ie).TrimEnd();
                var value = kv.Substring(ie + 1).TrimStart();
                if (!kvs.TryAdd(key, value))
                    Usage("Key \"" + key + "\" in \"" + kv + "\" is used more than once!", -3);
            }
            foreach (var e in kvs.ToList())
            {
                var k = e.Key;
                var v = e.Value;
                var t = k.ToUpper();
                if (!kvs.ContainsKey(t))
                    kvs.Add(t, v.ToUpper());
                t = k.ToLower();
                if (!kvs.ContainsKey(t))
                    kvs.Add(t, v.ToLower());
                t = StringTools.RemoveCamelCase(k);
                if (!kvs.ContainsKey(t))
                    kvs.Add(t, StringTools.RemoveCamelCase(v));
                t = StringTools.RemoveCamelCase(k, '_');
                if (!kvs.ContainsKey(t))
                    kvs.Add(t, StringTools.RemoveCamelCase(v, '_'));
            }
            Console.WriteLine("Folder: \"" + d + "\"");
            Console.WriteLine("Search and replacing:");
            foreach (var x in kvs.OrderBy(x => x.Key))
                Console.WriteLine("  \"" + x.Key + "\" => \"" + x.Value + "\"");
            var srs = kvs.OrderByDescending(x => x.Key.Length).ToList();
            String replace(String value)
            {
                foreach (var x in srs)
                    value = value.Replace(x.Key, x.Value);
                return value;
            }
            Console.WriteLine("Renaming folders");
            var dl = d.Length + 1;
            OnFoldersRec(d, fo =>
            {
                var o = Path.GetFileName(fo);
                var n = replace(o);
                if (o != n)
                {
                    var fb = Path.GetDirectoryName(fo);
                    o = Path.Combine(fb, o);
                    n = Path.Combine(fb, n);
                    Directory.Move(o, n);
                    Console.WriteLine("  \"" + o.Substring(dl) + "\" => \"" + n.Substring(dl) + "\"");
                }
            });
            Console.WriteLine("Renaming files");
            foreach (var fo in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            {
                var o = Path.GetFileName(fo);
                var n = replace(o);
                if (o != n)
                {
                    var fb = Path.GetDirectoryName(fo);
                    o = Path.Combine(fb, o);
                    n = Path.Combine(fb, n);
                    File.Move(o, n);
                    Console.WriteLine("  \"" + o.Substring(dl) + "\" => \"" + n.Substring(dl) + "\"");
                }
            };
            var exts = TextExtensions;
            Console.WriteLine("Replace in files");
            foreach (var fo in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(fo).ToLower().TrimStart('.');
                if (!exts.Contains(ext))
                    continue;
                try
                {
                    var rows = File.ReadAllLines(fo);
                    var rl = rows.Length;
                    int cc = 0;
                    for (int i = 0; i < rl; ++i)
                    {
                        var o = rows[i];
                        var n = replace(o);
                        if (o != n)
                        {
                            ++cc;
                            rows[i] = n;
                        }
                    }
                    if (cc <= 0)
                        continue;
                    File.WriteAllLines(fo, rows);
                    Console.WriteLine("  \"" + fo.Substring(dl) + "\" " + cc + (cc > 1 ? " rows changed." : " row changed."));
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  \"" + fo.Substring(dl) + "\" failed! Excpetion: " + ex.Message);
                    Console.ResetColor();
                }
            };
            Console.WriteLine("All done");
        }





    }

}
