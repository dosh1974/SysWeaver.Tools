using SysWeaver.Compression;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysWeaver
{

    class DecompressProgram
    {

        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0 = success or help requested, 1 = an exception while processing, 2 = invalid command line (from SysWeaver.CommandLine FilesToFolderTool).

        /// <summary>
        /// Exit code: a file couldn't be decompressed (invalid or truncated data).
        /// </summary>
        const int ExitDecompressFailed = 3;


        /// <summary>
        /// Get the file extension (with a "." prefix) of the preferred compressed version of a file (using Once)
        /// </summary>
        /// <param name="basePath">The file name without the compression extension</param>
        /// <param name="opt">Options</param>
        /// <returns>The extension of the preferred compressed version that exists, or null if none exist</returns>
        static String GetPreferredExt(String basePath, DecompressParams opt)
        {
            foreach (var ext in opt.ExtOrder)
                if (File.Exists(basePath + ext))
                    return ext;
            return null;
        }

        static void TryDelete(String file)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }

        static async Task<int> ProcessFile(IMessageHost msg, DecompressParams opt, String sourceFile, String relSource, String destFolder)
        {
            var ext = Path.GetExtension(sourceFile).FastToLower();
            var comp = CompManager.GetFromExt(ext);
            if (comp == null)
            {
                msg.AddMessage("Skipped " + relSource.ToQuoted() + " since it's not a supported compressed file", MessageLevels.Debug);
                return 0;
            }
            var basePath = sourceFile.Substring(0, sourceFile.Length - ext.Length);
            if (opt.Once)
            {
                var pref = GetPreferredExt(basePath, opt);
                if ((pref != null) && !pref.FastEquals(ext))
                {
                    msg.AddMessage("Skipped " + relSource.ToQuoted() + " since " + (Path.GetFileName(basePath) + pref).ToQuoted() + " is preferred", MessageLevels.Debug);
                    return 0;
                }
            }
            var destName = Path.GetFileName(basePath);
            var relDest = relSource.Substring(0, relSource.Length - ext.Length);
            var dest = Path.Combine(destFolder, destName);
            if (!opt.Overwrite && File.Exists(dest))
            {
                msg.AddMessage("Skipped " + relDest.ToQuoted() + " since it already exists", MessageLevels.Debug);
                return 0;
            }
            //  Decompress to a temp file and move it in place, so that a failure never leaves a partial file (and files decompressing to the same destination in parallel don't collide)
            var temp = String.Concat(dest, ".", Guid.NewGuid().ToString("N"), ".tmp");
            long srcSize, destSize;
            try
            {
                using (var s = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                using (var d = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    srcSize = s.Length;
                    await comp.DecompressAsync(s, d).ConfigureAwait(false);
                    destSize = d.Length;
                }
                File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(sourceFile));
            }
            catch (Exception ex)
            {
                TryDelete(temp);
                msg.AddMessage("Failed to decompress " + relSource.ToQuoted() + " using " + comp.Name.ToQuoted() + ": " + ex.Message, MessageLevels.Error);
                return ExitDecompressFailed;
            }
            try
            {
                File.Move(temp, dest, opt.Overwrite);
            }
            catch (IOException) when (!opt.Overwrite && File.Exists(dest))
            {
                //  Someone else created the destination after the check above
                TryDelete(temp);
                msg.AddMessage("Skipped " + relDest.ToQuoted() + " since it already exists", MessageLevels.Debug);
                return 0;
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
            msg.AddMessage(relDest.ToQuoted() + " [" + srcSize + " => " + destSize + "]");
            return 0;
        }

        static int Setup(DecompressParams opt)
        {
            var order = new List<String>();
            var seen = new HashSet<String>(StringComparer.Ordinal);
            void add(String ext)
            {
                if (!ext.FastStartsWith("."))
                    return;
                if (CompManager.GetFromExt(ext) == null)
                    return;
                if (seen.Add(ext))
                    order.Add(ext);
            }
            foreach (var x in (opt.Prefer ?? "").Split(",").Select(x => x.Trim().FastToLower()))
            {
                if (x.Length == 0)
                    continue;
                var comp = CompManager.GetFromHttp(x);
                if (comp == null)
                {
                    Console.WriteLine("Unknown compression method " + x.ToQuoted() + ", supported methods are: " + String.Join(", ", CompManager.HttpCodes.Order(StringComparer.Ordinal)));
                    return FilesToFolderTool.ExitInvalidArguments;
                }
                foreach (var ext in comp.FileExtensions.Order(StringComparer.Ordinal))
                    add("." + ext);
            }
            //  Remaining (not preferred) extensions, in a deterministic order
            foreach (var ext in CompManager.Extensions.Order(StringComparer.Ordinal))
                add(ext);
            opt.ExtOrder = order;
            return 0;
        }

        static Task<int> Main(string[] args)
        {
            CompDeflateNET.Register();
            CompGZipNET.Register();
            CompBrotliNativeNET.Register();
            CompZstdSharp.Register();

#if DEBUG
            return FilesToFolderTool.OnFilesAsync<DecompressParams>(args, ProcessFile, Setup);
#else//DEBUG
            return FilesToFolderTool.OnFilesParallelAsync<DecompressParams>(args, ProcessFile, Setup);
#endif//DEBUG
        }

    }

}

