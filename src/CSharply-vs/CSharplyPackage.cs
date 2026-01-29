using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace CSharply;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// Follows initialization pattern similar to CSharpier's CSharpierPackage.
/// </summary>
[VisualStudioContribution]
internal class CSharplyPackage : Extension
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
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        // Services can be registered here for dependency injection
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync(
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken)
    {
        // Initialize services in order, similar to CSharpierPackage.InitializeAsync
        await Logger.InitializeAsync(extensibility, null, cancellationToken);
        Logger.Instance.Info("Starting CSharply");

        await ProcessProvider.InitializeAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Kill running processes on dispose, similar to CSharpierPackage.Dispose
            try
            {
                ProcessProvider.GetInstance().KillRunningProcesses();
            }
            catch
            {
                // Ignore errors during dispose
            }
        }

        base.Dispose(disposing);
    }
}
