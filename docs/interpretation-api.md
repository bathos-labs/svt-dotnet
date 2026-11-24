# .NET authored-value interpretation API

These types are implemented in the existing BCL-only `ScalableVectorTemplates` assembly and
namespace. The [portable interpretation contract](../spec/interpretation.md) owns grammar,
classification, precision, budget precedence and recovery.

## Entry points and lifecycle

```csharp
public static class DefaultInterpreter
{
    public static PaintInterpretation InterpretPaint(
        TemplateTarget target,
        InterpretationOptions? options = null,
        CancellationToken cancellationToken = default);

    public static ImageInterpretation InterpretImage(
        TemplateTarget target,
        InterpretationOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Inspect once, select a target, optionally interpret its authored default, and let the host decide
whether to offer a typed control. Bind only an explicit edit. Interpretation is synchronous CPU work
over existing immutable evidence. It requires no owning template, session, service, disposal, async
wrapper, arbitrary-string overload or separate package. Inspect and Bind never call it.

InterpretPaint requires a Fill or Stroke target. InterpretImage requires an Image target. Text
already has directly usable string evidence and has no interpretation entry point. Both operations
always return a non-null result for valid programmer arguments unless canceled or an unexpected
runtime failure occurs.

There is no input/template overload or interpreted aggregate. A caller can traverse an input's
Targets in their existing order. A shared paint with raw values `red` and `#ff0000` still has a raw
Mixed summary even when both targets independently produce the same component values.

## Immutable results

```csharp
public enum PaintInterpretationKind
{
    Absent,
    Literal,
    NoPaint,
    RequiresContext,
    ResourceReference,
    UnsupportedOrUnrecognized
}

public enum ImageInterpretationKind
{
    Absent,
    Empty,
    FragmentReference,
    RelativeReference,
    AbsoluteReference,
    DataUri,
    UnsupportedOrUnrecognized
}

public enum InterpretationLimit
{
    PaintCharacters,
    ReferenceCharacters,
    DataUriHeaderCharacters,
    NumericTokenCharacters,
    NestingDepth
}

public sealed class PaintInterpretation
{
    public TemplateTarget Target { get; }
    public PaintInterpretationKind Kind { get; }
    public SrgbColor? Color { get; }
    public InterpretationLimit? ExceededLimit { get; }
}

public sealed class SrgbColor
{
    public double Red { get; }
    public double Green { get; }
    public double Blue { get; }
    public double Alpha { get; }
}

public sealed class ImageInterpretation
{
    public TemplateTarget Target { get; }
    public ImageInterpretationKind Kind { get; }
    public string? DeclaredMediaType { get; }
    public InterpretationLimit? ExceededLimit { get; }
}
```

All result/color constructors are internal. Public properties are get-only; there are no mutable
collections, borrowed buffers or mutable XML. Target is the exact supplied TemplateTarget instance,
not a copy. Its Default supplies the original raw string and origin, and its Location supplies the
existing hook location. Retaining the result retains that target and its string; interpretation does
not make another full raw-value copy.

Enforce these state invariants:

- Paint Color is non-null if and only if Kind is Literal. All four components are finite doubles in
  `[0,1]`, with encoded sRGB channels, unpremultiplied alpha and positive zero. No byte
  quantization, color management, color serializer or custom equality operation is provided.
- Paint Absent and image Absent correspond exactly to Target.Default.State Absent. An empty present
  paint is unsupported; an empty present image is Empty.
- DeclaredMediaType is non-null only for DataUri with a recognized explicit declaration. It contains
  the lowercased type/subtype only. DataUri with an omitted type has null here; every other image
  kind also has null. Do not invent an image type or expose a partial invalid header.
- ExceededLimit is non-null only for UnsupportedOrUnrecognized caused by that budget. Other
  unsupported results have null ExceededLimit. A limit outcome contains no color or media type.
  Exactly one limit is reported, following portable precedence.

There is no separate diagnostic collection or message string. Kinds and ExceededLimit provide
machine-readable outcomes without introducing warning text or severity into template inspection.
Unsupported means no useful interpretation under this profile; it is not a claim of invalid SVG,
CSS, URI syntax or image data. A successful lexical reference classification likewise does not prove
validity, safety or resolvability.

## Options and boundaries

```csharp
public sealed class InterpretationOptions
{
    public int MaxPaintCharacters { get; init; } = 4096;
    public int MaxReferenceCharacters { get; init; } = 4096;
    public int MaxDataUriHeaderCharacters { get; init; } = 1024;
    public int MaxNumericTokenCharacters { get; init; } = 64;
    public int MaxNestingDepth { get; init; } = 16;
}
```

Null options select these defaults. Options may be reused concurrently. Capture and validate all
five values at entry, including options unused by that target kind; do not retain the caller's
options object or introduce global mutable defaults. All values must be positive Int32 integers.
There is no InterpretationConfiguration or grammar/policy switch: the options group only bounded
work settings, following the existing InspectionOptions convention.

At entry check cancellation, then the required target, then its compatible Hook, then options. Null
target throws ArgumentNullException; an incompatible target throws ArgumentException naming target;
a nonpositive option throws ArgumentOutOfRangeException naming that property. Do not add redundant
defensive checks to trusted internally constructed target/default invariants after the public
argument has been narrowed.

Cancellation throws OperationCanceledException carrying the supplied token. Check before work,
throughout scans and before every normal return, including Absent, Empty, unsupported and limit
outcomes. Do not turn cancellation, allocation failures or implementation defects into unsupported
authored input. Concurrent calls over the same target/options are independent and do not mutate
either argument or another result. There is no operation-owned cache or aggregate failure state.

All limits apply inclusively in the specified UTF-16 units. For data URIs, parse the header only; do
not access payload characters or allocate a payload-sized intermediate string. Retaining its
existing source string is permitted. Numeric conversion and bounded header-token decoding do not
confer resource authority.

## Intended use

Given an inspected paint input, interpret each target explicitly:

```csharp
var limits = new InterpretationOptions();
foreach (var target in paintInput.Targets)
{
    var interpreted = DefaultInterpreter.InterpretPaint(target, limits);
    if (interpreted.Color is { } color)
    {
        // Display color.Red/Green/Blue/Alpha independently of edit intent.
        // Keep target.Default.Raw for the unchanged/no-edit path.
    }
    else
    {
        // Offer an appropriate raw/context/reference/default UI using Kind.
        // ExceededLimit explains a deliberately bounded interpretation.
    }
}
// Binding still requires an explicit TemplateBinding supplied by the host.
```

The [executable host editor example](../examples/editor/README.md) separates authored evidence,
per-target presentation and initial chooser state. It demonstrates an opaque 8-bit RGB control
without implicitly quantizing persisted values, discarding alpha, promoting URL fallbacks or
collapsing raw Mixed evidence. Its host-owned adapter is compiled into behavioral tests and the
isolated package consumer; it is not a library type or binding service.

Opening a chooser, receiving generic initialization notifications, accepting without an edit,
canceling, or returning to the initial chooser value must preserve the existing binding intent. Only
a genuine accepted change creates a replacement; explicit Reset removes it. Hosts must track user
actions independently of generic widget change events or differences between formatted and raw
strings. Retain raw evidence even when a constrained widget requires a lossy display conversion.

Keep public consumer tests focused on state invariants, target identity, wrong-kind/null arguments,
options, cancellation, grammar boundaries and preservation through ordinary binding. Add no
documentation-synchronization tests.
