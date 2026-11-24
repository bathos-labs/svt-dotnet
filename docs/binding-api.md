# .NET binding API

This is the implemented substitution contract. All types belong to `Bathos.Graphics.SvgTemplates`.
The lifecycle is inspect, prepare values, bind, then consume the independent output. Hosts own
acquisition, rendering and publication. This guide describes the `1.0.0-rc.1` candidate, including
the caller-buffer members in [output export](#output-export). Release acceptance is tracked in
[issue #1](https://github.com/svt-project/svt-dotnet/issues/1).

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

Xml remains immutable serialized XML without an XML declaration. Both byte exports produce exactly
its UTF-8 encoding, without a BOM or terminator. Neither export binds again, normalizes values,
reparses XML or changes serialization.

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
valid destinations. The byte export touches only the supplied slice; bytesWritten is a separate
output. Its storage must not overlap destination memory (an ordinary local variable satisfies this
requirement). Aliasing it into the span would make reporting the count overwrite destination bytes.
Destination capacity does not count toward binding budgets: a larger buffer neither causes rejection
nor changes output. Check exact capacity before any write; the BCL encoding try-method's failure
result alone does not guarantee unchanged memory. False means only insufficient capacity, not an
input diagnostic or a converted programming/runtime failure. There is no general rollback guarantee
for unexpected exceptions or invalid memory access.

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

### Pooled output and downstream lifetime

Keep a rented buffer until every downstream reader has finished, not just until export returns. The
following host helper takes a completed document, a caller-selected `ArrayPool<byte>`, and a
synchronous consumer. It uses `System.Buffers` and `Bathos.Graphics.SvgTemplates`:

```csharp
static void ConsumePooled(BoundSvg document, ArrayPool<byte> pool,
    Action<ReadOnlySpan<byte>> consume)
{
    byte[] rented = pool.Rent(document.Utf8ByteCount);
    try
    {
        if (!document.TryWriteUtf8Bytes(rented, out int bytesWritten))
            throw new InvalidOperationException("The rented buffer is too small.");

        // The consumer must finish reading before it returns, including when it throws.
        consume(rented.AsSpan(0, bytesWritten));
    }
    finally
    {
        pool.Return(rented);
    }
}
```

For example, this synchronous consumer decodes the written prefix into an independently owned string
and parses it before returning:

```csharp
ConsumePooled(document, ArrayPool<byte>.Shared, utf8 =>
{
    var parsed = System.Xml.Linq.XElement.Parse(System.Text.Encoding.UTF8.GetString(utf8));
    Console.WriteLine(parsed.Name.LocalName);
});
```

`Rent` supplies at least the requested capacity; the extra bytes may contain old data. Pass only
`bytesWritten` bytes to a consumer. The separate local `bytesWritten` does not overlap the rented
array. `finally` returns the loan to the same pool after consumption, including when export or
consumption throws. Do not access or return that loan again after this helper finishes. These are
the pool's [rent](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.rent) and
[return](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return) rules.

This helper is only for synchronous consumption that finishes all reads before returning or
throwing. An asynchronous operation or a consumer retaining memory needs a longer loan: keep it
until the actual completion/release boundary, including cancellation and failure, or supply
independently owned storage. Starting work is not completing it. An adapter that starts asynchronous
work must not use this synchronous helper to return the array while that work is still reading.

Destinations must be exclusively writable during export; concurrent exports require disjoint
destinations. Do not overwrite or reuse a buffer while any reader of its previous content remains
active. SVT retains no destination, owns no pool, and does not manage downstream publication,
scheduling, cancellation or disposal.

The [isolated package consumer](../eng/package/consumer/Program.cs) includes this helper and checks
prefix-only delivery, consumption before return, return after a controlled consumer failure, and
subsequent reuse. The decoding/parsing example allocates; it illustrates lifetime, not an
allocation-free pipeline.

### Allocation scope and retained representations

The zero-allocation guarantee applies to normal completed-document export into valid caller-supplied
storage. Consider the costs of each phase separately:

| Phase                                  | What this phase owns                                                                                    |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Preparation, binding and serialization | Captured input, XML processing, encoded images, serialization buffers and the completed document        |
| Destination acquisition                | A new array allocates; a cold or depleted pool may allocate, and a warm pool may retain excess capacity |
| Completed-document export              | `TryWriteUtf8Bytes` encodes into supplied storage; `ToUtf8Bytes` allocates a new array                  |
| Downstream consumption                 | Parsing, rendering, copies, native storage and publication belong to the consumer                       |

Allocated bytes per operation measure new managed allocations, not how much memory remains live.
`BoundSvg` retains the immutable UTF-16 `Xml` string and its byte count, not a UTF-8 cache.
Retaining the document for repeated exports keeps that string alive. A host needing only one
exported result can instead retain independently owned UTF-8 bytes and release its references to the
document (and any other references retaining its XML). That trades repeated encoding for an owned
byte array; collection timing remains a runtime decision. While both representations are retained,
both payloads contribute to live memory. Returning a pooled array may leave its full capacity
retained by the pool.

For illustration, these independently authored strings have the following payload sizes, with no BOM
or terminator. UTF-16 payload is `text.Length * sizeof(char)`; UTF-8 payload is
`Encoding.UTF8.GetByteCount(text)`:

| Literal text | UTF-16 payload bytes | UTF-8 payload bytes |
| ------------ | -------------------: | ------------------: |
| `Hello`      |                   10 |                   5 |
| `Café`       |                    8 |                   5 |
| `日本語`     |                    6 |                   9 |
| `😀`         |                    4 |                   4 |

For ASCII-heavy serialized XML, the character payload is approximately twice its UTF-8 payload;
Unicode content changes that ratio. This table measures only the illustrated text, not a complete
SVG. Actual export size includes markup and XML escaping and is given by `Utf8ByteCount`. The
arithmetic excludes object/array headers, alignment, unused pool capacity, other live objects,
temporary allocations, native memory and runtime overhead. It is not a measurement of total retained
memory or peak process working set.

Use the [export measurement guide](../eng/ExportPerformance/README.md) for the reproducible isolated
comparison. It excludes binding, buffer acquisition and downstream work; it does not measure pool
rentals or prove allocation-free acquisition from a warmed pool. Measure those phases separately
when evaluating an application, and label their boundaries. No UTF-8 cache, pool dependency,
ownership transfer or disposal API is added to hide the host's representation choice.

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
