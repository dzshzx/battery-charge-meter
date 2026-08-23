# Battery Power Monitoring

This context defines the electrical boundaries used when describing laptop power flow, the supply states that decide which formula applies, and the measurement kinds that say how honest a figure is. The boundary must always be named because different sensors measure different parts of the power path.

## Power Boundaries

**System Input Power (整机输入功率)**:
The instantaneous electrical power crossing the computer's external power-input boundary. It equals System Load Power plus signed Net Battery Terminal Power plus internal conversion losses; no sensor in the machine measures it directly, so the app only ever shows the estimated form below.
_Avoid_: Charger Power, Charging Port Power

**Net Battery Terminal Power (电池端净功率)**:
The signed electrical power crossing the battery-pack terminals. Positive values flow into the battery; negative values flow from the battery into the system.
_Avoid_: Battery Absorbed Power, Charging Power

**Battery Charge Power (电池充电功率)**:
The non-negative battery-terminal power while the battery is charging. Use Net Battery Terminal Power when one metric also represents discharge.
_Avoid_: Stored Chemical Power

**Battery Discharge Power (电池端放电功率)**:
The non-negative magnitude of Net Battery Terminal Power while the battery is discharging. On battery it is the measured source of System Load Power; on external power it is the battery's supplementation of the system.
_Avoid_: Battery Drain, Negative Charging Power

**CPU Package Power (CPU 包功率)**:
The power consumed by the processor package, read from the board's energy-meter (EMI) rail without drivers or elevation. It is one component of System Load Power and is also used to cross-check the Platform Power counter's energy unit.
_Avoid_: CPU Power, Processor Power Draw, Platform Power

**System Load Power (系统负载功率)**:
The power consumed by the computer's internal components while operating, excluding power flowing into the battery. While the machine runs on battery this is measured directly, because every watt it uses then leaves the battery terminals; on external power it can only be estimated.
_Avoid_: Motherboard Power, Whole-machine Power Draw

**Platform Power (平台功率)**:
The power reported by the processor's platform-level energy counter (Intel Psys, `MSR_PLATFORM_ENERGY_STATUS`). It covers the processor package plus the platform rails the board routes into that counter, and excludes battery charging power. Which rails are covered is an OEM board-design choice, so this is a close but not provably complete measurement of System Load Power. Report it under its own name rather than substituting it for either neighbouring term.
_Avoid_: System Input Power, System Load Power, CPU Power

**Estimated System Input Power (估算整机输入功率)**:
System Input Power derived on external power as Platform Power + signed Net Battery Terminal Power. Charging adds to the estimate; battery supplementation subtracts from it. The result omits charging-path and conversion losses that no available counter measures, so it reads low. Always carry the estimated qualifier; never present it as System Input Power.
_Avoid_: System Input Power, Measured Input Power

**Wall Input Power (墙端输入功率)**:
The power drawn by the external adapter from the AC supply. It additionally includes losses inside the adapter and is not the same as System Input Power.
_Avoid_: System Input Power, 市电侧输入功率

## Supply States

**Supply State (供电状态)**:
The battery driver's answer to "where is power coming from right now", resolved from the power-online / charging / discharging flags into exactly one of eight states (`BatterySupplyState`). The state, not the raw flags, decides which formula the app applies and which caption it shows.
_Avoid_: charging flag, AC status, battery mode

| State | Caption | Meaning | Whole-system row |
| --- | --- | --- | --- |
| External Power Idle | `AC / IDLE` | External power, battery neither charging nor discharging | Estimated System Input Power = Platform Power + 0 |
| External Power Charging | `CHARGING` | External power, battery charging (positive terminal power) | Estimated System Input Power = Platform Power + charge power |
| External Power Supplemented | `DISCHARGING` | External power, battery also feeding the system (negative terminal power) | Estimated System Input Power = Platform Power − supplementation; never clamped to zero |
| External Power Direction Unknown | `AC / UNKNOWN` | External power, firmware reports no battery direction | Estimated System Input Power with the battery term unavailable |
| Battery Discharging | `DISCHARGING` | No external power, battery discharging | System Load Power = Battery Discharge Power (measured) |
| Battery Direction Unknown | `ON BATTERY` | No external power, firmware reports no rate | System Load Power unavailable with reason |
| Unavailable | `BATTERY UNAVAILABLE` | No battery status at all | Unavailable with reason |
| Inconsistent | `BATTERY STATE INVALID` | Contradictory flags (charging and discharging, or charging without external power) | Unavailable with reason; never guessed |

**Battery Supplementation (电池补充)**:
The External Power Supplemented case: the adapter cannot cover the load, so the battery discharges while plugged in. Its negative terminal power must reduce the estimate, never be clamped.
_Avoid_: hybrid mode, fake charging

## Measurement Kinds

**Measured (实测)**:
A figure read from a hardware counter or firmware rate for exactly its named boundary (battery terminal, CPU package, Platform Power, on-battery System Load Power).
_Avoid_: accurate, exact

**Estimated (估算)**:
A figure derived from two or more measured figures and therefore missing whatever no counter measures (Estimated System Input Power, estimated battery current). Estimated values always carry the `≈` prefix and the muted colour in the UI; an estimate is never promoted to Measured and a missing figure is never substituted by a neighbouring boundary's number.
_Avoid_: approximate value shown as measured, Contract

## Example

Dev: "On AC the battery shows −5 W. Should the whole-system row just show Platform Power?"

Domain expert: "No. That is External Power Supplemented: the estimate is Platform Power + (−5 W), labelled 估算整机输入功率 with ≈. Dropping the negative term would overstate the input."
