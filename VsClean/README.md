# VsClean

[⬆ SysWeaver.Tools overview](../README.md)

> Deletes build output folders in folder trees: `bin` and `obj` next to a `.csproj`, and generated `web` / `data` folders next to `srcWeb` / `srcData`.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Forces a full rebuild of a whole repository by removing all build outputs, including the `web` and `data` folders that SysWeaver projects generate from `srcWeb` / `srcData` with [Compress](../Compress/README.md) and [SvgOpt](../SvgOpt/README.md).

## How it fits into SysWeaver

A prebuilt copy is kept in [`SysWeaver/_tools/VsClean`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md) and is used by the `Clean_All.bat` scripts of the SysWeaver, SysWeaver.Services and SysWeaver.Tools repositories (`..\SysWeaver\_tools\VsClean\VsClean.exe .`).

## Key features

- For each directory given (wildcards allowed), walks all sub folders and deletes:
  - `bin` and `obj` if the parent folder contains a `*.csproj` file
  - `web` if the parent folder has a `srcWeb` folder, `data` if it has a `srcData` folder
- A deleted folder isn't searched further; other folders are searched recursively
- Prints every deleted folder, and the reason when a folder couldn't be deleted (the others are still deleted)

## Using it

```
VsClean <Directory> [Directory..]
```

Exit codes: 0-2 as all tools (no arguments is 2), 3 if one or more folders couldn't be deleted, see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- Folders are deleted without confirmation (the `Clean_All.bat` scripts ask before running it)
- A directory that doesn't exist is silently ignored
- Folder names are matched case insensitively (`Bin`, `OBJ` and `srcweb` match too)

## Relationships

- **Project:** [`VsClean.csproj`](VsClean.csproj)
- **Builds on:** [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md)
