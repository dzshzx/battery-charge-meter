using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("Power Meter legacy launcher")]
[assembly: AssemblyProduct("Power Meter")]

// Installed only over an existing BatteryChargeMeter.exe. Keeps old shortcuts
// working. It lives in a user-writable folder, so setup migrates any elevated
// logon task that still runs it to the admin-only protected copy (or deletes
// the task when elevation is declined); it is never an elevated target.
internal static class LegacyLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--autostart"
            && args[0] != "--no-elevate" && args[0] != "--remove-autostart"))
            return 2;
        try
        {
            string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerMeter.exe");
            using (Process child = Process.Start(new ProcessStartInfo(target,
                args.Length == 0 ? "" : args[0]) { UseShellExecute = false }))
            {
                if (child == null) return 1;
                if (args.Length == 1 && args[0] == "--remove-autostart")
                {
                    child.WaitForExit();
                    return child.ExitCode;
                }
                return 0;
            }
        }
        catch { return 1; }
    }
}
