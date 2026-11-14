using Microsoft.Extensions.DependencyInjection;

namespace WebFilez;

/// <summary>
/// Marks a builder whose chain accepts WebFilez package methods such as <c>AddAzureBlob</c>.
/// Package methods constrain on this interface, so they stay off another product's builder.
/// </summary>
public interface IWebFilezBuilder
{
    /// <summary>The collection the chained registrations write to.</summary>
    IServiceCollection Services { get; }
}

/// <summary>
/// Collects WebFilez registrations so each optional package can add a chain method
/// without the core packages referencing that package.
/// </summary>
public sealed class WebFilezBuilder : IWebFilezBuilder
{
    /// <summary>Wraps the collection <c>AddWebFilez</c> registered into.</summary>
    public WebFilezBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }
}