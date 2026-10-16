# ComfyApiToCs

[⬆ SysWeaver.Tools overview](../README.md)

> Converts ComfyUI API format workflows (json) into C# source files with the input and output types to use with `ComfyUiService.RunWorkflow`.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Running a ComfyUI workflow from code needs an input type (the values to set) and an output type (the results). This tool writes them for one or more workflow files, so they can be regenerated whenever a workflow changes. Example: `AUK_Text_2_speech.json` becomes `AUK_Text_2_speech.cs` with the types `AukText2SpeechInput` and `AukText2SpeechOutput` (and nested types like `AukText2SpeechConfig` for inputs with an object value).

## How it fits into SysWeaver

A thin command line shell around `ComfyUiTypeGenerator.GetSource` of [SysWeaver.AI.ComfyUi](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.AI.ComfyUi/README.md) (the inputs and outputs are found from the `$Tag` / `#Tag` node title annotations, the member names are validated with the same matching rules as `RunWorkflow`), built on the "files in → folder out" shell (`FilesToFolderTool`) of [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md).

## Key features

- One `.cs` file per workflow, with the same name as the workflow file, containing the input type, the output type and any nested types
- The type base name is the cleaned up file name: words split at non letters / digits and case changes, each with an upper case first letter (`AUK_Text_2_speech` → `AukText2Speech`, `my-workflow v2` → `MyWorkflowV2`)
- Properties initialized with the workflow defaults by default (`-Properties false` for fields, `-Defaults false` to leave inputs null so the workflow default is used)
- Inputs with an object value get nested types (all members initialized, `[JsonPropertyName]` when the json name differs), `-Nested false` uses `JsonElement?`
- Namespace `SysWeaver.Ai.ComfyUi` by default (`-Namespace`, `-FileScoped false` for a namespace block)
- An unchanged `.cs` file isn't rewritten (no needless rebuilds); a file that can't be converted (ex: a UI format workflow) is reported, the others are converted
- Files are processed in parallel (sequentially in DEBUG builds)

## Using it

```
ComfyApiToCs [options] <SourceFiles> [DestFolder]

  SourceFiles   API format workflow files (ComfyUI "Workflow > Export (API)"), wildcard masks separated by ';',
                a trailing '+' searches sub folders (ex: .\workflows\*.json+)
  DestFolder    The output folder (default: the folder of the source file)

  -Namespace <ns>        Namespace of the types (default SysWeaver.Ai.ComfyUi), empty for none
  -FileScoped false      Use a namespace block instead of a file scoped namespace
  -TypeName <name>       Base name of the types (default: the cleaned up file name)
  -InputSuffix <s>       Input type suffix (default Input), -OutputSuffix <s> (default Output)
  -Properties false      Generate fields instead of properties
  -Defaults false        Don't initialize the inputs with the workflow defaults (null = the workflow default is used)
  -Nested false          Use JsonElement? for object inputs instead of nested types
  -Comments false        No xml documentation comments
  -ShortNames false      Include the tag in the member names (ex: ResolutionMegapixels)
  -Explicit              Map all members with [ComfyUiName("tag.input")]
  -Bypass                Add a bool? member per annotated node (false bypasses the node)
  -Casing <c>            Member names: Pascal (default), Camel, Snake or Original
  -FileType <t>          Type of media file inputs: ComfyUiFile (default), byte[], Uri or String
  -OutputType <t>        Type of the outputs: byte[] (default), byte[][], List<byte[]>, String, String[] or List<String>
  -UiFolder <folder>     Folder with UI format workflows, used to annotate subgraph inputs
  -Overwrite false       Skip existing .cs files
  -?                     Help
```

Ex: `ComfyApiToCs -Namespace MyApp.Comfy D:\ComfyUI\api\*.json .\Generated`

Exit codes: 0-2 as all tools, 3 if one or more files couldn't be converted (the others are), see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`ComfyApiToCs.csproj`](ComfyApiToCs.csproj)
- **Builds on:** [SysWeaver.AI.ComfyUi](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.AI.ComfyUi/README.md), [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Common](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Common/README.md)
