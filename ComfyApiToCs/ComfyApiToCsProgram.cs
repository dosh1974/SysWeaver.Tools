using SysWeaver.AI;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysWeaver
{

    internal class ComfyApiToCsProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0 = success or help requested, 1 = an exception while processing, 2 = invalid command line (from SysWeaver.CommandLine FilesToFolderTool).

        /// <summary>
        /// Exit code: one or more files couldn't be converted (not an API format workflow, invalid json etc), the others are converted.
        /// </summary>
        const int ExitConvertFailed = 3;

        static int Failed;

        /// <summary>
        /// Clean up a file name to a type name: words (split at non letters / digits and lower to upper case changes) with the first letter in upper case and the rest in lower case,
        /// ex: "AUK_Text_2_speech" => "AukText2Speech", "my-workflow v2" => "MyWorkflowV2"
        /// </summary>
        static String CleanTypeName(String name)
        {
            var sb = new StringBuilder(name.Length);
            var word = new StringBuilder();
            void Flush()
            {
                if (word.Length <= 0)
                    return;
                sb.Append(Char.ToUpperInvariant(word[0]));
                for (int i = 1; i < word.Length; ++i)
                    sb.Append(Char.ToLowerInvariant(word[i]));
                word.Clear();
            }
            char prev = '\0';
            foreach (var c in name)
            {
                if ((c > 127) || !Char.IsLetterOrDigit(c))
                {
                    Flush();
                    prev = '\0';
                    continue;
                }
                //  "maxDuration" => "max", "Duration", "Text2Speech" => "Text", "2", "Speech"
                if ((word.Length > 0) && ((Char.IsUpper(c) && Char.IsLower(prev)) || (Char.IsDigit(c) != Char.IsDigit(prev))))
                    Flush();
                word.Append(c);
                prev = c;
            }
            Flush();
            if (sb.Length <= 0)
                return "Workflow";
            if (Char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        static ComfyUiTypeOptions GetOptions(IMessageHost msg, ComfyApiToCsParams opt, String sourceFile)
        {
            var name = Path.GetFileNameWithoutExtension(sourceFile);
            return new ComfyUiTypeOptions
            {
                Namespace = opt.Namespace,
                FileScopedNamespace = opt.FileScoped,
                WorkflowName = name,
                TypeName = String.IsNullOrEmpty(opt.TypeName) ? CleanTypeName(name) : opt.TypeName,
                InputTypeSuffix = opt.InputSuffix ?? "",
                OutputTypeSuffix = opt.OutputSuffix ?? "",
                MemberCasing = opt.Casing,
                PreferShortNames = opt.ShortNames,
                ExplicitNames = opt.Explicit,
                UseProperties = opt.Properties,
                Comments = opt.Comments,
                InitializeDefaults = opt.Defaults,
                BypassMembers = opt.Bypass,
                NestedTypes = opt.Nested,
                FileInputType = opt.FileType,
                OutputType = opt.OutputType,
                UiWorkflowFolder = opt.UiFolder,
                Msg = msg,
            };
        }

        static async Task<int> ProcessFile(IMessageHost msg, ComfyApiToCsParams opt, String sourceFile, String relSource, String destFolder)
        {
            var destName = Path.GetFileNameWithoutExtension(sourceFile) + ".cs";
            var dest = Path.Combine(destFolder, destName);
            var relDest = Path.Combine(Path.GetDirectoryName(relSource) ?? "", destName);
            if (!opt.Overwrite && File.Exists(dest))
            {
                msg.AddMessage("Skipped " + relDest.ToQuoted() + " since it already exists", MessageLevels.Debug);
                return 0;
            }
            var o = GetOptions(msg, opt, sourceFile);
            String source;
            try
            {
                source = await ComfyUiTypeGenerator.GetSource(sourceFile, o).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref Failed);
                msg.AddMessage("Failed to convert " + relSource.ToQuoted() + ": " + ex.Message, MessageLevels.Error);
                return 0;
            }
            //  Don't touch an unchanged file (no rebuilds)
            if (File.Exists(dest) && (await File.ReadAllTextAsync(dest).ConfigureAwait(false) == source))
            {
                msg.AddMessage(relDest.ToQuoted() + " [unchanged]");
                return 0;
            }
            await File.WriteAllTextAsync(dest, source, new UTF8Encoding(false)).ConfigureAwait(false);
            msg.AddMessage(String.Concat(relDest.ToQuoted(), " [", o.TypeName + o.InputTypeSuffix, ", ", o.TypeName + o.OutputTypeSuffix, "]"));
            return 0;
        }

        static int Setup(ComfyApiToCsParams opt)
        {
            if (!String.IsNullOrEmpty(opt.Namespace) && opt.Namespace.Split('.').Any(x => (x.Length == 0) || Char.IsDigit(x[0]) || !x.All(c => Char.IsLetterOrDigit(c) || (c == '_'))))
                throw new Exception("Invalid namespace " + opt.Namespace.ToQuoted());
            if (String.Equals(opt.InputSuffix ?? "", opt.OutputSuffix ?? "", StringComparison.Ordinal))
                throw new Exception("The input and output suffix must differ");
            return 0;
        }


        /// <summary>
        /// Process exit codes (from SysWeaver.CommandLine FilesToFolderTool), 0 = success, non-zero (1-255) = failure:
        /// 0 = success or help requested, 1 = an exception while processing, 2 = invalid command line, 3 = one or more files couldn't be converted.
        /// </summary>
        static async Task<int> Main(string[] args)
        {
            int ret;
            var tool = FilesToFolderTool<ComfyApiToCsParams>.Instance;
            tool.SourceFilesHelp = "ComfyUI API format workflow files (saved using \"Workflow > Export (API)\"), wildcard masks separated by ';', a trailing '+' searches sub folders (ex: .\\workflows\\*.json+)";
            tool.DestFolderHelp = "The folder of the generated .cs files, one per workflow with the same name";
#if DEBUG
            ret = await FilesToFolderTool.OnFilesAsync<ComfyApiToCsParams>(args, ProcessFile, Setup).ConfigureAwait(false);
#else//DEBUG
            ret = await FilesToFolderTool.OnFilesParallelAsync<ComfyApiToCsParams>(args, ProcessFile, Setup).ConfigureAwait(false);
#endif//DEBUG
            if ((ret == 0) && (Failed > 0))
                ret = ExitConvertFailed;
            return ret;
        }
    }


}
