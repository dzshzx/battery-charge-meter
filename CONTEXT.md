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
The power consumed by the computer's internal components while operating, excluding power flowing into the battery.
_Avoid_: Motherboard Power

**Wall Input Power (墙端输入功率)**:
The power drawn by the external adapter from the AC supply. It additionally includes losses inside the adapter and is not the same as System Input Power.
_Avoid_: System Input Power
