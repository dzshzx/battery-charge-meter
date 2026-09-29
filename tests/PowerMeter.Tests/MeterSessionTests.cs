using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using static BatteryChargeMeter.Tests.Fixtures;

namespace BatteryChargeMeter.Tests
{
    public sealed class MeterSessionTests
    {
        [Fact]
        public void Session()
        {
                        PowerSample cpuPackage = PowerSample.FromValue(PowerBoundary.CpuPackage,
                MeasurementKind.Measured, 14.11, "fixture", TimeSpan.FromSeconds(1));
            PowerSample platform = PowerSample.FromValue(PowerBoundary.Platform,
                MeasurementKind.Measured, 27, "fixture", TimeSpan.FromSeconds(1));
            PowerSample noPlatform = PowerSample.Unsupported(PowerBoundary.Platform, "需要管理员权限");
            DateTimeOffset time = new DateTimeOffset(2026, 9, 27, 10, 20, 30, TimeSpan.FromHours(8));
            BatteryReading charging = Battery(true, true, true, 39.08);

            MeterSession session = new MeterSession(DisplayMode.WholeSystem);
            MeterView view = session.Observe(PowerSnapshot.Compose(time, 1, charging, cpuPackage, platform));
            Check("charging headline, row and tray show the estimated total, not battery or extra CPU",
                view.Headline.Text == "≈ 66.08 W" && view.Headline.Tone == ReadoutTone.NumericMuted
                && view.Rows[MeterView.WholeSystemRow].Text == "≈ 66.08 W"
                && view.Rows[MeterView.WholeSystemRow].Tone == ReadoutTone.Muted
                && view.Rows[MeterView.CpuPackageRow].Text == "14.11 W"
                && view.Rows[MeterView.CpuPackageRow].Tone == ReadoutTone.Ink
                && view.TrayGlyph == "66" && view.Accent == BatteryAccentKind.Charging
                && view.TrayTooltip == Strings.Get("估算整机输入功率") + ": ≈ 66.08 W"
                && view.Supply.Text == Strings.Get("已接电源 · 充电中") && view.Updated == "10:20:30");

            view = session.SetMode(DisplayMode.Battery);
            Check("battery mode changes headline and tray together and restarts statistics",
                session.Mode == DisplayMode.Battery && view.Headline.Text == "39.08 W"
                && view.Headline.Tone == ReadoutTone.Accent && view.TrayGlyph == "39"
                && view.TrayTooltip == Strings.Get("电池端净功率") + ": 39.08 W"
                && view.AverageValue == "-- W" && view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 0.0));
            view = session.Observe(PowerSnapshot.Compose(time, 2, charging, cpuPackage, platform));
            Check("mode change seeds statistics with the current reading",
                view.AverageValue == "39.08 W" && view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 1.0));
            session.Render();
            view = session.Render();
            Check("re-rendering does not record another reading",
                view.AverageCaption == Strings.Format("均值 · {0:0.#}/30s", 1.0));

            session = new MeterSession(DisplayMode.WholeSystem);
            view = session.Observe(PowerSnapshot.Compose(time, 1, charging, cpuPackage, noPlatform));
            Check("missing platform clears the total and tray and explains it once",
                view.Headline.Text == "N/A" && view.Rows[MeterView.WholeSystemRow].Text == "N/A"
                && view.TrayGlyph == "--" && view.Diagnostics.Count == 1
                && view.Diagnostics[0] == Strings.Get("平台功率") + ": " + Strings.Diagnostic("需要管理员权限"));

            BatteryReading unplugged = Battery(false, false, true, -17.5);
            view = session.Observe(PowerSnapshot.Compose(time, 2, unplugged, cpuPackage, noPlatform));
            Check("unplugged whole display works without platform or elevation",
                view.Headline.Text == "17.50 W" && view.TrayGlyph == "18"
                && view.TrayTooltip == Strings.Get("系统负载功率") + ": 17.50 W"
                && view.WholeCaption == Strings.Get("系统负载功率") && view.Diagnostics.Count == 1);

            BatteryReading supplemented = Battery(true, false, true, -5);
            session = new MeterSession(DisplayMode.WholeSystem);
            PowerSample platform24 = PowerSample.FromValue(PowerBoundary.Platform,
                MeasurementKind.Measured, 24, "fixture", TimeSpan.FromSeconds(1));
            session.Observe(PowerSnapshot.Compose(time, 0, supplemented, cpuPackage, platform24));
            view = session.Observe(PowerSnapshot.Compose(time, 1, supplemented, cpuPackage, platform24));
            Check("battery supplementation reduces the estimate and statistics stay estimated",
                view.Headline.Text == "≈ 19.00 W" && view.AverageValue == "≈ 19.00 W" && view.PeakValue == "≈ 19.00 W"
                && view.Supply.Text == Strings.Get("已接电源 · 电池补充") && view.Accent == BatteryAccentKind.Discharging);

            view = session.Fail("sensor failed");
            Check("sensor failure clears statistics and never shows a stale figure",
                view.Headline.Text == "N/A" && view.Accent == BatteryAccentKind.Error
                && view.Supply.Text == Strings.Get("传感器异常") && view.TrayGlyph == "--"
                && view.TrayTooltip == Strings.Get("整机功率") + ": N/A"
                && view.Diagnostics.Count == 1 && view.Diagnostics[0] == "sensor failed"
                && view.AverageValue == "-- W" && view.PeakValue == "-- W" && view.Updated == "10:20:30"
                && view.Rows[MeterView.PlatformRow].Text == "N/A" && view.BatteryLevel == -1);
            view = session.Render();
            Check("sensor failure survives re-rendering until the next reading",
                view.Supply.Text == Strings.Get("传感器异常"));
            view = session.Observe(PowerSnapshot.Compose(time, 10, supplemented, cpuPackage, platform24));
            Check("readings after a failure start new statistics",
                view.Headline.Text == "≈ 19.00 W" && view.AverageValue == "-- W");

            view = new MeterSession(DisplayMode.Battery).Render();
            Check("before the first reading the tray keeps the application icon",
                view.TrayGlyph == null && view.Headline.Text == "--.-- W" && view.Diagnostics.Count == 0);
        }

        [Fact]
        public void Display()
        {
                        PowerSample platform = PowerSample.FromValue(PowerBoundary.Platform, MeasurementKind.Measured, 24, "test", TimeSpan.FromSeconds(1));
            foreach (BatteryReading battery in new BatteryReading[] { Battery(true, true, true, 13), Battery(true, false, true, -5), Battery(false, false, true, -26) })
            {
                PowerSnapshot snapshot = Snapshot(battery, platform, 0);
                MeterView whole = new MeterSession(DisplayMode.WholeSystem).Observe(snapshot);
                MeterView terminal = new MeterSession(DisplayMode.Battery).Observe(snapshot);
                Check("selected boundary is shared without substitution: " + battery.SupplyState,
                    whole.Headline.Text == whole.Rows[MeterView.WholeSystemRow].Text
                    && whole.Title.Text == Strings.Get(PowerSample.LabelFor(snapshot.WholeSystem.Boundary))
                    && terminal.Headline.Text == terminal.Rows[MeterView.BatteryRow].Text
                    && terminal.Title.Text == Strings.Get(PowerSample.LabelFor(PowerBoundary.BatteryTerminal)));
            }
            BatteryReading charging = Battery(true, true, true, 13);
            PowerSnapshot missing = Snapshot(charging, PowerSample.Unsupported(PowerBoundary.Platform, "missing"), 0);
            Check("whole mode does not fall back to available battery",
                new MeterSession(DisplayMode.WholeSystem).Observe(missing).Headline.Text == "N/A"
                && new MeterSession(DisplayMode.Battery).Observe(missing).Headline.Text == "13.00 W");
            Check("preferences default safely for missing malformed or inaccessible storage",
                DisplayPreference.Load(delegate { return null; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "invalid"; }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { throw new InvalidOperationException(); }) == DisplayMode.WholeSystem
                && DisplayPreference.Load(delegate { return "Battery"; }) == DisplayMode.Battery);
        }

        [Fact]
        public void History()
        {
                        PowerHistory history = new PowerHistory();
            BatterySupplyState supply = BatterySupplyState.BatteryDischarging;
            history.Add(0, DisplayMode.Battery, supply, Value(-10));
            history.Add(1, DisplayMode.Battery, supply, Value(-20));
            history.Add(4, DisplayMode.Battery, supply, Value(-20));
            double coverage;
            double? average = history.Average(out coverage);
            Check("irregular observations use elapsed weighting and partial window", average.HasValue && Near(average.Value, -17.5) && Near(coverage, 4) && Near(history.Duration(60), 4));
            Check("peak retains sign at greatest magnitude", Near(history.Peak().Value, -20));
            history.Add(5, DisplayMode.Battery, supply, PowerSample.Unsupported(PowerBoundary.BatteryTerminal, "gap"));
            Check("missing sample leaves a gap and immediately unavailable statistics", !history.Peak().HasValue && !history.Average(out coverage).HasValue && !history.Points[3].Watts.HasValue);
            history.Add(6, DisplayMode.Battery, supply, Value(-30));
            average = history.Average(out coverage);
            Check("missing intervals are excluded without zero fill", Near(coverage, 4) && Near(average.Value, -17.5));
            history.Add(20, DisplayMode.Battery, supply, Value(-30));
            Check("resume gap clears baseline", history.Points.Count == 1 && !history.Average(out coverage).HasValue);
            history.Add(21, DisplayMode.WholeSystem, supply, Value(30));
            Check("mode change clears history", history.Points.Count == 1);
            history.Add(22, DisplayMode.WholeSystem, BatterySupplyState.ExternalPowerIdle, Value(30));
            Check("supply state change clears history", history.Points.Count == 1);
            history.Clear();
            for (int second = 0; second <= 70; second++) history.Add(second, DisplayMode.Battery, supply, Value(second == 20 ? -100 : -10));
            average = history.Average(out coverage);
            Check("30 second mean differs from 60 second peak; repeated readings stay fresh", Near(average.Value, -10) && Near(coverage, 30) && Near(history.Peak().Value, -100) && Near(history.Duration(60), 60));
            for (int second = 71; second <= 81; second++) history.Add(second, DisplayMode.Battery, supply, Value(-10));
            Check("old peak expires at real 60 second boundary", Near(history.Peak().Value, -10));
            history.Clear();
            for (int second = 0; second <= 32; second += 4)
                history.Add(second, DisplayMode.Battery, supply, Value(second == 0 ? 40 : 10));
            average = history.Average(out coverage);
            Check("weighted mean clips the first interval at exact 30 second cutoff", Near(average.Value, 12) && Near(coverage, 30));
            Check("nonfinite sensor values cannot reach readings or tray", !Value(Double.NaN).Available && !Value(Double.PositiveInfinity).Available);
        }

        [Fact]
        public void Language()
        {
            Check("language preference overrides the system, with safe fallback",
                Strings.Resolve("en", "zh-CN") == "en"
                && Strings.Resolve("zh-CN", "en-US") == "zh-CN"
                && Strings.Resolve("system", "zh-TW") == "zh-CN"
                && Strings.Resolve("invalid", "fr-FR") == "en");
            string saved = Strings.Preference;
            try
            {
                const string cached = "需要以管理员身份运行才能读取平台功率";
                Strings.Select("en", false);
                Check("English branding and cached diagnostic presentation",
                    Strings.AppName == "Power Meter" && Strings.Diagnostic(cached) == "Run as administrator to read platform power"
                    && Strings.Diagnostic("EMI 初始化失败: C:\\测试") == "EMI initialization failed: C:\\测试");
                Strings.Select("zh-CN", false);
                Check("Chinese branding and diagnostics switch back without driver restart",
                    Strings.AppName == "功率计" && Strings.Diagnostic(cached) == cached);
            }
            finally { Strings.Select(saved == "en" || saved == "zh-CN" ? saved : "system", false); }
        }
    }
}
