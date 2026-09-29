# Changelog / 更新日志

本项目遵循[语义化版本](https://semver.org/lang/zh-CN/)（[Semantic Versioning](https://semver.org/)）。

## [Unreleased]

（暂无）/ (nothing yet)

## [0.1.0] - 2026-09-28

首个正式打包版本。/ First formally packaged version.

### 新增 / Added
- 正式的仓库结构（`src/`/`tests/`/`samples/`/`docs/`/`scripts/`），NuGet 打包脚本
  （`scripts/pack-nuget.sh`/`.ps1`），GitHub Actions 发布 workflow
  （`.github/workflows/release.yml`），双语 API 文档（`docs/api/`），双语 README。
  Formal repository layout (`src/`/`tests/`/`samples/`/`docs/`/`scripts/`), a NuGet
  packaging script, a GitHub Actions release workflow, bilingual API docs, and a bilingual
  README.
- 核心库（`src/SevenZipLite`）新增 **`net8.0`** 目标框架，与 `net10.0` 多目标共存，方便
  尚未升级到最新 .NET 的项目直接引用；两个目标框架共用完全相同的源码。
  The core library (`src/SevenZipLite`) now multi-targets **`net8.0`** alongside `net10.0`,
  so projects that haven't upgraded to the latest .NET yet can reference it directly; both
  targets share identical source.
- WPF Demo（`samples/SevenZipLite.WpfDemo`）接入 .NET 9 引入的 WPF Fluent 主题
  （`ThemeMode="System"`），界面外观随 Windows 系统浅色/深色模式设置自动切换。
  The WPF demo (`samples/SevenZipLite.WpfDemo`) adopts the WPF Fluent theme introduced in
  .NET 9 (`ThemeMode="System"`), automatically switching its appearance to match the Windows
  light/dark mode setting.
- 新增 [`DISCLAIMER.md`](./DISCLAIMER.md)：说明本仓库代码/文档/脚本主要由 AI 辅助生成的
  免责声明（中英双语）。
  Added [`DISCLAIMER.md`](./DISCLAIMER.md): a bilingual disclaimer noting that this
  repository's code/docs/scripts were primarily generated with AI assistance.
- `SevenZipCompressor.CreateArchive` 新增 `threadCount` 参数，可显式控制压缩线程数（默认
  `Environment.ProcessorCount`）。
  `SevenZipCompressor.CreateArchive` gained a `threadCount` parameter to explicitly control
  the number of compression threads (defaults to `Environment.ProcessorCount`).
- 新增 `SevenZipLite.SelfTest.MultiThreadStressRunner`：多线程压缩稳定性压力测试，Linux 控制台
  （`--stress`）、WPF、MAUI 三个 Demo 均已接入独立入口。
  Added `SevenZipLite.SelfTest.MultiThreadStressRunner`, a stress test for multi-threaded
  compression stability, wired into all three demo apps via an independent entry point.

### 修复 / Fixed
- 修复多线程压缩（`mt > 1`）下 `UpdateCallback` 的输入流生命周期竞态 bug，该 bug 会导致
  `IOutArchive.UpdateItems` 偶发 `E_FAIL`。修复后重新默认启用多线程压缩，实测有明显吞吐量提升。
  详见 `docs/development-notes.zh-CN.md`。
  Fixed a native-input-stream lifetime race in `UpdateCallback` under multi-threaded
  compression (`mt > 1`) that caused intermittent `E_FAIL` from `IOutArchive.UpdateItems`.
  Multi-threaded compression is now safely re-enabled by default, with measurable throughput
  gains. See `docs/development-notes.zh-CN.md` (Chinese only) for the full investigation.
- 修复 win-x64/win-arm64 上批量 `ISetProperties::SetProperties` 报 `E_INVALIDARG` 的问题
  （PROPVARIANT 数组步幅在 64 位 Windows 上应为 24 字节而不是硬编码的 16 字节）。
  Fixed `E_INVALIDARG` from batched `ISetProperties::SetProperties` calls on win-x64/win-arm64
  (the PROPVARIANT array stride must be 24 bytes on 64-bit Windows, not the previously
  hardcoded 16).

### 变更 / Changed
- **许可证由 MIT 改为 GNU LGPL v2.1（或更新版本，LGPL-2.1-or-later）**，与仓库捆绑的 7-Zip
  原生库保持同一许可证家族；详见 [`LICENSE`](./LICENSE) 与
  [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md)。
  **License changed from MIT to GNU LGPL v2.1 (or later, LGPL-2.1-or-later)**, aligning with
  the same license family as the bundled 7-Zip native libraries; see
  [`LICENSE`](./LICENSE) and [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md).
  `PackageLicenseExpression` 同步改为 `LGPL-2.1-or-later`。
  `PackageLicenseExpression` updated to `LGPL-2.1-or-later` accordingly.
- 移除了 `tools/CodecDump`（内部调试用的编解码器枚举小工具），不再作为仓库的一部分维护/发布。
  Removed `tools/CodecDump` (an internal debug utility for enumerating registered codecs); it
  is no longer maintained/shipped as part of this repository.
- 项目布局重新组织：`SevenZipLite`→`src/SevenZipLite`，`SevenZipLite.SelfTest`/
  `SevenZipLite.Tests`→`tests/`，`SevenZipLite.WpfDemo`/`SevenZipLite.MauiDemo`→`samples/`。
  所有原生二进制统一收敛到仓库根 `native-libs/`（见其 README.md），不再在各项目下各自维护
  重复拷贝。
  Reorganized the repository layout: `SevenZipLite`→`src/SevenZipLite`,
  `SevenZipLite.SelfTest`/`SevenZipLite.Tests`→`tests/`,
  `SevenZipLite.WpfDemo`/`SevenZipLite.MauiDemo`→`samples/`. All native binaries are now
  consolidated under the repo-root `native-libs/` (see its README.md) instead of being
  duplicated per-project.
