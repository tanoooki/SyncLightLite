using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Management;
using Microsoft.Win32;

namespace SyncLightLite.Setup;

internal static class Program
{
    private const string AppName = "SyncLightLite";
    private const string Version = "1.0.1";
    private const string Publisher = "SyncLightLite contributors";
    private const string ShutdownTaskName = "SyncLightLite-Off-On-Shutdown";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/quiet", StringComparison.OrdinalIgnoreCase));
            var uninstall = args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
            if (uninstall)
            {
                Uninstall(quiet);
                if (!quiet) MessageBox.Show("SyncLightLite foi desinstalado.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Install(quiet);
            if (!quiet) MessageBox.Show("SyncLightLite foi instalado com sucesso.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SyncLightLite Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    private static string ProgramsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
    private static string AppFolder => Path.Combine(ProgramsDir, AppName);
    private static string StartupDir => Path.Combine(ProgramsDir, "Startup");
    private static string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    private static string ExePath => Path.Combine(InstallDir, "dist", "tray", "SyncLightLite.exe");
    private static string IconPath => Path.Combine(InstallDir, "assets", "SyncLightLite.ico");
    private static string OffCmd => Path.Combine(InstallDir, "scripts", "SyncLightLite-off.cmd");
    private static string InstalledUninstaller => Path.Combine(InstallDir, "Uninstall-SyncLightLite.exe");
    private static string ConfigPath => Path.Combine(InstallDir, "config.json");

    private static void Install(bool quiet)
    {
        var source = Path.Combine(AppContext.BaseDirectory, AppName);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Payload folder not found: {source}");

        Directory.CreateDirectory(InstallDir);
        Directory.CreateDirectory(AppFolder);
        Directory.CreateDirectory(StartupDir);
        Directory.CreateDirectory(LogDir);

        var previousConfig = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
        StopRunningApp();
        KillStaleLiveEngines();
        CopyDirectory(source, InstallDir, overwrite: true);
        if (previousConfig != null)
        {
            File.WriteAllText(ConfigPath, previousConfig);
            SeedDetectedLanguage(overwriteExisting: false);
        }
        else SeedDetectedLanguage(overwriteExisting: true);
        File.Copy(Process.GetCurrentProcess().MainModule!.FileName!, InstalledUninstaller, overwrite: true);

        RewriteInstalledHelperScripts();
        RemoveOldStartupCmdLaunchers();
        CreateShortcuts();
        DeleteShutdownTask();
        RegisterInstalledApp();
        if (!quiet) StartInstalledApp();
    }


    private static void SeedDetectedLanguage(bool overwriteExisting)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(ConfigPath))?.AsObject() ?? new JsonObject();
            if (!overwriteExisting && json.TryGetPropertyValue("language", out var existing) && !string.IsNullOrWhiteSpace(existing?.ToString())) return;
            json["language"] = DetectLanguage();
            File.WriteAllText(ConfigPath, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string DetectLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (name.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        return "en";
    }


    private static void StartInstalledApp()
    {
        if (!File.Exists(ExePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { Arguments = string.Empty, WorkingDirectory = InstallDir, UseShellExecute = true });
        }
        catch { }
    }

    private static void Uninstall(bool quiet)
    {
        StopRunningApp();
        KillStaleLiveEngines();
        DeleteShutdownTask();
        TryDelete(Path.Combine(ProgramsDir, "SyncLightLite.lnk"));
        TryDelete(Path.Combine(StartupDir, "SyncLightLite.lnk"));
        TryDelete(Path.Combine(StartupDir, "SyncLightLite.cmd"));
        TryDelete(Path.Combine(StartupDir, "SyncLightLite-start.cmd"));
        TryDeleteDirectory(AppFolder);
        RemoveInstalledAppRegistration();

        // If this exe is running from inside InstallDir, remove through a delayed cmd after exit.
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (currentExe.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            var cmd = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        }
        else
        {
            TryDeleteDirectory(InstallDir);
        }
    }

    private static void RewriteInstalledHelperScripts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OffCmd)!);
        File.WriteAllText(OffCmd, $"@echo off\r\nsetlocal\r\ncd /d \"{InstallDir}\"\r\nif not exist \"%LOCALAPPDATA%\\SyncLightLite\" mkdir \"%LOCALAPPDATA%\\SyncLightLite\"\r\nnode src\\live.js off >> \"%LOCALAPPDATA%\\SyncLightLite\\synclightlite.log\" 2>&1\r\n");
        File.WriteAllText(Path.Combine(InstallDir, "scripts", "SyncLightLite-tray.cmd"), $"@echo off\r\nsetlocal\r\nstart \"SyncLightLite\" \"{ExePath}\" --minimized\r\n");
    }

    private static void RemoveOldStartupCmdLaunchers()
    {
        TryDelete(Path.Combine(StartupDir, "SyncLightLite.cmd"));
        TryDelete(Path.Combine(StartupDir, "SyncLightLite-start.cmd"));
    }

    private static void CreateShortcuts()
    {
        CreateShortcut(Path.Combine(ProgramsDir, "SyncLightLite.lnk"), ExePath, "", "SyncLightLite");
        CreateShortcut(Path.Combine(AppFolder, "SyncLightLite.lnk"), ExePath, "", "SyncLightLite");
        CreateShortcut(Path.Combine(AppFolder, "Apagar luz.lnk"), OffCmd, "", "Apagar luz SyncLightLite");
        CreateShortcut(Path.Combine(StartupDir, "SyncLightLite.lnk"), ExePath, "--minimized", "SyncLightLite startup");
    }

    private static void CreateShortcut(string path, string target, string args, string description)
    {
        var escapedPath = Ps(path);
        var escapedTarget = Ps(target);
        var escapedArgs = Ps(args);
        var escapedDescription = Ps(description);
        var escapedWorkingDirectory = Ps(InstallDir);
        var escapedIcon = Ps(IconPath + ",0");
        var command = "$w=New-Object -ComObject WScript.Shell;" +
                      $"$s=$w.CreateShortcut('{escapedPath}');" +
                      $"$s.TargetPath='{escapedTarget}';" +
                      $"$s.Arguments='{escapedArgs}';" +
                      $"$s.WorkingDirectory='{escapedWorkingDirectory}';" +
                      $"$s.Description='{escapedDescription}';" +
                      $"$s.IconLocation='{escapedIcon}';" +
                      "$s.Save()";
        Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"");
    }

    private static void DeleteShutdownTask()
    {
        try { Run("schtasks.exe", $"/Delete /TN \"{ShutdownTaskName}\" /F"); } catch { }
    }

    private static void RegisterInstalledApp()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SyncLightLite");
        if (key == null) throw new InvalidOperationException("Could not create uninstall registry key.");
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("DisplayIcon", IconPath + ",0");
        key.SetValue("UninstallString", $"\"{InstalledUninstaller}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledUninstaller}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", Math.Max(1, DirectorySizeKb(InstallDir)), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    private static void RemoveInstalledAppRegistration()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SyncLightLite", throwOnMissingSubKey: false); } catch { }
    }


    private static void KillStaleLiveEngines()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'");
            foreach (ManagementObject process in searcher.Get())
            {
                var commandLine = (process["CommandLine"] as string) ?? string.Empty;
                var normalized = commandLine.Replace('/', '\\');
                if (!normalized.Contains("src\\live.js live", StringComparison.OrdinalIgnoreCase)) continue;
                var pid = Convert.ToInt32(process["ProcessId"]);
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }

    private static void StopRunningApp()
    {
        foreach (var process in Process.GetProcessesByName("SyncLightLite"))
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(1500); } catch { }
        }
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static int DirectorySizeKb(string directory)
    {
        try { return (int)(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024); }
        catch { return 1; }
    }

    private static void Run(string file, string args)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
        process?.WaitForExit(10000);
    }

    private static string Ps(string value) => value.Replace("'", "''");
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
}
