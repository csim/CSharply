using System.Diagnostics;
using Microsoft.VisualStudio.Extensibility.Editor;

namespace CSharply;

/// <summary>
/// Service for formatting C# files using CSharply.
/// Similar to CSharpier's FormattingService pattern.
/// </summary>
public class OrganizeService
{
    private static OrganizeService? _instance;
    private readonly Logger _logger;
    private readonly ProcessProvider _processProvider;

    private OrganizeService()
    {
        _logger = Logger.Instance;
        _processProvider = ProcessProvider.GetInstance();
    }

    public async Task<FormatResult> FormatAsync(ITextViewSnapshot textView)
    {
        int caretOffset = textView.Selection.InsertionPosition.Offset;
        string filePath = textView.Document.Uri.LocalPath;

        string fileContents = textView.Document.Text.CopyToString();
        if (string.IsNullOrEmpty(fileContents))
            return FormatResult.NoChange;

        int caretLine = fileContents[..Math.Min(caretOffset, fileContents.Length)]
            .Count(c => c == '\n');

        Stopwatch stopwatch = Stopwatch.StartNew();
        OrganizeResult result = await _processProvider.OrganizeFileAsync(fileContents);
        stopwatch.Stop();

        _logger.Info($"{filePath}");
        _logger.Info($"  outcome: {result.Outcome,-24} {stopwatch.ElapsedMilliseconds,5:N0}ms");

        string? organizedFileContents = result.OrganizedContents;

        string? action =
            string.IsNullOrEmpty(organizedFileContents) ? "empty content"
            : fileContents == organizedFileContents ? "no change"
            : null;

        if (action != null)
        {
            _logger.Info($"   action: {action}");
        }

        if (string.IsNullOrEmpty(organizedFileContents) || fileContents == organizedFileContents)
            return FormatResult.NoChange;

        return new FormatResult(organizedFileContents, caretLine);
    }

    public static OrganizeService GetInstance()
    {
        return _instance ??= new OrganizeService();
    }

    public static bool IsSupportedLanguage(string language)
    {
        return language is "CSharp" or "C#";
    }

    public bool ProcessSupportsFormatting(string filePath)
    {
        return _processProvider.HasWarmedProcessFor(filePath);
    }
}

public readonly record struct FormatResult(string? FormattedContent, int CaretLine)
{
    public static FormatResult NoChange => new(null, 0);
    public bool HasChanges => FormattedContent is not null;
}
