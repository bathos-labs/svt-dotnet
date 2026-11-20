import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { hash } from "../package/common.mjs";
import { github } from "./github.mjs";
import { identityHash, validateIdentity } from "./publication.mjs";

assert.equal(
  process.argv.length,
  5,
  "Usage: restore.mjs <tag> <approved-identity-hash> <destination>",
);
const [tag, expected, destination] = process.argv.slice(2);
assert.equal(
  process.env.GITHUB_REF,
  `refs/tags/${tag}`,
  "Recovery must run on the existing release tag",
);
assert.match(expected, /^[a-f0-9]{64}$/);
const api = github(process.env.GH_TOKEN);
const release = await api.find(tag);
assert.ok(release, "No retained release");
const bytes = await api.readAsset(release, "candidate-identity.json");
assert.ok(bytes, "Missing recovery identity");
const identity = JSON.parse(bytes);
validateIdentity(identity);
assert.equal(identityHash(identity), expected, "Retained identity differs from approval");
assert.equal(identity.tag, tag);
const archive = await api.readAsset(release, "candidate.zip");
assert.ok(archive, "Missing retained candidate");
assert.equal(hash(archive), identity.archive_sha256);
const seal = await api.readAsset(release, "candidate-sealed.json");
assert.ok(seal, "Candidate archival was not completed; retry the original archival job");
assert.deepEqual(JSON.parse(seal), { identity_sha256: expected });
await mkdir(dirname(resolve(destination)), { recursive: true });
await mkdir(destination); // Refuse an existing candidate, including a partial restore.
await writeFile(resolve(destination, "candidate-identity.json"), bytes, { flag: "wx" });
await writeFile(resolve(destination, "candidate.zip"), archive, { flag: "wx" });
console.log(`Restored approved candidate ${expected}`);
