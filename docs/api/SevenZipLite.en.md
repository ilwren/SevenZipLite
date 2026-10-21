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
- [`SevenZipArchiveEditor` (incremental add/delete)](#sevenziparchiveeditor-incremental-adddelete)
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
        public string? DiskPath { get; init; }
        public required string ArchivePath { get; init; }
        public Func<Stream>? OpenStream { get; init; }
        public long? Length { get; init; }
        public DateTime? LastWriteTimeUtc { get; init; }
    }

    public static IReadOnlyList<string> CreateArchive(
        IReadOnlyList<Entry> entries,
        string outputArchivePath,
        ArchiveFormat format,
        string? password = null,
        long volumeSize = 0,
        ZipMethodOverride? zipMethodOverride = null,
        int? threadCount = null,
        IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool encryptHeaders = true);
}
```

### `Entry`

| Member | Description |
|---|---|
| `DiskPath` | Real source file path on disk. Must be a real filesystem path `File.OpenRead` can open directly — not a URI string or a virtual path like Android's `content://` (if the path comes from a file picker/SAF, copy the content to a real temp file first). **Mutually exclusive with `OpenStream` — exactly one of the two must be set**; setting neither or both makes `CreateArchive` throw an `ArgumentException` naming the exact index immediately, instead of a context-free `ArgumentNullException` surfacing later from an internal `File.*` call. |
| `ArchivePath` | Relative path inside the archive; `/` is recommended as the separator for cross-platform consistency. **Must not be `null`/empty** — otherwise an `ArgumentException` naming the exact index is thrown. |
| `OpenStream` | When an entry's content comes from memory/network/another non-disk source, use this delegate instead of `DiskPath` — it's invoked once every time the compression process needs to read this entry's content, and must return a fresh, from-the-start, **seekable** read-only stream (native 7-Zip calls `Seek` on input streams while compressing; non-seekable sources such as raw network streams must be buffered into a `MemoryStream` or temp file by the caller first). Setting this requires also setting `Length`, otherwise `ArgumentException` is thrown. |
| `Length` | Required only when `OpenStream` is set: the stream's exact byte length (must be ≥ 0). 7-Zip's native layer needs the accurate size written into the archive header *before* it ever reads the content, and can't infer it from the stream afterwards — an inaccurate length produces a corrupted archive whose header size doesn't match the actual data, and this isn't guaranteed to fail during compression itself. Not needed with `DiskPath`, where the length is read from the filesystem automatically. |
| `LastWriteTimeUtc` | Optional, only meaningful when `OpenStream` is set: the modification time (UTC) recorded for this entry in the archive. Leaving it unset means no modification-time property is written for the entry. Not needed with `DiskPath`, where it's read from the filesystem automatically. |

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
| `progress` | Optional progress callback reporting byte-level compression progress (see `SevenZipProgress`: `BytesCompleted`/`BytesTotal`/`Percentage`). |
| `cancellationToken` | Optional cancellation token; cancellation granularity is "between entries" — an entry currently being compressed can't be interrupted mid-way. Throws `OperationCanceledException` once cancelled. |
| `encryptHeaders` | Whether to also encrypt the archive header (file names/sizes/directory structure and other metadata), only meaningful when `format == ArchiveFormat.SevenZip` and a non-null password is supplied (corresponds to the official 7z CLI's `-mhe=on`; the zip format doesn't support this capability at all, so the value is a no-op for zip). **Defaults to `true`** — this is deliberately the opposite of the official 7z.exe CLI's default (`false`, content-only encryption, header left in the clear): if you set only a password without this flag, the file names/sizes/directory structure stay readable without the password even though the whole archive is "password protected" (`SevenZipArchive.List` succeeds with no password at all), which is a common intuition trap. Pass `false` explicitly to restore the old behavior (e.g. if you actually want others to be able to browse the archive's contents without the password). |

**Return value**: the list of archive files actually produced — a single-element list
`[outputArchivePath]` when not splitting, or `[outputArchivePath.001, outputArchivePath.002, ...]`
when splitting into volumes.

**Exceptions**: `entries` being `null` throws `ArgumentNullException`; `entries` being non-null but
containing an entry with a `null`/empty `ArchivePath`, or neither/both of `DiskPath`/`OpenStream`
set, or `OpenStream` set with a missing/negative `Length`, throws `ArgumentException` whose
message names the exact index (e.g. `entries[2].DiskPath is empty...`), so you can pinpoint which
entry is at fault directly instead of checking them one by one.

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

**Entry content coming from a `Stream` (no disk file needed)**:

```csharp
byte[] payload = GetDataFromSomewhere(); // e.g. a network download, a database BLOB, etc.

var streamEntries = new List<SevenZipCompressor.Entry>
{
    // OpenStream must return a fresh, from-the-start stream on every call — constructing a new
    // MemoryStream naturally satisfies this; if the underlying resource can only be consumed
    // once, wrap it yourself so it can be re-read.
    new()
    {
        ArchivePath = "payload.bin",
        OpenStream = () => new MemoryStream(payload, writable: false),
        Length = payload.Length,
    },
};
SevenZipCompressor.CreateArchive(streamEntries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);
```

### `CreateArchiveFromDirectory`

Convenience method for compressing an entire directory on disk. Internally it recursively
walks the directory into a list of `Entry` and calls `CreateArchive`, so you don't have to
walk the tree and build archive-relative paths by hand. Has an async
`CreateArchiveFromDirectoryAsync` counterpart.

```csharp
public static IReadOnlyList<string> CreateArchiveFromDirectory(
    string sourceDirectory,
    string outputArchivePath,
    ArchiveFormat format,
    bool includeBaseDirectory = true,
    Func<string, bool>? filter = null,
    string? password = null,
    long volumeSize = 0,
    ZipMethodOverride? zipMethodOverride = null,
    int? threadCount = null,
    IProgress<SevenZipProgress>? progress = null,
    CancellationToken cancellationToken = default,
    bool encryptHeaders = true);
```

| Parameter | Description |
|---|---|
| `sourceDirectory` | Source directory to compress; throws `DirectoryNotFoundException` if it doesn't exist. |
| `includeBaseDirectory` | `true` (default): archive paths are prefixed with `sourceDirectory`'s own folder name as the root (e.g. compressing `C:\data\photos` yields `photos/a.jpg` — similar to Explorer's "Compress to xxx.zip"); `false`: flattens the directory's contents straight into the archive root (yields `a.jpg`, no `photos/` level). |
| `filter` | Optional filter callback that receives a file's full disk path and returns `false` to exclude it (e.g. excluding `.git`/`node_modules`). Defaults to including every file under the directory. |
| Remaining parameters | Same meaning and defaults as [`CreateArchive`](#createarchive) (password/volume size/compression method/thread count/progress/cancellation/whether to encrypt the archive header). |

**Known limitation**: only files are enumerated recursively — empty subdirectories are not
preserved as a standalone "directory entry" in the archive (they won't be recreated on
extraction). Throws `InvalidOperationException` when the directory has nothing to compress
(empty, or everything got excluded by `filter`).

**Example**:

```csharp
// Compress a whole directory, archive paths prefixed with "photos/"
SevenZipCompressor.CreateArchiveFromDirectory(@"C:\data\photos", @"C:\out\photos.zip", ArchiveFormat.Zip);

// Flatten to the root, excluding the .git directory
SevenZipCompressor.CreateArchiveFromDirectory(
    @"C:\repo", @"C:\out\repo.zip", ArchiveFormat.Zip,
    includeBaseDirectory: false,
    filter: path => !path.Replace('\\', '/').Contains("/.git/"));
```

## `SevenZipArchive`

High-level API for reading/extracting archives.

```csharp
public static class SevenZipArchive
{
    public static List<ExtractItemInfo> List(string archivePathOrFirstVolume, ArchiveFormat format,
        string? password = null, CancellationToken cancellationToken = default);
    public static void ExtractAll(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir,
        string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default);
    public static void ExtractEntryToStream(string archivePathOrFirstVolume, ArchiveFormat format,
        string entryPath, Stream outputStream, string? password = null,
        IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public static byte[] ExtractEntryBytes(string archivePathOrFirstVolume, ArchiveFormat format,
        string entryPath, string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

| Member | Description |
|---|---|
| `List(path, format, password, cancellationToken)` | Lists every entry in the archive (including directory entries). `path` can be a full path, the first volume of a split archive (e.g. `xxx.7z.001`), or the base name of a split archive (`xxx.7z`, which auto-discovers `.001`/`.002`/...). `password` is only needed when the archive has "encrypted file names" enabled (i.e. a 7z archive created with `encryptHeaders: true`, see [`CreateArchive`](#createarchive)) — an archive without header encryption can be listed without a password. |
| `ExtractAll(path, format, outputDir, password, progress, cancellationToken)` | Extracts everything into `outputDir` (created automatically). `password` is ignored for formats that don't support it. Wrong password or corrupted data throws `InvalidOperationException`. `progress`/`cancellationToken` have the same meaning as in [`CreateArchive`](#createarchive). |
| `ExtractEntryToStream(path, format, entryPath, outputStream, password, progress, cancellationToken)` | Writes a **single** entry's content directly into the caller-supplied `outputStream`, with no disk temp file in between. `entryPath` must exactly match the `Path` returned by `List()` (`/`-separated, case-sensitive; `\` is normalized automatically). `outputStream` must be writable and **seekable** (native extraction calls `Seek` on the output stream); its lifetime is entirely the caller's responsibility — this method never closes it. Throws `FileNotFoundException` if the entry doesn't exist, or `InvalidOperationException` if `entryPath` points to a directory. For `tar.gz`, the inner tar is extracted to a temp file first and the entry is located inside it, transparently to the caller. |
| `ExtractEntryBytes(path, format, entryPath, password, progress, cancellationToken)` | Convenience wrapper around `ExtractEntryToStream` that returns the entry's content as a `byte[]` (backed internally by a `MemoryStream`). Fine for small entries; for large ones prefer `ExtractEntryToStream` with your own stream to avoid holding the whole content in memory. |

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

// Pull just one entry straight into memory/a network stream, no disk file involved
using var ms = new MemoryStream();
SevenZipArchive.ExtractEntryToStream(@"C:\out\backup.7z", ArchiveFormat.SevenZip, "images/photo.jpg", ms);

// Or, more simply, get a byte[] directly
byte[] photoBytes = SevenZipArchive.ExtractEntryBytes(@"C:\out\backup.7z", ArchiveFormat.SevenZip, "images/photo.jpg");
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

## `SevenZipArchiveEditor` (incremental add/delete)

An object-oriented, `using`-friendly session for adding/removing entries in an **existing
7z/zip archive**. Unlike the "create-everything-at-once / extract-everything-at-once" static
methods on `SevenZipCompressor`/`SevenZipArchive` above, `SevenZipArchiveEditor` supports
**true incremental updates** — built on 7-Zip's native `IOutArchive::UpdateItems` mechanism
("keep unchanged entries, only re-encode new ones"), without re-packing the whole archive from
scratch.

```csharp
public sealed class SevenZipArchiveEditor : IDisposable
{
    public sealed class EntryInfo
    {
        public required string Path { get; init; }
        public required bool IsDir { get; init; }
        public long Size { get; init; }
        public required bool IsPendingChange { get; init; }
    }

    public static SevenZipArchiveEditor Open(string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true);
    public static SevenZipArchiveEditor Create(string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true);

    public bool HasPendingChanges { get; }

    public IReadOnlyList<EntryInfo> List();
    public void AddFile(string diskPath, string archivePath);
    public int AddDirectory(string sourceDirectory, bool includeBaseDirectory = true, Func<string, bool>? filter = null);
    public bool Delete(string archivePath);
    public int Delete(Func<EntryInfo, bool> predicate);
    public void ExtractAll(string outputDir, IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public void SaveChanges(IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public void Dispose();
}
```

| Member | Description |
|---|---|
| `Open(path, format, password, encryptHeaders)` | Opens an **existing** 7z/zip archive; throws `FileNotFoundException` if it doesn't exist. `encryptHeaders` (defaults to `true`) controls whether each `SaveChanges()` also encrypts the archive header; same meaning and default as `CreateArchive`'s parameter of the same name. |
| `Create(path, format, password, encryptHeaders)` | Starts a blank editing session; `path` doesn't need to exist yet — it's only actually created on the first `SaveChanges()` (an existing file at `path` is overwritten, same semantics as `CreateArchive`). `encryptHeaders` has the same meaning as above. |
| `List()` | Returns "what the archive would look like if `SaveChanges` were called right now" — saved entries (minus ones marked for deletion) plus queued-but-not-yet-saved new entries, **not** just what's currently on disk (`EntryInfo.IsPendingChange` tells the two apart). |
| `AddFile(diskPath, archivePath)` | Queues adding/replacing a file. Throws `FileNotFoundException` immediately if `diskPath` doesn't exist (fail fast); if `archivePath` collides with an existing entry (saved or queued) it's a **replace**. Only queued — the actual read happens at `SaveChanges` time. |
| `AddDirectory(...)` | Queues an entire directory, using the exact same walking rules as [`CreateArchiveFromDirectory`](#createarchivefromdirectory). Returns the number of files queued. |
| `Delete(archivePath)` | Deletes one entry by exact, case-sensitive path match; returns whether anything was actually found and removed. |
| `Delete(predicate)` | Calls `Delete(string)` for every entry in the current `List()` snapshot matching `predicate`; returns how many were actually removed. |
| `ExtractAll(outputDir, progress, cancellationToken)` | Extracts the current logical state (saved minus deleted, plus queued adds). `progress` only covers the "extract saved entries from the on-disk archive" step — queued new entries are plain `File.Copy`'d and don't count toward it. |
| `SaveChanges(progress, cancellationToken)` | Writes queued changes to disk for real: writes to a temp file, then atomically replaces the original only on success (a mid-way failure never corrupts the original; the temp file is cleaned up). Reopens itself against the new content as the new baseline on success. A no-op when there are no pending changes. |
| `Dispose()` | Releases native handles. **Does not auto-`SaveChanges`** — queued changes that were never explicitly saved are silently discarded here (check `HasPendingChanges` beforehand if unsure). This is an intentional transaction-like design, not a bug. |

**Scope limitations**:

- **Only `ArchiveFormat.SevenZip`/`ArchiveFormat.Zip` are supported** — `Tar`/`TarGZip` have no
  "entry index" concept internally and can't be updated in place; `Open`/`Create` throw
  `NotSupportedException` for those formats. You still need
  `SevenZipCompressor.CreateArchive` (full re-pack) for tar/tar.gz.
- **No multi-volume support** — `Open` only recognizes a single, complete file; it does not
  handle split `.7z.001`/`.002`/... archives.
- No single-entry extraction (only `ExtractAll`, same as the static `SevenZipArchive`).
- Path matching is exact, case-sensitive string comparison — no case folding, no wildcards.

**Example**:

```csharp
using var editor = SevenZipArchiveEditor.Open(@"C:\out\backup.zip", ArchiveFormat.Zip, password: "s3cr3t");

editor.Delete("old/report.pdf");
editor.AddFile(@"C:\data\new-report.pdf", "new-report.pdf");
editor.AddDirectory(@"C:\data\extra-photos", includeBaseDirectory: false);

Console.WriteLine(editor.HasPendingChanges); // true
editor.SaveChanges(); // only now are changes actually written to disk; otherwise they're
                       // discarded when the using block ends
```

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
| `ExtractEntryToStream`/`ExtractEntryBytes` given an `entryPath` that doesn't exist in the archive | `FileNotFoundException` |
| `ExtractEntryToStream`/`ExtractEntryBytes` given an `entryPath` that is a directory entry | `InvalidOperationException` |
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

### ⚠️ Extra requirement on Android: you must switch to the CoreCLR runtime (`UseMonoRuntime=false`)

This library's interop layer is built on .NET 8+'s Source-Generated COM
(`[GeneratedComInterface]`/`[GeneratedComClass]`, which relies on
`System.Runtime.InteropServices.ComWrappers` under the hood). **The Mono runtime that
`.NET for Android` uses by default does not support `ComWrappers` at all** (Microsoft's own API
docs tag the `ComWrappers` class with `[UnsupportedOSPlatform("android")]`). This applies
regardless of UI framework — MAUI, Avalonia for Android, or any other host built on the
`.NET for Android` SDK — calling any `CreateArchive`/`List`/`ExtractAll`/
`SevenZipArchiveEditor` method under Mono throws:

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
   at System.Runtime.InteropServices.ComWrappers.GetOrCreateObjectForComInstance(...)
   at SevenZipLite.Native.ComFactory.GetRcw(...)
```

**This is not a bug in this library, nor specific to any one API** — it reproduces on the
default Android runtime no matter which method you call. The fix is to explicitly opt your
Android host project (`.csproj`) into the **experimental** CoreCLR runtime introduced in
.NET 10:

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
    <!-- CoreCLR requires API 24 (Android 7.0 Nougat) or higher; the default template's 21 needs bumping -->
    <SupportedOSPlatformVersion>24.0</SupportedOSPlatformVersion>
    <UseMonoRuntime>false</UseMonoRuntime>
</PropertyGroup>
```

Prerequisites/limits:
- **Must target `net10.0-android` or later** — `UseMonoRuntime` as a CoreCLR opt-in switch
  didn't exist before .NET 10; it has no effect on `net8.0-android`/`net9.0-android`.
- **64-bit ABIs only**: `arm64-v8a`/`x86_64` (the two common emulator architectures) — **not**
  `armeabi-v7a`/`x86`, since CoreCLR doesn't ship a runtime pack for those 32-bit ABIs at all.
- As of .NET 10, this remains an officially **experimental, not production-recommended**
  feature (larger app size; some debugging/diagnostics are incomplete). Per the published
  roadmap, CoreCLR becomes the default runtime for Android/iOS/Mac Catalyst starting in
  .NET 11 (Mono becomes the opt-in compatibility path), at which point this requirement goes
  away on its own.
- After changing the property, do a **fully clean rebuild** (delete `bin`/`obj`, `adb
  uninstall` the old APK on the emulator/device, then rebuild+redeploy) — just flipping the
  property without clearing caches can leave the previously-built Mono-based APK installed,
  making it look like the fix "didn't work". You can confirm the new APK actually switched by
  unpacking it: `unzip -l <path to apk> | grep -E "libcoreclr|libmonosgen"` — seeing
  `libcoreclr.so` (not `libmonosgen-2.0.so`) confirms it took effect.

See [`docs/investigation-report.zh-CN.md`](./investigation-report.zh-CN.md) §8.7 (Chinese) for
the full real-device investigation notes and links to the official sources.

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
