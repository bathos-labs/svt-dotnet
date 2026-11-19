# .NET binding API

This is the agreed substitution contract. All types belong to `Bathos.Graphics.SvgTemplates`. The
lifecycle is inspect, prepare values, bind, then consume the independent output. Hosts own
acquisition, rendering and publication. Inspection and binding are implemented; the caller-buffer
members in [output export](#output-export) are agreed but not yet implemented, tracked in
[issue #16](https://github.com/bathos-labs/svt-dotnet/issues/16).

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
```

Assignments are immutable. Factories reject null arguments; they defer empty-key, XML-character and
template-dependent validation to Bind. Text and Paint have their corresponding InputKind; both image
factories have Kind Image. There are no implicit string conversions or mutable setters.

Bind checks the assignment count before copying entries into operation-owned storage. Callers must
not mutate the list during capture; it is not retained. Each call starts from the original template.
No binding session or service is required. Templates, captured images and configurations may be
reused concurrently; no call changes another call's inputs or output.

A non-null Document means the complete operation succeeded under the selected policy. Warnings may
accompany output; errors never do. Results and documents have internal constructors. Output must be
explicitly inspected if a caller wants to use it as a new template baseline.

## Output export

```csharp
public sealed class BoundSvg
{
    public string Xml { get; }
    public int Utf8ByteCount { get; }
    public byte[] ToUtf8Bytes();
    public bool TryWriteUtf8Bytes(Span<byte> destination, out int bytesWritten);
}
```

`Utf8ByteCount` and `TryWriteUtf8Bytes` are the agreed extension; `Xml` and `ToUtf8Bytes` already
exist. Xml remains immutable serialized XML without an XML declaration. Both byte exports produce
exactly its UTF-8 encoding, without a BOM or terminator. Neither export binds again, normalizes
values, reparses XML or changes serialization.

`Utf8ByteCount` is the exact number of bytes each successful export writes, equal to
`ToUtf8Bytes().Length`, not the UTF-16 character count or a maximum estimate. Capture it from
completed bounded serialization during Bind; reading it is constant-time and allocates nothing. The
count is positive for a valid SVG document, fits Int32, and cannot exceed the MaxOutputBytes under
which that document was admitted. It includes escaping, Unicode, base64 and duplicated image
references exactly as serialized.

`TryWriteUtf8Bytes` has two normal outcomes:

| Destination capacity    | Result  | bytesWritten  | Destination effect                                                   |
| ----------------------- | ------- | ------------- | -------------------------------------------------------------------- |
| Less than Utf8ByteCount | `false` | `0`           | Entire destination unchanged; no partial encoding                    |
| At least Utf8ByteCount  | `true`  | Utf8ByteCount | Exactly the output prefix is written; remaining bytes stay unchanged |

A default or empty span follows the insufficient-capacity rule. On failure, callers use
`Utf8ByteCount` for required capacity; bytesWritten does not report the required size. Slices are
valid destinations, and bytes outside the supplied slice are untouched. Destination capacity does
not count toward binding budgets: a larger buffer neither causes rejection nor changes output. Check
exact capacity before any write; the BCL encoding try-method's failure result alone does not
guarantee unchanged memory. False means only insufficient capacity, not an input diagnostic or a
converted programming/runtime failure. There is no general rollback guarantee for unexpected
exceptions or invalid memory access.

Writing encodes directly into the supplied span without a temporary output-sized array or managed
allocations by SVT during normal calls. Keep the immutable XML and recorded count; do not add a
retained UTF-8 cache, pooled storage, owner transfer or disposal lifecycle. `ToUtf8Bytes` remains
the allocating convenience: every call returns a new independent caller-owned array.

BoundSvg is reusable concurrently, including mixed reads and exports, provided concurrent writes use
nonoverlapping destinations. The caller keeps destination memory valid and exclusively writable by
this call until it returns. The document never retains a destination or exposes writable internal
storage. Changing exported bytes cannot change Xml, the count, subsequent exports or other results.

Export is synchronous and has no cancellation token, async form, configuration argument or
diagnostic result. Callers may check their own token before export, but export does not offer
mid-write cancellation. Publication to a renderer, stream or other destination remains a subsequent
host operation with its own failure semantics. IBufferWriter, Stream and native-buffer/ownership
overloads are outside this contract.

Only successful Bind results expose a document. MaxOutputBytes admission still occurs during binding
before a document is returned; export cannot recover a document from a failed bind or relax its
budgets. No option defaults, diagnostic codes, SVG annotations or format markers change.

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

Inspection, image preparation and binding cancellation throw OperationCanceledException and never
return partial output or a malformed-input diagnostic. Check before work, after external buffer
access, and at preparation, assignment and binding-serialization boundaries. Image preparation and
binding are synchronous CPU work over supplied memory; no async wrapper, stream overload or
filesystem/network access is part of this API. Completed-document export follows the separate
[output export](#output-export) contract and accepts no cancellation token.
