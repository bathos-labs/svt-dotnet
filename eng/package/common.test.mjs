import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile, symlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import {
  requireGate,
  packageVersion,
  validateReceipts,
  bundlePath,
  treeHashes,
  verifyBundle,
  json,
  sdk,
  packageId,
  hash,
  smokeChecks,
} from "./common.mjs";

const source = {
  commit: "0123456789abcdef0123456789abcdef01234567",
  dirty: false,
  fingerprint: "a".repeat(64),
};
test("preparation requires a successful gate for the exact source and a clean handoff", () => {
  const gate = { format_version: 0, status: "passed", source };
  requireGate(gate, source, false);
  assert.throws(() => requireGate({ ...gate, status: "failed" }, source, false));
  assert.throws(() => requireGate(gate, { ...source, fingerprint: "changed" }, true));
  const dirty = { ...source, dirty: true };
  requireGate({ ...gate, source: dirty }, dirty, true);
  assert.throws(() => requireGate({ ...gate, source: dirty }, dirty, false));
  assert.match(
    packageVersion(source, true, "0123456789abcdef"),
    /^0\.1\.0-review\.g0123456789ab\./,
  );
  assert.match(
    packageVersion(source, false, "0123456789abcdef"),
    /^0\.1\.0-preview\.g0123456789ab\./,
  );
  assert.notEqual(packageVersion(source, false), packageVersion(source, false));
  assert.ok(packageVersion(source, true, "0000000000000000").endsWith(".n0000000000000000"));
});
test("handoff needs both platforms to consume the same artifact, not a rebuilt version", () => {
  const manifest = {
    purpose: "handoff-candidate",
    source,
    sdk,
    package: { version: "example", sha256: "original" },
    symbols: { sha256: "symbols" },
  };
  const manifestHash = "b".repeat(64);
  const receipts = ["linux", "win32"].map((platform) => ({
    format_version: 0,
    status: "passed",
    bundle_manifest_sha256: manifestHash,
    purpose: manifest.purpose,
    source,
    sdk,
    package: manifest.package,
    symbols: manifest.symbols,
    environment: {
      platform,
      ...(platform === "linux" ? { distribution: { id: "ubuntu", version: "24.04" } } : {}),
    },
    empty_feed_rejected: true,
    checks: [...smokeChecks],
  }));
  validateReceipts(manifest, manifestHash, receipts);
  for (const change of [
    { status: "failed" },
    { package: { version: "example", sha256: "rebuilt" } },
    { symbols: { sha256: "other" } },
    { bundle_manifest_sha256: "c".repeat(64) },
    { format_version: 1 },
    { environment: { platform: "linux" } },
    { empty_feed_rejected: false },
    { checks: [] },
    { checks: Array(6).fill("unknown-check") },
    { source: { ...source, fingerprint: "other" } },
  ])
    assert.throws(() =>
      validateReceipts(manifest, manifestHash, [receipts[0], { ...receipts[1], ...change }]),
    );
  assert.throws(() => validateReceipts({ ...manifest, purpose: "review" }, manifestHash, receipts));
  assert.throws(() =>
    validateReceipts(manifest, manifestHash, [
      { ...receipts[0], environment: { platform: "linux", distribution: { id: "debian" } } },
      receipts[1],
    ]),
  );
});
async function createBundle(folder) {
  const version = packageVersion(source, true);
  const packageFile = `packages/${packageId}.${version}.nupkg`;
  const symbolFile = `packages/${packageId}.${version}.snupkg`;
  await mkdir(resolve(folder, "packages"));
  await writeFile(resolve(folder, packageFile), "package");
  await writeFile(resolve(folder, symbolFile), "symbols");
  const manifest = {
    format_version: 0,
    sdk,
    target_framework: "net10.0",
    purpose: "review",
    source,
    package: {
      id: packageId,
      version,
      file: packageFile,
      sha256: hash("package"),
      assembly_sha256: hash("assembly"),
    },
    symbols: { file: symbolFile, sha256: hash("symbols") },
    validation: {
      portable_gate: "passed",
      package_inspection: "passed",
      consumption: "pending",
      required_platforms: ["linux", "win32"],
    },
  };
  await json(resolve(folder, "validation-gate.json"), {
    format_version: 0,
    status: "passed",
    sdk,
    source,
  });
  await json(resolve(folder, "inspection.json"), {
    package_id: packageId,
    version,
    commit: source.commit,
    package_sha256: manifest.package.sha256,
    symbols_sha256: manifest.symbols.sha256,
    assembly_sha256: manifest.package.assembly_sha256,
  });
  await json(resolve(folder, "global.json"), { sdk: { version: sdk } });
  await refreshManifest(folder, manifest);
  return manifest;
}
async function refreshManifest(folder, manifest) {
  manifest.files = await treeHashes(folder);
  delete manifest.files["manifest.json"];
  await json(resolve(folder, "manifest.json"), manifest);
}
test("bundle integrity detects modified, added and removed files before consumption", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-bundle-test-"));
  try {
    const manifest = await createBundle(folder);
    await verifyBundle(folder);
    await writeFile(resolve(folder, manifest.package.file), "tampered");
    await assert.rejects(verifyBundle(folder));
    await writeFile(resolve(folder, manifest.package.file), "package");
    await writeFile(resolve(folder, "extra"), "extra");
    await assert.rejects(verifyBundle(folder));
    await rm(resolve(folder, "extra"));
    await rm(resolve(folder, manifest.symbols.file));
    await assert.rejects(verifyBundle(folder));
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
test("bundle reports must agree with provenance even when their inventory hashes match", async () => {
  const mutations = [
    ["validation-gate.json", { format_version: 0, status: "failed", sdk, source }],
    [
      "validation-gate.json",
      { format_version: 0, status: "passed", sdk, source: { ...source, commit: "f".repeat(40) } },
    ],
    ["validation-gate.json", { format_version: 0, status: "passed", sdk: "other", source }],
    ["inspection.json", { package_id: packageId, version: "different" }],
    ["global.json", { sdk: { version: "other" } }],
  ];
  for (const [file, content] of mutations) {
    const folder = await mkdtemp(resolve(tmpdir(), "svt-evidence-test-"));
    try {
      const manifest = await createBundle(folder);
      await json(resolve(folder, file), content);
      await refreshManifest(folder, manifest);
      await assert.rejects(verifyBundle(folder));
    } finally {
      await rm(folder, { recursive: true, force: true });
    }
  }
});
test("bundle metadata rejects missing assembly identity and inconsistent validation claims", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-metadata-test-"));
  try {
    const manifest = await createBundle(folder);
    const missingHash = structuredClone(manifest);
    delete missingHash.package.assembly_sha256;
    await json(resolve(folder, "manifest.json"), missingHash);
    await assert.rejects(verifyBundle(folder));
    for (const change of [
      { portable_gate: "failed" },
      { package_inspection: "pending" },
      { consumption: "passed" },
      { required_platforms: ["linux"] },
    ]) {
      await json(resolve(folder, "manifest.json"), {
        ...manifest,
        validation: { ...manifest.validation, ...change },
      });
      await assert.rejects(verifyBundle(folder));
    }
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
test("bundle inventory refuses symbolic links", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-link-test-"));
  try {
    await mkdir(resolve(folder, "real"));
    // Directory junctions are available without developer mode on Windows.
    await symlink(
      resolve(folder, "real"),
      resolve(folder, "alias"),
      process.platform === "win32" ? "junction" : "dir",
    );
    await assert.rejects(treeHashes(folder), /regular files/);
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
test("bundle paths cannot escape the validated artifact", () => {
  for (const name of ["../outside", "/absolute", "a//b", "a\\b", "C:/outside", "a/./b"])
    assert.throws(() => bundlePath(tmpdir(), name));
});
