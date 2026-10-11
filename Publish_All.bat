@echo off
rem Publishes (Release) all tools that have a publish profile and syncs the output to ..\SysWeaver\_tools
rem   - .pdb files and createdump.exe are not copied
rem   - .xml files are only copied if prefixed with "SysWeaver." or named after the tool itself
rem   - files are only copied if their content differs from the target
rem   - files in the target that aren't in the (filtered) publish output are removed
rem Pass any argument to skip the pause at the end.
setlocal
cd /D "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText('%~f0'); iex $s.Substring($s.LastIndexOf('##' + 'PS_SCRIPT##'))"
set RC=%ERRORLEVEL%

if "%~1"=="" pause
exit /b %RC%


##PS_SCRIPT##
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$targetRoot = [IO.Path]::GetFullPath((Join-Path $root '..\SysWeaver\_tools'))

# Project name -> folder name in the target (defaults to the project name)
$folderMap = @{ 'Compress' = 'compress'; 'SvgOpt' = 'svg_opt' }

function Test-SameContent([string]$a, [string]$b)
{
    $fa = Get-Item -LiteralPath $a
    $fb = Get-Item -LiteralPath $b
    if ($fa.Length -ne $fb.Length) { return $false }
    return (Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash
}

function Sync-Folder([string]$src, [string]$dst, [string]$appName)
{
    $srcFiles = Get-ChildItem -LiteralPath $src -Recurse -File | Where-Object {
        $ext = $_.Extension.ToLowerInvariant()
        if ($ext -eq '.pdb') { return $false }
        if ($_.Name -ieq 'createdump.exe') { return $false }
        if ($ext -eq '.xml') { return $_.Name.StartsWith('SysWeaver.', [StringComparison]::OrdinalIgnoreCase) -or ($_.Name -ieq "$appName.xml") }
        return $true
    }
    $keep = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $copied = 0; $removed = 0
    foreach ($f in $srcFiles)
    {
        $rel = $f.FullName.Substring($src.Length).TrimStart('\')
        [void]$keep.Add($rel)
        $d = Join-Path $dst $rel
        if ((Test-Path -LiteralPath $d -PathType Leaf) -and (Test-SameContent $f.FullName $d)) { continue }
        $dir = Split-Path -Parent $d
        if (-not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
        Copy-Item -LiteralPath $f.FullName -Destination $d -Force
        Write-Host "    Copied:  $rel"
        ++$copied
    }
    if (Test-Path -LiteralPath $dst)
    {
        foreach ($f in Get-ChildItem -LiteralPath $dst -Recurse -File -Force)
        {
            $rel = $f.FullName.Substring($dst.Length).TrimStart('\')
            if ($keep.Contains($rel)) { continue }
            Remove-Item -LiteralPath $f.FullName -Force
            Write-Host "    Removed: $rel"
            ++$removed
        }
        # Remove empty folders, deepest first
        Get-ChildItem -LiteralPath $dst -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force)) { Remove-Item -LiteralPath $_.FullName -Force }
        }
    }
    Write-Host "    $copied file(s) copied, $removed file(s) removed, $($srcFiles.Count - $copied) unchanged."
}

$failed = @()
$projects = Get-ChildItem -LiteralPath $root -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Properties\PublishProfiles\FolderProfile.pubxml') }
foreach ($p in $projects)
{
    $name = $p.Name
    $csproj = Join-Path $p.FullName "$name.csproj"
    $profile = Join-Path $p.FullName 'Properties\PublishProfiles\FolderProfile.pubxml'
    $folder = if ($folderMap.ContainsKey($name)) { $folderMap[$name] } else { $name }
    $dst = Join-Path $targetRoot $folder
    $publishDir = ([xml](Get-Content -LiteralPath $profile -Raw)).Project.PropertyGroup.PublishDir
    $src = [IO.Path]::GetFullPath((Join-Path $p.FullName $publishDir)).TrimEnd('\')

    Write-Host ''
    Write-Host "=== $name -> $dst" -ForegroundColor Cyan
    # Start from a clean publish folder so stale files never reach the target
    if (Test-Path -LiteralPath $src) { Remove-Item -LiteralPath $src -Recurse -Force }
    & dotnet publish $csproj -c Release -p:PublishProfile=FolderProfile -nologo -v:minimal
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $src))
    {
        Write-Host "    Publish FAILED, target left untouched!" -ForegroundColor Red
        $failed += $name
        continue
    }
    Sync-Folder $src $dst $name
}

Write-Host ''
if ($failed.Count -gt 0)
{
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All $($projects.Count) tool(s) published and synced." -ForegroundColor Green
exit 0
