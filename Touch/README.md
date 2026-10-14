# Touch

[⬆ SysWeaver.Tools overview](../README.md)

> Sets the last write time of all files matching one or more file masks to now.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Forces build systems and caches that compare file times (ex: MSBuild incremental builds) to treat files as changed. A minimal replacement for the Unix `touch` command, without creating missing files.

## How it fits into SysWeaver

A stand-alone helper with no SysWeaver dependencies; no build calls it and it isn't part of `SysWeaver/_tools`.

## Using it

```
Touch <[Path\]FileMask[+]> [[Path\]FileMask[+]..]
```

- A mask without a path is relative to the current directory
- A trailing `+` also searches sub folders (ex: `Touch src\*.cs+`)
- The name of each touched file is printed; files are never created

Exit codes: 0 on success, for help or with no arguments (nothing is done), 1 on an error (ex: the folder doesn't exist or a file couldn't be touched), see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`Touch.csproj`](Touch.csproj)
- **Builds on:** —
