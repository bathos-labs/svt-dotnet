import assert from "node:assert/strict";
import { identityHash } from "./publication.mjs";

// In-memory remote for release rehearsals. This module cannot perform network or filesystem writes.
export function simulatePublication(identity, archive, artifacts) {
  const state = {
    assets: new Map(),
    registry: new Map(),
    events: [],
    uploads: [],
    published: false,
    locked: false,
    authorizations: 0,
  };
  const remote = {
    async authorize(value) {
      assert.deepEqual(value, identity);
      state.authorizations++;
    },
    async lock() {
      assert.equal(state.locked, false, "Concurrent publisher");
      state.locked = true;
    },
    async unlock() {
      state.locked = false;
    },
    async draft() {
      return { published: state.published };
    },
    async readAsset(name) {
      return state.assets.get(name) ?? null;
    },
    async writeAsset(name, bytes) {
      assert.ok(!state.assets.has(name));
      state.assets.set(name, bytes);
    },
    async completion() {
      return state.completion ?? null;
    },
    async verifyCompletion(receipt) {
      assert.equal(receipt.verification, "public-evidence");
    },
    async state(artifact) {
      const value = state.registry.get(artifact.key);
      if (value === "pending") return "pending";
      if (value) {
        assert.equal(value, artifact.sha256, "Registry conflict");
        return "matching";
      }
      return "absent";
    },
    async upload(artifact) {
      assert.ok(state.assets.has("candidate-sealed.json"), "Upload before verified archival");
      assert.ok(!state.registry.has(artifact.key));
      state.uploads.push(artifact.key);
      state.registry.set(artifact.key, artifact.sha256);
    },
    async verifyPublic() {
      for (const artifact of artifacts)
        assert.equal(state.registry.get(artifact.key), artifact.sha256);
      return "public-evidence";
    },
    async saveCompletion(receipt) {
      assert.ok(!state.completion);
      state.completion = receipt;
    },
    async finalize() {
      state.published = true;
    },
  };
  const input = {
    identity,
    expectedIdentity: identityHash(identity),
    archive,
    artifacts,
    remote,
    record: async (event) => state.events.push(event),
  };
  return { input, state, remote };
}
