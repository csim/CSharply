#pragma warning disable VSEXTPREVIEW_OUTPUTWINDOW
using System.Runtime.Versioning;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace CSharply;

/// <summary>
/// Command handler for organizing C# files.
/// Similar structure to CSharpier's ReformatWithCSharpier.
/// </summary>
[VisualStudioContribution]
public sealed class OrganizeFileCommand : Command
{
    private OutputChannel? _outputChannel;
    private OrganizeService _organizeService = null!;

    public static OrganizeFileCommand Instance { get; private set; } = null!;

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration =>
        new("%CSharply.OrganizeFile.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
            Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
            Shortcuts = [new CommandShortcutConfiguration(ModifierKey.Control, Key.E, ModifierKey.Control, Key.F)]
        };

    /// <inheritdoc />
    [SupportedOSPlatform("windows8.0")]
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _outputChannel = await Extensibility
            .Views()
            .Output.CreateOutputChannelAsync("CSharply", cancellationToken);

        Logger.Instance.SetOutputWriter(message => _outputChannel?.Writer.WriteLine(message));

        _organizeService = OrganizeService.GetInstance();

        Instance = this;

        await base.InitializeAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows8.0")]
    public override async Task ExecuteCommandAsync(
        IClientContext context,
        CancellationToken cancellationToken)
    {
        ITextViewSnapshot? textView = await context.GetActiveTextViewAsync(cancellationToken);
        if (textView is null)
            return;

        FormatResult result = await _organizeService.FormatAsync(textView);
        if (!result.HasChanges)
            return;

        string organizedFileContents = result.FormattedContent!;
        int caretLine = result.CaretLine;

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
                cancellationToken);
    }
}
#pragma warning restore VSEXTPREVIEW_OUTPUTWINDOW
