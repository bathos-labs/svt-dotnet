# Bathos.Graphics.SvgTemplates

A BCL-only .NET 10 library for inspecting SVG template annotations and atomically replacing text,
paint, and image values. Templates remain ordinary SVG artwork with authored preview content.

This is a prerelease package. Pin its exact version while evaluating it; the API and specification
may change before a stable release. Code and repository-authored examples are MIT-licensed.

## Lifecycle

Supply SVG bytes, inspect the inputs and diagnostics, prepare typed values, then bind from the
immutable template. Reuse a `BindingConfiguration` to apply consistent policies throughout an
application. Each successful bind produces independently owned output; omission preserves authored
content, while an empty text value replaces it.

```csharp
using Bathos.Graphics.SvgTemplates;
using System.Text;

var source = Encoding.UTF8.GetBytes("""
    <svg xmlns="http://www.w3.org/2000/svg" data-svt-version="0">
      <text data-svt-text="heading" data-svt-label="Heading">Welcome</text>
    </svg>
    """);
var configuration = new BindingConfiguration();
var inspection = TemplateInspector.Inspect(source);
if (inspection.Template is null || !inspection.Diagnostics.IsEmpty)
    throw new InvalidOperationException("Review the structured inspection diagnostics.");

var result = inspection.Template.Bind(
    [TemplateBinding.Text("heading", "Hello & welcome")], configuration);
if (result.Document is null)
    throw new InvalidOperationException("Review the structured binding diagnostics.");
byte[] output = result.Document.ToUtf8Bytes();
```

When the host owns destination storage, use `Utf8ByteCount` for its exact required capacity and
`TryWriteUtf8Bytes(destination, out bytesWritten)` to encode directly without an intermediate array.
The example below allocates the destination once; a host may instead supply a slice of its existing
or pooled storage and retain responsibility for that storage's lifetime.

```csharp
var document = result.Document;
Span<byte> destination = new byte[document.Utf8ByteCount];
if (!document.TryWriteUtf8Bytes(destination, out int bytesWritten))
    throw new InvalidOperationException("Destination capacity is insufficient.");
// Consume destination[..bytesWritten] before returning or reusing the host buffer.
```

An insufficient span returns false with zero bytes written and leaves memory unchanged. Success
leaves the unused suffix unchanged. Neither operation retains the destination; the immutable
document remains reusable. Export is synchronous, has no cancellation token, and does not guarantee
atomic publication by a later renderer or transport. These members are new in source and are not
available in the previously published `0.1.0-beta.1` package.

For pooled storage, follow the complete
[downstream lifetime example](https://github.com/bathos-labs/svt-dotnet/blob/master/docs/binding-api.md#pooled-output-and-downstream-lifetime).
The
[allocation and retention guidance](https://github.com/bathos-labs/svt-dotnet/blob/master/docs/binding-api.md#allocation-scope-and-retained-representations)
explains how retaining the XML document, acquiring buffers, and downstream work differ from the
allocation-free export itself.

Use `EncodedImage.Create(bytes, mediaType, configuration)` before an `ImageContent` binding. Image
creation captures the supplied bytes, and binding rechecks its configured limits. `ImageReference`
retains a supplied reference without fetching it. Structured diagnostics describe rejected input;
programming errors throw, and cancellation throws `OperationCanceledException`.

## Boundaries

The library does not decode images, resolve resources or fonts, compute CSS, fit text, or render
SVG. Raw authored defaults are evidence, not effective rendered styles. Hosts control acquisition,
resource policy, rendering and output publication. A successful bind does not prove that an editor
preserved annotations or that a renderer can display the result.

See the [specification](https://github.com/bathos-labs/svt-dotnet/tree/master/spec),
[binding API](https://github.com/bathos-labs/svt-dotnet/blob/master/docs/binding-api.md), and
[authoring examples](https://github.com/bathos-labs/svt-dotnet/tree/master/examples/authoring). The
package contains the library, XML API documentation, README and license notices; the CLI and native
tools are separate.

See the [changelog](https://github.com/bathos-labs/svt-dotnet/blob/master/CHANGELOG.md) for release
notes. Package versions are independent of the template and JSON format markers, which remain 0.
