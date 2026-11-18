# Exploratory authoring and rendering probes

These opt-in investigations map capabilities and inform
[authoring guidance](../../docs/authoring.md). Run them when a supported tool, annotation contract
or documented authoring workflow changes, or when investigating a reported problem. They are not
part of the [repeatable release checks](README.md).

## Scripted probes

Use the same pinned setup as the release checks, then invoke the desired investigation:

```sh
task native:probe:render
node eng/visual/probe-authoring.mjs /path/to/Inkscape.app/Contents/MacOS/inkscape
```

The rendering probe compares authored `xml:base` image resolution with an embedded positive control
in Chrome and resvg. Expected resource-resolution failures are observations; crashes, timeouts,
failed controls or cleanup errors fail the probe. Ordinary relative references and relocation remain
release regressions.

The authoring probe exercises the optimized SVG extension separately from native/Plain saves. It
records preserved or changed inventory, or exporter unavailability. It does not establish visual
equivalence, all optimization settings or GUI behavior. Only the observed Inkscape save-failure
diagnostic is classified as unavailable; process failures, timeouts, unexpected diagnostics and
unreadable inspection results fail the probe. Inspect the reason for unavailable exports; a failed
extension is not proof that the file format is unsupported.

Reports go under `artifacts/native-exploratory-probe-*/`, use `purpose: "exploratory-probe"`, and
finish as `completed` when observations were recorded. Exit zero means the investigation completed,
including a documented limitation or unavailable exporter; it is not a release pass. Setup or
unhandled infrastructure failure exits nonzero. Compare new observations with previous evidence and
review meaningful changes before revising guidance or promoting behavior to a release assertion.

## Interactive authoring

UI capability mapping is a separate observed session, performed by a person or an assisted UI
session. It is not an executable GUI regression suite. Use disposable copies and record:

1. Tool version, OS, fonts, source hashes, save format and exact operations. Define expected keys,
   labels, targets and deliberate default changes before inspecting the output.
2. A no-edit save/reopen, then single-line text, paint and geometry edits. Save, close and reopen;
   repeat an edit to establish whether annotation repair is required. Observe whether text gains
   child spans or direct paint gains an overriding inline style.
3. Native and explicit Plain SVG saves separately. Confirm the selected format and serialized
   content; a filename extension does not prove the save mode. Record optimized settings separately.
4. API/CLI inspection and empty/partial/complete bindings against captured files, independent
   expectations and source preservation. Use the release renderer with an independently prepared
   edited corpus when making a visual claim; GUI display alone is not pinned rendering evidence.
5. Findings, unavailable checks and limitations. Preserve useful observations in authoring guidance,
   with screenshots/documents/logs under ignored artifacts. Close only disposable validation
   documents and sessions opened for the investigation; preserve preexisting user work.

GUI failure is unavailable evidence. Interactive observations neither make an automated release
report partial nor replace its checks. Multiline/styled text, arbitrary optimizer options and
unobserved editors remain outside the established workflow until separately investigated.
