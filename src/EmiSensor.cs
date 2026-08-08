using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
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
        private readonly List<string> discoveryErrors = new List<string>();
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

                List<string> selectedChannels;
                List<string> allChannels;
                List<string> errors;
                devicePath = SelectPackageDevice(
                    paths,
                    ReadChannels,
                    out selectedChannels,
                    out allChannels,
                    out errors);
                discoveredChannels.AddRange(allChannels);
                discoveryErrors.AddRange(errors);

                if (devicePath != null)
                {
                    channelNames.AddRange(selectedChannels);
                    packageChannelIndex = IndexOfPackageChannel(selectedChannels);
                }

                if (packageChannelIndex < 0)
                {
                    if (discoveredChannels.Count > 0)
                        unavailableReason = "EMI 没有 CPU 包功率通道";
                    else if (discoveryErrors.Count > 0)
                        unavailableReason = "EMI 设备不可读: " + discoveryErrors[0];
                    else
                        unavailableReason = "EMI 设备未报告任何通道";
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

        public IList<string> DiscoveryErrors
        {
            get { return discoveryErrors.AsReadOnly(); }
        }

        internal static string SelectPackageDevice(
            IList<string> paths,
            Func<string, List<string>> readChannels,
            out List<string> selectedChannels,
            out List<string> allChannels,
            out List<string> errors)
        {
            selectedChannels = new List<string>();
            allChannels = new List<string>();
            errors = new List<string>();

            if (paths == null || readChannels == null)
                return null;

            foreach (string path in paths)
            {
                List<string> names;
                try
                {
                    names = readChannels(path);
                }
                catch (Exception error)
                {
                    errors.Add(path + ": " + error.Message);
                    continue;
                }

                if (names == null)
                    continue;

                allChannels.AddRange(names);
                if (IndexOfPackageChannel(names) < 0)
                    continue;

                selectedChannels.AddRange(names);
                return path;
            }

            return null;
        }

        private static List<string> ReadChannels(string path)
        {
            using (SafeFileHandle handle = NativeEmi.Open(path))
            {
                if (handle == null || handle.IsInvalid)
                    return null;

                return ReadSupportedChannels(
                    handle,
                    NativeEmi.ReadVersion,
                    NativeEmi.ReadChannelNames);
            }
        }

        /// <summary>
        /// Runs the live version-to-metadata decision with injectable native
        /// operations. The production path supplies the EMI IOCTL functions;
        /// the self test supplies deterministic V1 metadata so a future version
        /// gate cannot silently make supported V1 devices disappear.
        /// </summary>
        internal static List<string> ReadSupportedChannels<TReader>(
            TReader reader,
            Func<TReader, ushort> readVersion,
            Func<TReader, ushort, List<string>> readChannelNames)
        {
            if (readVersion == null)
                throw new ArgumentNullException("readVersion");
            if (readChannelNames == null)
                throw new ArgumentNullException("readChannelNames");

            ushort version = readVersion(reader);
            if (version != 1 && version != 2)
                return null;

            return readChannelNames(reader, version);
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

            ulong oldEnergy = previousEnergy;
            ulong oldTime = previousTime;
            previousEnergy = energy;
            previousTime = time;

            double watts;
            TimeSpan window;
            if (!TryCalculatePower(oldEnergy, oldTime, energy, time, out watts, out window))
            {
                return PowerSample.Unsupported(
                    PowerBoundary.CpuPackage, "EMI 计数器已重置，正在重建基准");
            }

            return PowerSample.FromValue(
                PowerBoundary.CpuPackage,
                MeasurementKind.Measured,
                watts,
                "EMI " + channelNames[packageChannelIndex],
                window);
        }

        internal static bool TryCalculatePower(
            ulong oldEnergy,
            ulong oldTime,
            ulong energy,
            ulong time,
            out double watts,
            out TimeSpan window)
        {
            watts = 0.0;
            window = TimeSpan.Zero;

            if (energy < oldEnergy || time <= oldTime)
                return false;

            ulong energyDelta = energy - oldEnergy;
            ulong timeDelta = time - oldTime;

            // Energy is picowatt-hours, time is 100 ns units.
            double seconds = timeDelta / 1e7;
            watts = (energyDelta / 1e12) * 3600.0 / seconds;
            window = TimeSpan.FromSeconds(seconds);
            return !Double.IsNaN(watts) && !Double.IsInfinity(watts);
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

        public static List<string> ReadChannelNames(SafeFileHandle handle, ushort version)
        {
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

                    byte[] bytes = new byte[size];
                    Marshal.Copy(metadata, bytes, 0, size);
                    return ParseChannelNames(version, bytes);
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

        }

        /// <summary>
        /// Parses the two metadata layouts defined by emi.h. V1 describes one
        /// metered hardware channel; V2 carries a variable-length channel list.
        /// Keeping this byte parser separate lets the self test cover both
        /// layouts without depending on a particular machine's firmware.
        /// </summary>
        internal static List<string> ParseChannelNames(ushort version, byte[] metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException("metadata");

            List<string> names = new List<string>();
            if (version == 1)
            {
                // EMI_METADATA_V1: MeasurementUnit(4B), HardwareOEM(32B),
                // HardwareModel(32B), HardwareRevision(2B), NameSize(2B), Name.
                const int nameSizeOffset = 70;
                const int nameOffset = 72;
                ushort nameSize = ReadNameSize(metadata, nameSizeOffset, nameOffset);
                names.Add(DecodeName(metadata, nameOffset, nameSize));
                return names;
            }

            if (version != 2 || metadata.Length < 68)
                throw new InvalidOperationException("不支持的 EMI 元数据版本");

            // EMI_METADATA_V2: HardwareOEM(32B), HardwareModel(32B),
            // HardwareRevision(2B), ChannelCount(2B), then variable channels.
            ushort channelCount = BitConverter.ToUInt16(metadata, 66);
            int offset = 68;
            for (int i = 0; i < channelCount; i++)
            {
                ushort nameSize = ReadNameSize(metadata, offset + 4, offset + 6);
                names.Add(DecodeName(metadata, offset + 6, nameSize));
                offset += 6 + nameSize;
            }
            return names;
        }

        private static ushort ReadNameSize(byte[] metadata, int sizeOffset, int nameOffset)
        {
            if (sizeOffset < 0 || sizeOffset + 2 > metadata.Length)
                throw new InvalidOperationException("EMI 通道元数据过短");

            ushort nameSize = BitConverter.ToUInt16(metadata, sizeOffset);
            if (nameSize < 2 || (nameSize & 1) != 0 || nameOffset + nameSize > metadata.Length)
                throw new InvalidOperationException("EMI 通道名越界");
            return nameSize;
        }

        private static string DecodeName(byte[] metadata, int offset, ushort size)
        {
            return Encoding.Unicode.GetString(metadata, offset, size)
                .TrimEnd('\0', ' ');
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
