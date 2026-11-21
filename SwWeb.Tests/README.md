# SwWeb.Tests

[⬆ SysWeaver.Tools overview](../README.md)

> xUnit tests of [SwWeb](../SwWeb/README.md): the lexers, the bundler, every `check` rule family, `migrate` / `upgrade`, `verify-build` and the README contracts.

| | |
|---|---|
| **Project** | [`SwWeb.Tests.csproj`](SwWeb.Tests.csproj) (.NET 11, xUnit) |
| **Kind** | Test project |

## Purpose

SwWeb is a heuristic tool (a token based linter and a migrator without a parser), so its behaviour is pinned by examples: for each rule the mistake it must find, the right pattern it must leave alone and the allow comment that silences it; for `migrate` the exact text before and after. A frozen corpus of planted mistakes and generated variants keeps the rules from being tuned to one spelling.

## What it tests

| Test file | Area |
|---|---|
| [`JsLexerTests.cs`](JsLexerTests.cs), [`HtmlScannerTests.cs`](HtmlScannerTests.cs) | The JavaScript tokenizer (strings, templates, regex vs division, comments, private names, round trip) and the HTML scanner |
| [`BundlerTests.cs`](BundlerTests.cs), [`BundleVerifyTests.cs`](BundleVerifyTests.cs) | `SwWeb bundle`: output format, BOM / CRLF, `;` separation, `--check`, missing sources, `LoadOnce` guards and conflicts, size budgets, the embedded `sw_bundles.json`; the source verification (syntax, undefined calls, names declared twice) |
| [`CheckerTests.cs`](CheckerTests.cs) | The contract rules of `check`: names, encoding, syntax, translation calls, template variables, JSDoc headers, external resources, legacy globals, skip patterns, `element-css` ... |
| [`DesignTests.cs`](DesignTests.cs), [`DesignMigrateTests.cs`](DesignMigrateTests.cs) | The Design v2 rules (`--design`) and their `migrate --level design` rewrites (token renames, shadows, dialog wording, `Confirm` options against `ui/overlay.js`) |
| [`GateTests.cs`](GateTests.cs), [`GateTrustTests.cs`](GateTrustTests.cs) | The gate `check --strict --design` can't be passed by pages that break at run time or hide a pattern (`no-framework`, `tr-format`, `unknown-token`, `internal-id`, `theme-include`, `tr-untranslated`, `tr-alias`, indirect forms) |
| [`PortingTrapTests.cs`](PortingTrapTests.cs), [`SessionPageTests.cs`](SessionPageTests.cs) | Traps found by porting real apps (dry run worksheet equals `check`, folders above the web roots, `sw-root`, `script-order`, library includes); messaging only pages (`sw/session.js`) |
| [`MigrateTests.cs`](MigrateTests.cs) | `migrate` / `upgrade`: include rewriting, the JavaScript renames and their scopes, argument and options conversion, css hooks, compat bundle, encoding and line endings, report |
| [`MinifierReorderTests.cs`](MinifierReorderTests.cs), [`BuildVerifierTests.cs`](BuildVerifierTests.cs) | Rule `minifier-reorder` on cases run through the build's minifier, and `verify-build` on built outputs |
| [`UxRuleTests.cs`](UxRuleTests.cs), [`UxPatternTests.cs`](UxPatternTests.cs), [`UxFamilyTests.cs`](UxFamilyTests.cs), [`UxFlowTests.cs`](UxFlowTests.cs) | The `ux-*` rules of the UX guide: per rule, the api usage patterns, the rule families (secrets, raw html, formatting, pointer events, silent failures) and the call structure / value flow |
| [`UxVariantTests.cs`](UxVariantTests.cs) | Generated variants per rule family (each mistake and right pattern in many spellings); `SWWEB_VARIANT_DUMP=<file>` writes the failing ones |
| [`CorpusTests.cs`](CorpusTests.cs) | The frozen corpus [`Corpus/`](Corpus) (pages with planted mistakes and held-out sets, expectations in [`Corpus/expected.txt`](Corpus/expected.txt)); prints the catch rate per set |
| [`ReadmeContractTests.cs`](ReadmeContractTests.cs) | `SwWeb contracts`: the api rows of the framework READMEs, the generated browser manifest |
| [`RepositoryFixtureTests.cs`](RepositoryFixtureTests.cs) | Lexes every web file of the sibling SysWeaver repository and checks that `sw_tokens.txt` matches the tokens the framework defines |
| [`TestFolder.cs`](TestFolder.cs) | Helper: a temporary folder (below the system temp folder) that is deleted on dispose |

## Running it

```
dotnet test SysWeaver.Tools/SwWeb.Tests
```

The tests write only to temporary folders. Some tests read the sibling [SysWeaver](https://github.com/dosh1974/SysWeaver) repository (`..\SysWeaver` next to `SysWeaver.Tools`: framework sources, READMEs, built outputs) and do nothing when it isn't there; `BuildVerifierTests.TheSysWeaverBuildOutputsVerify` checks its last build's outputs. The corpus is copied to the output folder (`Corpus\**`).

## Relationships

- **Tests:** [SwWeb](../SwWeb/README.md) (project reference; `InternalsVisibleTo` gives access to the internal command line runner)
- **Reads:** the SysWeaver repository next to `SysWeaver.Tools`, when present
