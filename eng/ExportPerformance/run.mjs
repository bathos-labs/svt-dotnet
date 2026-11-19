import assert from "node:assert/strict";
import { resolve } from "node:path";
import { root, command, sourceIdentity, json } from "../package/common.mjs";

const path = resolve(root, "artifacts/performance/export.json");
const report = { format_version: 0, status: "running" };
await json(path, report);
try {
  assert.equal(process.argv.length, 2, "Usage: node eng/ExportPerformance/run.mjs");
  const source = await sourceIdentity();
  report.source = source;
  report.sdk = (await command("dotnet", ["--version"])).stdout.trim();
  const project = "eng/ExportPerformance/ExportPerformance.csproj";
  await command("dotnet", ["restore", project, "--locked-mode"]);
  await command("dotnet", ["build", project, "-c", "Release", "--no-restore"]);
  const result = await command("dotnet", [
    "eng/ExportPerformance/bin/Release/net10.0/ExportPerformance.dll",
  ]);
  assert.equal(result.stderr, "", "Unexpected measurement diagnostics");
  report.measurements = JSON.parse(result.stdout);
  assert.equal(report.measurements.status, "passed");
  assert.deepEqual(await sourceIdentity(), source, "Source changed during measurements");
  report.status = "passed";
  console.log(result.stdout);
} catch (error) {
  report.status = "failed";
  report.error = ((error.stdout ?? "") + (error.stderr ?? "") + error.message).replaceAll(
    root,
    "<repository>",
  );
  console.error(report.error);
  process.exitCode = 1;
} finally {
  await json(path, report);
  console.log(`Export measurements: ${report.status}; artifacts/performance/export.json`);
}
