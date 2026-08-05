# Windows 笔记本整机输入功率获取路径

## 结论

Windows 没有一个在普通笔记本上必然可用、直接返回“充电口边界整机输入功率”的通用 API。标准接口中，只有 **PMI/ACPI Power Meter** 或 **EMI** 在机器确实配有相应电表、固件/驱动又把正确边界暴露出来时，才可能给出该实测值；否则应报告 `Unsupported`，不能把电池功率、USB-PD 合同功率或软件估算标成 `System Input Power`。

这里的目标边界应定义为：电能跨过笔记本 DC/USB-C 入口、进入内部 power path 之前的实时有功功率。它同时包含直接供给系统和流向电池的功率，但不包含外置适配器的转换损耗。

## 路径判定

| 路径 | 返回量与测量边界 | 实测/估算 | 可用条件 | 能否通用得到整机输入 |
|---|---|---|---|---|
| `IOCTL_BATTERY_QUERY_STATUS` / `BATTERY_STATUS` | `Voltage` 明确是电池端子电压，`Rate` 是该电池当前充/放电速率；正值充电、负值放电，且可能未知或只是相对单位。[Microsoft: `BATTERY_STATUS`](https://learn.microsoft.com/en-us/windows/win32/power/battery-status-str) | 电池/驱动报告值 | 有电池类设备且固件实现相应字段 | **否**。边界在电池端子，不含系统从适配器直接取走的功率；满电时 `Rate≈0` 也不代表整机输入为 0。 |
| Battery WMI / 系统电池 API | `BATTERY_WMI_STATUS` 仍是电池的 `ChargeRate`、`DischargeRate`、`Voltage`；`SYSTEM_BATTERY_STATE.Rate` 也是电池充放电率。[Microsoft: `BATTERY_WMI_STATUS`](https://learn.microsoft.com/en-us/windows/win32/api/batclass/ns-batclass-battery_wmi_status)、[`SYSTEM_BATTERY_STATE`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_battery_state) | 电池/驱动报告值 | OEM 电池驱动提供字段；部分电池只报放电率 | **否**，只是同一电池边界的不同入口。 |
| `Win32_Battery`、`GetSystemPowerStatus`、WinRT `PowerManager` | 电量百分比、预计续航、充电/供电状态；没有实时瓦数。[Microsoft: `Win32_Battery`](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-battery)、[`SYSTEM_POWER_STATUS`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status)、[WinRT `PowerManager`](https://learn.microsoft.com/en-us/uwp/api/windows.system.power.powermanager) | 状态/估算 | 普遍可用，但字段仍可为 unknown | **否**。 |
| PMI / `Win32_PowerMeter` | 底层电表当前功率；WMI 的继承字段 `CurrentReading × 10^UnitModifier`，`BaseUnits` 为 W，`MeterType=0` 表示“相对被测对象的输入功率”。可关联到整机或子系统。[Microsoft: `Win32_PowerMeter`](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/powermeterprov/win32-powermeter)、[`CIM_NumericSensor`](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/cim-numericsensor) | **硬件实测**（通常为平均值） | Windows 7+；必须存在实现 PMI 的 WDM 驱动/硬件电表。系统级 PMI 的 `MeteredHardware` 为空；否则它列出被该回路供电的设备。[Microsoft: PMI](https://learn.microsoft.com/en-us/windows-hardware/drivers/powermeter/power-meter-interface)、[`PMI_METERED_HARDWARE_INFORMATION`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/pmi/ns-pmi-_pmi_metered_hardware_information) | **有条件可以**。只有 `MeterType=input` 且证实电表覆盖整机、物理位置就是所需入口边界时才成立；“input”本身不保证位于笔记本充电口。 |
| ACPI `ACPI000D` Power Meter | `_PMC` 声明单位、input/output、精度、采样/平均窗口；`_PMM` 返回包含功率因数的最新有功功率；`_PMD` 声明被测设备。[ACPI 6.5 §10.4](https://uefi.org/specs/ACPI/6.5/10_Power_Source_and_Power_Meter_Devices.html#power-meters) | **硬件/固件实测** | 固件实现 `ACPI000D` 及方法；Windows 的 `ACPIPMI.SYS` 可把 ACPI 4.0 Power Meter 接入 PMI。[Microsoft: PMB overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/powermeter/) | **有条件可以**，判据同 PMI。ACPI 允许测整机或设备集合，并未规定每台笔记本必须有，也未固定传感器在充电口。 |
| ACPI AC Adapter / Power Source | `_PSR` 只有 online/offline；`_PIF` 的 Maximum Input/Output Power 是**额定最大值**，不是当前功率。[ACPI 6.5 §10.3](https://uefi.org/specs/ACPI/6.5/10_Power_Source_and_Power_Meter_Devices.html#ac-adapters-and-power-source-objects) | 状态/铭牌值 | 固件暴露 `ACPI0003` | **否**。 |
| EMI (`GUID_DEVICE_ENERGY_METER`) | 板载电表在某条 rail 上测电压、电流并累计能量；两次 `AbsoluteEnergy/AbsoluteTime` 差分得到该 rail 的区间平均功率。[Microsoft: EMI](https://learn.microsoft.com/en-us/windows-hardware/drivers/powermeter/energy-meter-interface)、[`IOCTL_EMI_GET_MEASUREMENT`](https://learn.microsoft.com/en-us/windows/win32/api/emi/ni-emi-ioctl_emi_get_measurement) | **硬件实测** | Windows 10+ 且 OEM 驱动注册 EMI；V2 的通道名由设备提供。[Microsoft: `EMI_CHANNEL_V2`](https://learn.microsoft.com/en-us/windows/win32/api/emi/ns-emi-emi_channel_v2) | **有条件可以**。只有存在明确代表入口总 rail 的通道并由 OEM 文档确认边界时才可采用；常见 CPU/GPU/display 等 rail 只覆盖子系统，简单求和会漏掉/重复供电与转换损耗。 |
| E3、SRUM、WPT/ETW、`powercfg` 报告 | E3 给软件进程归因能耗；有匹配 EMI rail 时优先用实测，否则用软件估算模型。[Microsoft: EMI→E3](https://learn.microsoft.com/en-us/windows-hardware/drivers/powermeter/rail_naming) `powercfg /srumutil` 导出 Energy Estimation 数据，`/energy`、`/systempowerreport` 是诊断报告。[Microsoft: `powercfg`](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options) TraceProcessing 也明确区分 E3 “estimated” 与 EMI “measured”。[Microsoft: trace data](https://learn.microsoft.com/en-us/windows/apps/trace-processing/tutorial) | **混合：默认估算，EMI 存在时部分实测** | Windows 10/相应跟踪或历史数据；精度依模型和硬件 | **否（不能作为实时通用源）**。它是诊断/归因层，底层仍取决于 EMI，且组件能耗不等于充电口输入。 |
| Windows 性能计数器 / PerfMon | PMB 信息可供 PerfMon 使用，但来源仍是 UMPS→PMI，并不会凭软件生成一个整机瓦数传感器。[Microsoft: User-Mode Power Service](https://learn.microsoft.com/en-us/windows-hardware/drivers/powermeter/user-mode-power-service) | 取决于底层 PMI | 必须已有 PMI 电表 | **没有独立能力**；与 PMI 同一可用性和边界限制。 |
| USB-C / USB-PD PDO、RDO、UCSI | PDO 是 Source/Sink 能力，RDO/“Negotiated Power Level”是合同请求/上限，不是瞬时实际电流。Windows UCSI 要求 `GET_PDOS` 和合同变化通知，面向 BIOS/EC、驱动与故障提示。[Microsoft: UCSI](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/ucsi)、[USB-IF: USB PD 3.2 specification](https://www.usb.org/sites/default/files/USB_PD_R3.2_V1.2_2.zip) | 合同/能力值 | USB-C PD + UCSI 固件/驱动 | **否**。`V_contract × I_operating/max` 最多是合同值或上限，不能当实际输入。 |
| USB-PD PPS Status | PD 的 PPS Status 可由支持 PPS 的 Source 报输出电压/电流状态，但只适用于相应 PD/PPS 会话；测点在 Source 输出侧，且 Windows 没有面向普通应用的通用实时遥测 API。Windows 的 UCSI 测试接口默认禁用以防零售系统未授权访问。[USB-IF Source Power Test §6.5.10](https://www.usb.org/sites/default/files/USB-C%20Source%20Power%20Test%20Specification%202021%2005%2024.pdf)、[Microsoft: UCSI test interface](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/ucsi#ucsicontrole-exe) | 可能为 Source 实测/状态值 | PPS Source 支持、PD 控制器可取消息、OEM/驱动另行暴露 | **不通用，也不保证等于笔记本入口**（还隔着线缆，且普通 app 无标准入口）。 |
| USB HID Power Device / 外置串联电表 | HID Power Device Usage 定义了实际 `Voltage`、`Current` 和 `ActivePower`（W）；Windows 用户态可发现 HID collection 并读取 report。[USB-IF: HID Power Devices §4.1.2](https://www.usb.org/sites/default/files/pdcv11.pdf)、[Microsoft: opening a HID collection](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/finding-and-opening-a-hid-collection) | **硬件实测** | 另加串联表/智能电源，且设备实际实现相应 HID usage；普通 PD 充电器并不因此自动成为 HID 电表 | **借助外设可以**，但不属于笔记本内建通用能力；还需按电表放置位置定义边界。 |
| OEM EC / 充电芯片 / 厂商 SDK | 充电芯片本身可能有 VBUS、输入电流和系统/电池电压 ADC；例如 TI BQ25790 明确提供输入/电池/系统监测。[TI: BQ25790](https://www.ti.com/product/BQ25790) | 可为**硬件实测** | 特定主板器件；需 OEM EC 协议、ACPI/WMI 方法或签名驱动安全地读取并校准 | **可做型号专用 provider**，不是 Windows/跨机型标准。应由 OEM 资料确认寄存器、缩放、刷新率和测点。 |
| Intel `Psys` / RAPL platform domain | 兼容的充电电路可把 `Psys` 信号经 SVID 送入处理器，表示“处理器 + 其余平台”的热相关总功耗；Intel 明确说明该数据**不含电池充电功率**，而且由整机厂选择实现、默认关闭。[Intel Core Ultra 200H/200U: Platform Power Control](https://edc.intel.com/content/www/us/en/design/products-and-solutions/processors-and-chipsets/core-ultra-200h-and-200u-series-processors-datasheet-volume-1-of-2/platform-power-control/) Linux `turbostat` 把 Arrow Lake-H 映射到 `RAPL_PSYS`，用 `MSR_PLATFORM_ENERGY_STATUS` (`0x64D`) 输出 `SysWatt`。[Linux upstream: `turbostat.c`](https://github.com/torvalds/linux/blob/master/tools/power/x86/turbostat/turbostat.c) | 平台信号与累计能量计数器；具体精度依 OEM 实现 | 主板实际布线并由 BIOS 启用；Windows 用户态还需有权限读取 MSR 的签名内核驱动。LibreHardwareMonitor 的上游实现会在 Arrow Lake 上尝试把该计数器显示为 `CPU Platform`，但其当前 Windows 实现依赖另行安装的 PawnIO 驱动。[LibreHardwareMonitor: `IntelCpu.cs`](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs) | **能补齐主板侧平台功耗，但不是充电口输入功率**。可将 `Psys` 单独显示为“平台功耗（不含电池充电）”；`Psys + 电池充电功率` 只能作为入口功率近似下界，还缺充电路径和内部 DC/DC 损耗。 |
| CPU/GPU 厂商传感器 | Intel RAPL 报处理器各 power domain 的累计能量；NVML 报 GPU 及其相关电路功率。[Intel: RAPL](https://www.intel.com/content/www/us/en/developer/articles/technical/software-security-guidance/advisory-guidance/running-average-power-limit-energy-reporting.html)、[NVIDIA: `nvmlDeviceGetPowerUsage`](https://docs.nvidia.com/deploy/nvml-api/group__nvmlDeviceQueries.html) | 芯片/板级实测或模型化遥测，依平台 | 特定 CPU/GPU、驱动与权限 | **否**。边界只覆盖组件；相加仍缺显示、内存、存储、风扇、转换损耗等，也可能重叠。 |

## REDMI Book Pro 14 2025 的在线证据

- 小米官方 `TM2411` [驱动页](https://www.mi.com/service/notebook/drivers/TM2411)确实提供“英特尔平台监控技术”包；包内是 `IntcPMT.sys` 3.1.2.6，并匹配 Arrow Lake/Meteor Lake 的 `PCI\VEN_8086&DEV_7D0D`、`PCI\VEN_8086&DEV_AD0D`。这证明该机型系列准备了 Intel PMT 传输层，但 Intel 对 PMT 的公开定义只是通用遥测框架，可能用于分析功耗问题，并未承诺其中存在 DC-in/VBUS 实时功率字段，也没有公开普通应用可直接调用的该字段 API。[Intel PMT specification](https://www.intel.com/content/www/us/en/content-details/710389/intel-platform-monitoring-technology-intel-pmt-technical-specification.html)
- 上游 Linux 的 [REDMI WMI 驱动](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/redmi-wmi.c)目前只处理 OEM 功能/事件；没有公开整机瓦数读取。因此在线资料尚不能证明小米 EC/WMI 已把充电芯片的输入电流、电压或功率暴露给 Windows。
- 该机型最值得实机验证的顺序是：标准 `ACPI000D`/PMI → EMI 的入口总 rail → `Psys` (`0x64D`) → PMT 元数据/通道。前两者若存在可得到真正入口实测；`Psys` 若存在可得到不含充电的整个平台功耗；PMT 仅凭驱动存在还不能判定有可用瓦数。

## 开源与现成方案横向比较

广泛检索后的关键发现不是一个“无需权限即可读取所有 Windows 笔记本充电口功率”的现成库，而是几条边界不同、可组合的数据源。尤其是 Intel `Psys`：它在目标 Arrow Lake-H 平台上并非理论猜测，Intel PCM、WinPowerMonitor 和 LibreHardwareMonitor 都已有公开实现；真正的门槛是 Windows 的 MSR 访问必须经过内核驱动。

| 方案 | 实际能提供什么 | 部署、授权与成熟度 | 对本项目的判断 |
|---|---|---|---|
| [Intel PCM](https://github.com/intel/pcm) | PCM 已把 Arrow Lake (`ARL`) 列为支持 system-energy metric 的平台，并从 `MSR_SYS_ENERGY_STATUS` (`0x64D`) 读取 32 位累计能量。[平台判定](https://github.com/intel/pcm/blob/c284f1412ec21cdb640f1a358418c1eb10b032d5/src/cpucounters.h#L2660-L2677)、[读数实现](https://github.com/intel/pcm/blob/c284f1412ec21cdb640f1a358418c1eb10b032d5/src/cpucounters.cpp#L1781-L1785) | Windows 文档要求编译、签名并安装 `msr.sys`；测试证书还需要 test-signing mode。[Windows HOWTO](https://github.com/intel/pcm/blob/c284f1412ec21cdb640f1a358418c1eb10b032d5/doc/WINDOWS_HOWTO.md#L7-L18) | **证明目标 CPU 路线成立，但不能直接塞进便携 EXE。** 适合作为算法和机型支持依据，不适合作为面向普通用户的驱动发行物。 |
| [WinPowerMonitor](https://github.com/joular/WinPowerMonitor) + [Hubblo Windows RAPL driver](https://github.com/hubblo-org/windows-rapl-driver) | 先尝试读取 `0x64D` 的 Psys；不可用时退回 package/DRAM RAPL。[实现](https://github.com/joular/WinPowerMonitor/blob/9dcdd5160089b6c24a8e293e616e61c48e47c00c/src/hubblo_rapl.rs#L170-L231) | 应用为 GPL-3.0；驱动为 Apache-2.0，但公开 release 驱动是测试签名，正常 Windows 需开启测试模式。 | **很好的开源参考实现，不能作为正常 release 的依赖。** 其 fallback 组件和也不应标成整机输入功率。 |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu/) | LHM 会读取 `0x64D` 并显示为 `CPU Platform`。[定义与传感器](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/0ee5fec89915454c8cc68c571f2c54cdced08ad9/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs#L491-L510) PawnIO 的 Intel MSR 模块也已允许该寄存器。 | LHM 为 MPL-2.0。PawnIO 官方版驱动有数字签名，但仍需一次管理员安装；自定义模块、驱动再分发和许可证兼容性需单独审查。 | **当前最现实的“机内高级数据源”。** 先用 LHM/PawnIO 实机验证目标机是否出现 `CPU Platform`；验证成功后再决定是调用 LHM、写最小模块，还是做外部桥接。不能静默安装驱动。 |
| [HWiNFO](https://www.hwinfo.com/) shared memory / SDK | 若硬件确实暴露 Psys、智能 PSU 或其他总功率传感器，HWiNFO 可作为统一传感器桥。作者也明确指出：没有总功率硬件时，组件功率之和不能准确替代整机总功率。[说明](https://www.hwinfo.com/forum/threads/total-power-usage-of-a-system.10566/) | 免费版 shared memory 有连续运行时限；打包、嵌入或商用需要相应授权。[许可](https://www.hwinfo.com/licenses/) | **适合可选诊断桥，不适合默认或捆绑依赖。** 它不会凭软件创造不存在的入口传感器。 |
| [BatteryMonitor-Windows](https://github.com/ASG49/BatteryMonitor-Windows) + TC66 | 一个与本项目几乎同题的 MIT C# 应用：同时读 Windows 电池 BMS 和 TC66 USB-C 串联表，TC66 通过 COM 口提供实测电压、电流、功率和累计能量，并直接发布便携 EXE。[使用说明](https://github.com/ASG49/BatteryMonitor-Windows/blob/678b4e73c38357025034d8f2b4f8b790b7cfdde7/BatteryMonitor_Manual_v1.3.md#the-tc66-usb-power-meter-optional) | 无内核驱动；TC66 协议已有 [libsigrok 文档](https://sigrok.org/wiki/RDTech_TC66C)。但该应用名为 `Psys` 的字段只是用户手填的 4–5 W 基线，不是 Intel Psys 计数器。 | **证明“默认单 EXE + 可选外置表 provider”是成熟产品形态。** 这是最快能得到真实充电口功率的参考路径。 |
| FNIRSI FNB58 | 可串联实测 VBUS 电压、电流和功率。已有 Windows C++ 项目直接通过系统 HID 驱动读取，无需专用驱动；另有 MIT 的跨平台 BLE 工具，明确支持 Windows。[BCI2000 HID logger](https://www.bci2000.org/mediawiki/index.php/Contributions%3AFnirsiUSBLogger)、[`fnb58` BLE 工具](https://pypi.org/project/fnb58/) | 开放实现比 TC66 更新；HID 与 BLE 是两套不同协议。具体硬件版本、量程和高功率 PD 透传能力必须按目标充电器复核。 | **比串口表更接近即插即用的外置 provider。** 可优先评估 HID，其次 BLE；仍需真实设备做协议和高负载稳定性测试。 |
| AVHzY CT-3/C3、POWER-Z KM003C | 均可实测并记录 USB-C V/I/W。AVHzY 官方提供 Windows 软件和 C# 通信库；CT-3 标称 0–29 V、0–6 A。[官方页面](https://forum.avhzy.com/forum.php?mod=viewthread&tid=190) KM003C 已有 MIT 的逆向工具 [VoltReaver](https://github.com/dan8915610818/VoltReaver)。 | AVHzY 通信协议未正式公开，库的再分发授权需核实。VoltReaver 很新，Windows 还需用 Zadig 替换为 WinUSB 驱动。 | **可作为扩展兼容列表，不宜首发。** 前者有授权不确定性，后者会改变设备驱动且成熟度低。 |
| [Dr. PD](https://hackaday.io/project/205495-dr-pd-usb-pd-protocol-analyzer) 等开放硬件 | 串联测 VBUS V/I/W 并同步解析 PD，支持 SPR/EPR/PPS/AVS，最高 48 V/5 A/240 W；浏览器本地控制，无需安装驱动。 | 硬件、固件、主机软件都计划开源，但截至 2026-07 仍在 DVT/众筹前阶段。 | **未来最完整、最透明的外置方案，当前不能作为可购买即用的依赖。** |
| Shelly、Tasmota 智能插座，或 NUT 兼容 UPS/PDU | 测适配器的**市电侧输入有功功率**。Shelly 本地 RPC 的 `Switch.GetStatus` 返回 `apower`；NUT 统一字段为 `realpower`。[Shelly API](https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/Switch/)、[NUT](https://github.com/networkupstools/nut) | 通过局域网 HTTP/协议读取，不需要笔记本驱动，也不破坏单 EXE；需要额外设备和网络配置。 | **最容易接入的外部 provider，但边界在 AC 墙插。** 数值包含适配器自身损耗，不能标成充电口/DC 输入功率。 |
| PowerAPI、SmartWatts、Joulemeter 类软件模型 | 用性能计数器、利用率和回归模型估算组件或软件功耗；可用电池放电率或外部表做机器级校准。[PowerAPI](https://powerapi.org/reference/overview/)、[Joulemeter 论文](https://www.microsoft.com/en-us/research/wp-content/uploads/2010/06/JoulemeterVM.pdf) | 无法在接入电源时从操作系统观测到真实标签；不同亮度、风扇、外设、温度和电源转换效率都会使模型漂移。现有 PowerAPI 栈也不是可直接嵌入本 Windows 小程序的方案。 | **只能提供明确标注的“估算值”。** 可作为实验模式，不能作为默认的“充电口功率”。 |

另有看似反例的商业应用 “USB Connection Information”，但其官网明确把 USB-C PD 电压/电流/功率标为 [macOS only](https://usbconnectioninformation.com/)。Windows 版的存在并不说明普通 Win32 应用能读取实时 PD 电流。

## 推荐的产品数据模型

不要让一个“功率”标签承载不同物理量。建议固定使用以下专业术语：

| 中文 UI 名称 | 英文/内部名 | 定义 |
|---|---|---|
| **电池端充电功率** | Battery-terminal charging power | 当前项目已有值；电池驱动在电池端子边界报告的净充电功率。它不等于电芯实际储能速率，也不包含主板功耗。 |
| **平台功耗（不含电池充电）** | Platform power / Intel Psys | 处理器与其余平台的 Psys 总功耗；Intel 明确排除电池充电。 |
| **估算充电口输入功率** | Estimated DC input power | `Psys + max(电池端充电功率, 0)`。这是近似值，仍缺充电路径/DC-DC 损耗并受两个传感器的边界和校准影响。 |
| **充电口实测输入功率** | Measured DC input power | 串联 USB-C/DC 电表在笔记本入口测得的实时 `V × I`；这是用户所说“充电口功率”的正确名称。 |
| **USB-PD 协商功率** | USB-PD negotiated power / contract limit | 协商合同的请求值或上限，不是当前实际流过的功率；只能作为连接信息展示。 |
| **市电侧输入功率** | AC wall input power | 智能插座/UPS 在适配器之前测得，包含适配器损耗。 |

近似功率平衡为：

```text
充电口实测输入功率
  = 平台功耗
  + 电池端充电功率
  + 充电与内部电源路径损耗

市电侧输入功率
  = 充电口实测输入功率
  + 外置适配器损耗
```

因此 `Psys + 电池端充电功率` 应显示为“估算”，不能把缺失的损耗项暗中当作 0。

## 推荐的落地顺序

1. **默认仍保持一个直接运行的 EXE。** 内置 `BatteryTerminalProvider`，把当前数值更名为“电池端充电功率”；自动探测 PMI/EMI，但探测不到就保持 `Unsupported`。
2. **先做一次目标机 Psys 验证。** 经用户明确同意后安装官方签名 PawnIO，用 LibreHardwareMonitor 或最小探针确认 REDMI Book 上是否存在 `CPU Platform`。成功后可增加可选 `PlatformPsysProvider`，并给出“平台功耗”和“估算充电口输入功率”两个明确标签。
3. **把真实入口测量设计成可选 provider。** 最快原型可参考 BatteryMonitor-Windows 接 TC66；更理想的 Windows 体验是 FNB58 HID。外设不存在时不影响默认 EXE 启动。
4. **允许网络型墙插 provider，但单列边界。** Shelly/Tasmota/NUT 只显示“市电侧输入功率”，可用于计算适配器效率或交叉校准，不能替代 DC-in。
5. **PD 合同与软件模型只做辅助信息。** `PdContractProvider` 只显示协商档位；回归模型必须标为 `Estimated`，并附带校准时间、数据源和置信度。

推荐所有 provider 返回统一元数据：`valueW`、`boundary`、`measurementKind` (`Measured/Estimated/Contract`)、`source`、`sampleWindow`、`timestamp`、`accuracy/quality`。UI 再按边界决定名称，而不是让 provider 自己都声称返回“整机功率”。

## 可靠 provider 的实现门槛

1. **只接受可证明的物理边界。** PMI 必须同时满足 measurement 支持、`MeterType=input`、整机关联（或 systemwide `MeteredHardware`）；EMI/OEM 通道必须由固件或 OEM 资料明确为 DC jack/VBUS 入口总 rail。仅凭通道名猜测不够。
2. **能力发现而非假定存在。** 先枚举 `Win32_PowerMeter`/PMI 与 `GUID_DEVICE_ENERGY_METER`；均无合格实例时返回 `Unsupported`。型号专用 EC provider 必须白名单到明确的硬件/固件协议版本。
3. **按接口正确换算。** PMI 应应用 `BaseUnits`、`UnitModifier`、平均窗口和 accuracy；EMI 用两次累计能量与时间差计算平均 W，并处理计数器回绕、重置、时间倒退和采样窗口。
4. **处理多入口与混合供电。** 多个 USB-C/DC 输入只能在确认各通道互不重叠且处于同一边界后求和；电池同时充/放电不改变“入口电表”为事实源的原则。
5. **暴露数据质量。** 返回来源、`Measured/Estimated`、边界、采样时刻/窗口、精度和 stale/unknown 状态。E3、PD 合同、额定功率、CPU/GPU 之和与 `BATTERY_STATUS.Rate` 可单独展示，但不得降级冒充 `System Input Power`。

## 最终判断

可以写一个“**发现标准硬件电表则工作**”的 Windows provider，但不能写一个“**所有 Windows 笔记本都能得到整机入口瓦数**”的 provider。要可靠支持目标机器，至少需要以下之一：

- OEM 在充电入口总 rail 上布置电表，并通过 PMI/ACPI Power Meter 或 EMI 正确暴露；
- OEM/芯片厂提供有文档、可安全调用的 EC/充电芯片遥测接口与 Windows 驱动。

没有这些硬件与暴露层时，软件无法从电池净充放电率或 PD 协商值唯一反推出充电口实时输入功率。
