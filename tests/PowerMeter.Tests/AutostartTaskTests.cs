using System;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;
using Xunit;
using static BatteryChargeMeter.Tests.Fixtures;

namespace BatteryChargeMeter.Tests
{
    // Reads task definitions the way the manager does, without registering
    // anything: the scheduler connection only builds definitions.
    public sealed class AutostartTaskTests : IDisposable
    {
        private const string Sid = "S-1-5-21-111-222-333-1001";
        private const string InstalledPath = @"C:\Test & 测试\Meter 1.2.1.exe";
        private readonly TaskService scheduler = new TaskService();

        public void Dispose()
        {
            scheduler.Dispose();
        }

        private TaskDefinition Registered(string target, string sid)
        {
            TaskDefinition definition = scheduler.NewTask();
            AutostartManager.Configure(definition, target, sid);
            // Read back through XML, as the scheduler stores it.
            TaskDefinition stored = scheduler.NewTask();
            stored.XmlText = definition.XmlText;
            return stored;
        }

        private TaskDefinition Protected()
        {
            return Registered(SelfTestCopy(Sid), Sid);
        }

        // Reads a task as if this user's protected copy was taken from path.
        private static AutostartState Inspect(TaskDefinition definition, string path, string sid)
        {
            return AutostartManager.Inspect(definition, path, sid, SelfTestCopy(sid), path);
        }

        [Fact]
        public void RegisteredShapeRoundtrips()
        {
            TaskDefinition definition = Protected();
            AutostartState state = Inspect(definition, InstalledPath, Sid);
            ExecAction exec = (ExecAction)definition.Actions[0];
            Check("logon task roundtrips escaped protected copy path and elevated interactive user policy",
                state.Exists && state.Enabled && state.ThisCopy && state.Protected
                && state.Executable == InstalledPath && state.Target == SelfTestCopy(Sid)
                && exec.WorkingDirectory == System.IO.Path.GetDirectoryName(SelfTestCopy(Sid))
                && !definition.Settings.DisallowStartIfOnBatteries
                && !definition.Settings.StopIfGoingOnBatteries
                && definition.Settings.ExecutionTimeLimit == TimeSpan.Zero
                && definition.Settings.MultipleInstances == TaskInstancesPolicy.IgnoreNew
                && definition.XmlText.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"));
            Check("other copies are distinguished by the protected copy's recorded source",
                !AutostartManager.Inspect(definition, @"C:\Other\Meter.exe", Sid, SelfTestCopy(Sid), InstalledPath).ThisCopy
                && !AutostartManager.Inspect(definition, InstalledPath, Sid, SelfTestCopy(Sid), null).ThisCopy);
        }

        [Fact]
        public void UnprotectedTaskNeedsMigration()
        {
            AutostartState unprotected = Inspect(Registered(InstalledPath, Sid), InstalledPath, Sid);
            Check("elevated task running a user-writable file is never reported as working startup",
                unprotected.Exists && unprotected.ThisCopy && !unprotected.Protected && !unprotected.Enabled
                && !String.IsNullOrEmpty(unprotected.RepairReason));
        }

        [Fact]
        public void ProtectedCopyIdentity()
        {
            Check("protected copy identifies itself by the installation it was taken from",
                ProtectedCopy.IdentityFor(@"C:\Elsewhere\PowerMeter.exe", Sid) == @"C:\Elsewhere\PowerMeter.exe"
                && ProtectedCopy.IdentityFor(@"C:\Missing\" + Sid + @"\Other.exe", Sid) == @"C:\Missing\" + Sid + @"\Other.exe"
                && ProtectedCopy.IdentityFor(@"C:\Missing\" + Sid + @"\PowerMeter.exe", Sid) == @"C:\Missing\" + Sid + @"\PowerMeter.exe");
        }

        [Fact]
        public void DisabledTaskReadsDisabled()
        {
            TaskDefinition definition = Protected();
            definition.Settings.Enabled = false;
            Check("disabled task is read as disabled", !Inspect(definition, InstalledPath, Sid).Enabled);
        }

        // Each shape this program never registers is reported as a foreign
        // task (a distinct type, so removal can leave it and still finish).
        [Theory]
        [InlineData("source")]
        [InlineData("arguments")]
        [InlineData("second action")]
        [InlineData("principal")]
        public void ForeignShapeIsNeverAdopted(string change)
        {
            TaskDefinition definition = Protected();
            switch (change)
            {
                case "source":
                    definition.RegistrationInfo.Source = "someone-else";
                    break;
                case "arguments":
                    ((ExecAction)definition.Actions[0]).Arguments = "--other";
                    break;
                case "second action":
                    definition.Actions.Add(new ExecAction(@"C:\Other\Tool.exe"));
                    break;
                default:
                    definition.Principal.UserId = "S-1-5-21-111-222-333-1002";
                    break;
            }
            Assert.Throws<ForeignAutostartTaskException>(delegate { Inspect(definition, InstalledPath, Sid); });
        }

        [Fact]
        public void AccountNamesNormalizeToTheUser()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                string currentSid = identity.User.Value;
                TaskDefinition named = Registered(SelfTestCopy(currentSid), currentSid);
                named.Principal.UserId = identity.Name;
                ((LogonTrigger)named.Triggers[0]).UserId = identity.Name;
                Check("scheduler account-name normalization preserves exact user identity",
                    Inspect(named, InstalledPath, currentSid).Enabled);
                TaskDefinition other = Protected();
                ((LogonTrigger)other.Triggers[0]).UserId = currentSid;
                Check("different logon account cannot be treated as current-user startup",
                    !Inspect(other, InstalledPath, Sid).Enabled);
            }
        }

        [Theory]
        [InlineData("battery start")]
        [InlineData("battery stop")]
        [InlineData("idle")]
        [InlineData("network")]
        [InlineData("time limit")]
        [InlineData("instances")]
        [InlineData("run level")]
        [InlineData("trigger")]
        public void ChangedPolicyNeedsRepair(string change)
        {
            TaskDefinition definition = Protected();
            TaskSettings settings = definition.Settings;
            switch (change)
            {
                case "battery start": settings.DisallowStartIfOnBatteries = true; break;
                case "battery stop": settings.StopIfGoingOnBatteries = true; break;
                case "idle": settings.RunOnlyIfIdle = true; break;
                case "network": settings.RunOnlyIfNetworkAvailable = true; break;
                case "time limit": settings.ExecutionTimeLimit = TimeSpan.FromHours(1); break;
                case "instances": settings.MultipleInstances = TaskInstancesPolicy.Parallel; break;
                case "run level": definition.Principal.RunLevel = TaskRunLevel.LUA; break;
                default: definition.Triggers[0].Enabled = false; break;
            }
            AutostartState altered = Inspect(definition, InstalledPath, Sid);
            Check("changed task policy requires explicit repair: " + change,
                !altered.Enabled && !String.IsNullOrEmpty(altered.RepairReason));
        }

        [Fact]
        public void StaleConfirmationIsRejected()
        {
            AutostartState state = Inspect(Protected(), InstalledPath, Sid);
            Assert.Throws<InvalidOperationException>(delegate
            {
                AutostartManager.RequireUnchanged(state, Inspect(Registered(@"C:\Third copy\Meter.exe", Sid), InstalledPath, Sid));
            });
            Assert.Throws<InvalidOperationException>(delegate
            {
                AutostartManager.RequireUnchanged(state,
                    AutostartManager.Inspect(Protected(), InstalledPath, Sid, SelfTestCopy(Sid), @"C:\Third copy\Meter.exe"));
            });
            AutostartManager.RequireUnchanged(state, Inspect(Protected(), InstalledPath, Sid));
        }
    }
}
