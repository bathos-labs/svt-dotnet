# Contributing

The default branch is `master`. Read the [architecture](docs/architecture.md) before structural
changes. Discuss grammar, public API, dependencies and ownership before implementation. Keep design
proposals and delivery work in [GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

The solution contains a BCL-only inspection and substitution library, a BCL-only CLI executable, and
two MSTest projects running portable fixtures, CLI process tests and boundary tests. No package or
release workflow exists. The SDK is pinned to .NET 10.0.403 with latest-patch roll-forward. Shared
build settings require nullable analysis, warnings as errors, recommended analyzers, deterministic
builds, central package versions and locked CI restore. NuGet sources are restricted to nuget.org.
Test dependencies are centrally pinned, and all four projects have lockfiles.

## Validation

Use Node.js 24 with its bundled npm/npx. Task is optional. After Markdown edits, run
`task docs:format`, review the changes, then run `task check` before committing. The equivalent
commands are:

```sh
npx --yes prettier@3.9.10 --write "**/*.md"
git diff --check
git diff --cached --check
npx --yes prettier@3.9.10 --check "**/*.md"
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 remark --rc-path .remarkrc.cjs --ignore-path .prettierignore --frail --no-stdout --no-color .
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 node eng/CheckDocumentationLinks.test.cjs
```

Prettier runs at an exact version through npx, without a local package installation. A cold cache
requires npm registry access. Keep tool versions in this guide, Taskfile.yml and CI synchronized.
The configuration matches the Bathos prose convention: 100-column wrapping with embedded code
formatting disabled. Generated output is excluded through `.prettierignore`.

The documentation gate checks whitespace, Markdown formatting, local links and heading anchors, and
the link-checker's failure behavior. It makes no HTTP requests to validate external links. Review
private-reference sanitization explicitly; no automated confidentiality checker is claimed.

CI runs this documentation gate on pull requests and master pushes using read-only permissions,
SHA-pinned actions, Node.js 24 and an Ubuntu 24.04 runner. It can also be dispatched manually. It
uses the pinned .NET SDK for locked restore, formatting, Release build/analyzers and tests. No
native SDK workloads, runner cleanup or secrets are required. A separate Windows job performs locked
restore, Release build/analyzers and both test suites to exercise platform-specific file and process
behavior. Link tests explicitly report an inconclusive result if the host cannot create symbolic
links; report such unavailable evidence rather than claiming link coverage. CLI tests use isolated
temporary directories and deterministic staging/stream boundaries, without sleeps or polling. The
.NET commands included in `task check` are:

```sh
dotnet restore SvgTemplates.slnx --locked-mode
dotnet format SvgTemplates.slnx --no-restore --verify-no-changes
dotnet build SvgTemplates.slnx -c Release --no-restore
dotnet test --solution SvgTemplates.slnx -c Release --no-build
```

Add package consumption checks before release; see [release readiness](RELEASING.md). Portable
binding tests and native rendering evidence remain distinct.

## Review and publication

Commit cohesive changes when requested. PR descriptions lead with the problem and resulting
behavior, then material limitations or validation not supplied by CI. Do not commit machine paths,
IDE state, credentials, generated evidence or private consumer references. Keep examples independent
and preserve required legal attribution.

This project is private and unreleased. MIT applies to repository-authored code, specification,
documentation and assets. Public repository visibility and package publication still require
explicit decisions. Do not copy another library's released compatibility policy or introduce
development-format migrations. See [NOTICE](NOTICE).
