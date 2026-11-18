import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, mkdtemp } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { release } from "node:os";
import { root, pins, command, inspect, observeOptimizedExport, fileHash } from "./common.mjs";

const inkscape = process.argv[2];
assert.ok(
  inkscape && process.argv.length === 3,
  "Usage: probe-authoring.mjs <Inkscape executable>",
);
const version = (await command(inkscape, ["--version"])).stdout.trim();
assert.ok(version.startsWith(`Inkscape ${pins.inkscape.version} `));
const source = resolve(root, "examples/authoring");
await mkdir(resolve(root, "artifacts"), { recursive: true });
const output = await mkdtemp(resolve(root, "artifacts/native-exploratory-probe-authoring-"));
const report = {
  format_version: 0,
  purpose: "exploratory-probe",
  git_head: (await command("git", ["rev-parse", "HEAD"])).stdout.trim(),
  dirty: Boolean((await command("git", ["status", "--porcelain"])).stdout.trim()),
  tool: { ...pins.inkscape, actual_version: version, binary_sha256: await fileHash(inkscape) },
  environment: {
    platform: process.platform,
    release: release(),
    architecture: process.arch,
    node: process.version,
  },
  scope: "Characterize optimized SVG export; no release verdict or GUI claim.",
  hashes: {},
};
for (const name of ["probe-authoring.mjs", "common.mjs", "tools.json"])
  report.hashes["eng/visual/" + name] = await fileHash(resolve(root, "eng/visual", name));
for (const name of ["input.svg", "inventory.json"])
  report.hashes["text-card/" + name] = await fileHash(resolve(source, "text-card", name));
const optimized = resolve(output, "optimized.svg");
try {
  const expected = JSON.parse(await readFile(resolve(source, "text-card/inventory.json")));
  report.optimized = await observeOptimizedExport(
    () =>
      command(inkscape, [
        resolve(source, "text-card/input.svg"),
        "--export-type=svg",
        "--export-extension=org.inkscape.output.scour_inkscape",
        "--export-filename=" + optimized,
      ]),
    () => inspect(optimized, { allowStructuralDiagnostics: true }),
    expected,
  );
  if (report.optimized.reason)
    report.optimized.reason = report.optimized.reason.replaceAll(root, "<repository>");
  report.status = "completed";
} catch (error) {
  report.status = "failed";
  report.error = String(error.message).replaceAll(root, "<repository>");
  process.exitCode = 1;
}
await writeFile(resolve(output, "report.json"), JSON.stringify(report, null, 2) + "\n");
console.log(`${report.status}: ${relative(root, output)}/report.json`);
