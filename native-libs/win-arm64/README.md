# win-arm64 原生库存放位置（预留，尚未接入构建）

这个目录下已经放了一份从 7-zip.org 官方 `7z2603-arm64.exe` 安装包提取、改名而来的
`7zlite.dll`（ARM64，PE32+），获取方式与 `win-x64`/`win-x86` 完全一致（见那两个目录下的
`README.md`），只是把第二步下载的安装包换成官方 ARM64 版本。

**当前状态**：`SevenZipLite.AvaloniaDemo.csproj` 里按 RID（`win-x64`/`win-x86`/`linux-x64`）
挑选原生库的 `ItemGroup` 目前还没有加入 `win-arm64` 这一支——这个目录里的 `7zlite.dll` 属于
**预留/前瞻性存放**，现阶段不会被任何实际构建流程用到。如果之后要支持 Windows on ARM（原生
ARM64 运行，而不是通过 x64 模拟层），需要：
1. 在 `SevenZipLite.AvaloniaDemo.csproj` 里，把 `_AvaloniaDemoNativeRid` 的 OS 探测 fallback
   逻辑和按 RID 选择原生库的 `ItemGroup` 都加上 `win-arm64` 这一支；
2. 确认 Avalonia 本身（`Avalonia`/`Avalonia.Desktop` 等 NuGet 包）在 `win-arm64` RID 下有
   对应的 runtime 资源可用；
3. 用真实的 Windows on ARM 设备（或 ARM64 虚拟机）验证 `dotnet run`/`dotnet publish -r
   win-arm64 -p:PublishAot=true` 都能正常启动、原生调用不报 `BadImageFormatException`。

这几步目前均未执行，仅在此说明现状，避免误以为 ARM64 已经是一个可用的目标平台。

---

# win-arm64 native library location (reserved, not wired into the build yet)

A `7zlite.dll` (ARM64, PE32+) extracted and renamed from the official 7-zip.org
`7z2603-arm64.exe` installer already sits in this directory, obtained the exact same way
as `win-x64`/`win-x86` (see those directories' `README.md`) — just swap the installer
downloaded in step 2 for the official ARM64 build.

**Current status**: the RID-based (`win-x64`/`win-x86`/`linux-x64`) native-library
selection in `SevenZipLite.AvaloniaDemo.csproj` does not yet have a `win-arm64` leg — the
`7zlite.dll` in this directory is **reserved/forward-looking** and isn't used by any
actual build pipeline right now. To support Windows on ARM in the future (running
natively as ARM64, not through the x64 emulation layer), the following would be needed:
1. Add a `win-arm64` leg to both the `_AvaloniaDemoNativeRid` OS-detection fallback logic
   and the RID-based native-library `ItemGroup`s in `SevenZipLite.AvaloniaDemo.csproj`;
2. Confirm Avalonia itself (the `Avalonia`/`Avalonia.Desktop` etc. NuGet packages) ships
   matching runtime assets for the `win-arm64` RID;
3. Verify on a real Windows on ARM device (or an ARM64 VM) that both `dotnet run` and
   `dotnet publish -r win-arm64 -p:PublishAot=true` start up cleanly and native calls
   don't throw `BadImageFormatException`.

None of these steps have been done yet; this file only documents the current state so
ARM64 isn't mistaken for an already-supported target platform.
