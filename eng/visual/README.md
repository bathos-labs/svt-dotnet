# Native release validation

This validation-only tooling exercises the [original examples](../../examples/authoring/README.md)
through pinned Inkscape, Chrome and resvg. It adds no production dependency. `task check` runs the
portable API/CLI examples and harness unit tests on every PR; it does not launch native tools.

## Lifecycle and results

| Command                                         | Purpose                                                            | Result                           |
| ----------------------------------------------- | ------------------------------------------------------------------ | -------------------------------- |
| `task native:check`                             | Harness unit tests, also included in `task check`                  | Test pass/fail                   |
| `task native:setup`                             | Install and verify pinned rendering tools                          | Setup success/failure            |
| `task native:release:authoring -- <executable>` | Repeatable native/Plain SVG round trips and direct-attribute edits | Release check passed/failed      |
| `task native:release:render`                    | Repeatable source/binding, resource and pixel checks               | Release check passed/failed      |
| `task native:probe:authoring -- <executable>`   | Explore optimized export                                           | Observations, no release verdict |
| `task native:probe:render`                      | Explore authored XML-base support                                  | Observations, no release verdict |

Run release checks for every release candidate and relevant implementation, fixture or tool changes.
Run [exploratory probes](probes.md) when investigating capabilities or changing supported tools and
authoring guidance. Interactive sessions are a documented manual procedure, not unattended release
automation. A completed probe never substitutes for a passing release check.

Release reports use `purpose: "release-check"` and `status: "passed"` or `"failed"`; assertion,
resource, pixel and cleanup failures produce a nonzero exit. They do not wait for GUI evidence or
report partial merely because a probe was not run. Report startup/setup failures as unavailable
checks if no report can be produced. Each command proves only its declared scope; collect the full
candidate matrix before assessing readiness.

## Tools and provenance

`package.json` and its lockfile pin Puppeteer 25.18.1 and pngjs 7.0.0. `tools.json` pins Chrome
157.0.8103.78, resvg 0.47.0 and DejaVu Sans 2.37, plus the Inkscape 1.4.4 macOS ARM64 download.
Setup verifies resvg/font archive hashes and the extracted font hash. Reports record executable
hashes, versions, source/example hashes, Git commit and whether the tree is dirty. Preserve the
complete upstream font license copied beside the downloaded font; the font is not covered by this
repository's MIT license.

Rendering setup supports macOS ARM64 and Linux x64. Chrome is installed by the pinned Puppeteer
installer and its version is checked before capture. resvg disables system fonts; Chrome loads the
supplied DejaVu Sans font with `@font-face`. Font readiness, image decode and process completion are
awaited, with bounded watchdogs rather than sleeps. Both rendering entry points share this capture
and cleanup code in `rendering.mjs`.

## Release candidate checks

Build and run the portable gate, then install rendering tools:

```sh
task check
task native:setup
task native:release:render
```

Use the official Inkscape download pinned in `tools.json`, verifying its archive hash before
installation or extraction. No global application installation is required:

```sh
node eng/visual/release-authoring.mjs /path/to/Inkscape.app/Contents/MacOS/inkscape
```

The authoring runner creates native and Plain SVG corpora, opens/exports every original and expected
variant, then reopens/exports in a fresh process. It compares independent inventories (including
label provenance, descriptions, orders and group membership), runs API/CLI binding assertions and
checks geometry/direct-fill actions in both save modes. Each deliberate edit also gets empty,
partial and complete API/CLI binding checks against independent expected artwork; the filtered
runner requires at least one executed test. Optimized export and interactive editing are excluded
from this command. It waits for its child processes to exit and does not create an interactive
editing session or close a user's existing editor windows.

The shared-palette template carries its SVT declarations in the specified RDF/XML document metadata
envelope. Both save modes must preserve its resolved fields and groups through reopening and
direct-paint edits. The runner records stderr per operation and completes preservation checks, but
any tool diagnostics still fail the authoring release verdict. Passing rendering or preservation
checks do not override that verdict; no warning suppression is applied.

Use the printed artifact directory to render both saved corpora against the pre-save examples:

```sh
node eng/visual/release-render.mjs artifacts/native-release-check-authoring-example/native examples/authoring
node eng/visual/release-render.mjs artifacts/native-release-check-authoring-example/plain examples/authoring
```

The example directory above is illustrative. Original, empty, partial and complete states run in
both engines. Repeatability, decoded RGBA samples and dimensions use zero tolerance; replacement
artwork is independently authored. Relative resources and host-prepared relocation must render. CSS
overrides must remain visibly unchanged and missing resources must be rejected. The saved-corpus
commands also require exact same-engine before/after-save equality. The original corpus has 38
comparisons; each saved corpus has 46. No cross-engine pixel equality is asserted.

The authoring runner also emits `native-geometry`, `native-paint`, `plain-geometry` and
`plain-paint` corpora. Run the release renderer on each without a pre-save baseline to check the
deliberately changed artwork (14 comparisons each). Reports list their actual cases; subset runs
supplement the four-family candidate matrix and cannot replace it. Empty or duplicate suites are
rejected.

Review complete-state images for content, fitting, clipping, masking and stacking. Retain the
reports, captured documents and PNGs under `artifacts/native-release-check-*/`, together with the
portable gate result. A tolerance requires case-specific review before acceptance.

## Ubuntu container

The Dockerfile pins .NET SDK Ubuntu and Node images by digest and installs Chrome runtime libraries.
Reports record actual installed package versions. Chrome's sandbox is disabled inside this
disposable trusted-fixture container; host execution retains its default sandbox.

After building the portable binaries:

```sh
docker build --platform linux/amd64 -f eng/visual/Dockerfile -t svt-visual-validation .
mkdir -p artifacts/ubuntu
docker run --rm --platform linux/amd64 \
  --mount type=bind,source="$PWD",target=/workspace,readonly \
  --mount type=bind,source="$PWD/artifacts/ubuntu",target=/workspace/artifacts \
  -v /workspace/eng/visual/node_modules svt-visual-validation \
  sh -c 'git config --global --add safe.directory /workspace && node eng/visual/setup.mjs && node eng/visual/release-render.mjs'
```

Source is read-only; tools/evidence and locked Node dependencies use separate mounts. Record the
built image ID. Container evidence does not claim hosted CI or Windows rendering coverage.

## Evidence handling

Keep generated SVGs, screenshots, diffs, tools and logs outside source control. Share evidence with
its exact source revision and distinguish dirty-tree runs from committed builds. Check licenses,
asset hashes and private references before transfer. Exploratory observations remain separate from
release results and portable conformance. Package consumption and application integration require
their own evidence.
