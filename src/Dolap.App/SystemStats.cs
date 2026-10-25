using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Dolap.App;

public sealed record DriveStat(string Name, double UsedGb, double TotalGb)
{
    public double Percent => TotalGb <= 0 ? 0 : UsedGb / TotalGb * 100;
}
public sealed record ProcStat(string Name, double Cpu, double RamMb);
public sealed record Snapshot(double Cpu, double? Gpu, double RamUsedGb, double RamTotalGb, List<DriveStat> Drives, List<ProcStat> Top);

/// <summary>Sistem olcumleri: CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx), GPU (PDH "GPU Engine" 3D), disk (DriveInfo),
/// surecler (islemci zamani farki). Ek paket yok. Sample() arka planda cagrilir; ilk cagri taban degeri toplar.</summary>
sealed class SystemStats : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr q);

    ulong _idle, _total;
    readonly Dictionary<int, TimeSpan> _procTimes = [];
    DateTime _procAt;
    IntPtr _query, _gpu;
    readonly bool _gpuOk;

    public SystemStats()
    {
        _gpuOk = PdhOpenQuery(null, IntPtr.Zero, out _query) == 0
                 && PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu) == 0;
        if (_gpuOk) PdhCollectQueryData(_query);
    }

    List<ProcStat> _top = []; List<DriveStat> _drives = []; int _tick; double? _gpuValue;

    /// <summary>CPU/RAM her cagrida; GPU 4, surec listesi 3, disk 15 cagride bir (en pahali kisimlar, boylece monitorun kendi yuku dusuk).</summary>
    public Snapshot Sample()
    {
        var ram = Ram();
        // GPU sayaci (155+ motor ornegi) en pahali olcum: 4 cagride bir (~8 sn).
        if (_tick % 4 == 0) _gpuValue = Gpu();
        if (_tick % 3 == 0) _top = TopProcesses();
        if (_tick % 15 == 0) _drives = Drives();
        _tick++;
        return new(Cpu(), _gpuValue, ram.Used, ram.Total, _drives, _top);
    }

    double Cpu()
    {
        GetSystemTimes(out var i, out var k, out var u);
        ulong idle = i.Value, total = k.Value + u.Value; // kernel zamani idle'i icerir
        var di = idle - _idle; var dt = total - _total;
        _idle = idle; _total = total;
        return dt == 0 ? 0 : Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
    }

    static (double Used, double Total) Ram()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        const double G = 1024d * 1024 * 1024;
        return ((m.ullTotalPhys - m.ullAvailPhys) / G, m.ullTotalPhys / G);
    }

    /// <summary>Tum 3D motor orneklerinin toplami (Gorev Yoneticisi'ndeki GPU yuzdesine yakin).</summary>
    double? Gpu()
    {
        if (!_gpuOk || PdhCollectQueryData(_query) != 0) return null;
        uint size = 0;
        PdhGetFormattedCounterArray(_gpu, 0x200, ref size, out _, IntPtr.Zero);
        if (size == 0) return 0;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_gpu, 0x200, ref size, out var count, buf) != 0) return null;
            double sum = 0;
            var item = IntPtr.Size + 16; // PDH_FMT_COUNTERVALUE_ITEM_W: isim isaretcisi + {CStatus, double}
            for (var n = 0; n < count; n++)
            {
                var p = buf + n * item;
                if (Marshal.ReadInt32(p + IntPtr.Size) == 0) sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p + IntPtr.Size + 8));
            }
            return Math.Clamp(sum, 0, 100);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static List<DriveStat> Drives()
    {
        const double G = 1024d * 1024 * 1024;
        var list = new List<DriveStat>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new(d.Name.TrimEnd('\\'), (d.TotalSize - d.TotalFreeSpace) / G, d.TotalSize / G));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Iki ornek arasi islemci zamani farki / gecen sure / cekirdek sayisi. Erisim reddedilen surecler atlanir.</summary>
    List<ProcStat> TopProcesses()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _procAt).TotalMilliseconds * Environment.ProcessorCount;
        _procAt = now;
        var seen = new Dictionary<int, TimeSpan>();
        var stats = new List<ProcStat>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;
                    var t = p.TotalProcessorTime;
                    seen[p.Id] = t;
                    var cpu = _procTimes.TryGetValue(p.Id, out var prev) && elapsed > 0 ? (t - prev).TotalMilliseconds / elapsed * 100 : 0;
                    stats.Add(new(p.ProcessName, Math.Max(0, cpu), p.WorkingSet64 / 1048576d));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        _procTimes.Clear();
        foreach (var kv in seen) _procTimes[kv.Key] = kv.Value;
        // Ayni adli surecler (tarayici sekmeleri vb.) tek satirda toplanir.
        return stats.GroupBy(s => s.Name)
            .Select(g => new ProcStat(g.Key, g.Sum(x => x.Cpu), g.Sum(x => x.RamMb)))
            .OrderByDescending(s => s.Cpu).ThenByDescending(s => s.RamMb).Take(6).ToList();
    }

    public void Dispose() { if (_query != IntPtr.Zero) PdhCloseQuery(_query); }
}
