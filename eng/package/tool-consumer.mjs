import assert from "node:assert/strict";
import { mkdir, writeFile, readFile, cp, readdir, rm } from "node:fs/promises";
import { resolve, join } from "node:path";
import { command, fileHash, tool, cliPackageId, packageId, toolChecks, json } from "./common.mjs";
import { verifyPublicDebugging } from "../release/public-evidence.mjs";

// Every mode has its own home, NuGet caches and resolver cache. No checkout outputs are used.
export async function consumeTool({ bundle, manifest, work, output, feed, emptyFeed, published }) {
  const product = manifest.products.cli;
  const report = { status: "running", checks: [], modes: {} };
  const expectedVersion = `svt ${manifest.version} (commit ${manifest.source.commit.slice(0, 12)})\n`;
  const xml = (s) => s.replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
  for (const mode of ["global", "local", "one-off"]) {
    const folder = resolve(work, "tool-" + mode);
    const home = resolve(folder, "home");
    await mkdir(home, { recursive: true });
    await cp(resolve(bundle, "global.json"), resolve(folder, "global.json"));
    await cp(
      resolve(bundle, "examples/quickstart/template.svg"),
      resolve(folder, "template with spaces.svg"),
    );
    await cp(
      resolve(bundle, "examples/quickstart/bindings.json"),
      resolve(folder, "bindings with spaces.json"),
    );
    await cp(resolve(bundle, "examples/quickstart/landscape.png"), resolve(folder, "landscape.png"));
    const config = resolve(folder, "NuGet.config");
    const env = {
      ...process.env,
      HOME: home,
      USERPROFILE: home,
      DOTNET_CLI_HOME: home,
      NUGET_PACKAGES: resolve(folder, "cache"),
      NUGET_HTTP_CACHE_PATH: resolve(folder, "http"),
      NUGET_PLUGINS_CACHE_PATH: resolve(folder, "plugins"),
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
    };
    for (const key of [
      "DOTNET_ADDITIONAL_DEPS",
      "DOTNET_SHARED_STORE",
      "DOTNET_STARTUP_HOOKS",
      "DOTNET_ROLL_FORWARD",
      "NUGET_FALLBACK_PACKAGES",
    ])
      delete env[key];
    let step = 0;
    async function execute(file, args, expected = 0, input, extra = {}) {
      let result;
      try {
        result = { ...(await command(file, args, { cwd: folder, env, input, ...extra })), code: 0 };
      } catch (error) {
        if (!Number.isInteger(error.code) || error.killed || error.signal) throw error;
        result = error;
      }
      await writeFile(
        resolve(output, `tool-${mode}-${step++}.log`),
        (result.stdout + result.stderr).replaceAll(work, "<consumer>"),
      );
      if (expected === "host-failure")
        assert.notEqual(result.code, 0, "Host failure must be observable");
      else
        assert.equal(
          result.code,
          expected,
          `${mode}: ${file} ${args.join(" ")}\n${result.stdout}${result.stderr}`,
        );
      return result;
    }
    const configure = (source) =>
      writeFile(
        config,
        `<configuration><packageSources><clear/><add key="only" value="${xml(source)}"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>`,
      );
    await configure(emptyFeed);
    await execute("dotnet", ["--info"]); // Setup is deliberately outside application capture.
    // An empty, explicitly selected source must fail before any candidate enters this mode's cache.
    const absent = await execute(
      "dotnet",
      [
        "tool",
        "install",
        cliPackageId,
        "--tool-path",
        resolve(folder, "negative"),
        "--version",
        manifest.version,
        "--configfile",
        config,
      ],
      1,
    );
    assert.match(absent.stdout + absent.stderr, /not found|unable to find|NU1101|does not exist/i);
    await configure(published ? "https://api.nuget.org/v3/index.json" : feed);
    let invoke;
    if (mode === "global") {
      await execute("dotnet", [
        "tool",
        "install",
        "--global",
        cliPackageId,
        "--version",
        manifest.version,
        "--configfile",
        config,
      ]);
      const shim = resolve(home, ".dotnet/tools", process.platform === "win32" ? "svt.exe" : "svt");
      invoke = (args, code = 0, input) => execute(shim, args, code, input);
    } else if (mode === "local") {
      await execute("dotnet", ["new", "tool-manifest"]);
      await execute("dotnet", [
        "tool",
        "install",
        "--local",
        cliPackageId,
        "--version",
        manifest.version,
        "--configfile",
        config,
      ]);
      const local = JSON.parse(await readFile(resolve(folder, "dotnet-tools.json")));
      assert.equal(local.tools[cliPackageId.toLowerCase()].version, manifest.version);
      // A committed manifest must restore without the installing user's caches or resolver entries.
      for (const path of [
        home,
        env.NUGET_PACKAGES,
        env.NUGET_HTTP_CACHE_PATH,
        env.NUGET_PLUGINS_CACHE_PATH,
      ])
        await rm(path, { recursive: true, force: true });
      await mkdir(home, { recursive: true });
      await execute("dotnet", ["--info"]);
      await execute("dotnet", ["tool", "restore", "--configfile", config]);
      invoke = (args, code = 0, input) =>
        execute("dotnet", ["tool", "run", "svt", "--", ...args], code, input);
    } else {
      const prefix = [
        "tool",
        "exec",
        `${cliPackageId}@${manifest.version}`,
        "--configfile",
        config,
        "--",
      ];
      const cold = await execute("dotnet", [...prefix, "--version"]);
      assert.ok(
        cold.stdout.endsWith(expectedVersion),
        "Cold SDK output must include exact tool identity",
      );
      const warm = await execute("dotnet", [...prefix, "--version"]);
      assert.ok(warm.stdout.endsWith(expectedVersion));
      report.modes[mode] = {
        cold_stdout: cold.stdout,
        warm_stdout: warm.stdout,
        cold_stderr: cold.stderr,
        warm_stderr: warm.stderr,
      };
      await execute(
        "dotnet",
        [
          "tool",
          "exec",
          `${cliPackageId}@0.0.0-missing`,
          "--configfile",
          config,
          "--",
          "--version",
        ],
        1,
      );
      // Acquisition is separate. Further assertions use the exact restored entry point directly.
    }
    const archives = [];
    const binaries = [];
    async function visit(path) {
      for (const entry of await readdir(path, { withFileTypes: true })) {
        const next = join(path, entry.name);
        if (entry.isDirectory()) await visit(next);
        else if (entry.isFile()) {
          if (
            entry.name.toLowerCase() === `${cliPackageId}.${manifest.version}.nupkg`.toLowerCase()
          )
            archives.push(next);
          if (entry.name === "svt.dll") binaries.push(next);
        }
      }
    }
    await visit(folder);
    assert.ok(archives.length > 0 && binaries.length > 0, "No isolated installed package found");
    for (const archive of archives) {
      if (published) {
        await execute("dotnet", ["nuget", "verify", archive, "--all"]);
        await execute("dotnet", [
          tool,
          "compare-published",
          resolve(bundle, product.file),
          archive,
        ]);
      } else assert.equal(await fileHash(archive), product.sha256);
    }
    for (const binary of binaries) {
      assert.equal(await fileHash(binary), product.assembly_sha256);
      assert.equal(await fileHash(resolve(binary, "../svt.pdb")), product.pdb_sha256);
      assert.equal(
        await fileHash(resolve(binary, `../${packageId}.dll`)),
        manifest.products.library.assembly_sha256,
      );
      assert.equal(
        await fileHash(resolve(binary, `../${packageId}.pdb`)),
        manifest.products.library.pdb_sha256,
      );
    }
    const missingRuntime = await execute(
      "dotnet",
      ["exec", "--fx-version", "99.0.0", binaries[0], "--version"],
      "host-failure",
    );
    assert.ok(!missingRuntime.stdout.includes(expectedVersion));
    assert.match(missingRuntime.stderr + missingRuntime.stdout, /framework|runtime|\.NET/i);
    if (!invoke)
      invoke = (args, code = 0, input) => execute("dotnet", [binaries[0], ...args], code, input);
    assert.equal((await invoke(["--version"])).stdout, expectedVersion);
    assert.match((await invoke(["inspect", "--help"])).stdout, /inspect/);
    const inspected = await invoke([
      "inspect",
      "template with spaces.svg",
      "--format",
      "json",
      "--diagnostics",
      "json",
    ]);
    const inspection = JSON.parse(inspected.stdout);
    assert.ok(JSON.stringify(inspection).includes("heading"));
    assert.deepEqual(JSON.parse(inspected.stderr).diagnostics, []);
    const stdin = await invoke(
      ["inspect", "-", "--format", "json", "--diagnostics", "json"],
      0,
      await readFile(resolve(folder, "template with spaces.svg")),
    );
    assert.deepEqual(JSON.parse(stdin.stdout), inspection);
    const bound = await invoke([
      "bind",
      "template with spaces.svg",
      "--bindings",
      "bindings with spaces.json",
      "--output",
      "result with spaces.svg",
    ]);
    assert.equal(bound.stdout, "");
    assert.equal(bound.stderr, "");
    const svg = await readFile(resolve(folder, "result with spaces.svg"), "utf8");
    assert.match(svg, /Trail photography/);
    assert.match(svg, /June 12 · 9 AM · Pine Ridge/);
    assert.match(svg, /#166534/);
    const artwork = await readFile(resolve(folder, "landscape.png"));
    assert.ok(svg.includes(`data:image/png;base64,${artwork.toString("base64")}`));
    const before = await fileHash(resolve(folder, "result with spaces.svg"));
    const collision = await invoke(
      [
        "bind",
        "template with spaces.svg",
        "--bindings",
        "bindings with spaces.json",
        "--output",
        "result with spaces.svg",
        "--diagnostics",
        "json",
      ],
      3,
    );
    assert.ok(JSON.parse(collision.stderr).diagnostics.length > 0);
    assert.equal(await fileHash(resolve(folder, "result with spaces.svg")), before);
    await writeFile(resolve(folder, "empty.json"), "{}");
    const raw = await invoke([
      "bind",
      "template with spaces.svg",
      "--bindings",
      "empty.json",
      "--output",
      "-",
      "--diagnostics",
      "json",
    ]);
    assert.match(raw.stdout, /^<\?xml|^<svg/);
    assert.deepEqual(JSON.parse(raw.stderr).diagnostics, []);
    const bad = await invoke(
      ["inspect", "missing.svg", "--format", "json", "--diagnostics", "json"],
      3,
    );
    assert.equal(bad.stdout, "");
    assert.ok(JSON.parse(bad.stderr).diagnostics.length > 0);
    if (mode === "one-off") {
      const prefix = [
        "tool",
        "exec",
        `${cliPackageId}@${manifest.version}`,
        "--configfile",
        config,
        "--",
      ];
      const forwarded = await execute(
        "dotnet",
        [...prefix, "inspect", "-", "--format", "json", "--diagnostics", "json"],
        0,
        await readFile(resolve(folder, "template with spaces.svg")),
      );
      // Observe any SDK prefix separately; never silently parse it as application output.
      assert.ok(forwarded.stdout.endsWith(inspected.stdout));
      assert.ok(forwarded.stderr.endsWith(inspected.stderr));
      report.modes[mode].prepared_wrapper_stdout_prefix = forwarded.stdout.slice(
        0,
        -inspected.stdout.length,
      );
      await execute("dotnet", [
        ...prefix,
        "bind",
        "template with spaces.svg",
        "--bindings",
        "bindings with spaces.json",
        "--output",
        "one off.svg",
      ]);
      assert.equal(await readFile(resolve(folder, "one off.svg"), "utf8"), svg);
      const alias = await execute("dotnet", [
        "dnx",
        `${cliPackageId}@${manifest.version}`,
        "--configfile",
        config,
        "--",
        "--version",
      ]);
      assert.ok(alias.stdout.endsWith(expectedVersion));
      await execute("dotnet", [...prefix, "inspect", "missing.svg"], 3);
    }
    const missing = await execute("dotnet", ["tool", "run", "svt-uninstalled"], 1);
    assert.ok(missing.stdout + missing.stderr);
    report.modes[mode] = {
      ...report.modes[mode],
      status: "passed",
      package_sha256: await fileHash(archives[0]),
      assembly_sha256: await fileHash(binaries[0]),
      pdb_sha256: product.pdb_sha256,
      ...(published ? { repository_signature: "passed", original_contents: "passed" } : {}),
    };
  }
  if (published) report.debugging = await verifyPublicDebugging(bundle, manifest, output, "cli");
  report.status = "passed";
  report.checks = [...toolChecks];
  await json(resolve(output, "tool-report.json"), report);
  return report;
}
