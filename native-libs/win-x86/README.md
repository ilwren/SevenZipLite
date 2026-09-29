# win-x86 原生库存放位置

这个目录下的 `7zlite.dll` 就是 **7-zip.org 官方发布的 `7z.dll`（原始文件，未做任何修改）改名而来**。
本项目 Windows 侧不再自己编译原生库（此前"自建裁剪版 Format7zLite" / "MSVC-nmake 全量版"两条构建
路径均已废弃），因为 7-Zip 官方本来就为 Windows 提供预编译好的、支持全部格式的 `7z.dll`，直接用
即可，风险更低、维护成本更低。

## 怎么拿到这个文件（任选一种）

**方式一：从本机已安装的 7-Zip 直接复制（最简单，推荐给桌面用户）**

如果你的 Windows 机器上装了 7-Zip（https://www.7-zip.org/ 官方安装包），直接把

```
C:\Program Files\7-Zip\7z.dll
```

复制到这个目录，改名为 `7zlite.dll` 即可。

**方式二：从官方安装包里提取（不需要先在机器上安装 7-Zip，适合 CI/脚本化场景）**

1. 从 https://www.7-zip.org/download.html 下载对应版本的 **32 位安装包**（不是 "Extra" 包），
   例如 `7z2603-x86.exe`（文件名里的版本号会随官方发新版而变化）。
2. 用 7-Zip 自身（或任何支持解包 NSIS/自解压安装包的工具，如 `7z e`）从这个 `.exe` 里提取出
   `7z.dll`：
   ```powershell
   7z e 7z2603-x86.exe 7z.dll
   ```
3. 把提取出的 `7z.dll` 改名为 `7zlite.dll`，放进这个目录。

## 为什么不能用官方 "Extra" 包（`7z2603-extra.7z`）里的 `7z.dll`

Extra 包里的 `7z.dll` 是给 32/64 位混合、面向命令行 `7za.exe`/`7zr.exe` 场景精简过的版本，
不保证包含全部格式的编解码器；本项目需要的是**安装包内自带的那一份**（`7zFM.exe` 同目录下的
`7z.dll`），才是功能最全、跟 GUI/资源管理器扩展用的是同一份的官方标准版本。

## 校验

放好文件后，可以用 `dotnet-dis`/`dumpbin`/`objdump` 或直接看属性详情，确认：
- 文件确实是 32 位（PE32，机器类型 x86）；
- 文件属性里的版本号与你下载的安装包版本一致；
- 导出表里包含 `CreateObject`、`GetHandlerProperty`、`GetHandlerProperty2`
  （`dumpbin /exports 7zlite.dll` 或 `objdump -p 7zlite.dll` 均可查看）。

`SevenZipLite.WpfDemo.csproj` 会在检测到这个文件存在时才把它复制进输出目录；没有这个文件时
项目仍可以正常打开/编译，只是运行时调用原生方法会抛 `DllNotFoundException`。

仓库本身**不提交**这个二进制文件（版权归 Igor Pavlov / 7-Zip 项目所有，属于第三方官方发布物，
不应该被我们的仓库分发；关于 LGPL-2.1 许可与署名要求，见仓库根目录的许可说明）。
