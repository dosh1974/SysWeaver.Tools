# SetDotNetVersion

[⬆ SysWeaver.Tools overview](../README.md)

> Changes the target framework of all projects in a folder tree, and optionally enables (or disables) the .NET 11 runtime async.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Moves a whole repository to another .NET version in one go, instead of editing every `.csproj` (and publish profile) by hand.

## How it fits into SysWeaver

A stand-alone helper with no SysWeaver dependencies; no build calls it. A prebuilt copy is kept in [`SysWeaver/_tools/SetDotNetVersion`](https://github.com/dosh1974/SysWeaver/blob/main/_tools/README.md) (updated by `Publish_All.bat`).

## Key features

- Processes `*.csproj`, `*.props`, `*.targets` (ex: `Directory.Build.props`) and `*.pubxml` files in the folder and all sub folders (`bin`, `obj`, `.git`, `.vs` and `node_modules` are skipped)
- Changes `<TargetFramework>` and `<TargetFrameworks>`:
  - only .NET 5+ frameworks are changed, others (ex: `netstandard2.0`, `net48`) are left as is
  - platform suffixes are kept (`net9.0-windows` becomes `net10.0-windows`)
  - duplicates in a multi target list are removed (`net8.0;net9.0` becomes `net10.0`)
- Publish profiles: also changes the framework folder in `<PublishDir>` / `<PublishUrl>` (ex: `bin\Release\net10.0\publish\`)
- Runtime async (`-rasync`): sets `runtime-async=on` in an existing `<Features>` element, or adds a new property group after the one with the target framework:
  ```xml
  <PropertyGroup>
    <Features>runtime-async=on</Features>
  </PropertyGroup>
  ```
  Only applied to files where all target frameworks are `net11.0` or later (others are reported and skipped)
- Runtime async is removed when a project is changed to a framework before `net11.0`, or with `-rasync-`
- Files are edited as text: formatting, indentation, encoding (BOM) and line endings are kept, unchanged files aren't written
- Running it again with the same options changes nothing

## Using it

```
SetDotNetVersion [Options] <Folder>
```

| Option | Description |
|---|---|
| `-net8.0`, `-net9.0`, `-net10.0`, `-net11.0` | The target framework to set (`-net10` etc works too) |
| `-rasync` | Enable runtime async (requires `net11.0` or later). Without a framework option it's applied to projects already targeting `net11.0`+ |
| `-rasync-` | Disable (remove) runtime async |
| `-dry` | Only show what would be changed |

Examples:
```
SetDotNetVersion -net11.0 -rasync C:\Dev\SysWeaver
SetDotNetVersion -net10.0 -dry .
```

Note: PowerShell splits `-net10.0` into `-net10` and `.0` when calling an exe, use `'-net10.0'` or `-net10` in PowerShell.

Exit codes: 0-2 as all tools (no arguments is 2), 3 if one or more files couldn't be processed, see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- Frameworks set through properties (ex: `<TargetFramework>$(MyTfm)</TargetFramework>`) aren't resolved, but a `Directory.Build.props` that defines them is processed too
- `global.json` (SDK version) and package versions aren't changed
- Files without a .NET 5+ target framework never get runtime async added

## Relationships

- **Project:** [`SetDotNetVersion.csproj`](SetDotNetVersion.csproj)
- **Builds on:** —
