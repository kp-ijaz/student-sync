// "Keep me signed in": the sign-in survives closing the browser and restarting the web server; unticked, it ends with
// the browser session; signing out ends it on the server too.
//   RESTART_WEB="<command that restarts the web app>" SESSION_DIR=<web Sessions:StorePath> node puppeteer/session-persistence.mjs
// Without RESTART_WEB the restart step is reported as not run. Closing the browser is emulated by starting a new
// browser with only the persistent cookies, which is what a real browser keeps.
import fs from "node:fs";
import path from "node:path";
import { execSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { launch, newPage, signIn, users, viewports, demoPassword, BASE_URL, SCREENSHOT_DIR } from "./browser.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const axeSource = fs.readFileSync(path.resolve(here, "../node_modules/axe-core/axe.min.js"), "utf8");
const outDir = path.join(SCREENSHOT_DIR, "session-persistence");
fs.mkdirSync(outDir, { recursive: true });
const sessionDir = process.env.SESSION_DIR;
const failures = [];
const check = (name, ok, detail = "") => { console.log(`${ok ? "ok  " : "FAIL"} ${name}${detail ? " — " + detail : ""}`); if (!ok) failures.push(name); };
const DAY = 24 * 60 * 60;
const sessionCookie = async (page) => (await page.cookies()).find((c) => c.name === "ss_session");
const entries = () => (sessionDir && fs.existsSync(sessionDir) ? fs.readdirSync(sessionDir).filter((f) => f.endsWith(".entry")) : []);
const pathOf = (page) => new URL(page.url()).pathname;

/** A new browser holding only the cookies a real browser keeps after it is closed (the persistent ones). */
async function reopen(cookies) {
  const browser = await launch();
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.desktop);
  const kept = cookies.filter((c) => !c.session);
  if (kept.length) await page.setCookie(...kept);
  await page.goto(`${BASE_URL}/student`, { waitUntil: "networkidle0" });
  return { browser, page, consoleErrors, failedRequests, kept: kept.length };
}
const signedInAs = async (page, name) => pathOf(page) === "/student"
  && (await page.waitForFunction((n) => document.querySelector("h1")?.innerText.includes(n), { timeout: 15000 }, name).then(() => true, () => false));

// ---------- Sign-in page: the option, its default, keyboard and accessibility ----------
{
  const browser = await launch();
  try {
    for (const [name, vp] of [["1440", viewports.desktop], ["375", viewports.mobile]]) {
      const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
      await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
      const box = await page.$eval("input[name=remember]", (el) => ({ checked: el.checked, label: el.closest("label")?.innerText.trim(), hint: document.getElementById(el.getAttribute("aria-describedby"))?.innerText,
        size: Math.round(el.closest("label").getBoundingClientRect().height) }));
      check(`login ${name}: "Keep me signed in" is ticked by default with its hint`, box.checked && box.label === "Keep me signed in" && /shared/.test(box.hint ?? ""), JSON.stringify(box));
      if (vp.width < 1024) check(`login ${name}: option is at least 44 px tall to tap`, box.size >= 44, `${box.size}px`);
      check(`login ${name}: no horizontal overflow`, await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth));
      await page.evaluate(axeSource);
      const axe = await page.evaluate(() => window.axe.run(document, { runOnly: ["wcag2a", "wcag2aa", "wcag21aa"] }));
      const bad = axe.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
      check(`login ${name}: axe clean`, bad.length === 0, bad.map((v) => v.id).join(", "));
      await page.screenshot({ path: path.join(outDir, `login-${name}.png`), fullPage: true });
      if (name === "1440") {
        await page.focus("#email");
        const order = [];
        for (let i = 0; i < 4; i++) {
          await page.keyboard.press("Tab");
          order.push(await page.evaluate(() => { const a = document.activeElement; return a.id || a.name || a.getAttribute("aria-label") || a.innerText.trim(); }));
        }
        check("keyboard order: email → password → show password → keep signed in → Sign in", order.join(" → ") === "password → Show password → remember → Sign in", order.join(" → "));
        await page.focus("input[name=remember]");
        await page.screenshot({ path: path.join(outDir, "login-1440-focus.png"), clip: { x: 860, y: 520, width: 460, height: 160 } });
        // A failed attempt keeps the person's choice.
        await page.click("input[name=remember]");
        await page.type("#email", users.student); await page.type("#password", "definitely-wrong-1A");
        await Promise.all([page.waitForNavigation({ waitUntil: "networkidle0" }), page.click("button[type=submit]")]);
        check("failed sign-in with the box unticked keeps it unticked", page.url().includes("error=") && !(await page.$eval("input[name=remember]", (el) => el.checked)));
      }
      check(`login ${name}: no console errors`, consoleErrors.length === 0 && failedRequests.filter((r) => !r.includes("/account/login")).length === 0, [...consoleErrors, ...failedRequests].join("; "));
      await page.close();
    }
  } finally { await browser.close(); }
}

// ---------- Kept: survives closing the browser and a web restart; sign-out ends it everywhere ----------
let keptCookies;
{
  const before = entries().length;
  const browser = await launch();
  try {
    const { page } = await newPage(browser, viewports.desktop);
    await signIn(page, users.student); // the box is ticked by default
    const c = await sessionCookie(page);
    const days = c && !c.session ? (c.expires - Date.now() / 1000) / DAY : 0;
    check("kept: the sign-in cookie is persistent for about 30 days", !!c && !c.session && days > 29 && days <= 30.01, c ? `session=${c.session} days=${days.toFixed(2)} httpOnly=${c.httpOnly} sameSite=${c.sameSite}` : "no cookie");
    check("kept: the cookie stays HttpOnly and SameSite=Lax", !!c && c.httpOnly && c.sameSite === "Lax");
    keptCookies = await page.cookies();
  } finally { await browser.close(); }

  if (sessionDir) {
    const files = entries();
    check("server: the session is written to the session store", files.length === before + 1, `${before} → ${files.length}`);
    const clear = files.some((f) => { const t = fs.readFileSync(path.join(sessionDir, f), "latin1"); return t.includes("eyJ") || t.includes("accessToken") || t.includes("refreshToken"); });
    check("server: stored sessions contain no readable tokens (encrypted)", !clear);
  } else console.log("skip server-side store checks (SESSION_DIR not set)");

  let s = await reopen(keptCookies);
  check("kept: still signed in after closing and reopening the browser", s.kept > 0 && await signedInAs(s.page, "Aisha"), pathOf(s.page));
  await s.page.screenshot({ path: path.join(outDir, "reopened-1440.png") });
  await s.browser.close();

  if (process.env.RESTART_WEB) {
    execSync(process.env.RESTART_WEB, { stdio: "inherit" });
    s = await reopen(keptCookies);
    check("kept: still signed in after the web server restarted", await signedInAs(s.page, "Aisha"), pathOf(s.page));
    // The dashboard's data comes from the API with the stored tokens, so this also proves they were restored.
    const loaded = await s.page.waitForFunction(() => /Your classes/.test(document.body.innerText), { timeout: 15000 }).then(() => true, () => false);
    check("kept: dashboard data loads after the restart", loaded);
    check("kept: no console errors or failed requests after the restart", s.consoleErrors.length === 0 && s.failedRequests.length === 0, [...s.consoleErrors, ...s.failedRequests].join("; "));
    await s.browser.close();
  } else console.log("NOT RUN  web restart (set RESTART_WEB)");

  // Sign out from the reopened browser, then try the old cookie again.
  s = await reopen(keptCookies);
  const countBefore = entries().length;
  await Promise.all([s.page.waitForNavigation({ waitUntil: "networkidle0" }), s.page.$eval('form[action$="/account/logout"]', (f) => f.requestSubmit())]);
  check("sign-out returns to the sign-in page", pathOf(s.page) === "/login" && s.page.url().includes("signedOut"), s.page.url());
  check("sign-out removes the sign-in cookie", !(await sessionCookie(s.page)));
  if (sessionDir) check("sign-out deletes the stored session", entries().length === countBefore - 1, `${countBefore} → ${entries().length}`);
  await s.browser.close();
  s = await reopen(keptCookies);
  check("an old cookie copied after sign-out no longer signs in", pathOf(s.page) === "/login", pathOf(s.page));
  await s.browser.close();
}

// ---------- Not kept: ends with the browser session ----------
{
  const browser = await launch();
  let cookies;
  try {
    const { page } = await newPage(browser, viewports.desktop);
    await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
    await page.click("input[name=remember]");
    await page.type("#email", users.student); await page.type("#password", demoPassword());
    await Promise.all([page.waitForNavigation({ waitUntil: "networkidle0" }), page.click("button[type=submit]")]);
    check("not kept: signed in", pathOf(page) === "/student", pathOf(page));
    const c = await sessionCookie(page);
    check("not kept: the cookie is a browser-session cookie", !!c && c.session, c ? `session=${c.session}` : "no cookie");
    cookies = await page.cookies();
  } finally { await browser.close(); }
  const s = await reopen(cookies);
  check("not kept: signed out after closing and reopening the browser", pathOf(s.page) === "/login", pathOf(s.page));
  await s.browser.close();
}

console.log(failures.length ? `\n${failures.length} check(s) failed` : `\nAll checks passed. Screenshots: ${outDir}`);
process.exit(failures.length ? 1 : 0);
