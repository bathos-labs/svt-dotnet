# Architecture

SVT annotates ordinary SVG artwork with a bounded set of replacement inputs. The specification
describes the interchange contract; the .NET library implements it without owning rendering.

## Portable core

One assembly, `Bathos.Graphics.SvgTemplates`, owns controlled XML parsing, input discovery,
validation, typed substitution and diagnostics. Separate namespaces may organize those
responsibilities without adding contracts-only assemblies. The initial implementation uses the .NET
base libraries.

Hosts supply source bytes. Inspection owns an immutable document snapshot; callers do not receive a
mutable XML tree. Binding creates an independent output, and invalid requested values must not
produce partially substituted content. Scoped hook errors must remain distinguishable from an
unusable root document.

The core does not fetch images, probe paths, decode pixels, convert color, resolve fonts or render
SVG. Image values describe supplied replacement content; their exact public representation is still
subject to design review. Labels are presentation metadata, not binding identity.

## Consumers and tooling

A thin CLI exposes inspection, validation and substitution with structured diagnostics. Filesystem
input/output belongs at that boundary; output publication requires explicit caller intent.

Application adapters own their document references, freshness, default/unassigned/bound choices,
image preparation, rendering and UI. The template library must not acquire those responsibilities
through callbacks or service location.

## Specification and evidence

Language-neutral fixtures establish discovery, validation and substitution behavior. Renderer
integration checks establish preview behavior for named environments. Neither guarantees identical
pixels in every SVG viewer or preservation of annotations by every authoring tool.

Exact public signatures, annotation spelling, image resource policy and diagnostic locations are
unsettled. Design work is tracked in GitHub Issues rather than represented by empty public APIs.
