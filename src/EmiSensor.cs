using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryChargeMeter
{
    /// <summary>
    /// Reads CPU package power from the Windows Energy Meter Interface
    /// (GUID_DEVICE_ENERGY_METER). EMI needs no driver install and no
    /// elevation, so this source is available to the plain portable EXE.
    ///
    /// EMI reports a monotonic energy counter, not instantaneous power, so a
    /// figure only exists once two samples have been taken. The device supplies
    /// its own timestamps; those are used rather than wall-clock timing so the
    /// window is not distorted by this process being descheduled.
    /// </summary>
    internal sealed class EmiSensor : IDisposable
    {
        private const string PackageChannelSuffix = "_PKG";

        private readonly List<string> channelNames = new List<string>();
        private readonly List<string> discoveredChannels = new List<string>();
        private readonly string devicePath;
        private readonly int packageChannelIndex = -1;
        private readonly string unavailableReason;

        private ulong previousEnergy;
        private ulong previousTime;
        private bool hasPrevious;

        public EmiSensor()
        {
            try
            {
                List<string> paths = NativeEmi.EnumeratePaths();
                if (paths.Count == 0)
                {
                    unavailableReason = "本机没有 EMI 电表设备";
                    return;
                }

                foreach (string path in paths)
                {
                    List<string> names = ReadChannels(path);
                    if (names == null)
                        continue;

                    discoveredChannels.AddRange(names);

                    int index = IndexOfPackageChannel(names);
                    if (index < 0)
                        continue;

                    devicePath = path;
                    channelNames.AddRange(names);
                    packageChannelIndex = index;
                    break;
                }

                if (packageChannelIndex < 0)
                {
                    unavailableReason = discoveredChannels.Count == 0
                        ? "EMI 设备未报告任何通道"
                        : "EMI 没有 CPU 包功率通道";
                }
            }
            catch (Exception error)
            {
                unavailableReason = "EMI 初始化失败: " + error.Message;
            }
        }

        /// <summary>
        /// Channel names of the device in use, or of every device inspected when
        /// none carried a package channel. Surfaced so the capability report can
        /// show what the machine does expose instead of only that the lookup
        /// failed.
        /// </summary>
        public IList<string> DiscoveredChannels
        {
            get { return discoveredChannels.AsReadOnly(); }
        }

        private static List<string> ReadChannels(string path)
        {
            using (SafeFileHandle handle = NativeEmi.Open(path))
            {
                if (handle == null || handle.IsInvalid)
                    return null;

                // V1 metadata has a different layout that this build has never
                // been able to test against real hardware, so such a device is
                // skipped rather than parsed on a guess.
                if (NativeEmi.ReadVersion(handle) != 2)
                    return null;

                return NativeEmi.ReadChannelNames(handle);
            }
        }

        private static int IndexOfPackageChannel(List<string> names)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].EndsWith(PackageChannelSuffix, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public bool Available
        {
            get { return unavailableReason == null; }
        }

        public string UnavailableReason
        {
            get { return unavailableReason; }
        }

        public PowerSample Read()
        {
            if (!Available)
                return PowerSample.Unsupported(PowerBoundary.CpuPackage, unavailableReason);

            ulong energy;
            ulong time;
            try
            {
                using (SafeFileHandle handle = NativeEmi.Open(devicePath))
                {
                    if (handle == null || handle.IsInvalid)
                        return PowerSample.Unsupported(PowerBoundary.CpuPackage, "无法打开 EMI 设备");

                    NativeEmi.ReadMeasurement(
                        handle, channelNames.Count, packageChannelIndex, out energy, out time);
                }
            }
            catch (Exception error)
            {
                return PowerSample.Unsupported(PowerBoundary.CpuPackage, "EMI 读取失败: " + error.Message);
            }

            if (!hasPrevious)
            {
                previousEnergy = energy;
                previousTime = time;
                hasPrevious = true;
                return PowerSample.Unsupported(PowerBoundary.CpuPackage, "正在建立基准");
            }

            ulong energyDelta = energy - previousEnergy;
            ulong timeDelta = time - previousTime;
            previousEnergy = energy;
            previousTime = time;

            if (timeDelta == 0)
                return PowerSample.Unsupported(PowerBoundary.CpuPackage, "采样窗口为零");

            // Energy is picowatt-hours, time is 100 ns units.
            double seconds = timeDelta / 1e7;
            double watts = (energyDelta / 1e12) * 3600.0 / seconds;

            return PowerSample.FromValue(
                PowerBoundary.CpuPackage,
                MeasurementKind.Measured,
                watts,
                "EMI " + channelNames[packageChannelIndex],
                TimeSpan.FromSeconds(seconds));
        }

        public void Dispose()
        {
        }
    }

    internal static class NativeEmi
    {
        private static readonly Guid EnergyMeterGuid =
            new Guid("45BD8344-7ED6-49CF-A440-C276C933B053");

        private const uint DigcfPresent = 0x2;
        private const uint DigcfDeviceInterface = 0x10;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x80;
        private const uint IoctlGetVersion = 0x00224000;
        private const uint IoctlGetMetadataSize = 0x00224004;
        private const uint IoctlGetMetadata = 0x00224008;
        private const uint IoctlGetMeasurement = 0x0022400C;
        private const int ErrorNoMoreItems = 259;

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public uint Size;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public UIntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid guid,
            uint index, ref DeviceInterfaceData data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref DeviceInterfaceData data, IntPtr detail,
            uint detailSize, ref uint required, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle handle, uint code, IntPtr inBuffer, uint inSize,
            IntPtr outBuffer, uint outSize, ref uint returned, IntPtr overlapped);

        /// <summary>
        /// Windows creates one interface per energy meter, so a machine with an
        /// OEM meter alongside the generic RAPL one exposes several. Stopping at
        /// index zero would miss the package channel whenever it is not on the
        /// first device, which is most likely exactly on the boards that carry
        /// extra meters.
        /// </summary>
        public static List<string> EnumeratePaths()
        {
            List<string> paths = new List<string>();

            Guid guid = EnergyMeterGuid;
            IntPtr set = SetupDiGetClassDevs(
                ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == new IntPtr(-1))
                return paths;

            try
            {
                for (uint index = 0; ; index++)
                {
                    DeviceInterfaceData data = new DeviceInterfaceData();
                    data.Size = (uint)Marshal.SizeOf(typeof(DeviceInterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ErrorNoMoreItems)
                            break;

                        throw new InvalidOperationException(
                            "SetupDiEnumDeviceInterfaces 失败: " +
                            error.ToString(CultureInfo.InvariantCulture));
                    }

                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                    if (required == 0)
                        continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(
                                set, ref data, detail, required, ref required, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(IntPtr.Add(detail, 4)));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }

            return paths;
        }

        public static SafeFileHandle Open(string path)
        {
            return CreateFile(
                path, GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        }

        public static ushort ReadVersion(SafeFileHandle handle)
        {
            IntPtr buffer = Marshal.AllocHGlobal(2);
            try
            {
                uint returned = 0;
                if (!DeviceIoControl(handle, IoctlGetVersion, IntPtr.Zero, 0, buffer, 2, ref returned, IntPtr.Zero))
                    throw new InvalidOperationException("IOCTL_EMI_GET_VERSION 失败");

                return (ushort)Marshal.ReadInt16(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// EMI_METADATA_V2: HardwareOEM(32B), HardwareModel(32B),
        /// HardwareRevision(2B), ChannelCount(2B), then per channel
        /// MeasurementUnit(4B), ChannelNameSize(2B), ChannelName(NameSize).
        /// Names are space padded to a fixed width and must be trimmed before
        /// they are compared.
        /// </summary>
        public static List<string> ReadChannelNames(SafeFileHandle handle)
        {
            List<string> names = new List<string>();

            IntPtr sizeBuffer = Marshal.AllocHGlobal(4);
            try
            {
                uint returned = 0;
                if (!DeviceIoControl(
                        handle, IoctlGetMetadataSize, IntPtr.Zero, 0, sizeBuffer, 4, ref returned, IntPtr.Zero))
                    throw new InvalidOperationException("IOCTL_EMI_GET_METADATA_SIZE 失败");

                int size = Marshal.ReadInt32(sizeBuffer);
                if (size <= 68)
                    throw new InvalidOperationException("EMI 元数据过短");

                IntPtr metadata = Marshal.AllocHGlobal(size);
                try
                {
                    if (!DeviceIoControl(
                            handle, IoctlGetMetadata, IntPtr.Zero, 0, metadata, (uint)size, ref returned, IntPtr.Zero))
                        throw new InvalidOperationException("IOCTL_EMI_GET_METADATA 失败");

                    ushort channelCount = (ushort)Marshal.ReadInt16(metadata, 66);
                    int offset = 68;
                    for (int i = 0; i < channelCount; i++)
                    {
                        ushort nameSize = (ushort)Marshal.ReadInt16(metadata, offset + 4);
                        if (nameSize < 2 || offset + 6 + nameSize > size)
                            throw new InvalidOperationException("EMI 通道名越界");

                        string name = Marshal.PtrToStringUni(
                            IntPtr.Add(metadata, offset + 6), (nameSize / 2) - 1);
                        names.Add(name.TrimEnd('\0', ' '));
                        offset += 6 + nameSize;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(metadata);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sizeBuffer);
            }

            return names;
        }

        /// <summary>
        /// Each channel contributes an EMI_CHANNEL_MEASUREMENT_DATA of
        /// AbsoluteEnergy(8B, picowatt-hours) and AbsoluteTime(8B, 100 ns).
        /// </summary>
        public static void ReadMeasurement(
            SafeFileHandle handle, int channelCount, int channelIndex,
            out ulong energy, out ulong time)
        {
            int size = channelCount * 16;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint returned = 0;
                if (!DeviceIoControl(
                        handle, IoctlGetMeasurement, IntPtr.Zero, 0, buffer, (uint)size, ref returned, IntPtr.Zero))
                    throw new InvalidOperationException("IOCTL_EMI_GET_MEASUREMENT 失败");

                energy = (ulong)Marshal.ReadInt64(buffer, channelIndex * 16);
                time = (ulong)Marshal.ReadInt64(buffer, channelIndex * 16 + 8);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
