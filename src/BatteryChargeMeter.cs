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
        private Color lineColor = UiTheme.Charging;

        public SparklinePanel()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.Surface;
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
            if (values.Count < 2)
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float uiScale = Math.Max(1f, Width / 384f);

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
            double end = values[values.Count - 1].Seconds;
            double start = Math.Max(values[0].Seconds, end - 60);
            double span = Math.Max(1, end - start);

            // The step-filled area encodes magnitude against zero; hold the left
            // observation until the next sample, matching the time-weighted mean.
            using (SolidBrush area = new SolidBrush(Color.FromArgb(20, lineColor)))
            {
                for (int i = 1; i < values.Count; i++)
                {
                    if (!values[i - 1].Watts.HasValue || !values[i].Watts.HasValue)
                        continue;
                    float leftX = (float)((values[i - 1].Seconds - start) / span * (Width - 1));
                    float rightX = (float)((values[i].Seconds - start) / span * (Width - 1));
                    float heldY = ValueToY(values[i - 1].Watts.Value, min, max);
                    float top = Math.Min(heldY, zeroY);
                    g.FillRectangle(
                        area, leftX, top, Math.Max(1f, rightX - leftX), Math.Abs(zeroY - heldY));
                }
            }

            using (Pen zeroPen = new Pen(UiTheme.ZeroLine, uiScale))
            {
                zeroPen.DashStyle = DashStyle.Dash;
                g.DrawLine(zeroPen, 0, zeroY, Width, zeroY);
            }

            using (Pen valuePen = new Pen(lineColor, 2f * uiScale))
            {
                valuePen.LineJoin = LineJoin.Round;
                for (int i = 1; i < values.Count; i++)
                {
                    if (!values[i - 1].Watts.HasValue || !values[i].Watts.HasValue)
                        continue;
                    float leftX = (float)((values[i - 1].Seconds - start) / span * (Width - 1));
                    float rightX = (float)((values[i].Seconds - start) / span * (Width - 1));
                    PointF left = new PointF(leftX, ValueToY(values[i - 1].Watts.Value, min, max));
                    PointF held = new PointF(rightX, left.Y);
                    PointF right = new PointF(rightX, ValueToY(values[i].Watts.Value, min, max));
                    // Missing endpoints break the line.
                    g.DrawLine(valuePen, left, held);
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
        private Color fillColor = UiTheme.Charging;

        public BatteryBar()
        {
            DoubleBuffered = true;
        }

        public void SetValue(int value, Color color)
        {
            percentage = value;
            fillColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath track = WidgetPath.Rounded(
                new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2))
            using (SolidBrush trackBrush = new SolidBrush(UiTheme.Track))
                g.FillPath(trackBrush, track);

            if (percentage < 0)
                return;

            int width = Math.Max(2, (int)((Width - 1) * Math.Min(100, percentage) / 100.0));
            using (GraphicsPath fill = WidgetPath.Rounded(
                new Rectangle(0, 0, width, Height - 1), Math.Min((Height - 1) / 2, width / 2)))
            using (SolidBrush fillBrush = new SolidBrush(fillColor))
                g.FillPath(fillBrush, fill);
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
        private SegmentedControl modeSegments;
        private Panel footerBand;
        private ToolStripMenuItem wholeModeItem;
        private ToolStripMenuItem batteryModeItem;
        private string elevationMessage;
        private uint restoreMessage;
        private IntPtr restoreWindow;

        public MainForm(bool enableTray) : this(enableTray, null)
        {
        }

        public MainForm(bool enableTray, string startupMessage) : this(enableTray, startupMessage, false)
        {
        }

        public MainForm(bool enableTray, string startupMessage, bool hidden)
        {
            trayEnabled = enableTray;
            startHidden = hidden;
            Text = "Battery Charge Meter";
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            ClientSize = new Size(432, 500);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.Ink;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            TopMost = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // A build without the embedded icon keeps the stock window icon.
            }

            title = NewLabel("估算整机输入功率", 24, 16, 240, 20, 10f, FontStyle.Bold);
            title.ForeColor = UiTheme.Ink;

            stateLabel = NewLabel("READING...", 264, 18, 144, 18, 8.5f, FontStyle.Bold);
            stateLabel.TextAlign = ContentAlignment.MiddleRight;

            powerLabel = NewLabel("--.-- W", 24, 40, 384, 54, 33f, FontStyle.Bold);
            powerLabel.ForeColor = UiTheme.Charging;

            detailLabel = NewLabel("电池端 -- V    电池电流 ≈ -- A", 24, 100, 384, 16, 9f, FontStyle.Regular);
            detailLabel.ForeColor = UiTheme.Muted;

            chart = new SparklinePanel();
            chart.Location = new Point(24, 124);
            chart.Size = new Size(384, 72);
            Controls.Add(chart);

            statisticsLabel = NewLabel("均值 -- W（有效 --/30s）", 24, 204, 190, 15, 8.5f, FontStyle.Regular);
            statisticsLabel.ForeColor = UiTheme.Muted;

            historyLabel = NewLabel("峰值 -- W · 0/60s", 214, 204, 194, 15, 8.5f, FontStyle.Regular);
            historyLabel.ForeColor = UiTheme.Muted;
            historyLabel.TextAlign = ContentAlignment.MiddleRight;

            Label batterySection = NewLabel("电池", 24, 232, 80, 18, 9.5f, FontStyle.Bold);
            batterySection.ForeColor = UiTheme.Ink;
            Controls.Add(batterySection);

            percentageLabel = NewLabel("--%", 284, 230, 124, 20, 11f, FontStyle.Bold);
            percentageLabel.ForeColor = UiTheme.Ink;
            percentageLabel.TextAlign = ContentAlignment.MiddleRight;

            batteryBar = new BatteryBar();
            batteryBar.Location = new Point(24, 254);
            batteryBar.Size = new Size(384, 6);
            Controls.Add(batteryBar);

            Panel divider = new Panel();
            divider.BackColor = UiTheme.Hairline;
            divider.Bounds = new Rectangle(24, 276, 384, 1);
            Controls.Add(divider);

            BuildPowerSourceRows();

            Label note = NewLabel(
                "外电：平台 + 电池端净功率；未含转换损耗。",
                24, 352, 384, 14, 8f, FontStyle.Regular);
            note.ForeColor = UiTheme.Faint;

            errorLabel = new TextBox();
            errorLabel.Multiline = true;
            errorLabel.AutoSize = false;
            errorLabel.Bounds = new Rectangle(24, 370, 384, 28);
            errorLabel.ReadOnly = true;
            errorLabel.ScrollBars = ScrollBars.Vertical;
            errorLabel.BorderStyle = BorderStyle.None;
            errorLabel.BackColor = UiTheme.Surface;
            errorLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            errorLabel.ForeColor = UiTheme.ErrorRed;

            footerBand = new Panel();
            footerBand.BackColor = UiTheme.FooterBand;
            footerBand.Bounds = new Rectangle(0, 432, 432, 68);

            Panel footerRule = new Panel();
            footerRule.BackColor = UiTheme.FooterRule;
            footerRule.Bounds = new Rectangle(0, 0, 432, 1);
            footerBand.Controls.Add(footerRule);

            modeSegments = new SegmentedControl("整机功率", "电池端");
            modeSegments.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            modeSegments.Bounds = new Rectangle(24, 13, 192, 26);
            modeSegments.SelectedIndex = displayMode == DisplayMode.Battery ? 1 : 0;
            modeSegments.SelectionChanged += delegate
            {
                ChangeMode(modeSegments.SelectedIndex == 1 ? DisplayMode.Battery : DisplayMode.WholeSystem);
            };
            footerBand.Controls.Add(modeSegments);

            CheckBox topMostCheckBox = new CheckBox();
            topMostCheckBox.Text = "置顶";
            topMostCheckBox.Checked = true;
            topMostCheckBox.AutoSize = true;
            topMostCheckBox.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            topMostCheckBox.Location = new Point(232, 18);
            topMostCheckBox.ForeColor = UiTheme.Muted;
            topMostCheckBox.FlatStyle = FlatStyle.Flat;
            topMostCheckBox.CheckedChanged += delegate { TopMost = topMostCheckBox.Checked; };
            footerBand.Controls.Add(topMostCheckBox);

            updatedLabel = NewLabel("--:--:--", 348, 19, 60, 15, 8.5f, FontStyle.Regular);
            updatedLabel.ForeColor = UiTheme.Faint;
            updatedLabel.TextAlign = ContentAlignment.MiddleRight;
            footerBand.Controls.Add(updatedLabel);

            Controls.Add(footerBand);

            Controls.Add(title);
            Controls.Add(stateLabel);
            Controls.Add(powerLabel);
            Controls.Add(detailLabel);
            Controls.Add(statisticsLabel);
            Controls.Add(historyLabel);
            Controls.Add(percentageLabel);
            Controls.Add(note);
            Controls.Add(errorLabel);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += TimerTick;

            trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showMenuItem = new ToolStripMenuItem("显示窗口");
            showMenuItem.Font = new Font(showMenuItem.Font, FontStyle.Bold);
            showMenuItem.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("退出");
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
            trayIcon.Icon = Icon;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            BuildElevationControls(startupMessage);
            BuildAutostartControls();
            ChangeMode(displayMode);

            Shown += delegate
            {
                StartMonitoring();
            };

            dpiLayout = new DpiLayout(this, ClientSize);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dpiLayout.Apply(ReadWindowDpi());
            if (trayEnabled)
            {
                restoreMessage = GuiInstance.ListenForRestore(Handle, Application.ExecutablePath);
                restoreWindow = Handle;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (restoreWindow != IntPtr.Zero)
                GuiInstance.StopListening(restoreWindow, Application.ExecutablePath);
            restoreWindow = IntPtr.Zero;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (restoreMessage != 0 && message.Msg == restoreMessage)
            {
                RestoreFromTray();
                message.Result = IntPtr.Zero;
                return;
            }
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
                modeSegments.Invalidate();
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
            startHidden = false;
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
                chart.SetHistory(history.Points, UiTheme.ErrorRed);
                statisticsLabel.Text = "均值 -- W（有效 --/30s）";
                historyLabel.Text = "峰值 -- W · 0/60s";
                stateLabel.Text = "SENSOR ERROR";
                powerLabel.Text = "N/A";
                detailLabel.Text = "电池端 -- V   电池电流 ≈ -- A";
                percentageLabel.Text = "--%";
                batteryBar.SetValue(-1, UiTheme.ErrorRed);
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
            Color accent = UiTheme.Accent(profile.Accent);

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
            chart.SetHistory(history.Points, selected.Kind == MeasurementKind.Estimated ? UiTheme.Muted : accent);
            UpdateStatistics();
            powerLabel.ForeColor = selected.Kind == MeasurementKind.Estimated ? UiTheme.Muted : accent;
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
            batteryBar.SetValue(reading.Percentage, accent);
            UpdatePowerSources(snapshot);
            updatedLabel.Text = snapshot.Timestamp.ToString("HH:mm:ss");
            UpdateTrayDisplay(selected, profile);
        }

        private void UpdateTrayDisplay(
            PowerSample selected, BatterySupplyProfile profile)
        {
            if (!trayEnabled)
                return;

            string glyph = selected.Available ? TrayPowerText(selected.Watts) : "--";
            SetTrayIcon(glyph, UiTheme.TrayTile(profile.Accent));

            string power = selected.Available
                ? (selected.Kind == MeasurementKind.Estimated ? "≈ " : "") + selected.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W"
                : "N/A";
            SetTrayTooltip(PowerSample.LabelFor(selected.Boundary) + ": " + power);
        }

        private void UpdateTrayError()
        {
            if (!trayEnabled)
                return;
            SetTrayIcon("--", UiTheme.TrayNeutral);
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

        private void SetTrayIcon(string glyph, Color tileColor)
        {
            if (String.Equals(glyph, lastTrayGlyph, StringComparison.Ordinal) && tileColor.ToArgb() == lastTrayColor.ToArgb())
                return;

            Icon nextIcon = CreateTextIcon(glyph, tileColor);
            Icon previousIcon = generatedTrayIcon;
            generatedTrayIcon = nextIcon;
            trayIcon.Icon = nextIcon;
            lastTrayGlyph = glyph;
            lastTrayColor = tileColor;

            if (previousIcon != null)
                previousIcon.Dispose();
        }

        private Icon CreateTextIcon(string glyph, Color tileColor)
        {
            using (Bitmap bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // A solid tile keeps the watt glyph readable on any taskbar theme.
                using (GraphicsPath tile = WidgetPath.Rounded(new Rectangle(3, 3, 58, 58), 15))
                using (SolidBrush tileBrush = new SolidBrush(tileColor))
                    graphics.FillPath(tileBrush, tile);

                float fontSize = glyph.Length <= 2 ? 34f : 26f;
                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(glyph, font, Brushes.White, new RectangleF(0f, 1f, 64f, 63f), format);
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
            statisticsLabel.Text = String.Format(
                CultureInfo.InvariantCulture,
                "均值 {0} W（有效 {1:0.#}/30s）",
                average.HasValue ? prefix + average.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--",
                coverage);
            historyLabel.Text = String.Format(
                CultureInfo.InvariantCulture,
                "峰值 {0} W · {1}/60s",
                peak.HasValue ? prefix + peak.Value.ToString("0.00", CultureInfo.InvariantCulture) : "--",
                history.Duration(60).ToString("0.#", CultureInfo.InvariantCulture));
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
            if (modeSegments.SelectedIndex != index)
                modeSegments.SelectedIndex = index;
            if (latest != null)
                PresentSnapshot(latest, history.Points.Count == 0);
        }

        private void BuildElevationControls(string message)
        {
            bool elevated = Startup.IsElevated();
            elevationMessage = elevated ? null : message;
            Label status = NewLabel(elevated ? "管理员模式" : "普通模式", 24, 406, 88, 18, 8.5f, FontStyle.Regular);
            status.ForeColor = UiTheme.Muted;
            Controls.Add(status);
            LinkLabel retry = new LinkLabel();
            retry.Text = "以管理员身份重新启动";
            retry.Bounds = new Rectangle(112, 406, 220, 18);
            retry.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            retry.LinkColor = UiTheme.Ink;
            retry.ActiveLinkColor = UiTheme.Charging;
            retry.VisitedLinkColor = UiTheme.Ink;
            retry.Visible = !elevated;
            retry.LinkClicked += delegate
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
            if (route.Command == "gui")
            {
                using (GuiInstance instance = new GuiInstance())
                {
                    if (!instance.Acquire(Application.ExecutablePath, route.Handoff))
                    {
                        if (!route.StartHidden)
                            GuiInstance.RestoreExisting(Application.ExecutablePath);
                        return;
                    }
                    ElevationResult elevation = Startup.Route(route, Startup.IsElevated(), Startup.TryElevate);
                    if (elevation.Started)
                        return;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(true, elevation.Message, route.StartHidden));
                }
                return;
            }
            if (route.Command == "--remove-autostart")
            {
                try
                {
                    using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                        manager.Disable();
                }
                catch
                {
                    Environment.ExitCode = 1;
                }
                return;
            }
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

            Environment.ExitCode = 2;
        }
    }
}
