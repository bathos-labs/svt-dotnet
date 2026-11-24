import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, mkdtemp, rm, cp } from "node:fs/promises";
import { resolve, dirname } from "node:path";
import {
  root,
  command,
  verifyBundle,
  fileHash,
  sourceIdentity,
  json,
  treeHashes,
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
const toolInspection = JSON.parse(await readFile(resolve(bundle, "inspection-cli.json")));
const hosts = await mkdtemp(resolve(root, "artifacts/native-candidate-hosts-"));
const cliFolder = resolve(hosts, "tool");
const testFolder = resolve(hosts, "tests");
const hostEnv = {
  ...process.env,
  SVT_CLI_DLL: resolve(cliFolder, "svt.dll"),
  SVT_AUTHORING_TEST_HOST: resolve(testFolder, "ScalableVectorTemplates.Cli.Tests.dll"),
};
const report = {
  format_version: 0,
  status: "running",
  purpose: "release-check",
  source: manifest.source,
  products: manifest.products,
  bundle_manifest_sha256: await fileHash(resolve(bundle, "manifest.json")),
  platform: process.platform,
  reports: [],
  visual_review: "required",
};
async function check(script, args, comparisons) {
  const result = await command(process.execPath, [script, ...args], {
    timeout: 600000,
    env: hostEnv,
  });
  process.stdout.write(result.stdout);
  assert.equal(result.stderr.trim(), "", "Native runner diagnostics");
  const path = result.stdout.match(/^passed: (artifacts\/[^\r\n]+?\/report\.json)/m)?.[1];
  assert.ok(path, "Native runner did not produce a passing report");
  const evidence = JSON.parse(await readFile(resolve(root, path)));
  assert.equal(evidence.purpose, "release-check");
  assert.equal(evidence.status, "passed");
  assert.equal(evidence.git_head, manifest.source.commit);
  assert.equal(evidence.dirty, manifest.source.dirty);
  assert.equal(evidence.hashes["library.dll"], manifest.products.library.assembly_sha256);
  assert.equal(evidence.hashes["svt.dll"], manifest.products.cli.assembly_sha256);
  if (comparisons !== undefined) assert.equal(evidence.comparisons.length, comparisons);
  report.reports.push({ path, sha256: await fileHash(resolve(root, path)), comparisons });
  return dirname(path);
}
let mount;
try {
  await mkdir(cliFolder);
  if (process.platform === "darwin")
    await cp(
      resolve(root, "tests/ScalableVectorTemplates.Cli.Tests/bin/Release/net10.0"),
      testFolder,
      { recursive: true },
    );
  // Native tools and API/CLI comparison hosts receive a private copy of the exact payload.
  // Builds in an IDE or another terminal cannot replace these candidate assemblies.
  for (const entry of toolInspection.package_files.filter((p) =>
    p.startsWith("tools/net10.0/any/"),
  )) {
    const name = entry.slice("tools/net10.0/any/".length);
    const bytes = (
      await command("unzip", ["-p", resolve(bundle, manifest.products.cli.file), entry], {
        encoding: "buffer",
      })
    ).stdout;
    await writeFile(resolve(cliFolder, name), bytes);
    if (process.platform === "darwin" && (name.endsWith(".dll") || name.endsWith(".pdb")))
      await writeFile(resolve(testFolder, name), bytes);
  }
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
  for (const item of report.reports)
    item.files = await treeHashes(dirname(resolve(root, item.path)));
  report.status = "passed";
} catch (error) {
  report.status = "failed";
  report.error = error.message;
  throw error;
} finally {
  try {
    await rm(hosts, { recursive: true, force: true });
    if (mount) {
      await command("hdiutil", ["detach", mount]);
      await rm(mount, { recursive: true });
    }
  } catch (error) {
    report.status = "failed";
    report.cleanup_error = error.message;
    throw error;
  } finally {
    if (report.status === "running") report.status = "failed";
    await json(resolve(root, `artifacts/native-candidate-${process.platform}.json`), report);
  }
}
