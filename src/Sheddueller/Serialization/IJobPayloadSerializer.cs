namespace Sheddueller.Serialization;

/// <summary>
/// Serializes and deserializes captured job method arguments.
/// </summary>
public interface IJobPayloadSerializer
{
    /// <summary>
    /// Serializes captured method arguments.
    /// </summary>
    /// <param name="arguments">The evaluated serializable argument values.</param>
    /// <param name="parameterTypes">The corresponding target method parameter types.</param>
    /// <param name="cancellationToken">A token for canceling serialization.</param>
    /// <returns>An opaque payload that can later be deserialized by this serializer.</returns>
    ValueTask<SerializedJobPayload> SerializeAsync(
        IReadOnlyList<object?> arguments,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes captured method arguments.
    /// </summary>
    /// <param name="payload">The opaque serialized payload.</param>
    /// <param name="parameterTypes">The target method parameter types expected by the job handler.</param>
    /// <param name="cancellationToken">A token for canceling deserialization.</param>
    /// <returns>The argument values to pass to the job handler.</returns>
    ValueTask<IReadOnlyList<object?>> DeserializeAsync(
        SerializedJobPayload payload,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);
}
