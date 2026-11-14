using System.Diagnostics;
using System.Text.Json;

namespace SyncLightLite.Tray;

// One tracker per child: stdin delivery is not evidence of a successful HID write.
internal sealed class PowerEngineStop
{
    private volatile bool _requested, _offWritten, _acknowledged, _outputEnded;
    public void Observe(string? line)
    {
        if (line == null) { _outputEnded = true; return; }
        const string prefix = "SYNCLIGHT_STOP ";
        if (!_requested || !line.StartsWith(prefix, StringComparison.Ordinal)) return;
        try
        {
            using var json = JsonDocument.Parse(line[prefix.Length..]);
            if (json.RootElement.GetProperty("offRequested").GetBoolean())
            {
                _offWritten = json.RootElement.GetProperty("offWritten").GetBoolean();
                _acknowledged = true;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    public bool Stop(Process process, bool turnOff, int waitMs, bool killTree, Action<string> log)
    {
        var deadline = Stopwatch.StartNew();
        int Remaining() => Math.Max(0, waitMs - (int)deadline.ElapsedMilliseconds);
        try
        {
            if (!process.HasExited)
            {
                _requested = turnOff;
                process.StandardInput.WriteLine(turnOff ? "off" : "stop");
                process.StandardInput.Flush();
                process.StandardInput.Close();
                log($"[tray] requested live engine graceful {(turnOff ? "off" : "stop")}; pid={process.Id}; budgetMs={waitMs}");
                if (!process.WaitForExit(Remaining()))
                {
                    log($"[tray] live engine stop deadline; pid={process.Id}; killing owned process; ACK={_acknowledged}");
                    process.Kill(entireProcessTree: killTree);
                }
            }
            // Timed WaitForExit does not drain asynchronous stdout. Stay within
            // the SAME deadline, including when native capture teardown hangs.
            SpinWait.SpinUntil(() => _outputEnded, Remaining());
        }
        catch (Exception ex)
        {
            log("[tray] graceful live engine stop failed: " + ex.Message);
            try { if (!process.HasExited) process.Kill(entireProcessTree: killTree); }
            catch (Exception killError) { log("[tray] owned engine kill failed: " + killError.Message); }
        }
        log($"[tray] stop result; pid={process.Id}; offRequested={turnOff}; ACK={_acknowledged}; offWritten={_offWritten}; elapsedMs={deadline.ElapsedMilliseconds}");
        return turnOff && _acknowledged && _offWritten;
    }
}
