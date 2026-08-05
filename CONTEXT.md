# Battery Power Monitoring

This context defines the electrical boundaries used when describing laptop power flow. The boundary must always be named because different sensors measure different parts of the power path.

## Power Boundaries

**System Input Power (整机输入功率)**:
The instantaneous electrical power crossing the computer's external power-input boundary. It includes system load, battery charging at the battery terminals, and internal conversion losses.
_Avoid_: Charger Power, Charging Port Power

**Net Battery Terminal Power (电池端净功率)**:
The signed electrical power crossing the battery-pack terminals. Positive values flow into the battery; negative values flow from the battery into the system.
_Avoid_: Battery Absorbed Power, Charging Power

**Battery Charge Power (电池充电功率)**:
The non-negative battery-terminal power while the battery is charging. Use Net Battery Terminal Power when one metric also represents discharge.
_Avoid_: Stored Chemical Power

**System Load Power (系统负载功率)**:
The power consumed by the computer's internal components while operating, excluding power flowing into the battery. While the machine runs on battery this is measured directly, because every watt it uses then leaves the battery terminals; on external power it can only be estimated.
_Avoid_: Motherboard Power, Whole-machine Power Draw

**Platform Power (平台功率)**:
The power reported by the processor's platform-level energy counter (Intel Psys, `MSR_PLATFORM_ENERGY_STATUS`). It covers the processor package plus the platform rails the board routes into that counter, and excludes battery charging power. Which rails are covered is an OEM board-design choice, so this is a close but not provably complete measurement of System Load Power. Report it under its own name rather than substituting it for either neighbouring term.
_Avoid_: System Input Power, System Load Power, CPU Power

**Estimated System Input Power (估算整机输入功率)**:
System Input Power derived as Platform Power + Battery Charge Power. It omits the charging-path and conversion losses that no available counter measures, so it reads low. Always carry the estimated qualifier; never present it as System Input Power.
_Avoid_: System Input Power, Measured Input Power

**Wall Input Power (墙端输入功率)**:
The power drawn by the external adapter from the AC supply. It additionally includes losses inside the adapter and is not the same as System Input Power.
_Avoid_: System Input Power
