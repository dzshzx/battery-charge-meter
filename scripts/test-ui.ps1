[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\dist\PowerMeter.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\ui-preview')
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -File $PSCommandPath `
        -Executable $Executable -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "UI verification failed: $LASTEXITCODE" }
    return
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @'
using System;
using System.Drawing;
using System.Windows.Forms;
public static class FooterTextProbe {
    public static void AssertReadoutBaseline(Control control) {
        string text = control.Text;
        Color color = control.ForeColor;
        try {
            control.Text = "88.88 W";
            control.ForeColor = Color.Black;
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height)) {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                int[] bottoms = new int[bitmap.Width];
                for (int x = 0; x < bitmap.Width; x++) {
                    bottoms[x] = -1;
                    for (int y = 0; y < bitmap.Height; y++) {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.R < 128 && pixel.G < 128 && pixel.B < 128) bottoms[x] = y;
                    }
                }
                int right = bitmap.Width - 1;
                while (right >= 0 && bottoms[right] < 0) right--;
                int unitLeft = right;
                while (unitLeft >= 0 && bottoms[unitLeft] >= 0) unitLeft--;
                int unitBottom = -1, numberBottom = -1;
                for (int x = unitLeft + 1; x <= right; x++) unitBottom = Math.Max(unitBottom, bottoms[x]);
                for (int x = 0; x < unitLeft; x++) numberBottom = Math.Max(numberBottom, bottoms[x]);
                int tolerance = Math.Max(2, (int)Math.Ceiling(1.5 * control.Font.SizeInPoints / 44));
                if (numberBottom < 0 || unitBottom < 0 || Math.Abs(numberBottom - unitBottom) > tolerance)
                    throw new InvalidOperationException("Readout/unit baseline mismatch: " + numberBottom + " / " + unitBottom);
            }
        } finally { control.Text = text; control.ForeColor = color; }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static void AssertHover(Control control) {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var leave = control.GetType().GetMethod("OnMouseLeave", flags);
        var move = control.GetType().GetMethod("OnMouseMove", flags);
        var selected = control.GetType().GetProperty("SelectedIndex", flags);
        object selection = selected.GetValue(control, null);
        leave.Invoke(control, new object[] { EventArgs.Empty });
        using (Bitmap before = new Bitmap(control.Width, control.Height))
        using (Bitmap hover = new Bitmap(control.Width, control.Height))
        using (Bitmap after = new Bitmap(control.Width, control.Height)) {
            Rectangle bounds = new Rectangle(Point.Empty, before.Size);
            control.DrawToBitmap(before, bounds);
            move.Invoke(control, new object[] { new MouseEventArgs(MouseButtons.None, 0, control.Width * 3 / 4, control.Height / 2, 0) });
            control.DrawToBitmap(hover, bounds);
            leave.Invoke(control, new object[] { EventArgs.Empty });
            control.DrawToBitmap(after, bounds);
            int changes = 0;
            for (int y = 0; y < control.Height; y++)
                for (int x = 0; x < control.Width; x++) {
                    if (before.GetPixel(x, y) != hover.GetPixel(x, y)) changes++;
                    if (before.GetPixel(x, y) != after.GetPixel(x, y))
                        throw new InvalidOperationException("Segment hover did not clear on mouse leave.");
                }
            if (changes == 0 || !Object.Equals(selection, selected.GetValue(control, null)))
                throw new InvalidOperationException("Segment hover must render feedback without changing the selection.");
        }
    }
    public static void AssertIcon(Control control) {
        int ink = 0;
        using (Bitmap bitmap = new Bitmap(control.Width, control.Height)) {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            for (int y = control.Height / 4; y < control.Height * 3 / 4; y++)
                for (int x = control.Width / 4; x < control.Width * 3 / 4; x++) {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R < 96 && pixel.G < 96 && pixel.B < 96) ink++;
                }
        }
        if (ink < 4) throw new InvalidOperationException("Pin icon did not render.");
    }
    public static void Click(Control control) { ((IButtonControl)control).PerformClick(); }
    public static void Capture(Control control, string path) {
        using (Bitmap bitmap = new Bitmap(control.Width, control.Height)) {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    public static void AssertLayout(Control parent) {
        foreach (Control control in parent.Controls) {
            if (!control.Visible) continue;
            if (control.GetType().Name == "AlignedLabel") {
                Size required = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (required.Width > control.Width || required.Height > control.Height)
                    throw new InvalidOperationException("Clipped label: " + control.Text + "; required=" + required + "; actual=" + control.Size);
            }
            foreach (Control other in parent.Controls)
                if (other.Visible && other != control && control.Bounds.IntersectsWith(other.Bounds))
                    throw new InvalidOperationException("Overlapping controls: " + control.Text + " / " + other.Text);
            if (control.HasChildren) AssertLayout(control);
        }
    }
    public static int InkTop(Control control) {
        string text = control.Text;
        Color back = control.BackColor, fore = control.ForeColor;
        LinkLabel link = control as LinkLabel;
        Color linkColor = link == null ? Color.Empty : link.LinkColor;
        LinkBehavior behavior = link == null ? LinkBehavior.SystemDefault : link.LinkBehavior;
        try {
            control.Text = "Ag09";
            control.BackColor = Color.White;
            control.ForeColor = Color.Black;
            if (link != null) {
                link.LinkColor = Color.Black;
                link.LinkBehavior = LinkBehavior.NeverUnderline;
            }
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height)) {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++) {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.R < 96 && pixel.G < 96 && pixel.B < 96) return control.Top + y;
                    }
            }
            throw new InvalidOperationException("Footer text did not render.");
        } finally {
            control.Text = text;
            control.BackColor = back;
            control.ForeColor = fore;
            if (link != null) {
                link.LinkColor = linkColor;
                link.LinkBehavior = behavior;
            }
        }
    }
}
'@
$originalDpiContext = [FooterTextProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($originalDpiContext -eq [IntPtr]::Zero) { throw 'Unable to match the application Per-Monitor V2 DPI context.' }
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assembly = [Reflection.Assembly]::LoadFile($Executable)
$assembly.GetType('BatteryChargeMeter.EmbeddedUi').GetMethod('Initialize', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @())
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-Internal([string]$Name) {
    [Activator]::CreateInstance($assembly.GetType("BatteryChargeMeter.$Name"), $true)
}
function Get-Field($Target, [string]$Name) {
    $Target.GetType().GetField($Name, $flags).GetValue($Target)
}
function Set-Field($Target, [string]$Name, $Value) {
    $Target.GetType().GetField($Name, $flags).SetValue($Target, $Value)
}
function Invoke-Internal($Target, [string]$Name, [object[]]$Arguments = @()) {
    $plain = New-Object object[] $Arguments.Count
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $plain[$index] = $Arguments[$index].PSObject.BaseObject
    }
    $Target.GetType().GetMethod($Name, $flags).Invoke($Target, $plain)
}
function New-Sample([string]$Boundary, [double]$Watts) {
    $sample = New-Internal 'PowerSample'
    $sample.Available = $true
    $sample.Boundary = $Boundary
    $sample.Kind = if ($Boundary -eq 'EstimatedSystemInput') { 'Estimated' } else { 'Measured' }
    $sample.Watts = $Watts
    $sample.Source = 'UI acceptance fixture'
    return $sample
}
function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function Assert-Layout($Parent) {
    [FooterTextProbe]::AssertLayout($Parent)
}

$form = [Activator]::CreateInstance($assembly.GetType('BatteryChargeMeter.MainForm'), @($false))
try {
    $form.Icon = [Drawing.Icon]::ExtractAssociatedIcon($Executable)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    (Get-Field $form 'timer').Stop()
    Set-Field $form 'elevationMessage' $null
    Set-Field $form 'autostartMessage' $null
    Set-Field $form 'autostartStateMessage' $null
    $modeType = $assembly.GetType('BatteryChargeMeter.DisplayMode')
    $stringsType = $assembly.GetType('BatteryChargeMeter.Strings')
    foreach ($language in @('zh-CN', 'en', 'zh-CN')) {
    $stringsType.GetMethod('Select', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @($language, $false))
    Invoke-Internal $form 'ApplyLanguage'
    Assert-True ($form.Text -eq $(if ($language -eq 'en') { 'Power Meter' } else { '功率计' })) 'Incorrect localized application name'
    foreach ($dpi in @(96, 144, 168, 192, 288, 96)) {
        Invoke-Internal $form 'SendDpiTransition' @([int]$dpi)
        foreach ($scenario in @('idle', 'charging', 'discharging', 'supplemented', 'unavailable', 'starting', 'warming')) {
            $battery = New-Internal 'BatteryReading'
            $battery.SupplyState = switch ($scenario) {
                'charging' { 'ExternalPowerCharging' }
                'discharging' { 'BatteryDischarging' }
                'supplemented' { 'ExternalPowerSupplemented' }
                default { 'ExternalPowerIdle' }
            }
            $battery.PowerWatts = switch ($scenario) {
                'charging' { 39.08 }
                'discharging' { -17.50 }
                'supplemented' { -5.0 }
                default { 0.0 }
            }
            $battery.RateAvailable = $true
            $battery.VoltageAvailable = $true
            $battery.CurrentAvailable = $true
            $battery.VoltageVolts = 17.51
            $battery.CurrentAmps = $battery.PowerWatts / 17.51
            $battery.Percentage = 98
            $snapshot = New-Internal 'PowerSnapshot'
            $snapshot.Battery = $battery
            $snapshot.Timestamp = [DateTimeOffset]::Parse('2026-09-23T14:38:15+08:00')
            $snapshot.BatteryTerminal = New-Sample 'BatteryTerminal' $battery.PowerWatts
            $snapshot.CpuPackage = New-Sample 'CpuPackage' 10.1
            $mode = if ($scenario -eq 'discharging') { 'Battery' } else { 'WholeSystem' }
            Set-Field $form 'displayMode' ([Enum]::Parse($modeType, $mode))
            $segments = Get-Field $form 'modeSegments'
            $segments.GetType().GetProperty('SelectedIndex', $flags).SetValue($segments, [int]($mode -eq 'Battery'), $null)
            Invoke-Internal (Get-Field $form 'history') 'Clear'
            $lastTick = if ($scenario -eq 'starting') { 0 } elseif ($scenario -eq 'warming') { 3 } else { 60 }
            for ($tick = 0; $tick -le $lastTick; $tick++) {
                $snapshot.ElapsedSeconds = $tick
                $platform = 19.39 + [Math]::Sin($tick * 0.43) * 2.4
                if ($tick -eq 60) { $platform = 19.39 }
                $snapshot.Platform = New-Sample 'Platform' $platform
                $snapshot.WholeSystem = New-Sample 'EstimatedSystemInput' ($platform + $battery.PowerWatts)
                if ($scenario -eq 'discharging') {
                    $snapshot.WholeSystem = New-Sample 'SystemLoad' 17.5
                }
                if ($scenario -eq 'unavailable') {
                    $snapshot.Platform.Available = $false
                    $snapshot.WholeSystem.Available = $false
                    $reason = '需要以管理员身份运行才能读取平台功率'
                    $snapshot.Platform.UnavailableReason = $reason
                    $snapshot.WholeSystem.UnavailableReason = $reason
                }
                Set-Field $form 'latest' $snapshot
                Invoke-Internal $form 'PresentSnapshot' @($snapshot, $true)
            }
            [Windows.Forms.Application]::DoEvents()
            $scale = $dpi / 96.0
            $diagnostics = Get-Field $form 'errorLabel'
            $expectedHeight = if ($scenario -eq 'unavailable') { 528 } else { 468 }
            Assert-True ($form.AutoScrollMinSize.Height -eq [Math]::Round($expectedHeight * $scale)) `
                'Diagnostic expansion/collapse did not resize the content'
            Assert-True ($diagnostics.Visible -eq ($scenario -eq 'unavailable')) 'Unexpected diagnostic visibility'
            Assert-Layout $form
            $sourceValues = Get-Field $form 'sourceValues'
            Assert-True (@($sourceValues | Where-Object Visible).Count -eq 3) 'Only the three supporting metrics should repeat below the headline'
            $headlineIndex = if ($mode -eq 'Battery') { 0 } else { 3 }
            Assert-True (-not $sourceValues[$headlineIndex].Visible) 'Headline metric was duplicated in the detail group'
            if ($scenario -eq 'idle') {
                [FooterTextProbe]::AssertReadoutBaseline((Get-Field $form 'powerLabel'))
                $expectedAverage = if ($language -eq 'en') { '30s average' } else { '30 秒均值' }
                $expectedPeak = if ($language -eq 'en') { '60s peak' } else { '60 秒峰值' }
                Assert-True ((Get-Field $form 'statisticsCaption').Text -eq $expectedAverage) 'Full average window should have a concise caption'
                Assert-True ((Get-Field $form 'historyCaption').Text -eq $expectedPeak) 'Full peak window should have a concise caption'
                [FooterTextProbe]::AssertHover((Get-Field $form 'modeSegments'))
                $inkTops = @(@((Get-Field $form 'statisticsLabel'), (Get-Field $form 'historyLabel')) | ForEach-Object { [FooterTextProbe]::InkTop($_) })
                $range = $inkTops | Measure-Object -Minimum -Maximum
                Assert-True ($inkTops.Count -eq 2 -and $range.Maximum - $range.Minimum -le 1) `
                    "Statistic baselines differ at $dpi DPI ($language): $($inkTops -join ', ')"
            }
            if ($scenario -eq 'warming') {
                Assert-True ((Get-Field $form 'statisticsCaption').Text.Contains('3/30s')) 'Partial average coverage must remain visible'
                Assert-True ((Get-Field $form 'historyCaption').Text.Contains('3/60s')) 'Partial peak duration must remain visible'
            }
            if ($language -eq 'en') {
                Assert-True ($diagnostics.Text -notmatch '[\u4e00-\u9fff]') 'English diagnostics contain untranslated application text'
            }
            $path = Join-Path $OutputDirectory "$language-$scenario-$dpi.png"
            Invoke-Internal $form 'CapturePreview' @($path, "Fixture=$scenario; Dpi=$dpi")
            if ($scenario -eq 'idle') {
                $pin = Get-Field $form 'pinButton'
                [FooterTextProbe]::Click($pin)
                Assert-True (-not $form.TopMost) 'Pin action did not clear TopMost'
                [FooterTextProbe]::AssertIcon($pin)
                [FooterTextProbe]::Click($pin)
                Assert-True $form.TopMost 'Pin action did not restore TopMost'
                [FooterTextProbe]::AssertIcon($pin)
                [FooterTextProbe]::Click((Get-Field $form 'settingsButton'))
                [Windows.Forms.Application]::DoEvents()
                $settings = Get-Field $form 'settingsContent'
                Assert-True ($null -ne $settings -and -not $settings.IsDisposed) 'Settings did not open'
                Assert-True ($settings.Width -eq [Math]::Round(316 * $scale)) 'Settings content was scaled twice'
                Assert-Layout $settings
                [FooterTextProbe]::Capture($settings, (Join-Path $OutputDirectory "$language-settings-$dpi.png"))
                Invoke-Internal $form 'SendDpiTransition' @([int]$dpi)
                Assert-True ($null -eq (Get-Field $form 'settingsContent')) 'DPI transition did not close settings before rescaling fonts'
            }
        }
        Write-Host "PASS $language at $dpi DPI: seven reading scenarios, text fit, hover, geometry, diagnostics and captures"
    }
    }
}
finally {
    $form.Close()
    $form.Dispose()
    [FooterTextProbe]::SetThreadDpiAwarenessContext($originalDpiContext) | Out-Null
}
