# Contributing

The default branch is `master`. Read the [architecture](docs/architecture.md) before structural
changes. Discuss grammar, public API, dependencies and ownership before implementation. Keep design
proposals and delivery work in [GitHub Issues](https://github.com/bathos-labs/svt-dotnet/issues).

The repository currently contains documentation and preparation guidance only. There is no .NET
build, runtime test suite, package or release workflow yet. Establish a pinned SDK, locked restore,
analyzers and implementation tests when adding executable projects.

## Validation

Use Node.js 24 with its bundled npm/npx. Task is optional. After Markdown edits, run
`task docs:format`, review the changes, then run `task check` before committing. The equivalent
commands are:

```sh
npx --yes prettier@3.9.10 --write "**/*.md"
git diff --check
git diff --cached --check
npx --yes prettier@3.9.10 --check "**/*.md"
```

Prettier runs at an exact version through npx, without a local package installation. A cold cache
requires npm registry access. Keep its version in this guide and Taskfile.yml synchronized. The
configuration matches the Bathos prose convention: 100-column wrapping with embedded code formatting
disabled. Generated output is excluded through `.prettierignore`.

The current gate checks whitespace and Markdown formatting. Review local links and private-reference
sanitization explicitly; no automated link or confidentiality checker is claimed. Portable binding
tests and native rendering evidence must remain distinct when implemented.

## Review and publication

Commit cohesive changes when requested. PR descriptions lead with the problem and resulting
behavior, then material limitations or validation not supplied by CI. Do not commit machine paths,
IDE state, credentials, generated evidence or private consumer references. Keep examples independent
and preserve required legal attribution.

This project is private and unreleased. Public release, redistribution licenses and package
publication require explicit decisions. Do not copy another library's released compatibility policy
or introduce development-format migrations. See [NOTICE](NOTICE).
