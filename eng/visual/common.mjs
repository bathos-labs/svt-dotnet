import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
import { PNG } from "pngjs";

export const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
export const tools = resolve(root, "artifacts/visual-tools");
export const pins = JSON.parse(await readFile(new URL("./tools.json", import.meta.url)));
export const run = promisify(execFile);
export const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
export const fileHash = async (path) => hash(await readFile(path));
export async function command(file, args, options = {}) {
  return run(file, args, { cwd: root, timeout: 60000, maxBuffer: 16 * 1024 * 1024, ...options });
}
export function inventory(report) {
  return report.inputs.map((input) => ({
    ...input,
    targets: input.targets.map(({ location, ...target }) => target),
  }));
}
export function assertInventory(expected, report) {
  assert.equal(report.template_available, true, "Unusable template");
  assert.deepEqual(inventory(report), expected.inputs, "Binding inventory changed");
  assert.deepEqual(report.input_groups, expected.input_groups, "Presentation groups changed");
}
export function pixels(expectedBytes, actualBytes) {
  const expected = PNG.sync.read(expectedBytes),
    actual = PNG.sync.read(actualBytes);
  assert.equal(actual.width, expected.width, "Image width");
  assert.equal(actual.height, expected.height, "Image height");
  let changed = 0,
    maximum = 0;
  const diff = new PNG({ width: actual.width, height: actual.height });
  for (let p = 0; p < actual.data.length; p += 4) {
    let delta = 0;
    for (let c = 0; c < 4; c++)
      delta = Math.max(delta, Math.abs(actual.data[p + c] - expected.data[p + c]));
    if (delta) changed++;
    maximum = Math.max(maximum, delta);
    diff.data.set(delta ? [255, 0, 128, 255] : [255, 255, 255, 255], p);
  }
  return {
    changed_pixels: changed,
    maximum_channel_difference: maximum,
    diff: PNG.sync.write(diff),
  };
}
export async function download(url, destination, sha256) {
  await mkdir(dirname(destination), { recursive: true });
  try {
    if ((await fileHash(destination)) === sha256) return;
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  const response = await fetch(url, { signal: AbortSignal.timeout(60000) });
  assert.equal(response.ok, true, `Download ${url}: ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  assert.equal(hash(bytes), sha256, `Archive hash: ${url}`);
  await writeFile(destination, bytes);
}
export const cli = resolve(
  process.env.SVT_CLI_DLL ??
    resolve(root, "src/ScalableVectorTemplates.Cli/bin/Release/net10.0/svt.dll"),
);
export async function inspect(path, { allowStructuralDiagnostics = false } = {}) {
  const args = [cli, "inspect", path, "--format", "json", "--diagnostics", "json"];
  let result;
  try {
    result = await command("dotnet", args);
  } catch (error) {
    if (!allowStructuralDiagnostics || error.code !== 1 || error.killed || error.signal)
      throw error;
    result = error;
  }
  const { stdout, stderr } = result;
  const diagnostics = JSON.parse(stderr).diagnostics;
  assert.ok(Array.isArray(diagnostics), "Missing inspection diagnostics");
  if (!allowStructuralDiagnostics)
    assert.deepEqual(diagnostics, [], "Unexpected inspection diagnostic");
  return JSON.parse(stdout);
}

export function assertSuite(suite) {
  assert.equal(suite.format_version, 0);
  assert.equal(suite.pixel_tolerance, 0);
  assert.equal(suite.font_family, pins.font.family);
  for (const size of [suite.width, suite.height])
    assert.ok(Number.isSafeInteger(size) && size > 0, "Invalid viewport");
  assert.ok(Array.isArray(suite.cases) && suite.cases.length > 0, "Empty authoring suite");
  assert.equal(new Set(suite.cases).size, suite.cases.length, "Duplicate authoring cases");
  for (const name of suite.cases)
    assert.ok(typeof name === "string" && /^[a-z][a-z0-9-]*$/.test(name), "Invalid case name");
}

// Only the observed exporter diagnostic and a successfully read changed inventory
// are capability observations. Tool/process/JSON failures must reject the probe.
export async function observeOptimizedExport(exportSvg, inspectSvg, expected) {
  const result = await exportSvg();
  if (
    /^InkFileExportCmd::do_export_vector: Failed to save Inkscape file to: [^\r\n]+$/.test(
      result.stderr.trim(),
    )
  )
    return { status: "unavailable", reason: result.stderr.trim() };
  assert.equal(result.stderr.trim(), "", "Unexpected export diagnostic");
  const actual = await inspectSvg();
  assert.equal(typeof actual.template_available, "boolean", "Missing template availability");
  assert.ok(Array.isArray(actual.inputs), "Missing inspection inventory");
  try {
    assertInventory(expected, actual);
    return { status: "inventory-passed" };
  } catch (error) {
    if (!(error instanceof assert.AssertionError)) throw error;
    return { status: "inventory-changed", reason: error.message };
  }
}

// Narrowly recognize resource failures; infrastructure failures are never hazard evidence.
export function resourceFailure(engine, error) {
  const message = String(error.message);
  return engine === "chrome"
    ? /(?:EncodingError:|The source image cannot be decoded)/.test(message)
    : /Renderer diagnostics/.test(message) && /is not a path to an image/.test(message);
}
