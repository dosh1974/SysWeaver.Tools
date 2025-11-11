using System;

namespace FolderSync
{


    public sealed class Params
    {

        /// <summary>
        /// Filename, if specified the user and password is read from the file (should be single line of text in the user:key format)
        /// </summary>
        public String CredFile { get; set; }

        /// <summary>
        /// Username or key
        /// </summary>
        public String User { get; set; }

        /// <summary>
        /// Password
        /// </summary>
        public String Password { get; set; }

        /// <summary>
        /// If true, any bad server certificates are accepted.. NOT RECOMMENDED!
        /// </summary>
        public bool IgnoreCertErrors { get; set; }

        /// <summary>
        /// If true, the files will be synched to a folder that is not used immediately, must switch folders manually on the server.
        /// </summary>
        public bool NoSwitch { get; set; }

        /// <summary>
        /// One or more (separated by a ;) wild card patterns of files to ignore
        /// </summary>
        public String Ignore { get; set; }

        /// <summary>
        /// An optional comment
        /// </summary>
        public String Comment { get; set; }

        /// <summary>
        /// If true, do NOT use content dependent chunking
        /// </summary>
        public bool NoCdc { get; set; }

    }
}
