# Private package preparation

Prepare the library and portable symbols for an isolated consumer trial. Only
`Bathos.Graphics.SvgTemplates` is packable. The CLI, tests, package inspector and native tools are
not included in the package. No registry publication or release credentials are involved.

## Lifecycle

1. Run `task docs:format`, review the diff, then `task check` (or `node eng/check.mjs`). The gate
   preserves documentation, locked restore, formatting, analyzer and test checks and writes a
   receipt containing the Git commit, dirty state and fingerprint of tracked/untracked source files.
   Generated ignored artifacts are excluded. Changing source invalidates the receipt.
2. Use `task package:check` while changes are uncommitted. It prepares a uniquely identified
   `0.1.0-review.g<commit-prefix>.n<nonce>` artifact and consumes it locally. Its manifest records
   the dirty snapshot. Review packages cannot be finalized for handoff.
3. After review and commit, rerun the gate on the clean checkout, then `task package:prepare`. This
   produces `0.1.0-preview.g<commit-prefix>.n<nonce>` with the full source commit and artifact
   hashes. The nonce prevents reuse of a package identity across preparations; existing output is
   never overwritten. These are private development versions, not an agreed public release version.
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

## Package inspection

The BCL-only `eng/PackageValidation` executable checks the `.nupkg` and `.snupkg` without loading
the library. It checks identity/version/commit, MIT metadata and notices, README, XML API
documentation, the single target framework, and an explicit file allowlist. It rejects dependencies,
native imports, unintended assemblies, unsafe/duplicate archive paths and oversized archives. It
verifies that the portable PDB matches the DLL, source/debug paths are mapped under `/_/`, and
Source Link points to the exact repository commit. Embedded untracked sources are excluded.

The pinned SDK supplies Source Link; no additional library dependency is needed. The SDK omits
authors/license/readme fields from symbol-package metadata, so those are checked on the library
package. Both artifacts retain their repository identity. Source retrieval requires repository
access while development is private. Debug information for generated source need not resolve to a
Git file.

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
cancellation. It restores again in locked mode before building and running without restore.
Temporary projects and caches are removed; sanitized logs and a platform receipt stay under ignored
artifacts.

This is package-boundary evidence, not a duplicate conformance suite or a native rendering check.

## CI and Ubuntu reproduction

Ubuntu runs the full gate, prepares one clean-commit package, consumes it, and uploads its immutable
bundle with the Ubuntu receipt. Windows depends on that job, runs the portable suites, downloads the
same artifact and consumes it with a fresh cache. The Windows job then finalizes the manifest and
uploads a private-trial artifact. Actions are SHA-pinned; no package registry or secrets are used.
Both jobs read the exact SDK version from `global.json` and pass that version to the installer,
without asking it to interpret the ordinary roll-forward policy. Installation uses an isolated
runner directory so a newer preinstalled patch cannot be selected. Candidate transfers expire after
14 days and completed trial artifacts after 30 days; retain the exact validated files privately
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

macOS receipts are supplementary. Windows results require an actual Windows run; neither macOS nor a
Linux container substitutes for that evidence.

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
compatibility is implied. A private trial manifest does not authorize public publication.
