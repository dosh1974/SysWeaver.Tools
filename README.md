```
                                //////////\   ////\       ////\   //////////\
                              ////\_\_\_////\ ////\       ////\ ////\_\_\_////\
                              ////\     \_\_/ \_////\   ////\_/ ////\     \_\_/
                              \_//////////\     \_////////\_/   \_//////////\
                                \_\_\_\_////\     \_////\_/       \_\_\_\_////\
                              ////\     ////\       ////\       ////\     ////\
                              \_//////////\_/       ////\       \_//////////\_/
                                \_\_\_\_\_/         \_\_/         \_\_\_\_\_/

              ////\     ////\ ////////////\         ////\   ////\ ////////////\ //////////\
              ////\     ////\ ////\_\_\_\_/       //////\   ////\ ////\_\_\_\_/ ////\_\_////\
              ////\     ////\ ////\             ////////\   ////\ ////\         ////\   ////\
              ////\ //\ ////\ ////////\       ////\_////\ ////\_/ ////////\     //////////\_/
              ////\ //\ ////\ ////\_\_/     ////\_/ ////////\_/   ////\_\_/     ////\_\_////\
              //////\_//////\ ////\         ////\   //////\_/     ////\         ////\   ////\
              ////\_/ \_////\ ////////////\ ////\   ////\_/       ////////////\ ////\   ////\
              \_\_/     \_\_/ \_\_\_\_\_\_/ \_\_/   \_\_/         \_\_\_\_\_\_/ \_\_/   \_\_/
```

> ⚠️ **DISCLAIMER**  
>         SysWeaver is used in my personal and professional work.  
>         It's **not** intended for wide public consumption.  
>         I move fast, refactor and break things regularly.  
>         Use at your own risk!  

# OVERVIEW

Command line tools built on [SysWeaver](https://github.com/dosh1974/SysWeaver). Some of them are used to build SysWeaver itself: prebuilt copies live in [`SysWeaver\_tools`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md), and the SysWeaver and SysWeaver.Services project builds call `Compress` and `SvgOpt` to pre-compress and optimise their embedded web assets.  

| Tool | In `_tools` | Summary |
|---|---|---|
| [AcmeCertUtil](AcmeCertUtil/README.md) | | Obtains (or renews) a Let's Encrypt certificate for a domain through ACME and saves it as a password protected `.pfx` file. |
| [ComfyApiToCs](ComfyApiToCs/README.md) | `ComfyApiToCs` | Converts ComfyUI API format workflows (json) into C# source files with the input and output types (and nested types) to use with `ComfyUiService.RunWorkflow`. |
| [Compress](Compress/README.md) | `compress` (build) | Pre-compresses files into side-by-side compressed copies (Brotli, gzip, zstd, deflate), optionally minifying web files, recompressing PNGs and optimising shaders first. |
| [Decompress](Decompress/README.md) | `decompress` | Decompresses Brotli, gzip, zstd and deflate compressed files (the reverse of Compress). |
| [FolderSync](FolderSync/README.md) | | Pushes one or more local folders to a managed folder on a SysWeaver folder sync service (push-only). |
| [OnDuplicates](OnDuplicates/README.md) | `OnDuplicates` | Finds files with identical content in folder trees and lists them, or runs a command for each duplicate. |
| [RunComfyUi](RunComfyUi/README.md) | `RunComfyUi` | Runs a ComfyUI workflow (API format json) with its inputs as command line options and saves the results; the input and output types are generated and compiled at runtime. |
| [SearchAndReplaceAll](SearchAndReplaceAll/README.md) | | Renames folders and files and replaces text in source files for a set of `Key=Value` replacements, ex: to turn a copied project into a new one. |
| [SvgOpt](SvgOpt/README.md) | `svg_opt` (build) | Optimises SVG files with SysWeaver.Minifier.Svg and can write an HTML page comparing the originals with the results. |
| [SwCurl](SwCurl/README.md) | `SwCurl` | A curl-like HTTP client for SysWeaver APIs (SysWeaver login, compression, Tor) with HTTP results as exit codes. |
| [SwSyncTool](SwSyncTool/README.md) | `SwSyncTool` | Pushes and pulls folders to and from a SysWeaver folder sync service, and maintains local `.swcompact` archives and their chunk store. |
| [Touch](Touch/README.md) | | Sets the last write time of all files matching one or more file masks to now. |
| [VsClean](VsClean/README.md) | `VsClean` | Deletes build output folders (`bin` / `obj` next to a `.csproj`, generated `web` / `data` next to `srcWeb` / `srcData`); used by the `Clean_All.bat` scripts. |

## BUILD  
Building requires the .NET 10 SDK and that the [SysWeaver](https://github.com/dosh1974/SysWeaver) repository is cloned to the same folder as the [SysWeaver.Tools](https://github.com/dosh1974/SysWeaver.Tools) repository (the projects reference `..\..\SysWeaver\...`):  
```
C:\Dev\SysWeaver
C:\Dev\SysWeaver.Tools
```

The prebuilt copies in `SysWeaver\_tools` are updated with [`Publish_All.bat`](Publish_All.bat): for every tool that has a `Properties\PublishProfiles\FolderProfile.pubxml` publish profile it runs `dotnet publish -c Release -p:PublishProfile=FolderProfile` and syncs the publish folder to `..\SysWeaver\_tools\<folder>` (`Compress` to `compress`, `Decompress` to `decompress`, `SvgOpt` to `svg_opt`, other tools to their project name). `.pdb` files and `createdump.exe` are skipped, `.xml` files are only copied for `SysWeaver.*` and the tool itself, only changed files are copied and files that are no longer published are removed. A tool that fails to publish leaves its `_tools` folder untouched (and the script returns 1).  
Note that `*.pubxml` files are ignored by `.gitignore`, so the publish profiles are not part of the repository: create a `FolderProfile` folder publish profile with a `PublishDir` (the current `_tools` copies are self-contained Windows x64 publishes) for each tool that should be published, else the script publishes nothing.  

[`Clean_All.bat`](Clean_All.bat) deletes all build outputs of the repository using `..\SysWeaver\_tools\VsClean\VsClean.exe`.  

## EXIT CODES  
All tools follow the common convention **0 = success, non-zero (1-255) = failure** (Windows `if errorlevel 1` / `%errorlevel%`, PowerShell `$LASTEXITCODE`, POSIX shells, MSBuild `Exec`; Linux truncates exit codes to 8 bits and `if errorlevel 1` ignores negative codes, so no negative codes are used).  
The shared codes are the same as `FilesToFolderTool` in [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md) (used by Compress, Decompress and SvgOpt):  

| Code | Meaning |
|---|---|
| 0 | Success, also when help was requested (`-?`, `-help`) |
| 1 | Generic error (an exception) |
| 2 | Invalid command line (unknown option, missing or invalid arguments, no arguments) |

Tool specific codes:  

| Tool | Code | Meaning |
|---|---|---|
| Compress | 3 | A file couldn't be compressed using any of the compression methods (`-OnlyBest`) |
| ComfyApiToCs | 3 | One or more files couldn't be converted (the others are) |
| RunComfyUi | 3 | Running the workflow failed (connection, server or workflow error, cancelled) |
| RunComfyUi | 4 | The workflow returned no results |
| Decompress | 3 | A file couldn't be decompressed (invalid or truncated data) |
| SwCurl | 0 | The response has a 2xx status |
| SwCurl | 3 | The response has a 4xx status (client error) |
| SwCurl | 4 | The response has a 5xx status (server error) |
| SwCurl | 5 | The response has some other non successful status (1xx, 3xx) |
| SwCurl | 6 | The request failed without a response (connection refused, name resolution, TLS etc) |
| SwCurl | 7 | The request timed out |
| SwSyncTool | 3 | The operation failed for one or more items (an archive failed to expand / verify / touch / recover / stat, or push / pull reported errors; missing chunks found by verify are only reported) |
| FolderSync | 3 | The sync completed but reported errors |
| SearchAndReplaceAll | 3 | Replacing failed in one or more files (the others are processed) |
| VsClean | 3 | One or more folders couldn't be deleted (the others are deleted) |

SwCurl writes the HTTP status to the console (unless `-Silent`) and, for an error status, to the `-Save` file (the status line, ex: `<404> - Not found`, then the response body).  
SvgOpt, OnDuplicates, AcmeCertUtil and Touch only use 0-2 (Touch: 0 or 1, no arguments is a no-op).  
