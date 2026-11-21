using System;
using SysWeaver.Minifier;

namespace SysWeaver
{

    /// <summary>
    /// The command line options of SvgOpt: all SvgMinifierParams members plus Compare (parsed by SysWeaver.CommandLine FilesToFolderTool,
    /// the XML docs of the fields are the help text).
    /// </summary>
    public sealed class SvgOptParams : SvgMinifierParams
    {

        /// <summary>
        /// Supply a filename to create a .html file that can be used to compare the original and the modified files.
        /// ".html" is appended unless the name ends with ".htm" or ".html", a name without a folder is created in the output folder.
        /// No file is created if no files were processed.
        /// </summary>
        public String Compare;

        //  The cache key is SvgMinifierParams.CacheKey (starts with SvgMinifier.Version, shared with SvgTransformer)
    }


}
