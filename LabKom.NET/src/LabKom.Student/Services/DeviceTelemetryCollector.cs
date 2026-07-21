using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;

namespace LabKom.Student.Services;

[SupportedOSPlatform("windows")]
public sealed class DeviceTelemetryCollector
{
    private readonly object _sync = new();
    private readonly MachineIdentity _identity;
    private ulong _lastIdleTime;
    private ulong _lastKernelTime;
    private ulong _lastUserTime;
    private long _lastReceiveBytes;
    private long _lastSendBytes;
    private long _lastNetworkTimestamp;
    private long _sequence;

    public DeviceTelemetryCollector(MachineIdentity identity)
    {
        _identity = identity;
        ReadCpuTimes(out _lastIdleTime, out _lastKernelTime, out _lastUserTime);
        (_lastReceiveBytes, _lastSendBytes) = ReadNetworkTotals();
        _lastNetworkTimestamp = Stopwatch.GetTimestamp();
    }

    public DeviceTelemetry Capture()
    {
        lock (_sync)
        {
            var cpu = ReadCpuPercent();
            var (usedMemory, totalMemory) = ReadMemory();
            var (diskFree, diskTotal) = ReadSystemDisk();
            var (receiveRate, sendRate) = ReadNetworkRates();
            using var process = Process.GetCurrentProcess();

            return new DeviceTelemetry(
                _identity.PcName,
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Math.Max(0, Environment.TickCount64 / 1000),
                cpu,
                usedMemory,
                totalMemory,
                diskFree,
                diskTotal,
                receiveRate,
                sendRate,
                Math.Max(0, process.WorkingSet64),
                Math.Max(1, process.Threads.Count));
        }
    }

    private double ReadCpuPercent()
    {
        if (!ReadCpuTimes(out var idle, out var kernel, out var user)) return 0;
        var idleDelta = idle - _lastIdleTime;
        var kernelDelta = kernel - _lastKernelTime;
        var userDelta = user - _lastUserTime;
        _lastIdleTime = idle;
        _lastKernelTime = kernel;
        _lastUserTime = user;

        var total = kernelDelta + userDelta;
        if (total == 0) return 0;
        return Math.Clamp(
            100d * (total - Math.Min(idleDelta, total)) / total,
            0,
            100);
    }

    private (long ReceiveRate, long SendRate) ReadNetworkRates()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastNetworkTimestamp, now);
        var (receive, send) = ReadNetworkTotals();
        var receiveDelta = Math.Max(0, receive - _lastReceiveBytes);
        var sendDelta = Math.Max(0, send - _lastSendBytes);
        _lastReceiveBytes = receive;
        _lastSendBytes = send;
        _lastNetworkTimestamp = now;
        if (elapsed <= TimeSpan.Zero) return (0, 0);

        var maximum = ContractValidation.MaximumTelemetryNetworkBytesPerSecond;
        return (
            Math.Clamp((long)(receiveDelta / elapsed.TotalSeconds), 0, maximum),
            Math.Clamp((long)(sendDelta / elapsed.TotalSeconds), 0, maximum));
    }

    private static (long Receive, long Send) ReadNetworkTotals()
    {
        long receive = 0;
        long send = 0;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up
                || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            try
            {
                var statistics = network.GetIPStatistics();
                receive = SaturatingAdd(receive, statistics.BytesReceived);
                send = SaturatingAdd(send, statistics.BytesSent);
            }
            catch (NetworkInformationException)
            {
                // Adapter berubah saat enumerasi; sampel berikutnya mencoba lagi.
            }
        }
        return (receive, send);
    }

    private static (long Used, long Total) ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
            throw new InvalidOperationException("GlobalMemoryStatusEx gagal.");
        var total = checked((long)status.TotalPhysical);
        var available = Math.Min(total, checked((long)status.AvailablePhysical));
        return (total - available, total);
    }

    private static (long Free, long Total) ReadSystemDisk()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)
                   ?? throw new InvalidOperationException("Drive sistem tidak ditemukan.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.TotalSize <= 0)
            throw new InvalidOperationException("Drive sistem belum siap.");
        return (drive.AvailableFreeSpace, drive.TotalSize);
    }

    private static bool ReadCpuTimes(
        out ulong idle,
        out ulong kernel,
        out ulong user)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            idle = kernel = user = 0;
            return false;
        }

        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
        return true;
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out NativeFileTime idleTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeFileTime
    {
        private readonly uint _low;
        private readonly uint _high;
        public ulong Value => ((ulong)_high << 32) | _low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
