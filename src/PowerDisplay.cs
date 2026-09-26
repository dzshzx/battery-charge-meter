using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BatteryChargeMeter
{
    internal enum DisplayMode
    {
        WholeSystem,
        Battery
    }

    internal static class DisplayPreference
    {
        private const string Key = @"Software\BatteryChargeMeter";
        internal static DisplayMode Load()
        {
            return Load(delegate
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                    return key == null ? null : key.GetValue("DisplayMode");
            });
        }

        internal static DisplayMode Load(Func<object> read)
        {
            try
            {
                return String.Equals(read() as string, "Battery", StringComparison.Ordinal)
                    ? DisplayMode.Battery : DisplayMode.WholeSystem;
            }
            catch
            {
                return DisplayMode.WholeSystem;
            }
        }

        internal static void Save(DisplayMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
                    key.SetValue("DisplayMode", mode.ToString(), RegistryValueKind.String);
            }
            catch
            {
                // Read-only profiles retain the selection for this session.
            }
        }
    }

    internal sealed class TimedPower
    {
        internal double Seconds;
        internal double? Watts;
    }

    internal sealed class PowerHistory
    {
        internal const double MaximumGap = 5.0;
        private readonly List<TimedPower> points = new List<TimedPower>();
        private DisplayMode mode;
        private BatterySupplyState supply;
        internal IList<TimedPower> Points
        {
            get { return points.AsReadOnly(); }
        }

        internal void Clear()
        {
            points.Clear();
        }

        internal void Add(double seconds, DisplayMode nextMode, BatterySupplyState nextSupply, PowerSample sample)
        {
            if (points.Count > 0 && (mode != nextMode || supply != nextSupply
                || seconds <= points[points.Count - 1].Seconds
                || seconds - points[points.Count - 1].Seconds > MaximumGap))
                Clear();
            mode = nextMode;
            supply = nextSupply;
            points.Add(new TimedPower
            {
                Seconds = seconds,
                Watts = sample != null && sample.Available ? (double?)sample.Watts : null
            });
            // Retain one predecessor for clipping the first weighted interval.
            while (points.Count > 1 && points[1].Seconds <= seconds - 60)
                points.RemoveAt(0);
        }

        internal double Duration(double window)
        {
            return points.Count < 2 ? 0 : Math.Min(window, points[points.Count - 1].Seconds - points[0].Seconds);
        }

        internal double? Average(out double coverage)
        {
            coverage = 0;
            if (points.Count < 2 || !points[points.Count - 1].Watts.HasValue)
                return null;
            double cutoff = points[points.Count - 1].Seconds - 30;
            double energy = 0;
            // Left-held observations; an interval touching a missing reading is
            // excluded rather than claiming a value throughout an unknown gap.
            for (int i = 1; i < points.Count; i++)
            {
                TimedPower left = points[i - 1];
                TimedPower right = points[i];
                double duration = right.Seconds - Math.Max(left.Seconds, cutoff);
                if (duration <= 0 || !left.Watts.HasValue || !right.Watts.HasValue)
                    continue;
                energy += left.Watts.Value * duration;
                coverage += duration;
            }
            return coverage > 0 ? (double?)(energy / coverage) : null;
        }

        internal double? Peak()
        {
            if (points.Count == 0 || !points[points.Count - 1].Watts.HasValue)
                return null;
            double cutoff = points[points.Count - 1].Seconds - 60;
            double? peak = null;
            foreach (TimedPower point in points)
            {
                if (point.Seconds >= cutoff && point.Watts.HasValue
                    && (!peak.HasValue || Math.Abs(point.Watts.Value) > Math.Abs(peak.Value)))
                    peak = point.Watts;
            }
            return peak;
        }
    }
}
