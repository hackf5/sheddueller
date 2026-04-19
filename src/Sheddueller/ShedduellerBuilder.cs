using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sheddueller;

/// <summary>
/// Fluent configuration surface for Sheddueller registration.
/// </summary>
public sealed class ShedduellerBuilder
{
    internal ShedduellerBuilder(IServiceCollection services)
    {
        Services = services;
    }

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

        Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Replaces the task payload serializer with a singleton implementation type.
    /// </summary>
    public ShedduellerBuilder UseTaskPayloadSerializer<TSerializer>()
      where TSerializer : class, ITaskPayloadSerializer
    {
        Services.Replace(ServiceDescriptor.Singleton<ITaskPayloadSerializer, TSerializer>());
        return this;
    }

    /// <summary>
    /// Replaces the task payload serializer with a singleton instance.
    /// </summary>
    public ShedduellerBuilder UseTaskPayloadSerializer(ITaskPayloadSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        Services.Replace(ServiceDescriptor.Singleton(serializer));
        return this;
    }
}
