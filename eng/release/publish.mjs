import assert from "node:assert/strict";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import {
  command,
  tool,
  hash,
  json,
  treeHashes,
  verifyBundle,
  fileHash,
} from "../package/common.mjs";
import {
  publishRelease,
  identityHash,
  publicationOrder,
  recordedUploadState,
} from "./publication.mjs";
import { github } from "./github.mjs";

assert.equal(
  process.argv.length,
  4,
  "Usage: publish.mjs <archive-directory> <approved-identity-hash>",
);
assert.equal(
  process.env.GITHUB_ACTIONS,
  "true",
  "Publication runs only in the protected release workflow",
);
assert.ok(process.env.NUGET_API_KEY, "NuGet credential missing");
const directory = resolve(process.argv[2]);
const identity = JSON.parse(await readFile(resolve(directory, "candidate-identity.json")));
const expected = process.argv[3];
assert.equal(identityHash(identity), expected);
assert.equal(process.env.GITHUB_REF, `refs/tags/${identity.tag}`);
const archive = await readFile(resolve(directory, "candidate.zip"));
assert.equal(hash(archive), identity.archive_sha256);
const extracted = resolve(directory, "verified");
await command("dotnet", [tool, "extract", resolve(directory, "candidate.zip"), extracted]);
assert.deepEqual(await treeHashes(extracted), identity.files, "Retained inventory changed");
const transfer = resolve(extracted, "transfer"),
  bundle = resolve(transfer, "bundle"),
  native = resolve(extracted, "native");
await command(process.execPath, ["eng/release/verify-candidate.mjs", transfer, native]);
const manifest = await verifyBundle(bundle);
assert.equal(await fileHash(resolve(bundle, "manifest.json")), identity.manifest_sha256);
assert.equal(manifest.source.commit, identity.commit);
const api = github(process.env.GH_TOKEN);
const run = await api.request(`/actions/runs/${identity.validation_run}`);
assert.equal(run.head_sha, identity.commit);
assert.equal(run.event, "push");
assert.equal(run.path, ".github/workflows/release.yml");
assert.equal(run.repository.full_name, identity.repository);
const artifactList = [];
for (const key of publicationOrder) {
  const [role, kind] = key.split("-");
  const product = manifest.products[role];
  const file = kind === "symbols" ? product.symbols : product;
  artifactList.push({
    key,
    role,
    kind,
    ...file,
    bytes: await readFile(resolve(bundle, file.file)),
  });
}
await mkdir(resolve(directory, "attempt"));
let release,
  sequence = 0;
const attempt = `attempt-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}`;
const safe = (message) =>
  String(message)
    .replaceAll(process.env.NUGET_API_KEY, "<credential>")
    .replaceAll(process.env.GH_TOKEN ?? "<unset>", "<credential>");
async function record(event) {
  const name = `${attempt}-${String(sequence++).padStart(4, "0")}.json`;
  const value = { identity_sha256: expected, ...event };
  const bytes = Buffer.from(JSON.stringify(value));
  await writeFile(resolve(directory, "attempt", name), bytes, { flag: "wx" });
  // Completion is already retained before finalization. Published releases may be immutable;
  // the final attempt status stays in CI instead of adding assets after publication.
  if (event.status !== "complete") {
    await api.writeAsset(release, name, bytes);
    assert.equal(hash(await api.readAsset(release, name)), hash(bytes));
  }
}
let priorAttempts;
async function priorUploadState(artifact) {
  if (!priorAttempts) {
    const assets = (await api.assets(release)).filter(
      (asset) => asset.name.startsWith("attempt-") && asset.name.endsWith(".json"),
    );
    priorAttempts = [];
    for (const asset of assets)
      priorAttempts.push(JSON.parse(await api.readAsset(release, asset.name)));
  }
  return recordedUploadState(priorAttempts, artifact, expected);
}
async function download(url, headers = {}) {
  const response = await fetch(url, { headers, signal: AbortSignal.timeout(60000) });
  if (response.status === 404) return null;
  assert.ok(response.ok, `Registry evidence unavailable: ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}
async function state(artifact) {
  if (artifact.kind === "package") {
    const id = artifact.id.toLowerCase();
    const bytes = await download(
      `https://api.nuget.org/v3-flatcontainer/${id}/${manifest.version}/${id}.${manifest.version}.nupkg`,
    );
    if (!bytes) return priorUploadState(artifact);
    const path = resolve(directory, "attempt", `${id}.nupkg`);
    await writeFile(path, bytes);
    await command("dotnet", ["nuget", "verify", path, "--all"]);
    await command("dotnet", [tool, "compare-published", resolve(bundle, artifact.file), path]);
    return "matching";
  }
  const roles = artifact.role === "cli" ? ["library", "cli"] : ["library"];
  let absent = false;
  for (const role of roles) {
    const inspection = JSON.parse(await readFile(resolve(bundle, `inspection-${role}.json`)));
    const name = (role === "cli" ? "svt" : manifest.products.library.id).toLowerCase() + ".pdb";
    const bytes = await download(
      `https://symbols.nuget.org/download/symbols/${name}/${inspection.symbol_key}/${name}`,
      { SymbolChecksum: inspection.symbol_checksum, SymbolChecksumValidationSupported: "1" },
    );
    if (!bytes) absent = true;
    else assert.equal(hash(bytes), inspection.pdb_sha256, "Public symbols differ");
  }
  return absent ? priorUploadState(artifact) : "matching";
}
const remote = {
  async authorize() {
    // verify-candidate above checks current remote branch ancestry and all retained receipts.
    const ref = await api.request(`/git/ref/tags/${encodeURIComponent(identity.tag)}`);
    assert.equal(ref.object.type, "tag");
    const tag = await api.request(`/git/tags/${ref.object.sha}`);
    assert.equal(tag.object.sha, identity.commit);
    assert.equal(tag.object.type, "commit");
  },
  async lock() {},
  async unlock() {}, // The workflow serializes all initial/resume runs by tag.
  async draft() {
    release = await api.find(identity.tag);
    if (!release)
      release = await api.request("/releases", {
        method: "POST",
        body: {
          tag_name: identity.tag,
          target_commitish: identity.commit,
          name: identity.tag,
          draft: true,
          prerelease: identity.tag.includes("-"),
          body: `Validated candidate from run ${identity.validation_run}. Recovery identity: ${expected}.`,
        },
      });
    assert.equal(release.tag_name, identity.tag);
    return { published: !release.draft };
  },
  readAsset: (name) => api.readAsset(release, name),
  writeAsset: (name, bytes) => api.writeAsset(release, name, bytes),
  async completion() {
    const bytes = await api.readAsset(release, "publication-complete.json");
    return bytes ? JSON.parse(bytes) : null;
  },
  state,
  async upload(artifact) {
    const args = [
      "nuget",
      "push",
      resolve(bundle, artifact.file),
      "--api-key",
      process.env.NUGET_API_KEY,
      "--source",
      "https://api.nuget.org/v3/index.json",
    ];
    // Preflight already skips verified duplicates. A racing 409 stops this attempt;
    // resume must reconcile its identity before any later upload.
    if (artifact.kind === "package") args.push("--no-symbols");
    try {
      await command("dotnet", args);
    } catch (error) {
      throw new Error(
        safe(
          (error.stdout ?? "") +
            (error.stderr ?? "") +
            "Upload outcome unresolved; reconcile registry state before retrying.",
        ),
      );
    }
  },
  async saveCompletion(receipt) {
    await api.writeAsset(
      release,
      "publication-complete.json",
      Buffer.from(JSON.stringify(receipt)),
    );
  },
  async finalize(_, receipt) {
    const retained = JSON.parse(await api.readAsset(release, "publication-complete.json"));
    assert.deepEqual(retained, receipt);
    const changelog = await readFile("CHANGELOG.md", "utf8");
    const heading = `## [${manifest.version}] - `;
    const start = changelog.indexOf(heading);
    assert.ok(start >= 0);
    const end = changelog.indexOf("\n## ", start + heading.length);
    const notes = changelog.slice(start, end < 0 ? undefined : end).trim();
    await api.request(`/releases/${release.id}`, {
      method: "PATCH",
      body: {
        draft: false,
        prerelease: manifest.version.includes("-"),
        body: notes + `\n\nCandidate identity: ${expected}.`,
      },
    });
  },
};
try {
  await publishRelease({
    identity,
    expectedIdentity: expected,
    archive,
    artifacts: artifactList,
    remote,
    record,
  });
} catch (error) {
  await json(resolve(directory, "attempt/failure.json"), {
    status: "incomplete",
    identity_sha256: expected,
    error: safe(error.message),
  });
  console.error(safe(error.message));
  process.exitCode = 1;
}
