// Admin CRUD (2026-11-23) in a real browser: fee plans (add, edit, offered in "Add hours", delete), correcting a package,
// and deleting students — one with no records (deleted for good) and one with fees (archived, then found with the
// Archived filter). Throw-away students are created through the API with fictional data; the script leaves no fee plan
// behind. Screenshots at 375/768/1440; fails on overflow, console errors or failed requests.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot, demoPassword } from "./browser.mjs";

const API = process.env.API_BASE_URL ?? "http://localhost:5080";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };
const tag = Math.random().toString(36).slice(2, 8);

async function api(path, method = "GET", body) {
  if (!api.token) {
    const r = await fetch(`${API}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: users.admin, password: demoPassword(), clientName: "e2e" }) });
    api.token = (await r.json()).accessToken;
  }
  const r = await fetch(`${API}/api/v1/${path}`, { method, headers: { "content-type": "application/json", authorization: `Bearer ${api.token}` },
    body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) fail(`${method} ${path} → ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

const newStudent = async (name) => (await api("students", "POST", {
  fullName: `${name} ${tag} (Demo Student)`, email: `crud-${name.toLowerCase()}-${tag}@horizon.example.test`, studentCode: `CR-${tag}-${name[0]}`,
  schoolName: "Fictional Public School", timeZoneId: "Asia/Dubai" })).id;

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

const toast = (page, re) => page.waitForFunction((src) => [...document.querySelectorAll(".toast")].some((t) => new RegExp(src).test(t.textContent)), { timeout: 10000 }, re.source);

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `crud-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
const planName = `E2E plan ${tag}`;
try {
  await signIn(page, users.admin);

  // 1. Fee plans: reachable from the side menu, add one, edit its price.
  await page.goto(`${BASE_URL}/admin`, { waitUntil: "networkidle0" });
  if (!(await page.$('a[href="/admin/fee-plans"]'))) fail("Fee plans missing from the side menu");
  await page.goto(`${BASE_URL}/admin/fee-plans`, { waitUntil: "networkidle0" });
  await clickText(page, "main", "Add plan");
  await clickText(page, ".modal", "20 hours");
  await fill(page, "#fpp", 9000);
  await fill(page, "#fpn", planName);
  await shots(page, "plan-dialog");
  await clickText(page, ".modal .form-actions", "Add plan");
  await page.waitForFunction((n) => [...document.querySelectorAll(".fee-plan-row")].some((r) => r.textContent.includes(n)), { timeout: 10000 }, planName);
  await page.click(`button[aria-label="Edit ${planName}"]`);
  await wait(300);
  await fill(page, "#fpp", 8000);
  await clickText(page, ".modal .form-actions", "Save plan");
  const row = await page.waitForFunction((n) => [...document.querySelectorAll(".fee-plan-row")].find((r) => r.textContent.includes(n) && r.textContent.includes("8,000")), { timeout: 10000 }, planName);
  if (!/400/.test(await row.evaluate((r) => r.textContent))) fail("Per-hour price (8,000 ÷ 20 = 400) not shown");
  await shots(page, "plans");

  // 2. A student with fees: add hours from the plan, correct the package, then delete → archived.
  const withFees = await newStudent("Fees");
  await page.goto(`${BASE_URL}/students/${withFees}`, { waitUntil: "networkidle0" });
  await page.waitForSelector("#fees");
  await clickText(page, "#fees", "Add hours");
  await clickText(page, ".modal", planName);
  const [filledHours, filledPrice] = [parseFloat(await page.$eval("#fh", (e) => e.value)), parseFloat(await page.$eval("#fp", (e) => e.value))];
  if (filledHours !== 20 || filledPrice !== 8000) fail(`Plan did not fill hours and price: ${filledHours} h, ${filledPrice}`);
  await shots(page, "add-hours-plan");
  await clickText(page, ".modal .form-actions", "Add 20 h");
  await page.waitForFunction((n) => [...document.querySelectorAll(".fee-packages > li")].some((li) => li.textContent.includes(n)), { timeout: 10000 }, planName);
  await clickText(page, ".fee-packages", "Edit");
  await fill(page, "#eh", 25);
  await fill(page, "#ep", 10000);
  await clickText(page, ".modal .form-actions", "Save package");
  await page.waitForFunction(() => /10,000/.test(document.querySelector(".fee-packages")?.textContent ?? ""), { timeout: 10000 });
  await shots(page, "package-edited");

  await page.goto(`${BASE_URL}/admin/students?`, { waitUntil: "networkidle0" });
  await fill(page, 'input[type=search]', `CR-${tag}-F`);
  await page.waitForFunction((t) => document.querySelector(".desk-list tbody")?.textContent.includes(t), { timeout: 10000 }, `CR-${tag}-F`);
  await clickText(page, ".desk-list tbody", "Edit");
  await clickText(page, ".modal .form-actions", "Delete");
  await page.waitForFunction(() => document.querySelector(".modal h2")?.textContent.startsWith("Delete"), { timeout: 5000 });
  await shots(page, "delete-dialog");
  await clickText(page, ".modal .form-actions", "Delete student");
  await toast(page, /archived instead/);
  await page.waitForFunction(() => !document.querySelector(".desk-list tbody tr"), { timeout: 10000 });
  await page.select('select[aria-label="Status"]', "Archived");
  await page.waitForFunction((t) => document.querySelector(".desk-list tbody")?.textContent.includes(t), { timeout: 10000 }, `CR-${tag}-F`);
  await shots(page, "students-archived");

  // 3. A student with no records: deleted for good.
  await newStudent("Blank");
  await page.goto(`${BASE_URL}/admin/students`, { waitUntil: "networkidle0" });
  await fill(page, 'input[type=search]', `CR-${tag}-B`);
  await page.waitForFunction((t) => document.querySelector(".desk-list tbody")?.textContent.includes(t), { timeout: 10000 }, `CR-${tag}-B`);
  await clickText(page, ".desk-list tbody", "Edit");
  await clickText(page, ".modal .form-actions", "Delete");
  await clickText(page, ".modal .form-actions", "Delete student");
  await toast(page, /was deleted\./);

  // 4. Teachers page: status filter present (archived teachers can be found and restored).
  await page.goto(`${BASE_URL}/admin/teachers`, { waitUntil: "networkidle0" });
  if (!(await page.$('select[aria-label="Status"]'))) fail("Teachers page has no status filter");
  await shots(page, "teachers");

  // 5. Delete the fee plan (packages keep their copy).
  await page.goto(`${BASE_URL}/admin/fee-plans`, { waitUntil: "networkidle0" });
  await page.click(`button[aria-label="Delete ${planName}"]`);
  await clickText(page, ".modal .form-actions", "Delete plan");
  await page.waitForFunction((n) => ![...document.querySelectorAll(".fee-plan-row")].some((r) => r.textContent.includes(n)), { timeout: 10000 }, planName);

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("admin-crud: OK — fee plans CRUD, plan in Add hours, package edit, delete (archived + permanent), teacher filter");
} catch (e) {
  await screenshot(page, "crud-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
