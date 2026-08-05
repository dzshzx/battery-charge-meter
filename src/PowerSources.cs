using System;
using System.Globalization;

namespace BatteryChargeMeter
{
    /// <summary>One tick's worth of power figures, each carrying its boundary.</summary>
    internal sealed class PowerSnapshot
    {
        public PowerSample CpuPackage;
        public PowerSample Platform;
        public PowerSample EstimatedSystemInput;
    }

    /// <summary>
    /// Owns the optional power sources and derives the estimated system input
    /// figure from them.
    ///
    /// The battery terminal figure is not handled here; it comes from
    /// <see cref="BatterySensor"/> and is always available. Everything this
    /// class adds is capability-discovered at runtime and degrades to an
    /// explicit unsupported reason rather than to a substituted number.
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

        public string CpuPackageStatus
        {
            get { return emi.Available ? "可用" : emi.UnavailableReason; }
        }

        public string PlatformStatus
        {
            get { return pawnIo.Available ? "可用" : pawnIo.UnavailableReason; }
        }

        public PowerSnapshot Read(BatteryReading battery)
        {
            PowerSnapshot snapshot = new PowerSnapshot();
            snapshot.CpuPackage = emi.Read();

            PowerSample platform;
            double msrPackageWatts;
            bool platformRead = pawnIo.TryRead(out platform, out msrPackageWatts);

            if (platformRead)
                platform = Validate(platform, snapshot.CpuPackage, msrPackageWatts);

            snapshot.Platform = platform;
            snapshot.EstimatedSystemInput = DeriveInput(platform, battery);
            return snapshot;
        }

        /// <summary>
        /// Confirms the RAPL energy unit on this machine by checking the MSR
        /// package figure against the EMI package figure, which is produced by a
        /// wholly separate interface. Only the platform figure is derived from
        /// an assumed unit, so if the package figures agree the platform figure
        /// is trustworthy; if they do not, the scaling is wrong and no platform
        /// number should be shown.
        /// </summary>
        private static PowerSample Validate(
            PowerSample platform, PowerSample emiPackage, double msrPackageWatts)
        {
            if (emiPackage == null || !emiPackage.Available)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.Platform, "缺少 EMI 对照量，无法校验单位换算");
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
        /// Estimated system input = platform power + battery charge power.
        ///
        /// Psys excludes battery charging, so the two terms do not overlap. The
        /// sum still omits the charging-path and conversion losses that no
        /// counter on this machine measures, so the result reads low and is
        /// always reported as an estimate.
        /// </summary>
        private static PowerSample DeriveInput(PowerSample platform, BatteryReading battery)
        {
            if (platform == null || !platform.Available)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput,
                    platform == null ? "平台功率不可用" : platform.UnavailableReason);
            }

            if (battery == null || !battery.RateAvailable)
            {
                return PowerSample.Unsupported(
                    PowerBoundary.EstimatedSystemInput, "电池速率不可用");
            }

            double chargeWatts = Math.Max(battery.PowerWatts, 0.0);

            return PowerSample.FromValue(
                PowerBoundary.EstimatedSystemInput,
                MeasurementKind.Estimated,
                platform.Watts + chargeWatts,
                "平台功率 + 电池端充电功率",
                platform.Window);
        }

        public void Dispose()
        {
            emi.Dispose();
            pawnIo.Dispose();
        }
    }
}
