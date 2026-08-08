using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

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

            log.AppendLine();
            passed = failures == 0;
            log.AppendLine(passed
                ? "Power self test passed."
                : failures.ToString(CultureInfo.InvariantCulture) + " power self test case(s) failed.");

            return log.ToString();
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

        private static int LayoutCases(StringBuilder log)
        {
            Size viewport = DpiLayout.ConstrainClientSize(
                new Size(1290, 1500),
                new Size(1920, 1040),
                new Size(16, 39));

            return Check(log, "300 percent layout stays reachable on a 1080p display",
                viewport.Width == 1290 && viewport.Height == 985);
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
