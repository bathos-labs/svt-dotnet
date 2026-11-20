# Compatibility and versioning

SVT requires [Semantic Versioning 2.0.0](https://semver.org/). The library and CLI ship together at
one version. Version changes describe compatibility of the documented public contract, including
behavior, rather than the size of a diff.

## RC evaluation and stable adoption

`1.0.0-rc.1` has passed external package integration with no reported issues. Production-template
validation remains pending. Keep the exact RC pin during that evaluation; integration acceptance
does not authorize stable publication.

The RC surface is provisionally frozen. Avoid planned API or format changes during evaluation.
Necessary shipping corrections require a new RC, explicit release notes and renewed consumer
validation. Published artifacts and tags remain immutable. No prerelease migration layer is
required.

The stable guarantees below take effect at `1.0.0`. Until then, the project remains prerelease. The
[release guide](../RELEASING.md) owns publication steps and support arrangements; issue #1 owns
acceptance evidence and outstanding work.

## Classifying changes

| Revision | Use when                                                           | Examples                                                                                                     |
| -------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------ |
| Patch    | Fixing incorrect behavior without breaking the documented contract | Correct escaping to match the specification; optimize an internal implementation while preserving guarantees |
| Minor    | Adding backward-compatible capabilities or announcing deprecation  | Add an opt-in operation; add an optional output field under the rules below                                  |
| Major    | Changing an established contract incompatibly                      | Remove or rename an API; change a default policy; require a newer runtime; drop a supported input format     |

Use the highest classification needed by any change in either package. A small fix may be breaking;
a large internal refactor may be compatible. Correcting behavior that violates the specification can
be a patch, but calling a change a bug fix does not exempt a documented promise. Security fixes that
require breaking a promise need an explicit compatibility decision and release notice, not a silent
patch exception. Deprecation does not permit removal or forced migration within the same major.

Documentation and development-tool changes do not by themselves require a package release. If new
artifacts are published, use a new version; never replace an existing version. Changes to package
contents, dependencies or runtime requirements still need compatibility review.

## Stable 1.x boundaries

- Preserve the `Bathos.Graphics.SvgTemplates` and `Bathos.Graphics.SvgTemplates.Cli` package IDs,
  existing public namespaces, and the `svt` command.
- Preserve existing public .NET signatures, named parameters, nullability contracts and documented
  exceptions. Check source and binary compatibility; adding an overload can make existing calls
  ambiguous. New enum values or result kinds require explicit compatibility review, not automatic
  classification as an additive minor change.
- Preserve documented ownership, immutability, concurrency, atomic failure, ordering, and policy
  semantics. Existing defaults, resource budgets and counting rules remain stable. Introduce changed
  behavior through opt-in capabilities where needed to preserve existing results.
- Retain .NET 10 compatibility throughout 1.x. Additional targets are possible; dropping that
  support or requiring a newer runtime is a major change. The contributor SDK patch pin may advance
  without changing the consumer runtime requirement.
- Preserve documented export guarantees, including exact byte counts, encoding and allocation-free
  span operations. Throughput measurements, total application allocations, renderer behavior and
  pixel equivalence across external tools are not compatibility promises.

## Format versions

Package versions and SVG/JSON format markers are independent. Stable `1.0.0` retains
`data-svt-version="0"` and JSON `format_version: 0`. Once stable, 0 identifies a supported format;
it does not imply experimental behavior. Omitted versions in binding/configuration JSON select
format 0 permanently, never the latest format.

Throughout 1.x, existing format-0 templates and requests retain their documented meaning. An
incompatible format requires a new explicit marker; dropping support for an established format
requires a major package release. Adding a format does not authorize changing the meaning of an
existing marker or changing the default output format incompatibly. Do not implement speculative
readers or migrations before a concrete format change is agreed.

## CLI and diagnostic evolution

Preserve existing commands, flags, exit categories, stdout/stderr ownership, version/provenance
formatting, JSON field names and types, diagnostic-code meanings, and documented array ordering.
Preserve required fields and documented absence/null semantics. Existing input JSON remains strict:
unknown properties and invalid values are rejected according to the [CLI contract](cli.md).

Minor releases may add output fields and diagnostic codes without changing existing meanings or
operation success/failure rules. Consumers must ignore unknown output fields and handle unfamiliar
codes through their severity and structured context. Unknown codes are not equivalent to success.
Additional warnings must preserve the specified recovery, ordering and budget rules; new codes are
not permission to reject previously valid input. Changing a known code's meaning or severity is a
compatibility change. New enum values or result kinds need their own review.

Help and explanatory messages may improve while preserving their documented presentation semantics.
Messages are not machine parsing contracts. JSON property order and indentation remain unspecified;
documented array order remains significant. SVG output need not be byte-identical between releases,
but must preserve specified content and serialization/export guarantees. Within one completed
document, all export APIs still agree on the exact UTF-8 bytes.

## Review and enforcement

Before stable publication, record a reviewed public API/package baseline and enable automated
compatibility checks. Compare later 1.x releases against established stable surfaces, including
additions from intervening minor releases; comparing only with 1.0 is insufficient. Do not silently
advance a baseline or suppress a failure to admit a breaking change.

The [implemented checks](../eng/compatibility/README.md) use a nullable-aware source API baseline
and SDK package comparison against the exact published RC, with negative enforcement tests. This is
a provisional baseline; stable publication still requires review of the complete accepted surface.
Promote the package baseline only after the new release is publicly available and verified.

API checks supplement conformance, boundary and isolated-consumer tests. Review behavior, defaults,
formats and diagnostics separately; a compatible assembly surface alone cannot establish them.
