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
            AutostartStatus status;
            try
            {
                using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                    status = manager.Describe();
            }
            catch (Exception error)
            {
                status = AutostartPolicy.ReadFailed(ErrorMessage(error));
            }
            autostartStateMessage = status.Notice;
            bool known = status.Switch != AutostartSwitch.Unknown;
            bool enabled = status.Switch == AutostartSwitch.On;
            updatingAutostart = true;
            if (autostartCheckBox != null && !autostartCheckBox.IsDisposed)
            {
                if (status.Tooltip != null)
                    sourceTip.SetToolTip(autostartCheckBox, status.Tooltip);
                autostartCheckBox.CheckState = known
                    ? (enabled ? CheckState.Checked : CheckState.Unchecked)
                    : CheckState.Indeterminate;
            }
            autostartMenuItem.Checked = enabled;
            autostartMenuItem.ToolTipText = known ? "" : Strings.Get("状态读取失败，请查看主窗口中的原因。");
            updatingAutostart = false;
            RefreshDiagnostics();
        }

        private void ChangeAutostart(bool enable)
        {
            try
            {
                using (AutostartManager manager = AutostartManager.ForCurrentExecutable(Application.ExecutablePath))
                {
                    AutostartRemoval removal = AutostartRemoval.Removed;
                    if (enable)
                    {
                        // Refuse before asking about replacement.
                        if (!Startup.IsElevated())
                            throw new InvalidOperationException(AutostartPolicy.ElevationRequired);
                        AutostartState state = manager.Read();
                        bool replace = state.Exists && !state.ThisCopy;
                        if (replace && MessageBox.Show(this,
                            Strings.Format("自启当前指向：\n{0}\n\n更换为：\n{1}？", state.Executable, manager.Identity),
                            Strings.Get("更换自启程序"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                            return;
                        manager.Enable(state);
                    }
                    else
                    {
                        removal = manager.Disable();
                    }
                    autostartMessage = AutostartPolicy.Outcome(enable, removal);
                }
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
