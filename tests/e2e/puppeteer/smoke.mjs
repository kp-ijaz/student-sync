// Smoke check: launches system Chrome through puppeteer-core, opens the web app (or a data: page when
// BASE_URL=none), screenshots it at desktop and mobile widths and reports console errors / failed requests.
import { BASE_URL, launch, newPage, screenshot, viewports } from "./browser.mjs";

const target = BASE_URL === "none"
  ? "data:text/html,<title>Student Sync smoke</title><h1>Puppeteer is working</h1>"
  : BASE_URL;

const browser = await launch();
try {
  for (const [name, vp] of Object.entries({ desktop: viewports.desktop, mobile: viewports.mobile })) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
    await page.goto(target, { waitUntil: "networkidle0", timeout: 30_000 });
    const file = await screenshot(page, `smoke-${name}`);
    console.log(`${name}: "${await page.title()}" at ${page.url().slice(0, 60)} → ${file}`);
    console.log(`  console errors: ${consoleErrors.length}, failed requests: ${failedRequests.length}`);
    for (const e of [...consoleErrors, ...failedRequests]) console.log("  - " + e);
    await page.close();
  }
  console.log(`Chrome ${await browser.version()} OK`);
} finally {
  await browser.close();
}
