# SVT — Scalable Vector Templates

[![NuGet](https://img.shields.io/nuget/vpre/Bathos.Graphics.SvgTemplates?label=NuGet)](https://www.nuget.org/packages/Bathos.Graphics.SvgTemplates)
[![Validation](https://github.com/bathos-labs/svt-dotnet/actions/workflows/validate.yml/badge.svg?branch=master)](https://github.com/bathos-labs/svt-dotnet/actions/workflows/validate.yml)
[![Codecov](https://img.shields.io/codecov/c/github/bathos-labs/svt-dotnet?branch=master)](https://app.codecov.io/gh/bathos-labs/svt-dotnet)

A portable SVG templating specification and .NET library by Bathos Labs.

Templates remain ordinary SVG files with useful authored previews. Binding annotations identify
replaceable text, images and paint values; SVG retains ownership of layout and rendering.

This public, prerelease repository implements portable structural inspection and atomic substitution
with conformance tests and an [inspection and binding CLI](spec/cli.md). `SVT` is a working format
name, not an established standard.

## Boundaries

- `Bathos.Graphics.SvgTemplates`: one portable library for structural inspection and XML-safe
  substitution.
- A thin CLI exposes the same rules to external authors and agents.
- The specification and conformance fixtures remain independent of .NET.
- Consumers own acquisition, image preparation, rendering and application document state.

See the [changelog](CHANGELOG.md), [architecture](docs/architecture.md),
[specification](spec/README.md), [inspection API](docs/inspection-api.md),
[conformance](conformance/README.md), and [authoring guidance](docs/authoring.md). Design decisions
and delivery work belong in [GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

## Publication

The package name is `Bathos.Graphics.SvgTemplates`. Code, specification, documentation and
repository-authored examples are [MIT-licensed](LICENSE). See [NOTICE](NOTICE) and
[release policy](RELEASING.md). The published beta is
[`0.1.0-beta.1`](https://github.com/bathos-labs/svt-dotnet/releases/tag/v0.1.0-beta.1). Consumers
can pin it explicitly:

```sh
dotnet add package Bathos.Graphics.SvgTemplates --version '[0.1.0-beta.1]'
```

Prerelease APIs and the specification may change. Package versions are independent of the template
and JSON format markers, which remain 0. [Package preparation](eng/package/README.md) validates
isolated consumption, and the [package README](docs/package-readme.md) describes the API lifecycle.

## Development

The primary branch is `master`. See [Contributing](CONTRIBUTING.md) for the documentation validation
gate and working conventions. The .NET 10 SDK and common build policy are pinned for the first
implementation; CI checks documentation, locked restore, C# formatting, Release analyzers and tests.
With Node.js 24 and Task installed, run `task docs:format` after edits and `task check` before
committing. The solution is `SvgTemplates.slnx`.

Run `task test:coverage` for library and CLI line/branch evidence. CI uploads both Ubuntu and
Windows reports to Codecov after validation succeeds. Percentages are informational; missing or
invalid reports fail validation. See the [coverage guide](eng/coverage/README.md). Native rendering
evidence remains separate.

## Command-line use

Build the solution, then invoke the local executable through the .NET host. No global tool or
package installation is required or published:

```sh
dotnet build SvgTemplates.slnx -c Release
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll inspect template.svg --format json
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll bind template.svg --bindings bindings.json --output result.svg
```

A minimal binding file is:

```json
{
  "format_version": 0,
  "bindings": [{ "key": "heading", "kind": "text", "value": "Hello" }]
}
```

Use `--help` for command syntax and [the CLI contract](spec/cli.md) for typed image values,
configuration, budgets, diagnostics and publication policies. `--diagnostics json` emits a separate
JSON envelope on stderr; inspect's `--format json` controls discovery on stdout. Binding requires an
explicit destination, refuses existing files without `--overwrite`, and accepts `--output -` for SVG
stdout. Omitted assignments retain authored content. Inspection errors prevent publication unless
`--inspection-errors allow-scoped` is explicitly selected for a usable template.
