# 🤝 贡献指南 (Contributing Guide)

感谢你对 **ChatGPT Windows Network Launcher** 的关注与支持！我们欢迎一切形式的贡献，包括缺陷报告、功能建议、代码优化以及文档改进。

在提交 Pull Request (PR) 之前，请仔细阅读以下指南。

---

## 🛠️ 设计哲学与开发约束

本项目秉持以下核心工程原则，所有代码变更必须严格遵守：

1. **零外部重型依赖**：
   - 项目严禁引入 Node.js、Python、Go 或庞大的 .NET 8 / .NET Core 运行时；
   - 必须维持使用 Windows 系统自带的 C# 编译器（`csc.exe`，基于 .NET Framework 4.5+）即可完成单文件编译。
2. **零系统环境污染**：
   - 严禁通过修改 Windows 全局系统注册表时钟、安装驱动或开启全局 TAP/TUN 虚拟网卡的方式工作；
   - 任何环境变量改动必须纳入 `EnvironmentTransaction` 事务管理体系，支持毫秒级回滚与 Compare-and-Restore。
3. **非侵入式安全性**：
   - 坚决杜绝 DLL 注入、内存 Hook、修改 ChatGPT 客户端二进制文件等危险或合规存疑的行为；
   - 仅依赖官方支持的操作系统级进程启动与环境变量传递通道。
4. **批处理跨平台与编码兼容**：
   - `.bat` 批处理文件中的所有提示词、日志和结构必须使用 **100% 纯 ASCII 字符**，杜绝 GBK / UTF-8 跨字符集位移灾难；
   - 脚本中避免使用包含 `%~dp0` 的未防护圆括号块。

---

## 💻 本地开发与测试流程

### 1. 环境准备
- 操作系统：Windows 10 或 Windows 11 (x64)；
- 运行库：系统预装的 .NET Framework 4.5+（Windows 10/11 默认已预装，无需额外安装任何 SDK）。

### 2. 本地构建
克隆代码后，在项目根目录下执行：

```cmd
compile.bat
```
或使用构建并运行脚本：
```cmd
build.bat /build-only
```
编译成功后，将在当前目录生成独立的 `ChatGPTAntiBanLauncher.exe`。

### 3. 运行自动化测试套件 (必须全部通过)
在提交任何代码变更前，**必须**运行自动化测试套件验证防御逻辑：

```cmd
run_tests.bat
```

测试套件将自动编译并执行 3 组隔离测试：
- `IsolatedTests` (162 项防御断言)
- `TestSuite` (45 项组件断言)
- `TransactionTest` (5 项事务断言)

**提交 PR 的硬性前提是：总计 212 项断言全部 100% 通过（0 失败，0 告警）。**

---

## 📋 提交规范 (PR Guidelines)

1. **保持变更聚焦**：一个 PR 仅解决一个具体问题或引入一个独立特性；
2. **补充测试用例**：若新增了关键逻辑或修复了边界 Bug，请在 `build_test/IsolatedTests.cs` 中补充对应的防御性断言；
3. **遵循代码风格**：保持 C# 源码格式整洁、命名清晰、添加必要的底层原理注释；
4. **填写 PR 模板**：提交 Pull Request 时请如实勾选检查清单并附上变更说明。
