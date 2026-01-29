using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace CSharply;

/// <summary>
/// Command1 handler.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OrganizeFileCommand"/> class.
/// </remarks>
/// <param name="traceSource">Trace source instance to utilize.</param>
[VisualStudioContribution]
public class OrganizeFileCommand(TraceSource traceSource) : Command
{
    private readonly TraceSource _logger = Requires.NotNull(traceSource, nameof(traceSource));

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration =>
        new("%CSharply.OrganizeFile.DisplayName%")
        {
            // Use this object initializer to set optional parameters for the command. The required parameter,
            // displayName, is set above. DisplayName is localized and references an entry in .vsextension\string-resources.json.
            Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
            Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
        };

    [SupportedOSPlatform("windows8.0")]
    public override async Task ExecuteCommandAsync(
        IClientContext context,
        CancellationToken cancellationToken
    )
    {
        // Get the active text view
        ITextViewSnapshot? textView = await context.GetActiveTextViewAsync(cancellationToken);
        if (textView is null)
            return;

        // Save the current line number
        int caretOffset = textView.Selection.InsertionPosition.Offset;
        string filePath = textView.Document.Uri.LocalPath;

        // Get the current document content
        string fileContents = textView.Document.Text.CopyToString();
        if (string.IsNullOrEmpty(fileContents))
            return;

        // Calculate line number from offset
        int caretLine = fileContents[..Math.Min(caretOffset, fileContents.Length)]
            .Count(c => c == '\n');

        // Call the CSharply server to organize
        OrganizeResult result = await CSharplyAdapter.Instance.OrganizeFileAsync(fileContents);
        string? organizedFileContents = result.OrganizedContents;

        // Write status to the VisualStudio.Extensibility Output window pane
        _logger.TraceInformation($"{result.Status,30}: {filePath}");

        // Write status as a notification (always visible)
        await Extensibility
            .Shell()
            .ShowPromptAsync($"{result.Status}: {filePath}", PromptOptions.OK, cancellationToken);

        if (string.IsNullOrEmpty(organizedFileContents) || fileContents == organizedFileContents)
            return;

        // Replace the entire document content
        await Extensibility
            .Editor()
            .EditAsync(
                batch =>
                {
                    ITextDocumentSnapshot document = textView.Document;
                    ITextDocumentEditor editor = document.AsEditable(batch);
                    if (document.Length > 0)
                        editor.Delete(document.Text);
                    editor.Insert(0, organizedFileContents);

                    // Restore cursor to start of the same line (clamped to document line count)
                    int totalLines = organizedFileContents.Count(c => c == '\n') + 1;
                    int targetLine = Math.Min(caretLine, totalLines - 1);
                    int lineStartOffset = 0;
                    for (int i = 0; i < targetLine; i++)
                    {
                        int nextNewline = organizedFileContents.IndexOf('\n', lineStartOffset);
                        if (nextNewline < 0)
                            break;
                        lineStartOffset = nextNewline + 1;
                    }
                    TextPosition position = new(document, lineStartOffset);
                    textView
                        .AsEditable(batch)
                        .SetSelections([new Selection(new TextRange(position, position))]);
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows8.0")]
    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Use InitializeAsync for any one-time setup or initialization.
        return base.InitializeAsync(cancellationToken);
    }
}
