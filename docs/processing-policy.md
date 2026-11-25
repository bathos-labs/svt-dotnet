# .NET processing policy

This document preserves the .NET implementation's detailed behavior independently of the portable
SVT format. It accompanies the specification's conforming-input simplification; it does not adopt a
new canonical snapshot or change library behavior. The existing public API, regression tests and
accepted snapshot remain authoritative for this implementation.

The following policies were previously described by the accepted specification material. They are
now documented here so future spec changes do not remove the .NET promises. Other implementations
need not reproduce our diagnostic codes, source coordinates, scoped recovery, budget accounting or
cancellation mechanics. The [inspection API](inspection-api.md), [binding API](binding-api.md) and
[interpretation API](interpretation-api.md) define their .NET representations. Resource and
malformed-input tests remain implementation obligations.

## Inspection policy

Inspection accepts XML bytes and returns either an immutable template plus scoped diagnostics, or no
template plus a root failure. A usable template may have errors on rejected hooks; independently
valid inputs remain available. This is tolerant inspection, not strict authoring certification.

Support UTF-8 and UTF-16 XML encodings, including their optional byte-order marks, using XML
encoding detection. Reject other declared encodings. Invalid byte sequences are errors, never
replacement characters. XML 1.0 processing is required; duplicate attributes are malformed XML. DTD
declarations are rejected and external resolution is disabled. Cancellation must remain distinct
from malformed input and must not return a partial result. Cancellation throws
`OperationCanceledException` carrying the supplied token.

### Diagnostic model

Each diagnostic has a stable string code, severity, human-readable message, optional input key,
optional primary source location and ordered related locations. Messages are explanatory and are not
a parsing or conformance contract. Codes and structured fields are the machine interface.

Locations use one-based line and column numbers. Columns count UTF-16 code units, including a tab as
one unit; CRLF counts as one line break. This convention is explicit for non-.NET consumers. Hook
locations identify the start of the attribute name; element locations identify the start of the
element name. These are points, not byte spans. Root parsing failures use the parser-reported point
when available; failures before parsing may have no location. Invalid-byte failure locations are not
guaranteed. Related locations preserve discovery order.

| Code                     | Severity | Effect                                                                                          |
| ------------------------ | -------- | ----------------------------------------------------------------------------------------------- |
| `svt.xml.invalid`        | Error    | No template                                                                                     |
| `svt.xml.encoding`       | Error    | Unsupported declared encoding; no template                                                      |
| `svt.xml.dtd`            | Error    | Prohibited DTD; no template                                                                     |
| `svt.root.invalid`       | Error    | Wrong root namespace/name; no template                                                          |
| `svt.version.invalid`    | Error    | Missing, malformed or unsupported root marker; no template                                      |
| `svt.limit.exceeded`     | Error    | No template; structured `limit` identifies the exceeded option                                  |
| `svt.hook.target`        | Error    | Reject incompatible hook                                                                        |
| `svt.hook.key`           | Error    | Reject empty-key hook                                                                           |
| `svt.input.ambiguous`    | Error    | Reject all conflicting group members                                                            |
| `svt.input.labels`       | Warning  | Without a central label, retain first target label; related locations identify differing labels |
| `svt.annotation.unknown` | Warning  | Ignore unknown SVT annotation                                                                   |
| `svt.version.nested`     | Warning  | Ignore descendant version marker                                                                |

The [metadata recovery policy](#metadata-recovery-policy) defines the warning codes
`svt.metadata.container`, `svt.metadata.duplicate`, `svt.metadata.input`, `svt.metadata.group`,
`svt.metadata.invalid` and `svt.metadata.unknown`. Its recovery rules define label precedence,
declaration rejection and field-level fallback without changing hook admission.

Scoped diagnostics sort by primary source position, then code ordinally for ties. Ambiguity
locations identify the first admitted group member. Root/limit failures return one terminal
diagnostic and no partial discovery findings. If multiple faults exist, report the first encountered
by bounded XML parsing; after a successful parse check root name before version, then inspect hooks
and resolve metadata. Defer inline-label conflict findings until central label resolution is known.
Metadata warnings share the diagnostic budget and final ordering with hook findings.

### Inspection budgets

Defaults are operational policy, not format restrictions or performance guarantees. Callers may
raise or lower each positive integer limit explicitly; invalid options are programming errors. There
is no unlimited sentinel. A value exactly equal to a limit is permitted.

These are the .NET defaults and counting rules. Existing stamped reference and local policy tests
exercise them; they are not requirements for other implementations.

| Option                    | Default    | Counting rule                                                                                         |
| ------------------------- | ---------- | ----------------------------------------------------------------------------------------------------- |
| `MaxSourceBytes`          | 16,777,216 | Supplied byte count, including BOM                                                                    |
| `MaxXmlCharacters`        | 16,777,216 | Decoded UTF-16 code units before XML newline/entity normalization, including markup but excluding BOM |
| `MaxElementDepth`         | 64         | Root is depth 1; element nesting only                                                                 |
| `MaxElements`             | 100,000    | All elements, including unmarked artwork                                                              |
| `MaxAttributesPerElement` | 256        | All attributes, including namespace declarations                                                      |
| `MaxHooks`                | 10,000     | All occurrences of the four known hook attributes, even invalid hooks                                 |
| `MaxDiagnostics`          | 1,000      | Scoped errors and warnings before terminal rejection                                                  |

Apply byte limits before copying input; enforce structural limits during streaming XML parsing,
before constructing an unbounded tree. Discovery and grouping must be linear apart from bounded
diagnostic and presentation ordering; no recursive filesystem or SVG dependency traversal is
allowed. Metadata counts toward all applicable budgets, including hooks excluded from discovery
inside SVG metadata. Check cancellation before work and at element/discovery, metadata-resolution
and sorting boundaries. These budgets do not bound native decoding or pixel allocation because
inspection performs neither.

When the next scoped diagnostic would exceed the diagnostic budget, discard the partial inspection
and return a single `svt.limit.exceeded` with `limit=MaxDiagnostics`. Treat other budget failures
the same way. Count decoded characters at the decoding boundary; do not classify budget failures by
localized XML exception-message text. A parser's own character budget may provide an additional
defense but is not the definition of this counter.

SVG schema and property-value validation are outside core. Malformed paint and URI values do not
produce discovery errors or warnings. Optional interpretation is separate from discovery. Encoded
replacement byte limits and bound-output limits belong to substitution, not this inspection API.

## Hook admission and scoped recovery

A valid central label overrides inline labels. Otherwise the first admitted target supplies the
input label. Differing inline labels produce one warning at the first target with differing-label
targets as related locations, without rejecting the input. Defer these warnings until metadata label
resolution is complete; a central label suppresses them, a description-only declaration does not.

Process known hooks in document preorder, with hook order `text`, `image`, `fill`, `stroke` within
an element, independent of XML attribute order. Check target compatibility, then key presence. A
hook failing either check produces one error and is excluded before grouping. Group the remaining
hooks by exact key. Duplicate text/image targets or mixed kinds reject every member of that group.
Report one ambiguity error at its first hook, with the other hook locations as related locations.
Retain unrelated valid inputs.

Inputs appear in first-valid-target order. Targets within an input retain discovery order. The
separate
[presentation view](https://github.com/svt-project/svt-spec/blob/master/spec/metadata.md#ordering-and-single-level-groups)
never reorders either structural collection. An invalid hook excluded before grouping cannot
invalidate a compatible hook elsewhere.

## Metadata recovery policy

Count direct `svt:inputs` properties across all matching descriptions in all root-level SVG
`metadata` elements and their direct `rdf:RDF` children. At most one may occur, even if its literal
marker is invalid. Multiple containers disable central metadata before envelope or field validation.
A single container requires exactly `rdf:parseType="Literal"`; missing, differently cased or other
values produce one `svt.metadata.invalid` warning and discard its contents. No additional
RDF-namespace attributes are supported on that description or property, including alternate
identity, reference and datatype attributes. They invalidate the container rather than changing its
meaning. Check the literal marker first, then the description's RDF attributes, then the property's
RDF attributes; report only the first offending attribute in source order in that stage. Missing
markers point at the property element. No descendant findings are produced for a discarded
container.

All metadata semantic findings are warnings. They do not reject artwork inputs or prevent binding.
Malformed XML and exceeded inspection budgets still reject the whole template under the existing
[inspection policy](#inspection-policy). Binding preserves metadata semantically, including ignored
declarations, alongside unrelated artwork; it does not repair or remove metadata.

Warning messages explain the specific failed rule and what was ignored: for example, the permitted
`order` grammar/range, the prohibition on description child elements, or the single-description
limit. Findings sharing a code must still distinguish those failures for authors. Message wording is
explanatory, not a machine interface; use the existing codes and structured fields for program
logic. Locations, severity, recovery and diagnostic budgets are unchanged by message detail.

| Code                     | Condition                                                                               | Recovery                                                                                                  |
| ------------------------ | --------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `svt.metadata.container` | More than one eligible `svt:inputs` container                                           | Ignore all central metadata; use inline/key labels and the unnamed section                                |
| `svt.metadata.duplicate` | Repeated input or group key, even with identical declarations                           | Ignore every declaration of that identity, not just later occurrences                                     |
| `svt.metadata.input`     | Input declaration references an unknown or rejected input                               | Ignore the declaration                                                                                    |
| `svt.metadata.group`     | Input references an unknown or discarded group                                          | Ignore membership; retain its other valid fields                                                          |
| `svt.metadata.invalid`   | Invalid RDF envelope, missing/empty identity, invalid optional field or unexpected text | Ignore the container for an invalid envelope, declaration for an invalid identity, or affected field/text |
| `svt.metadata.unknown`   | Unknown SVT element or unknown attribute in the interpreted vocabulary                  | Ignore that attribute or the element's complete subtree                                                   |

Apply these precedence rules to avoid document-order-dependent recovery and cascading diagnostics:

- Multiple containers produce one container warning at the first container with the remaining
  containers as related locations. Do not interpret or diagnose their envelopes or contents.
- For a single container, identify all direct input/group declarations by their keys first. A
  missing/empty key produces one invalid warning and suppresses further diagnostics in that
  declaration. A nonempty key participates in duplicate detection even if its other fields are
  invalid or it references no admitted input.
- Duplicate detection is separate for input keys and group keys. Emit one duplicate warning at the
  first declaration with the others as related locations. Ignore their contents without additional
  field/reference warnings. For a unique unknown/rejected input, emit one input warning and ignore
  its contents before checking optional fields.
- Resolve optional fields independently for remaining declarations. A discarded group remains
  unresolved: each otherwise usable input referencing it receives a group warning. Invalid group
  reference syntax receives only an invalid warning, not an additional unresolved-group warning.
- Multiple description children produce one invalid warning at the first description, with the
  others as related locations; ignore all of those descriptions without inspecting their contents. A
  single description with any child element produces one invalid warning at that description and
  loses the description only. Do not flatten nested markup or produce descendant findings.
- Within `svt:inputs`, `svt:input` and `svt:group`, whitespace, comments and processing instructions
  between child elements are ignored. Other character data produces one invalid warning per parent
  element and is ignored. This restriction does not apply to the enclosing SVG `metadata` element.
  Unknown and foreign subtrees are not recursively interpreted.

Input-scoped findings carry the exact input key when it is available. Container and group findings,
and findings for missing/empty identities, have a null diagnostic key. An input's bad group
reference is input-scoped. Attribute findings point at the attribute name; missing identity and
element/text findings point at the relevant element name. Duplicate/container locations point at
element names. Related locations retain document order. Use the existing one-based UTF-16
coordinates, final source-position/code ordering and diagnostic budget. There is no new severity,
policy switch or budget option.

All metadata markup, text and attributes count toward existing source, XML, depth, element and
attribute budgets. Known hook attributes still count toward `MaxHooks`, including occurrences in
nonbinding metadata. Metadata warnings count toward `MaxDiagnostics`. Resolution must be linear
apart from bounded diagnostic and presentation sorting; do not perform repeated whole-document
searches per declaration. Cancellation also applies during metadata resolution and sorting.

## Binding request policy

Omission preserves original content, including differing defaults across shared paint targets. Empty
strings are explicit replacements. There is no default/unassigned sentinel, completeness
requirement, silent overwrite policy or general continue-on-error mode.

Bind operates only on admitted inputs. Unrelated scoped inspection errors do not prevent binding. An
admitted key takes precedence over invalid hooks excluded during inspection: those excluded hooks
remain untouched. Inspection must retain rejected-key evidence internally without exposing rejected
inputs as usable inputs.

Unknown supplied keys produce `svt.binding.unknown`: Error by default, or Warning with the
assignment ignored under IgnoreWithWarning. A key with rejected hooks but no admitted input is
rejected, not unknown. Rejected keys, duplicates (even identical assignments), wrong kinds and
XML-unrepresentable replacement values for admitted inputs are errors. Ignored unknown assignments
skip kind and value validation, but remain subject to budgets and duplicate-key rejection.

All submitted assignments count toward input budgets, including ignored assignments. A successful
operation publishes one complete document under the selected admission policy. Any error means no
document. Binding cancellation and failures never expose a partial document. Hosts control
subsequent saving or rendering; successful binding does not certify resource safety or rendering
behavior, or make downstream publication transactional.

### Binding validation order

After programming-argument checks and bounded assignment capture, apply these phases in order:

1. Enforce all submitted-value and image budgets before semantic validation, including values on
   unknown or duplicate keys. Count limits apply before capture; output and diagnostic limits apply
   in their respective later phases.
2. Identify duplicate ordinal-key groups. Reject each group once at its first assignment, including
   unknown keys and empty keys. Skip individual semantic validation for every member of that group.
3. Validate remaining assignments in supplied order: empty key, admitted/rejected/unknown lookup,
   kind, then XML representability. Stop at the first finding for each assignment. IgnoreWithWarning
   produces an unknown-key warning and skips further validation of that assignment. Its value is
   never serialized. Continue checking other assignments within the diagnostic budget.
4. Construct and serialize output only when request validation has no errors. Enforce the output
   budget before exposing a document.

Image creation checks encoded-byte limit, media-type length, then media-type grammar before copying
bytes. It reports the first failure. Null arguments remain programming errors; cancellation remains
cancellation throughout all phases.

### Binding diagnostics

Reuse the inspection diagnostic model. Binding results contain findings from this attempt only;
inspection findings remain in the inspection result. Known-key locations point to original hook
attributes, with other relevant targets in discovery order as related locations. Unknown keys and
image-creation failures have no template locations. The diagnostic key is the supplied key when
applicable. The limit field is non-null only for budget failures, using the budget name below.

| Code                    | Meaning                                                     | Effect                                               |
| ----------------------- | ----------------------------------------------------------- | ---------------------------------------------------- |
| `svt.binding.key`       | Empty assignment key                                        | Error; no document                                   |
| `svt.binding.unknown`   | Key is not known to inspection                              | Error, or warning and ignore under configured policy |
| `svt.binding.rejected`  | Key has no admitted input but has rejected hooks            | Error; no document                                   |
| `svt.binding.duplicate` | Multiple assignments for the same ordinal key               | Error; no document                                   |
| `svt.binding.kind`      | Replacement kind differs from input kind                    | Error; no document                                   |
| `svt.binding.value`     | Replacement is not representable in XML 1.0                 | Error; no document                                   |
| `svt.image.media-type`  | Encoded-image media type is outside the convenience grammar | Error; no image                                      |
| `svt.limit.exceeded`    | Positive budget exceeded                                    | One terminal error; no image or document             |

Binding diagnostics sort by assignment index, then code ordinally for ties. Messages are
explanatory, not machine contracts. Image creation returns the first failure. If the next diagnostic
exceeds MaxDiagnostics, return only the terminal limit diagnostic. Other budget failures likewise
discard accumulated findings. Warnings count toward MaxDiagnostics.

### Binding budgets

Defaults are operational policy, not format restrictions. All limits are positive integers; equality
is allowed and there is no unlimited sentinel. Use overflow-safe accounting. Configuration is
immutable and reusable, but limits apply per operation, not to application-wide retained objects or
concurrent work.

The following .NET defaults and counting rules are tested as implementation policy.

| Option                    | Default    | Counting rule                                                                                                      |
| ------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------ |
| MaxEncodedImageBytes      | 16,777,216 | Encoded content bytes per image, checked before capture and again during binding                                   |
| MaxMediaTypeCharacters    | 256        | UTF-16 units in the supplied media type, checked at creation and against binding configuration                     |
| MaxBindings               | 10,000     | All submitted assignments, before list capture                                                                     |
| MaxValueCharacters        | 1,048,576  | Each key or raw string payload individually, in UTF-16 units before XML escaping                                   |
| MaxTotalValueCharacters   | 8,388,608  | Sum of every submitted key and raw string payload, including duplicates and ignored assignments                    |
| MaxTotalEncodedImageBytes | 33,554,432 | Encoded bytes summed per assignment, even when an image object is reused                                           |
| MaxOutputBytes            | 67,108,864 | Complete serialized UTF-8 output, including repeated targets, escaping, base64 and duplicated reference attributes |
| MaxDiagnostics            | 1,000      | Errors and warnings accumulated before terminal rejection                                                          |

Image options own the first two limits; binding options own the others. Media types are governed by
their own limit; generated base64 is excluded from raw string budgets. Empty values contribute zero
units but still count as assignments. Raw data URLs supplied as image references are string
payloads, not encoded-content objects, and therefore use the string budgets.

Enforce output limits during bounded serialization, before constructing an oversized output. A
shared replacement is charged once to submitted-value budgets but every emitted target counts toward
output bytes. A successful image capture does not reserve binding capacity or bypass stricter later
configuration. The output limit exceeds default inspection limits; reinspecting a large output may
require explicitly raised inspection budgets.

## Interpretation resource policy

MaxNumericTokenCharacters counts sign, decimal point, exponent and exponent sign/digits, excluding a
trailing percent sign or hue unit. MaxDataUriHeaderCharacters counts from the initial `d` in `data:`
through the first comma inclusive. Payload bytes are not scanned or counted by that header limit.
These details remain .NET policy even when the portable spec no longer fixes the counters.

All options are positive integers, measured in UTF-16 code units except nesting depth. The following
profile defines the .NET defaults. Hosts may configure different positive limits. Counting units and
failure precedence here are .NET policy, not portable conformance obligations.

| Option                     | Default | ExceededLimit           |
| -------------------------- | ------: | ----------------------- |
| MaxPaintCharacters         |    4096 | PaintCharacters         |
| MaxReferenceCharacters     |    4096 | ReferenceCharacters     |
| MaxDataUriHeaderCharacters |    1024 | DataUriHeaderCharacters |
| MaxNumericTokenCharacters  |      64 | NumericTokenCharacters  |
| MaxNestingDepth            |      16 | NestingDepth            |

Equality passes; the first unit beyond a budget fails that interpretation with
UnsupportedOrUnrecognized and the corresponding ExceededLimit. No partially parsed color or media
type is returned. Unsupported syntax and numeric overflow have null ExceededLimit. No new
inspection/binding limit or diagnostic code is introduced.

After API argument/configuration validation, apply these rules:

1. An absent target returns Absent without scanning a value.
2. Paint checks the complete raw length before trimming or allocating derived strings. Scan
   left-to-right for the supported lexical structure, numeric-token lengths and nesting before
   semantic classification. Stop at the first lexical rejection or exceeded scan limit; later
   findings do not replace it. Numeric-token checks cover tokens outside strings and URL bodies,
   including opaque var() fallbacks. Nesting counts simultaneously open function parentheses; the
   outermost function is depth 1, including url(), and quoted parentheses do not count. Identifier
   and hash-token contents are not numeric tokens: digits inside custom-property names or
   hexadecimal colors do not consume MaxNumericTokenCharacters.
3. After a successful paint scan, classify the complete keyword, resource expression, contextual
   function expression or supported literal under the
   [portable interpretation grammar](https://github.com/svt-project/svt-spec/blob/master/spec/interpretation.md);
   otherwise return unsupported.
4. Images distinguish Empty, then data-prefix selection, then ordinary references. For ordinary
   references check the complete raw length before any character scan.
5. For data URIs find the comma within the header budget before interpreting header fields. If no
   comma occurs and the entire string fits the budget, return unsupported without a limit. If the
   string extends beyond the budget with no comma inside it, report DataUriHeaderCharacters without
   scanning farther, even if the unseen suffix contains no comma. Header grammar is checked only
   after a bounded comma is found.

Numeric and nesting budgets apply only to paint. Reference length applies only to ordinary image
references. Data headers use their own limit; payload length is not an interpretation budget.
Inspection still owns source and XML budgets before any target exists.

Use bounded linear scans with no recursive evaluation or backtracking regexes. Extra parsing storage
is bounded by options; retaining the already immutable target/raw string is not a new copy. No
allocation-free guarantee is made. Cancellation applies before work, throughout scans and before
return, including unsupported/limit results. No operation performs I/O, resource callbacks, CSS
cascade evaluation, image decoding, MIME sniffing, dimension discovery or rendering.
