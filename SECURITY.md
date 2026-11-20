# Security

Please
[report a vulnerability privately](https://github.com/bathos-labs/svt-dotnet/security/advisories/new).
Include the affected package version or commit, expected impact, and a small reproduction if
possible. Do not include credentials or sensitive documents. Use public issues for ordinary bugs;
keep possible vulnerability details in the private report while we investigate.

SVT is prerelease. Fixes are developed on `master` for the next prerelease; older prereleases do not
have a separate maintenance line. At stable 1.0, support follows the latest stable minor release;
consumers may need to upgrade within the major. No backport or response-time guarantee is made. See
the [support policy](RELEASING.md#stable-acceptance-and-support). Security fixes that break a
documented promise require an explicit compatibility decision and release notice.

SVT inspects and substitutes SVG content; it is not an SVG sanitizer. Applications remain
responsible for resource access and safe rendering of untrusted documents. See the
[architecture](docs/architecture.md) for ownership boundaries.
