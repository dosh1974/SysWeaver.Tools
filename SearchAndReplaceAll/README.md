# SearchAndReplaceAll

[⬆ SysWeaver.Tools overview](../README.md)

> Renames folders and files and replaces text in source files below a folder for a set of `Key=Value` replacements (including upper case, lower case and de-camel-cased variants), ex: to turn a copied project into a new one.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Copying an existing project or template and renaming it by hand means renaming folders, files, namespaces, class names and project references. This tool does it in one pass for any number of names.

## How it fits into SysWeaver

A stand-alone helper used while developing; no build calls it and it isn't part of `SysWeaver/_tools`. It only uses string helpers from [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md).

## Key features

- For each `Key=Value` pair the upper case, lower case, de-camel-cased (`MyKey` as `My key`) and underscore (`My_key`) variants are also replaced, unless such a key is given explicitly
- Longer keys are replaced first
- Renames all sub folders (deepest first), then all files, then replaces line by line in text files (`.txt`, `.log`, `.config`, `.xml`, `.html`, `.css`, `.js`, `.json`, `.h`, `.c`, `.hpp`, `.cpp`, `.cs`, `.vb`, `.vbs`, `.sln`, `.csproj`, `.csxproj`, `.bat`)
- A file that can't be updated is reported and the others are still processed

## Using it

```
SearchAndReplaceAll <Folder> <Key1=Value1> [Key2=Value2] .. [KeyN=ValueN]
```

Exit codes: 0-2 as all tools, 3 if replacing failed in one or more files, see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- Changes are made in place, there is no preview or undo; run it on a copy or under source control
- The replacements are case sensitive (apart from the generated variants) and plain text, not word based (`Foo=Bar` also changes `FooBar`)
- The top folder itself is not renamed; files with other extensions (ex: `.slnx`, `.md`, `.razor`) are renamed but their content isn't changed
- Files are rewritten with `File.WriteAllLines` (the original line endings and a missing final new line aren't preserved)

## Relationships

- **Project:** [`SearchAndReplaceAll.csproj`](SearchAndReplaceAll.csproj)
- **Builds on:** [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md)
