using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace BatteryChargeMeter
{
    internal sealed class GuiInstance : IDisposable
    {
        private Mutex mutex;
        private bool owned;

        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr changeInfo);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint processId);

        private static string InstanceKey(string path)
        {
            string sid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                sid = identity.User.Value;
            using (SHA256 hash = SHA256.Create())
                return sid + "." + BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
        }

        internal static uint ListenForRestore(IntPtr window, string path)
        {
            string name = "BatteryChargeMeter.Restore." + InstanceKey(path);
            uint message = RegisterWindowMessage(name);
            // Only this parameter-free restore message crosses integrity levels.
            // No task management, process launch, or other privileged action is exposed.
            if (message == 0 || !ChangeWindowMessageFilterEx(window, message, 1, IntPtr.Zero)
                || !SetProp(window, name, new IntPtr(1)))
                throw new InvalidOperationException("无法初始化窗口恢复通知。");
            return message;
        }

        internal static void StopListening(IntPtr window, string path)
        {
            RemoveProp(window, "BatteryChargeMeter.Restore." + InstanceKey(path));
        }

        internal static void RestoreExisting(string path)
        {
            string name = "BatteryChargeMeter.Restore." + InstanceKey(path);
            uint message = RegisterWindowMessage(name);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                bool sent = false;
                EnumWindows(delegate(IntPtr window, IntPtr ignored)
                {
                    if (GetProp(window, name) == IntPtr.Zero)
                        return true;
                    uint process;
                    GetWindowThreadProcessId(window, out process);
                    AllowSetForegroundWindow(process);
                    sent = PostMessage(window, message, IntPtr.Zero, IntPtr.Zero);
                    return !sent;
                }, IntPtr.Zero);
                if (sent)
                    return;
                Thread.Sleep(100);
            }
        }

        internal bool Acquire(string path, bool handoff)
        {
            SecurityIdentifier sid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                sid = identity.User;
            MutexSecurity security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
            bool created;
            try
            {
                mutex = new Mutex(false, @"Local\BatteryChargeMeter." + InstanceKey(path), out created, security);
            }
            catch (UnauthorizedAccessException)
            {
                // A higher-integrity instance can deny an ordinary launch a
                // writable handle. Its existing lock still prevents duplicates.
                if (!handoff)
                    return false;
                throw;
            }
            try
            {
                owned = mutex.WaitOne(handoff ? 15000 : 0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }
            return owned;
        }

        public void Dispose()
        {
            if (mutex == null)
                return;
            if (owned)
                mutex.ReleaseMutex();
            mutex.Dispose();
            mutex = null;
        }
    }
}
