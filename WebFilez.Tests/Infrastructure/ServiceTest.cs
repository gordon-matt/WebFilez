using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Transfers;

namespace WebFilez.Tests.Infrastructure;

/// <summary>
/// Runs the real services against two temporary roots: <c>files</c>, which is writable, and
/// <c>docs</c>, which is read-only. Each test gets fresh folders.
/// </summary>
public abstract class ServiceTest : IAsyncLifetime
{
    private ServiceProvider provider = null!;
    private AsyncServiceScope scope;

    protected TempFolder Files { get; } = new();

    protected TempFolder Docs { get; } = new();

    protected IFileManagerService Service => scope.ServiceProvider.GetRequiredService<IFileManagerService>();

    protected IFileTransferService Transfers => scope.ServiceProvider.GetRequiredService<IFileTransferService>();

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected virtual void Configure(WebFilezOptions options)
    {
    }

    public virtual ValueTask InitializeAsync()
    {
        provider = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices(configure: options =>
            {
                options.Roots.Add(new FileRoot { Id = "files", Name = "Files", Path = Files.Path });
                options.Roots.Add(new FileRoot { Id = "docs", Name = "Docs", Path = Docs.Path, IsReadOnly = true });
                options.Thumbnails.CacheDirectory = System.IO.Path.Combine(Files.Path, ".thumbs");
                Configure(options);
            })
            .BuildServiceProvider(validateScopes: true);

        scope = provider.CreateAsyncScope();
        return ValueTask.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        Files.Dispose();
        Docs.Dispose();
        GC.SuppressFinalize(this);
    }
}