# FolderSync

[⬆ SysWeaver.Tools overview](../README.md)

> Pushes one or more local folders to a managed folder on a SysWeaver folder sync service (a push-only client; SwSyncTool offers the same push plus pull and local archive operations).

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Deploys a local folder (ex: a published application) to a server running the folder sync service, transferring only what changed.

## How it fits into SysWeaver

A thin command line front end for `FolderSyncer.PushFolders` in [SysWeaver.FolderSync](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.FolderSync/README.md), talking to a server running [SysWeaver.MicroService.FolderSync](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.MicroService.FolderSync/README.md). It is not part of `SysWeaver/_tools`; [SwSyncTool](../SwSyncTool/README.md) `Push` covers the same use with more options.

## Key features

- Scans the local folders, checks them against the server's repository and uploads the missing data, printing progress and a transfer summary
- Content-defined chunking (only new chunks are uploaded) unless `-NoCdc` is given
- By default the server switches to the new version immediately; `-NoSwitch` uploads to a folder that must be switched to manually on the server
- `-Ignore` skips files matching wildcard masks

## Using it

```
FolderSync [options] <Source> <Server> <Name>

  Source   The local folder(s), separated by ';'
  Server   The server to sync to
  Name     The name of the server folder

  -User <name> -Password <pwd>   Credentials (or -CredFile <file> with a single "user:password" line)
  -Ignore <masks>                Wildcard masks of files to skip, separated by ';'
  -Comment <text>                Optional comment
  -NoSwitch                      Don't switch the server to the new version
  -NoCdc                         Don't use content-defined chunking
  -?                             Help
```

Exit codes: 0-2 as all tools (no arguments is 2), 3 if the sync completed but reported errors, see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- `-IgnoreCertErrors` accepts any server certificate; only use it for tests
- The "Chunks" percentage in the summary is always 100 % (it divides the new chunk count by itself)

## Relationships

- **Project:** [`FolderSync.csproj`](FolderSync.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.FolderSync](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.FolderSync/README.md)
