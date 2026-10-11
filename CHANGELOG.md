# Changelog / 更新日志

本项目遵循[语义化版本](https://semver.org/lang/zh-CN/)（[Semantic Versioning](https://semver.org/)）。

## [Unreleased]

## [0.1.3] - 2026-10-11

### 变更 / Changed
- 更新了 NuGet 包的部分元数据文案（作者信息、仓库地址）。

## [0.1.2] - 2026-10-11

### 新增 / Added
- **三个核心 API 都新增了异步版本：`SevenZipCompressor.CreateArchiveAsync` /
  `SevenZipArchive.ListAsync` / `SevenZipArchive.ExtractAllAsync`**。底层的 7-Zip
  native 调用本身是阻塞式的，这些异步方法只是用 `Task.Run` 把调用丢到线程池，让调用方
  的 `await` 不会卡住当前线程（比如 UI 线程），并不是真正意义上的异步 I/O。
- **`CreateArchive`/`List`/`ExtractAll`（含它们的 `*Async` 版本）新增了可选的
  `IProgress<SevenZipProgress>? progress` 参数**，用于汇报字节级压缩/解压进度。新增
  公共类型 `SevenZipProgress`（`BytesCompleted`/`BytesTotal`/`Percentage`）。tar.gz
  在内部是"先打包 tar 再 gzip 压缩"（创建）或"先解出内层 tar 再展开"（解压）两个独立
  阶段，会各自汇报一轮完整的 0%→100%，不会合并成一个加权总进度，调用方需要自行处理
  这个特殊情况（详见 `SevenZipProgress` 的类型注释）。
- **`CreateArchive`/`List`/`ExtractAll`（含它们的 `*Async` 版本）新增了可选的
  `CancellationToken cancellationToken` 参数**，支持取消正在进行的压缩/解压/列目录
  操作，取消后抛出标准的 `OperationCanceledException`。取消粒度是**条目之间**，不是
  字节级/单个条目内部：一旦某个文件已经开始被压缩/解压，这次取消请求要等这个文件处理
  完、native 下一次调用回调时才会生效，无法从内部中途打断单个文件的编解码过程。
- 以上新增参数全部是**追加在参数列表末尾的可选参数**，不影响任何现有调用代码，不是
  破坏性变更。

### 修复 / Fixed
- **修复进度汇报里"已完成字节数"的数值来源 bug**：7-Zip 的 `IProgress::SetCompleted`
  在 COM 层声明的是 `const UInt64 *completeValue`（指针，可以是 NULL 表示"当前不可用"），
  不是按值传递的 `UInt64`；本次实现进度汇报功能的过程中一度错误地把这个指针的地址本身
  强转成已完成字节数，导致汇报出来的数值远大于总字节数（自检用例已发现并覆盖回归，
  见 `AsyncApiTestRunner` 的 "progress reporting" 用例）。现在统一通过新增的
  `NativePtr.ReadUInt64OrDefault` 辅助方法正确解引用这个指针。
- **修复打开「加密文件名」（header encryption，`-mhe=on`）的 7z 归档时必定抛出
  `IInArchive.Open 失败, HRESULT=0x00000001` 的问题**：`ArchiveOpenCallback`（用于
  `SevenZipArchive.List`/`ExtractAll` 内部的 `IInArchive.Open` 阶段）此前没有实现
  `ICryptoGetTextPassword`，导致它在 Open 阶段完全无法向 native 侧提供密码——这类归档
  在 Open 阶段就需要密码才能解出头部结构（文件名/大小等），不是等到 Extract 才要密码。
  不实现该接口时，无论密码是否正确、是否含特殊字符，Open 都会失败，native 端把这种
  "需要密码但拿不到"的情况当作"无法识别的格式"返回 `S_FALSE`（COM 成功码，值为 1），
  而上层 `Check()` 用 `hr != S_OK` 判断失败，于是被包成一条很容易误导人、看起来像是
  "密码有问题"的 `HRESULT=0x00000001` 异常。现在 `ArchiveOpenCallback` 已实现
  `ICryptoGetTextPassword`，`List`/`ExtractAll` 都新增了可选的 `password` 参数并会一路
  透传给它；已用真实的 `7z -mhe=on` 归档实测复现问题、验证修复有效，并补充了更清晰的
  `S_FALSE` 专用错误提示（区分"密码错误/未提供密码/文件损坏或格式不匹配"），不再是一个
  裸的十六进制 HRESULT。
- `SevenZipArchive.Check()` 现在对 `S_FALSE`（HRESULT=1，一个合法的 COM 成功码，并非真正
  的失败）给出专门的、更有指向性的错误信息，而不是和其他真失败 HRESULT 一样笼统地报
  `HRESULT=0x00000001`。
- **修复 Release workflow 每次发布都在复用旧 Release、而不是创建全新 Release 的问题**：
  `softprops/action-gh-release` 对同一个 `tag_name` 默认是"创建或更新"语义——如果该
  tag 已经有一个 Release（比如之前手动跑过、或重新 dispatch 同一版本号重跑过
  workflow），它会直接复用旧 Release，只更新标题/说明文字，并把这次的新文件追加到
  旧 Release 原有的附件列表里，不会清空上次遗留的文件。现在 `create-release` job 会
  在创建 Release 前先显式删除同一 tag 下任何已存在的旧 Release（只删 Release 本身，
  不删 tag），保证每次发布看到的都是"这个 tag 还没有 Release"的干净状态，从而总是
  创建一个全新的 Release。
- **Release 的 GitHub Release 附件里不再包含 `.snupkg`**：`.snupkg`（调试符号包）是给
  符号服务器用的，仍然会在 `pack-nuget` job 的中间产物里保留、并在配置好下面提到的
  Trusted Publishing 后随 `.nupkg` 一起被 `dotnet nuget push` 自动推送到 nuget.org 的
  符号服务器，但不再附加到面向终端用户下载的 GitHub Release 里。

### 完善 / Improved
- **`publish-nuget-org` job 从长期有效的 API Key 迁移到 nuget.org 官方推荐的 Trusted
  Publishing（OIDC）**：nuget.org 明确提示自动化发布不建议继续用 API Key（哪怕是
  glob-pattern 限定、带过期时间的那种），推荐改用 Trusted Publishing。现在这个 job
  用 nuget.org 官方的 `NuGet/login@v1` action，在运行时拿 GitHub 签发的 OIDC token
  （job 加了 `permissions: id-token: write`）向 nuget.org 换一个只活 1 小时的临时
  API Key 再推送，不再需要在仓库里存一个长期有效的 `NUGET_API_KEY` secret——只需要
  配置一个 `NUGET_USERNAME` secret（你的 nuget.org 用户名）加上在 nuget.org 后台
  添加一条 Trusted Publishing 策略（Repository Owner / Repository /
  `Workflow File: release.yml`）。没配置时该 job 仍会正常跳过（不报错，GitHub
  Release 照常发布）。原先基于 `NUGET_API_KEY` 的流程已完全移除；如果之前配置过这个
  secret，现在可以从仓库里删掉了。
- **完善 `publish-nuget-org` job 的可观测性**：现在无论实际推送还是因为没配置
  `NUGET_USERNAME` 而跳过，都会在该次运行的 Job Summary 里明确写出结果（推送了哪个
  版本 / 为什么跳过），不用再去翻日志。`publish-nuget-org` job 自身的注释里补充了
  配置 Trusted Publishing 策略、以及在仓库 Secrets 里添加 `NUGET_USERNAME` 的具体
  步骤。

## [0.1.1] - 2026-10-06

### 变更 / Changed
- **用跨平台的 Avalonia Demo 替换了 WPF Demo**：`samples/SevenZipLite.WpfDemo` 已删除，
  新增 `samples/SevenZipLite.AvaloniaDemo`，功能上与旧 WPF Demo 完全对等（内置自检、手动
  压缩/解压、大文件吞吐量测试、多线程压力测试四大板块），但改用跨平台的 Avalonia UI 框架，
  同一套代码可在 Windows 和 Linux 上运行，并以 Native AOT（`PublishAot=true`）方式发布为
  win-x64、linux-x64 两个 RID 的原生可执行文件（不依赖目标机器安装 .NET 运行时；不含
  win-x86——.NET Native AOT 在 Windows 上只支持 x64/arm64，从未支持过 32 位，这是在真实
  CI 上跑出 `NETSDK1203` 之后才确认的 .NET SDK 硬限制，无法绕过）。
  CI 发布流程中的 `publish-wpf` job 相应替换为 `publish-avalonia`（两个 RID 各自在匹配的
  操作系统 runner 上发布，因为 Native AOT 不支持跨操作系统编译）。
  **Replaced the WPF demo with a cross-platform Avalonia demo**: `samples/
  SevenZipLite.WpfDemo` has been removed and `samples/SevenZipLite.AvaloniaDemo` added in
  its place, with full functional parity to the old WPF demo (built-in self-test, manual
  pack/extract, large-file throughput test, and multi-thread stress test) but built on the
  cross-platform Avalonia UI framework — the same codebase runs on both Windows and Linux,
  and is published with Native AOT (`PublishAot=true`) as native executables for the
  win-x64 and linux-x64 RIDs (no .NET runtime required on the target machine; win-x86 is
  excluded — .NET Native AOT on Windows only ever supported x64/arm64, never 32-bit, a
  hard .NET SDK limitation confirmed by an actual `NETSDK1203` failure on real CI, with no
  workaround).
  The CI release pipeline's `publish-wpf` job was replaced with `publish-avalonia`
  accordingly (each RID publishes on its matching OS runner, since Native AOT doesn't
  support cross-OS compilation).
- 核心库（`src/SevenZipLite`）新增 `<IsAotCompatible>true</IsAotCompatible>`，并新增永久性的
  `tests/SevenZipLite.AotSmokeTest` 项目，在 CI 的 `verify-aot` job 中把它发布为 Native AOT
  并实际运行，验证核心库在 AOT 下真的可用（而不只是声明兼容）。
  The core library (`src/SevenZipLite`) gained `<IsAotCompatible>true</IsAotCompatible>`,
  along with a new permanent `tests/SevenZipLite.AotSmokeTest` project that the CI
  `verify-aot` job publishes as Native AOT and actually runs, verifying the core library
  truly works under AOT (not just declaring compatibility).

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
