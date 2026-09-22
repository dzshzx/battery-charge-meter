using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(
    ".NETFramework,Version=v4.7",
    FrameworkDisplayName = ".NET Framework 4.7")]

namespace BatteryChargeMeter
{
    internal sealed class SparklinePanel : Panel
    {
        private readonly List<TimedPower> values = new List<TimedPower>();
        private Color lineColor = Color.FromArgb(52, 211, 153);

        public SparklinePanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 23, 42);
        }

        public void SetHistory(IList<TimedPower> history, Color color)
        {
            values.Clear();
            foreach (TimedPower point in PowerDisplay.VisibleHistory(history))
                values.Add(point);
            lineColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float uiScale = Math.Max(1f, Width / 390f);

            using (Pen gridPen = new Pen(Color.FromArgb(34, 51, 72), uiScale))
            {
                for (int i = 1; i < 4; i++)
                {
                    float y = Height * i / 4f;
                    g.DrawLine(gridPen, 0, y, Width, y);
                }
            }

            if (values.Count < 2)
                return;

            double min = 0.0;
            double max = 0.0;
            foreach (TimedPower point in values)
            {
                if (!point.Watts.HasValue)
                    continue;
                double value = point.Watts.Value;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            if (Math.Abs(max - min) < 0.1)
                max = min + 1.0;

            double padding = Math.Max((max - min) * 0.12, 0.5);
            min -= padding;
            max += padding;

            float zeroY = ValueToY(0.0, min, max);
            using (Pen zeroPen = new Pen(Color.FromArgb(70, 100, 125), uiScale))
            {
                zeroPen.DashStyle = DashStyle.Dash;
                g.DrawLine(zeroPen, 0, zeroY, Width, zeroY);
            }

            using (Pen glowPen = new Pen(Color.FromArgb(45, lineColor), 6f * uiScale))
            using (Pen valuePen = new Pen(lineColor, 2.2f * uiScale))
            {
                glowPen.LineJoin = LineJoin.Round;
                valuePen.LineJoin = LineJoin.Round;
                double end = values[values.Count - 1].Seconds;
                double start = Math.Max(values[0].Seconds, end - 60);
                double span = Math.Max(1, end - start);
                for (int i = 1; i < values.Count; i++)
                {
                    if (!values[i - 1].Watts.HasValue || !values[i].Watts.HasValue)
                        continue;
                    float leftX = (float)((values[i - 1].Seconds - start) / span * (Width - 1));
                    float rightX = (float)((values[i].Seconds - start) / span * (Width - 1));
                    PointF left = new PointF(leftX, ValueToY(values[i - 1].Watts.Value, min, max));
                    PointF held = new PointF(rightX, left.Y);
                    PointF right = new PointF(rightX, ValueToY(values[i].Watts.Value, min, max));
                    // Hold the left observation until the next sample, matching
                    // the time-weighted mean. Missing endpoints break the line.
                    g.DrawLine(glowPen, left, held);
                    g.DrawLine(valuePen, left, held);
                    g.DrawLine(glowPen, held, right);
                    g.DrawLine(valuePen, held, right);
                }
            }
        }

        private float ValueToY(double value, double min, double max)
        {
            double normalized = (value - min) / (max - min);
            return (float)((Height - 2) - normalized * (Height - 4));
        }
    }

    internal sealed class BatteryBar : Panel
    {
        private int percentage = -1;
        private Color fillColor = Color.FromArgb(52, 211, 153);

        public BatteryBar()
        {
            DoubleBuffered = true;
            Height = 7;
        }

        public void SetValue(int value, Color color)
        {
            percentage = value;
            fillColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush background = new SolidBrush(Color.FromArgb(38, 50, 69)))
                e.Graphics.FillRectangle(background, bounds);

            if (percentage >= 0)
            {
                int width = Math.Max(1, (int)((Width - 1) * Math.Min(100, percentage) / 100.0));
                using (SolidBrush fill = new SolidBrush(fillColor))
                    e.Graphics.FillRectangle(fill, new Rectangle(0, 0, width, Height - 1));
            }
        }
    }

    internal static class NativeMethods
    {
        internal const int WmDpiChanged = 0x02E0;
        internal const uint SwpNoZOrder = 0x0004;
        internal const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr windowHandle, out Rect rectangle);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        internal static extern bool PrintWindow(IntPtr windowHandle, IntPtr deviceContext, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr iconHandle);
    }

    internal sealed partial class MainForm : Form
    {
        private readonly Label stateLabel;
        private readonly Label updatedLabel;
        private readonly Label powerLabel;
        private readonly Label detailLabel;
        private readonly Label percentageLabel;
        private readonly Label statisticsLabel;
        private readonly TextBox errorLabel;
        private readonly SparklinePanel chart;
        private readonly BatteryBar batteryBar;
        private readonly Timer timer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly bool trayEnabled;
        private readonly DpiLayout dpiLayout;
        private readonly Color chargingColor = Color.FromArgb(52, 211, 153);
        private readonly Color dischargeColor = Color.FromArgb(251, 191, 36);
        private readonly Color idleColor = Color.FromArgb(96, 165, 250);
        private readonly Color errorColor = Color.FromArgb(248, 113, 113);
        private readonly Color trayDischargeColor = Color.FromArgb(255, 211, 64);
        private Icon generatedTrayIcon;
        private string lastTrayGlyph = "";
        private Color lastTrayColor = Color.Empty;
        private bool lastDpiWindowPositionApplied;
        private Point lastSuggestedPosition;
        private readonly Label title;
        private readonly Label historyLabel;
        private readonly PowerHistory history = new PowerHistory();
        private DisplayMode displayMode = DisplayPreference.Load();
        private PowerSnapshot latest;
        private ComboBox modeSelector;
        private ToolStripMenuItem wholeModeItem;
        private ToolStripMenuItem batteryModeItem;
        private string elevationMessage;

        public MainForm(bool enableTray) : this(enableTray, null)
        {
        }

        public MainForm(bool enableTray, string startupMessage)
        {
            trayEnabled = enableTray;
            Text = "Battery Charge Meter";
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            ClientSize = new Size(430, 564);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 24, 39);
            ForeColor = Color.FromArgb(226, 232, 240);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            TopMost = true;

            title = NewLabel("估算整机输入功率", 20, 17, 225, 24, 11f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(148, 163, 184);

            stateLabel = NewLabel("READING...", 245, 17, 165, 24, 8.5f, FontStyle.Bold);
            stateLabel.TextAlign = ContentAlignment.MiddleRight;

            powerLabel = NewLabel("--.-- W", 18, 49, 394, 68, 38f, FontStyle.Bold);
            powerLabel.ForeColor = chargingColor;

            detailLabel = NewLabel("电池端 -- V    电池电流 ≈ -- A", 22, 122, 305, 24, 9f, FontStyle.Regular);
            detailLabel.ForeColor = Color.FromArgb(148, 163, 184);

            percentageLabel = NewLabel("--%", 330, 122, 78, 24, 11f, FontStyle.Bold);
            percentageLabel.TextAlign = ContentAlignment.MiddleRight;

            batteryBar = new BatteryBar();
            batteryBar.Location = new Point(21, 153);
            batteryBar.Width = 388;
            Controls.Add(batteryBar);

            historyLabel = NewLabel("曲线 / 峰值 0s", 20, 174, 390, 18, 8.5f, FontStyle.Bold);
            historyLabel.ForeColor = Color.FromArgb(100, 116, 139);

            statisticsLabel = NewLabel("均值 -- W   峰值 -- W", 20, 194, 390, 20, 9f, FontStyle.Regular);
            statisticsLabel.ForeColor = Color.FromArgb(100, 116, 139);
            statisticsLabel.TextAlign = ContentAlignment.MiddleLeft;

            chart = new SparklinePanel();
            chart.Location = new Point(20, 220);
            chart.Size = new Size(390, 81);
            Controls.Add(chart);

            BuildPowerSourceRows();

            Label note = NewLabel(
                "外电：平台 + 电池端净功率；未含转换损耗。",
                20, 478, 390, 20, 9f, FontStyle.Regular);
            note.ForeColor = Color.FromArgb(100, 116, 139);

            CheckBox topMostCheckBox = new CheckBox();
            topMostCheckBox.Text = "Always on top";
            topMostCheckBox.Checked = true;
            topMostCheckBox.AutoSize = true;
            topMostCheckBox.Location = new Point(20, 539);
            topMostCheckBox.ForeColor = Color.FromArgb(203, 213, 225);
            topMostCheckBox.FlatStyle = FlatStyle.Flat;
            topMostCheckBox.CheckedChanged += delegate { TopMost = topMostCheckBox.Checked; };
            Controls.Add(topMostCheckBox);

            updatedLabel = NewLabel("Waiting for sensor...", 215, 536, 194, 22, 8.5f, FontStyle.Regular);
            updatedLabel.ForeColor = Color.FromArgb(100, 116, 139);
            updatedLabel.TextAlign = ContentAlignment.MiddleRight;

            errorLabel = new TextBox();
            errorLabel.Multiline = true;
            errorLabel.AutoSize = false;
            errorLabel.Bounds = new Rectangle(20, 429, 390, 45);
            errorLabel.ReadOnly = true;
            errorLabel.ScrollBars = ScrollBars.Vertical;
            errorLabel.BorderStyle = BorderStyle.None;
            errorLabel.BackColor = BackColor;
            errorLabel.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            errorLabel.ForeColor = errorColor;

            Controls.Add(title);
            Controls.Add(stateLabel);
            Controls.Add(powerLabel);
            Controls.Add(detailLabel);
            Controls.Add(percentageLabel);
            Controls.Add(historyLabel);
            Controls.Add(statisticsLabel);
            Controls.Add(note);
            Controls.Add(updatedLabel);
            Controls.Add(errorLabel);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += TimerTick;

            trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showMenuItem = new ToolStripMenuItem("Show meter");
            showMenuItem.Font = new Font(showMenuItem.Font, FontStyle.Bold);
            showMenuItem.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += delegate { Close(); };
            trayMenu.Items.Add(showMenuItem);
            wholeModeItem = new ToolStripMenuItem("整机功率");
            batteryModeItem = new ToolStripMenuItem("电池端净功率");
            wholeModeItem.Click += delegate { ChangeMode(DisplayMode.WholeSystem); };
            batteryModeItem.Click += delegate { ChangeMode(DisplayMode.Battery); };
            trayMenu.Items.Add(wholeModeItem);
            trayMenu.Items.Add(batteryModeItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitMenuItem);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Net battery terminal power: reading sensor...";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            modeSelector = new ComboBox();
            modeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            modeSelector.FlatStyle = FlatStyle.Flat;
            modeSelector.BackColor = Color.FromArgb(31, 41, 55);
            modeSelector.ForeColor = ForeColor;
            modeSelector.Items.AddRange(new object[] { "整机功率", "电池端净功率（正充 / 负放）" });
            modeSelector.Bounds = new Rectangle(20, 402, 390, 28);
            modeSelector.SelectedIndex = displayMode == DisplayMode.Battery ? 1 : 0;
            modeSelector.SelectedIndexChanged += delegate
            {
                ChangeMode(modeSelector.SelectedIndex == 1 ? DisplayMode.Battery : DisplayMode.WholeSystem);
            };
            Controls.Add(modeSelector);
            BuildElevationControls(startupMessage);
            ChangeMode(displayMode);

            Shown += delegate
            {
                if (trayEnabled)
                    trayIcon.Visible = true;
                RefreshReading();
                timer.Start();
            };

            dpiLayout = new DpiLayout(this, ClientSize);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dpiLayout.Apply(ReadWindowDpi());
        }

        protected override void WndProc(ref Message message)
        {
            // WinForms gates its DpiChanged event behind app.config; handle the native
            // message so the application EXE retains per-monitor scaling.
            if (message.Msg == NativeMethods.WmDpiChanged && message.LParam != IntPtr.Zero)
            {
                int newDpi = (int)(message.WParam.ToInt64() & 0xffff);
                NativeMethods.Rect suggested = (NativeMethods.Rect)Marshal.PtrToStructure(
                    message.LParam,
                    typeof(NativeMethods.Rect));

                lastSuggestedPosition = new Point(suggested.Left, suggested.Top);
                lastDpiWindowPositionApplied = NativeMethods.SetWindowPos(
                    Handle,
                    IntPtr.Zero,
                    suggested.Left,
                    suggested.Top,
                    suggested.Right - suggested.Left,
                    suggested.Bottom - suggested.Top,
                    NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
                if (!lastDpiWindowPositionApplied)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Unable to apply the suggested DPI window bounds.");
                }

                dpiLayout.Apply(newDpi);
                chart.Invalidate();
                batteryBar.Invalidate();
                message.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (trayEnabled && WindowState == FormWindowState.Minimized)
                BeginInvoke(new MethodInvoker(MinimizeToTray));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null)
                    timer.Dispose();
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
                if (generatedTrayIcon != null)
                    generatedTrayIcon.Dispose();
                if (trayMenu != null)
                    trayMenu.Dispose();
                if (dpiLayout != null)
                    dpiLayout.Dispose();
                DisposePowerSources();
            }
            base.Dispose(disposing);
        }

        private int ReadWindowDpi()
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(Handle);
                if (dpi > 0)
                    return (int)dpi;
            }
            catch (EntryPointNotFoundException)
            {
            }

            return 96;
        }

        private Label NewLabel(string text, int x, int y, int width, int height, float size, FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.BackColor = Color.Transparent;
            return label;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            RefreshReading();
        }

        private void MinimizeToTray()
        {
            ShowInTaskbar = false;
            Hide();
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void RefreshReading()
        {
            try
            {
                latest = powerSources.Capture();
                PresentSnapshot(latest, true);
            }
            catch (Exception ex)
            {
                latest = null;
                history.Clear();
                chart.SetHistory(history.Points, errorColor);
                statisticsLabel.Text = "均值 -- W   峰值 -- W";
                stateLabel.Text = "SENSOR ERROR";
                powerLabel.Text = "N/A";
                detailLabel.Text = "电池端 -- V   电池电流 ≈ -- A";
                percentageLabel.Text = "--%";
                batteryBar.SetValue(-1, errorColor);
                ShowSourceError(batteryValue, ex.Message);
                ShowSourceError(cpuPackageValue, ex.Message);
                ShowSourceError(platformValue, ex.Message);
                ShowSourceError(wholeSystemValue, ex.Message);
                errorLabel.Text = ex.Message;
                UpdateTrayError();
            }
        }

        private void PresentSnapshot(PowerSnapshot snapshot, bool addHistory)
        {
            BatteryReading reading = snapshot.Battery;
            PowerSample selected = PowerDisplay.Select(snapshot, displayMode);
            errorLabel.Text = "";

            BatterySupplyProfile profile = reading.SupplyProfile;
            Color accent = AccentColor(profile.Accent);

            stateLabel.Text = profile.StateText;
            stateLabel.ForeColor = accent;

            title.Text = PowerSample.LabelFor(selected.Boundary);
            if (selected.Available)
            {
                powerLabel.Text = (selected.Kind == MeasurementKind.Estimated ? "≈ " : "")
                    + selected.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W";
            }
            else
            {
                powerLabel.Text = "N/A";
                errorLabel.Text = selected.UnavailableReason;
            }
            if (addHistory)
                history.Add(snapshot.ElapsedSeconds, displayMode, reading.SupplyState, selected);
            chart.SetHistory(history.Points, selected.Kind == MeasurementKind.Estimated ? Color.FromArgb(148, 163, 184) : accent);
            UpdateStatistics();
            powerLabel.ForeColor = selected.Kind == MeasurementKind.Estimated ? Color.FromArgb(148, 163, 184) : accent;
            string voltageText = reading.VoltageAvailable
                ? reading.VoltageVolts.ToString("0.00", CultureInfo.InvariantCulture) + " V"
                : "--.-- V";
            string currentText = reading.CurrentAvailable
                ? reading.CurrentAmps.ToString("0.00", CultureInfo.InvariantCulture) + " A"
                : "--.-- A";
            detailLabel.Text = "电池端 " + voltageText + "   电池电流 ≈ " + currentText;
            percentageLabel.Text = reading.Percentage >= 0
                ? reading.Percentage.ToString(CultureInfo.InvariantCulture) + "%"
                : "--%";
            percentageLabel.ForeColor = accent;
            batteryBar.SetValue(reading.Percentage, accent);
            UpdatePowerSources(snapshot);
            int scalePercent = (int)Math.Round(dpiLayout.CurrentDpi * 100.0 / 96.0);
            updatedLabel.Text = "Updated " + snapshot.Timestamp.ToString("HH:mm:ss")
                + "  |  " + scalePercent.ToString(CultureInfo.InvariantCulture) + "%";
            UpdateTrayDisplay(selected, profile);
        }

        private Color AccentColor(BatteryAccentKind accent)
        {
            switch (accent)
            {
                case BatteryAccentKind.Charging:
                    return chargingColor;
                case BatteryAccentKind.Discharging:
                    return dischargeColor;
                case BatteryAccentKind.Idle:
                    return idleColor;
                default:
                    return errorColor;
            }
        }

        private void UpdateTrayDisplay(
            PowerSample selected, BatterySupplyProfile profile)
        {
            if (!trayEnabled)
                return;

            Color textColor = profile.Discharging ? trayDischargeColor : Color.White;
            string glyph = selected.Available ? TrayPowerText(selected.Watts) : "--";
            SetTrayIcon(glyph, textColor);

            string power = selected.Available
                ? (selected.Kind == MeasurementKind.Estimated ? "≈ " : "") + selected.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W"
                : "N/A";
            SetTrayTooltip(PowerSample.LabelFor(selected.Boundary) + ": " + power);
        }

        private void UpdateTrayError()
        {
            if (!trayEnabled)
                return;
            SetTrayIcon("--", Color.FromArgb(180, 180, 180));
            SetTrayTooltip(displayMode == DisplayMode.Battery ? "电池端净功率: N/A" : "整机功率: N/A");
        }

        private string TrayPowerText(double powerWatts)
        {
            double watts = Math.Abs(powerWatts);
            if (watts >= 99.5)
                return "99+";
            return Math.Round(watts, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);
        }

        private void SetTrayTooltip(string value)
        {
            if (String.IsNullOrEmpty(value))
                value = "Net battery terminal power";
            trayIcon.Text = value.Length <= 63 ? value : value.Substring(0, 63);
        }

        private void SetTrayIcon(string glyph, Color textColor)
        {
            if (String.Equals(glyph, lastTrayGlyph, StringComparison.Ordinal) && textColor.ToArgb() == lastTrayColor.ToArgb())
                return;

            Icon nextIcon = CreateTextIcon(glyph, textColor);
            Icon previousIcon = generatedTrayIcon;
            generatedTrayIcon = nextIcon;
            trayIcon.Icon = nextIcon;
            lastTrayGlyph = glyph;
            lastTrayColor = textColor;

            if (previousIcon != null)
                previousIcon.Dispose();
        }

        private Icon CreateTextIcon(string glyph, Color textColor)
        {
            using (Bitmap bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float fontSize = glyph.Length <= 2 ? 42f : 30f;
                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat())
                using (GraphicsPath path = new GraphicsPath())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    RectangleF bounds = new RectangleF(0f, -2f, 64f, 66f);
                    path.AddString(glyph, font.FontFamily, (int)FontStyle.Bold, fontSize, bounds, format);

                    using (Pen outline = new Pen(Color.FromArgb(235, 12, 18, 28), 5f))
                    using (SolidBrush fill = new SolidBrush(textColor))
                    {
                        outline.LineJoin = LineJoin.Round;
                        graphics.DrawPath(outline, path);
                        graphics.FillPath(fill, path);
                    }
                }

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(iconHandle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(iconHandle);
                }
            }
        }

        private void UpdateStatistics()
        {
            double coverage;
            double? average = history.Average(out coverage);
            double? peak = history.Peak();
            string prefix = latest != null && PowerDisplay.Select(latest, displayMode).Kind == MeasurementKind.Estimated ? "≈ " : "";
            historyLabel.Text = "曲线 / 峰值 " + history.Duration(60).ToString("0.#", CultureInfo.InvariantCulture) + "s（最多 60s，峰值保留符号）";
            statisticsLabel.Text = String.Format(
                CultureInfo.InvariantCulture,
                "均值 {0} W（有效 {1:0.#}/30s）   峰值 {2} W",
                average.HasValue ? prefix + average.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--",
                coverage, peak.HasValue ? prefix + peak.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--");
        }

        private void ChangeMode(DisplayMode mode)
        {
            if (displayMode != mode)
            {
                displayMode = mode;
                history.Clear();
                DisplayPreference.Save(mode);
            }
            wholeModeItem.Checked = mode == DisplayMode.WholeSystem;
            batteryModeItem.Checked = mode == DisplayMode.Battery;
            int index = mode == DisplayMode.Battery ? 1 : 0;
            if (modeSelector.SelectedIndex != index)
                modeSelector.SelectedIndex = index;
            if (latest != null)
                PresentSnapshot(latest, history.Points.Count == 0);
        }

        private void BuildElevationControls(string message)
        {
            bool elevated = Startup.IsElevated();
            elevationMessage = elevated ? null : message;
            Label status = NewLabel(elevated ? "管理员模式" : "普通模式", 20, 506, 160, 22, 9f, FontStyle.Regular);
            Controls.Add(status);
            Button retry = new Button();
            retry.Text = "重新以管理员身份启动";
            retry.Bounds = new Rectangle(210, 501, 200, 29);
            retry.Enabled = !elevated;
            retry.FlatStyle = FlatStyle.Flat;
            retry.Click += delegate
            {
                retry.Enabled = false;
                ElevationResult result = Startup.TryElevate();
                if (result.Started)
                    Close();
                else
                {
                    elevationMessage = result.Message;
                    if (latest != null)
                        UpdatePowerSources(latest);
                    else
                        errorLabel.Text = elevationMessage;
                    retry.Enabled = true;
                }
            };
            Controls.Add(retry);
        }

    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            StartupRoute route = StartupRoute.Parse(args);
            if (!route.Valid)
            {
                Environment.ExitCode = 2;
                return;
            }
            ElevationResult elevation = Startup.Route(route, Startup.IsElevated(), Startup.TryElevate);
            if (elevation.Started)
                return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 2 && String.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                using (MainForm preview = new MainForm(false))
                    preview.RenderPreview(args[1]);
                return;
            }

            if ((args.Length == 3 || args.Length == 4)
                && String.Equals(args[0], "--dpi-preview", StringComparison.OrdinalIgnoreCase))
            {
                int targetDpi;
                if (!Int32.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetDpi))
                    throw new ArgumentException("DPI must be an integer.", "args");

                using (MainForm preview = new MainForm(false))
                {
                    if (args.Length == 3)
                    {
                        preview.RenderDpiTransitionPreview(args[1], targetDpi);
                    }
                    else
                    {
                        int returnDpi;
                        if (!Int32.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out returnDpi))
                            throw new ArgumentException("Return DPI must be an integer.", "args");
                        preview.RenderDpiTransitionPreview(args[1], targetDpi, returnDpi);
                    }
                }
                return;
            }

            if (args.Length == 2 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                bool passed;
                string report = PowerSelfTest.Run(out passed);
                File.WriteAllText(args[1], report);
                Environment.Exit(passed ? 0 : 1);
                return;
            }

            if (args.Length == 2
                && String.Equals(args[0], "--third-party-notices", StringComparison.OrdinalIgnoreCase))
            {
                ThirdPartyNotices.WriteTo(args[1]);
                return;
            }

            if ((args.Length == 2 || args.Length == 3)
                && String.Equals(args[0], "--power-probe", StringComparison.OrdinalIgnoreCase))
            {
                int seconds = 5;
                if (args.Length == 3
                    && !Int32.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
                    throw new ArgumentException("Seconds must be an integer.", "args");

                File.WriteAllText(args[1], PowerDiagnostics.Run(seconds));
                return;
            }

            if (args.Length == 4 && String.Equals(args[0], "--tray-preview", StringComparison.OrdinalIgnoreCase))
            {
                bool discharging = String.Equals(args[3], "discharging", StringComparison.OrdinalIgnoreCase);
                using (MainForm preview = new MainForm(false))
                    preview.RenderTrayIconPreview(args[1], args[2], discharging);
                return;
            }

            Application.Run(new MainForm(true, elevation.Message));
        }
    }
}
