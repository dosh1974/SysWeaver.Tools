# tools

[⬆ Compress](../README.md)

> Third-party Windows executables that Compress runs to minify web files, recompress PNGs and pre-process / minify shaders before compressing them.

| | |
|---|---|
| **Project** | [Compress](../README.md) |
| **Kind** | Bundled third-party executables |

## Purpose

[`Compress.csproj`](../Compress.csproj) copies these files to the output folder (`CopyToOutputDirectory` `PreserveNewest`), and [`CompressProgram`](../CompressProgram.cs) runs them from the `tools` folder next to the executable. If a helper fails, the file is reported with a warning and compressed without that step; each step can be turned off (`-OptimizeWebFiles false`, `-RecompressImages false`, `-OptimizeShaders false`). [`Publish_All.bat`](../../Publish_All.bat) syncs them with the rest of the tool into [`SysWeaver/_tools/compress/tools`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/compress/tools/README.md), where the SysWeaver builds use them.

## Contents

| Item | Description |
|---|---|
| [`minify.exe`](minify.exe) | [minify](https://github.com/tdewolff/minify): minifies web files (`.js`, `.css`, `.html`, `.json`, `.svg`, `.xml`, ...) |
| [`ZopfliPng.exe`](ZopfliPng.exe) | [ZopfliPng](https://github.com/google/zopfli): recompresses `.png` files (the result replaces the original in the output folder) |
| [`preprocessor.exe`](preprocessor.exe) | GLSL preprocessor run on `.glsl` / `.frag` shaders (with `-DGL_ES`) |
| [`shader_minifier.exe`](shader_minifier.exe) | [shader_minifier](https://github.com/laurentlb/shader-minifier): minifies the pre-processed shaders |
| [`glsl_optimizer.exe`](glsl_optimizer.exe) | GLSL optimiser; shipped but currently not used by Compress |

## Limitations and considerations

- All helpers are Windows executables, so the type specific pre-processing only works on Windows.

## Relationships

- **Part of:** [Compress](../README.md)
- **Used by:** [`CompressProgram`](../CompressProgram.cs)
- **Published copy:** [SysWeaver/_tools/compress/tools](https://github.com/dosh1974/SysWeaver/blob/main/_tools/compress/tools/README.md)
