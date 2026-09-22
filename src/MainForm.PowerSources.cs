using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Source details render the same snapshot as the selected headline.
    /// </summary>
    internal sealed partial class MainForm
    {
        private const int SourceRowHeight = 20;
        private const int FirstSourceRowY = 322;
        private const int SourceRowSpacing = 19;

        private readonly PowerSources powerSources = new PowerSources();
        private readonly ToolTip sourceTip = new ToolTip();

        private Label cpuPackageValue;
        private Label batteryValue;
        private Label platformValue;
        private Label wholeSystemName;
        private Label wholeSystemValue;

        /// <summary>
        /// Builds the source rows. Called from the constructor before
        /// <see cref="DpiLayout"/> captures the design bounds, so these controls
        /// scale with everything else.
        /// </summary>
        private void BuildPowerSourceRows()
        {
            Label sectionTitle = NewLabel("POWER SOURCES", 20, 303, 200, 18, 8.5f, FontStyle.Bold);
            sectionTitle.ForeColor = Color.FromArgb(100, 116, 139);
            Controls.Add(sectionTitle);

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
            int y = FirstSourceRowY + (index * SourceRowSpacing);

            nameLabel = NewLabel(caption, 20, y, 190, SourceRowHeight, 9.5f, FontStyle.Regular);
            nameLabel.ForeColor = Color.FromArgb(148, 163, 184);
            Controls.Add(nameLabel);

            Label value = NewLabel("--", 210, y, 200, SourceRowHeight, 9.5f, FontStyle.Bold);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(value);

            return value;
        }

        private void UpdatePowerSources(PowerSnapshot snapshot)
        {
            Apply(batteryValue, snapshot.BatteryTerminal);
            Apply(cpuPackageValue, snapshot.CpuPackage);
            Apply(platformValue, snapshot.Platform);

            if (snapshot.WholeSystem != null)
                wholeSystemName.Text = PowerSample.LabelFor(snapshot.WholeSystem.Boundary);
            Apply(wholeSystemValue, snapshot.WholeSystem);
            System.Text.StringBuilder reasons = new System.Text.StringBuilder();
            if (!String.IsNullOrEmpty(elevationMessage))
                reasons.AppendLine(elevationMessage);
            if (!String.IsNullOrEmpty(autostartStateMessage))
                reasons.AppendLine(autostartStateMessage);
            else if (!String.IsNullOrEmpty(autostartMessage))
                reasons.AppendLine(autostartMessage);
            foreach (PowerSample sample in new PowerSample[] { snapshot.BatteryTerminal, snapshot.CpuPackage, snapshot.Platform })
            {
                if (sample != null && !sample.Available)
                    reasons.AppendLine(PowerSample.LabelFor(sample.Boundary) + "：" + sample.UnavailableReason);
            }
            PowerSample selected = PowerDisplay.Select(snapshot, displayMode);
            if (!selected.Available && selected.Boundary != PowerBoundary.BatteryTerminal
                && (snapshot.Platform.Available || selected.UnavailableReason != snapshot.Platform.UnavailableReason))
                reasons.AppendLine(PowerSample.LabelFor(selected.Boundary) + "：" + selected.UnavailableReason);
            errorLabel.Text = reasons.ToString().TrimEnd();
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
                target.ForeColor = Color.FromArgb(100, 116, 139);
                sourceTip.SetToolTip(
                    target,
                    sample == null ? "无数据" : sample.UnavailableReason);
                return;
            }

            string text = sample.Watts.ToString("0.00", CultureInfo.InvariantCulture) + " W";
            if (sample.Kind == MeasurementKind.Estimated)
                text = "≈ " + text;

            target.Text = text;
            target.ForeColor = sample.Kind == MeasurementKind.Estimated
                ? Color.FromArgb(148, 163, 184)
                : Color.FromArgb(203, 213, 225);
            sourceTip.SetToolTip(target, sample.Source);
        }

        private void ShowSourceError(Label target, string message)
        {
            if (target == null)
                return;

            target.Text = "N/A";
            target.ForeColor = Color.FromArgb(100, 116, 139);
            sourceTip.SetToolTip(target, message);
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
