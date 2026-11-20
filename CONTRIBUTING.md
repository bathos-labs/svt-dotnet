# Contributing

The default branch is `master`. Read the [architecture](docs/architecture.md) before structural
changes. Discuss grammar, public API, dependencies and ownership before implementation. Keep design
proposals and delivery work in [GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

The solution contains a BCL-only inspection and substitution library, a BCL-only CLI executable, and
three MSTest projects running portable fixtures, CLI process tests, boundary tests and package
inspection checks. A BCL-only engineering executable inspects package metadata and symbols. Package
validation and a guarded, tag-triggered publishing workflow are implemented. The SDK is pinned to
.NET 10.0.403 with latest-patch roll-forward. Shared build settings require nullable analysis,
warnings as errors, recommended analyzers, deterministic builds, central package versions and locked
CI restore. NuGet sources are restricted to nuget.org. Test dependencies are centrally pinned, and
all seven solution projects have lockfiles. The library and CLI are separately packable. A BCL-only
engineering executable measures completed-document export allocations and throughput.

## Local setup

Install the .NET 10 SDK and Node.js 24. Task is optional; `node eng/check.mjs` runs the same gate as
`task check`. Build from the repository root:

```sh
dotnet restore SvgTemplates.slnx --locked-mode
dotnet build SvgTemplates.slnx -c Release --no-restore
```

Run the source CLI without locating a build output:

```sh
task svt -- inspect examples/quickstart/template.svg
```

Without Task:

```sh
dotnet run --project src/Bathos.Graphics.SvgTemplates.Cli -c Release -- inspect examples/quickstart/template.svg
```

Task forwards quoted arguments, but maps command failures to its own status (normally 201). Build
messages are development output. Use a prepared installed tool for automation and exact SVT exit
codes. The [README walkthrough](README.md#try-it) describes installed usage; the first CLI release
is still pending. CLI builds embed the version and source revision; `--version` reads these without
Git or filesystem access and does not claim that a checkout is clean or published.

## Null-aware boundaries

All projects use nullable reference analysis with warnings as errors, including tests, engineering
tools and isolated consumers. Do not disable it locally or suppress nullable warnings to obtain a
clean build. Types must represent possible absence at public API, XML, JSON, serialization,
reflection and framework boundaries. Validate, reject or normalize those values at the boundary,
then use non-null contracts within trusted domain code. Preserve meaningful null states such as a
rejected result or an absent authored value; do not replace them with misleading empty strings.

For each boundary, review omitted values, explicit nulls, nullable collection entries and partially
initialized external objects. Required programmer arguments fail deliberately; malformed authored
content follows structured diagnostics. Optional configuration must have documented defaults.
Reflectively populated fields model the state observable during loading; validate required
combinations separately before admitting the object. Null-forgiving suppression is only appropriate
for a verified external initialization invariant that the type system cannot express, with the
invariant documented. Tests may deliberately pass suppressed nulls to exercise rejection contracts.

Verify important boundary behavior through focused tests; compiler analysis alone cannot prove
runtime inputs are non-null. Avoid redundant defensive checks after successful validation or tests
that merely enforce wording in documentation.

## Validation

Use Node.js 24 with its bundled npm/npx. Task is optional. After Markdown edits, run
`task docs:format`, review the changes, then run `task check` before committing. `task check` and
Ubuntu CI invoke the same `node eng/check.mjs` gate, which records a source-stamped receipt under
`artifacts/validation/`. Package preparation rejects failed or stale receipts. The documentation
commands in the gate are:

```sh
npx --yes prettier@3.9.10 --write "**/*.md"
git diff --check
git diff --cached --check
npx --yes prettier@3.9.10 --check "**/*.md"
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 remark --rc-path .remarkrc.cjs --ignore-path .prettierignore --frail --no-stdout --no-color .
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 node eng/CheckDocumentationLinks.test.cjs
```

Prettier runs at an exact version through npx, without a local package installation. A cold cache
requires npm registry access. Keep tool versions in this guide, Taskfile.yml and eng/check.mjs
synchronized. The configuration matches the Bathos prose convention: 100-column wrapping with
embedded code formatting disabled. Generated output is excluded through `.prettierignore`.

The default gate also runs `npm ci --prefix eng/visual --ignore-scripts` and the Node tests in
`eng/visual/`, `eng/package/`, `eng/release/`, `eng/coverage/`, and `eng/ExportPerformance/` to
verify evidence handling without launching browsers. The
[native validation guide](eng/visual/README.md) describes the separate repeatable
Inkscape/Chrome/resvg release checks and their generated evidence. Run these for release candidates
and relevant implementation/tool changes. [Exploratory probes](eng/visual/probes.md) are explicit
opt-in commands; interactive capability mapping is not part of `task check` or the repeatable native
checks.

The documentation gate checks whitespace, Markdown formatting, local links and heading anchors, and
the link-checker's failure behavior. It makes no HTTP requests to validate external links. Review
private-reference sanitization explicitly; no automated confidentiality checker is claimed.

CI runs this documentation gate on pull requests and master pushes using read-only repository
permissions, SHA-pinned actions, Node.js 24 and an Ubuntu 24.04 runner. It can also be dispatched
manually. It uses the pinned .NET SDK for locked restore, formatting, Release build/analyzers and
tests. No native SDK workloads, runner cleanup or secrets are required. Ubuntu prepares and inspects
one development bundle containing both packages after the gate and runs isolated consumption.
Windows then performs locked restore, Release build/analyzers and all three test suites, and
consumes that identical bundle artifact. Matching Ubuntu/Windows receipts produce a candidate
manifest and retained CI artifact; no registry publication is performed by ordinary CI. The separate
release workflow reuses these checks, adds isolated macOS library/tool consumption and native
candidate evidence, and requires the protected `nuget` environment before publishing. Link tests
explicitly report an inconclusive result if the host cannot create symbolic links; report such
unavailable evidence rather than claiming link coverage. CLI tests use isolated temporary
directories and deterministic staging/stream boundaries, without sleeps or polling. The .NET
commands included in `task check` are:

```sh
dotnet restore SvgTemplates.slnx --locked-mode
dotnet format SvgTemplates.slnx --no-restore --verify-no-changes
dotnet build SvgTemplates.slnx -c Release --no-restore
dotnet test --solution SvgTemplates.slnx -c Release --no-build
```

Run `task test:coverage` for production line and branch coverage using the centrally pinned
`coverlet.MTP` extension and MSTest. Ubuntu and Windows CI collect and verify fresh reports before a
separate upload job sends both platforms to Codecov. That job uses short-lived OIDC credentials for
trusted runs and public tokenless uploads for fork PRs. Percentages remain informational; collection
and upload failures fail CI. See the [coverage guide](eng/coverage/README.md).

Run `task perf:export` for reproducible UTF-8 export allocation and throughput checks. The
[measurement guide](eng/ExportPerformance/README.md) explains the small Unicode and near-budget
image cases, strict zero-allocation checks and informational timings. This opt-in command builds
Release and records source-stamped evidence separately from coverage and native rendering.

After the gate, run `task package:check` for a review-only package and isolated local consumption.
Use the [package preparation guide](eng/package/README.md) for clean-commit candidates,
exact-artifact Ubuntu/Windows checks and handoff manifests. The packaging workflow requires the
exact SDK pin and records it; ordinary solution builds retain the SDK's configured patch
roll-forward policy. See [release readiness](RELEASING.md). Portable binding tests, package
consumption and native rendering evidence remain distinct.

## Review and publication

Commit cohesive changes when requested. PR descriptions lead with the problem and resulting
behavior, then material limitations or validation not supplied by CI. Do not commit machine paths,
IDE state, credentials, generated evidence or private consumer references. Keep examples independent
and preserve required legal attribution.

This project is public and prerelease. MIT applies to repository-authored code, specification,
documentation and assets. Publication requires an explicit release tag and approval under the
[release policy](RELEASING.md). Do not copy another library's released compatibility policy or
introduce development-format migrations. See [NOTICE](NOTICE).
