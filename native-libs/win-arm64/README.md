# win-arm64 原生库存放位置（预留，尚未接入构建）

这个目录下已经放了一份从 7-zip.org 官方 `7z2603-arm64.exe` 安装包提取、改名而来的
`7zlite.dll`（ARM64，PE32+），获取方式与 `win-x64`/`win-x86` 完全一致（见那两个目录下的
`README.md`），只是把第二步下载的安装包换成官方 ARM64 版本。

**当前状态**：`SevenZipLite.WpfDemo.csproj` 的 `<Platforms>` 列表和条件 `ItemGroup` 目前只配置了
`x64`/`x86` 两个平台，还没有加入 `ARM64`——这个目录里的 `7zlite.dll` 属于**预留/前瞻性存放**，
现阶段不会被任何实际构建流程用到。如果之后要支持 Windows on ARM（原生 ARM64 运行，而不是通过
x64 模拟层），需要：
1. 在 `SevenZipLite.WpfDemo.csproj` 里把 `ARM64` 加入 `<Platforms>`；
2. 参照 x64/x86 现有的条件 `ItemGroup`，新增一段在 `Platform=='ARM64'` 时把这个目录下的
   `7zlite.dll` 复制到输出目录的规则；
3. 用真实的 Windows on ARM 设备（或 ARM64 虚拟机）验证 `dotnet run` 能正常启动、原生调用不报
   `BadImageFormatException`。

这几步目前均未执行，仅在此说明现状，避免误以为 ARM64 已经是一个可用的目标平台。
