# Release readiness

SVT is MIT-licensed and unreleased. The repository remains private; no package publishing workflow
or release credentials are configured. CI validates repository hygiene, portable inspection and
substitution, CLI process/file behavior, package contents and isolated package consumption.

Before a first release, require:

- An implemented library with conformance tests and a clean locked restore, formatting, analyzer,
  Release build and test gate. Keep the production library BCL-only.
- The [repeatable native checks](eng/visual/README.md#release-candidate-checks) for the candidate:
  macOS Inkscape round trips and Chrome/resvg comparisons on macOS and Ubuntu, with pinned tools and
  retained evidence. Review the images and report unavailable checks. Exploratory probes have
  separate observations and do not supply a release verdict or leave the automated result partial.
- Package metadata for `Bathos.Graphics.SvgTemplates`: MIT expression, repository URL, description,
  README, XML documentation, symbols and an explicitly chosen release version. Inspect the package
  and test an isolated consumer; do not publish development scaffolding.
- A changelog and agreed specification/API stability policy. Unreleased development has no
  compatibility or migration promise. Package versions and the format marker are distinct.
- A review of all content for redistribution rights and private references, plus an explicit
  decision to make the repository public and publish the package.
- Protected release authorization and short-lived publishing credentials. A future release workflow
  should publish the exact artifacts that passed validation, with version/tag checks and an approved
  publishing environment.

`Directory.Build.props` provides development metadata and defaults projects to non-packable. Only
the shipping library opts into packaging. The [private preparation workflow](eng/package/README.md)
requires a clean commit and matching gate receipt, then checks one exact artifact on Ubuntu and
Windows before producing a private trial manifest. Review-only packages cannot be promoted to
handoffs. Retain the tested bytes, symbols and evidence; do not rebuild under the same identity.
Native validation and consumer application integration remain separate requirements. Do not treat a
successful documentation CI run or a private trial manifest as public release authorization.
Implementation and release work are tracked in
[GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).
