using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Source details render the same view as the selected headline, as three
    /// supporting rows; the selected fourth value is the headline.
    /// </summary>
    internal sealed partial class MainForm
    {
        private readonly PowerSources powerSources = new PowerSources();
        private readonly ToolTip sourceTip = new ToolTip();

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
            AddSourceRow(MeterView.BatteryRow, PowerSample.LabelFor(PowerBoundary.BatteryTerminal));
            AddSourceRow(MeterView.CpuPackageRow, PowerSample.LabelFor(PowerBoundary.CpuPackage));
            AddSourceRow(MeterView.PlatformRow, PowerSample.LabelFor(PowerBoundary.Platform));

            // The last row answers the whole-machine question, which is a
            // different boundary on battery than on external power, so its
            // caption is rewritten each tick from the view.
            AddSourceRow(MeterView.WholeSystemRow, PowerSample.LabelFor(PowerBoundary.EstimatedSystemInput));
        }

        private void AddSourceRow(int index, string caption)
        {
            int y = 44 + index * 22;

            Label nameLabel = NewLabel(caption, 14, y, 184, 22, 9f, FontStyle.Regular);
            nameLabel.ForeColor = UiTheme.Muted;
            sourcesPanel.Controls.Add(nameLabel);

            Label value = NewLabel("--", 210, y, 120, 22, 9.5f, FontStyle.Regular);
            value.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            value.TextAlign = ContentAlignment.MiddleRight;
            value.ForeColor = UiTheme.Ink;
            sourcesPanel.Controls.Add(value);
            sourceNames[index] = nameLabel;
            sourceValues[index] = value;
        }

        private void LayoutSourceRows()
        {
            if (sourceNames[3] == null) return;
            float scale = dpiLayout != null && dpiLayout.CurrentDpi > 0 ? dpiLayout.CurrentDpi / 96f : 1f;
            int selected = session.Mode == DisplayMode.Battery ? MeterView.BatteryRow : MeterView.WholeSystemRow;
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

        private void UpdatePowerSources(MeterView view)
        {
            for (int index = 0; index < sourceValues.Length; index++)
                Apply(sourceValues[index], view.Rows[index], view.Accent);
            sourceNames[MeterView.WholeSystemRow].Text = view.WholeCaption;
            sourceTip.SetToolTip(sourceNames[MeterView.WholeSystemRow], view.WholeCaptionTooltip);
            UpdateDiagnostics(view);
            LayoutSourceRows();
        }

        private void RefreshDiagnostics()
        {
            UpdateDiagnostics(lastView ?? session.Render());
        }

        // Elevation and startup notices lead the measurement reasons.
        private void UpdateDiagnostics(MeterView view)
        {
            System.Text.StringBuilder reasons = new System.Text.StringBuilder();
            if (!String.IsNullOrEmpty(elevationMessage))
                reasons.AppendLine(Strings.Diagnostic(elevationMessage));
            if (!String.IsNullOrEmpty(autostartStateMessage))
                reasons.AppendLine(Strings.Diagnostic(autostartStateMessage));
            else if (!String.IsNullOrEmpty(autostartMessage))
                reasons.AppendLine(Strings.Diagnostic(autostartMessage));
            foreach (string line in view.Diagnostics)
                reasons.AppendLine(line);
            errorLabel.Text = reasons.ToString().TrimEnd();
        }

        private void Apply(Label target, ReadoutText row, BatteryAccentKind accent)
        {
            target.Text = row.Text;
            target.ForeColor = UiTheme.Tone(row.Tone, accent);
            sourceTip.SetToolTip(target, row.Tooltip);
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
