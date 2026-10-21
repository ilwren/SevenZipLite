# SevenZipLite.AvaloniaAndroidDemo — Avalonia + Android crash repro/diagnostic tool

This is not a "best practices" feature demo — it's a **single-purpose diagnostic tool** meant
to help anyone hitting a crash/force-close when calling SevenZipLite from their own Avalonia +
Android project reproduce the problem in a minimal, clean environment with no other business
logic in the way, so you can actually read the **full exception text** instead of digging
through system noise in `adb logcat`.

Background: see `docs/investigation-report.zh-CN.md` §8.7 and the ⚠️ subsection under "Native
library loading and platform requirements" in `docs/api/SevenZipLite.en.md`. .NET for Android
defaults to the Mono runtime, which does not support `ComWrappers` — the API this library's
interop layer depends on — so any call throws `PlatformNotSupportedException`.

## What this tool does

Open the app and work through the buttons top to bottom:

0. **Runtime diagnostics** — detects whether the current process is running on Mono or CoreCLR.
1. **`SevenZipCompressor.CreateArchive`** (single file, synchronous) — the shortest possible
   call stack, a minimal repro.
2. **`SevenZipCompressor.CreateArchiveFromDirectory`** (synchronous)
3. **`SevenZipCompressor.CreateArchiveFromDirectoryAsync`** (asynchronous) — mirrors the call
   path most commonly seen in crash stack traces.
4. **`SelfTestRunner.RunAll`** — reuses the repo's own self-test suite for broader coverage
   (7z/zip/tar/tar.gz × multi-volume × password).
5. (Optional) **Test with a real file you pick** — checks for a completely different and
   unrelated possible cause: on modern Android, file pickers often return `content://` virtual
   URIs rather than real filesystem paths, while `SevenZipCompressor.Entry.DiskPath` needs a
   path `File.OpenRead` can open directly. This step tells you explicitly whether that's what
   you're dealing with.

Every step has its own try/catch — **an exception here will not crash the app**. The full
`ex.ToString()` (including stack trace) is printed directly in the box under the button that
triggered it. There's also a breadcrumb mechanism (`DiagnosticLog`): if a genuine hard crash
ever does slip past every try/catch, the last progress marker written to disk is shown at the
top of the page the next time the app starts.

## Project layout

- `SevenZipLite.AvaloniaAndroidDemo/` — shared code (UI + diagnostic logic), `net10.0`,
  with direct `ProjectReference`s to `src/SevenZipLite` and `tests/SevenZipLite.SelfTest`.
  Deliberately not using MVVM/`{Binding}` — every control is wired via `x:Name` and
  straightforward code-behind so it's obvious at a glance what each button does.
- `SevenZipLite.AvaloniaAndroidDemo.Android/` — the Android head, `net10.0-android`, **already
  configured with `UseMonoRuntime=false` + `SupportedOSPlatformVersion=24.0`** (the exact
  CoreCLR switch this tool exists to validate). Native library packaging mirrors
  `samples/SevenZipLite.MauiDemo` exactly.
- `SevenZipLite.AvaloniaAndroidDemo.Desktop/` — a desktop head for Windows/Linux, so you can
  quickly rule out "is this a SevenZipLite/Avalonia problem at all" before deploying to an
  Android device/emulator.

## Build and run

### Run on desktop first (strongly recommended elimination step)

```bash
dotnet run --project samples/SevenZipLite.AvaloniaAndroidDemo/SevenZipLite.AvaloniaAndroidDemo.Desktop
```

If every test passes on desktop, the issue is specific to the Android runtime and you can move
on to the next step with confidence.

### Run on Android

Requires the Android SDK/NDK (`dotnet workload install android`), then:

```bash
dotnet build samples/SevenZipLite.AvaloniaAndroidDemo/SevenZipLite.AvaloniaAndroidDemo.Android \
  -f net10.0-android -c Debug -t:Run
```

> **Verification scope when this project was added to the repo**: the sandbox used to build
> it had no Android emulator/device and very limited RAM (not enough to hold a full Android
> SDK plus run a complete dex-packaging pass). It was verified up through "all C# code compiles
> with zero errors against the real `net10.0-android` reference assemblies and the
> `Avalonia.Android` package" (i.e. `dotnet build` reaches the dexing stage cleanly) — **not**
> through producing a final APK, installing it on a device/emulator, or actually triggering and
> seeing `PlatformNotSupportedException` on screen. If you hit any build error or runtime
> exception on your own machine — especially one related to this project's own code rather than
> the ComWrappers exception it's meant to reproduce — please report back the full error and the
> code will be adjusted.

## If the results don't match expectations

- **If step 0 shows CoreCLR is already active, but steps 1–3 still crash**: the
  `UseMonoRuntime=false` switch alone didn't fix it — this could point to an Avalonia-Android
  specific incompatibility, or some other unknown issue with the 7-Zip native library under
  CoreCLR-for-Android. Report the full exception text back; the fix strategy (e.g. the
  previously discussed fallback of hand-writing a COM vtable to bypass `ComWrappers` entirely)
  would need to be revisited.
- **If steps 1–4 all pass but step 5 fails, or `TryGetLocalPath()` returns `null`**: the
  problem is most likely not SevenZipLite at all, but your own code passing a `content://`
  virtual URI straight into `Entry.DiskPath`. Copy the file content to a real temporary file
  first.
