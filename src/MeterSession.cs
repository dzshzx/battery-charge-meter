using System;
using System.Collections.Generic;
using System.Globalization;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Colour intent of one piece of text. The window maps it to a colour;
    /// <see cref="Accent"/> takes the view's supply-state accent.
    /// </summary>
    internal enum ReadoutTone
    {
        Ink,
        Muted,
        NumericMuted,
        Accent
    }

    internal sealed class ReadoutText
    {
        public string Text;
        public ReadoutTone Tone;
        public string Tooltip;
    }

    /// <summary>
    /// Everything the window and tray show at one moment, already localized.
    /// It carries no colours or controls, so the display rules can be checked
    /// without a window.
    /// </summary>
    internal sealed class MeterView
    {
        internal const int BatteryRow = 0;
        internal const int CpuPackageRow = 1;
        internal const int PlatformRow = 2;
        internal const int WholeSystemRow = 3;

        public BatteryAccentKind Accent;
        public ReadoutText Title;
        public ReadoutText Headline;
        public ReadoutText Supply;
        public string Detail;
        public string Percentage;
        public int BatteryLevel;
        public ReadoutTone BatteryTone;
        public string AverageCaption;
        public string AverageValue;
        public string AverageTooltip;
        public string PeakCaption;
        public string PeakValue;
        public string PeakTooltip;
        // Fixed order: battery terminal, CPU package, platform, whole system.
        public ReadoutText[] Rows;
        public string WholeCaption;
        public string WholeCaptionTooltip;
        // Measurement reasons only; elevation and startup notices are the window's.
        public IList<string> Diagnostics;
        // Null until the first reading: the tray keeps the application icon.
        public string TrayGlyph;
        public string TrayTooltip;
        public string Updated;
    }

    /// <summary>
    /// The meter's display state: the selected mode, the latest snapshot, the
    /// statistics history and any sensor failure. Every operation returns the
    /// complete view, so the headline, supporting rows, statistics and tray are
    /// always derived from the same reading.
    /// </summary>
    internal sealed class MeterSession
    {
        private const string WholeSystemFormula =
            "外电：平台功率 + 带符号的电池端净功率，未含转换损耗。\n电池供电：电池端放电功率。CPU 包已包含在平台功率内。";

        private readonly PowerHistory history = new PowerHistory();
        private DisplayMode mode;
        private PowerSnapshot latest;
        private string failure;
        private DateTimeOffset? timestamp;
        private PowerBoundary wholeBoundary = PowerBoundary.EstimatedSystemInput;

        internal MeterSession(DisplayMode mode)
        {
            this.mode = mode;
        }

        internal DisplayMode Mode
        {
            get { return mode; }
        }

        /// <summary>Records one reading and appends it to the statistics.</summary>
        internal MeterView Observe(PowerSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            latest = snapshot;
            failure = null;
            timestamp = snapshot.Timestamp;
            if (snapshot.WholeSystem != null)
                wholeBoundary = snapshot.WholeSystem.Boundary;
            Append();
            return Render();
        }

        /// <summary>
        /// Drops the latest reading and the statistics: nothing is known until
        /// the next successful capture.
        /// </summary>
        internal MeterView Fail(string reason)
        {
            latest = null;
            failure = reason ?? "";
            history.Clear();
            return Render();
        }

        /// <summary>
        /// A mode change starts new statistics, seeded with the current reading.
        /// </summary>
        internal MeterView SetMode(DisplayMode next)
        {
            if (mode != next)
            {
                mode = next;
                history.Clear();
            }
            if (latest != null && history.Points.Count == 0)
                Append();
            return Render();
        }

        /// <summary>Rebuilds the current view without recording anything.</summary>
        internal MeterView Render()
        {
            if (latest != null)
                return ReadingView(latest);
            return failure != null ? FailureView() : InitialView();
        }

        private void Append()
        {
            BatteryReading reading = latest.Battery ?? new BatteryReading();
            history.Add(latest.ElapsedSeconds, mode, reading.SupplyState, Selected(latest));
        }

        private PowerSample Selected(PowerSnapshot snapshot)
        {
            return mode == DisplayMode.Battery ? snapshot.BatteryTerminal : snapshot.WholeSystem;
        }

        private MeterView ReadingView(PowerSnapshot snapshot)
        {
            BatteryReading reading = snapshot.Battery ?? new BatteryReading();
            BatterySupplyProfile profile = reading.SupplyProfile;
            PowerSample selected = Selected(snapshot);
            string headline = Watts(selected);

            MeterView view = new MeterView();
            view.Accent = profile.Accent;
            view.Supply = Text(Strings.Get(SupplyCaptionKey(reading.SupplyState)), ReadoutTone.Accent, null);
            view.Headline = Text(headline,
                selected != null && selected.Kind == MeasurementKind.Estimated ? ReadoutTone.NumericMuted : ReadoutTone.Accent,
                selected == null ? null : Strings.Diagnostic(selected.Available ? selected.Source : selected.UnavailableReason));
            view.Detail = Strings.Format("电池端 {0} · ≈ {1}",
                reading.VoltageAvailable
                    ? reading.VoltageVolts.ToString("0.00", CultureInfo.InvariantCulture) + " V"
                    : "--.-- V",
                reading.CurrentAvailable
                    ? reading.CurrentAmps.ToString("0.00", CultureInfo.InvariantCulture) + " A"
                    : "--.-- A");
            view.Percentage = reading.Percentage >= 0
                ? reading.Percentage.ToString(CultureInfo.InvariantCulture) + "%"
                : "--%";
            view.BatteryLevel = reading.Percentage;
            view.BatteryTone = profile.Accent == BatteryAccentKind.Idle ? ReadoutTone.Muted : ReadoutTone.Accent;
            view.Rows = new ReadoutText[]
            {
                Row(snapshot.BatteryTerminal),
                Row(snapshot.CpuPackage),
                Row(snapshot.Platform),
                Row(snapshot.WholeSystem)
            };
            view.Title = Text(Strings.Get(PowerSample.LabelFor(selected == null ? wholeBoundary : selected.Boundary)),
                ReadoutTone.Muted, TitleTooltip(view.Rows));
            WholeSystemCaption(view);
            view.Diagnostics = Diagnostics(snapshot, selected);
            view.TrayGlyph = selected != null && selected.Available ? TrayGlyph(selected.Watts) : "--";
            view.TrayTooltip = Strings.Get(PowerSample.LabelFor(selected == null ? wholeBoundary : selected.Boundary))
                + ": " + headline;
            view.Updated = UpdatedText();
            Statistics(view, selected != null && selected.Kind == MeasurementKind.Estimated);
            return view;
        }

        private MeterView FailureView()
        {
            string reason = Strings.Diagnostic(failure);
            MeterView view = new MeterView();
            view.Accent = BatteryAccentKind.Error;
            view.Supply = Text(Strings.Get("传感器异常"), ReadoutTone.Accent, null);
            view.Headline = Text("N/A", ReadoutTone.Muted, reason);
            view.Detail = Strings.Format("电池端 {0} · ≈ {1}", "-- V", "-- A");
            view.Percentage = "--%";
            view.BatteryLevel = -1;
            view.BatteryTone = ReadoutTone.Accent;
            view.Rows = new ReadoutText[4];
            for (int index = 0; index < view.Rows.Length; index++)
                view.Rows[index] = Text("N/A", ReadoutTone.Muted, reason);
            view.Title = Text(Strings.Get(PowerSample.LabelFor(
                mode == DisplayMode.Battery ? PowerBoundary.BatteryTerminal : wholeBoundary)),
                ReadoutTone.Muted, TitleTooltip(view.Rows));
            WholeSystemCaption(view);
            view.Diagnostics = new List<string> { reason };
            view.TrayGlyph = "--";
            view.TrayTooltip = Strings.Get(mode == DisplayMode.Battery ? "电池端净功率" : "整机功率") + ": N/A";
            view.Updated = UpdatedText();
            Statistics(view, false);
            return view;
        }

        // Shown only until the first capture completes.
        private MeterView InitialView()
        {
            MeterView view = new MeterView();
            view.Accent = BatteryAccentKind.Charging;
            view.Supply = Text(Strings.Get("正在读取"), ReadoutTone.Ink, null);
            view.Headline = Text("--.-- W", ReadoutTone.Accent, null);
            view.Detail = "";
            view.Percentage = "--%";
            view.BatteryLevel = -1;
            view.BatteryTone = ReadoutTone.Accent;
            view.Rows = new ReadoutText[4];
            for (int index = 0; index < view.Rows.Length; index++)
                view.Rows[index] = Text("--", ReadoutTone.Ink, null);
            view.Title = Text(Strings.Get(PowerSample.LabelFor(
                mode == DisplayMode.Battery ? PowerBoundary.BatteryTerminal : wholeBoundary)),
                ReadoutTone.Muted, TitleTooltip(view.Rows));
            WholeSystemCaption(view);
            view.Diagnostics = new List<string>();
            view.TrayTooltip = Strings.AppName + ": " + Strings.Get("正在读取");
            view.Updated = UpdatedText();
            Statistics(view, false);
            return view;
        }

        // The title explains the headline the same way its supporting row would.
        private string TitleTooltip(ReadoutText[] rows)
        {
            return mode == DisplayMode.WholeSystem
                ? Strings.Get(WholeSystemFormula)
                : rows[MeterView.BatteryRow].Tooltip;
        }

        // The last row's boundary changes with the supply state, so its caption
        // follows the most recent whole-system reading.
        private void WholeSystemCaption(MeterView view)
        {
            view.WholeCaption = Strings.Get(PowerSample.LabelFor(wholeBoundary));
            view.WholeCaptionTooltip = Strings.Get(WholeSystemFormula);
        }

        private string UpdatedText()
        {
            return timestamp.HasValue ? timestamp.Value.ToString("HH:mm:ss") : "--:--:--";
        }

        private static IList<string> Diagnostics(PowerSnapshot snapshot, PowerSample selected)
        {
            List<string> lines = new List<string>();
            foreach (PowerSample sample in new PowerSample[] { snapshot.BatteryTerminal, snapshot.CpuPackage, snapshot.Platform })
            {
                if (sample != null && !sample.Available)
                    lines.Add(Strings.Get(PowerSample.LabelFor(sample.Boundary)) + ": " + Strings.Diagnostic(sample.UnavailableReason));
            }
            // A whole-system figure that is missing only because platform power
            // is missing has already been explained by the platform line.
            PowerSample platform = snapshot.Platform;
            bool platformAvailable = platform != null && platform.Available;
            string platformReason = platform == null ? null : platform.UnavailableReason;
            if (selected != null && !selected.Available && selected.Boundary != PowerBoundary.BatteryTerminal
                && (platformAvailable || selected.UnavailableReason != platformReason))
                lines.Add(Strings.Get(PowerSample.LabelFor(selected.Boundary)) + ": " + Strings.Diagnostic(selected.UnavailableReason));
            return lines;
        }

        private void Statistics(MeterView view, bool estimated)
        {
            double coverage;
            double? average = history.Average(out coverage);
            double? peak = history.Peak();
            string prefix = estimated ? "≈ " : "";
            double duration = history.Duration(60);
            view.AverageTooltip = Strings.Format("30 秒均值 · 有效 {0:0.#}s", coverage);
            view.PeakTooltip = Strings.Format("60 秒峰值 · 最近 {0}s", duration.ToString("0.#", CultureInfo.InvariantCulture));
            view.AverageCaption = coverage >= 30 - 0.000001 ? Strings.Get("30 秒均值") : Strings.Format("均值 · {0:0.#}/30s", coverage);
            view.PeakCaption = duration >= 60 - 0.000001 ? Strings.Get("60 秒峰值") : Strings.Format("峰值 · {0:0.#}/60s", duration);
            view.AverageValue = (average.HasValue ? prefix + average.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--") + " W";
            view.PeakValue = (peak.HasValue ? prefix + peak.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--") + " W";
        }

        /// <summary>
        /// An unavailable source shows N/A and carries its reason in the
        /// tooltip. It is never filled in from a neighbouring boundary, because
        /// those measure different things.
        /// </summary>
        private static ReadoutText Row(PowerSample sample)
        {
            if (sample == null || !sample.Available)
                return Text("N/A", ReadoutTone.Muted,
                    sample == null ? Strings.Get("无数据") : Strings.Diagnostic(sample.UnavailableReason));
            return Text(Watts(sample),
                sample.Kind == MeasurementKind.Estimated ? ReadoutTone.Muted : ReadoutTone.Ink,
                Strings.Diagnostic(sample.Source));
        }

        // Estimated figures always carry the ≈ prefix (CONTEXT.md).
        private static string Watts(PowerSample sample)
        {
            if (sample == null || !sample.Available)
                return "N/A";
            return (sample.Kind == MeasurementKind.Estimated ? "≈ " : "")
                + sample.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W";
        }

        private static string TrayGlyph(double powerWatts)
        {
            double watts = Math.Abs(powerWatts);
            if (watts >= 99.5)
                return "99+";
            return Math.Round(watts, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);
        }

        private static string SupplyCaptionKey(BatterySupplyState state)
        {
            switch (state)
            {
                case BatterySupplyState.ExternalPowerIdle: return "已接电源 · 未充电";
                case BatterySupplyState.ExternalPowerCharging: return "已接电源 · 充电中";
                case BatterySupplyState.ExternalPowerSupplemented: return "已接电源 · 电池补充";
                case BatterySupplyState.ExternalPowerDirectionUnknown: return "已接电源 · 状态未知";
                case BatterySupplyState.BatteryDischarging: return "电池供电 · 放电中";
                case BatterySupplyState.BatteryDirectionUnknown: return "电池供电 · 速率未知";
                case BatterySupplyState.Inconsistent: return "电池状态异常";
                default: return "电池不可用";
            }
        }

        private static ReadoutText Text(string text, ReadoutTone tone, string tooltip)
        {
            ReadoutText value = new ReadoutText();
            value.Text = text;
            value.Tone = tone;
            value.Tooltip = tooltip;
            return value;
        }
    }
}
