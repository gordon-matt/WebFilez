using Microsoft.Extensions.DependencyInjection;

namespace WebFilez;

/// <summary>
/// Collects WebFilez registrations so each optional package can add a chain method
/// without the core packages referencing that package.
/// </summary>
public sealed class WebFilezBuilder
{
    /// <summary>Wraps the collection <c>AddWebFilez</c> registered into.</summary>
    public WebFilezBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The collection the chained registrations write to.</summary>
    public IServiceCollection Services { get; }
}
