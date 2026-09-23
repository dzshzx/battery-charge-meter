using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Source details render the same snapshot as the selected headline, as
    /// three supporting rows; the selected fourth value is the headline.
    /// </summary>
    internal sealed partial class MainForm
    {
        private readonly PowerSources powerSources = new PowerSources();
        private readonly ToolTip sourceTip = new ToolTip();

        private Label cpuPackageValue;
        private Label batteryValue;
        private Label platformValue;
        private Label wholeSystemName;
        private Label wholeSystemValue;
        private AntdUI.Panel sourcesPanel;
        private readonly Label[] sourceNames = new Label[4];
        private readonly Label[] sourceValues = new Label[4];

        /// <summary>
        /// Builds the source rows. Called from the constructor before
        /// <see cref="DpiLayout"/> captures the design bounds, so these controls
        /// scale with everything else.
        /// </summary>
        private void BuildPowerSourceRows()
        {
            Label unusedName;
            batteryValue = AddSourceRow(0, PowerSample.LabelFor(PowerBoundary.BatteryTerminal), out unusedName);
            cpuPackageValue = AddSourceRow(
                1, PowerSample.LabelFor(PowerBoundary.CpuPackage), out unusedName);
            platformValue = AddSourceRow(
                2, PowerSample.LabelFor(PowerBoundary.Platform), out unusedName);

            // The last row answers the whole-machine question, which is a
            // different boundary on battery than on external power, so its
            // caption is rewritten each tick from the sample itself.
            wholeSystemValue = AddSourceRow(
                3, PowerSample.LabelFor(PowerBoundary.EstimatedSystemInput), out wholeSystemName);
        }

        private Label AddSourceRow(int index, string caption, out Label nameLabel)
        {
            int y = 44 + index * 22;

            nameLabel = NewLabel(caption, 14, y, 184, 22, 9f, FontStyle.Regular);
            nameLabel.ForeColor = UiTheme.Muted;
            sourcesPanel.Controls.Add(nameLabel);

            Label value = NewLabel("--", 210, y, 120, 22, 9.5f, FontStyle.Regular);
            value.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.ForeColor = UiTheme.Ink;
            sourcesPanel.Controls.Add(value);
            sourceNames[index] = nameLabel;
            sourceValues[index] = value;

            return value;
        }

        private void LayoutSourceRows()
        {
            if (sourceNames[3] == null) return;
            float scale = dpiLayout != null && dpiLayout.CurrentDpi > 0 ? dpiLayout.CurrentDpi / 96f : 1f;
            int selected = displayMode == DisplayMode.Battery ? 0 : 3;
            int row = 0;
            for (int index = 0; index < sourceNames.Length; index++)
            {
                bool visible = index != selected;
                sourceNames[index].Visible = visible;
                sourceValues[index].Visible = visible;
                if (!visible) continue;
                int y = 44 + row++ * 22;
                sourceNames[index].Bounds = DpiLayout.ScaleBounds(new Rectangle(14, y, 184, 22), scale);
                sourceValues[index].Bounds = DpiLayout.ScaleBounds(new Rectangle(210, y, 120, 22), scale);
            }
        }

        private void UpdatePowerSources(PowerSnapshot snapshot)
        {
            Apply(batteryValue, snapshot.BatteryTerminal);
            Apply(cpuPackageValue, snapshot.CpuPackage);
            Apply(platformValue, snapshot.Platform);

            if (snapshot.WholeSystem != null)
                wholeSystemName.Text = Strings.Get(PowerSample.LabelFor(snapshot.WholeSystem.Boundary));
            sourceTip.SetToolTip(wholeSystemName, Strings.Get(
                "外电：平台功率 + 带符号的电池端净功率，未含转换损耗。\n电池供电：电池端放电功率。CPU 包已包含在平台功率内。"));
            Apply(wholeSystemValue, snapshot.WholeSystem);
            System.Text.StringBuilder reasons = new System.Text.StringBuilder();
            if (!String.IsNullOrEmpty(elevationMessage))
                reasons.AppendLine(Strings.Diagnostic(elevationMessage));
            if (!String.IsNullOrEmpty(autostartStateMessage))
                reasons.AppendLine(Strings.Diagnostic(autostartStateMessage));
            else if (!String.IsNullOrEmpty(autostartMessage))
                reasons.AppendLine(Strings.Diagnostic(autostartMessage));
            foreach (PowerSample sample in new PowerSample[] { snapshot.BatteryTerminal, snapshot.CpuPackage, snapshot.Platform })
            {
                if (sample != null && !sample.Available)
                    reasons.AppendLine(Strings.Get(PowerSample.LabelFor(sample.Boundary)) + ": " + Strings.Diagnostic(sample.UnavailableReason));
            }
            PowerSample selected = PowerDisplay.Select(snapshot, displayMode);
            if (!selected.Available && selected.Boundary != PowerBoundary.BatteryTerminal
                && (snapshot.Platform.Available || selected.UnavailableReason != snapshot.Platform.UnavailableReason))
                reasons.AppendLine(Strings.Get(PowerSample.LabelFor(selected.Boundary)) + ": " + Strings.Diagnostic(selected.UnavailableReason));
            errorLabel.Text = reasons.ToString().TrimEnd();
            LayoutSourceRows();
        }

        /// <summary>
        /// An unavailable source shows N/A and carries its reason in the
        /// tooltip. It is never filled in from a neighbouring boundary, because
        /// those measure different things.
        /// </summary>
        private void Apply(Label target, PowerSample sample)
        {
            if (target == null)
                return;

            if (sample == null || !sample.Available)
            {
                target.Text = "N/A";
                target.ForeColor = UiTheme.Muted;
                sourceTip.SetToolTip(
                    target,
                    sample == null ? Strings.Get("无数据") : Strings.Diagnostic(sample.UnavailableReason));
                return;
            }

            string text = sample.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W";
            if (sample.Kind == MeasurementKind.Estimated)
                text = "≈ " + text;

            target.Text = text;
            target.ForeColor = sample.Kind == MeasurementKind.Estimated
                ? UiTheme.Muted
                : UiTheme.Ink;
            sourceTip.SetToolTip(target, Strings.Diagnostic(sample.Source));
        }

        private void ShowSourceError(Label target, string message)
        {
            if (target == null)
                return;

            target.Text = "N/A";
            target.ForeColor = UiTheme.Muted;
            sourceTip.SetToolTip(target, Strings.Diagnostic(message));
        }

        private void DisposePowerSources()
        {
            if (powerSources != null)
                powerSources.Dispose();
            if (sourceTip != null)
                sourceTip.Dispose();
        }
    }
}
