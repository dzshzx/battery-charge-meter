using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// The secondary power-source rows. These live beside the headline battery
    /// figure rather than replacing it, because each is measured across a
    /// different boundary and the values are not interchangeable.
    /// </summary>
    internal sealed partial class MainForm
    {
        private const int SourceRowHeight = 20;
        private const int FirstSourceRowY = 334;
        private const int SourceRowSpacing = 22;

        private readonly PowerSources powerSources = new PowerSources();
        private readonly ToolTip sourceTip = new ToolTip();

        private Label cpuPackageValue;
        private Label platformValue;
        private Label estimatedInputValue;

        /// <summary>
        /// Builds the source rows. Called from the constructor before
        /// <see cref="DpiLayout"/> captures the design bounds, so these controls
        /// scale with everything else.
        /// </summary>
        private void BuildPowerSourceRows()
        {
            Label sectionTitle = NewLabel("POWER SOURCES", 20, 312, 200, 18, 8.5f, FontStyle.Bold);
            sectionTitle.ForeColor = Color.FromArgb(100, 116, 139);
            Controls.Add(sectionTitle);

            cpuPackageValue = AddSourceRow(0, PowerSample.LabelFor(PowerBoundary.CpuPackage));
            platformValue = AddSourceRow(1, PowerSample.LabelFor(PowerBoundary.Platform));
            estimatedInputValue = AddSourceRow(2, PowerSample.LabelFor(PowerBoundary.EstimatedSystemInput));

            sourceTip.SetToolTip(estimatedInputValue, "平台功率 + 电池端充电功率，不含充电路径损耗");
        }

        private Label AddSourceRow(int index, string caption)
        {
            int y = FirstSourceRowY + (index * SourceRowSpacing);

            Label name = NewLabel(caption, 20, y, 190, SourceRowHeight, 9.5f, FontStyle.Regular);
            name.ForeColor = Color.FromArgb(148, 163, 184);
            Controls.Add(name);

            Label value = NewLabel("--", 210, y, 200, SourceRowHeight, 9.5f, FontStyle.Bold);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(value);

            return value;
        }

        private void UpdatePowerSources(BatteryReading reading)
        {
            PowerSnapshot snapshot;
            try
            {
                snapshot = powerSources.Read(reading);
            }
            catch (Exception error)
            {
                ShowSourceError(cpuPackageValue, error.Message);
                ShowSourceError(platformValue, error.Message);
                ShowSourceError(estimatedInputValue, error.Message);
                return;
            }

            Apply(cpuPackageValue, snapshot.CpuPackage);
            Apply(platformValue, snapshot.Platform);
            Apply(estimatedInputValue, snapshot.EstimatedSystemInput);
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
