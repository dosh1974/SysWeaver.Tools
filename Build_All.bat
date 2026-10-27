@echo off
rem Build (publishes to a folder) all projects that have a publish profile, nothing is deployed anywhere!
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

$failed = @()
$built = @()
$projects = Get-ChildItem -LiteralPath $root -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Properties\PublishProfiles\FolderProfile.pubxml') }
foreach ($p in $projects)
{
    $name = $p.Name
    $csproj = Join-Path $p.FullName "$name.csproj"
    $profile = Join-Path $p.FullName 'Properties\PublishProfiles\FolderProfile.pubxml'
    $publishDir = ([xml](Get-Content -LiteralPath $profile -Raw)).Project.PropertyGroup.PublishDir
    $src = [IO.Path]::GetFullPath((Join-Path $p.FullName $publishDir)).TrimEnd('\')

    Write-Host ''
    Write-Host "=== $name" -ForegroundColor Cyan
    # Start from a clean publish folder
    if (Test-Path -LiteralPath $src) { Remove-Item -LiteralPath $src -Recurse -Force }
    & dotnet publish $csproj -c Release -p:PublishProfile=FolderProfile -nologo -v:minimal
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $src))
    {
        Write-Host "    Publish FAILED!" -ForegroundColor Red
        $failed += $name
        continue
    }else 
	{
        $built += $name
	}
}

Write-Host ''
if ($built.Count -gt 0)
{
    Write-Host "BUILT: $($built -join ', ')" -ForegroundColor Green
}

if ($failed.Count -gt 0)
{
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All $($projects.Count) projects(s) built (published)." -ForegroundColor Green
exit 0
