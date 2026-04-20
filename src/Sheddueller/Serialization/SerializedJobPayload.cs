namespace Sheddueller.Serialization;

/// <summary>
/// Opaque serialized job argument payload.
/// </summary>
public sealed record SerializedJobPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedJobPayload"/> class.
    /// </summary>
    public SerializedJobPayload(string contentType, byte[] data)
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
