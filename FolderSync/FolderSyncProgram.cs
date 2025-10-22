using System;
using System.Globalization;
using System.Threading.Tasks;
using SysWeaver;
using SysWeaver.FolderSync;

namespace FolderSync
{

    internal class FolderSyncProgram
    {

        static String V(long value) => value.ToString("### ### ### ### ### ### ### ##0").TrimStart();

        static async Task Main(string[] args)
        {
            var start = DateTime.UtcNow;
            CommandLineArgument[] ca =
            [
                CommandLineArgument.Make<String>("Source", false, null, "The source folder on this computer. Multiple folders can be specified separated by a ';'"),
                CommandLineArgument.Make<String>("Server", false, null, "The server to sync the folder to."),
                CommandLineArgument.Make<String>("Name", false, null, "The name of the server folder."),
            ];
            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                foreach (var x in CommandLine.SyntaxObject<Params>(ca))
                    Console.WriteLine(x);
                Console.ResetColor();
                return;
            }
            try
            {
                var cmd = CommandLine.ParseObject<Params>(out var opt, args, ca);
                var srcFolder = cmd.Arguments[0].Item2 as String;
                var name = cmd.Arguments[2].Item2 as String;
                var p = new FolderSyncerParams
                {
                    Comment = opt.Comment,
                    CredFile = opt.CredFile,
                    Server = cmd.Arguments[1].Item2 as String,
                    User = opt.User,
                    Password = opt.Password,
                    IgnoreCertErrors = opt.IgnoreCertErrors,
                };

                using var syncher = new FolderSyncer(p);
                Console.Write("Scanning \"" + srcFolder + "\"");
                var res = await syncher.SyncFolder(srcFolder, name, !opt.NoSwitch, (ev, data) =>
                {
                    switch (ev)
                    {
                        case FolderSyncEvents.Hashed:
                            Console.Write(".");
                            break;
                        case FolderSyncEvents.Scanned:
                            Console.WriteLine();
                            Console.Write("Checking against repo \"" + name + "\" at \"" + p.Server + "\"");
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
                        Console.WriteLine("Files: " + V(res.Uploaded) + " / " + V(res.SourceFiles) + "  ( " + (100M * res.Uploaded / Math.Max(1, res.SourceFiles)).ToString("0.00", CultureInfo.InvariantCulture) + " % )");
                        Console.WriteLine("Source bytes: " + V(res.UploadedSourceBytes) + " / " + V(res.SourceBytes) + "  ( " + (100M * res.UploadedSourceBytes / Math.Max(1, res.SourceBytes)).ToString("0.00", CultureInfo.InvariantCulture) + " % )");
                        Console.WriteLine("Network bytes: " + V(res.UploadedNetworkBytes) + " / " + V(res.UploadedSourceBytes) + "  ( " + (100M * res.UploadedNetworkBytes / Math.Max(1, res.UploadedSourceBytes)).ToString("0.00", CultureInfo.InvariantCulture) + " % )");
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
                return;
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
