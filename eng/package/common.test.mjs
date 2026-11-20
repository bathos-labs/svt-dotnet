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
  verifyRestoreDirectories,
  json,
  sdk,
  packageId,
  productIds,
  toolChecks,
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
    packageVersion("1.0.0-rc.1", source, true, "0123456789abcdef"),
    /^1\.0\.0-review\.g0123456789ab\./,
  );
  assert.match(
    packageVersion("1.0.0-rc.1", source, false, "0123456789abcdef"),
    /^1\.0\.0-preview\.g0123456789ab\./,
  );
  assert.notEqual(
    packageVersion("1.0.0-rc.1", source, false),
    packageVersion("1.0.0-rc.1", source, false),
  );
  assert.ok(
    packageVersion("1.0.0-rc.1", source, true, "0000000000000000").endsWith(".n0000000000000000"),
  );
});
test("development identities follow the shipping numeric version and reject aliases", () => {
  for (const version of ["0.1.0-beta.1", "1.0.0-rc.1", "2.3.4", "12.34.56-rc.2"])
    for (const review of [true, false])
      assert.equal(
        packageVersion(version, source, review, "0123456789abcdef"),
        `${version.split("-")[0]}-${review ? "review" : "preview"}.g0123456789ab.n0123456789abcdef`,
      );
  for (const version of ["01.0.0", "1.0", "1.0.0.0", "1.0.0+alias", "1.0.0-review.x"])
    assert.throws(() => packageVersion(version, source, true));
  assert.throws(() => packageVersion("1.0.0", { ...source, commit: "bad" }, true));
  assert.throws(() => packageVersion("1.0.0", source, true, "bad"));
});
test("handoff needs both platforms to consume the same artifact, not a rebuilt version", () => {
  const manifest = {
    purpose: "handoff-candidate",
    source,
    sdk,
    products: { library: { sha256: "original" }, cli: { sha256: "cli" } },
  };
  const manifestHash = "b".repeat(64);
  const receipts = ["linux", "win32"].map((platform) => ({
    format_version: 0,
    status: "passed",
    bundle_manifest_sha256: manifestHash,
    purpose: manifest.purpose,
    source,
    sdk,
    products: manifest.products,
    tool: { status: "passed", checks: toolChecks },
    environment: {
      platform,
      ...(platform === "linux" ? { distribution: { id: "ubuntu", version: "24.04" } } : {}),
    },
    empty_feed_rejected: true,
    distribution: "local-artifact",
    checks: [...smokeChecks],
  }));
  validateReceipts(manifest, manifestHash, receipts);
  for (const change of [
    { status: "failed" },
    { products: { library: { sha256: "rebuilt" } } },
    { tool: { status: "passed", checks: [] } },
    { bundle_manifest_sha256: "c".repeat(64) },
    { format_version: 1 },
    { environment: { platform: "linux" } },
    { empty_feed_rejected: false },
    { distribution: undefined },
    { distribution: "nuget.org" },
    { distribution: "unknown" },
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
async function createBundle(folder, purpose = "review") {
  const version =
    purpose === "release-candidate"
      ? "0.1.0-beta.1"
      : packageVersion("1.0.0-rc.1", source, true);
  await mkdir(resolve(folder, "packages"));
  const products = {};
  for (const [role, id] of Object.entries(productIds)) {
    const file = `packages/${id}.${version}.nupkg`;
    const symbols = `packages/${id}.${version}.snupkg`;
    await writeFile(resolve(folder, file), role + "package");
    await writeFile(resolve(folder, symbols), role + "symbols");
    products[role] = {
      id,
      version,
      file,
      sha256: hash(role + "package"),
      assembly_sha256: hash(role + "assembly"),
      pdb_sha256: hash(role + "pdb"),
      symbols: { file: symbols, sha256: hash(role + "symbols") },
    };
  }
  const manifest = {
    format_version: 0,
    sdk,
    target_framework: "net10.0",
    purpose,
    ...(purpose === "release-candidate" ? { release_tag: `v${version}` } : {}),
    source,
    version,
    products,
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
  for (const [role, product] of Object.entries(products))
    await json(resolve(folder, `inspection-${role}.json`), {
      package_id: product.id,
      version,
      commit: source.commit,
      package_sha256: product.sha256,
      symbols_sha256: product.symbols.sha256,
      assembly_sha256: product.assembly_sha256,
      pdb_sha256: product.pdb_sha256,
      ...(role === "cli"
        ? {
            bundled_library_sha256: products.library.assembly_sha256,
            bundled_library_pdb_sha256: products.library.pdb_sha256,
          }
        : {}),
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
test("release bundles require a public version, matching tag and clean source", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-release-bundle-test-"));
  try {
    const manifest = await createBundle(folder, "release-candidate");
    await verifyBundle(folder);
    for (const change of [
      { release_tag: undefined },
      { release_tag: "v0.1.0-beta.2" },
      { source: { ...source, dirty: true } },
      { purpose: "handoff-candidate" },
      { version: packageVersion("1.0.0-rc.1", source, false) },
    ]) {
      await json(resolve(folder, "manifest.json"), { ...manifest, ...change });
      await assert.rejects(verifyBundle(folder));
    }
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
test("development bundles reject numeric aliases and mismatched purpose or commit", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-development-bundle-test-"));
  try {
    const manifest = await createBundle(folder);
    await verifyBundle(folder);
    for (const change of [
      { version: manifest.version.replace("1.0.0-", "01.0.0-") },
      { version: manifest.version.replace("review", "preview") },
      { version: manifest.version.replace("g0123456789ab", "gffffffffffff") },
      { purpose: "handoff-candidate" },
    ]) {
      await json(resolve(folder, "manifest.json"), { ...manifest, ...change });
      await assert.rejects(verifyBundle(folder));
    }
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
test("bundle integrity detects modified, added and removed files before consumption", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-bundle-test-"));
  try {
    const manifest = await createBundle(folder);
    await verifyBundle(folder);
    await writeFile(resolve(folder, manifest.products.library.file), "tampered");
    await assert.rejects(verifyBundle(folder));
    await writeFile(resolve(folder, manifest.products.library.file), "librarypackage");
    await writeFile(resolve(folder, "extra"), "extra");
    await assert.rejects(verifyBundle(folder));
    await rm(resolve(folder, "extra"));
    await rm(resolve(folder, manifest.products.library.symbols.file));
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
    ["inspection-cli.json", { package_id: packageId, version: "different" }],
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
    delete missingHash.products.library.assembly_sha256;
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
test("restore isolation resolves directory aliases but rejects unrelated or extra directories", async () => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-restore-path-test-"));
  try {
    const feed = resolve(folder, "feed");
    const cache = resolve(folder, "cache");
    const alias = resolve(folder, "feed-alias");
    await mkdir(feed);
    await mkdir(cache);
    await symlink(feed, alias, process.platform === "win32" ? "junction" : "dir");
    const assets = (sources, folders) => ({
      project: { restore: { sources: Object.fromEntries(sources.map((path) => [path, {}])) } },
      packageFolders: Object.fromEntries(folders.map((path) => [path, {}])),
    });
    await verifyRestoreDirectories(assets([alias], [cache]), feed, cache);
    await verifyRestoreDirectories(assets([feed], [cache]), alias, cache);
    await assert.rejects(verifyRestoreDirectories(assets([cache], [cache]), feed, cache));
    await assert.rejects(verifyRestoreDirectories(assets([feed], [feed]), feed, cache));
    await assert.rejects(verifyRestoreDirectories(assets([feed, alias], [cache]), feed, cache));
    await assert.rejects(verifyRestoreDirectories(assets([feed], [cache, feed]), feed, cache));
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
