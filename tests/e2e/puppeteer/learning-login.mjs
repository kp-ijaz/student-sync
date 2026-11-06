// Learning cards (assignments / exams), student home greeting, class page card, sign-in password toggle.
import fs from "node:fs"; import path from "node:path";
import { BASE_URL, SCREENSHOT_DIR, launch, newPage, signIn, users, viewports } from "./browser.mjs";
const prefix = process.argv[2] ?? "after2";
const shot = async (page, name, full = false) => { const f = path.join(SCREENSHOT_DIR, `${name}.png`); await page.screenshot({ path: f, fullPage: full }); return f; };
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const browser = await launch();
try {
  for (const [vpName, vp] of Object.entries({ mobile: viewports.mobile, desktop: viewports.desktop })) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
    await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
    await page.type("#password", "secret-not-real");
    await page.click(".pw-toggle"); await wait(200);
    const t = await page.evaluate(() => ({ type: document.getElementById("password").type, pressed: document.querySelector(".pw-toggle").getAttribute("aria-pressed"), label: document.querySelector(".pw-toggle").getAttribute("aria-label") }));
    console.log(`login toggle @${vp.width}:`, JSON.stringify(t));
    await shot(page, `${prefix}-login-toggled-${vpName}`);
    await page.evaluate(() => { document.getElementById("password").value = ""; });
    await signIn(page, users.student);
    await page.goto(`${BASE_URL}/learning`, { waitUntil: "networkidle0" }); await wait(500);
    console.log(await shot(page, `${prefix}-learning-assignments-${vpName}`, true));
    await page.click('[role="tab"]:nth-child(3)'); await wait(800);
    console.log(await shot(page, `${prefix}-learning-exams-${vpName}`, true));
    await page.goto(`${BASE_URL}/student`, { waitUntil: "networkidle0" }); await wait(500);
    console.log(await shot(page, `${prefix}-student-home-${vpName}`));
    await page.goto(`${BASE_URL}/assignments`, { waitUntil: "networkidle0" }); await wait(500);
    console.log(await shot(page, `${prefix}-assignments-${vpName}`));
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    console.log(`${vpName}: overflow=${overflow} console errors ${consoleErrors.length}, failed requests ${failedRequests.length}`);
    for (const e of [...consoleErrors, ...failedRequests]) console.log("  - " + e.slice(0, 200));
    await page.close();
  }
  const { page, consoleErrors } = await newPage(browser, viewports.mobile);
  await signIn(page, users.teacher);
  await page.goto(`${BASE_URL}/sessions`, { waitUntil: "networkidle0" }); await wait(500);
  console.log(await shot(page, `${prefix}-teacher-classes-mobile`));
  await page.goto(`${BASE_URL}/assignments`, { waitUntil: "networkidle0" }); await wait(500);
  console.log(await shot(page, `${prefix}-teacher-assignments-mobile`));
  console.log(`teacher: console errors ${consoleErrors.length}`);
  await page.close();
} finally { await browser.close(); }
