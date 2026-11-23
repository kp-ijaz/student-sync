// Finance (2026-11-24) in a real browser: the Finance page (income, salaries, expenses, profit, 12 months, money in & out),
// adding, editing and deleting an expense, switching to the year view, and "People who left" on Fees and Finance for an
// archived student who still owes money. Test records are created through the API with fictional data. Screenshots at
// 375/768/1440; fails on overflow, console errors or failed requests.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot, demoPassword } from "./browser.mjs";

const API = process.env.API_BASE_URL ?? "http://localhost:5080";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };
const tag = Math.random().toString(36).slice(2, 7);

let token;
async function api(path, method = "GET", body) {
  if (!token) {
    const r = await fetch(`${API}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: users.admin, password: demoPassword(), clientName: "e2e" }) });
    token = (await r.json()).accessToken;
  }
  const r = await fetch(`${API}/api/v1/${path}`, { method, headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) fail(`${method} ${path} → ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

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

const hasText = (page, scope, text) => page.waitForFunction((s, t) => document.querySelector(s)?.textContent.includes(t), { timeout: 10000 }, scope, text);
const gone = (page, scope, text) => page.waitForFunction((s, t) => !document.querySelector(s)?.textContent.includes(t), { timeout: 10000 }, scope, text);

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `finance-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  // An archived student who still owes 3,000.
  const student = (await api("students", "POST", { fullName: `Leaver ${tag} (Demo Student)`, email: `leaver-${tag}@horizon.example.test`,
    studentCode: `LV-${tag}`, timeZoneId: "Asia/Dubai" })).id;
  await api(`students/${student}/packages`, "POST", { hours: 5, price: 4000, paidNow: 1000 });
  await api(`students/${student}`, "DELETE");

  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin`, { waitUntil: "networkidle0" });
  if (!(await page.$('a[href="/admin/finance"]'))) fail("Finance missing from the side menu");

  // 1. Finance page: add an expense, see it in the ledger and the totals, edit it, delete it.
  await page.goto(`${BASE_URL}/admin/finance`, { waitUntil: "networkidle0" });
  await page.waitForSelector(".finance-kpis");
  await clickText(page, "main", "Add expense");
  await clickText(page, ".modal", "Electricity");
  await fill(page, "#xa", 1234);
  await fill(page, "#xn", `Power bill ${tag}`);
  await shots(page, "expense-dialog");
  await clickText(page, ".modal .form-actions", "Add expense");
  await hasText(page, ".finance-ledger", `Power bill ${tag}`);
  // Search narrows the transactions; the Expenses total opens only expenses.
  await fill(page, 'input[aria-label="Search transactions"]', `Power bill ${tag}`);
  await page.waitForFunction(() => document.querySelectorAll(".finance-ledger li").length === 1, { timeout: 10000 });
  await clickText(page, ".finance-kpis", "Expenses");
  await hasText(page, ".finance-filtered", "1 line");
  await hasText(page, "main", "Electricity & water");
  await shots(page, "month");

  await page.waitForSelector(`button[aria-label="Edit expense Power bill ${tag}"]`, { timeout: 10000 });
  await page.click(`button[aria-label="Edit expense Power bill ${tag}"]`);
  await wait(500);
  await fill(page, "#xa", 1500);
  await clickText(page, ".modal .form-actions", "Save expense");
  await page.waitForFunction((t) => [...document.querySelectorAll(".finance-ledger li")].some((li) => li.textContent.includes(t) && li.textContent.includes("1,500")), { timeout: 10000 }, `Power bill ${tag}`);

  // Sorting by amount and clearing the filters.
  await page.select('select[aria-label="Sort transactions"]', "Largest");
  await wait(600);
  await clickText(page, ".finance-filtered", "Clear filters");
  await page.waitForFunction(() => document.querySelectorAll(".finance-ledger li").length >= 1, { timeout: 10000 });
  // People who left, on the Finance page.
  await hasText(page, ".former-balances", `Leaver ${tag}`);

  // Year view, then back to the month.
  await page.$$eval(".finance-period label", (ls) => ls.find((l) => l.textContent.trim() === "Year")?.click());
  await page.waitForFunction((y) => document.querySelector(".finance-nav h2")?.textContent.trim() === y, { timeout: 10000 }, String(new Date().getFullYear()));
  await shots(page, "year");
  await page.$$eval(".finance-period label", (ls) => ls.find((l) => l.textContent.trim() === "Month")?.click());
  await wait(800);
  await hasText(page, ".finance-nav h2", new Date().toLocaleString("en-GB", { month: "long" }));

  await page.waitForSelector(`button[aria-label="Delete expense Power bill ${tag}"]`, { timeout: 10000 });
  await page.click(`button[aria-label="Delete expense Power bill ${tag}"]`);
  await clickText(page, ".modal .form-actions", "Delete expense");
  await gone(page, "main", `Power bill ${tag}`);

  // 2. Fees page: the leaver is not in the main list, but in "People who left".
  await page.goto(`${BASE_URL}/admin/fees`, { waitUntil: "networkidle0" });
  await hasText(page, ".former-balances", `Leaver ${tag}`);
  const inMain = await page.$$eval("section[aria-labelledby=due-h] .fee-list li", (lis, t) => lis.some((li) => li.textContent.includes(t)), `Leaver ${tag}`);
  if (inMain) fail("A student who left is still in the main Fees list");
  await shots(page, "fees-people-who-left");

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("finance: OK — totals, expense add/edit/delete, year view, people who left on Finance and Fees");
} catch (e) {
  await screenshot(page, "finance-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
