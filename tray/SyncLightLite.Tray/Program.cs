using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.Json;
using Microsoft.Win32;

namespace SyncLightLite.Tray;

internal static class Program
{
    private const string MutexName = "SyncLightLite.SingleInstance";
    private const string ShowEventName = "SyncLightLite.OpenSettings";
    private static readonly string AppRoot = FindAppRoot();
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncLightLite");
    private static readonly string LogPath = Path.Combine(LogDir, "synclightlite.log");
    private static readonly string ConfigPath = Path.Combine(AppRoot, "config.json");
    private static Process? _live;
    private static NotifyIcon? _tray;
    private static SettingsForm? _settings;
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static bool _shuttingDown;

    [STAThread]
    private static void Main(string[] args)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        Directory.CreateDirectory(LogDir);
        ApplicationConfiguration.Initialize();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var ui = WindowsFormsSynchronizationContext.Current;
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) ui?.Post(_ => OpenSettingsWindow(), null);
        }) { IsBackground = true, Name = "SyncLightLite settings event" }.Start();

        SystemEvents.SessionEnding += (_, _) => ShutdownNow();
        Application.ApplicationExit += (_, _) => ShutdownNow();

        _tray = new NotifyIcon
        {
            Icon = FromIconImage(),
            Text = "SyncLightLite",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => OpenSettingsWindow();

        EnsureConfig();
        StartSyncLightLite();
        if (!args.Contains("--minimized")) OpenSettingsWindow();
        Application.Run();
    }

    private static string FindAppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "live.js"))) return dir.FullName;
            dir = dir.Parent;
        }
        var cwd = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(cwd, "src", "live.js"))) return cwd;
        return AppContext.BaseDirectory;
    }

    private static ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(TMenu("settings"), null, (_, _) => OpenSettingsWindow());
        menu.Items.Add(TMenu("startSync"), null, (_, _) => StartSyncLightLite());
        menu.Items.Add(TMenu("stopOff"), null, (_, _) => StopSyncLightLite(turnOff: true));
        menu.Items.Add(TMenu("offNow"), null, (_, _) => StopSyncLightLite(turnOff: true));
        menu.Items.Add(TMenu("openLog"), null, (_, _) => Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true }));
        menu.Items.Add(TMenu("exit"), null, (_, _) => { StopSyncLightLite(turnOff: true); Application.Exit(); });
        return menu;
    }

    private static void ApplyLanguageToTray()
    {
        if (_tray == null) return;
        _tray.ContextMenuStrip?.Dispose();
        _tray.ContextMenuStrip = BuildTrayMenu();
        _tray.Text = _live is { HasExited: false } ? TMenu("trayOn") : TMenu("trayOff");
    }

    private static string CurrentLanguage() => NormalizeLanguageCode(LoadConfig().Language);


    private static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        if (code.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (code.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        return "en";
    }

    private static string TMenu(string key) => MenuTranslations.GetValueOrDefault(CurrentLanguage(), MenuTranslations["en"]).GetValueOrDefault(key, MenuTranslations["en"].GetValueOrDefault(key, key));

    private static readonly Dictionary<string, Dictionary<string, string>> MenuTranslations = new()
    {
        ["en"] = new() { ["settings"] = "Settings", ["startSync"] = "Start sync", ["stopOff"] = "Stop and turn off", ["offNow"] = "Turn off now", ["openLog"] = "Open log", ["exit"] = "Exit", ["trayOn"] = "SyncLightLite - on", ["trayOff"] = "SyncLightLite - stopped" },
        ["pt-BR"] = new() { ["settings"] = "Configurações", ["startSync"] = "Iniciar sincronização", ["stopOff"] = "Parar e apagar luz", ["offNow"] = "Apagar luz agora", ["openLog"] = "Abrir log", ["exit"] = "Sair", ["trayOn"] = "SyncLightLite - ligado", ["trayOff"] = "SyncLightLite - parado" },
        ["es"] = new() { ["settings"] = "Configuración", ["startSync"] = "Iniciar sincronización", ["stopOff"] = "Detener y apagar", ["offNow"] = "Apagar ahora", ["openLog"] = "Abrir registro", ["exit"] = "Salir", ["trayOn"] = "SyncLightLite - encendido", ["trayOff"] = "SyncLightLite - detenido" },
        ["zh-CN"] = new() { ["settings"] = "设置", ["startSync"] = "开始同步", ["stopOff"] = "停止并关闭灯光", ["offNow"] = "立即关闭灯光", ["openLog"] = "打开日志", ["exit"] = "退出", ["trayOn"] = "SyncLightLite - 已开启", ["trayOff"] = "SyncLightLite - 已停止" }
    };

    private static void OpenSettingsWindow()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Show();
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(LoadConfig(), cfg =>
        {
            SaveConfig(cfg);
            ApplyLanguageToTray();
            StopSyncLightLite(turnOff: false);
            StartSyncLightLite();
        }, lang =>
        {
            var cfg = LoadConfig();
            cfg.Language = lang;
            SaveConfig(cfg);
            ApplyLanguageToTray();
        });
        _settings.Show();
        _settings.Activate();
    }

    private static void StartSyncLightLite()
    {
        KillStaleLiveEngines(_live?.Id);
        if (_live is { HasExited: false }) return;
        Directory.CreateDirectory(LogDir);
        var cfg = LoadConfig();
        var info = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"src\\live.js live --fps {cfg.Fps}",
            WorkingDirectory = AppRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        _live = Process.Start(info);
        if (_live == null) return;
        _live.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _live.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _live.BeginOutputReadLine();
        _live.BeginErrorReadLine();
        if (_tray != null) _tray.Text = TMenu("trayOn");
    }

    private static void StopSyncLightLite(bool turnOff, bool allowFallbackOff = true, int? waitMs = null)
    {
        var offSentByLiveEngine = false;
        try
        {
            if (_live is { HasExited: false })
            {
                offSentByLiveEngine = RequestLiveEngineStop(_live, turnOff);
                var gracefulWaitMs = waitMs ?? (turnOff ? 3500 : 1500);
                if (!_live.WaitForExit(gracefulWaitMs))
                {
                    AppendLog($"[tray] live engine did not exit after {gracefulWaitMs}ms; killing process tree");
                    _live.Kill(entireProcessTree: true);
                    _live.WaitForExit(Math.Min(1000, gracefulWaitMs));
                    offSentByLiveEngine = false;
                }
            }
            KillStaleLiveEngines(null);
        }
        catch (Exception ex) { AppendLog("[tray] stop failed: " + ex.Message); }
        finally
        {
            _live?.Dispose();
            _live = null;
            if (turnOff && !offSentByLiveEngine && allowFallbackOff) TurnOffLights();
            else if (turnOff && !offSentByLiveEngine) AppendLog("[tray] skipped fallback off process during Windows shutdown/logoff");
            if (_tray != null) _tray.Text = TMenu("trayOff");
        }
    }

    private static bool RequestLiveEngineStop(Process process, bool turnOff)
    {
        try
        {
            if (process.HasExited) return false;
            process.StandardInput.WriteLine(turnOff ? "off" : "stop");
            process.StandardInput.Flush();
            process.StandardInput.Close();
            AppendLog(turnOff ? "[tray] requested live engine graceful off" : "[tray] requested live engine graceful stop");
            return turnOff;
        }
        catch (Exception ex)
        {
            AppendLog("[tray] graceful live engine stop failed: " + ex.Message);
            return false;
        }
    }

    private static void KillStaleLiveEngines(int? keepPid)
    {
        try
        {
            var query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'";
            using var searcher = new System.Management.ManagementObjectSearcher(query);
            foreach (System.Management.ManagementObject process in searcher.Get())
            {
                var pid = Convert.ToInt32(process["ProcessId"]);
                if (keepPid.HasValue && pid == keepPid.Value) continue;
                var commandLine = (process["CommandLine"] as string) ?? string.Empty;
                var normalized = commandLine.Replace('/', '\\');
                if (!normalized.Contains("src\\live.js live", StringComparison.OrdinalIgnoreCase)) continue;
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); AppendLog($"[tray] stopped stale live engine pid={pid}"); } catch { }
            }
        }
        catch (Exception ex) { AppendLog("[tray] stale engine cleanup failed: " + ex.Message); }
    }

    private static void TurnOffLights() => RunNode("src\\live.js off", waitMs: 4000);

    private static void ShutdownNow()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        AppendLog("[tray] Windows shutdown/logoff detected; stopping live engine without launching new node processes");
        StopSyncLightLite(turnOff: true, allowFallbackOff: false, waitMs: 1800);
        _tray?.Dispose();
        _mutex?.Dispose();
        _showEvent?.Dispose();
        try { Application.ExitThread(); } catch { }
    }

    private static void RunNode(string args, int waitMs)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "node",
                Arguments = args,
                WorkingDirectory = AppRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(waitMs);
        }
        catch (Exception ex) { AppendLog("[tray] " + ex.Message); }
    }

    private static AppConfig LoadConfig()
    {
        EnsureConfig();
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new AppConfig(); }
        catch { return new AppConfig(); }
    }

    private static void SaveConfig(AppConfig config)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static void EnsureConfig()
    {
        if (!File.Exists(ConfigPath)) SaveConfig(new AppConfig { Language = DetectLanguage() });
    }

    private static string DetectLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (name.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        return "en";
    }

    private static string FindIconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "SyncLightLite.ico");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "SyncLightLite.ico");
    }

    private static Icon FromIconImage()
    {
        try
        {
            return new Icon(FindIconPath());
        }
        catch { return SystemIcons.Application; }
    }

    private static void AppendLog(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }
}

internal sealed class AppConfig
{
    public string Language { get; set; } = "en";
    public string Mode { get; set; } = "screen";
    public string EffectColor { get; set; } = "#ffffff";
    public int EffectSpeedPercent { get; set; } = 50;
    public int Fps { get; set; } = 30;
    public int CaptureScalePercent { get; set; } = 100;
    public int HeaderOffsetPercent { get; set; } = 0;
    public int HorizontalMarginPercent { get; set; } = 0;
    public int BrightnessPercent { get; set; } = 100;
    public int SaturationPercent { get; set; } = 100;
    public double Gamma { get; set; } = 1;
    public int ThresholdPercent { get; set; } = 0;
    public int BlackFloorPercent { get; set; } = 0;
    public double Smoothing { get; set; } = 0.35;
    public bool ReverseLedOrder { get; set; } = false;
}

internal sealed partial class SettingsForm : Form
{
    private static readonly Color Bg = Color.FromArgb(247, 239, 219);
    private static readonly Color Panel = Color.FromArgb(255, 250, 238);
    private static readonly Color Fg = Color.FromArgb(62, 47, 38);
    private static readonly Color Accent = Color.FromArgb(38, 112, 105);

    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _effectColor = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _effectSpeed = new() { Minimum = 1, Maximum = 100, Value = 50, Increment = 5 };
    private readonly NumericUpDown _fps = new() { Minimum = 15, Maximum = 120, Value = 30, Increment = 5 };
    private readonly NumericUpDown _scale = new() { Minimum = 35, Maximum = 100, Value = 100, Increment = 5 };
    private readonly NumericUpDown _offset = new() { Minimum = 0, Maximum = 65, Value = 0, Increment = 2 };
    private readonly NumericUpDown _hMargin = new() { Minimum = 0, Maximum = 25, Value = 0, Increment = 1 };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 150, Value = 100, Increment = 5 };
    private readonly NumericUpDown _saturation = new() { Minimum = 0, Maximum = 250, Value = 100, Increment = 5 };
    private readonly NumericUpDown _gamma = new() { Minimum = 40, Maximum = 300, Value = 100, Increment = 5 };
    private readonly NumericUpDown _threshold = new() { Minimum = 0, Maximum = 25, Value = 0, Increment = 1 };
    private readonly NumericUpDown _blackFloor = new() { Minimum = 0, Maximum = 25, Value = 0, Increment = 1 };
    private readonly NumericUpDown _smoothing = new() { Minimum = 5, Maximum = 90, Value = 35, Increment = 5 };
    private readonly CheckBox _reverseLedOrder = new() { Text = "Inverter ordem dos LEDs", AutoSize = true };
    private readonly Action<AppConfig> _save;
    private readonly Action<string> _languagePreview;
    private readonly Label _titleLabel = new() { Text = "SyncLightLite", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Fg, AutoSize = true, Location = new Point(78, 0) };
    private readonly Label _subtitleLabel = new() { Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(102, 80, 60), AutoSize = true, Location = new Point(80, 44) };
    private readonly Button _saveButton = new() { Dock = DockStyle.Right, Width = 230, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly LinkLabel _developerCredit = new() { AutoSize = true, LinkColor = Accent, ActiveLinkColor = Fg, VisitedLinkColor = Accent, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Left, Padding = new Padding(0, 12, 0, 0) };
    private readonly System.Windows.Forms.Timer _savePulseTimer = new() { Interval = 120 };
    private int _savePulseStep;
    private TabControl? _settingsTabs;
    private readonly List<Label> _rowLabels = new();
    private readonly Dictionary<Control, string> _rowHelp = new();

    public SettingsForm(AppConfig cfg, Action<AppConfig> save, Action<string> languagePreview)
    {
        _save = save;
        _languagePreview = languagePreview;
        Text = "SyncLightLite";
        Width = 760;
        Height = 800;
        MinimumSize = new Size(700, 480);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Fg;
        Icon = ProgramIcon();

        _language.Items.AddRange(new object[] { "🇺🇸 English", "🇧🇷 Português", "🇪🇸 Español", "🇨🇳 中文" });
        _language.SelectedIndex = LanguageIndex(cfg.Language);
        _language.SelectedIndexChanged += (_, _) => RefreshLanguage();

        _titleLabel.Text = "SyncLightLite";
        LoadProfileItems();
        LoadModeItems();
        LoadColorItems();
        _reverseLedOrder.Text = T("reverseLedOrder");
        _profile.SelectedIndex = 0;
        _profile.SelectedIndexChanged += (_, _) => ApplyProfile();
        _mode.SelectedIndex = ModeIndex(cfg.Mode);
        _effectColor.SelectedIndex = ColorIndex(cfg.EffectColor);
        _effectSpeed.Value = Clamp(cfg.EffectSpeedPercent, 1, 100);

        _fps.Value = Clamp(cfg.Fps, 15, 120);
        _scale.Value = Clamp(cfg.CaptureScalePercent, 35, 100);
        _offset.Value = Clamp(cfg.HeaderOffsetPercent, 0, 65);
        _hMargin.Value = Clamp(cfg.HorizontalMarginPercent, 0, 25);
        _brightness.Value = Clamp(cfg.BrightnessPercent, 0, 150);
        _saturation.Value = Clamp(cfg.SaturationPercent, 0, 250);
        _gamma.Value = (decimal)Clamp((int)Math.Round(cfg.Gamma * 100), 40, 300);
        _threshold.Value = Clamp(cfg.ThresholdPercent, 0, 25);
        _blackFloor.Value = Clamp(cfg.BlackFloorPercent, 0, 25);
        _smoothing.Value = (decimal)Clamp((int)Math.Round(cfg.Smoothing * 100), 5, 90);
        _reverseLedOrder.Checked = cfg.ReverseLedOrder;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1, BackColor = Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(Header(), 0, 0);
        root.Controls.Add(Body(), 0, 1);

        _developerCredit.Text = T("developerCredit");
        _developerCredit.Links.Clear();
        var tanoookiIndex = _developerCredit.Text.IndexOf("Tanoooki", StringComparison.Ordinal);
        if (tanoookiIndex >= 0) _developerCredit.Links.Add(tanoookiIndex, "Tanoooki".Length, "https://github.com/Tanoooki");
        _developerCredit.LinkClicked += (_, e) => Process.Start(new ProcessStartInfo(e.Link?.LinkData?.ToString() ?? "https://github.com/Tanoooki") { UseShellExecute = true });

        _saveButton.Text = T("saveRestart");
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Click += (_, _) => SaveAndApply();
        _savePulseTimer.Tick += (_, _) => SavedPulseTick();
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        footer.Controls.Add(_developerCredit);
        footer.Controls.Add(_saveButton);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        InitializeCapturePreview();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToWorkingArea(Screen.FromControl(this).WorkingArea);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // WinForms applies the suggested DPI bounds after this event returns.
        BeginInvoke((Action)(() => FitToWorkingArea(Screen.FromControl(this).WorkingArea)));
    }

    private void FitToWorkingArea(Rectangle area)
    {
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        var width = Math.Min(Width, area.Width);
        var height = Math.Min(Height, area.Height);
        Bounds = new Rectangle(Math.Clamp(Left, area.Left, area.Right - width),
            Math.Clamp(Top, area.Top, area.Bottom - height), width, height);
    }

    private Control Header()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        panel.Controls.Add(new PixelMonitor { Location = new Point(0, 5), Size = new Size(68, 58) });
        panel.Controls.Add(_titleLabel);
        _subtitleLabel.Text = T("subtitle");
        panel.Controls.Add(_subtitleLabel);
        return panel;
    }

    private Control Body()
    {
        _settingsTabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.Normal };
        RebuildSettingsRows();
        return _settingsTabs;
    }

    private void RebuildSettingsRows()
    {
        if (_settingsTabs == null) return;
        StopCaptureOverlay();
        // Reparent reusable inputs before disposing obsolete localized containers.
        foreach (var input in _rowHelp.Keys) input.Parent = null;
        foreach (var control in new Control[] { _capturePreview, _captureNote, _captureLegend, _overlayToggle }) control.Parent = null;
        var oldPages = _settingsTabs.TabPages.Cast<TabPage>().ToArray();
        _settingsTabs.TabPages.Clear();
        foreach (var oldPage in oldPages) oldPage.Dispose();
        _rowLabels.Clear();
        _rowHelp.Clear();

        _settingsTabs.TabPages.Add(BuildTabPage(T("tabGeneral"), new (string, Control, string)[]
        {
            (T("language"), _language, T("languageHelp")),
            (T("profile"), _profile, T("profileHelp")),
            (T("invertStrip"), _reverseLedOrder, T("invertHelp")),
            ("FPS", _fps, T("fpsHelp"))
        }));

        _settingsTabs.TabPages.Add(BuildTabPage(T("tabEffects"), new (string, Control, string)[]
        {
            (T("mode"), _mode, T("modeHelp")),
            (T("effectColor"), _effectColor, T("effectColorHelp")),
            (T("effectSpeed"), _effectSpeed, T("effectSpeedHelp"))
        }));

        _settingsTabs.TabPages.Add(BuildTabPage(T("tabCapture"), new (string, Control, string)[]
        {
            (T("scale"), _scale, T("scaleHelp")),
            (T("offset"), _offset, T("offsetHelp")),
            (T("hMargin"), _hMargin, T("hMarginHelp"))
        }));

        _settingsTabs.TabPages.Add(BuildTabPage(T("tabColor"), new (string, Control, string)[]
        {
            (T("brightness"), _brightness, T("brightnessHelp")),
            (T("saturation"), _saturation, T("saturationHelp")),
            (T("gamma"), _gamma, T("gammaHelp")),
            (T("threshold"), _threshold, T("thresholdHelp")),
            (T("blackFloor"), _blackFloor, T("blackFloorHelp")),
            (T("smoothing"), _smoothing, T("smoothingHelp"))
        }));
    }

    private TabPage BuildTabPage(string title, (string Label, Control Control, string Help)[] rows)
    {
        var page = new TabPage(title) { BackColor = Panel, ForeColor = Fg, Padding = new Padding(12), AutoScroll = true };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Panel, Padding = new Padding(12), ColumnCount = 2, RowCount = rows.Length };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        for (var i = 0; i < rows.Length; i++) AddRow(grid, i, rows[i].Label, rows[i].Control, rows[i].Help);
        if (rows.Any(row => row.Control == _scale))
        {
            var capture = BuildCapturePanel();
            capture.Dock = DockStyle.Top;
            capture.AutoSize = true;
            page.Controls.Add(capture);
        }
        page.Controls.Add(grid);
        return page;
    }

    private void AddRow(TableLayoutPanel grid, int row, string label, Control control, string help)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var labelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Panel, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var rowLabel = new Label { Text = label, AutoSize = true, ForeColor = Fg, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 8, 0, 0) };
        _rowLabels.Add(rowLabel);
        _rowHelp[control] = help;
        labelPanel.Controls.Add(rowLabel);
        var icon = HelpIcon(help);
        labelPanel.Controls.Add(icon);
        grid.Controls.Add(labelPanel, 0, row);
        control.Dock = DockStyle.Fill;
        control.BackColor = Color.FromArgb(255, 253, 247);
        control.ForeColor = Fg;
        new ToolTip { AutomaticDelay = 120, AutoPopDelay = 12000 }.SetToolTip(control, help);
        grid.Controls.Add(control, 1, row);
    }

    private static Label HelpIcon(string help)
    {
        var icon = new Label
        {
            Text = "?",
            AutoSize = false,
            Width = 18,
            Height = 18,
            Margin = new Padding(8, 8, 0, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Accent,
            ForeColor = Color.White,
            Cursor = Cursors.Help
        };
        new ToolTip { AutomaticDelay = 80, AutoPopDelay = 16000 }.SetToolTip(icon, help);
        return icon;
    }

    private void LoadModeItems()
    {
        var selected = Math.Max(0, _mode.SelectedIndex);
        _mode.Items.Clear();
        _mode.Items.AddRange(new object[] { T("modeScreen"), T("modeSolid"), T("modeBreathing"), T("modeRainbow"), T("modeColorCycle"), T("modeWave") });
        _mode.SelectedIndex = Math.Min(selected, _mode.Items.Count - 1);
    }

    private void LoadColorItems()
    {
        var selected = Math.Max(0, _effectColor.SelectedIndex);
        _effectColor.Items.Clear();
        _effectColor.Items.AddRange(new object[] { T("colorWhite"), T("colorRed"), T("colorGreen"), T("colorBlue"), T("colorPurple"), T("colorCyan"), T("colorWarm") });
        _effectColor.SelectedIndex = Math.Min(selected, _effectColor.Items.Count - 1);
    }


    private void LoadProfileItems()
    {
        var selected = Math.Max(0, _profile.SelectedIndex);
        _profile.Items.Clear();
        _profile.Items.AddRange(new object[] { T("custom"), T("economy"), T("standard"), T("fluid"), T("maximum") });
        _profile.SelectedIndex = Math.Min(selected, _profile.Items.Count - 1);
    }

    private void RefreshLanguage()
    {
        _languagePreview(LanguageCode());
        ApplyLanguageToAllControls();
    }

    private void ApplyLanguageToAllControls()
    {
        _titleLabel.Text = "SyncLightLite";
        LoadProfileItems();
        LoadModeItems();
        LoadColorItems();
        _reverseLedOrder.Text = T("reverseLedOrder");
        _subtitleLabel.Text = T("subtitle");
        _saveButton.Text = T("saveRestart");
        _developerCredit.Text = T("developerCredit");
        _developerCredit.Links.Clear();
        var tanoookiIndex = _developerCredit.Text.IndexOf("Tanoooki", StringComparison.Ordinal);
        if (tanoookiIndex >= 0) _developerCredit.Links.Add(tanoookiIndex, "Tanoooki".Length, "https://github.com/Tanoooki");
        RebuildSettingsRows();
    }

    private int LanguageIndex(string? code) => NormalizeLanguage(code) switch
    {
        "pt-BR" => 1,
        "es" => 2,
        "zh-CN" => 3,
        _ => 0
    };

    private string LanguageCode() => _language.SelectedIndex switch
    {
        1 => "pt-BR",
        2 => "es",
        3 => "zh-CN",
        _ => "en"
    };

    private string Lang => LanguageCode();

    private int ModeIndex(string? mode) => NormalizeMode(mode) switch
    {
        "solid" => 1,
        "breathing" => 2,
        "rainbow" => 3,
        "colorCycle" => 4,
        "wave" => 5,
        _ => 0
    };

    private string ModeCode() => _mode.SelectedIndex switch
    {
        1 => "solid",
        2 => "breathing",
        3 => "rainbow",
        4 => "colorCycle",
        5 => "wave",
        _ => "screen"
    };

    private static string NormalizeMode(string? mode) => mode is "solid" or "breathing" or "rainbow" or "colorCycle" or "wave" ? mode : "screen";

    private int ColorIndex(string? color) => NormalizeColor(color) switch
    {
        "#ff0000" => 1,
        "#00ff00" => 2,
        "#0000ff" => 3,
        "#8000ff" => 4,
        "#00ffff" => 5,
        "#ffd080" => 6,
        _ => 0
    };

    private string ColorCode() => _effectColor.SelectedIndex switch
    {
        1 => "#ff0000",
        2 => "#00ff00",
        3 => "#0000ff",
        4 => "#8000ff",
        5 => "#00ffff",
        6 => "#ffd080",
        _ => "#ffffff"
    };

    private static string NormalizeColor(string? color) => string.IsNullOrWhiteSpace(color) ? "#ffffff" : color.ToLowerInvariant();

    private static string NormalizeLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        if (code.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (code.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        return "en";
    }

    private string T(string key) => Translations.GetValueOrDefault(Lang, Translations["en"]).GetValueOrDefault(key, Translations["en"].GetValueOrDefault(key, key));

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en"] = new() { ["subtitle"] = "A little light. A space that feels like you.", ["developerCredit"] = "Developed by Tanoooki", ["language"] = "Language", ["languageHelp"] = "Choose the UI language. Default is English when Windows language is not supported.", ["profile"] = "Profile", ["profileHelp"] = "Quick presets: Economy lowers activity; Fluid/Maximum increase response.", ["invertStrip"] = "Invert strip", ["invertHelp"] = "Invert physical LED order if colors look mirrored or your strip starts on the opposite side.", ["reverseLedOrder"] = "Invert LED order", ["scale"] = "Vertical capture scale (%)", ["offset"] = "Ignore top/header (%)", ["hMargin"] = "Horizontal margin (%)", ["brightness"] = "Brightness (%)", ["saturation"] = "Saturation (%)", ["gamma"] = "Gamma (100 = neutral)", ["threshold"] = "Change threshold (%)", ["blackFloor"] = "Black floor (%)", ["smoothing"] = "Smoothing (%)", ["saveRestart"] = "Save and restart sync", ["saved"] = "Settings saved and sync restarted.", ["savedShort"] = "Saved", ["custom"] = "Custom", ["economy"] = "Economy", ["standard"] = "Standard", ["fluid"] = "Fluid", ["maximum"] = "Maximum", ["mode"] = "Lighting mode", ["modeHelp"] = "Choose screen sync or built-in effects similar to vendor presets.", ["modeScreen"] = "Screen sync", ["modeSolid"] = "Fixed color", ["modeBreathing"] = "Breathing", ["modeRainbow"] = "Rainbow", ["modeColorCycle"] = "Color cycle", ["modeWave"] = "Wave", ["effectColor"] = "Effect color", ["effectColorHelp"] = "Base color used by fixed, breathing and wave effects.", ["effectSpeed"] = "Effect speed (%)", ["effectSpeedHelp"] = "Animation speed for breathing/rainbow/wave modes.", ["colorWhite"] = "White", ["colorRed"] = "Red", ["colorGreen"] = "Green", ["colorBlue"] = "Blue", ["colorPurple"] = "Purple", ["colorCyan"] = "Cyan", ["colorWarm"] = "Warm white", ["tabGeneral"] = "General", ["tabEffects"] = "Effects", ["tabCapture"] = "Capture", ["tabColor"] = "Colors", ["fpsHelp"] = "Target refresh rate. 15–120 FPS.", ["scaleHelp"] = "Screen height used for sampling edges. 35–100%.", ["offsetHelp"] = "Moves capture area down. Useful to ignore browser tabs/header.", ["hMarginHelp"] = "Moves sampling inward from left/right edges. 0–25%.", ["brightnessHelp"] = "Final LED intensity. 100 is neutral.", ["saturationHelp"] = "Color strength. 100 is neutral.", ["gammaHelp"] = "Brightness curve. 100 is neutral.", ["thresholdHelp"] = "Minimum change before sending a packet.", ["blackFloorHelp"] = "Brightness below this becomes black.", ["smoothingHelp"] = "Higher means less flicker and more delay." },
        ["pt-BR"] = new() { ["subtitle"] = "Um pouco de luz. Um espaço com a sua cara.", ["developerCredit"] = "Desenvolvido por Tanoooki", ["language"] = "Idioma", ["languageHelp"] = "Escolha o idioma da interface. Se o Windows não for suportado, o padrão é inglês.", ["profile"] = "Perfil", ["profileHelp"] = "Presets rápidos: Econômico reduz atividade; Fluido/Máximo aumentam resposta.", ["invertStrip"] = "Inverter fita", ["invertHelp"] = "Inverte a ordem física dos LEDs se as cores aparecem espelhadas ou se a fita começa no lado oposto.", ["reverseLedOrder"] = "Inverter ordem dos LEDs", ["scale"] = "Escala vertical da captura (%)", ["offset"] = "Ignorar cabeçalho/topo (%)", ["hMargin"] = "Margem horizontal (%)", ["brightness"] = "Brilho (%)", ["saturation"] = "Saturação (%)", ["gamma"] = "Gamma (100 = neutro)", ["threshold"] = "Threshold de mudança (%)", ["blackFloor"] = "Piso preto (%)", ["smoothing"] = "Suavização (%)", ["saveRestart"] = "Salvar e reiniciar sync", ["saved"] = "Configurações salvas e sincronização reiniciada.", ["savedShort"] = "Salvo", ["custom"] = "Personalizado", ["economy"] = "Econômico", ["standard"] = "Padrão", ["fluid"] = "Fluido", ["maximum"] = "Máximo", ["mode"] = "Modo de iluminação", ["modeHelp"] = "Escolha sincronização de tela ou efeitos embutidos parecidos com os presets do original.", ["modeScreen"] = "Sincronizar tela", ["modeSolid"] = "Cor fixa", ["modeBreathing"] = "Respirar", ["modeRainbow"] = "Arco-íris", ["modeColorCycle"] = "Ciclo de cores", ["modeWave"] = "Onda", ["effectColor"] = "Cor do efeito", ["effectColorHelp"] = "Cor base usada por cor fixa, respirar e onda.", ["effectSpeed"] = "Velocidade do efeito (%)", ["effectSpeedHelp"] = "Velocidade da animação em modos respirar/arco-íris/onda.", ["colorWhite"] = "Branco", ["colorRed"] = "Vermelho", ["colorGreen"] = "Verde", ["colorBlue"] = "Azul", ["colorPurple"] = "Roxo", ["colorCyan"] = "Ciano", ["colorWarm"] = "Branco quente", ["tabGeneral"] = "Geral", ["tabEffects"] = "Efeitos", ["tabCapture"] = "Captura", ["tabColor"] = "Cores", ["fpsHelp"] = "Taxa de atualização alvo. 15–120 FPS.", ["scaleHelp"] = "Altura da tela usada para amostrar bordas. 35–100%.", ["offsetHelp"] = "Move a área capturada para baixo. Útil para ignorar abas/cabeçalho.", ["hMarginHelp"] = "Afasta a amostragem das bordas esquerda/direita. 0–25%.", ["brightnessHelp"] = "Intensidade final dos LEDs. 100 é neutro.", ["saturationHelp"] = "Força das cores. 100 é neutro.", ["gammaHelp"] = "Curva de luminosidade. 100 é neutro.", ["thresholdHelp"] = "Mudança mínima antes de enviar novo pacote.", ["blackFloorHelp"] = "Abaixo desse brilho vira preto.", ["smoothingHelp"] = "Mais alto = menos flicker e mais atraso." },
        ["es"] = new() { ["subtitle"] = "Un poco de luz. Un espacio a tu manera.", ["developerCredit"] = "Desarrollado por Tanoooki", ["language"] = "Idioma", ["languageHelp"] = "Elige el idioma de la interfaz. Si Windows no es compatible, se usa inglés.", ["profile"] = "Perfil", ["profileHelp"] = "Ajustes rápidos: Económico reduce actividad; Fluido/Máximo aumentan respuesta.", ["invertStrip"] = "Invertir tira", ["invertHelp"] = "Invierte el orden físico de los LEDs si los colores aparecen espejados o la tira empieza en el lado opuesto.", ["reverseLedOrder"] = "Invertir orden de LEDs", ["scale"] = "Escala vertical de captura (%)", ["offset"] = "Ignorar encabezado/superior (%)", ["hMargin"] = "Margen horizontal (%)", ["brightness"] = "Brillo (%)", ["saturation"] = "Saturación (%)", ["gamma"] = "Gamma (100 = neutro)", ["threshold"] = "Umbral de cambio (%)", ["blackFloor"] = "Piso negro (%)", ["smoothing"] = "Suavizado (%)", ["saveRestart"] = "Guardar y reiniciar sync", ["saved"] = "Configuración guardada y sincronización reiniciada.", ["savedShort"] = "Guardado", ["custom"] = "Personalizado", ["economy"] = "Económico", ["standard"] = "Estándar", ["fluid"] = "Fluido", ["maximum"] = "Máximo", ["mode"] = "Modo de iluminação", ["modeHelp"] = "Escolha sincronização de tela ou efeitos embutidos parecidos com os presets do original.", ["modeScreen"] = "Sincronizar tela", ["modeSolid"] = "Cor fixa", ["modeBreathing"] = "Respirar", ["modeRainbow"] = "Arco-íris", ["modeColorCycle"] = "Ciclo de cores", ["modeWave"] = "Onda", ["effectColor"] = "Cor do efeito", ["effectColorHelp"] = "Cor base usada por cor fixa, respirar e onda.", ["effectSpeed"] = "Velocidade do efeito (%)", ["effectSpeedHelp"] = "Velocidade da animação em modos respirar/arco-íris/onda.", ["colorWhite"] = "Branco", ["colorRed"] = "Vermelho", ["colorGreen"] = "Verde", ["colorBlue"] = "Azul", ["colorPurple"] = "Roxo", ["colorCyan"] = "Ciano", ["colorWarm"] = "Branco quente", ["tabGeneral"] = "General", ["tabEffects"] = "Efectos", ["tabCapture"] = "Captura", ["tabColor"] = "Colores", ["fpsHelp"] = "Frecuencia objetivo. 15–120 FPS.", ["scaleHelp"] = "Altura de pantalla usada para muestrear bordes. 35–100%.", ["offsetHelp"] = "Mueve el área capturada hacia abajo.", ["hMarginHelp"] = "Mueve el muestreo hacia dentro desde los bordes. 0–25%.", ["brightnessHelp"] = "Intensidad final de los LEDs. 100 es neutro.", ["saturationHelp"] = "Fuerza del color. 100 es neutro.", ["gammaHelp"] = "Curva de brillo. 100 es neutro.", ["thresholdHelp"] = "Cambio mínimo antes de enviar paquete.", ["blackFloorHelp"] = "Por debajo de este brillo se vuelve negro.", ["smoothingHelp"] = "Más alto = menos parpadeo y más retraso." },
        ["zh-CN"] = new() { ["subtitle"] = "一点灯光，让空间更有你的风格。", ["developerCredit"] = "由 Tanoooki 开发", ["language"] = "语言", ["languageHelp"] = "选择界面语言。不支持的 Windows 语言默认使用英语。", ["profile"] = "配置", ["profileHelp"] = "快速预设：节能降低活动；流畅/最大提高响应。", ["invertStrip"] = "反转灯带", ["invertHelp"] = "如果颜色左右镜像，或灯带从相反一侧开始，请反转 LED 物理顺序。", ["reverseLedOrder"] = "反转 LED 顺序", ["scale"] = "垂直采集比例 (%)", ["offset"] = "忽略顶部/标题栏 (%)", ["hMargin"] = "水平边距 (%)", ["brightness"] = "亮度 (%)", ["saturation"] = "饱和度 (%)", ["gamma"] = "伽马 (100 = 中性)", ["threshold"] = "变化阈值 (%)", ["blackFloor"] = "黑色阈值 (%)", ["smoothing"] = "平滑 (%)", ["saveRestart"] = "保存并重启同步", ["saved"] = "设置已保存，同步已重启。", ["savedShort"] = "已保存", ["custom"] = "自定义", ["economy"] = "节能", ["standard"] = "标准", ["fluid"] = "流畅", ["maximum"] = "最大", ["mode"] = "灯光模式", ["modeHelp"] = "选择屏幕同步或内置效果。", ["modeScreen"] = "屏幕同步", ["modeSolid"] = "固定颜色", ["modeBreathing"] = "呼吸", ["modeRainbow"] = "彩虹", ["modeColorCycle"] = "颜色循环", ["modeWave"] = "波浪", ["effectColor"] = "效果颜色", ["effectColorHelp"] = "固定、呼吸和波浪效果的基础颜色。", ["effectSpeed"] = "效果速度 (%)", ["effectSpeedHelp"] = "动画速度。", ["colorWhite"] = "白色", ["colorRed"] = "红色", ["colorGreen"] = "绿色", ["colorBlue"] = "蓝色", ["colorPurple"] = "紫色", ["colorCyan"] = "青色", ["colorWarm"] = "暖白", ["tabGeneral"] = "常规", ["tabEffects"] = "效果", ["tabCapture"] = "采集", ["tabColor"] = "颜色", ["fpsHelp"] = "目标刷新率。15–120 FPS。", ["scaleHelp"] = "用于边缘采样的屏幕高度。35–100%。", ["offsetHelp"] = "将采集区域向下移动。", ["hMarginHelp"] = "从左右边缘向内采样。0–25%。", ["brightnessHelp"] = "LED 最终亮度。100 为中性。", ["saturationHelp"] = "颜色强度。100 为中性。", ["gammaHelp"] = "亮度曲线。100 为中性。", ["thresholdHelp"] = "发送数据包前的最小变化。", ["blackFloorHelp"] = "低于此亮度则变为黑色。", ["smoothingHelp"] = "越高闪烁越少，但延迟越大。" }
    };

    private void ApplyProfile()
    {
        switch (_profile.SelectedIndex)
        {
            case 1: _fps.Value = 30; _smoothing.Value = 55; _threshold.Value = 4; break;
            case 2: _fps.Value = 30; _smoothing.Value = 35; _threshold.Value = 1; break;
            case 3: _fps.Value = 60; _smoothing.Value = 25; _threshold.Value = 0; break;
            case 4: _fps.Value = 120; _smoothing.Value = 15; _threshold.Value = 0; break;
        }
    }

    private void SaveAndApply()
    {
        _save(new AppConfig
        {
            Language = LanguageCode(),
            Mode = ModeCode(),
            EffectColor = ColorCode(),
            EffectSpeedPercent = (int)_effectSpeed.Value,
            Fps = (int)_fps.Value,
            CaptureScalePercent = (int)_scale.Value,
            HeaderOffsetPercent = (int)_offset.Value,
            HorizontalMarginPercent = (int)_hMargin.Value,
            BrightnessPercent = (int)_brightness.Value,
            SaturationPercent = (int)_saturation.Value,
            Gamma = (double)_gamma.Value / 100.0,
            ThresholdPercent = (int)_threshold.Value,
            BlackFloorPercent = (int)_blackFloor.Value,
            Smoothing = (double)_smoothing.Value / 100.0,
            ReverseLedOrder = _reverseLedOrder.Checked
        });
        ShowSavedPulse();
    }

    private void ShowSavedPulse()
    {
        _savePulseStep = 0;
        _savePulseTimer.Stop();
        _saveButton.Text = T("savedShort");
        _saveButton.BackColor = Color.FromArgb(46, 160, 67);
        _saveButton.FlatAppearance.BorderColor = Color.FromArgb(86, 211, 100);
        _saveButton.FlatAppearance.BorderSize = 2;
        _savePulseTimer.Start();
    }

    private void SavedPulseTick()
    {
        _savePulseStep++;
        var Success = Color.FromArgb(46, 160, 67);
        var bright = Color.FromArgb(86, 211, 100);
        _saveButton.BackColor = _savePulseStep % 2 == 0 ? Success : bright;
        if (_savePulseStep < 6) return;
        _savePulseTimer.Stop();
        _saveButton.Text = T("saveRestart");
        _saveButton.BackColor = Accent;
        _saveButton.FlatAppearance.BorderSize = 0;
    }


    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static string FindIconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "SyncLightLite.ico");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "SyncLightLite.ico");
    }

    private static Icon ProgramIcon()
    {
        try
        {
            return new Icon(FindIconPath());
        }
        catch { return SystemIcons.Application; }
    }
}
