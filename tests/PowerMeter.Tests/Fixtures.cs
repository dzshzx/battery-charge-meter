using System;
using System.Collections.Generic;
using Xunit;

namespace BatteryChargeMeter.Tests
{
    internal static class Fixtures
    {
        private const double Tolerance = 1e-9;

        internal static PowerSnapshot Snapshot(BatteryReading battery, PowerSample platform, double elapsedSeconds)
        {
            return PowerSnapshot.Compose(DateTimeOffset.Now, elapsedSeconds, battery, null, platform);
        }

        internal static PowerSample Whole(PowerSample platform, BatteryReading battery)
        {
            return Snapshot(battery, platform, 0).WholeSystem;
        }

        internal static PowerSample Value(double watts)
        {
            return PowerSample.FromValue(PowerBoundary.BatteryTerminal, MeasurementKind.Measured, watts, "test", TimeSpan.Zero);
        }

        internal static AutostartFacts Facts(bool exists, bool thisCopy, bool isProtected, bool enabled,
            bool foreign, bool copyOwned, bool copyMatches, bool elevated)
        {
            AutostartFacts facts = new AutostartFacts();
            facts.Task = new AutostartState { Exists = exists, ThisCopy = thisCopy, Protected = isProtected, Enabled = enabled };
            facts.Foreign = foreign;
            facts.CopyOwned = copyOwned;
            facts.CopyMatches = copyMatches;
            facts.Elevated = elevated;
            return facts;
        }

        internal static string Steps(AutostartPlan plan)
        {
            List<string> steps = new List<string>();
            if (plan.Refusal != null) steps.Add("refuse");
            if (plan.DeleteTask) steps.Add("delete");
            if (plan.InstallCopy) steps.Add("install");
            if (plan.RegisterTask) steps.Add("register");
            if (plan.RestartIfStopped) steps.Add("restart");
            if (plan.RemoveCopy) steps.Add("remove");
            return steps.Count == 0 ? "none" : String.Join("+", steps.ToArray());
        }

        internal static string SelfTestCopy(string sid)
        {
            return new ProtectedCopy(ProtectedCopy.DefaultRoot, sid).Executable;
        }

        internal static BatteryReading Battery(
            bool online, bool charging, bool rateAvailable, double watts)
        {
            BatteryReading reading = new BatteryReading();
            reading.ActiveBatteryCount = 1;
            if (charging)
                reading.SupplyState = BatterySupplyState.ExternalPowerCharging;
            else if (watts < 0.0)
            {
                reading.SupplyState = online
                    ? BatterySupplyState.ExternalPowerSupplemented
                    : BatterySupplyState.BatteryDischarging;
            }
            else
            {
                reading.SupplyState = online
                    ? BatterySupplyState.ExternalPowerIdle
                    : BatterySupplyState.BatteryDirectionUnknown;
            }
            reading.RateAvailable = rateAvailable;
            reading.PowerWatts = watts;
            return reading;
        }

        internal static bool Near(double actual, double expected)
        {
            return Math.Abs(actual - expected) < Tolerance;
        }

        // A named condition, as the release self-test reports it.
        internal static void Check(string name, bool condition)
        {
            Assert.True(condition, name);
        }
    }
}
