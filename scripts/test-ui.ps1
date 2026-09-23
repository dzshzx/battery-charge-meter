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
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::ThrowException)
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @'
using System;
using System.Drawing;
using System.Windows.Forms;
public static class FooterTextProbe {
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
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$assembly = [Reflection.Assembly]::LoadFile($Executable)
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
    $visible = @($Parent.Controls | Where-Object Visible)
    foreach ($control in $visible) {
        if ($control.GetType().Name -eq 'AlignedLabel') {
            $required = [Windows.Forms.TextRenderer]::MeasureText($control.Text, $control.Font,
                [Drawing.Size]::Empty, [Windows.Forms.TextFormatFlags]'NoPadding,SingleLine,NoPrefix')
            Assert-True ($required.Width -le $control.Width -and $required.Height -le $control.Height) `
                "Clipped label '$($control.Text)': needs $required, has $($control.Size)"
        }
        foreach ($other in $visible) {
            if ($control -ne $other) {
                Assert-True (-not $control.Bounds.IntersectsWith($other.Bounds)) `
                    "Overlapping controls '$($control.Text)' and '$($other.Text)'"
            }
        }
        if ($control.HasChildren) { Assert-Layout $control }
    }
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
        foreach ($scenario in @('idle', 'charging', 'discharging', 'supplemented', 'unavailable')) {
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
            for ($tick = 0; $tick -le 60; $tick++) {
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
            $expectedHeight = if ($scenario -eq 'unavailable') { 508 } else { 448 }
            Assert-True ($form.AutoScrollMinSize.Height -eq [Math]::Round($expectedHeight * $scale)) `
                'Diagnostic expansion/collapse did not resize the content'
            Assert-True ($diagnostics.Visible -eq ($scenario -eq 'unavailable')) 'Unexpected diagnostic visibility'
            Assert-Layout $form
            if ($scenario -eq 'idle') {
                $footer = Get-Field $form 'footerBand'
                $timestamp = Get-Field $form 'updatedLabel'
                $inkTops = @($footer.Controls | Where-Object {
                    $_.Visible -and $_.Top -eq $timestamp.Top -and $_ -is [Windows.Forms.Label]
                } | ForEach-Object { [FooterTextProbe]::InkTop($_) })
                $range = $inkTops | Measure-Object -Minimum -Maximum
                Assert-True ($inkTops.Count -ge 3 -and $range.Maximum - $range.Minimum -le 1) `
                    "Footer text baselines differ at $dpi DPI ($language): $($inkTops -join ', ')"
            }
            if ($language -eq 'en') {
                Assert-True ($diagnostics.Text -notmatch '[\u4e00-\u9fff]') 'English diagnostics contain untranslated application text'
            }
            $path = Join-Path $OutputDirectory "$language-$scenario-$dpi.png"
            Invoke-Internal $form 'CapturePreview' @($path, "Fixture=$scenario; Dpi=$dpi")
        }
        Write-Host "PASS $language at $dpi DPI: five supply/availability scenarios, text fit, geometry, diagnostics and captures"
    }
    }
}
finally {
    $form.Close()
    $form.Dispose()
}
