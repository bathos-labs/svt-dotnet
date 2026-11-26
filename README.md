# SVT — Scalable Vector Templates

[![NuGet](https://img.shields.io/nuget/vpre/ScalableVectorTemplates?label=NuGet)](https://www.nuget.org/packages/ScalableVectorTemplates)
[![Validation](https://github.com/svt-project/svt-dotnet/actions/workflows/validate.yml/badge.svg?branch=master)](https://github.com/svt-project/svt-dotnet/actions/workflows/validate.yml)
[![Codecov](https://img.shields.io/codecov/c/github/svt-project/svt-dotnet?branch=master)](https://app.codecov.io/gh/svt-project/svt-dotnet)

Make editable SVG templates with replaceable text, colors and images. SVT keeps your artwork as
ordinary SVG; your editor or viewer handles layout and rendering.

Use the .NET library in an application, or the CLI to inspect a template and apply values from a
JSON file. The packages are release candidates implementing the
[SVT 1.0 specification](https://github.com/svt-project/svt-spec/releases/tag/v1.0.0).

[Template format](#what-does-an-svt-template-look-like) · [Library](#use-the-library) ·
[CLI](#try-it) · [Authoring](#author-templates)

## What does an SVT template look like?

An SVT template is an SVG file with `data-svt-*` attributes that identify replaceable values. Design
the artwork once, then supply text, colors and images to create variations. This
[workshop invitation](examples/quickstart/template.svg) opens in an ordinary SVG viewer with its
authored placeholder content:

![Before binding: a workshop invitation with placeholder title, event details, blue accents and a gray landscape image.](examples/quickstart/template.svg)

These are its binding annotations, with fixed artwork omitted and embedded image data shortened for
readability. Download the linked template for a runnable example:

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="720" height="360"
     viewBox="0 0 720 360" data-svt-version="1.0">
  <!-- Fixed artwork and styling omitted. -->
  <path d="M32 80H104" stroke="#2563eb" data-svt-stroke="accent"/>
  <text x="32" y="145" data-svt-text="heading"
        data-svt-label="Event title">Your event title</text>
  <text x="32" y="182" data-svt-text="details">Date · Time · Location</text>
  <rect x="32" y="260" width="172" height="44" rx="22"
        fill="#2563eb" data-svt-fill="accent"/>
  <image x="410" y="24" width="286" height="312"
         href="data:image/svg+xml;base64,…" data-svt-image="artwork"/>
</svg>
```

The root marker declares the SVT format version. `data-svt-text` names a text input;
`data-svt-image` names an image input. `data-svt-fill` and `data-svt-stroke` share the paint key
`accent`, so one value updates both the button and divider. Optional `data-svt-label` attributes
give inputs display names for an editor or form. Applications discover inputs and supply values by
key, while the SVG defines the artwork and layout.

Supply a title, event details, a green accent and a
[landscape illustration](examples/quickstart/landscape.png) to produce this
[completed invitation](examples/quickstart/bound-preview.svg):

![After binding: a Trail photography workshop invitation for June 12 at 9 AM in Pine Ridge, with green accents and an embedded mountain landscape.](examples/quickstart/bound-preview.svg)

The changed values in the output are:

```xml
<path d="M32 80H104" stroke="#166534" data-svt-stroke="accent"/>
<text x="32" y="145" data-svt-text="heading"
      data-svt-label="Event title">Trail photography</text>
<text x="32" y="182" data-svt-text="details">June 12 · 9 AM · Pine Ridge</text>
<rect x="32" y="260" width="172" height="44" rx="22"
      fill="#166534" data-svt-fill="accent"/>
<image x="410" y="24" width="286" height="312"
       href="data:image/png;base64,…" data-svt-image="artwork"/>
```

Both excerpts abbreviate the image payload; the output contains the replacement image's bytes as a
base64 data URI. Image bindings can embed supplied content or replace a URI reference. The
annotations remain in the output; omitted inputs keep their authored values. See the
[specification](https://github.com/svt-project/svt-spec/blob/master/spec/README.md) for the format
overview and [binding syntax](https://github.com/svt-project/svt-spec/blob/master/spec/bindings.md)
for the complete annotation rules. The walkthrough below applies these same replacements with the
CLI.

## Use the library

The library and CLI share version **1.0.0-rc.3**. Pin the library exactly:

```sh
dotnet add package ScalableVectorTemplates --version '[1.0.0-rc.3]'
```

Start with the [package guide](docs/package-readme.md) for API usage and integration examples. See
the [changelog](CHANGELOG.md) for release history and the
[compatibility policy](docs/compatibility.md) for versioning guarantees. Package versions are
separate from the SVG and JSON format markers.

## Try it

The tool requires the .NET 10 SDK. The current release candidate is **1.0.0-rc.3**:

```sh
dotnet tool install --global ScalableVectorTemplates.Cli --version 1.0.0-rc.3
svt --version
```

For a quick trial, use `dnx ScalableVectorTemplates.Cli@1.0.0-rc.3 -- inspect template.svg`. For
development, use the [source command](CONTRIBUTING.md#local-setup) or validate a
[local package](eng/package/README.md).

Save the [example template](examples/quickstart/template.svg),
[bindings](examples/quickstart/bindings.json) and
[landscape image](examples/quickstart/landscape.png) together, then run from that folder:

```sh
svt inspect template.svg
```

You'll see **Event title** (`heading`, text), **Event details** (`details`, text), **Accent color**
(`accent`, paint) and **Event image** (`artwork`, image), with their existing values. Apply the
[example bindings](examples/quickstart/bindings.json):

```sh
svt bind template.svg --bindings bindings.json --output result.svg
```

Open `result.svg` in a browser or SVG editor. The placeholder becomes the “Trail photography”
invitation shown above, including its embedded landscape image. A successful bind prints nothing.

The binding file contains:

```json
{
  "bindings": [
    { "key": "heading", "kind": "text", "value": "Trail photography" },
    { "key": "details", "kind": "text", "value": "June 12 · 9 AM · Pine Ridge" },
    { "key": "accent", "kind": "paint", "value": "#166534" },
    {
      "key": "artwork",
      "kind": "image_content",
      "file": "landscape.png",
      "media_type": "image/png"
    }
  ]
}
```

The CLI reads `landscape.png` relative to the binding file and embeds its bytes in the output; the
resulting SVG needs no separate image file.

Leave out an assignment to keep its existing value. `{}` keeps all existing values. You can omit
`format_version`; it defaults to 0.

If a command fails:

- **File not found:** check the filename and run from the folder containing the template and
  bindings.
- **Output already exists:** choose a new name or add `--overwrite`. You cannot overwrite an input.
- **Unknown key:** use the exact key shown by `inspect`, rather than its display label.

Add `--help` or `-h` after `inspect` or `bind` for options. Use `inspect --format json` for complete
values and `--diagnostics json` for machine-readable errors on stderr. See the
[CLI reference](docs/cli.md) for configuration and image bindings.

### Use in a project or script

Create a local tool manifest and pin the version:

```sh
dotnet new tool-manifest
dotnet tool install --local ScalableVectorTemplates.Cli --version 1.0.0-rc.3
```

Commit `dotnet-tools.json`. In automation, initialize the SDK and run `dotnet tool restore` as
setup; stop if it fails. Then capture the separate execution step:

```sh
dotnet tool run svt -- inspect template.svg --format json --diagnostics json
```

One-off execution can print SDK setup messages, even with a warm cache. Use the prepared local tool
for JSON or SVG output. SDK failures occur before SVT and do not use its diagnostic format.

Tool updates are explicit: use `dotnet tool update` with `--global` or `--local` and the desired
`--version`. If `svt` is not found, follow the SDK's PATH instructions. If another command uses that
name, check `command -v svt` on macOS/Linux or `Get-Command svt` in PowerShell, then
`svt --version`. A local manifest with `dotnet tool run svt` avoids selecting a global command.

## Author templates

Add binding keys and optional labels to ordinary SVG elements. Start with the
[authoring guide](docs/authoring.md) and [more examples](examples/authoring/README.md), or read the
[specification](https://github.com/svt-project/svt-spec/blob/master/spec/README.md) for the full
contract.

SVT replaces values; it does not render, fetch images, fit text or compute CSS. Applications supply
resources and choose how to display the result. `SVT` is a working format name, not an established
standard.

## Contribute

See [Contributing](CONTRIBUTING.md) for setup and validation, and
[GitHub Issues](https://github.com/svt-project/svt-dotnet/issues) for planned work. The
[architecture](docs/architecture.md) explains the library and CLI boundaries. Use the
[security policy](SECURITY.md) to report a possible vulnerability privately.

Code, specification, documentation and repository-authored examples are [MIT-licensed](LICENSE). See
[NOTICE](NOTICE) and the [release policy](RELEASING.md).
