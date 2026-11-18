import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { verifyBundle, validateReceipts, fileHash } from "./common.mjs";

assert.equal(
  process.argv.length,
  6,
  "Usage: finalize.mjs <bundle> <ubuntu-receipt> <windows-receipt> <output-json>",
);
const manifest = await verifyBundle(resolve(process.argv[2]));
const manifestHash = await fileHash(resolve(process.argv[2], "manifest.json"));
const receipts = await Promise.all(
  process.argv.slice(3, 5).map(async (p) => JSON.parse(await readFile(resolve(p)))),
);
validateReceipts(manifest, manifestHash, receipts);
await writeFile(
  resolve(process.argv[5]),
  JSON.stringify(
    {
      format_version: 0,
      status: "package-validated",
      bundle_manifest_sha256: manifestHash,
      package: manifest.package,
      symbols: manifest.symbols,
      source: manifest.source,
      consumption: receipts,
      native_rendering: manifest.validation.native_rendering,
      consumer_integration: "pending",
      publication_authorized: false,
    },
    null,
    2,
  ) + "\n",
  { flag: "wx" },
);
console.log(
  "Validated identical package consumption on Ubuntu and Windows; candidate evidence created.",
);
