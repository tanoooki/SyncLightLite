namespace SyncLightLite.Tray;

internal enum PowerAction { None, Start, StopOff, Restart }

// Pure state: user intent must survive a display timeout, unlike engine liveness.
internal sealed class PowerState
{
    public bool Enabled { get; private set; }
    // Registration delivers the initial status asynchronously; never assume on.
    public int Display { get; private set; } = -1;
    public bool Suspended { get; private set; }
    public bool Locked { get; private set; }
    public PowerAction SessionLocked(bool locked) { Locked = locked; return Reconcile(); }
    public bool ShuttingDown { get; private set; }
    private bool _registrationFailed;
    public bool CanRun => Enabled && Display is 1 or 2 && !Locked && !Suspended && !ShuttingDown && !_registrationFailed;
    public PowerAction RegistrationFailed() { _registrationFailed = true; return Reconcile(); }
    public PowerAction Shutdown() { ShuttingDown = true; Enabled = false; return Reconcile(); }
    private bool _running;

    public PowerAction Suspend()
    {
        if (Suspended) return PowerAction.None;
        Suspended = true;
        // Require a fresh display notification, even if no off preceded sleep.
        Display = 0;
        return Reconcile();
    }
    public PowerAction Resume() { Suspended = false; return Reconcile(); }

    public PowerAction Disable() { Enabled = false; return Reconcile(); }
    public PowerAction Reload() => CanRun ? PowerAction.Restart : PowerAction.None;
    public PowerAction Enable() { Enabled = true; return Reconcile(); }
    public PowerAction DisplayChanged(int value)
    {
        if (value is < 0 or > 2) return PowerAction.None;
        Display = value;
        return Reconcile();
    }
    private PowerAction Reconcile()
    {
        if (_running == CanRun) return PowerAction.None;
        _running = CanRun;
        return _running ? PowerAction.Start : PowerAction.StopOff;
    }
}
