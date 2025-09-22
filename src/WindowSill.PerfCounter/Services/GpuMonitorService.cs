using System.Runtime.InteropServices;

using Windows.Win32;

namespace WindowSill.PerfCounter.Services;

internal class GpuMonitorService : IDisposable
{
    private readonly List<nint> _gpuCounters = [];
    private nint _query = nint.Zero;
    private bool _initialized = false;
    private readonly object _lockObject = new();

    public double? GetGpuUsage()
    {
        lock (_lockObject)
        {
            if (!_initialized && !InitializeGpuCounters())
            {
                return null;
            }

            return CollectGpuData();
        }
    }

    private bool InitializeGpuCounters()
    {
        try
        {
            // Close existing query if any
            if (_query != nint.Zero)
            {
                PdhCloseQuery(_query);
                _query = nint.Zero;
            }

            // Clear existing counters
            _gpuCounters.Clear();

            // Open a new query
            var status = PdhOpenQuery(nint.Zero, nint.Zero, out _query);
            if (status != 0)
            {
                return false;
            }

            // Try to add wildcard GPU counter
            var status3 = PdhAddCounter(_query, "\\GPU Engine(*)\\Utilization Percentage", nint.Zero, out var counter);
            if (status3 == 0)
            {
                _gpuCounters.Add(counter);
                _initialized = true;
                return true;
            }

            // If wildcard fails, try enumeration
            var instanceNames = EnumerateGpuEngineInstances();
            
            foreach (var instanceName in instanceNames)
            {
                var counterPath = $"\\GPU Engine({instanceName})\\Utilization Percentage";
                
                var status2 = PdhAddCounter(_query, counterPath, nint.Zero, out var instanceCounter);
                if (status2 == 0)
                {
                    _gpuCounters.Add(instanceCounter);
                }
            }

            _initialized = _gpuCounters.Count > 0;
            return _initialized;
        }
        catch
        {
            return false;
        }
    }

    private List<string> EnumerateGpuEngineInstances()
    {
        var instances = new List<string>();
        
        try
        {
            // Get buffer sizes first
            var counterListSize = 0u;
            var instanceListSize = 0u;
            
            var status
                = PdhEnumObjectItems(
                    null,
                    null,
                    "GPU Engine", 
                    nint.Zero,
                    ref counterListSize, 
                    nint.Zero, ref instanceListSize,
                    PERF_DETAIL.PERF_DETAIL_WIZARD,
                    0);

            if (status != PInvoke.PDH_MORE_DATA || instanceListSize == 0) // PDH_MORE_DATA
            {
                return GetFallbackInstances();
            }

            // Allocate buffer for instance names
            var buffer = Marshal.AllocHGlobal((int)instanceListSize * 2);
            
            try
            {
                status
                    = PdhEnumObjectItems(
                        null,
                        null,
                        "GPU Engine",
                        nint.Zero,
                        ref counterListSize,
                        buffer,
                        ref instanceListSize,
                        PERF_DETAIL.PERF_DETAIL_WIZARD,
                        0);

                if (status == 0)
                {
                    // Parse null-terminated string list
                    var current = buffer;
                    while (true)
                    {
                        var instanceName = Marshal.PtrToStringUni(current);
                        if (string.IsNullOrEmpty(instanceName))
                            break;

                        instances.Add(instanceName);
                        current = IntPtr.Add(current, (instanceName.Length + 1) * 2);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Fall back to common patterns
            instances = GetFallbackInstances();
        }

        return instances;
    }

    private static List<string> GetFallbackInstances()
    {
        return
        [
            "pid_*_luid_*_phys_*_eng_*_engtype_3D",
            "pid_*_luid_*_phys_*_eng_*_engtype_Graphics",
            "pid_*_luid_*_phys_*_eng_*_engtype_Compute"
        ];
    }

    private double? CollectGpuData()
    {
        if (_query == nint.Zero || _gpuCounters.Count == 0)
        {
            return null;
        }

        try
        {
            // Collect data twice with a small delay for accurate readings
            PdhCollectQueryData(_query);
            Thread.Sleep(100);
            PdhCollectQueryData(_query);

            double totalUsage = 0.0;
            var validCounters = 0;

            foreach (var counter in _gpuCounters)
            {
                // Try to get formatted counter array first (for wildcard counters)
                var bufferSize = 0u;
                var itemCount = 0u;
                
                var status = PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE,
                    ref bufferSize, ref itemCount, nint.Zero);

                if (status == PInvoke.PDH_MORE_DATA && itemCount > 0)
                {
                    var buffer = Marshal.AllocHGlobal((int)bufferSize);
                    try
                    {
                        status = PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE,
                            ref bufferSize, ref itemCount, buffer);

                        if (status == 0)
                        {
                            // Sum up all GPU engine utilizations
                            var current = buffer;
                            for (var i = 0; i < itemCount; i++)
                            {
                                var item = Marshal.PtrToStructure<PdhFmtCounterValueItemDouble>(current);
                                if (item.FmtValue.CStatus == PInvoke.PDH_CSTATUS_VALID_DATA) 
                                {
                                    totalUsage += item.FmtValue.doubleValue;
                                    validCounters++;
                                }
                                current = IntPtr.Add(current, Marshal.SizeOf<PdhFmtCounterValueItemDouble>());
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                else
                {
                    // Try single counter value
                    var counterValue = new PdhFmtCounterValue();
                    var status2 = PdhGetFormattedCounterValue(counter, PDH_FMT.PDH_FMT_DOUBLE, nint.Zero, ref counterValue);
                    
                    if (status2 == 0 && counterValue.CStatus == 0)
                    {
                        totalUsage += counterValue.doubleValue;
                        validCounters++;
                    }
                }
            }

            // Return average usage, clamped between 0 and 100
            return Math.Max(0.0, Math.Min(100.0, totalUsage));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            if (_query != nint.Zero)
            {
                PdhCloseQuery(_query);
                _query = nint.Zero;
            }
            _gpuCounters.Clear();
            _initialized = false;
        }
    }

    #region PDH P/Invoke Declarations

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItemDouble
    {
        public nint szName;
        public PdhFmtCounterValue FmtValue;
    }

    private enum PDH_FMT : uint
    {
        PDH_FMT_DOUBLE = 512U,
        PDH_FMT_LARGE = 1024U,
        PDH_FMT_LONG = 256U,
    }

    private enum PERF_DETAIL : uint
    {
        PERF_DETAIL_NOVICE = 100U,
        PERF_DETAIL_ADVANCED = 200U,
        PERF_DETAIL_EXPERT = 300U,
        PERF_DETAIL_WIZARD = 400U,
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(nint szDataSource, nint dwUserData, out nint phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddCounter(nint hQuery, string szFullCounterPath, nint dwUserData, out nint phCounter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint hQuery);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint hCounter, PDH_FMT dwFormat, nint lpdwType, ref PdhFmtCounterValue pValue);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArray(nint hCounter, PDH_FMT dwFormat, ref uint lpdwBufferSize, ref uint lpdwItemCount, nint ItemBuffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhEnumObjectItems(
        string? szDataSource,
        string? szMachineName,
        string szObjectName,
        nint mszCounterList,
        ref uint pcchCounterListLength,
        nint mszInstanceList,
        ref uint pcchInstanceListLength,
        PERF_DETAIL dwDetailLevel,
        uint dwFlags);

    #endregion
}
