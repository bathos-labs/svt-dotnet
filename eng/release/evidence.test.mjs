import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, rm, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { json, fileHash } from "../package/common.mjs";
import { verifyNative } from "./evidence.mjs";

test("native approval requires the complete candidate matrix and retained passing reports", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-native-evidence-"));
  const manifest = {
    source: { commit: "a".repeat(40), dirty: false },
    package: { assembly_sha256: "b".repeat(64) },
  };
  const manifestHash = "c".repeat(64);
  try {
    for (const platform of ["linux", "darwin"]) {
      const native = {
        format_version: 0,
        purpose: "release-check",
        status: "passed",
        source: manifest.source,
        package: manifest.package,
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
          hashes: { "library.dll": manifest.package.assembly_sha256 },
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
        native.reports.push({
          path: "artifacts/" + path,
          sha256: await fileHash(resolve(folder, path)),
          ...(count === null ? {} : { comparisons: count }),
        });
      }
      const summary = resolve(folder, `native-candidate-${platform}.json`);
      await json(summary, native);
      await verifyNative(folder, manifest, manifestHash, platform);
      for (const change of [
        { purpose: "exploratory-probe" },
        { status: "failed" },
        { package: { assembly_sha256: "other" } },
        { reports: [] },
        { bundle_manifest_sha256: "other" },
        { source: { ...manifest.source, dirty: true } },
      ]) {
        await json(summary, { ...native, ...change });
        await assert.rejects(verifyNative(folder, manifest, manifestHash, platform));
      }
      await json(summary, native);
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
