using System;
using System.IO;

namespace Touch
{
    internal class TouchProgram
    {
        static void Main(string[] args)
        {
            Char[] c = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
            var now = DateTime.UtcNow;
            foreach (var x in args)
            {
                var d = x.LastIndexOfAny(c) + 1;
                var fileMask = x.Substring(d);
                var path = Path.GetFullPath(x.Substring(0, d).TrimEnd(c));
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
    }
}
