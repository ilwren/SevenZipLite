# native-libs

这是本仓库所有原生库二进制的**唯一存放位置**（source of truth）。`src/SevenZipLite`
NuGet 打包脚本（`scripts/pack-nuget.*`）和各 Demo/测试项目都从这里引用同一份文件，
不再各自维护重复拷贝。

This is the **single source of truth** for every native binary in this repository.
The NuGet packaging script (`scripts/pack-nuget.*`) and every demo/test project reference
the files here directly instead of keeping their own duplicate copies.

| 目录 / Directory | RID | 来源 / Source | 说明 / Notes |
|---|---|---|---|
| `win-x64/` | `win-x64` | 7-zip.org 官方发布的 `7z.dll`，改名为 `7zlite.dll` | Official unmodified 7-Zip binary |
| `win-x86/` | `win-x86` | 同上 | Official unmodified 7-Zip binary |
| `win-arm64/` | `win-arm64` | 同上 | Official unmodified 7-Zip binary |
| `linux-x64/` | `linux-x64` | 用 `native/7zip-src/CPP/7zip/Bundles/Format7zF/build_linux.sh` 本机编译 | Built from unmodified official source |
| `android-arm64/` | `android-arm64` | Android NDK 交叉编译（镜像自 `samples/SevenZipLite.MauiDemo/NativeLibs/arm64-v8a/`） | Mirrored copy, see below |
| `android-arm/` | `android-arm` | 同上（镜像自 `armeabi-v7a/`） | Mirrored copy |
| `android-x64/` | `android-x64` | 同上（镜像自 `x86_64/`） | Mirrored copy |
| `android-x86/` | `android-x86` | 同上（镜像自 `x86/`） | Mirrored copy |

## 为什么 Android 的文件是"镜像"而不是唯一来源？

`SevenZipLite.MauiDemo.csproj` 用 `<AndroidNativeLibrary>` 引入原生库，.NET for Android
的构建工具**依据文件所在目录的字面名字**（`arm64-v8a`/`armeabi-v7a`/`x86_64`/`x86`，
Android ABI 命名，不是 RID 命名）来判断要把它打进 APK 里的哪个 `lib/<abi>/` 目录，
所以 MAUI 项目下 `NativeLibs/<abi>/` 这份目录结构不能去掉。为了不引入额外的技术风险
（跨语言的 MSBuild `<Link>`/元数据机制去驱动 Android 工具链的行为未经充分验证），
这里选择让 `native-libs/android-*/` 只作为 NuGet 打包脚本的输入来源，与 MAUI 项目下
那份保持内容一致但物理独立；更新 Android 原生库时，两处都需要同步替换。

## Why Android binaries are mirrored copies, not the sole source

`SevenZipLite.MauiDemo.csproj` uses `<AndroidNativeLibrary>`, and the .NET for Android
build tooling infers which `lib/<abi>/` folder to place a binary into **from the literal
name of its containing directory** (Android ABI names like `arm64-v8a`, not RID names).
So the `NativeLibs/<abi>/` layout inside the MAUI project can't be removed. To avoid
introducing risk from an unverified MSBuild `<Link>`/metadata trick to redirect the
Android tooling, the `native-libs/android-*/` copies here exist purely as the NuGet
packaging script's input and are kept in sync by hand — when updating the Android
binaries, update both locations.

## 已知缺口 / Known gaps

- `win-arm64` 目前**没有**对应的 .csproj RID 配置（`SevenZipLite.WpfDemo.csproj` 只声明了
  `x64`/`x86` 两个 Platform），这里放的二进制是预留，尚未接入。
  `win-arm64` has no matching `.csproj` RID wiring yet (only `x64`/`x86` platforms are
  declared); the binary here is a placeholder for future work.
- Android 交叉编译脚本（`build_android.sh`/`.ps1`）尚未提供，见仓库根 README "已知缺口"一节。
  The Android cross-compile script (`build_android.sh`/`.ps1`) has not been written yet;
  see the "Known gaps" section of the root README.
