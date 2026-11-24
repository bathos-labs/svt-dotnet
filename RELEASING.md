# Releasing

SVT is public, MIT-licensed, and prerelease. `1.0.0-rc.1` is published for both the library and CLI.
`0.1.0-beta.1` is the earlier library release. A version in an issue, contract or changelog does not
imply publication. Consumers should pin an exact published version and validate their integration.
External RC package integration is accepted with no reported issues. Keep `1.0.0-rc.1` while
production-template validation continues. The surface is provisionally frozen; necessary shipping
corrections require a new RC and renewed consumer validation. Formal stable guarantees begin at
`1.0.0`, not at RC acceptance. SVG and JSON format markers remain version 0 independently of NuGet.

## Release set

One release contains `Bathos.Graphics.SvgTemplates` and `Bathos.Graphics.SvgTemplates.Cli` at one
version/commit, with symbols for both. The CLI bundles the same library DLL/PDB. Its first published
release is `1.0.0-rc.1`. See the [packaging contract](spec/tool-distribution.md).

Archive and verify the candidate and its portable/platform/native evidence in a draft GitHub release
before the first NuGet upload. Publish that draft only after both products pass public consumption,
signature/content, symbols and Source Link checks. A draft stores evidence; it does not authorize
publication.

## Version and branch policy

Follow the required [Semantic Versioning and compatibility policy](spec/compatibility.md). Classify
changes by their effect on the public contract: compatible fixes are patches, compatible additions
and deprecations are minor revisions, and incompatible changes are major revisions. Use the highest
classification required by either package. Review behavior, defaults and machine output as well as
API signatures; diff size and labels such as "small fix" do not determine the revision.

`Directory.Build.props` owns the shipping version. [CHANGELOG.md](CHANGELOG.md) must have one
`Unreleased` section and a dated, populated entry for that version as its latest release. Use
canonical three-part versions with optional lowercase SemVer prerelease identifiers. Build metadata,
leading-zero aliases, and development `dev`, `review`, or `preview` identities are not release
versions.

Through the first stable 1.0 release, `master` is the active release branch for prereleases and the
stable release. Integrate reviewed work there using feature branches as needed; there is no separate
prerelease or maintenance line to protect. Revisit branch arrangements when the first stable release
is established. Published packages and historical tags remain immutable.

The existing release guard permits stable or prerelease versions on `master`, and prereleases only
on `release/*`; that latter capability is not needed for the current development process. Feature
branches can run ordinary validation but cannot prepare a release tag. An annotated `v<version>` tag
must identify the exact checked-out commit. Publication refreshes actual branch refs from origin and
requires that commit to be an ancestor of an eligible remote branch. Stale local tracking refs,
unpushed commits, failed fetches, dirty trees, and mismatched metadata fail closed.

## Stable acceptance and support

RC integration acceptance and production-template acceptance are separate. Keep the shipping version
and installation examples at `1.0.0-rc.1` until production-template validation is complete. Record
consumer-specific evidence in the consumer's tracking and a generic outcome in issue #1.

Before preparing stable `1.0.0`, complete production-template acceptance and review the accepted
surface against the [implemented API/package baseline](eng/compatibility/README.md), then update
version, changelog and prerelease guidance. Keep the published RC baseline pin through stable
preparation; promote it only after public verification of 1.0. Update AGENTS.md to establish the
released-project compatibility requirement at that transition. Repeat the full gate, allocation
checks, isolated three-platform consumption, native visual review and nonpublishing rehearsal on the
final candidate. Stable publication still requires explicit authorization; this policy does not
create a tag or authorize a release.

After 1.0, maintain the latest stable minor release. Fixes normally go into that line; consumers may
need to upgrade within 1.x. There is no promise of backports to older minors, simultaneous support
for multiple majors, indefinite maintenance or response times. Announce any future support
retirement explicitly. Older prereleases have no separate maintenance line. Keep SECURITY.md and the
compatibility policy aligned with the actual support status.

## Local preparation without publication

Use a feature branch to update the shipping version, dated changelog and consumer guidance. For an
uncommitted review on macOS ARM64 or Linux x64, run these commands from the repository root:

```sh
task docs:format
# Review the diff before validation.
task check
task perf:export
task native:setup
task package:check
CANDIDATE_BUNDLE=$(node -p "JSON.parse(require('fs').readFileSync('artifacts/package-current.json', 'utf8')).bundle")
task release:rehearse -- "$CANDIDATE_BUNDLE"
```

The rehearsal consumes the review bundle, runs the host's native matrix and tests recovery without
remote writes. On macOS it downloads and mounts the pinned Inkscape archive automatically, then
unmounts it on completion. It does not need an interactive editor session. Review the complete-state
images separately. Keep the source unchanged between the gate, packaging and rehearsal; editing a
file invalidates the recorded source fingerprint.

Review bundles use separate development identities and record uncommitted source. They validate
packaging and behavior but are not the tagged RC or publishable evidence. After review and merge,
rerun the gate and hosted rehearsal on master for clean-source Ubuntu/Windows/macOS evidence.
`task release:check TAG=v1.0.0-rc.1` is a read-only metadata/tree/branch check; it intentionally
fails on an uncommitted feature branch and does not replace tests or verify NuGet account
permissions. Leave tag creation and publication for the explicitly authorized release steps below.

## Candidate lifecycle

1. Review the implementation, package metadata, changelog, redistribution rights, private
   references, and known limitations. Merge through the portable Ubuntu/Windows gate. Retain LICENSE
   and NOTICE.
2. Optionally dispatch **NuGet release** on `master`. Dispatch is a non-publishing rehearsal: it
   runs the full gate, prepares both development packages, consumes the same bytes on Ubuntu and
   Windows and macOS, runs the native matrix, and rehearses recovery with fake remote services. It
   never enters the publishing job. Leave the dispatch mode at `rehearse`.
3. After explicit authorization to publish, update clean local `master` and set `TAG` to the
   reviewed version matching the package metadata and changelog (not an already-published tag):

   ```sh
   task release:check TAG='v<version>'
   task release:tag TAG='v<version>'
   task release:push TAG='v<version>'
   ```

   `release:tag` runs the full local gate and creates one annotated local tag. `release:push`
   verifies current remote ancestry and pushes only that tag. Neither command pushes the branch. Tag
   pushes trigger publication preparation; do not use them just to test the workflow.

4. The tag workflow checks policy, then calls the ordinary validation workflow with the release tag.
   Ubuntu builds the release set once and inspects both packages. Ubuntu and Windows each restore it
   into a fresh cache and run the isolated consumer. Package, symbols, source, SDK, bundle
   inventory, and both receipts must agree. The completed artifact is
   `validated-package-<version>-<attempt>`.
5. The macOS ARM64 job first consumes the identical library/tool packages in isolated caches,
   including global, local-manifest and one-off execution. Retain its receipt and diagnostics with
   the native evidence; release approval and recovery require that receipt to match the candidate.
   PR validation remains Ubuntu/Windows only, without a third coverage upload.

   Native jobs use the packaged CLI and the same library DLL/PDB in authoring-test hosts. The macOS
   ARM64 job runs pinned Inkscape native/Plain round trips and deliberate edits, followed by
   Chrome/resvg on the original, saved, and edited corpora (186 comparisons). Ubuntu runs the
   original corpus in both engines (38 comparisons). Preserve reports and images from both jobs.
   Exploratory probes are excluded and do not supply a release verdict.

6. Review complete-state images for content, fitting, clipping, masking, and stacking. The `nuget`
   environment reviewer confirms that evidence, package identity, changelog, and publication intent
   before approving the pending job. Machine checks and human visual review are separate
   requirements.
7. The publishing job revalidates retained evidence and current remote ancestry, creates a
   deterministic recovery archive and reports its identity hash. After protected approval it obtains
   a short-lived NuGet credential, archives and reads back the candidate in a draft release, then
   preflights all existing packages and symbols. Matching artifacts are verified and skipped;
   conflicts or uncertain state stop publication.
8. Upload missing artifacts in order: library package, CLI package, library symbols, CLI symbols.
   Primary pushes disable implicit symbol publication. Per-attempt write-ahead receipts distinguish
   accepted or ambiguous uploads from never-attempted ones. Public verification uses fresh caches,
   repository signatures, unchanged original contents, installed execution, symbols and Source Link.
9. Retain the completion receipt and publish the existing draft using the changelog and appropriate
   prerelease flag. Consumer application validation remains separate.
10. After public verification, review and promote the compatibility baseline: move accepted API
    additions into the shipped declarations and pin the newly published package and verified digest.
    Complete this before further development so additions in each release remain protected.

The tag workflow and rehearsal use SHA-pinned actions and the exact SDK in `global.json`. Native
installation has a separate tool/font pin set. Ubuntu Chrome runs without its sandbox only in the
trusted-fixture hosted runner, matching the existing disposable-container policy. See
[native validation](eng/visual/README.md) and [package preparation](eng/package/README.md).

## One-time owner setup

These are external settings, not changes made by checking in the workflow:

- Allow squash merging only; disable merge commits and rebase merging in repository settings.
- Apply or review [the master ruleset](eng/master-branch.ruleset.json). It requires pull requests,
  resolved review threads, and current **Documentation, library and CLI**, **Library and CLI on
  Windows**, and **Complete coverage upload** checks from GitHub Actions, with no bypass actors.
  Coverage percentages remain informational; missing evidence or failed uploads block merging. Zero
  required approving reviews keeps it usable by a single maintainer. Protect release tags from
  updates/deletions and limit their creation to release maintainers; they authorize the publishing
  path.
- Create the `nuget` environment with required reviewer approval and a deployment policy accepting
  only `v*` tags. Select reviewers who can assess the native evidence. Restrict administrator
  bypass. A sole maintainer must be able to approve their own run; use a separate reviewer when
  available.
- Configure
  [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
  for GitHub owner `svt-project`, repository `svt-dotnet`, workflow `release.yml`, environment
  `nuget`, and both package IDs (`Bathos.Graphics.SvgTemplates` and
  `Bathos.Graphics.SvgTemplates.Cli`). Confirm package ownership/name availability and organization
  permissions. Set the environment variable `NUGET_USER` to the NuGet account username used for the
  trust policy, not an organization name or email address. No long-lived API-key secret is required.
- Enable automatic deletion of merged branches, dependency alerts and Dependabot security updates,
  secret scanning and push protection, and private vulnerability reporting. Verify the private route
  in [SECURITY.md](SECURITY.md). Review access and open alerts; restrict default workflow
  permissions to read-only and disable workflow approval of pull requests. Grant write/OIDC access
  only in jobs that need it. Read these settings and rulesets back after applying them; repository
  files alone do not establish that protections are active. Weekly version updates are configured
  separately in [.github/dependabot.yml](.github/dependabot.yml).
- After merging the dependency configuration, inspect the first updater jobs and the dependency
  graph. Confirm the expected Actions/npm dependencies and resolved NuGet versions, including
  transitive test packages. An empty graph or unbounded version ranges do not establish alert
  coverage. Verify the [dependency-graph workflow](.github/workflows/dependencies.yml) has submitted
  the resolved NuGet/npm inventory from master. SHA-pinned Actions are reviewed through version
  updates separately. Verify the first Dependabot PR passes every required check, including coverage
  upload. NuGet audit during locked restore remains an independent check.

## Failures, retries, and evidence

Registry publication is not atomic. Do not rebuild, retag or replace a package version to complete a
partial release. An original entry or public PDB mismatch blocks all further uploads. NuGet signing
changes ZIP bytes, so verification compares original entries and records distributed hashes
separately. Symbol-server 404s and network/authentication failures do not prove absence.

The draft retains `candidate.zip`, `candidate-identity.json` and `candidate-sealed.json` before any
registry write. The identity binds the repository, tag/commit, original validation run, manifest,
archive and complete file inventory. Preserve candidate assets and validation receipts. Initial
archival may fill missing matching assets before it is sealed; changed or missing sealed evidence
stops recovery. Publication attempts and completion evidence are separate assets.

To resume after CI artifacts expire:

1. Select the existing release **tag** in the NuGet release workflow dispatch.
2. Choose `resume` and provide the original recovery identity hash from the approved run's summary.
3. Review and approve the same `nuget` environment gate. The workflow downloads the retained draft
   assets, validates the selected identity, original run, current tag/ancestry and retained
   evidence, and skips shipping-binary preparation. Initial and resume runs share one tag lock.

Retrying only the failed publishing job also preserves the original candidate. Do not rerun
successful preparation to retry publication. A failed initial asset upload can be retried from the
original retained CI inputs; conflicting or incomplete remote assets require explicit maintainer
reconciliation, never silent replacement.

For `v1.0.0-rc.1`, retry only the failed publishing job in the original tag run. That tag predates
the fix for creating the draft restore's parent directory on a fresh runner; its `resume` dispatch
stops before registry writes. Keep the original candidate artifacts available for this recovery
path.

Accepted uploads can take time to index. Verification uses bounded calls and fails visibly while
availability is unresolved. Wait for registry indexing, then resume with the same identity. An
ambiguous upload leaves write-ahead evidence and blocks automatic resending until its state is
reconciled; contact registry support if it never resolves. `--skip-duplicate` is a transport aid,
not proof of identity. If the archive or identity cannot be recovered, use a new version for both
products. Unlisting does not undo consumers' restores.

If public verification passed but GitHub finalization failed, recovery validates the retained
completion receipt and retries only release finalization. An already-published release is accepted
only after identity and completion checks. The nonpublishing rehearsal tests each archival/upload/
verification boundary and finalization failure without real remote mutations.

Candidate/native CI artifacts last 30 days; publication verification lasts 90 days. Retain draft
assets beyond those windows. A green portable gate or successful upload alone is not a completed
release. Local rehearsals identify their actual host; Windows package execution and Ubuntu native
checks need their own runs. Visual review and external integration remain explicit. No universal
SVG/editor compatibility or Windows native rendering is claimed.
