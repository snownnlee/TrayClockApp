using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.PawnIo;
using Microsoft.Win32;
using TrayClockApp.Core;

namespace TrayClockApp.Infra;

/// <summary>
/// 一次功耗采样结果。算法说明见 <see cref="PowerMonitor.Sample"/> 内的 ①②③ 分步注释。
/// </summary>
internal sealed class PowerSample
{
    /// <summary>整机功率（W）；<see cref="IsEstimated"/> 为 true 时为估算值。</summary>
    public double Watts { get; init; }

    /// <summary>true = 估算值；false = 电池放电实测值。</summary>
    public bool IsEstimated { get; init; }

    /// <summary>CPU 总负载（0~100）。</summary>
    public double CpuLoadPct { get; init; }

    /// <summary>是否接通交流电。</summary>
    public bool AcOnline { get; init; }

    /// <summary>剩余电量百分比（无电池为 null）。</summary>
    public double? BatteryPct { get; init; }

    /// <summary>预计可用时间（分钟，仅放电中有值）。</summary>
    public double? RemainingMinutes { get; init; }

    /// <summary>CPU 封装功耗实测值（W，Intel RAPL）；不可用为 null。</summary>
    public double? PkgWatts { get; init; }
}

/// <summary>RAPL 可用状态。宿主程序据此给出精准提示并决定回退策略。</summary>
internal enum RaplState
{
    /// <summary>尚未初始化 / 未启用。</summary>
    Disabled,

    /// <summary>可用，读数为 CPU 封装实测值。</summary>
    Available,

    /// <summary>需要管理员权限（PawnIO 驱动加载要求）。</summary>
    NotAdmin,

    /// <summary>未安装 PawnIO 驱动。</summary>
    DriverNotInstalled,

    /// <summary>驱动已安装但读取失败（未启动 / 被杀软或系统策略拦截）。</summary>
    DriverInactive,

    /// <summary>读取成功但计数器不增长（权限不足或驱动失效）。</summary>
    CounterNotAdvancing,

    /// <summary>CPU 不支持 RAPL（非 Intel 或型号过旧）。</summary>
    CpuNotSupported
}

/// <summary>
/// Intel RAPL：直接读 MSR 0x611 能量计数器差分求 CPU 封装功耗。
/// AMD 平台对应 MSR_RAPL_POWER_UNIT = 0xC0010299 / PKG_ENERGY_STATUS = 0xC001029B（单位公式相同）。
/// </summary>
internal sealed class RaplReader : IDisposable
{
    /// <summary>能量单位寄存器。</summary>
    private const uint MsrRaplPowerUnit = 0x606;

    /// <summary>包能量累计计数器（32 位，会回绕）。</summary>
    private const uint MsrPkgEnergyStatus = 0x611;

    /// <summary>PawnIO 驱动下载页。</summary>
    public const string DriverDownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases";

    /// <summary>PawnIO 静默安装命令（需管理员 CMD）。</summary>
    public const string DriverInstallCommand = "PawnIO_setup.exe -install -silent";

    private IntelMsr? _msr;
    private double _energyUnitUj;
    private ulong _lastEnergy;
    private long _lastTimestamp;
    private int _zeroStreak; // 连续读到 0 增量的次数（用于识别“假成功”）

    /// <summary>RAPL 可用状态。</summary>
    private RaplState State { get; set; } = RaplState.Disabled;

    /// <summary>RAPL 是否可用；不可用时上层自动回退为“CPU 负载 × TDP”估算。</summary>
    public bool Available => State == RaplState.Available;

    /// <summary>状态摘要（一行，可直接显示在界面上）。</summary>
    public string Status { get; private set; } = "未初始化";

    /// <summary>
    /// 初始化并自检。失败不抛异常：仅设置 State/Status，由上层回退到估算，程序照常工作。
    /// </summary>
    public void TryStart()
    {
        State = RaplState.Disabled;
        Status = "未初始化";

        try
        {
            var msr = new IntelMsr();

            // ① 读单位寄存器（支持 RAPL 的 Intel CPU 上恒为非 0；返回 0 即判定不可用）
            if (!msr.ReadMsr(MsrRaplPowerUnit, out var unit) || unit == 0)
            {
                msr.Close();
                SetEnvironmentFailure();
                return;
            }

            // 能量单位 = 0.5^(unit[12:8]) 焦耳，转为微焦
            _energyUnitUj = Math.Pow(0.5, (int)((unit >> 8) & 0x1F)) * 1e6;

            if (_energyUnitUj <= 0 || !msr.ReadMsr(MsrPkgEnergyStatus, out _lastEnergy))
            {
                msr.Close();
                State = RaplState.CpuNotSupported;
                Status = "RAPL 不可用：CPU 不支持能量计数器";
                return;
            }

            _lastTimestamp = Stopwatch.GetTimestamp();

            // ② 关键自检：非管理员下 PawnIO 会“读取成功但恒返回 0”，必须确认计数器真的在增长
            Thread.Sleep(400);
            if (!msr.ReadMsr(MsrPkgEnergyStatus, out var verify) || verify == _lastEnergy)
            {
                msr.Close();
                if (!IsAdmin)
                {
                    State = RaplState.NotAdmin;
                    Status = "RAPL 不可用：需要管理员权限";
                }
                else
                {
                    State = RaplState.DriverInactive;
                    Status = "RAPL 不可用：驱动可读但计数器不增长（驱动可能被拦截）";
                }

                return;
            }

            _msr = msr;
            _lastEnergy = verify;
            _lastTimestamp = Stopwatch.GetTimestamp();
            State = RaplState.Available;
            Status = "RAPL 已启用（CPU Package 实测）";
        }
        catch (Exception ex)
        {
            SetEnvironmentFailure(ex.Message);
        }
    }

    /// <summary>根据运行环境（管理员 / 驱动是否安装）给出精确的不可用原因与指引。</summary>
    private void SetEnvironmentFailure(string? detail = null)
    {
        if (!IsAdmin)
        {
            State = RaplState.NotAdmin;
            Status = "RAPL 不可用：需要管理员权限";
        }
        else if (!PawnIoInstalled)
        {
            State = RaplState.DriverNotInstalled;
            Status = "RAPL 不可用：未安装 PawnIO 驱动";
        }
        else
        {
            State = RaplState.DriverInactive;
            Status = "RAPL 不可用：PawnIO 已安装但驱动未生效";
        }

        if (!string.IsNullOrEmpty(detail)) Status += $"（{detail}）";
    }

    /// <summary>当前进程是否具有管理员权限。</summary>
    private static bool IsAdmin
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>系统是否已注册 PawnIO 驱动服务（判断“装没装驱动”，与权限无关）。</summary>
    private static bool PawnIoInstalled
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                return key is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>CPU 封装功率（W）。不可用或读取失败返回 null。</summary>
    public double? ReadPkgWatts()
    {
        if (_msr is null) return null;

        try
        {
            if (!_msr.ReadMsr(MsrPkgEnergyStatus, out var cur)) return null;

            var now = Stopwatch.GetTimestamp();
            var seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (seconds < 0.1) return null; // 间隔过短，差分噪声大，跳过本次

            // 32 位计数器回绕处理
            var delta = cur >= _lastEnergy
                ? cur - _lastEnergy
                : (1UL << 32) - _lastEnergy + cur;

            _lastEnergy = cur;
            _lastTimestamp = now;

            // 运行期兜底：连续多次 0 增量说明权限/驱动失效，自动停用 RAPL 并降级
            if (delta == 0)
            {
                if (++_zeroStreak < 10) return delta * _energyUnitUj / seconds / 1e6; // → W
                if (!IsAdmin)
                {
                    State = RaplState.NotAdmin;
                    Status = "RAPL 已自动关闭：读数恒为 0（需要管理员权限）";
                }
                else
                {
                    State = RaplState.CounterNotAdvancing;
                    Status = "RAPL 已自动关闭：读数恒为 0（驱动失效）";
                }

                return null;
            }

            _zeroStreak = 0;

            return delta * _energyUnitUj / seconds / 1e6; // → W
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _msr?.Close();
        _msr = null;
    }
}

/// <summary>
/// 整机功耗监控：电池实测 + Intel RAPL + CPU 负载启发式 三级融合。
/// 线程安全说明：<see cref="Sample"/> 非线程安全，请由单一线程/定时器串行调用。
/// </summary>
internal sealed partial class PowerMonitor : IDisposable
{
    /// <summary>机型默认底噪（W）：有电池 10，无电池 30。构造时确定，运行期不再变化。</summary>
    public double DefaultIdleWatts { get; private set; } = 30.0;

    /// <summary>手动指定的底噪（W）；≤ 0 表示“自动”。手动锁定期间不再做放电校准。</summary>
    public double ManualIdleWatts { get; set; } = -1;

    /// <summary>是否处于“自动”底噪模式（未被手动锁定）。</summary>
    public bool AutoIdle => ManualIdleWatts <= 0;

    /// <summary>
    /// 放电校准得到的底噪（W，放电会话内 baseline 的时间窗口均值）；≤ 0 表示尚未校准。
    /// 可写入上次落盘的结果，让重启后直接沿用（下次放电时由新会话的窗口均值覆盖）。
    /// </summary>
    public double CalibratedIdleWatts
    {
        get => _calibratedIdle;
        set => _calibratedIdle = double.IsFinite(value) && value is > 1 and < 200 ? value : -1;
    }

    /// <summary>当前实际参与估算的底噪（W）：手动 &gt; 放电校准 &gt; 机型默认。</summary>
    public double EffectiveIdleWatts => ManualIdleWatts > 0
        ? ManualIdleWatts
        : _calibratedIdle > 0
            ? _calibratedIdle
            : DefaultIdleWatts;

    /// <summary>当前底噪数值的来源，供界面显示。</summary>
    public string IdleWattsSource => ManualIdleWatts > 0
        ? "手动"
        : _calibratedIdle > 0
            ? "放电校准"
            : "机型默认";

    /// <summary>CPU 的 TDP（W）。仅在 RAPL 不可用时参与估算。</summary>
    private double TdpWatts { get; set; } = 65.0;

    /// <summary>RAPL（CPU 封装实测）是否可用。</summary>
    public bool RaplAvailable => _rapl.Available;

    /// <summary>RAPL 状态摘要（不可用时给出原因，便于界面提示/排查）。</summary>
    public string RaplStatus => _rapl.Status;

    /// <summary>重新检测 RAPL（用户安装驱动 / 提权重启后调用）。</summary>
    public void RetryRapl() => _rapl.TryStart();

    // ---------- 内部状态（必须跨采样保持，切勿每帧 new 本类） ----------
    private readonly RaplReader _rapl = new();
    private double _calibratedIdle = -1; // 放电校准（窗口均值）的底噪（-1 = 未校准；手动锁定期间保持不变）
    private double _dischargeEma = -1; // 电池放电功率 EMA（-1 = 无数据/插电中）

    /// <summary>校准时间窗口内的 baseline 样本（时间戳, 值），超出窗口的样本出队。</summary>
    private readonly Queue<(long Timestamp, double Baseline)> _baselineWindow = new();

    /// <summary>窗口内 baseline 之和（O(1) 求均值，避免每帧遍历）。</summary>
    private double _baselineSum;

    private ulong _lastIdle, _lastKernel, _lastUser;

    public PowerMonitor()
    {
        Native.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);

        // 有电池（笔记本）默认底噪 10W / TDP 28W；无电池（台式机）默认底噪 30W / TDP 65W
        if (ReadBatteryState().HasBattery)
        {
            DefaultIdleWatts = 10.0;
            TdpWatts = 28.0;
        }

        _rapl.TryStart();
    }

    /// <summary>采样一次。建议间隔 1000ms（500ms~2s 均可，过短 RAPL 差分噪声大）。</summary>
    public PowerSample Sample()
    {
        var cpu = ReadCpuLoad();
        var bat = ReadBatteryState();
        var pkg = _rapl.ReadPkgWatts();

        // ① 电池放电功率 EMA 平滑 + 尖峰剔除（供电切换瞬间计量值会突跳）
        if (bat is { HasBattery: true, AcOnline: false })
        {
            var raw = bat.DischargeMw / 1000.0;
            if (raw is > 0 and < 250)
                _dischargeEma = _dischargeEma < 0 ? raw : _dischargeEma * 0.9 + raw * 0.1; // α=0.1
        }
        else
        {
            _dischargeEma = -1; // 切到插电状态必须重置，避免跨状态污染
            ResetBaselineWindow(); // 本次放电会话结束：清空窗口，但保留已算出的窗口均值
        }

        // ② 电池放电中：整机实测值 + 顺带校准底噪
        if (bat is { HasBattery: true, AcOnline: false, DischargeMw: > 0 })
        {
            var watts = _dischargeEma > 0 ? _dischargeEma : bat.DischargeMw / 1000.0;

            // 有 RAPL 实测值且处于自动模式时，用 (整机 − CPU 包) 反推底噪，并入时间窗口统计；
            // 手动锁定底噪期间跳过校准，避免手动值被悄悄覆盖
            if (pkg is { } p && AutoIdle)
            {
                var baseline = watts - p; // 非 CPU 部分 ≈ 屏幕/内存/硬盘/主板
                if (baseline is > 1 and < 200) // 异常样本丢弃
                    PushBaseline(baseline);
            }

            return new PowerSample
            {
                Watts = Math.Round(watts, 1),
                IsEstimated = false,
                CpuLoadPct = cpu,
                AcOnline = false,
                BatteryPct = bat.RemainPct,
                RemainingMinutes = bat.RemainMin,
                PkgWatts = pkg,
            };
        }

        // ③ 插电 / 无电池：底噪 + CPU 部分功耗
        var idle = EffectiveIdleWatts;
        var cpuPart = pkg ?? cpu / 100.0 * TdpWatts * 0.9;

        return new PowerSample
        {
            Watts = Math.Round(idle + cpuPart, 1),
            IsEstimated = true,
            CpuLoadPct = cpu,
            AcOnline = bat.AcOnline,
            BatteryPct = bat.RemainPct,
            PkgWatts = pkg,
        };
    }

    /// <summary>
    /// 把一次有效 baseline 压入时间窗口，并用窗口算术平均刷新校准值。
    /// 单点波动会被窗口摊平；切到插电后估算用的就是这个均值。
    /// </summary>
    private void PushBaseline(double baseline)
    {
        var now = Stopwatch.GetTimestamp();
        _baselineWindow.Enqueue((now, baseline));
        _baselineSum += baseline;

        var cutoff = now - WindowTicks;
        while (_baselineWindow.Count > 0 && _baselineWindow.Peek().Timestamp < cutoff)
            _baselineSum -= _baselineWindow.Dequeue().Baseline;

        // 刚入队的那一个必然在窗口内，Count 不会为 0
        _calibratedIdle = _baselineSum / _baselineWindow.Count;
    }

    /// <summary>窗口时长（<see cref="Constants.IdleCalibrationWindowS"/>）对应的 Stopwatch 刻度数。</summary>
    private static long WindowTicks => (long)(Constants.IdleCalibrationWindowS * (double)Stopwatch.Frequency);

    /// <summary>清空窗口（放电会话结束时调用），已算出的窗口均值保留在 _calibratedIdle 中。</summary>
    private void ResetBaselineWindow()
    {
        if (_baselineWindow.Count == 0) return;
        _baselineWindow.Clear();
        _baselineSum = 0;
    }

    /// <summary>CPU 总负载（GetSystemTimes 差分；kernelTime 已含 idleTime，需减去）。</summary>
    private double ReadCpuLoad()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        var di = idle - _lastIdle;
        var dk = kernel - _lastKernel;
        var du = user - _lastUser;

        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;

        var busy = dk - di + du;
        var total = dk + du;
        return total == 0 ? 0 : Math.Clamp(busy * 100.0 / total, 0, 100);
    }

    private readonly struct BatteryState(
        bool hasBattery,
        bool acOnline,
        int dischargeMw,
        double? remainPct,
        double remainMin)
    {
        public bool HasBattery { get; } = hasBattery;
        public bool AcOnline { get; } = acOnline;
        public int DischargeMw { get; } = dischargeMw;
        public double? RemainPct { get; } = remainPct;
        public double RemainMin { get; } = remainMin;
    }

    private static BatteryState ReadBatteryState()
    {
        if (Native.CallNtPowerInformation(5 /* SystemBatteryState */, IntPtr.Zero, 0,
                out var st, (uint)Marshal.SizeOf<Native.SystemBatteryState>()) != 0
            || st.BatteryPresent == 0 || st.MaxCapacity == 0)
        {
            return new BatteryState(false, true, 0, null, 0);
        }

        var pct = Math.Clamp(st.RemainingCapacity * 100.0 / st.MaxCapacity, 0, 100);

        // ⚠️ Rate 为有符号功率，本机约定“负值 = 放电”（与 AIDA64 口径一致，与 MSDN 文档相反），
        //    按 Discharging 标志位取绝对值最稳妥。
        var dischargeMw = st.Discharging != 0 ? Math.Abs(st.Rate) : 0;

        // EstimatedTime 单位秒；部分机型会填 0 或不合理值，需过滤
        var remainMin = st.Discharging != 0 && st.EstimatedTime is > 0 and < 360000
            ? st.EstimatedTime / 60.0
            : 0;

        return new BatteryState(true, st.AcOnLine != 0, dischargeMw, pct, remainMin);
    }

    public void Dispose() => _rapl.Dispose();

    // =================================================================================
    //  P/Invoke 与结构体定义
    // =================================================================================
    private static partial class Native
    {
        [LibraryImport("powrprof.dll", SetLastError = true)]
        public static partial uint CallNtPowerInformation(
            int informationLevel, IntPtr inputBuffer, uint inputBufferSize,
            out SystemBatteryState outputBuffer, uint outputBufferSize);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemBatteryState
        {
            public byte AcOnLine; // BOOLEAN，偏移 0
            public byte BatteryPresent; // BOOLEAN，偏移 1
            public byte Charging; // BOOLEAN，偏移 2
            public byte Discharging; // BOOLEAN，偏移 3

            public byte Spare1, Spare2, Spare3, Spare4; // BOOLEAN×4，偏移 4~7

            public uint MaxCapacity; // mWh，偏移 8  —— 文档说是 Tag，实际不是
            public uint RemainingCapacity; // mWh，偏移 12
            public int Rate; // mW，有符号，放电为负，偏移 16
            public uint EstimatedTime; // 秒，偏移 20
            public uint DefaultAlert1; // mWh，偏移 24
            public uint DefaultAlert2; // mWh，偏移 28
        }
    }
}