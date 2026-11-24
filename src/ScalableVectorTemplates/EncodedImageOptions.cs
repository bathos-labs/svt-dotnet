namespace ScalableVectorTemplates;

/// <summary>Immutable initialization options for binding preparation.</summary>
public sealed class EncodedImageOptions
{
    /// <summary>Gets the positive MaxEncodedImageBytes budget.</summary>
    public int MaxEncodedImageBytes { get; init; } = 16_777_216;
    /// <summary>Gets the positive MaxMediaTypeCharacters budget.</summary>
    public int MaxMediaTypeCharacters { get; init; } = 256;
    internal EncodedImageOptions Snapshot()
    {
        var copy = new EncodedImageOptions
        {
            MaxEncodedImageBytes = MaxEncodedImageBytes,
            MaxMediaTypeCharacters = MaxMediaTypeCharacters,
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxEncodedImageBytes, nameof(MaxEncodedImageBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxMediaTypeCharacters, nameof(MaxMediaTypeCharacters));
        return copy;
    }
}
