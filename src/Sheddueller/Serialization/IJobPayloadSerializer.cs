namespace Sheddueller.Serialization;

/// <summary>
/// Serializes and deserializes captured job method arguments.
/// </summary>
public interface IJobPayloadSerializer
{
    /// <summary>
    /// Serializes captured method arguments.
    /// </summary>
    ValueTask<SerializedJobPayload> SerializeAsync(
        IReadOnlyList<object?> arguments,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes captured method arguments.
    /// </summary>
    ValueTask<IReadOnlyList<object?>> DeserializeAsync(
        SerializedJobPayload payload,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);
}
