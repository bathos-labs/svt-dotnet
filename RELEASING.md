# Release readiness

SVT is MIT-licensed and unreleased. The repository remains private; no package publishing workflow
or release credentials are configured. CI validates repository hygiene and portable structural
inspection.

Before a first release, require:

- An implemented library with conformance tests and a clean locked restore, formatting, analyzer,
  Release build and test gate. Keep the production library BCL-only.
- Package metadata for `Bathos.Graphics.SvgTemplates`: MIT expression, repository URL, description,
  README, XML documentation, symbols and an explicitly chosen release version. Inspect the package
  and test an isolated consumer; do not publish development scaffolding.
- A changelog and agreed specification/API stability policy. Version-zero development has no
  compatibility or migration promise. Package versions and the format marker are distinct.
- A review of all content for redistribution rights and private references, plus an explicit
  decision to make the repository public and publish the package.
- Protected release authorization and short-lived publishing credentials. A future release workflow
  should publish the exact artifacts that passed validation, with version/tag checks and an approved
  publishing environment.

`Directory.Build.props` provides development metadata and defaults projects to non-packable. The
shipping project must deliberately opt into packaging. Do not treat a successful documentation CI
run as release validation. Implementation and release work are tracked in
[GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).
