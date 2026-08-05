using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Reads Intel RAPL energy counters through the PawnIO kernel driver.
    ///
    /// This is the only route to <c>MSR_PLATFORM_ENERGY_STATUS</c> (Psys), the
    /// platform-wide counter that makes an input-power estimate possible. It is
    /// optional by design: the driver is a separate, user-installed, officially
    /// signed package. This program never installs it, and reports the platform
    /// boundary as unsupported when it is absent.
    ///
    /// Licensing: PawnIO is GPL-2.0 with an exception for independent modules
    /// that talk to it solely through its device IO control interface, which is
    /// what PawnIOLib does. PawnIOLib itself and the IntelMSR module are
    /// LGPL-2.1-or-later and are used unmodified.
    /// </summary>
    internal sealed class PawnIoSensor : IDisposable
    {
        private const uint MsrRaplPowerUnit = 0x606;
        private const uint MsrPkgEnergyStatus = 0x611;
        private const uint MsrPlatformEnergyStatus = 0x64D;
        private const string ModuleFileName = "IntelMSR.bin";

        private readonly string unavailableReason;
        private readonly IntPtr handle = IntPtr.Zero;
        private readonly double energyUnitJoules;

        private readonly Stopwatch clock = new Stopwatch();
        private long previousTicks;
        private ulong previousPkg;
        private ulong previousPsys;
        private bool hasPrevious;

        public PawnIoSensor()
        {
            try
            {
                string installDirectory = FindInstallDirectory();
                if (installDirectory == null)
                {
                    unavailableReason = "未安装 PawnIO 驱动";
                    return;
                }

                string libraryPath = Path.Combine(installDirectory, "PawnIOLib.dll");
                if (!File.Exists(libraryPath))
                {
                    unavailableReason = "PawnIO 安装目录缺少 PawnIOLib.dll";
                    return;
                }

                // Load by full path first so the later DllImport by bare name
                // resolves to this already-loaded module. PawnIO's directory is
                // not on PATH, and this program must not modify PATH.
                if (NativePawnIo.LoadLibrary(libraryPath) == IntPtr.Zero)
                {
                    unavailableReason = "无法加载 PawnIOLib.dll";
                    return;
                }

                byte[] module = LoadModule();
                if (module == null)
                {
                    unavailableReason = "缺少 " + ModuleFileName + " 模块";
                    return;
                }

                int result = NativePawnIo.pawnio_open(out handle);
                if (result < 0)
                {
                    unavailableReason = DescribeOpenFailure(result);
                    return;
                }

                result = NativePawnIo.pawnio_load(handle, module, (IntPtr)module.Length);
                if (result < 0)
                {
                    unavailableReason = "加载 IntelMSR 模块失败: 0x" + result.ToString("X8", CultureInfo.InvariantCulture);
                    return;
                }

                ulong unitRaw = ReadMsr(MsrRaplPowerUnit);
                int energyStatusUnits = (int)((unitRaw >> 8) & 0x1F);
                energyUnitJoules = Math.Pow(0.5, energyStatusUnits);

                clock.Start();
            }
            catch (DllNotFoundException)
            {
                unavailableReason = "PawnIOLib.dll 不可用";
            }
            catch (Exception error)
            {
                unavailableReason = "PawnIO 初始化失败: " + error.Message;
            }
        }

        public bool Available
        {
            get { return unavailableReason == null; }
        }

        public string UnavailableReason
        {
            get { return unavailableReason; }
        }

        /// <summary>
        /// Samples the platform and package counters over one shared window.
        ///
        /// The package figure exists only so the caller can check it against the
        /// independently measured EMI package figure. Agreement is what proves
        /// the energy-unit scaling is right on this machine; without that check
        /// a wrong unit would silently scale the platform figure too. RAPL
        /// energy registers carry no timestamp, so the window has to be measured
        /// here rather than assumed from the caller's tick interval.
        /// </summary>
        public bool TryRead(out PowerSample platform, out double packageWatts)
        {
            packageWatts = 0.0;

            if (!Available)
            {
                platform = PowerSample.Unsupported(PowerBoundary.Platform, unavailableReason);
                return false;
            }

            ulong pkg;
            ulong psys;
            long ticks;
            try
            {
                pkg = ReadMsr(MsrPkgEnergyStatus);
                psys = ReadMsr(MsrPlatformEnergyStatus);
                ticks = clock.ElapsedTicks;
            }
            catch (Exception error)
            {
                platform = PowerSample.Unsupported(PowerBoundary.Platform, "MSR 读取失败: " + error.Message);
                return false;
            }

            if (!hasPrevious)
            {
                previousPkg = pkg;
                previousPsys = psys;
                previousTicks = ticks;
                hasPrevious = true;
                platform = PowerSample.Unsupported(PowerBoundary.Platform, "正在建立基准");
                return false;
            }

            double seconds = (double)(ticks - previousTicks) / Stopwatch.Frequency;

            // RAPL energy registers are 32 bits wide and wrap.
            ulong pkgDelta = (pkg - previousPkg) & 0xFFFFFFFF;
            ulong psysDelta = (psys - previousPsys) & 0xFFFFFFFF;

            previousPkg = pkg;
            previousPsys = psys;
            previousTicks = ticks;

            if (seconds <= 0.0)
            {
                platform = PowerSample.Unsupported(PowerBoundary.Platform, "采样窗口为零");
                return false;
            }

            packageWatts = (pkgDelta * energyUnitJoules) / seconds;
            double platformWatts = (psysDelta * energyUnitJoules) / seconds;

            if (psysDelta == 0)
            {
                // A counter that never advances means the board does not route
                // the platform signal. That is a negative capability result, not
                // a zero-watt reading.
                platform = PowerSample.Unsupported(
                    PowerBoundary.Platform, "本机未启用 Psys 平台计数器");
                return false;
            }

            platform = PowerSample.FromValue(
                PowerBoundary.Platform,
                MeasurementKind.Measured,
                platformWatts,
                "PawnIO MSR 0x64D",
                TimeSpan.FromSeconds(seconds));
            return true;
        }

        /// <summary>
        /// PawnIO's device is installed with the security descriptor
        /// <c>D:P(A;;GA;;;SY)(A;;GA;;;BA)</c>, so only SYSTEM and administrators
        /// may open it. Access denied therefore usually means this process is
        /// not elevated rather than that anything is broken, and the user needs
        /// to be told that instead of an HRESULT. The elevation state is only
        /// consulted after a failure, so a machine whose administrator has
        /// widened that descriptor still works without a special case here.
        /// </summary>
        private static string DescribeOpenFailure(int result)
        {
            const int AccessDenied = unchecked((int)0x80070005);

            if (result == AccessDenied && !IsElevated())
                return "需要以管理员身份运行才能读取平台功率";

            return "pawnio_open 失败: 0x" + result.ToString("X8", CultureInfo.InvariantCulture);
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private ulong ReadMsr(uint msr)
        {
            ulong[] input = new ulong[] { msr };
            ulong[] output = new ulong[1];
            IntPtr returned;

            int result = NativePawnIo.pawnio_execute(
                handle, "ioctl_read_msr", input, (IntPtr)1, output, (IntPtr)1, out returned);
            if (result < 0)
            {
                throw new InvalidOperationException(
                    "MSR 0x" + msr.ToString("X", CultureInfo.InvariantCulture) +
                    " 读取失败: 0x" + result.ToString("X8", CultureInfo.InvariantCulture));
            }

            return output[0];
        }

        /// <summary>
        /// PawnIO records its own location in the uninstall key. Its directory
        /// is deliberately not on PATH, so this is the supported way to find it.
        /// </summary>
        private static string FindInstallDirectory()
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
            {
                if (key == null)
                    return null;

                string location = key.GetValue("InstallLocation") as string;
                if (String.IsNullOrEmpty(location) || !Directory.Exists(location))
                    return null;

                return location;
            }
        }

        /// <summary>
        /// The IntelMSR module is embedded so the program stays a single file,
        /// but a same-named file beside the executable wins. That ordering is
        /// deliberate: it lets a user swap in their own build of this
        /// LGPL-licensed module, which is what its licence requires, and it
        /// also allows testing a newer upstream module without a rebuild.
        /// </summary>
        private static byte[] LoadModule()
        {
            string beside = Path.Combine(
                Path.GetDirectoryName(typeof(PawnIoSensor).Assembly.Location) ?? ".",
                ModuleFileName);
            if (File.Exists(beside))
                return File.ReadAllBytes(beside);

            using (Stream stream = typeof(PawnIoSensor).Assembly
                .GetManifestResourceStream(ModuleFileName))
            {
                if (stream == null)
                    return null;

                byte[] buffer = new byte[stream.Length];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }

                return offset == buffer.Length ? buffer : null;
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
                NativePawnIo.pawnio_close(handle);
        }
    }

    internal static class NativePawnIo
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string path);

        [DllImport("PawnIOLib.dll")]
        public static extern int pawnio_open(out IntPtr handle);

        [DllImport("PawnIOLib.dll")]
        public static extern int pawnio_load(IntPtr handle, byte[] blob, IntPtr size);

        [DllImport("PawnIOLib.dll")]
        public static extern int pawnio_execute(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ulong[] input,
            IntPtr inSize,
            ulong[] output,
            IntPtr outSize,
            out IntPtr returnSize);

        [DllImport("PawnIOLib.dll")]
        public static extern int pawnio_close(IntPtr handle);
    }
}
