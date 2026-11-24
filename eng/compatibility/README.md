# Compatibility checks

The [compatibility policy](../../spec/compatibility.md) is enforced through nullable-aware source
API analysis, package validation and behavioral tests. These checks protect signatures, defaults and
behavior independently of package publication.

## Reviewed source contract

The library's [PublicAPI.Shipped.txt](../../src/ScalableVectorTemplates/PublicAPI.Shipped.txt)
retains the reviewed API declarations with only the approved namespace replacement. The filename is
the analyzer's contract baseline, not a claim that the renamed package has been published. Both API
files enable nullable tracking. Additions belong in `PublicAPI.Unshipped.txt` and require review.
The CLI has an empty public assembly API: its supported interface is the `svt` command.

The `1.0.0-rc.2` package and assembly identity change intentionally breaks binary compatibility with
`Bathos.Graphics.SvgTemplates` `1.0.0-rc.1`. Its published bytes and historical API records remain
in release history. No aliases, forwarding assemblies, or old-namespace compatibility paths are
added.

## Package validation

[PackageValidation.props](PackageValidation.props) enables SDK validation within the package,
requires portable `net10.0`, rejects validation-disable flags, and forces fresh validation on each
pack. There is no previous published binary package with the renamed identity. No cross-version
binary comparison is claimed during rc.2 preparation; arbitrary baseline overrides fail closed.

After rc.2 is published and publicly verified, establish a baseline against that exact signed
package and digest in a reviewed follow-up before further API development. Reinstate the verified
baseline download and cross-version negative tests, including removed APIs, parameter changes,
missing/modified baseline bytes, and stale SDK validation stamps. Do not point the baseline at an
unpublished package or suppress incompatibilities to obtain a passing result.

## Running and testing enforcement

`task check` runs source analyzers, behavioral tests, SDK packing validation and negative
enforcement checks. After locked restore and a Release build, `task compatibility:check` runs
package validation separately and writes a diagnostic package under ignored
`artifacts/compatibility/`.

Enforcement tests use isolated copies of the actual projects. They verify rejection of removed APIs,
parameter changes, nullability changes, enum values, optional defaults, missing API files and
accidentally public CLI types. An additive API requires an explicit declaration before build/pack
passes. Package checks reject disabled enforcement, arbitrary binary baselines, and loss of portable
framework support. A compilation error is never accepted as evidence of an API analyzer diagnostic.

These checks supplement conformance, boundary, package inspection and isolated-consumer tests.
Behavioral compatibility, allocation guarantees, native rendering and production-template acceptance
retain their separate validation requirements. Stable publication requires explicit authorization.
