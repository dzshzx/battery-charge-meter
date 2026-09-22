using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;

namespace BatteryChargeMeter
{
    /// <summary>One tick's worth of power figures, each carrying its boundary.</summary>
    internal sealed class PowerSnapshot
    {
        public DateTimeOffset Timestamp;
        public double ElapsedSeconds;
        public BatteryReading Battery;
        public PowerSample BatteryTerminal;
        public PowerSample CpuPackage;
        public PowerSample Platform;

        /// <summary>
        /// The whole-machine figure. Its boundary depends on the supply state:
        /// measured system load when running on battery, estimated system input
        /// when running on external power. Read
        /// <see cref="PowerSample.Boundary"/> rather than assuming either.
        /// </summary>
        public PowerSample WholeSystem;
    }

    /// <summary>
    /// Owns the optional power sources and derives the estimated system input
    /// figure from them.
    ///
    /// The battery terminal figure is not handled here; it comes from
    /// <see cref="BatterySensor"/> and carries its own availability state.
    /// Everything this class adds is capability-discovered at runtime and
    /// degrades to an explicit unsupported reason rather than to a substituted
    /// number.
    /// </summary>
    internal sealed class PowerSources : IDisposable
    {
        // A wrong energy-unit assumption would show up as a large multiplicative
        // error, not a small one, so the tolerance only has to be tight enough
        // to catch that while staying loose enough not to flag the ordinary
        // skew between two sensors sampling slightly different windows.
        private const double AbsoluteToleranceWatts = 1.5;
        private const double RelativeTolerance = 0.08;

        private readonly EmiSensor emi = new EmiSensor();
        private readonly PawnIoSensor pawnIo = new PawnIoSensor();
        private readonly Stopwatch clock = Stopwatch.StartNew();

        public PowerSnapshot Capture()
        {
            BatteryReading battery;
            try
            {
                battery = BatterySensor.Read();
            }
            catch (Exception error)
            {
                battery = new BatteryReading
                {
                    StatusUnavailableReason = error.Message,
                    RateUnavailableReason = error.Message
                };
            }
            return Read(battery);
        }

        public string CpuPackageStatus
        {
            get { return emi.Available ? "可用" : emi.UnavailableReason; }
        }

        public string PlatformStatus
        {
            get { return pawnIo.Available ? "可用" : pawnIo.UnavailableReason; }
        }

        /// <summary>
        /// Every EMI channel found while discovering sources. A machine that
        /// exposes only RAPL channels cannot report input power directly no
        /// matter how the values are combined, and seeing the list is how a
        /// bug report shows that.
        /// </summary>
        public IList<string> EmiChannels
        {
            get { return emi.DiscoveredChannels; }
        }

        public IList<string> EmiDiscoveryErrors
        {
            get { return emi.DiscoveryErrors; }
        }

        public PowerSnapshot Read(BatteryReading battery)
        {
            PowerSnapshot snapshot = new PowerSnapshot();
            snapshot.Timestamp = DateTimeOffset.Now;
            snapshot.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            snapshot.Battery = battery;
            snapshot.BatteryTerminal = BatterySample(battery);
            snapshot.CpuPackage = emi.Read();

            PowerSample platform;
            double msrPackageWatts;
            bool platformRead = pawnIo.TryRead(out platform, out msrPackageWatts);

            if (platformRead)
                platform = ValidatePlatform(platform, snapshot.CpuPackage, msrPackageWatts);

            snapshot.Platform = platform;
            snapshot.WholeSystem = DeriveWholeSystem(platform, battery);
            return snapshot;
        }

        internal static PowerSample BatterySample(BatteryReading battery)
        {
            if (battery == null || !battery.StatusAvailable || !battery.RateAvailable)
                return PowerSample.Unsupported(PowerBoundary.BatteryTerminal, battery == null ? "电池状态不可用"
                    : (!battery.StatusAvailable ? battery.StatusUnavailableReason : battery.RateUnavailableReason));
            return PowerSample.FromValue(PowerBoundary.BatteryTerminal, MeasurementKind.Measured,
                battery.PowerWatts, "WMI BatteryStatus；固件采样窗口与内部时间戳未知", TimeSpan.Zero);
        }

        /// <summary>
        /// Confirms the RAPL energy unit on this machine by checking the MSR
        /// package figure against the EMI package figure, which is produced by a
        /// wholly separate interface. Agreement raises confidence and a direct
        /// disagreement withholds the value. Missing EMI is not a Psys capability
        /// failure, so the platform reading remains available but is labelled as
        /// not cross-validated.
        /// </summary>
        internal static PowerSample ValidatePlatform(
            PowerSample platform, PowerSample emiPackage, double msrPackageWatts)
        {
            if (emiPackage == null || !emiPackage.Available)
            {
                platform.Source = String.IsNullOrEmpty(platform.Source)
                    ? "未交叉验证"
                    : platform.Source + "（EMI 未交叉验证）";
                return platform;
            }

            double difference = Math.Abs(msrPackageWatts - emiPackage.Watts);
            double tolerance = Math.Max(
                AbsoluteToleranceWatts, Math.Abs(emiPackage.Watts) * RelativeTolerance);

            if (difference > tolerance)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.Platform,
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "单位换算校验失败（MSR {0:0.00} W vs EMI {1:0.00} W）",
                        msrPackageWatts,
                        emiPackage.Watts));
            }

            return platform;
        }

        /// <summary>
        /// Answers the whole-machine power question, which is a different
        /// question in each supply state and therefore a different boundary.
        ///
        /// Running on battery, every watt the machine uses leaves the battery
        /// terminals, so total consumption is measured outright and no estimate
        /// is involved. Input power in that state is zero by definition and
        /// reporting the platform figure as input would be plainly wrong.
        ///
        /// Running on external power, input is estimated as platform power plus
        /// the signed battery power. The sign matters: an adapter at its limit
        /// lets the battery supplement the load, and clamping that term to zero
        /// would overstate what the port actually supplies. The sum still omits
        /// charging-path and conversion losses, so it reads low and stays an
        /// estimate.
        ///
        /// Internal rather than private so the self test can drive it across
        /// supply states without real hardware.
        /// </summary>
        internal static PowerSample DeriveWholeSystem(PowerSample platform, BatteryReading battery)
        {
            if (battery == null)
                return PowerSample.Unsupported(PowerBoundary.SystemLoad, "电池状态不可用");

            if (!battery.StatusAvailable)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.SystemLoad,
                    String.IsNullOrEmpty(battery.StatusUnavailableReason)
                        ? "电池状态不可用"
                        : battery.StatusUnavailableReason);
            }

            if (!battery.PowerOnline)
            {
                if (!battery.RateAvailable)
                {
                    return PowerSample.Unsupported(
                        PowerBoundary.SystemLoad, "电池放电速率不可用");
                }

                return PowerSample.FromValue(
                    PowerBoundary.SystemLoad,
                    MeasurementKind.Measured,
                    -battery.PowerWatts,
                    "电池端放电功率",
                    TimeSpan.Zero);
            }

            if (platform == null || !platform.Available)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput,
                    platform == null ? "平台功率不可用" : platform.UnavailableReason);
            }

            if (!battery.RateAvailable)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput, "电池速率不可用");
            }

            double estimatedWatts = platform.Watts + battery.PowerWatts;
            if (Double.IsNaN(estimatedWatts) || Double.IsInfinity(estimatedWatts))
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput, "估算结果不是有限数值");
            }
            if (estimatedWatts < 0.0)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput,
                    "估算结果为负，数据边界或采样窗口不一致");
            }

            return PowerSample.FromValue(
                PowerBoundary.EstimatedSystemInput,
                MeasurementKind.Estimated,
                estimatedWatts,
                "平台功率 + 电池端净功率",
                platform.Window);
        }

        public void Dispose()
        {
            pawnIo.Dispose();
        }
    }
}
