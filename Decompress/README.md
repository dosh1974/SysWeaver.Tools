# Decompress

[⬆ SysWeaver.Tools overview](../README.md)

> Decompresses Brotli, gzip, zstd and deflate compressed files (the reverse of Compress), optionally keeping only the preferred version when a file exists in several compressed forms.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Restores the original files from compressed copies, ex: to inspect pre-compressed web assets or to unpack files that are stored compressed. The compression method is selected from the file extension (ex: `app.js.br` becomes `app.js`).

## How it fits into SysWeaver

A prebuilt copy is kept in [`SysWeaver/_tools/decompress`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md), but no SysWeaver build step calls it. It is built on the "files in → folder out" shell (`FilesToFolderTool`) of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md) and the compression plug-ins of [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md).

## Key features

- Any file extension known by a registered compression method (`.br`, `.gz` / `.gzip`, `.zstd`, `.deflate`) is decompressed, other files are skipped
- `-Once` decompresses only one of several compressed versions of the same file (ex: `a.txt.br` and `a.txt.zstd`), using the `-Prefer` order
- Decompresses to a temporary file that is moved in place, so a failure never leaves a partial file; the output keeps the source file's last write time
- Files are processed in parallel (sequentially in DEBUG builds)

## Using it

```
Decompress [options] <SourceFiles> [DestFolder]

  SourceFiles   File names or wildcard masks, separated by ';', a trailing '+' searches sub folders (ex: .\web\*.br+)
  DestFolder    The output folder (default: the folder of the source file)

  -Overwrite false   Skip files that already exist in the output folder (default: overwrite)
  -Once              Only decompress the preferred compressed version of each file
  -Prefer <list>     Preference order for -Once, HTTP codes, comma separated (default zstd,br,gzip,deflate)
  -?                 Help
```

Exit codes: 0-2 as all tools (an unknown method in `-Prefer` is 2), 3 if a file couldn't be decompressed (invalid or truncated data), see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`Decompress.csproj`](Decompress.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md), [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md), [SysWeaver.Compression.BrotliNativeNET](https://github.com/dosh1974/SysWeaver/blob/main/Compression/SysWeaver.Compression.BrotliNativeNET/README.md), [SysWeaver.Compression.ZstdSharp](https://github.com/dosh1974/SysWeaver/blob/main/Compression/SysWeaver.Compression.ZstdSharp/README.md)
