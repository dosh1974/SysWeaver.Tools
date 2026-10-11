using System;
using SysWeaver;
using SysWeaver.Remote;


namespace SwCurl
{

    #pragma warning disable CS0649

    sealed class SwCurlParmas : CredentialParams
    {
        /// <summary>
        /// The auth method to use (if credentials are supplied)
        /// SysWeaver is the most secure.
        /// Bearer token can be used if auth method is HttpAuth and the user name is set to Bearer.
        /// </summary>
        public RemoteAuthMethod AuthMethod = RemoteAuthMethod.SysWeaverLogin;

        /// <summary>
        /// The http method to use
        /// </summary>
        public SwMethods HttpMethod = SwMethods.Auto;

        /// <summary>
        /// The compression method to use for the payload
        /// </summary>
        public SwCompression Compression = SwCompression.Auto;

        /// <summary>
        /// The mimetype of the payload if it's NOT a file
        /// </summary>
        public String Mime = "application/json";

        /// <summary>
        /// The text encoding to use 
        /// </summary>
        public String Encoding = "utf-8";

        /// <summary>
        /// The mimetype of the payload if it's a file.
        /// If null, the mime is derived from the file extension.
        /// </summary>
        public String FileMime = null;

        /// <summary>
        /// An optional filename where the result will be saved.
        /// If the response has an error status, the status line is saved, ex: "&lt;404&gt; - Not found", followed by a new line and the response body (if any).
        /// </summary>
        public String Save = null;

        /// <summary>
        /// When logging in useing SysWeaver, the "root" of the service is required, by default this is the root of the supplied end point.
        /// In some cases a services may listen to a prefix that is not at the root.
        /// This parameters then contains the root (added to the root of the supplied end point).
        /// </summary>
        public String SwRoot = null;

        /// <summary>
        /// If true, then server cert errors are ignored. NOT reccomended!
        /// </summary>
        public bool IgnoreCertErrors = false;

        /// <summary>
        /// If true, the supplied payload will never be interpreted as a filename
        /// </summary>
        public bool NoFile = false;

        /// <summary>
        /// If true, use the tor network to make the request (anonymize your IP).
        /// This is MUCH slower.
        /// </summary>
        public bool UseTor = false;

        /// <summary>
        /// Request time out in milliseconds
        /// </summary>
        public int Timeout = 5 * 60000;

        /// <summary>
        /// If true, no console message will be displayed.
        /// The HTTP status of a failed request is then only available in the Save file (the exit code is 3 for a 4xx, 4 for a 5xx, 5 for other statuses).
        /// </summary>
        public bool Silent = false;

        /// <summary>
        /// If true, dump the response to the console
        /// </summary>
        public bool Dump = false;

        /// <summary>
        /// Optional user agent string to use.
        /// </summary>
        public String UserAgent;

        /// <summary>
        /// Force binary dump of text data
        /// </summary>
        public bool Binary = false;

        /// <summary>
        /// Number of spaces to use for each tab
        /// </summary>
        public int TabSize = 3;

        /// <summary>
        /// Number of bytes per row in the binary dump
        /// </summary>
        public int BinaryWidth = 32;

        /// <summary>
        /// If the post data is less than this, use a get request if possible (SysWeaver only)
        /// </summary>
        public int GetThreshold = 1024;
    }

    #pragma warning restore CS0649


}
