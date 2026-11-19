# Releasing

SVT is public, MIT-licensed, and prerelease. `0.1.0-beta.1` is the first published beta. A later
version in an issue, contract or changelog does not imply publication. The API and specification may
change between prereleases. Consumers should pin an exact published version
(`Version="[0.1.0-beta.1]"` in a PackageReference) and validate their integration. There is no
stable API or format migration promise. The SVG marker and JSON envelopes remain version 0
independently of NuGet.

## Version and branch policy

`Directory.Build.props` owns the shipping version. [CHANGELOG.md](CHANGELOG.md) must have one
`Unreleased` section and a dated, populated entry for that version as its latest release. Use
canonical three-part versions with optional lowercase SemVer prerelease identifiers. Build metadata,
leading-zero aliases, and development `dev`, `review`, or `preview` identities are not release
versions.

Through the first stable 1.0 release, `master` is the active release branch for beta candidates and
the stable release. Integrate reviewed work there using feature branches as needed; there is no
separate prerelease or maintenance line to protect. Revisit branch and compatibility policy when the
first stable release is established. Published beta packages and historical tags remain immutable.

The existing release guard permits stable or prerelease versions on `master`, and prereleases only
on `release/*`; that latter capability is not needed for the current development process. Feature
branches can run ordinary validation but cannot prepare a release tag. An annotated `v<version>` tag
must identify the exact checked-out commit. Publication refreshes actual branch refs from origin and
requires that commit to be an ancestor of an eligible remote branch. Stale local tracking refs,
unpushed commits, failed fetches, dirty trees, and mismatched metadata fail closed.

## Candidate lifecycle

1. Review the implementation, package metadata, changelog, redistribution rights, private
   references, and known limitations. Merge through the portable Ubuntu/Windows gate. Retain LICENSE
   and NOTICE.
2. Optionally dispatch **NuGet release** on `master`. Dispatch is a non-publishing rehearsal: it
   runs the full gate, prepares a development package, consumes the same bytes on Ubuntu and
   Windows, and runs the native matrix. It never enters the publishing job.
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
   Ubuntu builds and inspects the intended version once. Ubuntu and Windows each restore it into a
   fresh cache and run the isolated consumer. Package, symbols, source, SDK, bundle inventory, and
   both receipts must agree. The completed artifact is `validated-package-<version>-<attempt>`.
5. Native jobs use that same packaged DLL in the CLI and authoring-test hosts. The macOS ARM64 job
   runs pinned Inkscape native/Plain round trips and deliberate edits, followed by Chrome/resvg on
   the original, saved, and edited corpora (186 comparisons). Ubuntu runs the original corpus in
   both engines (38 comparisons). Preserve reports and images from both jobs. Exploratory probes are
   excluded and do not supply a release verdict.
6. Review complete-state images for content, fitting, clipping, masking, and stacking. The `nuget`
   environment reviewer confirms that evidence, package identity, changelog, and publication intent
   before approving the pending job. Machine checks and human visual review are separate
   requirements.
7. The publishing job revalidates retained evidence and remote ancestry, obtains a short-lived NuGet
   credential through GitHub OIDC, and uploads the exact `.nupkg` and `.snupkg`. It never rebuilds
   them. The job then restores from nuget.org with fresh caches, verifies signing and unchanged
   original contents, runs the consumer, and downloads matching public symbols and Source Link
   sources.
8. After public verification succeeds, create a GitHub release for the existing tag using the
   changelog notes and the **prerelease** flag for beta versions. Retain the candidate and evidence
   with that release before CI artifacts expire. Consumer application validation remains separate.

The tag workflow and rehearsal use SHA-pinned actions and the exact SDK in `global.json`. Native
installation has a separate tool/font pin set. Ubuntu Chrome runs without its sandbox only in the
trusted-fixture hosted runner, matching the existing disposable-container policy. See
[native validation](eng/visual/README.md) and [package preparation](eng/package/README.md).

## One-time owner setup

These are external settings, not changes made by checking in the workflow:

- Apply or review [the master ruleset](eng/master-branch.ruleset.json). It requires pull requests,
  resolved review threads, and current **Documentation, library and CLI** and **Library and CLI on
  Windows** checks from GitHub Actions, with no bypass actors. Zero required approving reviews keeps
  it usable by a single maintainer. Protect release tags from updates/deletions and limit their
  creation to release maintainers; they authorize the publishing path.
- Create the `nuget` environment with required reviewer approval and a deployment policy accepting
  only `v*` tags. Select reviewers who can assess the native evidence. Restrict administrator
  bypass. A sole maintainer must be able to approve their own run; use a separate reviewer when
  available.
- Configure
  [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
  for owner `bathos-labs`, repository `svt-dotnet`, workflow `release.yml`, environment `nuget`, and
  package `Bathos.Graphics.SvgTemplates`. Confirm package ownership/name availability and
  organization permissions. Set the environment variable `NUGET_USER` to the NuGet account username
  used for the trust policy, not an organization name or email address. No long-lived API-key secret
  is required.
- Review public repository security settings, including secret scanning/push protection and
  reporting access. Configure them explicitly; repository files do not establish that these settings
  are active.

## Failures, retries, and evidence

Registry publication is not atomic across package and symbols. `--skip-duplicate` allows retrying an
upload of the retained candidate, but does not prove identity. Public verification must still
compare all original package entries and the PDB with the tested candidate. NuGet repository signing
changes ZIP bytes; record the distributed hash separately from the unsigned candidate hash.

NuGet indexing and symbol availability can lag an accepted upload. Verification fails visibly rather
than guessing that elapsed time proves availability. Retain the original bundle and rerun the failed
publishing job or run `task release:verify -- <bundle>` after indexing completes. Re-running only
the failed job reuses prior validated artifacts. Do not rerun successful package preparation to
retry an upload, replace a release tag, or rebuild under a published identity. A changed candidate
needs a new version and tag. Unlisting is not deletion or a reliable rollback for consumers who
already restored.

Candidate/native artifacts last 30 days; publication verification lasts 90 days. Failure logs have
shorter retention. A green portable gate, development handoff, or successful upload alone is not a
completed release. Unavailable native checks, failed public verification, and unresolved consumer
integration must remain explicit. Windows native rendering and universal SVG/editor compatibility
are not claimed. Known resource/CSS/layout boundaries remain documented in the specification and
[authoring guide](docs/authoring.md).
