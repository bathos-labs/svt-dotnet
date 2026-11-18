import assert from "node:assert/strict";

// Canonical NuGet identities: no aliases from casing, leading zeros or build metadata.
export function releaseVersion(version) {
  assert.match(
    version,
    /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([a-z0-9-]+(?:\.[a-z0-9-]+)*))?$/,
    "Expected a canonical release version",
  );
  const suffix = version.split("-").slice(1).join("-");
  for (const part of suffix.split("."))
    assert.ok(!/^0\d+$/.test(part), "Numeric prerelease identifiers cannot have leading zeros");
  assert.ok(
    !/^(dev|review|preview)(\.|$)/.test(suffix),
    "Development artifacts cannot be released",
  );
  return version;
}

export function eligibleBranch(branch, version) {
  return (
    branch === "master" ||
    (branch.startsWith("release/") && branch.length > 8 && version.includes("-"))
  );
}
