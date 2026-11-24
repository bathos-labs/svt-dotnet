using System.Collections.Immutable;
using System.Text;

namespace ScalableVectorTemplates;

/// <summary>An atomic binding outcome with diagnostics from this operation only.</summary>
public sealed class BindingResult
{
    internal BindingResult(BoundSvg? document, ImmutableArray<Diagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }
    /// <summary>Gets the complete document, or null on failure.</summary>
    public BoundSvg? Document { get; }
    /// <summary>Gets deterministic binding diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

/// <summary>An independent immutable SVG document.</summary>
public sealed class BoundSvg
{
    internal BoundSvg(string xml, int utf8ByteCount)
    {
        Xml = xml;
        Utf8ByteCount = utf8ByteCount;
    }
    /// <summary>Gets serialized XML without an XML declaration.</summary>
    public string Xml { get; }
    /// <summary>Gets the exact UTF-8 output size, recorded during binding, without allocating.</summary>
    public int Utf8ByteCount { get; }
    /// <summary>Returns newly allocated UTF-8 bytes without a BOM.</summary>
    public byte[] ToUtf8Bytes() => Encoding.UTF8.GetBytes(Xml);

    /// <summary>Encodes directly into caller-owned storage without allocating or retaining it.</summary>
    /// <param name="destination">Memory exclusively writable by this call until it returns.</param>
    /// <param name="bytesWritten">
    /// The exact output size on success; zero on insufficient capacity. Its storage must not overlap destination.
    /// </param>
    /// <returns>False only when capacity is insufficient, leaving the entire destination unchanged.</returns>
    /// <remarks>
    /// Success writes the same bytes as ToUtf8Bytes, without a BOM or terminator, and leaves the
    /// unused suffix unchanged. Use Utf8ByteCount for required capacity. Concurrent exports require
    /// nonoverlapping destinations. Export offers no cancellation or downstream publication guarantee.
    /// </remarks>
    public bool TryWriteUtf8Bytes(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        // Encoding's try-method can partially write before reporting insufficient capacity.
        if (destination.Length < Utf8ByteCount) return false;
        bytesWritten = Encoding.UTF8.GetBytes(Xml.AsSpan(), destination);
        return true;
    }
}

/// <summary>An encoded-image preparation outcome.</summary>
public sealed class EncodedImageResult
{
    internal EncodedImageResult(EncodedImage? image, ImmutableArray<Diagnostic> diagnostics)
    {
        Image = image;
        Diagnostics = diagnostics;
    }
    /// <summary>Gets independently captured content, or null on failure.</summary>
    public EncodedImage? Image { get; }
    /// <summary>Gets preparation diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
