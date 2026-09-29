# 7-Zip + C# Wrapper 方案调查报告

调查日期：2026-09-26
调查范围：7-Zip / p7zip 官方源码与衍生项目、NuGet 上主流 C# 封装库、Android 生态兼容性

---

## 一、结论摘要（TL;DR）

| 问题 | 结论 |
|---|---|
| **C# wrapper 能否覆盖 7-Zip 绝大多数功能？** | **仅在 Windows 桌面场景下基本成立**。成熟的 wrapper（SharpSevenZip / 早期 SevenZipSharp）本质是对 `7z.dll` 内部 COM 式接口的直接封送，因此能拿到与官方 GUI/CLI 相同的解压能力（20+ 格式只读）和有限的压缩能力（7z/zip/gzip/bzip2/tar/wim/xz 可写，这是 **7-Zip 本身**的限制，不是 wrapper 的限制）。但 CLI-only、GUI-only 的辅助功能（benchmark、shell 集成、SFX 向导、部分 hash/rename 命令）不在 COM 接口范围内，wrapper 覆盖不到。 |
| **跨平台（Linux/macOS）是否成立？** | 传统 wrapper（SevenZipSharp 系、SharpSevenZip、SevenZipExtractor）**几乎全部是 Windows-only**，因为它们依赖 .NET 的传统 COM Interop（`[ComImport]`），而该机制在 .NET Core/5+ 上**仅在 Windows 受支持**。2024 年后出现的新一代项目（如 `SevenZipSharper`）改用 .NET 8 的 Source-Generated COM，才第一次做到真正跨平台（Win/macOS/Linux），但项目非常新、社区规模很小。 |
| **Android 兼容性如何？** | **目前没有成熟、官方或被广泛使用的"7-Zip 原生引擎 + C# wrapper"组合能直接跑在 Android 上。** 7-Zip 官方从未发布 Android 版本；社区已证明可以用 NDK 把 7-Zip/p7zip 源码交叉编译成 Android 的 `.so`（技术可行），但目前面向 .NET/C#（MAUI、Xamarin.Android）的封装库都**没有随包附带 `android-arm64` 等 RID 的原生库**，需要自行编译+自行写绑定代码。Android 上唯一工程成熟、经过大量测试的方案是 Java/JNI 生态的 **7-Zip-JBinding-4Android**，但它是 Java 库，要在 .NET 项目里用需要额外做 Java Binding Library 桥接，并非原生 C# 体验。 |
| **若把范围收窄到 7z/zip/tar/tar.gz + 分卷 + 密码，自研一个纯 7-Zip 源码 + 纯 C# 的 Android 类库是否可行？** | **可行**，且是目前所有 Android 方案中工程风险最低的一条路（见第六章）。收窄格式后可以完全剔除 unRAR 授权代码与 GUI/UI 层代码；分卷功能官方作者已明确说明不在核心引擎内、可以纯 C# 实现，不需要碰原生代码；密码/加密模块直接复用现成 LGPL 代码即可。核心工作量是"NDK 交叉编译裁剪版 `.so`" + "基于 .NET 8 Source-Generated COM 写薄封装层"，两者均有可参考的现成先例，不存在阻断性技术障碍，但目前没有现成产品，需要自建并长期维护。**⚠️ 已用真实 Android 设备验证并修正**：Source-Generated COM 在 Android 默认的 Mono 运行时上完全不受支持（`ComWrappers` 类的硬限制），必须显式切换到 .NET 10 目前仍标注为**实验性**的 CoreCLR 运行时（`UseMonoRuntime=false`）才能跑通；不影响"可行"这一结论，但这是一个此前未预见、必须满足的前提条件，详见第八章 8.7 节。 |

---

## 二、7-Zip / p7zip 源码生态现状

### 2.1 官方主线（Igor Pavlov）

- **仓库现状**：早年 7-Zip 源码只以 `7zXXXX-src.7z` 压缩包形式挂在 SourceForge，2025 年起才有官方 GitHub 仓库 `github.com/ip7z/7zip`（此前 GitHub 上流传的多是社区镜像，如 `kornelski/7z`，现已归档并指向官方仓库）。
- **平台覆盖**：
  - Windows：GUI + CLI（`7z.exe`/`7zG.exe`）+ `7z.dll`（供程序调用的核心引擎，导出"类 COM"接口）。
  - **Linux/macOS 官方支持**：自 **21.01（2021 年）** 起，Igor Pavlov 本人发布官方 Linux/macOS 命令行版本，可执行文件名为 `7zz`（动态链接）/`7zzs`（静态链接），支持 x86/x64/ARM/ARM64。这是纯 CLI，无 GUI。
  - **Android/iOS**：官方**从未**发布过任何形式的移植版本，也没有官方 `.so`/`.aar`/NuGet 包。
- **历史 POSIX 移植 p7zip**：由第三方开发者（非 Igor Pavlov）维护，**自 2016 年 16.02 版本后已停止维护**。目前各 Linux 发行版（Arch、Ubuntu 22.04+ 等）已逐步用官方 `7zip`（7zz）包替代 `p7zip`。Termux 生态里也存在同样的迁移（有 PR 把 Termux 的 p7zip 换成官方 7zip）。但一些第三方 fork（如 `jinfeihan57/p7zip`）仍在维护 p7zip 以延续兼容性。
- **重要衍生分支**：
  - `mcmilk/7-Zip-Zstd`：给 7-Zip 加入 Zstandard/Brotli/LZ4/Lizard 等现代编解码器，官方 7z.dll 默认不含这些算法。
  - `7-Zip-JBinding` / `7-Zip-JBinding-4Android`：Java/JNI 封装（详见第五章）。
- **许可证要点**（对二次分发/商用非常关键）：
  - 主体代码：**LGPL-2.1-or-later**。
  - RAR 解压代码：LGPL + **unRAR 限制条款**——只能用来解压，**不能用来实现兼容 RAR 的压缩器**（即不能"逆向"出 RAR 压缩算法）。
  - 部分代码（XXH64）：BSD-2-Clause。
  - LZMA SDK 本体：公有领域（Public Domain），可自由使用。
  - 结论：无论直接用官方 DLL/SO，还是通过任何 C# wrapper，只要底层链接的是官方引擎，都自动继承这些许可证条款，需要在分发时附带许可证文本，且不能移除 unRAR 限制。

### 2.2 7-Zip 的"类 COM"接口本质（对 wrapper 可行性至关重要）

7-Zip 的核心归档接口（`IInArchive`、`IOutArchive`、`ISequentialInStream` 等，定义在 `CPP/7zip/Archive/IArchive.h`）采用了 **COM 风格的 vtable + `QueryInterface`/`AddRef`/`Release`**，但这是 **7-Zip 自己在 `CPP/Common/MyWindows.h` 里手写实现的"仿 COM"**，并不依赖 Windows 操作系统真正的 COM 运行时（`ole32.dll`/注册表/`CoCreateInstance`）。因此：

- 这套接口**在 Linux/macOS/Android 上编译完全没有 OS 级障碍**——`7zz`（官方 Linux/macOS 版）和大量 NDK 交叉编译的社区实践已证明这一点。
- 真正的障碍在 **C# 侧如何把这套 vtable 接口封送进 .NET**：传统做法是用 `[ComImport]` + `[Guid]` 让 .NET 运行时的 **Windows COM Interop**（RCW/CCW 机制）帮忙做 vtable 调用，但 **.NET Core/5+ 明确规定 COM Interop 只在 Windows 上可用**，非 Windows 平台调用会直接抛 `PlatformNotSupportedException`。这正是绝大多数老牌 C# wrapper 无法跨平台的根本原因（详见第三章）。
- 2024 年随 .NET 8 引入的 **Source-Generated COM**（`[GeneratedComInterface]`/`[GeneratedComClass]`）在编译期生成纯 P/Invoke 的 vtable 封送代码，**不需要 Windows COM 运行时**，理论上可以在任意支持 P/Invoke 的 .NET 平台（包括 Android 的 Mono/CoreCLR）上工作，这是目前唯一"技术路径正确"的跨平台方案，但生态才刚起步。

---

## 三、NuGet 上的 C# Wrapper 生态调研

按照实现机制，可以把 NuGet 上能找到的方案分为四类：

### 3.1 传统 COM 封装类（Windows-only）

| 包名 | 状态 | 说明 |
|---|---|---|
| `SevenZipSharp`（原始，CodePlex 出身） | 已停更（最后发布 2011 年左右） | 最早的开源封装，`[ComImport]` 方式包 `7z.dll`，需调用方自备/放置 7z.dll |
| `Squid-Box.SevenZipSharp` | **已归档**（2024-04 最后一版 1.6.2.24） | 社区维护最久的 fork，升级到 .NET Standard 2.0/.NET Core 3.1，但**依然是 Windows-only**（COM Interop），支持"7z.dll 一切兼容格式" |
| `SharpSevenZip`（JeremyAnsel fork） | 仍在维护 | 目标框架升级到 .NET 6/8，功能与上面一致，**同样依赖 `[ComImport]`，Windows-only** |
| `SevenZipSharp.Net45` | 已废弃（README 明确写"THIS PROJECT IS DEAD"） | 历史版本 |
| `SevenZipExtractor`（adoconnection） | 仍有更新 | 更轻量，仅做**解压**（不支持压缩），内置 x86/x64 `7z.dll`，Windows-only |

这一类库的**功能覆盖特征**：
- 因为直接调用 `7z.dll` 的 `IInArchive`/`IOutArchive`，**只读格式覆盖非常全**（7z/zip/rar/arj/cab/chm/cpio/deb/dmg/fat/hfs/iso/lzh/lzma/msi/nsis/ntfs/rpm/split/udf/wim/xar/z 等 20+ 种，与官方 GUI 一致）。
- **可写格式仅限 7-Zip 本身支持写的 7 种**：7z、zip、gzip、bzip2、tar、wim、xz——这是官方引擎的天花板，任何 wrapper 都无法突破。
- 支持密码保护（7z AES-256、zip AES/ZipCrypto）、分卷、自解压（SFX，部分库如 SevenZipSharp/SharpSevenZip 内置 SFX 模块）、更新已有归档（需要较新版本 `7z.dll`，≥ 9.04）。
- **覆盖不到的部分**：CLI 专属子命令（`b` 基准测试、`t` 部分校验模式的输出格式、`rn` 改名、`-scrc`）、Windows 资源管理器右键菜单集成、FAR 插件、独立 GUI 的"添加到压缩包"对话框里的所有可视化选项（这些是 shell/GUI 层代码，未通过 COM 接口暴露）。
- **仅 Windows 可用**是这一整类库最大的限制——名字或描述里出现"跨平台"字样，但实际上大多只打包了 `win-x86`/`win-x64` 的 `7z.dll`，脱离 Windows 直接不可用。

### 3.2 新一代跨平台 Source-Generated COM 封装

| 包名 | 状态 | 说明 |
|---|---|---|
| `DuraIT.SevenZipSharper` | 2024 年起，活跃开发中 | 使用 .NET 8 的 `[GeneratedComInterface]`/`[GeneratedComClass]`，**不依赖 Windows COM 运行时**，随包自带 `runtimes/<rid>/native/` 下的 `7z.dll`(win-x64/x86/arm64) / `7z.dylib`(osx-x64/arm64) / `7z.so`(linux-x64/arm64) |

这是目前唯一在架构上真正解决"跨平台封装 7-Zip 引擎"问题的方案，其源码里专门处理了大量**跨平台坑点**（这些坑点本身也说明了移植难度）：
- `PROPVARIANT` 结构体在 Windows 上是 24 字节，在 POSIX（`MyWindows.h` 里的精简实现）上是 16 字节，必须按平台分别打包/解包，否则会出现 `AccessViolationException` 或 `E_INVALIDARG`。
- `BSTR`：Windows 标准 BSTR 是"4 字节长度前缀 + UTF-16 字符"，POSIX 版 7-Zip 自己模拟的 BSTR 是"4 字节长度前缀 + 4 字节 `wchar_t`"，必须分别处理。
- `wchar_t` 宽度不同（Windows 2 字节 / POSIX 4 字节），字符串封送需要按平台转换，否则会发生静默的数据损坏。

但需要注意：
- 该项目目前 **star 数 / 使用规模都很小**（几十 star 级别），属于**新兴、尚未被广泛验证**的方案，不代表生产级成熟度。
- **目前没有任何该类项目发布过 `android-arm64`/`android-x64` 等移动端 RID 的原生库**——也就是说，架构上"可行"，但 Android 目标平台的现成产物**尚不存在**，需要自己交叉编译并扩展绑定代码。

### 3.3 进程调用型封装（Shell out 到 `7z`/`7za`/`7zz` 可执行文件）

一些较简单的库或者自定义代码直接用 `Process.Start` 调用命令行 `7z`/`7za`/`7zz`，解析其标准输出。优点是**天然跨平台**（只要目标机器上装了对应可执行文件），甚至理论上可以在 Android 上通过 `exec` 调用交叉编译好的 `7zz` 可执行文件；缺点是：
- 依赖运行环境 `PATH` 中存在可执行文件，**Android 应用sandbox 环境下无法随意 `exec` 任意二进制**（尤其 Android 10+ 对 `/data/local` 执行权限、以及 Google Play 对可执行文件签名校验的限制），需要把可执行文件放进 APK 的 `lib/<abi>/` 目录并命名为 `lib*.so` 才能被系统允许加载执行，工程上等同于把 CLI 程序伪装成"共享库"，较为 hacky。
- 无法细粒度获取进度、流式处理（一般靠打印进度百分比 + 正则解析），大文件/内存流场景体验差于原生 API 封装。

### 3.4 纯托管重实现（不依赖任何原生 7-Zip 二进制）

| 包名 | 说明 |
|---|---|
| `SharpCompress` | 纯 C# 实现，支持 rar 解压、7z 解压/部分写入（LZMA2，非 solid 写、需要可寻址流）、zip/tar/gzip/bzip2/lzip/xz 读写、zstd（借助 `ZstdSharp`）。**天然跨平台**（.NET Standard 2.0+ 即可，无原生依赖），可以直接在 Android 上用。 |
| `GrindCore.SharpCompress` | SharpCompress 的增强版，把部分算法换成"本地性能"实现（其内部仍是通过原生库 GrindCore 提供 Deflate/LZMA/Zstd/LZ4/Brotli），7z 格式**只支持读，不支持写**。 |

这一类的**功能覆盖明显低于原生引擎路线**：
- 7z 格式只能做**非 solid** 压缩，不支持 PPMd 编码器、不支持 BCJ2 等复合过滤器链、不支持创建自解压 exe。
- RAR 只能解压（且 RAR5 支持历史上滞后），不能创建 RAR。
- 不支持 wim、iso、cab、chm、nsis、udf、dmg 等只读格式中的大多数（SharpCompress 的目标格式集本就比 7-Zip 引擎小很多）。
- 性能通常低于原生 LZMA SDK（纯托管实现，无 SIMD/汇编优化）。

---

## 四、"能否几乎完整覆盖 7-Zip 功能"——综合判断

把"7-Zip 的功能"拆成三层来看，结论会更清晰：

1. **底层压缩/解压引擎能力**（LZMA/LZMA2/PPMd/BZip2/Deflate + 各种 filter + 20 余种归档格式的读取）
   → 只要 wrapper 是"直接封送官方原生 `7z.dll`/`7z.so`/`7zz` 引擎"这条路线（3.1、3.2 两类），**覆盖率非常高，接近 100%**，因为归档解析代码本身就是官方那一份，没有重新实现的语义差异。
2. **归档写入/创建能力**
   → 官方引擎本身只支持创建 7 种格式（7z/zip/gzip/bzip2/tar/wim/xz），wrapper 完整覆盖了这个子集，但**用户如果期望"能创建 rar/iso/cab 等"，无论什么 wrapper 都做不到**——这是 7-Zip 自身的产品边界，并非集成问题。
3. **CLI/GUI 专属外围功能**（Windows Shell 集成、FAR 插件、benchmark、部分命令行开关、SFX 配置向导 UI）
   → 这些没有对应的 COM 接口，**任何 C# wrapper 都覆盖不到**，需要自己用 P/Invoke 调 `7z.dll` 里零散的导出函数（如 `GetHandlerProperty`）或者直接调 CLI 可执行文件并解析文本输出来"手工"实现，工程量不小。

**结论**：在 **Windows 桌面**场景下，选用 `SharpSevenZip`/`Squid-Box.SevenZipSharp`（或其继任者）这类成熟封装，可以认为对 7-Zip"可编程访问的核心归档能力"做到了**接近完整覆盖**；但如果用户期望的是"7-Zip 客户端软件的全部功能（含 GUI/Shell/CLI 辅助命令）"，覆盖率会明显下降，需要额外补充实现。若目标平台不是 Windows，则只有 3.2 类新兴项目在架构上具备同等覆盖率的潜力，但工程成熟度目前明显不足。

---

## 五、Android 生态兼容性专项分析

### 5.1 7-Zip 官方对 Android 的支持现状

- **官方零支持**：Igor Pavlov 官方从未发布过 Android 版本、`.so`/`.aar`、或任何 Android 相关的构建脚本；官方仓库/网站也没有 Android 相关文档。
- 用户能在应用商店看到的"7Z: Zip 7Zip Rar File Manager"等 Android App，都是第三方基于开源代码自行编译打包的产品，并非官方出品。

### 5.2 社区层面的可行性证明

已有多个独立的社区项目证明"把 7-Zip/p7zip 源码通过 Android NDK 交叉编译"在技术上是可行的：

| 项目 | 基于版本 | 产出物 | 状态 |
|---|---|---|---|
| `peijunbo/7zip-android` | 官方 7-Zip 23.01 | 交叉编译出 Android 可执行的 `7zz`（命令行） | 个人项目，教程性质 |
| `hzy3774/AndroidP7zip` | p7zip（较旧） | Android 库 + JNI，`P7ZipApi.executeCommand()` 直接下发 7z 命令行参数 | 个人练手项目，长期未维护 |
| Termux 的 `7zip` 包 | 官方 7zz | aarch64/arm/x86_64 二进制 | Termux 官方仓库已收录，但这是 Linux 终端环境（需要 root/终端权限），并非普通 Android App 可直接调用 |
| `7-Zip-JBinding-4Android`（omicronapps） | vendor 了 p7zip 源码 | Android AAR 库，通过 JNI 暴露 Java API | **相对最成熟**，Maven/Gradle 可直接引用，8500+ 单元测试，支持 7z/zip/rar/tar/gzip/bzip2 等创建，20+ 格式解压 |

其中 **7-Zip-JBinding-4Android** 是目前 Android 生态里工程质量最高、经过最多测试验证的方案，但它是 **Java 库**（`net.sf.sevenzipjbinding.*`），通过 JNI 直接把 C++ 引擎绑定到 JVM，跟 .NET/C# 没有直接关系。

### 5.3 .NET（MAUI / Xamarin.Android）视角下的可行路径与限制

.NET for Android 的原生互操作机制与纯 Android(Java/Kotlin) 生态不完全相同，这里有几个关键技术事实：

1. **.NET for Android 支持打包任意 NDK 编译的 `.so`**：只需把 `.so` 放进项目的 `Resources/libs/android/<abi>/` 或通过 NuGet 的 `runtimes/android-<abi>/native/` 路径，并将 MSBuild 的 `BuildAction` 设为 `AndroidNativeLibrary`，最终会被打进 APK 的 `lib/<abi>/` 目录。之后可以用普通的 **`[DllImport]`/`[LibraryImport]` P/Invoke** 直接调用，**不需要经过 JNI/JVM**（因为 Mono/CoreCLR 运行时自己做本地互操作，跳过了 Java 层）。这一点从技术上说，比很多人以为的"Android 必须走 JNI"更友好。
2. **但 COM 式接口的封送在 Android 上依然是难题**：
   - 传统 `[ComImport]` 方式在 Mono/.NET for Android 上**同样不被支持**（COM Interop 被定性为 Windows 专属特性，Xamarin/Mono 历史上从未在非 Windows 目标上完整实现它）。
   - 唯一可行方式是第三章提到的 **Source-Generated COM**（`[GeneratedComInterface]`），或完全手写"用函数指针数组模拟 vtable + `Marshal`/`delegate*<...>` 调用"的封送代码（工作量参照 `SevenZipSharper` 的 `Interop/Archive` 目录，需要处理 `PROPVARIANT`、`BSTR`、`wchar_t` 宽度等一系列跨平台细节，并且 Android 的 bionic libc 与桌面 Linux glibc 在个别 ABI 细节上还可能有差异，需要额外验证）。
   - **截至目前，没有发现任何已发布的 NuGet 包把编译好的 `android-arm64-v8a`/`android-armeabi-v7a`/`android-x86_64` 原生 7-Zip 库连同这层 Source-Generated COM 绑定一起打包发布**。也就是说，"这条路径理论可行，但目前没有现成产品"。
   - **⚠️ 补充结论（本报告 8.7 节真机实测发现，撰写本节时尚未发现）**：Source-Generated COM
     机制运行时依赖的 `System.Runtime.InteropServices.ComWrappers` 类**在 .NET for Android
     默认使用的 Mono 运行时上完全不受支持**（会抛 `PlatformNotSupportedException`），必须显式
     切换到 .NET 10 引入的**实验性** CoreCLR 运行时（`UseMonoRuntime=false`）才能让这条路径
     真正在 Android 上跑通。也就是说"理论可行"这个判断本身没错，但**可行的前提条件**比本节
     原文严格：不是"随便哪个 .NET for Android 运行时都行"，而是"必须搭配 CoreCLR（目前仍是
     实验性选项）"。详见 8.7 节的完整根因分析、官方证据与修复记录。
3. **Google Play 的 64 位要求**：Google Play 自 2019 年起要求新上架/更新的 App 必须提供 64 位原生库（`arm64-v8a`，很多情况下也要 `x86_64`）。像 `AndroidP7zip` 这类只提供 `armeabi-v7a`/`x86` 老旧编译产物的项目，如果不重新编译 64 位版本，将无法满足现代 Google Play 上架要求。
4. **可执行文件（如 `7zz`）伪装成 `.so` 后 `exec` 的方式**：技术上有人这样做过（例如把 `p7zip` 的 `7z`/`7za` 可执行文件放进 `lib/<abi>/` 并 `chmod`+`Process.Start` 调用），但这种做法：
   - 在较新 Android（10+/W^X 安全策略、Scoped Storage）上容易碰到执行权限、路径限制问题；
   - Google Play 的应用签名/校验机制对"App 私有目录内动态可执行的非 `.so` 二进制"控制越来越严格，长期可持续性存疑；
   - 是否违反 unRAR 许可证关于"禁止用来实现 RAR 压缩器"的条款需要自查（一般只读不影响）。

### 5.4 Android 现实可行方案对比

| 方案 | 是否需要自行编译原生库 | C#/.NET 集成难度 | 功能覆盖 | 成熟度 |
|---|---|---|---|---|
| 自编译 `ip7z/7zip` 源码为 `.so` ＋ 手写/Source-Generated COM 绑定 | 需要（NDK 交叉编译，需处理跨平台封送细节） | 高（需要自己实现类似 `SevenZipSharper` 的 Interop 层，并额外验证 Android bionic 的兼容性） | 与桌面 7-Zip 引擎理论一致（读全格式，写 7z/zip/gzip/bzip2/tar/wim/xz） | 低（目前无现成产品，需要自建并长期维护） |
| 通过 .NET for Android 的 Java Binding Library 封装 `7-Zip-JBinding-4Android`（AAR） | 不需要（AAR 已提供预编译 `.so` + Java API） | 中（需要写 `<AndroidLibrary>` 绑定项目，把 Java API 映射成 C# API，涉及 JNI 调用开销） | 高（20+ 只读格式、7z/zip/tar/gzip/bzip2 读写、密码、分卷等，8500+ 单元测试验证） | **相对最高**（该库本身在纯 Android/Java 生态中维护多年） |
| 直接 `exec` 交叉编译的 `7zz`/`7za` 可执行文件 | 需要（或直接用 Termux 的现成二进制） | 低（`Process.Start` + 解析文本输出） | 中（功能等同 CLI，但缺少细粒度流式 API、进度回调弱） | 中低（长期在 Android 沙箱权限模型下有兼容性风险） |
| 改用纯托管 `SharpCompress` 等库，放弃"7-Zip 原生引擎"这一约束 | 不需要 | 低（普通 NuGet 引用即可，天然支持 `net8.0-android` 等 TFM） | **明显低于原生引擎**（无 PPMd 写、无 solid 7z 写、RAR 仅读且版本滞后、无 iso/cab/chm/nsis/udf 等只读格式支持） | 高（成熟、活跃维护，但功能是"精简版"） |

---

## 六、专项论证：自行裁剪 7-Zip 源码 + 纯 C# 封装，面向 Android 做一个「7z/zip/tar/tar.gz + 分卷 + 密码」类库是否可行

本章针对一个更具体的落地方案做可行性论证，约束条件如下（均来自需求方）：

- 目标格式仅需 **7z、zip、tar、tar.gz**（gzip）四种，以及它们对应的**分卷（多卷/split）**创建与解压；
- 加密仅需 **7z、zip 的密码创建**（不要求 RAR/其他格式）；
- 其他格式不优先支持；
- 交付物是一个**类库项目**（供其他 .NET 项目引用的 SDK，而非独立 App）；
- **不允许引入 7-Zip/C# 之外的技术栈**（明确排除 Java/Kotlin/JNI 桥接层）。

**结论先行：技术上完全可行，且难度比"泛用型 7-Zip 封装"低不少，因为目标格式集大幅收窄之后，可以避开绝大多数跨平台风险最高的部分（RAR/unRAR 许可证代码、UI 层文件系统枚举逻辑、GUI/Shell 相关代码），核心工作量集中在"NDK 交叉编译一个裁剪过的原生库"+"用 .NET 8 Source-Generated COM 写一层薄封装"，这两块都有可直接参考的现成先例（并非从零摸索）。**

### 6.1 原生侧：为什么裁剪之后的编译难度更低

7-Zip 官方源码在 `CPP/7zip/Bundles/` 目录下本来就提供了多种"裁剪变体"作为构建目标（并非社区自创，而是官方构建系统自带的模式）：

| 官方 Bundle | 产物 | 内容 |
|---|---|---|
| `Format7zF` | `7z.dll` / `7z.so` | **全部格式**（含 RAR/ISO/CAB 等，唯一自带 `makefile.gcc` 的跨平台 bundle） |
| `Format7z` | `7za.dll` | 仅 7z 格式，含 AES 加密 |
| `Format7zR` | `7zra.dll` | 仅 7z 格式，精简编解码器（仅 LZMA/LZMA2），**不含加密** |
| `Format7zExtract(R)` | `7zxa.dll`/`7zxr.dll` | 仅 7z 格式，只解压 |

这说明"只编译需要的格式子集"是官方本就支持、且反复实践过的模式，并不是需要摸索的"魔改"。要满足本需求（7z + zip + tar/gzip + 密码），可以在 `Format7zF` 的基础上新增一个自定义 Bundle（例如 `Format7zLite`），只保留：

- **格式解析（Archive handler）**：`CPP/7zip/Archive/7z/*`、`CPP/7zip/Archive/Zip/*`、`CPP/7zip/Archive/Tar/*`、`CPP/7zip/Archive/GZip/*`，以及它们共用的 `CPP/7zip/Archive/Common/*`。
- **编解码器（Codec）**：LZMA/LZMA2（`C/Lzma*.c`、`CPP/7zip/Compress/Lzma*Coder.cpp`）、Deflate（zip/gzip 默认算法）、BZip2（可选，tar.bz2 若不需要可去掉）、Copy（存储不压缩）。PPMd、ARM/ARM64/BCJ2 等特殊过滤器可按需去掉以进一步减小体积。
- **加密（Crypto）**：7z 用 `CPP/7zip/Crypto/7zAes.cpp`（AES-256 + SHA-256 派生密钥），zip 用 `CPP/7zip/Crypto/ZipCrypto.cpp`（传统弱加密）和/或 `WzAes.cpp`（WinZip AES-256，建议优先支持这个而非老 ZipCrypto）。**这两块都在 LGPL 主体协议下，与 unRAR 无关**。
- **通用基础设施**：`CPP/Common/*`、`CPP/7zip/Common/*`（流封装、`InBuffer`/`OutBuffer`、CRC 等）、`C/` 下的辅助 C 代码（`Alloc.c`、`CpuArch.c`、`Sha256.c` 等）。

**明确可以整体剔除的部分**：`CPP/7zip/Archive/{Rar, Iso, Cab, Chm, Nsis, Rpm, Deb, Udf, Wim, Xz(可选), ...}`、`CPP/7zip/Crypto/{Rar*, Aes256支持RAR部分}`、以及全部 `CPP/7zip/UI/{GUI, FileManager, Explorer, Far, SFXWin, SFXSetup}`（这些是 Windows Shell/GUI 代码，Android 用不到，也不必编译）。**去掉 RAR 相关代码后，产物不再包含 unRAR 限制条款覆盖的代码，许可证归属简化为纯 LGPL-2.1-or-later**，对商用分发更友好。

这在工程上就是"改 Makefile 里的目标文件列表 + 去掉几个格式的注册宏（`REGISTER_ARC` 调用）"，属于**构建配置层面的裁剪，不涉及修改编解码算法本身的逻辑**，风险和工作量都可控，是本报告认为"简单修改"这一说法成立的依据。

### 6.2 交叉编译到 Android NDK：已有先例验证

- 已有独立开发者项目（`peijunbo/7zip-android`）证明：把 `CC`/`CXX` 指向 Android NDK 的 `aarch64-linux-android21-clang`/`armv7a-linux-androideabi21-clang`/`i686-linux-android21-clang`/`x86_64-linux-android21-clang`，直接对 `CPP/7zip/Bundles/Alone2/makefile.gcc`（7-Zip 官方 23.01 源码）做少量修改后即可编译出可在 Android 上运行的产物，覆盖 `arm64-v8a`/`armeabi-v7a`/`x86`/`x86_64` 四种 ABI。
- `Format7zF`（即目标产物形态为**动态库 `.so` 而非可执行文件**）与 `Alone2` 编译的是同一套底层格式/编解码代码，只是链接方式与入口点（`main()` vs `CreateObject`/`GetHandlerProperty` 导出函数）不同，因此没有理由认为 `.so` 形态会比已经验证过的可执行文件形态更难编译，风险主要集中在导出符号表（`.def`/`--version-script`）的适配上，这是常规 NDK 交叉编译共享库的标准操作。
- 另需注意的 Android/Bionic 特有细节（在正式立项前应做小规模验证）：
  - **最低 API Level**：`arm64-v8a`/`x86_64` 要求 NDK 最低 API 21；若要用到 64 位时间戳等特性建议以 API 24+ 为基线。
  - **`wchar_t` 宽度**：Android Bionic 与桌面 Linux 一致，`wchar_t` 为 4 字节，这与 `SevenZipSharper` 项目已经处理过的"POSIX 分支"逻辑（`PROPVARIANT` 16 字节、BSTR 用 4 字节字符）是同一套，理论上**可以直接复用这部分 C# 封送代码，无需为 Android 单独重写**。
  - **无 `iconv`/locale 完整实现**：7-Zip 内部文件名编码转换主要走自研的 UTF-8⇄UTF-16 转换函数（`CPP/Common/UTFConvert.cpp`），不依赖系统 `iconv`，风险较低，但仍建议实测中文/emoji 文件名等边界情况。
  - **大文件/LFS**：Android 自 API 21 起系统调用默认已是 64 位安全（`off64_t`），一般不需要特殊宏，但建议编译时显式确认 `_FILE_OFFSET_BITS=64` 生效。
  - 汇编优化的 CRC/AES/SHA（`Asm/` 目录，x86 为主，近年也补充了 ARM64 版本）在 NDK 工具链下能否直接汇编存在不确定性，**建议第一版直接使用纯 C 回退实现（7-Zip 源码本身就有 C 版本兜底），牺牲一部分性能换取可移植性和更低的调试成本，后续再按需引入 NEON/ARM 汇编优化**。

### 6.3 分卷（多卷/Split）功能：不需要修改任何原生代码

这是本方案里最值得强调的"好消息"：**7-Zip 官方作者 Igor Pavlov 本人在其官方论坛上明确说明，"-v" 分卷功能并不在 `7z.dll`/核心归档引擎内部实现**（对 7z/zip/tar/gzip 都一样，是**格式无关**的通用字节流切分），而是在调用方（CLI `7z.exe`）里用一个很薄的流包装类实现的：

- 写入端参考实现：`CPP/7zip/UI/Common/Update.cpp` 里的 `COutMultiVolStream`——本质是实现 `Write()`/`Seek()`，写满设定的卷大小（如 10MB）就自动切换到下一个 `xxx.001`/`xxx.002`... 文件。
- 读取端参考实现：`CPP/7zip/Archive/Common/MultiStream.h`（`CInMultiVolStream`）——把若干个物理分卷文件首尾相接，对上层呈现为一个连续的输入流。
- 另有 `CPP/7zip/Archive/Split/` 提供"打开一个 `.001` 文件时自动探测并拼接后续分卷"的通用识别逻辑，同样是格式无关的薄层。

**这意味着分卷功能完全可以在 C# 侧实现**：只要我们已经通过 Source-Generated COM 实现了喂给原生编码器/解码器的 `ISequentialOutStream`/`IInStream`（这是让原生引擎读写数据必备的回调接口，无论如何都要写），只需在这层托管代码里加入"写满 N 字节自动换下一个 `FileStream`""读到文件尾自动打开下一个分卷"的逻辑即可，**原生 C++ 源码一行都不用改**。7-Zip 源码里的 `COutMultiVolStream`/`CInMultiVolStream` 可以作为托管实现的行为参考（对照着translate 成 C#），不需要照搬 C++ 代码本身。

需要提醒的一点澄清：这种分卷是"**对整份归档文件做纯字节切分**"（7z、zip、tar、tar.gz 均适用同一套逻辑），生成的 `.7z.001/.002...` 或 `.zip.001/.002...` 是 7-Zip 生态内的通用做法；如果最终交付物需要与其他工具（如 WinRAR 的多卷 zip、跨平台 unzip 工具）互操作，需要额外确认对方是否认可这种"泛型切分"命名/结构，而不是 PKZIP 规范里"真正的多卷 zip"格式（后者在卷标记、中心目录记录上有专门约定）。若只是自己应用内部产生和消费的分卷归档，这个差异不影响使用。

### 6.4 密码/加密功能：直接复用已有 Crypto 模块，无需改动

- **7z 加密**：`7zAes.cpp` 实现 AES-256-CBC，密钥由密码通过 SHA-256 多轮迭代派生（含随机 salt），同时支持"仅加密文件内容"和"连文件名/大小等头信息一起加密"（`-mhe=on`，对应 `IOutArchive`/`ISetProperties` 里设置属性即可，无需碰底层代码）。
- **zip 加密**：`ZipCrypto.cpp`（传统 PKWare 弱加密，兼容性最好但强度低，**不建议作为默认**）和 `WzAes.cpp`（WinZip 定义的 AES-256 zip 加密，強度高，主流工具如 7-Zip/WinRAR/WinZip 均可解，**建议作为默认选项**）。
- 这两部分代码都不涉及 unRAR 限制条款，是纯 LGPL 部分，**保留即可，无需修改一行加密算法代码**，C# 侧只需要在创建归档时通过 `ICryptoGetTextPassword2` 回调把密码传给原生层（这是标准的 `IArchiveUpdateCallback` 扩展接口，`SevenZipSharper` 的 `ARCHITECTURE.md` 里已经给出了这部分的 C# 接线示例，可以直接参考）。

### 6.5 C# 封装侧：技术路线与工作量评估

由于需求明确排除引入 Java，这就锁定了必须走"纯 P/Invoke"路线，而不能走 `7-Zip-JBinding-4Android` 那种 JNI 路线。具体做法：

1. **接口封送机制**：使用 .NET 8+ 的 **Source-Generated COM**（`[GeneratedComInterface]` + `[GeneratedComClass]`），而不是传统 `[ComImport]`（后者在非 Windows/Mono 环境不可用，Android 同样不可用）。这是一条**已经被验证过的路线**——`SevenZipSharper` 项目已经用这套机制在 Windows/macOS/Linux 三端跑通了同一份 7-Zip"类 COM"接口封送逻辑（`IInArchive`/`IOutArchive`/`ISequentialInStream`/`IArchiveUpdateCallback` 等），并且专门处理了 `PROPVARIANT`（16 字节 POSIX 版）、自定义 `BSTR`（4 字节 `wchar_t`）等跨平台细节——**这些处理逻辑对 Android（同为 POSIX/Bionic）大概率可以直接复用或只需小幅适配**，不需要从零设计。
2. **只需实现目标格式相关的最小接口子集**：不需要像通用型 wrapper 那样兼容全部 20 多种格式属性/回调，只需要覆盖 7z/zip/tar/gzip 四种格式创建与解压所需的 `IInArchive`、`IOutArchive`、`IArchiveUpdateCallback(2)`、`IArchiveExtractCallback`、`ICryptoGetTextPassword(2)`、`ISetProperties` 等接口，接口面比通用方案窄很多，工作量相应降低。
3. **原生库加载与分发**：把交叉编译出的 `lib7zlite.so`（`arm64-v8a`/`armeabi-v7a`/`x86_64`，视目标设备决定是否还要 `x86`）按 `.NET for Android`/`.NET MAUI` 的约定放进 `runtimes/android-arm64/native/`、`runtimes/android-arm/native/` 等目录并标记为 `AndroidNativeLibrary`（或者更底层地直接放 `Resources/libs/android/<abi>/` 目录），最终会被打进调用方 App 的 APK `lib/<abi>/` 下，调用方通过 `[LibraryImport("7zlite")]` 之类的方式即可像调用普通本地 DLL 一样调用，**不经过 JNI/JVM**，满足"不引入 Java"的约束。
4. **文件系统枚举逻辑无需照搬 CLI**：由于目标是"类库"而非"命令行工具"，压缩时"选哪些文件打进包"这件事完全可以由调用方（上层 C# 业务代码）决定并通过流的方式喂给原生编码器（`IArchiveUpdateCallback::GetStream` 等回调），**不需要移植 7-Zip UI 层里复杂的目录遍历/过滤逻辑**（`CPP/7zip/UI/Common/EnumDirItems.cpp` 等），这部分完全可以、也应该用 C#（`System.IO`）现成的目录遍历 API 实现，进一步减少需要移植的原生代码量。

### 6.6 综合工作量与风险评估

| 阶段 | 内容 | 相对工作量 | 主要风险 |
|---|---|---|---|
| 1. 源码裁剪与 Bundle 定义 | 新建精简 Bundle，剔除 RAR/ISO/CAB 等及 UI/GUI 代码 | 低-中 | 需要熟悉 7-Zip 源码结构，找全所有依赖的 `.cpp`（编译报错驱动查漏） |
| 2. NDK 交叉编译四种 ABI 的 `.so` | 参考已有 `Alone2` 交叉编译先例，改造为共享库产物 | 中 | 导出符号表、`wchar_t`/LFS 等平台细节需要实测验证；汇编优化模块建议先跳过 |
| 3. C# Source-Generated COM 封装层 | 参考 `SevenZipSharper` 架构，缩小接口面到 7z/zip/tar/gzip | 中 | PROPVARIANT/BSTR 跨平台封送需要在真机/模拟器上跑通，Bionic 与 glibc 可能有未知的细微差异需要调试 |
| 4. 分卷读写（C# 层） | 参照 `COutMultiVolStream`/`CInMultiVolStream` 思路用 C# 重写 | 低 | 边界条件（跨卷续写、最后一卷大小、异常中断续传）需要充分测试 |
| 5. 密码/加密接线 | 通过既有 `ICryptoGetTextPassword2`/`ISetProperties` 属性传参 | 低 | zip 建议默认 WinZip AES 而非弱 ZipCrypto，需要在 API 设计上做默认值取舍 |
| 6. 打包为 NuGet/类库，多 ABI 集成测试 | 按 `.NET for Android` RID 规范打包，真机覆盖测试 | 中 | 需要在多种 Android 版本（尤其是新版本的 Scoped Storage、16KB 页大小适配等）上实测 |

**总体判断**：这是一个目标明确、边界清晰、有多个独立技术点均已被他人验证过可行性的"中等复杂度自研 SDK"项目，不存在"做不到"的硬性技术障碍；比起"通用型、覆盖 7-Zip 全部格式"的跨平台封装（第五章的结论），本方案因为**主动收窄了格式范围、明确放弃 RAR 等法务/工程复杂度较高的部分、并且把文件枚举和分卷逻辑上移到托管代码实现**，反而是目前所有 Android + 7-Zip 集成路径里**工程风险最低、最适合"自己掌控全部代码、不引入 Java 依赖"这一约束**的方案。唯一要投入的是团队自身的 C++（NDK 交叉编译/源码裁剪）与 C#（Source-Generated COM/流式 P/Invoke）双技能实现与测试工作，目前没有现成的开源产品可以直接拿来用，需要自建并长期维护（后续 7-Zip 升级、新增 Android 版本适配都需要持续投入）。

---

## 七、给决策者的建议

1. **如果目标只是 Windows 桌面/服务端应用**：直接选用 `SharpSevenZip`（或继续使用已归档但仍可用的 `Squid-Box.SevenZipSharp`），配合自带最新版官方 `7z.dll`，可以获得对 7-Zip 归档能力接近完整的覆盖，是目前最省力、最成熟的方案。若担心许可证问题（LGPL 的动态链接一般无强制开源要求，但 unRAR 限制仍需遵守），建议在分发物中附带 7-Zip 的 `License.txt`。

2. **如果需要真正跨平台（Windows + Linux + macOS 桌面/服务端，不含移动端）**：可以评估新兴的 `DuraIT.SevenZipSharper`，但鉴于其发布时间短、社区验证不足，建议先做充分的兼容性/回归测试，或者作为观察对象而非立即生产落地。

3. **如果目标包含 Android（.NET MAUI / Xamarin.Android）**：
   - 若能接受额外引入一层 Java 依赖：优先考虑封装 `7-Zip-JBinding-4Android`（AAR）为 .NET Java Binding Library，是目前功能覆盖度和工程成熟度最好的现实路径。
   - 若必须保持纯 C#/P-Invoke、不想引入 JVM 依赖：需要自行完成"NDK 交叉编译 7-Zip 源码 + Source-Generated COM 绑定层"这一整套工作，目前市面上**没有现成的 NuGet 包**可以直接拿来用，需要评估自研投入（参考 `SevenZipSharper` 开源代码作为起点，但仍需为 Android RID 补充原生库编译脚本和绑定代码，并做设备兼容性测试）。
   - 若功能需求可以适当降级（例如只需要 zip/gzip/tar 的读写，不强求 7z 的 PPMd/固实压缩、不需要 RAR/iso 等冷门格式）：直接用纯托管的 `SharpCompress`，可以零成本获得 Android 支持，是"能马上落地"的务实选择，但要向业务方明确说明功能子集与原生 7-Zip 的差距。

4. **无论选择哪条路径**，都建议对以下几个"隐藏成本"提前评估：
   - 7-Zip 版本更新节奏较快（新版本会加入新的压缩过滤器如 ARM64 filter、修复安全漏洞），wrapper/原生库需要有持续更新机制，否则功能和安全性会逐渐落后。
   - unRAR 许可证限制、LGPL 动态链接合规要求。
   - 若涉及 zstd/brotli/lz4 等现代编解码器，需要切换到 `7-Zip-Zstd` 等第三方 fork 的原生库，而非官方 `7z.dll`/`7zz`，多数 C# wrapper 默认不包含这些 codec。

---

## 八、演示项目：从报告结论到可运行代码的落地验证

本章记录第六章"自行裁剪 7-Zip 源码 + 纯 C# 封装"方案的**实际落地实现**与在 Android 构建链路上的**真实验证结果**（而非纯文档推演）。全部代码位于仓库 `demo/` 目录，原生库交叉编译脚本位于 `7zip-src/CPP/7zip/Bundles/Format7zLite/`。

### 8.1 交付物结构

| 路径 | 说明 |
|---|---|
| `7zip-src/CPP/7zip/Bundles/Format7zLite/` | 裁剪版 7-Zip Bundle 源码 + `makefile.gcc`，只编译 7z/zip/tar/gzip 解码器与编码器，剔除 RAR/ISO/CAB 等 |
| `7zip-src/CPP/7zip/Bundles/Format7zLite/build_android.sh` | Linux/WSL 下用 Android NDK r27c 交叉编译 4 个 ABI 的 `7zlite.so`（已验证） |
| `7zip-src/CPP/7zip/Bundles/Format7zLite/build_android.ps1` | **本轮新增**：原生 PowerShell 脚本，直接调用 Windows 版 NDK clang，不依赖 WSL/MSYS2（详见 8.5） |
| `7zip-src/CPP/7zip/Bundles/Format7zLite/MissingCoderStubs.cpp` | 补齐 `NCrypto::NZipStrong::CDecoder`（WinZip Strong Encryption 解密，明确不支持）等被裁剪掉实现的类缺失的构造函数/虚方法桩实现，避免链接产物里出现导致 `dlopen()` 崩溃的未定义 vtable（详见 8.6） |
| `demo/SevenZipLite/` | 类库：Source-Generated COM（`LibraryImport`）封装层，`net10.0`，纯 C#，不依赖任何 Java/JNI |
| `demo/SevenZipLite.SelfTest/` | 纯 `System.IO` 逻辑的往返测试用例集（7z/zip/tar/tar.gz × 基本/密码/分卷/分卷+密码，共 11 组），供控制台与 MAUI 共用 |
| `demo/SevenZipLite.Tests/` | 控制台宿主，调用 `SelfTestRunner` 在 Linux x86_64 上跑 11 组用例 |
| `demo/SevenZipLite.MauiDemo/` | **本轮新增**：.NET MAUI（`net10.0-android` 单目标）演示 App，验证同一套 C# 互操作层在 Android 运行时上的构建与打包可行性 |

### 8.2 host 端（Linux x86_64）回归结果

`SevenZipLite.Tests` 通过共享的 `SelfTestRunner.RunAll` 跑 7z/zip/tar/tar.gz 四种格式 × 基本/密码/分卷/分卷+密码的组合（tar/tar.gz 不支持密码，故为 2×4−1=7 变体 + 4 基本 = 11 组），**11/11 全部通过**，证明 C# 互操作层（PROPVARIANT 封送、多卷流、Source-Generated COM 生命周期管理）在托管层的逻辑正确性，且这套逻辑与 Android 上运行的是**完全相同的一份 IL 代码**（`SevenZipLite.dll`／`SevenZipLite.SelfTest.dll` 直接被 MAUI 工程引用，未做任何平台特化分支）。

### 8.3 Android 原生库交叉编译结果

在全新拉取的 Android NDK r27c（`https://dl.google.com/android/repository/android-ndk-r27c-linux.zip`）下，`build_android.sh` 一次性稳定重建了全部 4 个 ABI：

| ABI | 产物大小 | `llvm-nm -D --undefined-only` 符号检查 |
|---|---|---|
| arm64-v8a | 1,308,904 字节 | 通过，除 libc/libm/libdl 标准符号与 1 个可安全忽略的弱符号外无任何未定义符号 |
| armeabi-v7a | 1,053,228 字节 | 通过 |
| x86_64 | 1,302,296 字节 | 通过 |
| x86 | 1,291,744 字节 | 通过 |

（以上是 8.6 节所述 `-static-libstdc++` 修复**之后**的最终产物大小；相比修复前体积增大约 25%~30%，
是因为把 libstdc++ 运行时代码静态链接进了 `.so` 本身，用体积换取"不依赖运行时才能确认存在与否的
`libc++_shared.so`、且能在 `dlopen()` 时一次性完成全部符号解析"的正确性，这个取舍是必要的。）

这证明此前针对 7-Zip 源码的裁剪与链接修复（详见第六章）对全部 4 个 Android ABI 普遍生效，不是只对单一架构凑巧可行。

### 8.4 .NET MAUI 集成与 Android 构建实测

在沙箱内额外搭建了最小 Android SDK（命令行工具 + `platform-tools` + `platforms;android-34/36` + `build-tools;34.0.0/36.0.0`，通过 JDK 21 驱动 `sdkmanager`）后，对 `SevenZipLite.MauiDemo` 做了实际的 `dotnet build -f net10.0-android` 验证，关键结果：

- **项目结构验证通过**：`<AndroidNativeLibrary Include="NativeLibs/<abi>/lib7zlite.so" />` 的声明方式被 MSBuild 正确识别，4 个 ABI 目录（`arm64-v8a`/`armeabi-v7a`/`x86_64`/`x86`）与 Android 标准 ABI 命名一致；`LibraryImport("7zlite")` 在 Android 运行时会被 .NET 运行时自动解析为 `lib7zlite.so`，无需额外的库名映射代码。
- **Debug 配置构建成功并产出已签名 APK**：`dotnet build -f net10.0-android -c Debug` 顺利完成，生成 `com.companyname.sevenziplite.mauidemo-Signed.apk`。用 `unzip -l` 检查 APK 内容，确认 `lib/arm64-v8a/lib7zlite.so` 与 `lib/x86_64/lib7zlite.so` 被正确打进了 APK（Debug 默认走 "Fast Deployment" 路径，只为最常见的两个模拟器/真机架构打包，这是 .NET for Android 的默认行为而非本项目特有限制）。这是本次交付里**从源码到可安装 APK 的完整链路第一次被真实验证通通**。
- **Release（全部 4 ABI）配置在本沙箱内未能完整跑通，原因是构建主机资源限制而非架构缺陷**，具体两个独立问题：
  1. 默认开启的 `RunAOTCompilation`（Release 下 Android 项目的默认值）需要 `Microsoft.NETCore.App.Runtime.Mono.linux-x64` 10.0.12 这个交叉编译宿主运行时包，但该版本目前未发布在 `nuget.org`（已验证的公开源里最高只有 9.0 系列预览版），这是 .NET 10 Android 工作负载在纯净沙箱环境下的一个已知 NuGet 供给缺口，不是本项目代码的问题；加 `-p:RunAOTCompilation=false -p:PublishTrimmed=false` 后可以绕开。
  2. 绕开 AOT 限制后，Release 下同时构建多 ABI 会在 `EnsureAllArchitecturesAreIdentical`（.NET for Android 的"marshal methods"多架构一致性校验，一个已知的 Android 工作负载内部实现细节）阶段偶发内部错误，加 `-p:AndroidEnableMarshalMethods=false` 可绕开该校验路径；但即使限制到单一 ABI（`android-x64`）重跑，Android 的 `d8`（Java 字节码打包为 dex 的工具）在本沙箱（**总内存仅 1.9GB，且无 swap**）下仍被内核 OOM Killer 以退出码 137 杀死。这是构建主机内存不足导致，在配备 ≥4GB 内存的常规开发机或 CI Runner 上不会重现（Android 官方文档建议 Android Studio/命令行构建至少准备 8GB 内存）。

**结论**：Debug 配置已经完整验证了"MAUI + 裁剪版 7-Zip 原生库 + 纯 C# P/Invoke"这套技术方案在 Android 构建链路上**从源码编译、原生库打包到生成可安装签名 APK 全流程可行**；Release 全 ABI 打包在**功能上没有已知障碍**，本次未能在沙箱内完整走完只是受限于沙箱的内存配额与该 .NET 10 时间点上 nuget.org 的 AOT 交叉包供给情况，建议用户在自己的开发机（内存 ≥4GB）或 CI（如 GitHub Actions 的标准 Windows/Linux/macOS runner 均为 7GB+ 内存）上重跑本仓库脚本以产出生产用的 Release APK。

### 8.5 Windows 构建脚本（原生，不依赖 WSL）

`build_android.ps1`（与 `build_android.sh` 同目录）最初的版本是"调用 WSL 复用 Linux 版脚本"，但这要求额外装好 WSL2，用户反馈更希望直接复用自己机器上已经配置好的原生 Windows NDK（例如安装在 `C:\Program Files (x86)\Android\` 下）。因此改为纯原生 PowerShell 实现，不依赖 WSL，也不依赖 MSYS2/Cygwin/mingw32-make。

**为什么不能直接在 Windows 上跑 `make -f makefile.gcc`**：7-Zip 官方的 `makefile.gcc`/`7zip_gcc.mak` 内部会用 `SystemDrive`/`SYSTEMDRIVE` 环境变量判断"是否在 Windows 上构建"（`ifdef IS_MINGW`）。这两个变量在任何 Windows 命令行环境下都天然存在，一旦命中就会切换到"用 MinGW GCC 构建原生 Windows 7z.dll"的分支——链接 `-loleaut32 -luuid -ladvapi32 -luser32` 等一整套 Windows 专属导入库、产物后缀改成 `.exe`——这与我们要做的"用 NDK clang 交叉编译 Android `.so`"是完全不同的目标，直接跑会在链接阶段报错（找不到这些 Windows 库）。即使想办法覆盖这个判断（如在 make 命令行传入空的 `SystemDrive=`），Windows 上原生也没有 `rm`/`mkdir -p` 这类 POSIX 工具供 recipe 使用，Android NDK 自带的 Windows 版 `prebuilt/windows-x86_64/bin/` 目录里也只有 `make.exe`/`echo.exe`/`cmp.exe` 三个类似工具，没有完整的 coreutils。

**实际做法**：与其在 `make` 这一层做兼容性对齐，脚本改为绕开 `make`，直接复现已验证的编译产物。具体流程是先在本次验证用的 Linux 环境里，对已经跑通的构建执行 `make -n -f makefile.gcc ...`（dry-run，只打印命令不真正编译），完整导出该 Bundle 实际会执行的全部 **133 条编译命令 + 1 条链接命令**（含每个源文件用 C 还是 C++ 编译器、宏定义、`XzDecoderStub.cpp` 那一条特殊的 `-I` 参数等），固化进脚本里的 `$Entries` 数据表；脚本在 Windows 上运行时，用 PowerShell 原生的 `New-Item`（代替 `mkdir -p`）创建输出目录，然后逐条直接调用 NDK 自带的 Windows 版编译器包装脚本（`<triple><api>-clang.cmd`/`<triple><api>-clang++.cmd`，例如 `aarch64-linux-android21-clang++.cmd`——这些包装脚本内部只是转发给 `clang.exe --target=<triple><api>`，与 Linux 上同名的包装脚本行为一致）完成编译和链接，全程不需要 `make`、`rm`、`mkdir -p`，因此也就不存在 `IS_MINGW` 误判的问题。

脚本支持：
- `-Abi <arm64|armeabi_v7a|x86_64|x86|all>` 选择构建单个或全部 ABI；
- `-NdkRoot <路径>` 手动指定 NDK 位置；不指定时会在常见安装位置（含用户提到的 `C:\Program Files (x86)\Android`、Android Studio 默认的 `%LOCALAPPDATA%\Android\Sdk\ndk\<版本号>` 等）递归查找 `clang.exe` 自动定位；
- 复用 `build_android.sh` 里同样的 `llvm-nm.exe` 未定义符号体检逻辑。

**验证方式**：由于沙箱是纯 Linux 环境、没有真实 Windows 机器可用，无法对着真实 NDK 跑一遍。但做了两层验证以确保脚本本身可靠：(1) 下载 NDK r27c 的 **Windows 版**安装包（`android-ndk-r27c-windows.zip`），核实其中确实包含 `toolchains/llvm/prebuilt/windows-x86_64/bin/<triple>21-clang(++).cmd` 这些包装脚本，且内容确认只是转发到 `clang.exe --target=...`，与 Linux 侧行为一致；(2) 在沙箱里装好 PowerShell 7（Linux 版二进制），用一组"假编译器"脚本（只记录调用参数、在 `-o` 目标位置创建占位文件）替换真实编译器，完整跑通 `build_android.ps1 -Abi all`，确认对 4 个 ABI 分别产生了预期的 **134 次调用**（133 次编译 + 1 次链接），且编译参数、源文件列表、链接时的目标文件顺序与 `make -n` 导出的原始记录**逐字节一致**。这证明了脚本的参数拼装、文件遍历、ABI 到三元组映射等全部逻辑正确，唯一未覆盖的是"真实 NDK clang.exe 在 Windows 上编译出的目标文件是否与 Linux 侧字节级等价"，这一点原理上应当成立（同一版本的 clang/lld、相同的 `--target=`、相同的源码和宏定义，只是宿主 OS 不同），但仍建议用户在自己的 Windows 机器上执行一遍，确认 `llvm-nm` 符号体检通过、且产物能被 8.4 节验证过的 MAUI 项目正常打包和运行。

**8.5.1 一次真实 Windows 运行反馈出的真实缺陷（已修复）**：用户在自己的 Windows 机器上（NDK 装在 `C:\Program Files (x86)\Android\AndroidNDK\android-ndk-r23c`）实际跑了脚本，第一个文件 `C\7zBuf2.c` 就报"编译失败...退出码=1"、且看不到任何编译器诊断信息。定位后发现这不是源码或参数问题，而是**脚本调用方式踩中了 NDK 自身的一个已知缺陷**：脚本最初版本调用的是 NDK 自带的 `<triple><api>-clang.cmd` 包装脚本，而下载并对比了 NDK **r23c**（问题版本）与 **r27c**（此前验证用的版本）的这个包装脚本源码后发现两者有一处关键差异——

```bat
:: r23c（有问题）：%_BIN_DIR%clang.exe 没有加引号
set "_BIN_DIR=" && %_BIN_DIR%clang.exe --target=aarch64-linux-android21 %*

:: r27c（已修复）：加了引号
set "_BIN_DIR=" && "%_BIN_DIR%clang.exe" --target=aarch64-linux-android21 %*
```

当 NDK 装在带空格的路径下（`C:\Program Files (x86)\...` 正是 Windows 最常见的默认安装位置！）时，r23c 这行未加引号的展开会被 `cmd.exe` 按空格拆词，实际尝试执行的变成了并不存在的 `C:\Program`，得到"不是内部或外部命令"的错误；包装脚本又用 `if ERRORLEVEL 1 exit /b 1` 把这个错误统一"吞"成了退出码 1，导致完全看不到真实原因。这是一个在社区里对老版本 NDK 的 `ndk-build.cmd`/`clang.cmd` 有多次类似报告的已知问题，并非本项目代码或 7-Zip 源码的问题。

**修复方案**：脚本改为完全不经过 `<triple><api>-clang(++).cmd` 包装脚本，而是直接调用 `clang.exe`/`clang++.exe` 本体，并由脚本自己拼接 `--target=<triple><api>` 参数（包装脚本原本也只做这一件事）。PowerShell 的 `&` 调用操作符对 `.exe` 是直接走 `CreateProcess`，不会再经过 `cmd.exe` 对未加引号路径的二次拆词，因此无论 NDK 装在什么路径（哪怕带空格、括号）都能正常工作，也不再依赖具体某个 NDK 版本是否修好了包装脚本里的引号问题。同时给编译/链接失败路径加了明确的错误输出：脚本现在会把编译器的原始 stdout/stderr 实时打印到控制台，并额外写入 `_o_android_<abi>/build.log`，避免再出现"只看到退出码、看不到真实错误"的情况。修复后用模拟编译器在"路径含空格+括号"的沙箱场景下重新跑通了单 ABI 与全部 4 ABI（536 次调用），并额外验证了故意注入的编译失败能被正确捕获并把错误信息呈现给用户。

**8.5.2 第二个真实发现的问题：clang 12（NDK r23c）在 32 位 ARM 上的后端崩溃（已用编译参数规避）**：修好上面的路径引号问题后，用户重新在真实 Windows + NDK r23c 上跑通了 arm64（`llvm-nm` 符号体检干净，产物 1,012,384 字节），但 armeabi_v7a 在编译 `C/AesOpt.c` 时崩溃：

```
fatal error: error in backend: Cannot select: intrinsic %llvm.arm.neon.aesmc
... Running pass 'ARM Instruction Selection' on function '@AesCbc_Encode_HW' ...
Android clang version 12.0.9 ...
clang: error: clang frontend command failed with exit code 70
```

排查后定位到：这不是本项目源码裁剪或参数配置的问题，而是 **7-Zip 上游源码自己也在注释里承认的、跨 clang 版本高度脆弱的一段代码**。`C/AesOpt.c`（以及用同一套判断逻辑的 `C/Aes.c`、`C/Sha1Opt.c`、`C/Sha256Opt.c`、`C/7zCrc.c`、`C/SwapBytes.c`、`CPP/7zip/Crypto/MyAes.cpp`、`CPP/Windows/SystemInfo.cpp`）为了在 32 位 ARM（armeabi-v7a）上也能用到 ARMv8 Crypto 扩展指令做 AES/SHA/CRC 硬件加速，用了一个"技巧"：通过 `__attribute__((__target__("armv8-a,aes")))` 之类的函数级 target 属性、并在 `#include <arm_neon.h>` 之前手动把 `__ARM_ARCH` 宏改写成 `8`，"骗"编译器认为当前在编译 ARMv8 代码，从而解锁 Crypto 扩展 intrinsic（如 `vaesmcq_u8`）。源码里专门有一段注释列出了 clang 3.8.1 到 16 各版本对这个判断条件的不同要求，说明作者自己也在持续跟踪各版本 clang 在这里的行为差异；NDK r23c 自带的 clang 12.0.9 恰好落在这个易碎区间——当函数被要求生成 ARMv8 Crypto 指令、但编译的基准目标（`--target=armv7a-linux-androideabi21`）仍是 ARMv7 时，老版本 LLVM 的 ARM 后端在"选指令"阶段直接崩溃，这是 LLVM/NDK 自身的已知缺陷类别（官方建议此类问题去 android-ndk/ndk 提 issue），并非可以通过修正 7-Zip 源码用法来避免的用户代码错误。

**修复方案**：在 `build_android.ps1` 里新增了一个只对 `armeabi_v7a` ABI 生效的额外编译参数 `-U__ARM_FP`。上述所有触发这个"技巧"的文件都统一以标准预定义宏 `__ARM_FP`（表示目标 FPU 能力）作为总开关，用 `-U__ARM_FP` 让编译器在预处理阶段看不到这个宏，会让这些文件在 armeabi_v7a 上一致地退回到纯 C 软件实现（`Aes.c` 和 `AesOpt.c` 用的是完全相同的判断逻辑，因此两边会同步禁用，不会出现"一个文件里定义了 `_HW` 后缀符号、另一个文件却引用不到"这种链接期不一致）。这是用少量 armeabi-v7a 上的 AES/SHA/CRC 软件回退性能，换取对更多 NDK 版本的兼容性——armeabi-v7a 本身面向的就是低端/老旧 32 位设备，可接受；arm64/x86_64/x86 三个 ABI 不受此问题影响，未做任何改动。修复后同样用模拟编译器验证了该参数只出现在 armeabi_v7a 一个 ABI 的编译命令里，其余 3 个 ABI 的命令行不受影响。建议用户更新脚本后重新跑一次 `-Abi armeabi_v7a` 确认能编译通过（arm64 已经过真实 NDK r23c 验证，无需重跑）。

### 8.6 真机验证发现的严重 bug 及修复：`DllNotFoundException`（已修复）

8.4 节验证的是"APK 能否成功构建、签名、内含正确 ABI 的原生库"，但**不等于**原生库在 Android
运行时上真的能被加载——用户后续在真实 Android 设备上安装 Debug APK、点击「运行自检」，
**11 个用例全部失败**，抛出 `System.DllNotFoundException: 7zlite`，堆栈定位到第一次尝试加载
原生库的位置（`NativeMethods.CreateObject`）。这与 8.5 节记录的"Windows host 跑控制台测试报
`DllNotFoundException`"是完全不同、更严重的问题——那个是预期行为（本项目从未构建 Windows 原生库），
这个是 **Android 真机上加载 `lib7zlite.so` 本身失败**，说明此前交付的 4 个 `.so` 文件本身有缺陷。

**根因（已用 NDK r27c 重新编译 + `readelf`/`llvm-nm` 实测验证）**：`build_android.sh`/
`build_android.ps1` 的链接命令都**没有加 `-static-libstdc++`**（也没有链接 `libc++_shared.so`），
导致产出的 `.so` 里 `__gxx_personality_v0`、`_ZTIi`、`_ZTIPKc`（RTTI/异常处理相关的 libc++
运行时符号）全部是**未解析、且重定位类型为 `R_AARCH64_GLOB_DAT`/`R_AARCH64_ABS64`（即时/eager
绑定）**的悬空符号。Android 的 Bionic 动态链接器对这类"数据类重定位"必须在 `dlopen()` 时**立即**
解析完，解析不到就直接返回 NULL（.NET 层表现成 `DllNotFoundException`，且不会给出具体原因）——
这与普通函数调用的懒绑定（`R_AARCH64_JUMP_SLOT`，只有真正调用到才会报错）完全不同。此 bug
**从本项目最早的 Linux r27c 构建开始就一直存在**，此前"符号检查干净"的结论是错的：两个脚本里
原本就有的未定义符号体检代码只是把结果打印出来，从未真正让构建失败，所以没能拦住这个问题。

排查过程中还发现了一个关联但独立的次要问题：Zip handler 代码（`ZipHandler.cpp`/
`ZipAddCommon.cpp`）为了支持 zip 格式理论上允许的全部方法，直接引用了
`NCompress::NXz::CEncoder`、`NCompress::NZstd::CDecoder`、`NCompress::NPpmdZip::CDecoder`/
`CEncoder` 这几个类（分别对应 zip 里几乎不会遇到的"xz 方法写入"、"zstd 方法读取"、
"ppmd 方法读写"，均明确不在本项目"仅 7z/zip/tar/tar.gz + 分卷 + 密码"的范围内）。这几个类
各自实现了一整套 COM 编解码虚接口，但对应的 `.cpp` 实现文件从未被纳入过 lite 构建（无论
Linux 还是 Windows 版本），导致它们的虚函数表（vtable）在链接产物里同样是未定义的 —— 而
C++ 对象的 vtable 指针写入同样是一次数据地址重定位，会触发和上面完全相同的"dlopen 即时崩溃"
风险。

**修复**：
1. 给两个构建脚本的链接命令都加上 `-static-libstdc++`（`build_android.sh` 的 `LIB2` 变量、
   `build_android.ps1` 的 `$LinkLibs` 数组），彻底消除对 `libc++_shared.so` 的运行时依赖；
   顺手给 `build_android.ps1` 补上了 `-z noexecstack`（真实 `makefile.gcc` 链接命令里有、
   之前 ps1 里缺失的一个次要保真度差异）。
2. 对 `NXz::CEncoder`/`NZstd::CDecoder`/`NPpmdZip::CDecoder`/`NPpmdZip::CEncoder` 这几个
   不支持的类，没有选择"补写全部虚方法凑一个假 vtable"（尝试过程中发现这样做本身也容易在
   细节上出错、且这几个类各自实现的接口方法数量不少，维护成本和出错面都偏高），而是更彻底地
   直接在 `ZipHandler.cpp`/`ZipAddCommon.cpp` 里注释掉这几个类的实例化分支，让它们改走
   已有的"未知方法 → 返回 `kUnsupportedMethod`/`E_NOTIMPL`"兜底路径，从根上避免这些类的任何
   符号被引用。新增了 `Format7zLite/MissingCoderStubs.cpp`（与已有的 `XzDecoderStub.cpp`
   同一先例风格），补齐另一个仍在使用、但同样被裁剪掉实现的类
   `NCrypto::NZipStrong::CDecoder`（对应极少见的 WinZip Strong Encryption 解密）缺失的
   构造函数与全部虚方法（`Init`/`Filter`/`CryptoSetPassword`）及其自有的 `ReadHeader`/
   `Init_and_CheckPassword` 方法，全部返回 `E_NOTIMPL`。
3. 两个构建脚本里原本"发现问题只打印、不拦截"的未定义符号体检，改成了**发现异常就直接让
   构建失败退出**，并新增了 `NEEDED` 依赖库检查（确认不再意外依赖 `libc++_shared.so`）。这是
   本次修复里同样重要的一环——避免同类问题将来再次悄悄溜过构建、只能靠真机测试才能发现。
   **补充：一次真实 Windows 运行反馈出的检测脚本本身的 bug（已修复）**：用户在自己的 Windows
   机器上用 NDK r23c 实际跑了修复后的脚本，构建本身成功（`NEEDED` 只有 `libc.so`/`libm.so`/
   `libdl.so`，符合预期），却在新加的未定义符号体检这一步被误判为失败——原因是最初这一步是按
   "符号名是否带 `@LIBC`/`@LIBM`/`@LIBDL` 版本后缀"来判断是否为正常 libc 符号，但不同 NDK
   版本/API level 自带的 `llvm-nm` 对这一点的处理并不一致（NDK r27c 的 Linux 版会加后缀，
   NDK r23c 的 Windows 版不会），导致一大批完全正常的 libc/pthread 符号（`malloc`、
   `pthread_mutex_lock` 等）在 r23c 上被误判成异常。修复方式是把判断依据从"版本后缀格式"
   改成"符号名字本身的写法"：只有 Itanium C++ 名字修饰（`_Z` 开头，涵盖虚表 `_ZTV*`、类型
   信息 `_ZTI*`/`_ZTS*`）、异常处理个性化例程 `__gxx_personality_v0`、栈展开 `_Unwind_*`，
   以及两个会被直接塞进虚表槽位当数据指针使用的 `__cxa_pure_virtual`/`__cxa_deleted_virtual`，
   才会被判定为"危险"——这几类是唯一真正会在 `dlopen()` 时触发致命 eager 数据重定位崩溃的
   符号类别；其余 `__cxa_*` 函数（如 `__cxa_atexit`，是 Bionic libc 自己实现的、按普通函数
   调用懒绑定）和所有其它 libc/libm/libdl/pthread 符号一律放行，不再关心版本后缀格式，
   在两个 NDK 版本上都验证过表现一致。

**验证**：用重新下载的 NDK r27c 对全部 4 个 ABI 重新跑通了修复后的 `build_android.sh`，
`llvm-nm -D --undefined-only` 的输出里，除了标准 `@LIBC` 符号和一个无害的弱符号
（`memfd_create`，Bionic 里用于探测可选内核特性是否存在的标准写法，未找到会安全地按“不可用”
处理，不影响 `dlopen`）外，不再有任何未定义符号；`readelf -d` 的 `NEEDED` 列表在 4 个 ABI 上
都只剩 `libc.so`/`libm.so`/`libdl.so`。`demo/SevenZipLite.MauiDemo/NativeLibs/` 下 4 个
`lib7zlite.so` 均已替换为修复后重新编译的版本（新的产物大小见 8.3 节表格）。

**仍然需要用户做的最后一步**：本沙箱环境无法安装到真实 Android 设备上运行，所以以上验证
停留在"静态符号/重定位层面确认不会再触发 dlopen 失败"，**强烈建议用户用本次修复后的代码
重新构建 Debug APK，装到之前报告过崩溃的同一台设备上，重新点击「运行自检」，确认恢复为
11/11 通过**，这样才算完整闭环。

### 8.7 第二个真机 bug 及修复：`PlatformNotSupportedException`（Mono 不支持 ComWrappers）

8.6 节的修复解决了原生库加载失败的问题，但用户在真机上重新测试后（DllNotFoundException
确认消失），**11 个用例又全部失败**，这次报错完全不同、发生在更深的一层：

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
  at System.Runtime.InteropServices.ComWrappers.GetOrCreateObjectForComInstance(...)
  at SevenZipLite.Native.ComFactory.GetRcw[IOutArchive](IntPtr unknownPtr)
  at SevenZipLite.SevenZipCompressor.CreateCore(...)
```

**根因（已通过官方渠道交叉确认，置信度高，非本项目代码 bug）**：`.NET for Android` 默认
使用 **Mono** 运行时，而 Mono **完全不支持** `System.Runtime.InteropServices.ComWrappers`——
这正是本项目 `SevenZipLite` 类库的互操作层（`Native/ComFactory.cs`）赖以工作的
Source-Generated COM 机制的运行时基础。证据：
- MS Learn 官方 API 文档对 `ComWrappers` 类标注 `[UnsupportedOSPlatform("android")]`
  （以及 `ios`/`tvos`/`browser`），完整实现另标注 `[SupportedOSPlatform("windows")]`。
- `dotnet/runtime` 官方 PR #111208（2025-01，评审原话）："System.Drawing depends on
  ComWrappers that are not supported on Mono."
- 本报告 8.2 节 host 端 Linux 控制台自检之所以 11/11 通过，是因为 `dotnet run` 在桌面/
  服务器场景下走的是 **CoreCLR**（完整支持 ComWrappers），跟 Android 默认使用的 Mono
  运行时不是同一回事——**host 端跑通只证明了"互操作层的 C# 代码逻辑本身没问题"，
  并不能代表 Android 真机（Mono 运行时）上也能跑通**，这是本项目在本报告前几版里
  一直存在、直到真机测试才暴露的一个假设盲区，本节做如实记录、不做淡化。

**修复**：.NET 10 起，.NET for Android 新增了一个**实验性**的 CoreCLR 运行时选项，用一行
MSBuild 属性即可让整个 App 切换到 CoreCLR 运行（微软官方文档 [.NET MAUI 10 What's New]
(https://github.com/dotnet/docs-maui/blob/main/docs/whats-new/dotnet-10.md) 原话：
"(Experimental) CoreCLR — Enables Android apps to run on the CoreCLR runtime
(instead of Mono).")：

```xml
<UseMonoRuntime Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">false</UseMonoRuntime>
```

已在 `demo/SevenZipLite.MauiDemo.csproj` 里加上此属性（连同官方模板同步建议的
`SupportedOSPlatformVersion` 从 21.0 调到 24.0）。CoreCLR 完整实现 `ComWrappers`，
**切换运行时不需要改动 `SevenZipLite` 类库、`ComFactory.cs`、原生库或任何一行互操作
代码**——这是选择"切运行时"而非"重写整个互操作层"（曾评估过的两个备选方案：新增
C++ 胶水函数改用扁平 C ABI + 普通 P/Invoke；或完全绕开 `ComWrappers`、在 C# 里手写
vtable 调用）的原因：官方支持、风险最低、改动面最小。

**⚠️ 必须如实告知的代价（微软官方原话，未删减未淡化）**：
> Expect that application size is currently larger than with Mono and that
> debugging and some runtime diagnostics are not fully functional yet...
> This is an experimental feature and not intended for production use.

也就是说，截至 .NET 10，Android 上的 CoreCLR 仍是官方明确标注的**实验性、不建议用于
生产环境**的功能——这是目前解决 ComWrappers 问题的唯一官方路径，但需要接受这个前提。
根据微软已公开的路线图，**.NET 11 会把 CoreCLR 转正为 Android/iOS/Mac Catalyst 的默认
运行时**（Mono 变为需要显式选择的兼容选项），届时此问题会自然消失、也不再是实验性
功能；本项目按既定要求固定使用 `net10.0`，因此现阶段只能先用这个实验性开关过渡，
建议在允许升级到 `net11.0` 后重新评估是否可以移除此属性。

**已完成真机闭环验证**：用户已重新构建 APK 并在同一台出现过上述报错的设备上重新点击
「运行自检」，结果从"11/11 全部失败"恢复为"11/11 全部通过"，确认 `UseMonoRuntime=false`
切 CoreCLR 这一修复方式在真实 Android 设备上生效。

**对本报告此前结论的修正声明**：本报告第 5.3/6.5 节此前论证"Source-Generated COM 在
Android 上可行"时，验证止步于"能否用 P/Invoke 调通 native 侧接口的静态代码层面"以及
"host 端 CoreCLR 环境跑通"，未预见到 Android 默认运行时（Mono）本身不支持 `ComWrappers`
这一机制性限制。结论修正为：**Source-Generated COM 路线在 Android 上可行，但前提是
显式切换到 .NET 10 的实验性 CoreCLR 运行时（`UseMonoRuntime=false`）；如果坚持使用
Android 的默认 Mono 运行时，则完全不可行**，这一前提在报告正文所有相关章节均应视为
新增的必要条件。

### 8.8 已知限制与后续建议

1. **8.7 节所述 CoreCLR 运行时切换已完成真机复核**：这是继 8.6 节之后本项目发现的
   第二个问题，根因分析与修复方案已通过官方文档/源码交叉确认，且用户已在真实设备上
   完成"重新构建 APK → 真机点击「运行自检」→ 11/11 通过"的闭环验证，不再是未决问题。
2. **Release/生产级 APK 的完整 4-ABI 构建**建议在内存充足的机器上进行（本报告 8.4 节已定位到具体的两个可绕过的构建参数问题）。
3. **交付时的清理约定**：`demo/` 下各项目的 `bin/`、`obj/` 目录，以及 `7zip-src/CPP/7zip/Bundles/Format7zLite/` 下的 `_o_android_*` 目录，都是编译过程中产生的中间/输出产物，交付前已清理，不包含在最终交付文件里；但 `demo/SevenZipLite.MauiDemo/NativeLibs/<abi>/lib7zlite.so` 是**刻意保留**的原生库二进制依赖（MAUI 项目打包 APK 时必需），不属于要清理的"编译产物"范畴——如需重新生成，运行 `build_android.sh`（Linux）或 `build_android.ps1`（Windows）即可，两个脚本现在都会**自动把产物拷贝改名进 `demo/SevenZipLite.MauiDemo/NativeLibs/<abi>/lib7zlite.so`**，不需要再手动拷贝（前提是 `demo/` 与 `7zip-src/` 保持同级目录结构）。
4. **`SevenZipLite.Tests`（host 控制台自检）只在 Linux 上开箱即用**：内置的 `lib7zlite.so` 是本项目为 Linux x86_64 编译的宿主库；在 Windows 上运行会报 `DllNotFoundException`，这是预期行为。该控制台项目定位始终是开发期间的辅助回归测试，不是面向 Windows 桌面的产品形态。**（后续更新：Windows 桌面场景已由独立新增的 `SevenZipLite.WpfDemo` 项目 + win-x64/win-x86 原生库覆盖，详见 8.9 节，本条限制不再适用于"本项目是否支持 Windows 桌面"这一问题，只说明 `SevenZipLite.Tests` 这一个控制台项目本身的定位没变。）**
5. **`demo/SevenZipLite.MauiDemo.csproj` 不应脱离解决方案单独还原**：它通过 `ProjectReference` 引用 `SevenZipLite.csproj`/`SevenZipLite.SelfTest.csproj`，如果在 Visual Studio 里只打开这一个项目、或命令行只对它单独 `dotnet restore`，NuGet 可能算不出完整依赖图而报 `NU1105`（"找不到 xxx.csproj 的项目信息"）。修复方式是新增了 `demo/SevenZipLite.sln`，请始终通过这个解决方案文件打开/还原/构建（`dotnet restore SevenZipLite.sln` / `dotnet build SevenZipLite.sln`，或 Visual Studio 里双击这个 .sln），不要单独操作 MauiDemo 的 .csproj。

### 8.9 后续更新：扩展到 win-x64 / win-x86 / linux-x64，新增 WPF 演示项目

在本报告主体完成之后，应用户要求在保证较小改动的前提下扩展了支持的 RID 范围与验证载体，
概要记录如下（详细过程与命令见 `demo/README.md`）：

- **新增 RID**：`win-x64`、`win-x86`、`linux-x64`（`linux-x64` 此前已隐式存在于
  `SevenZipLite.Tests`，本轮补齐为可复现的 `build_linux.sh` 脚本）。
- **构建工具链选择**：Windows 侧选用 **MinGW-w64**（而不是 MSVC/nmake），原因是
  `Format7zLite/Arc_gcc.mak` 与 `7zip_gcc.mak` 这两份 GNU make 构建文件本身就自带
  `IS_MINGW` 分支、跨 Linux/Android/Windows 目标共用同一份源码文件清单，选它可以把
  改动量压到最低，不需要额外维护一套 `.vcxproj`/`nmake` 工程。
- **踩到的新坑**：`IS_MINGW=1` 下首次链接会报 `undefined reference to vtable for
  NZstd::CDecoder` 等错误，是 8.6 节问题的"近亲"——同样是"故意不支持的编解码器类只有
  接口声明、没有完整实现"导致的虚表物化问题，但触发条件不同：8.6 节是 Android 动态链接器
  对懒绑定符号的强制立即解析，这次是给几个类补上析构函数后，C++ ABI 的"key function"
  规则要求本编译单元把这些类的完整虚表（含从未被真正实现的虚方法）都物化出来，Windows/MinGW
  的 `-static` 全静态链接对这类未解析符号零容忍，才第一次暴露；ELF `.so` 场景一直没触发过。
  修复方式与 8.6 节一致：给这几个类的全部虚方法都补上"永远不会被调用的死代码"桩实现。
- **新增验证载体**：`demo/SevenZipLite.WpfDemo`（`net10.0-windows`，WPF），专门覆盖
  win-x64/win-x86，与 `SevenZipLite.MauiDemo`（保持 Android-only 不变）分工明确；两者
  共享同一份 `SevenZipLite.SelfTest` 往返测试逻辑与同一套 `SevenZipLite` 互操作层代码，
  未引入任何新的技术栈或桥接方式，符合"仅 7-Zip 源码 + C#"的既定约束。
- **交付边界**：win-x64/win-x86/linux-x64（linux-x64 早前已交付过一份验证通过的产物）
  这几个新 RID 的**正式二进制不由本次沙箱产出**，仅交付经过自测验证的构建脚本
  （`build_windows.sh`/`build_windows.ps1`/`build_linux.sh`），由使用者在自己的
  Windows/Linux 环境运行脚本得到最终产物；沙箱内已完成脚本自身正确性的验证（交叉编译
  成功、`objdump` 核查导入表/导出表），但 `SevenZipLite.WpfDemo` 尚未在真实 Windows
  机器上做端到端运行验证，需要用户补上这最后一步。

---

## 九、主要信息来源

- 7-Zip 官网下载页与许可证：https://7-zip.org/download.html ，https://7-zip.org/license.txt
- 官方 GitHub 仓库：https://github.com/ip7z/7zip
- p7zip 历史仓库：https://github.com/freayd/p7zip ；p7zip 现状说明见 Linux 命令手册与各发行版文档
- 7-Zip-Zstd：https://github.com/mcmilk/7-Zip-Zstd
- SevenZipSharp 相关：
  - https://github.com/squid-box/SevenZipSharp （已归档）
  - https://www.nuget.org/packages/SharpSevenZip/
  - https://github.com/StevenBonePgh/SevenZipSharp
- SevenZipExtractor：https://github.com/adoconnection/SevenZipExtractor
- SevenZipSharper（跨平台新方案）：https://github.com/Dura-IT/sevenzipsharper （含 `ARCHITECTURE.md` 对 Source-Generated COM、PROPVARIANT/BSTR/wchar_t 跨平台细节的详细说明）
- SharpCompress：https://www.nuget.org/packages/sharpcompress/
- 7-Zip-JBinding / 7-Zip-JBinding-4Android：
  - https://github.com/omicronapps/7-Zip-JBinding-4Android
  - https://sourceforge.net/projects/sevenzipjbind/
- 社区 Android 交叉编译先例：
  - https://github.com/peijunbo/7zip-android
  - https://github.com/hzy3774/AndroidP7zip
  - Termux 官方 7zip 打包 PR：https://github.com/termux/termux-packages/pull/13019
- .NET COM Interop 平台限制说明：Stack Overflow / joelleach.net 相关文章（"COM Interop with .NET Core"系列）
- .NET for Android 原生库打包机制：Stack Overflow "Binding native Android (and iOS) libraries in MAUI"
- 7-Zip 官方 Bundles 目录结构与各变体说明：https://github.com/ip7z/7zip/tree/main/CPP/7zip/Bundles ；`bit7z` 项目 Wiki 对各 Bundle 产物的整理：https://github.com/rikyoz/bit7z/wiki/7z-DLLs
- Android NDK 交叉编译 7-Zip 官方源码先例：https://github.com/peijunbo/7zip-android
- 已验证的 .NET 8 Source-Generated COM 跨平台封装先例（Windows/macOS/Linux）：https://github.com/Dura-IT/sevenzipsharper/blob/main/ARCHITECTURE.md ；另一个用同样方式为 Linux 编译 `7z.so` 的项目：https://github.com/exocad/SevenZipSharpSimple
- 分卷（-v 开关）并非在 `7z.dll` 内实现、而是由调用方用 `COutMultiVolStream`/`CInMultiVolStream` 薄层实现，Igor Pavlov 本人的说明：https://sourceforge.net/p/sevenzip/discussion/45797/thread/fe944e47/ ，https://sourceforge.net/p/sevenzip/discussion/45798/thread/fac00633/
- `ComWrappers` 类在 Mono/Android 上不受支持的官方证据（8.7 节引用）：
  - MS Learn API 文档：https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.comwrappers （`UnsupportedOSPlatform("android"/"ios"/"tvos"/"browser")`）
  - `dotnet/runtime` PR #111208（"don't run drawing tests on Mono"）：https://github.com/dotnet/runtime/pull/111208
  - `dotnet/runtimelab` issue #306（NativeAOT COM interop 支持进度）：https://github.com/dotnet/runtimelab/issues/306
- .NET 10 for Android 实验性 CoreCLR 运行时（8.7 节修复方案依据）：
  - 官方 What's New 文档：https://github.com/dotnet/docs-maui/blob/main/docs/whats-new/dotnet-10.md
  - `dotnet/android` Build Properties 参考（`$(UseMonoRuntime)` 属性说明）：https://github.com/dotnet/android/blob/main/Documentation/docs-mobile/building-apps/build-properties.md
  - InfoQ 对 .NET 10 RC1 / .NET 11 MAUI 默认切换 CoreCLR 的报道：https://www.infoq.com/news/2025/09/net-maui-rc1/ ，https://windowsforum.com/threads/net-11-maui-switches-to-coreclr-by-default-what-it-means-for-android-ios.418542/
