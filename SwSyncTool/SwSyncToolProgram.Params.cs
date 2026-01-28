using System;
using SysWeaver;

namespace SwSyncTool
{
    static partial class SwSyncToolProgram
    {
        
        #pragma warning disable CS0649

        sealed class SyncToolParmas : CredentialParams
        {
            /// <summary>
            /// [Sync]
            /// Optional comment to use when synching to a remote repository.
            /// </summary>
            public String Comment;

            /// <summary>
            /// [Sync]
            /// One or more (separated by a ;) wild card patterns of files to ignore ("*.pdb" for instance).
            /// </summary>
            public String Ignore;

            /// <summary>
            /// [Sync]
            /// If true, and server cert errors are ignored. NOT reccomended!
            /// </summary>
            public bool IgnoreCertErrors;

            /// <summary>
            /// [Sync]
            /// The maximum concurrency to use, zero or negative is based on the number of hardware threads.
            /// </summary>
            public int MaxConcurrency = -1;

            /// <summary>
            /// [Sync]
            /// If true, the synched folder won't be activated.
            /// </summary>
            public bool NoActivate;

            /// <summary>
            /// [Sync]
            /// If true, Content Dependency Chunking optimizations won't be used.
            /// </summary>
            public bool NoCdc;

            /// <summary>
            /// [Compact, Expand and Recover] 
            /// Optional output directory.
            /// </summary>
            public String OutputDir;

            /// <summary>
            /// (Advanced) One or more folders (separated by a semi colon ';'), that will be used for chunk storage.
            /// </summary>
            public String Folders;

            /// <summary>
            /// (Advanced) Optionally specify a chunk size, must be a power of two.
            /// </summary>
            public int ChunkSize;

        }

        #pragma warning restore CS0649
        
    }



}
