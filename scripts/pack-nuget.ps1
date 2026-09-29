#!/usr/bin/env pwsh
<#
.SYNOPSIS
    构建并打包 SevenZipLite.Native NuGet 包。
    Build and pack the SevenZipLite.Native NuGet package.

.PARAMETER Version
    可选，语义化版本号（如 1.2.3 或 1.2.3-preview.1），不传则用
    Directory.Build.props 里的默认值。
    Optional semantic version (e.g. 1.2.3 or 1.2.3-preview.1); falls back to the
    default in Directory.Build.props if omitted.

.PARAMETER OutputDir
    可选，产物输出目录，默认 ./artifacts
    Optional output directory, defaults to ./artifacts

.EXAMPLE
    ./scripts/pack-nuget.ps1
.EXAMPLE
    ./scripts/pack-nuget.ps1 -Version 1.0.0
.EXAMPLE
    ./scripts/pack-nuget.ps1 -Version 1.0.0-rc.1 -OutputDir .\out

.NOTES
    前置条件 / Prerequisites:
    - 已安装 .NET SDK（与 src/SevenZipLite/SevenZipLite.csproj 里的 TargetFramework 匹配）
      .NET SDK installed (matching the TargetFramework in SevenZipLite.csproj)
    - 想要打进包里的原生库已经放好在仓库根 native-libs/<rid>/ 下（见该目录 README.md）；
      缺失的 RID 会被自动跳过，不会导致失败，但产出的包在对应平台上无法工作。
      Native binaries you want bundled are in place under the repo-root
      native-libs/<rid>/ (see that folder's README.md); missing RIDs are silently
      skipped rather than failing, but the resulting package won't work on that
      platform.
#>
param(
    [string]$Version = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$Project = Join-Path $RepoRoot "src/SevenZipLite/SevenZipLite.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "artifacts"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "== SevenZipLite.Native NuGet 打包 / packing =="
Write-Host "项目 / project  : $Project"
Write-Host "输出目录 / output: $OutputDir"
if ($Version) {
    Write-Host "版本 / version   : $Version"
} else {
    Write-Host "版本 / version   : (使用 Directory.Build.props 默认值 / using Directory.Build.props default)"
}
Write-Host ""

Write-Host "-- 检查原生库覆盖情况 / native library coverage --"
$rids = @(
    @{ Rid = "win-x64";       File = "native-libs/win-x64/7zlite.dll" },
    @{ Rid = "win-x86";       File = "native-libs/win-x86/7zlite.dll" },
    @{ Rid = "win-arm64";     File = "native-libs/win-arm64/7zlite.dll" },
    @{ Rid = "linux-x64";     File = "native-libs/linux-x64/lib7zlite.so" },
    @{ Rid = "android-arm64"; File = "native-libs/android-arm64/lib7zlite.so" },
    @{ Rid = "android-arm";   File = "native-libs/android-arm/lib7zlite.so" },
    @{ Rid = "android-x64";   File = "native-libs/android-x64/lib7zlite.so" },
    @{ Rid = "android-x86";   File = "native-libs/android-x86/lib7zlite.so" }
)
foreach ($entry in $rids) {
    $full = Join-Path $RepoRoot $entry.File
    if (Test-Path $full) {
        Write-Host ("  [x] {0,-14} {1}" -f $entry.Rid, $full)
    } else {
        Write-Host ("  [ ] {0,-14} (缺失/missing: {1})" -f $entry.Rid, $full)
    }
}
Write-Host ""

$versionArgs = @()
if ($Version) {
    $versionArgs = @("-p:Version=$Version")
}

dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet build $Project -c Release @versionArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

dotnet pack $Project -c Release --no-build -o $OutputDir @versionArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

Write-Host ""
Write-Host "== 完成 / done =="
Get-ChildItem -Path $OutputDir -Filter "*.nupkg" | ForEach-Object { Write-Host "  $($_.FullName)" }
Get-ChildItem -Path $OutputDir -Filter "*.snupkg" | ForEach-Object { Write-Host "  $($_.FullName)" }
