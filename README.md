# SevenZipLite

[![License: LGPL v2.1+](https://img.shields.io/badge/license-LGPL--2.1--or--later-blue.svg)](./LICENSE)
[![NuGet](https://img.shields.io/badge/nuget-SevenZipLite.Native-blue)](https://www.nuget.org/packages/SevenZipLite.Native)

> **中文** | [English](./README.en.md)

一个轻量的 .NET 封装，直接 P/Invoke 官方未裁剪的 7-Zip 原生引擎（`7z.dll`/`7z.so`），提供
7z / zip / tar / tar.gz 的读写能力（含分卷、密码），跨 Windows / Linux / Android 三个平台，
不依赖任何托管的重实现（如某些第三方 zip 库），压缩率和格式兼容性与官方 7-Zip 命令行完全一致。

> ⚠️ **AI 生成声明**：本仓库的 C#/.NET 封装代码、构建脚本、CI workflow 及文档主要由 AI
> 辅助生成。使用前请自行审阅代码质量与安全性，详见 [`DISCLAIMER.md`](./DISCLAIMER.md)。

## 目录

- [特性](#特性)
- [支持的平台与格式](#支持的平台与格式)
- [快速开始](#快速开始)
- [项目结构](#项目结构)
- [构建与运行](#构建与运行)
- [测试](#测试)
- [构建 NuGet 包](#构建-nuget-包)
- [发布（GitHub Actions）](#发布github-actions)
- [已知限制](#已知限制)
- [文档](#文档)
- [许可证](#许可证)
- [贡献](#贡献)

## 特性

- **直接调用官方 7-Zip 引擎**：不是重新实现的压缩算法，而是通过 COM 风格接口
  （`IInArchive`/`IOutArchive`）直接调用 7-Zip 官方源码编译出的原生库，压缩率、格式兼容性与
  官方 `7z`/`7za` 命令行完全一致。
- **四种归档格式**：7z、zip、tar、tar.gz 的创建与解压。
- **分卷归档**：创建/读取多卷 `.7z.001`/`.002`… 归档。
- **密码保护**：7z（AES-256 全归档加密）、zip（ZipCrypto/AES）密码保护。
- **多线程压缩**：可控制压缩线程数（`threadCount` 参数），充分利用多核 CPU。
- **zip 压缩方法可选**：`Store`（不压缩）或 `Deflate`。
- **三平台验证**：Windows（x64/x86/arm64）、Linux（x64）、Android（arm64-v8a/armeabi-v7a/
  x86_64/x86），分别提供 WPF、Linux 控制台自测、.NET MAUI 三个可运行的 Demo。
- **无额外托管依赖**：核心库只依赖 .NET BCL，不引入第三方压缩/COM 互操作包。
- **多目标框架**：核心库同时支持 **`net8.0`**（当前 LTS）与 **`net10.0`**，尚未升级到最新
  .NET 版本的项目也可以直接引用。
- **WPF Demo 跟随系统深浅色主题**：`samples/SevenZipLite.WpfDemo` 使用 .NET 9 起引入的 WPF
  Fluent 主题（`ThemeMode="System"`），随 Windows 系统的浅色/深色模式设置自动切换外观。

## 支持的平台与格式

| RID | 原生库 | 状态 |
|---|---|---|
| `win-x64` | `7zlite.dll` | ✅ 已验证 |
| `win-x86` | `7zlite.dll` | ✅ 已验证 |
| `win-arm64` | `7zlite.dll` | ✅ 已验证 |
| `linux-x64` | `lib7zlite.so` | ✅ 已验证 |
| `android-arm64`（`arm64-v8a`） | `lib7zlite.so` | ✅ 已验证（.NET MAUI） |
| `android-arm`（`armeabi-v7a`） | `lib7zlite.so` | ✅ 已验证（.NET MAUI） |
| `android-x64`（`x86_64`） | `lib7zlite.so` | ✅ 已验证（.NET MAUI 模拟器） |
| `android-x86`（`x86`） | `lib7zlite.so` | ✅ 已验证（.NET MAUI 模拟器） |

| 格式 | 读 | 写 | 分卷 | 密码 |
|---|---|---|---|---|
| 7z | ✅ | ✅ | ✅ | ✅（AES-256） |
| zip | ✅ | ✅ | ✅ | ✅（ZipCrypto/AES，取决于写入实现） |
| tar | ✅ | ✅ | ✅ | ❌（格式不支持） |
| tar.gz | ✅ | ✅ | ✅ | ❌（格式不支持） |

不支持 RAR/ISO/CAB 等格式——这是 API 层面的范围限制，详见 [`docs/api/SevenZipLite.md`](./docs/api/SevenZipLite.md)。

## 快速开始

```bash
dotnet add package SevenZipLite.Native
```

```csharp
using SevenZipLite;

// 压缩
var entries = new List<SevenZipCompressor.Entry>
{
    new() { DiskPath = @"C:\data\report.pdf", ArchivePath = "report.pdf" },
};
SevenZipCompressor.CreateArchive(entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);

// 解压
SevenZipArchive.ExtractAll(@"C:\out\backup.7z", ArchiveFormat.SevenZip, @"C:\restore");
```

完整 API（含分卷、密码、zip 方法选择、多线程、异常处理、原生库加载细节）见
[**API 文档**](./docs/api/SevenZipLite.md)（[English](./docs/api/SevenZipLite.en.md)）。

> ⚠️ 目前 NuGet 包尚未发布到 nuget.org（发布计划见下文"发布"一节），可先通过
> [`scripts/pack-nuget.sh`](./scripts/pack-nuget.sh) 本地打包后用本地源测试，或直接
> `ProjectReference` 本仓库的 `src/SevenZipLite/SevenZipLite.csproj`。

## 项目结构

```
SevenZipLite/
├── src/
│   └── SevenZipLite/              # 核心类库（打包为 NuGet 包 SevenZipLite.Native）
├── tests/
│   ├── SevenZipLite.SelfTest/     # 可复用的自测/性能/压力测试运行器（public API）
│   └── SevenZipLite.Tests/        # Linux 控制台自测程序（--stress 跑压力测试）
├── samples/
│   ├── SevenZipLite.WpfDemo/      # Windows WPF 可视化 Demo
│   └── SevenZipLite.MauiDemo/     # .NET MAUI（Android）Demo
├── native/
│   └── 7zip-src/                  # 官方未裁剪 7-Zip 源码（原样保留，用于本地编译原生库）
├── native-libs/                   # 各 RID 的编译产物（见其 README.md 的来源说明）
├── licenses/                      # 第三方许可证全文（LGPL-2.1 / 7-Zip / unRAR）
├── docs/
│   ├── api/                       # API 参考文档（中英双语）
│   ├── development-notes.zh-CN.md # 深度问题排查记录（多线程 bug、PROPVARIANT bug 等）
│   └── investigation-report.zh-CN.md # 早期技术选型调研报告
├── scripts/
│   ├── pack-nuget.sh / .ps1       # 一键构建 NuGet 包
├── .github/workflows/
│   └── release.yml                # 打 tag 后自动构建并发布 GitHub Release
├── Directory.Build.props          # 共享包元数据（作者/版本/仓库地址等）
├── LICENSE                        # LGPL-2.1-or-later（附原生库许可证说明）
├── THIRD-PARTY-NOTICES.md         # 第三方许可证归属说明（中英双语）
├── DISCLAIMER.md                  # AI 生成代码免责声明（中英双语）
└── SevenZipLite.sln
```

## 构建与运行

### 环境要求

- 核心库 `src/SevenZipLite` 多目标 **`net8.0` / `net10.0`**，安装其中任一版本对应的
  [.NET SDK](https://dotnet.microsoft.com/download) 即可构建它；仓库自带的测试/Demo 项目
  固定使用 **.NET SDK 10.0**，建议直接安装 10.0 SDK（内置对 8.0 目标框架的编译支持）。
- Windows 相关项目（`SevenZipLite.WpfDemo`）需要在 Windows 上用 `dotnet build`/`dotnet run`
  构建运行；在非 Windows 平台可以用 `-p:EnableWindowsTargeting=true` 做无头编译验证，但无法
  运行 UI。该项目使用了 .NET 9 引入的 WPF Fluent 主题（`ThemeMode`），因此其 `TargetFramework`
  固定为 `net10.0-windows`（>= `net9.0-windows` 即可，不受核心库 net8.0 目标的影响）。
- `SevenZipLite.MauiDemo` 需要安装 .NET MAUI workload（`dotnet workload install maui`）及
  Android SDK/NDK。

### 构建整个仓库

```bash
dotnet restore SevenZipLite.sln
dotnet build SevenZipLite.sln -c Release
```

### 运行 Linux 控制台自测（`SevenZipLite.Tests`）

```bash
dotnet run --project tests/SevenZipLite.Tests -c Release
# 跑多线程压力测试：
dotnet run --project tests/SevenZipLite.Tests -c Release -- --stress
```

### 运行 WPF Demo（仅 Windows）

```powershell
dotnet run --project samples\SevenZipLite.WpfDemo -c Release
```

如果原生库缺失，程序会给出明确的中文错误提示，指引你从 `native-libs/win-{x64,x86,arm64}/`
获取或从本地 7-Zip 安装目录复制 `7z.dll` 改名为 `7zlite.dll`。

### 运行 MAUI Demo（Android）

```bash
dotnet build samples/SevenZipLite.MauiDemo -f net10.0-android -c Debug -t:Run
```

## 测试

- **正确性测试**：`SevenZipLite.SelfTest.SelfTestRunner.RunAll`，覆盖 7z/zip/tar/tar.gz ×
  分卷 × 密码 × zip 方法的 13 组用例，三个 Demo（Linux 控制台、WPF、MAUI）都接入了这套测试并
  在界面/控制台展示结果。
- **性能测试**：`SevenZipLite.SelfTest.PerfTestRunner`，大文件（最高约 1GB）往返吞吐量测试。
- **多线程压力测试**：`SevenZipLite.SelfTest.MultiThreadStressRunner`，多轮次创建/解压/校验，
  用于验证多线程压缩路径的稳定性（历史上这里出现过一次已修复的竞态 bug，详见
  [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md)）。

三套测试的详细参数说明见 [API 文档 §SevenZipLite.SelfTest](./docs/api/SevenZipLite.md#sevenziplideselftest可选测试基准工具)。

## 构建 NuGet 包

```bash
# Linux / macOS
./scripts/pack-nuget.sh [版本号] [输出目录]
./scripts/pack-nuget.sh 1.0.0 ./artifacts

# Windows
.\scripts\pack-nuget.ps1 -Version 1.0.0 -OutputDir .\artifacts
```

脚本会：
1. 打印 8 个 RID 的原生库覆盖情况检查表（缺失的 RID 会跳过对应 `runtimes/` 条目，但不会中止打包）。
2. `dotnet restore` + `dotnet build -c Release` + `dotnet pack -c Release`。
3. 输出 `SevenZipLite.Native.<版本号>.nupkg` 与对应的 `.snupkg`（符号包）到指定目录（默认 `./artifacts`）。

不带参数运行则使用 `Directory.Build.props` 里的默认版本号 `0.1.0`。

## 发布（GitHub Actions）

[`.github/workflows/release.yml`](./.github/workflows/release.yml) 在推送 `v*` 格式的 tag
（如 `v1.0.0`）时自动触发，依次执行：

1. 在 Linux runner 上跑 `SevenZipLite.Tests` 自测（含 `--stress` 压力测试）作为发布前质量门槛。
2. 用 `scripts/pack-nuget.sh` 打出 `.nupkg`/`.snupkg`（版本号取自 tag）。
3. 在 Windows runner 上 `dotnet publish` WPF Demo（win-x64 与 win-x86），打包为 zip。
4. 在 Ubuntu runner 上安装 MAUI workload，构建 Android APK（未签名 Release 或 Debug 配置）。
5. 把上述所有产物（`.nupkg`、`.snupkg`、WPF zip、APK）作为附件创建一个 GitHub Release。
6. 可选的 nuget.org 推送步骤：**默认不执行**，只有在仓库 Secrets 里配置了 `NUGET_API_KEY`
   时才会启用（当前不需要，按用户要求保持关闭）。

触发发布：

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 已知限制

- 仅支持 7z/zip/tar/tar.gz 四种格式（RAR/ISO/CAB 等不支持）。
- 解压路径没有多线程加速（压缩有）。
- MAUI/Android 的原生库自动分发不走 NuGet 包内置的 `build`/`buildTransitive` 机制，需要手动
  参照 `samples/SevenZipLite.MauiDemo` 接入（详见 API 文档"原生库加载与平台要求"一节）。
- `native/7zip-src` 下暂时只有 `build_linux.sh`；`build_windows.*`/`build_android.*` 脚本尚不
  存在——Windows/Android 的原生库目前是手工获取/编译后直接放入 `native-libs/` 的，这是已知的
  待完善项，欢迎贡献对应脚本。
- 更详细的历史 bug 排查记录（含已修复的多线程竞态、PROPVARIANT 结构体大小问题、早期 Android
  `DllNotFoundException`/ComWrappers 问题）见 [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md)（仅中文）。

## 文档

| 文档 | 说明 |
|---|---|
| [`docs/api/SevenZipLite.md`](./docs/api/SevenZipLite.md) / [`.en.md`](./docs/api/SevenZipLite.en.md) | API 参考（中/英） |
| [`native-libs/README.md`](./native-libs/README.md) | 各 RID 原生库来源与 RID 映射表（中英双语） |
| [`docs/development-notes.zh-CN.md`](./docs/development-notes.zh-CN.md) | 深度问题排查记录（仅中文） |
| [`docs/investigation-report.zh-CN.md`](./docs/investigation-report.zh-CN.md) | 早期技术选型调研报告（仅中文） |
| [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md) | 第三方许可证归属说明（中英双语） |
| [`DISCLAIMER.md`](./DISCLAIMER.md) | AI 生成代码免责声明（中英双语） |
| [`CHANGELOG.md`](./CHANGELOG.md) | 更新日志（中英双语） |

## 许可证

封装代码（`src/`、`tests/`、`samples/`、`scripts/` 下的 C#/XAML/脚本代码）使用
**GNU Lesser General Public License v2.1（或您选择的任何更新版本，LGPL-2.1-or-later）**
许可证，见 [`LICENSE`](./LICENSE)。简单来说：

- 你可以自由使用/修改/分发本库（含商业用途）；
- 修改并分发本库源码时，需要按 LGPL 条款公开你的修改；
- 仅引用/链接本库（不修改其源码，例如通过 NuGet 包引用）通常不会影响你自己应用的许可证，
  但仍需满足"允许替换/重新链接本库"等 LGPL 条款（典型做法：动态链接，避免把本库静态编译进
  不提供重新链接手段的单文件可执行程序）。

仓库中捆绑/分发的 7-Zip 原生二进制（`native-libs/`）及其源码（`native/7zip-src/`）同样遵循
**LGPL-2.1**（部分文件为 BSD-3/BSD-2），RAR 解码相关代码另受 unRAR 许可证限制——这部分是
Igor Pavlov 及 7-Zip 项目贡献者独立拥有版权的第三方代码，与我们自己的封装代码版权分开。
完整第三方许可说明见 [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md) 和
[`licenses/`](./licenses/) 目录下的许可证原文。

> ⚠️ 本项目提供的许可证摘要不构成法律意见；如需在商业产品中分发本仓库的代码或原生二进制，
> 请自行审阅 LGPL-2.1 条款（尤其是动态链接与可替换性要求）并咨询法律顾问。

> ⚠️ **AI 生成声明**：本仓库代码/文档/脚本主要由 AI 辅助生成，详见
> [`DISCLAIMER.md`](./DISCLAIMER.md)。使用、修改、分发前请自行审阅代码质量、安全性与
> 许可证合规性。

## 贡献

欢迎 Issue 和 PR。提交前请确保：

- `dotnet build SevenZipLite.sln -c Release` 无警告无错误。
- `dotnet run --project tests/SevenZipLite.Tests -c Release -- --stress` 全部用例通过。
- 涉及原生库改动时，同步更新 `native-libs/README.md` 与受影响的 RID 说明。

> 📌 **注意**：`Directory.Build.props` 里的仓库地址目前是占位符
> `https://github.com/your-org/SevenZipLite`，请在 fork/发布前替换为你自己的实际仓库地址。
