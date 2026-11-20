import assert from "node:assert/strict";
import { readFile, cp, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import {
  command,
  verifyBundle,
  fileHash,
  json,
  environment,
  nonce,
  validateReceipt,
} from "../package/common.mjs";

assert.ok(
  process.argv.length >= 3 && process.argv.length <= 4,
  "Usage: rehearse.mjs <bundle> [Inkscape executable]",
);
import { rehearseArchive } from "./rehearsal-evidence.mjs";

const bundle = resolve(process.argv[2]);
const manifest = await verifyBundle(bundle);
const report = {
  format_version: 0,
  purpose: "nonpublishing-rehearsal",
  status: "running",
  source: manifest.source,
  bundle_manifest_sha256: await fileHash(resolve(bundle, "manifest.json")),
  products: manifest.products,
  environment: await environment(),
  publication_authorized: false,
  remote_writes: false,
};
// Deliberately no imports of the production GitHub/registry transports.
try {
  for (const [name, args] of [
    ["consumption", ["eng/package/consume.mjs", bundle]],
    ["recovery", ["--test", "eng/release/publication.test.mjs"]],
    ["native", ["eng/release/native.mjs", bundle, ...process.argv.slice(3)]],
  ]) {
    const result = await command(process.execPath, args, { timeout: 1800000 });
    process.stdout.write(result.stdout);
    process.stderr.write(result.stderr);
    report[name] = "passed";
  }
  const current = JSON.parse(await readFile("artifacts/package-consumption-current.json"));
  const consumption = JSON.parse(await readFile(current.report));
  validateReceipt(manifest, report.bundle_manifest_sha256, consumption, process.platform);
  report.consumption_receipt = current.report;
  report.native_receipt = `artifacts/native-candidate-${process.platform}.json`;
  const retained = resolve("artifacts", `rehearsal-archive-${nonce()}`);
  const content = resolve(retained, "content");
  await mkdir(retained);
  await cp(bundle, resolve(content, "bundle"), { recursive: true });
  await cp(current.report, resolve(content, "consumption.json"));
  const native = JSON.parse(await readFile(report.native_receipt));
  await cp(report.native_receipt, resolve(content, "native-candidate.json"));
  for (const item of native.reports) {
    const folder = item.path.slice(0, item.path.lastIndexOf("/"));
    await cp(folder, resolve(content, folder), { recursive: true });
  }
  report.archive = await rehearseArchive(
    content,
    retained,
    bundle,
    manifest,
    report.bundle_manifest_sha256,
  );
  report.visual_review = "required-separately";
  report.other_platforms = "CI required; this rehearsal proves only the recorded host";
  report.status = "passed";
} catch (error) {
  report.status = "failed";
  report.error = error.message;
  process.stderr.write((error.stdout ?? "") + (error.stderr ?? ""));
  process.exitCode = 1;
} finally {
  await json("artifacts/release-rehearsal.json", report);
  console.log(JSON.stringify(report));
}
