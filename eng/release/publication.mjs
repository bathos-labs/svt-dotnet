import assert from "node:assert/strict";
import { hash } from "../package/common.mjs";

export function validateIdentity(identity) {
  assert.equal(identity.format_version, 0);
  assert.equal(identity.repository, "svt-project/svt-dotnet");
  assert.match(identity.tag, /^v[0-9]+\.[0-9]+\.[0-9]+(?:-[a-z0-9.-]+)?$/);
  assert.match(identity.commit, /^[a-f0-9]{40}$/);
  assert.match(identity.manifest_sha256, /^[a-f0-9]{64}$/);
  assert.match(identity.archive_sha256, /^[a-f0-9]{64}$/);
  assert.match(identity.validation_run, /^[1-9][0-9]*$/);
}
export const identityHash = (identity) => hash(JSON.stringify(identity));
export const publicationOrder = [
  "library-package",
  "cli-package",
  "library-symbols",
  "cli-symbols",
];

// Consult the retained journal only when the registry cannot yet serve this artifact.
export function recordedUploadState(rows, artifact, identity) {
  let state = "absent";
  for (const row of rows) {
    assert.equal(row.identity_sha256, identity, "Foreign publication attempt");
    if (row.artifact !== artifact.key) continue;
    assert.equal(row.sha256, artifact.sha256, "Foreign artifact digest in publication attempt");
    if (row.state === "accepted" || row.state === "verified") state = "accepted";
    else {
      assert.equal(row.state, "upload-started", "Unknown artifact receipt");
      if (state === "absent") state = "pending";
    }
  }
  return state;
}

// This is the only publication state machine. Production and nonpublishing rehearsal supply
// different transports; no rehearsal flag can accidentally activate a production write.
export async function publishRelease({
  identity,
  expectedIdentity,
  archive,
  artifacts,
  remote,
  record,
  boundary = async () => {},
}) {
  validateIdentity(identity);
  assert.equal(identityHash(identity), expectedIdentity, "Recovery identity differs from approval");
  assert.equal(hash(archive), identity.archive_sha256, "Archive changed");
  assert.deepEqual(
    artifacts.map((a) => a.key),
    publicationOrder,
  );
  for (const artifact of artifacts) assert.equal(hash(artifact.bytes), artifact.sha256);
  // Revalidate authorization, ancestry, retained portable/native evidence on every attempt.
  await remote.authorize(identity);
  await remote.lock(identity.tag);
  try {
    const release = await remote.draft(identity);
    const identityBytes = Buffer.from(JSON.stringify(identity));
    const assets = [
      { name: "candidate-identity.json", bytes: identityBytes },
      { name: "candidate.zip", bytes: archive },
    ];
    const seal = await remote.readAsset("candidate-sealed.json");
    if (seal) assert.deepEqual(JSON.parse(seal), { identity_sha256: expectedIdentity });
    for (const asset of assets) {
      const existing = await remote.readAsset(asset.name);
      if (existing)
        assert.equal(hash(existing), hash(asset.bytes), `Retained ${asset.name} changed`);
      else {
        assert.ok(!seal && !release.published, "Sealed candidate assets cannot be repaired");
        await remote.writeAsset(asset.name, asset.bytes);
      }
      const retained = await remote.readAsset(asset.name);
      assert.ok(retained, `Missing retained ${asset.name}`);
      assert.equal(hash(retained), hash(asset.bytes));
      await boundary("archive:" + asset.name);
    }
    if (!seal) {
      assert.ok(!release.published, "Published release lacks candidate seal");
      await remote.writeAsset(
        "candidate-sealed.json",
        Buffer.from(JSON.stringify({ identity_sha256: expectedIdentity })),
      );
    }
    assert.deepEqual(JSON.parse(await remote.readAsset("candidate-sealed.json")), {
      identity_sha256: expectedIdentity,
    });
    await boundary("archive:sealed");
    const receipt = {
      status: "accepted",
      identity_sha256: expectedIdentity,
      artifacts: Object.fromEntries(artifacts.map(({ key, sha256 }) => [key, sha256])),
    };
    const completion = await remote.completion();
    if (completion) {
      assert.deepEqual(completion, receipt, "Retained acceptance differs from the candidate");
      if (!release.published) await remote.finalize(identity, completion);
      await record({
        status: "complete",
        identity_sha256: expectedIdentity,
        finalization_only: true,
      });
      return;
    }
    assert.ok(!release.published, "Published release has no completion evidence");
    // All states are checked before writing any package, including symbols and the other product.
    const states = [];
    for (const artifact of artifacts) {
      const state = await remote.state(artifact);
      assert.ok(
        ["absent", "matching", "accepted", "pending"].includes(state),
        `Unverified registry state: ${artifact.key}`,
      );
      states.push(state);
    }
    assert.ok(
      !states.includes("pending"),
      "Publication incomplete: reconcile an upload with an unknown outcome before retrying",
    );
    for (const [index, artifact] of artifacts.entries()) {
      if (states[index] === "accepted") continue;
      if (states[index] === "matching") {
        await record({ artifact: artifact.key, state: "verified", sha256: artifact.sha256 });
        continue;
      }
      // Write-ahead evidence makes a lost response distinguishable from an artifact never attempted.
      await record({ artifact: artifact.key, state: "upload-started", sha256: artifact.sha256 });
      await remote.upload(artifact);
      await record({ artifact: artifact.key, state: "accepted", sha256: artifact.sha256 });
      await boundary("upload:" + artifact.key);
    }
    await remote.saveCompletion(receipt);
    assert.deepEqual(await remote.completion(), receipt, "Acceptance receipt was not retained");
    await boundary("acceptance:retained");
    await remote.finalize(identity, receipt);
    await boundary("release:published");
    await record({ status: "complete", identity_sha256: expectedIdentity });
  } finally {
    await remote.unlock(identity.tag);
  }
}
