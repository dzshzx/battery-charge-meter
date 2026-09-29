using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using static BatteryChargeMeter.Tests.Fixtures;

namespace BatteryChargeMeter.Tests
{
    public sealed class AutostartPolicyTests
    {
        [Fact]
        public void Policy()
        {
                        // Synchronize: a protected copy never copies from its user-writable source.
            AutostartFacts fromCopy = Facts(true, true, false, true, false, true, false, true);
            fromCopy.RunningFromCopy = true;
            AutostartPlan plan = AutostartPolicy.Synchronize(fromCopy, true);
            Check("protected copy process never synchronizes",
                plan.Sync == AutostartSync.Current && Steps(plan) == "none");

            AutostartFacts unprotected = Facts(true, true, false, true, false, false, false, true);
            plan = AutostartPolicy.Synchronize(unprotected, false);
            Check("report-only sync names an unprotected task without acting",
                plan.Sync == AutostartSync.Unprotected && Steps(plan) == "none");
            plan = AutostartPolicy.Synchronize(unprotected, true);
            Check("elevated sync migrates an unprotected task to the protected copy",
                plan.Sync == AutostartSync.Current && Steps(plan) == "install+register+restart");
            unprotected.Elevated = false;
            plan = AutostartPolicy.Synchronize(unprotected, true);
            Check("sync without elevation refuses to write the copy",
                Steps(plan) == "refuse" && plan.Refusal == AutostartPolicy.ElevationRequiredForCopy);

            plan = AutostartPolicy.Synchronize(Facts(true, true, true, true, false, true, true, true), true);
            Check("matching protected copy is current",
                plan.Sync == AutostartSync.Current && Steps(plan) == "none");
            plan = AutostartPolicy.Synchronize(Facts(true, true, true, false, false, true, false, false), false);
            AutostartPlan staleDisabled = AutostartPolicy.Synchronize(Facts(true, true, true, false, false, true, false, true), true);
            AutostartPlan staleEnabled = AutostartPolicy.Synchronize(Facts(true, true, true, true, false, true, false, true), true);
            Check("outdated copy is refreshed and restarted only when startup is enabled",
                plan.Sync == AutostartSync.StaleCopy && Steps(plan) == "none"
                && Steps(staleDisabled) == "install" && Steps(staleEnabled) == "install+restart");

            plan = AutostartPolicy.Synchronize(Facts(false, false, false, false, false, true, false, false), false);
            AutostartPlan orphan = AutostartPolicy.Synchronize(Facts(false, false, false, false, false, true, false, true), true);
            AutostartPlan foreignOrphan = AutostartPolicy.Synchronize(Facts(false, false, false, false, true, true, false, false), false);
            AutostartPlan foreignApplied = AutostartPolicy.Synchronize(Facts(false, false, false, false, true, true, false, true), true);
            Check("a copy no task of this installation runs is removed, including behind a foreign task",
                plan.Sync == AutostartSync.OrphanCopy && Steps(orphan) == "remove"
                && foreignOrphan.Sync == AutostartSync.OrphanCopy && Steps(foreignApplied) == "remove");
            plan = AutostartPolicy.Synchronize(Facts(true, true, true, true, false, true, true, false), true);
            AutostartPlan staleOrdinary = AutostartPolicy.Synchronize(Facts(true, true, true, true, false, true, false, false), true);
            AutostartPlan orphanOrdinary = AutostartPolicy.Synchronize(Facts(false, false, false, false, false, true, false, false), true);
            AutostartPlan nothing = AutostartPolicy.Synchronize(Facts(false, false, false, false, false, false, false, false), true);
            Check("ordinary sync succeeds when current and refuses only real changes",
                plan.Sync == AutostartSync.Current && Steps(plan) == "none"
                && Steps(staleOrdinary) == "refuse" && Steps(orphanOrdinary) == "refuse"
                && nothing.Sync == AutostartSync.Current && Steps(nothing) == "none");
            plan = AutostartPolicy.Synchronize(Facts(true, false, true, true, false, false, false, true), true);
            Check("another installation's startup is left alone",
                plan.Sync == AutostartSync.Current && Steps(plan) == "none");

            // Disable.
            plan = AutostartPolicy.Disable(Facts(true, true, true, true, false, true, true, true));
            Check("elevated removal deletes the task and the copy",
                plan.Removal == AutostartRemoval.Removed && Steps(plan) == "delete+remove");
            plan = AutostartPolicy.Disable(Facts(true, true, true, true, false, true, true, false));
            Check("ordinary removal deletes the task and defers the copy",
                plan.Removal == AutostartRemoval.CopyNeedsElevation && Steps(plan) == "delete");
            plan = AutostartPolicy.Disable(Facts(false, false, false, false, true, true, false, true));
            AutostartPlan foreignOrdinary = AutostartPolicy.Disable(Facts(false, false, false, false, true, true, false, false));
            AutostartPlan foreignNoCopy = AutostartPolicy.Disable(Facts(false, false, false, false, true, false, false, false));
            Check("removal keeps a foreign task, still removes the copy, and elevation takes precedence",
                plan.Removal == AutostartRemoval.ForeignTaskKept && Steps(plan) == "remove"
                && foreignOrdinary.Removal == AutostartRemoval.CopyNeedsElevation && Steps(foreignOrdinary) == "none"
                && foreignNoCopy.Removal == AutostartRemoval.ForeignTaskKept && Steps(foreignNoCopy) == "none");
            plan = AutostartPolicy.Disable(Facts(true, false, true, true, false, false, false, false));
            Check("removal never deletes another installation's task",
                plan.Removal == AutostartRemoval.Removed && Steps(plan) == "none");
            plan = AutostartPolicy.Disable(Facts(true, true, false, true, false, false, false, false));
            Check("ordinary removal disarms an unprotected task without a copy",
                plan.Removal == AutostartRemoval.Removed && Steps(plan) == "delete");

            // Enable.
            AutostartState absent = new AutostartState();
            AutostartState other = new AutostartState { Exists = true, Registration = "<Task/>", Executable = @"C:\Other\PowerMeter.exe" };
            Check("enabling requires elevation and an unchanged confirmation",
                AutostartPolicy.Enable(false, absent, null).Refusal == AutostartPolicy.ElevationRequired
                && AutostartPolicy.Enable(true, absent, other).Refusal == AutostartPolicy.ChangedElsewhere
                && AutostartPolicy.Enable(true, null, absent).Refusal == AutostartPolicy.ChangedElsewhere
                && Steps(AutostartPolicy.Enable(true, other, other)) == "install+register+restart");

            // In-app switch.
            AutostartStatus status = AutostartPolicy.Describe(absent, true, "BatteryChargeMeter.Logon.S-1");
            Check("switch shows a foreign task as off with where to delete it",
                status.Switch == AutostartSwitch.Off
                && status.Notice == AutostartPolicy.ForeignTaskNotice + "BatteryChargeMeter.Logon.S-1");
            AutostartState working = new AutostartState { Exists = true, ThisCopy = true, Protected = true, Enabled = true };
            AutostartState repair = new AutostartState { Exists = true, ThisCopy = true, RepairReason = "repair" };
            AutostartState enabledRepair = new AutostartState { Exists = true, ThisCopy = true, Enabled = true, RepairReason = "repair" };
            Check("switch reflects working startup and asks for repair",
                AutostartPolicy.Describe(working, false, "t").Switch == AutostartSwitch.On
                && AutostartPolicy.Describe(working, false, "t").Notice == null
                && AutostartPolicy.Describe(repair, false, "t").Switch == AutostartSwitch.Off
                && AutostartPolicy.Describe(repair, false, "t").Notice == "repair"
                && AutostartPolicy.Describe(enabledRepair, false, "t").Switch == AutostartSwitch.On
                && AutostartPolicy.Describe(enabledRepair, false, "t").Notice == "repair");
            Check("switch names another installation's startup and reports read failures",
                AutostartPolicy.Describe(other, false, "t").Tooltip.Contains(other.Executable)
                && AutostartPolicy.Describe(absent, false, "t").Switch == AutostartSwitch.Off
                && AutostartPolicy.ReadFailed("x").Switch == AutostartSwitch.Unknown
                && AutostartPolicy.ReadFailed("x").Tooltip == null);
            Check("switch outcome names deferred copy removal",
                AutostartPolicy.Outcome(false, AutostartRemoval.CopyNeedsElevation).Contains("下次以管理员身份启动")
                && AutostartPolicy.Outcome(false, AutostartRemoval.ForeignTaskKept) == AutostartPolicy.Outcome(false, AutostartRemoval.Removed));
        }
    }
}
