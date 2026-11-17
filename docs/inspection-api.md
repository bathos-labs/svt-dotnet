# .NET inspection API

This is the implementation contract for the first slice, not documentation of shipped APIs. Target
.NET 10 with one BCL-only assembly named `Bathos.Graphics.SvgTemplates`. Use the same root
namespace. Keep parsing and mutable XML implementation types internal.

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

## Conformance and validation

The implementation consumes the [language-neutral fixture convention](../conformance/README.md).
Check structured diagnostic codes/locations and raw default evidence; do not assert localized parser
messages. Inspection must neither invoke a renderer nor probe image references.

Bootstrap the .NET SDK with an explicit global.json, central test dependency versions and locked
restore. Use the installed stable .NET 10 SDK feature band and document its pin; package selection
is a routine implementation choice subject to restore/build validation. Extend task check with .NET
formatting, Release build/analyzers and the test suite. Keep existing documentation checks. Do not
add release automation or publish packages in this slice.
