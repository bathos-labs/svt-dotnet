import assert from "node:assert/strict";
import { resolve } from "node:path";
import { verifyBundle } from "../package/common.mjs";
import { verifyRelease } from "./policy.mjs";
import { verifyRetainedEvidence } from "./evidence.mjs";

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
assert.equal(manifest.version, info.version);
assert.equal(manifest.source.commit, info.commit);
await verifyRetainedEvidence(transfer, resolve(process.argv[3]), manifest);
console.log(
  "Release candidate passed portable, package, Ubuntu/Windows consumption and native checks. Environment approval supplies visual review and publication authorization.",
);
