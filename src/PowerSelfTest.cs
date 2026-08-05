using System;
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

            failures += RateCases(log);
            failures += WholeSystemCases(log);

            log.AppendLine();
            passed = failures == 0;
            log.AppendLine(passed
                ? "Power self test passed."
                : failures.ToString(CultureInfo.InvariantCulture) + " power self test case(s) failed.");

            return log.ToString();
        }

        private static int RateCases(StringBuilder log)
        {
            int failures = 0;

            // Charging with a known charge rate.
            BatteryReading reading = new BatteryReading();
            reading.Charging = true;
            BatterySensor.ResolveRate(reading, 43802, 0);
            failures += Check(log, "charging, charge rate known",
                reading.RateAvailable && Near(reading.PowerWatts, 43.802));

            // Charging while only the opposite direction reports a rate. The
            // rate for the direction in effect is unknown, so nothing may be
            // presented as a measurement.
            reading = new BatteryReading();
            reading.Charging = true;
            BatterySensor.ResolveRate(reading, UInt32.MaxValue, 0);
            failures += Check(log, "charging, only discharge rate known",
                !reading.RateAvailable);

            // Discharging with a known discharge rate yields negative power.
            reading = new BatteryReading();
            reading.Discharging = true;
            BatterySensor.ResolveRate(reading, 0, 25000);
            failures += Check(log, "discharging, discharge rate known",
                reading.RateAvailable && Near(reading.PowerWatts, -25.0));

            // Discharging while only the opposite direction reports a rate.
            reading = new BatteryReading();
            reading.Discharging = true;
            BatterySensor.ResolveRate(reading, 0, UInt32.MaxValue);
            failures += Check(log, "discharging, only charge rate known",
                !reading.RateAvailable);

            // Neither direction active is a real zero, not an absence.
            reading = new BatteryReading();
            BatterySensor.ResolveRate(reading, UInt32.MaxValue, UInt32.MaxValue);
            failures += Check(log, "idle battery reports a known zero",
                reading.RateAvailable && Near(reading.PowerWatts, 0.0));

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

            return failures;
        }

        private static BatteryReading Battery(
            bool online, bool charging, bool rateAvailable, double watts)
        {
            BatteryReading reading = new BatteryReading();
            reading.PowerOnline = online;
            reading.Charging = charging;
            reading.Discharging = watts < 0.0;
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
