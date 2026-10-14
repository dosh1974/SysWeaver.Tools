# OnDuplicates

[⬆ SysWeaver.Tools overview](../README.md)

> Finds files with identical content in one or more folder trees and lists them, or runs a command for each duplicate.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Cleans up asset folders (ex: icon or avatar sets) that contain the same file under several names. Files are compared by a hash of their content, not by name or date.

## How it fits into SysWeaver

A prebuilt copy is kept in [`SysWeaver/_tools/OnDuplicates`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md); it isn't part of any build, but `SysWeaver.MicroService.Avatar/RemoveDuplicates.bat` uses it to delete duplicate avatar images:

```
..\_tools\OnDuplicates\OnDuplicates.exe srcData "cmd.exe /C del ""{0}"""
```

## Key features

- Hashes all files in the folders and their sub folders in parallel
- In each group of identical files the first path (ordinal sort) is kept as the original, every other file is a duplicate
- Without an action, prints `"<duplicate>" same as "<original>"` for each duplicate
- With an action, runs it once per duplicate: the first word is the executable, the rest is the argument string where `{0}` is replaced by the duplicate and `{1}` by the original

## Using it

```
OnDuplicates <Folders> [Action]

  Folders   The folders to search, separated by ';'
  Action    Optional command to run for each duplicate, ex: "cmd.exe /C del ""{0}"""
```

Exit codes: 0-2 as all tools (no arguments is 2; finding duplicates is not an error), see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- The action is started directly (not through the shell) and its exit code is ignored; use `cmd.exe /C ...` for shell commands
- The action must contain an executable followed by arguments; an executable path in quotes currently isn't recognised, and an action without arguments fails (exit code 1)
- An action that deletes files is permanent; run without an action first to see what would be affected

## Relationships

- **Project:** [`OnDuplicates.csproj`](OnDuplicates.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md)
