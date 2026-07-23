# Battery Charge Meter

一个轻量、免安装的 Windows 电池功率监视器。它直接读取 Windows/ACPI
电池传感器，每秒更新电池端的净充放电功率。

## 功能

- 实时显示充电或放电功率、电压、估算电流和电量
- 最近 60 秒功率曲线、平均值与峰值
- 最小化后隐藏到系统托盘，继续在后台监测
- 托盘文字图标显示整数瓦数，悬停显示精确到 `0.01 W` 的功率
- 充电时托盘文字为白色，放电时为黄色
- 双击托盘图标恢复窗口，右键菜单可显示窗口或退出
- 支持 Per-Monitor V2 高 DPI，能够适配 100%–300% 缩放及跨屏切换

> 显示值是电池实际获得或输出的净功率，不是插座端或充电器输入功率。
> 电脑运行本身也会消耗充电器提供的一部分功率。

## 运行

下载或克隆仓库后，保持下面两个文件位于同一目录，然后双击 EXE：

```text
dist/
├── BatteryChargeMeter.exe
└── BatteryChargeMeter.exe.config
```

程序未使用商业代码签名证书。Windows 首次运行若显示 SmartScreen 提示，
请先核对仓库来源和文件哈希，再决定是否运行。

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

脚本使用 Windows 自带的 .NET Framework C# 编译器，将结果写入 `dist/`。

## 项目结构

```text
.
├── dist/       # 可直接运行的发布文件
├── scripts/    # 构建脚本
└── src/        # C# 源码、DPI manifest 与运行配置
```
