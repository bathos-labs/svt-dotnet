# SVT — Scalable Vector Templates

[![NuGet](https://img.shields.io/nuget/vpre/Bathos.Graphics.SvgTemplates?label=NuGet)](https://www.nuget.org/packages/Bathos.Graphics.SvgTemplates)
[![Validation](https://github.com/bathos-labs/svt-dotnet/actions/workflows/validate.yml/badge.svg?branch=master)](https://github.com/bathos-labs/svt-dotnet/actions/workflows/validate.yml)
[![Codecov](https://img.shields.io/codecov/c/github/bathos-labs/svt-dotnet?branch=master)](https://app.codecov.io/gh/bathos-labs/svt-dotnet)

Make editable SVG templates with replaceable text, colors and images. SVT keeps your artwork as
ordinary SVG; your editor or viewer handles layout and rendering.

Use the .NET library in an application, or the CLI to inspect a template and apply values from a
JSON file. This project is public and prerelease. APIs and the specification may change.

## Try it

The CLI currently runs from source and requires the .NET 10 SDK. It is not included in the published
library package. From a checkout of this repository, build once:

```sh
dotnet build SvgTemplates.slnx -c Release
```

Inspect the [example template](examples/quickstart/template.svg) to see its editable inputs:

```sh
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll inspect examples/quickstart/template.svg
```

You'll see **Heading** (`heading`, text) and **Accent color** (`accent`, paint), with their existing
values. Apply the [example bindings](examples/quickstart/bindings.json):

```sh
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll bind examples/quickstart/template.svg --bindings examples/quickstart/bindings.json --output result.svg
```

Open `result.svg` in a browser or SVG editor. The heading becomes “Hello, SVG!” and the background
changes from blue to purple. A successful bind prints nothing.

The binding file contains:

```json
{
  "bindings": [
    { "key": "heading", "kind": "text", "value": "Hello, SVG!" },
    { "key": "accent", "kind": "paint", "value": "#7c3aed" }
  ]
}
```

Leave out an assignment to keep its existing value. `{}` keeps all existing values. You can omit
`format_version`; it defaults to 0.

If a command fails:

- **File not found:** check the filename and run these examples from the repository root.
- **Output already exists:** choose a new name or add `--overwrite`. You cannot overwrite an input.
- **Unknown key:** use the exact key shown by `inspect`, rather than its display label.

Add `--help` or `-h` after `inspect` or `bind` for options. Use `inspect --format json` for complete
values and `--diagnostics json` for machine-readable errors on stderr. See the
[CLI reference](spec/cli.md) for configuration and image bindings.

## Use the library

The published library beta is **0.1.0-beta.1**. Pin it explicitly:

```sh
dotnet add package Bathos.Graphics.SvgTemplates --version '[0.1.0-beta.1]'
```

Start with the [package guide](docs/package-readme.md). The current source includes unreleased
features; see the [changelog](CHANGELOG.md) before using the latest API documentation with the
published beta. Package versions are separate from the SVG and JSON format markers.

## Author templates

Add binding keys and optional labels to ordinary SVG elements. Start with the
[authoring guide](docs/authoring.md) and [more examples](examples/authoring/README.md), or read the
[specification](spec/README.md) for the full contract.

SVT replaces values; it does not render, fetch images, fit text or compute CSS. Applications supply
resources and choose how to display the result. `SVT` is a working format name, not an established
standard.

## Contribute

See [Contributing](CONTRIBUTING.md) for setup and validation, and
[GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues) for planned work. The
[architecture](docs/architecture.md) explains the library and CLI boundaries.

Code, specification, documentation and repository-authored examples are [MIT-licensed](LICENSE). See
[NOTICE](NOTICE) and the [release policy](RELEASING.md).
