# Battery Charge Meter

一个轻量、免安装的 Windows 笔记本功率监视器。它直接读取 Windows/ACPI
电池传感器与处理器能量计数器，每秒更新各口径功率。

## 功能

- 实时显示电池端净功率、电池端电压、估算电流和电量
- 实时显示 CPU 包功率（免驱动、免提权）
- 可选显示平台功率与估算整机输入功率（见下文“平台功率”）
- 最近 60 秒功率曲线、平均值与峰值
- 最小化后隐藏到系统托盘，继续在后台监测
- 托盘文字图标显示整数瓦数，悬停显示精确到 `0.01 W` 的功率
- 充电时托盘文字为白色，放电时为黄色
- 双击托盘图标恢复窗口，右键菜单可显示窗口或退出
- 支持 Per-Monitor V2 高 DPI，能够适配 100%–300% 缩放及跨屏切换

## 功率口径

- **整机输入功率（System Input Power）**：电能跨过电脑充电口进入整机的瞬时功率，
  包含系统负载、电池端充电功率和机内转换损耗。
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
整机输入功率 ≈ 系统负载功率 + 电池端充电功率 + 机内转换损耗
```

Windows 通用电池接口只报告电池端功率，不能据此反推出整机输入功率，也没有任何
接口直接实测充电口边界的输入功率。本程序改为给出**估算值**：

```text
估算整机输入功率 = 平台功率 + 电池端充电功率
```

平台功率取自 Intel Psys（`MSR_PLATFORM_ENERGY_STATUS`），Intel 明确该计数器不含
电池充电功率，两项因此不重叠。该估算仍缺少充电路径与转换损耗（典型 5%–10%），
所以读数偏低，界面始终标注为估算值，不会当作实测输入功率。

各指标的数据源探测在运行时进行；探测不到就显示不可用并给出原因，不会用适配器
额定功率、PD 协商功率或电池功率顶替。可用 `--power-probe` 查看本机探测结果：

```text
BatteryChargeMeter.exe --power-probe report.txt 10
```

## 运行

从 [Releases](https://github.com/dzshzx/battery-charge-meter/releases/latest)
下载最新的 `BatteryChargeMeter-vX.Y.Z-windows.exe`，然后直接双击运行。

程序未使用商业代码签名证书。Windows 首次运行若显示 SmartScreen 提示，
请先核对仓库来源，并使用 Release 附带的 `.sha256` 文件校验 EXE，再决定是否运行。

启动时会弹出 UAC 提权提示。平台功率所依赖的驱动只允许 SYSTEM 与管理员访问，
不提权就读不到该计数器，因此程序在启动时一次性请求管理员权限，而不是运行到
一半再降级。电池端净功率与 CPU 包功率不需要提权。

> 因为程序要求管理员权限，用注册表 `Run` 键做开机自启不会生效；如需自启，
> 请改用任务计划程序并勾选“使用最高权限运行”。

## 平台功率（可选）

电池端净功率与 CPU 包功率开箱即用，不需要任何额外组件。**平台功率**与
**估算整机输入功率**额外需要 [PawnIO](https://pawnio.eu/) 驱动——它是免费、
开源、经数字签名的通用内核驱动，请自行从官网下载安装包安装；本程序不会代为
安装驱动，也不会在未经你同意的情况下改动系统。

驱动所需的 `IntelMSR.bin` 模块已内嵌在 EXE 内，无需另行下载，发行版仍是单个
文件。若要换用自己编译或更新版本的模块，把同名文件放在 EXE 同目录即可覆盖
内嵌副本（详见 `third_party/NOTICE.md`）。

未安装驱动时，这两个指标显示不可用并说明原因，其余功能不受影响。

能否读到平台功率还取决于主板是否把 Psys 信号布线到处理器，这是整机厂的硬件
设计选择，逐机型不同。程序在运行时探测实际计数器是否推进来判定，不按机型
白名单假定；同时用 EMI 独立读到的 CPU 包功率校验能量单位换算，校验不通过就
不显示平台功率，而不是显示一个缩放错误的数值。

## 配置文件

当前版本不需要 `BatteryChargeMeter.exe.config`。旧版本中的这个文件不是用户设置，
只用于声明 CLR/.NET Framework 4.7 启动目标、兼容旧 CLR 2 激活策略，以及 WinForms
的 Per-Monitor V2 DPI 行为。本项目没有 CLR 2 或混合模式依赖；目标框架信息现已写入
程序集，DPI awareness 由 EXE 内嵌 manifest 声明，跨屏缩放由程序直接处理
`WM_DPICHANGED`，因此 Release 可以只提供一个 EXE。

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

脚本使用 Windows 自带的 .NET Framework C# 编译器，将单文件 EXE 写入 `dist/`。
`dist/` 是本地构建目录，不纳入版本控制。

## 发布

项目使用 GitHub Actions 构建和发布：

- 推送到 `master` 或向 `master` 提交 Pull Request 时，CI 会在 Windows
  runner 上执行一次完整构建。
- 推送符合 `vX.Y.Z` 格式的 tag 时，Release 工作流会校验 tag 与 manifest
  版本一致，重新构建程序，生成可直接运行的 EXE 和 SHA-256 校验文件，并创建
  GitHub Release。

发布新版本前先更新 `src/BatteryChargeMeter.manifest` 中的四段版本号。例如，
manifest 版本 `1.3.0.0` 对应 tag `v1.3.0`：

```powershell
git tag -a v1.3.0 -m "Release v1.3.0"
git push origin v1.3.0
```

发布产物只存在于 GitHub Release，不直接提交到仓库。

## 项目结构

```text
.
├── .github/    # CI 与 Release 工作流
├── scripts/    # 构建、测试与发布打包脚本
├── src/        # C# 源码与 DPI manifest
└── dist/       # 本地构建输出（不纳入版本控制）
```
