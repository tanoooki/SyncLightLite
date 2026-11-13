using System.Reflection;
using System.Text.Json;

internal static class Harness
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    static int saves;
    static void Saved<T>(T _) => saves++;
    static object Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void TestUi(Assembly assembly)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var configType = assembly.GetType("SyncLightLite.Tray.AppConfig")!;
        foreach (var language in new[] { "en", "pt-BR", "es", "zh-CN" })
        {
            var config = Activator.CreateInstance(configType)!;
            configType.GetProperty("Language")!.SetValue(config, language);
            var callback = typeof(Harness).GetMethod(nameof(Saved), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(configType).CreateDelegate(typeof(Action<>).MakeGenericType(configType));
            using var form = (Form)Activator.CreateInstance(assembly.GetType("SyncLightLite.Tray.SettingsForm")!, new object[] { config, callback, (Action<string>)(_ => { }) })!;
            Check(form.BackColor == Color.FromArgb(247, 239, 219), "Settings should use warm cream, not noir");
            var previewField = form.GetType().GetField("_capturePreview", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(previewField != null, "Capture tab must show live geometry preview");
            var preview = (Control)previewField!.GetValue(form)!;
            var tabs = (TabControl)Field(form, "_settingsTabs");
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Opacity = 0; // Work-area fitting may move the test window onscreen.
            form.Show();
            tabs.SelectedIndex = 2;
            Application.DoEvents();
            ((NumericUpDown)Field(form, "_scale")).Value = 90;
            ((NumericUpDown)Field(form, "_offset")).Value = 23;
            ((NumericUpDown)Field(form, "_hMargin")).Value = 10;
            var geometry = preview.GetType().GetProperty("Geometry")!.GetValue(preview)!;
            Check(Math.Abs((double)geometry.GetType().GetProperty("EffectiveScale")!.GetValue(geometry)! - .77) < .0001, "Unsaved controls must immediately clamp preview height");
            var note = (Label)Field(form, "_captureNote");
            Check(note.Text.Contains(language switch { "pt-BR" => "Salve", "es" => "Guarda", "zh-CN" => "保存", _ => "Save" }), "Preview must explain save/apply in each language");
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            var pixelCount = 0;
            for (var x=0; x<image.Width; x+=3) for(var y=0; y<image.Height; y+=3)
                if(image.GetPixel(x,y).ToArgb() == Color.FromArgb(38, 112, 105).ToArgb()) pixelCount++;
            image.Save(Path.Combine(AppContext.BaseDirectory, "preview-" + language + ".png"));
            Console.WriteLine($"layout form={form.ClientSize} tabs={tabs.Bounds} preview={preview.Bounds} visible={preview.Visible} teal={pixelCount}");
            Check(pixelCount > 20, "Drawn preview must contain teal sample outlines");
            var oldPage = tabs.TabPages[2];
            var languageBox = (ComboBox)Field(form, "_language");
            var previousLanguage = languageBox.SelectedIndex;
            languageBox.SelectedIndex = (previousLanguage + 1) % 4;
            Check(oldPage.IsDisposed, "Language refresh must dispose old tab containers without disposing reused controls");
            Check(!preview.IsDisposed, "Language refresh must preserve preview controls");
            languageBox.SelectedIndex = previousLanguage;
            tabs.SelectedIndex = 2;
            // A 1366x768 desktop leaves only 728px after the taskbar.
            form.Size = new Size(760, 728);
            Application.DoEvents();
            Check(form.Height <= 728, "Settings must fit a 728px work area, including window borders");
            form.Size = form.MinimumSize;
            Application.DoEvents();
            var legend = (Label)Field(form, "_captureLegend");
            Check(preview.Bottom <= legend.Top, "Preview must not overlap legend at minimum size");
            var toggleField = form.GetType().GetField("_overlayToggle", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(toggleField != null, "Capture tab needs a visible overlay on/off switch");
            var toggle = (CheckBox)toggleField!.GetValue(form)!;
            Check(toggle.Visible && toggle.Text.Length > 5, $"Overlay switch must be visible and localized: language={language} form={form.Visible} tab={tabs.SelectedIndex} parent={toggle.Parent?.Visible} text={toggle.Text}");
            var overlay = (Form)Field(form, "_captureOverlay");
            var styles = (CreateParams)overlay.GetType().GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
            Check((styles.ExStyle & 0x08000020) == 0x08000020, "Overlay must be nonactivating and click-through");
            var timer = (System.Windows.Forms.Timer)Field(form, "_overlayTimer");
            Check(timer.Interval == 30000, "Overlay must expire after 30 seconds");
            // Exercise native exclusion, never bypass it to display an unprotected overlay.
            toggle.Checked = true;
            Application.DoEvents();
            Check(!overlay.Visible || toggle.Checked, "Overlay and switch state must agree");
            Console.WriteLine($"{language}: protected native overlay visible={overlay.Visible}");
            if (overlay.Visible)
            {
                Check(GetWindowDisplayAffinity(overlay.Handle, out var affinity) && affinity == 0x11, "Visible overlay must be excluded from capture");
                Check(overlay.Bounds == Screen.PrimaryScreen!.Bounds, "Overlay must follow primary monitor, not settings monitor");
                ((NumericUpDown)Field(form, "_hMargin")).Value = 25;
                Check(ReferenceEquals(preview.GetType().GetProperty("Geometry")!.GetValue(preview), overlay.GetType().GetProperty("Geometry")!.GetValue(overlay)), "Overlay must update with unsaved preview");
            }
            var displayChanged = Message.Create(form.Handle, 0x7E, IntPtr.Zero, IntPtr.Zero);
            form.GetType().GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { displayChanged });
            Check(!overlay.Visible && !toggle.Checked, "Display change must stop stale desktop guides");
            toggle.Checked = true;
            tabs.SelectedIndex = 0;
            Check(!overlay.Visible && !toggle.Checked && !timer.Enabled, "Tab change must stop overlay");
            tabs.SelectedIndex = 2;
            toggle.Checked = true;
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timer, new object[] { EventArgs.Empty });
            Check(!overlay.Visible && !toggle.Checked, "Timeout must stop overlay");
            toggle.Checked = true;
            form.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            Check(!overlay.Visible && !toggle.Checked, "Minimize must stop overlay");
            form.WindowState = FormWindowState.Normal;
            toggle.Checked = true;
            form.Close();
            Check(overlay.IsDisposed, "Closing settings must dispose overlay");
            Check(saves == 0, "Preview changes must never save or restart the engine");
            form.Dispose();
            Check(preview.IsDisposed, "Preview must be disposed with settings");
        }
        Console.WriteLine("WinForms preview: four languages, real controls, geometry, drawing and disposal passed");
    }

    static void TestSmallScreens(Assembly assembly)
    {
        var configType = assembly.GetType("SyncLightLite.Tray.AppConfig")!;
        var formType = assembly.GetType("SyncLightLite.Tray.SettingsForm")!;
        var fit = formType.GetMethod("FitToWorkingArea", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(fit != null, "Settings must clamp initial and DPI-scaled bounds to the monitor working area");
        foreach (var language in new[] { "en", "pt-BR", "es", "zh-CN" })
        foreach (var scale in new[] { 1f, 1.25f, 1.5f })
        foreach (var size in new[] { new Size(1366, 728), new Size(1024, 560) })
        {
            var config = Activator.CreateInstance(configType)!;
            configType.GetProperty("Language")!.SetValue(config, language);
            var callback = typeof(Harness).GetMethod(nameof(Saved), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(configType).CreateDelegate(typeof(Action<>).MakeGenericType(configType));
            using var form = (Form)Activator.CreateInstance(formType, new object[] { config, callback, (Action<string>)(_ => { }) })!;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Opacity = 0; // Work-area fitting may move the test window onscreen.
            form.Show();
            form.Scale(new SizeF(scale, scale)); // Exercise real WinForms control scaling, not size arithmetic.
            var area = new Rectangle(-20000, -20000, size.Width, size.Height);
            fit!.Invoke(form, new object[] { area });
            Application.DoEvents();
            Check(area.Contains(form.Bounds), $"{language}/{scale}: outer window must fit {area}, got {form.Bounds}");
            Check(form.MinimumSize.Width <= area.Width && form.MinimumSize.Height <= area.Height, "Minimum size must not defeat the work-area clamp");
            var tabs = (TabControl)Field(form, "_settingsTabs");
            var save = (Button)Field(form, "_saveButton");
            var saveBounds = save.RectangleToScreen(save.ClientRectangle);
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                Application.DoEvents();
                Check(save.Visible && save.Enabled && area.Contains(saveBounds), $"Save must remain accessible: {language}/{scale}/{size} tab={tabs.SelectedIndex} visible={save.Visible} enabled={save.Enabled} save={saveBounds} form={form.Bounds}");
                for (Control? parent = save.Parent; parent != null; parent = parent.Parent)
                    Check(parent.RectangleToScreen(parent.ClientRectangle).Contains(saveBounds), "Save must not be clipped by an ancestor");
                Check(!tabs.RectangleToScreen(tabs.ClientRectangle).IntersectsWith(saveBounds), "Scrolling content must not overlap Save");
            }
            tabs.SelectedIndex = 2;
            var capture = tabs.TabPages[2];
            var toggle = (CheckBox)Field(form, "_overlayToggle");
            capture.ScrollControlIntoView(toggle);
            Application.DoEvents();
            Check(capture.RectangleToScreen(capture.ClientRectangle).Contains(toggle.RectangleToScreen(toggle.ClientRectangle)), "Last capture control must be reachable by scrolling");
            var preview = (Control)Field(form, "_capturePreview");
            var legend = (Control)Field(form, "_captureLegend");
            Check(preview.Bottom <= legend.Top, "Scaled preview must not overlap legend");
            Check(save.RectangleToScreen(save.ClientRectangle) == saveBounds, "Scrolling must leave Save fixed");
            Console.WriteLine($"small-screen {language} scale={scale} area={size} form={form.Size} scroll={capture.VerticalScroll.Visible}: passed");
        }
        Check(saves == 0, "Layout tests must not save settings");
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.Load("SyncLightLite");
            if (args[0] == "ui") { TestUi(assembly); TestSmallScreens(assembly); return 0; }
            var type = assembly.GetType("CaptureGeometry");
            if (type == null) throw new Exception("CaptureGeometry must expose real mapper sample rectangles");
            foreach (var line in File.ReadLines(args[0]))
            {
                using var json = JsonDocument.Parse(line);
                var a = json.RootElement;
                var result = type.GetMethod("Create")!.Invoke(null, new object[] {
                    a.GetProperty("width").GetInt32(), a.GetProperty("height").GetInt32(),
                    a.GetProperty("scale").GetDouble(), a.GetProperty("offset").GetDouble(),
                    a.GetProperty("margin").GetDouble(), a.GetProperty("reverse").GetBoolean(), 71 });
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
