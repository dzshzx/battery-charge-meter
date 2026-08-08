using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Windows.Forms;

namespace BatteryChargeMeter
{
    internal sealed class BatteryReading
    {
        public BatterySupplyState SupplyState = BatterySupplyState.Unavailable;
        public string StatusUnavailableReason;
        public int ActiveBatteryCount;
        public bool RateAvailable;
        public string RateUnavailableReason;
        public double PowerWatts;
        public bool VoltageAvailable;
        public bool CurrentAvailable;
        public double VoltageVolts;
        public double CurrentAmps;
        public int Percentage = -1;

        public BatterySupplyProfile SupplyProfile
        {
            get { return BatterySupplyStates.Describe(SupplyState); }
        }

        public bool StatusAvailable
        {
            get { return SupplyProfile.StatusAvailable; }
        }

        public bool PowerOnline
        {
            get { return SupplyProfile.PowerOnline; }
        }

        public bool Charging
        {
            get { return SupplyProfile.Charging; }
        }

        public bool Discharging
        {
            get { return SupplyProfile.Discharging; }
        }
    }

    internal static class BatterySensor
    {
        public static BatteryReading Read()
        {
            List<BatteryReading> activeReadings = new List<BatteryReading>();

            ManagementScope scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT * FROM BatteryStatus")))
            {
                foreach (ManagementObject item in searcher.Get())
                {
                    using (item)
                    {
                        if (HasProperty(item, "Active") && !ReadBoolean(item, "Active"))
                            continue;

                        bool powerOnline = ReadBoolean(item, "PowerOnline");
                        bool charging = ReadBoolean(item, "Charging");
                        bool discharging = ReadBoolean(item, "Discharging");
                        BatteryReading reading = new BatteryReading();
                        reading.SupplyState = BatterySupplyStates.FromFlags(
                            powerOnline, charging, discharging);
                        reading.ActiveBatteryCount = 1;
                        if (!reading.StatusAvailable)
                            reading.StatusUnavailableReason = "电池供电标志互相矛盾";

                        uint chargeRate = ReadUInt32(item, "ChargeRate");
                        uint dischargeRate = ReadUInt32(item, "DischargeRate");
                        uint voltage = ReadUInt32(item, "Voltage");

                        ResolveRate(reading, chargeRate, dischargeRate);
                        ResolveElectricalDetails(reading, voltage);
                        activeReadings.Add(reading);
                    }
                }
            }

            BatteryReading combined = CombineReadings(activeReadings);
            AssignSystemPercentage(combined, ReadSystemPercentage());
            return combined;
        }

        internal static BatteryReading CombineReadings(IList<BatteryReading> readings)
        {
            if (readings == null || readings.Count == 0)
            {
                return new BatteryReading
                {
                    SupplyState = BatterySupplyState.Unavailable,
                    StatusUnavailableReason = "本机没有活动电池",
                    RateUnavailableReason = "电池状态不可用"
                };
            }

            if (readings.Count == 1)
            {
                BatteryReading single = readings[0];
                if (single == null)
                {
                    return new BatteryReading
                    {
                        StatusUnavailableReason = "电池状态不可用",
                        RateUnavailableReason = "电池状态不可用"
                    };
                }

                single.ActiveBatteryCount = 1;
                return single;
            }

            BatteryReading combined = new BatteryReading();
            combined.ActiveBatteryCount = readings.Count;
            combined.RateAvailable = true;

            bool? powerOnline = null;
            bool sawCharging = false;
            bool sawDischarging = false;
            bool sawIdle = false;
            bool inconsistent = false;

            foreach (BatteryReading reading in readings)
            {
                if (reading == null || !reading.StatusAvailable)
                {
                    inconsistent = true;
                    combined.RateAvailable = false;
                    combined.StatusUnavailableReason = "至少一块电池状态不可用";
                    continue;
                }

                if (!powerOnline.HasValue)
                    powerOnline = reading.PowerOnline;
                else if (reading.PowerOnline != powerOnline.Value)
                {
                    inconsistent = true;
                    combined.RateAvailable = false;
                    combined.StatusUnavailableReason = "多块电池报告的供电状态不一致";
                }

                sawCharging = sawCharging || reading.Charging;
                sawDischarging = sawDischarging || reading.Discharging;
                sawIdle = sawIdle || (!reading.Charging && !reading.Discharging);

                if (!reading.RateAvailable)
                {
                    combined.RateAvailable = false;
                    if (String.IsNullOrEmpty(combined.RateUnavailableReason))
                    {
                        combined.RateUnavailableReason = String.IsNullOrEmpty(reading.RateUnavailableReason)
                            ? "至少一块电池速率不可用"
                            : reading.RateUnavailableReason;
                    }
                }

                combined.PowerWatts += reading.PowerWatts;
            }

            if (inconsistent || !powerOnline.HasValue)
            {
                combined.SupplyState = BatterySupplyState.Inconsistent;
            }
            else if (combined.RateAvailable)
            {
                combined.SupplyState = StateFromAggregatePower(
                    powerOnline.Value, combined.PowerWatts);
                if (!combined.StatusAvailable)
                {
                    combined.RateAvailable = false;
                    combined.StatusUnavailableReason = "聚合后的电池功率方向与供电状态不一致";
                }
            }
            else
            {
                combined.SupplyState = StateFromDirections(
                    powerOnline.Value, sawCharging, sawDischarging, sawIdle);
            }

            // Packs can have different terminal voltages, so a single voltage
            // or derived current would not describe the aggregate boundary.
            combined.VoltageAvailable = false;
            combined.CurrentAvailable = false;
            return combined;
        }

        private static BatterySupplyState StateFromAggregatePower(bool powerOnline, double powerWatts)
        {
            return BatterySupplyStates.FromFlags(
                powerOnline, powerWatts > 0.0, powerWatts < 0.0);
        }

        private static BatterySupplyState StateFromDirections(
            bool powerOnline, bool charging, bool discharging, bool idle)
        {
            int directionCount = (charging ? 1 : 0) + (discharging ? 1 : 0) + (idle ? 1 : 0);
            if (directionCount != 1)
            {
                return powerOnline
                    ? BatterySupplyState.ExternalPowerDirectionUnknown
                    : BatterySupplyState.BatteryDirectionUnknown;
            }
            return BatterySupplyStates.FromFlags(powerOnline, charging, discharging);
        }

        internal static void ResolveRate(BatteryReading reading, uint chargeRate, uint dischargeRate)
        {
            bool chargeKnown = chargeRate != UInt32.MaxValue;
            bool dischargeKnown = dischargeRate != UInt32.MaxValue;
            reading.RateUnavailableReason = null;

            switch (reading.SupplyState)
            {
                case BatterySupplyState.ExternalPowerCharging:
                    reading.RateAvailable = chargeKnown;
                    reading.PowerWatts = chargeKnown ? chargeRate / 1000.0 : 0.0;
                    if (!chargeKnown)
                        reading.RateUnavailableReason = "电池充电速率不可用";
                    break;

                case BatterySupplyState.ExternalPowerSupplemented:
                case BatterySupplyState.BatteryDischarging:
                    reading.RateAvailable = dischargeKnown;
                    reading.PowerWatts = dischargeKnown ? -(dischargeRate / 1000.0) : 0.0;
                    if (!dischargeKnown)
                        reading.RateUnavailableReason = "电池放电速率不可用";
                    break;

                case BatterySupplyState.ExternalPowerIdle:
                    reading.RateAvailable = true;
                    reading.PowerWatts = 0.0;
                    break;

                case BatterySupplyState.BatteryDirectionUnknown:
                    reading.RateAvailable = false;
                    reading.PowerWatts = 0.0;
                    reading.RateUnavailableReason = "离线状态下电池方向不明";
                    break;

                case BatterySupplyState.Inconsistent:
                    reading.RateAvailable = false;
                    reading.PowerWatts = 0.0;
                    reading.RateUnavailableReason = "电池供电标志互相矛盾";
                    break;

                default:
                    reading.RateAvailable = false;
                    reading.PowerWatts = 0.0;
                    reading.RateUnavailableReason = "电池速率不可用";
                    break;
            }
        }

        internal static void ResolveElectricalDetails(BatteryReading reading, uint voltageMillivolts)
        {
            reading.VoltageAvailable = false;
            reading.CurrentAvailable = false;
            reading.VoltageVolts = 0.0;
            reading.CurrentAmps = 0.0;

            if (voltageMillivolts == UInt32.MaxValue || voltageMillivolts == 0)
                return;

            reading.VoltageAvailable = true;
            reading.VoltageVolts = voltageMillivolts / 1000.0;

            if (!reading.RateAvailable)
                return;

            reading.CurrentAvailable = true;
            reading.CurrentAmps = Math.Abs(reading.PowerWatts) / reading.VoltageVolts;
        }

        internal static void AssignSystemPercentage(BatteryReading reading, int percentage)
        {
            if (reading == null)
                return;

            reading.Percentage = reading.StatusAvailable && percentage >= 0 && percentage <= 100
                ? percentage
                : -1;
        }

        private static int ReadSystemPercentage()
        {
            try
            {
                // PowerStatus is backed by GetSystemPowerStatus and therefore
                // reports the operating system's aggregate battery percentage,
                // rather than an arbitrary first Win32_Battery instance.
                float fraction = SystemInformation.PowerStatus.BatteryLifePercent;
                if (Single.IsNaN(fraction) || Single.IsInfinity(fraction) || fraction < 0f || fraction > 1f)
                    return -1;

                return (int)Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero);
            }
            catch
            {
            }

            return -1;
        }

        private static bool HasProperty(ManagementBaseObject item, string name)
        {
            return item.Properties[name] != null;
        }

        private static bool ReadBoolean(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }

        private static uint ReadUInt32(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value == null ? UInt32.MaxValue : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return UInt32.MaxValue;
            }
        }
    }
}
