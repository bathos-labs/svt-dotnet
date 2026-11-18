import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { resolve } from "node:path";
import { fileHash, verifyBundle, validateReceipts } from "../package/common.mjs";
import { verifyRelease } from "./policy.mjs";
import { verifyNative } from "./evidence.mjs";

assert.equal(
  process.argv.length,
  4,
  "Usage: verify-candidate.mjs <transfer> <native-evidence-root>",
);
const transfer = resolve(process.argv[2]);
const bundle = resolve(transfer, "bundle");
const manifest = await verifyBundle(bundle);
const info = await verifyRelease({ tag: manifest.release_tag, remote: true, requireTag: true });
assert.equal(manifest.purpose, "release-candidate");
assert.equal(manifest.package.version, info.version);
assert.equal(manifest.source.commit, info.commit);
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
  const evidenceRoot = resolve(process.argv[3]);
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
}
console.log(
  "Release candidate passed portable, package, Ubuntu/Windows consumption and native checks. Environment approval supplies visual review and publication authorization.",
);
