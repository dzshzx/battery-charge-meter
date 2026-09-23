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
            failures += PresentationCases(log);
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
                PowerSnapshot snapshot = new PowerSnapshot { Battery = battery, BatteryTerminal = PowerSources.BatterySample(battery), WholeSystem = PowerSources.DeriveWholeSystem(platform, battery) };
                failures += Check(log, "selected boundary is shared without substitution: " + battery.SupplyState,
                    Object.ReferenceEquals(PowerDisplay.Select(snapshot, DisplayMode.WholeSystem), snapshot.WholeSystem)
                    && Near(PowerDisplay.Select(snapshot, DisplayMode.Battery).Watts, battery.PowerWatts));
            }
            BatteryReading charging = Battery(true, true, true, 13);
            PowerSnapshot missing = new PowerSnapshot { BatteryTerminal = PowerSources.BatterySample(charging), WholeSystem = PowerSources.DeriveWholeSystem(PowerSample.Unsupported(PowerBoundary.Platform, "missing"), charging) };
            failures += Check(log, "whole mode does not fall back to available battery", !PowerDisplay.Select(missing, DisplayMode.WholeSystem).Available && PowerDisplay.Select(missing, DisplayMode.Battery).Available);
            failures += Check(log, "preferences default safely for missing malformed or inaccessible storage",
                DisplayPreference.Load(delegate { return null; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "invalid"; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { throw new InvalidOperationException(); }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "Battery"; }) == DisplayMode.Battery);
            return failures;
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
            failures += Check(log, "missing sample is a chart break and immediately unavailable statistics", !history.Peak().HasValue && !history.Average(out coverage).HasValue && !history.Points[3].Watts.HasValue);
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
            IList<TimedPower> visible = PowerDisplay.VisibleHistory(new TimedPower[]
            {
                new TimedPower { Seconds = 0, Watts = 1000 },
                new TimedPower { Seconds = 4, Watts = 10 },
                new TimedPower { Seconds = 62, Watts = 12 }
            });
            failures += Check(log, "chart excludes expired predecessor from segments and vertical scale",
                visible.Count == 2 && Near(visible[0].Seconds, 4) && Near(visible[0].Watts.Value, 10));
            failures += Check(log, "nonfinite sensor values cannot reach chart or tray", !Value(Double.NaN).Available && !Value(Double.PositiveInfinity).Available);
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
            string sid = "S-1-5-21-111-222-333-1001";
            string path = @"C:\Test & 测试\Meter 1.2.1.exe";
            string xml = AutostartManager.BuildXml(path, sid);
            AutostartState state = AutostartManager.Inspect(xml, path, sid);
            failures += Check(log, "logon task roundtrips escaped exact executable path and elevated interactive user policy",
                state.Exists && state.Enabled && state.ThisCopy && state.Executable == path
                && xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>")
                && xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>")
                && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
                && xml.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"));
            failures += Check(log, "other copies are distinguished without registry booleans",
                !AutostartManager.Inspect(xml, @"C:\Other\Meter.exe", sid).ThisCopy);
            failures += Check(log, "disabled task is read as disabled",
                !AutostartManager.Inspect(xml.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>"), path, sid).Enabled);
            bool foreignRejected = false;
            try
            {
                AutostartManager.Inspect(xml.Replace("BatteryChargeMeter.Logon.v1", "someone-else"), path, sid);
            }
            catch (InvalidOperationException)
            {
                foreignRejected = true;
            }
            failures += Check(log, "foreign task ownership marker prevents mutation", foreignRejected);
            using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                string currentSid = identity.User.Value;
                string accountXml = AutostartManager.BuildXml(path, currentSid).Replace(currentSid,
                    System.Security.SecurityElement.Escape(identity.Name));
                failures += Check(log, "scheduler account-name normalization preserves exact user identity",
                    AutostartManager.Inspect(accountXml, path, currentSid).Enabled);
                failures += Check(log, "different logon account cannot be treated as current-user startup",
                    !AutostartManager.Inspect(xml.Replace("<LogonTrigger><Enabled>true</Enabled><UserId>" + sid,
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
                AutostartState altered = AutostartManager.Inspect(changed, path, sid);
                failures += Check(log, "changed task policy requires explicit repair",
                    !altered.Enabled && !String.IsNullOrEmpty(altered.RepairReason));
            }
            bool staleRejected = false;
            try
            {
                AutostartManager.RequireUnchanged(state, AutostartManager.Inspect(
                    AutostartManager.BuildXml(@"C:\Third copy\Meter.exe", sid), path, sid));
            }
            catch (InvalidOperationException)
            {
                staleRejected = true;
            }
            failures += Check(log, "confirmation for copy A cannot authorize overwriting concurrent copy C", staleRejected);
            AutostartManager.RequireUnchanged(state, AutostartManager.Inspect(xml, path, sid));
            return failures;
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
            PowerSample result = PowerSources.DeriveWholeSystem(platform, Battery(false, false, true, -26.5));
            failures += Check(log, "on battery reports measured system load",
                result.Available
                    && result.Boundary == PowerBoundary.SystemLoad
                    && result.Kind == MeasurementKind.Measured
                    && Near(result.Watts, 26.5));

            failures += Check(log, "on battery never reports input power",
                result.Boundary != PowerBoundary.EstimatedSystemInput);

            // On battery with an unknown discharge rate there is no figure.
            result = PowerSources.DeriveWholeSystem(platform, Battery(false, false, false, 0.0));
            failures += Check(log, "on battery without a rate is unsupported", !result.Available);

            // On external power while charging, input is platform plus charge.
            result = PowerSources.DeriveWholeSystem(platform, Battery(true, true, true, 13.27));
            failures += Check(log, "on AC while charging estimates input",
                result.Available
                    && result.Boundary == PowerBoundary.EstimatedSystemInput
                    && result.Kind == MeasurementKind.Estimated
                    && Near(result.Watts, 37.27));

            // On external power with the battery supplementing the adapter, the
            // port supplies less than the platform draws. Clamping the battery
            // term to zero here would overstate input.
            result = PowerSources.DeriveWholeSystem(platform, Battery(true, false, true, -5.0));
            failures += Check(log, "on AC while battery supplements subtracts",
                result.Available && Near(result.Watts, 19.0));

            result = PowerSources.DeriveWholeSystem(platform, Battery(true, false, true, -30.0));
            failures += Check(log, "negative estimated input is rejected",
                !result.Available
                    && result.UnavailableReason == "估算结果为负，数据边界或采样窗口不一致");

            // A full battery on external power contributes nothing.
            result = PowerSources.DeriveWholeSystem(platform, Battery(true, false, true, 0.0));
            failures += Check(log, "on AC with an idle battery equals platform",
                result.Available && Near(result.Watts, 24.0));

            // Without platform power there is no input estimate, and the reason
            // has to survive rather than be replaced by a substituted number.
            PowerSample missing = PowerSample.Unsupported(PowerBoundary.Platform, "驱动未安装");
            result = PowerSources.DeriveWholeSystem(missing, Battery(true, true, true, 13.27));
            failures += Check(log, "on AC without platform power is unsupported",
                !result.Available && result.UnavailableReason == "驱动未安装");

            // On AC with an unknown rate for the direction in effect.
            result = PowerSources.DeriveWholeSystem(platform, Battery(true, true, false, 0.0));
            failures += Check(log, "on AC without a battery rate is unsupported", !result.Available);

            result = PowerSources.DeriveWholeSystem(platform, null);
            failures += Check(log, "missing battery reading is unsupported", !result.Available);

            BatteryReading noBattery = BatterySensor.CombineReadings(
                new BatteryReading[0]);
            result = PowerSources.DeriveWholeSystem(platform, noBattery);
            failures += Check(log, "no active battery preserves its status reason",
                !result.Available
                    && result.UnavailableReason == noBattery.StatusUnavailableReason);

            return failures;
        }

        private static int PresentationCases(StringBuilder log)
        {
            // Exercise the actual window/tray consumer of the shared snapshot;
            // pure selector tests alone cannot catch an old battery-only call site.
            int failures = 0;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            Type formType = typeof(MainForm);
            using (MainForm form = new MainForm(true))
            {
                BatteryReading battery = Battery(true, true, true, 39.08);
                PowerSnapshot snapshot = new PowerSnapshot();
                snapshot.Battery = battery;
                snapshot.Timestamp = DateTimeOffset.Now;
                snapshot.ElapsedSeconds = 1;
                snapshot.BatteryTerminal = PowerSources.BatterySample(battery);
                snapshot.Platform = PowerSample.FromValue(PowerBoundary.Platform,
                    MeasurementKind.Measured, 27, "fixture", TimeSpan.FromSeconds(1));
                snapshot.CpuPackage = PowerSample.FromValue(PowerBoundary.CpuPackage,
                    MeasurementKind.Measured, 14.11, "fixture", TimeSpan.FromSeconds(1));
                snapshot.WholeSystem = PowerSources.DeriveWholeSystem(snapshot.Platform, battery);
                formType.GetField("latest", flags).SetValue(form, snapshot);
                formType.GetField("displayMode", flags).SetValue(form, DisplayMode.WholeSystem);
                System.Reflection.MethodInfo present = formType.GetMethod("PresentSnapshot", flags);
                present.Invoke(form, new object[] { snapshot, true });
                Label headline = (Label)formType.GetField("powerLabel", flags).GetValue(form);
                Label whole = (Label)formType.GetField("wholeSystemValue", flags).GetValue(form);
                NotifyIcon tray = (NotifyIcon)formType.GetField("trayIcon", flags).GetValue(form);
                failures += Check(log, "charging window and tray display total, not battery or extra CPU",
                    headline.Text == "≈ 66.08 W" && whole.Text == headline.Text
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "66"
                    && tray.Text.Contains(Strings.Get("估算整机输入功率")) && tray.Text.Contains("≈ 66.08 W"));

                formType.GetField("displayMode", flags).SetValue(form, DisplayMode.Battery);
                snapshot.ElapsedSeconds = 2;
                present.Invoke(form, new object[] { snapshot, true });
                failures += Check(log, "battery mode changes actual headline and tray together",
                    headline.Text == "39.08 W" && tray.Text.Contains(Strings.Get("电池端净功率"))
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "39");

                formType.GetField("displayMode", flags).SetValue(form, DisplayMode.WholeSystem);
                snapshot.Platform = PowerSample.Unsupported(PowerBoundary.Platform, "需要管理员权限");
                snapshot.WholeSystem = PowerSources.DeriveWholeSystem(snapshot.Platform, battery);
                snapshot.ElapsedSeconds = 3;
                present.Invoke(form, new object[] { snapshot, true });
                TextBox reason = (TextBox)formType.GetField("errorLabel", flags).GetValue(form);
                failures += Check(log, "missing platform clears actual total and tray and shows reason",
                    headline.Text == "N/A" && whole.Text == "N/A" && reason.Text.Contains("需要管理员权限")
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "--");

                battery.SupplyState = BatterySupplyState.BatteryDischarging;
                battery.PowerWatts = -17.5;
                snapshot.BatteryTerminal = PowerSources.BatterySample(battery);
                snapshot.WholeSystem = PowerSources.DeriveWholeSystem(snapshot.Platform, battery);
                snapshot.ElapsedSeconds = 4;
                present.Invoke(form, new object[] { snapshot, true });
                failures += Check(log, "unplugged whole display works without platform or elevation",
                    headline.Text == "17.50 W" && tray.Text.Contains(Strings.Get("系统负载功率"))
                    && (string)formType.GetField("lastTrayGlyph", flags).GetValue(form) == "18");
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
