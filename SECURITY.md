# Security

Please
[report a vulnerability privately](https://github.com/bathos-labs/svt-dotnet/security/advisories/new).
Include the affected package version or commit, expected impact, and a small reproduction if
possible. Do not include credentials or sensitive documents. Use public issues for ordinary bugs;
keep possible vulnerability details in the private report while we investigate.

SVT is prerelease. Fixes are developed on `master` for the next prerelease; older prereleases do not
have a separate maintenance line. Establishing a stable support policy is part of the 1.0 release.

SVT inspects and substitutes SVG content; it is not an SVG sanitizer. Applications remain
responsible for resource access and safe rendering of untrusted documents. See the
[architecture](docs/architecture.md) for ownership boundaries.
