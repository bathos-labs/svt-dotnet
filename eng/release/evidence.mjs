import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileHash, bundlePath } from "../package/common.mjs";

export async function verifyNative(folder, manifest, manifestHash, platform) {
  const counts = platform === "darwin" ? [38, null, 46, 46, 14, 14, 14, 14] : [38];
  assert.ok(["darwin", "linux"].includes(platform));
  const read = async (path) => JSON.parse(await readFile(path));
  const native = await read(resolve(folder, `native-candidate-${platform}.json`));
  assert.equal(native.format_version, 0);
  assert.equal(native.purpose, "release-check");
  assert.equal(native.status, "passed");
  assert.equal(native.platform, platform);
  assert.deepEqual(native.source, manifest.source);
  assert.deepEqual(native.package, manifest.package);
  assert.equal(native.bundle_manifest_sha256, manifestHash);
  assert.deepEqual(
    native.reports.map((report) => report.comparisons ?? null),
    counts,
  );
  assert.equal(
    new Set(native.reports.map((report) => report.path)).size,
    counts.length,
    "Duplicate native evidence",
  );
  for (const report of native.reports) {
    assert.ok(report.path.startsWith("artifacts/"));
    const path = bundlePath(folder, report.path.slice("artifacts/".length));
    assert.equal(await fileHash(path), report.sha256, "Native evidence changed");
    const evidence = await read(path);
    assert.equal(evidence.format_version, 0);
    assert.equal(evidence.status, "passed");
    assert.equal(evidence.purpose, "release-check");
    assert.equal(evidence.git_head, manifest.source.commit);
    assert.equal(evidence.dirty, false);
    assert.equal(evidence.environment.platform, platform);
    assert.equal(evidence.hashes["library.dll"], manifest.package.assembly_sha256);
    if (report.comparisons !== undefined) {
      assert.equal(evidence.comparisons.length, report.comparisons);
      assert.ok(evidence.comparisons.every((item) => item.changed_pixels === 0));
      assert.equal(evidence.missing_resource_rejected, true);
    } else {
      assert.deepEqual(
        evidence.modes.map((mode) => [mode.mode, mode.status, mode.api_cli]),
        [
          ["native", "passed", "passed"],
          ["plain", "passed", "passed"],
        ],
      );
      assert.deepEqual(
        evidence.edits.map((edit) => [edit.mode, edit.name, edit.status, edit.api_cli]),
        ["native", "plain"].flatMap((mode) =>
          ["geometry", "paint"].map((name) => [mode, name, "passed", "passed"]),
        ),
      );
    }
  }
}
