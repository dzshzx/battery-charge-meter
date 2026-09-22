# Battery Charge Meter

一个轻量的 Windows 笔记本功率监视器。它直接读取 Windows/ACPI 电池传感器与
处理器能量计数器，每秒更新各口径功率；Release 同时提供免安装便携版与安装包。

> 功率术语、供电状态与测量类型在 `CONTEXT.md`；本页提供产品行为与发布说明，Agent 入口为 `AGENTS.md`。

## 功能

- 实时显示电池端净功率、电池端电压、估算电流和电量
- 实时显示 CPU 包功率（免驱动、免提权）
- 可选显示平台功率与估算整机输入功率（见下文“平台功率”）
- 默认显示整机功率；可在窗口或托盘菜单切换到电池端净功率，记住当前用户的选择
- 主数字、托盘、最近 60 秒曲线、30 秒时间加权平均和 60 秒峰值使用同一口径
- 最小化后隐藏到系统托盘，继续在后台监测
- 托盘文字图标显示所选功率的整数瓦数（大于等于 99.5 W 显示 `99+`），悬停显示口径、估算标识和两位小数
- 充电时托盘文字为白色，放电时为黄色
- 双击托盘图标恢复窗口，右键菜单可显示窗口或退出
- 支持 Per-Monitor V2 高 DPI，能够适配 100%–300% 缩放及跨屏切换

## 功率口径

- **整机输入功率（System Input Power）**：电能跨过电脑充电口进入整机的瞬时功率，
  近似由系统负载、电池端带符号净功率和机内转换损耗组成。
- **电池端净功率（Net Battery Terminal Power）**：电能跨过电池包端子的带符号功率；
  正值表示充电，负值表示放电。
- **CPU 包功率（CPU Package Power）**：处理器封装消耗的功率。
- **平台功率（Platform Power）**：处理器加主板路由进该计数器的平台供电总功率，
  不含电池充电功率。覆盖哪些供电轨由整机厂布线决定。
- **系统负载功率（System Load Power）**：CPU、GPU、屏幕、主板和外设等内部组件
  消耗的总功率；称为“主板功耗”会遗漏大量负载。
- **墙端输入功率（Wall Input Power）**：充电器从插座取得的功率，还包含充电器自身损耗。

近似功率平衡为：

```text
整机输入功率 ≈ 系统负载功率 + 电池端净功率（带符号） + 机内转换损耗
```

「整机用了多少电」在两种供电状态下是**两个不同的问题**，所以界面最后一行的
标题会随状态切换。

**用电池时**，充电口没有电流进来，输入功率恒为零，问它没有意义。有意义的是整机
在消耗多少——而这一档是**实测**的：机器用的每一瓦都从电池端子流出，别无来源。

```text
系统负载功率 = 电池端放电功率                        （实测）
```

**接外部电源时**，才需要问充电器送进来多少。Windows 没有任何接口直接实测充电口
边界的输入功率，只能估算：

```text
估算整机输入功率 = 平台功率 + 电池端净功率（带符号）  （估算）
```

平台功率取自 Intel Psys（`MSR_PLATFORM_ENERGY_STATUS`），Intel 明确该计数器不含
电池充电功率，两项因此不重叠。电池项**带符号**：充电时为正；而适配器功率吃紧时
电池会反过来放电补充负载，此时为负，输入功率相应低于平台功率。

该估算仍缺少充电路径与转换损耗（典型 5%–10%），所以读数偏低。估算值带 `≈` 前缀
并与实测值配色区分，不会当作实测输入功率。

各指标的数据源探测在运行时进行；探测不到就显示不可用并给出原因，不会用适配器
额定功率、PD 协商功率或电池功率顶替。可用 `--power-probe` 查看本机探测结果：

```text
BatteryChargeMeter.exe --power-probe report.txt 10
```

全部命令行开关（不带参数即启动窗口）：

| 开关 | 作用 |
| --- | --- |
| `--no-elevate` | 直接打开窗口，不主动请求管理员权限；继承启动者当前权限 |
| `--autostart` | 登录自启入口，直接进入托盘，不主动请求提权；由计划任务提供管理员权限 |
| `--remove-autostart` | 删除指向当前 EXE 的自启任务，供卸载器使用；不打开窗口或请求提权，失败退出码为 `1` |
| `--power-probe <report.txt> [秒数=5]` | 逐源探测并写出各口径的采样报告 |
| `--self-test <report.txt>` | 跑功率推导自检（供电状态 × 固件速率组合），通过退出码 0、失败 1；CI 与 `scripts/test.ps1` 依赖它 |
| `--third-party-notices <out.txt>` | 导出内嵌的第三方通知全文 |
| `--screenshot <out.png>` | 渲染主窗口截图 |
| `--dpi-preview <out.png> <目标 DPI> [返回 DPI]` | 渲染跨 DPI 切换预览（`scripts/test.ps1` 使用） |
| `--tray-preview <out.png> <瓦数> <charging\|discharging>` | 渲染托盘文字图标预览（`scripts/test.ps1` 使用） |

诊断、自检和预览命令均不弹 UAC；需要探测 PawnIO 时，从已有管理员权限的终端运行。
未知参数、缺失参数或非法组合以退出码 `2` 退出，不打开窗口。

## 显示模式与统计

首次启动选择“整机功率”：插电时显示带 `≈` 的“估算整机输入功率”，拔电时显示
“系统负载功率”。切换到“电池端净功率”后，充电为正、放电为负。主数字、托盘和
统计同步切换；明细始终保留电池、CPU 包、平台及整机功率。CPU 包属于平台的一部分，
不额外加到整机估算中。电压和估算电流属于电池端，不表示充电口电压或电流。

主数字显示最新读数。30 秒平均值按实际经过的时间加权；60 秒峰值按绝对值选取并
保留符号，曲线采用同样的时间轴。采样不足时显示已覆盖时长；切换模式、供电状态
变化或采样中断超过 5 秒会重新开始统计。数据不可用时显示 `N/A`、托盘 `--`，
曲线断开，缺失区间不参与平均；不会自动改用另一种功率或补零。

电池固件的更新节奏可能比界面慢，相同读数可持续多个刷新周期；平均值用于观察
趋势，并不提高硬件测量精度。诊断报告记录应用观察时间、供电状态、原始电池字段、
权限、来源和采样窗口；电池管理系统内部采集时刻仍未知。

显示模式保存在当前 Windows 账号的 `HKCU\Software\BatteryChargeMeter`。
同账号 UAC 确认后仍使用同一设置；输入另一管理员账号凭据时，使用该账号的设置。
设置读取失败使用默认整机模式，写入失败不影响本次使用。

## 运行

从 [Releases](https://github.com/dzshzx/battery-charge-meter/releases/latest) 选择一种形式：

- 下载 `BatteryChargeMeter-vX.Y.Z-windows-setup.exe`，按向导安装到当前用户并从
  开始菜单启动；安装本身不需要管理员权限。
- 下载 `BatteryChargeMeter-vX.Y.Z-windows.exe`，无需安装，直接双击运行。

程序未使用商业代码签名证书。Windows 首次运行若显示 SmartScreen 提示，
请先核对仓库来源，并使用所选产物旁的 `.sha256` 文件校验，再决定是否运行。

默认打开窗口时会请求一次 UAC 管理员确认，已经提权的启动不重复请求。确认后读取
可用的平台功率和整机估算；取消或启动失败则继续普通运行，窗口直接说明原因并提供
重新请求管理员权限的按钮。普通模式仍可读取电池和 CPU 包功率，纯电池供电时也能
显示系统负载功率；插电时平台功率不可用则整机估算保持 `N/A`。

EXE manifest 继续使用 `asInvoker`，由界面启动流程主动请求提权，因此可以在取消后
继续运行。安装包仍按当前用户安装，不要求管理员权限；完成页启动应用时才遵循上述
请求流程。应用不会安装驱动，也不会通过提权失败反复重启。

窗口底部和托盘菜单提供“开机自启（登录后）”，默认关闭。以管理员身份打开应用后
勾选一次，后续登录当前 Windows 账号时自动以管理员权限运行，直接在托盘显示读数，
无需再次确认 UAC。普通模式下启用时会提示先使用窗口里的管理员重启按钮。
手动再次打开同一路径的程序会唤回已有窗口，重复的自启启动会直接退出。

自启使用 Windows 任务计划程序，为当前用户注册 `BatteryChargeMeter.Logon.<SID>`，
不保存密码；允许电池供电时启动，拔电不停止，也没有空闲、网络或运行时限条件。
开关读取实际任务状态；任务条件被修改时会提示重新勾选修复。实现遵循 Windows 的
[任务权限模型](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks)。

便携版请先放到固定目录再启用；移动后需从新位置重新启用，并确认替换原路径。
取消勾选只删除指向当前 EXE 的任务。确认卸载后，卸载器只清理本安装路径的任务；
取消卸载保留自启配置，清理失败会中止卸载并保留程序，也不会删除另一份程序的自启任务。应用不接管其他手工配置的
注册表或启动文件夹项目。

## 平台功率（可选）

电池端净功率与 CPU 包功率开箱即用，不需要任何额外组件。**平台功率**与
**估算整机输入功率**额外需要 [PawnIO](https://pawnio.eu/) 驱动并以管理员身份运行——它是免费、
开源、经数字签名的通用内核驱动，请自行从官网下载安装包安装；本程序不会代为
安装驱动，也不会在未经你同意的情况下改动系统。

驱动所需的 `IntelMSR.bin` 模块已内嵌在应用 EXE 内，无需另行下载。便携版可以只
保留这个 EXE；安装包还会安装第三方通知与 LGPL-2.1 许可证。若要换用自己编译或
更新版本的模块，把同名文件放在应用 EXE 同目录即可覆盖内嵌副本（详见
`third_party/NOTICE.md`）。

可用 `BatteryChargeMeter.exe --third-party-notices notices.txt` 从 EXE 提取第三方
通知与 LGPL-2.1 全文。每个 Release 还会在 EXE 旁提供相同通知和与内嵌模块精确
对应的 `PawnIO.Modules-0.2.10-source.zip` 源码包。

未安装驱动时，这两个指标显示不可用并说明原因，其余功能不受影响。

能否读到平台功率还取决于主板是否把 Psys 信号布线到处理器，这是整机厂的硬件
设计选择，逐机型不同。程序在运行时探测实际计数器是否推进来判定，不按机型
白名单假定；有 EMI CPU 包功率时会用它交叉校验能量单位，校验不通过就不显示
平台功率。没有 EMI 时仍显示 Psys，但来源提示会明确标记“未交叉验证”。

## 配置文件

当前版本不需要 `BatteryChargeMeter.exe.config`。旧版本中的这个文件不是用户设置，
只用于声明 CLR/.NET Framework 4.7 启动目标、兼容旧 CLR 2 激活策略，以及 WinForms
的 Per-Monitor V2 DPI 行为。本项目没有 CLR 2 或混合模式依赖；目标框架信息现已写入
程序集，DPI awareness 由 EXE 内嵌 manifest 声明，跨屏缩放由程序直接处理
`WM_DPICHANGED`。因此程序不依赖 `.exe.config` 旁置文件；便携 EXE 与安装包使用
同一个应用程序集。

## 系统要求

- Windows 10 或 Windows 11
- .NET Framework 4.7 或更高版本
- 笔记本固件需要通过 ACPI/WMI 暴露电池充放电速率

部分机型只报告充电状态，不报告实时功率；这种情况下界面会显示 `N/A`。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\scripts\build.ps1
```

脚本使用 Windows 自带的 .NET Framework C# 编译器，将应用 EXE 写入 `dist/`。
应用图标为 `src/BatteryChargeMeter.ico`，由 `python scripts/make-icon.py`
生成（Pillow ≥ 8.2）；修改图形后重跑该脚本并连同 ICO 一起提交。
`dist/` 是本地构建目录，不纳入版本控制。

## 发布

项目使用 GitHub Actions 构建和发布：

- 推送到 `master` 或向 `master` 提交 Pull Request 时，CI 会在 Windows
  runner 上执行一次完整构建。
- 推送符合 `vX.Y.Z` 格式的 tag 时，Release 工作流会校验 tag 与 manifest
  版本一致、tag 为带注解 tag 且位于 `master`，重新构建程序，同时生成便携 EXE、
  当前用户安装包及各自的 SHA-256 校验文件，并随第三方通知和对应源码包创建
  GitHub Release。

发布新版本时，先从远端 tag 历史生成完整的只读版本计划，再修改
`src/BatteryChargeMeter.manifest` 的四段版本号：

```powershell
python scripts/version_plan.py plan `
  --repository dzshzx/battery-charge-meter `
  --target v=X.Y.Z
```

版本精确递增一个 patch 可沿用已有发布授权；minor、major 或跳号 patch 必须暂停，等用户
明确确认输出的完整“基线到目标”计划。基线未知和降级会直接拒绝。候选通过任务分支和 PR 合入 `master`，
等待该同一 SHA 的 Windows CI 全绿，再确认远端 tag 未占用并创建匹配的带注解 tag。
以下以 `v1.2.1` 为示例基线，按获准计划选择对应的一组命令。
精确递增 patch 到 `v1.2.2` 时，manifest 为 `1.2.2.0`：

```powershell
# 等待 master 上这个 SHA 的 CI 成功
git tag -a v1.2.2 -m "Release v1.2.2"
git push origin v1.2.2
```

已经明确确认的 minor、major 或跳号 patch 计划使用带摘要的 tag 命令。
例如获准从 `v1.2.1` 升到 `v1.3.0` 时，manifest 为 `1.3.0.0`：

```powershell
git tag -a v1.3.0 -m "Release v1.3.0" `
  -m "Version-Approval: sha256:<version_plan.py 输出的摘要>"
git push origin v1.3.0
```

Release 会排除本次 tag 并从远端记录重建计划；基线或目标变化时，在创建 Release 前拒绝。
摘要只证明计划一致，不构成独立的身份审批。

远端发布 tag 不移动、不复用；若 tag 后才发现失败，修复后发布下一个 patch。

发布产物只存在于 GitHub Release，不直接提交到仓库。

## 项目结构

```text
.
├── .github/     # CI 与 Release 工作流
├── docs/        # 功率数据源与边界研究记录
├── installer/   # Inno Setup 安装包定义
├── scripts/     # 构建、测试与发布打包脚本
├── src/         # C# 源码与 DPI manifest
├── third_party/ # 内嵌模块、许可证、通知与对应源码
├── LICENSE      # 本项目代码的 MIT 许可证（第三方组件见 third_party/）
├── AGENTS.md    # 修改与验证时必须保持的项目约束
├── CONTEXT.md   # 功率口径的统一术语
└── dist/        # 本地构建输出（不纳入版本控制）
```

## 许可证

本项目代码以 MIT 许可证发布（见 `LICENSE`）。内嵌的 PawnIO `IntelMSR.bin` 模块为 LGPL-2.1：其通知、许可证文本与对应源码包随 Release 资产和 `third_party/` 一并提供，见 `third_party/NOTICE.md`。
