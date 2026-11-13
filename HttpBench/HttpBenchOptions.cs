using System;

namespace SysWeaver.HttpBench
{
    /// <summary>
    /// The command line options of the HttpBench console tool, they match the settings of the HTTP bench page.
    /// Every option is optional: an option that is not given keeps the value of the parameter file (-File), else the default.
    /// </summary>
    public sealed class HttpBenchOptions
    {
        /// <summary>
        /// A parameter file (json with "DataType": "HttpBench"), as exported from the HTTP bench page or written with -Save.
        /// It is loaded first, the other options override its values.
        /// </summary>
        public String File;

        /// <summary>
        /// The root url of the SysWeaver server to test, ex: https://myserver:1212/ (required, here or in the parameter file).
        /// </summary>
        public String Server;

        /// <summary>
        /// The duration of the test in seconds (default 60).
        /// </summary>
        public int? Duration;

        /// <summary>
        /// The number of workers (default 16): clients that each send one request at a time, with their own connection and session.
        /// </summary>
        public int? Workers;

        /// <summary>
        /// The end points to call, a ';' (or ',') separated list of:
        /// All, Default (the modules that are on by default), None, a module ("Files", "Images", "Application", "Auth", "Types", "Explore", "Errors"),
        /// an end point ("Application.GetMenu" or just "GetMenu").
        /// Prefix an item with '!' to remove it, a list that starts with a removal starts from the current selection.
        /// Ex: "All;!Errors", "Files;Application.GetMenu", "!Explore". Default: the end points of the parameter file, else Default. See -List.
        /// </summary>
        public String Apis;

        /// <summary>
        /// How the apis that take a value get it: POST (the json body, default) or GET (the query string).
        /// </summary>
        public String Values;

        /// <summary>
        /// The compression of the api values: None (default), br, gzip, deflate (zstd when available).
        /// POST: the Content-Encoding of the body. GET: the "_" + letter + base64url query string payload.
        /// </summary>
        public String ValueCompression;

        /// <summary>
        /// The accepted response compressions (the Accept-Encoding header): All (default, like a browser), None, or one of br, gzip, deflate (zstd).
        /// </summary>
        public String AcceptEncoding;

        /// <summary>
        /// Accept any https certificate: true (default, self signed, LAN and expired certificates of test servers) or false (validate certificates).
        /// </summary>
        public bool? AnyCertificate;

        /// <summary>
        /// The time out of one request in seconds (default 30), a request that takes longer is counted as an error.
        /// </summary>
        public int? Timeout;

        /// <summary>
        /// The time in seconds that the requests in flight at the end of the test may take to complete (default 5).
        /// </summary>
        public int? Drain;

        /// <summary>
        /// Wait at most this many seconds for the server to answer "serverTime" before the test starts (default 30, 0 = don't wait).
        /// </summary>
        public int? WaitForServer;

        /// <summary>
        /// Seconds to wait after the server answered, when it didn't answer at once (it was starting, default 10).
        /// </summary>
        public int? StartDelay;

        /// <summary>
        /// Warm up before the test: true (default) or false.
        /// The warm up calls the same end points (-WarmUpSeconds), then the server idles while "serverTime" is called every second (-WarmUpPause),
        /// so it can finish its optimizations (tiered compilation) and caches while it stays active. The warm up is not in the report.
        /// </summary>
        public bool? WarmUp;

        /// <summary>
        /// The duration of the warm up in seconds (default 10).
        /// </summary>
        public int? WarmUpSeconds;

        /// <summary>
        /// The pause between the warm up and the test in seconds (default 20), "serverTime" is called every second.
        /// </summary>
        public int? WarmUpPause;

        /// <summary>
        /// Also write the results (the totals, the parameters and the report name) as json to this file, for scripts.
        /// </summary>
        public String Json;

        /// <summary>
        /// Also save the parameters (after the overrides) to this parameter file, it can be imported on the HTTP bench page.
        /// </summary>
        public String Save;

        /// <summary>
        /// Don't run a test: only show the parameters (and save them with -Save).
        /// </summary>
        public bool NoRun;

        /// <summary>
        /// List the modules and end points (the names to use with -Apis) and exit.
        /// </summary>
        public bool List;
    }
}
