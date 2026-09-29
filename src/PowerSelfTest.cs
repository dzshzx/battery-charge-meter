using System;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.TaskScheduler;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Release smoke test of the built EXE (--self-test): the embedded
    /// libraries load and the window binds a view. The rules themselves are
    /// unit-tested in tests/PowerMeter.Tests.
    /// </summary>
    internal static class PowerSelfTest
    {
        public static string Run(out bool passed)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            failures += StartupCases(log);
            failures += AutostartCases(log);
            failures += WindowCases(log);

            log.AppendLine();
            passed = failures == 0;
            log.AppendLine(passed
                ? "Power self test passed."
                : failures.ToString(CultureInfo.InvariantCulture) + " power self test case(s) failed.");

            return log.ToString();
        }

        // System.CommandLine is loaded from the EXE's embedded resources.
        private static int StartupCases(StringBuilder log)
        {
            StartupRoute probe = StartupRoute.Parse(new string[] { "--POWER-PROBE", "out.txt", "12" });
            return Check(log, "embedded command line parser routes and rejects arguments",
                probe.Valid && probe.Command == "--power-probe" && probe.Seconds == 12
                && StartupRoute.Parse(new string[0]).Valid
                && !StartupRoute.Parse(new string[] { "--wat" }).Valid);
        }

        // TaskScheduler is loaded from the EXE's embedded resources; this
        // builds a definition without registering it.
        private static int AutostartCases(StringBuilder log)
        {
            string sid = "S-1-5-21-111-222-333-1001";
            string copy = new ProtectedCopy(ProtectedCopy.DefaultRoot, sid).Executable;
            string path = @"C:\Test & 测试\Meter.exe";
            using (TaskService scheduler = new TaskService())
            {
                TaskDefinition definition = scheduler.NewTask();
                AutostartManager.Configure(definition, copy, sid);
                TaskDefinition stored = scheduler.NewTask();
                stored.XmlText = definition.XmlText;
                AutostartState state = AutostartManager.Inspect(stored, path, sid, copy, path);
                return Check(log, "embedded task scheduler library roundtrips the logon task",
                    state.Exists && state.Enabled && state.ThisCopy && state.Protected && state.Executable == path);
            }
        }

        private static int WindowCases(StringBuilder log)
        {
            // Exercise the actual window/tray binding once; the display rules
            // themselves are covered at the MeterSession interface.
            int failures = 0;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            Type formType = typeof(MainForm);
            using (MainForm form = new MainForm(true))
            {
                BatteryReading battery = Battery(true, true, true, 39.08);
                PowerSnapshot snapshot = PowerSnapshot.Compose(DateTimeOffset.Now, 1, battery,
                    PowerSample.FromValue(PowerBoundary.CpuPackage, MeasurementKind.Measured, 14.11, "fixture", TimeSpan.FromSeconds(1)),
                    PowerSample.FromValue(PowerBoundary.Platform, MeasurementKind.Measured, 27, "fixture", TimeSpan.FromSeconds(1)));
                Label headline = (Label)formType.GetField("powerLabel", flags).GetValue(form);
                Label[] rows = (Label[])formType.GetField("sourceValues", flags).GetValue(form);
                NotifyIcon tray = (NotifyIcon)formType.GetField("trayIcon", flags).GetValue(form);

                formType.GetField("session", flags).SetValue(form, new MeterSession(DisplayMode.WholeSystem));
                form.ShowReading(snapshot);
                failures += Check(log, "window and tray bind the whole-system view",
                    headline.Text == "≈ 66.08 W" && rows[MeterView.WholeSystemRow].Text == headline.Text
                    && headline.ForeColor == UiTheme.NumericMuted
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "66"
                    && tray.Text == Strings.Get("估算整机输入功率") + ": ≈ 66.08 W");

                formType.GetField("session", flags).SetValue(form, new MeterSession(DisplayMode.Battery));
                form.ShowReading(snapshot);
                failures += Check(log, "window and tray bind the battery view",
                    headline.Text == "39.08 W" && headline.ForeColor == UiTheme.Charging
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "39"
                    && tray.Text == Strings.Get("电池端净功率") + ": 39.08 W");
            }
            return failures;
        }

        private static BatteryReading Battery(
            bool online, bool charging, bool rateAvailable, double watts)
        {
            BatteryReading reading = new BatteryReading();
            reading.ActiveBatteryCount = 1;
            if (charging)
                reading.SupplyState = BatterySupplyState.ExternalPowerCharging;
            else if (watts < 0.0)
            {
                reading.SupplyState = online
                    ? BatterySupplyState.ExternalPowerSupplemented
                    : BatterySupplyState.BatteryDischarging;
            }
            else
            {
                reading.SupplyState = online
                    ? BatterySupplyState.ExternalPowerIdle
                    : BatterySupplyState.BatteryDirectionUnknown;
            }
            reading.RateAvailable = rateAvailable;
            reading.PowerWatts = watts;
            return reading;
        }

        private static int Check(StringBuilder log, string name, bool condition)
        {
            log.AppendLine((condition ? "PASS  " : "FAIL  ") + name);
            return condition ? 0 : 1;
        }
    }
}
