# Authoring and rendering examples

These small original SVGs exercise editable text, shared paints, image fitting and layered artwork.
They and the geometric PNG/JPEG illustrations are MIT-licensed under the repository
[LICENSE](../../LICENSE). No external artwork is included. See
[authoring guidance](../../docs/authoring.md).

## Try an example

With the .NET 10 SDK, build from the repository root and inspect the shared-color template:

```sh
dotnet build SvgTemplates.slnx -c Release
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll inspect examples/authoring/shared-palette/input.svg
dotnet src/Bathos.Graphics.SvgTemplates.Cli/bin/Release/net10.0/svt.dll bind examples/authoring/shared-palette/input.svg --bindings examples/authoring/shared-palette/partial.json --output palette.svg
```

Open `palette.svg` in a browser or Inkscape and compare it with
[the expected artwork](shared-palette/partial.svg). Omitted bindings retain their authored values.
Choose a new output name or add `--overwrite` to repeat the command. Start with the
[smaller walkthrough](../../README.md#try-it) if you're new to SVT.

## Example families

| Directory             | Intent                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------- |
| `text-card`           | Leaf text/span inputs, Unicode, literal XML characters, whitespace, empty and long text      |
| `shared-palette`      | Shared fill/stroke, raw defaults, central labels/descriptions and ordered groups             |
| `image-fitting`       | Portrait, landscape and transparent square images; crop and contain; href/XLink              |
| `layered-composition` | Clipping, masking, transforms, opacity and foreground order                                  |
| `hazards`             | Separate expected structural findings, silent hook loss, CSS overrides and missing resources |
| `references`          | Relative resources and authored XML base context, separate from self-contained examples      |

Each positive directory has `input.svg`, an independent `inventory.json`, and empty/partial/complete
binding requests. `partial.svg` and `complete.svg` are independently authored expectations, not
outputs captured from the binder. `suite.json` fixes the initial 480 × 320 viewport and zero pixel
tolerance. The API/CLI test suite checks the inventory and semantic XML against these files.

The first four families are self-contained except for their font. Native checks load the exact
DejaVu Sans font pinned by the [validation tooling](../../eng/visual/README.md). Ordinary viewers
may substitute another installed font. The source assets and their hashes are in `assets/`; embedded
images are the same original bytes. The JPEG was encoded from the landscape PNG.

Hazard JSON specifies template availability, diagnostic codes and admitted keys. Silent hook loss is
deliberately a successful inspection with no inputs; the independent inventory check detects it. CSS
override is deliberately a visually unchanged replacement. Missing-resource rendering must fail the
positive rendering harness without becoming a new library admission rule.

Reference cases preserve their authored strings. The renderer runner also exercises explicit host
preparation when relocating a file. The separate exploratory runner records XML-base support per
renderer. Generated SVGs, screenshots, diffs, tool downloads and logs belong in ignored
`artifacts/`.
