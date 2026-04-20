namespace Sheddueller.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sheddueller.Serialization;

/// <summary>
/// Fluent configuration surface for Sheddueller registration.
/// </summary>
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
    public ShedduellerBuilder ConfigureOptions(Action<ShedduellerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        this.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Replaces the job payload serializer with a singleton implementation type.
    /// </summary>
    public ShedduellerBuilder UseJobPayloadSerializer<TSerializer>()
      where TSerializer : class, IJobPayloadSerializer
    {
        this.Services.Replace(ServiceDescriptor.Singleton<IJobPayloadSerializer, TSerializer>());
        return this;
    }

    /// <summary>
    /// Replaces the job payload serializer with a singleton instance.
    /// </summary>
    public ShedduellerBuilder UseJobPayloadSerializer(IJobPayloadSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        this.Services.Replace(ServiceDescriptor.Singleton(serializer));
        return this;
    }
}
