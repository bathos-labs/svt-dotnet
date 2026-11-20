import assert from "node:assert/strict";
import { cp, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import { verifyBundle, json, nonce } from "../package/common.mjs";
import { verifyRetainedEvidence } from "./evidence.mjs";
import { rehearseArchive } from "./rehearsal-evidence.mjs";

assert.equal(process.argv.length, 4, "Usage: rehearse-archive.mjs <transfer> <native-root>");
const transfer = resolve(process.argv[2]),
  native = resolve(process.argv[3]),
  bundle = resolve(transfer, "bundle");
const manifest = await verifyBundle(bundle);
const manifestHash = await verifyRetainedEvidence(transfer, native, manifest);
const retained = resolve("artifacts", `rehearsal-archive-${nonce()}`),
  content = resolve(retained, "content");
await mkdir(retained);
await cp(transfer, resolve(content, "transfer"), { recursive: true });
await cp(native, resolve(content, "native"), { recursive: true });
const report = {
  format_version: 0,
  purpose: "nonpublishing-rehearsal",
  status: "passed",
  publication_authorized: false,
  remote_writes: false,
  bundle_manifest_sha256: manifestHash,
  products: manifest.products,
  archive: await rehearseArchive(content, retained, bundle, manifest, manifestHash),
};
await json("artifacts/release-rehearsal.json", report);
console.log(JSON.stringify(report));
