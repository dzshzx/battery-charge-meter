using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed partial class MainForm
    {
        private AntdUI.Button pinButton;
        private AntdUI.Button settingsButton;
        private Form settingsPopup;
        private Panel settingsContent;

        private AntdUI.Button UtilityButton(string text, string icon)
        {
            AntdUI.Button button = new AntdUI.Button();
            button.Text = text;
            button.IconSvg = icon;
            button.Font = Font;
            button.ForeColor = UiTheme.Ink;
            button.BackColor = UiTheme.FooterBand;
            button.DefaultBack = UiTheme.FooterBand;
            button.BackHover = UiTheme.Track;
            button.BackActive = UiTheme.Chip;
            button.ForeHover = UiTheme.Ink;
            button.ForeActive = UiTheme.Ink;
            button.Radius = 6;
            button.WaveSize = 0;
            button.BorderWidth = 0;
            return button;
        }

        private void BuildSettingsControls()
        {
            AntdUI.Style.SetPrimary(UiTheme.Ink);
            AntdUI.Config.Animation = SystemInformation.IsMenuAnimationEnabled;
            pinButton = UtilityButton("", UiIcons.Pin);
            pinButton.ToggleIconSvg = UiIcons.Pin;
            pinButton.IconToggleAnimation = 0;
            pinButton.IconSize = new Size(16, 16);
            pinButton.Bounds = new Rectangle(232, 6, 36, 32);
            pinButton.AutoToggle = true;
            pinButton.Toggle = TopMost;
            pinButton.ToggleBack = UiTheme.Chip;
            pinButton.ToggleFore = UiTheme.Ink;
            pinButton.DefaultBack = UiTheme.Chip;
            pinButton.ToggleChanged += delegate
            {
                TopMost = pinButton.Toggle;
                pinButton.DefaultBack = TopMost ? UiTheme.Chip : UiTheme.FooterBand;
            };
            pinButton.AccessibleName = Strings.Get("置顶");
            sourceTip.SetToolTip(pinButton, Strings.Get("置顶"));
            footerBand.Controls.Add(pinButton);

            settingsButton = UtilityButton(Strings.Get("设置"), UiIcons.Settings);
            settingsButton.IconSize = new Size(16, 16);
            settingsButton.Tag = "设置";
            settingsButton.Bounds = new Rectangle(280, 6, 84, 32);
            settingsButton.Click += delegate { OpenSettings(); };
            footerBand.Controls.Add(settingsButton);
        }

        private void CloseSettings()
        {
            Form popup = settingsPopup;
            settingsPopup = null;
            if (popup != null && !popup.IsDisposed) { popup.Close(); popup.Dispose(); }
            autostartCheckBox = null;
        }

        private Rectangle SettingsBounds(int x, int y, int width, int height)
        {
            float scale = dpiLayout.CurrentDpi / 96f;
            return new Rectangle(DpiLayout.ScaleValue(x, scale), DpiLayout.ScaleValue(y, scale),
                DpiLayout.ScaleValue(width, scale), DpiLayout.ScaleValue(height, scale));
        }

        private void OpenSettings()
        {
            if (settingsPopup != null && !settingsPopup.IsDisposed) { CloseSettings(); return; }
            bool elevated = Startup.IsElevated();
            Panel content = new Panel();
            settingsContent = content;
            content.Size = SettingsBounds(0, 0, 316, elevated ? 134 : 174).Size;
            content.BackColor = UiTheme.Surface;
            content.Font = Font;

            autostartCheckBox = new AntdUI.Checkbox();
            autostartCheckBox.Text = Strings.Get("开机自启");
            autostartCheckBox.Font = Font;
            autostartCheckBox.Bounds = SettingsBounds(0, 0, 316, 32);
            autostartCheckBox.Fill = UiTheme.Ink;
            autostartCheckBox.ForeColor = UiTheme.Ink;
            autostartCheckBox.CheckedChanged += delegate
            {
                if (!updatingAutostart) ChangeAutostart(autostartCheckBox.Checked);
            };
            content.Controls.Add(autostartCheckBox);
            RefreshAutostart();

            AlignedLabel languageLabel = new AlignedLabel();
            languageLabel.Text = Strings.Get("语言");
            languageLabel.Font = Font;
            languageLabel.ForeColor = UiTheme.Ink;
            languageLabel.Bounds = SettingsBounds(0, 44, 96, 32);
            content.Controls.Add(languageLabel);
            AntdUI.Select language = new AntdUI.Select();
            language.Name = "Language";
            language.Font = Font;
            language.List = true;
            language.Bounds = SettingsBounds(120, 44, 196, 32);
            language.Items.AddRange(new object[] { Strings.Get("跟随系统"), "简体中文", "English" });
            language.SelectedIndex = Strings.Preference == "zh-CN" ? 1 : Strings.Preference == "en" ? 2 : 0;
            language.SelectedIndexChanged += delegate
            {
                try
                {
                    Strings.Select(language.SelectedIndex == 1 ? "zh-CN" : language.SelectedIndex == 2 ? "en" : "system", true);
                    ApplyLanguage();
                    BeginInvoke(new MethodInvoker(CloseSettings));
                }
                catch (Exception error) { errorLabel.Text = error.Message; }
            };
            content.Controls.Add(language);

            Panel rule = new Panel();
            rule.BackColor = UiTheme.Hairline;
            rule.Bounds = SettingsBounds(0, 90, 316, 1);
            content.Controls.Add(rule);
            AlignedLabel status = new AlignedLabel();
            status.Text = Strings.Format("当前权限：{0}", Strings.Get(elevated ? "管理员模式" : "普通模式"));
            status.Font = Font;
            status.ForeColor = UiTheme.Muted;
            status.Bounds = SettingsBounds(0, 104, 316, 24);
            content.Controls.Add(status);

            if (!elevated)
            {
                AntdUI.Button retry = UtilityButton(Strings.Get("以管理员身份重新启动"), null);
                retry.Font = Font;
                retry.BackColor = UiTheme.Surface;
                retry.DefaultBack = UiTheme.Surface;
                retry.BorderWidth = 1;
                retry.DefaultBorderColor = UiTheme.FooterRule;
                retry.Bounds = SettingsBounds(0, 138, 316, 32);
                retry.Click += delegate
                {
                    retry.Enabled = false;
                    ElevationResult result = Startup.TryElevate();
                    if (result.Started) Close();
                    else
                    {
                        elevationMessage = result.Message;
                        RefreshDiagnostics();
                        retry.Enabled = true;
                    }
                };
                content.Controls.Add(retry);
            }

            AntdUI.Popover.Config config = new AntdUI.Popover.Config(settingsButton, content);
            config.Font = Font;
            config.Back = UiTheme.Surface;
            config.Fore = UiTheme.Ink;
            config.Radius = 8;
            config.ArrowSize = 0;
            config.ArrowAlign = AntdUI.TAlign.TR;
            // Bounds and fonts above already use this window's physical DPI.
            config.Dpi = 1f;
            config.OnClosed = delegate { autostartCheckBox = null; settingsPopup = null; settingsContent = null; };
            settingsPopup = AntdUI.Popover.open(config);
        }
    }

    // Unmodified Lucide pin and settings-2 geometry; licenses are embedded.
    internal static class UiIcons
    {
        private const string Start = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><g fill=\"none\" stroke=\"#1b1b1b\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";
        internal const string Pin = Start + "<path d=\"M12 17v5\"/><path d=\"M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z\"/></g></svg>";
        internal const string Settings = Start + "<path d=\"M14 17H5\"/><path d=\"M19 7h-9\"/><circle cx=\"17\" cy=\"17\" r=\"3\"/><circle cx=\"7\" cy=\"7\" r=\"3\"/></g></svg>";
    }
}
