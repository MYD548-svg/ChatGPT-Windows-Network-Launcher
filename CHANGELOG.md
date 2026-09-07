# 📝 更新日志 (Changelog)

本项目的所有显著变更均记录于此文件。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，并遵循 [语义化版本 2.0.0](https://semver.org/lang/zh-CN/)。

---

## [v0.5.0] - 2026-09-06

这是 **ChatGPT Windows Network Launcher** 的重要工程化里程碑版本，完成了从轻量启动脚本到具备企业级事务防护与端到端实机验证架构的蜕变。

### ✨ 新增特性 (Added)
- **双模式网络代理注入**：
  - 支持【本地代理模式】：精准注入 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`、`NO_PROXY` 环境变量；
  - 支持【显式直连模式】：彻底剥离继承自父级进程的陈旧代理变量，防止脏配置继承；
  - 内置常见代理端口一键预设（Clash 7897, v2ray 7890, Xray 10808, SS 1080）与 Socket 存活探测。
- **目标时区配置与 V8/Chromium POSIX 适配**：
  - 支持标准 IANA 城市时区及自定义 UTC 偏移；
  - 自动将 UTC 偏移转换为 POSIX 规范的 `Etc/GMT` 格式，规避 V8 引擎在 Windows 下解析非标准 `UTC-X` 的误判缺陷；
  - 引入**严格免夏令时 (Non-DST) 映射校验**（如 Darwin +9:30、Kolkata +5:30），明确拒绝带夏令时跳变且不可靠的偏移（如 Newfoundland -3:30、Chatham +12:45），严禁静默替换。
- **权威 HTTPS 公网出口感知与防风控比对**：
  - 全流程采用安全 HTTPS GeoIP 接口，剔除所有明文 HTTP 接口；
  - 显式启用 TLS 1.2+ 安全协商，实施严格的 TLS 证书链校验；
  - 区分证书异常、网络超时、代理不可达等状态码，客观比对出口地理位置与配置时区的一致性。
- **双客户端形态智能调度**：
  - 独立桌面版（Standalone）：支持 `ProcessStartInfo` 纯净子进程环境变量隔离唤起；
  - 微软应用商店版（Store AppX `OpenAI.Codex`）：通过 Shell COM 接口 (`IApplicationActivationManager`) 进行高兼容性瞬态唤起。
- **无头命令行调度能力**：
  - 支持 `ChatGPTAntiBanLauncher.exe --launch` 无头直接拉起，向控制台输出结构化启动与回滚状态。

### 🛡️ 安全与防护机制 (Security & Reliability)
- **严格变量白名单控制**：仅允许管理受信任的 5 个白名单环境变量，绝不触碰 `PATH`、`USER` 等关键系统变量。
- **环境预写式日志事务 (WAL) 与 Compare-and-Restore**：
  - 启动前自动创建磁盘 WAL 日志，以当前用户 SID 构建全局互斥锁（`Global\ChatGPTLauncher_EnvLock_<UserSID>`），杜绝并发写入冲突与降级风险；
  - 启动后实施 Compare-and-Restore：若检测到变量已被外部程序修改，严格保留外部值不覆盖，并在界面呈现琥珀色警告，避免代理凭据泄露；
  - 客户端拉起后瞬态毫秒级清理恢复，注册表 100% 零残留。
- **安全配置存储**：配置文件落盘采用安全原子替换（Safe Atomic Replace），在目标文件被独占占用时 100% 保留原配置，坚决不回退删除目标。
- **进程安全核验与防误退保护**：
  - 启动前核对目标进程全路径与目录边界，拒绝匹配相似前缀目录；
  - 优先发送 `CloseMainWindow()` 优雅退出信号，缺少用户显式授权时拒绝强制 Kill。

### 🧪 验证与自动化测试 (Verification)
- **端到端 L4 实机验证**：完成对官方应用商店版 `OpenAI.Codex` (Centennial Win32) 的端到端实机验证，PEB 环境块提取与网络连接审计证实出向流量 100% 经本地代理转发。
- **三套自动化隔离测试套件**：建立 `IsolatedTests` (162 项)、`TestSuite` (45 项)、`TransactionTest` (5 项)，总计 212 项断言全部 100% 通过。
