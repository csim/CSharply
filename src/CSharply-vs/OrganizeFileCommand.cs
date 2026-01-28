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
    //private readonly TraceSource _logger = Requires.NotNull(traceSource, nameof(traceSource));

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

        // Get the current document content
        string fileContents = textView.Document.Text.CopyToString();
        if (string.IsNullOrEmpty(fileContents))
            return;

        // Call the CSharply server to organize
        string? organizedContent = await CSharplyAdapter.Instance.OrganizeFileAsync(fileContents);
        if (organizedContent is null)
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
                    editor.Insert(0, organizedContent);
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
