# SVT — Scalable Vector Templates

A portable SVG templating specification and .NET library by Bathos Labs.

Templates remain ordinary SVG files with useful authored previews. Binding annotations identify
replaceable text, images and paint values; SVG retains ownership of layout and rendering.

This private, unreleased repository implements portable structural inspection and atomic
substitution with conformance tests and an [inspection and binding CLI](spec/cli.md). `SVT` is a
working format name, not an established standard.

## Boundaries

- `Bathos.Graphics.SvgTemplates`: one portable library for structural inspection and XML-safe
  substitution.
- A thin CLI exposes the same rules to external authors and agents.
- The specification and conformance fixtures remain independent of .NET.
- Consumers own acquisition, image preparation, rendering and application document state.

See [architecture](docs/architecture.md), [specification](spec/README.md),
[inspection API](docs/inspection-api.md), [conformance](conformance/README.md), and
[authoring guidance](docs/authoring.md). Design decisions and delivery work belong in
[GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

## Publication

The package name is `Bathos.Graphics.SvgTemplates`. Code, specification, documentation and
repository-authored examples are [MIT-licensed](LICENSE). See [NOTICE](NOTICE) and
[release readiness](RELEASING.md). The repository remains private and no package release is
authorized yet. [Private package preparation](eng/package/README.md) builds inspected artifacts and
validates isolated consumption without registry publication. The
[package README](docs/package-readme.md) describes the public API lifecycle.

## Development

The primary branch is `master`. See [Contributing](CONTRIBUTING.md) for the documentation validation
gate and working conventions. The .NET 10 SDK and common build policy are pinned for the first
implementation; CI checks documentation, locked restore, C# formatting, Release analyzers and tests.
With Node.js 24 and Task installed, run `task docs:format` after edits and `task check` before
committing. The solution is `SvgTemplates.slnx`.

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
