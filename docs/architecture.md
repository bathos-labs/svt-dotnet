# Architecture

SVT annotates ordinary SVG artwork with a bounded set of replacement inputs. The specification
describes the interchange contract; the .NET library implements it without owning rendering.

## Portable core

One assembly, `ScalableVectorTemplates`, owns controlled XML parsing, input discovery, structural
diagnostics and XML-safe substitution. Separate namespaces may organize those responsibilities
without adding contracts-only assemblies. The initial implementation uses the .NET base libraries.

Hosts supply source bytes. Inspection owns an immutable document snapshot; callers do not receive a
mutable XML tree. Binding creates an independent output, and structurally unusable requests must not
produce partially substituted content. Scoped hook errors must remain distinguishable from an
unusable root document.

The core does not fetch images, probe paths, decode pixels, perform color management, resolve fonts
or render SVG. Optional authored-literal interpretation may calculate sRGB components from the
specified HSL subset. [Image values](../spec/images.md) describe supplied encoded content or
references. Hosts retain resource authority. Labels are presentation metadata, not binding identity.

Encoded-image substitution assembles a data URL from a validated literal media type and base64
bytes, with XML escaping handled by serialization. Optional interpretation reads only a bounded
data-URL header. General URL parsing, normalization and resource resolution are not responsibilities
of discovery or binding; supplied image references remain opaque strings.

The [central metadata contract](../spec/metadata.md) extends inspection with optional labels,
descriptions, ordering and one level of groups. The specified RDF/XML envelope attaches one
XML-valued input property to the document resource. The core recognizes that bounded XML profile
without general RDF processing or resource resolution. Resolve metadata against admitted inputs
once, preserving structural discovery order and exposing a separate immutable presentation view over
the same input objects. Metadata does not create bindings, supply defaults or enforce host form
behavior. SVG metadata subtrees are nonbinding content, still subject to XML and resource budgets.
Binding preserves them semantically.

[Caller-buffer export](binding-api.md#output-export) extends consumption of a completed document.
Bind owns validation, bounded serialization and exact UTF-8 size accounting. BoundSvg retains
immutable XML and its byte count; export writes into caller-supplied storage without retaining that
memory. Allocation, pooling, destination lifetime and subsequent publication remain host
responsibilities. Export introduces no binding session, owned buffer, renderer dependency or
disposal lifecycle.

## Consumers and tooling

A thin CLI exposes inspection and substitution with structured diagnostics. Filesystem input/output
belongs at that boundary; output publication requires explicit caller intent. The
[CLI contract](../spec/cli.md) defines inspect and bind commands, typed JSON assignments, reusable
configuration, separate result/diagnostic streams and explicit staged file publication. The BCL-only
executable owns request parsing, file acquisition and publication; the existing library remains the
only inspector and binder. Its default refusal to publish after inspection errors is host policy;
the portable binder continues to admit valid inputs independently of unrelated scoped inspection
errors.

The [distribution contract](../spec/tool-distribution.md) packages the existing CLI separately at
the library's version and source commit. The tool bundles the exact library DLL and matching
symbols. Candidate inspection and receipts bind both products to one release; no second binder,
repository or production framework is introduced. Prepared local-tool execution is the automation
path. SDK acquisition and contributor build wrappers retain their own failure/output behavior.
[Release recovery](../spec/tool-distribution.md#draft-release-recovery) uses the original validated
artifacts retained in a draft GitHub release under protected approval.

Application adapters own their document references, freshness, default/unassigned/bound choices,
image preparation, rendering and UI. The template library must not acquire those responsibilities
through callbacks or service location.

## Specification and evidence

Language-neutral fixtures establish discovery, structural rejection and substitution behavior.
Renderer integration checks establish preview behavior for named environments. Neither guarantees
identical pixels in every SVG viewer or preservation of annotations by every authoring tool.

The [inspection API](inspection-api.md) and [SVT specification](../spec/README.md) define the first
implementation boundary. The agreed [binding API](binding-api.md) and
[substitution contract](../spec/substitution.md) define substitution behavior. BindingConfiguration
groups reusable immutable policy; it is not a service or mutable binding session. Encoded images own
captured bytes; each bind produces an independent immutable document. The
[interpretation API](interpretation-api.md) provides explicit per-target operations after
inspection.

## Optional value interpretation

The [interpretation contract](../spec/interpretation.md) defines pure, opt-in operations over one
inspected target at a time, in the existing assembly. Results retain the original immutable target
and distinguish literal values, no paint, context requirements, resource references and unrecognized
syntax. Discovery and binding do not invoke interpretation. Failure or a parsing limit affects only
that interpretation result, not template validity or binding admission.

Literal colors use normalized double-precision sRGB and unpremultiplied alpha. Image interpretation
classifies references and reads bounded data-URI headers without scanning payloads. There is no
aggregate interpreted Common/Mixed summary, approximate color equality, automatic canonicalization,
new service, assembly, dependency or CLI output change.

Helpers do not compute CSS, fetch resources, decode images or establish effective rendered values.
Consumers own validation, rendering and resource policy. No renderer, schema validator or
inline-style parser is required by the core.
