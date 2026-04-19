using System.Text.Json;

namespace Sheddueller;

/// <summary>
/// Default task payload serializer based on System.Text.Json.
/// </summary>
public sealed class SystemTextJsonTaskPayloadSerializer : ITaskPayloadSerializer
{
  /// <summary>
  /// Content type used for the default JSON argument payload format.
  /// </summary>
  public const string JsonContentType = "application/vnd.sheddueller.arguments+json;v=1";

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  /// <inheritdoc />
  public ValueTask<SerializedTaskPayload> SerializeAsync(
    IReadOnlyList<object?> arguments,
    IReadOnlyList<Type> parameterTypes,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(arguments);
    ArgumentNullException.ThrowIfNull(parameterTypes);
    cancellationToken.ThrowIfCancellationRequested();
    EnsureEqualCounts(arguments.Count, parameterTypes.Count);

    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
      writer.WriteStartArray();

      for (var i = 0; i < arguments.Count; i++)
      {
        JsonSerializer.Serialize(writer, arguments[i], parameterTypes[i], SerializerOptions);
      }

      writer.WriteEndArray();
    }

    return ValueTask.FromResult(new SerializedTaskPayload(JsonContentType, stream.ToArray()));
  }

  /// <inheritdoc />
  public ValueTask<IReadOnlyList<object?>> DeserializeAsync(
    SerializedTaskPayload payload,
    IReadOnlyList<Type> parameterTypes,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(payload);
    ArgumentNullException.ThrowIfNull(parameterTypes);
    cancellationToken.ThrowIfCancellationRequested();

    if (!string.Equals(payload.ContentType, JsonContentType, StringComparison.Ordinal))
    {
      throw new InvalidOperationException($"Unsupported task payload content type '{payload.ContentType}'.");
    }

    using var document = JsonDocument.Parse(payload.Data);
    var root = document.RootElement;

    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != parameterTypes.Count)
    {
      throw new InvalidOperationException("Task payload does not match the expected argument count.");
    }

    var arguments = new object?[parameterTypes.Count];
    var index = 0;

    foreach (var element in root.EnumerateArray())
    {
      arguments[index] = element.Deserialize(parameterTypes[index], SerializerOptions);
      index++;
    }

    return ValueTask.FromResult<IReadOnlyList<object?>>(arguments);
  }

  private static void EnsureEqualCounts(int argumentCount, int parameterTypeCount)
  {
    if (argumentCount != parameterTypeCount)
    {
      throw new ArgumentException("The number of arguments must match the number of parameter types.");
    }
  }
}
