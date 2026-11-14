using System.Runtime.InteropServices;

namespace SyncLightLite.Tray;

// Owned by the tray UI thread, never by SettingsForm. A hidden top-level HWND
// also receives session-end broadcasts; message-only windows do not.
internal sealed class PowerWindow : NativeWindow, IDisposable
{
    internal static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private readonly Action<int> _display;
    private readonly Action<string> _log;
    private readonly Action _suspend, _resume, _shutdown;
    private readonly PowerRegistration _displayRegistration, _suspendRegistration;
    private bool _disposed;
    private Action<bool>? _locked;
    private bool _sessionRegistered;
    private readonly int _sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;

    public PowerWindow(Action<int> display, Action suspend, Action resume, Action shutdown, Action<string> log,
        Action registrationFailed)
        : this(display, suspend, resume, shutdown, log, registrationFailed,
            hwnd =>
            {
                var guid = SessionDisplayStatus;
                return new PowerRegistration(() => RegisterPowerSettingNotification(hwnd, ref guid, 0),
                    UnregisterPowerSettingNotification, log, "display");
            },
            hwnd => new PowerRegistration(() => RegisterSuspendResumeNotification(hwnd, 0),
                UnregisterSuspendResumeNotification, log, "suspend")) { }

    // Registration may synchronously send the initial display status into WndProc.
    // Bind callbacks before creating/registering the HWND; Enable follows construction.
    internal PowerWindow(Action<int> display, Action suspend, Action resume, Action shutdown, Action<string> log,
        Action registrationFailed, Func<nint, PowerRegistration> registerDisplay, Func<nint, PowerRegistration> registerSuspend)
    {
        _display = display; _suspend = suspend; _resume = resume; _shutdown = shutdown; _log = log;
        CreateHandle(new CreateParams { Caption = "SyncLightLite.Power", Style = 0, ExStyle = 0x80 });
        _displayRegistration = registerDisplay(Handle);
        _suspendRegistration = registerSuspend(Handle);
        if (!_displayRegistration.IsRegistered || !_suspendRegistration.IsRegistered) registrationFailed();
    }

    protected override void WndProc(ref Message m)
    {
        if (!_disposed && m.Msg == 0x2b1)
        {
            _log($"[power-native] msg=0x2b1 event=0x{m.WParam:x} session={m.LParam}");
            if (m.LParam == _sessionId && m.WParam is 7 or 8) _locked?.Invoke(m.WParam == 7);
        }
        if (!_disposed && m.Msg is 0x218 or 0x16)
        {
            var detail = "";
            if (m.Msg == 0x218 && m.WParam == 0x8013 && m.LParam != 0)
            {
                var length = Marshal.ReadInt32(m.LParam, 16);
                detail = $" guid={Marshal.PtrToStructure<Guid>(m.LParam)} length={length}";
                if (length == 4) detail += $" value={Marshal.ReadInt32(m.LParam, 20)}";
            }
            _log($"[power-native] msg=0x{m.Msg:x} event=0x{m.WParam:x}{detail}");
        }
        if (!_disposed && m.Msg == 0x218)
        {
            switch ((int)m.WParam)
            {
                case 0x8013:
                    if (m.LParam != 0 && Marshal.PtrToStructure<Guid>(m.LParam) == SessionDisplayStatus
                        && Marshal.ReadInt32(m.LParam, 16) == 4)
                    {
                        var value = Marshal.ReadInt32(m.LParam, 20);
                        if (value is >= 0 and <= 2) _display(value);
                    }
                    break;
                case 4: _suspend(); break;
                case 0x12:
                case 7: _resume(); break;
            }
            m.Result = 1;
            return;
        }
        if (!_disposed && m.Msg == 0x16 && m.WParam != 0) _shutdown(); // WM_ENDSESSION, not a cancelled query
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sessionRegistered && !WTSUnRegisterSessionNotification(Handle))
            _log($"[power] WTS unregister failed: {Marshal.GetLastWin32Error()}");
        _displayRegistration.Dispose();
        _suspendRegistration.Dispose();
        DestroyHandle();
    }

    public void RegisterSession(Action<bool> locked, Action failed)
    {
        _locked = locked;
        _sessionRegistered = WTSRegisterSessionNotification(Handle, 0); // NOTIFY_FOR_THIS_SESSION
        nint data = 0;
        try
        {
            if (!_sessionRegistered) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (!WTSQuerySessionInformationW(0, _sessionId, 25, out data, out var bytes))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // WTSINFOEX level 1: DWORD Level, 8-byte-aligned union; its first
            // fields are SessionId, SessionState, SessionFlags (Windows 10+).
            if (bytes < 20 || Marshal.ReadInt32(data) != 1) throw new InvalidOperationException("invalid WTSInfoEx level/length");
            var flags = Marshal.ReadInt32(data, 16);
            if (flags is not (0 or 1)) throw new InvalidOperationException("unknown WTS lock state");
            _log($"[power-native] initial WTS session={_sessionId} flags={flags}");
            locked(flags == 0);
        }
        catch (Exception ex) { _log("[power] WTS registration/initial state failed: " + ex.Message); failed(); }
        finally { if (data != 0) WTSFreeMemory(data); }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(nint server, int session, int info, out nint data, out int bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterSuspendResumeNotification(nint recipient, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterSuspendResumeNotification(nint handle);
}
