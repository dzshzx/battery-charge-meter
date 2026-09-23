using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;
using System.Xml;

namespace BatteryChargeMeter
{
    internal sealed class AutostartState
    {
        public bool Exists;
        public bool Enabled;
        public bool ThisCopy;
        public string Executable;
        public string Registration;
        public string RepairReason;
    }

    // Use the Windows-provided Task Scheduler COM API. No service, password,
    // external executable, or third-party scheduler library is needed.
    internal sealed class AutostartManager : IDisposable
    {
        private const string Owner = "BatteryChargeMeter.Logon.v1";
        private readonly string executable;
        private readonly string sid;
        private readonly string taskName;
        private object service;
        private object folder;

        internal AutostartManager(string path, string userSid, string name)
        {
            executable = Path.GetFullPath(path);
            sid = userSid;
            taskName = name;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                Call(service, "Connect");
                folder = Call(service, "GetFolder", @"\");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static AutostartManager ForCurrentExecutable(string path)
        {
            string userSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                userSid = identity.User.Value;
            return new AutostartManager(path, userSid, "BatteryChargeMeter.Logon." + userSid);
        }

        internal AutostartState Read()
        {
            object task = null;
            try
            {
                try
                {
                    task = Call(folder, "GetTask", taskName);
                }
                catch (Exception error)
                {
                    if (IsMissing(error))
                        return new AutostartState();
                    throw;
                }
                return Inspect((string)Get(task, "Xml"), executable, sid);
            }
            finally
            {
                Release(task);
            }
        }

        internal void Enable(AutostartState expected)
        {
            if (!Startup.IsElevated())
                throw new InvalidOperationException("请先点击“重新以管理员身份启动”，再启用开机自启。");
            Mutate(delegate { EnableCore(expected); });
        }

        private void EnableCore(AutostartState expected)
        {
            AutostartState before = Read();
            RequireUnchanged(expected, before);
            object registered = null;
            try
            {
                registered = Call(folder, "RegisterTask", taskName,
                    BuildXml(executable, sid), 6 | 0x10, sid, null, 3,
                    "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGXSD;;;" + sid + ")");
                // The same user's ordinary token may inspect, run and delete
                // the task (including per-user uninstall), but cannot rewrite
                // its elevated action. Keep full control for SYSTEM/admins and
                // suppress Scheduler's automatic extra principal ACE.
            }
            finally
            {
                Release(registered);
            }
            AutostartState after = Read();
            if (!after.ThisCopy || !after.Enabled)
                throw new InvalidOperationException("自启任务写入后的读取校验失败。");
        }

        internal static void RequireUnchanged(AutostartState expected, AutostartState actual)
        {
            if (expected == null || expected.Exists != actual.Exists
                || !String.Equals(expected.Registration, actual.Registration, StringComparison.Ordinal))
                throw new InvalidOperationException("自启任务已被另一份程序修改，请重新读取并确认后再试。");
        }

        internal void Disable()
        {
            Mutate(DisableCore);
        }

        private void DisableCore()
        {
            AutostartState before = Read();
            if (!before.Exists || !before.ThisCopy)
                return;
            Call(folder, "DeleteTask", taskName, 0);
            if (Read().ThisCopy)
                throw new InvalidOperationException("自启任务删除后的读取校验失败。");
        }

        private void Mutate(Action change)
        {
            MutexSecurity security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid),
                MutexRights.FullControl, AccessControlType.Allow));
            bool created;
            using (Mutex gate = new Mutex(false, @"Local\BatteryChargeMeter.Autostart." + taskName, out created, security))
            {
                bool acquired;
                try
                {
                    acquired = gate.WaitOne(5000);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                    throw new InvalidOperationException("另一份程序正在修改自启，请稍后重试。");
                try
                {
                    change();
                }
                finally
                {
                    gate.ReleaseMutex();
                }
            }
        }

        internal static AutostartState Inspect(string xml, string path, string userSid)
        {
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(xml);
            XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            if (Text(document, ns, "/t:Task/t:RegistrationInfo/t:Source") != Owner
                || !MatchesUser(Text(document, ns, "/t:Task/t:Principals/t:Principal/t:UserId"), userSid)
                || document.SelectNodes("/t:Task/t:Actions/*", ns).Count != 1
                || Text(document, ns, "/t:Task/t:Actions/t:Exec/t:Arguments") != "--autostart")
                throw new InvalidOperationException("同名任务不属于本程序，未修改。");
            string target = Text(document, ns, "/t:Task/t:Actions/t:Exec/t:Command");
            bool enabled = Text(document, ns, "/t:Task/t:Settings/t:Enabled") != "false";
            bool correctPolicy = Text(document, ns, "/t:Task/t:Principals/t:Principal/t:RunLevel") == "HighestAvailable"
                && Text(document, ns, "/t:Task/t:Principals/t:Principal/t:LogonType") == "InteractiveToken"
                && MatchesUser(Text(document, ns, "/t:Task/t:Triggers/t:LogonTrigger/t:UserId"), userSid)
                && Text(document, ns, "/t:Task/t:Triggers/t:LogonTrigger/t:Enabled") != "false";
            bool correctSettings = Text(document, ns, "/t:Task/t:Settings/t:DisallowStartIfOnBatteries") == "false"
                && Text(document, ns, "/t:Task/t:Settings/t:StopIfGoingOnBatteries") == "false"
                && DefaultFalse(document, ns, "RunOnlyIfIdle")
                && DefaultFalse(document, ns, "RunOnlyIfNetworkAvailable")
                && Text(document, ns, "/t:Task/t:Settings/t:ExecutionTimeLimit") == "PT0S"
                && Text(document, ns, "/t:Task/t:Settings/t:MultipleInstancesPolicy") == "IgnoreNew";
            return new AutostartState
            {
                Exists = true,
                Executable = target,
                ThisCopy = IsCurrentCopy(target, path),
                Enabled = enabled && correctPolicy && correctSettings,
                Registration = document.OuterXml,
                RepairReason = !correctPolicy || !correctSettings
                    ? "自启任务设置已变化（权限、触发条件或运行限制），请重新勾选以修复。" : null
            };
        }

        internal static bool IsCurrentCopy(string target, string path)
        {
            string actual = Path.GetFullPath(target);
            string current = Path.GetFullPath(path);
            if (String.Equals(actual, current, StringComparison.OrdinalIgnoreCase)) return true;
            // The installer replaces the old EXE with our forwarding launcher.
            // An arbitrary sibling or a portable copy elsewhere is not an alias.
            if (!String.Equals(Path.GetFileName(current), "PowerMeter.exe", StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Path.GetFileName(actual), "BatteryChargeMeter.exe", StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Path.GetDirectoryName(actual), Path.GetDirectoryName(current), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(actual) || !File.Exists(current)) return false;
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(actual).FileDescription == "Power Meter legacy launcher";
        }

        private static bool MatchesUser(string value, string userSid)
        {
            if (String.Equals(value, userSid, StringComparison.OrdinalIgnoreCase))
                return true;
            if (String.IsNullOrWhiteSpace(value))
                return false;
            try
            {
                // Scheduler can serialize a logon trigger SID as DOMAIN\\user.
                return ((SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier))).Value == userSid;
            }
            catch (IdentityNotMappedException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static bool DefaultFalse(XmlDocument document, XmlNamespaceManager ns, string setting)
        {
            string value = Text(document, ns, "/t:Task/t:Settings/t:" + setting);
            return value == "" || value == "false";
        }

        private static string Text(XmlDocument document, XmlNamespaceManager ns, string xpath)
        {
            XmlNode node = document.SelectSingleNode(xpath, ns);
            return node == null ? "" : node.InnerText;
        }

        internal static string BuildXml(string path, string userSid)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml("<Task version='1.2' xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'>"
                + "<RegistrationInfo><Source>" + Owner + "</Source></RegistrationInfo>"
                + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId /></LogonTrigger></Triggers>"
                + "<Principals><Principal id='User'><UserId /><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>"
                + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
                + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
                + "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>"
                + "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>"
                + "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>"
                + "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>"
                + "<RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"
                + "<Priority>7</Priority></Settings><Actions Context='User'><Exec><Command /><Arguments>--autostart</Arguments><WorkingDirectory /></Exec></Actions></Task>");
            XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("t", document.DocumentElement.NamespaceURI);
            document.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger/t:UserId", ns).InnerText = userSid;
            document.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:UserId", ns).InnerText = userSid;
            document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", ns).InnerText = Path.GetFullPath(path);
            document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:WorkingDirectory", ns).InnerText = Path.GetDirectoryName(Path.GetFullPath(path));
            return document.OuterXml;
        }

        private static bool IsMissing(Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null)
                error = error.InnerException;
            return error.HResult == unchecked((int)0x80070002);
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture);
        }

        private static object Get(object target, string property)
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        public void Dispose()
        {
            Release(folder);
            folder = null;
            Release(service);
            service = null;
        }
    }
}
