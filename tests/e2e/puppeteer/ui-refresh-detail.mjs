// Viewport-sized detail captures for the 2026-11 UI refresh: stacked tables, stat cards,
// bottom-sheet dialog, tablet/desktop dashboards. Usage: node puppeteer/ui-refresh-detail.mjs
import path from "node:path";
import fs from "node:fs";
import { BASE_URL, SCREENSHOT_DIR, launch, newPage, signIn, users, viewports } from "./browser.mjs";

const dir = path.join(SCREENSHOT_DIR, "detail");
fs.mkdirSync(dir, { recursive: true });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const browser = await launch();
const errors = [];
try {
  for (const [vpName, vp] of Object.entries({ mobile: viewports.mobile, tablet: viewports.tablet, desktop: viewports.desktop })) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
    await signIn(page, users.admin);
    for (const route of ["/admin", "/admin/students", "/admin/teachers", "/admin/reports", "/admin/parents"]) {
      await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
      await wait(500);
      await page.screenshot({ path: path.join(dir, `admin${route.replace(/\//g, "_")}-${vpName}.png`) });
    }
    // Edit-student dialog (a bottom sheet on phones), opened through the app's own Modal — no browser dialogs.
    await page.goto(`${BASE_URL}/admin/students`, { waitUntil: "networkidle0" });
    await wait(400);
    const edit = await page.$$("table.table tbody button.btn-ghost");
    if (edit[0]) {
      await edit[0].click();
      await wait(600);
      await page.screenshot({ path: path.join(dir, `admin-student-edit-dialog-${vpName}.png`) });
    }
    errors.push(...consoleErrors.map((e) => `[${vpName}] ${e}`), ...failedRequests.map((e) => `[${vpName}] ${e}`));
    await page.close();
  }
  for (const [role, user, route] of [["student", users.student, "/student"], ["teacher", users.teacher, "/teacher"]]) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
    await signIn(page, user);
    await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
    await wait(500);
    if (role === "teacher") {
      await page.evaluate(() => document.querySelector(".t-stats")?.scrollIntoView({ block: "center" }));
      await wait(300);
    }
    await page.screenshot({ path: path.join(dir, `${role}-home-mobile.png`) });
    errors.push(...consoleErrors, ...failedRequests);
    await page.close();
  }
} finally {
  await browser.close();
}
console.log(`errors: ${errors.length}`);
for (const e of errors) console.log("  " + e.slice(0, 200));
console.log(dir);
