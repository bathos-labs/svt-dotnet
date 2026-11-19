# Coverage evidence

Run `task test:coverage` with the pinned SDK and Node.js 24. The command restores locked packages,
builds Release, and runs the library and CLI suites with the centrally pinned `coverlet.MTP`
extension. MSTest and its Microsoft.Testing.Platform dependencies are upgraded together; do not mix
an older runner with independently upgraded extensions.

The collector measures the shipping library and `svt` CLI assembly. Tests, package inspection tools,
generated source, and dependencies are excluded. The CLI suite also exercises the library; Codecov
combines overlapping observations rather than adding the percentages. This evidence does not cover
native renderers or establish visual fidelity.

`artifacts/coverage/` belongs to this command and is cleared before each run. It contains raw LCOV,
test output, repository-relative normalized reports, and a source-stamped `report.json`.
Verification requires fresh, internally consistent line/branch counts, repository-owned source
paths, and the expected production modules with executed lines and instrumented branches.
Percentages have no minimum threshold. Run the command serially and do not edit source or rebuild
during collection.

CI retains each platform's evidence for 14 days. After Ubuntu and Windows validation pass, the
upload job sends both verified reports under `linux` and `windows` flags. Codecov does not carry
forward stale platform reports. Project and patch status are informational; collection, evidence,
and upload errors fail CI. `task check` tests the evidence validator without running a second set of
instrumented tests; coverage is a separate CI step and local command.

The Codecov GitHub integration must have access to this public repository. Trusted runs use OIDC
with `id-token: write` confined to the upload job (and allowed by the reusable release caller).
External fork PRs use Codecov's public tokenless upload support. No long-lived upload token is
stored in the repository. Confirm the first upload, both flags, source mapping, and README badge
after these workflow changes reach GitHub; local report validation cannot establish service setup.
