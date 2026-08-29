using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayClockApp.Infra;

internal static partial class BatteryIo
{
    // ---- CreateFile 参数（winbase.h） ----
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 0x3;
    private const uint FileAttributeNormal = 0x80;

    // ---- 电池 IOCTL 控制码（batclass.h） ----
    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;
    private const uint IoctlBatteryQueryStatus = 0x0029404C;

    // ---- BATTERY_QUERY_INFORMATION 查询级别（BATTERY_INFORMATION_LEVEL） ----
    private const uint BatteryInformation = 0;
    private const uint BatteryDeviceName = 4;
    private const uint BatteryManufactureName = 6;
    private const uint BatterySerialNumber = 8;

    // ---- BATTERY_STATUS.PowerState 状态位 ----
    private const uint BatteryPowerOnLine = 0x1; // 交流电源在线
    private const uint BatteryCharging = 0x4; // 正在充电

    // ---- BATTERY_INFORMATION.Capabilities 能力位 ----
    private const uint BatteryCapacityRelative = 0x40000000; // 容量为相对值而非 mWh

    // ---- SetupAPI 枚举标志（setupapi.h） ----
    private const uint DigcfPresent = 0x00000002; // 仅已连接设备
    private const uint DigcfDeviceInterface = 0x00000010; // 枚举设备接口而非设备节点

    /// <summary>GUID_DEVICE_BATTERY：电池设备接口类 GUID。</summary>
    private static readonly Guid BatteryInterfaceGuid = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

    /// <summary>SP_DEVICE_INTERFACE_DETAIL_DATA 中 DevicePath 相对结构头的偏移（缓存避免重复计算）。</summary>
    private static readonly IntPtr DevicePathOffset = Marshal.OffsetOf<SpDeviceInterfaceDetailData>("DevicePath");

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFileW(
        string name, uint access, uint share, IntPtr sec,
        uint creation, uint flags, IntPtr tmpl);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        IntPtr h, uint code, byte[] inBuf, int inSize,
        byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial void CloseHandle(IntPtr h);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial void SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ---- 缓存与复用（PathsLock 串行保护；电池热插拔时 refresh 重建） ----
    private static readonly Lock PathsLock = new();
    private static readonly List<string> CachedPaths = []; // 设备接口路径缓存
    private static readonly Dictionary<string, BatteryInfo> InfoPool = []; // 按路径复用的电池信息对象池

    // 复用 IOCTL 缓冲：所有查询均在 PathsLock 下串行执行，可安全共享，避免每轮采样分配
    private static readonly byte[] TagOut = new byte[4]; // QUERY_TAG 输出（4 字节）
    private static readonly byte[] InfoRaw = new byte[Marshal.SizeOf<BatteryInfoStruct>()]; // 静态信息输出（36 字节）
    private static readonly byte[] StatusOut = new byte[16]; // QUERY_STATUS 输出（16 字节）
    private static readonly byte[] StringOut = new byte[1024]; // 字符串信息输出（设备名/制造商/序列号）
    private static readonly byte[] QueryInBuf = new byte[12]; // QUERY_INFORMATION 输入（12 字节）
    private static readonly byte[] WaitStatusBuf = new byte[20]; // QUERY_STATUS 输入（BATTERY_WAIT_STATUS，20 字节）

    /// <summary>
    /// 获取全部电池设备接口路径：首次调用枚举并缓存，之后直接返回缓存，避免重复枚举。
    /// </summary>
    /// <param name="log">日志回调（输出失败原因）。</param>
    /// <param name="refresh">true 时丢弃旧缓存重新枚举（电池热插拔/路径变化后使用）。</param>
    private static List<string> GetBatteryPaths(Action<string>? log = null, bool refresh = false)
    {
        lock (PathsLock)
        {
            if (refresh) CachedPaths.Clear();
            if (CachedPaths.Count > 0) return CachedPaths;
            EnumerateBatteryPaths(CachedPaths, log);
            return CachedPaths;
        }
    }

    /// <summary>全量查询所有电池信息（Init 时一次性使用）；任一设备失败只跳过该设备。</summary>
    public static List<BatteryInfo> QueryAll(Action<string>? log = null, bool refresh = false)
    {
        var result = new List<BatteryInfo>();
        RefreshInto(result, log, refresh);
        return result;
    }

    /// <summary>
    /// 复用列表刷新全部电池信息：清空 target 后填充最新数据。电池对象按路径复用（InfoPool），
    /// 避免每轮采样产生新的 BatteryInfo 与字符串。
    /// </summary>
    public static void RefreshInto(List<BatteryInfo> target, Action<string>? log = null, bool refresh = false)
    {
        lock (PathsLock)
        {
            target.Clear();
            foreach (var path in GetBatteryPaths(log, refresh))
            {
                var info = QueryOne(path, log);
                if (info is not null) target.Add(info);
            }
        }
    }

    /// <summary>通过 SetupAPI 枚举电池设备接口，把可打开的完整设备路径填充到 paths。</summary>
    private static void EnumerateBatteryPaths(List<string> paths, Action<string>? log)
    {
        try
        {
            var guid = BatteryInterfaceGuid;
            var devInfoSet = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (IsInvalidHandle(devInfoSet)) return;

            try
            {
                // 索引从 0 递增，直到 SetupDiEnumDeviceInterfaces 返回 false
                for (uint index = 0;; index++)
                {
                    var ifData = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                    if (!SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref guid, index, ref ifData)) break;

                    var path = GetDeviceInterfacePath(devInfoSet, ref ifData);
                    if (path is not null) paths.Add(path);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"SetupAPI 枚举电池接口失败: {ex.Message}");
        }
    }

    /// <summary>读取设备接口详情中的完整设备路径；失败返回 null。</summary>
    private static string? GetDeviceInterfacePath(IntPtr devInfoSet, ref SpDeviceInterfaceData ifData)
    {
        // 先查询所需缓冲区大小（正常会返回 ERROR_INSUFFICIENT_BUFFER）
        SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref ifData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (requiredSize == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // 结构首字段 cbSize 必须预填：x86 为 6，x64 为 8
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref ifData, buffer, requiredSize, out _, IntPtr.Zero)) return null;

            // 设备路径（WCHAR 变长数组）紧跟在 cbSize 之后
            return Marshal.PtrToStringUni(IntPtr.Add(buffer, (int)DevicePathOffset));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>打开单个电池设备并读取完整信息；打开/查询失败返回 null。</summary>
    private static BatteryInfo? QueryOne(string devicePath, Action<string>? log)
    {
        var handle = CreateFileW(devicePath, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (IsInvalidHandle(handle))
        {
            log?.Invoke($"CreateFileW 失败 {devicePath}: err={Marshal.GetLastWin32Error()}");
            return null;
        }

        try
        {
            // 第一步：查询电池标签（BATTERY_TAG），后续 IOCTL 均需携带
            if (!IoControl(handle, IoctlBatteryQueryTag, null, TagOut, out _))
            {
                log?.Invoke($"BATTERY_QUERY_TAG 失败 {devicePath}: err={Marshal.GetLastWin32Error()}");
                return null;
            }

            var tag = BitConverter.ToUInt32(TagOut, 0);

            // 第二步：查询静态信息（容量、身份等）
            var info = QueryInfo(handle, tag, BatteryInformation);
            if (info is null)
            {
                log?.Invoke($"BATTERY_QUERY_INFORMATION 失败 {devicePath}: err={Marshal.GetLastWin32Error()}");
                return null;
            }

            var bi = info.Value;

            // 按路径复用对象池实例，避免每轮采样新建 BatteryInfo 与字符串
            if (!InfoPool.TryGetValue(devicePath, out var result))
            {
                result = new BatteryInfo { DevicePath = devicePath };
                InfoPool[devicePath] = result;
            }

            result.DesignCapacity = (int)bi.DesignedCapacity;
            result.FullChargeCapacity = (int)bi.FullChargedCapacity;
            result.CapacityUnits = (bi.Capabilities & BatteryCapacityRelative) != 0 ? "RELATIVE" : "MWH";
            result.DeviceName = SetIfChanged(result.DeviceName, QueryString(handle, tag, BatteryDeviceName));
            result.Manufacturer = SetIfChanged(result.Manufacturer, QueryString(handle, tag, BatteryManufactureName));
            result.SerialNumber = SetIfChanged(result.SerialNumber, QueryString(handle, tag, BatterySerialNumber));

            // 第三步：查询实时状态（剩余容量、是否充电等）；失败不影响已取得的静态信息
            if (!IoControl(handle, IoctlBatteryQueryStatus, BuildWaitStatus(tag), StatusOut, out _)) return result;
            var st = MemoryMarshal.Read<BatteryStatus>(StatusOut);
            result.CurrentCapacity = (int)st.Capacity;
            result.PowerOnLine = (st.PowerState & BatteryPowerOnLine) != 0;
            result.Charging = (st.PowerState & BatteryCharging) != 0;

            return result;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>查询指定级别的电池静态信息。</summary>
    private static BatteryInfoStruct? QueryInfo(IntPtr handle, uint tag, uint level)
    {
        if (!IoControl(handle, IoctlBatteryQueryInformation, BuildQueryIn(tag, level), InfoRaw, out _)) return null;
        return MemoryMarshal.Read<BatteryInfoStruct>(InfoRaw);
    }

    /// <summary>查询电池字符串信息（设备名/制造商/序列号）；失败返回空串。</summary>
    private static string QueryString(IntPtr handle, uint tag, uint level)
    {
        if (!IoControl(handle, IoctlBatteryQueryInformation, BuildQueryIn(tag, level), StringOut, out var returned)) return "";

        // 先定位 NUL 终止符再按有效长度解码，避免"整缓冲解码 + 切片"的多余分配
        var chars = returned / 2;
        for (var i = 0; i < chars; i++)
        {
            if (StringOut[i * 2] != 0 || StringOut[i * 2 + 1] != 0) continue;
            chars = i;
            break;
        }

        return Encoding.Unicode.GetString(StringOut, 0, chars * 2).Trim();
    }

    /// <summary>字符串字段复用：内容相同则保留旧引用，避免每轮采样产生等价的新字符串。</summary>
    private static string SetIfChanged(string current, string next)
        => string.Equals(current, next, StringComparison.Ordinal) ? current : next;

    /// <summary>写入 QUERY_INFORMATION 的 12 字节输入缓冲并返回（BatteryTag + InformationLevel + AtRate）。</summary>
    private static byte[] BuildQueryIn(uint tag, uint level)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(QueryInBuf, tag);
        BinaryPrimitives.WriteUInt32LittleEndian(QueryInBuf.AsSpan(4), level);
        return QueryInBuf;
    }

    /// <summary>
    /// 写入 QUERY_STATUS 的 20 字节输入缓冲并返回（BatteryTag + Timeout + PowerState + LowCapacity + HighCapacity）。
    /// 输入缓冲不足 20 字节时驱动会返回 ERROR_INSUFFICIENT_BUFFER，导致状态查询失败。
    /// </summary>
    private static byte[] BuildWaitStatus(uint tag)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(WaitStatusBuf, tag);
        // Timeout/PowerState/LowCapacity/HighCapacity 均为 0：立即返回、不限状态、不设容量范围
        return WaitStatusBuf;
    }

    /// <summary>IOCTL 包装：统一处理输入缓冲与返回字节数。</summary>
    private static bool IoControl(IntPtr h, uint code, byte[]? inBuf, byte[] outBuf, out int returned)
    {
        returned = 0;
        return DeviceIoControl(h, code, inBuf ?? Array.Empty<byte>(), inBuf?.Length ?? 0,
            outBuf, outBuf.Length, out returned, IntPtr.Zero);
    }

    /// <summary>判断句柄是否为无效值（NULL 或 INVALID_HANDLE_VALUE）。</summary>
    private static bool IsInvalidHandle(IntPtr h) => h == IntPtr.Zero || h == new IntPtr(-1);

    /// <summary>BATTERY_INFORMATION（batclass.h）：电池静态能力信息。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInfoStruct
    {
        public uint Capabilities; // 能力位（BATTERY_CAPACITY_RELATIVE 等）
        public byte Technology; // 化学技术（0=其他 1=镍镉 2=镍氢 3=锂离子 4=锂聚合物）
        public byte Reserved0, Reserved1, Reserved2; // 保留
        public byte Chemistry0, Chemistry1, Chemistry2, Chemistry3; // 化学组成标识（如 "LION"）
        public uint DesignedCapacity; // 设计容量（相对值时单位任意，绝对值为 mWh）
        public uint FullChargedCapacity; // 满充容量（当前可达到的最大容量）
        public uint DefaultAlert1; // 低电量报警阈值
        public uint DefaultAlert2; // 严重低电量报警阈值
        public uint CriticalBias; // 临界偏置（设计容量与满充容量之比）
        public uint CycleCount; // 充放电循环次数
    }

    /// <summary>BATTERY_STATUS（batclass.h）：电池实时状态。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState; // 状态位（BATTERY_POWER_ON_LINE / BATTERY_CHARGING 等）
        public uint Capacity; // 当前剩余容量（单位与 BATTERY_INFORMATION 一致）
        public uint Voltage; // 当前电压（mV）
        public uint Rate; // 充放电速率（正数放电、负数充电）
    }

    /// <summary>SP_DEVICE_INTERFACE_DATA（setupapi.h）：设备接口信息，供 SetupDiEnumDeviceInterfaces 使用。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize; // 结构大小，每次调用前必须填充为 Marshal.SizeOf 的值
        public Guid InterfaceClassGuid; // 设备接口类 GUID
        public uint Flags; // 保留标志
        public IntPtr Reserved; // 保留，必须为 IntPtr.Zero
    }

    /// <summary>
    /// SP_DEVICE_INTERFACE_DETAIL_DATA（setupapi.h）：设备接口详细信息。
    /// C 结构为 { DWORD cbSize; WCHAR DevicePath[1]; }，DevicePath 为变长数组，
    /// 此处仅以单个 char 占位，通过 Marshal.OffsetOf 计算路径在缓冲区中的偏移。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SpDeviceInterfaceDetailData
    {
        public uint cbSize; // 结构大小（MSDN：x86 填 6，x64 填 8）
        public char DevicePath; // 设备路径（变长数组，占位用）
    }
}

/// <summary>单个电池设备的完整信息（身份 + 静态容量 + 实时状态），供上层 UI 展示。</summary>
internal sealed class BatteryInfo
{
    // 身份信息
    /// <summary>设备接口完整路径（如 \\?\ACPI#...），按路径精准查询的关键标识。</summary>
    public string DevicePath = "";

    /// <summary>设备名（同时用作电源的显示名称）。</summary>
    public string DeviceName = "";

    /// <summary>制造商。</summary>
    public string Manufacturer = "";

    /// <summary>序列号。</summary>
    public string SerialNumber = "";

    // 容量信息（单位由 CapacityUnits 指定）
    /// <summary>设计容量（出厂满容量）。</summary>
    public int DesignCapacity;

    /// <summary>满充容量（当前可达到的最大容量）。</summary>
    public int FullChargeCapacity;

    /// <summary>当前剩余容量。</summary>
    public int CurrentCapacity;

    /// <summary>容量单位："MWH"（毫瓦时）或 "RELATIVE"（相对值）。</summary>
    public string CapacityUnits = "";

    // 实时状态
    /// <summary>是否接入交流电源。</summary>
    public bool PowerOnLine;

    /// <summary>是否正在充电。</summary>
    public bool Charging;
}