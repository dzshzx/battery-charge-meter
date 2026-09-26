using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Drives the power derivation across supply states and firmware rate
    /// combinations that no single machine can produce on demand.
    ///
    /// These paths are where wrong numbers reach the user without any sensor
    /// misbehaving, so they are checked mechanically rather than by whatever
    /// state the test machine happened to be in.
    /// </summary>
    internal static class PowerSelfTest
    {
        private const double Tolerance = 1e-9;

        public static string Run(out bool passed)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            failures += SupplyStateCases(log);
            failures += RateCases(log);
            failures += BatteryAggregationCases(log);
            failures += WholeSystemCases(log);
            failures += CounterCases(log);
            failures += ValidationCases(log);
            failures += EmiMetadataCases(log);
            failures += LayoutCases(log);
            failures += SessionCases(log);
            failures += WindowCases(log);
            failures += DisplayCases(log);
            failures += HistoryCases(log);
            failures += StartupCases(log);
            failures += AutostartCases(log);
            failures += LanguageCases(log);

            log.AppendLine();
            passed = failures == 0;
            log.AppendLine(passed
                ? "Power self test passed."
                : failures.ToString(CultureInfo.InvariantCulture) + " power self test case(s) failed.");

            return log.ToString();
        }

        private static int DisplayCases(StringBuilder log)
        {
            int failures = 0;
            PowerSample platform = PowerSample.FromValue(PowerBoundary.Platform, MeasurementKind.Measured, 24, "test", TimeSpan.FromSeconds(1));
            foreach (BatteryReading battery in new BatteryReading[] { Battery(true, true, true, 13), Battery(true, false, true, -5), Battery(false, false, true, -26) })
            {
                PowerSnapshot snapshot = Snapshot(battery, platform, 0);
                MeterView whole = new MeterSession(DisplayMode.WholeSystem).Observe(snapshot);
                MeterView terminal = new MeterSession(DisplayMode.Battery).Observe(snapshot);
                failures += Check(log, "selected boundary is shared without substitution: " + battery.SupplyState,
                    whole.Headline.Text == whole.Rows[MeterView.WholeSystemRow].Text
                    && whole.Title.Text == Strings.Get(PowerSample.LabelFor(snapshot.WholeSystem.Boundary))
                    && terminal.Headline.Text == terminal.Rows[MeterView.BatteryRow].Text
                    && terminal.Title.Text == Strings.Get(PowerSample.LabelFor(PowerBoundary.BatteryTerminal)));
            }
            BatteryReading charging = Battery(true, true, true, 13);
            PowerSnapshot missing = Snapshot(charging, PowerSample.Unsupported(PowerBoundary.Platform, "missing"), 0);
            failures += Check(log, "whole mode does not fall back to available battery",
                new MeterSession(DisplayMode.WholeSystem).Observe(missing).Headline.Text == "N/A"
                && new MeterSession(DisplayMode.Battery).Observe(missing).Headline.Text == "13.00 W");
            failures += Check(log, "preferences default safely for missing malformed or inaccessible storage",
                DisplayPreference.Load(delegate { return null; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "invalid"; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { throw new InvalidOperationException(); }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "Battery"; }) == DisplayMode.Battery);
            return failures;
        }

        private static PowerSnapshot Snapshot(BatteryReading battery, PowerSample platform, double elapsedSeconds)
        {
            return PowerSnapshot.Compose(DateTimeOffset.Now, elapsedSeconds, battery, null, platform);
        }

        private static PowerSample Whole(PowerSample platform, BatteryReading battery)
        {
            return Snapshot(battery, platform, 0).WholeSystem;
        }

        private static PowerSample Value(double watts)
        {
            return PowerSample.FromValue(PowerBoundary.BatteryTerminal, MeasurementKind.Measured, watts, "test", TimeSpan.Zero);
        }

        private static int HistoryCases(StringBuilder log)
        {
            int failures = 0;
            PowerHistory history = new PowerHistory();
            BatterySupplyState supply = BatterySupplyState.BatteryDischarging;
            history.Add(0, DisplayMode.Battery, supply, Value(-10));
            history.Add(1, DisplayMode.Battery, supply, Value(-20));
            history.Add(4, DisplayMode.Battery, supply, Value(-20));
            double coverage;
            double? average = history.Average(out coverage);
            failures += Check(log, "irregular observations use elapsed weighting and partial window", average.HasValue && Near(average.Value, -17.5) && Near(coverage, 4) && Near(history.Duration(60), 4));
            failures += Check(log, "peak retains sign at greatest magnitude", Near(history.Peak().Value, -20));
            history.Add(5, DisplayMode.Battery, supply, PowerSample.Unsupported(PowerBoundary.BatteryTerminal, "gap"));
            failures += Check(log, "missing sample leaves a gap and immediately unavailable statistics", !history.Peak().HasValue && !history.Average(out coverage).HasValue && !history.Points[3].Watts.HasValue);
            history.Add(6, DisplayMode.Battery, supply, Value(-30));
            average = history.Average(out coverage);
            failures += Check(log, "missing intervals are excluded without zero fill", Near(coverage, 4) && Near(average.Value, -17.5));
            history.Add(20, DisplayMode.Battery, supply, Value(-30));
            failures += Check(log, "resume gap clears baseline", history.Points.Count == 1 && !history.Average(out coverage).HasValue);
            history.Add(21, DisplayMode.WholeSystem, supply, Value(30));
            failures += Check(log, "mode change clears history", history.Points.Count == 1);
            history.Add(22, DisplayMode.WholeSystem, BatterySupplyState.ExternalPowerIdle, Value(30));
            failures += Check(log, "supply state change clears history", history.Points.Count == 1);
            history.Clear();
            for (int second = 0; second <= 70; second++) history.Add(second, DisplayMode.Battery, supply, Value(second == 20 ? -100 : -10));
            average = history.Average(out coverage);
            failures += Check(log, "30 second mean differs from 60 second peak; repeated readings stay fresh", Near(average.Value, -10) && Near(coverage, 30) && Near(history.Peak().Value, -100) && Near(history.Duration(60), 60));
            for (int second = 71; second <= 81; second++) history.Add(second, DisplayMode.Battery, supply, Value(-10));
            failures += Check(log, "old peak expires at real 60 second boundary", Near(history.Peak().Value, -10));
            history.Clear();
            for (int second = 0; second <= 32; second += 4)
                history.Add(second, DisplayMode.Battery, supply, Value(second == 0 ? 40 : 10));
            average = history.Average(out coverage);
            failures += Check(log, "weighted mean clips the first interval at exact 30 second cutoff", Near(average.Value, 12) && Near(coverage, 30));
            failures += Check(log, "nonfinite sensor values cannot reach readings or tray", !Value(Double.NaN).Available && !Value(Double.PositiveInfinity).Available);
            return failures;
        }

        private static int StartupCases(StringBuilder log)
        {
            int failures = 0;
            int launches = 0;
            Func<ElevationResult> launch = delegate
            {
                launches++;
                return new ElevationResult { Started = true };
            };
            failures += Check(log, "ordinary GUI requests elevation once and exits parent on success", Startup.Route(StartupRoute.Parse(new string[0]), false, launch).Started && launches == 1);
            Startup.Route(StartupRoute.Parse(new string[0]), true, launch);
            Startup.Route(StartupRoute.Parse(new string[] { "--no-elevate" }), false, launch);
            Startup.Route(StartupRoute.Parse(new string[] { "--elevation-attempted" }), false, launch);
            string[][] cli = new string[][]
            {
                new string[] { "--self-test", "x" },
                new string[] { "--power-probe", "x", "2" },
                new string[] { "--third-party-notices", "x" },
                new string[] { "--screenshot", "x" },
                new string[] { "--dpi-preview", "x", "168" },
                new string[] { "--tray-preview", "x", "42", "charging" }
            };
            foreach (string[] args in cli)
            {
                StartupRoute route = StartupRoute.Parse(args);
                failures += Check(log, "CLI route valid without GUI: " + args[0], route.Valid && route.Command != "gui");
                Startup.Route(route, false, launch);

                StartupRoute missingOutput = StartupRoute.Parse(new string[] { args[0] });
                failures += Check(log, "missing CLI arguments fail without UAC: " + args[0],
                    !missingOutput.Valid && !Startup.Route(missingOutput, false, launch).Started && launches == 1);
            }
            failures += Check(log, "already elevated, explicit ordinary GUI, child marker and all CLI bypass UAC", launches == 1);
            string[][] invalid = new string[][]
            {
                new string[] { "--wat" },
                new string[] { "--self-test" },
                new string[] { "--no-elevate", "extra" },
                new string[] { "--power-probe", "x", "0" },
                new string[] { "--dpi-preview", "x", "bad" },
                new string[] { "--dpi-preview", "x" },
                new string[] { "--tray-preview", "x", "42" },
                new string[] { "--tray-preview", "x", "42", "bogus" }
            };
            foreach (string[] args in invalid)
            {
                StartupRoute route = StartupRoute.Parse(args);
                failures += Check(log, "invalid arguments rejected: " + String.Join(" ", args), !route.Valid);
                Startup.Route(route, false, launch);
            }
            failures += Check(log, "invalid route cannot launch UAC", launches == 1);
            StartupRoute probe = StartupRoute.Parse(new string[] { "--POWER-PROBE", "out.txt", "12" });
            StartupRoute defaultProbe = StartupRoute.Parse(new string[] { "--power-probe", "out.txt" });
            StartupRoute dpi = StartupRoute.Parse(new string[] { "--dpi-preview", "shot.png", "168", "96" });
            StartupRoute tray = StartupRoute.Parse(new string[] { "--tray-preview", "icon.png", "99+", "Discharging" });
            failures += Check(log, "CLI routes carry parsed arguments for dispatch",
                probe.Valid && probe.Command == "--power-probe" && probe.Path == "out.txt" && probe.Seconds == 12
                && defaultProbe.Seconds == 5
                && dpi.Path == "shot.png" && dpi.Dpis.Length == 2 && dpi.Dpis[0] == 168 && dpi.Dpis[1] == 96
                && tray.Path == "icon.png" && tray.TrayGlyph == "99+" && tray.TrayDischarging);
            ElevationResult cancel = Startup.Launch(delegate { throw new System.ComponentModel.Win32Exception(1223); });
            ElevationResult failure = Startup.Launch(delegate { throw new InvalidOperationException("failure"); });
            failures += Check(log, "cancel and launch failure retain ordinary GUI with reason", !cancel.Started && cancel.Message.Contains("取消") && !failure.Started && failure.Message.Contains("failure") && !Startup.Launch(delegate { return false; }).Started);
            return failures;
        }

        private static int AutostartCases(StringBuilder log)
        {
            int calls = 0;
            StartupRoute route = StartupRoute.Parse(new string[] { "--autostart" });
            Startup.Route(route, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            int failures = Check(log, "autostart hides GUI and never requests UAC even with ordinary token",
                route.Valid && route.Command == "gui" && route.StartHidden && route.SuppressElevation && calls == 0);
            StartupRoute cleanup = StartupRoute.Parse(new string[] { "--remove-autostart" });
            Startup.Route(cleanup, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            failures += Check(log, "uninstall cleanup is non-GUI and never elevates",
                cleanup.Valid && cleanup.Command != "gui" && calls == 0);
            failures += Check(log, "autostart rejects extra arguments",
                !StartupRoute.Parse(new string[] { "--autostart", "extra" }).Valid);
            StartupRoute sync = StartupRoute.Parse(new string[] { "--sync-autostart" });
            Startup.Route(sync, false, delegate
            {
                calls++;
                return new ElevationResult { Started = true };
            });
            failures += Check(log, "installer startup synchronization is non-GUI and never elevates",
                sync.Valid && sync.Command == "--sync-autostart" && calls == 0
                && !StartupRoute.Parse(new string[] { "--sync-autostart", "extra" }).Valid);
            string sid = "S-1-5-21-111-222-333-1001";
            string path = @"C:\Test & 测试\Meter 1.2.1.exe";
            string xml = AutostartManager.BuildXml(SelfTestCopy(sid), sid);
            AutostartState state = Inspect(xml, path, sid);
            failures += Check(log, "logon task roundtrips escaped protected copy path and elevated interactive user policy",
                state.Exists && state.Enabled && state.ThisCopy && state.Protected
                && state.Executable == path && state.Target == SelfTestCopy(sid)
                && xml.Contains("<WorkingDirectory>" + System.Security.SecurityElement.Escape(System.IO.Path.GetDirectoryName(SelfTestCopy(sid))) + "</WorkingDirectory>")
                && xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>")
                && xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>")
                && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
                && xml.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"));
            failures += Check(log, "other copies are distinguished by the protected copy's recorded source",
                !AutostartManager.Inspect(xml, @"C:\Other\Meter.exe", sid, SelfTestCopy(sid), path).ThisCopy
                && !AutostartManager.Inspect(xml, path, sid, SelfTestCopy(sid), null).ThisCopy);
            AutostartState unprotected = Inspect(AutostartManager.BuildXml(path, sid), path, sid);
            failures += Check(log, "elevated task running a user-writable file is never reported as working startup",
                unprotected.Exists && unprotected.ThisCopy && !unprotected.Protected && !unprotected.Enabled
                && !String.IsNullOrEmpty(unprotected.RepairReason));
            failures += Check(log, "protected copy identifies itself by the installation it was taken from",
                ProtectedCopy.IdentityFor(@"C:\Elsewhere\PowerMeter.exe", sid) == @"C:\Elsewhere\PowerMeter.exe"
                && ProtectedCopy.IdentityFor(@"C:\Missing\" + sid + @"\Other.exe", sid) == @"C:\Missing\" + sid + @"\Other.exe"
                && ProtectedCopy.IdentityFor(@"C:\Missing\" + sid + @"\PowerMeter.exe", sid) == @"C:\Missing\" + sid + @"\PowerMeter.exe");
            failures += Check(log, "disabled task is read as disabled",
                !Inspect(xml.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>"), path, sid).Enabled);
            // Each shape this program never registers is reported as a foreign
            // task (a distinct type, so removal can leave it and still finish).
            string[] foreign = new string[]
            {
                xml.Replace("BatteryChargeMeter.Logon.v1", "someone-else"),
                xml.Replace("<Arguments>--autostart</Arguments>", "<Arguments>--other</Arguments>"),
                xml.Replace("</Exec></Actions>", "</Exec><Exec><Command>C:\\Other\\Tool.exe</Command></Exec></Actions>"),
                xml.Replace("<Principal id=\"User\"><UserId>" + sid, "<Principal id=\"User\"><UserId>S-1-5-21-111-222-333-1002")
            };
            foreach (string changed in foreign)
            {
                bool foreignRejected = false;
                try
                {
                    Inspect(changed, path, sid);
                }
                catch (ForeignAutostartTaskException)
                {
                    foreignRejected = true;
                }
                failures += Check(log, "foreign task shape is reported as not this program's and never adopted",
                    foreignRejected && changed != xml);
            }
            using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                string currentSid = identity.User.Value;
                string accountXml = AutostartManager.BuildXml(SelfTestCopy(currentSid), currentSid).Replace(
                    "<UserId>" + currentSid + "</UserId>",
                    "<UserId>" + System.Security.SecurityElement.Escape(identity.Name) + "</UserId>");
                failures += Check(log, "scheduler account-name normalization preserves exact user identity",
                    Inspect(accountXml, path, currentSid).Enabled);
                failures += Check(log, "different logon account cannot be treated as current-user startup",
                    !Inspect(xml.Replace("<LogonTrigger><Enabled>true</Enabled><UserId>" + sid,
                        "<LogonTrigger><Enabled>true</Enabled><UserId>" + currentSid), path, sid).Enabled);
            }
            string[] drift = new string[]
            {
                xml.Replace("<DisallowStartIfOnBatteries>false", "<DisallowStartIfOnBatteries>true"),
                xml.Replace("<StopIfGoingOnBatteries>false", "<StopIfGoingOnBatteries>true"),
                xml.Replace("<RunOnlyIfIdle>false", "<RunOnlyIfIdle>true"),
                xml.Replace("<RunOnlyIfNetworkAvailable>false", "<RunOnlyIfNetworkAvailable>true"),
                xml.Replace("<ExecutionTimeLimit>PT0S", "<ExecutionTimeLimit>PT1H"),
                xml.Replace("<MultipleInstancesPolicy>IgnoreNew", "<MultipleInstancesPolicy>Parallel")
            };
            foreach (string changed in drift)
            {
                AutostartState altered = Inspect(changed, path, sid);
                failures += Check(log, "changed task policy requires explicit repair",
                    !altered.Enabled && !String.IsNullOrEmpty(altered.RepairReason));
            }
            bool staleRejected = false;
            try
            {
                AutostartManager.RequireUnchanged(state, Inspect(
                    AutostartManager.BuildXml(@"C:\Third copy\Meter.exe", sid), path, sid));
            }
            catch (InvalidOperationException)
            {
                staleRejected = true;
            }
            failures += Check(log, "confirmation for copy A cannot authorize overwriting concurrent copy C", staleRejected);
            staleRejected = false;
            try
            {
                AutostartManager.RequireUnchanged(state, AutostartManager.Inspect(xml, path, sid, SelfTestCopy(sid), @"C:\Third copy\Meter.exe"));
            }
            catch (InvalidOperationException)
            {
                staleRejected = true;
            }
            failures += Check(log, "confirmation cannot authorize overwriting a protected copy another installation now owns", staleRejected);
            AutostartManager.RequireUnchanged(state, Inspect(xml, path, sid));
            return failures;
        }

        private static string SelfTestCopy(string sid)
        {
            return new ProtectedCopy(ProtectedCopy.DefaultRoot, sid).Executable;
        }

        // Reads a task as if this user's protected copy was taken from path.
        private static AutostartState Inspect(string xml, string path, string sid)
        {
            return AutostartManager.Inspect(xml, path, sid, SelfTestCopy(sid), path);
        }

        private static int SupplyStateCases(StringBuilder log)
        {
            int failures = 0;
            BatterySupplyState inconsistent = BatterySupplyStates.FromFlags(
                false, true, true);
            failures += Check(log, "contradictory battery flags form one invalid state",
                inconsistent == BatterySupplyState.Inconsistent
                    && BatterySupplyStates.FromFlags(false, true, false)
                        == BatterySupplyState.Inconsistent
                    && !BatterySupplyStates.Describe(inconsistent).StatusAvailable);

            BatterySupplyState supplemented = BatterySupplyStates.FromFlags(
                true, false, true);
            BatterySupplyProfile profile = BatterySupplyStates.Describe(supplemented);
            failures += Check(log, "one supply profile drives window and tray presentation",
                supplemented == BatterySupplyState.ExternalPowerSupplemented
                    && profile.StatusAvailable
                    && profile.PowerOnline
                    && !profile.Charging
                    && profile.Discharging
                    && profile.StateText == "DISCHARGING"
                    && profile.TrayMode == "Discharging"
                    && profile.Accent == BatteryAccentKind.Discharging);
            return failures;
        }

        private static int RateCases(StringBuilder log)
        {
            int failures = 0;

            // Charging with a known charge rate.
            BatteryReading reading = new BatteryReading();
            reading.SupplyState = BatterySupplyState.ExternalPowerCharging;
            BatterySensor.ResolveRate(reading, 43802, 0);
            BatterySensor.ResolveElectricalDetails(reading, 12000);
            failures += Check(log, "charging, charge rate known",
                reading.RateAvailable
                    && reading.VoltageAvailable
                    && reading.CurrentAvailable
                    && Near(reading.PowerWatts, 43.802)
                    && Near(reading.VoltageVolts, 12.0)
                    && Near(reading.CurrentAmps, 43.802 / 12.0));

            // Charging while only the opposite direction reports a rate. The
            // rate for the direction in effect is unknown, so nothing may be
            // presented as a measurement.
            reading = new BatteryReading();
            reading.SupplyState = BatterySupplyState.ExternalPowerCharging;
            BatterySensor.ResolveRate(reading, UInt32.MaxValue, 0);
            failures += Check(log, "charging, only discharge rate known",
                !reading.RateAvailable);

            // Discharging with a known discharge rate yields negative power.
            reading = new BatteryReading();
            reading.SupplyState = BatterySupplyState.BatteryDischarging;
            BatterySensor.ResolveRate(reading, 0, 25000);
            failures += Check(log, "discharging, discharge rate known",
                reading.RateAvailable && Near(reading.PowerWatts, -25.0));

            // Discharging while only the opposite direction reports a rate.
            reading = new BatteryReading();
            reading.SupplyState = BatterySupplyState.BatteryDischarging;
            BatterySensor.ResolveRate(reading, 0, UInt32.MaxValue);
            failures += Check(log, "discharging, only charge rate known",
                !reading.RateAvailable);

            // Neither direction active while external power is present is a
            // real zero, not an absence.
            reading = new BatteryReading();
            reading.SupplyState = BatterySupplyState.ExternalPowerIdle;
            BatterySensor.ResolveRate(reading, UInt32.MaxValue, UInt32.MaxValue);
            failures += Check(log, "online idle battery reports a known zero",
                reading.RateAvailable && Near(reading.PowerWatts, 0.0));

            // With no external power and neither direction flag, firmware has
            // not identified what supplies the running machine. Calling that a
            // measured zero would fabricate a whole-system reading.
            reading = new BatteryReading();
            BatterySensor.ResolveRate(reading, UInt32.MaxValue, UInt32.MaxValue);
            BatterySensor.ResolveElectricalDetails(reading, 12000);
            failures += Check(log, "offline battery without a direction is unavailable",
                !reading.RateAvailable
                    && reading.VoltageAvailable
                    && !reading.CurrentAvailable
                    && Near(reading.VoltageVolts, 12.0));

            return failures;
        }

        private static int BatteryAggregationCases(StringBuilder log)
        {
            int failures = 0;
            BatteryReading first = Battery(false, false, true, -10.0);
            BatteryReading second = Battery(false, false, true, -15.0);
            first.Percentage = 12;
            second.Percentage = 88;

            BatteryReading combined = BatterySensor.CombineReadings(
                new BatteryReading[] { first, second });
            BatterySensor.AssignSystemPercentage(combined, 64);

            failures += Check(log, "two active batteries aggregate terminal power",
                combined.StatusAvailable
                    && combined.ActiveBatteryCount == 2
                    && combined.RateAvailable
                    && combined.Discharging
                    && combined.Percentage == 64
                    && Near(combined.PowerWatts, -25.0));

            BatteryReading unavailable = BatterySensor.CombineReadings(
                new BatteryReading[0]);
            BatterySensor.AssignSystemPercentage(unavailable, 64);
            failures += Check(log, "missing active battery is not labelled on battery",
                unavailable.SupplyProfile.StateText == "BATTERY UNAVAILABLE"
                    && unavailable.Percentage == -1);

            return failures;
        }

        private static int WholeSystemCases(StringBuilder log)
        {
            int failures = 0;
            PowerSample platform = PowerSample.FromValue(
                PowerBoundary.Platform, MeasurementKind.Measured, 24.0, "test", TimeSpan.FromSeconds(1));

            // On battery the machine draws nothing through the port, so the
            // platform figure must not be presented as input power.
            PowerSample result = Whole(platform, Battery(false, false, true, -26.5));
            failures += Check(log, "on battery reports measured system load",
                result.Available
                    && result.Boundary == PowerBoundary.SystemLoad
                    && result.Kind == MeasurementKind.Measured
                    && Near(result.Watts, 26.5));

            failures += Check(log, "on battery never reports input power",
                result.Boundary != PowerBoundary.EstimatedSystemInput);

            // On battery with an unknown discharge rate there is no figure.
            result = Whole(platform, Battery(false, false, false, 0.0));
            failures += Check(log, "on battery without a rate is unsupported", !result.Available);

            // On external power while charging, input is platform plus charge.
            result = Whole(platform, Battery(true, true, true, 13.27));
            failures += Check(log, "on AC while charging estimates input",
                result.Available
                    && result.Boundary == PowerBoundary.EstimatedSystemInput
                    && result.Kind == MeasurementKind.Estimated
                    && Near(result.Watts, 37.27));

            // On external power with the battery supplementing the adapter, the
            // port supplies less than the platform draws. Clamping the battery
            // term to zero here would overstate input.
            result = Whole(platform, Battery(true, false, true, -5.0));
            failures += Check(log, "on AC while battery supplements subtracts",
                result.Available && Near(result.Watts, 19.0));

            result = Whole(platform, Battery(true, false, true, -30.0));
            failures += Check(log, "negative estimated input is rejected",
                !result.Available
                    && result.UnavailableReason == "估算结果为负，数据边界或采样窗口不一致");

            // A full battery on external power contributes nothing.
            result = Whole(platform, Battery(true, false, true, 0.0));
            failures += Check(log, "on AC with an idle battery equals platform",
                result.Available && Near(result.Watts, 24.0));

            // Without platform power there is no input estimate, and the reason
            // has to survive rather than be replaced by a substituted number.
            PowerSample missing = PowerSample.Unsupported(PowerBoundary.Platform, "驱动未安装");
            result = Whole(missing, Battery(true, true, true, 13.27));
            failures += Check(log, "on AC without platform power is unsupported",
                !result.Available && result.UnavailableReason == "驱动未安装");

            // On AC with an unknown rate for the direction in effect.
            result = Whole(platform, Battery(true, true, false, 0.0));
            failures += Check(log, "on AC without a battery rate is unsupported", !result.Available);

            result = Whole(platform, null);
            failures += Check(log, "missing battery reading is unsupported", !result.Available);

            BatteryReading noBattery = BatterySensor.CombineReadings(
                new BatteryReading[0]);
            result = Whole(platform, noBattery);
            failures += Check(log, "no active battery preserves its status reason",
                !result.Available
                    && result.UnavailableReason == noBattery.StatusUnavailableReason);

            return failures;
        }

        private static int SessionCases(StringBuilder log)
        {
            int failures = 0;
            PowerSample cpuPackage = PowerSample.FromValue(PowerBoundary.CpuPackage,
                MeasurementKind.Measured, 14.11, "fixture", TimeSpan.FromSeconds(1));
            PowerSample platform = PowerSample.FromValue(PowerBoundary.Platform,
                MeasurementKind.Measured, 27, "fixture", TimeSpan.FromSeconds(1));
            PowerSample noPlatform = PowerSample.Unsupported(PowerBoundary.Platform, "需要管理员权限");
            DateTimeOffset time = new DateTimeOffset(2026, 9, 27, 10, 20, 30, TimeSpan.FromHours(8));
            BatteryReading charging = Battery(true, true, true, 39.08);

            MeterSession session = new MeterSession(DisplayMode.WholeSystem);
            MeterView view = session.Observe(PowerSnapshot.Compose(time, 1, charging, cpuPackage, platform));
            failures += Check(log, "charging headline, row and tray show the estimated total, not battery or extra CPU",
                view.Headline.Text == "≈ 66.08 W" && view.Headline.Tone == ReadoutTone.NumericMuted
                && view.Rows[MeterView.WholeSystemRow].Text == "≈ 66.08 W"
                && view.Rows[MeterView.WholeSystemRow].Tone == ReadoutTone.Muted
                && view.Rows[MeterView.CpuPackageRow].Text == "14.11 W"
                && view.Rows[MeterView.CpuPackageRow].Tone == ReadoutTone.Ink
                && view.TrayGlyph == "66" && view.Accent == BatteryAccentKind.Charging
                && view.TrayTooltip == Strings.Get("估算整机输入功率") + ": ≈ 66.08 W"
                && view.Supply.Text == Strings.Get("已接电源 · 充电中") && view.Updated == "10:20:30");

            view = session.SetMode(DisplayMode.Battery);
            failures += Check(log, "battery mode changes headline and tray together and restarts statistics",
                session.Mode == DisplayMode.Battery && view.Headline.Text == "39.08 W"
                && view.Headline.Tone == ReadoutTone.Accent && view.TrayGlyph == "39"
                && view.TrayTooltip == Strings.Get("电池端净功率") + ": 39.08 W"
                && view.AverageValue == "-- W" && view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 0.0));
            view = session.Observe(PowerSnapshot.Compose(time, 2, charging, cpuPackage, platform));
            failures += Check(log, "mode change seeds statistics with the current reading",
                view.AverageValue == "39.08 W" && view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 1.0));
            session.Render();
            view = session.Render();
            failures += Check(log, "re-rendering does not record another reading",
                view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 1.0));

            session = new MeterSession(DisplayMode.WholeSystem);
            view = session.Observe(PowerSnapshot.Compose(time, 1, charging, cpuPackage, noPlatform));
            failures += Check(log, "missing platform clears the total and tray and explains it once",
                view.Headline.Text == "N/A" && view.Rows[MeterView.WholeSystemRow].Text == "N/A"
                && view.TrayGlyph == "--" && view.Diagnostics.Count == 1
                && view.Diagnostics[0] == Strings.Get("平台功率") + ": " + Strings.Diagnostic("需要管理员权限"));

            BatteryReading unplugged = Battery(false, false, true, -17.5);
            view = session.Observe(PowerSnapshot.Compose(time, 2, unplugged, cpuPackage, noPlatform));
            failures += Check(log, "unplugged whole display works without platform or elevation",
                view.Headline.Text == "17.50 W" && view.TrayGlyph == "18"
                && view.TrayTooltip == Strings.Get("系统负载功率") + ": 17.50 W"
                && view.WholeCaption == Strings.Get("系统负载功率") && view.Diagnostics.Count == 1);

            BatteryReading supplemented = Battery(true, false, true, -5);
            session = new MeterSession(DisplayMode.WholeSystem);
            PowerSample platform24 = PowerSample.FromValue(PowerBoundary.Platform,
                MeasurementKind.Measured, 24, "fixture", TimeSpan.FromSeconds(1));
            session.Observe(PowerSnapshot.Compose(time, 0, supplemented, cpuPackage, platform24));
            view = session.Observe(PowerSnapshot.Compose(time, 1, supplemented, cpuPackage, platform24));
            failures += Check(log, "battery supplementation reduces the estimate and statistics stay estimated",
                view.Headline.Text == "≈ 19.00 W" && view.AverageValue == "≈ 19.00 W" && view.PeakValue == "≈ 19.00 W"
                && view.Supply.Text == Strings.Get("已接电源 · 电池补充") && view.Accent == BatteryAccentKind.Discharging);

            view = session.Fail("sensor failed");
            failures += Check(log, "sensor failure clears statistics and never shows a stale figure",
                view.Headline.Text == "N/A" && view.Accent == BatteryAccentKind.Error
                && view.Supply.Text == Strings.Get("传感器异常") && view.TrayGlyph == "--"
                && view.TrayTooltip == Strings.Get("整机功率") + ": N/A"
                && view.Diagnostics.Count == 1 && view.Diagnostics[0] == "sensor failed"
                && view.AverageValue == "-- W" && view.PeakValue == "-- W" && view.Updated == "10:20:30"
                && view.Rows[MeterView.PlatformRow].Text == "N/A" && view.BatteryLevel == -1);
            view = session.Render();
            failures += Check(log, "sensor failure survives re-rendering until the next reading",
                view.Supply.Text == Strings.Get("传感器异常"));
            view = session.Observe(PowerSnapshot.Compose(time, 10, supplemented, cpuPackage, platform24));
            failures += Check(log, "readings after a failure start new statistics",
                view.Headline.Text == "≈ 19.00 W" && view.AverageValue == "-- W");

            view = new MeterSession(DisplayMode.Battery).Render();
            failures += Check(log, "before the first reading the tray keeps the application icon",
                view.TrayGlyph == null && view.Headline.Text == "--.-- W" && view.Diagnostics.Count == 0);
            return failures;
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

        private static int LayoutCases(StringBuilder log)
        {
            Size viewport = DpiLayout.ConstrainClientSize(
                new Size(1290, 1500),
                new Size(1920, 1040),
                new Size(16, 39));

            return Check(log, "300 percent layout stays reachable on a 1080p display",
                viewport.Width == 1290 && viewport.Height == 985);
        }

        private static int LanguageCases(StringBuilder log)
        {
            int failures = Check(log, "language preference overrides the system, with safe fallback",
                Strings.Resolve("en", "zh-CN") == "en"
                && Strings.Resolve("zh-CN", "en-US") == "zh-CN"
                && Strings.Resolve("system", "zh-TW") == "zh-CN"
                && Strings.Resolve("invalid", "fr-FR") == "en");
            string saved = Strings.Preference;
            try
            {
                const string cached = "需要以管理员身份运行才能读取平台功率";
                Strings.Select("en", false);
                failures += Check(log, "English branding and cached diagnostic presentation",
                    Strings.AppName == "Power Meter" && Strings.Diagnostic(cached) == "Run as administrator to read platform power"
                    && Strings.Diagnostic("EMI 初始化失败: C:\\测试") == "EMI initialization failed: C:\\测试");
                Strings.Select("zh-CN", false);
                failures += Check(log, "Chinese branding and diagnostics switch back without driver restart",
                    Strings.AppName == "功率计" && Strings.Diagnostic(cached) == cached);
            }
            finally { Strings.Select(saved == "en" || saved == "zh-CN" ? saved : "system", false); }
            return failures;
        }

        private static int EmiMetadataCases(StringBuilder log)
        {
            byte[] channelName = Encoding.Unicode.GetBytes("CPU_PKG\0");
            byte[] metadata = new byte[72 + channelName.Length];
            Buffer.BlockCopy(
                BitConverter.GetBytes((ushort)channelName.Length), 0, metadata, 70, 2);
            Buffer.BlockCopy(channelName, 0, metadata, 72, channelName.Length);

            IList<string> channels = EmiSensor.ReadSupportedChannels(
                metadata,
                delegate(byte[] ignored) { return (ushort)1; },
                delegate(byte[] bytes, ushort version)
                {
                    return NativeEmi.ParseChannelNames(version, bytes);
                });
            int failures = Check(log, "EMI V1 version and package metadata are accepted",
                channels.Count == 1 && channels[0] == "CPU_PKG");

            EmiDiscoveryResult discovery = EmiSensor.SelectPackageDevice(
                new string[] { "bad-device", "good-device" },
                delegate(string path)
                {
                    if (path == "bad-device")
                        throw new InvalidOperationException("broken metadata");
                    return EmiSensor.ReadSupportedChannels(
                        metadata,
                        delegate(byte[] ignored) { return (ushort)1; },
                        delegate(byte[] bytes, ushort version)
                        {
                            return NativeEmi.ParseChannelNames(version, bytes);
                        });
                });

            failures += Check(log, "bad EMI device does not hide a later package meter",
                discovery.DevicePath == "good-device"
                    && discovery.SelectedChannels.Count == 1
                    && discovery.DiscoveredChannels.Count == 1
                    && discovery.Errors.Count == 1);
            return failures;
        }

        private static int ValidationCases(StringBuilder log)
        {
            PowerSample platform = PowerSample.FromValue(
                PowerBoundary.Platform,
                MeasurementKind.Measured,
                24.0,
                "PawnIO MSR 0x64D",
                TimeSpan.FromSeconds(1));
            PowerSample missingEmi = PowerSample.Unsupported(
                PowerBoundary.CpuPackage, "本机没有 EMI 电表设备");

            PowerSample validated = PowerSources.ValidatePlatform(
                platform, missingEmi, 12.0);

            return Check(log, "Psys remains available without EMI cross-check",
                validated.Available
                    && Near(validated.Watts, 24.0)
                    && validated.Source.IndexOf("未交叉验证", StringComparison.Ordinal) >= 0);
        }

        private static int CounterCases(StringBuilder log)
        {
            double watts;
            TimeSpan window;
            bool accepted = EmiSensor.TryCalculatePower(
                2000, 200, 1000, 300, out watts, out window);

            int failures = Check(
                log, "EMI counter regression requires a new baseline", !accepted);
            failures += Check(log, "EMI long pause discards the counter interval",
                !EmiSensor.TryCalculatePower(0, 1, 10000, 600000001, out watts, out window));

            RaplSampleTracker tracker = new RaplSampleTracker();
            PowerSample platform;
            double packageWatts;
            bool first = tracker.TryAdvance(
                1000, 2000, 0, 1000, 0.001, out platform, out packageWatts);
            bool longWindow = tracker.TryAdvance(
                2000, 4000, 60000, 1000, 0.001, out platform, out packageWatts);
            bool resumed = tracker.TryAdvance(
                2100, 4200, 61000, 1000, 0.001, out platform, out packageWatts);
            failures += Check(log, "long RAPL window re-baselines the production tracker",
                !first
                    && !longWindow
                    && resumed
                    && platform.Available
                    && Near(platform.Watts, 0.2)
                    && Near(packageWatts, 0.1));
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

        private static bool Near(double actual, double expected)
        {
            return Math.Abs(actual - expected) < Tolerance;
        }

        private static int Check(StringBuilder log, string name, bool condition)
        {
            log.AppendLine((condition ? "PASS  " : "FAIL  ") + name);
            return condition ? 0 : 1;
        }
    }
}
