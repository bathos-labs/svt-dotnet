# Changelog

Consumer-facing changes are recorded here. Prereleases may change the API and specification; pin the
exact package version while evaluating them.

## [Unreleased]

## [1.0.0-rc.1] - 2026-11-20

- Package the CLI as the version-pinned `Bathos.Graphics.SvgTemplates.Cli` .NET tool, with command
  `svt`, global/local installation and one-off execution. Add `task svt` for contributors and
  isolated installed-tool checks alongside independent library consumption.
- Validate and release the library and CLI together, retaining the exact packages, symbols and
  native evidence in a draft release before publication. Resume partial releases from those bytes
  under protected approval; rehearse interruptions without registry or release writes.

- Add command-specific CLI help and `-h`, clearer text diagnostics, labeled locations and readable
  Unicode. Inspection shows labels and bounded value previews, hiding embedded image payloads; JSON
  keeps complete evidence. Version output includes the product version and available commit.
- Add an inspect-to-bind walkthrough with a small template and binding file. Move contributor setup
  and validation guidance out of the README.

- Default omitted CLI binding/configuration `format_version` to 0 and omitted `bindings` to an empty
  array. `{}` is a valid no-assignment request; strict explicit-value validation, processing budgets
  and publication protections still apply. Machine-output envelopes retain explicit markers.

- Add optional per-target paint and image interpretation with immutable results and bounded parsing.
  Supported color literals produce sRGB with alpha; context and paint references remain unevaluated.
  Image classification reads data-URI headers without examining payloads. Raw evidence and binding
  admission remain unchanged; interpreted aggregation is deferred. An executable host-owned editor
  example separates authored evidence, presentation and chooser state, preserving default intent
  across initialization and unchanged/canceled interactions.

- Add optional RDF/XML document metadata for central input labels, descriptions, ordering and
  single-level groups. Inspection exposes label provenance and a separate immutable presentation
  view; CLI discovery includes those fields and ordered key references. SVG metadata subtrees are
  nonbinding content. Metadata warnings explain the failed rule and its recovery in API and CLI
  diagnostics.

- Add `BoundSvg.Utf8ByteCount` and `TryWriteUtf8Bytes` for exact sizing and allocation-free UTF-8
  export into caller-owned storage. Insufficient capacity leaves storage unchanged; successful
  writes preserve its unused suffix. Binding budgets and serialization remain unchanged.
- Document pooled output lifetime through downstream consumption, with an executable consumer
  example, and distinguish export allocations from retained representations and end-to-end costs.

## [0.1.0-beta.1] - 2026-11-18

- First public beta of the BCL-only .NET 10 SVG template library.
- Inspect immutable SVG templates with structured diagnostics, raw authored defaults, and
  replaceable text, paint, and image inputs identified by binding keys and optional labels.
- Bind typed values atomically with reusable configuration, configurable admission policies,
  budgets, cancellation, and independently owned output. Omitted values retain authored artwork.
- Supply encoded image bytes or explicit references without giving the library resource access or
  rendering responsibilities.
- Include XML API documentation, portable symbols, Source Link, and version-pinned usage guidance.
- Provide a source-built inspection/binding CLI and independently authored conformance examples. The
  CLI and native validation tools are not part of the NuGet package.

The beta does not promise stable API or specification compatibility. SVG retains layout ownership;
hosts own resource acquisition, decoding, fonts, CSS interpretation, fitting, and rendering. Native
evidence covers named tools and environments, not universal SVG rendering. The template marker and
JSON envelopes remain version 0 independently of the package version.

[Unreleased]: https://github.com/bathos-labs/svt-dotnet/compare/v1.0.0-rc.1...HEAD
[1.0.0-rc.1]: https://github.com/bathos-labs/svt-dotnet/releases/tag/v1.0.0-rc.1
[0.1.0-beta.1]: https://github.com/bathos-labs/svt-dotnet/releases/tag/v0.1.0-beta.1
