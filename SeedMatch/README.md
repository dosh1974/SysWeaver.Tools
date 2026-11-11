# SeedMatch

[⬆ SysWeaver.Tools overview](../README.md)

> Finds an app seed (`AppSeed`) whose generated application theme is closest to some given colors, ex: the colors of a logo.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

The application theme of a SysWeaver app (the `application` theme of `common/theme.css`, the generated icon, logo and banner) is derived from `AppSeed` in the `SysWeaver.AppInfo` service parameters.
When an app has a hand made logo, SeedMatch picks the seed whose theme fits that logo best: it tests random seeds, compares the four theme colors of each seed with the input colors (perceptual OKLab distance) and lists the closest ones.

The theme only depends on a few random values (main hue, saturation, complement and accent spread), so about 2400 distinct themes exist: the default of 1,000,000 tested seeds covers all of them (about 1.5 s).

## Using it

```
SeedMatch [Name=]#rrggbb[*weight] [..] [-tries:N] [-top:N] [-start:N]
```

- `Name=#rrggbb` compares the color with that theme color: `Background` (`--ThemeBackground`), `Main` (`--ThemeMain`), `Acc1` (`--ThemeAcc1`) or `Acc2` (`--ThemeAcc2`)
- `#rrggbb` (no name) compares the color with the closest of the four theme colors
- `*weight` weights a color (default 1), ex: `Main=#e522a4*2`
- `-tries:N` the number of random seeds to test (default 1000000), `-top:N` the number of distinct themes to list (default 5), `-start:N` the seed of the generator that picks the seeds (default 1: the same input always gives the same result)

The distance is the OKLab distance * 100 (about 1 is just noticeable), weighted and averaged over the given colors. Seed 0 (derive the seed from the app name) is never tested.

Example (the QuizzWeaver logo):

```
SeedMatch Background=#08190b Main=#e522a4 Acc1=#50e55c Acc2=#50e575
...
Best AppSeed: 1241796862
```

Exit codes: 0 on success or for help, 1 on an error (ex: an invalid color), see [EXIT CODES](../README.md#exit-codes).

## Relationships

- **Project:** [`SeedMatch.csproj`](SeedMatch.csproj)
- **Builds on:** `SysWeaver.Common` (`HashColors`, the same theme colors as `HttpServerBase` and the explore module's `SeedColors`)
