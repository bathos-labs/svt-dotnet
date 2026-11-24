import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { command } from "../package/common.mjs";
import { releaseVersion, eligibleBranch } from "./version.mjs";
import { metadata, verifyRelease } from "./policy.mjs";

test("public identities avoid NuGet normalization aliases and development packages", () => {
  for (const version of ["0.1.0-beta.1", "1.0.0", "2.1.0-rc.12", "1.0.0-beta-test"])
    assert.equal(releaseVersion(version), version);
  for (const version of [
    "1.0",
    "01.0.0",
    "1.0.0.0",
    "1.0.0-BETA.1",
    "1.0.0-beta.01",
    "1.0.0+build",
    "1.0.0-dev",
    "1.0.0-review.gabc",
    "v1.0.0",
    "1.0.0;echo",
    "1.0.0-",
  ])
    assert.throws(() => releaseVersion(version));
  assert.equal(eligibleBranch("master", "1.0.0"), true);
  assert.equal(eligibleBranch("release/1.0", "1.0.0-beta.1"), true);
  for (const branch of ["feature/test", "release/", "release-1.0"])
    assert.equal(eligibleBranch(branch, "1.0.0-beta.1"), false);
  assert.equal(eligibleBranch("release/1.0", "1.0.0"), false);
});

async function repository(run) {
  const work = await mkdtemp(resolve(tmpdir(), "svt-release-test-"));
  const cwd = resolve(work, "checkout"),
    remote = resolve(work, "origin.git");
  await mkdir(cwd);
  const git = async (...args) => (await command("git", args, { cwd })).stdout.trim();
  try {
    await git("init", "--initial-branch=master");
    await git("config", "user.name", "Release fixture");
    await git("config", "user.email", "fixture@example.invalid");
    await git("config", "commit.gpgsign", "false");
    await git("config", "tag.gpgsign", "false");
    await writeFile(
      resolve(cwd, "Directory.Build.props"),
      "<Project><PropertyGroup><Version>0.1.0-beta.1</Version></PropertyGroup></Project>\n",
    );
    await writeFile(
      resolve(cwd, "CHANGELOG.md"),
      "# Changelog\n\n## [Unreleased]\n\n## [0.1.0-beta.1] - 2026-01-01\n\n- First beta.\n\n[0.1.0-beta.1]: https://github.com/svt-project/svt-dotnet/releases/tag/v0.1.0-beta.1\n",
    );
    await git("add", ".");
    await git("commit", "-m", "Release fixture");
    await command("git", ["init", "--bare", "--initial-branch=master", remote], { cwd: work });
    await git("remote", "add", "origin", remote);
    await git("push", "origin", "master");
    await run({ cwd, remote, git });
  } finally {
    await rm(work, { recursive: true, force: true });
  }
}

test("local preparation rejects dirty trees, feature branches and mismatched tags", async () => {
  await repository(async ({ cwd, git }) => {
    assert.equal((await verifyRelease({ cwd, tag: "v0.1.0-beta.1" })).version, "0.1.0-beta.1");
    await assert.rejects(verifyRelease({ cwd, tag: "v0.1.0-beta.2" }), /Tag must match/);
    await writeFile(resolve(cwd, "untracked"), "unreviewed");
    await assert.rejects(verifyRelease({ cwd }), /clean committed/);
    await rm(resolve(cwd, "untracked"));
    await git("switch", "-c", "feature/work");
    await assert.rejects(verifyRelease({ cwd }), /master or a prerelease/);
    await git("switch", "-c", "release/beta");
    await verifyRelease({ cwd });
  });
});

test("release notes must have a unique, dated, populated latest entry for Version", async () => {
  await repository(async ({ cwd }) => {
    const file = resolve(cwd, "CHANGELOG.md"),
      original = await readFile(file, "utf8");
    for (const content of [
      original.replace("- First beta.", ""),
      original.replace("2026-01-01", "2026-02-30"),
      original.replace("2026-01-01", "2999-01-01"),
      original.replaceAll("0.1.0-beta.1", "0.1.0-beta.2"),
      original + "\n## [Unreleased]\n",
      original.replace("[0.1.0-beta.1]:", "[wrong]:"),
    ]) {
      await writeFile(file, content);
      await assert.rejects(metadata(cwd));
    }
  });
});

test("remote authorization requires an annotated matching tag and actually pushed ancestry", async () => {
  await repository(async ({ cwd, git }) => {
    const tag = "v0.1.0-beta.1";
    await git("tag", tag);
    await assert.rejects(verifyRelease({ cwd, tag, remote: true, requireTag: true }), /annotated/);
    await git("tag", "-d", tag);
    await git("tag", "-a", tag, "-m", "Beta");
    await git("checkout", "--detach", tag);
    await verifyRelease({ cwd, tag, remote: true, requireTag: true });
    await git("switch", "-c", "feature/unpushed");
    await writeFile(resolve(cwd, "change"), "change");
    await git("add", ".");
    await git("commit", "-m", "Unpushed change");
    await assert.rejects(
      verifyRelease({ cwd, tag, remote: true, requireTag: true }),
      /tagged commit/,
    );
    await git("tag", "-f", "-a", tag, "-m", "Fixture replacement");
    await git("update-ref", "refs/remotes/origin/master", "HEAD");
    await assert.rejects(
      verifyRelease({ cwd, tag, remote: true, requireTag: true }),
      /eligible branch at origin/,
    );
    await git("push", "origin", "HEAD:refs/heads/feature/unpushed");
    await assert.rejects(
      verifyRelease({ cwd, tag, remote: true, requireTag: true }),
      /eligible branch at origin/,
    );
    await git("push", "origin", "HEAD:refs/heads/release/beta");
    await verifyRelease({ cwd, tag, remote: true, requireTag: true });
    await git("push", "origin", ":refs/heads/release/beta");
    await assert.rejects(
      verifyRelease({ cwd, tag, remote: true, requireTag: true }),
      /eligible branch at origin/,
    );
    await git("remote", "set-url", "origin", resolve(cwd, "does-not-exist"));
    await assert.rejects(verifyRelease({ cwd, tag, remote: true, requireTag: true }));
    assert.equal(await git("for-each-ref", "--format=%(refname)", "refs/svt-release/"), "");
  });
});
