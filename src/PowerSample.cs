using System;

namespace BatteryChargeMeter
{
    /// <summary>
    /// The electrical boundary a power figure is measured across. Every value
    /// shown to the user must carry its boundary, because sensors on different
    /// boundaries are not interchangeable. See CONTEXT.md for the definitions.
    /// </summary>
    internal enum PowerBoundary
    {
        BatteryTerminal,
        CpuPackage,
        Platform,
        SystemLoad,
        EstimatedSystemInput,
    }

    /// <summary>
    /// Whether a figure came off a hardware counter or was derived from others.
    /// Never promote an estimate to <see cref="Measured"/>.
    /// </summary>
    internal enum MeasurementKind
    {
        Measured,
        Estimated,
    }

    /// <summary>
    /// One power figure plus the provenance needed to label it honestly. A
    /// sample with <see cref="Available"/> false carries the reason instead of
    /// a value; callers must not substitute a neighbouring boundary's number.
    /// </summary>
    internal sealed class PowerSample
    {
        public bool Available;
        public string UnavailableReason;
        public double Watts;
        public PowerBoundary Boundary;
        public MeasurementKind Kind;
        public string Source;
        public TimeSpan Window;

        public static PowerSample FromValue(
            PowerBoundary boundary,
            MeasurementKind kind,
            double watts,
            string source,
            TimeSpan window)
        {
            PowerSample sample = new PowerSample();
            sample.Available = true;
            sample.Boundary = boundary;
            sample.Kind = kind;
            sample.Watts = watts;
            sample.Source = source;
            sample.Window = window;
            return sample;
        }

        public static PowerSample Unsupported(PowerBoundary boundary, string reason)
        {
            PowerSample sample = new PowerSample();
            sample.Available = false;
            sample.Boundary = boundary;
            sample.UnavailableReason = reason;
            return sample;
        }

        /// <summary>Chinese UI label for the boundary, per CONTEXT.md.</summary>
        public static string LabelFor(PowerBoundary boundary)
        {
            switch (boundary)
            {
                case PowerBoundary.BatteryTerminal:
                    return "电池端净功率";
                case PowerBoundary.CpuPackage:
                    return "CPU 包功率";
                case PowerBoundary.Platform:
                    return "平台功率";
                case PowerBoundary.SystemLoad:
                    return "系统负载功率";
                case PowerBoundary.EstimatedSystemInput:
                    return "估算整机输入功率";
                default:
                    return "未知口径";
            }
        }
    }
}
