# API Reference (`SevenZipLite` library)

> [中文版本](./SevenZipLite.md)

Namespace: `SevenZipLite` (NuGet package `SevenZipLite.Native`). This document covers the
public, user-facing API surface. Types under `SevenZipLite.Native`/`SevenZipLite.Callbacks`/
`SevenZipLite.Streams` are `internal` interop implementation details and out of scope here
(see `docs/development-notes.zh-CN.md`, Chinese only, and the source comments if you need
them). This document also covers a few `SevenZipLite.SelfTest` helpers that are useful to
consumers, not just this repo's own demos.

## Table of contents

- [`ArchiveFormat`](#archiveformat)
- [`ArchiveFormatExtensions`](#archiveformatextensions)
- [`SevenZipCompressor`](#sevenzipcompressor)
- [`SevenZipArchive`](#sevenziparchive)
- [`ExtractItemInfo`](#extractiteminfo)
- [`ZipMethodOverride`](#zipmethodoverride)
- [Exceptions and error handling](#exceptions-and-error-handling)
- [Threading and concurrency](#threading-and-concurrency)
- [Native library loading and platform requirements](#native-library-loading-and-platform-requirements)
- [`SevenZipLite.SelfTest` (optional: test/benchmark utilities)](#sevenziplideselftest-optional-testbenchmark-utilities)

---

## `ArchiveFormat`

```csharp
public enum ArchiveFormat
{
    SevenZip,
    Zip,
    Tar,
    TarGZip,
}
```

The four archive formats this library supports. RAR/ISO/CAB and other formats the underlying
7-Zip engine can theoretically read are **not** exposed — this is a deliberate scope
restriction, not a capability limitation of the underlying (unmodified, official) 7-Zip engine.

## `ArchiveFormatExtensions`

```csharp
public static ArchiveFormat DetectFromFileName(string path);
public static bool SupportsPassword(this ArchiveFormat format);
```

| Member | Description |
|---|---|
| `DetectFromFileName(path)` | Guesses the format from the file extension (`.7z` → `SevenZip`, `.zip` → `Zip`, `.tar` → `Tar`, `.tar.gz`/`.tgz` → `TarGZip`). Throws `NotSupportedException` for unrecognized extensions. |
| `format.SupportsPassword()` | Returns `true` only for `SevenZip`/`Zip`; `Tar`/`TarGZip` return `false` (those formats have no native encryption). |

## `SevenZipCompressor`

High-level API for creating archives.

```csharp
public static class SevenZipCompressor
{
    public sealed class Entry
    {
        public required string DiskPath { get; init; }
        public required string ArchivePath { get; init; }
    }

    public static IReadOnlyList<string> CreateArchive(
        IReadOnlyList<Entry> entries,
        string outputArchivePath,
        ArchiveFormat format,
        string? password = null,
        long volumeSize = 0,
        ZipMethodOverride? zipMethodOverride = null,
        int? threadCount = null);
}
```

### `Entry`

| Member | Description |
|---|---|
| `DiskPath` | Real source file path on disk. |
| `ArchivePath` | Relative path inside the archive; `/` is recommended as the separator for cross-platform consistency. |

### `CreateArchive`

| Parameter | Description |
|---|---|
| `entries` | Files to pack (only files are supported — there's no explicit "add empty directory" entry; if you need to preserve an empty directory, put a placeholder file in it). |
| `outputArchivePath` | Final archive path, **without** a volume-number suffix; this is the final file name when not splitting into volumes. |
| `format` | Archive format; `TarGZip` internally builds a temporary `.tar` first, then wraps it in gzip. |
| `password` | Optional password, only effective for `SevenZip`/`Zip`; a non-null value is silently ignored for formats that don't support passwords. |
| `volumeSize` | Volume size in bytes, `0` means no splitting; when greater than 0, `outputArchivePath.001`, `.002`, ... are created in the same directory. The return value is the list of actually-created volume paths. |
| `zipMethodOverride` | Only meaningful when `format == Zip`; see [`ZipMethodOverride`](#zipmethodoverride). |
| `threadCount` | Number of compression threads; defaults to `Environment.ProcessorCount` when `null`. Pass `1` to force single-threaded compression. See [Threading and concurrency](#threading-and-concurrency). |

**Return value**: the list of archive files actually produced — a single-element list
`[outputArchivePath]` when not splitting, or `[outputArchivePath.001, outputArchivePath.002, ...]`
when splitting into volumes.

**Example**:

```csharp
using SevenZipLite;

var entries = new List<SevenZipCompressor.Entry>
{
    new() { DiskPath = @"C:\data\report.pdf", ArchivePath = "report.pdf" },
    new() { DiskPath = @"C:\data\photo.jpg",  ArchivePath = "images/photo.jpg" },
};

// Plain 7z, no splitting, no encryption
var volumes = SevenZipCompressor.CreateArchive(entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);

// Encrypted zip, split into 50MB volumes
var volumes2 = SevenZipCompressor.CreateArchive(
    entries, @"C:\out\backup.zip", ArchiveFormat.Zip,
    password: "s3cr3t", volumeSize: 50L * 1024 * 1024);

// Force single-threaded compression (e.g. on a memory/CPU-constrained device)
var volumes3 = SevenZipCompressor.CreateArchive(
    entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip, threadCount: 1);
```

## `SevenZipArchive`

High-level API for reading/extracting archives.

```csharp
public static class SevenZipArchive
{
    public static List<ExtractItemInfo> List(string archivePathOrFirstVolume, ArchiveFormat format);
    public static void ExtractAll(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir, string? password = null);
}
```

| Member | Description |
|---|---|
| `List(path, format)` | Lists every entry in the archive (including directory entries). `path` can be a full path, the first volume of a split archive (e.g. `xxx.7z.001`), or the base name of a split archive (`xxx.7z`, which auto-discovers `.001`/`.002`/...). |
| `ExtractAll(path, format, outputDir, password)` | Extracts everything into `outputDir` (created automatically). `password` is ignored for formats that don't support it. Wrong password or corrupted data throws `InvalidOperationException`. |

**Example**:

```csharp
using SevenZipLite;

var items = SevenZipArchive.List(@"C:\out\backup.7z", ArchiveFormat.SevenZip);
foreach (var item in items)
    Console.WriteLine($"{item.Path}\t{(item.IsDir ? "<DIR>" : item.Size.ToString())}");

SevenZipArchive.ExtractAll(@"C:\out\backup.zip", ArchiveFormat.Zip, @"C:\restore", password: "s3cr3t");

// Split archives: passing either the base name or the first volume works
SevenZipArchive.ExtractAll(@"C:\out\backup.7z", ArchiveFormat.SevenZip, @"C:\restore");       // auto-discovers .001/.002/...
SevenZipArchive.ExtractAll(@"C:\out\backup.7z.001", ArchiveFormat.SevenZip, @"C:\restore");   // same effect
```

## `ExtractItemInfo`

```csharp
public sealed class ExtractItemInfo
{
    public required uint Index { get; init; }
    public required string Path { get; init; }
    public required bool IsDir { get; init; }
    public long Size { get; init; }
}
```

Entry information returned by `SevenZipArchive.List`. `Path` always uses `/` as the separator
(backslashes from Windows-created archives are normalized internally). `Size` is typically `0`
for directory entries.

## `ZipMethodOverride`

```csharp
public enum ZipMethodOverride
{
    Store = 0,
    Deflate = 8,
}
```

Explicitly selects the zip compression method (matching the native 7-Zip
`NFileHeader::NCompressionMethod` values), implemented via the standard
`ISetProperties::SetProperties` call with `name="m"` — the same mechanism as the 7-Zip
command-line `-mm=` switch. Only meaningful for
`SevenZipCompressor.CreateArchive(..., format: ArchiveFormat.Zip, ...)`; ignored for other
formats.

- `Store`: plain copy, no compression, fastest, good for files that are already compressed
  (images/videos/already-compressed archives).
- `Deflate`: the standard zip compression algorithm, best compatibility (recognized by nearly
  every unzip tool).

## Exceptions and error handling

This library does not define its own exception types; it throws standard BCL exceptions:

| Scenario | Exception type |
|---|---|
| Native call returns a non-`S_OK` HRESULT (wrong password, corrupted data, unsupported operation, etc.) | `InvalidOperationException`, with the specific HRESULT (e.g. `0x80004005`) embedded in `Message` for further diagnosis |
| Format can't be guessed from the file name (`DetectFromFileName`) | `NotSupportedException` |
| Native library not found (`7zlite.dll`/`lib7zlite.so`) | `System.DllNotFoundException` (built into .NET) — see [Native library loading and platform requirements](#native-library-loading-and-platform-requirements) |
| Ordinary .NET I/O errors (disk full, permission denied, path not found, etc.) | The corresponding standard `System.IO` exception (`IOException`/`UnauthorizedAccessException`/`DirectoryNotFoundException`, etc.), propagated unwrapped |

Quick HRESULT reference (the common ones):

| HRESULT | Meaning | Common trigger |
|---|---|---|
| `0x80004005` (E_FAIL) | Generic failure | Wrong password, corrupted data, archive/format mismatch |
| `0x80070057` (E_INVALIDARG) | Invalid argument | An internal bug (fixed once already, see `docs/development-notes.zh-CN.md`); should not occur in normal use |

## Threading and concurrency

- `SevenZipCompressor.CreateArchive` is **synchronous/blocking** — it occupies the calling
  thread until compression finishes. If calling from a UI thread, wrap it in `Task.Run(...)`
  yourself (all three sample apps do exactly this).
- Compression uses multiple threads internally by default (`threadCount` parameter, defaulting
  to `Environment.ProcessorCount`) — this refers to 7-Zip's own native multi-threaded
  compression, **not** "call `CreateArchive` concurrently from multiple threads". Do **not**
  call `CreateArchive`/`ExtractAll` concurrently against the same output path from multiple
  threads; concurrent calls against different paths have no known issues but haven't been
  specifically tested/optimized either.
- The extraction path (`ExtractAll`) currently has **no** multi-threaded acceleration — it's
  the 7-Zip engine's own single-threaded, sequential extraction.
- For multi-file zip/tar scenarios, the benefit of multi-threaded compression depends on
  whether the file count is ≥ the thread count (7-Zip dispatches work per-file to worker
  threads; a single file is never split across threads). The 7z/LZMA2 format does show
  measured multi-threaded speedup even for a single large file.

## Native library loading and platform requirements

This library loads the native library via `[LibraryImport("7zlite")]`; .NET's default probing
rules look in the application's output directory:

| Platform | Expected file name | How it's obtained |
|---|---|---|
| Windows (win-x64/win-x86/win-arm64) | `7zlite.dll` | The official `7z.dll` from 7-zip.org, simply renamed, unmodified |
| Linux (linux-x64) | `lib7zlite.so` | Built locally from the unmodified vendored source under `native/7zip-src` |
| Android (arm64-v8a/armeabi-v7a/x86_64/x86) | `lib7zlite.so` | Same as above, cross-compiled for the matching ABI |

When referencing this library via the `SevenZipLite.Native` NuGet package:
- If the consuming project declares a `RuntimeIdentifier` (or publishes self-contained/as a
  single file), the standard `runtimes/{rid}/native/*` mechanism handles it automatically.
- For plain, non-RID `dotnet run`/`dotnet build` (e.g. typical desktop app debugging), the
  package's bundled `build`/`buildTransitive` MSBuild `.targets` files automatically copy the
  matching native library to the output directory based on the current build machine's
  OS/architecture (covers only the four desktop RIDs: win-x64/win-x86/win-arm64/linux-x64).
- **Android/.NET MAUI projects are not covered by the mechanisms above**: see
  `samples/SevenZipLite.MauiDemo/SevenZipLite.MauiDemo.csproj` for the `<AndroidNativeLibrary>`
  pattern and wire the matching ABI's `lib7zlite.so` into your own MAUI/Android project
  manually.

A missing native library throws `System.DllNotFoundException: Unable to load DLL '7zlite'...`
— a built-in .NET exception, not something this library defines itself.

## `SevenZipLite.SelfTest` (optional: test/benchmark utilities)

This project was originally built for this repo's own demos/tests, but its API is `public`;
if you want to run a similar "does this 7-Zip wrapper actually work reliably on my target
device" check in your own project, you can reference it directly (`ProjectReference`, or copy
the source — it isn't published as its own separate NuGet package).

### `SelfTestRunner.RunAll(string rootDir)`

Runs 13 round-trip correctness test cases across format × volume-splitting × password × method
override combinations (create → list → extract → byte-for-byte comparison), returning
`IReadOnlyList<SelfTestResult>` (each with `Name`/`Pass`/`Detail`/`Log`).

### `PerfTestRunner.Run(...)`

```csharp
public static PerfTestResult Run(
    string rootDir, long sizeBytes,
    ArchiveFormat format = ArchiveFormat.SevenZip,
    string? password = null,
    ZipMethodOverride? zipMethodOverride = null,
    bool keepFiles = false,
    Action<string>? onProgress = null);
```

Generates a synthetic large file of configurable size (designed for up to ~1GB), times
compression/extraction, and returns throughput (MB/s), compression ratio, and whether a
streaming SHA-256 check passed. Used to observe real-world throughput; not part of the
correctness test suite.

### `MultiThreadStressRunner.Run(...)`

```csharp
public static bool Run(
    string rootDir, int iterations, int fileCount,
    ArchiveFormat format = ArchiveFormat.Zip,
    string? password = null,
    Action<string>? log = null);
```

Repeatedly creates/extracts an archive containing several files and SHA-256-verifies the
result, using a higher file count/iteration count to provide stronger confidence than
`SelfTestRunner` that the multi-threaded compression path is stable. See the multi-threading
bug investigation in `docs/development-notes.zh-CN.md` (Chinese only) for background.
