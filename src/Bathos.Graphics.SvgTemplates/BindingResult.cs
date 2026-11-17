using System.Collections.Immutable;
using System.Text;

namespace Bathos.Graphics.SvgTemplates;

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
    internal BoundSvg(string xml) => Xml = xml;
    /// <summary>Gets serialized XML without an XML declaration.</summary>
    public string Xml { get; }
    /// <summary>Returns newly allocated UTF-8 bytes without a BOM.</summary>
    public byte[] ToUtf8Bytes() => Encoding.UTF8.GetBytes(Xml);
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
