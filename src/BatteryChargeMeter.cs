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
[assembly: System.Reflection.AssemblyTitle("Power Meter")]
[assembly: System.Reflection.AssemblyProduct("Power Meter")]

namespace BatteryChargeMeter
{
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

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute,
            out Rect rectangle, int size);

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
        private readonly Label statisticsCaption;
        private readonly Label historyCaption;
        private readonly TextBox errorLabel;
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
        private MeterSession session = new MeterSession(DisplayPreference.Load());
        private MeterView lastView;
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
            Text = Strings.AppName;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            ClientSize = new Size(384, 528);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Canvas;
            ForeColor = UiTheme.Ink;
            Font = new Font(UiTheme.TextFont, 9f, FontStyle.Regular, GraphicsUnit.Point);
            TopMost = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // A build without the embedded icon keeps the stock window icon.
            }

            title = NewLabel("估算整机输入功率", 20, 56, 344, 22, 10f, FontStyle.Regular);
            title.ForeColor = UiTheme.Muted;
            title.TextAlign = ContentAlignment.MiddleCenter;

            stateLabel = NewLabel("正在读取", 20, 168, 344, 20, 9f, FontStyle.Regular);
            stateLabel.TextAlign = ContentAlignment.MiddleCenter;

            powerLabel = new PowerReadout();
            powerLabel.Text = "--.-- W";
            powerLabel.Bounds = new Rectangle(20, 80, 344, 84);
            powerLabel.Font = UiTheme.DisplayFont(44f);
            powerLabel.TextAlign = ContentAlignment.MiddleCenter;
            powerLabel.ForeColor = UiTheme.Charging;

            detailLabel = NewLabel("", 20, 192, 344, 20, 9f, FontStyle.Regular);
            detailLabel.ForeColor = UiTheme.Muted;
            detailLabel.TextAlign = ContentAlignment.MiddleCenter;
            updatedLabel = NewLabel("--:--:--", 20, 12, 160, 20, 9f, FontStyle.Regular);
            updatedLabel.ForeColor = UiTheme.Faint;

            statisticsCaption = NewLabel("", 32, 228, 144, 18, 9f, FontStyle.Regular);
            statisticsCaption.ForeColor = UiTheme.Faint;
            statisticsCaption.TextAlign = ContentAlignment.MiddleCenter;
            historyCaption = NewLabel("", 208, 228, 144, 18, 9f, FontStyle.Regular);
            historyCaption.ForeColor = UiTheme.Faint;
            historyCaption.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(statisticsCaption);
            Controls.Add(historyCaption);

            statisticsLabel = NewLabel("", 32, 252, 144, 26, 14f, FontStyle.Regular);
            statisticsLabel.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Point);
            statisticsLabel.ForeColor = UiTheme.NumericMuted;
            statisticsLabel.TextAlign = ContentAlignment.MiddleCenter;

            historyLabel = NewLabel("", 208, 252, 144, 26, 14f, FontStyle.Regular);
            historyLabel.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Point);
            historyLabel.ForeColor = UiTheme.NumericMuted;
            historyLabel.TextAlign = ContentAlignment.MiddleCenter;

            Panel statisticsRule = new Panel();
            statisticsRule.BackColor = UiTheme.Hairline;
            statisticsRule.Bounds = new Rectangle(192, 232, 1, 40);
            Controls.Add(statisticsRule);

            sourcesPanel = new AntdUI.Panel();
            sourcesPanel.Bounds = new Rectangle(20, 296, 344, 118);
            sourcesPanel.BackColor = UiTheme.Canvas;
            sourcesPanel.Back = UiTheme.GroupSurface;
            sourcesPanel.Radius = 10;
            sourcesPanel.BorderWidth = 0;
            sourcesPanel.BorderColor = UiTheme.Hairline;
            Controls.Add(sourcesPanel);

            Label batterySection = NewLabel("电池电量", 14, 12, 96, 20, 9f, FontStyle.Regular);
            batterySection.ForeColor = UiTheme.Ink;
            sourcesPanel.Controls.Add(batterySection);

            percentageLabel = NewLabel("--%", 258, 12, 72, 20, 10f, FontStyle.Regular);
            percentageLabel.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point);
            percentageLabel.ForeColor = UiTheme.Ink;
            percentageLabel.TextAlign = ContentAlignment.MiddleRight;

            batteryBar = new BatteryBar();
            batteryBar.Location = new Point(116, 21);
            batteryBar.Size = new Size(132, 4);
            sourcesPanel.Controls.Add(batteryBar);

            Panel divider = new Panel();
            divider.BackColor = UiTheme.Hairline;
            divider.Bounds = new Rectangle(14, 40, 316, 1);
            sourcesPanel.Controls.Add(divider);

            BuildPowerSourceRows();

            errorLabel = new TextBox();
            errorLabel.Multiline = true;
            errorLabel.AutoSize = false;
            errorLabel.Bounds = new Rectangle(20, 424, 344, 48);
            errorLabel.ReadOnly = true;
            errorLabel.ScrollBars = ScrollBars.Vertical;
            errorLabel.BorderStyle = BorderStyle.None;
            errorLabel.BackColor = UiTheme.Canvas;
            errorLabel.Font = new Font(UiTheme.TextFont, 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            errorLabel.ForeColor = UiTheme.ErrorRed;
            errorLabel.TextChanged += delegate { UpdateDiagnosticLayout(); };

            footerBand = new Panel();
            footerBand.BackColor = UiTheme.FooterBand;
            footerBand.Bounds = new Rectangle(0, 484, 384, 44);
            footerBand.Controls.Add(updatedLabel);

            Panel footerRule = new Panel();
            footerRule.BackColor = UiTheme.FooterRule;
            footerRule.Bounds = new Rectangle(0, 0, 384, 1);
            footerBand.Controls.Add(footerRule);

            modeSegments = new SegmentedControl("整机功率", "电池端");
            modeSegments.Font = new Font(UiTheme.TextFont, 9f, FontStyle.Regular, GraphicsUnit.Point);
            modeSegments.Bounds = new Rectangle(98, 16, 188, 28);
            modeSegments.SelectedIndex = session.Mode == DisplayMode.Battery ? 1 : 0;
            modeSegments.SelectionChanged += delegate
            {
                ChangeMode(modeSegments.SelectedIndex == 1 ? DisplayMode.Battery : DisplayMode.WholeSystem);
            };
            Controls.Add(modeSegments);

            BuildSettingsControls();

            Controls.Add(footerBand);

            Controls.Add(title);
            Controls.Add(stateLabel);
            Controls.Add(powerLabel);
            Controls.Add(detailLabel);
            Controls.Add(statisticsLabel);
            Controls.Add(historyLabel);
            sourcesPanel.Controls.Add(percentageLabel);
            Controls.Add(errorLabel);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += TimerTick;

            trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showMenuItem = LocalizedMenuItem("显示窗口");
            showMenuItem.Font = new Font(showMenuItem.Font, FontStyle.Bold);
            showMenuItem.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem exitMenuItem = LocalizedMenuItem("退出");
            exitMenuItem.Click += delegate { Close(); };
            trayMenu.Items.Add(showMenuItem);
            wholeModeItem = LocalizedMenuItem("整机功率");
            batteryModeItem = LocalizedMenuItem("电池端净功率");
            wholeModeItem.Click += delegate { ChangeMode(DisplayMode.WholeSystem); };
            batteryModeItem.Click += delegate { ChangeMode(DisplayMode.Battery); };
            trayMenu.Items.Add(wholeModeItem);
            trayMenu.Items.Add(batteryModeItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitMenuItem);

            trayIcon = new NotifyIcon();
            trayIcon.Text = Strings.AppName + ": " + Strings.Get("正在读取");
            trayIcon.Icon = Icon;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            BuildElevationControls(startupMessage);
            BuildAutostartControls();
            BuildLanguageControls();
            ChangeMode(session.Mode);

            Shown += delegate
            {
                StartMonitoring();
            };

            dpiLayout = new DpiLayout(this, ClientSize, errorLabel, 60);
            UpdateDiagnosticLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dpiLayout.Apply(ReadWindowDpi());
            LayoutSourceRows();
            if (trayEnabled)
            {
                restoreMessage = GuiInstance.ListenForRestore(Handle, Startup.Identity);
                restoreWindow = Handle;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (restoreWindow != IntPtr.Zero)
                GuiInstance.StopListening(restoreWindow, Startup.Identity);
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
                CloseSettings();
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
                LayoutSourceRows();
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
                CloseSettings();
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
            Label label = new AlignedLabel();
            label.Tag = text;
            label.Text = Strings.Get(text);
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.Font = new Font(UiTheme.TextFont, size, style, GraphicsUnit.Point);
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
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
                ShowReading(powerSources.Capture());
            }
            catch (Exception ex)
            {
                ShowView(session.Fail(ex.Message));
            }
        }

        internal void ShowReading(PowerSnapshot snapshot)
        {
            ShowView(session.Observe(snapshot));
        }

        // Binds a view to the controls; all display rules live in MeterSession.
        private void ShowView(MeterView view)
        {
            lastView = view;
            title.Text = view.Title.Text;
            title.ForeColor = UiTheme.Tone(view.Title.Tone, view.Accent);
            sourceTip.SetToolTip(title, view.Title.Tooltip);
            stateLabel.Text = view.Supply.Text;
            stateLabel.ForeColor = UiTheme.Tone(view.Supply.Tone, view.Accent);
            powerLabel.Text = view.Headline.Text;
            powerLabel.ForeColor = UiTheme.Tone(view.Headline.Tone, view.Accent);
            sourceTip.SetToolTip(powerLabel, view.Headline.Tooltip);
            detailLabel.Text = view.Detail;
            percentageLabel.Text = view.Percentage;
            batteryBar.SetValue(view.BatteryLevel, UiTheme.Tone(view.BatteryTone, view.Accent));

            statisticsCaption.Text = view.AverageCaption;
            historyCaption.Text = view.PeakCaption;
            sourceTip.SetToolTip(statisticsCaption, view.AverageTooltip);
            sourceTip.SetToolTip(statisticsLabel, view.AverageTooltip);
            sourceTip.SetToolTip(historyCaption, view.PeakTooltip);
            sourceTip.SetToolTip(historyLabel, view.PeakTooltip);
            statisticsLabel.Text = view.AverageValue;
            historyLabel.Text = view.PeakValue;

            UpdatePowerSources(view);
            updatedLabel.Text = view.Updated;
            if (trayEnabled)
            {
                if (view.TrayGlyph != null)
                    SetTrayIcon(view.TrayGlyph, UiTheme.TrayTile(view.Accent));
                SetTrayTooltip(view.TrayTooltip);
            }
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

        private void ChangeMode(DisplayMode mode)
        {
            if (session.Mode != mode)
                DisplayPreference.Save(mode);
            MeterView view = session.SetMode(mode);
            wholeModeItem.Checked = mode == DisplayMode.WholeSystem;
            batteryModeItem.Checked = mode == DisplayMode.Battery;
            int index = mode == DisplayMode.Battery ? 1 : 0;
            if (modeSegments.SelectedIndex != index)
                modeSegments.SelectedIndex = index;
            LayoutSourceRows();
            ShowView(view);
        }

        private void BuildElevationControls(string message)
        {
            elevationMessage = Startup.IsElevated() ? null : message;
        }

        private void UpdateDiagnosticLayout()
        {
            bool expanded = !String.IsNullOrWhiteSpace(errorLabel.Text);
            errorLabel.Visible = expanded;
            if (dpiLayout != null)
                dpiLayout.SetSectionExpanded(expanded);
            LayoutSourceRows();
        }

    }

    internal static class Program
    {
        // An elevated launch migrates an unprotected task and refreshes this
        // installation's protected copy. Failures leave the state for the
        // settings panel to report.
        private static void SynchronizeAutostart()
        {
            try
            {
                using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                    manager.Synchronize(true);
            }
            catch (Exception)
            {
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            EmbeddedUi.Initialize();
            Run(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run(string[] args)
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
                    if (!instance.Acquire(Startup.Identity, route.Handoff))
                    {
                        if (!route.StartHidden)
                            GuiInstance.RestoreExisting(Startup.Identity);
                        return;
                    }
                    ElevationResult elevation = Startup.Route(route, Startup.IsElevated(), Startup.TryElevate);
                    if (elevation.Started)
                        return;
                    if (Startup.IsElevated())
                        SynchronizeAutostart();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(true, elevation.Message, route.StartHidden));
                }
                return;
            }
            if (route.Command == "--remove-autostart")
            {
                // 0: removed or absent; 3: task gone, but deleting this
                // installation's protected copy needs an elevated rerun;
                // 6: removed, but a same-name task that is not this program's
                // was left unchanged (see AutostartRemoval).
                try
                {
                    using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                        Environment.ExitCode = (int)manager.Disable();
                }
                catch
                {
                    Environment.ExitCode = 1;
                }
                return;
            }
            if (route.Command == "--sync-autostart")
            {
                // Elevated: apply and return 0. Ordinary token: report what an
                // elevated run would change (3 outdated copy, 4 unprotected
                // task, 5 unused copy) without requesting UAC.
                try
                {
                    using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                        Environment.ExitCode = (int)manager.Synchronize(Startup.IsElevated());
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
