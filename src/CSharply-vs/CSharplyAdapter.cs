using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CSharply;

public readonly record struct OrganizeResult(string OrganizedContents, string Outcome);

public sealed class CSharplyAdapter : IDisposable
{
    private const int _defaultPort = 8249;
    private bool _disposed;
    private readonly HttpClient _httpClient = new();
    private bool _isInstalled;
    private const uint _jOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private IntPtr _jobHandle;
    private int _serverPort;
    private Process? _serverProcess;

    public static CSharplyAdapter Instance { get; } = new CSharplyAdapter();

    public void Dispose()
    {
        if (_disposed)
            return;

        StopServer();

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }

        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void EnsureInstalled()
    {
        if (_isInstalled)
            return;

        ExecuteResult checkResult = Execute("csharply", "--version");
        if (checkResult.ExitCode == 0)
        {
            _isInstalled = true;
            return;
        }

        Execute("dotnet", "tool install -g CSharply");
    }

    public async Task<OrganizeResult> OrganizeFileAsync(string fileContents)
    {
        using StringContent content = new(fileContents, Encoding.UTF8, "text/plain");

        HttpResponseMessage response = await _httpClient.PostAsync(
            new Uri($"http://localhost:{_serverPort}/organize"),
            content
        );

        string status = string.Empty;
        if (response.Headers.TryGetValues("x-outcome", out IEnumerable<string>? values))
        {
            status = values.FirstOrDefault() ?? string.Empty;
        }

        if (!response.IsSuccessStatusCode)
        {
            return new OrganizeResult(string.Empty, $"Error: {response.StatusCode}");
            //string error = await response.Content.ReadAsStringAsync();
            //throw new Exception($"CSharply organize failed: {error}");
        }

        string? organizedContents = await response.Content.ReadAsStringAsync();

        return new OrganizeResult(organizedContents, status);
    }

    public void StartServer()
    {
        EnsureInstalled();

        if (_serverProcess is not null && !_serverProcess.HasExited)
            return;

        _serverPort = FindAvailablePort(_defaultPort);

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "csharply",
                Arguments = $"server --port {_serverPort}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        _serverProcess.Start();
        AssignProcessToJobObject(_serverProcess);
    }

    public void StopServer()
    {
        if (_serverProcess is null || _serverProcess.HasExited)
            return;

        try
        {
            _serverProcess.Kill(entireProcessTree: true);
            _serverProcess.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
        }
    }

    private void AssignProcessToJobObject(Process process)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        if (_jobHandle == IntPtr.Zero)
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new()
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = _jOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            if (
                !SetInformationJobObject(
                    _jobHandle,
                    JobObjectInfoType.ExtendedLimitInformation,
                    ref info,
                    length
                )
            )
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!AssignProcessToJobObject(_jobHandle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    private static ExecuteResult Execute(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 30000
    )
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        StringBuilder outputBuilder = new();
        StringBuilder errorBuilder = new();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data is not null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is not null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{fileName}' timed out after {timeoutMs}ms");
        }

        // Ensure async output reading completes
        process.WaitForExit();

        return new ExecuteResult(
            process.ExitCode,
            outputBuilder.ToString(),
            errorBuilder.ToString()
        );
    }

    private static int FindAvailablePort(int startingPort)
    {
        for (int port = startingPort; port < startingPort + 100; port++)
        {
            if (IsPortAvailable(port))
                return port;
        }

        throw new InvalidOperationException(
            $"Could not find an available port starting from {startingPort}"
        );
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        int cbJobObjectInfoLength
    );

    ~CSharplyAdapter()
    {
        Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }
}

public readonly record struct ExecuteResult(int ExitCode, string Output, string Error);
