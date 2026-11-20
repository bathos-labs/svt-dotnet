# CLI packaging and release contract

The library and CLI are packaged separately. The installation examples below describe the planned
`1.0.0-beta.1` release, not an available tool package or authorization to publish it. The
[CLI contract](cli.md) continues to own commands, diagnostics, streams and exit codes.

## Release unit

One release contains two independently installable packages at one version and source commit:

| Role    | Package                            | Purpose                      |
| ------- | ---------------------------------- | ---------------------------- |
| Library | `Bathos.Graphics.SvgTemplates`     | Application API              |
| CLI     | `Bathos.Graphics.SvgTemplates.Cli` | .NET tool with command `svt` |

Reuse the existing projects, library and binding lifecycle. The CLI package contains its runtime
closure, including the exact library DLL shipped in the library package. Installing the CLI does not
require separately installing the library. Check DLL and matching portable PDB identity across the
two products; sharing a version string is insufficient. Keep the library package independently
consumable, with its existing BCL-only boundary and no dependency on the CLI.

Each product has a `.nupkg` and a matching `.snupkg`, with portable symbols and Source Link for its
managed assemblies. The tool includes the entry point, tool metadata, runtime/dependency files,
required assemblies, matching portable PDBs, license/notices and its own package README. Inspect an
explicit file allowlist and reject unintended tests, engineering tools, native assets or source-tree
paths. Package documentation points to the corresponding release tag; Source Link points to the
exact source commit. Development packages use their recorded source identity.

The tool is a portable, framework-dependent `net10.0` package. Document the .NET 10 SDK for
supported installation and execution workflows; the executable needs the .NET 10 runtime. Consumers
do not need the repository's exact SDK patch, while candidate preparation and validation use its
pinned SDK. Do not enable automatic major-runtime roll-forward or bundle a runtime. Standalone
native executables and platform-specific downloads remain separate work.

Both products use the version owned by `Directory.Build.props`. Package versions remain independent
of SVG/JSON format markers. Preserve the
[release version policy](../RELEASING.md#version-and-branch-policy), including its exclusion of
build metadata from published identities. The CLI's general version formatter still preserves
genuine build metadata for nonrelease builds.

## Installation and execution

Support all three SDK installation paths. Use explicit versions in examples and automation. A
missing requested version fails; it must not silently select another version. Updates are explicit,
with no SVT auto-update behavior.

For regular personal use:

```sh
dotnet tool install --global Bathos.Graphics.SvgTemplates.Cli --version 1.0.0-beta.1
svt inspect template.svg
```

Use the SDK's normal tool directory and PATH setup. Do not add a custom installer, modify shell
profiles or overwrite an unrelated command. Document how to confirm the selected executable and
version if `svt` is already present on PATH.

For a project, create a manifest deliberately and commit it:

```sh
dotnet new tool-manifest
dotnet tool install --local Bathos.Graphics.SvgTemplates.Cli --version 1.0.0-beta.1
```

Restore and run from the project's trusted directory. The manifest selects the exact tool version;
use `dotnet tool run svt` to select the local tool rather than a global PATH command.

For a quick trial:

```sh
dnx Bathos.Graphics.SvgTemplates.Cli@1.0.0-beta.1 -- inspect template.svg
```

The equivalent SDK entry point is `dotnet tool exec`. One-off execution may acquire packages and
emit SDK/NuGet output. Do not promise a clean data stream on either a cold or a warm invocation
across SDK versions and environments. SDK confirmation flags belong to the installed SDK, not SVT;
do not require flags unavailable on the supported .NET 10 SDK.

### Automation output

The recommended automation path separates setup from execution:

```sh
dotnet tool restore
dotnet tool run svt -- inspect template.svg --format json --diagnostics json
```

These are separate steps: stop if restore fails, and capture only the execution step as SVT output.
Initialize the SDK and restore the tool before starting data capture, using the same prepared
SDK/home/cache context. Restore logs are setup evidence, not JSON or SVG. For isolated jobs,
`--tool-path` installation and direct invocation of its shim are also suitable prepared execution.

Once SVT starts, preserve its stdin, argument boundaries, working directory, stdout/stderr and exit
status. Pass tool arguments after `--` when using an SDK wrapper. Keep relative paths relative to
the caller's working directory. The installed tool adds no startup banner, package lookup, implicit
restore or output rewriting of its own.

SDK startup, missing-tool/runtime, restore and wrapper failures may occur before SVT starts. They
remain host failures: require nonzero status, but do not claim an SVT exit category or diagnostic
JSON envelope. Never hide them, strip guessed log prefixes or mix setup output into the data stream.
A quiet flag is not a portable guarantee. As with the existing CLI, shell redirection can create or
truncate a destination before execution; use named `--output` for SVT file-publication guarantees.

### Contributor invocation

Provide `task svt -- inspect template.svg` and document the direct SDK equivalent. The task runs the
source project, preserves arguments including spaces, and makes build/execution failures observable.
Task and build messages are development output; the task is not the machine-output interface.
Document Task's exit-status behavior rather than claiming that every wrapper preserves exact SVT
exit codes. Prepared installed execution owns the automation examples.

## Candidate preparation and validation

Extend the existing candidate and inspector model with two required, named products: `library` and
`cli`. Record their package IDs, shared version, `.nupkg`/`.snupkg` paths and SHA-256 hashes,
assembly and PDB identities, and inspection results. A common manifest binds both products to the
same source commit/fingerprint, SDK, target framework and bundle inventory. Update the
in-development manifest and receipts directly; do not introduce legacy readers or migration paths.

Build the release set once. Both products must pass inspection before either can be finalized.
Validate tool metadata, entry point, runtime closure, license/notices, documentation and provenance;
verify the bundled library and PDB against the library product. The installed `--version` must match
the package version and recorded revision, including prerelease text and the abbreviated commit.
Reject missing, substituted, inconsistent or stale artifacts and evidence.

Ubuntu and Windows consume the same retained bundle outside the checkout, with isolated homes,
caches and feeds. Require the requested package to be unavailable in a negative feed control before
adding the candidate feed. Exercise library-only consumption independently. For the CLI, test
one-off execution, global installation, local manifest installation/restore, and prepared execution.
Do not resolve source project outputs or an already-installed tool accidentally.

Check help/version, inspect/bind, omitted/default values, filenames with spaces, stdin, nonzero
exits, JSON/SVG redirection and working-directory semantics. Verify restored package and loaded
assembly identity, both cold and warm wrapper behavior, and missing-tool/runtime or acquisition
failures. Installed SVT tests must preserve full JSON evidence and the publication protections in
[the CLI contract](cli.md). Keep the source-built walkthrough and installed-tool walkthrough clearly
distinguished until package validation is complete.

Native candidate checks use the packaged CLI and matching library bytes. Test hosts may still be
built for validation, but their runtime library must be the candidate's. Bind native reports and
review images to the same candidate identity; rebuilding a shipping binary cannot supply this proof.
Portable conformance, native rendering and external application acceptance remain separate.

## Draft-release recovery

A release candidate is immutable by policy. A GitHub draft is storage, not proof of immutability or
publication approval. Before the first NuGet upload, the protected publication job must retain and
read back the exact candidate, portable/platform/native evidence and review images in a draft GitHub
release attached to the existing annotated tag. Verify the archived inventory and hashes against the
approved candidate before proceeding.

Record the repository, tag/commit, originating validation run and candidate manifest hash as the
recovery identity. Resume must select that original identity explicitly and validate its approval
and evidence; it must not infer authorization from the presence of a draft. Missing or unverifiable
original identity is a failure. Review/development bundles cannot be promoted to release candidates.

Keep candidate assets separate from per-attempt publication reports. Never replace candidate assets
or their validation receipts. If draft upload is interrupted before any NuGet write, an exact retry
may add missing assets; an existing asset must match. Once the complete archive is verified, retries
may add new attempt reports but may not repair an incomplete or changed candidate by rebuilding it.
Treat missing, altered or contradictory retained evidence as a failure. Preserve matching retained
bytes only; never recreate missing shipping artifacts under the same published identity.

Use one publication lock per release tag across initial runs and recovery runs. Do not cancel an
active publisher or allow a recovery attempt to publish concurrently. Recheck remote tag/commit,
eligible branch ancestry, candidate identity and retained receipts on every attempt. Keep GitHub
release write permission and NuGet OIDC credentials confined to protected release jobs. Confirm
package ownership and publishing permissions for both IDs before the first upload.

Ordinary workflow dispatch remains a nonpublishing rehearsal. Recovery requires an explicit mode on
the existing release tag, selecting the retained candidate identity and entering the same `nuget`
approval environment. It must skip shipping-binary preparation and reuse the original validated
bundle. Re-running only a failed publication job is also valid if it retains that exact identity and
evidence. Do not rerun successful preparation, move a tag or choose a new candidate to finish an
existing release.

The draft remains unpublished until the release set passes public verification. If publishing the
GitHub release fails after NuGet verification, retry that finalization with the same assets and
recorded verification; do not rebuild or repeat successful NuGet uploads. If the release is already
published, verify its identity and completion evidence before reporting success.

## Publication and partial completion

After protected approval, revalidate the retained candidate and preflight every artifact that is
already present in the registry. A known mismatch blocks all further uploads, including those for
another product. Upload missing artifacts in a fixed order: library `.nupkg`, CLI `.nupkg`, library
`.snupkg`, CLI `.snupkg`. Suppress implicit symbol uploads when pushing primary packages so each
operation has one recorded outcome. Both products remain one release even though uploads are not
atomic.

| Observed state                                                 | Required action                                                                             |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Package absent, with no unresolved earlier upload              | Upload the retained artifact and record the outcome.                                        |
| Existing package matches                                       | Verify its repository signature and original contents; record verification and skip upload. |
| Upload accepted, timed out ambiguously, or indexing incomplete | Record an unresolved state and reconcile availability. Do not infer absence or completion.  |
| Existing package or public PDB differs                         | Stop before further uploads. Changed binaries require a new version and tag for the pair.   |
| Registry state cannot be established                           | Stop visibly. Authentication, transport and service failures are not evidence of absence.   |

Persist per-attempt outcomes independently for each package and symbol artifact, bound to the
candidate manifest hash. A local journal helps recovery but is not registry truth: reconcile it with
remote evidence on every attempt. A duplicate response is not proof of identity. `--skip-duplicate`,
if used, is only a transport aid; public verification remains mandatory.

NuGet repository signing changes archive bytes. Verify the signature and compare every original
package entry against the unsigned candidate, recording the distributed hash separately. Confirm
public PDB bytes by their debug identity/checksum and validate Source Link source retrieval. Shared
library PDB identity must remain consistent across both products. Symbol-server 404 responses do not
establish that a symbol upload never occurred.

Bound network calls and verification attempts. Report unavailable or indexing-pending artifacts as
incomplete with a nonzero result and retained diagnostics; elapsed time is not success. If an
upload's outcome is uncertain, reconcile first. A controlled retry may resend only the identical
retained bytes, and any conflict still requires identity verification. Never substitute a newly
built artifact. Unlisting is not an automatic rollback for consumers who already restored.

Public completion requires fresh-cache library consumption and installed-tool execution from
nuget.org, verified original contents and assembly identities for both packages, matching public
symbols and Source Link, and retained verification receipts. Then publish the existing draft with
the correct prerelease flag. If the original candidate or its validation identity cannot be
recovered, stop; a corrected release needs a new version for both products. Tag creation and actual
registry publication still require explicit authorization.

## Acceptance evidence

Implementation must pass the full gate, package inspection, isolated Ubuntu/Windows consumption, and
the installed quickstart. Exercise the nonpublishing release rehearsal with both products and the
pinned native matrix, recording actual environments and separate visual review.

Test recovery without registry writes: inject interruption after each asset-upload, package-upload,
symbol-upload and verification boundary. Cover partial draft archival, duplicate/conflicting
artifacts, delayed indexing, ambiguous timeouts, authentication/service failures, missing or altered
candidate evidence, wrong tags/identities, concurrent attempts and failed final GitHub publication.
Verify retries reuse the candidate, preserve completed work, and never report a partial release as
complete. The rehearsal uses controlled substitutes for remote mutation and cannot obtain NuGet
publishing credentials, create release tags or publish a GitHub release.

Do not add documentation-wording regression tests. Specification approval, successful rehearsal and
package validation do not themselves authorize a public release.
