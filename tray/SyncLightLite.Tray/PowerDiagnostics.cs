namespace SyncLightLite.Tray;

internal static class PowerDiagnostics
{
    public static string Format(string line) => $"{DateTimeOffset.Now:O} {line}";
}
