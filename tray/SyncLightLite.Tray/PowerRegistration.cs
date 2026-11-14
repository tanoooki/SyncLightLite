using System.Runtime.InteropServices;

namespace SyncLightLite.Tray;

internal sealed class PowerRegistration : IDisposable
{
    private nint _handle;
    private readonly Func<nint, bool> _release;
    private readonly Action<string> _log;
    private readonly string _name;
    public bool IsRegistered => _handle != 0;
    public PowerRegistration(Func<nint> register, Func<nint, bool> release, Action<string> log, string name)
    {
        _release = release; _log = log; _name = name;
        _handle = register();
        if (_handle == 0) log($"[power] {name} registration failed: {Marshal.GetLastWin32Error()}");
    }
    public void Dispose()
    {
        var handle = _handle;
        _handle = 0;
        if (handle != 0 && !_release(handle))
            _log($"[power] {_name} unregister failed: {Marshal.GetLastWin32Error()}");
    }
}
