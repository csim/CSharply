using System.Diagnostics;
using Microsoft.VisualStudio.Extensibility;

namespace CSharply;

public class Logger
{
    private readonly TraceSource? _traceSource;
    private Action<string>? _writeToOutput;

    public static Logger Instance { get; private set; } = null!;

    private Logger(TraceSource? traceSource)
    {
        _traceSource = traceSource;
    }

    public static Task InitializeAsync(
        VisualStudioExtensibility extensibility,
        TraceSource? traceSource,
        CancellationToken cancellationToken)
    {
        Logger instance = new(traceSource);
        Instance = instance;
        return Task.CompletedTask;
    }

    public void SetOutputWriter(Action<string> writeAction)
    {
        _writeToOutput = writeAction;
    }

    public void Debug(string message)
    {
        Log(message, "DEBUG");
        _traceSource?.TraceEvent(TraceEventType.Verbose, 0, message);
    }

    public void Info(string message)
    {
        Log(message, "INFO");
        _traceSource?.TraceEvent(TraceEventType.Information, 0, message);
    }

    public void Warn(string message)
    {
        Log(message, "WARN");
        _traceSource?.TraceEvent(TraceEventType.Warning, 0, message);
    }

    public void Error(string message)
    {
        Log(message, "ERROR");
        _traceSource?.TraceEvent(TraceEventType.Error, 0, message);
    }

    public void Error(Exception ex)
    {
        Error($"{ex.Message}\n{ex.StackTrace}");
    }

    private void Log(string message, string logLevel)
    {
        string formattedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel}] {message}";
        _writeToOutput?.Invoke(formattedMessage);
        System.Diagnostics.Debug.WriteLine(formattedMessage);
    }
}
