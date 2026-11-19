# JsConvert

[⬆ SysWeaver.Tools overview](../README.md)

> Converts JavaScript files and the scripts in html pages to older JavaScript versions (ES2017, ES2015, ES5 or a list of browsers) for old and embedded browsers, using SysWeaver.Converter.Js — a command line replacement for converting files with the Babel REPL.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Pages that are served to devices with an old embedded browser (ex: point of sale terminals) must not use JavaScript syntax the browser doesn't understand. JsConvert converts the files as a build or deploy step: class fields, private members, `?.`, `??`, async functions, etc. are rewritten to what the target supports, the runtime helpers are added and missing built-ins are reported (or polyfilled).

## How it fits into SysWeaver

The conversion is [SysWeaver.Converter.Js](https://github.com/dosh1974/SysWeaver/blob/main/Converter/SysWeaver.Converter.Js/README.md); the tool adds the "files in → folder out" shell (`FilesToFolderTool`) of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), html handling by extension, shared helper files and console / MSBuild friendly messages.

## Key features

- All `JsConverterParams` members are command line options (ex: `-Target "chrome 69"`, `-Loose`, `-Minify`, `-BuiltIns Polyfill`, `-Exclude Generators`)
- **Site cloning** (default for `.html` / `.htm` files): the pages and everything they use are written to the destination folder, keeping the paths relative to the site root (the common folder of all used files, or `-SiteRoot`):
  - followed: `<script src>`, `<link href>` (style sheets, module preloads, icons, manifests, ...), `img` / `source` / `video` / `audio` / `track` / `input` / `embed` / `object` sources and `srcset`, `style` elements and attributes, css `@import` and `url()`, static and dynamic `import` with literal urls, `new Worker(...)`, `importScripts(...)`, service worker registration, and string literals in scripts that are relative paths of existing `.js` / `.mjs` / `.css` files (ex: `includeJs("lib.js")`, `-SiteScanStrings false` turns this off)
  - not followed: dependencies created with computed urls, iframes / frames, links to other pages (`a href`), external urls (`http:`, `//`, `data:` ...), bare module specifiers; referenced files that don't exist are reported as warnings
  - pages, scripts and modules are converted, style sheets and other files (images, fonts, ...; `-SiteAssets false` to skip them) are copied
  - with `-HelpersFile` a script element loading the helpers file is added to every page before its first script
  - `-Site false` converts only the html file itself (inline scripts and event handler attributes, everything else is kept)
- Other files are converted as JavaScript (script or module, detected automatically)
- `-HelpersFile helpers.js` writes all helpers (and polyfills) used by the converted files to one file instead of adding them to every file
- Diagnostics are written as `file(line,col): warning: message` (Visual Studio / MSBuild format), information messages (missing built-ins) with `-Verbose`
- Output files are only written if their content changed (unchanged files keep their time stamp), `-Suffix .es5` writes `name.es5.js`; the source is never overwritten unless `-Overwrite` is given
- `-ShowTarget` prints the resolved target and the features that will be transformed
- Files are processed in parallel (sequentially in DEBUG builds)

## Using it

```
JsConvert [options] <SourceFiles> [DestFolder]

  SourceFiles   File names or wildcard masks, separated by ';', a trailing '+' searches sub folders (ex: .\web\*.js+;.\web\*.html+)
  DestFolder    The output folder (default: the folder of the source file, then -Suffix or -Overwrite is required)

  -Target <t>           es5, es2015 ... es2022, or browsers ("chrome 69", "safari 12, firefox 60"), default es2017
  -Include / -Exclude   Features to always / never transform (ex: -Exclude Generators)
  -Loose                Smaller, faster output that deviates from the specification in edge cases
  -BuiltIns <m>         Ignore, Report (default) or Polyfill missing built-ins
  -HelpersFile <file>   Write the helpers to one file (load it before the converted files)
  -Minify               Minified output (white space only)
  -Comments <c>         All (default), License or None
  -Suffix <text>        Added to the output names (ex: .es5)
  -WarningsAsErrors     Fail files with warnings
  -Verbose              Also show information messages
  -ShowTarget           Show the target and the transformed features
  -Site <bool>          Html files: clone the site (default true)
  -SiteRoot <folder>    The site root (root relative urls, output layout)
  -SiteAssets <bool>    Also copy images, fonts etc (default true)
  -?                    Help (all options)
```

Examples:

```bat
:: Clone a site (the pages, their scripts, style sheets and assets) converted for an old browser
JsConvert -Target "chrome 69" .\srcWeb\*.html+ .\SiteForOldDevices

:: Pages for an old embedded browser (here: one that supports what Chrome 69 supports)
JsConvert -Target "chrome 69" .\PosInfoWeb\*.js+;.\PosInfoWeb\*.html .\PosInfoWeb.Old

:: ES5 with a shared helpers file and polyfills, minified
JsConvert -Target es5 -BuiltIns Polyfill -HelpersFile helpers.js -Minify .\srcWeb\*.js+ .\web
```

Exit codes: 0-2 as all tools (see [EXIT CODES](../README.md#exit-codes)), 3 = one or more files couldn't be converted (errors, or warnings with `-WarningsAsErrors`); the other files are converted.

## Relationships

- **Project:** [`JsConvert.csproj`](JsConvert.csproj)
- **Builds on:** [SysWeaver.Converter.Js](https://github.com/dosh1974/SysWeaver/blob/main/Converter/SysWeaver.Converter.Js/README.md), [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md)
