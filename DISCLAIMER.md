# 免责声明 / Disclaimer

## 中文

本仓库（SevenZipLite）中的 C#/.NET 封装代码、构建脚本（`scripts/`）、CI/CD workflow
（`.github/workflows/`）、以及大部分文档（含本文件、`README*.md`、`docs/` 下的说明文档）是在
**AI（人工智能）辅助下生成/编写**的，并由人工进行了审阅、测试与调整，但**并未经过专业第三方
代码审计或正式安全评估**。

在使用、修改、二次分发本项目之前，请你自行：

- **审阅代码质量与正确性**：虽然仓库自带的自测/性能/压力测试（`tests/SevenZipLite.SelfTest`）
  已经过实际运行验证（见 `docs/development-notes.zh-CN.md` 中记录的排查过程），但测试覆盖面
  有限，不能保证在你的具体使用场景、边界条件或未来的 .NET/7-Zip 版本更新下没有缺陷。
- **审阅安全性**：本项目通过 P/Invoke 调用原生二进制（7-Zip 引擎），涉及非托管内存操作
  （PROPVARIANT 结构体读写、COM 互操作等）。如果你在处理不受信任的归档文件、或者在安全敏感
  场景下使用本项目，请自行评估潜在风险（例如恶意构造的归档文件可能触发的原生库层面的漏洞——
  这类风险来自 7-Zip 原生引擎本身，不是本封装层能够完全规避的）。
- **审阅许可证合规性**：本项目及其依赖的第三方组件采用多种许可证（见
  [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md)），请确认你的使用方式符合各自的许可证
  条款。

**不提供任何担保**。按 [`LICENSE`](./LICENSE)（GNU LGPL v2.1 或更新版本）的条款，本软件
"按现状提供"，不附带任何明示或暗示的担保，包括但不限于对适销性、特定用途适用性的担保。作者/
贡献者对因使用本软件（无论是否经过修改）而导致的任何损失或损害不承担责任。

如果你发现代码中的问题、bug 或安全隐患，欢迎通过 Issue/PR 反馈。

## English

The C#/.NET wrapper code, build scripts (`scripts/`), CI/CD workflow
(`.github/workflows/`), and most of the documentation in this repository (SevenZipLite) —
including this file, `README*.md`, and the documents under `docs/` — were **generated/written
with the assistance of AI (artificial intelligence)**, then reviewed, tested, and adjusted by a
human. They have **not** undergone a professional third-party code audit or a formal security
assessment.

Before using, modifying, or redistributing this project, please independently:

- **Review code quality and correctness**: the repository's own self-test/performance/stress
  test suite (`tests/SevenZipLite.SelfTest`) has been exercised and verified in practice (see
  the investigation notes in `docs/development-notes.zh-CN.md`), but test coverage is limited
  and cannot guarantee the absence of defects in your specific use case, edge cases, or future
  .NET/7-Zip version updates.
- **Review security**: this project uses P/Invoke to call a native binary (the 7-Zip engine),
  involving unmanaged memory operations (PROPVARIANT struct marshaling, COM interop, etc.). If
  you process untrusted archive files, or use this project in a security-sensitive context,
  assess the potential risk yourself (e.g. maliciously crafted archives could potentially
  trigger vulnerabilities at the native-engine level — such risk originates from the 7-Zip
  native engine itself and cannot be fully mitigated by this wrapper layer).
- **Review license compliance**: this project and the third-party components it depends on use
  multiple licenses (see [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md)); confirm your
  intended use complies with each applicable license's terms.

**No warranty is provided**. Per the terms of [`LICENSE`](./LICENSE) (GNU LGPL v2.1 or later),
this software is provided "as is", without warranty of any kind, express or implied, including
but not limited to the warranties of merchantability and fitness for a particular purpose. The
authors/contributors are not liable for any loss or damage arising from the use of this
software, modified or not.

If you find an issue, bug, or security concern in the code, contributions via Issues/PRs are
welcome.
