# 第三方声明 / Third-Party Notices

## 中文

本仓库、以及由 `src/SevenZipLite` 打包生成的 `SevenZipLite.Native` NuGet 包，
均以 **GNU Lesser General Public License v2.1（或您选择的任何更新版本）**
（简称 LGPL-2.1-or-later，见 `LICENSE`）分发**我们自己编写的 C#/.NET 封装代码**
（`src/`、`tests/`、`samples/`、`scripts/`、构建/文档脚本）。

本仓库同时包含/分发**并非我们编写**的第三方成分（同样主要是 LGPL v2.1，但版权归属、
适用范围与我们自己的封装代码是分开、独立的）：

| 成分 | 位置 | 许可证 | 说明 |
|---|---|---|---|
| 7-Zip 官方源码快照 | `native/7zip-src/` | 主要是 **GNU LGPL v2.1**，少数文件是 BSD-3-Clause / BSD-2-Clause，RAR 解码部分附加 unRAR 限制条款 | 原样保留自 [`github.com/ip7z/7zip`](https://github.com/ip7z/7zip)，未做任何修改 |
| 编译/下载得到的原生二进制（`7zlite.dll` / `lib7zlite.so`） | `native-libs/`，以及打包进 `SevenZipLite.Native` NuGet 包的 `runtimes/*/native/` | 同上（LGPL v2.1 为主） | Windows 版直接是 7-zip.org 官方发布的 `7z.dll` 改名；Linux/Android 版由 `native/7zip-src` 未经修改的源码本机/交叉编译而成 |

完整许可证原文见 `licenses/` 目录：

- `licenses/LGPL-2.1.txt` —— GNU Lesser General Public License v2.1 全文（我们自己的封装
  代码与 7-Zip 原生库共同使用的许可证）
- `licenses/7-Zip-License.txt` —— 7-Zip 官方 `License.txt`，说明各文件分别适用的许可证
- `licenses/unRAR-License.txt` —— unRAR 解码代码附加的使用限制（本项目自己的 C# API
  不暴露 RAR 格式，但底层未裁剪的原生库本身编译进了 RAR 解码支持）

**合规提示（不构成法律意见）**：本项目对 7-Zip 原生库的使用方式是"进程内动态加载、
未做任何修改的独立共享库文件"（`P/Invoke` 调用未修改的 `.dll`/`.so`），这通常被认为是
LGPL v2.1 里限制最少的使用形式之一。我们自己的 C#/.NET 封装代码本身也采用 LGPL-2.1-or-later
授权（见 `LICENSE`），这意味着：

- 你可以自由使用、修改、以源码或二进制形式分发本库（含商业用途）；
- 如果你**修改**了本库的源码并分发修改后的版本，需要按 LGPL 条款公开你的修改；
- 如果你只是**引用/链接**本库（例如通过 NuGet 包引用，不修改其源码），LGPL-2.1 通常不会对
  你自己应用程序的许可证提出要求，但仍需要满足"允许用户替换/重新链接本库"等 LGPL 条款
  （典型做法：动态链接，不要把本库静态编译进单文件可执行程序而不提供重新链接手段）。

如果你二次分发本项目（尤其是把原生库静态链接进你自己的可执行文件、或者修改了
`native/7zip-src` 里的源码、或修改了本仓库的 C# 封装代码），请自行确认你的分发方式满足
LGPL v2.1（以及适用时的 unRAR 限制条款）的全部要求，必要时咨询专业法律意见。

7-Zip 原生库部分版权归 Igor Pavlov 及 7-Zip 项目贡献者所有；本仓库 C#/.NET 封装代码部分
版权归 SevenZipLite Contributors 所有。

> 另见根目录 [`DISCLAIMER.md`](./DISCLAIMER.md)：本项目的 C#/.NET 封装代码由 AI 辅助生成，
> 使用前请自行审阅。

## English

This repository, and the `SevenZipLite.Native` NuGet package produced from
`src/SevenZipLite`, distribute **our own C#/.NET wrapper code**
(`src/`, `tests/`, `samples/`, `scripts/`, build/documentation tooling) under
the **GNU Lesser General Public License v2.1 (or, at your option, any later
version)** — LGPL-2.1-or-later, see `LICENSE`.

This repository additionally contains/redistributes third-party components
that we did **not** write (also predominantly LGPL v2.1, but with separate,
independent copyright ownership from our own wrapper code):

| Component | Location | License | Notes |
|---|---|---|---|
| 7-Zip official source snapshot | `native/7zip-src/` | Mostly **GNU LGPL v2.1**, a few files BSD-3-Clause / BSD-2-Clause, RAR decoding carries an additional unRAR restriction | Vendored verbatim, unmodified, from [`github.com/ip7z/7zip`](https://github.com/ip7z/7zip) |
| Compiled/downloaded native binaries (`7zlite.dll` / `lib7zlite.so`) | `native-libs/`, and the `runtimes/*/native/` assets bundled in the `SevenZipLite.Native` NuGet package | Same as above (predominantly LGPL v2.1) | The Windows binaries are the official `7z.dll` from 7-zip.org, simply renamed; the Linux/Android binaries are built (unmodified) from `native/7zip-src` |

Full license texts live under `licenses/`:

- `licenses/LGPL-2.1.txt` — full text of the GNU Lesser General Public License v2.1
  (used by both our own wrapper code and the 7-Zip native library)
- `licenses/7-Zip-License.txt` — 7-Zip's own `License.txt`, explaining which license
  applies to which file
- `licenses/unRAR-License.txt` — the additional restriction that applies to the
  unRAR-derived decoding code (this project's own C# API does not expose the RAR
  format, but the unmodified native library it links against was compiled with
  RAR decoding support built in)

**Compliance note (not legal advice)**: this project uses the 7-Zip native
library by dynamically loading an unmodified shared library file at runtime
(P/Invoke against an unmodified `.dll`/`.so`), which is generally considered
one of the least restrictive usage patterns under LGPL v2.1. Our own C#/.NET
wrapper code is itself licensed under LGPL-2.1-or-later (see `LICENSE`), which
means:

- You're free to use, modify, and redistribute this library in source or
  binary form (including commercially);
- If you **modify** this library's source code and distribute the modified
  version, you must make your changes available under the same LGPL terms;
- If you merely **reference/link** this library (e.g. via the NuGet package,
  without modifying its source), LGPL-2.1 generally does not impose licensing
  requirements on your own application, but you still need to satisfy LGPL
  terms such as allowing users to replace/re-link the library (typical
  approach: dynamic linking; avoid statically compiling this library into a
  single-file executable without providing a re-linking mechanism).

If you redistribute this project further — especially if you statically link
the native library into your own executable, modify the vendored source under
`native/7zip-src`, or modify this repo's own C# wrapper code — please
independently verify that your distribution method satisfies all requirements
of LGPL v2.1 (and the unRAR restriction, where applicable), and consult
qualified legal counsel if in doubt.

Copyright for the 7-Zip native library portions belongs to Igor Pavlov and the
7-Zip project contributors; copyright for this repository's C#/.NET wrapper
code belongs to the SevenZipLite Contributors.

> See also [`DISCLAIMER.md`](./DISCLAIMER.md): this project's C#/.NET wrapper
> code was generated with AI assistance — please review it yourself before use.
