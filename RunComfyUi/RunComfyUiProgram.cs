using SysWeaver.AI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysWeaver
{

    /// <summary>
    /// Runs a ComfyUI workflow from the command line.
    /// The input and output types are generated from the workflow (ComfyUiTypeGenerator) and compiled at runtime (Roslyn),
    /// the input type inherits ComfyUiParams so its members (the connection parameters and the workflow inputs) are the command line options.
    /// </summary>
    internal class RunComfyUiProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0 = success or help (the parameters) shown, 1 = an exception (ex: the types couldn't be compiled), 2 = invalid command line (same as SysWeaver.CommandLine FilesToFolderTool).

        /// <summary>
        /// Exit code: running the workflow failed (connection, server or workflow error, cancelled).
        /// </summary>
        const int ExitRunFailed = 3;

        /// <summary>
        /// Exit code: the workflow returned no results.
        /// </summary>
        const int ExitNoResults = 4;

        const String WorkflowArg = "Workflow";
        const String DestinationArg = "Destination";

        static readonly CommandLineArgument[] ValidArgs =
        [
            CommandLineArgument.Make<String>(WorkflowArg, false, null, "A ComfyUI API format workflow file (saved using \"Workflow > Export (API)\")"),
            CommandLineArgument.Make<String>(DestinationArg, true, null,
                "Where to save the results: an existing folder (a new file named after the workflow, with the extension of the result) or a file name.\n" +
                "Without a destination the parameters of the workflow are shown."),
        ];

        static async Task<int> Main(string[] args)
        {
            var msg = new MessageHost(ConsoleMessageHandler.GetSync());
            //  File inputs: a local file or an url, json values (inputs without a nested type)
            CommandLine.AddParser(typeof(ComfyUiFile), ParseFile);
            CommandLine.AddParser(typeof(JsonElement), s => JsonDocument.Parse(s).RootElement.Clone());

            //  The workflow file decides the options, find it first
            var wfFile = FindWorkflowFile(args);
            if (wfFile == null)
            {
                var help = (args.Length == 0) || args.Any(IsHelp);
                if (!help)
                    msg.AddMessage("No workflow file (an existing .json file) found on the command line", MessageLevels.Error);
                ShowSyntax(msg, new ComfyUiParams());
                return help ? FilesToFolderTool.ExitSuccess : FilesToFolderTool.ExitInvalidArguments;
            }
            var name = Path.GetFileNameWithoutExtension(wfFile);
            var folder = Path.GetDirectoryName(Path.GetFullPath(wfFile));

            //  Messages while loading the workflow and creating the types (ex: the subgraph annotations), all are shown when running, only warnings and errors with the help
            var loadMessages = new BufferedMessages();
            var loadMsg = new MessageHost(loadMessages);

            //  The connection parameters given on the command line, used to load the workflow (the UI format workflow can be fetched from the server to annotate subgraph inputs)
            ComfyUiParams conn;
            try
            {
                conn = ParseConnection(args);
            }
            catch (Exception ex)
            {
                msg.AddMessage(ex.Message, MessageLevels.Error);
                ShowSyntax(msg, new ComfyUiParams());
                return FilesToFolderTool.ExitInvalidArguments;
            }
            conn.WorkflowFolder = folder;
            ComfyUiWorkflow wf;
            try
            {
                wf = await new ComfyUiService(null, conn, loadMsg).GetWorkflow(name).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                //  Ex: the server can't be reached to fetch the UI workflow, the parameters can still be shown
                loadMsg.AddMessage("Failed to load the workflow with annotated subgraph inputs, using the API workflow only: " + ex.Message, MessageLevels.Warning);
                try
                {
                    wf = ComfyUiWorkflow.Load(name, wfFile, null, loadMsg);
                }
                catch (Exception ex2)
                {
                    loadMessages.Replay(msg, MessageLevels.All);
                    msg.AddMessage("Failed to load the workflow " + wfFile.ToFilename() + ": " + ex2.Message, MessageLevels.Error);
                    return FilesToFolderTool.ExitInvalidArguments;
                }
            }

            //  The runtime types
            WorkflowTypes types;
            try
            {
                types = WorkflowTypes.Create(wf, loadMsg);
            }
            catch (Exception ex)
            {
                loadMessages.Replay(msg, MessageLevels.All);
                msg.AddMessage(ex.Message, MessageLevels.Error);
                return FilesToFolderTool.ExitGenericError;
            }

            //  Parse all options into an instance of the input type
            var opt = (ComfyUiParams)Activator.CreateInstance(types.Input);
            opt.WorkflowFolder = folder;
            CommandLine cmd;
            try
            {
                cmd = CommandLine.ParseObject<Object>(out _, args, ValidArgs, opt, CommandLine.OptionMembers.All);
            }
            catch (Exception ex)
            {
                loadMessages.Replay(msg, MessageLevels.Warning);
                msg.AddMessage(ex.Message, MessageLevels.Error);
                ShowHelp(msg, (ComfyUiParams)Activator.CreateInstance(types.Input), types, wf, folder);
                return FilesToFolderTool.ExitInvalidArguments;
            }
            var dest = cmd?.Arguments.FirstOrDefault(x => x.Item1.Name == DestinationArg)?.Item2 as String;
            if (String.IsNullOrEmpty(dest))
            {
                //  Help requested or no destination: show the parameters
                loadMessages.Replay(msg, MessageLevels.Warning);
                ShowHelp(msg, (ComfyUiParams)Activator.CreateInstance(types.Input), types, wf, folder);
                return FilesToFolderTool.ExitSuccess;
            }
            //  The workflow is always loaded from the folder of the workflow file
            loadMessages.Replay(msg, MessageLevels.All);
            opt.WorkflowFolder = folder;
            return await Run(msg, opt, types, name, dest).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps messages to show them later (or not)
        /// </summary>
        sealed class BufferedMessages : MessageHandler
        {
            public BufferedMessages() : base(Modes.NativeSync)
            {
            }

            readonly List<Message> Messages = new();

            protected override Task Add(Message message)
            {
                lock (Messages)
                    Messages.Add(message);
                return CompletedTask;
            }

            /// <summary>
            /// Add the kept messages with at least some level to a message host (once)
            /// </summary>
            public void Replay(IMessageHost msg, MessageLevels minLevel)
            {
                Message[] all;
                lock (Messages)
                {
                    all = Messages.ToArray();
                    Messages.Clear();
                }
                foreach (var m in all.Where(x => x.Level >= minLevel))
                {
                    if (m.Exception != null)
                        msg.AddMessage(m.Text, m.Exception, m.Level);
                    else
                        msg.AddMessage(m.Text, m.Level);
                }
            }
        }

        #region Command line

        static bool IsHelp(String a)
        {
            var s = a;
            if (!CommandLine.IsOption(ref s))
                return false;
            return (s == CommandLine.HelpOption1.Name) || s.FastEquals(CommandLine.HelpOption2.Name);
        }

        /// <summary>
        /// The workflow file: the first argument that is an existing .json file
        /// </summary>
        static String FindWorkflowFile(String[] args)
        {
            foreach (var a in args)
            {
                var s = a;
                if (CommandLine.IsOption(ref s))
                    continue;
                if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                    return Path.GetFullPath(a);
            }
            return null;
        }

        /// <summary>
        /// Parse the connection options (the options of ComfyUiParams) only, all other arguments are ignored
        /// </summary>
        static ComfyUiParams ParseConnection(String[] args)
        {
            var p = new ComfyUiParams();
            var opts = CommandLine.GetOptions(p, CommandLine.OptionMembers.All).Where(x => x.Item2 != null).ToDictionary(x => x.Item1.Name, x => x.Item1.Args?.Length ?? 0, CommandLine.DefaultOptionsComparer);
            var sub = new List<String>();
            for (int i = 0; i < args.Length; ++i)
            {
                var s = args[i];
                if (!CommandLine.IsOption(ref s) || !opts.TryGetValue(s, out var count))
                    continue;
                sub.Add(args[i]);
                for (int j = 0; (j < count) && (i + 1 < args.Length); ++j)
                    sub.Add(args[++i]);
            }
            CommandLine.ParseObject(out p, sub.ToArray(), [], p, CommandLine.OptionMembers.All);
            return p;
        }

        static Object ParseFile(String s)
        {
            s = s.Trim();
            if (Uri.TryCreate(s, UriKind.Absolute, out var u) && ((u.Scheme == Uri.UriSchemeHttp) || (u.Scheme == Uri.UriSchemeHttps)))
                return ComfyUiFile.FromUrl(s);
            if (!File.Exists(s))
                throw new ArgumentException("The file " + s.ToQuoted() + " doesn't exist");
            //  No name: a unique name is made from the content
            return ComfyUiFile.FromFile(s);
        }

        static void ShowSyntax(IMessageHost msg, Object opt)
        {
            foreach (var t in CommandLine.SyntaxObject<Object>(ValidArgs, opt, CommandLine.OptionMembers.All))
                msg.AddMessage(t);
        }

        static void ShowHelp(IMessageHost msg, ComfyUiParams opt, WorkflowTypes types, ComfyUiWorkflow wf, String folder)
        {
            opt.WorkflowFolder = folder;
            msg.AddMessage(String.Concat("Workflow ", wf.Name.ToQuoted(), " (", Path.Combine(folder, wf.Name + ".json").ToFilename(), ")"));
            ShowSyntax(msg, opt);
            msg.AddMessage("Outputs:");
            foreach (var o in types.Outputs)
                msg.AddMessage(String.Concat("  ", o.Tag.ToQuoted(), " (", o.Ext, ")"));
        }

        #endregion//Command line

        #region Run

        static async Task<int> Run(IMessageHost msg, ComfyUiParams opt, WorkflowTypes types, String name, String dest)
        {
            Object result;
            using (var cts = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancel = (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    var service = new ComfyUiService(null, opt, msg);
                    msg.AddMessage(String.Concat("Running ", name.ToQuoted(), " on ", service.EndPoint.ToString(), " (Ctrl+C to cancel)"));
                    var sw = Stopwatch.StartNew();
                    var run = typeof(ComfyUiService).GetMethod(nameof(ComfyUiService.RunWorkflow)).MakeGenericMethod(types.Input, types.Output);
                    var task = (Task)run.Invoke(service, [opt, name, cts.Token]);
                    await task.ConfigureAwait(false);
                    result = task.GetType().GetProperty(nameof(Task<int>.Result)).GetValue(task);
                    msg.AddMessage(String.Concat("Done in ", sw.Elapsed.TotalSeconds.ToString("0.0"), " s"));
                }
                catch (Exception ex)
                {
                    if (ex is TargetInvocationException tie && (tie.InnerException != null))
                        ex = tie.InnerException;
                    msg.AddMessage((cts.IsCancellationRequested ? "Cancelled: " : "Failed to run the workflow: ") + ex.Message, MessageLevels.Error);
                    return ExitRunFailed;
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            }
            //  All results of all outputs, the extension from the data (if known) else the one guessed from the output node
            var files = new List<(byte[] Data, String Tag, String Ext)>();
            foreach (var (m, tag, ext) in types.Outputs)
            {
                var v = (m is FieldInfo f ? f.GetValue(result) : ((PropertyInfo)m).GetValue(result)) as IEnumerable<byte[]>;
                if (v == null)
                    continue;
                foreach (var data in v)
                {
                    if ((data == null) || (data.Length <= 0))
                        continue;
                    var e = ComfyUiFile.GetExtension(data);
                    files.Add((data, tag, e == ".bin" ? ext : e));
                }
            }
            if (files.Count <= 0)
            {
                msg.AddMessage("The workflow returned no results", MessageLevels.Error);
                return ExitNoResults;
            }
            foreach (var (fileName, data) in GetFileNames(files, name, dest))
            {
                await File.WriteAllBytesAsync(fileName, data).ConfigureAwait(false);
                msg.AddMessage(String.Concat("Saved ", fileName.ToFilename(), " [", data.Length.ToString(), " bytes]"));
            }
            return FilesToFolderTool.ExitSuccess;
        }

        /// <summary>
        /// The file names of the results.
        /// An existing folder (or a name ending with a separator): new files named after the workflow (and the output tag if there are several outputs), with the extension of the result.
        /// Else a file name (the extension of the result is added if it has none): the first result is saved to it, other results to new files named after it.
        /// </summary>
        static IEnumerable<(String FileName, byte[] Data)> GetFileNames(List<(byte[] Data, String Tag, String Ext)> files, String name, String dest)
        {
            var isFolder = Directory.Exists(dest) || dest.EndsWith(Path.DirectorySeparatorChar) || dest.EndsWith(Path.AltDirectorySeparatorChar);
            if (isFolder)
            {
                var folder = Path.GetFullPath(dest);
                Directory.CreateDirectory(folder);
                var multipleOutputs = files.Select(x => x.Tag).Distinct(StringComparer.Ordinal).Count() > 1;
                foreach (var f in files)
                    yield return (Unique(folder, multipleOutputs ? String.Concat(name, "_", CleanFileName(f.Tag)) : name, f.Ext), f.Data);
                yield break;
            }
            var fileName = Path.GetFullPath(dest);
            //  A file name without an extension gets the extension of the (first) result
            if (String.IsNullOrEmpty(Path.GetExtension(fileName)))
                fileName += files[0].Ext;
            var dir = Path.GetDirectoryName(fileName);
            Directory.CreateDirectory(dir);
            yield return (fileName, files[0].Data);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            foreach (var f in files.Skip(1))
                yield return (Unique(dir, stem, f.Ext), f.Data);
        }

        /// <summary>
        /// A file name that doesn't exist: "stem.ext", else "stem_2.ext", "stem_3.ext" etc
        /// </summary>
        static String Unique(String folder, String stem, String ext)
        {
            var fn = Path.Combine(folder, stem + ext);
            for (int i = 2; File.Exists(fn); ++i)
                fn = Path.Combine(folder, String.Concat(stem, "_", i.ToString(), ext));
            return fn;
        }

        static String CleanFileName(String s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new String(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        }

        #endregion//Run
    }


}
