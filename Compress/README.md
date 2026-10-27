# Compress

[⬆ SysWeaver.Tools overview](../README.md)

> Pre-compresses files into side-by-side compressed copies (Brotli, gzip, zstd, deflate), optionally minifying web files, recompressing PNGs and optimising shaders first — used by the SysWeaver builds for embedded web and data assets.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |
| **Platform** | Windows (the bundled minifier, PNG and shader tools are Windows executables) |

## Purpose

SysWeaver serves embedded web assets pre-compressed, so they are compressed once at build time instead of on every request. Compress takes source files (typically a `srcWeb` or `srcData` folder) and writes the compressed copies (ex: `app.js.br`) to an output folder (typically `web` or `data`) that is embedded into the assembly.

## How it fits into SysWeaver

The prebuilt copy in [`SysWeaver/_tools/compress`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md) is called from `Exec` build steps in many SysWeaver and SysWeaver.Services projects, ex: `..\_tools\compress\Compress.exe -Methods br .\srcWeb\*+ .\web`. It is built on the "files in → folder out" shell (`FilesToFolderTool`) of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md) and the compression plug-ins of [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md).

## Key features

- Compresses each file with every method in `-Methods` (default `br,gzip,zstd`) at the best level, or only keeps the smallest result (`-OnlyBest`); `-OnlyBetter` skips results that aren't smaller than the original
- Type specific pre-processing before compression (each can be turned off):
  - web files (`.js`, `.mjs`, `.css`, `.htm`, `.html`, `.xhtml`, `.json`, `.svg`, `.xml`, `.rss`, `.webmanifest`) are minified with `minify`; lines between `//DEBUG_BEGIN` and `//DEBUG_END` are removed from `.js` files first
  - `.png` files are recompressed with ZopfliPng (the result replaces the original in the output folder)
  - `.glsl` / `.frag` shaders are stripped (comments and white space removed, short number literals, nothing renamed or evaluated) so that they work exactly like the original, also in [Effect Weaver](https://github.com/dosh1974/EffectWeaver) (its shader parser reads settings from the source, unless `-RemoveShaderVars`). `-MinifyShaders true` also tries shader_minifier (renaming, inlining, preprocessed) and uses it when smaller: about 10 % smaller after Brotli, but shader_minifier 1.6.0 rewrites can break a shader (one effect compiled and then rendered black / lost the WebGL context), so verify such results. What is kept in both modes:
    - the tunable variable candidates are kept with their comments: `#define NAME literal` (top level or in an `#ifndef NAME` guard) and `const T NAME = literal;` with a comment (aligned comment continuation lines joined, `// var: {...}` annotations compacted), in the original order
    - the iMouse control comments (`// iMouse.x = Name | Description | Default`) are kept as compact lines at the top
    - `main`, `mainImage` and `vertexMain` (the optional vertex stage) are entry points; the snippet macros the effect defines (`#define EW_SDF(p) map(p)`) are kept with the functions they use, and functions named like snippets (`Ew…`) keep their names; one top level declaration per line (snippets are inserted by line when the effect is built)
    - conditionals on compiler macros (`#if __VERSION__ >= 300`, they differ between the GLSL ES 3.00 / 1.00 builds) are kept as is if they only contain directives
    - with `-MinifyShaders`: if a kept define is used by a conditional (directly or through another macro), or a compiler macro conditional contains code, the shader is only stripped; the shader_minifier output (with and without aggressive inlining) is used only if it's smaller than stripping, and the source if nothing is smaller
    - the parser's candidate rules are mirrored in `CompressProgram_Shaders.cs` (keep them in sync with EffectWeaver.Common `ShaderParser`); bumping `ShaderTypeVersion` re-processes the cached shaders
- Files whose type doesn't benefit from compression (by MIME type) are only copied, unless `-Force` is used (`.jpg`, `.jpeg` and `.ico` are always compressed)
- Results are cached per source file, and outputs are only written when they changed, so incremental builds are fast
- Files are processed in parallel (sequentially in DEBUG builds)

## Key components

- [`CompressParams`](CompressParams.cs) — the command line options
- [`CompressProgram`](CompressProgram.cs) — the type specific processing, compression and caching
- [`CompressProgram_Shaders`](CompressProgram_Shaders.cs) — the shader optimization (Effect Weaver's parser rules, minimize / strip)

## Using it

```
Compress [options] <SourceFiles> [DestFolder]

  SourceFiles   File names or wildcard masks, separated by ';', a trailing '+' searches sub folders (ex: .\srcWeb\*+)
  DestFolder    The output folder (default: the folder of the source file)

  -Methods <list>          Compression methods (HTTP codes), comma separated: br, gzip, zstd, deflate (default br,gzip,zstd)
  -OnlyBest                Only save the smallest compressed version
  -OnlyBetter              Skip a compressed version that isn't smaller than the original
  -CopyOriginal            Also copy the original file to the output folder
  -Force                   Compress files even if their type normally isn't compressed
  -OptimizeWebFiles false  Don't minify web files (also -RecompressImages false, -OptimizeShaders false)
  -MinifyShaders true      Also minify shaders with shader_minifier when smaller (verify the results, see above)
  -?                       Help (all options)
```

Exit codes: 0-2 as all tools, 3 if a file couldn't be compressed with any method (`-OnlyBest`), see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- The helper executables in [`tools/`](tools) (`minify.exe`, `ZopfliPng.exe`, `preprocessor.exe`, `shader_minifier.exe`; `glsl_optimizer.exe` is shipped but currently unused) are Windows binaries copied next to the tool; if one fails, the file is reported with a warning and compressed without the type specific step
- Unknown names in `-Methods` are silently ignored; `org` (documented as "copy the original") and an empty entry (ex: a trailing comma) currently make the option validation throw, which ends with exit code 2
- A file named `typescript.js` is copied as is (not minified)

## Folder layout

| Folder | Content |
|---|---|
| [`tools/`](tools/README.md) | Bundled Windows helper executables (minify, ZopfliPng, shader preprocessor / minifier, unused glsl_optimizer), copied next to the tool |

## Relationships

- **Project:** [`Compress.csproj`](Compress.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md), [SysWeaver.Compression](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Compression/README.md), [SysWeaver.Compression.BrotliNativeNET](https://github.com/dosh1974/SysWeaver/blob/main/Compression/SysWeaver.Compression.BrotliNativeNET/README.md), [SysWeaver.Compression.ZstdSharp](https://github.com/dosh1974/SysWeaver/blob/main/Compression/SysWeaver.Compression.ZstdSharp/README.md), [SysWeaver.Serialization](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Serialization/README.md), [SysWeaver.Serialization.NewtonsoftJson](https://github.com/dosh1974/SysWeaver/blob/main/Serialization/SysWeaver.Serialization.NewtonsoftJson/README.md), [SysWeaver.Storage](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Storage/README.md)
- **Third-party:** bundled executables: [minify](https://github.com/tdewolff/minify), [ZopfliPng](https://github.com/google/zopfli), [shader_minifier](https://github.com/laurentlb/shader-minifier) and a GLSL preprocessor
