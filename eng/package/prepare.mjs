import { verifySnapshot } from "../conformance/snapshot.mjs";
import assert from "node:assert/strict";
import { readFile, mkdir, copyFile, cp, rm, writeFile, appendFile } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { declaredVersion, verifyRelease } from "../release/policy.mjs";
import {
  root,
  packageId,
  cliPackageId,
  productIds,
  sdk,
  tool,
  command,
  json,
  sourceIdentity,
  requireGate,
  packageVersion,
  treeHashes,
  environment,
} from "./common.mjs";

assert.ok(
  process.argv.length === 2 ||
    (process.argv.length === 3 && process.argv[2] === "--review") ||
    (process.argv.length === 4 && process.argv[2] === "--release"),
  "Usage: prepare.mjs [--review | --release v<version>]",
);
const review = process.argv[2] === "--review";
const release =
  process.argv[2] === "--release"
    ? await verifyRelease({ tag: process.argv[3], remote: true, requireTag: true })
    : null;
const source = await sourceIdentity();
const referenceSnapshot = await verifySnapshot(resolve(root, "conformance"));
const gate = JSON.parse(await readFile(resolve(root, "artifacts/validation/gate.json")));
requireGate(gate, source, review);
assert.equal(gate.sdk, sdk, "Package preparation requires a gate run with the exact SDK pin");
assert.equal((await command("dotnet", ["--version"])).stdout.trim(), sdk);
const version = release?.version ?? packageVersion(await declaredVersion(), source, review);
const output = resolve(root, "artifacts/packages", version);
await mkdir(resolve(root, "artifacts/packages"), { recursive: true });
await mkdir(output); // Never overwrite an existing version or artifact.
const bundle = resolve(output, "bundle");
const packages = resolve(bundle, "packages");
await mkdir(packages, { recursive: true });
const project = resolve(root, "src", cliPackageId, cliPackageId + ".csproj");
const properties = [
  "-p:ContinuousIntegrationBuild=true",
  `-p:Version=${version}`,
  `-p:PackageVersion=${version}`,
  `-p:DocumentationRef=${release?.tag ?? source.commit}`,
];
const build = resolve(output, "build");
for (const args of [
  ["restore", project, "--locked-mode", "--artifacts-path", build, ...properties],
  ["build", project, "-c", "Release", "--no-restore", "--artifacts-path", build, ...properties],
]) {
  const result = await command("dotnet", args);
  await appendFile(resolve(output, "pack.log"), result.stdout + result.stderr);
}
const products = {};
for (const [role, id] of Object.entries(productIds)) {
  const result = await command("dotnet", [
    "pack",
    resolve(root, "src", id, id + ".csproj"),
    "-c",
    "Release",
    "--no-build",
    "--no-restore",
    "--artifacts-path",
    build,
    "-o",
    packages,
    ...properties,
  ]);
  await appendFile(resolve(output, "pack.log"), result.stdout + result.stderr);
  const packageFile = `packages/${id}.${version}.nupkg`;
  const symbolFile = `packages/${id}.${version}.snupkg`;
  const args = [resolve(bundle, packageFile), resolve(bundle, symbolFile), version, source.commit];
  if (role === "cli") {
    args.unshift("inspect-tool");
    args.push(
      resolve(bundle, products.library.file),
      resolve(bundle, products.library.symbols.file),
    );
  }
  const inspected = await command("dotnet", [tool, ...args]);
  assert.equal(inspected.stderr, "", "Package inspector diagnostic");
  const inspection = JSON.parse(inspected.stdout);
  await json(resolve(bundle, `inspection-${role}.json`), inspection);
  products[role] = {
    id,
    version,
    file: packageFile,
    sha256: inspection.package_sha256,
    assembly_sha256: inspection.assembly_sha256,
    pdb_sha256: inspection.pdb_sha256,
    symbols: { file: symbolFile, sha256: inspection.symbols_sha256 },
  };
}
await json(resolve(bundle, "validation-gate.json"), gate);
await json(resolve(bundle, "reference-snapshot.json"), referenceSnapshot);
await cp(resolve(root, "examples/authoring"), resolve(bundle, "examples/authoring"), {
  recursive: true,
});
// The repository example README links into the full checkout. The handoff has its own instructions.
await rm(resolve(bundle, "examples/authoring/README.md"));
for (const name of ["LICENSE", "NOTICE", "global.json"])
  await copyFile(resolve(root, name), resolve(bundle, name));
await cp(resolve(root, "examples/quickstart"), resolve(bundle, "examples/quickstart"), {
  recursive: true,
});
await mkdir(resolve(bundle, "consumer"));
await copyFile(
  resolve(root, "eng/package/consumer/Program.cs"),
  resolve(bundle, "consumer/Program.cs"),
);
await copyFile(
  resolve(root, "examples/editor/PaintEditorAdapter.cs"),
  resolve(bundle, "consumer/PaintEditorAdapter.cs"),
);
const template = await readFile(
  resolve(root, "eng/package/consumer/PackageConsumer.csproj.in"),
  "utf8",
);
assert.equal(template.split("@VERSION@").length, 2);
await writeFile(
  resolve(bundle, "consumer/PackageConsumer.csproj"),
  template.replace("@VERSION@", version),
);
await writeFile(
  resolve(bundle, "HANDOFF.md"),
  `# Package validation\n\nPackage: ${packageId} ${version}\n\nSource commit: ${source.commit}\nSource fingerprint: ${source.fingerprint}\nSpecification/reference commit: ${referenceSnapshot.source_commit}\nReference asset digest: ${referenceSnapshot.sha256}\nThe accepted reference inventory is retained in reference-snapshot.json.\n\n${review ? "REVIEW ONLY: built from an uncommitted development snapshot. This is not a handoff candidate." : "Prepared from a clean committed checkout after the full gate. Handoff readiness also requires matching Ubuntu and Windows consumption receipts."}\n\nThe packages directory contains the exact inspected library, CLI and portable symbols. SHA-256 values, SDK ${sdk}, net10.0, source state and file inventory are recorded in manifest.json. Do not rebuild or replace files under this version. Source Link points at the recorded commit and requires repository access. Review-only source changes are identified by the fingerprint and are not available through that commit.\n\nThe consumer directory is an independent PackageReference smoke application. From a checkout of the recorded source, run:\n\n\`\`\`sh\nnode eng/package/consume.mjs /path/to/this/bundle\n\`\`\`\n\nThe runner copies the consumer outside the checkout, uses a new cache and only this local feed, verifies the loaded assembly hash, and records a separate platform receipt. It does not rebuild the package. Review-only bundles require the reviewed tooling snapshot.\n\nOriginal SVGs, real image assets, independent expectations and binding requests are under examples/authoring. Asset rights and hashes are included; LICENSE and NOTICE cover repository-authored material. No fonts or native rendering tools are distributed in this bundle.\n\n[Specification](https://github.com/svt-project/svt-spec/tree/${referenceSnapshot.source_commit}/spec), [API lifecycle](https://github.com/svt-project/svt-dotnet/blob/${source.commit}/docs/binding-api.md), [diagnostics and budgets](https://github.com/svt-project/svt-spec/blob/${referenceSnapshot.source_commit}/spec/substitution.md), [authoring guidance](https://github.com/svt-project/svt-dotnet/blob/${source.commit}/docs/authoring.md).\n\nPortable tests, package consumption and native rendering are distinct evidence. These package checks do not rerun authoring/rendering validation or establish application integration. Retain the applicable native reports separately. Hosts own image decoding, resource resolution, CSS behavior, fitting and rendering. This prerelease does not establish universal rendering, released compatibility or authorization to publish.\n`,
);
assert.deepEqual(await sourceIdentity(), source, "Source changed during package preparation");
const manifest = {
  format_version: 0,
  purpose: release ? "release-candidate" : review ? "review" : "handoff-candidate",
  ...(release ? { release_tag: release.tag } : {}),
  source,
  sdk,
  target_framework: "net10.0",
  environment: await environment(),
  version,
  products,
  validation: {
    portable_gate: "passed",
    package_inspection: "passed",
    consumption: "pending",
    required_platforms: ["linux", "win32"],
    native_rendering: {
      status: "separate-evidence-not-rerun",
      prior_results: "https://github.com/svt-project/svt-dotnet/issues/10",
      implementation_commit: "269d4b25769ce144a476c0c7ac938b62d3d60c8e",
      evidence_scope:
        "Historical reviewed working-tree runs, with tooling hashes checked against the implementation commit; not a native run of this package candidate.",
      workflows: [
        {
          name: "Inkscape native/Plain save, reopen and direct-attribute edits",
          platform: "macOS",
          result: "passed",
        },
        {
          name: "Chrome/resvg original corpus",
          platform: "macOS",
          result: "passed",
          comparisons: 38,
        },
        {
          name: "Chrome/resvg native and Plain saved corpora",
          platform: "macOS",
          result: "passed",
          comparisons: 92,
        },
        {
          name: "Chrome/resvg four deliberately edited corpora",
          platform: "macOS",
          result: "passed",
          comparisons: 56,
        },
        {
          name: "Chrome/resvg original corpus",
          platform: "Ubuntu",
          result: "passed",
          comparisons: 38,
        },
      ],
      raw_evidence: "Retained separately; not included in this package bundle.",
      limitations: [
        "Candidate native release checks have not been rerun by package preparation.",
        "Windows native rendering was not validated.",
        "Optimized command-line export was unavailable in the tested environment.",
        "Authored xml:base resolution remains a limitation in both pinned renderers; ordinary relative references and host-prepared relocation passed.",
        "Interactive editing and optimized export observations are exploratory, not recurring release verdicts.",
      ],
    },
    consumer_integration: "pending",
  },
  files: await treeHashes(bundle),
};
await json(resolve(bundle, "manifest.json"), manifest);
await json(resolve(root, "artifacts/package-current.json"), {
  bundle: relative(root, bundle),
  version,
});
if (process.env.GITHUB_OUTPUT)
  await appendFile(
    process.env.GITHUB_OUTPUT,
    `bundle=${relative(root, bundle)}\nversion=${version}\n`,
  );
console.log(
  JSON.stringify({
    status: "prepared",
    purpose: manifest.purpose,
    bundle: relative(root, bundle),
    version,
  }),
);
