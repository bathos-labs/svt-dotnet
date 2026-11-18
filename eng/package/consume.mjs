import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, mkdtemp, copyFile, cp, rm } from "node:fs/promises";
import { resolve, join, relative } from "node:path";
import { tmpdir, platform } from "node:os";
import {
  root,
  packageId,
  sdk,
  command,
  json,
  verifyBundle,
  bundlePath,
  fileHash,
  nonce,
  smokeChecks,
  environment,
} from "./common.mjs";

assert.ok(process.argv.length <= 3, "Usage: consume.mjs [bundle]");
const bundle = process.argv[2]
  ? resolve(process.argv[2])
  : resolve(
      root,
      JSON.parse(await readFile(resolve(root, "artifacts/package-current.json"))).bundle,
    );
const manifest = await verifyBundle(bundle);
const manifestHash = await fileHash(resolve(bundle, "manifest.json"));
assert.equal((await command("dotnet", ["--version"])).stdout.trim(), sdk);
const work = await mkdtemp(join(tmpdir(), "svt-package-consumer-"));
const output = resolve(
  root,
  "artifacts/package-consumption",
  `${manifest.package.version}-${platform()}-${nonce()}`,
);
await mkdir(output, { recursive: true });
const report = {
  format_version: 0,
  status: "running",
  package: manifest.package,
  symbols: manifest.symbols,
  bundle_manifest_sha256: manifestHash,
  source: manifest.source,
  purpose: manifest.purpose,
  sdk,
  environment: await environment(),
};
const redact = (text) =>
  String(text).replaceAll(work, "<consumer>").replaceAll(root, "<repository>");
const project = resolve(work, "consumer/PackageConsumer.csproj");
const cache = resolve(work, "cache");
const feed = resolve(work, "feed");
const emptyFeed = resolve(work, "empty-feed");
const config = resolve(work, "NuGet.config");
const properties = [
  "-p:ImportDirectoryBuildProps=false",
  "-p:ImportDirectoryBuildTargets=false",
  "-p:ImportDirectoryPackagesProps=false",
  "-p:ManagePackageVersionsCentrally=false",
];
const env = {
  ...process.env,
  NUGET_PACKAGES: cache,
  NUGET_HTTP_CACHE_PATH: resolve(work, "http-cache"),
  NUGET_PLUGINS_CACHE_PATH: resolve(work, "plugin-cache"),
  DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  DOTNET_NOLOGO: "1",
};
async function execute(args) {
  try {
    const result = await command("dotnet", args, { cwd: work, env });
    await writeFile(
      resolve(output, `step-${report.steps ?? 0}.log`),
      redact(result.stdout + result.stderr),
    );
    report.steps = (report.steps ?? 0) + 1;
    return result;
  } catch (error) {
    await writeFile(
      resolve(output, `step-${report.steps ?? 0}.log`),
      redact((error.stdout ?? "") + (error.stderr ?? "") + error.message),
    );
    report.steps = (report.steps ?? 0) + 1;
    throw error;
  }
}
const xml = (value) =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
async function configure(source) {
  await writeFile(
    config,
    `<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="local" value="${xml(source)}"/></packageSources><packageSourceMapping><clear/><packageSource key="local"><package pattern="${packageId}"/></packageSource></packageSourceMapping><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>`,
  );
}
try {
  await cp(resolve(bundle, "consumer"), resolve(work, "consumer"), { recursive: true });
  await copyFile(resolve(bundle, "global.json"), resolve(work, "global.json"));
  await cp(resolve(bundle, "examples"), resolve(work, "examples"), { recursive: true });
  await mkdir(feed);
  await mkdir(emptyFeed);
  await mkdir(cache);
  const artifact = bundlePath(bundle, manifest.package.file);
  await copyFile(artifact, resolve(feed, `${packageId}.${manifest.package.version}.nupkg`));
  await configure(emptyFeed);
  // Prove that a missing local artifact cannot be supplied by an ambient source/cache.
  await assert.rejects(
    execute([
      "restore",
      project,
      "--configfile",
      config,
      "--packages",
      cache,
      "--force",
      "--no-http-cache",
      ...properties,
    ]),
    (error) =>
      error.code === 1 && !error.killed && !error.signal && /NU1101/.test(error.stdout ?? ""),
  );
  report.empty_feed_rejected = true;
  await configure(feed);
  await execute([
    "restore",
    project,
    "--configfile",
    config,
    "--packages",
    cache,
    "--force",
    "--no-http-cache",
    "--use-lock-file",
    ...properties,
  ]);
  const assets = JSON.parse(await readFile(resolve(work, "consumer/obj/project.assets.json")));
  assert.deepEqual(Object.keys(assets.libraries), [`${packageId}/${manifest.package.version}`]);
  assert.equal(assets.libraries[`${packageId}/${manifest.package.version}`].type, "package");
  assert.deepEqual(
    Object.keys(assets.project.restore.sources).map((p) => resolve(p)),
    [feed],
  );
  assert.deepEqual(
    Object.keys(assets.packageFolders).map((p) => resolve(p)),
    [cache],
  );
  assert.ok(
    Object.values(assets.project.restore.frameworks).every(
      (f) => Object.keys(f.projectReferences ?? {}).length === 0,
    ),
  );
  const restored = resolve(
    cache,
    packageId.toLowerCase(),
    manifest.package.version,
    `${packageId.toLowerCase()}.${manifest.package.version}.nupkg`,
  );
  assert.equal(await fileHash(restored), manifest.package.sha256, "Restored package bytes differ");
  await execute([
    "restore",
    project,
    "--configfile",
    config,
    "--packages",
    cache,
    "--locked-mode",
    "--no-http-cache",
    ...properties,
  ]);
  await execute(["build", project, "-c", "Release", "--no-restore", ...properties]);
  const result = await execute([
    resolve(work, "consumer/bin/Release/net10.0/PackageConsumer.dll"),
    resolve(work, "examples/authoring"),
    manifest.package.version,
    manifest.package.assembly_sha256,
    manifest.source.commit,
  ]);
  assert.equal(result.stderr, "", "Consumer diagnostics");
  const smoke = JSON.parse(result.stdout);
  assert.equal(smoke.status, "passed");
  assert.deepEqual(smoke.checks, smokeChecks, "Incomplete lifecycle smoke run");
  report.checks = smoke.checks;
  await verifyBundle(bundle);
  assert.equal(
    await fileHash(resolve(bundle, "manifest.json")),
    manifestHash,
    "Bundle changed during consumption",
  );
  report.status = "passed";
} catch (error) {
  report.status = "failed";
  report.error = redact((error.stdout ?? "") + (error.stderr ?? "") + error.message);
  console.error(report.error);
  process.exitCode = 1;
} finally {
  try {
    await rm(work, { recursive: true, force: true });
  } catch (error) {
    report.status = "failed";
    report.cleanup_error = redact(error.message);
    console.error(report.cleanup_error);
    process.exitCode = 1;
  }
  await json(resolve(output, "report.json"), report);
  await json(resolve(root, "artifacts/package-consumption-current.json"), {
    report: relative(root, resolve(output, "report.json")),
  });
  console.log(
    JSON.stringify({
      status: report.status,
      report: relative(root, resolve(output, "report.json")),
    }),
  );
}
