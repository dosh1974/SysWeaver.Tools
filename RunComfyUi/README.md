# RunComfyUi

[⬆ SysWeaver.Tools overview](../README.md)

> Runs a ComfyUI workflow (API format json) from the command line: the workflow inputs become command line options, the results are saved to files.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Run any annotated ComfyUI workflow without writing code: `RunComfyUi -PositivePrompt "Create an image of a dragon" image_t2i.json .\` saves the generated image in the current folder. Without a destination the tool lists the options of the workflow (its inputs with the workflow defaults, and the connection parameters) and its outputs.

## How it fits into SysWeaver

The input and output types are generated from the workflow by `ComfyUiTypeGenerator` of [SysWeaver.AI.ComfyUi](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.AI.ComfyUi/README.md) and compiled at runtime with Roslyn. The input type inherits `ComfyUiParams`, so one object holds both the connection parameters and the workflow inputs; it is parsed with `CommandLine.ParseObject` of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md) (the help is generated from the xml documentation the compiler writes next to the generated assembly) and passed to `ComfyUiService.RunWorkflow`.

## Key features

- The options are the workflow inputs (the `$Tag` annotated nodes and the subgraph inputs, the UI workflow is fetched from the server or read from `-UiWorkflowFolder` to annotate them) and the connection parameters (`-EndPoint`, `-ApiKey`, `-ComfyUiToken`, `-NetworkTimeoutSeconds` etc)
- Options that aren't given are not sent, so the workflow defaults are used (the help shows them)
- File inputs (ex: the image of a "Load Image" node) take a local file name (uploaded) or an http(s) url; inputs with an object value become nested options (ex: `-Config.Speed 1.5`)
- All results of all outputs (`#Tag` annotated nodes) are saved; the extension comes from the result data (png, jpg, webp, gif, mp4, webm, wav, flac, ogg, mp3), else from the output node ("format" input or the node class)
- Destination: an existing folder (or a name ending with a separator) gets new files named after the workflow (and the output tag if there are several outputs), `_2`, `_3` etc are appended so no file is overwritten. Else it's a file name (the extension of the result is added if it has none), more results get new files named after it
- The compiled types are cached in `%TEMP%\RunComfyUi` (keyed by the generated source), so only the first run of a workflow compiles
- Ctrl+C cancels a running workflow

## Using it

```
RunComfyUi [options] <Workflow> [Destination]

  Workflow      A ComfyUI API format workflow file (saved using "Workflow > Export (API)")
  Destination   An existing folder or a file name, without a destination the options of the workflow are shown

  -<Input> <value>       A workflow input (see the options of the workflow)
  -EndPoint <url>        The ComfyUI(-Connect) server (default http://127.0.0.1:8188)
  -ApiKey <key>          Optional api key (or -CredFile)
  -?                     Help
```

Ex:
```
RunComfyUi D:\ComfyUI\api\image_t2i.json
RunComfyUi -PositivePrompt "Create an image of a dragon" -Steps 20 D:\ComfyUI\api\image_t2i.json .\
RunComfyUi -Image photo.jpg D:\ComfyUI\api\image_edit.json .\edited.png
```

Requires a ComfyUI server with [ComfyUI-Connect](https://github.com/Good-Dream-Studio/ComfyUI-Connect); the workflow is uploaded to it (under its file name) unless `-UploadWorkflows false`.
The types are compiled against the assemblies of the tool, so it can't be published as a single file.

Exit codes: 0-2 as all tools (0 also when the options are shown), 3 if running the workflow failed (connection, server or workflow error, cancelled), 4 if the workflow returned no results, see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`RunComfyUi.csproj`](RunComfyUi.csproj)
- **Builds on:** [SysWeaver.AI.ComfyUi](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.AI.ComfyUi/README.md), [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md), [SysWeaver.Script](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Script/README.md) (Roslyn)
