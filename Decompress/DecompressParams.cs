using System;
using System.Collections.Generic;

namespace SysWeaver
{
    sealed class DecompressParams
    {
        /// <summary>
        /// If true, existing destination files are overwritten, else they are skipped
        /// </summary>
        public bool Overwrite = true;

        /// <summary>
        /// If true, only one compressed version of a file is decompressed (ex: if both "a.txt.br" and "a.txt.zstd" exists, only one of them is decompressed, using the Prefer order)
        /// </summary>
        public bool Once = false;

        /// <summary>
        /// The order of preference when using Once (http codes like zstd, br, gzip, deflate), any other supported compression method is less preferred
        /// </summary>
        public String Prefer = "zstd,br,gzip,deflate";

        /// <summary>
        /// All supported file extensions (with a "." prefix) in order of preference
        /// </summary>
        internal IReadOnlyList<String> ExtOrder;
    }

}

