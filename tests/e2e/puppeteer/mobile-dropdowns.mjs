// Phone view (2026-11-24): at 375 px every single-choice group is a dropdown list (no rows of pills), page actions and
// filters line up, and the dropdowns work — an expense and a lead are added through them, then removed. Also checks that
// desktop still shows pills. Screenshots at 375 px; fails on overflow, console errors or failed requests.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot, demoPassword } from "./browser.mjs";

const API = process.env.API_BASE_URL ?? "http://localhost:5080";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };
const tag = Math.random().toString(36).slice(2, 7);

async function fill(page, selector, value) {
  await page.waitForSelector(selector, { timeout: 10000 });
  await page.$eval(selector, (el) => { el.value = ""; });
  await page.type(selector, String(value));
  await page.$eval(selector, (el) => el.dispatchEvent(new Event("change", { bubbles: true })));
  await wait(150);
}
async function clickText(page, scope, text) {
  const handle = await page.waitForFunction((scope, text) =>
    [...document.querySelectorAll(`${scope} button, ${scope} a.btn`)].find((b) => b.offsetParent !== null && b.textContent.trim().startsWith(text) && !b.disabled), { timeout: 10000 }, scope, text);
  await handle.asElement().click();
  await wait(500);
}
const visible = (page, selector) => page.$$eval(selector, (els) => els.filter((e) => e.offsetParent !== null).length);
async function shot(page, name) {
  await wait(400);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  if (overflow > 0) fail(`${name} overflows by ${overflow}px at 375`);
  await screenshot(page, `mobile-${name}`);
}
// Chooses an option of a visible dropdown by its text.
async function choose(page, scope, text) {
  const ok = await page.$$eval(`${scope} select.choice-select`, (sels, text) => {
    for (const s of sels.filter((x) => x.offsetParent !== null)) {
      const o = [...s.options].find((o) => o.textContent.trim() === text);
      if (o) { s.value = o.value; s.dispatchEvent(new Event("change", { bubbles: true })); return true; }
    }
    return false;
  }, text);
  if (!ok) fail(`No dropdown option "${text}" in ${scope}`);
  await wait(300);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
try {
  await signIn(page, users.admin);

  // Finance: Add expense with the category and "paid by" dropdowns.
  await page.goto(`${BASE_URL}/admin/finance`, { waitUntil: "networkidle0" });
  await shot(page, "finance");
  await clickText(page, ".page-header", "Add expense");
  if (await visible(page, ".modal .choice-pills")) fail("Pills are still shown on a phone");
  if ((await visible(page, ".modal select.choice-select")) !== 2) fail("Expected two dropdown lists (category, paid by)");
  await choose(page, ".modal", "Internet & phone");
  await choose(page, ".modal", "UPI");
  await fill(page, "#xa", 321);
  await fill(page, "#xn", `Mobile ${tag}`);
  await shot(page, "expense-dialog");
  await clickText(page, ".modal .form-actions", "Add expense");
  await page.waitForFunction((t) => [...document.querySelectorAll(".finance-ledger li")].some((li) => li.textContent.includes(t) && li.textContent.includes("Internet & phone") && li.textContent.includes("UPI")), { timeout: 10000 }, `Mobile ${tag}`);
  await page.click(`button[aria-label="Delete expense Mobile ${tag}"]`);
  await clickText(page, ".modal .form-actions", "Delete expense");
  await page.waitForFunction((t) => !document.querySelector("main").textContent.includes(t), { timeout: 10000 }, `Mobile ${tag}`);

  // Marketing: add a lead with the source dropdown, log a call with the outcome dropdown, move it with the status dropdown.
  await page.goto(`${BASE_URL}/marketing/leads`, { waitUntil: "networkidle0" });
  await shot(page, "leads");
  await clickText(page, ".page-header", "Add lead");
  await fill(page, "#ld-n", `Mobile ${tag}`);
  await fill(page, "#ld-p", `93${Math.floor(10000000 + Math.random() * 89999999)}`);
  await choose(page, ".modal", "Referral");
  await page.waitForSelector("#ld-rb", { timeout: 5000 }); // "Referred by" appears for referrals
  await shot(page, "lead-dialog");
  await clickText(page, ".modal .form-actions", "Add lead");
  await page.waitForFunction((t) => document.querySelector(".lead-table")?.textContent.includes(t), { timeout: 10000 }, `Mobile ${tag}`);
  await page.$$eval(".lead-table a", (as, l) => as.find((a) => a.textContent.includes(l))?.click(), `Mobile ${tag}`);
  await page.waitForFunction(() => /\/marketing\/leads\/[0-9a-f-]{36}$/.test(location.pathname), { timeout: 10000 });
  await clickText(page, ".page-header", "Log contact");
  await choose(page, ".modal", "WhatsApp");
  await choose(page, ".modal", "Call back later");
  await shot(page, "contact-dialog");
  await clickText(page, ".modal .form-actions", "Save");
  await page.waitForFunction(() => /WhatsApp · Call back later/.test(document.querySelector(".lead-log")?.textContent ?? ""), { timeout: 10000 });
  await choose(page, ".lead-detail", "Interested");
  await page.waitForFunction(() => /Interested/.test(document.querySelector(".lead-detail .badge")?.textContent ?? ""), { timeout: 10000 });
  await shot(page, "lead");
  const leadId = page.url().split("/").pop();

  // Other phone screens: no overflow, tidy actions.
  for (const [name, path] of [["marketing", "/marketing"], ["campaigns", "/marketing/campaigns"], ["salaries", "/admin/salaries"], ["fees", "/admin/fees"], ["students", "/admin/students"], ["fee-plans", "/admin/fee-plans"]]) {
    await page.goto(`${BASE_URL}${path}`, { waitUntil: "networkidle0" });
    await shot(page, name);
  }

  // Desktop keeps the pills.
  await page.setViewport(viewports.desktop);
  await page.goto(`${BASE_URL}/admin/finance`, { waitUntil: "networkidle0" });
  await clickText(page, ".page-header", "Add expense");
  if (!(await visible(page, ".modal .choice-pills")) || (await visible(page, ".modal select.choice-select"))) fail("Desktop should show pills, not dropdowns");

  // Clean up.
  const r = await fetch(`${API}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email: users.admin, password: demoPassword(), clientName: "e2e" }) });
  await fetch(`${API}/api/v1/marketing/leads/${leadId}`, { method: "DELETE", headers: { authorization: `Bearer ${(await r.json()).accessToken}` } });

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("mobile-dropdowns: OK — dropdown lists on phones (expense, lead, contact, status), pills on desktop, no overflow on 10 phone screens");
} catch (e) {
  await screenshot(page, "mobile-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
