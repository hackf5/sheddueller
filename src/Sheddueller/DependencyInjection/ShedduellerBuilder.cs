#pragma warning disable IDE0130

namespace Sheddueller;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// Replaces the task payload serializer with a singleton implementation type.
    /// </summary>
    public ShedduellerBuilder UseTaskPayloadSerializer<TSerializer>()
      where TSerializer : class, ITaskPayloadSerializer
    {
        this.Services.Replace(ServiceDescriptor.Singleton<ITaskPayloadSerializer, TSerializer>());
        return this;
    }

    /// <summary>
    /// Replaces the task payload serializer with a singleton instance.
    /// </summary>
    public ShedduellerBuilder UseTaskPayloadSerializer(ITaskPayloadSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        this.Services.Replace(ServiceDescriptor.Singleton(serializer));
        return this;
    }
}
