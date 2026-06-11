using SysWeaver.Compression;
using SysWeaver.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SysWeaver.Minifier;

namespace SysWeaver
{


    internal class SvgOptProgram
    {


        sealed class SvgMeta
        {
            public long SrcSize { get; set; }
            public long Size { get; set; }
            public string Filename { get; set; }
        }

        static bool Copy(String source, String dest)
        {
            var fs = new FileInfo(source);
            var fd = new FileInfo(dest);
            if (fd.Exists)
            {
                if (fs.Length == fd.Length)
                    if (fs.LastWriteTimeUtc == fd.LastWriteTimeUtc)
                        return false;
            }
            File.Copy(source, dest, true);
            fd = new FileInfo(dest);
            fd.LastWriteTimeUtc = fs.LastWriteTimeUtc;
            return true;
        }

        static async Task<int> ProcessFile(IMessageHost msg, SvgOptParams opt, String sourceFile, String relSource, String destFolder)
        {
            var filename = Path.GetFileName(sourceFile);
            var destFilename = Path.Combine(destFolder, filename);
            var suffix = opt.CacheKey;
            bool didProcces = false;
            var meta = await FileMetaData.ProcessAsync<SvgMeta>("SvgOptMeta", sourceFile, async (file, baseName, data) =>
            {
                //  If we have some cached data
                if (data != null)
                {
                    //  Validate the data and if ok use it
                    var fi = new FileInfo(data.Filename);
                    if (fi.Exists && (fi.Length == data.Size))
                        return null;
                }
                //  Need to create some new data
                var dest = baseName + ".svg";
                data = new SvgMeta
                {
                    Filename = dest,
                };
                data.SrcSize = new FileInfo(sourceFile).Length;
                String svgText;
                try
                {
                    svgText = await File.ReadAllTextAsync(sourceFile);
                    var optText = SvgMinifier.Optimize(svgText, msg, opt);
                    if (optText == null)
                    {
                        data.Size = -1;
                        Copy(sourceFile, dest);
                    }
                    else
                    {
                        await File.WriteAllTextAsync(dest, optText);
                        data.Size = new FileInfo(dest).Length;
                    }
                }
                catch (Exception ex)
                {
                    msg.AddMessage("Failed to read or process svg, using a copy of the original", ex, MessageLevels.Warning);
                    Copy(sourceFile, dest);
                }
                didProcces = true;
                return data;
            }, 30, suffix);
            if (meta.Size >= 0)
            {
                msg.AddMessage(String.Concat(filename.ToQuoted(), didProcces ? " [processed] " : " [cached] ", meta.SrcSize, " => ", meta.Size, " [", ((100M * meta.Size) / meta.SrcSize).ToString("0.00", CultureInfo.InvariantCulture), "%]"));
                Copy(meta.Filename, destFilename);
            }
            else
            {
                msg.AddMessage(String.Concat(filename.ToQuoted(), " [failed!]"));
            }
            return 0;
        }


        static readonly String[] TestZero =
        [
            "22", "22",
            "1.00023", "1.00023",
            "3.0002300", "3.00023",
            "10.500", "10.5",
            "13.0", "13",
            "0.0" , "0",
            "0.97", ".97",
            ".10" , ".1",
            ".0" , "0",
            "-0.0", "0",
            "-0.01", "-.01",
            "-.000" , "0",
            "04", "4",
            "005.0", "5",
            "000", "0",
            "000.000", "0",
            "-000", "0",
            "-000.000", "0",
        ];

        
        static async Task<int> Main(string[] args)
        {
            CompGZipNET.Register();
            /*

            Func<String, String> method = s => TrimLeadingZeros(TrimTrailingZeros(s));

            var l = TestZero.Length;
            for (int i = 0; i < l; i += 2)
            {
                var s = TestZero[i];
                var d = TestZero[i + 1];
                var g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }
                s = "Apa" + s;
                d = "Apa" + d;
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }
                s += s;
                d += d;
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }

                s += "z";
                d += "z";
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }

            }
            */

            //var c = OptimizePath("M2400 0h4800v4800H2400zm2490 4430l-45-863a95 95 0 01111-98l859 151-116-320a65 65 0 0120-73l941-762-212-99a65 65 0 01-34-79l186-572-542 115a65 65 0 01-73-38l-105-247-423 454a65 65 0 01-111-57l204-1052-327 189a65 65 0 01-91-27l-332-652-332 652a65 65 0 01-91 27l-327-189 204 1052a65 65 0 01-111 57l-423-454-105 247a65 65 0 01-73 38l-542-115 186 572a65 65 0 01-34 79l-212 99 941 762a65 65 0 0120 73l-116 320 859-151a95 95 0 01111 98l-45 863z");
            //var c = OptimizePathArcs("M3 1.052zm-.718.157a2.26 2.26 0 0 0-.055.023l.06.935c.041.489.36.773.713.885.354-.112.672-.396.714-.885l.059-.932a1.362 1.362 0 0 0-.055-.026l-.056.927a.947.947 0 0 1-.662.86.947.947 0 0 1-.662-.86z");
            //var c = OptimizePath("2 .4 2.7");


            int ret;
            SvgOptParams p = null;
            int Setup(SvgOptParams x)
            {
                p = x;
                return 0;
            }

            List<Tuple<String, String>> allFiles = new List<Tuple<string, string>>();
            String dest = null;
            Task<int> proc(IMessageHost msg, SvgOptParams opt, String sourceFile, String relSource, String destFolder)
            {
                lock (allFiles)
                {
                    allFiles.Add(Tuple.Create(sourceFile, Path.Combine(destFolder, relSource)));
                    if ((dest == null) || (destFolder.Length < dest.Length))
                        dest = destFolder;
                }
                return ProcessFile(msg, opt, sourceFile, relSource, destFolder);
            }

#if DEBUG
            ret = await FilesToFolderTool.OnFilesAsync<SvgOptParams>(args, proc, Setup).ConfigureAwait(false);
#else//DEBUG
            ret = await FilesToFolderTool.OnFilesParallelAsync<SvgOptParams>(args, proc, Setup).ConfigureAwait(false);
#endif//DEBUG 
            var c = p.Compare;
            if (c != null) 
            {
                var e = Path.GetExtension(p.Compare)?.FastToLower();
                if ((e != ".htm") || (e != ".html"))
                    c += ".html";
                if (String.IsNullOrEmpty(Path.GetDirectoryName(c)))
                    c = Path.Combine(dest, c);
                c = new FileInfo(c).FullName;
                ImageCompareGallery.Create(c, allFiles.OrderBy(x => x.Item1));
                Console.WriteLine("Save image comparsion file to " + c.ToFilename());
            }
            return ret;
        }
    }


}
