using System;
using SysWeaver.AI;

namespace SysWeaver
{

    public sealed class ComfyApiToCsParams
    {
        /// <summary>
        /// The namespace of the generated types, empty for no namespace
        /// </summary>
        public String Namespace = "SysWeaver.Ai.ComfyUi";

        /// <summary>
        /// Use a file scoped namespace ("namespace X;"), false for a namespace block
        /// </summary>
        public bool FileScoped = true;

        /// <summary>
        /// The base name of the types, if empty the cleaned up file name is used (ex: "AUK_Text_2_speech.json" => "AukText2Speech").
        /// Only useful when converting a single file.
        /// </summary>
        public String TypeName;

        /// <summary>
        /// Appended to the base name for the input type
        /// </summary>
        public String InputSuffix = "Input";

        /// <summary>
        /// Appended to the base name for the output type
        /// </summary>
        public String OutputSuffix = "Output";

        /// <summary>
        /// Generate properties ({ get; set; }), false for fields
        /// </summary>
        public bool Properties = true;

        /// <summary>
        /// Initialize the input members with the workflow defaults, false leaves them null (not sent, so the workflow default is used)
        /// </summary>
        public bool Defaults = true;

        /// <summary>
        /// Generate nested types for inputs with an object value (named the base name + the member name), false uses JsonElement?
        /// </summary>
        public bool Nested = true;

        /// <summary>
        /// Generate xml documentation comments (the node, the input and the workflow default)
        /// </summary>
        public bool Comments = true;

        /// <summary>
        /// Use the shortest member names that map unambiguously (ex: "Megapixels"), false includes the tag (ex: "ResolutionMegapixels")
        /// </summary>
        public bool ShortNames = true;

        /// <summary>
        /// Map all members explicitly with [ComfyUiName("tag.input")], else only members that can't be mapped by name
        /// </summary>
        public bool Explicit;

        /// <summary>
        /// Generate a bool? member for each annotated node, setting it to false bypasses the node(s)
        /// </summary>
        public bool Bypass;

        /// <summary>
        /// How the member names are formatted: Pascal, Camel, Snake or Original
        /// </summary>
        public ComfyUiNameCasing Casing = ComfyUiNameCasing.Pascal;

        /// <summary>
        /// The type of inputs that looks like a media file name (ex: the image of a "Load Image" node): ComfyUiFile, byte[], Uri or String
        /// </summary>
        public String FileType = "ComfyUiFile";

        /// <summary>
        /// The type of the outputs: byte[] (the first result), byte[][] or List&lt;byte[]&gt; (all results), String, String[] or List&lt;String&gt; (base64)
        /// </summary>
        public String OutputType = "byte[]";

        /// <summary>
        /// Optional folder with UI format workflows (as saved by ComfyUI using "Workflow > Save"), the one with the same name as the API workflow is used to annotate the inputs of subgraphs
        /// </summary>
        public String UiFolder;

        /// <summary>
        /// Overwrite existing .cs files, false skips them
        /// </summary>
        public bool Overwrite = true;
    }


}
