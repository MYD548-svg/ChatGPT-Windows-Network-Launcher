using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ChatGPTAntiBanLauncher
{
    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // Modern UI Palette
        private static readonly Color BgWindow = Color.FromArgb(248, 249, 250);
        private static readonly Color BgCard = Color.FromArgb(255, 255, 255);
        private static readonly Color BorderCard = Color.FromArgb(226, 232, 240);
        private static readonly Color AccentBlue = Color.FromArgb(15, 108, 189);
        private static readonly Color AccentHover = Color.FromArgb(29, 122, 203);
        private static readonly Color AccentPressed = Color.FromArgb(11, 92, 163);
        private static readonly Color TextPrimary = Color.FromArgb(30, 41, 59);
        private static readonly Color TextSecondary = Color.FromArgb(100, 116, 139);
        private static readonly Color InfoBarBg = Color.FromArgb(239, 246, 255);
        private static readonly Color InfoBarText = Color.FromArgb(29, 78, 216);

        // State & Settings
        private LauncherSettings currentSettings;
        private TargetClientInfo targetClient;

        // UI Controls - Network Proxy Card
        private RoundedCard cardProxy;
        private ModernButton btnModeVpn;
        private ModernButton btnModeDirect;
        private Panel pnlProxySettings;
        private TextBox txtProxyPort;
        private ModernButton btnAutoPort;
        private Label lblPortStatus;
        private System.Windows.Forms.Timer portCheckTimer;
        private bool isUpdatingPortInternally = false;

        // UI Controls - Node Detect Card
        private RoundedCard cardNodeDetect;
        private Label lblDetectIp;
        private Label lblDetectTz;
        private Label lblDetectCompare;
        private ModernButton btnDetect;
        private ModernButton btnApplyDetectedTz;
        private string lastDetectedIana = null;
        private TimeSpan? lastDetectedOffset = null;
        private bool hasDetectedTz = false;

        // UI Controls - TimeZone Settings
        private RoundedCard cardTz;
        private ComboBox comboMode;
        private ComboBox comboIana;
        private ComboBox comboUtc;
        private CheckBox chkGracefulClose;
        private Panel pnlPresets;
        private List<ModernButton> presetButtons = new List<ModernButton>();

        // UI Controls - Live Preview
        private RoundedCard cardPreview;
        private Label lblPreviewTzBadge;
        private Label lblPreviewProxyBadge;

        // UI Controls - Status and Actions
        private Label lblStatusDot;
        private Label lblStatusText;
        private ModernButton btnLaunch;
        private ModernButton btnSave;
        private ModernButton btnShortcut;
        private Label lblClientFoundStatus;

        public MainForm()
        {
            string loadErr;
            currentSettings = SettingsStore.Load(out loadErr);

            // Startup recovery for any dirty transaction from prior crash
            RecoveryResult recoveryRes = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();

            // Locate target client
            targetClient = ChatGPTLocator.LocateClient();

            ApplyModernFormStyle();
            InitializeModernComponents();
            ApplySettingsToUI();
            UpdatePreview();

            if (recoveryRes != null && recoveryRes.HasDangling)
            {
                if (!recoveryRes.Success || recoveryRes.ConflictCount > 0)
                {
                    SetStatus("启动恢复提示: " + recoveryRes.Message, Color.FromArgb(220, 38, 38));
                    MessageBox.Show(
                        "检测到上次运行遗留的环境变量事务：\n\n" + recoveryRes.Message + "\n\n若环境异常，请手动检查用户环境变量及 WAL 日志。",
                        "环境变量事务恢复提示",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    SetStatus("启动恢复成功: " + recoveryRes.Message, Color.FromArgb(22, 163, 74));
                }
            }
            else if (recoveryRes != null && !recoveryRes.Success)
            {
                SetStatus("事务检查警告: " + recoveryRes.Message, Color.FromArgb(234, 179, 8));
            }
            else if (!string.IsNullOrEmpty(loadErr))
            {
                SetStatus(loadErr, Color.FromArgb(234, 179, 8));
            }

            this.Shown += (s, e) => { this.ActiveControl = null; };

            portCheckTimer = new System.Windows.Forms.Timer();
            portCheckTimer.Interval = 3000;
            portCheckTimer.Tick += (s, e) => CheckProxyPortStatus();
            portCheckTimer.Start();
            CheckProxyPortStatus();
        }

        private void ApplyModernFormStyle()
        {
            this.Text = "ChatGPT Windows 网络环境启动器 v0.5";
            this.ClientSize = new Size(680, 800);
            this.MinimumSize = new Size(640, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BgWindow;
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.None;

            Font baseFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            try
            {
                using (Font test = new Font("Segoe UI Variable Text", 9F))
                {
                    if (test.Name == "Segoe UI Variable Text")
                    {
                        baseFont = new Font("Segoe UI Variable Text", 9F);
                    }
                }
            }
            catch { }
            this.Font = baseFont;

            try
            {
                int cornerVal = DWMWCP_ROUND;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerVal, sizeof(int));
            }
            catch { }
        }

        private void InitializeModernComponents()
        {
            // Bottom Action Bar
            Panel pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Width = this.ClientSize.Width,
                Height = 88,
                BackColor = BgWindow
            };

            Panel pnlBottomDiv = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = BorderCard
            };
            pnlBottom.Controls.Add(pnlBottomDiv);

            int bottomPadX = 20;
            Panel pnlStatus = new Panel
            {
                Location = new Point(bottomPadX, 10),
                Size = new Size(this.ClientSize.Width - bottomPadX * 2, 22),
                BackColor = Color.Transparent
            };
            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 197, 94),
                Location = new Point(0, 2),
                Size = new Size(16, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblStatusText = new Label
            {
                Text = "就绪 - 独立进程环境隔离启动准备完毕",
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                ForeColor = TextSecondary,
                Location = new Point(18, 2),
                Size = new Size(pnlStatus.Width - 20, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlStatus.Controls.Add(lblStatusDot);
            pnlStatus.Controls.Add(lblStatusText);
            pnlBottom.Controls.Add(pnlStatus);

            Panel pnlActions = new Panel
            {
                Location = new Point(bottomPadX, 36),
                Size = new Size(this.ClientSize.Width - bottomPadX * 2, 40),
                BackColor = Color.Transparent
            };

            btnShortcut = new ModernButton
            {
                Text = "桌面快捷方式",
                Location = new Point(0, 0),
                Size = new Size(130, 36),
                BackColor = BgCard,
                ForeColor = TextPrimary,
                BorderColor = BorderCard,
                HoverColor = Color.FromArgb(241, 245, 249),
                PressedColor = Color.FromArgb(226, 232, 240),
                CornerRadius = 6
            };
            btnShortcut.Click += (s, e) => CreateShortcut();
            pnlActions.Controls.Add(btnShortcut);

            btnSave = new ModernButton
            {
                Text = "仅保存配置",
                Location = new Point(140, 0),
                Size = new Size(120, 36),
                BackColor = BgCard,
                ForeColor = TextPrimary,
                BorderColor = BorderCard,
                HoverColor = Color.FromArgb(241, 245, 249),
                PressedColor = Color.FromArgb(226, 232, 240),
                CornerRadius = 6
            };
            btnSave.Click += (s, e) => { string err; SaveSettings(true, out err); };
            pnlActions.Controls.Add(btnSave);

            btnLaunch = new ModernButton
            {
                Text = "🚀 保存并启动 ChatGPT",
                Location = new Point(270, 0),
                Size = new Size(pnlActions.Width - 270, 36),
                BackColor = AccentBlue,
                ForeColor = Color.White,
                BorderColor = Color.Empty,
                HoverColor = AccentHover,
                PressedColor = AccentPressed,
                CornerRadius = 6,
                Font = new Font(this.Font.FontFamily, 9.5F, FontStyle.Bold)
            };
            btnLaunch.Click += (s, e) => LaunchChatGPT();
            pnlActions.Controls.Add(btnLaunch);

            pnlBottom.Resize += (s, e) =>
            {
                int fullW = pnlBottom.ClientSize.Width;
                int innerW = Math.Max(200, fullW - bottomPadX * 2);
                pnlStatus.Width = innerW;
                lblStatusText.Width = Math.Max(50, innerW - 20);
                pnlActions.Width = innerW;
                if (btnLaunch != null) btnLaunch.Width = Math.Max(100, innerW - 270);
            };

            pnlBottom.Controls.Add(pnlActions);
            this.Controls.Add(pnlBottom);

            // Scrollable Content Area
            Panel pnlScroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = BgWindow
            };
            this.Controls.Add(pnlScroll);
            pnlBottom.SendToBack();

            int padX = 20;
            int cardWidth = this.ClientSize.Width - padX * 2;
            int curY = 16;

            // 1. Header Area
            Panel pnlHeader = new Panel
            {
                Location = new Point(padX, curY),
                Size = new Size(cardWidth, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            RoundedCard iconBadge = new RoundedCard
            {
                Location = new Point(0, 2),
                Size = new Size(48, 48),
                BackColor = Color.FromArgb(238, 242, 255),
                BorderColor = Color.FromArgb(224, 231, 255),
                CornerRadius = 10
            };
            Label lblLogo = new Label
            {
                Text = "🌐",
                Font = new Font("Segoe UI Emoji", 20F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            iconBadge.Controls.Add(lblLogo);
            pnlHeader.Controls.Add(iconBadge);

            Label lblTitle = new Label
            {
                Text = "ChatGPT Windows 网络环境启动器",
                Font = new Font(this.Font.FontFamily, 13F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(58, 2),
                Size = new Size(cardWidth - 60, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlHeader.Controls.Add(lblTitle);

            lblClientFoundStatus = new Label
            {
                Text = targetClient.DisplayDescription,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                ForeColor = targetClient.IsFound ? Color.FromArgb(5, 150, 105) : Color.FromArgb(239, 68, 68),
                Location = new Point(58, 28),
                Size = new Size(cardWidth - 60, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlHeader.Controls.Add(lblClientFoundStatus);

            pnlScroll.Controls.Add(pnlHeader);
            curY += 68;

            // 2. Card: Network Proxy Mode
            cardProxy = CreateRoundedCard(padX, curY, cardWidth, 145);
            curY += 157;

            Label lblProxyTitle = CreateCardTitle("🌐 网络代理模式", 16, 14);
            cardProxy.Controls.Add(lblProxyTitle);

            btnModeVpn = new ModernButton
            {
                Text = "🟢 本地代理模式",
                Location = new Point(16, 42),
                Size = new Size(160, 34),
                BackColor = Color.FromArgb(236, 253, 245),
                ForeColor = Color.FromArgb(5, 150, 105),
                BorderColor = Color.FromArgb(167, 243, 208),
                CornerRadius = 6,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Bold)
            };
            btnModeVpn.Click += (s, e) => SwitchNetworkMode("vpn");
            cardProxy.Controls.Add(btnModeVpn);

            btnModeDirect = new ModernButton
            {
                Text = "🔵 直连模式 (清除代理变量)",
                Location = new Point(184, 42),
                Size = new Size(185, 34),
                BackColor = BgCard,
                ForeColor = TextSecondary,
                BorderColor = BorderCard,
                CornerRadius = 6,
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular)
            };
            btnModeDirect.Click += (s, e) => SwitchNetworkMode("direct");
            cardProxy.Controls.Add(btnModeDirect);

            pnlProxySettings = new Panel
            {
                Location = new Point(16, 82),
                Size = new Size(cardWidth - 32, 54),
                BackColor = BgCard
            };

            Label lblPortPrompt = new Label
            {
                Text = "本地代理端口:",
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Regular),
                ForeColor = TextPrimary,
                Location = new Point(0, 6),
                Size = new Size(95, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlProxySettings.Controls.Add(lblPortPrompt);

            txtProxyPort = new TextBox
            {
                Text = currentSettings.ProxyPort.ToString(),
                Location = new Point(98, 6),
                Size = new Size(58, 24),
                Font = new Font(this.Font.FontFamily, 9F),
                TextAlign = HorizontalAlignment.Center
            };
            txtProxyPort.TextChanged += (s, e) =>
            {
                if (isUpdatingPortInternally) return;
                int p;
                if (int.TryParse(txtProxyPort.Text.Trim(), out p) && p > 0 && p <= 65535)
                {
                    if (currentSettings.AutoDetectProxy)
                    {
                        currentSettings.AutoDetectProxy = false;
                        UpdateAutoPortButtonVisual();
                    }
                    currentSettings.ProxyPort = p;
                    CheckProxyPortStatus();
                    UpdatePreview();
                }
            };
            pnlProxySettings.Controls.Add(txtProxyPort);

            // Auto Pill Button
            int chipX = 162;
            btnAutoPort = new ModernButton
            {
                Text = currentSettings.AutoDetectProxy ? "🟢 自动" : "⚡ 自动",
                Location = new Point(chipX, 4),
                Size = new Size(68, 26),
                BackColor = currentSettings.AutoDetectProxy ? Color.FromArgb(236, 253, 245) : Color.FromArgb(248, 250, 252),
                ForeColor = currentSettings.AutoDetectProxy ? Color.FromArgb(5, 150, 105) : TextSecondary,
                BorderColor = currentSettings.AutoDetectProxy ? Color.FromArgb(167, 243, 208) : BorderCard,
                CornerRadius = 4,
                Font = new Font(this.Font.FontFamily, 7.5F, FontStyle.Bold)
            };
            btnAutoPort.Click += (s, e) =>
            {
                currentSettings.AutoDetectProxy = true;
                UpdateAutoPortButtonVisual();
                CheckProxyPortStatus();
            };
            pnlProxySettings.Controls.Add(btnAutoPort);
            chipX += 73;

            // Preset port pills
            string[] portPresets = new string[] { "7897 (Clash)", "7890 (v2ray)", "10808 (Xray)", "1080 (SS)" };
            int[] portVals = new int[] { 7897, 7890, 10808, 1080 };
            for (int i = 0; i < portPresets.Length; i++)
            {
                int val = portVals[i];
                ModernButton portBtn = new ModernButton
                {
                    Text = portPresets[i],
                    Location = new Point(chipX, 4),
                    Size = new Size(88, 26),
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = TextSecondary,
                    BorderColor = BorderCard,
                    CornerRadius = 4,
                    Font = new Font(this.Font.FontFamily, 7.5F)
                };
                portBtn.Click += (s, e) =>
                {
                    currentSettings.AutoDetectProxy = false;
                    UpdateAutoPortButtonVisual();
                    txtProxyPort.Text = val.ToString();
                };
                pnlProxySettings.Controls.Add(portBtn);
                chipX += 93;
            }

            lblPortStatus = new Label
            {
                Text = "正在检测代理端口...",
                Font = new Font(this.Font.FontFamily, 8F, FontStyle.Regular),
                ForeColor = TextSecondary,
                Location = new Point(0, 32),
                Size = new Size(pnlProxySettings.Width, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlProxySettings.Controls.Add(lblPortStatus);
            cardProxy.Controls.Add(pnlProxySettings);
            pnlScroll.Controls.Add(cardProxy);

            // 3. Card: Exit Node & TimeZone Detection
            cardNodeDetect = CreateRoundedCard(padX, curY, cardWidth, 152);
            curY += 164;

            Label lblDetectTitle = CreateCardTitle("🔍 节点公网出口感知与时区一致性核对", 16, 14);
            cardNodeDetect.Controls.Add(lblDetectTitle);

            btnDetect = new ModernButton
            {
                Text = "🔍 检测公网出口",
                Location = new Point(16, 42),
                Size = new Size(130, 32),
                BackColor = BgCard,
                ForeColor = AccentBlue,
                BorderColor = Color.FromArgb(191, 219, 254),
                HoverColor = Color.FromArgb(239, 246, 255),
                CornerRadius = 6,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Bold)
            };
            btnDetect.Click += (s, e) => DetectCurrentNode();
            cardNodeDetect.Controls.Add(btnDetect);

            btnApplyDetectedTz = new ModernButton
            {
                Text = "⚡ 采用该节点时区",
                Location = new Point(154, 42),
                Size = new Size(140, 32),
                BackColor = Color.FromArgb(236, 253, 245),
                ForeColor = Color.FromArgb(5, 150, 105),
                BorderColor = Color.FromArgb(167, 243, 208),
                HoverColor = Color.FromArgb(209, 250, 229),
                CornerRadius = 6,
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Bold),
                Visible = false
            };
            btnApplyDetectedTz.Click += (s, e) => ApplyDetectedTimezone();
            cardNodeDetect.Controls.Add(btnApplyDetectedTz);

            lblDetectIp = new Label
            {
                Text = "出口 IP: 点击左侧按钮发起探测 (HTTPS)",
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                ForeColor = TextSecondary,
                Location = new Point(16, 84),
                Size = new Size(260, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardNodeDetect.Controls.Add(lblDetectIp);

            lblDetectTz = new Label
            {
                Text = "节点时区: --",
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Regular),
                ForeColor = TextSecondary,
                Location = new Point(16, 110),
                Size = new Size(260, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardNodeDetect.Controls.Add(lblDetectTz);

            lblDetectCompare = new Label
            {
                Text = "时区比对: 未检测",
                Font = new Font(this.Font.FontFamily, 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(285, 78),
                Size = new Size(cardWidth - 300, 64),
                TextAlign = ContentAlignment.TopLeft
            };
            cardNodeDetect.Controls.Add(lblDetectCompare);
            pnlScroll.Controls.Add(cardNodeDetect);

            // 4. Card: Timezone Settings
            cardTz = CreateRoundedCard(padX, curY, cardWidth, 245);
            curY += 257;

            Label lblTzTitle = CreateCardTitle("⚙️ 目标时区配置 (TZ 环境变量)", 16, 14);
            cardTz.Controls.Add(lblTzTitle);

            Label lblMode = new Label
            {
                Text = "时区模式:",
                Location = new Point(16, 44),
                Size = new Size(80, 24),
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardTz.Controls.Add(lblMode);

            comboMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(100, 44),
                Size = new Size(cardWidth - 120, 26),
                Font = new Font(this.Font.FontFamily, 9F)
            };
            comboMode.Items.Add("IANA 标准名称 (推荐，如 America/Los_Angeles)");
            comboMode.Items.Add("UTC 偏移量模式 (自动转换为 POSIX Etc/GMT 格式)");
            comboMode.Items.Add("关闭时区伪装 (不注入 TZ 环境变量)");
            comboMode.SelectedIndexChanged += (s, e) =>
            {
                int idx = comboMode.SelectedIndex;
                comboIana.Visible = (idx == 0);
                comboUtc.Visible = (idx == 1);
                pnlPresets.Visible = (idx == 0 || idx == 1);
                UpdatePreview();
                CompareNodeTimezone();
            };
            cardTz.Controls.Add(comboMode);

            Label lblTzSelect = new Label
            {
                Text = "选择时区:",
                Location = new Point(16, 80),
                Size = new Size(80, 24),
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardTz.Controls.Add(lblTzSelect);

            comboIana = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Location = new Point(100, 80),
                Size = new Size(cardWidth - 120, 26),
                Font = new Font(this.Font.FontFamily, 9F)
            };
            comboIana.Items.AddRange(TimezoneHelper.StandardIanaZones);
            comboIana.TextChanged += (s, e) => { UpdatePreview(); CompareNodeTimezone(); };
            cardTz.Controls.Add(comboIana);

            comboUtc = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(100, 80),
                Size = new Size(cardWidth - 120, 26),
                Font = new Font(this.Font.FontFamily, 9F),
                Visible = false
            };
            PopulateUtcOffsets();
            comboUtc.SelectedIndexChanged += (s, e) => { UpdatePreview(); CompareNodeTimezone(); };
            cardTz.Controls.Add(comboUtc);

            // Preset Chips
            pnlPresets = new Panel
            {
                Location = new Point(100, 116),
                Size = new Size(cardWidth - 120, 68),
                BackColor = BgCard
            };
            AddPresetChips();
            cardTz.Controls.Add(pnlPresets);

            chkGracefulClose = new CheckBox
            {
                Text = "启动前安全关闭正在运行的 ChatGPT 实例 (支持优雅关闭与超时确认)",
                Location = new Point(16, 198),
                Size = new Size(cardWidth - 32, 24),
                ForeColor = TextPrimary,
                Checked = true,
                Font = new Font(this.Font.FontFamily, 8.5F)
            };
            cardTz.Controls.Add(chkGracefulClose);
            pnlScroll.Controls.Add(cardTz);

            // 5. Card: Live Preview
            cardPreview = CreateRoundedCard(padX, curY, cardWidth, 100);
            curY += 112;

            Label lblPreviewTitle = CreateCardTitle("📊 拟注入环境变量预览", 16, 12);
            cardPreview.Controls.Add(lblPreviewTitle);

            lblPreviewTzBadge = new Label
            {
                Text = "TZ = America/Los_Angeles",
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                ForeColor = InfoBarText,
                BackColor = InfoBarBg,
                Location = new Point(16, 36),
                Size = new Size(cardWidth - 32, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardPreview.Controls.Add(lblPreviewTzBadge);

            lblPreviewProxyBadge = new Label
            {
                Text = "HTTP_PROXY = http://127.0.0.1:7897 | ALL_PROXY = http://127.0.0.1:7897",
                Font = new Font("Consolas", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(5, 150, 105),
                BackColor = Color.FromArgb(236, 253, 245),
                Location = new Point(16, 62),
                Size = new Size(cardWidth - 32, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            cardPreview.Controls.Add(lblPreviewProxyBadge);
            pnlScroll.Controls.Add(cardPreview);

            pnlScroll.Resize += (s, e) =>
            {
                int w = pnlScroll.ClientSize.Width - padX * 2;
                if (w > 200)
                {
                    cardProxy.Width = w;
                    cardNodeDetect.Width = w;
                    cardTz.Width = w;
                    cardPreview.Width = w;
                    pnlHeader.Width = w;
                    pnlProxySettings.Width = w - 32;
                    comboMode.Width = w - 120;
                    comboIana.Width = w - 120;
                    comboUtc.Width = w - 120;
                    pnlPresets.Width = w - 120;
                    lblDetectCompare.Width = Math.Max(180, w - 300);
                    lblDetectCompare.Height = 64;
                    lblPreviewTzBadge.Width = w - 32;
                    lblPreviewProxyBadge.Width = w - 32;
                }
            };
        }

        private RoundedCard CreateRoundedCard(int x, int y, int w, int h)
        {
            return new RoundedCard
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = BgCard,
                BorderColor = BorderCard,
                CornerRadius = 10,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
        }

        private Label CreateCardTitle(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font(this.Font.FontFamily, 9.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(x, y),
                AutoSize = true
            };
        }

        private void SwitchNetworkMode(string mode)
        {
            currentSettings.NetworkMode = mode;
            bool isVpn = (mode == "vpn");

            btnModeVpn.BackColor = isVpn ? Color.FromArgb(236, 253, 245) : BgCard;
            btnModeVpn.ForeColor = isVpn ? Color.FromArgb(5, 150, 105) : TextSecondary;
            btnModeVpn.BorderColor = isVpn ? Color.FromArgb(167, 243, 208) : BorderCard;
            btnModeVpn.Font = new Font(this.Font.FontFamily, 9F, isVpn ? FontStyle.Bold : FontStyle.Regular);

            btnModeDirect.BackColor = !isVpn ? Color.FromArgb(239, 246, 255) : BgCard;
            btnModeDirect.ForeColor = !isVpn ? AccentBlue : TextSecondary;
            btnModeDirect.BorderColor = !isVpn ? Color.FromArgb(191, 219, 254) : BorderCard;
            btnModeDirect.Font = new Font(this.Font.FontFamily, 9F, !isVpn ? FontStyle.Bold : FontStyle.Regular);

            pnlProxySettings.Visible = isVpn;

            // Invalidate older probe result upon mode switch
            NetworkProbeService.NextGeneration();
            UpdatePreview();
        }

        private void UpdateAutoPortButtonVisual()
        {
            if (btnAutoPort == null) return;
            bool isAuto = currentSettings.AutoDetectProxy;
            btnAutoPort.Text = isAuto ? "🟢 自动" : "⚡ 自动";
            btnAutoPort.BackColor = isAuto ? Color.FromArgb(236, 253, 245) : Color.FromArgb(248, 250, 252);
            btnAutoPort.ForeColor = isAuto ? Color.FromArgb(5, 150, 105) : TextSecondary;
            btnAutoPort.BorderColor = isAuto ? Color.FromArgb(167, 243, 208) : BorderCard;
            btnAutoPort.Invalidate();
        }

        private void CheckProxyPortStatus()
        {
            if (currentSettings.NetworkMode != "vpn")
            {
                lblPortStatus.Text = "直连模式：不注入代理环境变量。";
                lblPortStatus.ForeColor = TextSecondary;
                return;
            }

            bool isAuto = currentSettings.AutoDetectProxy;
            int curPort = currentSettings.ProxyPort;

            ThreadPool.QueueUserWorkItem(delegate
            {
                if (isAuto)
                {
                    int detectedPort = ProxyService.DetectActiveProxyPort(curPort);
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(delegate
                        {
                            if (detectedPort > 0)
                            {
                                if (currentSettings.ProxyPort != detectedPort || txtProxyPort.Text != detectedPort.ToString())
                                {
                                    isUpdatingPortInternally = true;
                                    currentSettings.ProxyPort = detectedPort;
                                    txtProxyPort.Text = detectedPort.ToString();
                                    isUpdatingPortInternally = false;
                                    UpdatePreview();
                                }
                                lblPortStatus.Text = string.Format("🟢 自动接入: 127.0.0.1:{0} 正在正常监听中", detectedPort);
                                lblPortStatus.ForeColor = Color.FromArgb(5, 150, 105);
                            }
                            else
                            {
                                lblPortStatus.Text = "⚪ 自动探测中: 未检测到活跃的本地代理端口 (请确保代理软件已启动)";
                                lblPortStatus.ForeColor = Color.FromArgb(234, 179, 8);
                            }
                        }));
                    }
                }
                else
                {
                    bool listening = ProxyService.IsPortListening("127.0.0.1", curPort, 200);
                    int activeAltPort = 0;
                    if (!listening)
                    {
                        int[] candidates = new int[] { 7897, 7890, 10808, 1080 };
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (candidates[i] != curPort && ProxyService.IsPortListening("127.0.0.1", candidates[i], 100))
                            {
                                activeAltPort = candidates[i];
                                break;
                            }
                        }
                    }

                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(delegate
                        {
                            if (listening)
                            {
                                lblPortStatus.Text = string.Format("🟢 127.0.0.1:{0} 正在正常监听中", curPort);
                                lblPortStatus.ForeColor = Color.FromArgb(5, 150, 105);
                            }
                            else
                            {
                                if (activeAltPort > 0)
                                {
                                    lblPortStatus.Text = string.Format("⚪ 127.0.0.1:{0} 未监听 (检测到端口 {1} 正在运行，可点击上方按钮切换)", curPort, activeAltPort);
                                }
                                else
                                {
                                    lblPortStatus.Text = string.Format("⚪ 127.0.0.1:{0} 未检测到监听服务 (请确保代理软件已运行)", curPort);
                                }
                                lblPortStatus.ForeColor = Color.FromArgb(234, 179, 8);
                            }
                        }));
                    }
                }
            });
        }

        private void DetectCurrentNode()
        {
            btnDetect.Enabled = false;
            btnDetect.Text = "探测中...";
            lblDetectIp.Text = "出口 IP: 正在连接权威 HTTPS 接口...";
            lblDetectTz.Text = "节点时区: 正在获取...";
            lblDetectCompare.Text = "时区比对: 正在计算...";
            lblDetectCompare.ForeColor = TextSecondary;

            bool isVpn = (currentSettings.NetworkMode == "vpn");
            ProxyConfig proxy = new ProxyConfig("127.0.0.1", currentSettings.ProxyPort);
            int token = NetworkProbeService.NextGeneration();

            ThreadPool.QueueUserWorkItem(delegate
            {
                ProbeResult result = NetworkProbeService.ProbeNode(proxy, isVpn, token, 5000);

                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke(new Action(delegate
                    {
                        btnDetect.Enabled = true;
                        btnDetect.Text = "🔍 检测公网出口";

                        if (result.GenerationToken != NetworkProbeService.CurrentGeneration)
                        {
                            // Older request expired, discard
                            return;
                        }

                        if (result.Success)
                        {
                            lastDetectedIana = result.IanaTimezone;
                            lastDetectedOffset = result.UtcOffset;
                            hasDetectedTz = true;

                            lblDetectIp.Text = string.Format("出口 IP: {0} ({1})", result.Ip, result.Country ?? "未知");
                            lblDetectTz.Text = string.Format("节点时区: {0} ({1})",
                                result.IanaTimezone,
                                TimezoneHelper.FormatOffset(result.UtcOffset));
                            btnApplyDetectedTz.Visible = true;

                            CompareNodeTimezone();
                        }
                        else
                        {
                            lblDetectIp.Text = "出口 IP: 探测失败";
                            lblDetectTz.Text = "节点时区: --";
                            lblDetectCompare.Text = string.Format("探测失败 ({0}): {1}", result.ErrorKind, result.ErrorMessage);
                            lblDetectCompare.ForeColor = Color.FromArgb(239, 68, 68);
                        }
                    }));
                }
            });
        }

        private void CompareNodeTimezone()
        {
            if (!hasDetectedTz || string.IsNullOrEmpty(lastDetectedIana))
            {
                lblDetectCompare.Text = "时区比对: 未检测节点";
                lblDetectCompare.ForeColor = TextSecondary;
                return;
            }

            if (comboMode.SelectedIndex == 2) // Disabled
            {
                lblDetectCompare.Text = "时区比对: 当前已选择【关闭时区伪装】，将按操作系统默认时区运行。";
                lblDetectCompare.ForeColor = TextSecondary;
                return;
            }

            string mode = (comboMode.SelectedIndex == 0) ? "iana" : "utc";
            string selectedIana = comboIana.Text.Trim();
            string selectedUtc = comboUtc.Text.Trim();

            bool isMatched = TimezoneHelper.IsTimezoneMatch(mode, selectedIana, selectedUtc, lastDetectedIana, lastDetectedOffset);

            if (isMatched)
            {
                lblDetectCompare.Text = "时区比对: 🟢 配置时区与探测到的出口节点时区一致。";
                lblDetectCompare.ForeColor = Color.FromArgb(5, 150, 105);
            }
            else
            {
                string targetDisplay = (mode == "iana") ? selectedIana : selectedUtc;
                lblDetectCompare.Text = string.Format("时区比对: ⚠️ 时区配置不一致。\n探测节点: [{0}]\n当前配置: [{1}]", lastDetectedIana, targetDisplay);
                lblDetectCompare.ForeColor = Color.FromArgb(220, 38, 38);
            }
        }

        private void ApplyDetectedTimezone()
        {
            if (!hasDetectedTz || string.IsNullOrEmpty(lastDetectedIana)) return;

            comboMode.SelectedIndex = 0; // Switch to IANA mode
            comboIana.Text = lastDetectedIana;

            CompareNodeTimezone();
            UpdatePreview();
            SetStatus(string.Format("已对齐为节点时区: {0}", lastDetectedIana), Color.FromArgb(5, 150, 105));
        }

        private void PopulateUtcOffsets()
        {
            for (int i = -12; i <= 14; i++)
            {
                string text = string.Format("UTC{0}{1}", i >= 0 ? "+" : "", i);
                comboUtc.Items.Add(text);
            }
            comboUtc.Text = currentSettings.UtcOffset;
        }

        private void AddPresetChips()
        {
            var presets = new[]
            {
                new { Label = "美西 (洛杉矶)", Iana = "America/Los_Angeles", Utc = "UTC-8" },
                new { Label = "美东 (纽约)",   Iana = "America/New_York",    Utc = "UTC-5" },
                new { Label = "英国 (伦敦)",   Iana = "Europe/London",       Utc = "UTC+0" },
                new { Label = "日本 (东京)",   Iana = "Asia/Tokyo",          Utc = "UTC+9" },
                new { Label = "新加坡",        Iana = "Asia/Singapore",      Utc = "UTC+8" },
                new { Label = "香港",          Iana = "Asia/Hong_Kong",      Utc = "UTC+8" }
            };

            int col = 0, row = 0;
            int chipW = 100, chipH = 26, gapX = 8, gapY = 6;

            foreach (var p in presets)
            {
                ModernButton chip = new ModernButton
                {
                    Text = p.Label,
                    Location = new Point(col * (chipW + gapX), row * (chipH + gapY)),
                    Size = new Size(chipW, chipH),
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = TextSecondary,
                    BorderColor = BorderCard,
                    CornerRadius = 6,
                    Font = new Font(this.Font.FontFamily, 8F)
                };

                string ianaVal = p.Iana;
                string utcVal = p.Utc;
                chip.Click += (s, e) =>
                {
                    if (comboMode.SelectedIndex == 0)
                    {
                        comboIana.Text = ianaVal;
                    }
                    else if (comboMode.SelectedIndex == 1)
                    {
                        comboUtc.Text = utcVal;
                    }
                    UpdatePreview();
                };

                presetButtons.Add(chip);
                pnlPresets.Controls.Add(chip);

                col++;
                if (col >= 3)
                {
                    col = 0;
                    row++;
                }
            }
        }

        private void UpdatePreview()
        {
            bool disableTz = (comboMode.SelectedIndex == 2);
            string mode = (comboMode.SelectedIndex == 0) ? "iana" : "utc";
            TimezoneResolveResult tzRes = TimezoneHelper.ResolveTimezone(mode, comboIana.Text.Trim(), comboUtc.Text.Trim(), disableTz);

            if (tzRes.IsDisabled)
            {
                lblPreviewTzBadge.Text = "(时区注入已关闭：目标进程将遵从系统原生时区)";
                lblPreviewTzBadge.ForeColor = TextSecondary;
                lblPreviewTzBadge.BackColor = Color.FromArgb(241, 245, 249);
            }
            else if (!tzRes.Success)
            {
                lblPreviewTzBadge.Text = string.Format("⚠️ 时区配置无效: {0}", tzRes.ErrorMessage);
                lblPreviewTzBadge.ForeColor = Color.FromArgb(220, 38, 38);
                lblPreviewTzBadge.BackColor = Color.FromArgb(254, 242, 242);
            }
            else
            {
                lblPreviewTzBadge.Text = string.Format("TZ = {0} ({1})", tzRes.ResolvedTzValue, tzRes.DisplaySummary);
                lblPreviewTzBadge.ForeColor = InfoBarText;
                lblPreviewTzBadge.BackColor = InfoBarBg;
            }

            if (currentSettings.NetworkMode == "vpn")
            {
                lblPreviewProxyBadge.Text = string.Format("HTTP_PROXY = http://127.0.0.1:{0} | ALL_PROXY = http://127.0.0.1:{0}", currentSettings.ProxyPort);
                lblPreviewProxyBadge.ForeColor = Color.FromArgb(5, 150, 105);
                lblPreviewProxyBadge.BackColor = Color.FromArgb(236, 253, 245);
            }
            else
            {
                lblPreviewProxyBadge.Text = "(直连模式：显式移除 HTTP_PROXY / HTTPS_PROXY / ALL_PROXY)";
                lblPreviewProxyBadge.ForeColor = AccentBlue;
                lblPreviewProxyBadge.BackColor = Color.FromArgb(239, 246, 255);
            }
        }

        private void ApplySettingsToUI()
        {
            if (currentSettings.DisableTz)
            {
                comboMode.SelectedIndex = 2;
            }
            else
            {
                comboMode.SelectedIndex = (currentSettings.Mode == "iana") ? 0 : 1;
            }

            if (!string.IsNullOrEmpty(currentSettings.IanaName)) comboIana.Text = currentSettings.IanaName;
            if (!string.IsNullOrEmpty(currentSettings.UtcOffset)) comboUtc.Text = currentSettings.UtcOffset;
            chkGracefulClose.Checked = currentSettings.GracefulClose;

            SwitchNetworkMode(currentSettings.NetworkMode ?? "vpn");
            isUpdatingPortInternally = true;
            txtProxyPort.Text = currentSettings.ProxyPort.ToString();
            isUpdatingPortInternally = false;
            UpdateAutoPortButtonVisual();
        }

        private bool SaveSettings(bool showNotification, out string errorMessage)
        {
            errorMessage = null;

            // 1. Validate and apply port
            string portText = txtProxyPort.Text.Trim();
            int port;
            if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
            {
                errorMessage = string.Format("代理端口格式无效 ('{0}')，端口必须是 1 到 65535 之间的有效整数", portText);
                if (showNotification)
                {
                    MessageBox.Show(errorMessage, "输入格式错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("保存失败: 端口格式错误", Color.FromArgb(239, 68, 68));
                }
                return false;
            }
            currentSettings.ProxyPort = port;

            // 2. Validate timezone settings
            bool disableTz = (comboMode.SelectedIndex == 2);
            string mode = (comboMode.SelectedIndex == 0) ? "iana" : "utc";
            string ianaText = comboIana.Text.Trim();
            string utcText = comboUtc.Text.Trim();

            TimezoneResolveResult tzRes = TimezoneHelper.ResolveTimezone(mode, ianaText, utcText, disableTz);
            if (!tzRes.Success)
            {
                errorMessage = tzRes.ErrorMessage;
                if (showNotification)
                {
                    MessageBox.Show("时区配置错误:\n" + errorMessage, "时区校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("保存失败: " + errorMessage, Color.FromArgb(239, 68, 68));
                }
                return false;
            }

            currentSettings.DisableTz = disableTz;
            currentSettings.Mode = mode;
            currentSettings.IanaName = ianaText;
            currentSettings.UtcOffset = utcText;
            currentSettings.GracefulClose = chkGracefulClose.Checked;

            // 3. Commit to store
            string saveError;
            bool ok = SettingsStore.Save(currentSettings, out saveError);
            if (!ok)
            {
                errorMessage = saveError;
                if (showNotification)
                {
                    MessageBox.Show("保存配置失败:\n" + saveError, "保存错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("保存配置失败: " + saveError, Color.FromArgb(239, 68, 68));
                }
                return false;
            }

            if (showNotification)
            {
                SetStatus("配置已成功保存！", Color.FromArgb(5, 150, 105));
            }
            return true;
        }

        private void CreateShortcut()
        {
            try
            {
                string saveErr;
                if (!SaveSettings(false, out saveErr))
                {
                    MessageBox.Show("创建快捷方式前保存配置失败:\n" + saveErr, "保存配置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetStatus("快捷方式创建中止: 配置保存失败", Color.FromArgb(239, 68, 68));
                    return;
                }

                DesktopShortcutManager.CreateOrUpdate("ChatGPT (网络环境版)", "ChatGPT Windows 网络环境启动器");
                MessageBox.Show("桌面快捷方式「ChatGPT (网络环境版)」创建成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("桌面快捷方式已创建", Color.FromArgb(5, 150, 105));
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建快捷方式失败:\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("创建快捷方式失败: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void LaunchChatGPT()
        {
            string saveErr;
            if (!SaveSettings(false, out saveErr))
            {
                MessageBox.Show("启动前保存配置或时区校验失败:\n" + saveErr + "\n\n已中止启动。", "启动前检查失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("启动中止: " + saveErr, Color.FromArgb(239, 68, 68));
                return;
            }

            btnLaunch.Enabled = false;
            btnSave.Enabled = false;
            btnShortcut.Enabled = false;
            SetStatus("正在执行启动前检查...", AccentBlue);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 1. Locate client
                    TargetClientInfo client = ChatGPTLocator.LocateClient();
                    if (!client.IsFound)
                    {
                        this.Invoke(new Action(delegate
                        {
                            MessageBox.Show(
                                "未能定位到官方 ChatGPT 客户端的安装！\n\n请确认：\n1. 是否已通过应用商店安装 ChatGPT (OpenAI.Codex)；\n2. 或是否安装了独立桌面版。",
                                "未找到客户端", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            SetStatus("未定位到 ChatGPT 客户端", Color.FromArgb(239, 68, 68));
                            ResetActionButtons();
                        }));
                        return;
                    }

                    // 2. Identify existing instances and handle shutdown
                    List<Process> existing = LauncherService.FindMatchingTargetProcesses(client);
                    if (existing.Count > 0)
                    {
                        this.Invoke(new Action(delegate
                        {
                            SetStatus(string.Format("检测到 {0} 个目标进程实例，正在执行关闭保护...", existing.Count), Color.FromArgb(234, 179, 8));
                        }));

                        string shutdownFail;
                        bool shutdownOk = LauncherService.CloseExistingInstances(
                            existing,
                            !chkGracefulClose.Checked,
                            promptMsg =>
                            {
                                DialogResult dr = DialogResult.No;
                                this.Invoke(new Action(delegate
                                {
                                    dr = MessageBox.Show(promptMsg, "退出超时确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                                }));
                                return (dr == DialogResult.Yes);
                            },
                            out shutdownFail);

                        if (!shutdownOk)
                        {
                            this.Invoke(new Action(delegate
                            {
                                SetStatus("启动取消: " + (shutdownFail ?? "旧进程未能安全退出"), Color.FromArgb(239, 68, 68));
                                ResetActionButtons();
                            }));
                            return;
                        }
                    }

                    // 3. Launch
                    this.Invoke(new Action(delegate
                    {
                        SetStatus("正在唤起客户端并注入环境...", AccentBlue);
                    }));

                    LaunchResult result = LauncherService.Launch(client, currentSettings);

                    this.Invoke(new Action(delegate
                    {
                        if (result.Succeeded)
                        {
                            if (result.WarningLevel == LaunchWarningLevel.Warning || result.HasConflicts)
                            {
                                string warnMsg = result.WarningMessage ?? result.Summary ?? string.Format("客户端已启动 (PID: {0})，检测到 {1} 项外部修改并已保留", result.TargetPid, result.ConflictCount);
                                MessageBox.Show("客户端已成功启动，但环境恢复阶段检测到外部修改:\n\n" + warnMsg + "\n\n为避免破坏外部工具或系统配置，冲突变量已保留当前值未覆盖。", "启动与环境冲突提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                SetStatus("警告: " + warnMsg, Color.FromArgb(217, 119, 6));
                            }
                            else
                            {
                                string displayMsg = string.Format("启动成功: {0} | {1}", result.ProcessStatus, result.EnvironmentAdoptionStatus);
                                SetStatus(displayMsg, Color.FromArgb(5, 150, 105));
                            }
                        }
                        else
                        {
                            string err = result.ErrorMessage ?? result.Summary ?? "启动或环境注入异常";
                            if (result.TargetPid > 0)
                            {
                                MessageBox.Show("客户端已拉起，但环境恢复异常:\n" + err + "\n\n请检查 WAL 日志或用户环境变量！", "启动与恢复告警", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                SetStatus("警告: " + err, Color.FromArgb(220, 38, 38));
                            }
                            else
                            {
                                MessageBox.Show("启动失败:\n" + err, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                SetStatus("启动失败: " + err, Color.FromArgb(239, 68, 68));
                            }
                        }
                        ResetActionButtons();
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(delegate
                    {
                        MessageBox.Show("启动过程出现异常:\n" + ex.Message, "异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SetStatus("启动异常: " + ex.Message, Color.FromArgb(239, 68, 68));
                        ResetActionButtons();
                    }));
                }
            });
        }

        private void ResetActionButtons()
        {
            btnLaunch.Enabled = true;
            btnSave.Enabled = true;
            btnShortcut.Enabled = true;
        }

        private void SetStatus(string text, Color dotColor)
        {
            lblStatusText.Text = text;
            lblStatusDot.ForeColor = dotColor;
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;

        private static void EnsureConsoleOutput()
        {
            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                IntPtr stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (stdOut != IntPtr.Zero && stdOut != new IntPtr(-1))
                {
                    Microsoft.Win32.SafeHandles.SafeFileHandle sfh = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdOut, false);
                    FileStream fs = new FileStream(sfh, FileAccess.Write);
                    StreamWriter sw = new StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
                    Console.SetOut(sw);
                    Console.SetError(sw);
                }
            }
            catch { }
        }

        public static int RunHeadlessLaunch(string[] args)
        {
            EnsureConsoleOutput();
            Console.WriteLine("[INFO] ChatGPT Anti-Ban Launcher - Headless Launch Mode");

            string loadErr;
            LauncherSettings settings = SettingsStore.Load(out loadErr);
            if (settings == null)
            {
                Console.WriteLine("[ERROR] Failed to load configuration: " + loadErr);
                return 1;
            }

            RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
            if (rec != null && rec.HasDangling)
            {
                Console.WriteLine("[WARN] Dangling transaction recovered from prior session. Conflicts: " + rec.ConflictCount);
            }

            TargetClientInfo client = ChatGPTLocator.LocateClient();
            if (!client.IsFound)
            {
                Console.WriteLine("[ERROR] Could not locate official ChatGPT client (OpenAI.Codex or Standalone).");
                return 2;
            }

            Console.WriteLine(string.Format("[INFO] Target client: {0} ({1})", client.DisplayDescription, client.ExecutablePath));

            List<Process> existing = LauncherService.FindMatchingTargetProcesses(client);
            if (existing.Count > 0)
            {
                Console.WriteLine(string.Format("[INFO] Found {0} running instance(s), performing graceful close...", existing.Count));
                string shutdownFail;
                bool closed = LauncherService.CloseExistingInstances(existing, true, null, out shutdownFail, 3000);
                if (!closed)
                {
                    Console.WriteLine("[WARN] Graceful close could not terminate old instances: " + shutdownFail);
                }
            }

            Console.WriteLine(string.Format("[INFO] Injecting parameters: NetworkMode={0}, Port={1}, TzMode={2}, IANA={3}, UTC={4}",
                settings.NetworkMode, settings.ProxyPort, settings.Mode, settings.IanaName, settings.UtcOffset));

            LaunchResult result = LauncherService.Launch(client, settings);
            Console.WriteLine("[RESULT] Succeeded=" + result.Succeeded);
            Console.WriteLine("[RESULT] TargetPid=" + result.TargetPid);
            Console.WriteLine("[RESULT] WarningLevel=" + result.WarningLevel);
            Console.WriteLine("[RESULT] HasConflicts=" + result.HasConflicts);
            Console.WriteLine("[RESULT] ConflictCount=" + result.ConflictCount);
            Console.WriteLine("[RESULT] ProcessStatus=" + result.ProcessStatus);
            Console.WriteLine("[RESULT] EnvironmentAdoptionStatus=" + result.EnvironmentAdoptionStatus);
            Console.WriteLine("[RESULT] Summary=" + result.Summary);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                Console.WriteLine("[RESULT] ErrorMessage=" + result.ErrorMessage);
            }
            if (!string.IsNullOrEmpty(result.WarningMessage))
            {
                Console.WriteLine("[RESULT] WarningMessage=" + result.WarningMessage);
            }

            return result.Succeeded ? 0 : 3;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string first = args[0].ToLowerInvariant();
                if (first == "--launch" || first == "-l" || first == "/launch" || first == "--headless")
                {
                    return RunHeadlessLaunch(args);
                }
                if (first == "--help" || first == "-h" || first == "/?")
                {
                    EnsureConsoleOutput();
                    Console.WriteLine("ChatGPT Anti-Ban Launcher");
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  ChatGPTAntiBanLauncher.exe           Start graphical user interface (GUI)");
                    Console.WriteLine("  ChatGPTAntiBanLauncher.exe --launch  Start client immediately in headless mode");
                    Console.WriteLine("  ChatGPTAntiBanLauncher.exe --help    Display this help text");
                    return 0;
                }
            }

            try
            {
                try { SetProcessDPIAware(); } catch { }
                NetworkProbeService.ConfigureSecurityProtocols();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText("crash.log", ex.ToString());
                }
                catch { }
                MessageBox.Show(ex.ToString(), "启动异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
