using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CSharply;

public sealed class CSharplyAdapter : IDisposable
{
    private const int _defaultPort = 8249;
    private bool _disposed;
    private readonly HttpClient _httpClient = new();
    private bool _isInstalled;
    private int _serverPort;
    private Process? _serverProcess;

    public static CSharplyAdapter Instance { get; } = new CSharplyAdapter();

    public void Dispose()
    {
        if (_disposed)
            return;

        StopServer();
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

    public async Task<string?> OrganizeFileAsync(string fileContents)
    {
        using StringContent content = new(fileContents, Encoding.UTF8, "text/plain");

        HttpResponseMessage response = await _httpClient.PostAsync(
            new Uri($"http://localhost:{_serverPort}/organize"),
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            return null;
            //string error = await response.Content.ReadAsStringAsync();
            //throw new Exception($"CSharply organize failed: {error}");
        }

        return await response.Content.ReadAsStringAsync();
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
}

public readonly record struct ExecuteResult(int ExitCode, string Output, string Error);
