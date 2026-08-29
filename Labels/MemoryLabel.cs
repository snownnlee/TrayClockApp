using System.Runtime.InteropServices;
using System.Windows.Media;
using TrayClockApp.Core;

namespace TrayClockApp.Labels;

public partial class MemoryLabel : LabelBase
{
    private const string Emoji = "\U0001F4BE ";

    private volatile string _cachedMemoryRate = Emoji + "--- / --- GB";
    private Timer? _sampler;
    private MemoryStatusEx _status = new() { DwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public override void Init()
    {
        base.Init();
        Text.Text = _cachedMemoryRate;
        Text.Foreground = Brushes.Yellow;
        Text.Width = 140;
    }

    public override void Update()
    {
        Text.Text = _cachedMemoryRate;
    }

    protected override void StartOnce()
    {
        _sampler = new Timer(_ => CalculateMemoryRate(), null, 0, 1000);
    }

    public override void OnStop()
    {
        _sampler?.Dispose();
        _sampler = null;
        base.OnStop();
    }

    private void CalculateMemoryRate()
    {
        try
        {
            if (!GlobalMemoryStatusEx(ref _status)) return;
            var totalGb = _status.UllTotalPhys / (double)Constants.DataSize1Gb;
            var usedGb = (_status.UllTotalPhys - _status.UllAvailPhys) / (double)Constants.DataSize1Gb;
            _cachedMemoryRate = Emoji + $"{usedGb:F1} / {totalGb:F1} GB";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"内存采样失败: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;
    }
}