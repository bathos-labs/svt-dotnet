# Authoring SVG templates

Use ordinary SVG artwork with `data-svt-version="0"` on its SVG root. Add a stable binding key to
its target and an optional `data-svt-label` for display. See the
[binding contract](../spec/bindings.md) and the
[original examples](../examples/authoring/README.md).

## Keep inputs editable

- Put text hooks on a leaf `text` or `tspan`. Editing tools can insert spans beneath a previously
  leaf text element; inspect again after editing. A parent text hook cannot flatten those spans.
- Put paint values directly on the marked `fill` or `stroke` attribute. A stylesheet or inline style
  can override that attribute. Binding can succeed without visibly changing the paint; SVT does not
  repair CSS precedence.
- Use the same paint key for deliberately shared fill/stroke targets. Different authored values
  remain mixed, and missing direct attributes remain absent even when SVG inheritance supplies a
  visible value. Omission preserves each target independently.
- Keep image geometry, clipping and `preserveAspectRatio` in SVG. Use real embedded image bytes for
  self-contained previews. Relative references need their original effective base location; saving
  output somewhere else does not make SVT rewrite them.
- Keep text within the space appropriate to the design. Long content remains at its authored size;
  no automatic fitting, wrapping or shrinking is provided. Empty text is a replacement, not
  omission.

## Save, reopen and verify

Preserve a source copy. Inkscape SVG and Plain SVG are separate workflows; optimized export needs
its own checks. After saving and reopening, compare the expected keys, labels, kinds, target counts
and raw defaults with discovery. A successful command with no findings does not prove that hooks
survived: a file with all hooks removed may still be a usable template with zero inputs.

Run inspection and exercise empty, partial and complete bindings. Check both the unbound artwork and
replacement output. Compare semantic XML rather than attribute order, namespace-prefix spelling, XML
declaration or indentation. Meaningful text whitespace and authored values remain significant.

Keep structural findings separate from rendering problems. For example, a text hook with child
elements is structurally invalid; an unavailable image reference can remain structurally valid.
Conflicting labels on a shared paint input warn while retaining its first label. Preserve those
findings instead of changing discovery admission to suit one editor or renderer.

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
