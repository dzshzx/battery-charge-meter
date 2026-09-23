using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed partial class MainForm
    {
        private readonly ContextMenuStrip languageMenu = new ContextMenuStrip();
        private LinkLabel languageLink;

        private ToolStripMenuItem LocalizedMenuItem(string key)
        {
            return new ToolStripMenuItem(Strings.Get(key)) { Tag = key };
        }

        private void BuildLanguageControls()
        {
            languageLink = new LinkLabel();
            languageLink.UseCompatibleTextRendering = false;
            languageLink.TextAlign = ContentAlignment.MiddleLeft;
            languageLink.Text = Strings.IsChinese ? "EN" : "中文";
            languageLink.Bounds = new Rectangle(270, 44, 42, 18);
            languageLink.Font = new Font(UiTheme.TextFont, 8f, FontStyle.Regular, GraphicsUnit.Point);
            languageLink.LinkColor = UiTheme.Muted;
            languageLink.ActiveLinkColor = UiTheme.Ink;
            languageLink.AccessibleName = "语言 / Language";
            sourceTip.SetToolTip(languageLink, "语言 / Language");
            languageLink.LinkClicked += delegate { languageMenu.Show(languageLink, new Point(0, languageLink.Height)); };
            footerBand.Controls.Add(languageLink);
            ToolStripMenuItem trayLanguage = new ToolStripMenuItem("语言 / Language");
            foreach (string code in new string[] { "system", "zh-CN", "en" })
            {
                string selected = code;
                string name = code == "system" ? Strings.Get("跟随系统") : code == "zh-CN" ? "简体中文" : "English";
                ToolStripMenuItem windowItem = new ToolStripMenuItem(name) { Tag = code };
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
                windowItem.Click += select;
                trayItem.Click += select;
                languageMenu.Items.Add(windowItem);
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
            TranslateMenu(languageMenu.Items);
            languageLink.Text = Strings.IsChinese ? "EN" : "中文";
            modeSegments.AccessibleName = Strings.Get("功率显示口径");
            modeSegments.Invalidate();
            RefreshAutostart();
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
