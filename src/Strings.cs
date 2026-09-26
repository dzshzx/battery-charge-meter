using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace BatteryChargeMeter
{
    // Chinese source strings are stable lookup keys. Hardware diagnostics stay
    // in their original form until presentation, so cached failures and source
    // descriptions can change language without reopening a driver.
    internal static class Strings
    {
        private const string SettingsPath = @"Software\BatteryChargeMeter";
        private static string preference = LoadPreference();
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "功率计", "Power Meter" },
            { "设置", "Settings" },
            { "语言", "Language" },
            { "更新时间", "Last updated" },
            { "当前权限：{0}", "Permission: {0}" },
            { "30 秒均值 · 有效 {0:0.#}s", "30s average · {0:0.#}s valid" },
            { "60 秒峰值 · 最近 {0}s", "60s peak · last {0}s" },
            { "30 秒均值", "30s average" },
            { "60 秒峰值", "60s peak" },
            { "均值 · {0:0.#}/30s", "Average · {0:0.#}/30s" },
            { "峰值 · {0:0.#}/60s", "Peak · {0:0.#}/60s" },
            { "电池端 {0} · ≈ {1}", "Battery {0} · ≈ {1}" },
            { "估算整机输入功率", "Estimated system input" },
            { "电池端净功率", "Net battery terminal power" },
            { "CPU 包功率", "CPU package power" },
            { "平台功率", "Platform power" },
            { "系统负载功率", "System load power" },
            { "未知口径", "Unknown boundary" },
            { "正在读取", "Reading" },
            { "电池端 {0}   电池电流 ≈ {1}", "Battery {0}   Current ≈ {1}" },
            { "均值 {0} W（有效 {1:0.#}/30s）", "Avg {0} W ({1:0.#}/30s valid)" },
            { "峰值 {0} W · {1}/60s", "Peak {0} W · {1}/60s" },
            { "电池电量", "Battery level" },
            { "整机功率", "System" },
            { "电池端", "Battery" },
            { "置顶", "Pin on top" },
            { "开机自启", "Start at login" },
            { "开机自启（登录后）", "Start at login" },
            { "显示窗口", "Show window" },
            { "退出", "Exit" },
            { "传感器异常", "Sensor error" },
            { "管理员模式", "Admin" },
            { "普通模式", "Standard" },
            { "以管理员身份重新启动", "Restart as administrator" },
            { "功率显示口径", "Power display mode" },
            { "跟随系统", "System default" },
            { "已接电源 · 未充电", "AC · Idle" },
            { "已接电源 · 充电中", "AC · Charging" },
            { "已接电源 · 电池补充", "AC · Battery assist" },
            { "已接电源 · 状态未知", "AC · Unknown" },
            { "电池供电 · 放电中", "Battery · Discharging" },
            { "电池供电 · 速率未知", "Battery · Rate unknown" },
            { "电池状态异常", "Invalid battery state" },
            { "电池不可用", "Battery unavailable" },
            { "无数据", "No data" },
            { "外电：平台功率 + 带符号的电池端净功率，未含转换损耗。\n电池供电：电池端放电功率。CPU 包已包含在平台功率内。",
              "On AC: platform + signed net battery terminal power, excluding conversion losses.\nOn battery: battery discharge power. CPU package power is already part of platform power." },
            { "当前自启指向：{0}；勾选可确认更换。", "Startup currently points to: {0}. Select to confirm replacement." },
            { "以当前 Windows 用户登录后，从仅管理员可写的受保护副本以管理员权限运行并留在托盘。移动程序后需重新启用。", "Run elevated in the tray from an administrator-only protected copy when this Windows user signs in. Enable again after moving the application." },
            { "自启状态读取失败：{0}", "Unable to read startup settings: {0}" },
            { "状态读取失败，请查看主窗口中的原因。", "Unable to read startup settings. See the main window for details." },
            { "请先点击下方“以管理员身份重新启动”，再勾选开机自启。", "Select Restart as administrator before enabling startup." },
            { "自启当前指向：\n{0}\n\n更换为：\n{1}？", "Startup currently points to:\n{0}\n\nReplace it with:\n{1}?" },
            { "更换自启程序", "Replace startup application" },
            { "已启用开机自启：登录后以管理员权限运行受保护副本，在托盘显示。", "Startup enabled: the protected copy runs elevated in the tray at sign-in." },
            { "已关闭此程序的开机自启。", "Startup disabled for this copy." },
            { "已关闭此程序的开机自启；受保护副本将在下次以管理员身份启动时删除。", "Startup disabled. The protected copy is removed the next time the app runs as administrator." },
            { "自启仍指向普通权限可替换的程序文件，请以管理员身份重新勾选，改用受保护副本。", "Startup still runs a file that standard processes can replace. Enable startup again as administrator to use the protected copy." },
            { "写入或删除自启副本需要管理员权限。", "Administrator access is required to change the protected startup copy." },
            { "只能从正在运行的程序本身创建自启副本。", "The protected startup copy can only be created from the running application itself." },
            { "自启副本写入后的校验失败。", "Protected startup copy verification failed after writing." },
            { "自启副本删除后的校验失败。", "Protected startup copy verification failed after deletion." },
            { "自启副本写入校验失败。", "Protected startup copy verification failed while writing." },
            { "读取自启副本来源时文件被截断。", "The application file was truncated while creating the protected startup copy." },
            { "自启设置未完成：{0}", "Startup settings were not changed: {0}" },
            { "普通模式：平台功率可能需要管理员权限。", "Standard mode: platform power may require administrator access." },
            { "管理员启动未成功，继续普通模式。", "Elevated launch did not start. Continuing in standard mode." },
            { "已取消管理员授权，继续普通模式。", "Administrator access was cancelled. Continuing in standard mode." },
            { "管理员启动失败，继续普通模式：", "Elevated launch failed. Continuing in standard mode: " },
            { "请先点击“重新以管理员身份启动”，再启用开机自启。", "Restart as administrator before enabling startup." },
            { "自启任务写入后的读取校验失败。", "Startup task verification failed after writing." },
            { "自启任务已被另一份程序修改，请重新读取并确认后再试。", "Another copy changed the startup task. Refresh and confirm before retrying." },
            { "自启任务删除后的读取校验失败。", "Startup task verification failed after deletion." },
            { "另一份程序正在修改自启，请稍后重试。", "Another copy is changing startup settings. Try again shortly." },
            { "同名任务不属于本程序，未修改。", "A task with this name belongs to another application. It was left unchanged." },
            { "同名自启任务不属于本程序，已保留未改；如不再需要，请在任务计划程序库中手工删除：{0}", "A startup task with this name belongs to another application and was left unchanged. If it is no longer needed, delete it manually in the Task Scheduler Library: {0}" },
            { "自启任务设置已变化（权限、触发条件或运行限制），请重新勾选以修复。", "Startup permissions, triggers or limits changed. Enable startup again to repair the task." },
            { "无法初始化窗口恢复通知。", "Unable to initialize the window restore notification." },
            { "电池状态不可用", "Battery status unavailable" },
            { "WMI BatteryStatus；固件采样窗口与内部时间戳未知", "WMI BatteryStatus; firmware sampling window and internal timestamp unknown" },
            { "未交叉验证", "Not cross-validated" },
            { "电池端放电功率", "Battery discharge power" },
            { "平台功率不可用", "Platform power unavailable" },
            { "电池速率不可用", "Battery rate unavailable" },
            { "估算结果不是有限数值", "The estimate is not a finite number" },
            { "采样结果不是有限数值", "The sample is not a finite number" },
            { "估算结果为负，数据边界或采样窗口不一致", "Negative estimate: measurement boundaries or sampling windows do not agree" },
            { "平台功率 + 电池端净功率", "Platform power + net battery terminal power" },
            { "电池供电标志互相矛盾", "Conflicting battery supply flags" },
            { "本机没有活动电池", "No active battery on this computer" },
            { "至少一块电池状态不可用", "At least one battery has no status" },
            { "多块电池报告的供电状态不一致", "Batteries report different supply states" },
            { "至少一块电池速率不可用", "At least one battery has no rate" },
            { "聚合后的电池功率方向与供电状态不一致", "Combined battery power direction conflicts with the supply state" },
            { "电池充电速率不可用", "Battery charge rate unavailable" },
            { "电池放电速率不可用", "Battery discharge rate unavailable" },
            { "离线状态下电池方向不明", "Battery flow direction unknown while unplugged" },
            { "本机没有 EMI 电表设备", "No EMI energy meter on this computer" },
            { "EMI 没有 CPU 包功率通道", "EMI has no CPU package power channel" },
            { "EMI 设备不可读: ", "EMI device cannot be read: " },
            { "EMI 设备未报告任何通道", "EMI device reported no channels" },
            { "EMI 初始化失败: ", "EMI initialization failed: " },
            { "无法打开 EMI 设备", "Unable to open EMI device" },
            { "EMI 读取失败: ", "EMI read failed: " },
            { "正在建立基准", "Establishing a baseline" },
            { "EMI 计数器已重置，正在重建基准", "EMI counter reset; rebuilding the baseline" },
            { "SetupDiEnumDeviceInterfaces 失败: ", "SetupDiEnumDeviceInterfaces failed: " },
            { "IOCTL_EMI_GET_VERSION 失败", "IOCTL_EMI_GET_VERSION failed" },
            { "IOCTL_EMI_GET_METADATA_SIZE 失败", "IOCTL_EMI_GET_METADATA_SIZE failed" },
            { "EMI 元数据过短", "EMI metadata is too short" },
            { "IOCTL_EMI_GET_METADATA 失败", "IOCTL_EMI_GET_METADATA failed" },
            { "不支持的 EMI 元数据版本", "Unsupported EMI metadata version" },
            { "EMI 通道元数据过短", "EMI channel metadata is too short" },
            { "EMI 通道名越界", "EMI channel name is out of bounds" },
            { "IOCTL_EMI_GET_MEASUREMENT 失败", "IOCTL_EMI_GET_MEASUREMENT failed" },
            { "未安装 PawnIO 驱动", "PawnIO driver is not installed" },
            { "PawnIO 安装目录缺少 PawnIOLib.dll", "PawnIOLib.dll is missing from the PawnIO installation" },
            { "无法加载 PawnIOLib.dll", "Unable to load PawnIOLib.dll" },
            { "缺少 IntelMSR.bin 模块", "IntelMSR.bin module is missing" },
            { "加载 IntelMSR 模块失败: 0x", "Failed to load IntelMSR module: 0x" },
            { "PawnIOLib.dll 不可用", "PawnIOLib.dll unavailable" },
            { "PawnIO 初始化失败: ", "PawnIO initialization failed: " },
            { "MSR 读取失败: ", "MSR read failed: " },
            { "需要以管理员身份运行才能读取平台功率", "Run as administrator to read platform power" },
            { "pawnio_open 失败: 0x", "pawnio_open failed: 0x" },
            { "采样窗口为零", "Sampling window is zero" },
            { "采样间隔过长，正在重新建立基准", "Sampling gap too long; rebuilding the baseline" },
            { "本机未启用 Psys 平台计数器", "Psys platform counter is not enabled on this computer" }
        };

        internal static string Preference { get { return preference; } }
        internal static bool IsChinese { get { return Resolve(preference, CultureInfo.CurrentUICulture.Name) == "zh-CN"; } }
        internal static string AppName { get { return Get("功率计"); } }

        internal static string Resolve(string saved, string systemCulture)
        {
            if (saved == "zh-CN" || saved == "en") return saved;
            return (systemCulture ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
        }

        internal static void Select(string language, bool persist)
        {
            if (language != "zh-CN" && language != "en" && language != "system")
                throw new ArgumentException("Unsupported language.", "language");
            if (persist)
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
                    key.SetValue("Language", language, RegistryValueKind.String);
            preference = language;
        }

        private static string LoadPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsPath))
                    return key == null ? "system" : key.GetValue("Language", "system") as string ?? "system";
            }
            catch { return "system"; }
        }

        internal static string Get(string key)
        {
            string value;
            return !IsChinese && key != null && English.TryGetValue(key, out value) ? value : key;
        }

        internal static string Format(string key, params object[] args)
        {
            return String.Format(CultureInfo.InvariantCulture, Get(key), args);
        }

        internal static string Diagnostic(string text)
        {
            if (IsChinese || String.IsNullOrEmpty(text)) return text;
            string value;
            if (English.TryGetValue(text, out value)) return value;
            // Only translate authored prefixes; OS exception text, identifiers
            // and user paths are retained byte-for-byte.
            foreach (KeyValuePair<string, string> entry in English)
            {
                if (entry.Key.EndsWith("{0}", StringComparison.Ordinal))
                {
                    string prefix = entry.Key.Substring(0, entry.Key.Length - 3);
                    if (text.StartsWith(prefix, StringComparison.Ordinal))
                        return entry.Value.Replace("{0}", Diagnostic(text.Substring(prefix.Length)));
                }
                if ((entry.Key.EndsWith(": ", StringComparison.Ordinal)
                    || entry.Key.EndsWith("：", StringComparison.Ordinal)
                    || entry.Key.EndsWith(": 0x", StringComparison.Ordinal))
                    && text.StartsWith(entry.Key, StringComparison.Ordinal))
                    return entry.Value + text.Substring(entry.Key.Length);
            }
            const string checkPrefix = "单位换算校验失败（";
            if (text.StartsWith(checkPrefix, StringComparison.Ordinal) && text.EndsWith("）", StringComparison.Ordinal))
                return "Energy-unit validation failed (" + text.Substring(checkPrefix.Length, text.Length - checkPrefix.Length - 1) + ")";
            const string suffix = "（EMI 未交叉验证）";
            if (text.EndsWith(suffix, StringComparison.Ordinal))
                return text.Substring(0, text.Length - suffix.Length) + " (not cross-validated with EMI)";
            const string readFailure = " 读取失败: 0x";
            int position = text.IndexOf(readFailure, StringComparison.Ordinal);
            if (position >= 0)
                return text.Substring(0, position) + " read failed: 0x" + text.Substring(position + readFailure.Length);
            return text;
        }
    }
}
