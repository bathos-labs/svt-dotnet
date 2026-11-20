import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { resolve, dirname } from "node:path";
import {
  fileHash,
  bundlePath,
  validateReceipts,
  validateReceipt,
  treeHashes,
} from "../package/common.mjs";

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
  assert.deepEqual(native.products, manifest.products);
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
    assert.deepEqual(
      await treeHashes(dirname(path)),
      report.files,
      "Native files changed or missing",
    );
    const evidence = await read(path);
    assert.equal(evidence.format_version, 0);
    assert.equal(evidence.status, "passed");
    assert.equal(evidence.purpose, "release-check");
    assert.equal(evidence.git_head, manifest.source.commit);
    assert.equal(evidence.dirty, false);
    assert.equal(evidence.environment.platform, platform);
    assert.equal(evidence.hashes["library.dll"], manifest.products.library.assembly_sha256);
    assert.equal(evidence.hashes["svt.dll"], manifest.products.cli.assembly_sha256);
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

export async function verifyRetainedEvidence(transfer, nativeRoot, manifest) {
  const bundle = resolve(transfer, "bundle");
  const manifestHash = await fileHash(resolve(bundle, "manifest.json"));
  const read = async (path) => JSON.parse(await readFile(path));
  validateReceipts(
    manifest,
    manifestHash,
    await Promise.all(["ubuntu", "windows"].map((os) => read(resolve(transfer, os + ".json")))),
  );
  for (const [runner, platform] of [
    ["macos-15", "darwin"],
    ["ubuntu-24.04", "linux"],
  ]) {
    const evidenceRoot = nativeRoot;
    const attempts = (await readdir(evidenceRoot))
      .filter(
        (name) =>
          name.startsWith(`native-${runner}-`) &&
          /^\d+$/.test(name.slice(`native-${runner}-`.length)),
      )
      .sort((a, b) => Number(b.split("-").at(-1)) - Number(a.split("-").at(-1)));
    assert.ok(attempts.length > 0, `Missing native evidence for ${runner}`);
    const folder = resolve(evidenceRoot, attempts[0]);
    await verifyNative(folder, manifest, manifestHash, platform);
    if (platform === "darwin")
      validateReceipt(
        manifest,
        manifestHash,
        await read(resolve(folder, "macos-consumption.json")),
        "darwin",
      );
  }
  return manifestHash;
}
