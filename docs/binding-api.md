# .NET binding API

This is the agreed contract for substitution, not implemented functionality. All types belong to
`Bathos.Graphics.SvgTemplates`. The lifecycle is inspect, prepare values, bind, then consume the
independent output. Hosts own acquisition, rendering and publication.

## Reusable configuration

```csharp
public sealed class BindingConfiguration
{
    public static BindingConfiguration Default { get; }
    public BindingConfiguration(
        BindingOptions? bindingOptions = null,
        EncodedImageOptions? imageOptions = null);
    public BindingOptions BindingOptions { get; }
    public EncodedImageOptions ImageOptions { get; }
}

public enum UnknownKeyBehavior { Error, IgnoreWithWarning }

public sealed class BindingOptions
{
    public UnknownKeyBehavior UnknownKeyBehavior { get; init; } = UnknownKeyBehavior.Error;
    public int MaxBindings { get; init; } = 10_000;
    public int MaxValueCharacters { get; init; } = 1_048_576;
    public int MaxTotalValueCharacters { get; init; } = 8_388_608;
    public int MaxTotalEncodedImageBytes { get; init; } = 33_554_432;
    public int MaxOutputBytes { get; init; } = 67_108_864;
    public int MaxDiagnostics { get; init; } = 1_000;
}

public sealed class EncodedImageOptions
{
    public int MaxEncodedImageBytes { get; init; } = 16_777_216;
    public int MaxMediaTypeCharacters { get; init; } = 256;
}
```

Construction validates positive limits and defined enum values, then captures independent immutable
snapshots. Null option arguments select defaults. Configuration contains no template, assignments,
diagnostics, cancellation token, resource callbacks or mutable operation state. It is safe for
concurrent reuse and requires no disposal. `Default` is immutable, not a global mutable setting.

Both operations below accept nullable configuration; null selects `BindingConfiguration.Default`.
There is no second per-call options layer. The [substitution contract](../spec/substitution.md)
defines budget counting and failure policy.

## Assignment and binding

```csharp
public sealed class TemplateBinding
{
    public string Key { get; }
    public InputKind Kind { get; }
    public static TemplateBinding Text(string key, string value);
    public static TemplateBinding Paint(string key, string value);
    public static TemplateBinding ImageReference(string key, string reference);
    public static TemplateBinding ImageContent(string key, EncodedImage image);
}

// Instance member of InspectedTemplate:
public BindingResult Bind(
    IReadOnlyList<TemplateBinding> bindings,
    BindingConfiguration? configuration = null,
    CancellationToken cancellationToken = default);

public sealed class BindingResult
{
    public BoundSvg? Document { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class BoundSvg
{
    public string Xml { get; }
    public byte[] ToUtf8Bytes();
}
```

Assignments are immutable. Factories reject null arguments; they defer empty-key, XML-character and
template-dependent validation to Bind. Text and Paint have their corresponding InputKind; both image
factories have Kind Image. There are no implicit string conversions or mutable setters.

Bind checks the assignment count before copying entries into operation-owned storage. Callers must
not mutate the list during capture; it is not retained. Each call starts from the original template.
No binding session or service is required. Templates, captured images and configurations may be
reused concurrently; no call changes another call's inputs or output.

A non-null Document means the complete operation succeeded under the selected policy. Warnings may
accompany output; errors never do. Results and documents have internal constructors. Xml is
immutable; ToUtf8Bytes returns a new caller-owned array without a BOM. Serialization omits the XML
declaration. Neither mutable XML nor borrowed byte buffers are exposed. Output must be explicitly
inspected if a caller wants to use it as a new template baseline.

## Encoded-image preparation

```csharp
public sealed class EncodedImage
{
    public string MediaType { get; }
    public int ByteLength { get; }
    public static EncodedImageResult Create(
        ReadOnlyMemory<byte> content,
        string mediaType,
        BindingConfiguration? configuration = null,
        CancellationToken cancellationToken = default);
}

public sealed class EncodedImageResult
{
    public EncodedImage? Image { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
```

Create validates size and media-type constraints before copying bytes. Image is non-null only on
success; failure returns one diagnostic. Result and image constructors are internal. The image owns
its bytes, exposes no buffer, retains no configuration and needs no disposal. Creation does not
produce base64 or decode pixels. Bind rechecks the image against its own configuration, even when
creation used a more permissive configuration. Creation checks byte limit, media-type length, then
grammar before capture. See [images](../spec/images.md) for formatting.

```csharp
var configuration = new BindingConfiguration(
    new BindingOptions { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning });
var prepared = EncodedImage.Create(imageBytes, "image/png", configuration);
if (prepared.Image is not { } image)
{
    // Present prepared.Diagnostics; do not silently omit a failed image selection.
    return;
}

var result = template.Bind(
[
    TemplateBinding.Text("heading", "Welcome"),
    TemplateBinding.ImageContent("portrait", image),
], configuration);
// Present result.Diagnostics. Consume result.Document only when non-null.
```

## Exceptions and cancellation

Null required arguments throw ArgumentNullException; a null list entry throws ArgumentException.
Nonpositive limits throw ArgumentOutOfRangeException; undefined enum values are argument errors.
Expected input failures use diagnostics, including invalid media types and XML-unrepresentable
strings. Do not convert allocation failures or programming defects into input diagnostics.

Cancellation throws OperationCanceledException and never returns partial output or a malformed-input
diagnostic. Check before work, after external buffer access, and at preparation, assignment and
serialization boundaries. Image preparation and binding are synchronous CPU work over supplied
memory; no async wrapper, stream overload or filesystem/network access is part of this API.
