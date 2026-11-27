# Authoring SVG templates

Use ordinary SVG artwork with `data-svt-version="1.0"` on its SVG root. Add a stable binding key to
its target and an optional `data-svt-label` for display. See the
[binding contract](https://github.com/svt-project/svt-spec/blob/master/spec/bindings.md) and the
[original examples](../examples/authoring/README.md).

## Try a template first

The [interactive SVT Authoring Guide](https://svt-project.github.io/cookbook/) includes a
[fit-or-crop comparison](https://svt-project.github.io/cookbook/#image-fit), a
[zoomed detail](https://svt-project.github.io/cookbook/#zoom-image-detail), and a
[ticket with a shared text key](https://svt-project.github.io/cookbook/#repeat-a-label). Each
example has a **Download template** link. The same SVG works in the .NET CLI.

### Bind replacement artwork in two frames

With the [.NET 10 CLI tool installed](../README.md#try-it), run these commands in an empty directory
using a POSIX shell and curl. Download the two-frame template and the cookbook’s portrait beacon
PNG. Keep the
[site license](https://github.com/svt-project/svt-project.github.io/blob/master/LICENSE) with
redistributed copies.

```sh
guide=https://svt-project.github.io/cookbook
curl -fsSLo image-fit.svg "$guide/recipes/image-fit.svg"
curl -fsSLo beacon.png "$guide/artwork/beacon.png"
svt inspect image-fit.svg --format json
```

The inventory contains one image input, `artwork`, with two targets. Bind an empty request to see
the authored landscape in both frames. Then assign the beacon once.

```sh
printf '%s\n' '{}' > defaults.json
svt bind image-fit.svg --bindings defaults.json --output default.svg

cat > beacon.json <<'JSON'
{
  "bindings": [
    { "key": "artwork", "kind": "image_content", "file": "beacon.png", "media_type": "image/png" }
  ]
}
JSON
svt bind image-fit.svg --bindings beacon.json --output replacement.svg
```

Open `default.svg` and `replacement.svg` side by side. Omitting `artwork` preserves each original
`href`. The explicit assignment embeds the same beacon bytes in both targets. The left frame shows
the full portrait with space at its sides (`xMidYMid meet`). The right frame fills its bounds by
cropping the top and bottom (`xMidYMid slice`). Both files can be opened without the companion PNG.
Use fresh output names if you repeat the commands.

For a comparison across browser engines and the limits of that evidence, see
[Checking a template](https://svt-project.github.io/cookbook/#renderer-notes).

### Reuse the image binding or change a shared ticket number

In the same directory and shell, the zoom template accepts the same `artwork` assignment. Its two
views reference one image in `<defs>`. The ticket instead exposes a text input named `ticket` on two
separate elements.

```sh
curl -fsSLo zoom.svg "$guide/recipes/zoom-image-detail.svg"
svt bind zoom.svg --bindings beacon.json --output beacon-detail.svg

curl -fsSLo ticket.svg "$guide/recipes/repeat-a-label.svg"
svt inspect ticket.svg
printf '%s\n' '{"bindings":[{"key":"ticket","kind":"text","value":"FM-0091"}]}' > ticket.json
svt bind ticket.svg --bindings ticket.json --output numbered-ticket.svg
```

Open `beacon-detail.svg` to compare the whole image and the enlarged region, or
`numbered-ticket.svg` to see `FM-0091` on both the passenger ticket and the claim stub.

The [local authoring examples](../examples/authoring/README.md#try-an-example) include more image
and shared-paint bindings.

## Keep inputs editable

- Put text hooks on a leaf `text` or `tspan`. Editing tools can insert spans beneath a previously
  leaf text element; inspect again after editing. A parent text hook cannot flatten those spans.
- Put paint values directly on the marked `fill` or `stroke` attribute. A stylesheet or inline style
  can override that attribute. Binding can succeed without visibly changing the paint; SVT does not
  repair CSS precedence.
- Reuse a key for any number of targets of the same kind. For example, two `data-svt-text="title"`
  hooks display one title in different positions. Two `data-svt-image="photo"` hooks can display the
  same replacement with different crops. A paint key can appear on both fill and stroke. Different
  authored values remain mixed, and missing direct attributes remain absent even when SVG
  inheritance supplies a visible value. Omission preserves each target independently.
- Keep image geometry, clipping and `preserveAspectRatio` in SVG. Use real embedded image bytes for
  self-contained previews. Relative references need their original effective base location; saving
  output somewhere else does not make SVT rewrite them.
- Keep text within the space appropriate to the design. Long content remains at its authored size;
  no automatic fitting, wrapping or shrinking is provided. Empty text is a replacement, not
  omission.

## Polish the input list

Use optional
[central metadata](https://github.com/svt-project/svt-spec/blob/master/spec/metadata.md) for logical
input labels, plain-text help, ordering and one level of groups. Place it in the specified RDF/XML
document description with `rdf:about=""` and `rdf:parseType="Literal"`; the envelope gives the input
declarations a document-metadata meaning. Keep binding keys stable. Central labels override inline
labels; inputs without a central label retain the first target's inline label or their key. Use
spaced orders such as 10 and 20 to allow later insertion. Ungrouped inputs remain visible in a final
unnamed section. These hints describe presentation, not required values or binding constraints.

The [shared-palette example](../examples/authoring/shared-palette/input.svg) demonstrates this
polish without changing the artwork or raw defaults. SVG metadata and embedded samples are
nonbinding content; keep actual target hooks on artwork outside metadata.

## Save, reopen and verify

Preserve a source copy. Inkscape SVG and Plain SVG are separate workflows; optimized export needs
its own checks. After saving and reopening, compare the expected keys, labels, kinds, target counts
and raw defaults with discovery. Also compare descriptions, label provenance, group membership and
presentation order when metadata is present. A successful command with no findings does not prove
that hooks survived: a file with all hooks removed may still be a usable template with zero inputs.

Run inspection and exercise empty, partial and complete bindings. Check both the unbound artwork and
replacement output. Compare semantic XML rather than attribute order, namespace-prefix spelling, XML
declaration or indentation. Meaningful text whitespace and authored values remain significant.

Keep structural findings separate from rendering problems. For example, a text hook with child
elements is structurally invalid; an unavailable image reference can remain structurally valid.
Without a central label override, conflicting inline labels on a shared input warn while retaining
its first label. Preserve those findings instead of changing discovery admission to suit one editor
or renderer.

## Exploratory observations

These observations came from capability mapping, not a recurring GUI regression suite. Repeat the
[interactive procedure](../eng/visual/probes.md#interactive-authoring) when the supported editor,
annotation contract or authoring guidance changes.

Inkscape 1.4.4 on macOS preserved the text-card's leaf hook through repeated single-line text-tool
edits and native save/reopen cycles. Moving that object also preserved its bindings. An explicit
Plain SVG export of the edited text card also retained its leaf hooks after reopening. This evidence
covers the named operations; multiline or styled edits still need reinspection.

The Fill and Stroke panel wrote the edited color to inline CSS while retaining the original marked
`fill` attribute. Its visible color therefore stopped following that binding. For templates with
replaceable paint, edit the direct attribute in the XML editor or prepare the direct attributes
after visual design. Do not assume a color-panel edit updates a binding's authored default.

Optimized SVG export with the observed default settings preserved hooks in the shared-palette
example, but converted CSS to attributes and shortened a raw color literal. Those changes alter
SVT's default evidence even when the picture stays the same. Treat optimized output as a separately
reviewed template, not a preservation guarantee. The broader native and Plain SVG command-line round
trips remain separate from these interactive observations.

## Validation workflow

The [native validation tools](../eng/visual/README.md) produce a per-workflow report, captured SVGs,
rendered images and pixel differences outside source control. Their scope is explicit:

| Workflow                                                         | Purpose and cadence                                                          |
| ---------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| API/CLI fixtures and harness unit tests                          | Portable gate on every PR and release                                        |
| Inkscape CLI native/Plain save/reopen and direct-attribute edits | Repeatable release checks on macOS for each candidate                        |
| Chrome/resvg source and bound output comparisons                 | Repeatable release checks on macOS and Ubuntu for each candidate             |
| Interactive editing, optimized export and XML-base support       | Explicit exploratory probes when relevant tools or contracts change          |
| Cross-renderer review                                            | Human review of candidate images for content, fitting, clipping and stacking |

Release checks report passed or failed independently of exploratory observations. Probes record
capabilities and limitations without granting release approval. Known CSS overrides and missing
resources remain asserted regression cases; the unresolved XML-base behavior is a separate probe.

Use pinned tools, fonts and resources. Missing assets, font failures and incomplete capture cannot
be accepted as a positive visual baseline. A tolerance needs case-specific justification before
acceptance; changing the threshold after a failure is not evidence of correctness. Report exact
versions, platform and limitations with each run. The tools do not certify every SVG editor or
renderer, and native evidence does not replace the portable repository gate.
