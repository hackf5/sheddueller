#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Serializes and deserializes captured task method arguments.
/// </summary>
public interface ITaskPayloadSerializer
{
    /// <summary>
    /// Serializes captured method arguments.
    /// </summary>
    ValueTask<SerializedTaskPayload> SerializeAsync(
        IReadOnlyList<object?> arguments,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes captured method arguments.
    /// </summary>
    ValueTask<IReadOnlyList<object?>> DeserializeAsync(
        SerializedTaskPayload payload,
        IReadOnlyList<Type> parameterTypes,
        CancellationToken cancellationToken = default);
}
