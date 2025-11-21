using System;
using SysWeaver;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SysWeaver.FolderSync;
using System.Threading;
using System.Collections.Concurrent;

namespace SwSyncTool
{
    static partial class SwSyncToolProgram
    {

        static readonly Op[] OpList = [
            new Op(
                Report,
                "Report",
                "Report the current usage.\nArgument is the number of days a chunk have to be unused before considered old (default 400).",
                0, 1
                ),
            new Op(
                QuickReport,
                "QuickReport",
                "Report the current usage (but skipping chunk decompression).\nArgument is the number of days a chunk have to be unused before considered old (default 400).",
                0, 1
                ),
            new Op(
                Prune,
                "Prune",
                "PERMANENTLY remove chunks that is considered old (haven't been used for X days).\nArgument is the number of days a chunk have to be unused before considered old (default 400).",
                0, 1
                ),
            new Op(
                Add,
                "Add",
                "Add file(s) or folder(s)\nArguments are any files or folders.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Compact,
                "Compact",
                "Compact file(s) or folder(s) to .swcompact files.\nArguments are any files or folders.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Verify,
                "Verify",
                "Verify .swcompact file(s).\nArguments are .swcompact files.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Touch,
                "Touch",
                "Mark all chunks used by some .swcompact file(s) as being in use.\nArguments are .swcompact files.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Expand,
                "Expand",
                "Expand .swcompact file(s).\nArguments are .swcompact files.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Recover,
                "Recover",
                "Recover .swcompact file(s), same as expand but replace missing chunks with 'MISSING CHUNK. '\nArguments are .swcompact files.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Stats,
                "Stats",
                "Get some statistics for one or more .swcompact file(s).\nArguments are .swcompact files.\nWildcards are supported.",
                1, 10000
                ),
            new Op(
                Sync,
                "Sync",
                "Synchronize one or more folders with a remote folder sync service.\nArguments are: [ServerPrefix] [RepoName] [Folders..]",
                3, 10000
                ),
            ];

        static IReadOnlyDictionary<String, Op> GetOps() => OpList.ToDictionary(x => x.Name.FastToLower(), StringComparer.Ordinal).Freeze();

        static readonly IReadOnlyDictionary<String, Op> Ops = GetOps();

        const int MaxHeader = 25;

        static void DumpSize(String prefix, long size, String suffix = null)
        {
            var val = size.ToValueString(null, null, 16);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(String.Concat("   ", prefix, ':').PadRight(MaxHeader));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(val);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" bytes");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            if (suffix != null)
                Console.WriteLine(" (" + suffix + ")");
            else
                Console.WriteLine();
        }


        static void DumpTime(String prefix, DateTime time, bool utc = true, String suffix = null)
        {
            var val = (utc ? time : time.ToLocalTime()).ToString("yyyy-MM-dd HH:mm");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(String.Concat("   ", prefix, ':').PadRight(MaxHeader));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(val);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(utc ? " UTC" : " local");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            if (suffix != null)
                Console.WriteLine(" (" + suffix + ")");
            else
                Console.WriteLine();
        }

        static void DumpSection(String name)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("== ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(name);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" ==");
        }

        static void DumpCount(String prefix, long count, Decimal percentage)
            => DumpCount(prefix, count, percentage.ToValueString(2, null, " %"));

        static void DumpCount(String prefix, long count, String suffix = null)
        {
            var val = count.ToValueString(null, null, 16);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(String.Concat("   ", prefix, ':').PadRight(MaxHeader));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(val);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            if (suffix != null)
                Console.WriteLine(" (" + suffix + ")");
            else
                Console.WriteLine();
        }


        static void DumpError(String prefix, String val)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(String.Concat("   ", prefix, ':').PadRight(MaxHeader));
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(val);
        }

        static void DumpPercentage(String prefix, Decimal value, String suffix = null)
        {
            var val = value.ToValueString(3, null, null, 16);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(String.Concat("   ", prefix, ':').PadRight(MaxHeader));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(val);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" %");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            if (suffix != null)
                Console.WriteLine(" (" + suffix + ")");
            else
                Console.WriteLine();
        }

        static void DumpFolder(CdcFolderStats stats)
        {
            DumpSection(stats.Folder);
            var oc = stats.OtherCount;
            var cc = stats.ChunkCount;
            DumpCount("File count", oc + cc);
            DumpSize("Disc size", stats.DiscSize, "estimated");
            DumpCount("Chunks", cc);
            DumpSize("Chunk size", stats.ChunkSize);
            var c = stats.ChunkUncompressedSize;
            if (c > 0)
            {
                DumpSize("Uncompressed size", c);
                DumpSize("Average size", (c + (cc >> 1)) / cc);
                DumpPercentage("Compression ratio", (100M * stats.ChunkSize) / (Decimal)c);
            }
            if (oc > 0)
            {
                DumpCount("Other count", oc);
                DumpSize("Other size", stats.OtherSize);
            }
            DumpTime("Older than", stats.Old);
            oc = stats.OldCount;
            if (oc > 0)
            {
                DumpCount("Old file count", oc);
                DumpSize("Old disc size", stats.OldSize, "estimated");
            }
            Console.WriteLine();
        }

        static void DumpFolder(CdcPruneStats stats)
        {
            DumpSection(stats.Folder);
            DumpTime("Older than", stats.Old);
            var size = stats.BeforeDiscSize;
            var count = stats.BeforeFileCount;
            DumpCount("Before count", count);
            DumpSize("Before size", size, "estimated");
            var errCount = stats.DeleteErrors;
            if (errCount > 0)
            {
                DumpCount("Delete errors", errCount);
                DumpError("First error", stats.DeleteErr);
            }
            var deleteCount = stats.PruneFileCount;
            if (deleteCount > 0)
            {
                var deleteSize = stats.PruneDiscSize;
                DumpCount("Deleted count", deleteCount);
                DumpSize("Deleted size", deleteSize, "estimated");
                DumpPercentage("Deleted count", (100M * deleteCount) / count);
                DumpPercentage("Deleted size", (100M * deleteSize) / size);
                DumpCount("After count", count - deleteCount);
                DumpSize("After size", size - deleteSize, "estimated");
            }
            Console.WriteLine();
        }

        static async ValueTask<int> InternalReport(String[] args, CdcProps props, Params p, bool uncompress)
        {
            int days = args.Length > 1 ? int.Parse(args[1]) : 400;
            if (days < 3)
                throw new Exception("The minimum number of days accpeted is 3");
            var old = DateTime.UtcNow.AddDays(-days).ToStartOfDay(12);
            var stats = await ContentDependentChunking.GetFolderStats(uncompress, old, props);
            CdcFolderStats merged = null;
            foreach (var s in stats)
            {
                DumpFolder(s);
                merged = merged == null ? s : merged.Merge(s);
            }
            if (stats.Length > 1)
                DumpFolder(merged);
            return 0;
        }
        
        static ValueTask<int> Report(String[] args, CdcProps props, Params p)
            => InternalReport(args, props, p, true);

        static ValueTask<int> QuickReport(String[] args, CdcProps props, Params p)
            => InternalReport(args, props, p, false);


        static async ValueTask<int> Prune(String[] args, CdcProps props, Params p)
        {
            int days = args.Length > 1 ? int.Parse(args[1]) : 400;
            if (days < 3)
                throw new Exception("The minimum number of days accpeted is 3");
            var old = DateTime.UtcNow.AddDays(-days).ToStartOfDay(12);
            
            var stats = await ContentDependentChunking.Prune(old, props);
            CdcPruneStats merged = null;
            foreach (var s in stats)
            {
                DumpFolder(s);
                merged = merged == null ? s : merged.Merge(s);
            }
            if (stats.Length > 1)
                DumpFolder(merged);
            return 0;
        }


        static async ValueTask<int> Add(String[] args, CdcProps props, Params p)
        {
            var al = args.Length;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Collecting files");
            var h = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < al; ++ i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                    h.Add(file);
                foreach (var folder in Directory.GetDirectories(dir, name))
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                        h.Add(file);
            }
            var count = h.Count;
            Console.Write(", found ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(count.ToValueString());
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(count == 1 ? " file." : " files.");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Processing");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            var l = new AsyncLock(Math.Max(1, Environment.ProcessorCount - 1));
            async ValueTask AddOne(String f)
            {
                using var _ = await l.Lock().ConfigureAwait(false);
                await ContentDependentChunking.Add(f, props).ConfigureAwait(false);
                Console.Write('.');
            }
            await h.ToList().ProcessAsyncValue(AddOne);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("ok!");
            return 0;
        }

        static String V(long value) => value.ToValueString();

        static async ValueTask<int> Sync(String[] args, CdcProps props, Params p)
        {
            var server = args[1];
            var name = args[2];
            using var syncher = new FolderSyncer(new FolderSyncerParams
            {
                Comment = p.Comment,
                CredFile = p.CredFile,
                Password = p.Password,
                User = p.User,
                IgnoreCertErrors = p.IgnoreCertErrors,
                MaxConcurrency = p.MaxConcurrency,
                Server = server,
            });
            Func<String, bool> ignore = null;
            var ig = p.Ignore;
            if (!String.IsNullOrEmpty(ig))
            {
                var ip = ig.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (ip.Length > 0)
                {
                    ignore = name =>
                    {
                        foreach (var x in ip)
                        {
                            if (Wildcard.Match(name, x))
                                return true;
                        }
                        return false;
                    };
                }
            }
            var al = args.Length;
            HashSet<String> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 3; i < al; ++ i)
            {
                foreach (var x in args[i].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var xx = x.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var ls = x.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) + 1;
                    var folder = xx.Substring(0, ls);
                    if (folder.Length <= 0)
                        folder = Environment.CurrentDirectory;
                    var mask = xx.Substring(ls);
                    foreach (var ff in Directory.GetDirectories(folder, mask, SearchOption.TopDirectoryOnly))
                        folders.Add(ff);
                }
            }
            var fs = String.Join(';', folders.OrderBy(x => x));
            Console.Write("Scanning \"" + fs + "\"");
            var res = await syncher.SyncFolder(fs, name, !p.NoActivate, !p.NoCdc, ignore, (ev, data) =>
            {
                switch (ev)
                {
                    case FolderSyncEvents.Hashed:
                        Console.Write(".");
                        break;
                    case FolderSyncEvents.Scanned:
                        Console.WriteLine();
                        Console.Write("Checking against repo \"" + name + "\" at \"" + server + "\"");
                        break;
                    case FolderSyncEvents.Checked:
                        Console.WriteLine();
                        Console.Write("Uploading");
                        break;
                    case FolderSyncEvents.Uploaded:
                        Console.Write(".");
                        break;

                }
            });
            Console.WriteLine();
            Console.WriteLine();
            var exs = res.Errors;
            if (exs == null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                if (res.Uploaded <= 0)
                {
                    Console.WriteLine("Everything is up to date!");
                    Console.WriteLine("Source files: " + V(res.SourceFiles));
                    Console.WriteLine("Source bytes: " + V(res.SourceBytes));
                }
                else
                {
                    Console.WriteLine("Files: " + V(res.Uploaded) + " / " + V(res.SourceFiles) + "  ( " + (100M * res.Uploaded / Math.Max(1, res.SourceFiles)).ToValueString() + " % )");
                    Console.WriteLine("Source bytes: " + V(res.UploadedSourceBytes) + " / " + V(res.SourceBytes) + "  ( " + (100M * res.UploadedSourceBytes / Math.Max(1, res.SourceBytes)).ToValueString() + " % )");
                    Console.WriteLine("Network bytes: " + V(res.UploadedNetworkBytes) + " / " + V(res.UploadedSourceBytes) + "  ( " + (100M * res.UploadedNetworkBytes / Math.Max(1, res.UploadedSourceBytes)).ToValueString() + " % )");
                    if (res.ChunkCount > 0)
                    {
                        Console.WriteLine("Chunks: " + V(res.NewChunkCount) + " / " + V(res.ChunkCount) + "  ( " + (100M * res.NewChunkCount / Math.Max(1, res.ChunkCount)).ToValueString() + " % )");
                        Console.WriteLine("New chunk bytes: " + V(res.NewChunkSize));
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Errors:");
                int i = 0;
                foreach (var ex in exs)
                {
                    ++i;
                    Console.WriteLine(i.ToString().PadLeft(4) + ": " + ex.Message);
                }
            }
            return 0;
        }

        static String GetArchFilename(String fileOrFolder, Params p)
        {
            var d = p.OutputDir;
            if (d == null)
                return null;
            return Path.Combine(d, Path.GetFileName(fileOrFolder) + ContentDependentChunking.DotFileExt);
        }

        static String GetTargetFolder(String file, Params p)
        {
            var d = p.OutputDir;
            if (d == null)
                return null;
            return Path.Combine(d, Path.GetFileNameWithoutExtension(file));
        }

        static async ValueTask<int> Compact(String[] args, CdcProps props, Params p)
        {
            var al = args.Length;
            var h = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < al; ++i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                {
                    Console.Write(String.Concat("File: \"", file, '"'));
                    await ContentDependentChunking.Compact(file, GetArchFilename(file, p), props);
                    Console.WriteLine();
                }
                foreach (var folder in Directory.GetDirectories(dir, name))
                {
                    Console.Write(String.Concat("Folder: \"", folder, '"'));
                    await ContentDependentChunking.Compact(folder, GetArchFilename(folder, p), props);
                    Console.WriteLine();
                }
            }
            return 0;
        }

        static async ValueTask<int> Expand(String[] args, CdcProps props, Params p)
        {
            var al = args.Length;
            var h = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            Exception exl = null;
            for (int i = 1; i < al; ++i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(file);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    try
                    {
                        await ContentDependentChunking.Expand(file, GetTargetFolder(file, p), props);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(" ok!");
                    }
                    catch (Exception ex)
                    {
                        exl = exl ?? ex;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write(" error: ");
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            if (exl != null)
                return -1;
            return 0;
        }

        static void WriteStatsSummary(CdcChunkStats stats,
            long arcCount,
            long failedArcCount,
            String tab = null
            )
        {
            var uniqueChunkCount = stats.UniqueChunks.Count;
            bool isSummary = tab == null;
            tab = tab ?? "   ";
            if (isSummary)
            {
                if (arcCount == 1)
                {
                    if (failedArcCount > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        DumpSection("SUMMARY");
                        DumpCount("Failed archives", failedArcCount, (100M * failedArcCount) / arcCount);
                        DumpCount("Total archives", arcCount);
                        return;
                    }
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("All files are ok!");
                    return;
                }
                DumpSection("SUMMARY");
            }
            if (stats.TotalMissing > 0)
            {
                if (!isSummary)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(tab + "Missing chunks:");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    foreach (var x in stats.MissingChunks.OrderBy(x => x))
                        Console.WriteLine(tab + "  " + x);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(tab + "Broken files:");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    foreach (var x in stats.BrokenFiles.OrderBy(x => x))
                        Console.WriteLine(tab + "  " + x);
                }
                DumpCount("Total missing chunk", stats.TotalMissing, (100M * stats.TotalMissing) / stats.ChunkCount);
                DumpCount("Unique missing chunks", stats.MissingChunks.Count, (100M * stats.MissingChunks.Count) / uniqueChunkCount);
                DumpCount("Total broken files", stats.BrokenFiles.Count, (100M * stats.BrokenFiles.Count) / stats.FileCount);
            }
            if (isSummary && (failedArcCount > 0))
            {
                DumpCount("Failed archives", failedArcCount, (100M * failedArcCount) / arcCount);
            }
            {
                if (isSummary)
                    DumpCount("Total archives", arcCount);
                DumpCount("Total files", stats.FileCount);
                DumpCount("Total chunks", stats.ChunkCount);
                if (uniqueChunkCount > 0)
                {
                    DumpCount("Unique chunks", uniqueChunkCount);
                    DumpPercentage("Chunk reuse", 100M - (100M * uniqueChunkCount) / stats.ChunkCount);
                }
                DumpSize("Total compressed", stats.ChunkCompSize);
                if (stats.ChunkExpSize > 0)
                {
                    DumpSize("Total expanded", stats.ChunkExpSize);
                    DumpPercentage("Compression ratio", (100M * stats.ChunkCompSize) / stats.ChunkExpSize);
                }
            }
            if (isSummary && (failedArcCount <= 0) && (stats.TotalMissing <= 0))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("All files are ok!");
            }
        }

        static void WriteStatsSummary(StatsSum sum)
            => WriteStatsSummary(sum.Get(out var a, out var b, out var _), a, b);

        static void WriteStats(CdcChunkStats s, String tab = "   ")
            => 
            WriteStatsSummary(s, 0, 0, tab);

        static ValueTask<int> Verify(String[] args, CdcProps props, Params p)
            => InternalVerify(args, props, p, false);

        static ValueTask<int> Touch(String[] args, CdcProps props, Params p)
            => InternalVerify(args, props, p, true);
        sealed class StatsSum
        {
            readonly ConcurrentDictionary<String, int> MissingChunks = new(StringComparer.Ordinal);
            readonly ConcurrentDictionary<String, int> UniqueChunks = new(StringComparer.Ordinal);
            readonly ConcurrentDictionary<String, int> BrokenFiles = new(StringComparer.Ordinal);
            readonly ConcurrentDictionary<String, CdcChunkFileStats> FileData = new(StringComparer.Ordinal);
            long TotalMissing = 0;
            long FileCount = 0;
            long ChunkCount = 0;
            long ChunkCompSize = 0;
            long ChunkSize = 0;
            long ArcCount = 0;
            long FailedArcCount = 0;
            long FileSize = 0;
            String FirstEx;

            public void Add(String filePrefix, CdcChunkStats res)
            {
                Interlocked.Increment(ref ArcCount);
                Interlocked.Add(ref FileSize, res.FileSize);
                Interlocked.Add(ref FileCount, res.FileCount);
                Interlocked.Add(ref ChunkCount, res.ChunkCount);
                Interlocked.Add(ref ChunkCompSize, res.ChunkCompSize);
                Interlocked.Add(ref ChunkSize, res.ChunkExpSize);
                var fileData = FileData;
                foreach (var x in res.Files)
                    fileData.TryAdd(filePrefix + x.Name, x);
                var uniqueChunks = UniqueChunks;
                foreach (var x in res.UniqueChunks)
                    uniqueChunks.TryAdd(x, 0);
                if (res.TotalMissing > 0)
                {
                    Interlocked.Add(ref TotalMissing, res.TotalMissing);
                    var missingChunks = MissingChunks;
                    foreach (var m in res.MissingChunks)
                        missingChunks.TryAdd(m, 0);
                    var brokenFiles = BrokenFiles;
                    foreach (var m in res.BrokenFiles)
                        brokenFiles.TryAdd(filePrefix + m, 0);
                    Interlocked.Increment(ref FailedArcCount);
                }
            }

            public void OnException(Exception ex)
            {
                Interlocked.CompareExchange(ref FirstEx, ex.Message, null);
                Interlocked.Increment(ref ArcCount);
                Interlocked.Increment(ref FailedArcCount);
            }

            public CdcChunkStats Get(out long arcCount, out long failedArcCount, out string firstException)
            {
                arcCount = ArcCount;
                failedArcCount = FailedArcCount;
                firstException = FirstEx;
                return new CdcChunkStats(
                    FileSize,
                    FileCount,
                    ChunkCount,
                    TotalMissing,
                    ChunkCompSize,
                    ChunkSize,
                    MissingChunks.Keys.ToList(),
                    BrokenFiles.Keys.ToList(),
                    UniqueChunks.Keys.ToList(),
                    FileData.Values.ToList()
                    );
            }
        }

        static async ValueTask<int> InternalVerify(String[] args, CdcProps props, Params p, bool touch)
        {
            var al = args.Length;
            var h = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            Exception exl = null;
            var sum = new StatsSum();
            for (int i = 1; i < al; ++i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(file);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    try
                    {
                        var res = await ContentDependentChunking.Verify(file, props, touch);
                        sum.Add(Path.GetFileNameWithoutExtension(file) + "/", res);
                        if (res.TotalMissing > 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(" missing " + res.TotalMissing.ToValueString() + " (" + res.MissingChunks.Count.ToValueString() + " unique) in " + res.BrokenFiles.Count.ToValueString());
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(" ok!");
                        }
                    }
                    catch (Exception ex)
                    {
                        sum.OnException(ex);
                        exl = exl ?? ex;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(" error: " + ex.Message);
                    }
                }
            }
            WriteStatsSummary(sum);
            if (exl != null)
                return -1;
            return 0;
        }


        static async ValueTask<int> Recover(String[] args, CdcProps props, Params p)
        {
            var al = args.Length;
            Exception exl = null;
            var sum = new StatsSum();
            for (int i = 1; i < al; ++i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(file);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\"");
                    try
                    {
                        var res = await ContentDependentChunking.Recover(file, GetTargetFolder(file, p), props);
                        sum.Add(Path.GetFileNameWithoutExtension(file) + "/", res);
                        if (res.TotalMissing > 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(" missing " + res.TotalMissing.ToValueString() + " (" + res.MissingChunks.Count.ToValueString() + " unique) in " + res.BrokenFiles.Count.ToValueString());
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(" ok!");
                        }
                    }
                    catch (Exception ex)
                    {
                        sum.OnException(ex);
                        exl = exl ?? ex;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(" error: " + ex.Message);
                    }
                }
            }
            WriteStatsSummary(sum);
            if (exl != null)
                return -1;
            return 0;
        }

        static async ValueTask<int> Stats(String[] args, CdcProps props, Params p)
        {
            var al = args.Length;
            Exception exl = null;
            var sum = new StatsSum();
            for (int i = 1; i < al; ++i)
            {
                var dir = PathExt.GetDirectoryAndMask(args[i], out var name);
                foreach (var file in Directory.GetFiles(dir, name))
                {
                    DumpSection(file);
                    try
                    {
                        var res = await ContentDependentChunking.Verify(file, props, false, true);
                        sum.Add(Path.GetFileNameWithoutExtension(file) + "/", res);
                        WriteStats(res);
                    }
                    catch (Exception ex)
                    {
                        sum.OnException(ex);
                        exl = exl ?? ex;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("   Error: " + ex.Message);
                    }
                    Console.WriteLine();
                }
            }
            WriteStatsSummary(sum);
            if (exl != null)
                return -1;
            return 0;
        }


        static int Usage(String err = null, int retCode = 0, Op op = null)
        {
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(err);
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            foreach (var t in CommandLine.SyntaxObject<Params>(Args, CommandLine.OptionMembers.All))
                Console.WriteLine(t);
            /*Console.Write("Use: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("SwSyncTool.exe ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(op == null ? "[Operation]" : op.Name);
            if ((op?.MinArgs ?? 1) > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" <Arguments>");
            }else
                Console.WriteLine();
            */
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Operations:");
            IReadOnlyList<Op> ops = op == null ? OpList.ToList() : [op];
            var maxOpLen = ops.Max(x => x.Name.Length) + 1;
            var sp = "\n" + new String(' ', maxOpLen + 2);
            foreach (var x in ops)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  " + x.Name.PadRight(maxOpLen));
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(x.Description.Replace("\n", sp));
                Console.WriteLine();
            }
            Console.ResetColor();
            return retCode;
        }

  

        static readonly CommandLineArgument[] Args = [
                CommandLineArgument.Make<String>("operation", false, null, "The operation to perform"),
                null,
            ];



        static async Task<int> Main(string[] argsC)
        {
            var start = DateTime.UtcNow;
            CommandLine cmd;
            Params p;
            try
            {
                cmd = CommandLine.ParseObject<Params>(out p, argsC, Args, CommandLine.OptionMembers.All);
                if (cmd == null)
                    return Usage();
            }
            catch (Exception ex)
            {
                return Usage(ex.Message, -1);
            }
            var args = cmd.Arguments;
            var al = args.Length;
            if (al <= 0)
            {
                foreach (var t in CommandLine.SyntaxObject<Params>(Args, CommandLine.OptionMembers.All))
                    Console.WriteLine(t);
                return Usage();
            }
            --al;
            argsC = args.Convert(x => x.Item2?.ToString());
            var opName = args[0].Item2 as String;
            if (!Ops.TryGetValue(opName.FastToLower(), out var op))
                return Usage("Invalid operation \"" + opName + "\"!", -1);
            try
            {
                if (al < op.MinArgs)
                    return Usage("Too few arguments!", -2, op);
                if (al > op.MaxArgs)
                    return Usage("Too many arguments!", -3, op);

                var props = CdcProps.Default;
                if (!String.IsNullOrEmpty(p.Folders))
                    props = new CdcProps(props.AverageSize, 0, 0, props.Hash, props.HashName, props.Comp.HttpCode, p.Folders.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                if (p.ChunkSize > 0)
                    props = new CdcProps(p.ChunkSize, 0, 0, props.Hash, props.HashName, props.Comp.HttpCode, p.Folders.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                var res = await op.Func(argsC, props, p);
                if (res != 0)
                    return Usage(null, res, op);
                var took = DateTime.UtcNow - start;
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine(took.TotalSeconds.ToValueString(2, "All done! Took: ", " seconds"));
                return 0;
            }
            catch (Exception ex)
            {
                return Usage(ex.Message, -2, op);
            }
        }


    }



}
