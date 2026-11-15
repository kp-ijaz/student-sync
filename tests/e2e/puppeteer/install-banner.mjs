// Install banner (InstallBanner.razor + js/app.js): shown when the browser can install the app, iOS steps on iOS,
// hidden once installed or after "Not now". The browser's own "beforeinstallprompt" event is replaced by a
// synthetic one so the check is deterministic.
//   node puppeteer/install-banner.mjs            (web on BASE_URL; INSTALL_USER overrides the account signed in)
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { launch, newPage, signIn, users, viewports, BASE_URL, SCREENSHOT_DIR } from "./browser.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const axeSource = fs.readFileSync(path.resolve(here, "../node_modules/axe-core/axe.min.js"), "utf8");
const outDir = path.join(SCREENSHOT_DIR, "install-banner");
fs.mkdirSync(outDir, { recursive: true });
const IPHONE = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
const failures = [];
const check = (name, ok, detail = "") => { console.log(`${ok ? "ok  " : "FAIL"} ${name}${detail ? " — " + detail : ""}`); if (!ok) failures.push(name); };
const shot = (page, name) => page.screenshot({ path: path.join(outDir, `${name}.png`) });

/** Only synthetic install events reach the app; counts prompt() calls in window.__prompts. */
const prepare = (page) => page.evaluateOnNewDocument(() => {
  window.__prompts = 0;
  window.addEventListener("beforeinstallprompt", (e) => { if (!e.synthetic) e.stopImmediatePropagation(); }, true);
  window.__offerInstall = () => {
    const e = new Event("beforeinstallprompt", { cancelable: true });
    e.synthetic = true; e.prompt = () => { window.__prompts++; }; e.userChoice = Promise.resolve({ outcome: "accepted" });
    window.dispatchEvent(e);
  };
});
const state = (page) => page.evaluate(() => {
  const b = document.querySelector("ss-install-banner");
  const visible = (el) => !!el && el.getClientRects().length > 0;
  const install = b?.querySelector("[data-install-action=install]"), dismiss = b?.querySelector("[data-install-action=dismiss]");
  return {
    present: !!b, visible: visible(b), top: b ? Math.round(b.getBoundingClientRect().top) : null,
    text: b ? [...b.querySelectorAll(".install-text > *")].filter(visible).map((e) => e.textContent.trim()).join(" | ") : "",
    installVisible: visible(install), installHeight: install ? Math.round(install.getBoundingClientRect().height) : 0,
    dismissSize: dismiss ? Math.round(Math.min(dismiss.getBoundingClientRect().width, dismiss.getBoundingClientRect().height)) : 0,
    overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    prompts: window.__prompts, snoozed: !!localStorage.getItem("ss-install-dismissed"),
  };
});
// Same rule tags as the Playwright accessibility block in tests/journeys.spec.ts.
const axe = async (page, label) => {
  await page.evaluate(axeSource);
  const result = await page.evaluate(() => window.axe.run(document, { runOnly: ["wcag2a", "wcag2aa", "wcag21aa"] }));
  const bad = result.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  check(`axe ${label}: no serious/critical violations`, bad.length === 0, bad.map((v) => `${v.id} (${v.nodes.length}): ${v.nodes.map((n) => n.target.join(" ")).join(" ; ")}`).join(", "));
};

const browser = await launch();
try {
  // ---- Sign-in page, Chromium behaviour ----
  for (const [name, vp] of Object.entries(viewports)) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
    await prepare(page);
    await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
    let s = await state(page);
    check(`login ${name}: banner present but hidden until the browser offers installation`, s.present && !s.visible);
    await page.evaluate(() => window.__offerInstall());
    s = await state(page);
    check(`login ${name}: banner shown at the top with the Install button`, s.visible && s.top === 0 && s.installVisible, JSON.stringify(s));
    check(`login ${name}: install wording, no iOS steps`, s.text.includes("Install Student Sync") && s.text.includes("home screen") && !s.text.includes("Share"), s.text);
    check(`login ${name}: no horizontal overflow`, s.overflow <= 0, `${s.overflow}px`);
    if (vp.width < 1024) check(`login ${name}: touch targets ≥ 44 px`, s.installHeight >= 44 && s.dismissSize >= 44, `${s.installHeight}/${s.dismissSize}`);
    await shot(page, `login-${vp.width}`);
    if (name !== "tablet") await axe(page, `login ${name}`);
    if (name === "desktop") {
      // Keyboard: both buttons are reachable and show the focus ring.
      await page.focus("[data-install-action=install]");
      await page.keyboard.press("Tab");
      const focused = await page.evaluate(() => document.activeElement?.getAttribute("aria-label"));
      check("keyboard: Tab moves from Install to Not now", focused === "Not now", String(focused));
      await shot(page, "login-1440-focus");
      await page.click("[data-install-action=install]");
      s = await state(page);
      check("Install calls the browser prompt once and hides the banner", s.prompts === 1 && !s.visible, JSON.stringify(s));
      await page.evaluate(() => window.__offerInstall());
      await page.click("[data-install-action=dismiss]");
      s = await state(page);
      check("Not now hides the banner and remembers it", !s.visible && s.snoozed);
      await page.reload({ waitUntil: "networkidle0" });
      await page.evaluate(() => window.__offerInstall());
      check("stays hidden after a reload while snoozed", !(await state(page)).visible);
      await page.evaluate(() => localStorage.setItem("ss-install-dismissed", String(Date.now() - 15 * 24 * 60 * 60 * 1000)));
      await page.reload({ waitUntil: "networkidle0" });
      await page.evaluate(() => window.__offerInstall());
      check("offered again 15 days after Not now", (await state(page)).visible);
      // Chrome cannot emulate display-mode, so report "standalone" the way an installed app's window would.
      await page.evaluateOnNewDocument(() => {
        const real = window.matchMedia.bind(window);
        window.matchMedia = (q) => (q.includes("display-mode: standalone") ? { matches: true, media: q, addEventListener() {}, removeEventListener() {} } : real(q));
      });
      await page.reload({ waitUntil: "networkidle0" });
      await page.evaluate(() => window.__offerInstall());
      check("never shown inside the installed app (standalone)", !(await state(page)).visible);
    }
    check(`login ${name}: no console errors / failed requests`, consoleErrors.length === 0 && failedRequests.length === 0, [...consoleErrors, ...failedRequests].join("; "));
    await page.close();
  }

  // ---- iPhone: no install event exists, so the Share-menu steps are shown ----
  {
    const { page } = await newPage(browser, viewports.mobile);
    await page.setUserAgent(IPHONE);
    await prepare(page);
    await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
    const s = await state(page);
    check("iPhone: banner shown with the Share steps and no Install button", s.visible && s.text.includes("Share") && s.text.includes("Add to Home Screen") && !s.installVisible, JSON.stringify(s));
    check("iPhone: no horizontal overflow", s.overflow <= 0, `${s.overflow}px`);
    await shot(page, "login-375-iphone");
    await axe(page, "login iPhone");
    await page.close();
  }

  // ---- Signed in (main shell) ----
  for (const [name, vp] of [["desktop", viewports.desktop], ["mobile", viewports.mobile]]) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
    await prepare(page);
    await signIn(page, process.env.INSTALL_USER ?? users.student);
    await page.waitForSelector("ss-install-banner");
    await new Promise((r) => setTimeout(r, 1500)); // prerendered markup is replaced when the page turns interactive
    await page.evaluate(() => { localStorage.removeItem("ss-install-dismissed"); window.__offerInstall(); });
    const s = await state(page);
    const topbarTop = await page.evaluate(() => Math.round(document.querySelector(".topbar").getBoundingClientRect().top));
    check(`shell ${name}: banner shown above the app bar`, s.visible && s.installVisible && s.top === 0 && topbarTop > 0, JSON.stringify({ ...s, topbarTop }));
    check(`shell ${name}: no horizontal overflow`, s.overflow <= 0, `${s.overflow}px`);
    await shot(page, `shell-${vp.width}`);
    await axe(page, `shell ${name}`);
    await page.click("[data-install-action=dismiss]");
    check(`shell ${name}: Not now hides it`, !(await state(page)).visible);
    check(`shell ${name}: no console errors / failed requests`, consoleErrors.length === 0 && failedRequests.length === 0, [...consoleErrors, ...failedRequests].join("; "));
    await page.close();
  }
} finally { await browser.close(); }
console.log(failures.length ? `\n${failures.length} check(s) failed` : `\nAll checks passed. Screenshots: ${outDir}`);
process.exit(failures.length ? 1 : 0);
