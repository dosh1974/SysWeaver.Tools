# HttpBench

[⬆ SysWeaver.Tools overview](../README.md)

> A console tool that performance tests a SysWeaver web server (the tests of the HTTP bench page, without a server of its own) and writes the results as a markdown report.

| | |
|---|---|
| **Kind** | Console tool (exe) |
| **Engine** | [SysWeaver.HttpBench.Core](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.HttpBench.Core/README.md) |
| **Options** | `HttpBenchOptions.cs`, parsed with `CommandLine.ParseObject` (SysWeaver.CommandLine) |
| **Prebuilt** | [`SysWeaver/_tools/HttpBench`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md) (updated by `Publish_All.bat`) |

## Syntax

```
HttpBench [options] [report_name]
```

`report_name` is the markdown report, default `Report_[server].md`: the server address without `http://` / `https://`, with `:` (the port) and `/` as `_`, ex: `Report_localhost_1212.md`.

The parameters are taken from (later wins): the defaults, the parameter file (`-File`), the other options. `-?` shows the help (from the xml documentation of `HttpBenchOptions`), `-List` the modules and end points.

| Option | Default | |
|---|---|---|
| `-File <file>` | | A parameter file (json, `"DataType": "HttpBench"`), as exported from the page (*Export*) or written with `-Save` |
| `-Server <url>` | | The root url of the server to test (required here or in the file) |
| `-Duration <s>` | 60 | Duration in seconds |
| `-Workers <n>` | 16 | Workers: clients that each send one request at a time, with their own connection and session |
| `-Apis <list>` | Default | The end points: a `;` separated list of `All`, `Default`, `None`, modules (`Files`, `Images`, `Application`, `Auth`, `Types`, `Explore`, `Errors`) and end points (`Application.GetMenu` or `GetMenu`); `!` removes. A list that starts with a removal edits the selection of the file, ex: `"All;!Errors"`, `"!Explore"` |
| `-Values POST\|GET` | POST | How the apis that take a value get it: the json body or the query string |
| `-ValueCompression <c>` | None | Compression of the api values: None, br, gzip, deflate |
| `-AcceptEncoding <c>` | All | The Accept-Encoding header: All, None, br, gzip, deflate |
| `-AnyCertificate true\|false` | true | Accept self signed, LAN and expired https certificates |
| `-Timeout <s>` | 30 | Time out of one request (counted as an error) |
| `-Drain <s>` | 5 | Time for the requests in flight at the end |
| `-WaitForServer <s>` | 30 | Wait at most this long for the server to answer `serverTime` before starting (0 = don't wait) |
| `-StartDelay <s>` | 10 | Wait this long after the server answered, when it didn't answer at once (it was starting) |
| `-WarmUp true\|false` | true | Warm up before the test (not in the report) |
| `-WarmUpSeconds <s>` | 10 | Duration of the warm up: the same end points, workers and settings as the test |
| `-WarmUpPause <s>` | 20 | Pause after the warm up, with one `serverTime` request per second (the server finishes its optimizations while it stays active) |
| `-Json <file>` | | Also write the results as json (`"DataType": "HttpBenchResult"`: the report, the parameters and the totals), for scripts |
| `-Save <file>` | | Also save the parameters (after the overrides) as a parameter file, it can be imported on the page |
| `-NoRun` | | Only show (and save) the parameters |
| `-List` | | List the modules and end points |

## Examples

```bat
HttpBench -File ServerTest.json
HttpBench -File ServerTest.json -Server https://localhost:1212 LocalTest.md
HttpBench -Server https://myserver/ -Duration 300 -Workers 64 -Apis "All;!Errors" -Save MyServer.json
```

The first one runs the test of `ServerTest.json` and writes `Report_[server].md`, the second one runs it against `https://localhost:1212/` and writes `LocalTest.md`.

## Phases

1. **Wait for the server**: `serverTime` is requested (once a second) until it answers, at most `-WaitForServer` seconds (exit code 4 if it never does). When it didn't answer the first request it was starting: HttpBench waits `-StartDelay` seconds more.
2. **Warm up** (`-WarmUp`, on by default): the test runs for `-WarmUpSeconds`, then a pause of `-WarmUpPause` seconds with one `serverTime` request per second. A server that just started compiles and caches on the first requests; the warm up keeps that out of the results.
3. **Test**: the report and the summary are of this phase only.

## Output

The console shows the parameters (one line each), a progress line while the test runs (bar, percent, time, requests/s of the last second, average and P95 response time, errors; a line every 10 % when the output is redirected) and a short summary. Terminals that support it (Windows Terminal) also show the progress on the tab / task bar. The first Ctrl+C stops the test (the report is still written), a second one ends the program.

The report has the key results on top (requests/s, response times: average, median, P90, P95, P99, min / max, time to first byte; errors, traffic), then the parameters and the tables: end points, modules, status codes, errors, the timeline and the response time distribution (see [SysWeaver.HttpBench.Core](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.HttpBench.Core/README.md#the-markdown-report)).

Exit codes: 0 completed, 1 failed (or the report could not be written), 2 invalid arguments or parameter file, 3 stopped with Ctrl+C (before the test: no report), 4 the server didn't answer within `-WaitForServer` seconds.

Note: the server must accept the host name of the url, a SysWeaver server that listens on `http://localhost:7480/` answers `http://127.0.0.1:7480/` with 400 Bad Request (counted as errors).
