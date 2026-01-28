using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace CSharply;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration =>
        new()
        {
            Metadata = new(
                id: "CSharply.dce3961f-e68e-4f14-9097-1a6b2e4e1ba5",
                version: ExtensionAssemblyVersion,
                publisherName: "Clint Simon",
                displayName: "CSharply for Visual Studio",
                description: "Organize C# files using the CSharply dotnet tool"
            ),
        };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        CSharplyAdapter.Instance.Dispose();

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync(
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken
    )
    {
        await base.OnInitializedAsync(extensibility, cancellationToken);

        await Task.Run(CSharplyAdapter.Instance.StartServer, cancellationToken);
    }
}
