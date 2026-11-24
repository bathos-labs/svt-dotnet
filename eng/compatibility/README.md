# Compatibility checks

The [compatibility policy](../../docs/compatibility.md) is enforced through nullable-aware source
API analysis, package validation and behavioral tests. These checks protect signatures, defaults and
behavior independently of package publication.

## Reviewed source contract

The library's [PublicAPI.Shipped.txt](../../src/ScalableVectorTemplates/PublicAPI.Shipped.txt)
records the API published in `ScalableVectorTemplates` `1.0.0-rc.2`. Both API files enable nullable
tracking. Additions belong in `PublicAPI.Unshipped.txt` and require review. The CLI has an empty
public assembly API: its supported interface is the `svt` command.

The `1.0.0-rc.2` package and assembly identity change intentionally breaks binary compatibility with
`Bathos.Graphics.SvgTemplates` `1.0.0-rc.1`. Its published bytes and historical API records remain
in release history. No aliases, forwarding assemblies, or old-namespace compatibility paths are
added.

## Package validation

[PackageValidation.props](PackageValidation.props) enables SDK validation within the package and
against the exact published `ScalableVectorTemplates` `1.0.0-rc.2` package. The NuGet
repository-signed artifact has SHA-256
`37c3c2104608a06a76d8c0f41585791f3485e4c172e977a0555700c887d38214`, verified against the
[release completion receipt](https://github.com/svt-project/svt-dotnet/releases/tag/v1.0.0-rc.2) and
an independent public download. This is the distributed package digest, not the unsigned upload
digest. No package binary is checked into the repository.

Locked restore acquires the exact baseline through the SDK's package download. Each pack verifies
its bytes, requires portable `net10.0`, rejects identity/path substitutions and validation-disable
flags, and removes the SDK's incremental validation stamp before comparison. Missing or altered
baseline bytes fail closed, including when using a custom NuGet cache.

Promote the baseline only after a later release is publicly verified and its full accepted surface
is reviewed. Update the version/digest and shipped source declarations together where needed; never
suppress incompatibilities or point the baseline at an unpublished package to make checks pass. RC.2
has no pending source API additions to promote. The CLI retains an empty public assembly API; its
command behavior remains covered separately.

## Running and testing enforcement

`task check` runs source analyzers, behavioral tests, SDK packing validation and negative
enforcement checks. After locked restore and a Release build, `task compatibility:check` runs
package validation separately and writes a diagnostic package under ignored
`artifacts/compatibility/`.

Enforcement tests use isolated copies of the actual projects. Removed APIs and renamed parameters
fail both source analysis and independent binary comparison, even with a future-dated SDK stamp.
They also verify rejection of removed APIs, parameter changes, nullability changes, enum values,
optional defaults, missing API files and accidentally public CLI types. An additive API requires an
explicit declaration before build/pack passes. Package checks reject disabled enforcement, arbitrary
binary baselines, missing/altered baseline bytes, and loss of portable framework support. A
compilation error is never accepted as evidence of an API analyzer diagnostic.

These checks supplement conformance, boundary, package inspection and isolated-consumer tests.
Behavioral compatibility, allocation guarantees, native rendering and production-template acceptance
retain their separate validation requirements. Stable publication requires explicit authorization.
