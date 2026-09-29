# 开发笔记：疑难 bug 排查记录（仅中文，面向维护者）

> **English note**: this document is maintenance-only deep-dive material (root-cause
> investigations for two tricky native-interop bugs, plus historical Android device
> notes) and is kept in Chinese only, since it's dense, code-heavy, and primarily
> useful to contributors modifying the interop layer. If you're integrating this
> library as a consumer, you don't need to read this file — see the root `README.md`
> / `README.en.md` and `docs/api/`. If you *are* modifying `Callbacks/`, `Streams/`,
> or `Native/` under `src/SevenZipLite`, please read the relevant section below first
> so a previously-fixed bug (or a previously-*disproven* theory) doesn't get
> reintroduced.

本文档收录本项目排查时间最长、根因最容易被误诊的几个 bug 的完整排查记录，从
`demo/README.md`（项目重组前的旧文档）原样迁移过来，仅更新了路径引用。

## ⚠️ 多线程压缩下 `IOutArchive.UpdateItems` 偶发 `E_FAIL`（历史上出过一次误诊，本节是更正后的最终结论）

这是本项目排查时间最长、也是**唯一一个曾经被错误定性过一次**的 bug，**任何后续维护者改动
压缩相关回调代码（`UpdateCallback`/`ExtractCallback`/`ComInStream`/`ComOutStream`）前都应该
先读完这一节**，避免重新得出第一次排查时那个看似合理、实际错误的结论。

**现象**：`SevenZipCompressor.CreateArchive` 创建归档，线程数（`mt` 属性）大于 1 时，
`IOutArchive.UpdateItems` 会偶发性地失败，报 `E_FAIL (HRESULT=0x80004005)`；文件数越多、
线程数越高，复现概率越大。少量文件（比如 `SelfTestRunner` 的 13 个用例，每个用例只有 2~3
个几十 KB 的合成文件）时很难触发，容易被误判为"正常"。

### 第一次排查给出的结论（❌ 已证明是误诊，完整记录下来防止未来又得出同样的错误结论）

最初观察到的现象是"zip 默认方法(Deflate)下 `UpdateItems` 必现 `E_FAIL`，且失败发生在**任何一次
`GetStream`/`Read` 回调被调用之前**"。在 100% 官方未裁剪的 `Format7zF` 源码里插入 native 诊断
日志逐层定位（但插入的位置事后证明不够精确，并没有真正定位到失败发生的确切代码行），并且用
编译期宏 `Z7_ST`（强制单线程构建）复现验证——单线程构建下全部用例 100% 通过——据此得出结论：

> ".NET 的源生成 COM 互操作（`System.Runtime.InteropServices.Marshalling`）只提供裸 vtable
> 调用，没有传统基于 ole32 的 COM 那套套间(apartment)/跨线程编组(marshalling)机制，从
> native 自己创建、未依附 CLR 的工作线程回调进 CCW 从设计上就不安全，第一次调用就会失败。"

并据此在 C# 层用 `ISetProperties::SetProperties("mt", 1)` 强制单线程压缩来"修复"——这个变通
方案确实能让当时观察到的症状消失，所以一度被当作正式修复方案使用了一段时间。

**这个结论是错的**。真正的破绽在于：后来实现"大文件吞吐量测试"时发现单线程压缩速度低得不
合理，重新怀疑 `mt=1` 是否真的必要，用更精确的诊断手段（直接在 `UpdateCallback`/`ComInStream`
里加日志，而不是猜测 native 侧代码路径）重新验证 `mt>1` 时到底发生了什么，才发现整个"跨线程
回调不安全"的假设根本站不住脚——多线程回调本身从头到尾都工作正常，问题出在托管侧回调实现
自己的一个状态管理 bug 上。

### 重新排查后的真实根因

在本项目早期的 `UpdateCallback.GetStream` 实现里，用一个共享字段（例如 `_currentAdapter`）
缓存"当前"输入流，并在每次 `GetStream` 开头、以及 `SetOperationResult` 里都 `Dispose` 它——
这个写法隐含假设了 native 对该回调的调用是**严格单线程顺序**的：`GetStream(i)` 返回的流被
完整读完、压缩完成，才会轮到 `GetStream(i+1)`。

跟踪 7-Zip 官方源码 `CPP/7zip/Archive/Zip/ZipUpdate.cpp` 发现这个假设在多线程分发路径
（`Update2()`，`mtMode = numThreads > 1` 时启用）下并不成立：`SetOperationResult(kOK)` 在
`Update2()` 里是紧跟在 `GetStream(i)` 之后、由**同一个主调度线程**在真正开始压缩**之前**就
调用的——它只是用来做进度记账，根本不是"这个文件已经压缩完，可以关闭输入流了"的信号。真正
的读取发生在**稍后**，由某个工作线程异步执行的 `CThreadInfo::WaitAndCode()` 里，与主线程调用
`GetStream(i+1)`/`SetOperationResult` 完全是并发的。

于是实际发生的时序是：主线程为第 `i+1` 个文件调用 `GetStream`/`SetOperationResult` 时，会把
共享字段里指向第 `i` 个文件的 `FileStream` 提前 `Dispose` 掉——而这时工作线程可能才刚开始读、
甚至还没开始读第 `i` 个文件。读到一半（或还没开始读）的 `FileStream` 被突然关闭，抛出
`ObjectDisposedException`，被 `ComInStream.Read` 里的 catch-all 吞掉转成 `E_FAIL`，逐层
向上传播成 `UpdateItems` 的最终失败——这是一个普普通通的"共享可变状态被跨调用提前释放"竞态
bug，跟 .NET 互操作是否支持从 native 线程回调完全没有关系。强制 `mt=1` 之所以能让症状消失，
只是因为单线程路径（`Update2St`）下 `GetStream`/`SetOperationResult` 确实是严格顺序配对的，
恰好绕开了这个假设被打破的场景，并不是因为多线程回调本身不安全。

顺带验证过程中还确认了另一个容易想当然的假设也是错的：.NET 的 `ComWrappers`/
`StrategyBasedComWrappers` 在 native 把某个 CCW 接口指针 `Release()` 到引用计数 0 时，
**不会**自动调用被包装的托管对象的 `IDisposable.Dispose()`（这跟经典 COM 互操作、或者 RCW
方向的 `Marshal.ReleaseComObject` 不是一回事）。修复过程中一度尝试"干脆不主动 Dispose，
反正 native 释放引用计数时会自动清理"，结果导致 `UpdateCallback` 的输入流永久泄漏（只读流，
危害较小），以及 `ExtractCallback` 的输出流永久不关闭（`FileShare.None` 独占写，危害很大：
后续任何尝试打开/读取同一文件的代码，包括自带的校验逻辑，都会报"the process cannot access
the file because it is being used by another process"）——这条路也走不通，必须显式、主动地
管理生命周期。

### 正式修复（两个回调类因为 native 调用契约不同，需要两种不同的生命周期管理方式）

- **`UpdateCallback`**（压缩，多线程分发契约）：`GetStream` 不再使用共享字段，每次都创建一个
  全新的 `ComInStream`，登记进一个线程安全集合（`ConcurrentBag<ComInStream>`）；
  `SetOperationResult` 变成纯粹的空操作；所有登记过的流统一延迟到整个 `IOutArchive.UpdateItems`
  调用彻底返回、`UpdateCallback.Dispose()` 触发时才一次性关闭。因为这些都是只读输入流，在整个
  压缩过程期间都保持打开不会有正确性问题，代价只是"同时打开的文件描述符数量"略高于理论最优
  （等于本次归档的文件总数），这个代价对本项目"少量/中量文件"的封装库定位而言可以接受。
  详见 `src/SevenZipLite/Callbacks/UpdateCallback.cs` 类型注释。
- **`ExtractCallback`**（解压，单线程顺序契约）：保留原来的"共享字段 + `GetStream`/
  `SetOperationResult` 成对及时 `Dispose`"写法——这个写法在这里是**正确**的，因为 7-Zip 的
  `Extract` 路径在实际用到的场景下确实是单线程顺序处理每个条目的，`GetStream`/
  `SetOperationResult` 严格配对；而且及时关闭输出文件在这里是**必需**的（不然会导致上面
  提到的"文件被占用"错误）。**不要**把 `UpdateCallback` 的"延迟到最后统一释放"写法照搬到这里。

修复后 `mt` 恢复默认为 `Environment.ProcessorCount`（见 `SevenZipCompressor.CreateCore`，
可通过新增的 `CreateArchive(..., threadCount:)` 参数覆盖，传 `1` 等价于旧的强制单线程行为），
真正启用了 7-Zip 原生的多线程压缩加速。实测：沙箱环境（2 核，环境本身有一定噪声干扰）下用
7z/LZMA2 格式压缩 128MB 合成数据，单线程约 7.0 MB/s，多线程约 12~31 MB/s（多次测量有波动，
但方向和量级一致，明显快于单线程）；对于多文件 zip/tar 场景，多线程带来的收益取决于文件数量
和大小分布（`ZipUpdate.cpp::Update2` 的多线程是按文件粒度分发的，单个文件不会被拆给多个线程，
只有文件数 ≥ 线程数时才能真正吃满并行度）。

**验证方式**：除了 `SevenZipLite.SelfTest/SelfTestRunner.cs` 的 13 个正确性用例（覆盖格式 ×
分卷 × 密码矩阵，但每个用例文件很少、只跑一轮，不足以稳定复现这类竞态），新增了
`SevenZipLite.SelfTest/MultiThreadStressRunner.cs`：反复创建一个包含若干个中小文件的归档、
解压、逐文件 SHA-256 校验，用更多文件数 / 更多轮次给"多线程路径是否稳定"提供更强的信心。
这是一个独立于 13 项自检之外的可选诊断工具（跑起来比较慢，关注点也不同：这里关心的是"多轮/
高并发下会不会出现偶发竞态"，而不是"单次操作的结果是否正确"），Linux 控制台
（`dotnet run -c Release -- --stress`）、WPF、MAUI 三个 Demo 都提供了触发它的独立入口。

## ⚠️ win-x64 上批量 `SetProperties` 报 `E_INVALIDARG`（PROPVARIANT 数组步幅硬编码）

这是上面那个 bug 修复完之后，在**真实 Windows（WPF Demo + 官方 `7z.dll`）** 上才暴露出来的
第二个回归，Linux/Android 自测从未复现过，记录下来避免以后重犯。

**现象**：`SevenZipLite.SelfTest` 的 13 个用例里，只有同时设置 `mt` 和 `m`（zip 方法覆盖）
两个属性的 "zip with Store method override" / "zip with explicit Deflate method override" 在
真实 Windows 上失败，报 `ISetProperties.SetProperties(mt,m) 失败, HRESULT=0x80070057`
（`E_INVALIDARG`）；其余 11 个只设置单个属性（`mt=1`）的用例全部正常。Linux 控制台自测
（13/13）和 Android 真机自测完全没有这个问题。

**根因**：`SetUInt32Properties`（`src/SevenZipLite/SevenZipCompressor.cs`）把要传给 native 的
`PROPVARIANT[]` 数组按**固定 16 字节步幅**手工排布在一块 `AllocHGlobal` 缓冲区里，
`SetProperties` 在 native 侧用 `values + i * sizeof(PROPVARIANT)` 做指针步进读取每个元素——
这个"固定 16 字节"的假设只在 Linux/Android 上成立（用的是 7-Zip 自己在
`Common/MyWindows.h` 里重新定义的简化版 `tagPROPVARIANT`，union 成员最大 8 字节，
不管 32/64 位恒定是 8+8=16 字节），**在真实 Windows 上不成立**：Windows 用的是操作系统
`<oaidl.h>` 里的真实 `PROPVARIANT`，其 union 还包含 `CAC`/`CAUB` 这类"计数+指针"形式的
成员（如 `{ ULONG cElems; ... *pElems; }）`，在 64 位下为了让内部指针 8 字节对齐，
`sizeof(PROPVARIANT)` 在 **win-x64/win-arm64 上是 24 字节**，只有 **win-x86 上才是 16
字节**。只传 1 个属性时数组只有一个元素、偏移量 0 对两种步幅都成立，bug 完全不会暴露；
一旦同时传 2 个属性，用错误的 16 字节步幅在 win-x64 上会让 native 从错误的偏移（我们写在
偏移 16，native 按 `sizeof=24` 去读偏移 24）读出未初始化的垃圾数据，而且我们分配的缓冲区
本身也只有 `16*n` 字节，不够 native 按 `24*n` 字节去访问，构成越界读——这正是
`E_INVALIDARG` 只在"批量 2 个及以上属性"时出现、且只在 win-x64 上出现的完整成因。

**修复方式**：`SetUInt32Properties` 里的 PROPVARIANT 步幅改成运行时按平台/位数动态判定：

```csharp
int propVariantSize = OperatingSystem.IsWindows() && nint.Size == 8 ? 24 : 16;
```

即 Windows 64 位（win-x64/win-arm64）用 24 字节，其余情况（Windows 32 位、以及任何非
Windows 平台）用 16 字节；分配缓冲区大小同步用这个动态值，并在写入前把每个 PROPVARIANT
槽位整体清零（不止是 `PropVariant.WriteToNative` 实际写到的前 16 字节），避免 win-x64 下
每个槽位多出来的 8 字节残留未初始化数据。纯 C# marshaling 层修复，不需要重新编译任何一个
平台的原生库。

**验证情况**：Linux 自测在修复后重新跑过 13/13 全部通过（`OperatingSystem.IsWindows()`
为 false 时逻辑分支和步幅完全不变，行为未受影响）；win-x64 场景由用户在真实 Windows +
官方 `7z.dll` 上重新跑通 13/13 确认（本仓库沙箱环境没有真实 Windows，无法直接复现/验证
这一路径，只能保证 Linux 路径的回归测试）。

## Android 真机历史 bug 记录（均已修复，供追溯）

以下第一个 bug 是在**旧的自建裁剪版 `Format7zLite` bundle**上真机测试时发现并修复的。
现行架构改用官方未裁剪 `Format7zF` bundle 后，"DllNotFoundException"那一条涉及的具体
根因（`MissingCoderStubs.cpp`/`-static-libstdc++` 缺失）已经不适用（那份文件已随
`Format7zLite` 一起删除，官方 bundle 不存在"故意不实现的桩类"问题）；但**"未加
`-static-libstdc++` 导致 Bionic 动态链接器立即绑定失败"这一条通用经验**在编写新的
Android 交叉编译脚本时仍然要重新验证一遍。第二个"ComWrappers 在 Mono 上不受支持"的 bug
则与选用哪个 native bundle 完全无关，**在新架构下依然成立、依然需要**。

<details>
<summary>点击展开：`DllNotFoundException: 7zlite`（历史记录，绑定已删除的 Format7zLite 构建脚本）</summary>

真机测试中发现过一次**真正的原生库缺陷**：在真实 Android 设备上点击"内置自检"，全部用例失败，
抛出 `System.DllNotFoundException: 7zlite`，堆栈定位在第一次尝试加载原生库
（`NativeMethods.CreateObject`）。

**根因**：当时的 `build_android.sh`/`build_android.ps1`（已删除）链接 `lib7zlite.so` 时没有加
`-static-libstdc++`，导致产物里 `__gxx_personality_v0`、`_ZTIi` 等 C++ 异常/RTTI运行时符号
成为**立即绑定**（`R_AARCH64_GLOB_DAT`/`R_AARCH64_ABS64`）的未定义符号。Android 的 Bionic
动态链接器要求这类符号在 `dlopen()` 时必须立即解析完，解析不到就直接判定整个 `.so` 加载失败。

**修复**：给构建脚本的链接命令加上 `-static-libstdc++`。**编写新的 `build_android.sh` 时
务必重新确认这一点**（用 `readelf -d` 检查 `NEEDED` 只剩 `libc.so`/`libm.so`/`libdl.so`）。

</details>

### `PlatformNotSupportedException`（ComWrappers 在 Mono 上不受支持，已修复，现行架构下依然需要）

在真机上运行"内置自检"，原生库能正常加载，但会抛出：

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
  at System.Runtime.InteropServices.ComWrappers.GetOrCreateObjectForComInstance(...)
  at SevenZipLite.Native.ComFactory.GetRcw[IOutArchive](IntPtr unknownPtr)
  at SevenZipLite.SevenZipCompressor.CreateCore(...)
  at SevenZipLite.SevenZipCompressor.CreateArchive(...)
```

**根因**：`.NET for Android` 默认使用 **Mono** 运行时，而 Mono **完全不支持**
`System.Runtime.InteropServices.ComWrappers`（Source-Generated COM 机制运行时依赖的
核心类）——这是 .NET 运行时层面的能力缺口，不是本项目代码的 bug：
- 官方 API 文档对 `ComWrappers` 类标注 `[UnsupportedOSPlatform("android")]`（以及
  `ios`/`tvos`/`browser`），完整实现另标注 `[SupportedOSPlatform("windows")]`。
- `dotnet/runtime` 官方 PR #111208（2025-01，评审原话）："System.Drawing depends on
  ComWrappers that are not supported on Mono."
- 本项目 host 端 Linux 控制台自检之所以能全部通过，是因为桌面/服务器场景下的
  `dotnet run` 走的是 **CoreCLR**（完整支持 ComWrappers），跟 Android 默认的 Mono
  运行时不是同一回事——host 端跑通并不能代表 Android 真机上一定能跑通，这正是本
  项目此前的一个假设盲区。

**修复**：.NET 10 起，.NET for Android 新增了一个**实验性**的 CoreCLR 运行时选项，
只需在 `SevenZipLite.MauiDemo.csproj` 里给 Android 目标加一行属性即可切换：

```xml
<UseMonoRuntime Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">false</UseMonoRuntime>
```

（官方文档：[.NET MAUI 10 What's New](https://github.com/dotnet/docs-maui/blob/main/docs/whats-new/dotnet-10.md)，
原话："(Experimental) CoreCLR — Enables Android apps to run on the CoreCLR runtime
(instead of Mono)."）CoreCLR 完整实现了 `ComWrappers`，切换运行时后**不需要改动
`SevenZipLite` 类库、`ComFactory.cs`、原生库或任何一行互操作代码**。

配合这个开关，`SupportedOSPlatformVersion` 也从 21.0 调到了 24.0（微软 .NET 10
项目模板的新默认值）。

**⚠️ 必须告知的重要限制（微软官方原话，未删减未淡化）**：
> Expect that application size is currently larger than with Mono and that
> debugging and some runtime diagnostics are not fully functional yet...
> This is an experimental feature and not intended for production use.

截至 .NET 10，Android 上的 CoreCLR 仍被官方明确标注为**实验性、不建议用于生产环境**——
这是目前解决 ComWrappers 问题的唯一官方路径。根据微软已公开的路线图，**.NET 11 会把
CoreCLR 转正为 Android / iOS / Mac Catalyst 的默认运行时**（Mono 变成需要显式选择的兼容
选项），届时这个问题会自然消失、也不再是实验性功能。本项目按既定要求固定使用 `net10.0`，
因此现阶段只能先用这个实验性开关；如果后续允许升级到 `net11.0`，建议评估后移除这个属性。

**这项改动已在真机上重新验证通过**：重新构建 APK 并在同一台设备上重新点击"内置自检"后，
全部用例从"全部失败"恢复为"全部通过"。
