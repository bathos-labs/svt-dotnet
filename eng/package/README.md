# Package preparation

Prepare the library, CLI tool and their portable symbols as one candidate. Both products share a
version and commit. The CLI includes the exact library DLL/PDB; library consumers do not depend on
the CLI. Preparation and consumption do not publish or need registry credentials.

The [packaging contract](../../docs/tool-distribution.md) defines the two-product manifest,
installation behavior and [draft recovery](../../RELEASING.md#failures-retries-and-evidence).

## Lifecycle

1. Run `task docs:format`, review the diff, then `task check` (or `node eng/check.mjs`). The gate
   preserves documentation, locked restore, formatting, analyzer and test checks and writes a
   receipt containing the Git commit, dirty state and fingerprint of tracked/untracked source files.
   Generated ignored artifacts are excluded. Changing source invalidates the receipt.
2. Use `task package:check` while changes are uncommitted. It prepares a uniquely identified
   `1.0.0-review.g<commit-prefix>.n<nonce>` artifact and consumes it locally. Its manifest records
   the dirty snapshot. Review packages cannot be finalized for handoff.
3. After review and commit, rerun the gate on the clean checkout, then `task package:prepare`. This
   produces `1.0.0-preview.g<commit-prefix>.n<nonce>` with the full source commit and artifact
   hashes. The nonce prevents reuse of a package identity across preparations; existing output is
   never overwritten. These are development versions and cannot be published by the release
   workflow.
4. Run `task package:consume -- <bundle>` on the exact prepared artifact. The optional argument
   defaults to the last successful preparation recorded under `artifacts/`. A separate receipt
   records each platform's result without modifying the bundle.
5. Finalize only after Ubuntu and Windows consume the same artifact:

   ```sh
   node eng/package/finalize.mjs path/to/bundle ubuntu.json windows.json handoff.json
   ```

   The finalizer rejects review packages, failed/missing platform checks, mismatched source
   identity, SDK, package/symbol hashes or bundle-manifest hashes, and an existing output file. Keep
   `handoff.json`, both receipts and the unchanged bundle together. A matching version alone is
   insufficient.

The workflow requires Node 24 and the exact SDK pinned in `global.json`. This tighter artifact check
does not change ordinary solution build roll-forward settings. Source identity is checked before and
after preparation. Run tools against a stable checkout, not while editing files concurrently.

Development packages use the numeric version from `Directory.Build.props`, followed by a unique
`review` or `preview` suffix. For `1.0.0-rc.2`, the examples above keep the same `1.0.0` assembly
version as the portable test hosts. Only tagged release preparation uses the exact shipping package
identity.

## Package inspection

The BCL-only `eng/PackageValidation` executable checks each `.nupkg` and `.snupkg` without loading
shipping assemblies. It checks identity/version/commit, MIT metadata and notices, README, XML API
documentation, the single target framework, and an explicit file allowlist. It rejects dependencies,
native imports, unintended assemblies, unsafe/duplicate archive paths and oversized archives. It
verifies that the portable PDB matches the DLL, source/debug paths are mapped under `/_/`, and
Source Link points to the exact repository commit. Embedded untracked sources are excluded.

The pinned SDK supplies Source Link; no additional library dependency is needed. The SDK omits
authors/license/readme fields from symbol-package metadata, so those are checked on each primary
package. Both artifacts retain their repository identity. Source retrieval uses the public
repository at the recorded commit. Debug information for generated source need not resolve to a Git
file.

These executable checks support a manual rights and private-reference review; they do not claim to
detect every sensitive string. Inspect changed documentation, identifiers and original assets before
distributing a new artifact.

## Isolated consumption

The smoke application is copied outside the checkout with its own project and SDK configuration.
Inherited directory build/central-package imports are disabled. It references one exact package
version, uses only the supplied local feed and a fresh global-packages/HTTP cache, and clears
fallback feeds/folders. A negative restore against an empty feed must fail before the positive
restore. NuGet audit is disabled for this offline, dependency-free smoke application; repository
audits remain on.

Bundle verification cross-checks the embedded gate, inspection and SDK records against the manifest,
in addition to checking every file hash. Consumption receipts identify the entire manifest and
symbol package, so different samples or evidence cannot be combined under a shared package version.
These consistency checks are not signatures or an independent attestation of the producer; retrieve
the bundle and receipts from the trusted validation run and retain their hashes.

The runner verifies the resolved assets contain only the intended package, with no project
references or ambient package folders. It checks restored archive bytes and the loaded library hash,
then executes inspect → prepare values → bind → consume-output checks. Coverage includes reusable
configuration, raw Common/Mixed/Absent defaults, text/paint/image values, omission/empty values,
image and output ownership, structured rejection, stricter policy rechecks, budgets and
cancellation. Optional per-target interpretation checks literal precision, context/reference
classification, bounded data headers, typed limits, source identity and unchanged raw/binding
evidence. The [host-owned editor adapter](../../examples/editor/README.md) is copied into the
isolated consumer and compiled against the package. Its interaction checks preserve omission across
initialization, no edit, cancel and change-and-return; only an accepted change creates a
replacement, and Reset removes it. Completed documents exercise exact UTF-8 sizing, caller-buffer
export, insufficient-capacity preservation, sliced destinations and independent reuse. The
[documented pooled helper](../../docs/binding-api.md#pooled-output-and-downstream-lifetime) is
compiled in this consumer and exercised with a deterministic host pool: only the written prefix
reaches the reader, the loan remains active throughout consumption, and it is returned after success
or a controlled consumer failure before subsequent reuse. It restores again in locked mode before
building and running without restore. Temporary projects and caches are removed; sanitized logs and
a platform receipt stay under ignored artifacts.

The tool runner separately tests global installation, a local manifest and cold/warm one-off
execution. Every mode has its own home and caches, with an empty-feed negative control. It verifies
restored archive/DLL/PDB hashes, help/version, inspect/bind, stdin, paths with spaces, JSON/SVG
streams, exit codes and output protection. SDK setup logs are retained separately from prepared
execution; source-built binaries and ambient global tools cannot satisfy these checks.

This is package-boundary evidence, not a duplicate conformance suite or a native rendering check.

## CI and Ubuntu reproduction

Ubuntu runs the full gate, prepares one clean-commit two-product bundle, consumes it, and uploads
its immutable bundle with the Ubuntu receipt. Windows depends on that job, runs the portable suites,
downloads the same artifact and consumes it with a fresh cache. The Windows job then finalizes the
manifest and uploads a validated-package artifact. Actions are SHA-pinned; no package registry or
secrets are used. Both jobs read the exact SDK version from `global.json` and pass that version to
the installer, without asking it to interpret the ordinary roll-forward policy. Installation uses an
isolated runner directory so a newer preinstalled patch cannot be selected. Candidate transfers
expire after 14 days and completed trial artifacts after 30 days; retain the exact validated files
before expiry. Failed validation cannot create a completed trial manifest.

For local Ubuntu consumption, use the existing digest-pinned validation container after building it
as described in the [native guide](../visual/README.md#ubuntu-container). Supply a bundle created on
the host; the consumer does not rebuild it:

```sh
mkdir -p artifacts/package-ubuntu
docker run --rm --platform linux/amd64 \
  --mount type=bind,source="$PWD",target=/workspace,readonly \
  --mount type=bind,source="$PWD/artifacts/package-ubuntu",target=/workspace/artifacts \
  --mount type=bind,source="$PWD/path/to/bundle",target=/bundle,readonly \
  svt-visual-validation node eng/package/consume.mjs /bundle
```

The development handoff finalizer requires Ubuntu and Windows receipts. The release workflow also
requires macOS consumption of the identical bundle and retains that receipt with native evidence.
Windows results require an actual Windows run; neither macOS nor a Linux container substitutes for
that evidence.

## Handoff scope

The bundle contains the exact packages, inspection and gate reports, provenance/file hashes, a
standalone smoke project, original SVG/binding/image examples, license notices and instructions
linked to the recorded specification/API commit. Review-only links identify the base commit and
cannot retrieve uncommitted source changes.

The manifest carries the historical named-workflow matrix and limitations from #10, links its
completion record, and explicitly distinguishes that earlier evidence from this package candidate.
Raw native reports remain separate and are not included or rerun by this workflow. Retain the
applicable reports with the trial separately, and record consumer integration in the consuming
repository. No image decoding, fetching, CSS repair, fitting, universal rendering or released
compatibility is implied. A candidate manifest does not authorize publication.

## Public releases

The [release workflow](../../RELEASING.md) reuses this pipeline with
`node eng/package/prepare.mjs --release v<version>`. That mode requires an annotated tag, matching
version/changelog, clean source, and eligible ancestry fetched from origin. It uses the declared
version and a `release-candidate` manifest. Review and ordinary development bundles cannot enter the
publishing job. Packaged README and release-note links point to the release tag; development
packages use the source commit. Source Link always uses the commit.

`node eng/package/consume.mjs <bundle> --published` restores the exact release version from
nuget.org into a fresh cache, validates its signature and every original archive entry, runs the
same consumer, and retrieves matching public symbols and Source Link source files. Repository
signing changes the archive hash: the receipt records the distributed hash separately while
requiring identical original contents and loaded assembly bytes. The candidate bundle remains
unchanged. Registry indexing delays fail verification explicitly; rerun against the retained bundle
after availability, without rebuilding or republishing a different artifact. Local consumption
receipts and public-feed receipts are distinct.

## Nonpublishing rehearsal

Install the pinned native tools with `task native:setup`. After preparing a candidate, run:

```sh
task release:rehearse -- path/to/bundle
```

An optional second argument selects the pinned Inkscape executable on macOS. The command consumes
both packages, exercises publication interruption/recovery with in-memory remote substitutes, and
runs the host's native candidate matrix. It writes `artifacts/release-rehearsal.json`. It cannot
create tags, write GitHub releases or obtain NuGet publishing credentials. A local rehearsal records
only the current platform; Ubuntu/Windows consumption and the complete native matrix still require
CI. Visual review remains separate.
