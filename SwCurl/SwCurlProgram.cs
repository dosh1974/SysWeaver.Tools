using System;
using SysWeaver;
using System.Threading.Tasks;
using SysWeaver.Remote;
using System.Net.Http;
using System.IO;
using SysWeaver.Compression;
using System.Text;
using System.Collections.Generic;
using System.Net.Http.Headers;


namespace SwCurl
{

    static partial class SwCurlProgram
    {

        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.
        //  The HTTP status code is written to the console (unless -Silent) and to the -Save file.

        /// <summary>
        /// Exit code: success, a 2xx HTTP status (also returned when help was requested).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error (an exception, ex: failed to read the payload file or to write the -Save file).
        /// </summary>
        const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line arguments.
        /// </summary>
        const int ExitInvalidArguments = 2;
        /// <summary>
        /// Exit code: the server responded with a 4xx HTTP status (client error).
        /// </summary>
        const int ExitHttpClientError = 3;
        /// <summary>
        /// Exit code: the server responded with a 5xx HTTP status (server error).
        /// </summary>
        const int ExitHttpServerError = 4;
        /// <summary>
        /// Exit code: the server responded with some other non successful HTTP status (1xx or 3xx).
        /// </summary>
        const int ExitHttpOtherStatus = 5;
        /// <summary>
        /// Exit code: the request failed without a response (connection refused, name resolution, TLS errors etc).
        /// </summary>
        const int ExitRequestFailed = 6;
        /// <summary>
        /// Exit code: the request timed out.
        /// </summary>
        const int ExitTimeout = 7;

        static readonly String[] ExitCodeHelp =
        [
            "Exit codes:",
            "  0  Success (a 2xx status), or help requested",
            "  1  Generic error",
            "  2  Invalid command line",
            "  3  HTTP 4xx status (client error)",
            "  4  HTTP 5xx status (server error)",
            "  5  Other non successful HTTP status (1xx, 3xx)",
            "  6  Request failed (connection, name resolution, TLS etc)",
            "  7  Request timed out",
        ];

        static int GetExitCode(int httpStatus)
        {
            if ((httpStatus >= 400) && (httpStatus < 500))
                return ExitHttpClientError;
            if ((httpStatus >= 500) && (httpStatus < 600))
                return ExitHttpServerError;
            return ExitHttpOtherStatus;
        }

        static int GetExitCode(Exception ex)
        {
            if ((ex is TimeoutException) || ((ex is TaskCanceledException) && (ex.InnerException is TimeoutException)))
                return ExitTimeout;
            if (ex is HttpRequestException)
                return ExitRequestFailed;
            return ExitGenericError;
        }

        static int Usage(String err = null, int retCode = ExitSuccess)
        {
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(err);
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            foreach (var t in CommandLine.SyntaxObject<SwCurlParmas>(Args, CommandLine.OptionMembers.All))
                Console.WriteLine(t);
            foreach (var t in ExitCodeHelp)
                Console.WriteLine(t);
            Console.ResetColor();
            return retCode;
        }


        static readonly CommandLineArgument[] Args = [
                CommandLineArgument.Make<String>("endpoint", false, null, "The API end point, ex: http://localhost/Test/api"),
                CommandLineArgument.Make<String>("data", true, null, "The data to send. If it's a valid filename, data is read from the file, else the payload argument string is sent as UTF8"),
            ];

        static readonly ICompEncoder[] Comps =
            [
                null,
                null,
                CompBrotliNETNew.Instance,
                CompDeflateNET.Instance,
                CompGZipNET.Instance
            ];

        static readonly String[] CompGetPrefixes =
        [
                "?_u",
                "?_u",
                "?_b",
                "?_d",
                "?_g",
        ];



        static ConsoleColor GetColor(Char c)
        {
            if ((c >= 'a') && (c <= 'z'))
                return ConsoleColor.Gray;
            if ((c >= 'A') && (c <= 'Z'))
                return ConsoleColor.Gray;
            if ((c >= '0') && (c <= '9'))
                return ConsoleColor.DarkCyan;
            if (ColMap.TryGetValue(c, out var cc))
                return cc;
            if ((c < ' ') || Char.IsWhiteSpace(c))
                return ConsoleColor.DarkRed;
            return ConsoleColor.DarkGray;
        }

        const ConsoleColor Math = ConsoleColor.DarkCyan;
        const ConsoleColor Code = ConsoleColor.DarkMagenta;
        const ConsoleColor Text = ConsoleColor.DarkGreen;
        const ConsoleColor Special = ConsoleColor.Blue;

        static readonly IReadOnlyDictionary<Char, ConsoleColor> ColMap = new Dictionary<Char, ConsoleColor>
        {
            {  '+',  Math },
            {  '-',  Math },
            {  '*',  Math },
            {  '/',  Math },
            {  '%',  Math },
            {  '=',  Math },
            {  '<',  Math },
            {  '>',  Math },

            {  '{',  Code },
            {  '}',  Code },
            {  '[',  Code },
            {  ']',  Code },
            {  '^',  Code },
            {  '|',  Code },
            {  '&',  Code },
            {  '_',  Code },
            {  ';',  Code },
            {  ':',  Code },
            {  '"',  Code },
            {  '\'',  Code },

            {  ' ',  Text },
            {  '.',  Text },
            {  ',',  Text },
            {  '!',  Text },
            {  '?',  Text },

            {  '@',  Special },
            {  '£',  Special },
            {  '€',  Special },
            {  '¤',  Special },
            {  '´',  Special },
            {  '`',  Special },
            {  '\\',  Special },
        }.Freeze();


        static readonly IReadOnlyDictionary<Char, Action<int>> Keep = new Dictionary<Char, Action<int>>
        {
            { '\n', x => Console.WriteLine() },
            { '\t', x => {
                var old = Console.GetCursorPosition().Left;
                var n = old + x;
                n /= x;
                n *= x;
                Console.Write(new String(' ', n - old));
            }},
        }.Freeze();


        static void WriteColoredHex(Byte b)
        {
            Console.ForegroundColor = GetColor((Char)b);
            var h = HexChars;
            Console.Write(h[b >> 4]);
            Console.Write(h[b & 0xf]);
            Console.Write(' ');
        }

        static readonly Char[] HexChars = "0123456789ABCDEF".ToCharArray();

        static void WriteColored(Char c)
        {
            var cc = GetColor(c);
            Console.ForegroundColor = cc;
            if (cc == ConsoleColor.DarkRed)
                c = '·';
            Console.Write(c);
        }


        static async Task<int> Main(string[] argsC)
        {
            CommandLine cmd;
            SwCurlParmas p;
            try
            {
                cmd = CommandLine.ParseObject<SwCurlParmas>(out p, argsC, Args, CommandLine.OptionMembers.All);
                if (cmd == null)
                    return Usage();
            }
            catch (Exception ex)
            {
                return Usage(ex.Message, ExitInvalidArguments);
            }
            var args = cmd.Arguments;
            var al = args.Length;
            if (al <= 0)
                return Usage(null, ExitInvalidArguments);
            Encoding textEncoding;
            try
            {
                textEncoding = Encoding.GetEncoding(p.Encoding);
            }
            catch (Exception ex)
            {
                return Usage(ex.Message, ExitInvalidArguments);
            }
            IUnmanagedReadOnlyMemory<Byte> mem = null;
            HttpContent content = null;
            try
            {
                var consoleData = p.Dump || p.Binary;
                var consoleVerbose = !p.Silent;
                String saveTo = null;
                if (p.Save != null)
                {
                    saveTo = Path.GetFullPath(p.Save);
                    await PathExt.EnsureCanWriteFileAsync(saveTo);
                }
                var api = args[0].Item2 as String;
                var payload = al > 1 ? (args[1].Item2 as String) : null;
                String get = "";
                var httpMethod = p.HttpMethod;
                if (String.IsNullOrEmpty(payload))
                {
                    if (httpMethod == SwMethods.Auto)
                        httpMethod = SwMethods.GET;
                }
                else { 
                    if (httpMethod == SwMethods.GET)
                        if (p.AuthMethod != RemoteAuthMethod.SysWeaverLogin)
                            return Usage("Can't have a payload with the GET method!", ExitInvalidArguments);
                    if (httpMethod == SwMethods.Auto)
                        if (p.AuthMethod != RemoteAuthMethod.SysWeaverLogin)
                            httpMethod = SwMethods.POST;

                    String mt = null;
                    if ((!p.NoFile) && PathExt.IsValidPathToFile(payload, true))
                    {
                        mt = p.FileMime;
                        if (String.IsNullOrEmpty(p.FileMime))
                            mt = MimeTypeMap.GetMimeType(Path.GetExtension(payload), false).Item1;
                        mem = await FileReadOnlyMemory.ReadAsync(payload);
                    }else
                    {
                        mt = p.Mime;
                        mem = UnmanagedMemory.Create(textEncoding.GetBytes(payload).AsMemory());
                    }
                    //  Add charset
                    if (mt.IndexOf("charset") < 0)
                    {
                        if (MimeTypeMap.GetMimeType(mt, false).Item1.FastEquals(MimeTypeMap.GetMimeType(mt, true).Item1))
                            mt += "; charset=" + textEncoding.WebName;
                    }
                    //  Compress
                    var comp = p.Compression;
                    if (comp == SwCompression.Auto)
                    {
                        comp = SwCompression.None;
                        if (p.AuthMethod == RemoteAuthMethod.SysWeaverLogin)
                            if (MimeTypeMap.GetMimeType(mt.SplitFirst(';').Trim()).Item2)
                                comp = SwCompression.Brotli;
                    }
                    var getThreshold = System.Math.Max(0, p.GetThreshold);
                    var compT = Comps[(int)comp];
                    if (compT != null)
                    {
                        var cm = compT.GetCompressed(mem.Memory.Span, CompEncoderLevels.Best);
                        if (cm.Length < mem.Memory.Length)
                        {
                            //  Compressed
                            //  GET or other?
                            if (httpMethod == SwMethods.Auto)
                                httpMethod = cm.Length < getThreshold ? SwMethods.GET : SwMethods.POST;
                            if (httpMethod == SwMethods.GET)
                            {
                                get = CompGetPrefixes[(int)comp] + Convert.ToBase64String(cm.Span);
                                mem.Dispose();
                                mem = null;
                            }
                            else
                            {
                                mem.Dispose();
                                mem = null;
                                content = new ReadOnlyMemoryContent(cm);
                                content.Headers.ContentEncoding.Add(compT.HttpCode);
                            }
                        }
                    }
                    if ((get.Length == 0) && (content == null))
                    {
                        //  Uncompressed
                        //  GET or other?
                        if (httpMethod == SwMethods.Auto)
                            httpMethod = mem.Memory.Length < getThreshold ? SwMethods.GET : SwMethods.POST;
                        if (httpMethod == SwMethods.GET)
                        {
                            get = CompGetPrefixes[0] + Convert.ToBase64String(mem.Memory.Span);
                            mem.Dispose();
                            mem = null;
                        }
                        else
                        {
                            content = new ReadOnlyMemoryContent(mem.Memory);
                        }
                    }
                    if (content != null)
                        content.Headers.ContentType = new MediaTypeHeaderValue(mt);
                }
                var root = api.Substring(0, api.IndexOf('/', api.IndexOf("://") + 3) + 1);
                if (!String.IsNullOrEmpty(p.SwRoot))
                    root += (p.SwRoot.Trim('/') + '/');
                api += get;
                var t = new RemoteConnection
                {
                    AuthMethod = p.AuthMethod,
                    User = p.User,
                    Password = p.Password,
                    CredFile = p.CredFile,
                    IgnoreCertErrors = p.IgnoreCertErrors,
                    UseTor = p.UseTor,
                    BaseUrl = root,
                    TimeoutInMilliSeconds = p.Timeout,
                    UserAgent = p.UserAgent,
                };
                var start = DateTime.UtcNow;
                var hm = HttpMethod.Parse(httpMethod.ToString());
                if (consoleVerbose)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("Request: ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(hm.Method);
                    Console.Write(' ');
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(api);
                    Console.ResetColor();
                    Console.WriteLine();
                    if (content != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("Content-Type: ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write(content.Headers.ContentType);
                        var enc = content.Headers.ContentEncoding.ToString();
                        if (!String.IsNullOrEmpty(enc))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(", Content-Encoding: ");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write(enc);
                        }
                        var l = content.Headers.ContentLength ?? -1;
                        if (l >= 0)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(", Content-Length: ");
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write(l.ToValueString());
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(l == 1 ? " byte" : " bytes");
                        }
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                }
                using var msg = new HttpRequestMessage(hm, api);
                if (content != null)
                    msg.Content = content;
                using var connection = t.Create<IDummy>();
                var client = (connection as RemoteConnectionBase).Client;
                var res = await client.SendAsync(msg);
                double took = (DateTime.UtcNow - start).TotalMilliseconds;
                var rc = res.Content;
                var resData = String.Concat('<', (int)res.StatusCode, "> - ", res.StatusCode.ToString().RemoveCamelCase());
                if (res.IsSuccessStatusCode)
                {
                    Byte[] data = null;
                    if (rc == null)
                    {
                        if (saveTo != null)
                            await File.WriteAllTextAsync(saveTo, resData);
                    }else
                    {
                        data = await rc.ReadAsByteArrayAsync();
                        took = (DateTime.UtcNow - start).TotalMilliseconds;
                        if (saveTo != null)
                            await File.WriteAllBytesAsync(saveTo, data);
                    }
                    //  Dump response info
                    if (consoleVerbose)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("Response: ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(resData);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("Took: ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(took.ToValueString(1));
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(" ms");
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                    if (data != null)
                    {
                        var dataLen = data.LongLength;
                        var mime = rc.Headers?.ContentType?.ToString();
                        if (consoleVerbose)
                        {
                            if (!String.IsNullOrEmpty(mime))
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Write("Content-Type: ");
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.Write(mime);
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Write(", ");
                            }
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("Content-Length: ");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write(dataLen.ToValueString());
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(dataLen == 1 ? " byte" : " bytes");
                            Console.ResetColor();
                            Console.WriteLine();
                        }
                        //  Dump data
                        if (consoleData)
                        {
                            var charSet = rc.Headers?.ContentType?.CharSet;
                            if (charSet == null)
                            {
                                if (mime.FastStartsWith("text/"))
                                    charSet = "utf-8";
                                if (charSet == null)
                                {
                                    if (mime.FastStartsWith("application/"))
                                    {
                                        if (MimeTypeMap.GetMimeType(mime).Item2)
                                            charSet = "utf-8";
                                    }
                                }
                            }
                            if ((charSet != null) && (!p.Binary))
                            {
                                //  Text
                                var keep = Keep;
                                var enc = Encoding.GetEncoding(charSet);
                                var tab = System.Math.Max(1, p.TabSize);
                                var str = enc.GetString(data);
                                foreach (var c in str)
                                {
                                    if (keep.TryGetValue(c, out var a))
                                        a(tab);
                                    else
                                        WriteColored(c);
                                }
                                Console.ResetColor();
                                Console.WriteLine();
                            }
                            else
                            {
                                var maxL = dataLen.ToValueString().Length;
                                var maxHL = ((dataLen.ToString("x").Length + 1) >> 1) << 1;
                                var bw = System.Math.Max(1, p.BinaryWidth);
                                //  Binary
                                for (long i = 0; i < dataLen; i += bw)
                                {
                                    var count = dataLen - i;
                                    if (count > bw)
                                        count = bw;
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.Write(i.ToValueString().PadLeft(maxL));
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.Write(" | ");
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.Write("0x");
                                    Console.Write(i.ToString("x").PadLeft(maxL, '0'));
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.Write(" | ");
                                    for (int j = 0; j < count; ++j)
                                        WriteColoredHex(data[i + j]);
                                    if (count < bw)
                                        Console.Write(new String(' ', (bw - (int)count) * 3));
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.Write("| ");
                                    for (int j = 0; j < count; ++j)
                                        WriteColored((Char)data[i + j]);
                                    Console.WriteLine();
                                }
                                Console.ResetColor();
                                Console.WriteLine();
                            }
                        }
                    }
                }
                else
                {
                    if (rc != null)
                    {
                        try
                        {
                            var str = await rc.ReadAsStringAsync();
                            took = (DateTime.UtcNow - start).TotalMilliseconds;
                            if (!String.IsNullOrEmpty(str))
                                resData = String.Concat(resData, Environment.NewLine, str);
                        }
                        catch (Exception ex)
                        {
                            resData = String.Concat(resData, Environment.NewLine, "Failed to read result data as a string:", Environment.NewLine, ex);
                        }
                    }
                    if (saveTo != null)
                        await File.WriteAllTextAsync(saveTo, resData);
                    if (consoleVerbose)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(resData);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("Took: ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(took.ToValueString(1));
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(" ms");
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                    return GetExitCode((int)res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                return Usage(ex.Message, GetExitCode(ex));
            }
            finally
            {
                content?.Dispose();
                mem?.Dispose();
            }
            return ExitSuccess;
        }

    }



}
