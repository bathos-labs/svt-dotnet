import assert from "node:assert/strict";

export function github(token) {
  assert.ok(token, "GitHub release token is required");
  const base = "https://api.github.com/repos/svt-project/svt-dotnet";
  async function request(path, { method = "GET", body, bytes = false, allow404 = false } = {}) {
    const url = path.startsWith("https://uploads.github.com/") ? path : base + path;
    const response = await fetch(url, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: bytes ? "application/octet-stream" : "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        ...(body
          ? {
              "Content-Type": Buffer.isBuffer(body)
                ? "application/octet-stream"
                : "application/json",
            }
          : {}),
      },
      ...(body ? { body: Buffer.isBuffer(body) ? body : JSON.stringify(body) } : {}),
      signal: AbortSignal.timeout(120000),
    });
    if (response.status === 404 && allow404) return null;
    assert.ok(response.ok, `GitHub ${method} failed: ${response.status}`);
    if (response.status === 204) return null;
    return bytes ? Buffer.from(await response.arrayBuffer()) : response.json();
  }
  async function find(tag) {
    // The tag endpoint returns published releases only; authenticated listing includes drafts.
    const matches = [];
    for (let page = 1; ; page++) {
      const rows = await request(`/releases?per_page=100&page=${page}`);
      assert.ok(Array.isArray(rows), "Invalid GitHub release listing");
      for (const release of rows) {
        assert.ok(release && typeof release.tag_name === "string", "Invalid GitHub release");
        if (release.tag_name === tag) matches.push(release);
      }
      if (rows.length < 100) break;
    }
    assert.ok(matches.length <= 1, "Multiple releases match the candidate tag");
    return matches[0] ?? null;
  }
  async function assets(release) {
    const result = [];
    for (let page = 1; ; page++) {
      const rows = await request(`/releases/${release.id}/assets?per_page=100&page=${page}`);
      result.push(...rows);
      if (rows.length < 100) break;
    }
    return result;
  }
  async function readAsset(release, name) {
    const matches = (await assets(release)).filter((a) => a.name === name);
    assert.ok(matches.length <= 1, "Duplicate release assets");
    if (!matches.length) return null;
    assert.equal(
      matches[0].state,
      "uploaded",
      "Incomplete release asset upload; reconcile before retrying",
    );
    return request(`/releases/assets/${matches[0].id}`, { bytes: true });
  }
  async function writeAsset(release, name, bytes) {
    assert.equal(await readAsset(release, name), null, "Refusing to replace a release asset");
    const url = release.upload_url.split("{")[0];
    await request(`${url}?name=${encodeURIComponent(name)}`, { method: "POST", body: bytes });
  }
  return { request, find, readAsset, writeAsset };
}
