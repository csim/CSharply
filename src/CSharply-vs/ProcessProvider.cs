using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CSharply;

public readonly record struct OrganizeResult(string OrganizedContents, string Outcome);

public sealed class ProcessProvider : IDisposable
{
    private const int DefaultPort = 8249;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private static ProcessProvider? _instance;
    private readonly HttpClient _httpClient = new();
    private readonly Logger _logger;

    private bool _disposed;
    private bool _isInstalled;
    private IntPtr _jobHandle;
    private int _serverPort;
    private Process? _serverProcess;

    private ProcessProvider(Logger logger)
    {
        _logger = logger;
    }

    public static ProcessProvider GetInstance()
    {
        return _instance ??= new ProcessProvider(Logger.Instance);
    }

    public static async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _instance = new ProcessProvider(Logger.Instance);
        Logger.Instance.Info("CSharply process provider initialized");

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        KillRunningProcesses();

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }

        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void KillRunningProcesses()
    {
        StopServer();
    }

    public bool HasWarmedProcessFor(string filePath)
    {
        // Check if the server process is running
        return _serverProcess is not null && !_serverProcess.HasExited;
    }

    public async Task<OrganizeResult> OrganizeFileAsync(string fileContents)
    {
        await EnsureStartedAsync();

        using StringContent content = new(fileContents, Encoding.UTF8, "text/plain");

        try
        {
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
                _logger.Warn($"CSharply server returned error: {response.StatusCode}");
                return new OrganizeResult(string.Empty, $"Error: {response.StatusCode}");
            }

            string? organizedContents = await response.Content.ReadAsStringAsync();

            return new OrganizeResult(organizedContents, status);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            return new OrganizeResult(string.Empty, $"Error: {ex.Message}");
        }
    }

    private async Task EnsureInstalledAsync()
    {
        if (_isInstalled)
            return;

        _logger.Info("Checking if CSharply is installed...");
        ExecuteResult checkResult = Execute("csharply", "--version");
        if (checkResult.ExitCode == 0)
        {
            _logger.Info($"CSharply version: {checkResult.Output.Trim()}");
            _isInstalled = true;
            return;
        }

        _logger.Info("CSharply not found, installing globally...");
        ExecuteResult installResult = Execute("dotnet", "tool install -g CSharply");
        if (installResult.ExitCode != 0)
        {
            _logger.Error($"Failed to install CSharply: {installResult.Error}");
            throw new InvalidOperationException(
                $"Failed to install CSharply: {installResult.Error}"
            );
        }

        _logger.Info("CSharply installed successfully");
        _isInstalled = true;

        await Task.CompletedTask;
    }

    private async Task EnsureStartedAsync()
    {
        await EnsureInstalledAsync();

        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            _logger.Debug("CSharply server already running");
            return;
        }

        _serverPort = FindAvailablePort(DefaultPort);
        _logger.Info($"Starting CSharply server on port {_serverPort}...");

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

        _logger.Info("CSharply server started");

        await Task.CompletedTask;
    }

    private void StopServer()
    {
        if (_serverProcess is null || _serverProcess.HasExited)
            return;

        _logger.Debug("Stopping CSharply server...");

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

        _logger.Debug("CSharply server stopped");
    }

    #region Process Helpers

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
                    LimitFlags = JobObjectLimitKillOnJobClose,
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
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        if (!AssignProcessToJobObject(_jobHandle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

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

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
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

    #endregion

    #region Native Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        int cbJobObjectInfoLength
    );

    #endregion

    #region Native Types

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

    #endregion

    ~ProcessProvider()
    {
        Dispose();
    }
}

public readonly record struct ExecuteResult(int ExitCode, string Output, string Error);
