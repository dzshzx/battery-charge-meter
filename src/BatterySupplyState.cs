namespace BatteryChargeMeter
{
    internal enum BatterySupplyState
    {
        Unavailable,
        ExternalPowerIdle,
        ExternalPowerCharging,
        ExternalPowerSupplemented,
        ExternalPowerDirectionUnknown,
        BatteryDischarging,
        BatteryDirectionUnknown,
        Inconsistent
    }

    internal enum BatteryAccentKind
    {
        Error,
        Idle,
        Charging,
        Discharging
    }

    /// <summary>
    /// The complete meaning of one supply state. Consumers read this profile
    /// instead of independently interpreting combinations of WMI flags.
    /// </summary>
    internal sealed class BatterySupplyProfile
    {
        public BatterySupplyProfile(
            bool statusAvailable,
            bool powerOnline,
            bool charging,
            bool discharging,
            string stateText,
            string trayMode,
            BatteryAccentKind accent)
        {
            StatusAvailable = statusAvailable;
            PowerOnline = powerOnline;
            Charging = charging;
            Discharging = discharging;
            StateText = stateText;
            TrayMode = trayMode;
            Accent = accent;
        }

        public bool StatusAvailable { get; private set; }
        public bool PowerOnline { get; private set; }
        public bool Charging { get; private set; }
        public bool Discharging { get; private set; }
        public string StateText { get; private set; }
        public string TrayMode { get; private set; }
        public BatteryAccentKind Accent { get; private set; }
    }

    internal static class BatterySupplyStates
    {
        private static readonly BatterySupplyProfile Unavailable = new BatterySupplyProfile(
            false, false, false, false,
            "BATTERY UNAVAILABLE", "Battery unavailable", BatteryAccentKind.Error);

        private static readonly BatterySupplyProfile ExternalPowerIdle = new BatterySupplyProfile(
            true, true, false, false,
            "AC / IDLE", "AC / idle", BatteryAccentKind.Idle);

        private static readonly BatterySupplyProfile ExternalPowerCharging = new BatterySupplyProfile(
            true, true, true, false,
            "CHARGING", "Charging", BatteryAccentKind.Charging);

        private static readonly BatterySupplyProfile ExternalPowerSupplemented = new BatterySupplyProfile(
            true, true, false, true,
            "DISCHARGING", "Discharging", BatteryAccentKind.Discharging);

        private static readonly BatterySupplyProfile ExternalPowerDirectionUnknown = new BatterySupplyProfile(
            true, true, false, false,
            "AC / UNKNOWN", "AC / battery direction unknown", BatteryAccentKind.Error);

        private static readonly BatterySupplyProfile BatteryDischarging = new BatterySupplyProfile(
            true, false, false, true,
            "DISCHARGING", "Discharging", BatteryAccentKind.Discharging);

        private static readonly BatterySupplyProfile BatteryDirectionUnknown = new BatterySupplyProfile(
            true, false, false, false,
            "ON BATTERY", "On battery", BatteryAccentKind.Discharging);

        private static readonly BatterySupplyProfile Inconsistent = new BatterySupplyProfile(
            false, false, false, false,
            "BATTERY STATE INVALID", "Battery state invalid", BatteryAccentKind.Error);

        public static BatterySupplyState FromFlags(
            bool powerOnline, bool charging, bool discharging)
        {
            if (charging && discharging)
                return BatterySupplyState.Inconsistent;
            if (charging && !powerOnline)
                return BatterySupplyState.Inconsistent;
            if (charging)
                return BatterySupplyState.ExternalPowerCharging;
            if (discharging)
            {
                return powerOnline
                    ? BatterySupplyState.ExternalPowerSupplemented
                    : BatterySupplyState.BatteryDischarging;
            }

            return powerOnline
                ? BatterySupplyState.ExternalPowerIdle
                : BatterySupplyState.BatteryDirectionUnknown;
        }

        public static BatterySupplyProfile Describe(BatterySupplyState state)
        {
            switch (state)
            {
                case BatterySupplyState.ExternalPowerIdle:
                    return ExternalPowerIdle;
                case BatterySupplyState.ExternalPowerCharging:
                    return ExternalPowerCharging;
                case BatterySupplyState.ExternalPowerSupplemented:
                    return ExternalPowerSupplemented;
                case BatterySupplyState.ExternalPowerDirectionUnknown:
                    return ExternalPowerDirectionUnknown;
                case BatterySupplyState.BatteryDischarging:
                    return BatteryDischarging;
                case BatterySupplyState.BatteryDirectionUnknown:
                    return BatteryDirectionUnknown;
                case BatterySupplyState.Inconsistent:
                    return Inconsistent;
                default:
                    return Unavailable;
            }
        }
    }
}
