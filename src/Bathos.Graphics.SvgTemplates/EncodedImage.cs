using System.Xml;

namespace Bathos.Graphics.SvgTemplates;

/// <summary>Independently owned encoded bytes and their declared media type.</summary>
public sealed class EncodedImage
{
    private readonly byte[] _content;
    private EncodedImage(byte[] content, string mediaType)
    {
        _content = content;
        MediaType = mediaType;
    }
    /// <summary>Gets the lowercase, unescaped declared media type.</summary>
    public string MediaType { get; }
    /// <summary>Gets the captured byte count.</summary>
    public int ByteLength => _content.Length;
    /// <summary>Validates preparation limits and captures content without decoding it.</summary>
    public static EncodedImageResult Create(ReadOnlyMemory<byte> content, string mediaType,
        BindingConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        cancellationToken.ThrowIfCancellationRequested();
        var options = (configuration ?? BindingConfiguration.Default).ImageOptions;
        try
        {
            BindingLimitException.Check(content.Length, options.MaxEncodedImageBytes, nameof(options.MaxEncodedImageBytes));
            BindingLimitException.Check(mediaType.Length, options.MaxMediaTypeCharacters, nameof(options.MaxMediaTypeCharacters));
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || mediaType.Length == 6 ||
                !mediaType.AsSpan(6).ContainsOnlySubtypeCharacters())
                return new(null, [new("svt.image.media-type", DiagnosticSeverity.Error,
                    "Expected image/ followed by an ASCII subtype without parameters or fragment delimiters.", null, null, [], null)]);
            var source = content.Span;
            cancellationToken.ThrowIfCancellationRequested();
            var copy = source.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new(new(copy, mediaType.ToLowerInvariant()), []);
        }
        catch (BindingLimitException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, [error.ToDiagnostic()]);
        }
    }
    internal void WriteDataUrl(XmlWriter writer, CancellationToken token)
    {
        // Data-URL media types are parsed literally, not percent-decoded. The validated
        // subtype excludes delimiters; XmlWriter handles attribute escaping separately.
        writer.WriteString("data:" + MediaType + ";base64,");
        for (var offset = 0; offset < _content.Length;)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(12288, _content.Length - offset);
            writer.WriteBase64(_content, offset, count);
            offset += count;
        }
    }
}

internal static class MediaTypeCharacters
{
    internal static bool ContainsOnlySubtypeCharacters(this ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (!char.IsAsciiLetterOrDigit(character) && !"!$&^_.+-".Contains(character, StringComparison.Ordinal)) return false;
        return true;
    }
}
