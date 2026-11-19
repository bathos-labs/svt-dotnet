# UTF-8 export measurements

Run `task perf:export` (or `node eng/ExportPerformance/run.mjs`) from the repository root. The
BCL-only executable uses the public BoundSvg API. The wrapper restores with lockfiles, builds
Release, and writes a source-stamped report to `artifacts/performance/export.json`. A failed run
invalidates earlier evidence, including argument and source-inspection failures. Generated reports
remain untracked. The full gate builds and formats this project and tests receipt invalidation; the
measurements are an explicit opt-in check, separate from instrumented coverage.

Two deterministic cases exercise completed documents:

| Case               | Content                                                                                                                                  | Export iterations per sample |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- |
| Small Unicode      | ASCII, accented and supplementary characters, CJK and XML-sensitive text                                                                 | 10,000                       |
| Near-budget images | The same text plus two encoded-image assignments, each written to href and XLink href; more than 99% of the default 64 MiB output budget | 8                            |

The image bytes are synthetic encoded content; SVT does not decode them, and this is not rendering
evidence. Both cases use default binding budgets. Preparation, binding, expected-output generation,
destination allocation and equality checks occur outside the measured regions. The large case
retains the document, destination and expected bytes, so the process uses substantially more memory
than the 64 MiB output itself.

Each case compares `ToUtf8Bytes()` followed by copying into a preallocated destination against
`TryWriteUtf8Bytes` into that same destination. Separate operations measure `Utf8ByteCount` and a
one-byte-short span with 100,000 iterations per sample. Every operation warms up for its iteration
count before five measured samples. This executable disables
[tiered compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation#tiered-compilation)
so short runs do not mix compilation tiers; the report records this setting. This controlled
comparison does not model an application's dynamically optimized tiered execution. Execution order
alternates by sample. Full collections occur before, not inside, each sample; any collections caused
by the allocating baseline remain part of that measurement.

The runner checks byte equivalence, exact counts and short-buffer preservation before measurement,
then validates counts and destination bytes after every sample. Reset the destination to non-UTF-8
sentinel bytes before each warmup and sample, outside measurement. Successful exports must replace
them with the exact output; sizing and failed exports must leave them intact. A buffer already
containing valid output could otherwise hide an accidental rewrite during a failed call.
`GC.GetAllocatedBytesForCurrentThread` must report zero in every warmed sizing, successful span and
insufficient-span sample. The baseline must allocate at least the output size per call. Delegates,
samples, reporting and assertions that format messages run outside allocation accounting; successful
checks inside calls do not allocate.

The report records runtime, OS, architecture, SDK, GC mode, output size, default budget, warmup,
iterations, allocation totals, individual timing samples and medians. Throughput is emitted only for
operations that actually export bytes. There is no speed threshold: tiny operations include
delegate/timing overhead, and throughput depends on runtime, host load and hardware. This measures
steady-state export costs, not cold JIT behavior, total process allocation, binding costs, host
buffer acquisition or downstream renderer copies. Neither instrumentation nor a debugger should be
attached when collecting comparison evidence.
