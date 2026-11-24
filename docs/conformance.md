# .NET conformance and reference assets

The implementation embeds an explicitly accepted snapshot of canonical SVT reference assets under
`conformance/`. It owns its runners, additional boundary/regression tests and authoring harness.
Specification updates do not change this checkout automatically.

## Snapshot provenance

`conformance/snapshot.json` records the source repository, full specification commit, asset-set
digest, license and full accepted file inventory. `node eng/conformance/check.mjs` verifies the
local bytes and detects missing, changed or extra canonical files without network access. The full
gate runs this check before tests. Imported data is unchanged; implementation-specific tests live
under `tests/`, and visual examples remain under `examples/authoring/`.

To adopt a new snapshot, review the source commit and its portable contract changes, copy the listed
canonical files from that revision, and update the provenance manifest in the same reviewed change.
Retain LICENSE and any applicable notices; record exclusions explicitly (none in this snapshot). Run
the full gate and relevant package/native checks before accepting the update. Never fetch a floating
specification revision during builds or tests.

The .NET implementation uses the accepted reference budget profiles as its current defaults; changes
remain subject to [compatibility policy](compatibility.md). Cancellation maps to the exceptions in
the API documentation; CLI transport and publication rules are implementation-owned.

## Runner coverage

The [host editor example](../examples/editor/README.md) is also compiled into behavioral tests and
the isolated package consumer. Its separate interaction checks establish that chooser
initialization, quantization and unchanged/canceled interactions do not become implicit bindings.
These are host intent tests, not additional color grammar, inspection admission rules or
documentation wording tests.

## CLI validation

The CLI test project also executes the inspection and substitution fixtures through the real command
entry point. It compares discovery data, structured diagnostics and semantic XML output, while
checking exit status and separating stdout results from stderr diagnostics. Fixture options are
translated explicitly into CLI configuration; the fixture envelope is not an additional CLI format.

Additional tests generate small isolated requests for strict JSON and Unicode, configuration
precedence, stdin ownership, relative image acquisition, admission policies, resource limits and
publication behavior. File tests cover resolved input collisions, symbolic links, distinct hard
links, filesystem case behavior, overwrite races and preservation of existing output. Staging
boundaries and controlled streams exercise cancellation and reporting failures without timing-based
polling. Native rendering and authoring-tool round trips remain separate evidence.

## Authoring examples

The [authoring corpus](../examples/authoring/README.md) adds independently authored visual examples,
raw inventories and expected SVGs. The API/CLI tests exercise these alongside portable conformance.
The [native runner](../eng/visual/README.md) separately records editor round trips, resource
readiness and rendered comparisons; its reports are not portable conformance fixtures.
