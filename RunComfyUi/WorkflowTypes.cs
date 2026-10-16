using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SysWeaver.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysWeaver
{

    /// <summary>
    /// The runtime (Roslyn compiled) input and output types of a workflow.
    /// The input type inherits ComfyUiParams, so its members are both the connection parameters and the workflow inputs (used as command line options).
    /// </summary>
    sealed class WorkflowTypes
    {
        const String Namespace = "RunComfyUi.Workflow";
        const String TypeName = "Workflow";

        /// <summary>
        /// The input type (inherits ComfyUiParams)
        /// </summary>
        public readonly Type Input;

        /// <summary>
        /// The output type, each member is a List&lt;byte[]&gt; (all results of an output)
        /// </summary>
        public readonly Type Output;

        /// <summary>
        /// The outputs: the member of the output type, the output tag and the guessed file extension (from the output node)
        /// </summary>
        public readonly IReadOnlyList<(MemberInfo Member, String Tag, String Ext)> Outputs;

        WorkflowTypes(Type input, Type output, IReadOnlyList<(MemberInfo, String, String)> outputs)
        {
            Input = input;
            Output = output;
            Outputs = outputs;
        }

        /// <summary>
        /// Create the types of a workflow
        /// </summary>
        /// <param name="wf">The (annotated) workflow</param>
        /// <param name="msg">Messages</param>
        public static WorkflowTypes Create(ComfyUiWorkflow wf, IMessageHost msg)
        {
            var o = new ComfyUiTypeOptions
            {
                Namespace = Namespace,
                WorkflowName = wf.Name,
                TypeName = TypeName,
                InputBaseType = nameof(ComfyUiParams),
                //  Unset inputs are null (not sent, the workflow default is used), the help shows the (shortened) workflow default from the comment
                InitializeDefaults = false,
                //  Properties: the workflow inputs are listed before the (inherited) connection parameters
                UseProperties = true,
                //  Nested types become nested command line options (ex: -Config.Speed 1.5)
                NestedTypes = true,
                FileInputType = nameof(ComfyUiFile),
                OutputType = "List<byte[]>",
                Comments = true,
                Msg = msg,
            };
            var source = ComfyUiTypeGenerator.GetSource(wf, o);
            var asm = LoadAssembly(source);
            var input = asm.GetType(String.Concat(Namespace, ".", TypeName, o.InputTypeSuffix), true);
            var output = asm.GetType(String.Concat(Namespace, ".", TypeName, o.OutputTypeSuffix), true);
            return new WorkflowTypes(input, output, GetOutputs(wf, output));
        }

        #region Compile

        /// <summary>
        /// Compile the source (or use a cached compilation), the assembly and its xml documentation are written to a cache folder
        /// so that the documentation is found for the command line help (SysWeaver.Docs reads "Assembly.xml" next to the assembly)
        /// </summary>
        static Assembly LoadAssembly(String source)
        {
            //  The key includes the SysWeaver.AI.ComfyUi version (the generated code references it)
            var comfy = typeof(ComfyUiParams).Assembly;
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(String.Concat(source, "\n", comfy.ManifestModule.ModuleVersionId.ToString()))), 0, 12);
            var asmName = "RunComfyUi_" + key;
            var folder = Path.Combine(Path.GetTempPath(), "RunComfyUi", key);
            var dll = Path.Combine(folder, asmName + ".dll");
            var xml = Path.Combine(folder, asmName + ".xml");
            if (!(File.Exists(dll) && File.Exists(xml)))
            {
                var (peData, xmlData) = Compile(asmName, source);
                Directory.CreateDirectory(folder);
                //  Write to temp files and move in place (another process may do the same)
                WriteAtomic(xml, xmlData);
                WriteAtomic(dll, peData);
            }
            return Assembly.LoadFrom(dll);
        }

        static void WriteAtomic(String fileName, byte[] data)
        {
            var temp = String.Concat(fileName, ".", Guid.NewGuid().ToString("N"), ".tmp");
            File.WriteAllBytes(temp, data);
            try
            {
                File.Move(temp, fileName, true);
            }
            catch
            {
                File.Delete(temp);
                if (!File.Exists(fileName))
                    throw;
            }
        }

        static (byte[] Pe, byte[] Xml) Compile(String asmName, String source)
        {
            //  All runtime assemblies (the generated code uses System.Text.Json, collections etc) and the application ones
            var files = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is String tpa)
                foreach (var f in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    files.Add(f);
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (!a.IsDynamic && !String.IsNullOrEmpty(a.Location))
                    files.Add(a.Location);
            if (files.Count <= 0)
                throw new Exception("Can't find the assemblies to compile the workflow types against (a single file publish isn't supported)");
            var refs = files.Where(File.Exists).Select(x => MetadataReference.CreateFromFile(x)).ToList();
            var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithDocumentationMode(DocumentationMode.Parse);
            var tree = CSharpSyntaxTree.ParseText(source, parse);
            var comp = CSharpCompilation.Create(asmName, [tree], refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Disable));
            using var pe = new MemoryStream();
            using var xml = new MemoryStream();
            var res = comp.Emit(pe, xmlDocumentationStream: xml);
            if (!res.Success)
            {
                var sb = new StringBuilder("Failed to compile the workflow types:");
                foreach (var d in res.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error))
                    sb.AppendLine().Append(d.Id).Append(": ").Append(d.GetMessage());
                sb.AppendLine().AppendLine("Source:").Append(source);
                throw new Exception(sb.ToString());
            }
            return (pe.ToArray(), xml.ToArray());
        }

        #endregion//Compile

        #region Outputs

        static String Norm(String s) => new String(s.Where(Char.IsLetterOrDigit).Select(Char.ToLowerInvariant).ToArray());

        /// <summary>
        /// The output members, their tags and guessed extensions
        /// </summary>
        static IReadOnlyList<(MemberInfo, String, String)> GetOutputs(ComfyUiWorkflow wf, Type output)
        {
            JsonDocument doc = null;
            try
            {
                if (!String.IsNullOrEmpty(wf.FileName) && File.Exists(wf.FileName))
                    doc = JsonDocument.Parse(File.ReadAllBytes(wf.FileName));
            }
            catch
            {
            }
            using (doc)
            {
                var list = new List<(MemberInfo, String, String)>();
                foreach (var m in output.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(x => x is FieldInfo || x is PropertyInfo))
                {
                    //  The tag: explicit or matched by name (like RunWorkflow does)
                    var tag = m.GetCustomAttribute<ComfyUiNameAttribute>()?.Name ?? wf.Outputs.FirstOrDefault(x => Norm(x) == Norm(m.Name)) ?? m.Name;
                    list.Add((m, tag, doc == null ? ".bin" : GuessExtension(doc.RootElement, tag)));
                }
                return list;
            }
        }

        static readonly HashSet<String> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "png", "jpg", "jpeg", "webp", "gif", "bmp", "tif", "tiff", "exr",
            "mp4", "webm", "mov", "mkv", "avi",
            "flac", "wav", "mp3", "opus", "ogg", "m4a",
            "txt", "json", "glb", "obj", "ply",
        };

        /// <summary>
        /// Guess the file extension of an output from its node(s): a "format" like input (ex: "png", "video/h264-mp4"), else the node class (ex: "SaveAudioMP3", "SaveVideo")
        /// </summary>
        static String GuessExtension(JsonElement root, String tag)
        {
            foreach (var node in root.EnumerateObject())
            {
                var n = node.Value;
                if ((n.ValueKind != JsonValueKind.Object) || !n.TryGetProperty("_meta", out var meta) || (meta.ValueKind != JsonValueKind.Object))
                    continue;
                if (!meta.TryGetProperty("title", out var title) || (title.ValueKind != JsonValueKind.String) || !title.GetString().Contains("#" + tag, StringComparison.Ordinal))
                    continue;
                if (n.TryGetProperty("inputs", out var inputs) && (inputs.ValueKind == JsonValueKind.Object))
                {
                    foreach (var name in new[] { "format", "file_format", "extension", "output_format", "container" })
                    {
                        if (!inputs.TryGetProperty(name, out var f) || (f.ValueKind != JsonValueKind.String))
                            continue;
                        //  The last known extension in the value, ex: "video/h264-mp4" => "mp4"
                        var ext = f.GetString().Split(['/', '-', '.', ' ', '_'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault(KnownExtensions.Contains);
                        if (ext != null)
                            return "." + (ext.FastEquals("jpeg") ? "jpg" : ext.FastToLower());
                    }
                }
                var c = (n.TryGetProperty("class_type", out var ct) && (ct.ValueKind == JsonValueKind.String) ? ct.GetString() : "").FastToLower();
                if (c.Contains("video"))
                    return c.Contains("webm") ? ".webm" : ".mp4";
                if (c.Contains("audio"))
                    return c.Contains("mp3") ? ".mp3" : c.Contains("opus") ? ".opus" : c.Contains("wav") ? ".wav" : ".flac";
                if (c.Contains("webp"))
                    return ".webp";
                if (c.Contains("text") || c.Contains("string"))
                    return ".txt";
                if (c.Contains("image") || c.Contains("png"))
                    return ".png";
            }
            return ".bin";
        }

        #endregion//Outputs
    }

}
