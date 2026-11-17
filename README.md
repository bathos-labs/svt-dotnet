# SVT — Scalable Vector Templates

A portable SVG templating specification and .NET library by Bathos Labs.

Templates remain ordinary SVG files with useful authored previews. Binding annotations identify
replaceable text, images and paint values; SVG retains ownership of layout and rendering.

This private, unreleased repository implements portable structural inspection and atomic
substitution with conformance tests. The [CLI contract](spec/cli.md) is agreed but not implemented.
`SVT` is a working format name, not an established standard.

## Boundaries

- `Bathos.Graphics.SvgTemplates`: one portable library for structural inspection and XML-safe
  substitution.
- A thin CLI will expose the same rules to external authors and agents.
- The specification and conformance fixtures remain independent of .NET.
- Consumers own acquisition, image preparation, rendering and application document state.

See [architecture](docs/architecture.md), [specification](spec/README.md),
[inspection API](docs/inspection-api.md), and [conformance](conformance/README.md). Design decisions
and delivery work belong in [GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

## Publication

The intended package name is `Bathos.Graphics.SvgTemplates`. Code, specification, documentation and
repository-authored examples are [MIT-licensed](LICENSE). See [NOTICE](NOTICE) and
[release readiness](RELEASING.md). The repository remains private and no package release is
authorized or available yet.

## Development

The primary branch is `master`. See [Contributing](CONTRIBUTING.md) for the documentation validation
gate and working conventions. The .NET 10 SDK and common build policy are pinned for the first
implementation; CI checks documentation, locked restore, C# formatting, Release analyzers and tests.
With Node.js 24 and Task installed, run `task docs:format` after edits and `task check` before
committing. The solution is `SvgTemplates.slnx`.
