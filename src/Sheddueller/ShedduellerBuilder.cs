namespace Sheddueller;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheddueller.Serialization;

/// <summary>
/// Fluent configuration surface for Sheddueller registration.
/// </summary>
/// <remarks>
/// Provider packages extend this type with storage-specific methods such as <c>UsePostgres</c>.
/// </remarks>
public sealed class ShedduellerBuilder
{
    internal ShedduellerBuilder(IServiceCollection services)
      => this.Services = services;

    /// <summary>
    /// Gets the underlying service collection for provider extensions.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Configures Sheddueller runtime options.
    /// </summary>
    /// <param name="configure">The options callback to apply through the host options system.</param>
    /// <returns>The same builder for chained configuration.</returns>
    public ShedduellerBuilder ConfigureOptions(Action<ShedduellerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        this.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Configures Sheddueller runtime options with access to the final service provider.
    /// </summary>
    /// <param name="configure">The options callback to apply through the host options system.</param>
    /// <returns>The same builder for chained configuration.</returns>
    public ShedduellerBuilder ConfigureOptions(Action<IServiceProvider, ShedduellerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        this.Services.AddOptions<ShedduellerOptions>()
          .Configure<IServiceProvider>((options, serviceProvider) => configure(serviceProvider, options));
        return this;
    }

    /// <summary>
    /// Replaces the job payload serializer with a singleton implementation type.
    /// </summary>
    /// <typeparam name="TSerializer">The serializer implementation type.</typeparam>
    /// <returns>The same builder for chained configuration.</returns>
    public ShedduellerBuilder UseJobPayloadSerializer<TSerializer>()
      where TSerializer : class, IJobPayloadSerializer
    {
        this.Services.Replace(ServiceDescriptor.Singleton<IJobPayloadSerializer, TSerializer>());
        return this;
    }

    /// <summary>
    /// Replaces the job payload serializer with a singleton instance.
    /// </summary>
    /// <param name="serializer">The serializer instance to use for job arguments.</param>
    /// <returns>The same builder for chained configuration.</returns>
    public ShedduellerBuilder UseJobPayloadSerializer(IJobPayloadSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        this.Services.Replace(ServiceDescriptor.Singleton(serializer));
        return this;
    }
}
