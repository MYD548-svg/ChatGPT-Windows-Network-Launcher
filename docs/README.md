# 📖 项目技术文档与开发档案索引 (Documentation Index)

欢迎查阅 **ChatGPT Windows Network Launcher (ChatGPT Windows 网络环境启动器)** 的内部技术演进档案与验收验证报告。

为了保持项目根目录的整洁性与开源规范，所有的历史复盘、架构设计备忘录与工程级验证报告均归档于本目录：

---

## 📂 目录结构导航

```text
docs/
├── reports/                            # 权威实测与验收证据报告
│   ├── VALIDATION_REPORT_V0_5.md       # v0.5 详细工程验证与三套测试套件全景报告 (212 项断言)
│   └── REAL_CLIENT_ACCEPTANCE_REPORT.md# L4 真实应用商店版客户端实机注入与网络捕获报告
├── dev/                                # 架构设计与关键技术踩坑记忆库
│   ├── PROJECT_MEMORY.md               # 核心设计哲学、高 DPI 适配、批处理编码避坑全量记录
│   └── PROJECT_SUMMARY_FOR_AUDIT.txt   # 代码审计与结构总览
└── archive/                            # 定向修复阶段的任务执行单 (Taskbooks)
    ├── FIX_TASKBOOK_V0_5_FINAL.md      # 终审定案任务单
    ├── FIX_TASKBOOK_V0_5_ROUND3.md     # 第三轮加固任务单
    ├── FIX_TASKBOOK_V0_5_ROUND2.md     # 第二轮防御任务单
    └── IMPLEMENTATION_TASKBOOK_V0_5.md # v0.5 初始落地任务单
```

---

## 📑 核心报告导读

### 1. [L4 级真实客户端实机端到端验收报告 (REAL_CLIENT_ACCEPTANCE_REPORT.md)](reports/REAL_CLIENT_ACCEPTANCE_REPORT.md)
- **验证对象**：微软官方应用商店版 `OpenAI.Codex` (Centennial 桌面桥接应用)
- **关键实测证据**：
  - 通过 Win32 API 动态读取 64 位运行中进程的 PEB 环境块，证实 `HTTP_PROXY` 与 `TZ` 成功注入主进程、网络服务进程 (`NetworkService`) 与后端服务进程 (`codex.exe`)；
  - 动态捕获网络连接，证实对外出向流量 **100% 经本地代理转发**，直连公网 IP 数量为 0；
  - 揭示并解释了 Chromium Renderer 页面沙箱环境变量清洗机制与网络分工。

### 2. [v0.5 工程验证与测试报告 (VALIDATION_REPORT_V0_5.md)](reports/VALIDATION_REPORT_V0_5.md)
- **验证范围**：静态语法编译 (L1)、组件与沙箱测试 (L2)、V8 引擎 POSIX 映射 (L3)；
- **测试结果**：包含 `IsolatedTests` (162 项)、`TestSuite` (45 项)、`TransactionTest` (5 项)，总计 **212 项自动化测试断言全部通过**；
- **安全保障**：跨会话 SID 互斥锁、WAL 事务回滚、Compare-and-Restore 防改值冲突、全路径精确进程核验。

### 3. [项目记忆全量知识库 (PROJECT_MEMORY.md)](dev/PROJECT_MEMORY.md)
- **踩坑实录**：
  - Windows `cmd.exe` 在多字节 UTF-8 下的字符漂移灾难与纯 ASCII 解决方案；
  - 125% / 150% 高 DPI 下 WinForms 界面截断与 `SetProcessDPIAware` 治理；
  - C# 跨命名空间类名冲突 (`System.Windows.Forms.Timer` vs `System.Threading.Timer`)；
  - 配置文件安全原子替换与目标占用防删除机制。
