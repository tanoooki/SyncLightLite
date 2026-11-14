namespace SyncLightLite.Tray;

// All entry points, timer ticks and engine operations run on the tray UI thread.
// Delegates isolate process/HID side effects from deterministic lifecycle tests.
internal sealed class PowerLifecycle
{
    private readonly PowerState _state = new();
    private readonly Func<bool> _alive;
    private readonly Action _start;
    private readonly Action<bool, bool, int?> _stop;
    private readonly Action<bool> _timer;
    private readonly Action<string> _log;
    private bool _recovering;
    private int _attempts, _ticks;

    public PowerLifecycle(Func<bool> alive, Action start, Action<bool, bool, int?> stop,
        Action<bool> timer, Action<string> log)
    { _alive = alive; _start = start; _stop = stop; _timer = timer; _log = log; }

    public bool CanRun => _state.CanRun;
    public void RegistrationFailed()
    {
        var action = _state.RegistrationFailed();
        CancelRecovery(); Apply(action, true);
        _log("[power] power notification registration failed; sync paused until app restart (Start sync cannot override)");
    }
    public void Enable()
    {
        Trace("enable", _state.Enable());
        if (CanRun && !_alive()) TryStart();
    }
    public void Disable()
    {
        if (_state.ShuttingDown) return;
        Trace("disable", _state.Disable()); CancelRecovery();
        _stop(true, !_state.Suspended, _state.Suspended ? 1800 : null);
    }
    public void DisplayChanged(int value)
    {
        var action = _state.DisplayChanged(value);
        Trace("display:" + value, action);
        if (!CanRun) CancelRecovery();
        Apply(action, false);
    }
    public void Suspend()
    {
        var action = _state.Suspend();
        Trace("suspend", action);
        CancelRecovery(); Apply(action, true);
    }
    public void SessionLocked(bool locked)
    {
        var action = _state.SessionLocked(locked); Trace("locked:" + locked, action);
        if (!CanRun) CancelRecovery();
        Apply(action, _state.Suspended);
    }
    public void Resume()
    {
        var action = _state.Resume(); Trace("resume", action); Apply(action, false);
    }
    public void Reload()
    {
        var action = _state.Reload(); Trace("reload", action);
        if (action != PowerAction.Restart) return;
        CancelRecovery(); _stop(false, false, null); TryStart();
    }
    public void Shutdown()
    {
        if (_state.ShuttingDown) return;
        Trace("shutdown", _state.Shutdown()); CancelRecovery(); _stop(true, false, 1800);
    }
    private void Apply(PowerAction action, bool suspending)
    {
        // Display-off/lock can precede a queued suspend on this same UI thread.
        // Never spend the manual 3500ms + fallback/WMI budget here.
        if (action == PowerAction.StopOff) _stop(true, false, suspending ? 1800 : 1200);
        if (action == PowerAction.Start)
        {
            _recovering = true; _attempts = _ticks = 0; _timer(true);
        }
    }
    public void Tick()
    {
        if (!_recovering || !CanRun) { CancelRecovery(); return; }
        if (++_ticks > 15) { CancelRecovery(); return; }
        if (_alive()) return;
        if (_attempts >= 5)
        {
            _log("[power] resume retry budget exhausted; use Start sync to retry");
            CancelRecovery(); return;
        }
        _attempts++;
        TryStart();
    }
    private void TryStart()
    {
        Trace("engine-start attempt=" + _attempts, PowerAction.Start);
        try { _start(); }
        catch (Exception ex) { _log("[power] engine start failed: " + ex.Message); }
    }
    private void CancelRecovery() { _recovering = false; _timer(false); }
    private void Trace(string reason, PowerAction action) => _log($"[power] reason={reason}; action={action}; enabled={_state.Enabled}; display={_state.Display}; locked={_state.Locked}; suspended={_state.Suspended}; shutdown={_state.ShuttingDown}; canRun={CanRun}; alive={_alive()}; retry={_attempts}/{_ticks}");
}
