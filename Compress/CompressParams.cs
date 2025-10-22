

using SysWeaver.Compression;
using System;
using System.Collections.Generic;

namespace SysWeaver
{
    sealed class CompressParams
    {
        /// <summary>
        /// If true, only the best compression method is saved
        /// </summary>
        public bool OnlyBest = false;


        /// <summary>
        /// Skip a file if the compressed size is greater or equal to the original
        /// </summary>
        public bool OnlyBetter = false;

        /// <summary>
        /// If true, image files such as png and jpeg may be re-compressed, trying to make smaller
        /// </summary>
        public bool RecompressImages = true;

        /// <summary>
        /// If true, the original file is copied to the destination
        /// </summary>
        public bool CopyOriginal = false;

        /// <summary>
        /// The compression methods to use (br, zgip, zstd, deflate), or use "org" to copy the original (or the type compressed version)
        /// </summary>
        public String Methods = "br,gzip,zstd";

        /// <summary>
        /// If true, .js files will be minimized
        /// </summary>
        public bool OptimizeWebFiles = true;

        /// <summary>
        /// If true, .glsl files will be optimized
        /// </summary>
        public bool OptimizeShaders = true;

        /// <summary>
        /// If true, shader variables will be removed
        /// </summary>
        public bool RemoveShaderVars = false;

        internal ICompType[] CompTypes;

        internal List<Tuple<bool, Dictionary<String, CompressProgram.TypeCompressor>>> TypeCompressors;


        public String CacheKey => String.Concat(
            RecompressImages ? 'i' : '_',
            OptimizeWebFiles ? 'i' : '_',
            OptimizeShaders ? 'i' : '_',
            RemoveShaderVars ? 'i' : '_',
            "");
    }

}


