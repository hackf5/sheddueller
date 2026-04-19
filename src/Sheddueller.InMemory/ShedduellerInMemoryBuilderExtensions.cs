using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sheddueller;

/// <summary>
/// Registration extensions for the in-memory Sheddueller provider.
/// </summary>
public static class ShedduellerInMemoryBuilderExtensions
{
  /// <summary>
  /// Uses the process-local in-memory task store.
  /// </summary>
  public static ShedduellerBuilder UseInMemoryStore(this ShedduellerBuilder builder)
  {
    ArgumentNullException.ThrowIfNull(builder);

    builder.Services.Replace(ServiceDescriptor.Singleton<ITaskStore, InMemoryTaskStore>());
    return builder;
  }
}
