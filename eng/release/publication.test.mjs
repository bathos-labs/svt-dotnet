import test from "node:test";
import assert from "node:assert/strict";
import { simulatePublication } from "./simulation.mjs";
import { hash } from "../package/common.mjs";
import { publishRelease, publicationOrder, recordedUploadState } from "./publication.mjs";

function fixture() {
  const archive = Buffer.from("retained candidate and visual evidence");
  const identity = {
    format_version: 0,
    repository: "svt-project/svt-dotnet",
    tag: "v1.0.0-beta.1",
    commit: "a".repeat(40),
    manifest_sha256: "b".repeat(64),
    archive_sha256: hash(archive),
    validation_run: "1234",
  };
  const artifacts = publicationOrder.map((key) => ({
    key,
    bytes: Buffer.from(key),
    sha256: hash(key),
  }));
  return simulatePublication(identity, archive, artifacts);
}

test("publication archives evidence before ordered writes and is repeatable after completion", async () => {
  const { input, state } = fixture();
  await publishRelease(input);
  assert.deepEqual(state.uploads, publicationOrder);
  assert.equal(state.published, true);
  await publishRelease(input);
  assert.deepEqual(state.uploads, publicationOrder);
  assert.equal(state.authorizations, 2);
  assert.equal(state.events.at(-1).status, "complete");
});
for (const stop of [
  "archive:candidate-identity.json",
  "archive:candidate.zip",
  "archive:sealed",
  ...publicationOrder.map((key) => "upload:" + key),
  "acceptance:retained",
  "release:published",
]) {
  test(`recovery reuses exact bytes after ${stop}`, async () => {
    const { input, state } = fixture();
    await assert.rejects(
      publishRelease({
        ...input,
        boundary: async (point) => {
          if (point === stop) throw new Error("interrupted");
        },
      }),
      /interrupted/,
    );
    assert.equal(state.locked, false);
    assert.ok(!state.events.some((e) => e.status === "complete"));
    await publishRelease(input);
    assert.equal(state.published, true);
    assert.deepEqual(state.uploads, publicationOrder);
  });
}

test("all registry states are checked before uploading another product", async () => {
  for (const value of ["conflict", "pending"]) {
    const { input, state } = fixture();
    state.registry.set("cli-symbols", value);
    await assert.rejects(publishRelease(input));
    assert.deepEqual(state.uploads, []);
    assert.equal(state.published, false);
  }
});
test("uncertain upload and authentication failures cannot become success", async () => {
  for (const failure of ["timeout", "authentication"]) {
    const { input, state, remote } = fixture();
    if (failure === "timeout")
      remote.upload = async (artifact) => {
        state.registry.set(artifact.key, "pending");
        throw new Error("timeout");
      };
    if (failure === "authentication")
      remote.state = async () => {
        throw new Error("authentication");
      };
    await assert.rejects(publishRelease(input));
    assert.equal(state.published, false);
    assert.ok(!state.events.some((e) => e.status === "complete"));
    if (failure === "timeout") {
      await assert.rejects(publishRelease(input), /incomplete/);
      assert.equal(state.events.filter((e) => e.state === "upload-started").length, 1);
    }
  }
});
test("recovery distinguishes matching accepted receipts from an unconfirmed attempt", () => {
  const { input } = fixture();
  const artifact = input.artifacts[0];
  const started = {
    identity_sha256: input.expectedIdentity,
    artifact: artifact.key,
    state: "upload-started",
    sha256: artifact.sha256,
  };
  const accepted = { ...started, state: "accepted" };
  const classify = (rows) => recordedUploadState(rows, artifact, input.expectedIdentity);
  assert.equal(classify([]), "absent");
  assert.equal(classify([started]), "pending");
  assert.equal(classify([accepted, started]), "accepted");
  assert.equal(classify([started, accepted]), "accepted");
  assert.throws(() => classify([{ ...accepted, sha256: "wrong" }]));
  assert.throws(() => classify([{ ...accepted, identity_sha256: "wrong" }]));
});

test("recorded acceptance survives an interruption while both feeds are indexing", async () => {
  const { input, state, remote } = fixture();
  const upload = remote.upload;
  remote.upload = async (artifact) => {
    await upload(artifact);
    state.registry.set(artifact.key, "accepted");
  };
  await assert.rejects(
    publishRelease({
      ...input,
      boundary: async (point) => {
        if (point === "upload:cli-package") throw new Error("interrupted");
      },
    }),
    /interrupted/,
  );
  await publishRelease(input);
  assert.deepEqual(state.uploads, publicationOrder);
  assert.deepEqual(
    state.completion.artifacts,
    Object.fromEntries(input.artifacts.map((a) => [a.key, a.sha256])),
  );
  assert.equal(state.completion.status, "accepted");
  assert.equal(state.published, true);
});
test("changed or missing sealed evidence and foreign approval stop recovery", async () => {
  for (const mutation of [
    "missing",
    "changed",
    "identity",
    "tag",
    "commit",
    "approval",
    "completion",
  ]) {
    const { input, state } = fixture();
    await assert.rejects(
      publishRelease({
        ...input,
        boundary: async (point) => {
          if (point === "archive:sealed") throw new Error("stop");
        },
      }),
    );
    if (mutation === "missing") state.assets.delete("candidate.zip");
    if (mutation === "changed") state.assets.set("candidate.zip", Buffer.from("changed"));
    if (mutation === "identity") state.assets.set("candidate-identity.json", Buffer.from("{}"));
    if (mutation === "tag") input.identity.tag = "v1.0.0-beta.2";
    if (mutation === "commit") input.identity.commit = "c".repeat(40);
    if (mutation === "approval") input.expectedIdentity = "d".repeat(64);
    if (mutation === "completion")
      state.completion = {
        status: "accepted",
        identity_sha256: input.expectedIdentity,
        artifacts: {},
      };
    await assert.rejects(publishRelease(input));
    assert.deepEqual(state.uploads, []);
  }
});
test("finalization failure retries only GitHub publication with retained acceptance", async () => {
  const { input, state, remote } = fixture();
  const finalize = remote.finalize;
  remote.finalize = async () => {
    throw new Error("GitHub unavailable");
  };
  await assert.rejects(publishRelease(input));
  assert.ok(state.completion);
  assert.equal(state.published, false);
  remote.finalize = finalize;
  remote.state = async () => {
    throw new Error("Must not repeat registry work");
  };
  await publishRelease(input);
  assert.equal(state.published, true);
  assert.deepEqual(state.uploads, publicationOrder);
});
test("concurrent recovery cannot acquire an active tag lock", async () => {
  const { input, state } = fixture();
  let unblock, entered;
  const held = new Promise((resolve) => {
    unblock = resolve;
  });
  const acquired = new Promise((resolve) => {
    entered = resolve;
  });
  const first = publishRelease({
    ...input,
    boundary: async (point) => {
      if (point === "archive:sealed") {
        entered();
        await held;
      }
    },
  });
  await acquired;
  try {
    await assert.rejects(publishRelease(input), /Concurrent publisher/);
    assert.equal(state.locked, true);
  } finally {
    unblock();
    await first;
  }
  assert.deepEqual(state.uploads, publicationOrder);
});
test("an existing published release without acceptance evidence is rejected", async () => {
  const { input, state } = fixture();
  state.published = true;
  await assert.rejects(publishRelease(input));
  assert.deepEqual(state.uploads, []);
});

test("a duplicate racing preflight stops subsequent artifacts and is reconciled on retry", async () => {
  const { input, state, remote } = fixture();
  const upload = remote.upload;
  remote.upload = async (artifact) => {
    if (artifact.key === "cli-package") {
      state.registry.set(artifact.key, "foreign-content");
      throw new Error("409 conflict");
    }
    await upload(artifact);
  };
  await assert.rejects(publishRelease(input), /409 conflict/);
  assert.deepEqual(state.uploads, ["library-package"]);
  assert.equal(state.published, false);
  await assert.rejects(publishRelease(input), /Registry conflict/);
  assert.deepEqual(state.uploads, ["library-package"]);
});
