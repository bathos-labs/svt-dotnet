import assert from "node:assert/strict";
import { resolve, dirname } from "node:path";
import { readdir } from "node:fs/promises";
import { root, command, sourceIdentity, json, sdk } from "./package/common.mjs";

// One portable gate supplies Task, CI and source-stamped package validation receipts.
const report = {
  format_version: 0,
  status: "running",
  source: await sourceIdentity(),
  sdk,
  checks: [],
};
const remark = [
  "--yes",
  "--package=remark-cli@12.0.1",
  "--package=remark-gfm@4.0.1",
  "--package=remark-validate-links@13.1.0",
  "--package=remark-lint-no-undefined-references@5.0.2",
];
const checks = [
  ["git", ["diff", "--check"]],
  ["git", ["diff", "--cached", "--check"]],
  ["npx", ["--yes", "prettier@3.9.10", "--check", "**/*.md"]],
  [
    "npx",
    [
      ...remark,
      "remark",
      "--rc-path",
      ".remarkrc.cjs",
      "--ignore-path",
      ".prettierignore",
      "--frail",
      "--no-stdout",
      "--no-color",
      ".",
    ],
  ],
  ["npx", [...remark, "node", "eng/CheckDocumentationLinks.test.cjs"]],
  ["dotnet", ["restore", "SvgTemplates.slnx", "--locked-mode"]],
  ["dotnet", ["format", "SvgTemplates.slnx", "--no-restore", "--verify-no-changes"]],
  ["dotnet", ["build", "SvgTemplates.slnx", "-c", "Release", "--no-restore"]],
  ["dotnet", ["test", "--solution", "SvgTemplates.slnx", "-c", "Release", "--no-build"]],
  ["npm", ["ci", "--prefix", "eng/visual", "--ignore-scripts"]],
  [
    "node",
    [
      "--test",
      ...(
        await Promise.all(
          ["eng/visual", "eng/package"].map(async (folder) =>
            (await readdir(resolve(root, folder)))
              .filter((name) => name.endsWith(".test.mjs"))
              .sort()
              .map((name) => `${folder}/${name}`),
          ),
        )
      ).flat(),
    ],
  ],
];
try {
  // A new attempt invalidates a prior success even before the first check completes.
  await json(resolve(root, "artifacts/validation/gate.json"), report);
  // Ordinary validation honors global.json's patch roll-forward policy.
  report.sdk = (await command("dotnet", ["--version"])).stdout.trim();
  for (const [file, args] of checks) {
    console.log(`> ${file} ${args.join(" ")}`);
    // Windows npm entry points are cmd files. Invoke the bundled CLI through Node directly.
    const output =
      process.platform === "win32" && ["npm", "npx"].includes(file)
        ? await command(process.execPath, [
            resolve(dirname(process.execPath), `node_modules/npm/bin/${file}-cli.js`),
            ...args,
          ])
        : await command(file, args);
    process.stdout.write(output.stdout);
    process.stderr.write(output.stderr);
    report.checks.push({ command: file, arguments: args, status: "passed" });
  }
  assert.deepEqual(await sourceIdentity(), report.source, "Source changed during validation");
  report.status = "passed";
} catch (error) {
  report.status = "failed";
  process.stderr.write((error.stdout ?? "") + (error.stderr ?? "") + error.message + "\n");
  process.exitCode = 1;
} finally {
  await json(resolve(root, "artifacts/validation/gate.json"), report);
}
