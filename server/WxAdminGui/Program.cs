using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WxAdmin.Shared;

namespace WxAdminGui;

/// <summary>
/// 大面聊天 · 图形管理端：后端/前端启停、端口、自启动、托盘后台、在线用户、安全设置、用户管理、控制台与日志。
/// 与 WxAdmin 控制台共用 wxadmin.json、.server.pid、security.json 与同一套管理 API。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startHidden: args.Any(a => a is "--tray" or "-t")));
    }
}

internal static class G
{
    /// <summary>bin/Debug/net8.0 上溯 3 级 → 项目目录（WxAdminGui）。</summary>
    public static string GuiDir { get; } = Up(AppContext.BaseDirectory, 3);
    public static string ServerDir { get; } = Directory.GetParent(GuiDir)!.FullName;
    public static string WxAdminDir { get; } = Path.Combine(ServerDir, "WxAdmin");
    public static string WebProject { get; } = Path.Combine(ServerDir, "WeChatWeb");
    public static string WebDll { get; } = Path.Combine(WebProject, "bin", "Debug", "net8.0", "WeChatWeb.dll");
    public static string ConfigFile { get; } = Path.Combine(WxAdminDir, "wxadmin.json");
    public static string PidFile { get; } = Path.Combine(WxAdminDir, ".server.pid");
    public static string SecurityFile { get; } = Path.Combine(WebProject, "security.json");
    public static string LogDir { get; } = Path.Combine(WebProject, "logs");
    public static string ConsoleLogFile { get; } = Path.Combine(LogDir, "console.log");

    public static HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// 管理 API 一律返回 camelCase JSON；GUI 的 DTO（SecurityDto/LimitsDto）是 PascalCase。
    /// 默认反序列化大小写敏感会全部匹配不上 → 读成空/默认（"配置/限制是空的"）。
    /// 这里统一大小写不敏感，所有「读配置/读限制/读图库/读安全」都用它。
    /// </summary>
    public static readonly JsonSerializerOptions ApiJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static string Up(string path, int levels)
    {
        var dir = new DirectoryInfo(path);
        for (var i = 0; i < levels && dir != null; i++) dir = dir.Parent;
        return dir!.FullName;
    }
}

internal sealed class WxConfig
{
    public int Port { get; set; } = 5000;
    /// <summary>启动管理端时自动启动后端服务。</summary>
    public bool AutoStartServer { get; set; }
    /// <summary>开机自启（写 HKCU Run 注册表；以本属性为准会在保存时同步注册表）。</summary>
    public bool AutoRun { get; set; }

    public static WxConfig Load()
    {
        try
        {
            if (File.Exists(G.ConfigFile))
            {
                var c = JsonSerializer.Deserialize<WxConfig>(File.ReadAllText(G.ConfigFile));
                if (c != null) return c;
            }
        }
        catch { /* 损坏则用默认 */ }
        return new WxConfig();
    }

    public void Save() => File.WriteAllText(G.ConfigFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

internal sealed class SecurityCfg
{
    public string RegisterKey { get; set; } = "";   // 空 = 关闭注册，管理员在控制台设置注册密码
    public bool EnableCaptcha { get; set; } = true;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 10;
    public int AuthPerMinute { get; set; } = 20;
    public int ApiPerMinute { get; set; } = 300;
    public int FwRequestsPerMinute { get; set; } = 1000;
    public int FwMaxConcurrent { get; set; } = 60;
    public long FwBytesPerMinute { get; set; } = 500L * 1024 * 1024;
    public int FwBanThreshold { get; set; } = 50;
    public int FwBanMinutes { get; set; } = 30;
    public int FloodMessagesPerMinute { get; set; } = 60;

    public static SecurityCfg LoadFile()
    {
        try
        {
            if (File.Exists(G.SecurityFile))
            {
                var s = JsonSerializer.Deserialize<SecurityCfg>(File.ReadAllText(G.SecurityFile));
                if (s != null) return s;
            }
        }
        catch { /* 损坏则用默认 */ }
        return new SecurityCfg();
    }

    public void SaveFile() => File.WriteAllText(G.SecurityFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

internal sealed class MainForm : Form
{
    private readonly WxConfig _cfg = WxConfig.Load();
    private readonly bool _startHidden;

    /* ---- 概览页 ---- */
    private readonly Label _lblStatus = new() { Text = "状态：检测中…", AutoSize = true, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold) };
    private readonly Label _lblDetail = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnStart = new() { Text = "启动服务", AutoSize = true };
    private readonly Button _btnStop = new() { Text = "停止后端", AutoSize = true };
    private readonly Button _btnRestart = new() { Text = "重启服务", AutoSize = true };
    private readonly Button _btnOpenLogs = new() { Text = "打开日志目录", AutoSize = true };
    private readonly Button _btnOpenWeb = new() { Text = "打开网页", AutoSize = true };
    private readonly NumericUpDown _nudPort = new() { Minimum = 1, Maximum = 65535, Value = 5000, Width = 110 };
    private readonly Button _btnPortSave = new() { Text = "保存配置", AutoSize = true };
    private readonly CheckBox _chkAutoRun = new() { Text = "开机自启动（当前用户）", AutoSize = true };
    private readonly CheckBox _chkAutoStartServer = new() { Text = "启动管理端时自动启动服务", AutoSize = true };

    /* ---- 在线用户（概览页） ---- */
    private readonly GroupBox _onlineBox = new() { Text = "在线用户", Dock = DockStyle.Fill, Padding = new Padding(8) };
    private readonly ListView _online = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Button _btnOnlineRefresh = new() { Text = "刷新", AutoSize = true };
    private bool _onlineBusy;

    /* ---- 安全页 ---- */
    private readonly TextBox _txtRegKey = new() { Width = 220 };
    private readonly CheckBox _chkCaptcha = new() { Text = "启用图形验证码", AutoSize = true, Checked = true };
    private readonly NumericUpDown _nudAttempts = new() { Minimum = 1, Maximum = 999, Width = 80, Value = 5 };
    private readonly NumericUpDown _nudLockMin = new() { Minimum = 1, Maximum = 999, Width = 80, Value = 10 };
    private readonly NumericUpDown _nudPerMin = new() { Minimum = 5, Maximum = 9999, Width = 80, Value = 20 };
    private readonly NumericUpDown _nudApiPerMin = new() { Minimum = 10, Maximum = 999999, Width = 90, Value = 300 };
    // 防火墙（IP 维度）+ 消息洪泛（身份维度）限制：security.json 一直有、此前 GUI 没有控件。
    // 服务端有下限地板（fw 请求 ≥100 / 并发 ≥2 / 下行 ≥1MB），控件 Minimum 与之对齐。
    private readonly NumericUpDown _nudFwReq = new() { Minimum = 100, Maximum = 9999999, Width = 90, Value = 1000 };
    private readonly NumericUpDown _nudFwConc = new() { Minimum = 2, Maximum = 99999, Width = 70, Value = 60 };
    private readonly NumericUpDown _nudFwMb = new() { Minimum = 1, Maximum = 102400, Width = 80, Value = 500 };
    private readonly NumericUpDown _nudFwBanHit = new() { Minimum = 1, Maximum = 99999, Width = 70, Value = 50 };
    private readonly NumericUpDown _nudFwBanMin = new() { Minimum = 1, Maximum = 10080, Width = 70, Value = 30 };
    private readonly NumericUpDown _nudFloodMsgs = new() { Minimum = 1, Maximum = 100000, Width = 80, Value = 60 };
    private readonly Button _btnSecRead = new() { Text = "读取", AutoSize = true };
    private readonly Button _btnSecSave = new() { Text = "保存", AutoSize = true };
    private readonly Label _lblSecHint = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };
    /* ---- 日志页自动刷新 ---- */
    private bool _logBusy;
    private TabPage? _logTab;
    private TabControl? _tabs;

    /* ---- 文件与群限制（GUI 可写，后端即时生效） ---- */
    private readonly NumericUpDown _nudMaxFileMb = new() { Minimum = 1, Maximum = 1024, Width = 70 };   // 最大 1GB（与后端 Kestrel/存储上限对齐）
    private readonly NumericUpDown _nudSpeedMb = new() { Minimum = 1, Maximum = 200, Width = 70 };
    private readonly NumericUpDown _nudKeepDays = new() { Minimum = 1, Maximum = 365, Width = 70 };
    private readonly NumericUpDown _nudGroupMembers = new() { Minimum = 2, Maximum = 1000, Width = 70 };
    private readonly NumericUpDown _nudGroupAdmins = new() { Minimum = 0, Maximum = 50, Width = 70 };
    private readonly Button _btnLimitsRead = new() { Text = "读取限制", AutoSize = true };
    private readonly Button _btnLimitsSave = new() { Text = "保存限制", AutoSize = true };
    private readonly Button _btnCleanupFiles = new() { Text = "立即清理过期文件", AutoSize = true };
    private readonly Button _btnCleanupData = new() { Text = "立即清理数据", AutoSize = true };
    private readonly Label _lblLimitsHint = new() { Text = "", AutoSize = true, ForeColor = Color.DimGray };

    /* ---- 用户页 ---- */
    private readonly ListView _users = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Button _btnUsersRefresh = new() { Text = "刷新用户", AutoSize = true };
    private readonly Button _btnBanToggle = new() { Text = "停用 / 恢复", AutoSize = true };
    private readonly Button _btnResetPwd = new() { Text = "重置密码", AutoSize = true };
    private readonly Button _btnCreateUser = new() { Text = "创建用户", AutoSize = true };
    private readonly Button _btnDeleteUser = new() { Text = "删除用户", AutoSize = true, ForeColor = Color.Firebrick };

    /* ---- 日志页 ---- */
    private readonly ComboBox _cmbLogType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
    private readonly Button _btnLogRefresh = new() { Text = "刷新日志", AutoSize = true };

    /* ---- 用户反馈页 ---- */
    private readonly ListView _feedback = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Button _btnFbRefresh = new() { Text = "刷新", AutoSize = true };
    private readonly Button _btnFbCopy = new() { Text = "复制选中", AutoSize = true };
    private readonly Button _btnFbCopyAll = new() { Text = "复制全部", AutoSize = true };
    private readonly Button _btnFbDelete = new() { Text = "删除选中", AutoSize = true };
    private readonly Label _lblFbHint = new() { Text = "客户端「设置 → 问题反馈」提交（可复制正文 + 客户端快照）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) };

    /* ---- 蜜罐页（管理 API GET/PUT /api/admin/honeypot；开关 + 事件日志 + 金丝雀） ---- */
    private readonly ListView _honeyEvents = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly ListView _honeyCanaries = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly CheckBox _chkHoneyEnabled = new() { Text = "蜜罐总开关", AutoSize = true };
    private readonly CheckBox _chkHoneyTarpit = new() { Text = "tarpit 拖延", AutoSize = true };
    private readonly Button _btnHoneySave = new() { Text = "保存开关", AutoSize = true };
    private readonly Button _btnHoneyRefresh = new() { Text = "刷新", AutoSize = true };
    private readonly Button _btnHoneyCopy = new() { Text = "复制选中事件", AutoSize = true };
    private readonly Button _btnHoneyCopyAll = new() { Text = "复制全部事件", AutoSize = true };
    private readonly Label _lblHoneyHint = new() { Text = "诱饵留痕 + tarpit 拖延 + 金丝雀溯源；数据存 data/honey/（隔离，不碰真实系统）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) };

    /* ---- 滑块图库页 ---- */
    private readonly ListView _bgs = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly PictureBox _bgPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 34, 40) };
    private readonly Button _btnBgUpload = new() { Text = "上传背景…", AutoSize = true };
    private readonly Button _btnBgDelete = new() { Text = "删除选中", AutoSize = true };
    private readonly Button _btnBgRefresh = new() { Text = "刷新", AutoSize = true };
    private readonly Button _btnBgPrev = new() { Text = "◀ 上一页", AutoSize = true };
    private readonly Button _btnBgNext = new() { Text = "下一页 ▶", AutoSize = true };
    private readonly Label _lblBgPage = new() { Text = "第 1 / 1 页 · 共 0 张", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) };
    private readonly Label _lblBgHint = new() { Text = "（PNG、300x150、≤512KB；滑块验证随机使用图库背景）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) };
    private int _bgPage = 1, _bgPages = 1;
    private Image? _bgImage;

    /* ---- 登录背景页 ---- */
    private readonly ComboBox _cmbLoginMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly NumericUpDown _numRipple = new() { Minimum = 0, Maximum = 255, Width = 72, Value = 47 };
    private readonly NumericUpDown _numG = new() { Minimum = 0, Maximum = 255, Width = 72, Value = 111 };
    private readonly NumericUpDown _numB = new() { Minimum = 0, Maximum = 255, Width = 72, Value = 237 };
    private readonly CheckBox _chkUseImage = new() { Text = "使用背景图（覆盖波纹/纯色）", AutoSize = true };
    private readonly ListBox _lstLoginImages = new() { Width = 220, Height = 120 };
    private readonly PictureBox _loginPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 34, 40) };
    private readonly Button _btnLoginApply = new() { Text = "应用并保存", AutoSize = true };
    private readonly Button _btnLoginUpload = new() { Text = "上传背景图…", AutoSize = true };
    private readonly Button _btnLoginDeleteImg = new() { Text = "删除选中图", AutoSize = true };
    private readonly Button _btnLoginRefresh = new() { Text = "刷新", AutoSize = true };
    private readonly Label _lblLoginHint = new() { Text = "波纹为默认动画背景（登录页蓝色动波纹，暗色自动浅蓝）；纯色/图片可替换。", AutoSize = true, ForeColor = Color.DimGray };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _msg = new() { Text = "就绪" };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly NotifyIcon _tray = new();
    private bool _running;
    private int _pid;
    private bool _exiting;
    private bool _trayHintShown;

    public MainForm(bool startHidden = false)
    {
        _startHidden = startHidden;
        Text = "大面聊天 · 图形管理端";

        Icon = SafeExeIcon();

        // 自适应工作区（远程桌面等窄屏环境也放得下）
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Width = Math.Min(960, Math.Max(480, (int)(wa.Width * 0.96f)));
        Height = Math.Min(720, Math.Max(420, (int)(wa.Height * 0.94f)));
        MinimumSize = new Size(Math.Min(460, wa.Width), Math.Min(400, wa.Height));
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei", 9F);

        BuildLayout();
        BuildTray();

        _users.Columns.Add("账号", 140);
        _users.Columns.Add("昵称", 120);
        _users.Columns.Add("状态", 80);
        _users.Columns.Add("Id", 130);

        _online.Columns.Add("昵称", 180);
        _online.Columns.Add("用户 ID", 160);
        _online.Columns.Add("最后活跃", 170);

        _cmbLogType.Items.AddRange(new object[] { "控制台输出", "auth", "requests", "reports" });
        _cmbLogType.SelectedIndex = 0;

        _btnStart.Click += (_, _) => Run(() => { StartServer(); RefreshStatus(); });
        _btnStop.Click += (_, _) => Run(() => { StopServer(); RefreshStatus(); });
        _btnRestart.Click += (_, _) => Run(() => { StopServer(); StartServer(); RefreshStatus(); });

        _btnOpenLogs.Click += (_, _) => OpenLogs();
        _btnOpenWeb.Click += (_, _) => OpenWeb();
        _btnPortSave.Click += (_, _) => Run(SavePort);
        _chkAutoRun.CheckedChanged += (_, _) => Run(SaveAutoRun);
        _chkAutoStartServer.CheckedChanged += (_, _) => Run(() => { _cfg.AutoStartServer = _chkAutoStartServer.Checked; _cfg.Save(); });
        _btnSecRead.Click += (_, _) => RunAsync(async () => { await LoadSecurityAsync(); _msg.Text = "安全设置已读取"; });
        _btnSecSave.Click += (_, _) => RunAsync(async () => { await SaveSecurityAsync(); _msg.Text = "安全设置已保存"; });
        _btnLimitsRead.Click += (_, _) => RunAsync(async () => { await LoadLimitsAsync(); _msg.Text = "限制配置已读取"; });
        _btnLimitsSave.Click += (_, _) => RunAsync(async () => { await SaveLimitsAsync(); _msg.Text = "限制配置已保存（即时生效）"; });
        _btnCleanupFiles.Click += (_, _) => RunAsync(CleanupFilesAsync);
        _btnCleanupData.Click += (_, _) => RunAsync(CleanupDataAsync);
        _btnUsersRefresh.Click += (_, _) => RunAsync(async () => { await LoadUsersAsync(); _msg.Text = "用户列表已刷新"; });
        _btnBanToggle.Click += (_, _) => RunAsync(ToggleBanAsync);
        _btnResetPwd.Click += (_, _) => RunAsync(ResetPasswordAsync);
        _btnCreateUser.Click += (_, _) => RunAsync(CreateUserAsync);
        _btnDeleteUser.Click += (_, _) => RunAsync(DeleteUserAsync);
        _cmbLogType.SelectedIndexChanged += (_, _) => { if (!_logBusy) _logBusy = true; RunAsync(async () => { try { await LoadLogsAsync(); } finally { _logBusy = false; } }); };
        _btnLogRefresh.Click += (_, _) => { if (!_logBusy) _logBusy = true; RunAsync(async () => { try { await LoadLogsAsync(); } finally { _logBusy = false; } }); };
        _btnOnlineRefresh.Click += (_, _) => RunAsync(LoadOnlineAsync);
        _btnFbRefresh.Click += (_, _) => RunAsync(LoadFeedbackAsync);
        _btnFbCopy.Click += (_, _) => Run(CopySelectedFeedback);
        _btnFbCopyAll.Click += (_, _) => Run(CopyAllFeedback);
        _btnFbDelete.Click += (_, _) => RunAsync(DeleteSelectedFeedbackAsync);
        _btnHoneyRefresh.Click += (_, _) => RunAsync(async () => { await LoadHoneyAsync(); _msg.Text = "蜜罐已刷新"; });
        _btnHoneySave.Click += (_, _) => RunAsync(async () => { await SaveHoneyAsync(); _msg.Text = "蜜罐开关已保存（即时生效）"; });
        _btnHoneyCopy.Click += (_, _) => Run(CopySelectedHoney);
        _btnHoneyCopyAll.Click += (_, _) => Run(CopyAllHoney);
        _btnBgUpload.Click += (_, _) => RunAsync(SliderBgUploadAsync);
        _btnBgDelete.Click += (_, _) => RunAsync(SliderBgDeleteAsync);
        _btnBgRefresh.Click += (_, _) => RunAsync(async () => { await LoadSliderBgsAsync(); _msg.Text = "图库已刷新"; });
        _btnBgPrev.Click += (_, _) => RunAsync(async () => { if (_bgPage > 1) { _bgPage--; await LoadSliderBgsAsync(); } });
        _btnBgNext.Click += (_, _) => RunAsync(async () => { if (_bgPage < _bgPages) { _bgPage++; await LoadSliderBgsAsync(); } });
        _bgs.SelectedIndexChanged += (_, _) => RunAsync(SliderBgPreviewAsync);

        // 登录背景页
        _btnLoginApply.Click += (_, _) => RunAsync(async () => { await SaveLoginBgAsync(); _msg.Text = "登录背景已保存，登录页下次渲染生效"; });
        _btnLoginRefresh.Click += (_, _) => RunAsync(async () => { await LoadLoginBgAsync(); _msg.Text = "登录背景已刷新"; });
        _btnLoginUpload.Click += (_, _) => RunAsync(async () => { await UploadLoginImgAsync(); });
        _btnLoginDeleteImg.Click += (_, _) => RunAsync(async () => { await DeleteLoginImgAsync(); });
        _chkUseImage.CheckedChanged += (_, _) => _loginPreview.Invalidate();
        _cmbLoginMode.SelectedIndexChanged += (_, _) => _loginPreview.Invalidate();
        _numRipple.ValueChanged += (_, _) => _loginPreview.Invalidate();
        _numG.ValueChanged += (_, _) => _loginPreview.Invalidate();
        _numB.ValueChanged += (_, _) => _loginPreview.Invalidate();
        _lstLoginImages.SelectedIndexChanged += async (_, _) =>
        {
            if (_lstLoginImages.SelectedItem is string name && name.Length > 0)
            {
                _chkUseImage.Checked = true;
                try
                {
                    using var req = ApiRequest(HttpMethod.Get, $"login-background/images/{Uri.EscapeDataString(name)}");
                    using var resp = await G.Http.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        _loginPreview.Image = Image.FromStream(new MemoryStream(bytes));
                    }
                }
                catch { /* 预览失败忽略 */ }
            }
        };

        _timer.Tick += (_, _) =>
        {
            RefreshStatus();
            // 日志页自动刷新：服务运行中 + 日志 tab 处于活动页 + 无手动刷新进行中
            if (_running && !_logBusy
                && _logTab != null && _tabs != null
                && ReferenceEquals(_tabs.SelectedTab, _logTab))
            {
                _logBusy = true;
                try
                {
                    LoadLogsAsync().ContinueWith(_ => _logBusy = false, TaskScheduler.Default);
                }
                catch { _logBusy = false; }
            }
        };
        _timer.Start();

        Load += (_, _) =>
        {
            RefreshStatus();
            _nudPort.Value = _cfg.Port;
            _suspendAutoRun = true;
            _chkAutoStartServer.Checked = _cfg.AutoStartServer;
            _chkAutoRun.Checked = QueryAutoRun();
            _suspendAutoRun = false;
            RunAsync(async () =>
            {
                await LoadSecurityAsync();
                if (_running)
                {
                    await LoadUsersAsync();
                    await LoadLogsAsync();
                }
            });
            if (_startHidden)
            {
                BeginInvoke(HideToTray);
            }
            if (_cfg.AutoStartServer && !_running)
            {
                Run(() => { StartServer(); RefreshStatus(); });
            }
        };

        // 关闭按钮 = 最小化到托盘后台运行；托盘菜单「退出」才真正退出
        FormClosing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideToTray();
        };
    }

    private static Icon SafeExeIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    /* ---------------- 布局：标签页（窄屏自适应） ---------------- */

    private void BuildLayout()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(10, 4) };
        var tabs = _tabs;

        /* 概览 */
        var overview = new TabPage("概览");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        top.Controls.AddRange(new Control[] { _lblStatus, _lblDetail });

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 0), WrapContents = true };
        actions.Controls.AddRange(new Control[] { _btnStart, _btnStop, _btnRestart, _btnOpenWeb, _btnOpenLogs });

        var portBox = new GroupBox { Text = "配置（保存到 wxadmin.json，永久生效；端口需重启服务后生效）", Dock = DockStyle.Top, Height = 64, Padding = new Padding(8, 6, 8, 4) };
        var portRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        portRow.Controls.AddRange(new Control[] { new Label { Text = "端口：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudPort, _btnPortSave });
        portBox.Controls.Add(portRow);

        var autoBox = new GroupBox { Text = "自启动", Dock = DockStyle.Top, Height = 96, Padding = new Padding(8) };
        var autoCol = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        autoCol.Controls.AddRange(new Control[] { _chkAutoRun, _chkAutoStartServer });
        autoBox.Controls.Add(autoCol);

        var autoHint = new Label
        {
            Text = "说明：开机自启动会在当前用户的启动项注册本程序（带 --tray 参数，静默进托盘）；\r\n停止后端 = 只停服务进程，管理端继续在托盘运行。",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(8, 4, 8, 0),
        };

        var onlineTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };
        onlineTop.Controls.Add(_btnOnlineRefresh);
        onlineTop.Controls.Add(new Label { Text = "（服务运行中每 3 秒自动刷新）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) });
        _onlineBox.Controls.Add(_online);
        _onlineBox.Controls.Add(onlineTop);

        overview.Controls.Add(_onlineBox);
        overview.Controls.Add(autoHint);
        overview.Controls.Add(autoBox);
        overview.Controls.Add(portBox);
        overview.Controls.Add(actions);
        overview.Controls.Add(top);

        /* 安全 */
        var security = new TabPage("安全设置");
        var secBox = new GroupBox { Text = "安全（运行中走管理 API 即时生效；停止时写 security.json）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var secLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0) };

        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row1.Controls.AddRange(new Control[]
        {
            new Label { Text = "注册密码：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _txtRegKey,
            new Label { Text = "（留空 = 关闭注册）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(6, 6, 16, 0) },
            _chkCaptcha,
        });
        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row2.Controls.AddRange(new Control[]
        {
            new Label { Text = "登录失败：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudAttempts,
            new Label { Text = "次 → 锁定", AutoSize = true, Padding = new Padding(4, 6, 0, 0) }, _nudLockMin,
            new Label { Text = "分钟", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
        });
        var row3 = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row3.Controls.AddRange(new Control[]
        {
            new Label { Text = "登录接口限流：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudPerMin,
            new Label { Text = "次/分钟/IP", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "全站 API 限流：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudApiPerMin,
            new Label { Text = "次/分钟/IP", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var row3b = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row3b.Controls.AddRange(new Control[]
        {
            new Label { Text = "防火墙·每IP请求：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudFwReq,
            new Label { Text = "次/分钟（下限100）", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "并发连接：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudFwConc,
            new Label { Text = "个", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var row3c = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row3c.Controls.AddRange(new Control[]
        {
            new Label { Text = "下行流量：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudFwMb,
            new Label { Text = "MB/分钟", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "触发封禁：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudFwBanHit,
            new Label { Text = "次429/分钟 → 封", AutoSize = true, Padding = new Padding(4, 6, 0, 0) }, _nudFwBanMin,
            new Label { Text = "分钟", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var row3d = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row3d.Controls.AddRange(new Control[]
        {
            new Label { Text = "每用户发消息：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudFloodMsgs,
            new Label { Text = "条/分钟（身份维度，防偷 token 重放洪水）", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var row4 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
        row4.Controls.AddRange(new Control[] { _btnSecRead, _btnSecSave, new Label { Width = 12 }, _lblSecHint });

        // 文件与群限制
        var limBox = new GroupBox { Text = "文件与群限制（保存后即时生效）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
        var limCol = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        var limRow1 = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        limRow1.Controls.AddRange(new Control[] {
            new Label { Text = "单文件上限：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudMaxFileMb,
            new Label { Text = "MB", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "上传限速：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudSpeedMb,
            new Label { Text = "MB/s", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "文件保留：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudKeepDays,
            new Label { Text = "天", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var limRow2 = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        limRow2.Controls.AddRange(new Control[] {
            new Label { Text = "单群人数：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudGroupMembers,
            new Label { Text = "人", AutoSize = true, Padding = new Padding(4, 6, 16, 0) },
            new Label { Text = "群管理员：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _nudGroupAdmins,
            new Label { Text = "人（不含群主）", AutoSize = true, Padding = new Padding(4, 6, 0, 0) },
        });
        var limRow3 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        limRow3.Controls.AddRange(new Control[] { _btnLimitsRead, _btnLimitsSave, _btnCleanupFiles, _btnCleanupData, new Label { Width = 8 }, _lblLimitsHint });

        limCol.Controls.AddRange(new Control[] { limRow1, limRow2, limRow3 });
        limBox.Controls.Add(limCol);

        secLayout.Controls.AddRange(new Control[] { row1, row2, row3, row3b, row3c, row3d, row4, limBox });
        secBox.Controls.Add(secLayout);
        security.Controls.Add(secBox);

        /* 用户 */
        var usersTab = new TabPage("用户管理");
        var userTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        userTop.Controls.AddRange(new Control[] { _btnCreateUser, _btnUsersRefresh, _btnBanToggle, _btnResetPwd, _btnDeleteUser });
        _users.Dock = DockStyle.Fill;
        usersTab.Controls.Add(_users);
        usersTab.Controls.Add(userTop);

        /* 日志 */
        _logTab = new TabPage("日志 / 控制台");
        var logTab = _logTab;
        var logTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        logTop.Controls.AddRange(new Control[]
        {
            new Label { Text = "来源：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _cmbLogType,
            _btnLogRefresh,
            new Label { Text = "（控制台输出即使服务已停止也可查看）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) },
        });
        logTab.Controls.Add(_txtLog);
        logTab.Controls.Add(logTop);

        /* 用户反馈 */
        var fbTab = new TabPage("用户反馈");
        _feedback.Columns.Add("时间", 150);
        _feedback.Columns.Add("用户", 150);
        _feedback.Columns.Add("反馈内容", 320);
        var fbTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        fbTop.Controls.AddRange(new Control[]
        {
            _btnFbRefresh,
            _btnFbCopy,
            _btnFbCopyAll,
            _btnFbDelete,
            _lblFbHint,
        });
        fbTab.Controls.Add(_feedback);
        fbTab.Controls.Add(fbTop);

        /* 蜜罐（开关 + 事件日志 + 金丝雀） */
        var honeyTab = new TabPage("蜜罐");
        _honeyEvents.Columns.Add("时间", 150);
        _honeyEvents.Columns.Add("来源IP", 130);
        _honeyEvents.Columns.Add("方法", 44);
        _honeyEvents.Columns.Add("路径", 150);
        _honeyEvents.Columns.Add("诱饵", 64);
        _honeyEvents.Columns.Add("延迟ms", 64);
        _honeyEvents.Columns.Add("金丝雀", 56);
        _honeyEvents.Columns.Add("动作", 56);
        _honeyCanaries.Columns.Add("类型", 96);
        _honeyCanaries.Columns.Add("值", 190);
        _honeyCanaries.Columns.Add("种在哪", 150);
        _honeyCanaries.Columns.Add("创建时间", 150);
        var honeyTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        honeyTop.Controls.AddRange(new Control[]
        {
            _chkHoneyEnabled,
            _chkHoneyTarpit,
            _btnHoneySave,
            _btnHoneyRefresh,
            _btnHoneyCopy,
            _btnHoneyCopyAll,
            _lblHoneyHint,
        });
        var honeyCanaryBox = new GroupBox { Text = "金丝雀（honeytoken，可溯源）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        honeyCanaryBox.Controls.Add(_honeyCanaries);
        var honeyCanaryPanel = new Panel { Dock = DockStyle.Right, Width = 620, Padding = new Padding(4) };
        honeyCanaryPanel.Controls.Add(honeyCanaryBox);
        honeyTab.Controls.Add(_honeyEvents);
        honeyTab.Controls.Add(honeyCanaryPanel);
        honeyTab.Controls.Add(honeyTop);

        /* 滑块图库 */
        var bgsTab = new TabPage("滑块图库");
        _bgs.Columns.Add("文件名", 190);
        _bgs.Columns.Add("大小", 80);
        _bgs.Columns.Add("修改时间", 150);
        var bgTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        bgTop.Controls.AddRange(new Control[] { _btnBgUpload, _btnBgDelete, _btnBgRefresh, _btnBgPrev, _btnBgNext, _lblBgPage, _lblBgHint });
        var bgPreviewBox = new GroupBox { Text = "预览（选中左侧列表项）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        bgPreviewBox.Controls.Add(_bgPreview);
        var bgPreviewPanel = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(4) };
        bgPreviewPanel.Controls.Add(bgPreviewBox);
        bgsTab.Controls.Add(_bgs);
        bgsTab.Controls.Add(bgPreviewPanel);
        bgsTab.Controls.Add(bgTop);

        /* 登录背景 */
        var loginTab = new TabPage("登录背景");
        _cmbLoginMode.Items.AddRange(new object[] { "波纹（默认·动）", "纯色", "图片" });
        _cmbLoginMode.SelectedIndex = 0;
        var loginTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        loginTop.Controls.AddRange(new Control[]
        {
            new Label { Text = "模式：", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _cmbLoginMode,
            new Label { Text = " 主色 RGB：", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            _numRipple, _numG, _numB,
            _chkUseImage,
            _btnLoginApply,
            _btnLoginRefresh,
            _lblLoginHint,
        });
        var loginImgPanel = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8) };
        loginImgPanel.Controls.Add(new Label { Text = "背景图库（选中 = 用作图片模式背景）", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 4) });
        loginImgPanel.Controls.Add(_lstLoginImages);
        var imgBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        imgBtns.Controls.AddRange(new Control[] { _btnLoginUpload, _btnLoginDeleteImg });
        loginImgPanel.Controls.Add(imgBtns);
        var loginPreviewBox = new GroupBox { Text = "预览（波纹效果见登录页；图片/纯色显示于此）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        loginPreviewBox.Controls.Add(_loginPreview);
        loginTab.Controls.Add(loginPreviewBox);
        loginTab.Controls.Add(loginImgPanel);
        loginTab.Controls.Add(loginTop);
        // 预览默认挂一个波纹的近似静态渐变（真实动效在登录页前端画）
        _loginPreview.Paint += (_, e) =>
        {
            if (_chkUseImage.Checked) return;
            var c = Color.FromArgb((int)_numRipple.Value, (int)_numG.Value, (int)_numB.Value);
            var c2 = Color.FromArgb(255, (int)_numRipple.Value, (int)_numG.Value, (int)_numB.Value);
            using var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, _loginPreview.Width, _loginPreview.Height), c, c2, 45f);
            e.Graphics.FillRectangle(br, 0, 0, _loginPreview.Width, _loginPreview.Height);
        };

        tabs.TabPages.AddRange(new[] { overview, security, usersTab, fbTab, honeyTab, bgsTab, loginTab, logTab });

        _status.Items.Add(_msg);

        Controls.Add(tabs);
        Controls.Add(_status);
    }

    /* ---------------- 托盘 ---------------- */

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (_, _) => RestoreFromTray());
        menu.Items.Add("启动服务", null, (_, _) => Run(() => { StartServer(); RefreshStatus(); }));
        menu.Items.Add("停止后端", null, (_, _) => Run(() => { StopServer(); RefreshStatus(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出软件", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.Icon = Icon ?? SystemIcons.Application;
        _tray.Text = "大面聊天管理端（右键操作）";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(2500, "大面聊天管理端", "窗口已最小化到托盘后台运行，双击托盘图标打开。", ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void ExitApp()
    {
        _exiting = true;
        _tray.Visible = false;
        _tray.Dispose();
        Close();
    }

    /* ---------------- 开机自启（HKCU Run） ---------------- */

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "大面聊天管理端";
    private bool _suspendAutoRun;

    private static bool QueryAutoRun()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(RunValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    private void SaveAutoRun()
    {
        if (_suspendAutoRun) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) throw new InvalidOperationException("无法写注册表");
            if (_chkAutoRun.Checked)
            {
                var exe = Application.ExecutablePath;
                key.SetValue(RunValueName, $"\"{exe}\" --tray");
                _msg.Text = "已开启开机自启动。";
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                _msg.Text = "已关闭开机自启动。";
            }
            _cfg.AutoRun = _chkAutoRun.Checked;
            _cfg.Save();
        }
        catch (Exception ex)
        {
            _msg.Text = "自启动设置失败：" + ex.Message;
            _suspendAutoRun = true;
            _chkAutoRun.Checked = QueryAutoRun();
            _suspendAutoRun = false;
        }
    }

    /* ---------------- 服务启停 ---------------- */

    private static bool PortInUse(int port)
    {
        /*
         * 用系统真实监听表判断，不能用「127.0.0.1 试绑定」：
         * 服务绑定 0.0.0.0（通配）时，Windows 允许对 127.0.0.1 的试绑定照样成功，
         * 探测得假阴性 → 端口明明被占却判定空闲 → 启动必然 AddressInUse 整站崩溃。
         */
        foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties
                     .GetIPGlobalProperties().GetActiveTcpListeners())
        {
            if (ep.Port == port) return true;
        }
        return false;
    }

    private readonly object _consoleLock = new();

    private void AppendConsole(string? line)
    {
        if (line == null) return;
        try
        {
            lock (_consoleLock)
            {
                Directory.CreateDirectory(G.LogDir);
                RotateIfLarge(G.ConsoleLogFile, 10L * 1024 * 1024);
                File.AppendAllText(G.ConsoleLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* 磁盘忙时丢一行不影响运行 */ }
    }

    /// <summary>
    /// 日志文件超过 <paramref name="maxBytes"/> 时轮转：保留最近一份 .1，
    /// 当前文件重命名为 .1（覆盖旧 .1），下次追加写到新文件。
    /// 轮转失败静默忽略，不能影响主流程。
    /// </summary>
    private static void RotateIfLarge(string path, long maxBytes)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length <= maxBytes) return;
        var archive = path + ".1";
        try
        {
            if (File.Exists(archive)) File.Delete(archive);
            File.Move(path, archive);
        }
        catch { /* 轮转失败不阻断 */ }
    }

    private void StartServer()
    {
        if (!File.Exists(G.WebDll))
        {
            _msg.Text = "未找到已编译服务，正在 dotnet build …";
            Application.DoEvents();
            var build = Process.Start(new ProcessStartInfo("dotnet", $"build \"{G.WebProject}\" -v q")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            build.WaitForExit();
            if (build.ExitCode != 0 || !File.Exists(G.WebDll))
            {
                _msg.Text = "编译失败，无法启动。";
                return;
            }
        }
        if (PortInUse(_cfg.Port))
        {
            _msg.Text = $"端口 {_cfg.Port} 被占用。";
            return;
        }
        if (File.Exists(Path.Combine(G.WebProject, "Data", "certs", "selfsigned.pfx")) && PortInUse(_cfg.Port + 1))
        {
            _msg.Text = $"管理端口 {_cfg.Port + 1} 被占用，无法启动（TLS 模式下该端口为 GUI 管理专用环回口）。";
            return;
        }

        // TLS 模式（证书存在）：公网主端口与 443 全部提供 HTTPS；--urls 只绑本机环回管理明文口（GUI 管理专用、不对外暴露）
        var tls = File.Exists(Path.Combine(G.WebProject, "Data", "certs", "selfsigned.pfx"));
        var urlsArg = tls ? $"http://127.0.0.1:{_cfg.Port + 1}" : $"http://0.0.0.0:{_cfg.Port}";
        AppendConsole($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===== 启动服务 " +
                      (tls
                          ? $"https://0.0.0.0:{_cfg.Port} + https://0.0.0.0:443（管理环回 http://127.0.0.1:{_cfg.Port + 1}）"
                          : $"http://0.0.0.0:{_cfg.Port}（监听所有网卡，局域网用本机 IP 访问）") +
                      " =====");

        var psi = new ProcessStartInfo("dotnet", $"exec \"{G.WebDll}\" --urls {urlsArg}")
        {
            WorkingDirectory = G.WebProject,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ApplyHttpsEnv(psi);
        var proc = Process.Start(psi)!;

        // 控制台输出（等价 dotnet 控制台）→ logs/console.log，GUI 日志页直读
        proc.OutputDataReceived += (_, e) => AppendConsole(e.Data);
        proc.ErrorDataReceived += (_, e) => AppendConsole(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        File.WriteAllText(G.PidFile, proc.Id.ToString());
        Thread.Sleep(1200);
        if (proc.HasExited)
        {
            _msg.Text = "服务启动失败，请查看「日志/控制台」页。";
            return;
        }
        _msg.Text = $"服务已启动：{(tls ? "https" : "http")}://0.0.0.0:{_cfg.Port}（PID {proc.Id}）";
    }

    /// <summary>
    /// DEP-SEC HTTPS：证书存在即注入 WX_HTTPS_* 环境变量，主端口与 443 同时提供 TLS。
    /// 某个端口被占（如 IIS）由服务端逐端口跳过并告警，绝不因绑定失败整站起不来。
    /// </summary>
    private void ApplyHttpsEnv(ProcessStartInfo psi)
    {
        var cert = Path.Combine(G.WebProject, "Data", "certs", "selfsigned.pfx");
        if (!File.Exists(cert)) return;
        var tlsPorts = _cfg.Port == 443 ? "443" : $"{_cfg.Port},443";
        psi.EnvironmentVariables["WX_HTTPS_CERT"] = cert;
        psi.EnvironmentVariables["WX_HTTPS_CERT_PASSWORD"] = "wxchat2026";
        psi.EnvironmentVariables["WX_HTTPS_PORT"] = tlsPorts;
        AppendConsole($"[HTTPS] 自签名证书已加载 → https://0.0.0.0:{tlsPorts}（对外全 TLS；管理 API 走本机环回 http://127.0.0.1:{_cfg.Port + 1}）");
    }

    private void StopServer()
    {
        // ① 按 pid 文件精确停（本 GUI 启动的服务）
        if (File.Exists(G.PidFile) && int.TryParse(File.ReadAllText(G.PidFile).Trim(), out var pid))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
                _msg.Text = $"服务已停止（PID {pid}）。";
            }
            catch (ArgumentException)
            {
                // pid 已失效（可能指向了别的外部和进程）：不直接 return，落到端口兜底
                _msg.Text = "PID 文件失效，改为按端口停止…";
            }
            File.Delete(G.PidFile);
        }

        // ② 兜底：端口仍在监听（外部/.bat 启动、pid 文件缺失或失效）→ 按端口杀进程
        if (PortInUse(_cfg.Port))
        {
            var stopped = KillListenerPort(_cfg.Port);
            _msg.Text = stopped
                ? "服务已停止（按端口兜底）。"
                : $"端口 {_cfg.Port} 仍在监听（需管理员权限或改用「停止.bat」）。";
            File.Delete(G.PidFile);
        }
        else
        {
            _msg.Text = "没有运行中的服务。";
        }
        AppendConsole($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===== 服务停止操作完成 =====");
    }

    /// <summary>按监听端口杀进程（兜底，供「外部启动」的服务也能停）。返回是否确实停掉。</summary>
    private static bool KillListenerPort(int port)
    {
        var pid = GetPidByPort(port);
        if (pid <= 0) return !PortInUse(port);
        try
        {
            var psi = new ProcessStartInfo("taskkill", $"/f /t /pid {pid}")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(3000);
        }
        catch { /* 已退出/权限问题：以下端口复检为准 */ }
        return !PortInUse(port);
    }

    /// <summary>取监听指定端口的进程 PID（用 netstat 解析，最稳）。找不到返回 0。</summary>
    private static int GetPidByPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", $"-ano")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                // 形如：TCP  0.0.0.0:30011  0.0.0.0:0  LISTENING  12345
                var idx = line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase);
                if (idx < 0 || !line.Contains($":{port}", StringComparison.Ordinal)) continue;
                var pidPart = line.Substring(idx).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (int.TryParse(pidPart, out var pid) && pid > 0) return pid;
            }
        }
        catch { /* 解析失败返回 0 */ }
        return 0;
    }

    /// <summary>保存全部配置：端口 / 开机自启动 / 启动时自动启动服务（写入 wxadmin.json，开机自启同步注册表）。</summary>
    private void SavePort()
    {
        _cfg.Port = (int)_nudPort.Value;
        _cfg.AutoStartServer = _chkAutoStartServer.Checked;
        _cfg.AutoRun = _chkAutoRun.Checked;
        _cfg.Save();
        SaveAutoRun(); // 同步 HKCU Run，并回写配置
        _msg.Text = $"配置已保存：{G.ConfigFile}"
            + (_running ? $"（端口 {_cfg.Port} 需「重启服务」后生效）" : "。");
        RefreshStatus();
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(G.LogDir);
        Process.Start(new ProcessStartInfo("explorer.exe", G.LogDir) { UseShellExecute = true });
    }

    private void OpenWeb()
    {
        // 前端已编译进后端 wwwroot，直接打开后端地址即可，无需另起前端
        Process.Start(new ProcessStartInfo($"{(TlsMode ? "https" : "http")}://localhost:{_cfg.Port}") { UseShellExecute = true });
    }

    /* ---------------- 在线用户 ---------------- */

    private async Task LoadOnlineAsync()
    {
        if (!_running) return;
        _onlineBusy = true;
        try
        {
            using var req = ApiRequest(HttpMethod.Get, "online");
            using var resp = await G.Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var users = doc.RootElement.GetProperty("users");
            var count = 0;
            _online.BeginUpdate();
            _online.Items.Clear();
            foreach (var u in users.EnumerateArray())
            {
                var item = new ListViewItem(u.GetProperty("nickname").GetString() ?? "");
                item.SubItems.Add(u.GetProperty("userId").GetString() ?? "");
                var la = u.GetProperty("lastActive").GetString();
                item.SubItems.Add(FormatActive(la));
                _online.Items.Add(item);
                count++;
            }
            _online.EndUpdate();
            _onlineBox.Text = $"在线用户（{count}）";
        }
        catch { /* 服务刚停/网络抖动：保留旧列表 */ }
        finally
        {
            _onlineBusy = false;
        }
    }

    private static string FormatActive(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return "-";
        return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime().ToString("MM-dd HH:mm:ss")
            : iso;
    }

    /// <summary>ISO 时间戳 → 本地可读（含年月，跨天日志用）；解析失败原样返回。</summary>
    private static string FormatIso(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return "-";
        return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : iso;
    }

    /* ---------------- 状态轮询 ---------------- */

    private void RefreshStatus()
    {
        _pid = 0;
        if (File.Exists(G.PidFile) && int.TryParse(File.ReadAllText(G.PidFile).Trim(), out var pid))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                _pid = p.Id;
            }
            catch (ArgumentException) { /* 已退出 */ }
        }
        // ★ 运行态以「端口是否在监听」为准，不依赖本 GUI 写的 pid 文件：
        //   服务若由 .bat / 上一次 GUI / 其它方式拉起（pid 文件缺失或对不上），
        //   旧逻辑 _pid==0 → _running=false → 用户/图库/在线/限制/日志全部拒加载（"刷新没用、很多是空的"）。
        //   读管理 API 只需要"服务可达"，端口在监听即可确认；pid 仅用于精确停止。
        var listening = PortInUse(_cfg.Port);
        _running = listening;

        _lblStatus.Text = listening ? "状态：运行中" : "状态：未运行";
        _lblStatus.ForeColor = listening ? Color.FromArgb(7, 193, 96) : Color.Gray;
        _lblDetail.Text = $"端口 {_cfg.Port}（{(listening ? "已监听" : "空闲")}） · PID {(_pid > 0 ? _pid.ToString() : (listening ? "未知（外部启动）" : "-"))} · {(TlsMode ? "https" : "http")}://0.0.0.0:{_cfg.Port}（前端已内置）";
        _btnStart.Enabled = !listening;
        // 停止/重启：服务在跑就能尝试（pid 文件缺失时按端口兜底 kill），不再因 pid 未知而灰掉
        _btnStop.Enabled = listening;
        _btnRestart.Enabled = listening;
        _tray.Text = $"大面聊天管理端 · {(listening ? "运行中" : "未运行")}";
        if (listening && !_onlineBusy) RunAsync(LoadOnlineAsync);
    }

    /* ---------------- 管理 API（X-Admin-Key 头） ---------------- */

    // TLS 模式下管理 API 在环回明文口（主端口+1）；无证书的旧模式在主端口本身
    private static bool TlsMode => File.Exists(Path.Combine(G.WebProject, "Data", "certs", "selfsigned.pfx"));

    private string Api(string path) => $"http://127.0.0.1:{(TlsMode ? _cfg.Port + 1 : _cfg.Port)}/api/admin/{path}";

    /// <summary>
    /// 管理鉴权：本机程序直接读取服务端生成的密钥文件（data/admin.key），无需用户输入任何密码。
    /// 密钥文件只在本机，且后端管理接口仅接受来自 loopback 的请求。
    /// </summary>
    private string LocalAdminKey () => AdminKeyFile.Read() ?? "";

    private HttpRequestMessage ApiRequest(HttpMethod method, string path, string? json = null)
    {
        var req = new HttpRequestMessage(method, Api(path));
        var key = LocalAdminKey();
        if (!string.IsNullOrEmpty(key)) req.Headers.Add("X-Admin-Key", key);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    private async Task<T?> GetJsonAsync<T>(string path)
    {
        using var req = ApiRequest(HttpMethod.Get, path);
        using var resp = await G.Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        // 管理 API 返回 camelCase，DTO 是 PascalCase：必须大小写不敏感，否则全读成空/默认
        return JsonSerializer.Deserialize<T>(json, G.ApiJsonOpts);
    }

    /* ---------------- 滑块图库管理 ---------------- */

    private sealed class BgListDto
    {
        public int total { get; set; }
        public int pages { get; set; }
        public int page { get; set; }
        public List<BgItemDto> items { get; set; } = new();
    }

    private sealed class BgItemDto
    {
        public string name { get; set; } = "";
        public int sizeKb { get; set; }
        public DateTime modifiedAt { get; set; }
    }

    private async Task LoadSliderBgsAsync()
    {
        BgListDto? dto;
        try { dto = await GetJsonAsync<BgListDto>($"slider/backgrounds?page={_bgPage}&pageSize=10"); }
        catch { _msg.Text = "图库加载失败，请先启动服务"; return; }
        if (dto == null) return;
        _bgPage = Math.Max(1, dto.page);
        _bgPages = Math.Max(1, dto.pages);
        _lblBgPage.Text = $"第 {_bgPage} / {_bgPages} 页 · 共 {dto.total} 张";
        _bgs.Items.Clear();
        foreach (var i in dto.items)
        {
            var it = new ListViewItem(i.name);
            it.SubItems.Add($"{i.sizeKb} KB");
            it.SubItems.Add(i.modifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            it.Tag = i.name;
            _bgs.Items.Add(it);
        }
    }

    private async Task SliderBgPreviewAsync()
    {
        if (_bgs.SelectedItems.Count == 0) return;
        var name = _bgs.SelectedItems[0].Tag as string ?? "";
        if (name.Length == 0) return;
        try
        {
            using var req = ApiRequest(HttpMethod.Get, $"slider/backgrounds/{Uri.EscapeDataString(name)}/preview");
            using var resp = await G.Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var old = _bgImage;
            _bgImage = Image.FromStream(new MemoryStream(bytes));
            _bgPreview.Image = _bgImage;
            old?.Dispose();
        }
        catch { /* 预览失败忽略 */ }
    }

    private async Task SliderBgUploadAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择滑块背景图（PNG，300x150）",
            Filter = "PNG 图片|*.png",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var bytes = await File.ReadAllBytesAsync(dlg.FileName);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(dlg.FileName));
        using var req = ApiRequest(HttpMethod.Post, "slider/backgrounds");
        req.Content = content;
        using var resp = await G.Http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
        {
            _msg.Text = "背景已上传，滑块验证即刻随机使用";
            await LoadSliderBgsAsync();
        }
        else
        {
            var body = await resp.Content.ReadAsStringAsync();
            _msg.Text = ExtractApiError(body) ?? $"上传失败（{(int)resp.StatusCode}）";
        }
    }

    private async Task SliderBgDeleteAsync()
    {
        if (_bgs.SelectedItems.Count == 0) { _msg.Text = "请先选中要删除的背景"; return; }
        var name = (_bgs.SelectedItems[0].Tag as string) ?? "";
        if (name.Length == 0) { _msg.Text = "无效的背景名"; return; }
        if (MessageBox.Show($"确定删除背景 {name}？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        using var req = ApiRequest(HttpMethod.Delete, $"slider/backgrounds/{Uri.EscapeDataString(name)}");
        using var resp = await G.Http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
        {
            _msg.Text = "已删除";
            await LoadSliderBgsAsync();
        }
        else
        {
            _msg.Text = "删除失败，请先启动服务";
        }
    }

    /* ---------------- 登录背景管理 ---------------- */

    private sealed class LoginBgDto
    {
        public string mode { get; set; } = "ripple";
        public string rippleColor { get; set; } = "#2F6FED";
        public string color { get; set; } = "#2F6FED";
        public string image { get; set; } = "";
    }

    private sealed class LoginBgImagesDto
    {
        public int total { get; set; }
        public List<LoginBgImgDto> items { get; set; } = new();
    }

    private sealed class LoginBgImgDto
    {
        public string name { get; set; } = "";
        public int sizeKb { get; set; }
    }

    private sealed class LoginBgSaveDto
    {
        public string? mode { get; set; }
        public string? rippleColor { get; set; }
        public string? color { get; set; }
        public string? image { get; set; }
    }

    private static string HexColor (int r, int g, int b) =>
        string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);

    /// <summary>解析 #RRGGBB（大小写均可），成功返回对应 Color 与 true。</summary>
    private static bool TryParseHex (string? hex, out Color c)
    {
        c = Color.Empty;
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#') return false;
        var s = hex.Substring(1);
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        c = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return true;
    }

    private async Task LoadLoginBgAsync ()
    {
        try
        {
            var dto = await GetJsonAsync<LoginBgDto>("login-background");
            if (dto == null) return;
            _cmbLoginMode.SelectedIndex = dto.mode switch
            {
                "color" => 1,
                "image" => 2,
                _ => 0,
            };
            if (TryParseHex(dto.rippleColor, out var c))
            {
                _numRipple.Value = c.R; _numG.Value = c.G; _numB.Value = c.B;
            }
            _chkUseImage.Checked = dto.mode == "image" && !string.IsNullOrEmpty(dto.image);
            await LoadLoginImagesAsync();
        }
        catch { _msg.Text = "登录背景读取失败，请先启动服务"; }
    }

    private async Task LoadLoginImagesAsync ()
    {
        try
        {
            var dto = await GetJsonAsync<LoginBgImagesDto>("login-background/images");
            _lstLoginImages.Items.Clear();
            if (dto != null)
                foreach (var i in dto.items)
                    _lstLoginImages.Items.Add($"{i.name}（{i.sizeKb}KB）");
        }
        catch { /* 图片列表加载失败不影响配置 */ }
    }

    private async Task SaveLoginBgAsync ()
    {
        var hex = HexColor((int)_numRipple.Value, (int)_numG.Value, (int)_numB.Value);
        var mode = _chkUseImage.Checked ? "image" : (_cmbLoginMode.SelectedIndex == 1 ? "color" : "ripple");
        // 图片模式的文件名来自后台列表（选中项格式 "name（xxKB）"，取「（」之前的原文），绝不本地构造
        var imgName = "";
        if (mode == "image" && _lstLoginImages.SelectedItem is string sel)
        {
            var idx = sel.IndexOf('（');
            imgName = idx > 0 ? sel.Substring(0, idx).Trim() : sel.Trim();
        }
        var body = JsonSerializer.Serialize(new LoginBgSaveDto
        {
            mode = mode,
            rippleColor = hex,
            color = hex,
            image = imgName,
        });
        using var req = ApiRequest(HttpMethod.Put, "login-background", body);
        using var resp = await G.Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var b = await resp.Content.ReadAsStringAsync();
            _msg.Text = ExtractApiError(b) ?? $"保存失败（{(int)resp.StatusCode}）";
            return;
        }
        await LoadLoginBgAsync();
    }

    private async Task UploadLoginImgAsync ()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择登录背景图（PNG/JPG/WEBP，≤1MB）",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.webp",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var bytes = await File.ReadAllBytesAsync(dlg.FileName);
        using var content = new MultipartFormDataContent();
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            });
        content.Add(fc, "file", System.IO.Path.GetFileName(dlg.FileName));
        using var req = ApiRequest(HttpMethod.Post, "login-background/images");
        req.Content = content;
        using var resp = await G.Http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
        {
            _msg.Text = "登录背景图已上传，请在左侧选中后保存";
            await LoadLoginImagesAsync();
        }
        else
        {
            var b = await resp.Content.ReadAsStringAsync();
            _msg.Text = ExtractApiError(b) ?? $"上传失败（{(int)resp.StatusCode}）";
        }
    }

    private async Task DeleteLoginImgAsync ()
    {
        if (_lstLoginImages.SelectedIndex < 0)
        {
            _msg.Text = "请先选中要删除的背景图";
            return;
        }
        var sel = _lstLoginImages.SelectedItem?.ToString() ?? "";
        var idx = sel.IndexOf('（');
        var name = (idx > 0 ? sel.Substring(0, idx) : sel).Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (MessageBox.Show($"确定删除登录背景图 {name}？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        using var req = ApiRequest(HttpMethod.Delete, $"login-background/images/{Uri.EscapeDataString(name)}");
        using var resp = await G.Http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
        {
            _msg.Text = "已删除";
            await LoadLoginImagesAsync();
        }
        else
        {
            _msg.Text = "删除失败，请先启动服务";
        }
    }

    private static string? ExtractApiError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return e.GetString();
        }
        catch { /* 非 JSON 忽略 */ }
        return null;
    }

    private sealed class SecurityDto
    {
        public string RegisterKey { get; set; } = "";   // 空 = 关闭注册，管理员在控制台设置注册密码
        public bool EnableCaptcha { get; set; } = true;
        public int MaxLoginAttempts { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 10;
        public int AuthPerMinute { get; set; } = 20;
        public int ApiPerMinute { get; set; } = 300;
        public int FwRequestsPerMinute { get; set; } = 1000;
        public int FwMaxConcurrent { get; set; } = 60;
        public long FwBytesPerMinute { get; set; } = 500L * 1024 * 1024;
        public int FwBanThreshold { get; set; } = 50;
        public int FwBanMinutes { get; set; } = 30;
        public int FloodMessagesPerMinute { get; set; } = 60;
    }

    private async Task LoadSecurityAsync()
    {
        SecurityDto? s = null;
        if (_running)
        {
            try { s = await GetJsonAsync<SecurityDto>("security"); }
            catch { /* 退回文件 */ }
        }
        s ??= JsonSerializer.Deserialize<SecurityDto>(JsonSerializer.Serialize(SecurityCfg.LoadFile()));

        _txtRegKey.Text = s!.RegisterKey;
        _chkCaptcha.Checked = s.EnableCaptcha;
        _nudAttempts.Value = Math.Clamp(s.MaxLoginAttempts, 1, 999);
        _nudLockMin.Value = Math.Clamp(s.LockoutMinutes, 1, 999);
        _nudPerMin.Value = Math.Clamp(s.AuthPerMinute, 5, 9999);
        _nudApiPerMin.Value = Math.Clamp(s.ApiPerMinute, 10, 999999);
        _nudFwReq.Value = Math.Clamp(s.FwRequestsPerMinute, 100, 9999999);
        _nudFwConc.Value = Math.Clamp(s.FwMaxConcurrent, 2, 99999);
        _nudFwMb.Value = Math.Clamp(s.FwBytesPerMinute / 1024 / 1024, 1L, 102400L);
        _nudFwBanHit.Value = Math.Clamp(s.FwBanThreshold, 1, 99999);
        _nudFwBanMin.Value = Math.Clamp(s.FwBanMinutes, 1, 10080);
        _nudFloodMsgs.Value = Math.Clamp(s.FloodMessagesPerMinute, 1, 100000);
        _lblSecHint.Text = _running ? "来源：管理 API（运行中，保存即时生效）" : "来源：security.json（服务停止，启动后生效）";
    }

    private sealed class LimitsDto
    {
        public long MaxFileMb { get; set; } = 100;
        public long UploadSpeedMb { get; set; } = 10;
        public int KeepDays { get; set; } = 5;
        public int MaxGroupMembers { get; set; } = 100;
        public int MaxGroupAdmins { get; set; } = 5;
        public int MaxSelectOnCreate { get; set; } = 99;
    }

    private async Task LoadLimitsAsync()
    {
        var s = await GetJsonAsync<LimitsDto>("limits");
        if (s == null) return;
        _nudMaxFileMb.Value = Math.Clamp((int)s.MaxFileMb, 1, 1024);
        _nudSpeedMb.Value = Math.Clamp((int)s.UploadSpeedMb, 1, 200);
        _nudKeepDays.Value = Math.Clamp(s.KeepDays, 1, 365);
        _nudGroupMembers.Value = Math.Clamp(s.MaxGroupMembers, 2, 1000);
        _nudGroupAdmins.Value = Math.Clamp(s.MaxGroupAdmins, 0, 50);
        _lblLimitsHint.Text = "同内容文件自动去重（只存一份），超期文件每小时自动清理";
    }

    private async Task SaveLimitsAsync()
    {
        var payload = JsonSerializer.Serialize(new
        {
            maxFileMb = (long)_nudMaxFileMb.Value,
            uploadSpeedMb = (long)_nudSpeedMb.Value,
            keepDays = (int)_nudKeepDays.Value,
            maxGroupMembers = (int)_nudGroupMembers.Value,
            maxGroupAdmins = (int)_nudGroupAdmins.Value,
            maxSelectOnCreate = Math.Max(2, (int)_nudGroupMembers.Value - 1),
        });
        using var req = ApiRequest(HttpMethod.Put, "limits", payload);
        using var resp = await G.Http.SendAsync(req);
        if (resp.IsSuccessStatusCode) await LoadLimitsAsync();
        else _msg.Text = "保存失败：" + (int)resp.StatusCode;
    }

    private async Task CleanupFilesAsync()
    {
        using var req = ApiRequest(HttpMethod.Post, "files/cleanup", "{}");
        using var resp = await G.Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) { _msg.Text = "清理失败，请先启动服务"; return; }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var removed = doc.RootElement.GetProperty("removed").GetInt32();
        var days = doc.RootElement.GetProperty("keepDays").GetInt32();
        _msg.Text = $"已清理 {removed} 个超过 {days} 天的文件";
    }

    /// <summary>立即清理数据：等效把「7 天清理」立即执行一次（删除超出保留期的聊天记录）。</summary>
    private async Task CleanupDataAsync()
    {
        if (MessageBox.Show(this,
            "立即执行一次数据清理？将删除超过保留期（7 天）的聊天记录。" + Environment.NewLine
            + "（相当于把后台 7 天一次的自动清理现在就执行）",
            "立即清理数据", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var req = ApiRequest(HttpMethod.Post, "data/cleanup", "{}");
        using var resp = await G.Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) { _msg.Text = "数据清理失败，请先启动服务"; return; }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        int removed = 0, days = 7;
        if (doc.RootElement.TryGetProperty("removedMessages", out var rm)) removed = rm.GetInt32();
        if (doc.RootElement.TryGetProperty("keepDays", out var kd)) days = kd.GetInt32();
        _msg.Text = $"已立即清理数据：删除 {removed} 条超过 {days} 天的聊天记录";
        MessageBox.Show(this, $"已删除 {removed} 条超过 {days} 天的聊天记录。",
            "立即清理数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SaveSecurityAsync()
    {
        var s = new SecurityDto
        {
            RegisterKey = _txtRegKey.Text.Trim(),
            EnableCaptcha = _chkCaptcha.Checked,
            MaxLoginAttempts = (int)_nudAttempts.Value,
            LockoutMinutes = (int)_nudLockMin.Value,
            AuthPerMinute = (int)_nudPerMin.Value,
            ApiPerMinute = (int)_nudApiPerMin.Value,
            FwRequestsPerMinute = (int)_nudFwReq.Value,
            FwMaxConcurrent = (int)_nudFwConc.Value,
            FwBytesPerMinute = (long)_nudFwMb.Value * 1024 * 1024,
            FwBanThreshold = (int)_nudFwBanHit.Value,
            FwBanMinutes = (int)_nudFwBanMin.Value,
            FloodMessagesPerMinute = (int)_nudFloodMsgs.Value,
        };

        if (_running)
        {
            using var req = ApiRequest(HttpMethod.Put, "security", JsonSerializer.Serialize(s));
            using var resp = await G.Http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                _msg.Text = "已通过管理 API 保存并即时生效。";
                await LoadSecurityAsync();
                return;
            }
            _msg.Text = $"管理 API 保存失败（{(int)resp.StatusCode}），改为写入文件。";
        }
        JsonSerializer.Deserialize<SecurityCfg>(JsonSerializer.Serialize(s))!.SaveFile();
        _msg.Text = $"已写入 {G.SecurityFile}" + (_running ? "" : "（重启后生效）。");
        await LoadSecurityAsync();
    }

    private sealed class UsersDto
    {
        public List<UserItem> Users { get; set; } = new();
    }

    private sealed class UserItem
    {
        public string Id { get; set; } = "";
        public string Account { get; set; } = "";
        public string Nickname { get; set; } = "";
        public bool Banned { get; set; }
        public bool IsBot { get; set; }
    }

    private async Task LoadUsersAsync()
    {
        if (!_running)
        {
            _msg.Text = "用户在内存中，需先启动服务。";
            return;
        }
        using var req = ApiRequest(HttpMethod.Get, "users");
        using var resp = await G.Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        _users.Items.Clear();
        foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
        {
            var banned = u.GetProperty("banned").GetBoolean();
            var bot = u.GetProperty("isBot").GetBoolean();
            var item = new ListViewItem(u.GetProperty("account").GetString())
            {
                Tag = u.GetProperty("id").GetString(),
            };
            item.SubItems.Add(u.GetProperty("nickname").GetString() ?? "");
            item.SubItems.Add(banned ? "已停用" : bot ? "机器人" : "正常");
            item.SubItems.Add(u.GetProperty("id").GetString() ?? "");
            if (banned) item.ForeColor = Color.Firebrick;
            _users.Items.Add(item);
        }
    }

    private ListViewItem? SelectedUser()
    {
        if (_users.SelectedItems.Count == 0)
        {
            _msg.Text = "请先在列表中选择用户。";
            return null;
        }
        return _users.SelectedItems[0];
    }

    private async Task ToggleBanAsync()
    {
        var item = SelectedUser();
        if (item == null) return;
        var bannedNow = item.SubItems[2].Text == "已停用";
        using var req = ApiRequest(HttpMethod.Put, $"users/{item.Tag}/banned", JsonSerializer.Serialize(new { banned = !bannedNow }));
        using var resp = await G.Http.SendAsync(req);
        _msg.Text = resp.IsSuccessStatusCode
            ? (!bannedNow ? $"已停用 {item.Text}" : $"已恢复 {item.Text}")
            : $"操作失败：{(int)resp.StatusCode}";
        await LoadUsersAsync();
    }

    /// <summary>创建用户（干净项目没有种子账号，第一个账号从这里建）。密码留空 = 随机生成。</summary>
    private async Task CreateUserAsync()
    {
        if (!_running)
        {
            _msg.Text = "请先启动服务，再创建用户。";
            return;
        }

        using var f = new Form
        {
            Text = "创建用户",
            Width = 420,
            Height = 280,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };
        var lblPhone = new Label { Text = "账号：", Left = 14, Top = 22, AutoSize = true };
        var txtPhone = new TextBox { Left = 110, Top = 19, Width = 280 };
        var lblNick = new Label { Text = "昵称：", Left = 14, Top = 60, AutoSize = true };
        var txtNick = new TextBox { Left = 110, Top = 57, Width = 280 };
        var lblPwd = new Label { Text = "密码：", Left = 14, Top = 98, AutoSize = true };
        var txtPwd = new TextBox { Left = 110, Top = 95, Width = 280 };
        var hint = new Label
        {
            Text = "账号：11 位手机号、3-32 位字母/数字/下划线或 2-16 位中文" + Environment.NewLine
                + "密码：8-32 位；留空则随机生成并显示一次",
            Left = 14, Top = 134, Width = 380, Height = 44, ForeColor = Color.DimGray,
        };
        var ok = new Button { Text = "创建", Left = 240, Top = 186, Width = 78, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 324, Top = 186, Width = 78, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lblPhone, txtPhone, lblNick, txtNick, lblPwd, txtPwd, hint, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;

        var payload = JsonSerializer.Serialize(new
        {
            phone = txtPhone.Text.Trim(),
            nickname = txtNick.Text.Trim(),
            password = txtPwd.Text.Trim(),
        });
        using var req = ApiRequest(HttpMethod.Post, "users", payload);
        using var resp = await G.Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            _msg.Text = "创建失败：" + body;
            return;
        }
        using var doc = JsonDocument.Parse(body);
        var created = doc.RootElement.GetProperty("phone").GetString() ?? "";
        var pwd = doc.RootElement.TryGetProperty("password", out var pw) ? pw.GetString() ?? "" : "";
        _msg.Text = $"已创建用户 {created}";
        MessageBox.Show(this,
            $"账号：{created}" + Environment.NewLine + $"密码：{pwd}" + Environment.NewLine + Environment.NewLine + "请立即保存，密码只显示这一次。",
            "用户已创建", MessageBoxButtons.OK, MessageBoxIcon.Information);
        await LoadUsersAsync();
    }

    private async Task ResetPasswordAsync()
    {
        var item = SelectedUser();
        if (item == null) return;
        var pwd = PromptPassword($"为 {item.Text} 设置新密码", "新密码（至少 6 位）：");
        if (string.IsNullOrEmpty(pwd)) return;
        using var req = ApiRequest(HttpMethod.Put, $"users/{item.Tag}/password", JsonSerializer.Serialize(new { password = pwd }));
        using var resp = await G.Http.SendAsync(req);
        _msg.Text = resp.IsSuccessStatusCode
            ? $"已重置 {item.Text} 的密码，并吊销其全部登录令牌。"
            : $"重置失败：{(int)resp.StatusCode}";
    }

    /// <summary>删除用户（级联清理联系人/会话/群/朋友圈/文件/令牌，不可恢复）。需先选中 + 二次确认。</summary>
    private async Task DeleteUserAsync()
    {
        var item = SelectedUser();
        if (item == null) return;

        var id = item.Tag as string ?? "";
        var label = $"{item.Text}（{item.SubItems[1].Text}）";
        var confirm = MessageBox.Show(this,
            $"确定删除用户 {label}？" + Environment.NewLine + Environment.NewLine
            + "将同时删除：其联系人、私聊会话与消息、群内身份与所属群、"
            + "其发布的朋友圈、好友申请、收藏、上传的文件，并立即注销其登录。" + Environment.NewLine
            + "此操作不可恢复。",
            "确认删除用户", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        using var req = ApiRequest(HttpMethod.Delete, $"users/{Uri.EscapeDataString(id)}");
        using var resp = await G.Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            _msg.Text = $"已删除 {item.Text}";
            var removed = ParseRemovedSummary(body);
            if (removed.Length > 0)
                MessageBox.Show(this, $"已删除 {item.Text}，级联清理：{removed}。",
                    "删除完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadUsersAsync();
        }
        else
        {
            _msg.Text = "删除失败：" + (ExtractApiError(body) ?? ((int)resp.StatusCode).ToString());
        }
    }

    /// <summary>把删除响应的 removed 摘要拼成可读中文文本（如「联系人 3、群 1、朋友圈 2 …」）。</summary>
    private static string ParseRemovedSummary(string json)
    {
        var names = new Dictionary<string, string>
        {
            ["contacts"] = "联系人",
            ["conversations"] = "会话",
            ["groupMemberships"] = "群成员身份",
            ["groups"] = "群",
            ["moments"] = "朋友圈",
            ["friendRequests"] = "好友申请",
            ["favorites"] = "收藏",
            ["files"] = "文件",
        };
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("removed", out var r)) return "";
            var parts = new List<string>();
            foreach (var kv in r.EnumerateObject())
            {
                if (kv.Value.GetInt32() > 0)
                    parts.Add($"{names.GetValueOrDefault(kv.Name, kv.Name)} {kv.Value.GetInt32()}");
            }
            return string.Join("、", parts);
        }
        catch { return ""; }
    }

    private static string? PromptPassword(string title, string label)
    {
        using var f = new Form
        {
            Text = title,
            Width = 360,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
        };
        var lbl = new Label { Text = label, Left = 12, Top = 14, AutoSize = true };
        var txt = new TextBox { Left = 12, Top = 40, Width = 320, UseSystemPasswordChar = true };
        var ok = new Button { Text = "确定", Left = 180, Top = 78, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 257, Top = 78, Width = 75, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }

    /* ---------------- 日志（API 或直读文件） ---------------- */

    private async Task LoadLogsAsync()
    {
        var source = _cmbLogType.SelectedItem?.ToString() ?? "控制台输出";

        if (source == "控制台输出")
        {
            _txtLog.Text = ReadTailFile(G.ConsoleLogFile, 400, "（暂无控制台输出。启动服务后，后端的控制台日志会实时写入这里。）");
            ScrollLog();
            return;
        }

        if (_running)
        {
            try
            {
                using var req = ApiRequest(HttpMethod.Get, $"logs?type={source}&lines=300");
                using var resp = await G.Http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var lines = doc.RootElement.GetProperty("lines");
                _txtLog.Text = lines.ValueKind == JsonValueKind.String
                    ? lines.GetString()
                    : string.Join(Environment.NewLine, lines.EnumerateArray().Select(x => x.ToString()));
                ScrollLog();
                return;
            }
            catch { /* 服务刚停：退回读文件 */ }
        }

        _txtLog.Text = ReadTailFile(Path.Combine(G.LogDir, $"{source}.jsonl"), 400, "（服务未运行，且日志文件为空。）");
        ScrollLog();
    }

    private void ScrollLog()
    {
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private static string ReadTailFile(string path, int maxLines, string emptyText)
    {
        try
        {
            if (!File.Exists(path)) return emptyText;
            var tail = new Queue<string>(maxLines + 1);
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                tail.Enqueue(line);
                if (tail.Count > maxLines) tail.Dequeue();
            }
            return tail.Count == 0 ? emptyText : string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex)
        {
            return "读取失败：" + ex.Message;
        }
    }

    /* ---------------- 用户反馈（管理 API GET /api/admin/feedback；查看 + 复制） ---------------- */

    private sealed class FbListDto
    {
        public int count { get; set; }
        public List<FbItemDto> items { get; set; } = new();
    }

    private sealed class FbItemDto
    {
        public string id { get; set; } = "";
        public string ts { get; set; } = "";
        public string? user { get; set; }
        public string? nickname { get; set; }
        public string body { get; set; } = "";
        public string? client { get; set; }
    }

    private async Task LoadFeedbackAsync()
    {
        if (!_running)
        {
            _feedback.Items.Clear();
            _msg.Text = "用户反馈需先启动服务（管理 API）";
            return;
        }
        try
        {
            var dto = await GetJsonAsync<FbListDto>($"feedback?limit=200");
            _feedback.Items.Clear();
            if (dto == null || dto.items == null || dto.items.Count == 0)
            {
                _msg.Text = "暂无用户反馈";
                return;
            }
            foreach (var it in dto.items)
            {
                var who = string.IsNullOrWhiteSpace(it.nickname) ? (it.user ?? "-") : $"{it.nickname}（{it.user}）";
                var row = new ListViewItem(it.ts)
                {
                    Tag = it,
                };
                row.SubItems.Add(who);
                row.SubItems.Add(it.body.Length > 60 ? it.body.Substring(0, 60) + "…" : it.body);
                _feedback.Items.Add(row);
            }
            _msg.Text = $"用户反馈 {dto.count} 条已加载";
        }
        catch
        {
            _msg.Text = "用户反馈加载失败，请先启动服务";
        }
    }

    /// <summary>把一条反馈格式化成可读文本（正文 + 客户端快照），供复制。</summary>
    private static string FbToText(FbItemDto it)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"时间：{it.ts}");
        sb.AppendLine($"用户：{it.nickname}（{it.user}）");
        sb.AppendLine("反馈内容：");
        sb.AppendLine(it.body);
        if (!string.IsNullOrWhiteSpace(it.client))
        {
            sb.AppendLine();
            sb.AppendLine("客户端快照：");
            sb.AppendLine(it.client);
        }
        return sb.ToString();
    }

    private void CopySelectedFeedback()
    {
        if (_feedback.SelectedItems.Count == 0)
        {
            _msg.Text = "请先选中一条用户反馈";
            return;
        }
        var parts = new List<string>();
        foreach (ListViewItem item in _feedback.SelectedItems)
        {
            if (item.Tag is FbItemDto dto) parts.Add(FbToText(dto));
        }
        CopyToClipboard(string.Join(Environment.NewLine + "————————" + Environment.NewLine, parts));
    }

    private void CopyAllFeedback()
    {
        if (_feedback.Items.Count == 0)
        {
            _msg.Text = "没有可复制的用户反馈";
            return;
        }
        var parts = new List<string>();
        foreach (ListViewItem item in _feedback.Items)
        {
            if (item.Tag is FbItemDto dto) parts.Add(FbToText(dto));
        }
        CopyToClipboard(string.Join(Environment.NewLine + "————————" + Environment.NewLine, parts));
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _msg.Text = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            _msg.Text = "复制失败：" + ex.Message;
        }
    }

    private async Task DeleteSelectedFeedbackAsync()
    {
        if (!_running)
        {
            _msg.Text = "删除用户反馈需先启动服务（管理 API）";
            return;
        }
        var selected = _feedback.SelectedItems.Cast<ListViewItem>().ToList();
        if (selected.Count == 0)
        {
            _msg.Text = "请先选中要删除的用户反馈";
            return;
        }
        var confirm = MessageBox.Show(this,
            $"确定删除选中的 {selected.Count} 条用户反馈？此操作不可恢复。",
            "确认删除用户反馈", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        var ok = 0;
        var failed = 0;
        foreach (ListViewItem item in selected)
        {
            var id = item.Tag is FbItemDto dto ? dto.id : "";
            if (string.IsNullOrEmpty(id)) { failed++; continue; }
            try
            {
                using var req = ApiRequest(HttpMethod.Delete, "feedback/" + Uri.EscapeDataString(id));
                using var resp = await G.Http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode) { ok++; continue; }
                failed++;
                _msg.Text = "部分删除失败：" + (ExtractApiError(body) ?? ((int)resp.StatusCode).ToString());
            }
            catch
            {
                failed++;
                _msg.Text = "删除失败（网络/服务不可用），请重试";
            }
        }
        if (failed == 0) _msg.Text = $"已删除 {ok} 条用户反馈";
        await LoadFeedbackAsync();
    }

    /* ---------------- 蜜罐（管理 API GET/PUT /api/admin/honeypot；开关 + 事件 + 金丝雀） ---------------- */

    private sealed class HoneyDto
    {
        public bool enabled { get; set; }
        public bool tarpit { get; set; }
        public List<HoneyEventDto> events { get; set; } = new();
        public List<HoneyCanaryDto> canaries { get; set; } = new();
    }

    private sealed class HoneyEventDto
    {
        public string ts { get; set; } = "";
        public string ip { get; set; } = "";
        public string ua { get; set; } = "";
        public string method { get; set; } = "";
        public string path { get; set; } = "";
        public string lure { get; set; } = "";
        public int delayMs { get; set; }
        public List<string> canaries { get; set; } = new();
        public string action { get; set; } = "";
    }

    private sealed class HoneyCanaryDto
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "";
        public string value { get; set; } = "";
        public string createdAt { get; set; } = "";
        public string plantedAt { get; set; } = "";
    }

    private async Task LoadHoneyAsync()
    {
        if (!_running)
        {
            _honeyEvents.Items.Clear();
            _honeyCanaries.Items.Clear();
            _msg.Text = "蜜罐需先启动服务（管理 API）";
            return;
        }
        try
        {
            var dto = await GetJsonAsync<HoneyDto>($"honeypot?limit=200");
            _honeyEvents.Items.Clear();
            _honeyCanaries.Items.Clear();
            if (dto == null)
            {
                _msg.Text = "蜜罐数据为空";
                return;
            }
            _chkHoneyEnabled.Checked = dto.enabled;
            _chkHoneyTarpit.Checked = dto.tarpit;
            foreach (var e in dto.events)
            {
                var row = new ListViewItem(FormatIso(e.ts))
                {
                    Tag = e,
                };
                row.SubItems.Add(e.ip ?? "-");
                row.SubItems.Add(e.method ?? "");
                row.SubItems.Add(e.path ?? "");
                row.SubItems.Add(e.lure ?? "");
                row.SubItems.Add(e.delayMs.ToString());
                row.SubItems.Add((e.canaries?.Count ?? 0).ToString());
                row.SubItems.Add(e.action ?? "");
                _honeyEvents.Items.Add(row);
            }
            foreach (var c in dto.canaries)
            {
                var row = new ListViewItem(c.type ?? "")
                {
                    Tag = c,
                };
                row.SubItems.Add(c.value ?? "");
                row.SubItems.Add(c.plantedAt ?? "");
                row.SubItems.Add(FormatIso(c.createdAt));
                _honeyCanaries.Items.Add(row);
            }
            _msg.Text = $"蜜罐 {(dto.enabled ? "已开启" : "已关闭")}：事件 {_honeyEvents.Items.Count} 条、金丝雀 {_honeyCanaries.Items.Count} 枚";
        }
        catch
        {
            _msg.Text = "蜜罐加载失败，请先启动服务";
        }
    }

    private async Task SaveHoneyAsync()
    {
        if (!_running)
        {
            _msg.Text = "请先启动服务再改蜜罐开关";
            return;
        }
        try
        {
            var req = ApiRequest(HttpMethod.Put, $"honeypot?enabled={(_chkHoneyEnabled.Checked ? "true" : "false")}&tarpit={(_chkHoneyTarpit.Checked ? "true" : "false")}");
            using var resp = await G.Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                _msg.Text = "蜜罐开关保存失败：" + (int)resp.StatusCode;
                return;
            }
            await LoadHoneyAsync();
        }
        catch (Exception ex)
        {
            _msg.Text = "蜜罐开关保存失败：" + ex.Message;
        }
    }

    /// <summary>把一条蜜罐事件格式化成可读文本（含 UA + 金丝雀值），供复制。</summary>
    private static string HoneyEventToText(HoneyEventDto e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"时间：{FormatIso(e.ts)}");
        sb.AppendLine($"来源：{e.ip}  {e.method} {e.path}");
        sb.AppendLine($"诱饵：{e.lure}　延迟：{e.delayMs}ms　动作：{e.action}");
        if (!string.IsNullOrWhiteSpace(e.ua)) sb.AppendLine("UA：" + e.ua);
        if (e.canaries is { Count: > 0 }) sb.AppendLine("金丝雀：" + string.Join("、", e.canaries));
        return sb.ToString();
    }

    private void CopySelectedHoney()
    {
        if (_honeyEvents.SelectedItems.Count == 0)
        {
            _msg.Text = "请先选中一条蜜罐事件";
            return;
        }
        var parts = new List<string>();
        foreach (ListViewItem item in _honeyEvents.SelectedItems)
        {
            if (item.Tag is HoneyEventDto e) parts.Add(HoneyEventToText(e));
        }
        CopyToClipboard(string.Join(Environment.NewLine + "————————" + Environment.NewLine, parts));
    }

    private void CopyAllHoney()
    {
        if (_honeyEvents.Items.Count == 0)
        {
            _msg.Text = "没有可复制的蜜罐事件";
            return;
        }
        var parts = new List<string>();
        foreach (ListViewItem item in _honeyEvents.Items)
        {
            if (item.Tag is HoneyEventDto e) parts.Add(HoneyEventToText(e));
        }
        CopyToClipboard(string.Join(Environment.NewLine + "————————" + Environment.NewLine, parts));
    }

    /* ---------------- 通用执行（吞异常到状态栏） ---------------- */

    private async void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _msg.Text = "出错：" + ex.Message;
        }
        await Task.CompletedTask;
    }

    private async void RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _msg.Text = "出错：" + ex.Message;
        }
    }
}
