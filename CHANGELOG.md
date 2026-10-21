# Changelog / 更新日志

本项目遵循[语义化版本](https://semver.org/lang/zh-CN/)（[Semantic Versioning](https://semver.org/)）。

## [0.1.7] - 2026-10-19

### 新增 / Added
- 打包/解压不再强制要求磁盘文件路径，新增对 `Stream` 的直接支持：
  - `SevenZipCompressor.Entry` 新增 `OpenStream`（`Func<Stream>?`）、`Length`（`long?`）、
    `LastWriteTimeUtc`（`DateTime?`）三个成员，作为 `DiskPath` 的替代——条目内容可以来自
    内存（`MemoryStream`）或任何按需可重复打开的流，不需要先落盘成临时文件再压缩。
    `DiskPath` 随之从 `required` 改为可选，但与 `OpenStream` 互斥：必须且只能设置两者之一，
    设置 `OpenStream` 时必须同时提供准确的 `Length`（7-Zip 原生层在真正读取内容之前就需要
    把大小写进归档头，无法事后从流推断）。`ValidateEntries` 相应扩展了校验分支，覆盖
    “两者都未设置”“两者都设置”“设置了 OpenStream 但 Length 缺失/为负数”三种新增错误场景，
    均抛出带具体下标的 `ArgumentException`。
  - `SevenZipArchive` 新增 `ExtractEntryToStream(archivePath, format, entryPath, outputStream,
    ...)`：把归档内单个条目的内容直接写进调用方提供的 `Stream`，不经过磁盘临时文件；以及
    便捷封装 `ExtractEntryBytes(...)` 直接返回 `byte[]`。两者均有对应的 `*Async` 版本。
    找不到指定条目抛 `FileNotFoundException`，条目是目录抛 `InvalidOperationException`。
    `tar.gz` 内部会先解出临时 tar 再从中定位条目，对调用方透明。
  - **硬性约束（两个方向共用）**：调用方提供的 `Stream` 必须支持 `Seek`（native 7-Zip 在
    压缩/解压过程中都会对流调用 `Seek`，这是已有内部实现 `ComInStream`/`ComOutStream` 的
    既有要求，不是新增限制）——不支持 `Seek` 的来源（比如原始网络流）需要调用方自己先缓冲
    到 `MemoryStream` 或临时文件。
  - 新增 13 条自检回归用例（`tests/SevenZipLite.SelfTest/StreamIoTestRunner.cs`），覆盖混合
    `DiskPath`/`OpenStream` 条目创建归档、四种格式（7z/zip/tar/tar.gz）下 `ExtractEntryToStream`
    往返校验、带密码场景、`ExtractEntryBytes` 便捷方法，以及上述全部新增校验分支的异常回归。

### 文档 / Docs
- `docs/api/SevenZipLite.md`/`.en.md` 更新 `Entry`、`CreateArchive`、`SevenZipArchive` 对应章节
  和异常速查表，补充 `OpenStream`/`Length`/`LastWriteTimeUtc`/`ExtractEntryToStream`/
  `ExtractEntryBytes` 的说明和示例代码。

## [0.1.6] - 2026-10-19

### 新增 / Added
- `SevenZipCompressor.CreateArchive`（及内部走同一路径的 `CreateArchiveFromDirectory`/
  `tar.gz`）现在会在真正开始压缩之前，对传入的 `entries` 列表做显式参数校验：`entries`
  本身为 `null` 抛 `ArgumentNullException`；某一项的 `DiskPath`/`ArchivePath` 为
  `null`/空字符串时抛出 `ArgumentException`，异常信息**包含具体是第几个条目**（如
  `entries[2].DiskPath 为空...`）。此前这种情况会在内部调用 `FileInfo`/
  `File.GetLastWriteTimeUtc` 时抛出一个完全不带上下文的 `ArgumentNullException`
  （`"Value cannot be null. (Parameter 'path')"`），排查起来只能一条条加断点去试，尤其是
  当路径是从外部来源（比如文件选择器/某种 Uri 转换逻辑）生成、在某种边缘情况下变成
  `null` 却没有在调用处判空时。详见 `docs/api/SevenZipLite.md`/`.en.md` 里 `Entry`
  和 `CreateArchive` 的更新说明，以及 `tests/SevenZipLite.SelfTest/
  EntryValidationTestRunner.cs` 的回归用例。
- 新增 `samples/SevenZipLite.AvaloniaAndroidDemo`：一个专用的 Avalonia + Android 崩溃复现/
  诊断工具（非功能 Demo），帮助在自己项目里调用 SevenZipLite 遇到崩溃/闪退、但日志噪音太大
  定位不到的用户，在最小环境里逐步复现并看到完整异常信息（含一个"硬崩溃也能留痕"的本地
  面包屑日志机制）。Android 头项目已预置好 `UseMonoRuntime=false`/
  `SupportedOSPlatformVersion=24.0` 这个 CoreCLR 切换。用法见该目录下的 README.md/README.en.md。

### 文档 / Docs
- 在 README/`docs/api/SevenZipLite.md`（中英文）里补充了一个此前只写在
  `SevenZipLite.MauiDemo.csproj` 注释和调查报告里、但没有放进正式 API 文档的**通用性** Android
  平台要求：任何基于 `.NET for Android` SDK 的宿主项目（不止 MAUI，Avalonia for Android 等同样
  适用）都必须在 `net10.0-android` 及以上目标下显式设置 `UseMonoRuntime=false` 切到实验性
  CoreCLR 运行时，否则调用本库任意方法都会在 `ComWrappers.GetOrCreateObjectForComInstance` 处
  抛 `PlatformNotSupportedException`（Mono 运行时完全不支持 `ComWrappers`）。此前只有 MAUI 用户
  能通过阅读 Demo 项目源码碰巧发现这个前提条件，现在任何平台的 Android 宿主项目使用者都能在
  正式文档里直接看到。

## [0.1.5] - 2026-10-15

### ⚠️ 行为变更 / Behavior change
- **7z 密码保护默认改为同时加密归档头（文件名/文件大小/目录结构等元数据）**，对应官方 7z
  命令行的 `-mhe=on`。此前只加密文件内容，不加密归档头——这其实是官方 `7z.exe`/`7za`
  本身的默认行为（只传 `-p<密码>` 不加 `-mhe=on` 不会加密文件名），但本库从未把这个开关
  暴露出来，导致"设了密码的压缩包"其实谁都能看到里面有哪些文件、文件名是什么，只是打不开
  内容——这是一个容易被忽略的直觉盲区，本库认为更安全的默认值更符合大多数调用方的真实预期，
  所以反过来把默认值定为"加密"。
  - `SevenZipCompressor.CreateArchive`/`CreateArchiveFromDirectory`（含 `*Async` 版本）和
    `SevenZipArchiveEditor.Open`/`Create` 都新增了 `bool encryptHeaders = true` 参数；
    仅在 `format == ArchiveFormat.SevenZip` 且提供了非空密码时才有意义，zip 格式本身不
    支持这个能力（标准 zip 的加密只覆盖条目数据，中央目录里的文件名永远明文），传这个
    参数对 zip 无效。
  - **升级影响**：如果你的代码此前依赖"带密码的 7z 归档不传密码也能 `List()`/浏览文件名"
    这个行为，升级后这个调用会抛出 `InvalidOperationException`（S_FALSE，头部需要密码才能
    解析）——需要显式传 `encryptHeaders: false` 才能恢复旧行为。**对已经用旧版本创建好的
    归档没有影响**（这个参数只影响新创建/新保存的归档；旧归档该怎么读还怎么读，密码该怎么
    传还怎么传）。

## [0.1.4] - 2026-10-15

### 新增 / Added
- **`SevenZipCompressor.CreateArchiveFromDirectory`（含 `*Async` 版本）**：压缩整个磁盘
  目录的便捷方法，不用再自己递归遍历目录、手工拼接每个文件的归档内相对路径。
  - `includeBaseDirectory`（默认 `true`）控制归档内路径是否带上源目录自身的名字作为根
    （类似资源管理器右键"压缩到 xxx.zip"），`false` 则把目录内容直接铺平到归档根。
  - 可选的 `Func<string, bool> filter` 回调按完整磁盘路径过滤文件（比如排除
    `.git`/`node_modules`）。
  - 其余参数（密码/分卷/压缩方法/线程数/`IProgress<SevenZipProgress>` 进度回调/
    `CancellationToken`）语义和 `CreateArchive` 完全一致；底层就是构造出
    `IReadOnlyList<Entry>` 后调用 `CreateArchive`，进度回调原本就支持，这里只是顺带
    打通到新方法上。已知限制：空目录不会在归档里单独保留一个"目录条目"。
- **`SevenZipArchiveEditor`：新增的 `IDisposable`、可以用 `using` 管理的"对已有 7z/zip
  归档做增量增删条目"会话**，区别于 `SevenZipCompressor`/`SevenZipArchive` 那套"一次性
  整体创建/整体解压"的静态方法——不需要解压整个归档重新打包，而是复用 7-Zip 原生
  `IOutArchive::UpdateItems` 的"保留未改动条目、只重新编码新增条目"机制。
  - `SevenZipArchiveEditor.Open(path, format, password)` 打开已存在的归档；
    `SevenZipArchiveEditor.Create(path, format, password)` 新建一个空白会话（文件可以
    还不存在，第一次 `SaveChanges()` 时才真正写入）。
  - `List()` / `AddFile(diskPath, archivePath)` / `AddDirectory(sourceDirectory,
    includeBaseDirectory, filter)` / `Delete(archivePath)` / `Delete(predicate)` /
    `ExtractAll(outputDir)` / `SaveChanges()` / `HasPendingChanges`。`AddFile`/`Delete`
    只是在内存里排队，必须显式调用 `SaveChanges()` 才会真正写回磁盘（写入临时文件成功后
    再原子替换原文件，中途失败不会破坏原归档）；同名 `AddFile` 对现有条目是"替换"语义。
  - **仅支持 7z 和 zip 格式**（tar 的内部实现、tar.gz 都没有"条目索引"概念，无法原地
    增删，对这两种格式调用会抛出 `NotSupportedException`，仍然只能用
    `SevenZipCompressor.CreateArchive` 整体重新打包）；不支持分卷归档。
  - **`Dispose()` 不会自动保存排队中的改动**（可以用 `HasPendingChanges` 自查）——这是
    有意的事务式设计（不 `SaveChanges()` 就等于没发生），不是 bug。

### 其他 / Internal
- `Callbacks.UpdateSourceItem` 新增 `ExistingArchiveIndex` 字段，用于支撑
  `SevenZipArchiveEditor.SaveChanges` 的"保留旧条目 + 写入新条目"混合更新场景，对现有
  `SevenZipCompressor.CreateArchive`（全部是新条目）的行为没有影响。

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
