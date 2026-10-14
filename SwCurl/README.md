# SwCurl

[⬆ SysWeaver.Tools overview](../README.md)

> A curl-like HTTP client for SysWeaver APIs: sends an optional payload (a string or a file) with SysWeaver login or HTTP authentication, optional compression and Tor routing, and prints or saves the response.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Calls SysWeaver web APIs from scripts and build steps, ex: to trigger an action on a server after a deploy. It speaks SysWeaver's own login and request conventions (compressed payloads, small payloads sent as GET query data) and reports the HTTP result as an exit code, so scripts can react to failures.

## How it fits into SysWeaver

Uses [SysWeaver.Remote.Connection](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Remote.Connection/README.md) for the authenticated `HttpClient` (SysWeaver login or HTTP authentication) and, with `-UseTor`, [SysWeaver.Tor](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Tor/README.md). A prebuilt copy is kept in [`SysWeaver/_tools/SwCurl`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md); no SysWeaver build step calls it.

## Key features

- The payload argument is read from a file if it's an existing file name (MIME type from the extension or `-FileMime`), else the string is sent (`-Mime`, default `application/json`, `-Encoding`, default `utf-8`)
- `-HttpMethod Auto` uses GET without a payload and POST with one; with the SysWeaver login, payloads smaller than `-GetThreshold` bytes are sent as GET query data
- `-Compression Auto` Brotli compresses compressible payloads when the SysWeaver login is used (else no compression), or choose `None`, `Brotli`, `Deflate`, `GZip`
- Prints the request, the status, timing and content information; `-Dump` prints the response body (colour coded text, or a hex dump for binary data or with `-Binary`)
- `-Save <file>` writes the response body; for an error status the status line (ex: `<404> - Not found`) followed by the body
- The exit code tells success, HTTP client / server errors, connection failures and timeouts apart

## Using it

```
SwCurl [options] <endpoint> [data]

  endpoint   The API end point, ex: http://localhost/Test/api
  data       Optional payload: a file name or a string

  -User <name> -Password <pwd>   Credentials (or -CredFile <file>)
  -AuthMethod <method>           SysWeaverLogin (default) or HttpAuth (basic auth, or a bearer token if the user is "Bearer")
  -SwRoot <path>                 Service root (prefix) for the SysWeaver login, if not at the root of the end point
  -HttpMethod <method>           Auto (default), GET, POST, PUT, DELETE
  -Save <file>                   Save the response
  -Dump                          Print the response body
  -Silent                        Don't print the request and response information (errors are still printed)
  -Timeout <ms>                  Request timeout (default 300000)
  -UseTor                        Send the request through the Tor network
  -?                             Help (all options and the exit codes)
```

Exit codes: 0 for a 2xx status, 1-2 as all tools, 3 / 4 / 5 for a 4xx / 5xx / other status, 6 if the request failed without a response, 7 on a timeout, see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- `-IgnoreCertErrors` accepts any server certificate; only use it for tests
- A GET with a payload is rejected (exit 2) unless the SysWeaver login is used
- The project targets x64 (`PlatformTarget`)

## Relationships

- **Project:** [`SwCurl.csproj`](SwCurl.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md), [SysWeaver.Remote.Connection](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Remote.Connection/README.md), [SysWeaver.Tor](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Tor/README.md)
