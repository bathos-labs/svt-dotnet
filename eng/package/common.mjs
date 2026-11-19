import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { readFile, writeFile, mkdir, lstat, readlink, readdir, realpath } from "node:fs/promises";
import { dirname, resolve, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { platform, release, arch } from "node:os";
import { releaseVersion } from "../release/version.mjs";

export const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
export const packageId = "Bathos.Graphics.SvgTemplates";
export const sdk = JSON.parse(await readFile(resolve(root, "global.json"))).sdk.version;
export const tool = resolve(
  root,
  "eng/PackageValidation/bin/Release/net10.0/PackageValidation.dll",
);
export const run = promisify(execFile);
export const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
export const fileHash = async (path) => hash(await readFile(path));
export const nonce = () => randomUUID().replaceAll("-", "").slice(0, 16);
export const smokeChecks = [
  "loaded-package-identity",
  "inspection-text-omission-empty-ownership",
  "caller-owned-utf8-output",
  "pooled-output-consumption-lifetime",
  "raw-paint-shared-and-default-evidence",
  "encoded-images-references-and-policy-recheck",
  "structured-rejection-budgets-and-policy",
  "cancellation",
];
export async function environment() {
  const result = {
    platform: platform(),
    release: release(),
    architecture: arch(),
    node: process.version,
  };
  if (platform() === "linux") {
    const values = Object.fromEntries(
      (await readFile("/etc/os-release", "utf8"))
        .split("\n")
        .filter((line) => /^[A-Z_]+=/.test(line))
        .map((line) => {
          const equals = line.indexOf("=");
          return [line.slice(0, equals), line.slice(equals + 1).replace(/^"(.*)"$/, "$1")];
        }),
    );
    result.distribution = { id: values.ID, version: values.VERSION_ID };
  }
  return result;
}
export async function command(file, args, options = {}) {
  return run(file, args, { cwd: root, timeout: 600000, maxBuffer: 32 * 1024 * 1024, ...options });
}
export async function json(path, value) {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, JSON.stringify(value, null, 2) + "\n");
}
export async function sourceIdentity() {
  const commit = (await command("git", ["rev-parse", "HEAD"])).stdout.trim();
  const dirty = Boolean(
    (await command("git", ["status", "--porcelain", "--untracked-files=all"])).stdout.trim(),
  );
  const names = [
    ...new Set(
      (
        await command("git", ["ls-files", "--cached", "--others", "--exclude-standard", "-z"])
      ).stdout
        .split("\0")
        .filter(Boolean),
    ),
  ].sort();
  const files = {};
  for (const name of names) {
    const path = resolve(root, name);
    try {
      const stat = await lstat(path);
      files[name] = stat.isSymbolicLink() ? hash(await readlink(path)) : await fileHash(path);
    } catch (error) {
      if (error.code !== "ENOENT") throw error;
      files[name] = "deleted";
    }
  }
  return { commit, dirty, fingerprint: hash(JSON.stringify(files)) };
}
export function requireGate(gate, source, review) {
  assert.equal(gate.format_version, 0);
  assert.equal(gate.status, "passed", "Run the full validation gate first");
  assert.deepEqual(gate.source, source, "Validation receipt does not match the source tree");
  if (!review)
    assert.equal(
      source.dirty,
      false,
      "Handoff preparation requires a clean committed checkout; use review for uncommitted validation",
    );
}
export function packageVersion(source, review, token = nonce()) {
  assert.match(source.commit, /^[a-f0-9]{40}$/);
  assert.match(token, /^[a-f0-9]{16}$/);
  return `0.1.0-${review ? "review" : "preview"}.g${source.commit.slice(0, 12)}.n${token}`;
}
export async function treeHashes(folder) {
  const files = {};
  async function visit(path) {
    for (const entry of await readdir(path, { withFileTypes: true })) {
      const child = resolve(path, entry.name);
      if (entry.isDirectory()) await visit(child);
      else {
        assert.ok(entry.isFile(), "Bundle may contain only regular files");
        files[relative(folder, child).split(sep).join("/")] = await fileHash(child);
      }
    }
  }
  await visit(folder);
  return Object.fromEntries(Object.entries(files).sort(([a], [b]) => a.localeCompare(b)));
}
export function bundlePath(folder, name) {
  assert.ok(
    typeof name === "string" &&
      name.length > 0 &&
      !name.includes("\\") &&
      !name.includes(":") &&
      name.split("/").every((p) => p && p !== "." && p !== ".."),
    "Unsafe bundle path",
  );
  return resolve(folder, name);
}
export async function verifyBundle(folder) {
  const manifest = JSON.parse(await readFile(resolve(folder, "manifest.json")));
  assert.equal(manifest.format_version, 0);
  assert.equal(manifest.package.id, packageId);
  if (manifest.purpose === "release-candidate") {
    releaseVersion(manifest.package.version);
    assert.equal(manifest.release_tag, `v${manifest.package.version}`);
    assert.equal(manifest.source.dirty, false);
  } else {
    assert.match(
      manifest.package.version,
      /^0\.1\.0-(?:preview|review)\.g[a-f0-9]{12}\.n[a-f0-9]{16}$/,
    );
    assert.ok(
      manifest.package.version.startsWith(
        `0.1.0-${manifest.purpose === "review" ? "review" : "preview"}.g${manifest.source.commit.slice(0, 12)}.`,
      ),
    );
    assert.equal(manifest.release_tag, undefined);
  }
  assert.equal(manifest.sdk, sdk, "SDK pin changed; validate with the bundle's SDK");
  assert.match(manifest.source.commit, /^[a-f0-9]{40}$/);
  assert.match(manifest.source.fingerprint, /^[a-f0-9]{64}$/);
  assert.equal(typeof manifest.source.dirty, "boolean");
  assert.equal(manifest.target_framework, "net10.0");
  assert.ok(["review", "handoff-candidate", "release-candidate"].includes(manifest.purpose));
  assert.match(manifest.package.sha256, /^[a-f0-9]{64}$/);
  assert.match(manifest.package.assembly_sha256, /^[a-f0-9]{64}$/);
  assert.match(manifest.symbols.sha256, /^[a-f0-9]{64}$/);
  assert.equal(manifest.package.file, `packages/${packageId}.${manifest.package.version}.nupkg`);
  assert.equal(manifest.symbols.file, `packages/${packageId}.${manifest.package.version}.snupkg`);
  const actual = await treeHashes(folder);
  delete actual["manifest.json"];
  assert.deepEqual(actual, manifest.files, "Bundle contents changed");
  for (const name of Object.keys(manifest.files)) bundlePath(folder, name);
  assert.equal(manifest.package.sha256, actual[manifest.package.file]);
  assert.equal(manifest.symbols.sha256, actual[manifest.symbols.file]);
  const readJson = async (name) => JSON.parse(await readFile(bundlePath(folder, name)));
  const gate = await readJson("validation-gate.json");
  requireGate(gate, manifest.source, manifest.purpose === "review");
  assert.equal(gate.sdk, manifest.sdk);
  const inspection = await readJson("inspection.json");
  assert.equal(inspection.package_id, manifest.package.id);
  assert.equal(inspection.version, manifest.package.version);
  assert.equal(inspection.commit, manifest.source.commit);
  assert.equal(inspection.package_sha256, manifest.package.sha256);
  assert.equal(inspection.symbols_sha256, manifest.symbols.sha256);
  assert.equal(inspection.assembly_sha256, manifest.package.assembly_sha256);
  assert.equal((await readJson("global.json")).sdk.version, manifest.sdk);
  assert.equal(manifest.validation.portable_gate, "passed");
  assert.equal(manifest.validation.package_inspection, "passed");
  assert.equal(manifest.validation.consumption, "pending");
  assert.deepEqual(manifest.validation.required_platforms, ["linux", "win32"]);
  return manifest;
}
export async function verifyRestoreDirectories(assets, feed, cache) {
  // NuGet expands Windows short names; compare the actual directories, not their spellings.
  for (const [paths, expected] of [
    [Object.keys(assets.project.restore.sources), feed],
    [Object.keys(assets.packageFolders), cache],
  ]) {
    assert.deepEqual(
      await Promise.all(paths.map((path) => realpath(path))),
      [await realpath(expected)],
      "Restore used a different source or cache directory",
    );
  }
}
export function validateReceipts(manifest, manifestHash, receipts) {
  assert.match(manifestHash, /^[a-f0-9]{64}$/);
  assert.ok(
    ["handoff-candidate", "release-candidate"].includes(manifest.purpose),
    "Review packages cannot become candidates",
  );
  assert.equal(manifest.source.dirty, false);
  assert.deepEqual(receipts.map((r) => r.environment.platform).sort(), ["linux", "win32"]);
  for (const receipt of receipts) {
    assert.equal(receipt.format_version, 0);
    assert.equal(receipt.status, "passed");
    assert.equal(
      receipt.bundle_manifest_sha256,
      manifestHash,
      "Consumption used a different bundle",
    );
    assert.equal(receipt.purpose, manifest.purpose);
    assert.equal(receipt.sdk, manifest.sdk);
    assert.deepEqual(receipt.source, manifest.source);
    assert.deepEqual(receipt.symbols, manifest.symbols);
    assert.deepEqual(
      receipt.package,
      manifest.package,
      "Consumption did not validate this exact artifact",
    );
    assert.equal(receipt.empty_feed_rejected, true);
    assert.equal(
      receipt.distribution,
      "local-artifact",
      "Candidate validation requires the original artifact",
    );
    assert.deepEqual(receipt.checks, smokeChecks);
    if (receipt.environment.platform === "linux")
      assert.equal(
        receipt.environment.distribution?.id,
        "ubuntu",
        "Ubuntu consumption is required",
      );
  }
}
