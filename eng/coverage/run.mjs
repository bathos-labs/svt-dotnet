import assert from "node:assert/strict";
import { mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { command, root, json, sourceIdentity, environment, hash } from "../package/common.mjs";
import { normalizeCoverage } from "./evidence.mjs";

assert.equal(process.argv.length, 2, "Usage: node eng/coverage/run.mjs");
const output = resolve(root, "artifacts/coverage");
// This directory belongs exclusively to this serial coverage command, never to package evidence.
await rm(output, { recursive: true, force: true });
await mkdir(resolve(output, "normalized"), { recursive: true });
const source = await sourceIdentity();
const files = new Set(
  (
    await command("git", ["ls-files", "--cached", "--others", "--exclude-standard", "--", "src"])
  ).stdout
    .trim()
    .split("\n"),
);
const report = {
  format_version: 0,
  status: "running",
  source,
  environment: await environment(),
  collector: (await readFile(resolve(root, "Directory.Packages.props"), "utf8")).match(
    /Include="coverlet.MTP" Version="([^"]+)"/,
  )[1],
  suites: [],
};
async function execute(name, args) {
  try {
    const result = await command("dotnet", args, {
      // Surface collector unload failures instead of accepting partial evidence.
      env: { ...process.env, COVERLET_MTP_EXCEPTIONLOG_ENABLED: "1" },
    });
    await writeFile(resolve(output, `${name}.log`), result.stdout + result.stderr);
    process.stdout.write(result.stdout);
    process.stderr.write(result.stderr);
  } catch (error) {
    await writeFile(
      resolve(output, `${name}.log`),
      (error.stdout ?? "") + (error.stderr ?? "") + error.message,
    );
    throw error;
  }
}
try {
  for (const [name, project, expected] of [
    ["library", "ScalableVectorTemplates.Tests", ["ScalableVectorTemplates"]],
    [
      "cli",
      "ScalableVectorTemplates.Cli.Tests",
      ["ScalableVectorTemplates", "ScalableVectorTemplates.Cli"],
    ],
  ]) {
    const assembly = `tests/${project}/bin/Release/net10.0/${project}.dll`;
    const results = `artifacts/coverage/${name}-results`;
    const args = [
      assembly,
      "--coverlet",
      "--coverlet-output-format",
      "lcov",
      "--coverlet-include",
      "[ScalableVectorTemplates]*",
      "--coverlet-exclude-by-file",
      "**/obj/**",
      "--coverlet-exclude-by-attribute",
      "GeneratedCodeAttribute",
      "--results-directory",
      results,
      "--coverlet-file-prefix",
      name,
    ];
    if (name === "cli") args.push("--coverlet-include", "[svt]*");
    await execute(name, args);
    const reports = (await readdir(resolve(root, results))).filter((file) =>
      file.endsWith(".info"),
    );
    assert.equal(reports.length, 1, `Expected one fresh LCOV report for ${name}`);
    const raw = await readFile(resolve(root, results, reports[0]), "utf8");
    const checked = normalizeCoverage(raw, root, files, expected);
    await writeFile(resolve(output, "normalized", `${name}.info`), checked.text);
    report.suites.push({
      name,
      totals: checked.totals,
      files: checked.files,
      sha256: hash(checked.text),
    });
  }
  assert.deepEqual(await sourceIdentity(), source, "Source changed during coverage collection");
  report.status = "passed";
} catch (error) {
  report.status = "failed";
  report.error = error.message.replaceAll(root, "<repository>");
  console.error(report.error);
  process.exitCode = 1;
} finally {
  await json(resolve(output, "report.json"), report);
  console.log(
    `Coverage evidence: ${report.status}; artifacts/coverage/report.json (no percentage threshold).`,
  );
}
