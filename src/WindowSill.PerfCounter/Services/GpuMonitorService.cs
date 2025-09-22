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
            // Check if there's a dedicated GPU first
            if (!HasDedicatedGpu())
            {
                return false;
            }

            // Close existing query if any
            if (_query != nint.Zero)
            {
                PdhCloseQuery(_query);
                _query = nint.Zero;
            }

            // Clear existing counters
            _gpuCounters.Clear();

            // Open a new query
            uint status = PdhOpenQuery(nint.Zero, nint.Zero, out _query);
            if (status != 0)
            {
                return false;
            }

            // Try to add wildcard GPU counter
            uint status3 = PdhAddCounter(_query, "\\GPU Engine(*)\\Utilization Percentage", nint.Zero, out nint counter);
            if (status3 == 0)
            {
                _gpuCounters.Add(counter);
                _initialized = true;
                return true;
            }

            // If wildcard fails, try enumeration
            List<string> instanceNames = EnumerateGpuEngineInstances();

            foreach (string instanceName in instanceNames)
            {
                string counterPath = $"\\GPU Engine({instanceName})\\Utilization Percentage";

                uint status2 = PdhAddCounter(_query, counterPath, nint.Zero, out nint instanceCounter);
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

    private bool HasDedicatedGpu()
    {
        try
        {
            // Use Registry-based detection - more reliable than DXGI COM
            return CheckRegistryForDedicatedGpu() || CheckGpuCountersExist();
        }
        catch
        {
            // If Registry fails, fall back to checking if GPU performance counters exist
            return CheckGpuCountersExist();
        }
    }

    private bool CheckRegistryForDedicatedGpu()
    {
        try
        {
            // Check the Windows Registry for installed display adapters
            nint hKey = nint.Zero;

            // Open the display adapter registry key
            uint result = RegOpenKeyEx(
                HKEY_LOCAL_MACHINE,
                "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}",
                0,
                KEY_READ,
                out hKey);

            if (result != ERROR_SUCCESS || hKey == nint.Zero)
            {
                return false;
            }

            try
            {
                uint index = 0;
                char[] subKeyName = new char[256];

                while (true)
                {
                    uint subKeyNameSize = (uint)subKeyName.Length;

                    result = RegEnumKeyEx(hKey, index, subKeyName, ref subKeyNameSize,
                        nint.Zero, null, nint.Zero, nint.Zero);

                    if (result != ERROR_SUCCESS)
                    {
                        break;
                    }

                    string subKey = new string(subKeyName, 0, (int)subKeyNameSize);

                    // Skip non-numeric subkeys (like "Properties")
                    if (!subKey.All(char.IsDigit))
                    {
                        index++;
                        continue;
                    }

                    // Check this adapter
                    if (IsAdapterDedicated(hKey, subKey))
                    {
                        return true;
                    }

                    index++;
                }
            }
            finally
            {
                RegCloseKey(hKey);
            }
        }
        catch
        {
            // Registry access failed
        }

        return false;
    }

    private bool IsAdapterDedicated(nint parentKey, string subKeyName)
    {
        try
        {
            nint adapterKey = nint.Zero;
            uint result = RegOpenKeyEx(parentKey, subKeyName, 0, KEY_READ, out adapterKey);

            if (result != ERROR_SUCCESS || adapterKey == nint.Zero)
            {
                return false;
            }

            try
            {
                // Get adapter description
                string description = GetRegistryString(adapterKey, "DriverDesc") ?? "";
                string hardwareId = GetRegistryString(adapterKey, "MatchingDeviceId") ?? "";

                // Combine for analysis
                string adapterInfo = $"{description} {hardwareId}".ToUpperInvariant();

                // Check for dedicated GPU indicators
                return IsDedicatedGpuByDescription(adapterInfo);
            }
            finally
            {
                RegCloseKey(adapterKey);
            }
        }
        catch
        {
            return false;
        }
    }

    private string? GetRegistryString(nint hKey, string valueName)
    {
        try
        {
            uint type = 0;
            uint dataSize = 0;

            // Get the size first
            uint result = RegQueryValueEx(hKey, valueName, nint.Zero, ref type, nint.Zero, ref dataSize);
            if (result != ERROR_SUCCESS || dataSize == 0)
            {
                return null;
            }

            // Allocate buffer and get the actual data
            nint buffer = Marshal.AllocHGlobal((int)dataSize);
            try
            {
                result = RegQueryValueEx(hKey, valueName, nint.Zero, ref type, buffer, ref dataSize);
                if (result == ERROR_SUCCESS && type == REG_SZ)
                {
                    return Marshal.PtrToStringUni(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    private static bool IsDedicatedGpuByDescription(string adapterInfo)
    {
        // Check for dedicated GPU brands/models
        string[] dedicatedKeywords = {
            "NVIDIA", "GEFORCE", "QUADRO", "TESLA", "RTX", "GTX",
            "AMD RADEON RX", "AMD RADEON R9", "AMD RADEON R7",
            "RADEON RX", "RADEON R9", "RADEON R7",
            "RADEON HD 7", "RADEON HD 6", "RADEON HD 5"
        };

        foreach (string keyword in dedicatedKeywords)
        {
            if (adapterInfo.Contains(keyword))
            {
                return true;
            }
        }

        // Check for integrated GPU patterns to exclude
        string[] integratedKeywords = {
            "INTEL HD", "INTEL UHD", "INTEL IRIS",
            "AMD RADEON GRAPHICS", "RADEON VEGA",
            "INTEGRATED", "ONBOARD"
        };

        foreach (string keyword in integratedKeywords)
        {
            if (adapterInfo.Contains(keyword))
            {
                return false;
            }
        }

        // If it contains VEN_ vendor IDs for known dedicated GPU vendors, it's likely dedicated
        if (adapterInfo.Contains("VEN_10DE") || // NVIDIA
            (adapterInfo.Contains("VEN_1002") && !adapterInfo.Contains("RADEON GRAPHICS"))) // AMD (but not APU)
        {
            return true;
        }

        return false;
    }

    private bool CheckGpuCountersExist()
    {
        try
        {
            // Try to check if GPU Engine performance object exists
            uint counterListSize = 0u;
            uint instanceListSize = 0u;

            uint status = PdhEnumObjectItems(
                null,
                null,
                "GPU Engine",
                nint.Zero,
                ref counterListSize,
                nint.Zero,
                ref instanceListSize,
                PERF_DETAIL.PERF_DETAIL_WIZARD,
                0);

            // If GPU Engine object exists and has instances, likely has dedicated GPU
            return status == PInvoke.PDH_MORE_DATA && instanceListSize > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> EnumerateGpuEngineInstances()
    {
        var instances = new List<string>();

        try
        {
            // Get buffer sizes first
            uint counterListSize = 0u;
            uint instanceListSize = 0u;

            uint status
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
            nint buffer = Marshal.AllocHGlobal((int)instanceListSize * 2);

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
                    nint current = buffer;
                    while (true)
                    {
                        string? instanceName = Marshal.PtrToStringUni(current);
                        if (string.IsNullOrEmpty(instanceName))
                        {
                            break;
                        }

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
            int validCounters = 0;

            foreach (nint counter in _gpuCounters)
            {
                // Try to get formatted counter array first (for wildcard counters)
                uint bufferSize = 0u;
                uint itemCount = 0u;

                uint status = PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE,
                    ref bufferSize, ref itemCount, nint.Zero);

                if (status == PInvoke.PDH_MORE_DATA && itemCount > 0)
                {
                    nint buffer = Marshal.AllocHGlobal((int)bufferSize);
                    try
                    {
                        status = PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE,
                            ref bufferSize, ref itemCount, buffer);

                        if (status == 0)
                        {
                            // Sum up all GPU engine utilizations
                            nint current = buffer;
                            for (int i = 0; i < itemCount; i++)
                            {
                                PdhFmtCounterValueItemDouble item = Marshal.PtrToStructure<PdhFmtCounterValueItemDouble>(current);
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
                    uint status2 = PdhGetFormattedCounterValue(counter, PDH_FMT.PDH_FMT_DOUBLE, nint.Zero, ref counterValue);

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

    #region Registry P/Invoke Declarations

    private static readonly nint HKEY_LOCAL_MACHINE = new nint(0x80000002);
    private const uint KEY_READ = 0x20019;
    private const uint ERROR_SUCCESS = 0;
    private const uint REG_SZ = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegOpenKeyEx(nint hKey, string subKey, uint options, uint samDesired, out nint phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegEnumKeyEx(nint hKey, uint dwIndex, char[] lpName, ref uint lpcchName,
        nint lpReserved, char[]? lpClass, nint lpcchClass, nint lpftLastWriteTime);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegQueryValueEx(nint hKey, string lpValueName, nint lpReserved,
        ref uint lpType, nint lpData, ref uint lpcbData);

    [DllImport("advapi32.dll")]
    private static extern uint RegCloseKey(nint hKey);

    #endregion
}
