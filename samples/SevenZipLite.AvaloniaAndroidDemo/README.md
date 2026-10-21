# SevenZipLite.AvaloniaAndroidDemo —— Avalonia + Android 崩溃复现/诊断工具

这不是一个"展示最佳实践"的完整 Demo，而是一个**单一用途的诊断工具**：帮助在自己的
Avalonia + Android 项目里调用 SevenZipLite 时遇到崩溃/闪退的用户，在一个最小、干净、
没有其他业务代码干扰的环境里复现问题，看清楚**完整的异常信息**，而不是去翻一堆被系统
日志淹没的 `adb logcat` 输出。

背景见 `docs/investigation-report.zh-CN.md` §8.7 和 `docs/api/SevenZipLite.md`
「原生库加载与平台要求」一节的 ⚠️ 小节：.NET for Android 默认用 Mono 运行时，而 Mono
不支持 SevenZipLite 互操作层依赖的 `ComWrappers`，调用任何方法都会抛
`PlatformNotSupportedException`。

## 这个工具能做什么

打开 App 之后，从上到下按顺序点按钮：

0. **运行环境诊断** —— 判断当前进程到底在用 Mono 还是 CoreCLR 运行时。
1. **`SevenZipCompressor.CreateArchive`**（单文件、同步）—— 调用栈最短的最小复现。
2. **`SevenZipCompressor.CreateArchiveFromDirectory`**（同步）
3. **`SevenZipCompressor.CreateArchiveFromDirectoryAsync`**（异步）—— 复刻最常见报错
   堆栈里出现的调用路径。
4. **`SelfTestRunner.RunAll`** —— 复用仓库自带的自检用例，覆盖面更广（7z/zip/tar/tar.gz
   × 分卷 × 密码）。
5.（可选）**用你自己选的真实文件测试** —— 排查另一个和 ComWrappers 完全无关的可能原因：
   Android 现代存储模型下，文件选择器返回的常常是 `content://` 虚拟路径而不是真实文件系统
   路径，`SevenZipCompressor.Entry.DiskPath` 需要的是能被 `File.OpenRead` 直接打开的真实
   路径。这一步会明确告诉你选出来的路径是不是这种情况。

每一步都单独 try/catch，**异常不会导致 App 崩溃**，而是把 `ex.ToString()`（含完整堆栈）
直接打印在对应按钮下面的框里。另外还有一层"面包屑"机制（`DiagnosticLog`）：如果真的发生了
连 try/catch 都没接住的硬崩溃，上次运行的进度记录会保存在磁盘上，下次启动时自动显示在页面
最上方的"上次运行记录"里。

## 项目结构

- `SevenZipLite.AvaloniaAndroidDemo/` —— 共享代码（UI、诊断逻辑），`TargetFramework=net10.0`，
  直接 `ProjectReference` 到 `src/SevenZipLite` 和 `tests/SevenZipLite.SelfTest`。
  刻意不用 MVVM/`{Binding}`，所有控件用 `x:Name` + 代码后置直接调用，方便一眼看清每个按钮
  具体做了什么。
- `SevenZipLite.AvaloniaAndroidDemo.Android/` —— Android 头项目，`TargetFramework=
  net10.0-android`，**已经预置好 `UseMonoRuntime=false` + `SupportedOSPlatformVersion=24.0`
  这个 CoreCLR 切换**（就是用来验证/复现这个问题本身的核心开关），原生库打包方式跟
  `samples/SevenZipLite.MauiDemo` 完全一样。
- `SevenZipLite.AvaloniaAndroidDemo.Desktop/` —— 桌面头项目，跑在 Windows/Linux 上，用来
  在部署到 Android 真机/模拟器之前，先快速确认是不是 SevenZipLite 本身或 Avalonia 共享代码
  的问题（如果桌面上都跑不通，那就和 Android/CoreCLR 完全无关）。

## 构建与运行

### 先在桌面跑一遍（强烈建议，排除法的第一步）

```bash
dotnet run --project samples/SevenZipLite.AvaloniaAndroidDemo/SevenZipLite.AvaloniaAndroidDemo.Desktop
```

如果桌面上所有测试都能通过，说明问题确实只和 Android 运行时相关，可以放心进入下一步。

### 在 Android 上跑

需要先安装 Android SDK/NDK（`dotnet workload install android`），然后：

```bash
dotnet build samples/SevenZipLite.AvaloniaAndroidDemo/SevenZipLite.AvaloniaAndroidDemo.Android \
  -f net10.0-android -c Debug -t:Run
```

> **本仓库这次新增该项目时的验证范围说明**：受限于开发环境内存（没有 Android
> 模拟器/真机，且可用内存很小，装不下完整的 Android SDK + 跑一次完整的 dex 打包），
> 这个项目在原始开发环境里只验证到了「全部 C# 代码针对真实的 `net10.0-android` 参考
> 程序集 + `Avalonia.Android` 包编译零错误」这一步（即 `dotnet build` 能顺利跑到
> dexing 阶段），**没有验证到生成最终 APK、安装到真机/模拟器、实际点击按钮触发
> `PlatformNotSupportedException` 并在界面上看到异常信息这个完整闭环**。
> 如果你在自己机器上构建/运行时遇到任何编译错误或运行时异常（尤其是跟本项目代码本身
> 相关，而不是预期中要复现的那个 ComWrappers 异常），请把完整错误信息发回来，我会
> 根据实际情况调整代码。

## 如果测试结果和预期不一致

- **如果第 0 步显示已经是 CoreCLR，但第 1~3 步仍然崩溃**：说明 `UseMonoRuntime=false`
  这个开关本身没解决问题，可能是某个 Avalonia-Android 特有的兼容性问题，或者 7-Zip 原生库
  在 CoreCLR-for-Android 下有其他未知的兼容性问题——这种情况请把完整异常堆栈发回来，
  需要重新评估处理方案（当时讨论过的备选方案之一是手写 COM vtable 自己做互操作，绕开
  `ComWrappers`）。
- **如果第 1~4 步都通过，但第 5 步失败或 `TryGetLocalPath()` 返回 `null`**：问题大概率不是
  SevenZipLite 本身，而是你自己项目里把 `content://` 风格的虚拟路径直接传给了
  `Entry.DiskPath`，需要先把文件内容拷贝到真实临时文件。
