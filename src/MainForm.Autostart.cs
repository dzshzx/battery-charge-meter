using System;
using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed partial class MainForm
    {
        private AntdUI.Checkbox autostartCheckBox;
        private ToolStripMenuItem autostartMenuItem;
        private bool updatingAutostart;
        private string autostartMessage;
        private string autostartStateMessage;
        private bool startHidden;
        private bool monitoringStarted;

        private void BuildAutostartControls()
        {
            autostartMenuItem = LocalizedMenuItem("开机自启（登录后）");
            autostartMenuItem.Click += delegate { ChangeAutostart(!autostartMenuItem.Checked); };
            trayMenu.Items.Insert(3, autostartMenuItem);
            trayMenu.Opening += delegate { RefreshAutostart(); };
            Activated += delegate { RefreshAutostart(); };
            RefreshAutostart();
        }

        private void RefreshAutostart()
        {
            bool enabled = false;
            bool known = true;
            autostartStateMessage = null;
            try
            {
                using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                {
                    AutostartState state = manager.Read();
                    enabled = state.ThisCopy && state.Enabled;
                    if (state.ThisCopy && !String.IsNullOrEmpty(state.RepairReason))
                        autostartStateMessage = state.RepairReason;
                    if (autostartCheckBox != null && !autostartCheckBox.IsDisposed)
                        sourceTip.SetToolTip(autostartCheckBox, state.Exists && !state.ThisCopy
                        ? Strings.Format("当前自启指向：{0}；勾选可确认更换。", state.Executable)
                        : Strings.Get("以当前 Windows 用户登录后，从仅管理员可写的受保护副本以管理员权限运行并留在托盘。移动程序后需重新启用。"));
                }
            }
            catch (Exception error)
            {
                known = false;
                autostartStateMessage = "自启状态读取失败：" + ErrorMessage(error);
            }
            updatingAutostart = true;
            if (autostartCheckBox != null && !autostartCheckBox.IsDisposed)
                autostartCheckBox.CheckState = known
                    ? (enabled ? CheckState.Checked : CheckState.Unchecked)
                    : CheckState.Indeterminate;
            autostartMenuItem.Checked = enabled;
            autostartMenuItem.ToolTipText = known ? "" : Strings.Get("状态读取失败，请查看主窗口中的原因。");
            updatingAutostart = false;
            if (latest != null)
                UpdatePowerSources(latest);
        }

        private void ChangeAutostart(bool enable)
        {
            try
            {
                using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                {
                    if (enable)
                    {
                        if (!Startup.IsElevated())
                            throw new InvalidOperationException("请先点击下方“以管理员身份重新启动”，再勾选开机自启。");
                        AutostartState state = manager.Read();
                        bool replace = state.Exists && !state.ThisCopy;
                        if (replace && MessageBox.Show(this,
                            Strings.Format("自启当前指向：\n{0}\n\n更换为：\n{1}？", state.Executable, manager.Identity),
                            Strings.Get("更换自启程序"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                            return;
                        manager.Enable(state);
                    }
                    else if (manager.Disable())
                    {
                        autostartMessage = "已关闭此程序的开机自启；受保护副本将在下次以管理员身份启动时删除。";
                        return;
                    }
                }
                autostartMessage = enable ? "已启用开机自启：登录后以管理员权限运行受保护副本，在托盘显示。" : "已关闭此程序的开机自启。";
            }
            catch (Exception error)
            {
                autostartMessage = "自启设置未完成：" + ErrorMessage(error);
            }
            finally
            {
                RefreshAutostart();
            }
        }

        private static string ErrorMessage(Exception error)
        {
            while (error.InnerException != null)
                error = error.InnerException;
            return error.Message;
        }

        private void StartMonitoring()
        {
            if (monitoringStarted)
                return;
            monitoringStarted = true;
            if (trayEnabled)
                trayIcon.Visible = true;
            RefreshReading();
            timer.Start();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && startHidden)
            {
                if (!IsHandleCreated)
                    CreateHandle();
                ShowInTaskbar = false;
                StartMonitoring();
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }
    }
}
