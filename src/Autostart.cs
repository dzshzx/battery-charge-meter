using System;
using System.IO;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;
using Microsoft.Win32.TaskScheduler;

namespace BatteryChargeMeter
{
    internal sealed class AutostartState
    {
        public bool Exists;
        public bool Enabled;
        public bool ThisCopy;
        // True when the task runs this user's admin-only protected copy.
        public bool Protected;
        // The installation that owns startup (the protected copy's source).
        public string Executable;
        // The file the task actually executes.
        public string Target;
        public string Registration;
        public string RepairReason;
    }

    internal enum AutostartSync
    {
        Current = 0,
        // This installation's task runs an outdated protected copy.
        StaleCopy = 3,
        // This installation's task still runs a user-writable file.
        Unprotected = 4,
        // No task runs this installation's protected copy any more.
        OrphanCopy = 5
    }

    // Exit codes of --remove-autostart (1 is a failure, 2 invalid arguments).
    internal enum AutostartRemoval
    {
        Removed = 0,
        // This installation's task is gone, but deleting its protected copy
        // needs an elevated rerun.
        CopyNeedsElevation = 3,
        // This installation's startup is removed; a same-name task that fails
        // the ownership check was left unchanged for the user to delete.
        ForeignTaskKept = 6
    }

    // A task with this program's name whose structure is not one this program
    // registers (edited by the user or another program). It is never
    // modified; removal treats it as not this program's startup.
    internal sealed class ForeignAutostartTaskException : InvalidOperationException
    {
        internal ForeignAutostartTaskException()
            : base("同名任务不属于本程序，未修改。")
        {
        }
    }

    // What the manager read before deciding: the task (a foreign same-name
    // task reads as absent), the protected copy and the process token.
    internal sealed class AutostartFacts
    {
        public AutostartState Task = new AutostartState();
        public bool Foreign;
        // This process is itself the protected copy, whose source is
        // user-writable, so it never synchronizes from it.
        public bool RunningFromCopy;
        // The copy's recorded source is this installation.
        public bool CopyOwned;
        // The copy holds exactly this installation's files.
        public bool CopyMatches;
        public bool Elevated;
    }

    // A decision, carried out by the manager in this order: delete the task,
    // install the copy, register the task, restart stopped instances, remove
    // the copy. A refusal performs nothing.
    internal sealed class AutostartPlan
    {
        public AutostartSync Sync;
        public AutostartRemoval Removal;
        public bool DeleteTask;
        public bool InstallCopy;
        public bool RegisterTask;
        // Restart logon instances the copy installation stopped.
        public bool RestartIfStopped;
        public bool RemoveCopy;
        public string Refusal;
    }

    internal enum AutostartSwitch
    {
        Off,
        On,
        Unknown
    }

    // How the in-app switch presents startup. Notice is a diagnostic source
    // string; Tooltip is already localized.
    internal sealed class AutostartStatus
    {
        public AutostartSwitch Switch;
        public string Notice;
        public string Tooltip;
    }

    // Every startup decision, as pure functions of what was read. The manager
    // gathers facts and executes plans; nothing here touches the scheduler,
    // the file system or the process token.
    internal static class AutostartPolicy
    {
        internal const string ElevationRequired = "请先点击下方“以管理员身份重新启动”，再勾选开机自启。";
        internal const string ElevationRequiredForCopy = "写入或删除自启副本需要管理员权限。";
        internal const string ChangedElsewhere = "自启任务已被另一份程序修改，请重新读取并确认后再试。";
        // Chinese lookup key ending in {0}; the task name follows verbatim.
        internal const string ForeignTaskNotice = "同名自启任务不属于本程序，已保留未改；如不再需要，请在任务计划程序库中手工删除：";

        // Migrates a task that still runs a user-writable file, refreshes an
        // outdated copy, and removes a copy no task uses. Without apply it
        // only reports what an elevated run would change.
        internal static AutostartPlan Synchronize(AutostartFacts facts, bool apply)
        {
            AutostartPlan plan = new AutostartPlan();
            if (facts.RunningFromCopy)
                return plan;
            AutostartState task = facts.Task;
            AutostartSync needed;
            if (task.Exists && task.ThisCopy && !task.Protected)
                needed = AutostartSync.Unprotected;
            else if (task.Exists && task.ThisCopy)
                needed = facts.CopyMatches ? AutostartSync.Current : AutostartSync.StaleCopy;
            else
                needed = facts.CopyOwned ? AutostartSync.OrphanCopy : AutostartSync.Current;
            plan.Sync = needed;
            if (!apply || needed == AutostartSync.Current)
                return plan;
            if (!facts.Elevated)
                return Refuse(plan, ElevationRequiredForCopy);
            plan.Sync = AutostartSync.Current;
            switch (needed)
            {
                case AutostartSync.Unprotected:
                    plan.InstallCopy = true;
                    plan.RegisterTask = true;
                    plan.RestartIfStopped = true;
                    break;
                case AutostartSync.StaleCopy:
                    plan.InstallCopy = true;
                    plan.RestartIfStopped = task.Enabled;
                    break;
                default:
                    plan.RemoveCopy = true;
                    break;
            }
            return plan;
        }

        // Deletes this installation's task and, when elevated, its copy. A
        // foreign task is left unchanged and reported; CopyNeedsElevation takes
        // precedence because the elevated rerun reports the foreign task again.
        internal static AutostartPlan Disable(AutostartFacts facts)
        {
            AutostartPlan plan = new AutostartPlan();
            plan.DeleteTask = facts.Task.Exists && facts.Task.ThisCopy;
            plan.Removal = facts.Foreign ? AutostartRemoval.ForeignTaskKept : AutostartRemoval.Removed;
            if (!facts.CopyOwned)
                return plan;
            if (!facts.Elevated)
            {
                plan.Removal = AutostartRemoval.CopyNeedsElevation;
                return plan;
            }
            plan.RemoveCopy = true;
            return plan;
        }

        // Every protected task has the same action, so the owner recorded with
        // the copy is part of what the user confirmed.
        internal static AutostartPlan Enable(bool elevated, AutostartState expected, AutostartState actual)
        {
            AutostartPlan plan = new AutostartPlan();
            if (!elevated)
                return Refuse(plan, ElevationRequired);
            if (!Unchanged(expected, actual))
                return Refuse(plan, ChangedElsewhere);
            plan.InstallCopy = true;
            plan.RegisterTask = true;
            plan.RestartIfStopped = true;
            return plan;
        }

        internal static bool Unchanged(AutostartState expected, AutostartState actual)
        {
            return expected != null && actual != null && expected.Exists == actual.Exists
                && String.Equals(expected.Registration, actual.Registration, StringComparison.Ordinal)
                && String.Equals(expected.Executable, actual.Executable, StringComparison.OrdinalIgnoreCase);
        }

        internal static AutostartStatus Describe(AutostartState task, bool foreign, string taskName)
        {
            AutostartStatus status = new AutostartStatus();
            status.Switch = task.ThisCopy && task.Enabled ? AutostartSwitch.On : AutostartSwitch.Off;
            // Not this program's startup: show it as off, leave the task alone
            // and say where to delete it.
            if (foreign)
                status.Notice = ForeignTaskNotice + taskName;
            else if (task.ThisCopy && !String.IsNullOrEmpty(task.RepairReason))
                status.Notice = task.RepairReason;
            status.Tooltip = task.Exists && !task.ThisCopy
                ? Strings.Format("当前自启指向：{0}；勾选可确认更换。", task.Executable)
                : Strings.Get("以当前 Windows 用户登录后，从仅管理员可写的受保护副本以管理员权限运行并留在托盘。移动程序后需重新启用。");
            return status;
        }

        internal static AutostartStatus ReadFailed(string reason)
        {
            AutostartStatus status = new AutostartStatus();
            status.Switch = AutostartSwitch.Unknown;
            status.Notice = "自启状态读取失败：" + reason;
            return status;
        }

        // Diagnostic source string for the result of the in-app switch.
        internal static string Outcome(bool enabled, AutostartRemoval removal)
        {
            if (enabled)
                return "已启用开机自启：登录后以管理员权限运行受保护副本，在托盘显示。";
            return removal == AutostartRemoval.CopyNeedsElevation
                ? "已关闭此程序的开机自启；受保护副本将在下次以管理员身份启动时删除。"
                : "已关闭此程序的开机自启。";
        }

        private static AutostartPlan Refuse(AutostartPlan plan, string reason)
        {
            plan.Refusal = reason;
            return plan;
        }
    }

    // Registers and reads the logon task through the TaskScheduler library
    // (a managed wrapper over the Windows Task Scheduler 2.0 COM API). No
    // service, password or external executable is needed.
    internal sealed class AutostartManager : IDisposable
    {
        private const string Owner = "BatteryChargeMeter.Logon.v1";
        private const string Arguments = "--autostart";
        // Full control for SYSTEM and administrators; the same user's ordinary
        // token may inspect, run and delete the task (including per-user
        // uninstall), but cannot rewrite its elevated action. The protected
        // DACL suppresses Scheduler's automatic extra principal ACE. The
        // action itself lives in an admin-only directory (ProtectedCopy).
        private const string SecurityPrefix = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGXSD;;;";
        // The running file, and the installation it represents. They differ
        // only when running from the protected copy.
        private readonly string image;
        private readonly string identity;
        private readonly ProtectedCopy copy;
        private readonly string sid;
        private readonly string taskName;
        private readonly bool elevated;
        private TaskService service;
        private TaskFolder folder;

        internal AutostartManager(string path, string userSid, string name, string protectedRoot)
        {
            image = Path.GetFullPath(path);
            identity = ProtectedCopy.IdentityFor(image, userSid);
            copy = new ProtectedCopy(protectedRoot, userSid);
            sid = userSid;
            taskName = name;
            elevated = Startup.IsElevated();
            try
            {
                service = new TaskService();
                folder = service.RootFolder;
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
            return new AutostartManager(path, userSid, "BatteryChargeMeter.Logon." + userSid, ProtectedCopy.DefaultRoot);
        }

        internal string Identity { get { return identity; } }

        internal AutostartState Read()
        {
            using (Task task = folder.Tasks.Exists(taskName) ? folder.Tasks[taskName] : null)
            {
                if (task == null)
                    return new AutostartState();
                return Inspect(task.Definition, identity, sid, copy.Executable, copy.ReadSource());
            }
        }

        internal void Enable(AutostartState expected)
        {
            Mutate(delegate
            {
                // Refuse before reading when not elevated, as the UI does.
                Execute(AutostartPolicy.Enable(elevated, expected, elevated ? Read() : null));
            });
        }

        internal static void RequireUnchanged(AutostartState expected, AutostartState actual)
        {
            if (!AutostartPolicy.Unchanged(expected, actual))
                throw new InvalidOperationException(AutostartPolicy.ChangedElsewhere);
        }

        // Deletes this installation's task and, when elevated, its protected
        // copy. A same-name task that fails the ownership check is left
        // unchanged and reported (AutostartPolicy.Disable).
        internal AutostartRemoval Disable()
        {
            AutostartRemoval result = AutostartRemoval.Removed;
            Mutate(delegate
            {
                AutostartPlan plan = AutostartPolicy.Disable(Gather(false));
                Execute(plan);
                result = plan.Removal;
            });
            return result;
        }

        // Brings the protected copy in line with this installation
        // (AutostartPolicy.Synchronize). A protected copy never synchronizes,
        // because its source is user-writable.
        internal AutostartSync Synchronize(bool apply)
        {
            AutostartSync result = AutostartSync.Current;
            Mutate(delegate
            {
                AutostartPlan plan = AutostartPolicy.Synchronize(Gather(true), apply);
                Execute(plan);
                result = plan.Sync;
            });
            return result;
        }

        // The in-app switch state; a scheduler failure propagates. Reads only
        // the task, because the window refreshes it on every activation.
        internal AutostartStatus Describe()
        {
            bool foreign;
            AutostartState task = ReadOwn(out foreign);
            return AutostartPolicy.Describe(task, foreign, taskName);
        }

        // Like Read, but a same-name task that fails the ownership check reads
        // as absent: it is not this program's startup, never runs this
        // installation's copy as far as cleanup is concerned, and is never
        // touched. The copy lives below Program Files, so removing it cannot
        // let an ordinary process plant a file where such a task points.
        private AutostartState ReadOwn(out bool foreign)
        {
            foreign = false;
            try
            {
                return Read();
            }
            catch (ForeignAutostartTaskException)
            {
                foreign = true;
                return new AutostartState();
            }
        }

        private AutostartFacts Gather(bool forSynchronize)
        {
            AutostartFacts facts = new AutostartFacts();
            facts.Elevated = elevated;
            facts.RunningFromCopy = !ProtectedCopy.SamePath(image, identity);
            // A protected copy never synchronizes, so it reads nothing.
            if (forSynchronize && facts.RunningFromCopy)
                return facts;
            facts.Task = ReadOwn(out facts.Foreign);
            facts.CopyOwned = copy.OwnedBy(identity);
            // Only synchronization compares file contents. Removal must never
            // depend on reading files beside the user-writable installation,
            // so uninstall and the in-app switch always have a way out.
            facts.CopyMatches = forSynchronize && facts.Task.Exists && facts.Task.ThisCopy
                && facts.Task.Protected && copy.Matches(image);
            return facts;
        }

        private void Execute(AutostartPlan plan)
        {
            if (plan.Refusal != null)
                throw new InvalidOperationException(plan.Refusal);
            if (plan.DeleteTask)
            {
                folder.DeleteTask(taskName, false);
                if (Read().ThisCopy)
                    throw new InvalidOperationException("自启任务删除后的读取校验失败。");
            }
            int stopped = plan.InstallCopy ? copy.Install(image, identity) : 0;
            if (plan.RegisterTask)
                Register();
            if (plan.RestartIfStopped && stopped > 0)
                RunTask();
            if (plan.RemoveCopy)
                copy.Remove();
        }

        // The protected copy must already hold this installation's files.
        private void Register()
        {
            TaskDefinition definition = service.NewTask();
            Configure(definition, copy.Executable, sid);
            using (folder.RegisterTaskDefinition(taskName, definition,
                TaskCreation.CreateOrUpdate | TaskCreation.IgnoreRegistrationTriggers,
                sid, null, TaskLogonType.InteractiveToken, SecurityPrefix + sid + ")"))
            {
            }
            AutostartState after = Read();
            if (!after.ThisCopy || !after.Enabled || !after.Protected)
                throw new InvalidOperationException("自启任务写入后的读取校验失败。");
        }

        private void RunTask()
        {
            try
            {
                using (Task task = folder.Tasks[taskName])
                    task.Run();
            }
            catch (Exception)
            {
                // The instance returns at the next sign-in.
            }
        }

        private void Mutate(System.Action change)
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

        // The one task shape this program registers: a logon trigger and an
        // elevated interactive principal for the user, battery-, idle- and
        // network-independent, unlimited, single-instance.
        internal static void Configure(TaskDefinition definition, string path, string userSid)
        {
            string target = Path.GetFullPath(path);
            definition.RegistrationInfo.Source = Owner;
            definition.Triggers.Add(new LogonTrigger { UserId = userSid, Enabled = true });
            definition.Principal.Id = "User";
            definition.Principal.UserId = userSid;
            definition.Principal.LogonType = TaskLogonType.InteractiveToken;
            definition.Principal.RunLevel = TaskRunLevel.Highest;
            definition.Actions.Context = "User";
            definition.Actions.Add(new ExecAction(target, Arguments, Path.GetDirectoryName(target)));
            TaskSettings settings = definition.Settings;
            settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
            settings.DisallowStartIfOnBatteries = false;
            settings.StopIfGoingOnBatteries = false;
            settings.AllowHardTerminate = true;
            settings.StartWhenAvailable = true;
            settings.RunOnlyIfNetworkAvailable = false;
            settings.IdleSettings.StopOnIdleEnd = false;
            settings.IdleSettings.RestartOnIdle = false;
            settings.AllowDemandStart = true;
            settings.Enabled = true;
            settings.Hidden = false;
            settings.RunOnlyIfIdle = false;
            settings.WakeToRun = false;
            settings.ExecutionTimeLimit = TimeSpan.Zero;
            settings.Priority = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }

        internal static AutostartState Inspect(TaskDefinition definition, string path, string userSid,
            string protectedExecutable, string protectedSource)
        {
            ExecAction exec = definition.Actions.Count == 1 ? definition.Actions[0] as ExecAction : null;
            if (definition.RegistrationInfo.Source != Owner
                || !MatchesUser(definition.Principal.UserId, userSid)
                || exec == null
                || exec.Arguments != Arguments)
                throw new ForeignAutostartTaskException();
            string target = exec.Path ?? "";
            TaskSettings settings = definition.Settings;
            LogonTrigger logon = null;
            foreach (Trigger trigger in definition.Triggers)
                if (logon == null)
                    logon = trigger as LogonTrigger;
            bool correctPolicy = definition.Principal.RunLevel == TaskRunLevel.Highest
                && definition.Principal.LogonType == TaskLogonType.InteractiveToken
                && logon != null && MatchesUser(logon.UserId, userSid) && logon.Enabled;
            bool correctSettings = !settings.DisallowStartIfOnBatteries
                && !settings.StopIfGoingOnBatteries
                && !settings.RunOnlyIfIdle
                && !settings.RunOnlyIfNetworkAvailable
                && settings.ExecutionTimeLimit == TimeSpan.Zero
                && settings.MultipleInstances == TaskInstancesPolicy.IgnoreNew;
            bool isProtected = protectedExecutable != null && target.Length > 0
                && ProtectedCopy.SamePath(target, protectedExecutable);
            bool thisCopy = isProtected
                ? protectedSource != null && ProtectedCopy.SamePath(protectedSource, path)
                : target.Length > 0 && IsCurrentCopy(target, path);
            return new AutostartState
            {
                Exists = true,
                Executable = isProtected && protectedSource != null ? protectedSource : target,
                Target = target,
                Protected = isProtected,
                ThisCopy = thisCopy,
                // An elevated task that runs a user-writable file is never
                // reported as working startup, even if its policy is intact.
                Enabled = settings.Enabled && correctPolicy && correctSettings && isProtected,
                Registration = definition.XmlText,
                RepairReason = !correctPolicy || !correctSettings
                    ? "自启任务设置已变化（权限、触发条件或运行限制），请重新勾选以修复。"
                    : !isProtected
                        ? "自启仍指向普通权限可替换的程序文件，请以管理员身份重新勾选，改用受保护副本。"
                        : null
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

        // The XML of a task in the registered shape, for harnesses that plant
        // tasks directly (for example one left by an older version).
        internal static string BuildXml(string path, string userSid)
        {
            using (TaskService scheduler = new TaskService())
            {
                TaskDefinition definition = scheduler.NewTask();
                Configure(definition, path, userSid);
                return definition.XmlText;
            }
        }

        public void Dispose()
        {
            if (folder != null)
                folder.Dispose();
            folder = null;
            if (service != null)
                service.Dispose();
            service = null;
        }
    }
}
