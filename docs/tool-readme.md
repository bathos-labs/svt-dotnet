# SVT command-line tool

Inspect editable SVG templates and apply text, colors and images from a JSON file. Requires the .NET
10 SDK for installation and the .NET 10 runtime to run. The library is included in the tool.

Install this package at the exact version you intend to use:

```sh
dotnet tool install --global Bathos.Graphics.SvgTemplates.Cli --version <version>
svt inspect template.svg
svt bind template.svg --bindings bindings.json --output result.svg
```

Open the result in a browser or SVG editor. Use `svt --version` to check the version, and
`svt inspect --help` or `svt bind --help` for options.

For a project, create a tool manifest with `dotnet new tool-manifest`, install with `--local`
instead of `--global`, and commit the manifest. In automation, initialize the SDK and run
`dotnet tool restore` as a separate setup step. Stop if setup fails, then capture output from
`dotnet tool run svt -- inspect template.svg --format json --diagnostics json`. SDK setup errors are
separate from SVT's JSON diagnostics.

For a quick trial, use `dnx Bathos.Graphics.SvgTemplates.Cli@<version> -- inspect template.svg`.
One-off execution can include SDK installation messages; use a prepared local tool for scripts.

SVT does not render artwork, fetch resources, fit text or compute CSS. Package versions are
independent of SVG and binding-file format markers. This project is prerelease.

See the [quickstart](https://github.com/svt-project/svt-dotnet/blob/master/README.md),
[CLI reference](https://github.com/svt-project/svt-dotnet/blob/master/spec/cli.md), and
[authoring guide](https://github.com/svt-project/svt-dotnet/blob/master/docs/authoring.md).
