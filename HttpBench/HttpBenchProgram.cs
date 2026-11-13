using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SysWeaver.HttpBench
{
    /// <summary>
    /// HttpBench: performance tests a SysWeaver web server from the command line and writes a markdown report.
    /// Syntax: HttpBench [options] [report_name]
    /// </summary>
    static class HttpBenchProgram
    {
        const int ExitOk = 0;
        const int ExitFailed = 1;
        const int ExitInvalidArguments = 2;
        const int ExitStopped = 3;
        const int ExitNoServer = 4;

        static readonly CommandLineArgument[] Arguments =
        [
            CommandLineArgument.Make<String>("report_name", true, null, "The file name of the markdown report, default \"Report_[server].md\" (ex: Report_localhost_1212.md)"),
        ];

        static int Main(String[] args)
        {
            HttpBenchOptions o;
            String reportName;
            try
            {
                var cl = CommandLine.ParseObject(out o, args, Arguments, new HttpBenchOptions(), CommandLine.OptionMembers.Fields);
                if (cl == null)
                {
                    WriteHelp();
                    return ExitOk;
                }
                reportName = cl.Arguments.FirstOrDefault()?.Item2 as String;
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                Console.WriteLine("Use -? for help.");
                return ExitInvalidArguments;
            }
            if (o.List)
            {
                WriteCatalog();
                return ExitOk;
            }
            BenchStartRequest settings;
            BenchLimits limits;
            BenchSetup setup;
            try
            {
                settings = GetSettings(o);
                limits = new BenchLimits
                {
                    RequestTimeoutSeconds = o.Timeout ?? 30,
                    DrainSeconds = o.Drain ?? 5,
                };
                if (limits.RequestTimeoutSeconds < 1)
                    throw new ArgumentException("The time out must be at least 1 second");
                if (limits.DrainSeconds < 0)
                    throw new ArgumentException("The drain time can't be negative");
                if ((o.WaitForServer < 0) || (o.StartDelay < 0) || (o.WarmUpSeconds < 0) || (o.WarmUpPause < 0))
                    throw new ArgumentException("-WaitForServer, -StartDelay, -WarmUpSeconds and -WarmUpPause can't be negative");
                setup = BenchSetup.Create(settings, limits);
                if (String.IsNullOrWhiteSpace(reportName))
                    reportName = DefaultReportName(setup.Target);
            }
            catch (Exception ex) when ((ex is ArgumentException) || (ex is InvalidDataException) || (ex is IOException) || (ex is UnauthorizedAccessException))
            {
                Error(ex.Message);
                return ExitInvalidArguments;
            }
            WriteParameters(setup.Settings, reportName, o);
            if (!String.IsNullOrEmpty(o.Save))
            {
                try
                {
                    BenchSettingsFile.Save(o.Save, setup.Settings);
                    Console.WriteLine("Parameters saved to " + o.Save);
                }
                catch (Exception ex)
                {
                    Error("Can't save the parameters to " + o.Save + ": " + ex.Message);
                    return ExitFailed;
                }
            }
            if (o.NoRun)
                return ExitOk;
            return Run(o, setup, limits, reportName).GetAwaiter().GetResult();
        }

        #region Settings

        /// <summary>
        /// The settings: the defaults, then the parameter file, then the options that were given.
        /// </summary>
        static BenchStartRequest GetSettings(HttpBenchOptions o)
        {
            var s = String.IsNullOrEmpty(o.File) ? new BenchStartRequest() : LoadFile(o.File);
            if (o.Server != null)
                s.Target = o.Server.Trim();
            if (String.IsNullOrWhiteSpace(s.Target))
                throw new ArgumentException("No server to test: use -Server <url> (or a parameter file with a Target, -File)");
            if (o.Duration.HasValue)
                s.DurationSeconds = o.Duration.Value;
            if (o.Workers.HasValue)
                s.Concurrency = o.Workers.Value;
            if (o.Apis != null)
                s.EndPoints = SelectEndPoints(o.Apis, s.EndPoints ?? BenchSetup.DefaultEndPoints());
            if (o.Values != null)
            {
                s.SendAsGet = o.Values.Trim().ToUpperInvariant() switch
                {
                    "POST" => false,
                    "GET" => true,
                    _ => throw new ArgumentException("-Values must be POST or GET"),
                };
            }
            if (o.ValueCompression != null)
                s.RequestCompression = Compression(o.ValueCompression, false);
            if (o.AcceptEncoding != null)
                s.ResponseCompression = Compression(o.AcceptEncoding, true);
            if (o.AnyCertificate.HasValue)
                s.IgnoreCertificateErrors = o.AnyCertificate.Value;
            return s;
        }

        static BenchStartRequest LoadFile(String filename)
        {
            if (!System.IO.File.Exists(filename))
                throw new ArgumentException("The parameter file " + filename.ToQuoted() + " doesn't exist");
            try
            {
                return BenchSettingsFile.Load(filename);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(filename + ": " + ex.Message);
            }
        }

        /// <summary>
        /// A compression option: "none" / "" = none, "all" / "*" = all (Accept-Encoding only), else an http compression code.
        /// </summary>
        static String Compression(String v, bool allowAll)
        {
            v = v.Trim();
            if ((v.Length == 0) || v.Equals("none", StringComparison.OrdinalIgnoreCase))
                return "";
            if (allowAll && ((v == "*") || v.Equals("all", StringComparison.OrdinalIgnoreCase)))
                return "*";
            var codes = BenchSetup.CompressionCodes();
            return codes.FirstOrDefault(x => x.Equals(v, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(String.Concat("Unknown compression ", v.ToQuoted(), ", valid: ", allowAll ? "All, " : "", "None, ", String.Join(", ", codes)));
        }

        /// <summary>
        /// The end points of an -Apis selection (see <see cref="HttpBenchOptions.Apis"/>).
        /// </summary>
        static String[] SelectEndPoints(String apis, String[] current)
        {
            var items = apis.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (items.Length <= 0)
                throw new ArgumentException("-Apis is empty, use -List to see the end points");
            var all = BenchCatalog.Modules.SelectMany(x => x.EndPoints).ToArray();
            //  A list that starts with a removal edits the current selection, else it replaces it
            var first = items[0];
            var sel = new HashSet<String>((first[0] == '!') || (first[0] == '-') ? current : [], StringComparer.Ordinal);
            foreach (var item in items)
            {
                var remove = (item[0] == '!') || (item[0] == '-');
                var name = remove ? item.Substring(1).Trim() : item;
                IEnumerable<String> ids;
                if (name.Equals("All", StringComparison.OrdinalIgnoreCase) || (name == "*"))
                    ids = all.Select(x => x.Id);
                else if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    ids = BenchSetup.DefaultEndPoints();
                else if (name.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    sel.Clear();
                    continue;
                }
                else
                {
                    var m = BenchCatalog.Modules.FirstOrDefault(x => x.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (m != null)
                        ids = m.EndPoints.Select(x => x.Id);
                    else
                    {
                        var e = all.Where(x => x.Id.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (e.Length <= 0)
                            e = all.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (e.Length <= 0)
                            throw new ArgumentException("Unknown module or end point " + name.ToQuoted() + " in -Apis, use -List to see them");
                        if (e.Length > 1)
                            throw new ArgumentException(String.Concat(name.ToQuoted(), " in -Apis is ambiguous, use one of: ", String.Join(", ", e.Select(x => x.Id))));
                        ids = e.Select(x => x.Id);
                    }
                }
                foreach (var id in ids)
                {
                    if (remove)
                        sel.Remove(id);
                    else
                        sel.Add(id);
                }
            }
            //  The catalog order
            return all.Where(x => sel.Contains(x.Id)).Select(x => x.Id).ToArray();
        }

        /// <summary>
        /// "Report_[server].md": the server address without the scheme, the port separator ':' (and anything else a file name can't hold) as '_'.
        /// </summary>
        static String DefaultReportName(Uri target)
        {
            var s = target.ToString();
            var i = s.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0)
                s = s.Substring(i + 3);
            s = s.TrimEnd('/');
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append((c == ':') || (c == '/') || (c == '\\') || invalid.Contains(c) ? '_' : c);
            return "Report_" + sb + ".md";
        }

        #endregion//Settings

        #region Console output

        static void Error(String message)
        {
            var c = ConsoleTools.ForegroundColor;
            ConsoleTools.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Error: " + message);
            ConsoleTools.ForegroundColor = c;
        }

        static void WriteHelp()
        {
            Console.WriteLine("HttpBench - performance test a SysWeaver web server, the results are written as a markdown report.");
            Console.WriteLine();
            foreach (var l in CommandLine.SyntaxObject(Arguments, new HttpBenchOptions(), CommandLine.OptionMembers.Fields, "Use: HttpBench "))
                Console.WriteLine(l);
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  HttpBench -Server https://localhost:1212/");
            Console.WriteLine("  HttpBench -File ServerTest.json");
            Console.WriteLine("  HttpBench -File ServerTest.json -Server https://localhost:1212 LocalTest.md");
            Console.WriteLine("  HttpBench -Server https://myserver/ -Duration 300 -Workers 64 -Apis \"All;!Errors\" -Save MyServer.json");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 completed, 1 failed, 2 invalid arguments, 3 stopped (Ctrl+C, the report of a started test is still written),");
            Console.WriteLine("            4 the server didn't answer within -WaitForServer seconds.");
        }

        static void WriteCatalog()
        {
            Console.WriteLine("Modules and end points (use the module or end point names with -Apis, * = on by default):");
            foreach (var m in BenchCatalog.Modules)
            {
                Console.WriteLine();
                Console.WriteLine(String.Concat(m.Default ? "* " : "  ", m.Id, " - ", m.Name));
                var pad = m.EndPoints.Max(x => x.Id.Length) + 2;
                foreach (var e in m.EndPoints)
                    Console.WriteLine(String.Concat("    ", e.Id.PadRight(pad), e.Method.PadRight(5), e.Path));
            }
            Console.WriteLine();
            Console.WriteLine("Compressions: " + String.Join(", ", BenchSetup.CompressionCodes()));
        }

        static void WriteParameters(BenchStartRequest s, String reportName, HttpBenchOptions o)
        {
            var rows = new List<(String Name, String Value)>();
            if (!String.IsNullOrEmpty(o.File))
                rows.Add(("Parameters", o.File));
            rows.AddRange(BenchMarkDownReport.Parameters(s));
            var (wait, delay, warmUp, warmUpSeconds, pause) = Phases(o);
            rows.Add(("Start", wait > 0 ? String.Concat("wait up to ", wait, " s for the server (then ", delay, " s if it was starting)") : "at once"));
            rows.Add(("Warm up", warmUp ? String.Concat(warmUpSeconds, " s, then a ", pause, " s pause") : "off"));
            rows.Add(("Report", reportName));
            var pad = rows.Max(x => x.Name.Length) + 2;
            var width = ConsoleWidth();
            Console.WriteLine("HttpBench");
            foreach (var (n, v) in rows)
            {
                var line = String.Concat("  ", n.PadRight(pad), v);
                //  Long values (the end points) are cut to one line, the report has all of them
                if (line.Length > width)
                    line = line.Substring(0, Math.Max(10, width - 3)) + "...";
                Console.WriteLine(line);
            }
            Console.WriteLine();
        }

        static int ConsoleWidth()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    var w = Console.WindowWidth;
                    if (w > 20)
                        return w - 1;
                }
            }
            catch
            {
            }
            return 119;
        }

        #endregion//Console output

        #region Running

        /// <summary>
        /// Cancelled by the first Ctrl+C.
        /// </summary>
        static readonly CancellationTokenSource Stopping = new CancellationTokenSource();

        /// <summary>
        /// The run (warm up or test) that a Ctrl+C stops.
        /// </summary>
        static volatile BenchRun Current;

        static int StopCount;

        static void OnCancel(Object _, ConsoleCancelEventArgs e)
        {
            //  The first Ctrl+C stops (the report of a test that started is still written), a second one ends the program
            if (Interlocked.Increment(ref StopCount) != 1)
                return;
            e.Cancel = true;
            Stopping.Cancel();
            Current?.Stop();
        }

        /// <summary>
        /// The phases before the test: the longest wait for the server, the delay after a server that was starting, the warm up and the pause after it.
        /// </summary>
        static (int Wait, int Delay, bool WarmUp, int WarmUpSeconds, int Pause) Phases(HttpBenchOptions o)
        {
            var secs = o.WarmUpSeconds ?? 10;
            return (o.WaitForServer ?? 30, o.StartDelay ?? 10, (o.WarmUp ?? true) && (secs > 0), secs, o.WarmUpPause ?? 20);
        }

        static async Task<int> Run(HttpBenchOptions o, BenchSetup setup, BenchLimits limits, String reportName)
        {
            var (wait, delay, warmUp, warmUpSeconds, pause) = Phases(o);
            var cancel = Stopping.Token;
            Console.CancelKeyPress += OnCancel;
            try
            {
                using (var probe = new BenchServerProbe(setup.Target, setup.Settings.IgnoreCertificateErrors, TimeSpan.FromSeconds(Math.Clamp(limits.RequestTimeoutSeconds, 1, 5))))
                {
                    try
                    {
                        if ((wait > 0) && (!await WaitForServer(probe, wait, delay, cancel).ConfigureAwait(false)))
                            return ExitNoServer;
                        if (warmUp)
                        {
                            var ws = setup.Settings.Clone();
                            ws.DurationSeconds = warmUpSeconds;
                            using (var w = new BenchRun(1, BenchSetup.Create(ws, limits), limits))
                            {
                                await RunWithProgress(w, "Warm up", false).ConfigureAwait(false);
                                cancel.ThrowIfCancellationRequested();
                                var s = w.GetStatus();
                                Console.WriteLine(String.Concat("Warm up: ", BenchMarkDownReport.Num(s.Requests), " requests, ", BenchMarkDownReport.Num(s.Rps, 1),
                                    " requests/s, errors ", BenchMarkDownReport.Num(s.Errors)));
                            }
                            if (pause > 0)
                                await Pause(probe, pause, cancel).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Stopped before the test, no report was written.");
                        return ExitStopped;
                    }
                }
                using var run = new BenchRun(2, setup, limits);
                await RunWithProgress(run, "Test", true).ConfigureAwait(false);
                var st = run.GetStatus();
                WriteSummary(st);
                try
                {
                    WriteFile(reportName, BenchMarkDownReport.Create(run, "HttpBench"));
                    Console.WriteLine("Report: " + Path.GetFullPath(reportName));
                    if (!String.IsNullOrEmpty(o.Json))
                    {
                        WriteFile(o.Json, ResultJson(run, st, reportName, o.Json));
                        Console.WriteLine("Results: " + Path.GetFullPath(o.Json));
                    }
                }
                catch (Exception ex)
                {
                    Error("Can't write the results: " + ex.Message);
                    return ExitFailed;
                }
                return st.State switch
                {
                    BenchStates.Completed => ExitOk,
                    BenchStates.Stopped => ExitStopped,
                    _ => ExitFailed,
                };
            }
            finally
            {
                Console.CancelKeyPress -= OnCancel;
            }
        }

        static void WriteFile(String filename, String text)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filename));
            if (!String.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(filename, text, new UTF8Encoding(false));
        }

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// The results as json: "DataType": "HttpBenchResult", the report (relative to the json file), the parameters and the totals (<see cref="BenchStatus"/>).
        /// </summary>
        static String ResultJson(BenchRun run, BenchStatus s, String reportName, String jsonName)
        {
            var jsonDir = Path.GetDirectoryName(Path.GetFullPath(jsonName)) ?? "";
            var o = new JsonObject
            {
                ["DataType"] = "HttpBenchResult",
                ["Report"] = Path.GetRelativePath(jsonDir, Path.GetFullPath(reportName)).Replace('\\', '/'),
                ["Settings"] = JsonSerializer.SerializeToNode(run.Settings, JsonOptions),
                ["Status"] = JsonSerializer.SerializeToNode(s, JsonOptions),
            };
            return o.ToJsonString(JsonOptions);
        }

        /// <summary>
        /// Wait for the server to answer, then the start delay if it didn't answer at once.
        /// </summary>
        /// <returns>False if it didn't answer in time</returns>
        static async Task<bool> WaitForServer(BenchServerProbe probe, int wait, int delay, CancellationToken cancel)
        {
            var line = new StatusLine();
            var (ok, failures, err) = await probe.WaitAsync(TimeSpan.FromSeconds(wait), cancel,
                (e, secs) => line.Write(String.Concat("Waiting for the server (", Math.Min(wait, Math.Floor(secs)).ToString("0", CultureInfo.InvariantCulture), " of ", wait, " s): ", e))).ConfigureAwait(false);
            if (!ok)
            {
                line.End();
                Error(String.Concat("The server didn't answer ", probe.Uri, " within ", wait, " s: ", err));
                return false;
            }
            if (failures <= 0)
                return true;
            line.Write(String.Concat("The server answered after ", failures, failures == 1 ? " failed request" : " failed requests"));
            line.End();
            //  A server that was starting gets some time to finish starting (background work after the start)
            for (int i = delay; i > 0; --i)
            {
                line.Write(String.Concat("Start delay: ", i, " s left"));
                await Task.Delay(1000, cancel).ConfigureAwait(false);
            }
            if (delay > 0)
            {
                line.Write(String.Concat("Start delay: ", delay, " s"));
                line.End();
            }
            return true;
        }

        /// <summary>
        /// The pause after the warm up: one serverTime request per second, so the server stays active while it finishes its optimizations.
        /// </summary>
        static async Task Pause(BenchServerProbe probe, int seconds, CancellationToken cancel)
        {
            var line = new StatusLine();
            int failed = 0;
            double lastMs = 0;
            for (int i = 0; i < seconds; ++i)
            {
                var r0 = Stopwatch.GetTimestamp();
                var (err, ms) = await probe.PingAsync(cancel).ConfigureAwait(false);
                if (err == null)
                    lastMs = ms;
                else
                    ++failed;
                line.Write(String.Concat("Pause: ", seconds - i, " s left, serverTime ", BenchMarkDownReport.Ms(lastMs), failed > 0 ? String.Concat(", ", failed, " failed") : ""));
                var left = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(r0);
                if (left > TimeSpan.Zero)
                    await Task.Delay(left, cancel).ConfigureAwait(false);
            }
            line.Write(String.Concat("Pause: ", seconds, " s, ", seconds - failed, " of ", seconds, " serverTime requests answered"));
            line.End();
            Console.WriteLine();
        }

        /// <summary>
        /// Run a warm up or a test and show its progress.
        /// </summary>
        static async Task RunWithProgress(BenchRun run, String label, bool blankAfter)
        {
            Current = run;
            var progress = new ProgressMeter(run, label);
            try
            {
                run.Start();
                //  A Ctrl+C between the check of the token and Current being set
                if (Stopping.IsCancellationRequested)
                    run.Stop();
                var done = run.WaitAsync();
                while (!done.IsCompleted)
                {
                    progress.Update(StopCount > 0);
                    await Task.WhenAny(done, Task.Delay(250)).ConfigureAwait(false);
                }
                await done.ConfigureAwait(false);
            }
            finally
            {
                Current = null;
                progress.End(blankAfter);
            }
        }

        static void WriteSummary(BenchStatus s)
        {
            var inv = CultureInfo.InvariantCulture;
            Console.WriteLine(String.Concat("Test ", s.State.ToString().ToLowerInvariant(), " after ", BenchMarkDownReport.Duration(s.ElapsedSeconds),
                String.IsNullOrEmpty(s.Message) ? "" : ": " + s.Message));
            Console.WriteLine(String.Concat("  Requests/s  ", BenchMarkDownReport.Num(s.Rps, 1), " (peak ", BenchMarkDownReport.Num(s.PeakRps), ")"));
            Console.WriteLine(String.Concat("  Latency     avg ", BenchMarkDownReport.Ms(s.AvgMs), ", median ", BenchMarkDownReport.Ms(s.P50Ms),
                ", P95 ", BenchMarkDownReport.Ms(s.P95Ms), ", P99 ", BenchMarkDownReport.Ms(s.P99Ms), ", max ", BenchMarkDownReport.Ms(s.MaxMs)));
            var c = ConsoleTools.ForegroundColor;
            if (s.Errors > 0)
                ConsoleTools.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(String.Concat("  Requests    ", BenchMarkDownReport.Num(s.Requests), ", errors ", BenchMarkDownReport.Num(s.Errors), " (", s.ErrorPercent.ToString("0.00", inv), " %)"));
            ConsoleTools.ForegroundColor = c;
        }

        /// <summary>
        /// A status line that is redrawn in place; when the output is redirected only the last text is written (by <see cref="End"/>).
        /// </summary>
        sealed class StatusLine
        {
            readonly bool Redirected = Console.IsOutputRedirected;
            int LastLength;
            String Last;

            public void Write(String text)
            {
                Last = text;
                if (Redirected)
                    return;
                var width = ConsoleWidth();
                if (text.Length > width)
                    text = text.Substring(0, width);
                var len = text.Length;
                if (len < LastLength)
                    text = text.PadRight(LastLength);
                LastLength = len;
                Console.Write("\r" + text);
            }

            public void End()
            {
                if (Last == null)
                    return;
                if (Redirected)
                    Console.WriteLine(Last);
                else
                    Console.WriteLine();
                Last = null;
                LastLength = 0;
            }
        }

        /// <summary>
        /// The progress of a warm up or test on one console line (redrawn in place), or a line every 10 % when the output is redirected.
        /// Also sets the terminal (task bar / tab) progress indicator when the console supports it.
        /// </summary>
        sealed class ProgressMeter
        {
            public ProgressMeter(BenchRun run, String label)
            {
                Run = run;
                Label = label.PadRight(8);
                Redirected = Console.IsOutputRedirected;
            }

            readonly BenchRun Run;
            readonly String Label;
            readonly bool Redirected;
            int LastLength;
            int LastTenth = -1;

            public void Update(bool stopping)
            {
                var s = Run.GetStatus();
                var pct = (int)Math.Clamp(Math.Floor(s.Progress), 0, 100);
                ConsoleTools.SetProgress(stopping ? ConsoleProgressDisplays.Warning : ConsoleProgressDisplays.Normal, pct);
                var elapsed = Math.Min(s.ElapsedSeconds, s.DurationSeconds);
                var stats = String.Concat(
                    BenchMarkDownReport.Num(s.State == BenchStates.Running ? s.CurrentRps : s.Rps), " req/s  avg ",
                    BenchMarkDownReport.Ms(s.AvgMs), "  P95 ", BenchMarkDownReport.Ms(s.P95Ms), "  errors ", BenchMarkDownReport.Num(s.Errors));
                var state = stopping ? "stopping" : s.State == BenchStates.Finishing ? "finishing" : String.Concat(BenchMarkDownReport.Duration(elapsed), " / ", BenchMarkDownReport.Duration(s.DurationSeconds));
                if (Redirected)
                {
                    var tenth = pct / 10;
                    if (tenth == LastTenth)
                        return;
                    LastTenth = tenth;
                    Console.WriteLine(String.Concat(Label, pct.ToString(CultureInfo.InvariantCulture).PadLeft(3), " %  ", state, "  ", stats));
                    return;
                }
                const int BarWidth = 24;
                var fill = (int)Math.Round(BarWidth * pct / 100.0);
                var bar = String.Concat("[", new String('#', fill), new String('.', BarWidth - fill), "] ");
                var line = String.Concat(Label, bar, pct.ToString(CultureInfo.InvariantCulture).PadLeft(3), " %  ", state, "  ", stats);
                var width = ConsoleWidth();
                if (line.Length > width)
                    line = line.Substring(0, width);
                var len = line.Length;
                if (len < LastLength)
                    line = line.PadRight(LastLength);
                LastLength = len;
                Console.Write("\r" + line);
            }

            public void End(bool blankAfter)
            {
                ConsoleTools.SetProgress(ConsoleProgressDisplays.Disabled, 0);
                if (!Redirected && (LastLength > 0))
                {
                    //  The last state (100 %) stays on the line
                    Update(false);
                    Console.WriteLine();
                }
                if (blankAfter)
                    Console.WriteLine();
            }
        }

        #endregion//Running
    }
}
