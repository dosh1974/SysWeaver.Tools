using System;
using SysWeaver.Converter.Js;

namespace SysWeaver
{
    /// <summary>
    /// The command line options of JsConvert (all converter options plus the tool options)
    /// </summary>
    public sealed class JsConvertParams : JsConverterParams
    {
        /// <summary>
        /// Text to add to the output file names (before the extension), ex: ".es5" writes "common.es5.js".
        /// </summary>
        public String Suffix;

        /// <summary>
        /// Allow the output to overwrite the source file (when there is no destination folder and no suffix).
        /// </summary>
        public bool Overwrite;

        /// <summary>
        /// Write all helper functions (and polyfills) used by the converted files to this file instead of adding them to every file (sets Helpers to External).
        /// The file must be loaded (ex: with a script element) before the converted files. A name without a folder is created in the destination folder.
        /// </summary>
        public String HelpersFile;

        /// <summary>
        /// Also show information messages (ex: built-ins that might be missing in the target).
        /// </summary>
        public bool Verbose;

        /// <summary>
        /// Show the target (EcmaScript version and browsers) and the features that will be transformed.
        /// </summary>
        public bool ShowTarget;

        /// <summary>
        /// Always write the output files (by default a file is only written if its content changed, so the time stamp of unchanged files is kept).
        /// </summary>
        public bool Force;

        /// <summary>
        /// Html files: clone the site, i.e. also process all scripts, style sheets (and other files, see SiteAssets) the pages use, including the files those reference
        /// (css @import / url(), script imports, workers and literal script / css paths). Scripts are converted, other files are copied, the paths relative to the site root are kept.
        /// Dependencies created dynamically with computed urls can't be found, iframes and links to other pages are not followed.
        /// If false, only the html file itself is converted.
        /// </summary>
        public bool Site = true;

        /// <summary>
        /// The root folder of the site(s): root relative urls ("/js/a.js") are resolved from here and the output keeps the paths relative to it.
        /// Default: root relative urls are searched in the folder of the page and its parents, the root is the common folder of all used files.
        /// </summary>
        public String SiteRoot;

        /// <summary>
        /// Site mode: also copy the other files used by the pages and style sheets (images, fonts, icons, manifests ...), not only scripts and style sheets.
        /// </summary>
        public bool SiteAssets = true;

        /// <summary>
        /// Site mode: also follow string literals in scripts that are relative paths of existing .js / .mjs / .css files (ex: includeJs("lib.js")).
        /// </summary>
        public bool SiteScanStrings = true;
    }
}
