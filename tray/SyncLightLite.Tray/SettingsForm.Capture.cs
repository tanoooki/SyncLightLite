namespace SyncLightLite.Tray;

internal sealed partial class SettingsForm
{
    private readonly CaptureOverlay _captureOverlay = new();
    private readonly CheckBox _overlayToggle = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 4) };
    private readonly System.Windows.Forms.Timer _overlayTimer = new() { Interval = 30000 };
    private readonly CapturePreview _capturePreview = new() { Dock = DockStyle.Fill, MinimumSize = new Size(200, 150) };
    private readonly Label _captureNote = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
    private readonly Label _captureLegend = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };

    private Control BuildCapturePanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12), BackColor = Panel };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(_captureNote, 0, 0);
        panel.Controls.Add(_capturePreview, 0, 1);
        panel.Controls.Add(_captureLegend, 0, 2);
        panel.Controls.Add(_overlayToggle, 0, 3);
        UpdateCapturePreview();
        return panel;
    }

    private void InitializeCapturePreview()
    {
        _overlayToggle.CheckedChanged += (_, _) => ToggleCaptureOverlay();
        _overlayTimer.Tick += (_, _) => StopCaptureOverlay();
        _settingsTabs!.SelectedIndexChanged += (_, _) => StopCaptureOverlay();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) StopCaptureOverlay(); };
        VisibleChanged += (_, _) => { if (!Visible) StopCaptureOverlay(); };
        FormClosing += (_, _) => StopCaptureOverlay();
        _scale.ValueChanged += (_, _) => UpdateCapturePreview();
        _offset.ValueChanged += (_, _) => UpdateCapturePreview();
        _hMargin.ValueChanged += (_, _) => UpdateCapturePreview();
        _reverseLedOrder.CheckedChanged += (_, _) => UpdateCapturePreview();
        UpdateCapturePreview();
    }

    private void UpdateCapturePreview()
    {
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var geometry = CaptureGeometry.Create(screen.Width, screen.Height, (double)_scale.Value / 100,
            (double)_offset.Value / 100, (double)_hMargin.Value / 100, _reverseLedOrder.Checked);
        _capturePreview.Geometry = geometry;
        _captureOverlay.Geometry = geometry;
        _overlayToggle.Text = CaptureText(_overlayToggle.Checked ? "overlayOff" : "overlayOn");
        _captureNote.Text = CaptureText("note");
        _captureLegend.Text = string.Format(CaptureText("legend"), geometry.EffectiveScale * 100);
        _capturePreview.AccessibleName = CaptureText("title");
        _capturePreview.AccessibleDescription = _captureLegend.Text;
    }

    private void StopCaptureOverlay()
    {
        _overlayTimer.Stop();
        _captureOverlay.Hide();
        _overlayToggle.Checked = false;
    }

    private void ToggleCaptureOverlay()
    {
        if (!_overlayToggle.Checked)
        {
            StopCaptureOverlay();
            _overlayToggle.Text = CaptureText("overlayOn");
            return;
        }
        UpdateCapturePreview();
        var bounds = Screen.PrimaryScreen?.Bounds;
        if (!Visible || WindowState == FormWindowState.Minimized || _settingsTabs?.SelectedIndex != 2 ||
            bounds == null || !_captureOverlay.TryShowProtected(bounds.Value))
        {
            StopCaptureOverlay();
            _captureNote.Text = CaptureText("unavailable");
            return;
        }
        _overlayTimer.Start();
        _overlayToggle.Text = CaptureText("overlayOff");
    }

    private string CaptureText(string key) => CaptureTranslations[Lang][key];
    private static readonly Dictionary<string, Dictionary<string, string>> CaptureTranslations = new()
    {
        ["en"] = new() {
            ["overlayOn"] = "Show desktop guides · 30 seconds", ["overlayOff"] = "Hide desktop guides now", ["unavailable"] = "Desktop guides unavailable: Windows could not exclude them from capture. Use the preview below; save to apply.",
            ["title"] = "Primary monitor · capture preview",
            ["note"] = "Preview only · changes appear here immediately. Save and restart sync to apply them to your lights.",
            ["legend"] = "Teal: sides · amber: top · square: LED 1\nRight → top → left (reversed when enabled). No bottom strip.\nEffective height: {0:0.#}% · fixed sample thickness · 10× downsampled." },
        ["pt-BR"] = new() {
            ["overlayOn"] = "Mostrar guias na tela · 30 segundos", ["overlayOff"] = "Ocultar guias da tela agora", ["unavailable"] = "Guias indisponíveis: o Windows não pôde excluí-las da captura. Use a prévia abaixo; salve para aplicar.",
            ["title"] = "Monitor principal · prévia da captura",
            ["note"] = "Só uma prévia · os ajustes aparecem aqui na hora. Salve e reinicie a sincronização para aplicar às luzes.",
            ["legend"] = "Verde: laterais · âmbar: topo · quadrado: LED 1\nDireita → topo → esquerda (ou ordem invertida). Sem faixa inferior.\nAltura efetiva: {0:0.#}% · espessura fixa · resolução reduzida 10×." },
        ["es"] = new() {
            ["overlayOn"] = "Mostrar guías en pantalla · 30 segundos", ["overlayOff"] = "Ocultar guías de pantalla ahora", ["unavailable"] = "Guías no disponibles: Windows no pudo excluirlas de la captura. Usa la vista previa; guarda para aplicar.",
            ["title"] = "Monitor principal · vista previa",
            ["note"] = "Solo vista previa · los ajustes aparecen al instante. Guarda y reinicia la sincronización para aplicarlos a las luces.",
            ["legend"] = "Verde: lados · ámbar: arriba · cuadrado: LED 1\nDerecha → arriba → izquierda (o invertido). Sin tira inferior.\nAltura efectiva: {0:0.#}% · grosor fijo · resolución reducida 10×." },
        ["zh-CN"] = new() {
            ["overlayOn"] = "显示桌面辅助线 · 30 秒", ["overlayOff"] = "立即隐藏桌面辅助线", ["unavailable"] = "无法显示辅助线：Windows 无法将其排除在采集之外。请使用下方预览，保存后应用。",
            ["title"] = "主显示器 · 采集预览",
            ["note"] = "仅预览 · 调整会立即显示。保存并重启同步后才会应用到灯光。",
            ["legend"] = "青色：两侧 · 琥珀色：顶部 · 方块：LED 1\n右 → 顶 → 左（可反转）。底部没有灯带。\n有效高度：{0:0.#}% · 固定采样厚度 · 分辨率缩小 10 倍。" }
    };

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x7E && _settingsTabs != null) // WM_DISPLAYCHANGE
        {
            StopCaptureOverlay();
            UpdateCapturePreview();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _overlayTimer.Stop();
            _overlayTimer.Dispose();
            _captureOverlay.Dispose();
            _savePulseTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
