import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { command, hash } from "../package/common.mjs";
import { identityHash } from "./publication.mjs";

async function fixture(run) {
  const work = await mkdtemp(resolve(tmpdir(), "svt-draft-restore-"));
  const archive = "retained candidate bytes";
  const identity = {
    format_version: 0,
    repository: "svt-project/svt-dotnet",
    tag: "v1.0.0-rc.1",
    commit: "a".repeat(40),
    manifest_sha256: "b".repeat(64),
    archive_sha256: hash(archive),
    validation_run: "1234",
  };
  const expected = identityHash(identity);
  const destination = resolve(work, "artifacts/release-archive");
  const script = new URL("./restore.mjs", import.meta.url).href;
  const restore = (seal = expected) => {
    const assets = {
      "candidate-identity.json": JSON.stringify(identity),
      "candidate.zip": archive,
      "candidate-sealed.json": JSON.stringify({ identity_sha256: seal }),
    };
    return command(process.execPath, [
      "--input-type=module",
      "-e",
      `
      import assert from 'node:assert/strict';
      const assets = ${JSON.stringify(assets)};
      const tag = ${JSON.stringify(identity.tag)};
      process.argv = ['node', 'restore.mjs', tag, ${JSON.stringify(expected)}, ${JSON.stringify(destination)}];
      process.env.GITHUB_REF = 'refs/tags/' + tag;
      process.env.GH_TOKEN = 'fixture-token';
      globalThis.fetch = async (url, options) => {
        assert.equal(options.method, 'GET', 'Recovery must not write to GitHub');
        const path = new URL(url).pathname;
        if (path.endsWith('/releases')) return Response.json([{id: 1, tag_name: tag}]);
        if (path.endsWith('/releases/1/assets')) return Response.json(
          Object.keys(assets).map(name => ({id: name, name, state: 'uploaded'})));
        const name = path.split('/').at(-1);
        assert.ok(Object.hasOwn(assets, name), 'Unexpected network request: ' + url);
        return new Response(assets[name]);
      };
      await import(${JSON.stringify(script)});
      `,
    ]);
  };
  try {
    await run({ work, destination, archive, identity, restore });
  } finally {
    await rm(work, { recursive: true, force: true });
  }
}

test("draft recovery creates missing parents and refuses to replace a restored candidate", async () => {
  await fixture(async ({ destination, archive, identity, restore }) => {
    await restore();
    assert.equal(await readFile(resolve(destination, "candidate.zip"), "utf8"), archive);
    assert.deepEqual(
      JSON.parse(await readFile(resolve(destination, "candidate-identity.json"))),
      identity,
    );
    await assert.rejects(restore(), (error) => /EEXIST/.test(error.stderr));
    assert.equal(await readFile(resolve(destination, "candidate.zip"), "utf8"), archive);
  });
});

test("draft recovery verifies the retained seal before creating local directories", async () => {
  await fixture(async ({ work, restore }) => {
    await assert.rejects(restore("c".repeat(64)));
    assert.deepEqual(await readdir(work), []);
  });
});
