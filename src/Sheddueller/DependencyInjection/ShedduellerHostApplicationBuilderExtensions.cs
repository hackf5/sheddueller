#pragma warning disable IDE0130

namespace Microsoft.Extensions.Hosting;

using Microsoft.Extensions.DependencyInjection;

using Sheddueller;

/// <summary>
/// Host application builder extensions for Sheddueller.
/// </summary>
public static class ShedduellerHostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Sheddueller services to a host application builder.
    /// </summary>
    public static HostApplicationBuilder AddSheddueller(
        this HostApplicationBuilder builder,
        Action<ShedduellerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSheddueller(configure);
        return builder;
    }
}
