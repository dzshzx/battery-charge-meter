using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed partial class MainForm
    {
        private ToolStripMenuItem LocalizedMenuItem(string key)
        {
            return new ToolStripMenuItem(Strings.Get(key)) { Tag = key };
        }

        private void BuildLanguageControls()
        {
            ToolStripMenuItem trayLanguage = new ToolStripMenuItem("语言 / Language");
            foreach (string code in new string[] { "system", "zh-CN", "en" })
            {
                string selected = code;
                string name = code == "system" ? Strings.Get("跟随系统") : code == "zh-CN" ? "简体中文" : "English";
                ToolStripMenuItem trayItem = new ToolStripMenuItem(name) { Tag = code };
                EventHandler select = delegate
                {
                    try
                    {
                        Strings.Select(selected, true);
                        ApplyLanguage();
                    }
                    catch (Exception error)
                    {
                        errorLabel.Text = error.Message;
                    }
                };
                trayItem.Click += select;
                trayLanguage.DropDownItems.Add(trayItem);
            }
            trayMenu.Items.Insert(trayMenu.Items.Count - 2, trayLanguage);
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            Text = Strings.AppName;
            TranslateControls(this);
            TranslateMenu(trayMenu.Items);
            pinButton.AccessibleName = Strings.Get("置顶");
            sourceTip.SetToolTip(pinButton, Strings.Get("置顶"));
            sourceTip.SetToolTip(updatedLabel, Strings.Get("更新时间"));
            modeSegments.AccessibleName = Strings.Get("功率显示口径");
            modeSegments.Invalidate();
            RefreshAutostart();
            UpdateStatistics();
            if (latest != null) PresentSnapshot(latest, false);
        }

        private static void TranslateControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                string key = control.Tag as string;
                if (key != null) control.Text = Strings.Get(key);
                if (control.HasChildren) TranslateControls(control);
            }
        }

        private static void TranslateMenu(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                ToolStripMenuItem menu = item as ToolStripMenuItem;
                if (menu == null) continue;
                string key = menu.Tag as string;
                if (key == "system" || key == "zh-CN" || key == "en")
                {
                    menu.Checked = Strings.Preference == key;
                    if (key == "system") menu.Text = Strings.Get("跟随系统");
                }
                else if (key != null) menu.Text = Strings.Get(key);
                TranslateMenu(menu.DropDownItems);
            }
        }
    }
}
