# .NET inspection API

This is the implemented inspection contract for the unreleased .NET 10 BCL-only assembly named
`Bathos.Graphics.SvgTemplates`. The root namespace matches the assembly name. Keep parsing and
mutable XML implementation types internal.

The public entry point is synchronous CPU work over already supplied memory:

```csharp
public static InspectionResult Inspect(
    ReadOnlyMemory<byte> source,
    InspectionOptions? options = null,
    CancellationToken cancellationToken = default);
```

Declare it on `TemplateInspector`. Null options select the documented defaults. No stream overload,
async wrapper, disposable owner, resource callback, binder stub or native dependency is required.
Invalid positive-limit options throw ArgumentOutOfRangeException; malformed supplied XML returns a
diagnostic result. Cancellation throws OperationCanceledException. Do not convert allocation
failures or programming defects into authoring diagnostics.

## Immutable result model

- `InspectionResult` contains nullable `Template` and immutable `Diagnostics`. Null Template means
  root/limit rejection. A non-null template may coexist with scoped error diagnostics.
- `InspectedTemplate` exposes ordered immutable `Inputs`. It owns its private source representation;
  caller memory may be reused after Inspect returns. It exposes no mutable XML or source buffer.
- `TemplateInput` exposes `Key`, effective `Label`, `Kind` (Text, Image, Paint), ordered `Targets`,
  and `Default` evidence summary. Keys are ordinal identities, not labels or XML element IDs.
- `TemplateTarget` exposes `Hook` (Text, Image, Fill, Stroke), `ElementName`, `Location` at its hook
  attribute, and `Default` evidence. ElementName is the SVG local name; admitted targets always have
  the SVG namespace.
- `TargetDefault` exposes State (Present or Absent), nullable `Raw`, and nullable `Source`
  (TextContent, Fill, Stroke, Href or XLinkHref). Present always contains a string, including empty;
  Absent never does. Source follows the [default evidence contract](../spec/defaults.md).
- `InputDefault` exposes State (Common, Mixed or Absent) and nullable common `Raw`. Only Common
  contains a string. No typed color or parsed URI appears in the discovery model.
- `Diagnostic` contains `Code`, `Severity` (Error or Warning), `Message`, nullable `Key`, nullable
  `Location`, immutable `RelatedLocations` and nullable `Limit`. Limit is present only for a budget
  diagnostic and uses the exact option name.
- `SourceLocation` contains positive one-based `Line` and `Column` using the
  [specified source convention](../spec/inspection.md).
- `InspectionOptions` exposes the seven positive integer budgets in that contract with the stated
  defaults. Snapshot options at call entry; no global mutable defaults.

Use immutable arrays for ordered public collections. Expose only states that satisfy the documented
invariants: Present target states and Common summary states always contain raw strings; other states
do not. Prefer internal construction for result objects over public constructors that admit
inconsistent combinations. Internal representation and record/class choices remain implementation
decisions.

The [interpretation API](interpretation-api.md) consumes individual TemplateTarget instances
explicitly after inspection. Its results retain those same instances and never change this model,
raw Common/Mixed/Absent summaries, diagnostics or admission. Interpretation does not add parsed
color/URI properties to TemplateTarget or TemplateInput.

## Presentation metadata extension

Resolve the [metadata contract](../spec/metadata.md) during inspection, after structural input
admission. These public members expose resolved presentation in the existing assembly and namespace:

```csharp
public enum InputLabelSource { Key, Element, Metadata }

// Additions to TemplateInput; existing Label stays the non-null effective label.
public string? Description { get; }
public InputLabelSource LabelSource { get; }
public int? Order { get; }

// Addition to InspectedTemplate; existing Inputs stays in discovery order.
public ImmutableArray<TemplateInputGroup> InputGroups { get; }

public sealed class TemplateInputGroup
{
    public string? Key { get; }
    public string? Label { get; }
    public string? Description { get; }
    public int? Order { get; }
    public ImmutableArray<TemplateInput> Inputs { get; }
}
```

`TemplateInput.LabelSource` records the winning label source, including `Metadata` when that label
happens to equal the inline label or key. Description and order are null when absent or discarded;
order 0 is a real value. No default value or hook-level label provenance is replaced by these
fields.

`InputGroups` is the resolved presentation order, including the final unnamed section when needed.
Named groups have non-null, nonempty keys and effective labels. The unnamed section has null Key,
Label, Description and Order; null is a meaningful state, not an error or an empty-string sentinel.
Every group has at least one member. With no inputs, InputGroups is empty; otherwise every admitted
input occurs exactly once. Each member is the same immutable `TemplateInput` instance as in Inputs,
not a copy or another parsed representation. Neither collection is default/uninitialized or contains
null entries. Raw target/default evidence and the order of Inputs and Targets are unchanged.

Use internal construction to enforce these invariants. Expose no mutable XML, public metadata
builder, binding service, alternate inspection entry point or separate resolution lifecycle. Hosts
may use the groups for forms or choose their own presentation. Binding continues to use keys and
admitted targets only. Nullable fields must reflect validated absence at the XML boundary; do not
suppress analysis or propagate unvalidated XML values into the public result.

## Conformance and validation

The implementation consumes the [language-neutral fixture convention](../conformance/README.md).
Check structured diagnostic codes/locations and raw default evidence; do not assert localized parser
messages. Inspection must neither invoke a renderer nor probe image references.

Use the SDK pinned in global.json and the shared Directory.Build.props policy. Test dependencies are
pinned centrally in Directory.Packages.props with committed lockfiles. Run `task check` for
documentation, locked restore, .NET formatting, Release analyzers and tests. Package and release
validation follow the [release guide](../RELEASING.md).
