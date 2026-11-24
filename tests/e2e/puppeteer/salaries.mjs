// Salaries (2026-11-24) in a real browser: the teacher list is visible on desktop, "Add salary payment" records an
// amount for a chosen teacher (the slip opens), the payment is listed on the teacher's page (Salary card near the top)
// and in Finance. The test teacher is created through the API with fictional data. Screenshots at 375/768/1440.
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

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `salaries-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  const name = `Payee ${tag} (Demo Teacher)`;
  const teacher = (await api("teachers", "POST", { fullName: name, email: `payee-${tag}@horizon.example.test`, timeZoneId: "Asia/Dubai" })).id;

  await signIn(page, users.admin);
  const month = new Date(); const m = `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, "0")}`;
  await page.goto(`${BASE_URL}/admin/salaries?month=${m}`, { waitUntil: "networkidle0" });

  // The list is visible on desktop (it was hidden at ≥ 1024 px before 2026-11-24).
  await page.waitForFunction((n) => [...document.querySelectorAll(".salary-row")].some((r) => r.offsetParent !== null && r.textContent.includes(n)), { timeout: 10000 }, name);
  await fill(page, 'input[aria-label="Search teachers"]', `Payee ${tag}`);
  await page.waitForFunction(() => document.querySelectorAll(".salary-row").length === 1, { timeout: 10000 });
  await shots(page, "page");

  // Add salary payment → choose the teacher → amount → record. The slip opens.
  await clickText(page, ".page-header", "Add salary payment");
  await page.select("#spt", teacher);
  await wait(600);
  await fill(page, "#spa", 2750);
  await fill(page, "#spn", `Advance ${tag}`);
  await shots(page, "dialog");
  await clickText(page, ".modal .form-actions", "Record payment");
  await page.waitForFunction(() => location.pathname.startsWith("/salary-slips/"), { timeout: 10000 });

  // The payment is on the teacher's page, in the Salary card right under "About".
  await page.goto(`${BASE_URL}/teachers/${teacher}`, { waitUntil: "networkidle0" });
  await page.waitForFunction(() => /2,750/.test(document.querySelector("#pay")?.textContent ?? ""), { timeout: 10000 });
  const order = await page.evaluate(() => { const about = document.querySelector("#t-about")?.closest("section"); const pay = document.querySelector("#pay");
    return about && pay ? about.compareDocumentPosition(pay) & Node.DOCUMENT_POSITION_FOLLOWING : 0; });
  if (!order) fail("Salary card is not below About");
  await shots(page, "teacher");

  // And in Finance, found by its note.
  await page.goto(`${BASE_URL}/admin/finance?kind=Salary`, { waitUntil: "networkidle0" });
  await fill(page, 'input[aria-label="Search transactions"]', `Advance ${tag}`);
  await page.waitForFunction((n) => document.querySelector(".finance-ledger")?.textContent.includes(n), { timeout: 10000 }, name);

  // Edit it from Finance (owner request 2026-11-24: every entry can be corrected): 2,750 → 3,000.
  await page.evaluate((n) => [...document.querySelectorAll(".finance-ledger > li")].find((li) => li.textContent.includes(n)).querySelector('button[title="Edit"]').click(), name);
  await page.waitForFunction(() => parseFloat(document.querySelector(".modal #spa")?.value) === 2750, { timeout: 10000 });
  await fill(page, "#spa", 3000);
  await shots(page, "edit-dialog");
  await clickText(page, ".modal .form-actions", "Save changes");
  await page.waitForFunction((n) => { const li = [...document.querySelectorAll(".finance-ledger > li")].find((l) => l.textContent.includes(n)); return li && /3,000/.test(li.textContent); }, { timeout: 10000 }, name);

  // Fees ▸ Salaries lists the month's payments with edit and delete; delete it there with a reason.
  await page.goto(`${BASE_URL}/admin/salaries?month=${m}`, { waitUntil: "networkidle0" });
  await page.waitForFunction((n) => [...document.querySelectorAll(".salary-payments > li")].some((li) => li.textContent.includes(n) && /3,000/.test(li.textContent)), { timeout: 10000 }, name);
  await page.evaluate((n) => [...document.querySelectorAll(".salary-payments > li")].find((li) => li.textContent.includes(n)).scrollIntoView({ block: "center" }), name);
  await shots(page, "payments");
  await page.evaluate((n) => [...document.querySelectorAll(".salary-payments > li")].find((li) => li.textContent.includes(n)).querySelector('button[title="Delete"]').click(), name);
  await page.waitForSelector(".modal #vr");
  await fill(page, "#vr", "E2E clean-up");
  await shots(page, "delete-dialog");
  await clickText(page, ".modal .form-actions", "Remove payment");
  await page.waitForFunction((n) => !document.querySelector(".modal") && ![...document.querySelectorAll(".salary-payments > li")].some((li) => li.textContent.includes(n)), { timeout: 10000 }, name);
  const pay = await api(`teachers/${teacher}/pay`);
  if (pay.paid !== 0 || !pay.payments[0].voided) fail(`Payment should be removed and count as 0 paid: ${JSON.stringify(pay.payments[0])}`);

  // The teacher's page offers the same edit and delete on a live payment (none left here: the removed one is struck out).
  await page.goto(`${BASE_URL}/teachers/${teacher}`, { waitUntil: "networkidle0" });
  await page.waitForFunction(() => /Removed: E2E clean-up/.test(document.querySelector("#pay")?.textContent ?? ""), { timeout: 10000 });

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("salaries: OK — list, search, add salary payment, shown on teacher page and Finance, edited in Finance, deleted on Salaries");
} catch (e) {
  await screenshot(page, "salaries-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
