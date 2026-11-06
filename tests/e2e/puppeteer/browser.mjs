// Puppeteer launch helper for Student Sync browser checks (CLAUDE.md section O4).
// Uses the system Chrome through puppeteer-core: no browser download, nothing added to production.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer-core";

const here = path.dirname(fileURLToPath(import.meta.url));

export const BASE_URL = process.env.BASE_URL ?? process.env.WEB_BASE_URL ?? "http://localhost:5090";
export const SCREENSHOT_DIR = path.join(here, "screenshots");

/** Same widths as the Playwright suite and DESIGN.md breakpoints. */
export const viewports = {
  mobile: { width: 375, height: 812 },
  tablet: { width: 768, height: 1024 },
  desktop: { width: 1440, height: 900 },
};

export function executablePath() {
  return process.env.PUPPETEER_EXECUTABLE_PATH ?? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
}

export async function launch(opts = {}) {
  const exe = executablePath();
  if (!fs.existsSync(exe)) throw new Error(`Chrome not found at ${exe}; set PUPPETEER_EXECUTABLE_PATH`);
  return puppeteer.launch({ executablePath: exe, headless: process.env.HEADED ? false : true, ...opts });
}

/** Demo password: E2E_PASSWORD, else DEMO_PASSWORD from the git-ignored repo .env. Never hard-code it. */
export function demoPassword() {
  if (process.env.E2E_PASSWORD) return process.env.E2E_PASSWORD;
  const env = fs.readFileSync(path.resolve(here, "../../../.env"), "utf8");
  const line = env.split("\n").find((l) => l.startsWith("DEMO_PASSWORD="));
  if (!line) throw new Error("Set E2E_PASSWORD or DEMO_PASSWORD in .env");
  return line.slice("DEMO_PASSWORD=".length).trim();
}

/** Fictional demo accounts created by `seed-demo` (same list as tests/helpers.ts). */
export const users = {
  admin: "admin@horizon.example.test",
  teacher: "amira@horizon.example.test",
  privateTeacher: "priya@horizon.example.test",
  student: "aisha@horizon.example.test",
  fatima: "fatima@horizon.example.test",
  otherAdmin: "admin@northbridge.example.test",
};

/** Opens a page at a viewport and attaches console-error and failed-request collectors. */
export async function newPage(browser, viewport = viewports.desktop) {
  const page = await browser.newPage();
  await page.setViewport(viewport);
  const consoleErrors = [];
  const failedRequests = [];
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });
  page.on("pageerror", (e) => consoleErrors.push(e.message));
  page.on("requestfailed", (r) => failedRequests.push(`${r.method()} ${r.url()} — ${r.failure()?.errorText}`));
  page.on("response", (r) => { if (r.status() >= 400) failedRequests.push(`${r.request().method()} ${r.url()} — HTTP ${r.status()}`); });
  return { page, consoleErrors, failedRequests };
}

export async function signIn(page, email) {
  await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
  await page.type("input[type=email], #email", email);
  await page.type("input[type=password]", demoPassword());
  await Promise.all([
    page.waitForNavigation({ waitUntil: "networkidle0" }),
    page.click("button[type=submit]"),
  ]);
  if (page.url().includes("/login")) throw new Error(`Sign-in failed for ${email}`);
}

export async function screenshot(page, name) {
  fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
  const file = path.join(SCREENSHOT_DIR, `${name}.png`);
  await page.screenshot({ path: file, fullPage: true });
  return file;
}
