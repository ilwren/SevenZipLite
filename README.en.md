# SevenZipLite

[![License: LGPL v2.1+](https://img.shields.io/badge/license-LGPL--2.1--or--later-blue.svg)](./LICENSE)
[![NuGet](https://img.shields.io/badge/nuget-SevenZipLite.Native-blue)](https://www.nuget.org/packages/SevenZipLite.Native)

> [中文](./README.md) | **English**

A lightweight .NET wrapper that P/Invokes the official, unmodified 7-Zip native engine
(`7z.dll`/`7z.so`), providing read/write support for 7z / zip / tar / tar.gz (including
multi-volume archives and passwords) across Windows / Linux / Android. It's not a reimplemented
managed compression stack — compression ratio and format compatibility exactly match the
official 7-Zip command-line tools.

> ⚠️ **AI-generated disclosure**: the C#/.NET wrapper code, build scripts, CI workflow, and
> documentation in this repository were primarily generated with AI assistance. Review the
> code quality and security yourself before use — see [`DISCLAIMER.md`](./DISCLAIMER.md).

## Table of contents

- [Features](#features)
- [Supported platforms and formats](#supported-platforms-and-formats)
- [Quick start](#quick-start)
- [Project structure](#project-structure)
- [Build and run](#build-and-run)
- [Testing](#testing)
- [Building the NuGet package](#building-the-nuget-package)
- [Releasing (GitHub Actions)](#releasing-github-actions)
- [Known limitations](#known-limitations)
- [Documentation](#documentation)
- [License](#license)
- [Contributing](#contributing)

## Features

- **Calls the official 7-Zip engine directly**: not a reimplemented compression algorithm —
  it talks to the native library built from official, unmodified 7-Zip source through its
  COM-style interfaces (`IInArchive`/`IOutArchive`), so compression ratio and format
  compatibility exactly match the official `7z`/`7za` CLI tools.
- **Four archive formats**: create and extract 7z, zip, tar, tar.gz.
- **Multi-volume archives**: create/read split `.7z.001`/`.002`... archives.
- **Password protection**: 7z (full-archive AES-256 encryption), zip (ZipCrypto/AES).
- **Multi-threaded compression**: control the compression thread count (`threadCount`
  parameter) to make full use of multi-core CPUs.
- **Selectable zip compression method**: `Store` (no compression) or `Deflate`.
- **Validated on three platforms**: Windows (x64/x86/arm64), Linux (x64), Android
  (arm64-v8a/armeabi-v7a/x86_64/x86), each backed by a runnable demo — WPF, a Linux console
  self-test, and .NET MAUI.
- **No extra managed dependencies**: the core library depends only on the .NET BCL — no
  third-party compression or COM interop packages.
- **Multi-targeted**: the core library targets both **`net8.0`** (current LTS) and
  **`net10.0`**, so projects that haven't upgraded to the latest .NET yet can reference it
  directly.
- **WPF demo follows the system light/dark theme**: `samples/SevenZipLite.WpfDemo` uses the
  WPF Fluent theme introduced in .NET 9 (`ThemeMode="System"`), automatically switching its
  appearance to match the Windows light/dark mode setting.

## Supported platforms and formats

| RID | Native library | Status |
|---|---|---|
| `win-x64` | `7zlite.dll` | ✅ Verified |
| `win-x86` | `7zlite.dll` | ✅ Verified |
| `win-arm64` | `7zlite.dll` | ✅ Verified |
| `linux-x64` | `lib7zlite.so` | ✅ Verified |
| `android-arm64` (`arm64-v8a`) | `lib7zlite.so` | ✅ Verified (.NET MAUI) |
| `android-arm` (`armeabi-v7a`) | `lib7zlite.so` | ✅ Verified (.NET MAUI) |
| `android-x64` (`x86_64`) | `lib7zlite.so` | ✅ Verified (.NET MAUI emulator) |
| `android-x86` (`x86`) | `lib7zlite.so` | ✅ Verified (.NET MAUI emulator) |

| Format | Read | Write | Volumes | Password |
|---|---|---|---|---|
| 7z | ✅ | ✅ | ✅ | ✅ (AES-256) |
| zip | ✅ | ✅ | ✅ | ✅ (ZipCrypto/AES, depends on the writer) |
| tar | ✅ | ✅ | ✅ | ❌ (format has no encryption) |
| tar.gz | ✅ | ✅ | ✅ | ❌ (format has no encryption) |

RAR/ISO/CAB and other formats are not supported — this is an API-level scope restriction; see
[`docs/api/SevenZipLite.en.md`](./docs/api/SevenZipLite.en.md) for details.

## Quick start

```bash
dotnet add package SevenZipLite.Native
```

```csharp
using SevenZipLite;

// Compress
var entries = new List<SevenZipCompressor.Entry>
{
    new() { DiskPath = @"C:\data\report.pdf", ArchivePath = "report.pdf" },
};
SevenZipCompressor.CreateArchive(entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);

// Extract
SevenZipArchive.ExtractAll(@"C:\out\backup.7z", ArchiveFormat.SevenZip, @"C:\restore");
```

For the full API (multi-volume archives, passwords, zip method selection, threading, exception
handling, native library loading details) see the
[**API documentation**](./docs/api/SevenZipLite.en.md) ([中文](./docs/api/SevenZipLite.md)).

> ⚠️ The NuGet package has not been published to nuget.org yet (see the "Releasing" section
> below for the publishing plan). In the meantime, pack it locally with
> [`scripts/pack-nuget.sh`](./scripts/pack-nuget.sh) and test against a local feed, or just add
> a `ProjectReference` to this repo's `src/SevenZipLite/SevenZipLite.csproj`.

## Project structure

```
SevenZipLite/
├── src/
│   └── SevenZipLite/              # Core library (packaged as the SevenZipLite.Native NuGet package)
├── tests/
│   ├── SevenZipLite.SelfTest/     # Reusable self-test/perf/stress test runners (public API)
│   └── SevenZipLite.Tests/        # Linux console self-test program (--stress runs the stress test)
├── samples/
│   ├── SevenZipLite.WpfDemo/      # Windows WPF visual demo
│   └── SevenZipLite.MauiDemo/     # .NET MAUI (Android) demo
├── native/
│   └── 7zip-src/                  # Official, unmodified 7-Zip source (kept as-is, used to build native libs locally)
├── native-libs/                   # Compiled native binaries per RID (see its README.md for provenance)
├── licenses/                      # Full third-party license texts (LGPL-2.1 / 7-Zip / unRAR)
├── docs/
│   ├── api/                       # API reference (Chinese and English)
│   ├── development-notes.zh-CN.md # Deep bug investigation notes (multi-threading bug, PROPVARIANT bug, etc.) — Chinese only
│   └── investigation-report.zh-CN.md # Early technology-selection research report — Chinese only
├── scripts/
│   ├── pack-nuget.sh / .ps1       # One-command NuGet package build
├── .github/workflows/
│   └── release.yml                # Builds and publishes a GitHub Release when a tag is pushed
├── Directory.Build.props          # Shared package metadata (authors/version/repo URL, etc.)
├── LICENSE                        # LGPL-2.1-or-later (with a note about the native library licenses)
├── THIRD-PARTY-NOTICES.md         # Third-party license attribution (Chinese and English)
├── DISCLAIMER.md                  # AI-generated code disclaimer (Chinese and English)
└── SevenZipLite.sln
```

## Build and run

### Prerequisites

- The core library (`src/SevenZipLite`) multi-targets **`net8.0` / `net10.0`** — either
  matching [.NET SDK](https://dotnet.microsoft.com/download) is enough to build it. The
  repo's own test/demo projects are pinned to **.NET SDK 10.0**; installing the 10.0 SDK is
  recommended (it can also compile the `net8.0` target).
- The Windows-specific project (`SevenZipLite.WpfDemo`) must be built/run on Windows with
  `dotnet build`/`dotnet run`. On non-Windows platforms you can do a headless compile check
  with `-p:EnableWindowsTargeting=true`, but you cannot run its UI. This project uses the
  WPF Fluent theme's `ThemeMode` API introduced in .NET 9, so its `TargetFramework` is pinned
  to `net10.0-windows` (any `net9.0-windows` or later would work; unrelated to the core
  library's net8.0 target).
- `SevenZipLite.MauiDemo` requires the .NET MAUI workload
  (`dotnet workload install maui`) and the Android SDK/NDK.

### Build the whole repo

```bash
dotnet restore SevenZipLite.sln
dotnet build SevenZipLite.sln -c Release
```

### Run the Linux console self-test (`SevenZipLite.Tests`)

```bash
dotnet run --project tests/SevenZipLite.Tests -c Release
# Run the multi-threading stress test:
dotnet run --project tests/SevenZipLite.Tests -c Release -- --stress
```

### Run the WPF demo (Windows only)

```powershell
dotnet run --project samples\SevenZipLite.WpfDemo -c Release
```

If the native library is missing, the app shows a clear (Chinese-language) error message
pointing you to `native-libs/win-{x64,x86,arm64}/`, or explains how to copy `7z.dll` from a
local 7-Zip install and rename it to `7zlite.dll`.

### Run the MAUI demo (Android)

```bash
dotnet build samples/SevenZipLite.MauiDemo -f net10.0-android -c Debug -t:Run
```

## Testing

- **Correctness tests**: `SevenZipLite.SelfTest.SelfTestRunner.RunAll` — 13 test cases covering
  7z/zip/tar/tar.gz × multi-volume × password × zip method combinations. All three demos
  (Linux console, WPF, MAUI) wire this up and show results in their console/UI.
- **Performance tests**: `SevenZipLite.SelfTest.PerfTestRunner` — round-trip throughput test
  with a large synthetic file (up to ~1GB).
- **Multi-threading stress test**: `SevenZipLite.SelfTest.MultiThreadStressRunner` — repeated
  create/extract/verify cycles used to validate the stability of the multi-threaded
  compression path (this area had a race-condition bug that has since been fixed; see
  [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md), Chinese only, for
  the full history).

Detailed parameters for all three test utilities are documented in
[API reference §SevenZipLite.SelfTest](./docs/api/SevenZipLite.en.md#sevenziplideselftest-optional-testbenchmark-utilities).

## Building the NuGet package

```bash
# Linux / macOS
./scripts/pack-nuget.sh [version] [output-dir]
./scripts/pack-nuget.sh 1.0.0 ./artifacts

# Windows
.\scripts\pack-nuget.ps1 -Version 1.0.0 -OutputDir .\artifacts
```

The script:
1. Prints a coverage checklist for all 8 native-library RIDs (a missing RID is skipped in the
   `runtimes/` entries, but does not fail the pack).
2. Runs `dotnet restore` + `dotnet build -c Release` + `dotnet pack -c Release`.
3. Writes `SevenZipLite.Native.<version>.nupkg` and its matching `.snupkg` (symbols package) to
   the given output directory (default `./artifacts`).

Running it with no arguments uses the default version `0.1.0` from `Directory.Build.props`.

## Releasing (GitHub Actions)

[`.github/workflows/release.yml`](./.github/workflows/release.yml) triggers automatically when
a `v*`-style tag (e.g. `v1.0.0`) is pushed, and runs:

1. `SevenZipLite.Tests` self-tests (including the `--stress` stress test) on a Linux runner, as
   a pre-release quality gate.
2. Packs `.nupkg`/`.snupkg` with `scripts/pack-nuget.sh` (version taken from the tag).
3. `dotnet publish`es the WPF demo on a Windows runner (win-x64 and win-x86), zipped as
   artifacts.
4. Installs the MAUI workload on an Ubuntu runner and builds an Android APK (unsigned Release
   or Debug configuration).
5. Attaches all of the above artifacts (`.nupkg`, `.snupkg`, WPF zips, APK) to a newly created
   GitHub Release.
6. An optional nuget.org push step: **disabled by default**, only runs if `NUGET_API_KEY` is
   configured in the repo's Secrets (not needed right now, left off per the maintainer's
   request).

To trigger a release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Known limitations

- Only 7z/zip/tar/tar.gz are supported (no RAR/ISO/CAB etc.).
- Extraction has no multi-threaded acceleration (compression does).
- MAUI/Android native-library distribution does not go through the NuGet package's built-in
  `build`/`buildTransitive` mechanism; you need to wire it up manually following the
  `samples/SevenZipLite.MauiDemo` pattern (see the "Native library loading and platform
  requirements" section of the API docs).
- `native/7zip-src` currently only has `build_linux.sh`; `build_windows.*`/`build_android.*`
  scripts don't exist yet — the Windows/Android native libraries are currently obtained/built
  manually and dropped into `native-libs/`. This is a known gap; contributions for those
  scripts are welcome.
- More detailed historical bug investigation notes (a fixed multi-threading race condition, a
  PROPVARIANT struct-size issue, early Android `DllNotFoundException`/ComWrappers issues) are
  in [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md) (Chinese only).

## Documentation

| Document | Description |
|---|---|
| [`docs/api/SevenZipLite.en.md`](./docs/api/SevenZipLite.en.md) / [中文](./docs/api/SevenZipLite.md) | API reference (English/Chinese) |
| [`native-libs/README.md`](./native-libs/README.md) | Native library provenance and RID mapping table (Chinese and English) |
| [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md) | Deep bug investigation notes (Chinese only) |
| [`docs/investigation-report.zh-CN.md`](./docs/investigation-report.zh-CN.md) | Early technology-selection research report (Chinese only) |
| [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md) | Third-party license attribution (Chinese and English) |
| [`DISCLAIMER.md`](./DISCLAIMER.md) | AI-generated code disclaimer (Chinese and English) |
| [`CHANGELOG.md`](./CHANGELOG.md) | Changelog (Chinese and English) |

## License

The wrapper code (C#/XAML/scripts under `src/`, `tests/`, `samples/`, `scripts/`) is licensed
under the **GNU Lesser General Public License v2.1, or (at your option) any later version**
(LGPL-2.1-or-later) — see [`LICENSE`](./LICENSE). In short:

- You're free to use/modify/distribute this library, including commercially;
- If you modify and distribute this library's source, you must make your changes available
  under the same LGPL terms;
- Merely referencing/linking this library (without modifying its source, e.g. via the NuGet
  package) generally doesn't affect your own application's license, but you still need to
  satisfy LGPL terms such as allowing the library to be replaced/re-linked (typical approach:
  dynamic linking, avoiding static-compiling this library into a single-file executable with
  no re-linking mechanism).

The 7-Zip native binaries bundled/distributed in this repository (`native-libs/`) and their
source (`native/7zip-src/`) are likewise licensed under **LGPL-2.1** (a few files under
BSD-3/BSD-2), and the RAR-decoding code is further restricted by the unRAR license — this is
third-party code independently copyrighted by Igor Pavlov and the 7-Zip project contributors,
separate from our own wrapper code's copyright. See
[`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md) and the full license texts under
[`licenses/`](./licenses/) for details.

> ⚠️ The license summary provided here is not legal advice. If you plan to distribute this
> repository's code or native binaries in a commercial product, please review the LGPL-2.1
> terms yourself (especially the dynamic-linking and re-linkability requirements) and consult
> legal counsel.

> ⚠️ **AI-generated disclosure**: the code/docs/scripts in this repository were primarily
> generated with AI assistance — see [`DISCLAIMER.md`](./DISCLAIMER.md). Review code quality,
> security, and license compliance yourself before using, modifying, or distributing it.

## Contributing

Issues and PRs are welcome. Before submitting, please make sure:

- `dotnet build SevenZipLite.sln -c Release` produces no warnings and no errors.
- `dotnet run --project tests/SevenZipLite.Tests -c Release -- --stress` passes all test cases.
- If your change touches the native libraries, update `native-libs/README.md` and the affected
  RID notes accordingly.

> 📌 **Note**: the repository URL in `Directory.Build.props` is currently the placeholder
> `https://github.com/your-org/SevenZipLite` — replace it with your actual repository URL
> before forking/releasing.
