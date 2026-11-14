using System.Reflection;

internal static class Harness
{
    private static int _passed;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fake-engine")
        {
            Console.WriteLine("READY"); Console.Out.Flush();
            Console.ReadLine();
            if (args[1] == "closed-stdin") { Thread.Sleep(5000); return 0; }
            if (args[1] != "missing") Console.WriteLine("SYNCLIGHT_STOP {\"offRequested\":true,\"offWritten\":" + (args[1] == "failure" ? "false" : "true") + "}");
            Console.Out.Flush();
            if (args[1] == "blocked") Thread.Sleep(5000);
            return 0;
        }
        try
        {
            Test("owned engine stop requires actual off ACK and shares bounded deadline", () =>
            {
                var type = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerEngineStop");
                Check(type != null, "Stop must track HID off acknowledgement, not stdin request delivery");
                foreach (var scenario in new[] { "success", "failure", "missing", "blocked", "closed-stdin" })
                {
                    var logs = new List<string>();
                    dynamic tracker = Activator.CreateInstance(type!)!;
                    using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = Environment.ProcessPath!, Arguments = "--fake-engine " + scenario,
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
                    })!;
                    using var ready = new ManualResetEventSlim();
                    child.OutputDataReceived += (_, e) => { if (e.Data == "READY") ready.Set(); else tracker.Observe(e.Data); };
                    child.BeginOutputReadLine();
                    Check(ready.Wait(5000), "fake child startup");
                    if (scenario == "closed-stdin") child.StandardInput.Close();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    bool offWritten = tracker.Stop(child, true, 500, false, (Action<string>)logs.Add);
                    Check(offWritten == (scenario is "success" or "blocked"), "ACK outcome for " + scenario + ": " + string.Join(" | ", logs));
                    Check(watch.ElapsedMilliseconds < 1400, "single bounded stop deadline for " + scenario);
                    var exited = child.WaitForExit(1000);
                    if (!exited) child.Kill(); // Own fake child only, including a failing test.
                    Check(exited, "test child must terminate for " + scenario);
                    Check(logs.Any(x => x.Contains("offWritten=")), "off outcome must be logged separately from request");
                }
            });
            Test("power diagnostics include raw native events, state and action", () =>
            {
                var logs = new List<string>();
                var life = new SyncLightLite.Tray.PowerLifecycle(() => false, () => { }, (_, _, _) => { }, _ => { }, logs.Add);
                using var window = new SyncLightLite.Tray.PowerWindow(life.DisplayChanged, life.Suspend, life.Resume, life.Shutdown, logs.Add, life.RegistrationFailed);
                life.DisplayChanged(1); life.Enable();
                SendDisplay(window.Handle, 0); SendMessage(window.Handle, 0x218, 0x12, 0);
                Check(logs.Any(x => x.Contains("msg=0x218") && x.Contains("event=0x8013") && x.Contains("value=0") && x.Contains("2b84c20e", StringComparison.OrdinalIgnoreCase)), "raw display GUID/value must be traceable");
                Check(logs.Any(x => x.Contains("event=0x12")), "automatic resume must be distinguishable from user resume");
                Check(logs.Any(x => x.Contains("reason=display:0") && x.Contains("action=StopOff") && x.Contains("canRun=False")), "state/action logging must explain stop and rejected restart");
                var format = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerDiagnostics")?.GetMethod("Format");
                Check(format != null, "timestamped diagnostic formatter required");
                var formatted = (string)format!.Invoke(null, new object[] { "test" })!;
                Check(DateTimeOffset.TryParse(formatted.Split(' ')[0], out _) && formatted.EndsWith(" test"), "logs must have ISO timestamp with timezone");
            });
            Test("explicit session lock blocks display-on and retry until unlock", () =>
            {
                var lifeType = typeof(SyncLightLite.Tray.PowerLifecycle);
                var method = lifeType.GetMethod("SessionLocked");
                Check(method != null, "WTS lock requires a distinct lifecycle gate, not mouse-idle inference");
                var starts = 0; var stops = 0;
                var life = new SyncLightLite.Tray.PowerLifecycle(() => false, () => starts++, (_, _, _) => stops++, _ => { }, _ => { });
                life.DisplayChanged(1); life.Enable();
                method!.Invoke(life, new object[] { true });
                life.DisplayChanged(0); life.DisplayChanged(1); life.Resume(); life.Enable(); life.Reload(); life.Tick();
                Check(starts == 1 && stops == 1 && !life.CanRun, "session display-on while locked cannot relight");
                method.Invoke(life, new object[] { false }); life.Tick();
                Check(starts == 2, "unlock with known display-on resumes enabled intent");
                method.Invoke(life, new object[] { true }); life.DisplayChanged(0);
                method.Invoke(life, new object[] { false }); life.Tick();
                Check(starts == 2, "unlock is not proof the display is on");
                life.Disable(); life.DisplayChanged(1); life.Tick(); Check(starts == 2, "unlock cannot override manual stop");
            });
            Test("hidden WTS messages gate current session without treating idle as off", () =>
            {
                var logs = new List<string>(); var locks = new List<bool>();
                using var window = new SyncLightLite.Tray.PowerWindow(_ => { }, () => { }, () => { }, () => { }, logs.Add, () => { });
                var attach = window.GetType().GetMethod("RegisterSession");
                Check(attach != null, "hidden window must subscribe WTS lock/unlock and query initial lock state");
                attach!.Invoke(window, new object[] { (Action<bool>)locks.Add, (Action)(() => throw new Exception("WTS registration/query failed")) });
                Check(locks.Count == 1, "initial lock state must be read before Enable");
                locks.Clear();
                var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                SendMessage(window.Handle, 0x2b1, 7, session + 100); // another session
                SendMessage(window.Handle, 0x2b1, 7, session);
                SendMessage(window.Handle, 0x2b1, 8, session);
                SendMessage(window.Handle, 0x2b1, 0xf, session); // desktop ready is not unlock
                Check(locks.SequenceEqual(new[] { true, false }), "only current-session lock/unlock changes lock gate");
                Check(logs.Any(x => x.Contains("msg=0x2b1") && x.Contains("session=")), "WTS raw transitions logged");
            });
            Test("startup waits for explicit display status even when initial off is delayed", () =>
            {
                var starts = 0; var timer = false;
                var life = new SyncLightLite.Tray.PowerLifecycle(() => false, () => starts++,
                    (_, _, _) => { }, enabled => timer = enabled, _ => { });
                life.Enable(); life.Reload(); life.Resume(); life.Tick();
                Check(starts == 0 && !timer && !life.CanRun, "unknown initial display must not start an engine");
                life.DisplayChanged(0); life.Enable(); life.Reload(); life.Tick();
                Check(starts == 0 && !timer, "delayed initial off must produce zero starts");
                life.DisplayChanged(2);
                Check(starts == 0 && timer, "explicit dim must queue startup");
                life.Tick(); Check(starts == 1, "explicit dim permits startup");
            });
            Test("notification registration failure latches sync paused until app restart", () =>
            {
                var starts = 0; var timer = false; var logs = new List<string>();
                var life = new SyncLightLite.Tray.PowerLifecycle(() => false, () => starts++,
                    (_, _, _) => { }, enabled => timer = enabled, logs.Add);
                var failed = life.GetType().GetMethod("RegistrationFailed");
                Check(failed != null, "registration failure must explicitly inhibit startup");
                // Initial display-on can arrive synchronously inside registration.
                life.DisplayChanged(1); failed!.Invoke(life, null);
                life.Enable(); life.DisplayChanged(2); life.Reload(); life.Resume();
                for (var i = 0; i < 30; i++) life.Tick();
                life.Disable(); life.Enable();
                Check(starts == 0 && !timer && !life.CanRun, "manual start and late events cannot bypass failed registration");
                Check(logs.Any(x => x.Contains("paused") && x.Contains("restart")), "log must explain the fail-closed recovery policy");
            });
            Test("registration callbacks are safe before window construction returns", () =>
            {
                var constructor = typeof(SyncLightLite.Tray.PowerWindow).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SingleOrDefault(c => c.GetParameters().Length == 8);
                Check(constructor != null, "window registration must expose a deterministic native registration seam");
                foreach (var fail in new[] { "none", "display", "suspend" })
                foreach (var initial in new[] { 0, 1, 2 })
                foreach (var duringRegistration in new[] { false, true })
                {
                    var starts = 0; var timer = false; var logs = new List<string>();
                    var life = new SyncLightLite.Tray.PowerLifecycle(() => false, () => starts++,
                        (_, _, _) => { }, enabled => timer = enabled, logs.Add);
                    SyncLightLite.Tray.PowerRegistration Register(nint hwnd, string kind)
                    {
                        return new SyncLightLite.Tray.PowerRegistration(() =>
                        {
                            if (kind == "display" && duringRegistration) SendDisplay(hwnd, initial);
                            Check(starts == 0, "registration must not start an engine before Enable");
                            return fail == kind ? 0 : 123;
                        }, _ => true, logs.Add, kind);
                    }
                    using var window = (SyncLightLite.Tray.PowerWindow)constructor!.Invoke(new object[] {
                        (Action<int>)life.DisplayChanged, (Action)life.Suspend, (Action)life.Resume,
                        (Action)life.Shutdown, (Action<string>)logs.Add, (Action)life.RegistrationFailed,
                        (Func<nint, SyncLightLite.Tray.PowerRegistration>)(hwnd => Register(hwnd, "display")),
                        (Func<nint, SyncLightLite.Tray.PowerRegistration>)(hwnd => Register(hwnd, "suspend")) });
                    life.Enable();
                    if (!duringRegistration)
                    {
                        Check(starts == 0 && !timer, "Enable before initial native callback must not start");
                        SendDisplay(window.Handle, initial); life.Tick();
                    }
                    Check(starts == (fail == "none" && initial != 0 ? 1 : 0), "startup must honor initial callback and both registration results");
                    if (fail != "none")
                    {
                        SendDisplay(window.Handle, 1); life.Enable(); life.Reload(); life.Resume(); life.Tick();
                        Check(starts == 0 && !timer && !life.CanRun, "failed registration stays paused after late native on");
                        Check(logs.Any(x => x.Contains(fail + " registration failed")) && logs.Any(x => x.Contains("app restart")), "failure and recovery policy logged");
                    }
                }
            });
            Test("display timeout stops LEDs and screen-on restores enabled intent", () =>
            {
                var type = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerState");
                Check(type != null, "PowerState must exist: display timeout currently has no lifecycle handler");
                dynamic state = Activator.CreateInstance(type!)!;
                state.DisplayChanged(1);
                Equal("Start", state.Enable());
                Equal("StopOff", state.DisplayChanged(0));
                Equal("Start", state.DisplayChanged(1));
            });
            Test("automatic resume never trusts the pre-suspend display-on state", () =>
            {
                dynamic state = new SyncLightLite.Tray.PowerState();
                state.DisplayChanged(1); state.Enable();
                Check(state.GetType().GetMethod("Suspend") != null, "Suspend transition is missing");
                Equal("StopOff", state.Suspend());
                Equal("None", state.Resume());
                Equal("Start", state.DisplayChanged(1));
            });
            Test("manual stop while paused prevents restart, including after saving", () =>
            {
                dynamic state = new SyncLightLite.Tray.PowerState();
                state.DisplayChanged(1); state.Enable(); state.DisplayChanged(0);
                Check(state.GetType().GetMethod("Disable") != null, "Manual intent cancellation is missing");
                Equal("None", state.Disable());
                Equal("None", state.Reload());
                Equal("None", state.DisplayChanged(1));
                Check(!state.Enabled, "save must not enable stopped synchronization");
            });
            Test("shutdown is terminal despite late resume, enable and config events", () =>
            {
                dynamic state = new SyncLightLite.Tray.PowerState(); state.DisplayChanged(1); state.Enable();
                Check(state.GetType().GetMethod("Shutdown") != null, "Terminal shutdown gate is missing");
                Equal("StopOff", state.Shutdown());
                Equal("None", state.Shutdown());
                Equal("None", state.Resume()); Equal("None", state.DisplayChanged(1));
                Equal("None", state.Enable()); Equal("None", state.Reload());
                Check(!state.CanRun, "shutdown must prohibit new engines");
            });
            Test("invalid display payloads cannot wake a paused engine", () =>
            {
                var state = new SyncLightLite.Tray.PowerState(); state.DisplayChanged(1); state.Enable(); state.DisplayChanged(0);
                Equal("None", state.DisplayChanged(3));
                Equal("None", state.DisplayChanged(-1));
            });
            Test("duplicates, dimming, config pause and either resume ordering", () =>
            {
                var state = new SyncLightLite.Tray.PowerState();
                Equal("None", state.DisplayChanged(1));
                Equal("Start", state.Enable()); Equal("None", state.Enable());
                Equal("None", state.DisplayChanged(2)); Equal("Restart", state.Reload());
                Equal("StopOff", state.DisplayChanged(0)); Equal("None", state.DisplayChanged(0));
                Equal("None", state.Reload()); Equal("None", state.Suspend());
                Equal("None", state.DisplayChanged(1)); Equal("None", state.Suspend());
                Equal("Start", state.Resume()); Equal("None", state.Resume());
                Equal("StopOff", state.Suspend()); Equal("None", state.Resume());
                Equal("Start", state.DisplayChanged(2));
            });
            Test("hidden native window dispatches real injected power messages", NativeMessages);
            Test("lifecycle applies off policy and delays bounded hardware recovery", () =>
            {
                var type = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerLifecycle");
                Check(type != null, "Engine lifecycle coordinator is missing");
                bool alive = false, timer = false;
                int starts = 0;
                var stops = new List<string>();
                dynamic lifecycle = Activator.CreateInstance(type!, (Func<bool>)(() => alive),
                    (Action)(() => { starts++; alive = true; }),
                    (Action<bool, bool, int?>)((off, fallback, wait) => { stops.Add($"{off}:{fallback}:{wait}"); alive = false; }),
                    (Action<bool>)(v => timer = v), (Action<string>)(_ => { }))!;
                lifecycle.DisplayChanged(1); lifecycle.Enable(); Check(starts == 1, "manual start");
                lifecycle.DisplayChanged(0); Check(!alive && !timer, "display off stops engine and retries");
                Check(stops.Single() == "True:False:1200", "automatic display-off must leave room for queued suspend; no slow fallback process");
                lifecycle.Reload(); Check(starts == 1, "save cannot bypass display pause");
                lifecycle.DisplayChanged(1); Check(starts == 1 && timer, "wake is delayed for USB");
                lifecycle.Tick(); Check(starts == 2, "delayed restart");
                lifecycle.Tick(); Check(starts == 2, "live engines never duplicate");
                lifecycle.Suspend(); Check(stops.Last() == "True:False:1800", "suspend must not launch fallback");
                lifecycle.Resume(); lifecycle.Tick(); Check(starts == 2, "automatic wake with screen off stays off");
                lifecycle.DisplayChanged(1); lifecycle.Disable(); lifecycle.Tick();
                Check(starts == 2 && !timer, "stop cancels pending wake");
                lifecycle.Shutdown(); lifecycle.DisplayChanged(1); lifecycle.Enable(); lifecycle.Tick();
                Check(starts == 2 && stops.Last() == "True:False:1800", "terminal bounded shutdown");
            });
            Test("wake retries exceptions and early exits with bounded attempts and window", () =>
            {
                var attempts = 0; var timer = false; var alive = true; var logs = new List<string>();
                var lifecycle = new SyncLightLite.Tray.PowerLifecycle(() => alive,
                    () => { attempts++; throw new InvalidOperationException("USB not ready"); },
                    (_, _, _) => alive = false, value => timer = value, logs.Add);
                lifecycle.DisplayChanged(1); lifecycle.Enable(); lifecycle.DisplayChanged(0); lifecycle.DisplayChanged(1);
                for (var i = 0; i < 40; i++) lifecycle.Tick();
                Check(attempts == 5 && !timer, $"retry budget: attempts={attempts}, timer={timer}");
                Check(logs.Any(x => x.Contains("USB not ready")), "failed starts must be logged");
                attempts = 0; alive = true;
                lifecycle = new SyncLightLite.Tray.PowerLifecycle(() => alive,
                    () => { attempts++; alive = true; }, (_, _, _) => alive = false,
                    value => timer = value, logs.Add);
                lifecycle.DisplayChanged(1); lifecycle.Enable(); lifecycle.DisplayChanged(0); lifecycle.DisplayChanged(1);
                lifecycle.Tick(); alive = false; lifecycle.Tick();
                Check(attempts == 2, "process started then exited must retry");
                for (var i = 0; i < 40; i++) lifecycle.Tick();
                Check(attempts == 2 && !timer, "successful live engine is observed then retry window closes");
                alive = false; lifecycle.Tick(); Check(attempts == 2, "no endless restart after recovery window");
            });
            Test("registration failures are logged and owned registrations dispose once", () =>
            {
                var type = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerRegistration");
                Check(type != null, "Testable notification registration ownership is missing");
                var logs = new List<string>(); var releases = new List<nint>();
                dynamic failed = Activator.CreateInstance(type!, (Func<nint>)(() => 0),
                    (Func<nint, bool>)(h => { releases.Add(h); return true; }), (Action<string>)logs.Add, "display")!;
                failed.Dispose(); failed.Dispose();
                Check(logs.Count == 1 && logs[0].Contains("display") && releases.Count == 0, "failed registration logs, never unregisters NULL");
                dynamic valid = Activator.CreateInstance(type!, (Func<nint>)(() => 123),
                    (Func<nint, bool>)(h => { releases.Add(h); return false; }), (Action<string>)logs.Add, "suspend")!;
                valid.Dispose(); valid.Dispose();
                Check(releases.SequenceEqual(new nint[] {123}) && logs.Count == 2, "successful handle released once, release failure logged");
            });
            Test("native display events drive one engine through pause and ordered resume", () =>
            {
                var starts = 0; var stops = 0; var alive = false;
                var lifecycle = new SyncLightLite.Tray.PowerLifecycle(() => alive,
                    () => { starts++; alive = true; }, (_, _, _) => { stops++; alive = false; }, _ => { }, _ => { });
                using var window = new SyncLightLite.Tray.PowerWindow(lifecycle.DisplayChanged, lifecycle.Suspend,
                    lifecycle.Resume, lifecycle.Shutdown, _ => { }, lifecycle.RegistrationFailed);
                lifecycle.DisplayChanged(1); lifecycle.Enable();
                var payload = System.Runtime.InteropServices.Marshal.AllocHGlobal(24);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"), payload, false);
                    System.Runtime.InteropServices.Marshal.WriteInt32(payload, 16, 4);
                    System.Runtime.InteropServices.Marshal.WriteInt32(payload, 20, 0);
                    SendMessage(window.Handle, 0x218, 0x8013, payload);
                    SendMessage(window.Handle, 0x218, 0x8013, payload);
                    Check(starts == 1 && stops == 1 && !alive, "duplicate display-off is idempotent");
                    lifecycle.Reload(); SendMessage(window.Handle, 0x218, 4, 0);
                    SendMessage(window.Handle, 0x218, 0x12, 0); lifecycle.Tick();
                    Check(starts == 1, "automatic resume must wait for display-on");
                    System.Runtime.InteropServices.Marshal.WriteInt32(payload, 20, 1);
                    SendMessage(window.Handle, 0x218, 0x8013, payload); lifecycle.Tick();
                    Check(starts == 2 && alive, "display-on resumes enabled synchronization");
                    SendMessage(window.Handle, 0x16, 1, 0); lifecycle.Tick();
                    Check(starts == 2 && stops == 2 && !alive, "session end shuts down exactly once");
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(payload); }
            });
            Test("manual start failures are logged without crashing the tray", () =>
            {
                var logs = new List<string>();
                var lifecycle = new SyncLightLite.Tray.PowerLifecycle(() => false,
                    () => throw new InvalidOperationException("node unavailable"), (_, _, _) => { }, _ => { }, logs.Add);
                lifecycle.DisplayChanged(1); lifecycle.Enable(); lifecycle.Reload();
                Check(logs.Count(x => x.Contains("node unavailable")) == 2, "manual startup and reload errors must be logged");
            });
            Test("off, suspend and shutdown cancel queued retries without duplicate events resetting budget", () =>
            {
                foreach (var cancel in new[] { "off", "suspend", "shutdown" })
                {
                    bool alive = true, timer = false; var starts = 0;
                    var life = new SyncLightLite.Tray.PowerLifecycle(() => alive, () => starts++,
                        (_, _, _) => alive = false, value => timer = value, _ => { });
                    life.DisplayChanged(1); life.Enable(); life.DisplayChanged(0); life.DisplayChanged(1);
                    if (cancel == "off") life.DisplayChanged(0);
                    else if (cancel == "suspend") life.Suspend();
                    else life.Shutdown();
                    for (var i = 0; i < 30; i++) life.Tick();
                    Check(starts == 0 && !timer, cancel + " cancels queued work");
                }
                var attempts = 0;
                var duplicate = new SyncLightLite.Tray.PowerLifecycle(() => false, () => attempts++,
                    (_, _, _) => { }, _ => { }, _ => { });
                duplicate.DisplayChanged(1); duplicate.Enable(); duplicate.DisplayChanged(0); duplicate.DisplayChanged(1);
                for (var i = 0; i < 30; i++) { duplicate.DisplayChanged(1); duplicate.Resume(); duplicate.Tick(); }
                Check(attempts == 6, "duplicate broadcasts cannot reset the five-attempt recovery budget");
            });
            Console.WriteLine($"Power lifecycle: {_passed} tests passed (no UI/HID/power changes)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);
    private static void SendDisplay(nint hwnd, int value)
    {
        var payload = System.Runtime.InteropServices.Marshal.AllocHGlobal(24);
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(SyncLightLite.Tray.PowerWindow.SessionDisplayStatus, payload, false);
            System.Runtime.InteropServices.Marshal.WriteInt32(payload, 16, 4);
            System.Runtime.InteropServices.Marshal.WriteInt32(payload, 20, value);
            SendMessage(hwnd, 0x218, 0x8013, payload);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(payload); }
    }
    private static void NativeMessages()
    {
        var type = Assembly.GetExecutingAssembly().GetType("SyncLightLite.Tray.PowerWindow");
        Check(type != null, "Native power notification window is missing");
        var events = new List<string>();
        var registrationLogs = new List<string>();
        using var window = (IDisposable)Activator.CreateInstance(type!,
            (Action<int>)(v => events.Add("display:" + v)), (Action)(() => events.Add("suspend")),
            (Action)(() => events.Add("resume")), (Action)(() => events.Add("shutdown")),
            (Action<string>)registrationLogs.Add, (Action)(() => registrationLogs.Add("registration failed")))!;
        Check(registrationLogs.Count == 0, "real Windows registration failed: " + string.Join(",", registrationLogs));
        var hwnd = (nint)type!.GetProperty("Handle")!.GetValue(window)!;
        Check(hwnd != 0 && !IsWindowVisible(hwnd), "listener must have an invisible real HWND");
        var data = System.Runtime.InteropServices.Marshal.AllocHGlobal(24);
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"), data, false);
            System.Runtime.InteropServices.Marshal.WriteInt32(data, 16, 4);
            System.Runtime.InteropServices.Marshal.WriteInt32(data, 20, 0);
            Check(SendMessage(hwnd, 0x218, 0x8013, data) == 1, "handled power broadcast must return TRUE");
            SendMessage(hwnd, 0x218, 4, 0);
            SendMessage(hwnd, 0x218, 0x12, 0);
            SendMessage(hwnd, 0x218, 7, 0);
            SendMessage(hwnd, 0x16, 1, 0);
            // Malformed data and unrelated settings must be ignored.
            var count = events.Count;
            SendMessage(hwnd, 0x218, 0x8013, 0);
            System.Runtime.InteropServices.Marshal.WriteInt32(data, 16, 3);
            SendMessage(hwnd, 0x218, 0x8013, data);
            System.Runtime.InteropServices.Marshal.WriteInt32(data, 16, 4);
            System.Runtime.InteropServices.Marshal.WriteInt32(data, 20, 99);
            SendMessage(hwnd, 0x218, 0x8013, data);
            System.Runtime.InteropServices.Marshal.StructureToPtr(Guid.Empty, data, false);
            SendMessage(hwnd, 0x218, 0x8013, data);
            SendMessage(hwnd, 0x16, 0, 0); // cancelled session end
            Check(events.Count == count, "malformed/unrelated/cancelled messages are inert");
            Check(string.Join(",", events) == "display:0,suspend,resume,resume,shutdown", "native dispatch: " + string.Join(",", events));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(data); }
        window.Dispose(); window.Dispose();
        Check(!IsWindow(hwnd), "disposing twice releases the real hidden HWND");
        Check(registrationLogs.All(x => x.StartsWith("[power-native]")), "real Windows unregister failed: " + string.Join(",", registrationLogs));
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(string expected, object actual) => Check(expected == actual.ToString(), $"Expected {expected}, got {actual}");
    private static void Test(string name, Action test) { test(); _passed++; Console.WriteLine("PASS " + name); }
}
