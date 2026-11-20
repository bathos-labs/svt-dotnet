import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, rm, readFile, writeFile, cp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { json, fileHash, treeHashes, smokeChecks, toolChecks, sdk } from "../package/common.mjs";
import { verifyNative, verifyRetainedEvidence } from "./evidence.mjs";

async function writeNative(folder, manifest, manifestHash, platform) {
  const native = {
    format_version: 0,
    purpose: "release-check",
    status: "passed",
    source: manifest.source,
    products: manifest.products,
    bundle_manifest_sha256: manifestHash,
    platform,
    reports: [],
  };
  const counts = platform === "linux" ? [38] : [38, null, 46, 46, 14, 14, 14, 14];
  for (const [index, count] of counts.entries()) {
    const path = `run-${index}/report.json`;
    const report = {
      format_version: 0,
      status: "passed",
      purpose: "release-check",
      git_head: manifest.source.commit,
      dirty: false,
      environment: { platform },
      hashes: {
        "library.dll": manifest.products.library.assembly_sha256,
        "svt.dll": manifest.products.cli.assembly_sha256,
      },
      missing_resource_rejected: true,
    };
    if (count !== null)
      report.comparisons = Array.from({ length: count }, () => ({ changed_pixels: 0 }));
    else {
      report.modes = ["native", "plain"].map((mode) => ({
        mode,
        status: "passed",
        api_cli: "passed",
      }));
      report.edits = ["native", "plain"].flatMap((mode) =>
        ["geometry", "paint"].map((name) => ({
          mode,
          name,
          status: "passed",
          api_cli: "passed",
        })),
      );
    }
    await json(resolve(folder, path), report);
    await writeFile(resolve(folder, `run-${index}/preview.png`), "image fixture");
    native.reports.push({
      files: await treeHashes(resolve(folder, `run-${index}`)),
      path: "artifacts/" + path,
      sha256: await fileHash(resolve(folder, path)),
      ...(count === null ? {} : { comparisons: count }),
    });
  }
  await json(resolve(folder, `native-candidate-${platform}.json`), native);
  return native;
}

test("native approval requires the complete candidate matrix and retained passing reports", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-native-evidence-"));
  const manifest = {
    source: { commit: "a".repeat(40), dirty: false },
    products: {
      library: { assembly_sha256: "b".repeat(64) },
      cli: { assembly_sha256: "d".repeat(64) },
    },
  };
  const manifestHash = "c".repeat(64);
  try {
    for (const platform of ["linux", "darwin"]) {
      const native = await writeNative(folder, manifest, manifestHash, platform);
      const summary = resolve(folder, `native-candidate-${platform}.json`);
      await json(summary, native);
      await verifyNative(folder, manifest, manifestHash, platform);
      for (const change of [
        { purpose: "exploratory-probe" },
        { status: "failed" },
        { products: { library: { assembly_sha256: "other" } } },
        { reports: [] },
        { bundle_manifest_sha256: "other" },
        { source: { ...manifest.source, dirty: true } },
      ]) {
        await json(summary, { ...native, ...change });
        await assert.rejects(verifyNative(folder, manifest, manifestHash, platform));
      }
      await json(summary, native);
      const preview = resolve(folder, "run-0/preview.png");
      await writeFile(preview, "altered");
      await assert.rejects(verifyNative(folder, manifest, manifestHash, platform), /files changed/);
      await rm(preview);
      await assert.rejects(verifyNative(folder, manifest, manifestHash, platform), /files changed/);
      await writeFile(preview, "image fixture");
      const path = resolve(folder, "run-0/report.json"),
        original = JSON.parse(await readFile(path));
      for (const change of [
        { status: "failed" },
        { purpose: "exploratory-probe" },
        { dirty: true },
        { hashes: { "library.dll": "rebuilt" } },
        { missing_resource_rejected: false },
        { comparisons: original.comparisons.slice(1) },
      ]) {
        await json(path, { ...original, ...change });
        // Even internally consistent hashes cannot turn a failed/incomplete report into approval.
        native.reports[0].sha256 = await fileHash(path);
        native.reports[0].files = await treeHashes(resolve(folder, "run-0"));
        await json(summary, native);
        await assert.rejects(verifyNative(folder, manifest, manifestHash, platform));
      }
      await json(path, original);
      await assert.rejects(verifyNative(folder, manifest, manifestHash, platform), /changed/);
    }
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});

test("release evidence requires matching macOS consumption from the selected native attempt", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-release-evidence-"));
  const transfer = resolve(folder, "transfer"),
    nativeRoot = resolve(folder, "native");
  const manifest = {
    purpose: "handoff-candidate",
    sdk,
    source: { commit: "a".repeat(40), dirty: false, fingerprint: "d".repeat(64) },
    products: {
      library: { assembly_sha256: "b".repeat(64) },
      cli: { assembly_sha256: "c".repeat(64) },
    },
  };
  try {
    await json(resolve(transfer, "bundle/manifest.json"), manifest);
    const manifestHash = await fileHash(resolve(transfer, "bundle/manifest.json"));
    const receipt = (platform) => ({
      format_version: 0,
      status: "passed",
      purpose: manifest.purpose,
      sdk,
      bundle_manifest_sha256: manifestHash,
      source: manifest.source,
      products: manifest.products,
      environment: {
        platform,
        ...(platform === "linux" ? { distribution: { id: "ubuntu" } } : {}),
      },
      distribution: "local-artifact",
      empty_feed_rejected: true,
      checks: smokeChecks,
      tool: { status: "passed", checks: toolChecks },
    });
    await json(resolve(transfer, "ubuntu.json"), receipt("linux"));
    await json(resolve(transfer, "windows.json"), receipt("win32"));
    const mac = resolve(nativeRoot, "native-macos-15-1");
    await writeNative(mac, manifest, manifestHash, "darwin");
    await writeNative(
      resolve(nativeRoot, "native-ubuntu-24.04-1"),
      manifest,
      manifestHash,
      "linux",
    );
    const verify = () => verifyRetainedEvidence(transfer, nativeRoot, manifest);
    await assert.rejects(verify(), /macos-consumption/);
    const path = resolve(mac, "macos-consumption.json"),
      original = receipt("darwin");
    await json(path, original);
    assert.equal(await verify(), manifestHash);
    for (const change of [
      { status: "failed" },
      { environment: { platform: "linux" } },
      { bundle_manifest_sha256: "e".repeat(64) },
      { source: { ...manifest.source, fingerprint: "other" } },
      { products: { ...manifest.products, cli: { assembly_sha256: "rebuilt" } } },
      { sdk: "other" },
      { purpose: "review" },
      { checks: [] },
      { tool: { status: "failed", checks: toolChecks } },
      { tool: { status: "passed", checks: [] } },
      { empty_feed_rejected: false },
      { distribution: "nuget.org" },
    ]) {
      await json(path, { ...original, ...change });
      await assert.rejects(verify());
    }
    await json(path, original);
    // A passing older attempt cannot supply the missing result for the latest native run.
    const latest = resolve(nativeRoot, "native-macos-15-2");
    await cp(mac, latest, { recursive: true });
    await rm(resolve(latest, "macos-consumption.json"));
    await assert.rejects(verify(), /macos-consumption/);
    await json(resolve(latest, "macos-consumption.json"), original);
    assert.equal(await verify(), manifestHash);
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
