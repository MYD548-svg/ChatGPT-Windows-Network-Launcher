# 项目记忆全量知识库 (Project Memory) - ChatGPT 时区启动器

---

## 一、 项目背景与架构设计

### 1.1 项目定位
- **目标应用**：Windows 官方应用商店版 ChatGPT 客户端（AppX 包名：`OpenAI.Codex`）。
- **技术栈**：原生单文件 C# 源码 (`Program.cs`) + 原生 Windows Forms GUI + Windows 内置 `csc.exe` 编译器（.NET Framework 4.0/4.5+）。
- **设计哲学**：纯绿色单文件、免装 Python/.NET 8 庞大运行时、无控制台黑框、对系统全局环境零污染。

### 1.2 核心工作机制
1. **进程级时区隔离注入**：
   - 借助 `ProcessStartInfo.EnvironmentVariables["TZ"] = tzValue` 仅向 ChatGPT 进程注入环境变量。
   - ChatGPT 底层（Electron/Chromium/Node.js/V8）遵从 POSIX 规范的 `TZ` 变量调整内部 JavaScript 时间（`Intl.DateTimeFormat` / `Date()`）。
   - **完全不修改 Windows 全局系统时区**，系统时钟与其他应用完全不受影响。
2. **动态 AppX 包定位**：
   - 避免硬编码版本号。通过 PowerShell 命令 `(Get-AppxPackage -Name '*OpenAI.Codex*' | Select-Object -First 1).InstallLocation` 动态解析最新的 `app\ChatGPT.exe` 物理路径，适配后续一切升级。
3. **安全优雅退出防护**：
   - 启动前探测已有 `ChatGPT` 实例，优先发送 `CloseMainWindow()` 信号并等待 3 秒；若未退出则弹窗友好提醒用户保存并在托盘退出，**坚决不执行强行 Kill 进程**，避免丢失未保存的对话与草稿。
4. **配置持久化**：
   - 配置文件保存在用户独立应用数据目录：%LOCALAPPDATA%\ChatGPTTimeZoneLauncher\settings.json。

---

## 二、 新增功能与特性演进

### 2.1 智能代理节点感知与防风控比对模块
- **背景与痛点**：
  OpenAI 具备严密的地理位置与行为风控机制。若用户节点代理在**日本/新加坡**（UTC+9 / UTC+8），但启动器误选了**美东**（UTC-5），将产生“出口 IP 与客户端上报时区严重冲突（Timezone/IP Mismatch）”，极易招致账号风控。
- **技术实现细节**：
  1. **纯海外知名探测接口**：
     - 首选接口：`https://ipinfo.io/json`
     - 备选容灾：`https://api.ip.sb/geoip`
     - 这两个域名被各大主流代理工具（Clash、v2rayN、Sing-box、Surge）规则分流列表默认归为海外节点，能有效防止测出国内真实 IP。
  2. **绝对透明与纯净策略**：
     - **杜绝任何后台静默探测或数据上报**，仅在用户点击【🔍 检测当前节点】按钮时主动触发。
  3. **异步并发与网络协议保障**：
     - 采用 `ThreadPool.QueueUserWorkItem` + `BeginInvoke` 异步模型，彻底杜绝 UI 线程卡顿或假死。
     - 强制设置 5000ms 超时保护。
     - 显式启用 TLS 1.2 握手：`ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | ...`，解决 .NET 4.0/4.5 默认 TLS 1.0 导致的 HTTPS 握手失败。
  4. **时区比对与一键对齐**：
     - 内置涵盖北美、欧洲、亚太核心枢纽（New York, Los Angeles, Singapore, Tokyo, London 等）的 IANA-Windows 时区映射引擎。
     - 实时智能比对状态：
       - 🟢 **完全匹配**：当前选择与节点时区一致（安全无隐患）；
       - 🔴 **冲突预警**：当前选择与节点时区存在偏差（提示可能引发风控）；
     - 提供【⚡ 一键匹配该时区】按钮，点击后自动联动下拉框或 UTC 偏移并刷新全局预览。

---

## 三、 关键问题复盘与技术踩坑实录 (Critical Diagnostics)

### 3.1 Windows `cmd.exe` 批处理文件多字节编码与字节漂移灾难
- **错误现象**：
  运行 `build.bat` 时控制台爆发一系列报错：
  - `'一繊璇?ChatGPT' 不是内部或外部命令`
  - `'[閿欒] 鏈湪绯荤粡妫€娴嬪埌 C# 缂栬瘧鍣?csc.exe镄?'`
  - `'cs' 不是内部或外部命令`
  - `'ptimize+' 不是内部或外部命令`
- **底层根因分析**：
  1. **字符集长度失配**：Windows 中文系统的 CMD 默认代码页为 CP936 (GBK)，处理每个汉字为固定 **2 字节**；而源码/脚本文件在现代编辑器中均默认保存为 UTF-8，每个汉字占 **3 字节**。
  2. **行偏移指针错位（Byte Alignment Drift）**：`cmd.exe` 没有 UTF-8 批处理解析器，遇到 3 字节汉字时强行按双字节拼接，多出的奇数孤立字节直接导致后面的字符流发生位移。
  3. **吞噬关键词与换行符**：
     - 原命令 `/optimize+` 前方的斜杠与字母 `/o` 被上一行的残余字节拼成了乱码字，剩下的 `ptimize+` 被 CMD 误当成独立命令；
     - 换行符 CRLF 被吞，导致 `Program.cs` 被截断成命令 `'cs'`；
     - 乱码 `'一繊璇'` 则是 `title 编译...` 中的中文字符在 GBK 下的错误呈现。
  4. **`chcp 65001` 的副作用**：在批处理脚本内部执行 `chcp 65001` 切换编码，会导致 CMD 产生历史悠久的文件读取指针漂移 Bug，直接使 `if not exist` 错位成 `exist`。
- **永久解决方案（行业金标准）**：
  - **将 `.bat` 批处理脚本内的所有提示词、标题、日志全面重构为 100% 纯 ASCII（纯英文）字符！**
  - **原理**：ASCII 字符集（0~127）在 UTF-8、GBK、ANSI、ISO-8859-1 等任何编码下，其二进制字节**100% 单字节对齐且绝对相等**。纯 ASCII 批处理脚本在全世界任何语言、任何配置的 Windows 机器上，永远不可能发生编码错位或字符吞噬。

---

### 3.2 高 DPI（125% / 150%）屏幕下的 UI 比例失真与截断
- **错误现象**：
  1. 左侧选项标题 **“IANA 城市时区”** 被横向截断成 **“IANA 城市时”**；
  2. **“美东/美西/新加坡/东京”** 快捷胶囊按钮与上下方的 ComboBox 贴合，下方边缘漏出底层蓝色边框；
  3. 底部操作按钮（“保存设置”、“创建快捷方式”、“保存并启动”）下半部分被窗口底部边缘裁切。
- **底层根因分析**：
  1. **缺少系统级 DPI 感知**：程序未声明 DPI-Aware，Windows 会强制介入并执行基于字体的粗暴换算（`AutoScaleMode.Font`），导致原本精确的像素排版发生比例崩塌。
  2. **硬编码宽度不足**：左侧 Label 宽度硬编码为 `110px`。在 125%/150% 缩放下，6 个 9.5F 的中文字符渲染宽度达到 120~128px，超出的汉字直接被裁剪。
  3. **垂直行间距过紧**：行间距原本仅预留 36px~40px。高 DPI 下 ComboBox 自身高度增至 30px 左右，导致快捷胶囊与下拉框上下粘连重叠。
  4. **窗口客户区高度缺失**：直接设定 `Size` 而非 `ClientSize`，导致 Windows 标题栏和厚边框侵占了容器高度。
- **永久解决方案**：
  1. 在入口 `Main()` 中通过 P/Invoke 显式调用 `SetProcessDPIAware()`，并锁定表单 `this.AutoScaleMode = AutoScaleMode.None;`，彻底禁用 WinForms 内部混乱的自动缩放。
  2. 将标题栏宽度统一拓宽至 **`135px`**，右移对齐，留足安全冗余，“IANA 城市时区”完整呈现。
  3. 拉大各行垂直间距（增至 44px~48px），彻底隔离快捷胶囊与输入框。
  4. 采用 `ClientSize = new Size(680, 890)` 锁定纯客户区域，并在操作按钮下方设置 24px 缓冲区垫片，彻底消除按钮贴边切除。

---

### 3.3 C# 跨命名空间类名冲突 (CS0104)
- **错误现象**：
  `Program.cs(61,17): error CS0104: “Timer”是“System.Threading.Timer”和“System.Windows.Forms.Timer”之间的不明确的引用`
- **底层根因分析**：
  为实现异步探测引入了 `using System.Threading;`，而该命名空间中包含 `System.Threading.Timer`；原项目已有 `using System.Windows.Forms;`，其中包含 `System.Windows.Forms.Timer`。编译器无法推断 `private Timer previewTimer;` 具体指代哪一个。
- **永久解决方案**：
  将表单中所有涉及界面计时器的声明与实例化显式写为全限定类名：**`System.Windows.Forms.Timer`**。

---

### 3.4 进程文件独占锁问题
- **错误现象**：
  编译报错提示目标可执行文件 `ChatGPTTimeZoneLauncher.exe` 正在被另一个进程使用，拒绝访问。
- **底层根因分析**：
  Windows 对正在运行的 `.exe` 二进制文件实施严格的排他锁保护，`csc.exe` 的 `/out:` 指令无法覆盖正在运行中的程序。
- **标准操作流程**：
  在运行编译脚本前，必须确保任务栏或后台托盘中的所有启动器进程均已关闭退出。

---

### 3.5 代理公网出口 IP 探测遭遇 429 限流与 TUN 模式下 SSL 握手超时故障
- **错误现象**：
  用户开启 VPN 全局模式与虚拟网卡（TUN 模式）后，点击【检测当前节点】提示：`探测超时或未能连接海外接口，请确认系统代理是否开启`。
- **底层根因分析**：
  1. **公共代理 IP 限流 (HTTP 429)**：原程序首选 `ipinfo.io/json`。用户所使用的海外商业代理/VPN 节点 IP 被全网多人共用，极易耗尽免费接口的调用频次，导致直接返回 `429 Too Many Requests`，在 C# `HttpWebRequest` 中抛出 `WebException`。
  2. **TUN 模式下特定接口 SSL 握手异常**：备选的 `api.ip.sb/geoip` 在 TUN 虚拟网卡 Fake-IP/Socks 代理环境下，发生 SSL 握手阻滞或超时。
  3. **缺乏多源瀑布容灾**：仅依赖单一/老旧接口，任一受挫即导致感知失败。
- **永久解决方案**：
  - 升级为多通道高可用瀑布式感知引擎：
    1. 首选通道：`https://ipwhois.app/json/`（纯海外、全 HTTPS、秒级响应、完整返回 IP/国家/城市/IANA 时区/GMT 偏移量）
    2. 核心通道：`http://ip-api.com/json`（纯海外、极速免证书握手、对 TUN 虚拟网卡兼容性极佳）
    3. 备用容灾：保留 `ipinfo.io/json` 与 `api.ip.sb/geoip`。
  - 每次请求超时设为 3500ms，失败无缝切入下一通道。
  - 严格保持**用户点击主动触发**原则，绝无后台静默探测。

---

### 3.6 界面比例失衡、底部按钮裁切与预设标签滚动条伪影故障
- **错误现象**：
  1. 窗口高度过高，在常见 1080P（125% 或 150% 缩放）屏幕上，底部的【保存设置】、【创建桌面快捷方式】、【🚀 保存并启动 ChatGPT】被任务栏遮挡或窗口边缘裁切只露出一半。
  2. 预设标签（美东、美西、新加坡等）下方漏出一条浅蓝色的边框/小方块滑块。
  3. 优雅退出复选框右侧文案 `（防止丢失未保存内容）` 被裁切。
- **底层根因分析**：
  1. **固定绝对坐标定位缺陷**：所有控件均以绝对 `curY` 累加放置在同一个顶层 Panel 中，总高度硬编码达 890px。在笔记本或高 DPI 屏幕上，总高超过了系统实际屏幕工作区（WorkArea），导致最底部的操作栏直接掉出屏幕可视区。
  2. **FlowLayoutPanel 强制水平滚动条**：快捷胶囊按钮外层容器设置了固定宽度且 `WrapContents = false`，当 5 个药丸按钮总宽度稍超容器宽度时，Windows Forms 控件机制强行在容器底部生成了一个 17px 的 Windows 水平滚动条，但因容器高度只有 34px，被挤压裁切只露出了蓝色的滚动条滑块与箭头！
  3. **复选框单行文本超宽截断**：CheckBox 设置了固定高度 26px，长文本无法完整展开换行或完整展示。
- **永久解决方案**：
  1. **底部固定动作栏架构（DockStyle.Bottom）**：
     - 将底部状态栏（就绪指示灯）与三大操作按钮独立置入一个底部固定面板，采用 `Dock = DockStyle.Bottom`。
     - 无论窗体如何缩放、屏幕分辨率高低，操作按钮**100% 始终锚定在窗体底部可视区**，绝不可能被裁切。
  2. **窗口自适应与平滑滚动**：
     - 主卡片区域置入 `Dock = DockStyle.Fill` 的独立滚动面板，默认窗口尺寸精简至 `640 x 720`（最小尺寸 `600 x 680`），在大中小屏幕上均可优雅展示。
  3. **预设标签滚动条彻底消除**：
     - 设置 `flowPresets.AutoScroll = false;`，胶囊按钮采用 92x26 紧凑药丸规格，彻底杜绝出现蓝色横向滚动条伪影。
  4. **全宽自适应复选框与清晰排版**：
     - 复选框采用自适应宽度，文案完整呈现；自绘按钮与卡片全面启用 `TextRenderingHint.ClearTypeGridFit` 与抗锯齿，文字清晰锐利。

---

### 3.7 界面左右留白失衡、水平截断与 Windows 98 复古焦点虚线框彻底根除
- **错误现象**：
  1. 卡片全部靠左（X=0），右侧漏出 60px 大缝隙，左右极度失衡。
  2. 顶部提示条被截断为 `...亦始终`；时区药丸胶囊单行溢出，`东京` 被切半，`伦敦` 彻底消失；底部复选框 `丢失内容` 被切；主启动按钮右侧溢出窗口只露半边。
  3. 按钮点击时周围浮现 Windows 98 风格的黑色小点状虚线焦点框（Focus Cues）。
  4. 标题“代理节点与公网出口感知”前方的 Emoji 在无 Emoji 字体下渲染为乱码方块（tofu box）。
- **底层根因分析**：
  1. 卡片硬编码绝对坐标 `Point(0, curY)` 与固定宽度 574px，且未计算对称边距；
  2. 底部面板 `pnlBottom` 初始创建时默认宽为 200，内部控件赋予 `Anchor=Left|Right` 后在 Form Resize 时产生了 `-440px` 的负边距位移畸变，导致主按钮膨胀至 790px 并将文字居中偏出窗外；
  3. WinForms 默认 `Button` 在获得键盘/鼠标焦点时由系统底层强制绘制焦点虚线；
  4. 缺少中文字体 Emoji 回退支持。
- **永久解决方案**：
  1. **左右 20px 对称响应式网格**：窗口尺寸优化至 `660 x 730`，卡片统一采用 `padX = 20`、`Width = ClientSize.Width - 40`，自适应动态对齐，完全杜绝截断。
  2. **显式 Resize 布局替代脆弱多层 Anchor**：底部 `pnlBottom` 绑定 Resize 事件，确定性计算按钮宽度，启动主按钮永远保持精确对称。
  3. **去除复古焦点虚线框**：`ModernButton` 覆写 `ShowFocusCues { get { return false; } }`，且设置 `ControlStyles.Selectable = false`，UI 瞬间恢复 Windows 11 Fluent 纯粹质感。
  4. **时区预设胶囊动态选中高亮**：5 大时区药丸尺寸精调并在选中时触发天蓝色主题高亮与边框（Active Chip），并与模式/UTC 实时联动。
  5. **ChatGPT 本地官方独立安装路径优先探测**：先探测 `%LOCALAPPDATA%\Programs\ChatGPT\ChatGPT.exe`，实现毫秒级启动与免 PowerShell 依赖。

### 3.8 IANA 城市时区字体模糊、按钮黑边与胶囊滚动条杂色彻底根除
- **错误现象**：
  1. “IANA 城市时区”标签被截断为“IANA 城市时”；下拉框内部文字模糊看不清。
  2. 按钮左右下侧有明显的 1px 黑色直角边框与硬角。
  3. 时区胶囊下方漏出浅蓝色三角形碎片与水平阴影。
- **底层根因分析**：
  1. ComboBox 默认 `DropDown` 可编辑模式在打开或禁用时自动全选文本，在暗蓝底色上叠加灰字导致极低对比度模糊；且 `labelWidth=110px` 在高 DPI 下截断了“区”字。
  2. `ModernButton` 继承自 `Button`，Windows 原生 FlatStyle 默认绘制了 1px 直角黑色轮廓，自绘未覆盖外圈直角背景造成黑角暴露。
  3. 胶囊使用了 `FlowLayoutPanel`，在 125% DPI 下 5 个胶囊总宽略超容器，底层强行生成了水平滚动条；但因高度受限被腰斩，漏出滚动条滑块与箭头残影。
- **永久解决方案**：
  1. `labelWidth` 拓宽至 135px；ComboBox 统一设为 `DropDownList`；`Shown` 事件重置激活焦点，彻底杜绝蓝色高亮选区。
  2. `ModernButton` 开启 `UserPaint | SupportsTransparentBackColor`，在 `OnPaintBackground` 和 `OnPaint` 中优先使用 `Parent.BackColor` 覆盖擦平整个矩形外圈，彻底消除黑边。
  3. 废除 `FlowLayoutPanel`，换用轻量 `Panel` 结合绝对坐标精确排布胶囊（总宽 406px，留白 35px），物理级杜绝 Windows 滚动条生成。


### 3.9 测试渲染与实机 Windows 11 Fluent 视觉主题同构与图标方框根除
- **错误现象**：
  1. 离屏测试快照呈现为 Windows 2000 Classic 风格（灰底 3D 凸起黑三角 ComboBox 箭头、黑白方块复选框），而实机为 Windows 11 Fluent 样式（细 Chevron 折叠箭头、纯蓝圆角对勾复选框）。
  2. 实机点击【检测当前节点】后，按钮上的旋转图标变成了 `[⟳]`（外框内嵌符号）。
- **底层根因分析**：
  1. 测试脚本直接加载程序集创建窗体，跳过了 `Main()` 中的 `Application.EnableVisualStyles()`，导致系统公共控件降级为 Classic 样式；
  2. `btnDetect` 检测完成后使用了高位 Emoji `🔄` (U+1F504)，中文字体无原生矢量字形，触发 Windows 字体链接回退生成带方框字形。
- **永久解决方案**：
  1. 测试捕获管线在窗体创建前显式执行 `Application.EnableVisualStyles()` 与 `Application.SetCompatibleTextRenderingDefault(false)`，实现与实机 100% 现代视觉同构。
  2. `btnDetect.Text` 改用基本多语言平面标准无框旋转字符 `↻`（U+21BB），彻底消除方框乱码。
  3. `btnApplyDetectedTz` 拓展宽度至 148px、高度至 30px，与左侧比对标签在 `Y = 93` 完美对齐。

---

### 3.10 从时区启动器全面演进为“防风控启动器”架构升级
- **背景与痛点**：
  原时区启动器仅处理了客户端时区环境变量，但在国内网络环境下，Windows 商店版 ChatGPT / Codex 客户端极易遭遇网络直连超时、模型无法连接、持续处于 Reconnecting 状态；此外，UWP 应用如果直接由进程启动，偶发权限限制或无法继承某些网络配置。
- **底层架构升级**：
  1. **双模式网络代理注入**：
     - 深度融合 `codex-proxy-switcher-win` 优秀实践，引入【VPN 代理模式】与【原生直连模式】一键切换；
     - 自动向本次启动实例注入 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`、`NO_PROXY`（默认 `127.0.0.1:7897`），并在界面提供 7897 (Clash)、7890 (v2ray)、10808 (Xray)、1080 (SS) 快捷胶囊与端口监听健康检查；
  2. **UWP 原生 COM 激活与瞬态环境变量广播**：
     - 引入 `IApplicationActivationManager` COM 接口，精准定位 `OpenAI.Codex_*!App` 安全拉起客户端；
     - 采用 `TemporaryLaunchEnvironment`：在启动前一刻写入用户级环境变量并通过 `SendMessageTimeout(WM_SETTINGCHANGE)` 广播，唤起完毕后在毫秒级时间内全部还原并再次广播，实现**启动期间短暂生效、全局环境零污染**；
  3. **代理感知型出口节点与时区智能比对**：
     - 在 VPN 模式下探测海外出口节点时，强制请求绑定 `new WebProxy("127.0.0.1", port)`，确保 100% 测出真实代理出口时区，彻底避免由于本地直连误报时差而触发 OpenAI 封控；
  4. **工程模块化拆分与编译防护**：
     - 将庞大的单文件逻辑拆分为 `Program.cs`（主窗口及业务流）、`LauncherCore.cs`（COM 激活、广播、配置迁移与网络检测底层）、`UIControls.cs`（圆角卡片与现代按钮控件），彻底杜绝单次代码输出过大导致的大模型流中断；
     - `build.bat` 升级为编译多文件源码并直接生成 `ChatGPTAntiBanLauncher.exe`，依然维持纯原生免装运行库单文件形态。

### 3.11 节点探测遭遇 SSL/TLS 安全通道创建失败与容灾穿透根除
- **错误现象**：
  用户在界面点击【检测出口节点】时，比对状态显示红色报警：`比对: 探测失败: 请求被中止: 未能创建 SSL/TLS 安全通道。`，出口 IP 探测失败。
- **底层根因分析**：
  1. **.NET 4.0 历史协议默认值问题**：Windows 系统内置 .NET Framework 4.0 的 `ServicePointManager.SecurityProtocol` 默认仅开启 SSL 3.0 / TLS 1.0。现代海外权威接口（如 `ipwhois.app`）均已强行要求 TLS 1.2 / TLS 1.3，握手被直接拒绝。在代码模块化拆分重构时遗漏了原有的协议开启语句。
  2. **局部异常穿透导致容灾跳过**：`FetchUrl` 没有内置 try-catch，首选 HTTPS 接口报错时异常直接穿透至外层，导致后续免 TLS 的纯 HTTP 接口（`http://ip-api.com`）未被执行。
- **永久解决方案**：
  1. **多重 TLS 协议与宽松证书容灾保障**：
     - 在程序启动及每次请求前显式注入 `ServicePointManager.SecurityProtocol = (SecurityProtocolType)12288 | (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;` 并提供 TLS 1.2 兜底；
     - 注入 `ServerCertificateValidationCallback = delegate { return true; };` 消除代理软件 MITM 证书对 GeoIP 探测的干扰；
  2. **FetchUrl 单元自愈与 4 级瀑布流容灾**：
     - `FetchUrl` 内部独立捕获任何单次网络/证书/429异常并优雅返回 `null`；
     - 依次尝试 `ipwhois.app` -> `ip-api.com` (免证书纯 HTTP) -> `ipinfo.io` -> `api.ip.sb`，杜绝任何穿透崩溃。

### 3.12 真实应用商店版客户端端到端实测验收与代理/时区采用机制实测定论 (L4 实测闭环)
- **背景与痛点**：
  此前项目所有测试均停留在“组件级/隔离沙箱”阶段，Windows 商店版（`OpenAI.Codex`）在真实启动后，沙箱内核是否会采纳启动器注入的代理与时区环境变量一直缺乏直接运行证据。
- **实机深入排查与实测证据**：
  1. **命令行无头拉起升级**：
     - 为 `Program.cs` 增加 `Main(string[] args)` 参数解析与 `EnsureConsoleOutput()`，支持 `ChatGPTAntiBanLauncher.exe --launch` 无头调度并向控制台输出结构化结果。
  2. **跨进程 64 位 PEB 环境块深度提取**：
     - 使用 Win32 `NtQueryInformationProcess` 和 `ReadProcessMemory` 直接读取运行中 `ChatGPT.exe` (PID 21608)、核心网络进程 `NetworkService` (PID 4044) 及后端 `codex.exe` (PID 3572) 的 PEB 环境块：
     - 证实三者均 **100% 完整包含 `TZ = Asia/Tokyo`、`HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`**，证明瞬态注册表写入与 COM 激活成功穿透至客户端。
  3. **出向网络连接抓包证实代理 100% 采用**：
     - 对客户端全进程树进行 TCP 连接监控，核心网络服务进程与本地后端向 `127.0.0.1:7897` 建立了 **18 条 Established 活跃连接**；
     - **直连公网 IP 连接数严格为 0**！证实全部对外网络流量严格由代理端口接管。
  4. **时区机制分层剖析**：
     - 后端与 Node.js 运行时完全遵循 POSIX 规范采纳了 `TZ`；
     - 前端 Chromium Renderer 页面因 Chromium 进程派生沙箱清洗策略（剔除环境变量）以及 Windows 版 Chromium V8 优先调用 Win32 本地系统时区 API，网页 DOM JavaScript 未采纳环境变量时区。
  5. **系统环境 100% 干净回滚**：
     - 唤起后毫秒级内注册表 5 项环境变量全部恢复为 `<NULL>`，WAL 事务日志销毁为 0，宿主系统环境零污染。

---

## 四、 项目文件清单与职责说明

| 文件路径 | 职责定位 | 关键修改点 |
| :--- | :--- | :--- |
| **`Program.cs`** | 主窗口与业务流控制 | 界面交互、VPN/直连模式切换、节点探测联动、新增 CLI `--launch` 无头启动调度 |
| **`LauncherCore.cs`** | 底层能力核心 | COM 激活接口、瞬态环境变量广播、桌面快捷方式生成、配置读写与自动兼容迁移 |
| **`UIControls.cs`** | 现代自绘控件 | `RoundedCard`、`ModernButton`、Windows 11 Fluent 视觉自绘支持 |
| **`build.bat`** | 原生一键构建脚本 | 全面改写为 **100% 纯 ASCII（英文）**，多源编译输出，优化 `/build-only` 免 pause |
| **`REAL_CLIENT_ACCEPTANCE_REPORT.md`** | 真实客户端实测验收报告 | 详尽记载真实 PID、PEB 提取数据、TCP 连接抓包证据与时区底层机制分析 |
| **`VALIDATION_REPORT_V0_5.md`** | 全量验证报告 | 收录 L1~L4 级全量测试断言与实机闭环结论 |
| **`PROJECT_MEMORY.md`** | 全量项目知识库与复盘记忆 | 完整记载系统架构、改动历史、重大踩坑记录与标准解法 |
| **`ChatGPTAntiBanLauncher.exe`** | 编译产物 | 绿色独立可执行文件（支持双击 GUI 与 CLI `--launch`） |







