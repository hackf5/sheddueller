#pragma warning disable IDE0130

namespace Sheddueller;

/// <summary>
/// Opaque serialized task argument payload.
/// </summary>
public sealed record SerializedTaskPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedTaskPayload"/> class.
    /// </summary>
    public SerializedTaskPayload(string contentType, byte[] data)
    {
        this.ContentType = contentType;
        this.Data = data;
    }

    /// <summary>
    /// Gets the serializer-owned content type.
    /// </summary>
    public string ContentType { get; init; }

#pragma warning disable CA1819
    /// <summary>
    /// Gets the serializer-owned payload bytes.
    /// </summary>
    public byte[] Data { get; init; }
}
