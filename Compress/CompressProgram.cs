

using SysWeaver.Compression;
using SysWeaver.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace SysWeaver
{

    partial class CompressProgram
    {

        const int CacheDays = 60;
        const int TypeCacheDays = 60;
        
        /// <summary>
        /// Version for the pure compresson part
        /// </summary>
        const String Version = "1";
        
        /// <summary>
        /// Version for type specific transformation / compression
        /// </summary>
        const String TypeVersion = "4";

        /// <summary>
        /// Version of the shader optimization (added to the TypeVersion of shaders only, so other types keep their cached results).
        /// 8: shaders are stripped (shader_minifier only with MinifyShaders), Effect Weaver shaders keep working: the variable candidates, the iMouse channel comments, vertexMain, conditionals on compiler macros (__VERSION__), the snippet macros (EW_...) and the functions they use / named like snippets (Ew...), one top level declaration per line; no new overloads; the smallest of minimized (with / without aggressive inlining), stripped and the source.
        /// </summary>
        const String ShaderTypeVersion = "8";

        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0 = success or help requested, 1 = an exception while processing, 2 = invalid command line (from SysWeaver.CommandLine FilesToFolderTool).

        /// <summary>
        /// Exit code: a file couldn't be compressed using any of the compression methods (with OnlyBest).
        /// </summary>
        const int ExitCompressFailed = 3;



        static readonly String ToolsFolder = Path.Combine(CommandLine.ExecutableFolder, "tools");

        /// <summary>
        /// https://github.com/google/zopfli
        /// </summary>
        static readonly String ToolZopFliPng = Path.Combine(ToolsFolder, "ZopfliPng.exe");
        
        /// <summary>
        /// https://github.com/tdewolff/minify/
        /// </summary>
        static readonly String ToolMinify = Path.Combine(ToolsFolder, "minify.exe");

        static readonly String ToolOptimizeShaders = Path.Combine(ToolsFolder, "glsl_optimizer.exe");

        /// <summary>
        /// https://github.com/laurentlb/shader-minifier
        /// </summary>
        static readonly String ToolMinimizeShaders = Path.Combine(ToolsFolder, "shader_minifier.exe");

        static readonly String ToolPreProcessorShaders = Path.Combine(ToolsFolder, "preprocessor.exe");


        internal delegate int TypeCompressor(String source, String dest, CompressParams p);

        static int CompressPng(String source, String dest, CompressParams p)
        {
            return ExternalProcess.Run(ToolZopFliPng, "-y -m " + source.ToQuoted() + " " + dest.ToQuoted());
        }

        static bool CanRemove(char c)
        {
            if (Char.IsLetterOrDigit(c))
                return false;
            if (c=='_')
                return false;
            return true;
        }

        static int ExtCompressShader(String source, String dest)
        {
            return ExternalProcess.Run(ToolOptimizeShaders, source.ToQuoted() + " " + dest.ToQuoted());

        }


        static int ExtMinimizeShader(String source, String dest, IEnumerable<String> keepFunctions = null, bool aggressive = true)
        {
            //  vertexMain: the entry point of the optional vertex stage (Effect Weaver), never removed or renamed (like keepFunctions).
            //  --no-overloading: 1.6.0 can create duplicate overloads (ex: two "float m(float,float)"), an invalid shader.
            var keep = String.Join(",", new[] { "main", "mainImage", "vertexMain" }.Concat(keepFunctions ?? Array.Empty<String>()).Distinct());
            return ExternalProcess.Run(ToolMinimizeShaders, "-o " + dest.ToQuoted() + "  --move-declarations --field-names xyzw " + (aggressive ? "--aggressive-inlining " : "") + "--format text --no-sequence --no-overloading --preserve-externals --no-renaming-list " + keep + " " + source.ToQuoted());
        }

        static readonly IReadOnlySet<Char> WhiteSpaces = ReadOnlyData.Set("\r\n\t ".ToCharArray());

        static int Minimize(String source, String dest, CompressParams p)
        {
            var ext = Path.GetExtension(source).FastToLower();
            if (ext.FastEquals(".js"))
            {
                var l = File.ReadAllLines(source);
                int c = l.Length;
                int o = 0;
                bool inDebug = false;
                var whiteSpaces = WhiteSpaces;
                for (int i = 0; i < c; ++i)
                {
                    var t = l[i];
                    var x = t.RemoveChars(whiteSpaces);
                    if (inDebug)
                    {
                        inDebug = !x.FastStartsWith("//DEBUG_END");
                        continue;
                    }
                    inDebug = x.FastStartsWith("//DEBUG_BEGIN");
                    if (inDebug)
                        continue;
                    l[o] = t;
                    ++o;
                }
                if (o != c)
                {
                    var data = TempFolder.Get("JsTemp", 1);
                    String temp;
                    using (var rng = SecureRng.Get())
                        temp = Path.Combine(data, rng.GetGuid24() + ".js");
                    TempFolder.DeleteOnExit(temp);
                    Array.Resize(ref l, o);
                    File.WriteAllLines(temp, l);
                    source = temp;
                }
            }
            if (Path.GetFileName(source).FastEquals("typescript.js"))
            { 
                File.Copy(source, dest, true);
                return 0;
            }
//            return ExternalProcess.Run(ToolMinify, "--js-keep-var-names --js-version 2019 --html-keep-default-attrvals --html-keep-document-tags --svg-precision 0 --js-precision 0 --json-precision 0 --json-keep-numbers -o " + dest.ToQuoted() + " " + source.ToQuoted());
            return ExternalProcess.Run(ToolMinify, "--html-keep-default-attrvals --html-keep-document-tags --svg-precision 0 --js-precision 0 --json-precision 0 --json-keep-numbers -o " + dest.ToQuoted() + " " + source.ToQuoted());
        }

        static readonly IReadOnlyDictionary<String, TypeCompressor> ImageCompressors = new Dictionary<string, TypeCompressor>(StringComparer.Ordinal)
        {
            { ".png", CompressPng },
        }.Freeze();

        static readonly IReadOnlyDictionary<String, TypeCompressor> WebCompressors = new Dictionary<string, TypeCompressor>(StringComparer.Ordinal)
        {
            { ".js", Minimize },
            { ".css", Minimize },
            { ".htm", Minimize },
            { ".html", Minimize },
            { ".json", Minimize },
            { ".mjs", Minimize },
            { ".rss", Minimize },
            { ".svg", Minimize },
            { ".webmanifest", Minimize },
            { ".xhtml", Minimize },
            { ".xml", Minimize },
        }.Freeze();

        static readonly IReadOnlyDictionary<String, TypeCompressor> ShadersCompressors = new Dictionary<string, TypeCompressor>(StringComparer.Ordinal)
        {
            { ".glsl", OptimizeShaders },
            { ".frag", OptimizeShaders },
        }.Freeze();


        static readonly IReadOnlyDictionary<String, bool> ForcedCompression = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { ".jpg", true },
            { ".ico", true },
            { ".jpeg", true },
        }.Freeze();

        sealed class CompMeta
        {
            public long SrcSize { get; set; }
            public long Size { get; set; }
            public string Filename { get; set; }
            public String Ext { get; set; }
        }

        static Task<CompMeta> GetComp(String sourceFile, ICompType comp)
        {
            if (comp == null)
            {
                var l = new FileInfo(sourceFile).Length;
                return Task.FromResult(new CompMeta
                {
                    Filename = sourceFile,
                    Size = l,
                    SrcSize = l,
                    Ext = "",
                });
            }
            return FileMetaData.ProcessAsync<CompMeta>("Compress_" + comp.HttpCode, sourceFile, async (file, baseName, data) =>
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
                var dest = baseName + "Comp." + (comp.FileExtensions?.FirstOrDefault() ?? ".dta");
                data = new CompMeta
                {
                    Filename = dest,
                };
                using (var s = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
                using (var d = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    data.SrcSize = s.Length;
                    await comp.CompressAsync(s, d, CompEncoderLevels.Best).ConfigureAwait(false);
                    data.Size = d.Length;
                }
                return data;
            }, CacheDays, Version);
        }


        static Task<CompMeta> GetTypeCompressor(String sourceFile, String extension, TypeCompressor comp, CompressParams opt)
        {
            return FileMetaData.ProcessAsync<CompMeta>("TypeCompress" + extension, sourceFile, (file, baseName, data) =>
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
                var dest = baseName + "TypeComp" + extension;
                data = new CompMeta
                {
                    Filename = dest,
                };
                data.SrcSize = new FileInfo(sourceFile).Length;
                var r = comp(sourceFile, dest, opt);
                if (r == 0)
                {
                    data.Size = new FileInfo(dest).Length;
                }
                else
                {
                    data.Size = -1;
                }
                return data;
            }, TypeCacheDays, TypeVersion + (ShadersCompressors.ContainsKey(extension) ? "s" + ShaderTypeVersion : "") + opt.CacheKey);
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
            for (int i = 0; ; ++ i)
            {
                try
                {
                    File.Copy(source, dest, true);
                    break;
                }
                catch
                {
                    if (i > 10)
                        throw;
                    Thread.Sleep(100);
                }
            }
            fd = new FileInfo(dest);
            fd.LastWriteTimeUtc = fs.LastWriteTimeUtc;
            return true;
        }

        static async Task<int> ProcessFile(IMessageHost msg, CompressParams opt, String sourceFile, String relSource, String destFolder)
        {
            bool copyOriginal = opt.CopyOriginal;
            bool onlyBetter = opt.OnlyBetter;
            var filename = Path.GetFileName(sourceFile);
            var destFilename = Path.Combine(destFolder, filename);
            var srcSize = new FileInfo(sourceFile).Length;
            var key = Path.GetExtension(sourceFile).FastToLower();
            var mime = MimeTypeMap.GetMimeType(key);

            foreach (var typeComp in opt.TypeCompressors)
            {
                if (typeComp.Item2.TryGetValue(key, out var comp))
                {
                    var g = await GetTypeCompressor(sourceFile, key, comp, opt).ConfigureAwait(false);
                    if ((g == null) || (g.Size < 0))
                    {
                        msg.AddMessage(relSource.ToQuoted() + " Failed to type compress!", MessageLevels.Warning);
                    }else
                    {
                        if (typeComp.Item1)
                        {
                            if (Copy(g.Filename, destFilename))
                                msg.AddMessage(relSource.ToQuoted() + " [" + g.SrcSize + " => " + g.Size + "]");
                            sourceFile = destFilename;
                            onlyBetter = true;
                            copyOriginal = false;
                        }
                        else
                        {
                            sourceFile = g.Filename;
                        }
                    }
                    break;
                }
            }
            var uncompressable = !mime.Item2;
            if (opt.Force)
            {
                uncompressable = false;
            }
            else
            {
                if (ForcedCompression.TryGetValue(key, out var forceComp))
                    uncompressable = !forceComp;
            }

            if (copyOriginal || uncompressable)
            {
                if (!String.Equals(destFolder, Path.GetDirectoryName(sourceFile), StringComparison.OrdinalIgnoreCase))
                {
                    if (Copy(sourceFile, destFilename))
                        msg.AddMessage(relSource.ToQuoted() + " copied");
                }
                if (uncompressable)
                    return 0;
            }
            var methods = opt.CompTypes;
            if (methods.Length <= 0)
                return 0;
            if (opt.OnlyBest)
            {
                CompMeta best = null;
                foreach (var method in methods)
                {
                    var g = await GetComp(sourceFile, method).ConfigureAwait(false);
                    if (g == null)
                    {
                        if (method == null)
                            msg.AddMessage("Failed to get size of original file", MessageLevels.Warning);
                        else
                            msg.AddMessage("Failed to compress file using " + method.Name.ToQuoted(), MessageLevels.Warning);
                        continue;
                    }
                    if (best == null)
                    {
                        best = g;
                        continue;
                    }
                    if (g.Size >= best.Size)
                        continue;
                    best = g;
                }
                if (best == null)
                {
                    msg.AddMessage("Couldn't compress the file using any compression method", MessageLevels.Error);
                    return ExitCompressFailed;
                }
                var destExt = best.Ext ?? Path.GetExtension(best.Filename);
                var destName = filename + destExt;
                if (onlyBetter && (best.Size >= best.SrcSize))
                {
                    msg.AddMessage("Skipped " + (relSource + destExt).ToQuoted() + " since it's larger [" + best.SrcSize + " => " + best.Size + "]", MessageLevels.Debug);
                    return 0;
                }
                var dest = Path.Combine(destFolder, destName);
                if (Copy(best.Filename, dest))
                    msg.AddMessage((relSource + destExt).ToQuoted() + " [" + srcSize + " => " + best.Size + "]");
            }
            else
            {
                foreach (var method in methods)
                {
                    var best = await GetComp(sourceFile, method).ConfigureAwait(false);
                    if (best == null)
                    {
                        if (method == null)
                            msg.AddMessage("Failed to get size of original file", MessageLevels.Warning);
                        else
                            msg.AddMessage("Failed to compress file using " + method.Name.ToQuoted(), MessageLevels.Warning);
                        continue;
                    }
                    var destExt = best.Ext ?? Path.GetExtension(best.Filename);
                    var destName = filename + destExt;
                    if (onlyBetter && (best.Size >= best.SrcSize))
                    {
                        msg.AddMessage("Skipped " + (relSource + destExt).ToQuoted() + " since it's larger [" + best.SrcSize + " => " + best.Size + "]", MessageLevels.Debug);
                        return 0;
                    }
                    var dest = Path.Combine(destFolder, destName);
                    if (Copy(best.Filename, dest))
                        msg.AddMessage((relSource + destExt).ToQuoted() + " [" + srcSize + " => " + best.Size + "]");
                }
            }
            return 0;
        }

        static int Setup(CompressParams opt)
        {
            var hs = new HashSet<ICompType>();
            foreach (var x in opt.Methods.Split(",").Select(x => x.Trim()))
            {
                if ((x.Length == 0) || String.Equals(x, "org", StringComparison.OrdinalIgnoreCase))
                {
                    hs.Add(null);
                    continue;
                }
                var comp = CompManager.GetFromHttp(x);
                if (comp != null)
                    hs.Add(comp);
            }
            opt.CompTypes = hs.OrderBy(x => x.HttpCode).ToArray();
            var tc = new List<Tuple<bool, IReadOnlyDictionary<string, TypeCompressor>>>();
            opt.TypeCompressors = tc;
            if (opt.RecompressImages)
                tc.Add(Tuple.Create(true, ImageCompressors));
            if (opt.OptimizeWebFiles)
                tc.Add(Tuple.Create(false, WebCompressors));
            if (opt.OptimizeShaders)
                tc.Add(Tuple.Create(false, ShadersCompressors));
            return 0;
        }

        static Task<int> Main(string[] args)
        {
            CompDeflateNET.Register();
            CompBrotliNET.Register();
            CompGZipNET.Register();
            CompBrotliNativeNET.Register();
            CompZstdSharp.Register();

#if DEBUG
            return FilesToFolderTool.OnFilesAsync<CompressParams>(args, ProcessFile, Setup);
#else//DEBUG
            return FilesToFolderTool.OnFilesParallelAsync<CompressParams>(args, ProcessFile, Setup);
#endif//DEBUG
        }



    }

}


