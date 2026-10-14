# SvgOpt

[⬆ SysWeaver.Tools overview](../README.md)

> Optimises SVG files with SysWeaver.Minifier.Svg (cached and in parallel) and can write an HTML page that compares the originals with the results — used by the SysWeaver builds for icons.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Icons and other SVG assets are minified (and optionally normalised, ex: fill and size removed so they can be styled with CSS) before they are compressed and embedded. The comparison page makes it easy to check visually that the optimisation didn't break an image.

## How it fits into SysWeaver

The prebuilt copy in [`SysWeaver/_tools/svg_opt`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md) is called from `Exec` build steps in SysWeaver projects with icons, typically followed by [Compress](../Compress/README.md):

```
..\_tools\svg_opt\SvgOpt.exe -Compare .\WebCompare -RemoveFill -RemoveSize .\srcWeb\icons\*.svg .\srcWeb\icons\.opt\
..\_tools\compress\Compress.exe -Methods br .\srcWeb\icons\.opt\*.svg+ .\web\icons
```

The optimisation itself is [SysWeaver.Minifier.Svg](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Minifier.Svg/README.md); the tool adds the "files in → folder out" shell (`FilesToFolderTool`) of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), caching and the comparison page.

## Key features

- All `SvgMinifierParams` members are command line options (ex: `-RemoveFill`, `-RemoveSize`, `-MaxDecimals 2`)
- Results are cached per source file and option set, so unchanged files aren't processed again; outputs are only written when they changed
- A file that can't be optimised is reported (or copied unchanged if it can't be read or processed); it doesn't change the exit code
- `-Compare <name>` writes an HTML page with the original and optimised images side by side (`.html` is appended unless the name ends with `.htm` / `.html`, a name without a folder is created in the output folder, no page is written when no file was processed)
- Files are processed in parallel (sequentially in DEBUG builds)

## Key components

- [`SvgOptParams`](SvgOptParams.cs) — the options (`SvgMinifierParams` plus `Compare`)
- [`ImageCompareGallery`](ImageCompareGallery.cs) — writes the comparison page

## Using it

```
SvgOpt [options] <SourceFiles> [DestFolder]

  SourceFiles   File names or wildcard masks, separated by ';', a trailing '+' searches sub folders (ex: .\icons\*.svg)
  DestFolder    The output folder (default: the folder of the source file)

  -Compare <name>    Write an HTML comparison page
  -RemoveFill        Remove fill colours (so the icon can be coloured with CSS)
  -RemoveSize        Remove the width and height (so the icon can be sized with CSS)
  -MaxDecimals <n>   Limit the number of decimals
  -?                 Help (all minifier options)
```

Exit codes: 0-2 as all tools, see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`SvgOpt.csproj`](SvgOpt.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md), [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md), [SysWeaver.Minifier.Svg](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Minifier.Svg/README.md), [SysWeaver.Serialization](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Serialization/README.md), [SysWeaver.Storage](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Storage/README.md)
