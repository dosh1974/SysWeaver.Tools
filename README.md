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
>         SysWeaver is used in my personal and proffesional work.  
>         It's **not** intended for wide public consumption.  
>         I move fast, refactor and break things regulary.  
>         Use at your own risk!  

# OVERVIEW

Some command line tools (some used for building SysWeaver).  

## BUILD  
Building requires that the [SysWeaver](https://github.com/dosh1974/SysWeaver) repository is Cloned to the same folder as the [SysWeaver.Tools](https://github.com/dosh1974/SysWeaver.Tools) repository:  
```
C:\Dev\SysWeaver
C:\Dev\SysWeaver.Tools
```

The prebuilt copies in `SysWeaver\_tools` (used by the SysWeaver builds) are updated manually, there is no script: publish the tool with its `FolderProfile` publish profile (`dotnet publish -c Release -p:PublishProfile=FolderProfile`, a self-contained win-x64 folder) and copy the publish folder over the matching `_tools` sub folder (`compress`, `svg_opt`, `SwCurl`, `SwSyncTool`, `OnDuplicates`, `VsClean`).  

## EXIT CODES  
All tools follow the common convention **0 = success, non-zero (1-255) = failure** (Windows `if errorlevel 1` / `%errorlevel%`, PowerShell `$LASTEXITCODE`, POSIX shells, MSBuild `Exec`; Linux truncates exit codes to 8 bits and `if errorlevel 1` ignores negative codes, so no negative codes are used).  
The shared codes are the same as `FilesToFolderTool` in SysWeaver.CommandLine (used by Compress and SvgOpt):  

| Code | Meaning |
|---|---|
| 0 | Success, also when help was requested (`-?`, `-help`) |
| 1 | Generic error (an exception) |
| 2 | Invalid command line (unknown option, missing or invalid arguments, no arguments) |

Tool specific codes:  

| Tool | Code | Meaning |
|---|---|---|
| Compress | 3 | A file couldn't be compressed using any of the compression methods (`-OnlyBest`) |
| SwCurl | 0 | The response has a 2xx status |
| SwCurl | 3 | The response has a 4xx status (client error) |
| SwCurl | 4 | The response has a 5xx status (server error) |
| SwCurl | 5 | The response has some other non successful status (1xx, 3xx) |
| SwCurl | 6 | The request failed without a response (connection refused, name resolution, TLS etc) |
| SwCurl | 7 | The request timed out |
| SwSyncTool | 3 | The operation failed for one or more items (an archive failed to expand / verify / touch / recover / stat, or push / pull reported errors) |
| FolderSync | 3 | The sync completed but reported errors |
| SearchAndReplaceAll | 3 | Replacing failed in one or more files (the others are processed) |
| VsClean | 3 | One or more folders couldn't be deleted (the others are deleted) |

SwCurl writes the HTTP status to the console (unless `-Silent`) and, for an error status, to the `-Save` file (the status line, ex: `<404> - Not found`, then the response body).  
SvgOpt, OnDuplicates, AcmeCertUtil and Touch only use 0-2 (Touch: 0 or 1, no arguments is a no-op).  
Before these were fixed: Compress returned -4, SwCurl -1 / -2 and the HTTP status code (ex: 404, and 200 for success), SwSyncTool -1 / -2 / -3, SearchAndReplaceAll always 0, SvgOpt crashed on `-?` and on an invalid command line, FolderSync, OnDuplicates, AcmeCertUtil, VsClean and Touch returned 0 on errors or crashed.  
