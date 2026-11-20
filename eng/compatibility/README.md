# Compatibility checks

The [compatibility policy](../../spec/compatibility.md) is enforced through API analysis, package
comparison and behavioral tests. SVT remains `1.0.0-rc.1`; these checks protect the provisionally
frozen RC surface without declaring stable support or adding migration paths.

## Reviewed baseline

The baseline is the public `Bathos.Graphics.SvgTemplates` package at **1.0.0-rc.1**, from tag
`v1.0.0-rc.1`, source commit `a5b814af03440ea2e711553b1136f846c94686d7`.
[PackageBaseline.props](PackageBaseline.props) pins its exact version and signed NuGet package
SHA-256. NuGet restores it as a build-time download, not a runtime dependency. Packing checks the
digest before the SDK validates compatibility. Missing or different baseline bytes fail validation.
Packing rejects switches that disable package, baseline, assembly or named-parameter validation. The
SDK validation stamp is cleared for each pack attempt so a previous verdict cannot skip the current
comparison.

The library's [PublicAPI.Shipped.txt](../../src/Bathos.Graphics.SvgTemplates/PublicAPI.Shipped.txt)
records the 195 public declarations generated from the RC source using the pinned Microsoft public
API analyzer. Both API files enable nullable tracking. The CLI files deliberately describe an empty
public assembly API: its supported interface is the command line, not its internal .NET types.

The initial source baseline was checked against the tagged RC: shipping C# source is unchanged.
Package validation also compares the built assembly with the published package. The analyzer is
centrally pinned with private assets in the two shipping projects; it does not flow to consumers.

## Running checks

`task check` runs the analyzers during build, the behavioral tests, library package validation and
the compatibility enforcement tests. After locked restore and a Release build, run just package
comparison with:

```sh
task compatibility:check
```

It creates a fresh local diagnostic package under ignored `artifacts/compatibility/`. This is not a
publishable candidate or a substitute for isolated package consumption. Normal package preparation
also runs SDK validation on the actual library package. Ubuntu and Windows CI run the enforcement
checks; release workflows reuse those jobs.

Enforcement tests copy the actual project configuration into a temporary checkout. They prove that
removals and named-parameter changes fail both source and binary checks; nullability, enum values,
optional parameters, missing API files and accidental CLI public types fail source checks. Missing
or altered baseline bytes, replacement paths, disable flags and loss of portable framework support
fail packing. Tests also cover custom cache paths and a cached success that must not hide a broken
assembly. An additive member fails until explicitly recorded in the unshipped API file, then passes
package comparison. Tests never edit the working source or accept a compiler error as proof of
compatibility enforcement.

## Reviewing changes and promoting a release

Keep shipped declarations intact. Proposed additions belong in `PublicAPI.Unshipped.txt` and require
contract and SemVer review. Do not use removal markers, suppression files or analyzer/validator
disable flags to bypass the policy. An intentional pre-stable correction needs explicit review and a
new RC; an incompatible stable change requires a major revision.

Before stable publication, review the complete API surface, accepted additions and behavioral
contracts against the provisional RC baseline. Keep the existing published package pin during
preparation: pointing at an unpublished 1.0 package would prevent its own release from validating.
After public verification of a release, promote accepted additions to the shipped file and update
the baseline version and verified package digest in a reviewed follow-up. Preserve the history and
do this before further development, including for later minor releases, so their additions are
protected. Do not silently regenerate the shipped file or move the baseline to clear a failure.

The source and package checks do not prove behavioral compatibility, overload-resolution safety for
all possible callers, or correctness of every attribute. Existing fixtures, CLI process tests,
package inspection and isolated consumers cover formats, diagnostics, output protection, package
identity and the tool's bundled library. Explicit default-budget tests protect initializer values
that API signatures cannot express. Documented allocation guarantees, native rendering evidence and
external production-template acceptance retain their separate checks. No automatic proof of SemVer
classification or general behavioral compatibility is claimed.
