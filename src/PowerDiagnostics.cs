using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Console report of what this machine can actually measure. Power source
    /// availability is a per-machine hardware and firmware fact, so users and
    /// bug reports need a way to see the capability discovery result directly
    /// rather than inferring it from a greyed-out label.
    /// </summary>
    internal static class PowerDiagnostics
    {
        public static string Run(int seconds)
        {
            if (seconds < 1)
                throw new ArgumentOutOfRangeException("seconds", "Sample duration must be at least 1 second.");

            StringBuilder output = new StringBuilder();

            using (PowerSources sources = new PowerSources())
            {
                output.AppendLine("Power source discovery");
                output.AppendLine("  Elevated token: " + Startup.IsElevated());
                output.AppendLine("  Timestamp is application observation time, not a BMS timestamp.");
                output.AppendLine("  Battery firmware window / BMS internal timestamp: unknown.");
                output.AppendLine("  CPU EMI cross-check validates energy units, not Psys rail coverage or total accuracy.");
                output.AppendLine("  CPU package (EMI): " + sources.CpuPackageStatus);
                output.AppendLine("  Platform (PawnIO): " + sources.PlatformStatus);
                output.AppendLine("  EMI channels: " + DescribeChannels(sources.EmiChannels));
                output.AppendLine("  EMI discovery errors: " + DescribeChannels(sources.EmiDiscoveryErrors));
                output.AppendLine();

                // The energy counters are cumulative, so one tick is spent
                // establishing baselines before any figure exists. It is taken
                // outside the reporting window so that the requested duration is
                // the number of rows produced.
                Baseline(sources);

                output.AppendLine(String.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-7} {1,-14} {2,-14} {3,-14} {4}",
                    "t",
                    "Battery_W",
                    "CpuPkg_W",
                    "Platform_W",
                    "WholeSystem_W"));

                for (int second = 1; second <= seconds; second++)
                {
                    Thread.Sleep(1000);

                    PowerSnapshot snapshot = sources.Capture();
                    BatteryReading battery = snapshot.Battery;
                    output.AppendLine(String.Format(CultureInfo.InvariantCulture,
                        "timestamp={0:o}; elapsed={1:0.000}s; supply={2}; active_batteries={3}; battery_voltage={4}; battery_current_estimate={5}",
                        snapshot.Timestamp, snapshot.ElapsedSeconds, battery.SupplyState, battery.ActiveBatteryCount,
                        battery.VoltageAvailable ? battery.VoltageVolts.ToString("0.000", CultureInfo.InvariantCulture) + " V" : "n/a",
                        battery.CurrentAvailable ? "≈ " + battery.CurrentAmps.ToString("0.000", CultureInfo.InvariantCulture) + " A" : "n/a"));
                    foreach (string raw in battery.RawReadings) output.AppendLine("  raw battery: " + raw);

                    output.AppendLine(String.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-7} {1,-14} {2,-14} {3,-14} {4}",
                        snapshot.ElapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s",
                        Describe(snapshot.BatteryTerminal),
                        Describe(snapshot.CpuPackage),
                        Describe(snapshot.Platform),
                        DescribeWholeSystem(snapshot.WholeSystem)));
                }
            }

            return output.ToString();
        }

        private static void Baseline(PowerSources sources)
        {
            Thread.Sleep(1000);
            try
            {
                sources.Capture();
            }
            catch (Exception)
            {
                // A failure here is reported by the first real row instead.
            }
        }

        private static string DescribeChannels(IList<string> channels)
        {
            if (channels == null || channels.Count == 0)
                return "(none)";

            string[] copy = new string[channels.Count];
            channels.CopyTo(copy, 0);
            return String.Join(", ", copy);
        }

        /// <summary>
        /// The whole-system boundary changes with the supply state, so the
        /// report names the boundary alongside the value rather than letting a
        /// fixed column heading imply the wrong one.
        /// </summary>
        private static string DescribeWholeSystem(PowerSample sample)
        {
            string value = Describe(sample);
            if (sample == null || !sample.Available)
                return value;

            return value + " (" + PowerSample.LabelFor(sample.Boundary) + ")";
        }

        private static string Describe(PowerSample sample)
        {
            if (sample == null)
                return "n/a";

            if (!sample.Available)
                return "n/a (" + sample.UnavailableReason + ")";

            return (sample.Kind == MeasurementKind.Estimated ? "≈ " : "")
                + sample.Watts.ToString("0.00", CultureInfo.InvariantCulture)
                + " [available=true; window=" + (sample.Window > TimeSpan.Zero
                    ? sample.Window.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s" : "unknown")
                + "; source=" + sample.Source + "]";
        }
    }
}
