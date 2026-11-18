import assert from "node:assert/strict";
import { createServer } from "node:http";
import { readFile, writeFile, mkdir, cp, readdir, mkdtemp } from "node:fs/promises";
import { resolve, relative, extname, sep } from "node:path";
import { once } from "node:events";
import { platform, release, arch } from "node:os";
import puppeteer from "puppeteer";
import { PNG } from "pngjs";
import { root, tools, pins, command, cli, pixels, fileHash, assertSuite } from "./common.mjs";

// Both entry points use identical tool pins, capture/readiness and cleanup behavior.
export async function runRendering({ purpose, source, baseline = null }, checks) {
  assert.ok(["release-check", "exploratory-probe"].includes(purpose));
  const artifactRoot = resolve(root, "artifacts");
  await mkdir(artifactRoot, { recursive: true });
  const output = await mkdtemp(resolve(artifactRoot, `native-${purpose}-render-${platform()}-`));
  const work = resolve(output, "documents");
  await cp(source, work, { recursive: true });
  if (baseline) await cp(baseline, resolve(work, "__baseline"), { recursive: true });
  const suite = JSON.parse(await readFile(resolve(work, "suite.json")));
  assertSuite(suite);
  const font = resolve(tools, "DejaVuSans.ttf");
  assert.equal(await fileHash(font), pins.font.file_sha256);
  const renderer = resolve(tools, "resvg");
  assert.equal((await command(renderer, ["--version"])).stdout.trim(), pins.resvg_version);
  const executablePath = await puppeteer.executablePath();
  const report = {
    format_version: 0,
    purpose,
    git_head: (await command("git", ["rev-parse", "HEAD"])).stdout.trim(),
    dirty: Boolean((await command("git", ["status", "--porcelain"])).stdout.trim()),
    environment: {
      platform: platform(),
      release: release(),
      architecture: arch(),
      node: process.version,
    },
    tools: {
      chrome: pins.chrome,
      resvg: pins.resvg_version,
      font: pins.font,
      chrome_binary_sha256: await fileHash(executablePath),
      resvg_binary_sha256: await fileHash(renderer),
    },
    configuration: {
      cases: suite.cases,
      baseline_comparison: Boolean(baseline),
      width: suite.width,
      height: suite.height,
      device_scale: 1,
      background: "white",
      pixel_tolerance: 0,
      cross_renderer_pixel_equality: false,
    },
    hashes: {},
    comparisons: [],
    status: "running",
  };
  async function hashes(folder, prefix = "") {
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      const path = resolve(folder, entry.name),
        name = `${prefix}${entry.name}`;
      if (entry.isDirectory()) await hashes(path, name + "/");
      else if (entry.isFile()) report.hashes[name] = await fileHash(path);
    }
  }
  await hashes(source, "examples/");
  if (baseline) await hashes(baseline, "baseline/");
  report.environment.dotnet = (await command("dotnet", ["--version"])).stdout.trim();
  if (platform() === "linux")
    report.environment.system_packages = (
      await command("dpkg-query", ["-W", "-f=${Package}=${Version}\n"])
    ).stdout
      .trim()
      .split("\n");
  const assetManifest = JSON.parse(await readFile(resolve(source, "assets/manifest.json")));
  for (const [name, expected] of Object.entries(assetManifest.files))
    assert.equal(await fileHash(resolve(source, "assets", name)), expected, "Asset hash: " + name);
  for (const name of [
    "common.mjs",
    "rendering.mjs",
    purpose === "release-check" ? "release-render.mjs" : "probe-render.mjs",
    "tools.json",
    "package-lock.json",
  ])
    report.hashes["eng/visual/" + name] = await fileHash(resolve(root, "eng/visual", name));
  report.hashes["svt.dll"] = await fileHash(cli);
  report.hashes["library.dll"] = await fileHash(
    resolve(cli, "../Bathos.Graphics.SvgTemplates.dll"),
  );
  let browser;
  const server = createServer(async (request, response) => {
    try {
      const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
      const path = pathname === "/font.ttf" ? font : resolve(work, "." + pathname);
      if (path !== font && !path.startsWith(work + sep)) {
        response.writeHead(403).end();
        return;
      }
      const bytes = await readFile(path);
      const type =
        { ".svg": "image/svg+xml", ".png": "image/png", ".jpg": "image/jpeg", ".ttf": "font/ttf" }[
          extname(path)
        ] || "application/octet-stream";
      response.writeHead(200, { "Content-Type": type, "Cache-Control": "no-store" });
      response.end(bytes);
    } catch {
      response.writeHead(404).end();
    }
  });
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const base = `http://127.0.0.1:${server.address().port}`;
  async function chrome(path, destination) {
    const page = await browser.newPage();
    const failures = [];
    try {
      await page.setViewport({ width: suite.width, height: suite.height, deviceScaleFactor: 1 });
      page.setDefaultTimeout(15000);
      page.setDefaultNavigationTimeout(15000);
      await page.setRequestInterception(true);
      page.on("request", (request) => {
        if (request.url().startsWith(base + "/") || request.url().startsWith("data:"))
          request.continue();
        else {
          failures.push("Unexpected external resource");
          request.abort();
        }
      });
      page.on("requestfailed", (request) => {
        failures.push(`Resource failed: ${new URL(request.url()).pathname}`);
      });
      page.on("response", (response) => {
        if (response.status() >= 400 && !response.url().endsWith("/favicon.ico"))
          failures.push(`HTTP ${response.status()}: ${new URL(response.url()).pathname}`);
      });
      await page.goto(
        `${base}/${relative(work, path).split(sep).map(encodeURIComponent).join("/")}`,
        { waitUntil: "load" },
      );
      // Explicit resource/font completion, not elapsed time or network-idle polling.
      await page.evaluate(async () => {
        const style = document.createElementNS("http://www.w3.org/2000/svg", "style");
        style.textContent =
          '@font-face { font-family:"DejaVu Sans"; src:url("/font.ttf"); font-weight:normal; font-style:normal; }';
        document.documentElement.append(style);
        const loaded = await document.fonts.load('18px "DejaVu Sans"');
        if (loaded.length !== 1) throw new Error("Pinned font was not loaded");
        await document.fonts.ready;
        await Promise.all(
          [...document.querySelectorAll("image")].map(async (element) => {
            const reference =
              element.getAttribute("href") ??
              element.getAttributeNS("http://www.w3.org/1999/xlink", "href");
            if (!reference) throw new Error("Image has no resource");
            const image = new Image();
            image.src = new URL(reference, element.baseURI).href;
            await image.decode();
            if (!image.naturalWidth || !image.naturalHeight) throw new Error("Empty decoded image");
          }),
        );
      });
      assert.deepEqual(failures, []);
      const bytes = await page.screenshot({
        type: "png",
        captureBeyondViewport: false,
        omitBackground: false,
      });
      const decoded = PNG.sync.read(Buffer.from(bytes));
      assert.equal(decoded.width, suite.width);
      assert.equal(decoded.height, suite.height);
      await writeFile(destination, bytes);
    } finally {
      await page.close();
    }
  }
  async function resvg(path, destination) {
    const result = await command(renderer, [
      "--skip-system-fonts",
      "--use-font-file",
      font,
      "--font-family",
      pins.font.family,
      "--background",
      "white",
      path,
      destination,
    ]);
    assert.equal(result.stderr.trim(), "", "Renderer diagnostics");
    const image = PNG.sync.read(await readFile(destination));
    assert.equal(image.width, suite.width);
    assert.equal(image.height, suite.height);
  }
  async function compare(label, expected, actual) {
    const result = pixels(await readFile(expected), await readFile(actual));
    report.comparisons.push({
      label,
      changed_pixels: result.changed_pixels,
      maximum_channel_difference: result.maximum_channel_difference,
    });
    if (result.changed_pixels)
      await writeFile(resolve(output, label.replaceAll("/", "-") + "-diff.png"), result.diff);
  }
  try {
    browser = await puppeteer.launch({
      executablePath,
      headless: true,
      protocolTimeout: 15000,
      args: [
        "--force-color-profile=srgb",
        ...(process.env.SVT_CHROME_NO_SANDBOX === "1" ? ["--no-sandbox"] : []),
      ],
    });
    assert.ok((await browser.version()).includes(pins.chrome));
    await checks({
      work,
      output,
      suite,
      report,
      baseline,
      engines: [
        ["chrome", chrome],
        ["resvg", resvg],
      ],
      compare,
    });
    report.status = report.comparisons.every((item) => item.changed_pixels === 0)
      ? purpose === "release-check"
        ? "passed"
        : "completed"
      : "failed";
  } catch (error) {
    report.status = "failed";
    report.error = String(error.message).replaceAll(root, "<repository>");
  } finally {
    const cleanup = await Promise.allSettled([
      browser?.close(),
      new Promise((resolve, reject) =>
        server.close((error) => (error ? reject(error) : resolve())),
      ),
    ]);
    const errors = cleanup.filter((result) => result.status === "rejected");
    if (errors.length) {
      report.status = "failed";
      report.cleanup_errors = errors.map((result) =>
        String(result.reason).replaceAll(root, "<repository>"),
      );
    }
    await writeFile(resolve(output, "report.json"), JSON.stringify(report, null, 2) + "\n");
    console.log(
      `${report.status}: ${relative(root, output)}/report.json (${report.comparisons.length} comparisons)`,
    );
  }
  if (report.status === "failed") process.exitCode = 1;
  return report;
}
