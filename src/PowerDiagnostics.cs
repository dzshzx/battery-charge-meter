using System;
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
        public static string Run(int samples)
        {
            StringBuilder output = new StringBuilder();

            using (PowerSources sources = new PowerSources())
            {
                output.AppendLine("Power source discovery");
                output.AppendLine("  CPU package (EMI): " + sources.CpuPackageStatus);
                output.AppendLine("  Platform (PawnIO): " + sources.PlatformStatus);
                output.AppendLine();
                output.AppendLine(String.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-7} {1,-14} {2,-14} {3,-14} {4}",
                    "t",
                    "Battery_W",
                    "CpuPkg_W",
                    "Platform_W",
                    "EstInput_W"));

                for (int i = 0; i <= samples; i++)
                {
                    Thread.Sleep(1000);

                    BatteryReading battery;
                    try
                    {
                        battery = BatterySensor.Read();
                    }
                    catch (Exception error)
                    {
                        output.AppendLine("battery read failed: " + error.Message);
                        continue;
                    }

                    PowerSnapshot snapshot = sources.Read(battery);

                    // The first tick only establishes the counter baselines.
                    if (i == 0)
                        continue;

                    output.AppendLine(String.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-7} {1,-14} {2,-14} {3,-14} {4}",
                        i.ToString(CultureInfo.InvariantCulture) + "s",
                        battery.RateAvailable
                            ? battery.PowerWatts.ToString("0.00", CultureInfo.InvariantCulture)
                            : "n/a",
                        Describe(snapshot.CpuPackage),
                        Describe(snapshot.Platform),
                        Describe(snapshot.EstimatedSystemInput)));
                }
            }

            return output.ToString();
        }

        private static string Describe(PowerSample sample)
        {
            if (sample == null)
                return "n/a";

            if (!sample.Available)
                return "n/a (" + sample.UnavailableReason + ")";

            return sample.Watts.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
