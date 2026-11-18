import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, mkdtemp, rm } from "node:fs/promises";
import { resolve, dirname } from "node:path";
import {
  root,
  command,
  verifyBundle,
  fileHash,
  hash,
  sourceIdentity,
  json,
  packageId,
} from "../package/common.mjs";
import { pins, tools, download } from "../visual/common.mjs";

assert.ok(
  process.argv.length >= 3 && process.argv.length <= 4,
  "Usage: native.mjs <bundle> [Inkscape executable]",
);
assert.ok(["darwin-arm64", "linux-x64"].includes(`${process.platform}-${process.arch}`));
const bundle = resolve(process.argv[2]);
const manifest = await verifyBundle(bundle);
assert.deepEqual(
  await sourceIdentity(),
  manifest.source,
  "Native checks require the candidate source snapshot",
);
const artifact = resolve(bundle, manifest.package.file);
const assembly = (
  await command("unzip", ["-p", artifact, `lib/net10.0/${packageId}.dll`], { encoding: "buffer" })
).stdout;
assert.equal(hash(assembly), manifest.package.assembly_sha256);
// Native hosts and tests exercise the exact packaged DLL, not their independently built copy.
for (const folder of [
  "src/Bathos.Graphics.SvgTemplates.Cli",
  "tests/Bathos.Graphics.SvgTemplates.Cli.Tests",
])
  await writeFile(resolve(root, folder, `bin/Release/net10.0/${packageId}.dll`), assembly);
const report = {
  format_version: 0,
  status: "running",
  purpose: "release-check",
  source: manifest.source,
  package: manifest.package,
  bundle_manifest_sha256: await fileHash(resolve(bundle, "manifest.json")),
  platform: process.platform,
  reports: [],
  visual_review: "required",
};
async function check(script, args, comparisons) {
  const result = await command(process.execPath, [script, ...args], { timeout: 600000 });
  process.stdout.write(result.stdout);
  assert.equal(result.stderr.trim(), "", "Native runner diagnostics");
  const path = result.stdout.match(/^passed: (artifacts\/[^\r\n]+?\/report\.json)/m)?.[1];
  assert.ok(path, "Native runner did not produce a passing report");
  const evidence = JSON.parse(await readFile(resolve(root, path)));
  assert.equal(evidence.purpose, "release-check");
  assert.equal(evidence.status, "passed");
  assert.equal(evidence.git_head, manifest.source.commit);
  assert.equal(evidence.dirty, manifest.source.dirty);
  assert.equal(evidence.hashes["library.dll"], manifest.package.assembly_sha256);
  if (comparisons !== undefined) assert.equal(evidence.comparisons.length, comparisons);
  report.reports.push({ path, sha256: await fileHash(resolve(root, path)), comparisons });
  return dirname(path);
}
let mount;
try {
  await check("eng/visual/release-render.mjs", [], 38);
  if (process.platform === "darwin") {
    let inkscape = process.argv[3];
    if (!inkscape) {
      await mkdir(tools, { recursive: true });
      const dmg = resolve(tools, "Inkscape.dmg");
      await download(pins.inkscape.url, dmg, pins.inkscape.sha256);
      mount = await mkdtemp(resolve(tools, "inkscape-mount-"));
      await command("hdiutil", ["attach", "-nobrowse", "-readonly", "-mountpoint", mount, dmg]);
      inkscape = resolve(mount, "Inkscape.app/Contents/MacOS/inkscape");
    }
    const corpus = await check("eng/visual/release-authoring.mjs", [inkscape]);
    for (const mode of ["native", "plain"])
      await check(
        "eng/visual/release-render.mjs",
        [resolve(corpus, mode), "examples/authoring"],
        46,
      );
    for (const mode of ["native-geometry", "native-paint", "plain-geometry", "plain-paint"])
      await check("eng/visual/release-render.mjs", [resolve(corpus, mode)], 14);
  }
  await verifyBundle(bundle);
  assert.deepEqual(await sourceIdentity(), manifest.source);
  report.status = "passed";
} finally {
  if (mount) {
    await command("hdiutil", ["detach", mount]);
    await rm(mount, { recursive: true });
  }
  if (report.status === "running") report.status = "failed";
  await json(resolve(root, `artifacts/native-candidate-${process.platform}.json`), report);
}
