import test from "node:test";
import assert from "node:assert/strict";
import { github } from "./github.mjs";

test("release lookup includes paginated drafts and rejects ambiguous or unavailable state", async (t) => {
  const api = github("test-token");
  let pages;
  const calls = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push(url);
    assert.equal(options.headers.Authorization, "Bearer test-token");
    const page = Number(new URL(url).searchParams.get("page"));
    assert.equal(new URL(url).pathname, "/repos/bathos-labs/svt-dotnet/releases");
    const value = pages[page - 1];
    if (value instanceof Error) throw value;
    return value instanceof Response ? value : Response.json(value);
  });
  const draft = { id: 123, tag_name: "v1.0.0-beta.1", draft: true };
  pages = [Array.from({ length: 100 }, (_, i) => ({ tag_name: `v0.0.${i}` })), [draft]];
  assert.deepEqual(await api.find(draft.tag_name), draft);
  assert.equal(calls.length, 2);
  pages = [[{ ...draft, draft: false }]];
  assert.equal((await api.find(draft.tag_name)).draft, false);
  pages = [[]];
  assert.equal(await api.find(draft.tag_name), null);
  for (const value of [
    [draft, { ...draft, id: 456, draft: false }],
    { message: "bad response" },
    [null],
    new Response("Denied", { status: 403 }),
    new Response("Unavailable", { status: 503 }),
    new Response("Missing", { status: 404 }),
    new Error("connection lost"),
  ]) {
    pages = [value];
    await assert.rejects(api.find(draft.tag_name));
  }
});

test("release asset reads require complete uploads and writes never overwrite", async (t) => {
  const api = github("test-token");
  const release = {
    id: 123,
    upload_url:
      "https://uploads.github.com/repos/bathos-labs/svt-dotnet/releases/123/assets{?name,label}",
  };
  let rows = [{ id: 1, name: "candidate.zip", state: "uploaded" }];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(options.method, "GET");
    if (url.includes("/assets?")) return Response.json(rows);
    assert.ok(url.endsWith("/releases/assets/1"));
    return new Response("candidate bytes");
  });
  assert.equal((await api.readAsset(release, "candidate.zip")).toString(), "candidate bytes");
  await assert.rejects(
    api.writeAsset(release, "candidate.zip", Buffer.from("replacement")),
    /replace/,
  );
  rows[0].state = "starter";
  await assert.rejects(api.readAsset(release, "candidate.zip"), /Incomplete/);
  rows.push(rows[0]);
  await assert.rejects(api.readAsset(release, "candidate.zip"), /Duplicate/);
  rows = [];
  assert.equal(await api.readAsset(release, "candidate.zip"), null);
});
