import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";
import test from "node:test";

for (const [name, args, message] of [
  ["invalid arguments", ["unexpected"], "Usage:"],
  ["source inspection failure", [], "Source inspection failed"],
]) {
  test(`export measurements invalidate prior success on ${name}`, async (t) => {
    const root = await mkdtemp(join(tmpdir(), "svt-export-evidence-"));
    t.after(() => rm(root, { recursive: true, force: true }));
    await mkdir(join(root, "eng/ExportPerformance"), { recursive: true });
    await mkdir(join(root, "eng/package"), { recursive: true });
    await mkdir(join(root, "artifacts/performance"), { recursive: true });
    const runner = join(root, "eng/ExportPerformance/run.mjs");
    await writeFile(runner, await readFile(new URL("./run.mjs", import.meta.url)));
    // Fail before build/measurement. The real wrapper must replace the stale success receipt.
    await writeFile(
      join(root, "eng/package/common.mjs"),
      `
      import { writeFile } from "node:fs/promises";
      import { fileURLToPath } from "node:url";
      export const root = fileURLToPath(new URL("../..", import.meta.url));
      export const json = (path, value) => writeFile(path, JSON.stringify(value));
      export async function sourceIdentity() { throw new Error("Source inspection failed"); }
      export async function command() { throw new Error("Unexpected tool execution"); }
    `,
    );
    const receipt = join(root, "artifacts/performance/export.json");
    await writeFile(receipt, JSON.stringify({ status: "passed", stale: true }));
    await assert.rejects(
      promisify(execFile)(process.execPath, [runner, ...args]),
      (error) => error.code === 1 && error.stderr.includes(message),
    );
    const report = JSON.parse(await readFile(receipt, "utf8"));
    assert.equal(report.status, "failed");
    assert.equal(report.stale, undefined);
    assert.ok(report.error.includes(message));
  });
}
