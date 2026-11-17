# Architecture

SVT annotates ordinary SVG artwork with a bounded set of replacement inputs. The specification
describes the interchange contract; the .NET library implements it without owning rendering.

## Portable core

One assembly, `Bathos.Graphics.SvgTemplates`, owns controlled XML parsing, input discovery,
structural diagnostics and XML-safe substitution. Separate namespaces may organize those
responsibilities without adding contracts-only assemblies. The initial implementation uses the .NET
base libraries.

Hosts supply source bytes. Inspection owns an immutable document snapshot; callers do not receive a
mutable XML tree. Binding creates an independent output, and structurally unusable requests must not
produce partially substituted content. Scoped hook errors must remain distinguishable from an
unusable root document.

The core does not fetch images, probe paths, decode pixels, convert color, resolve fonts or render
SVG. [Image values](../spec/images.md) describe supplied encoded content or references. Hosts retain
resource authority. Labels are presentation metadata, not binding identity.

## Consumers and tooling

A thin CLI exposes inspection and substitution with structured diagnostics. Filesystem input/output
belongs at that boundary; output publication requires explicit caller intent.

Application adapters own their document references, freshness, default/unassigned/bound choices,
image preparation, rendering and UI. The template library must not acquire those responsibilities
through callbacks or service location.

## Specification and evidence

Language-neutral fixtures establish discovery, structural rejection and substitution behavior.
Renderer integration checks establish preview behavior for named environments. Neither guarantees
identical pixels in every SVG viewer or preservation of annotations by every authoring tool.

The [inspection API](inspection-api.md) and [SVT specification](../spec/README.md) define the first
implementation boundary. The agreed [binding API](binding-api.md) and
[substitution contract](../spec/substitution.md) define the next implementation boundary.
BindingConfiguration groups reusable immutable policy; it is not a service or mutable binding
session. Encoded images own captured bytes; each bind produces an independent immutable document.
Substitution is not implemented yet. Optional interpretation remains separate design work tracked in
GitHub Issues; do not add empty public APIs for it.

## Optional value interpretation

Pure, opt-in helpers may interpret a documented subset of raw literals for editor controls. Keep
them in the same assembly initially, separate from discovery and binding admission. Preserve source
evidence and distinguish literal values, no paint, context requirements, resource references and
unrecognized syntax. Failure to interpret is not template invalidity.

Helpers do not compute CSS, fetch resources, decode images or establish effective rendered values.
Consumers own validation, rendering and resource policy. No renderer, schema validator or
inline-style parser is required by the core.
