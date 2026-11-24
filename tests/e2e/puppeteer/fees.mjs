// Fees & hours (addendum A) in a real browser: the admin adds hours on a student's profile with a first payment, sees the
// student on the dashboard's "Fees due" card and the Fees page, takes the rest, then removes both payments and cancels the
// package so repeated runs leave nothing owing. Screenshots at 375/768/1440; fails on overflow, console errors or failed requests.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot } from "./browser.mjs";

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };

async function fill(page, selector, value) {
  await page.$eval(selector, (el) => { el.value = ""; });
  await page.type(selector, String(value));
  await page.$eval(selector, (el) => el.dispatchEvent(new Event("change", { bubbles: true })));
  await wait(150);
}

async function clickText(page, scope, text) {
  const handle = await page.waitForFunction((scope, text) =>
    [...document.querySelectorAll(`${scope} button`)].find((b) => b.textContent.trim().startsWith(text) && !b.disabled), { timeout: 10000 }, scope, text);
  await handle.asElement().click();
  await wait(400);
}

async function overflowCheck(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `fees-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  await signIn(page, users.admin);

  // A demo student's profile → Fees & hours.
  await page.goto(`${BASE_URL}/admin/students`, { waitUntil: "networkidle0" });
  const href = await page.$$eval('a[href^="/students/"]', (as) => as.map((a) => a.getAttribute("href")).find((h) => /^\/students\/[0-9a-f-]{36}$/.test(h)));
  if (!href) fail("No student link on /admin/students");
  const studentId = href.split("/").pop();
  await page.goto(`${BASE_URL}${href}`, { waitUntil: "networkidle0" });
  await page.waitForSelector("#fees");

  // Add 10 hours for 5000 with 2000 paid now by UPI.
  await clickText(page, "#fees", "Add hours");
  await clickText(page, ".modal", "10 hours");
  await fill(page, "#fp", 5000);
  await fill(page, "#fn", 2000);
  await clickText(page, ".modal", "UPI");
  await fill(page, ".modal .fee-method + .field input", "E2E-UPI-1");
  await clickText(page, ".modal", "Add 10 h");
  await page.waitForFunction(() => !document.querySelector(".modal"), { timeout: 10000 });
  const pkg = await page.waitForFunction(() => [...document.querySelectorAll(".fee-packages > li")].find((li) => li.textContent.includes("E2E-UPI-1")), { timeout: 10000 });
  const pkgText = await pkg.evaluate((li) => li.textContent);
  if (!/3,000 due/.test(pkgText)) fail(`Package should show 3,000 due: ${pkgText}`);
  await overflowCheck(page, "profile");

  // The student is on the dashboard card and the Fees page.
  await page.goto(`${BASE_URL}/admin`, { waitUntil: "networkidle0" });
  await page.waitForSelector(".home-fees");
  const onCard = await page.$(`.home-fees a[href="/students/${studentId}#fees"]`);
  const total = await page.$eval(".home-fees", (el) => el.textContent);
  if (!/To collect/.test(total)) fail("Fees due card shows no total");
  if (!onCard && !/See all/.test(total)) fail("Student missing from the Fees due card");
  if (!(await page.$$eval(".dash-kpis .kpi", (m) => m.some((x) => x.textContent.includes("Fees due"))))) fail("No Fees due metric");
  await overflowCheck(page, "dashboard");

  await page.goto(`${BASE_URL}/admin/fees`, { waitUntil: "networkidle0" });
  await page.waitForSelector(`.fee-list a[href="/students/${studentId}#fees"]`, { timeout: 10000 });
  await overflowCheck(page, "page");

  // Take the rest, then undo everything: remove both payments (kept as removed) and cancel the package.
  await page.goto(`${BASE_URL}${href}`, { waitUntil: "networkidle0" });
  await page.waitForSelector("#fees .fee-packages");
  await page.evaluate(() => [...document.querySelectorAll(".fee-packages > li")].find((li) => li.textContent.includes("E2E-UPI-1"))?.setAttribute("data-e2e", "pkg"));
  await clickText(page, '[data-e2e="pkg"]', "Take payment");
  const amount = await page.$eval("#pa", (el) => el.value);
  if (Number(amount) !== 3000) fail(`Take payment should default to the 3000 due, got ${amount}`);
  await clickText(page, ".modal", "Save payment");
  await page.waitForFunction(() => [...document.querySelectorAll('[data-e2e="pkg"] .badge, .fee-packages > li .badge')].some((b) => b.textContent.trim() === "Paid"), { timeout: 10000 });
  for (let i = 0; i < 2; i++) {
    await page.evaluate(() => [...document.querySelectorAll(".fee-packages > li")].find((li) => li.textContent.includes("E2E-UPI-1"))?.setAttribute("data-e2e", "pkg"));
    await clickText(page, '[data-e2e="pkg"] .fee-payments', "Remove");
    await fill(page, "#vr", "E2E clean-up");
    await clickText(page, ".modal", "Remove payment");
    await page.waitForFunction(() => !document.querySelector(".modal"), { timeout: 10000 });
    await wait(300);
  }
  await page.evaluate(() => [...document.querySelectorAll(".fee-packages > li")].find((li) => li.textContent.includes("E2E-UPI-1"))?.setAttribute("data-e2e", "pkg"));
  await clickText(page, '[data-e2e="pkg"]', "Cancel package");
  await page.waitForFunction(() => [...document.querySelectorAll(".fee-packages > li.cancelled")].some((li) => li.textContent.includes("E2E-UPI-1")), { timeout: 10000 });

  const failed = failedRequests.filter((r) => !r.includes("/files/download") && !r.includes("/photo"));
  if (consoleErrors.length) fail(`Console errors: ${consoleErrors.join(" | ")}`);
  if (failed.length) fail(`Failed requests: ${failed.join(" | ")}`);
  console.log("Fees checks passed at 375, 768 and 1440px (add hours, dashboard card, Fees page, take payment, remove, cancel).");
} finally {
  await browser.close();
}
